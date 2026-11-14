# LayoutFixer

أداة صغيرة لويندوز تصلّح النص الذي كتبته بلغة الكيبورد الخاطئة (عربي ↔ إنجليزي) بضغطة زر.
A tiny Windows tray tool that fixes text typed with the wrong keyboard layout (Arabic ↔ English) with one key press.

```
hgsghl ugd;l   →   السلام عليكم
أثممخ          →   Hello      (and the other way round)
```

---

## طريقة الاستخدام (عربي)

1. شغّل `LayoutFixer.exe`. تظهر أيقونة صغيرة في شريط المهام قرب الساعة (قد تكون داخل السهم ^).
2. اكتب كالعادة. إذا كتبت بالغلط (مثلاً كتبت إنجليزي وأنت تقصد عربي) **حدّد النص**.
3. اضغط **Pause** (أو **Ctrl+Alt+F**).
4. يُستبدل النص بالنص الصحيح، وتتبدّل لغة الكيبورد تلقائيًا إلى اللغة التي كنت تقصدها لتكمل الكتابة.

ملاحظات:
- إذا لم تحدد شيئًا، تُصلَّح **السطر الحالي** كله.
- الاتجاه يُكتشف تلقائيًا: النص اللاتيني يتحول إلى عربي، والنص العربي يتحول إلى إنجليزي.
- الحافظة (Clipboard) تُستعاد كما كانت بعد التحويل.
- كليك يمين على الأيقونة: **Fix now** / **Run at Windows startup** (تشغيل مع ويندوز) / **Exit**.

### التشغيل مع ويندوز
كليك يمين على الأيقونة ثم فعّل **Run at Windows startup**.

### البناء من الكود
لا تحتاج تثبيت أي شيء، يكفي مترجم C# الموجود أصلًا في ويندوز:

```
build.bat
```

### القيود
- يعتمد على تخطيط **Arabic 101** القياسي مقابل **US QWERTY**.
- لا يعمل داخل البرامج التي تعمل بصلاحية المدير إلا إذا شغّلت الأداة بصلاحية المدير أيضًا.
- التحويل يحدث عند الضغط على الاختصار فقط، وليس أثناء الكتابة.
- لا يستخدم الإنترنت ولا يحفظ أي نص.

---

## Usage (English)

1. Run `LayoutFixer.exe`. A tray icon appears near the clock.
2. When you notice text typed in the wrong layout, **select it**.
3. Press **Pause** (or **Ctrl+Alt+F**).
4. The text is replaced with the correct one and the keyboard layout is switched to the language you meant.

Notes:
- Nothing selected → the current line is fixed.
- Direction is detected automatically (mostly Latin → Arabic, mostly Arabic → English).
- Your clipboard contents are restored afterwards.
- Tray menu: **Fix now**, **Run at Windows startup**, **Exit**.

### Build
No installs needed; it uses the C# compiler that ships with Windows:

```
build.bat
```

### Limitations
- Assumes the standard **Arabic 101** layout on a **US QWERTY** keyboard.
- Won't work in apps running as administrator unless LayoutFixer is also run as administrator.
- Converts only when you press the hotkey; it does not fix text while you type.
- No network access, and no text is stored or logged.

## License
MIT
