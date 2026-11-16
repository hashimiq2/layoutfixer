// LayoutFixer - fixes text typed with the wrong keyboard layout, between ANY two installed layouts.
// Select the text (or nothing = from the start of the line to the cursor) and press the hotkey.
// Build:  build.bat   (uses the C# compiler that ships with Windows)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ---------------------------------------------------------------- native helpers
static class Native
{
    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr hkl);
    [DllImport("user32.dll")] public static extern int ToUnicodeEx(uint vk, uint sc, byte[] state,
        [Out, MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 4)] StringBuilder buf, int cch, uint flags, IntPtr hkl);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTU { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTU u; }
}

// ---------------------------------------------------------------- clipboard (native, fast retries)
static class Clip
{
    public static IntPtr Owner = IntPtr.Zero;

    static bool Open()
    {
        for (int i = 0; i < 20; i++) { if (Native.OpenClipboard(Owner)) return true; Thread.Sleep(3); }
        return false;
    }

    public static string Get()
    {
        if (!Open()) return null;
        try
        {
            IntPtr h = Native.GetClipboardData(13);   // CF_UNICODETEXT
            if (h == IntPtr.Zero) return null;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Native.GlobalUnlock(h); }
        }
        finally { Native.CloseClipboard(); }
    }

    public static void Set(string s)
    {
        if (!Open()) return;
        try
        {
            Native.EmptyClipboard();
            char[] c = (s + "\0").ToCharArray();
            IntPtr h = Native.GlobalAlloc(0x2, (UIntPtr)(c.Length * 2));   // GMEM_MOVEABLE
            if (h == IntPtr.Zero) return;
            IntPtr p = Native.GlobalLock(h);
            Marshal.Copy(c, 0, p, c.Length);
            Native.GlobalUnlock(h);
            Native.SetClipboardData(13, h);
        }
        finally { Native.CloseClipboard(); }
    }
}

// ---------------------------------------------------------------- installed layouts
class Item
{
    public IntPtr Hkl; public string Name;
    public override string ToString() { return Name; }
}

static class Layouts
{
    public static List<Item> GetAll()
    {
        var arr = new IntPtr[64];
        int n = Native.GetKeyboardLayoutList(64, arr);
        var items = new List<Item>();
        var seen = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            string name = NameOf(arr[i]);
            if (seen.ContainsKey(name)) { seen[name]++; name += " #" + seen[name]; } else seen[name] = 1;
            items.Add(new Item { Hkl = arr[i], Name = name });
        }
        return items;
    }

    static string NameOf(IntPtr hkl)
    {
        long v = (long)hkl;
        int lang = (int)(v & 0xFFFF), dev = (int)((v >> 16) & 0xFFFF);
        string n;
        try { n = CultureInfo.GetCultureInfo(lang).EnglishName; } catch { n = "Language 0x" + lang.ToString("X4"); }
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\0000" + dev.ToString("X4")))
            {
                if (k != null) { object t = k.GetValue("Layout Text"); if (t != null) n += " - " + t; }
            }
        }
        catch { }
        return n;
    }

    public static IntPtr Find(List<Item> items, string hex)
    {
        if (string.IsNullOrEmpty(hex)) return IntPtr.Zero;
        long v; try { v = Convert.ToInt64(hex, 16); } catch { return IntPtr.Zero; }
        foreach (var it in items) if ((long)it.Hkl == v) return it.Hkl;
        return IntPtr.Zero;
    }

    public static string Hex(IntPtr h) { return ((long)h).ToString("X8"); }
}

// ---------------------------------------------------------------- key-position based converter
class Conv
{
    public IntPtr A, B;
    Dictionary<string, string> aToB, bToA;
    HashSet<char> aOnly = new HashSet<char>(), bOnly = new HashSet<char>();
    int maxKeyLength = 1;

    public Conv(IntPtr a, IntPtr b)
    {
        A = a; B = b;
        aToB = Build(a, b); bToA = Build(b, a);
        foreach (string k in aToB.Keys) if (k.Length > maxKeyLength) maxKeyLength = k.Length;
        foreach (string k in bToA.Keys) if (k.Length > maxKeyLength) maxKeyLength = k.Length;
        var aSet = new HashSet<char>(); var bSet = new HashSet<char>();
        foreach (var k in aToB.Keys) if (k.Length == 1) aSet.Add(k[0]);
        foreach (var k in bToA.Keys) if (k.Length == 1) bSet.Add(k[0]);
        foreach (char c in aSet) if (!bSet.Contains(c)) aOnly.Add(c);
        foreach (char c in bSet) if (!aSet.Contains(c)) bOnly.Add(c);
    }

    static string Out(IntPtr hkl, uint sc, int st)
    {
        uint vk = Native.MapVirtualKeyEx(sc, 1, hkl);
        if (vk == 0) return null;
        byte[] ks = new byte[256];
        if ((st & 1) != 0) ks[0x10] = 0x80;
        if ((st & 2) != 0) { ks[0x11] = 0x80; ks[0x12] = 0x80; }
        var sb = new StringBuilder(8);
        int n = Native.ToUnicodeEx(vk, sc, ks, sb, sb.Capacity, 4, hkl);   // 4 = don't touch keyboard state
        if (n <= 0) return null;                                              // dead key / nothing
        string s = sb.ToString(0, n);
        return (s.Length == 0 || char.IsControl(s[0])) ? null : s;
    }

    static Dictionary<string, string> Build(IntPtr from, IntPtr to)
    {
        var d = new Dictionary<string, string>();
        var scs = new List<uint>();
        for (uint s = 0x02; s <= 0x35; s++) scs.Add(s);
        scs.Add(0x56);
        for (int st = 0; st < 4; st++)               // none, shift, AltGr, shift+AltGr
            foreach (uint sc in scs)
            {
                string a = Out(from, sc, st), b = Out(to, sc, st);
                if (a != null && b != null && !d.ContainsKey(a)) d[a] = b;
            }
        return d;
    }

    // Score distinctive source characters first; use the active keyboard layout to
    // resolve genuine ties (common for punctuation and very short strings).
    public string Convert(string s, IntPtr activeLayout, out bool aToBDir)
    {
        int sa = 0, sb = 0;
        foreach (char c in s) { if (aOnly.Contains(c)) sa++; else if (bOnly.Contains(c)) sb++; }
        aToBDir = sa == sb ? (activeLayout == IntPtr.Zero || activeLayout == A) : sa > sb;
        var map = aToBDir ? aToB : bToA;
        var o = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            string r = null; int used = 0;
            for (int len = Math.Min(maxKeyLength, s.Length - i); len > 0; len--)
                if (map.TryGetValue(s.Substring(i, len), out r)) { used = len; break; }
            // Caps Lock can produce uppercase Latin text without Shift. Keyboard
            // layouts do not generally have an uppercase Arabic layer, so retry
            // the unshifted key before leaving these letters untouched.
            if (used == 0 && char.IsUpper(s[i]) && map.TryGetValue(char.ToLowerInvariant(s[i]).ToString(), out r)) used = 1;
            if (used > 0) { o.Append(r); i += used - 1; }
            else o.Append(s[i]);
        }
        string converted = o.ToString();
        return converted == s ? null : converted;
    }
}

// ---------------------------------------------------------------- settings storage
static class Config
{
    const string Key = @"Software\LayoutFixer";
    public static string Get(string name)
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { object o = k == null ? null : k.GetValue(name); return o == null ? null : o.ToString(); } } catch { return null; }
    }
    public static void Set(string name, string val)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, val);
    }

    public static readonly string[] HotNames = { "Pause", "Ctrl+Alt+F", "Ctrl+Shift+F", "F9", "Scroll Lock" };
    public static void HotSpec(string n, out uint mod, out uint vk)
    {
        switch (n)
        {
            case "Ctrl+Alt+F": mod = 0x3; vk = 0x46; break;
            case "Ctrl+Shift+F": mod = 0x6; vk = 0x46; break;
            case "F9": mod = 0; vk = 0x78; break;
            case "Scroll Lock": mod = 0; vk = 0x91; break;
            default: mod = 0; vk = 0x13; break;     // Pause
        }
    }

    // resolves the two chosen layouts (falls back to sensible defaults)
    public static void Load(List<Item> items, out IntPtr a, out IntPtr b, out string hot, out bool firstRun)
    {
        string s1 = Get("Layout1"), s2 = Get("Layout2");
        firstRun = s1 == null || s2 == null;
        hot = Get("Hotkey"); if (Array.IndexOf(HotNames, hot) < 0) hot = "Pause";
        a = Layouts.Find(items, s1); b = Layouts.Find(items, s2);
        if (a == IntPtr.Zero || b == IntPtr.Zero || a == b)
        {
            IntPtr en = IntPtr.Zero, other = IntPtr.Zero;
            foreach (var it in items) if (((long)it.Hkl & 0x3FF) == 0x09) { en = it.Hkl; break; }
            foreach (var it in items) if (it.Hkl != en && ((long)it.Hkl & 0x3FF) != 0x09) { other = it.Hkl; break; }
            if (en != IntPtr.Zero && other != IntPtr.Zero) { a = en; b = other; }
            else if (items.Count >= 2) { a = items[0].Hkl; b = items[1].Hkl; }
            else { a = IntPtr.Zero; b = IntPtr.Zero; }
        }
    }
}

// ---------------------------------------------------------------- settings window
class SettingsForm : Form
{
    public ComboBox C1 = new ComboBox(), C2 = new ComboBox(), CH = new ComboBox();
    public CheckBox Startup = new CheckBox();

    public SettingsForm(List<Item> items, IntPtr a, IntPtr b, string hot, bool startup)
    {
        Text = "Layout Fixer";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(420, 262);
        Font = new Font("Segoe UI", 9f);

        Func<string, int, Label> lab = (t, y) => { var l = new Label { Text = t, Left = 16, Top = y, Width = 388, AutoSize = false }; Controls.Add(l); return l; };
        Action<ComboBox, int> place = (c, y) => { c.Left = 16; c.Top = y; c.Width = 388; c.DropDownStyle = ComboBoxStyle.DropDownList; Controls.Add(c); };

        lab("Language 1  /  اللغة الأولى", 14);
        place(C1, 34);
        lab("Language 2  /  اللغة الثانية", 66);
        place(C2, 86);
        lab("Hotkey  /  الاختصار", 118);
        place(CH, 138);

        foreach (var it in items) { C1.Items.Add(it); C2.Items.Add(it); }
        foreach (var n in Config.HotNames) CH.Items.Add(n);
        foreach (Item it in C1.Items) if (it.Hkl == a) C1.SelectedItem = it;
        foreach (Item it in C2.Items) if (it.Hkl == b) C2.SelectedItem = it;
        CH.SelectedItem = hot;

        Startup.Text = "Run at Windows startup  /  تشغيل مع ويندوز"; Startup.Left = 16; Startup.Top = 174; Startup.Width = 388; Startup.Checked = startup;
        Controls.Add(Startup);

        var link = new LinkLabel { Text = "Add a language in Windows settings  /  إضافة لغة من إعدادات ويندوز", Left = 16, Top = 202, Width = 388 };
        link.LinkClicked += (s, e) => { try { Process.Start("ms-settings:regionlanguage"); } catch { } };
        Controls.Add(link);

        var ok = new Button { Text = "Save", Left = 232, Top = 226, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 324, Top = 226, Width = 80, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;

        FormClosing += (s, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (C1.SelectedItem == null || C2.SelectedItem == null || C1.SelectedItem == C2.SelectedItem)
            {
                MessageBox.Show("Please choose two different languages.\nاختر لغتين مختلفتين.", "Layout Fixer");
                e.Cancel = true;
            }
        };
    }
}

// ---------------------------------------------------------------- one-click install / uninstall
static class Setup
{
    static string Dir { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayoutFixer"); } }
    static string InstalledExe { get { return System.IO.Path.Combine(Dir, "LayoutFixer.exe"); } }
    static string Shortcut { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Layout Fixer.lnk"); } }
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LayoutFixer";

    public static bool IsInstalledCopy()
    {
        return string.Equals(System.IO.Path.GetFullPath(Application.ExecutablePath), InstalledExe, StringComparison.OrdinalIgnoreCase);
    }

    static void KillOthers()
    {
        int me = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcessesByName("LayoutFixer")) { if (p.Id != me) { try { p.Kill(); p.WaitForExit(2000); } catch { } } }
    }

    static void MakeShortcut(string path, string target)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { path });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { System.IO.Path.GetDirectoryName(target) });
            lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "Layout Fixer" });
            lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
        }
        catch { }
    }

    // returns 0 = installed and started, 1 = run once without installing, 2 = cancelled
    public static int Offer()
    {
        int result = 2;
        using (var f = new Form())
        {
            f.Text = "Layout Fixer - Setup"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new Size(420, 190); f.Font = new Font("Segoe UI", 9f);
            f.Controls.Add(new Label { Left = 16, Top = 14, Width = 388, Height = 54, Text = "Layout Fixer fixes text typed in the wrong keyboard layout (e.g. Arabic <-> English) with one key.\r\nيصلّح الكتابة بلغة الكيبورد الخاطئة بضغطة زر." });
            var startup = new CheckBox { Left = 16, Top = 80, Width = 388, Checked = true, Text = "Start automatically with Windows  /  تشغيل تلقائي مع ويندوز" };
            f.Controls.Add(startup);
            var install = new Button { Left = 16, Top = 130, Width = 120, Height = 32, Text = "Install  /  تثبيت" };
            var once = new Button { Left = 146, Top = 130, Width = 140, Height = 32, Text = "Run without installing" };
            var cancel = new Button { Left = 296, Top = 130, Width = 108, Height = 32, Text = "Cancel" };
            f.Controls.Add(install); f.Controls.Add(once); f.Controls.Add(cancel);
            install.Click += (s, e) =>
            {
                try { Install(startup.Checked); result = 0; f.Close(); }
                catch (Exception ex) { MessageBox.Show("Install failed:\n" + ex.Message, "Layout Fixer"); }
            };
            once.Click += (s, e) => { result = 1; f.Close(); };
            cancel.Click += (s, e) => { result = 2; f.Close(); };
            f.AcceptButton = install;
            f.ShowDialog();
        }
        return result;
    }

    static void Install(bool startup)
    {
        KillOthers();
        System.IO.Directory.CreateDirectory(Dir);
        System.IO.File.Copy(Application.ExecutablePath, InstalledExe, true);
        MakeShortcut(Shortcut, InstalledExe);
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (startup) k.SetValue("LayoutFixer", "\"" + InstalledExe + "\""); else k.DeleteValue("LayoutFixer", false);
        }
        using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", "Layout Fixer");
            k.SetValue("Publisher", "hashimiq2");
            k.SetValue("DisplayIcon", InstalledExe);
            k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
            k.SetValue("InstallLocation", Dir);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        }
        Process.Start(InstalledExe);
    }

    public static void Uninstall(bool ask)
    {
        if (ask && MessageBox.Show("Remove Layout Fixer from this PC?\nحذف الأداة من الجهاز؟", "Layout Fixer", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        KillOthers();
        try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k.DeleteValue("LayoutFixer", false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\LayoutFixer", false); } catch { }
        try { System.IO.File.Delete(Shortcut); } catch { }
        // the running exe lives in Dir, so delete the folder a moment after we exit
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"") { CreateNoWindow = true, UseShellExecute = false };
            Process.Start(psi);
        }
        catch { }
        Environment.Exit(0);
    }
}

// ---------------------------------------------------------------- tray application
class App : Form
{
    const int WM_HOTKEY = 0x312, WM_INPUTLANGCHANGEREQUEST = 0x50;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    NotifyIcon tray;
    Conv conv;
    string hotName = "Pause";
    bool dialogOpen;
    Icon icon;
    IntPtr lastTargetWindow = IntPtr.Zero;
    System.Windows.Forms.Timer foregroundTimer;

    [STAThread]
    static void Main(string[] a)
    {
        if (a.Length >= 1 && (a[0] == "--test" || a[0] == "--layouts"))
        {
            Console.OutputEncoding = Encoding.UTF8;
            var items = Layouts.GetAll();
            if (a[0] == "--layouts") { foreach (var it in items) Console.WriteLine(Layouts.Hex(it.Hkl) + "  " + it.Name); return; }
            IntPtr x, y; string hot; bool fr;
            Config.Load(items, out x, out y, out hot, out fr);
            var c = new Conv(x, y); bool dir;
            Console.WriteLine(c.Convert(a[1], IntPtr.Zero, out dir) + "   [" + (dir ? "1->2" : "2->1") + "]");
            return;
        }
        Application.EnableVisualStyles();
        if (a.Length >= 1 && a[0] == "--uninstall") { Setup.Uninstall(true); return; }
        // double-clicked from Downloads (not the installed copy): offer a one-click install
        if (!Setup.IsInstalledCopy() && !(a.Length >= 1 && a[0] == "--portable"))
        {
            int r = Setup.Offer();            // 0 = installed & relaunched, 1 = run once, 2 = cancel
            if (r != 1) return;
        }
        bool created;
        using (var m = new Mutex(true, "LayoutFixer_single", out created))
        {
            if (!created)
            {
                try { EventWaitHandle.OpenExisting("LayoutFixer_show").Set(); } catch { }
                return;
            }
            Native.timeBeginPeriod(1);   // precise 1 ms sleeps -> consistent speed
            Application.EnableVisualStyles();
            Application.Run(new App());
        }
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                using (var br = new SolidBrush(Color.FromArgb(37, 99, 235))) g.FillEllipse(br, 0, 0, 31, 31);
                using (var f = new Font("Segoe UI", 12f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("⇄", f, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
                }
            }
            IntPtr h = bmp.GetHicon();
            Icon i = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return i;
        }
    }

    public App()
    {
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        icon = MakeIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Fix selected text", null, (s, e) => Fix());
        menu.Items.Add("Settings...", null, (s, e) => ShowSettings());
        if (Setup.IsInstalledCopy()) menu.Items.Add("Uninstall...", null, (s, e) => { tray.Visible = false; Setup.Uninstall(true); tray.Visible = true; });
        menu.Items.Add("Exit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                if (lastTargetWindow != IntPtr.Zero) Native.SetForegroundWindow(lastTargetWindow);
                Thread.Sleep(40);
                Fix();
            }
        };

        CreateHandle();                 // the form is never shown; hotkeys need a window handle
        Clip.Owner = Handle;
        lastTargetWindow = Native.GetForegroundWindow();
        foregroundTimer = new System.Windows.Forms.Timer { Interval = 200, Enabled = true };
        foregroundTimer.Tick += (s, e) =>
        {
            IntPtr active = Native.GetForegroundWindow();
            if (active != IntPtr.Zero && active != Handle) lastTargetWindow = active;
        };
        ApplySettings();

        // lets a second launch of the exe open the settings of the running instance
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "LayoutFixer_show");
        var t = new Thread(() => { while (true) { ev.WaitOne(); try { BeginInvoke((Action)ShowSettings); } catch { } } });
        t.IsBackground = true; t.Start();

        bool first; { IntPtr x, y; string h; Config.Load(Layouts.GetAll(), out x, out y, out h, out first); }
        if (first || conv == null) BeginInvoke((Action)ShowSettings);
    }

    protected override void SetVisibleCore(bool v) { base.SetVisibleCore(false); }
    protected override void OnFormClosed(FormClosedEventArgs e) { Native.UnregisterHotKey(Handle, 1); base.OnFormClosed(e); }
    protected override void WndProc(ref Message m) { if (m.Msg == WM_HOTKEY) Fix(); base.WndProc(ref m); }

    // (re)load settings: layouts -> converter, hotkey registration, tray text
    void ApplySettings()
    {
        var items = Layouts.GetAll();
        IntPtr a, b; bool fr;
        Config.Load(items, out a, out b, out hotName, out fr);
        conv = (a == IntPtr.Zero || b == IntPtr.Zero) ? null : new Conv(a, b);
        Native.UnregisterHotKey(Handle, 1);
        uint mod, vk; Config.HotSpec(hotName, out mod, out vk);
        bool ok = Native.RegisterHotKey(Handle, 1, mod, vk);
        string names = "not configured";
        if (conv != null)
        {
            string n1 = "?", n2 = "?";
            foreach (var it in items) { if (it.Hkl == a) n1 = it.Name; if (it.Hkl == b) n2 = it.Name; }
            names = Short(n1) + " <-> " + Short(n2);
        }
        string txt = "Layout Fixer: " + names + " (" + hotName + (ok ? ")" : " - key in use!)");
        tray.Text = txt.Length > 63 ? txt.Substring(0, 63) : txt;
    }

    static string Short(string n) { int i = n.IndexOf(" - "); if (i > 0) n = n.Substring(0, i); i = n.IndexOf(" ("); return i > 0 ? n.Substring(0, i) : n; }

    void ShowSettings()
    {
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var items = Layouts.GetAll();
            IntPtr a, b; string hot; bool fr;
            Config.Load(items, out a, out b, out hot, out fr);
            using (var f = new SettingsForm(items, a, b, hot, IsStartup()))
            {
                f.Icon = icon;
                if (f.ShowDialog() == DialogResult.OK)
                {
                    Config.Set("Layout1", Layouts.Hex(((Item)f.C1.SelectedItem).Hkl));
                    Config.Set("Layout2", Layouts.Hex(((Item)f.C2.SelectedItem).Hkl));
                    Config.Set("Hotkey", (string)f.CH.SelectedItem);
                    SetStartup(f.Startup.Checked);
                    ApplySettings();
                }
            }
        }
        finally { dialogOpen = false; }
    }

    static bool IsStartup() { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("LayoutFixer") != null; }
    static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (on) k.SetValue("LayoutFixer", "\"" + Application.ExecutablePath + "\""); else k.DeleteValue("LayoutFixer", false);
        }
    }

    // ------------------------------------------------------------ the actual fix
    static void Key(byte vk, bool up) { Native.keybd_event(vk, 0, up ? 2u : 0u, UIntPtr.Zero); }
    static void Combo(byte mod, byte vk) { Key(mod, false); Key(vk, false); Key(vk, true); Key(mod, true); }

    static void WaitModifiersUp()
    {
        for (int i = 0; i < 40; i++)
        {
            if ((Native.GetAsyncKeyState(0x11) & 0x8000) == 0 && (Native.GetAsyncKeyState(0x12) & 0x8000) == 0 && (Native.GetAsyncKeyState(0x10) & 0x8000) == 0) return;
            Thread.Sleep(5);
        }
    }

    // Ctrl+C and wait until the clipboard really changes (clipboard sequence number), then read it
    static string CopySelection(int timeoutMs)
    {
        uint s0 = Native.GetClipboardSequenceNumber();
        Combo(0x11, 0x43);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (Native.GetClipboardSequenceNumber() != s0) return Clip.Get();
            Thread.Sleep(1);
        }
        return null;
    }

    // types the text directly (no clipboard involved) -> no dependence on the target app's paste timing
    static bool TypeText(string text)
    {
        var inp = new List<Native.INPUT>();
        Action<ushort, ushort, uint> add = (vk, sc, fl) =>
        {
            var i = new Native.INPUT { type = 1 };
            i.u.ki.wVk = vk; i.u.ki.wScan = sc; i.u.ki.dwFlags = fl;
            inp.Add(i);
        };
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n') { add(0x0D, 0, 0); add(0x0D, 0, 2); }
            else if (c == '\t') { add(0x09, 0, 0); add(0x09, 0, 2); }
            else { add(0, c, 4); add(0, c, 4 | 2); }
        }
        return Native.SendInput((uint)inp.Count, inp.ToArray(), Marshal.SizeOf(typeof(Native.INPUT))) == inp.Count;
    }

    // opt-in timing log (set env LF_DEBUG=1); never writes any user text
    static void Dbg(string s)
    {
        if (Environment.GetEnvironmentVariable("LF_DEBUG") == null) return;
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lf_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n"); } catch { }
    }

    void Fix()
    {
        Dbg("hotkey");
        if (conv == null) { ShowSettings(); return; }
        WaitModifiersUp();
        string saved = Clip.Get();

        string text = CopySelection(150);
        bool lineMode = false;
        if (string.IsNullOrEmpty(text))
        {
            // Nothing selected: select the entire current draft, so a tray click
            // converts it without requiring the user to highlight it first.
            Combo(0x11, 0x41);
            text = CopySelection(300);
            lineMode = true;
        }
        Dbg("copied len=" + (text == null ? -1 : text.Length) + " lineMode=" + lineMode);
        if (saved != null) Clip.Set(saved);                    // give the user's clipboard back right away
        if (string.IsNullOrEmpty(text)) return;

        bool aToB;
        IntPtr foreground = Native.GetForegroundWindow();
        uint thread = Native.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        IntPtr activeLayout = Native.GetKeyboardLayout(thread);
        string fixedText = conv.Convert(text, activeLayout, out aToB);
        if (fixedText == null || fixedText == text)
        {
            if (lineMode) { Key(0x27, false); Key(0x27, true); }   // collapse our selection (Right arrow)
            return;
        }

        // Electron editors (including Discord) may report Unicode SendInput as
        // accepted while dropping VK_PACKET text. Clipboard paste is reliable there.
        // The original selection is still active, so paste replaces it directly.
        Clip.Set(fixedText);
        Thread.Sleep(25);
        Combo(0x11, 0x56);
        Thread.Sleep(120);
        if (saved != null) Clip.Set(saved);

        Dbg("typed " + fixedText.Length + " chars");
        // switch the keyboard layout to the language the text was meant for
        IntPtr hkl = aToB ? conv.B : conv.A;
        Native.PostMessage(Native.GetForegroundWindow(), WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }
}
