// LayoutFixer - fixes text typed with the wrong keyboard layout (Arabic <-> English)
// Select the text (or nothing = current line) and press  Pause  or  Ctrl+Alt+F.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Map
{
    // US QWERTY key  ->  Arabic 101 key (unshifted / shifted)
    const string EnN = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    const string ArN = "ذضصثقفغعهخحجدشسيبلاتنمكطئءؤرلاىةوزظ";
    const string EnS = "~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
    const string ArS = "ًٌَُّلإإ‘÷×؛<>ٍِ][لأأـ،/:\"~ْ}{لآآ’,.؟";

    public static Dictionary<char, string> ToAr = new Dictionary<char, string>();
    public static Dictionary<char, char> ToEn = new Dictionary<char, char>();

    static Map()
    {
        // Arabic 101 unshifted row (explicit, one char per key; "لا" key is a 2-char ligature on 'b')
        string[] arN = { "ذ","ض","ص","ث","ق","ف","غ","ع","ه","خ","ح","ج","د","ش","س","ي","ب","ل","ا","ت","ن","م","ك","ط","ئ","ء","ؤ","ر","لا","ى","ة","و","ز","ظ" };
        // keys in the same order: ` q w e r t y u i o p [ ] a s d f g h j k l ; ' z x c v b n m , . /
        string[] arSh = { "ّ","َ","ً","ُ","ٌ","لإ","إ","‘","÷","×","؛","<",">","ِ","ٍ","]","[","لأ","أ","ـ","،","/",":","\"","~","ْ","}","{","لآ","آ","’",",",".","؟" };
        string en = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
        string enS = "~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
        // build explicit ordered key list matching arN/arSh (34 entries)
        string keys = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
        // arN order given above follows: ` q w e r t y u i o p [ ] a s d f g h j k l ; ' z x c v b n m , . /
        // the table has 34 entries and keys has 34 chars.
        for (int i = 0; i < keys.Length && i < arN.Length; i++)
        {
            ToAr[keys[i]] = arN[i];
            if (arN[i].Length == 1 && !ToEn.ContainsKey(arN[i][0])) ToEn[arN[i][0]] = keys[i];
        }
        for (int i = 0; i < enS.Length && i < arSh.Length; i++)
        {
            ToAr[enS[i]] = arSh[i];
            if (arSh[i].Length == 1 && !ToEn.ContainsKey(arSh[i][0])) ToEn[arSh[i][0]] = enS[i];
        }
        // ligature letters coming back to English
        ToEn['ل'] = 'g';
        // Arabic-Indic digits -> Latin
        for (int d = 0; d < 10; d++) { ToEn[(char)(0x0660 + d)] = (char)('0' + d); ToEn[(char)(0x06F0 + d)] = (char)('0' + d); }
    }

    static bool IsArabic(char c) { return (c >= 0x0600 && c <= 0x06FF) || (c >= 0xFB50 && c <= 0xFEFF); }

    // true if the text is mostly Latin keystrokes that should become Arabic
    public static bool LooksLatin(string s)
    {
        int ar = 0, la = 0;
        foreach (char c in s) { if (IsArabic(c)) ar++; else if (char.IsLetter(c)) la++; }
        return la >= ar;
    }

    public static string Convert(string s, bool toArabic)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (toArabic)
            {
                string r; sb.Append(ToAr.TryGetValue(c, out r) ? r : c.ToString());
            }
            else
            {
                // "لا" pair -> b, "لأ" -> G, "لإ" -> T, "لآ" -> B
                if (c == 'ل' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'ا') { sb.Append('b'); i++; continue; }
                    if (n == 'أ') { sb.Append('G'); i++; continue; }
                    if (n == 'إ') { sb.Append('T'); i++; continue; }
                    if (n == 'آ') { sb.Append('B'); i++; continue; }
                }
                char e; sb.Append(ToEn.TryGetValue(c, out e) ? e : c);
            }
        }
        return sb.ToString();
    }
}

class App : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [DllImport("user32.dll")] static extern bool UnloadKeyboardLayout(IntPtr hkl);

    // pick an already-installed layout by primary language (0x01 = Arabic, 0x09 = English); never loads a new one
    static IntPtr FindLayout(int primary)
    {
        var list = new IntPtr[32]; int n = GetKeyboardLayoutList(32, list);
        for (int i = 0; i < n; i++) if ((((long)list[i]) & 0x3FF) == primary) return list[i];
        return IntPtr.Zero;
    }

    const int WM_HOTKEY = 0x312, WM_INPUTLANGCHANGEREQUEST = 0x50;
    const uint KEYUP = 2;
    NotifyIcon tray;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static void Key(byte vk, bool up) { keybd_event(vk, 0, up ? KEYUP : 0, UIntPtr.Zero); }
    static void Combo(byte mod, byte vk)
    {
        Key(mod, false); Key(vk, false); Key(vk, true); Key(mod, true);
    }

    [STAThread]
    static void Main(string[] a)
    {
        if (a.Length >= 2 && a[0] == "--test")
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool ar = Map.LooksLatin(a[1]);
            Console.WriteLine(Map.Convert(a[1], ar));
            return;
        }
        bool created;
        using (var m = new Mutex(true, "LayoutFixer_single", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.Run(new App());
        }
    }

    public App()
    {
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Fix now  (Pause / Ctrl+Alt+F)", null, (s, e) => Fix());
        var startup = new ToolStripMenuItem("Run at Windows startup") { CheckOnClick = true, Checked = IsStartup() };
        startup.CheckedChanged += (s, e) => SetStartup(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Layout Fixer  (Pause = fix selection)", ContextMenuStrip = menu, Visible = true };
        tray.BalloonTipTitle = "Layout Fixer";
        CreateHandle();   // the form is never shown, so create the handle explicitly (needed for the hotkeys)
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(Handle, 1, 0, 0x13);            // Pause
        RegisterHotKey(Handle, 2, 0x1 | 0x2, 0x46);    // Ctrl+Alt+F
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); base.OnFormClosed(e); }
    protected override void SetVisibleCore(bool v) { base.SetVisibleCore(false); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) Fix();
        base.WndProc(ref m);
    }

    static bool IsStartup() { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("LayoutFixer") != null; }
    static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (on) k.SetValue("LayoutFixer", "\"" + Application.ExecutablePath + "\""); else k.DeleteValue("LayoutFixer", false);
        }
    }

    static void WaitModifiersUp()
    {
        for (int i = 0; i < 40; i++)
        {
            if ((GetAsyncKeyState(0x11) & 0x8000) == 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0 && (GetAsyncKeyState(0x10) & 0x8000) == 0) return;
            Thread.Sleep(10);
        }
    }

    static string CopySelection()
    {
        uint seq0 = GetClipboardSequenceNumber();
        Combo(0x11, 0x43);                              // Ctrl+C
        for (int i = 0; i < 80; i++)                    // up to ~400 ms, checked every 5 ms
        {
            Thread.Sleep(5);
            if (GetClipboardSequenceNumber() != seq0)
            {
                for (int j = 0; j < 5; j++) { try { return Clipboard.GetText(); } catch { Thread.Sleep(10); } }
                return null;
            }
        }
        return null;
    }


    void Fix()
    {
        WaitModifiersUp();
        string savedText = null;
        try { if (Clipboard.ContainsText()) savedText = Clipboard.GetText(); } catch { }

        string text = CopySelection();
        if (string.IsNullOrEmpty(text))
        {
            // nothing selected -> select the current line (Shift+Home) and copy it
            Key(0x10, false); Key(0x24, false); Key(0x24, true); Key(0x10, true);
            Thread.Sleep(15);
            text = CopySelection();
        }
        if (string.IsNullOrEmpty(text)) { Restore(savedText); return; }

        bool toArabic = Map.LooksLatin(text);
        string fixedText = Map.Convert(text, toArabic);
        try { Clipboard.SetText(fixedText); } catch { Restore(savedText); return; }
        Thread.Sleep(5);
        Combo(0x11, 0x56);                              // Ctrl+V
        Thread.Sleep(90);
        Restore(savedText);

        // switch the keyboard layout so the user can keep typing in the intended language
        IntPtr hkl = FindLayout(toArabic ? 0x01 : 0x09);
        if (hkl != IntPtr.Zero) PostMessage(GetForegroundWindow(), WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }

    static void Restore(string t)
    {
        try { if (t != null) Clipboard.SetText(t); else Clipboard.Clear(); } catch { }
    }
}
