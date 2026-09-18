@echo off
rem ---------------------------------------------------------------------------
rem  Double-click launcher for Clear-WindowsJunk.ps1
rem
rem  Double-clicking a .ps1 opens it in an editor instead of running it, and
rem  "Run with PowerShell" still obeys the machine execution policy and closes
rem  the window the instant anything goes wrong. This does neither.
rem
rem  Options live in Clear-WindowsJunk.settings.txt next to this file.
rem ---------------------------------------------------------------------------

setlocal
set "PS1=%~dp0Clear-WindowsJunk.ps1"

if not exist "%PS1%" (
    echo Cannot find Clear-WindowsJunk.ps1 next to this launcher.
    echo Looked in: %~dp0
    echo.
    pause
    exit /b 1
)

rem Already elevated? Run it. Otherwise re-launch this same file through UAC so
rem the system targets are reachable; the script itself would only ask again.
net session >nul 2>&1
if %errorlevel% equ 0 goto :run

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Start-Process -FilePath '%~f0' -Verb RunAs" 2>nul
if %errorlevel% neq 0 (
    echo.
    echo Elevation was declined - continuing without administrator rights.
    echo System caches ^(Windows Temp, Windows Update, WER^) will be skipped.
    echo.
    goto :run
)
exit /b 0

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
set "RC=%errorlevel%"

rem The script runs its own 5s countdown on a normal finish. Only hold the
rem window open when something actually went wrong.
if not "%RC%"=="0" (
    echo.
    echo Script exited with code %RC%.
    pause
)
endlocal
exit /b %RC%
