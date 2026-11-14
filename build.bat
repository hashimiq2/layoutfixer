@echo off
rem Builds LayoutFixer.exe with the C# compiler that ships with Windows (no installs needed)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /out:LayoutFixer.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll LayoutFixer.cs
if %errorlevel%==0 (echo Built LayoutFixer.exe) else (echo Build failed)
