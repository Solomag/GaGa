@echo off
rem Build GaGa with the C# compiler that ships with .NET Framework 4.x (no Visual Studio needed).
rem Output: Build\GaGa.exe
setlocal
cd /d "%~dp0Source\GaGa"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set OUT=%~dp0Build
if not "%~1"=="" set OUT=%~1
if not exist "%OUT%" mkdir "%OUT%"

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:"%OUT%\GaGa.exe" ^
  /win32icon:App.ico ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Windows.Forms.dll ^
  /reference:"%FW%\WPF\PresentationCore.dll" /reference:"%FW%\WPF\WindowsBase.dll" ^
  /resource:StreamsFile\Resources\Streams.ini,GaGa.StreamsFile.Resources.Streams.ini ^
  /resource:NotifyIconPlayer\Resources\Buffering1.ico,GaGa.NotifyIconPlayer.Resources.Buffering1.ico ^
  /resource:NotifyIconPlayer\Resources\Buffering2.ico,GaGa.NotifyIconPlayer.Resources.Buffering2.ico ^
  /resource:NotifyIconPlayer\Resources\Buffering3.ico,GaGa.NotifyIconPlayer.Resources.Buffering3.ico ^
  /resource:NotifyIconPlayer\Resources\Buffering4.ico,GaGa.NotifyIconPlayer.Resources.Buffering4.ico ^
  /resource:NotifyIconPlayer\Resources\Idle.ico,GaGa.NotifyIconPlayer.Resources.Idle.ico ^
  /resource:NotifyIconPlayer\Resources\Playing-muted.ico,GaGa.NotifyIconPlayer.Resources.Playing-muted.ico ^
  /resource:NotifyIconPlayer\Resources\Playing.ico,GaGa.NotifyIconPlayer.Resources.Playing.ico ^
  /recurse:*.cs
exit /b %ERRORLEVEL%
