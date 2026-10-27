@echo off
rem Builds all tools with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
set WPF=%FW%\WPF
cd /d "%~dp0"

"%CSC%" /nologo /target:exe    /out:btaudio.exe  btaudio.cs || exit /b 1
"%CSC%" /nologo /target:winexe /out:btaudiow.exe btaudio.cs || exit /b 1
"%CSC%" /nologo /target:winexe /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" /r:"%WPF%\WindowsBase.dll" /out:btmute.exe btmute.cs || exit /b 1
"%CSC%" /nologo /target:winexe /r:System.Windows.Forms.dll /out:probe.exe probe.cs || exit /b 1
echo Built btaudio.exe, btaudiow.exe, btmute.exe, probe.exe
