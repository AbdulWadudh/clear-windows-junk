@echo off
rem Builds Clear-WindowsJunk.exe with the C# compiler that ships with Windows.
rem No .NET SDK, no NuGet, no project file.

setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find csc.exe - is the .NET Framework 4.x installed?
    exit /b 1
)

set "REFDIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF"
set "OUT=%~dp0..\Clear Windows Junk.exe"

if not exist "%~dp0app.ico" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-icon.ps1" >nul 2>&1

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /win32icon:"%~dp0app.ico" ^
  /out:"%OUT%" ^
  /reference:"System.dll" ^
  /reference:"System.Core.dll" ^
  /reference:"System.Management.dll" ^
  /reference:"System.ServiceProcess.dll" ^
  /reference:"System.Xml.dll" ^
  /reference:"%REFDIR%\PresentationFramework.dll" ^
  /reference:"%REFDIR%\PresentationCore.dll" ^
  /reference:"%REFDIR%\WindowsBase.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" ^
  "%~dp0AssemblyInfo.cs" "%~dp0Theme.cs" "%~dp0Core.cs" "%~dp0Engine.cs" "%~dp0Tree.cs" "%~dp0Layout.cs" "%~dp0Dialogs.cs" "%~dp0Confirm.cs" "%~dp0Ui.cs" "%~dp0SelfTest.cs"

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)
echo Built %OUT%
endlocal
