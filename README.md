# LayoutFixer

أداة صغيرة لويندوز تصلّح النص الذي كتبته بلغة الكيبورد الخاطئة بضغطة زر، بين **أي لغتين** تختارهما (عربي ↔ إنجليزي، أو أي لغتين أخريين).
A tiny Windows tray tool that fixes text typed with the wrong keyboard layout with one key press, between **any two languages** you choose (Arabic ↔ English, or any other pair).

```
hgsghl ugd;l   →   السلام عليكم
أثممخ          →   Hello      (and the other way round)
```

## التنصيب (سهل جدًا) / Install (very easy)

1. نزّل [**LayoutFixer.exe**](https://github.com/hashimiq2/layoutfixer/raw/main/LayoutFixer.exe) واضغط عليه مرتين.
   Download **LayoutFixer.exe** and double-click it.
2. تظهر نافذة صغيرة: اترك علامة **Start automatically with Windows** (تشغيل تلقائي مع ويندوز) إذا تريد أن يعمل دائمًا، ثم اضغط **Install**.
   A small window appears: keep **Start automatically with Windows** ticked if you want it always on, then press **Install**.
3. تُفتح نافذة الإعدادات: اختر **لغتين** وزر الاختصار، ثم **Save**. انتهى.
   The settings window opens: pick your **two languages** and the hotkey, then **Save**. Done.

> إذا ظهرت رسالة SmartScreen ("Windows protected your PC") اضغط **More info ← Run anyway**؛ الملف غير موقّع رقميًا لأنه مشروع مفتوح المصدر، والكود كاملًا في هذا المستودع وتستطيع بناءه بنفسك (`build.bat`).
> If SmartScreen appears, press **More info → Run anyway**. The exe is unsigned because it is a hobby open-source project; the full source is here and you can build it yourself with `build.bat`.

لإلغاء التنصيب: كليك يمين على أيقونة الأداة ← **Uninstall**، أو من *Settings ← Apps ← Layout Fixer*.
To remove: right-click the tray icon → **Uninstall**, or *Settings → Apps → Layout Fixer*.

## طريقة الاستخدام / Usage

1. اكتب كالعادة. إذا كتبت بلغة الكيبورد الخاطئة **حدّد النص**.
   Type as usual. When you notice the wrong layout, **select the text**.
2. اضغط الاختصار (الافتراضي **Pause**).
   Press the hotkey (default **Pause**).
3. يُستبدل النص بالنص الصحيح، وتتبدل لغة الكيبورد تلقائيًا إلى اللغة التي كنت تقصدها.
   The text is replaced with the right one and the keyboard layout switches to the language you meant.

- إذا لم تحدد شيئًا، يُصلَّح النص من **بداية السطر حتى المؤشر**.
  Nothing selected → the text from the start of the line to the cursor is fixed.
- الاتجاه يُكتشف تلقائيًا بين اللغتين اللتين اخترتهما.
  The direction is detected automatically between your two chosen languages.
- الحافظة (Clipboard) تُستعاد كما كانت.
  Your clipboard is restored.

### الإعدادات / Settings
كليك يمين على الأيقونة ← **Settings** (أو ضغطتان على الأيقونة، أو شغّل الـexe مرة ثانية):
Right-click the tray icon → **Settings** (or double-click it, or launch the exe again):

- **Language 1 / Language 2**: أي لغتين مثبتتين على ويندوز. لإضافة لغة جديدة استخدم الرابط داخل النافذة (Add a language).
  Any two layouts installed in Windows. Use the link in the window to add a new language to Windows.
- **Hotkey**: `Pause` · `Ctrl+Alt+F` · `Ctrl+Shift+F` · `F9` · `Scroll Lock`. (لو كيبوردك بدون Pause اختر غيره / pick another one if your keyboard has no Pause key.)
- **Run at Windows startup**: تشغيل مع ويندوز.

## كيف يعمل / How it works
التحويل يعتمد على **مواضع المفاتيح**: لكل مفتاح فعلي على الكيبورد يُسأل ويندوز ماذا ينتج في اللغة الأولى وماذا ينتج في الثانية (`ToUnicodeEx`)، فيُبنى جدول التحويل تلقائيًا لأي زوج لغات، بما فيها الحروف المركبة مثل «لا».
Conversion is based on **key positions**: for every physical key Windows is asked what it produces in layout 1 and in layout 2 (`ToUnicodeEx`), so the mapping is built automatically for any pair of layouts, including ligatures like «لا».

النص المصحَّح يُكتب مباشرة كإدخال Unicode (بدون لصق)، لذلك السرعة ثابتة ولا تعتمد على بطء البرنامج الذي تكتب فيه.
The corrected text is typed directly as Unicode input (no paste), so speed is consistent and doesn't depend on the target app.

## البناء من الكود / Build from source
لا تحتاج تثبيت أي شيء؛ يكفي مترجم C# الموجود أصلًا في ويندوز:
Nothing to install; it uses the C# compiler that ships with Windows:

```
build.bat
```

## القيود / Limitations
- لا يعمل داخل البرامج التي تعمل بصلاحية المدير إلا إذا شغّلت الأداة بصلاحية المدير أيضًا.
  Won't work in apps running as administrator unless LayoutFixer is also run as administrator.
- يصلّح عند الضغط على الاختصار فقط، وليس أثناء الكتابة.
  Fixes on hotkey press only, not while you type.
- مفاتيح الـ dead keys (حروف التشكيل المركبة في بعض اللغات) غير مدعومة.
  Dead keys are not supported.
- لا إنترنت ولا يُحفظ أي نص. للتشخيص فقط: المتغير `LF_DEBUG=1` يكتب أوقات التنفيذ (بدون أي نص) في `%TEMP%\lf_debug.log`.
  No network access, no text is stored. For diagnostics only, `LF_DEBUG=1` writes timings (never text) to `%TEMP%\lf_debug.log`.

## License
MIT
