@echo off
setlocal EnableExtensions
title Windows Temp / Cache Cleaner

rem ===========================================================================
rem  Clear.bat - Windows 11 temp / cache cleaner (single file, no prompts)
rem
rem  Double-click it. It elevates itself, cleans, shows a summary and the
rem  window closes after 5 seconds - press any key while it counts down to
rem  keep the window open (then any key again to close it).
rem
rem  Optional switches:
rem    Clear.bat -DryRun            preview only, deletes nothing
rem    Clear.bat -WinUpdate         also clear the Windows Update download cache
rem    Clear.bat -Dumps             also clear crash dumps
rem    Clear.bat -RecycleBin        also empty the Recycle Bin
rem    Clear.bat -BrowserCache      also clear Edge/Chrome/Firefox caches
rem    Clear.bat -Prefetch          also clear Prefetch (slows next launches)
rem    Clear.bat -All               all of the above except -RootTemp
rem    Clear.bat -ComponentCleanup  DISM WinSxS compaction (slow)
rem    Clear.bat -RootTemp          also clear C:\Temp (may hold real data)
rem
rem  Why this is not the old XP-era script any more:
rem    * no rd /s /q + md on system folders - the folder itself is kept
rem    * junctions are never followed, so %USERPROFILE%\Local Settings,
rem      Cookies and Recent can no longer eat the real data behind them
rem    * the old "%SysteDrive%\Temp" typo (which meant \Temp on the current
rem      drive) is gone, and C:\Temp is excluded unless explicitly asked for
rem    * system32\dllcache is gone from Windows - not recreated here
rem    * Prefetch, cookies, WebCache, browsing history and WinSxS are left
rem      alone unless a switch says otherwise
rem ===========================================================================

set "SELF=%~f0"
set "CLEARARGS=%*"
set "SKIP="
set "RC=0"

rem --- elevation -------------------------------------------------------------
net session >nul 2>&1
if not errorlevel 1 goto :elevated

echo.
echo  Administrator rights required - requesting elevation...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$a=@{FilePath=$env:SELF;Verb='RunAs'}; if($env:CLEARARGS){$a.ArgumentList=$env:CLEARARGS}; Start-Process @a"
if not errorlevel 1 goto :end

echo.
echo  Elevation was refused or cancelled.
echo  Right-click Clear.bat and pick "Run as administrator".
timeout /t 10 /nobreak >nul
goto :end

:elevated
for /f "delims=:" %%a in ('findstr /n /b /c:"::PS_BODY::" "%~f0"') do set "SKIP=%%a"
if not defined SKIP goto :nobody

powershell -NoProfile -ExecutionPolicy Bypass -Command "$t=(Get-Content -LiteralPath $env:SELF | Select-Object -Skip ([int]$env:SKIP)) -join [Environment]::NewLine; & ([scriptblock]::Create($t))"
set "RC=%errorlevel%"
goto :end

:nobody
echo.
echo  Internal error: the PowerShell body of this file could not be located.
echo  Re-download a clean copy of Clear.bat.
timeout /t 10 /nobreak >nul

:end
exit /b %RC%

::PS_BODY::
<#
.SYNOPSIS
    Safe temp / cache cleaner for Windows 11 (24H2, 25H2, 26H2 and later).

.DESCRIPTION
    Modern replacement for the old XP-era Clear.bat.

    Design rules (why this is safe to run as admin):
      * Only hard-coded allow-listed folders are touched.
      * Only the CONTENTS of a folder are removed - never the folder itself,
        so NTFS permissions / junctions / attributes are never destroyed.
      * Junctions and symlinks are never followed. The old script deleted real
        data through %USERPROFILE%\Local Settings\* compatibility junctions.
      * Protected locations are refused even if someone edits the list.
      * Locked / in-use files are skipped quietly instead of forcing a reboot.

.PARAMETER DryRun
    Show what would be deleted and how much space would be freed. Deletes nothing.

.PARAMETER NoWait
    Do not hold the window open for the 5 second summary at the end.

.PARAMETER WinUpdate
    Also clear the Windows Update download cache (often 2 GB+).
    Stops/restarts wuauserv + BITS, empties SoftwareDistribution\Download,
    clears the Delivery Optimization cache and Windows Setup (Panther) logs.

.PARAMETER RecycleBin
    Empty the Recycle Bin.

.PARAMETER Dumps
    Remove crash dumps (LiveKernelReports, Minidump, MEMORY.DMP).

.PARAMETER Prefetch
    Clear Prefetch. Not recommended - it slows down the next few launches.

.PARAMETER BrowserCache
    Clear Edge / Chrome / Firefox HTTP caches. Skipped while the browser runs.

.PARAMETER ComponentCleanup
    Run DISM /StartComponentCleanup to compact WinSxS. Slow (5-30 minutes).

.PARAMETER ResetBase
    With -ComponentCleanup: also pass /ResetBase. After this, installed
    updates can no longer be uninstalled. Use only on a healthy system.

.PARAMETER RootTemp
    Also clear C:\Temp. Off by default - that folder often holds real
    user or project data, which is exactly what the old script used to eat.

.PARAMETER All
    Everything except -RootTemp, -ComponentCleanup and -ResetBase.

.PARAMETER Log
    Write a transcript to %ProgramData%\ClearTool\Logs.

.EXAMPLE
    Clear.bat -DryRun

.EXAMPLE
    Clear.bat -WinUpdate -Dumps
#>

# --- switch parsing ----------------------------------------------------------
# Switches arrive as text ("-DryRun -WinUpdate") from Clear.bat. They are parsed
# against a fixed allow-list, so no user-supplied text is ever executed.
$switchList = 'DryRun|NoWait|WinUpdate|RecycleBin|Dumps|Prefetch|BrowserCache|ComponentCleanup|ResetBase|RootTemp|All|Log'
$rawArgs    = (@([string]$env:CLEARARGS) + $args) -join ' '
$opts       = @{}
foreach ($m in [regex]::Matches($rawArgs, '(?i)-(?:' + $switchList + ')\b')) {
    $opts[$m.Value.TrimStart('-').ToLower()] = $true
}

$DryRun           = [bool]$opts['dryrun']
$NoWait           = [bool]$opts['nowait']
$WinUpdate        = [bool]$opts['winupdate']
$RecycleBin       = [bool]$opts['recyclebin']
$Dumps            = [bool]$opts['dumps']
$Prefetch         = [bool]$opts['prefetch']
$BrowserCache     = [bool]$opts['browsercache']
$ComponentCleanup = [bool]$opts['componentcleanup']
$ResetBase        = [bool]$opts['resetbase']
$RootTemp         = [bool]$opts['roottemp']
$All              = [bool]$opts['all']
$Log              = [bool]$opts['log']

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

# Never resolve anything relative to wherever the user launched us from.
Set-Location -LiteralPath $env:SystemRoot

# ---------------------------------------------------------------- helpers ----

function Write-Head  { param([string]$Text) Write-Host "`n$Text" -ForegroundColor Cyan; Write-Host ('-' * 78) -ForegroundColor DarkGray }
function Write-Ok    { param([string]$Text) Write-Host "  [ OK ] $Text" -ForegroundColor Green }
function Write-Skip  { param([string]$Text) Write-Host "  [SKIP] $Text" -ForegroundColor DarkYellow }
function Write-Err   { param([string]$Text) Write-Host "  [FAIL] $Text" -ForegroundColor Red }

function Test-KeyAvailable {
    # Single place that touches the console keyboard. Throws when stdin is
    # redirected (scheduled task, piped output), which Wait-Close handles.
    return [Console]::KeyAvailable
}

function Read-KeyPress { return [Console]::ReadKey($true) }

function Wait-Close {
    param([int]$Seconds = 5)
    if ($NoWait) { return }

    $unit = if ($Seconds -eq 1) { 'second' } else { 'seconds' }

    # Interactive countdown. Any key pressed while it runs cancels the auto-close
    # and the window stays open until another key is pressed.
    try {
        # Drop keys that were already buffered during the clean, so only a press
        # during the countdown counts.
        while (Test-KeyAvailable) { $null = Read-KeyPress }

        Write-Host ''
        Write-Host ("  Closing in {0} {1} - press any key to keep this window open" -f $Seconds, $unit) -ForegroundColor DarkGray

        $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
        while ([DateTime]::UtcNow -lt $deadline) {
            if (Test-KeyAvailable) {
                $null = Read-KeyPress
                Write-Host ''
                Write-Host '  Auto-close cancelled.' -ForegroundColor Yellow
                Write-Host '  Press any key to close.' -ForegroundColor DarkGray
                $null = Read-KeyPress
                return
            }
            Start-Sleep -Milliseconds 100
        }
    } catch {
        # No usable console (output redirected, scheduled task, etc.) - plain wait.
        Write-Host ''
        Write-Host ("  Closing in {0} {1}..." -f $Seconds, $unit) -ForegroundColor DarkGray
        try { Start-Sleep -Seconds $Seconds } catch { }
    }
}

function Format-Size {
    param([int64]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0,8:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0,8:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0,8:N1} KB' -f ($Bytes / 1KB)) }
    return ('{0,8} B ' -f $Bytes)
}

$script:ProtectedExact = @(
    $env:SystemDrive + '\'
    $env:SystemRoot
    $env:SystemRoot + '\System32'
    $env:SystemRoot + '\SysWOW64'
    $env:SystemRoot + '\WinSxS'
    $env:SystemRoot + '\assembly'
    $env:SystemRoot + '\servicing'
    $env:SystemRoot + '\Boot'
    $env:SystemRoot + '\System32\config'
    $env:SystemRoot + '\System32\DriverStore'
    $env:SystemRoot + '\System32\dllcache'
    $env:SystemRoot + '\Prefetch\ReadyBoot'
    $env:ProgramFiles
    ${env:ProgramFiles(x86)}
    $env:ProgramData
    $env:ProgramData + '\Microsoft\Windows\Start Menu'
    $env:USERPROFILE
    $env:LOCALAPPDATA
    $env:APPDATA
    $env:LOCALAPPDATA + '\Microsoft\Windows\WebCache'
    $env:LOCALAPPDATA + '\Microsoft\Windows\History'
    $env:LOCALAPPDATA + '\Microsoft\Windows\INetCookies'
    $env:APPDATA   + '\Microsoft\Windows\Recent'
    'C:\Recovery'
    'C:\$Recycle.Bin'
    'C:\Users'
    'C:\Users\Public'
)

# Anything *inside* these is off-limits.
$script:ProtectedPrefix = @(
    $env:SystemRoot + '\System32'
    $env:SystemRoot + '\SysWOW64'
    $env:SystemRoot + '\WinSxS'
    $env:SystemRoot + '\servicing'
    $env:SystemRoot + '\Boot'
    $env:SystemRoot + '\assembly'
    $env:ProgramFiles
    ${env:ProgramFiles(x86)}
    $env:ProgramData + '\Microsoft\Windows\Start Menu'
    'C:\Users\Public'
    'C:\Recovery'
    'C:\$Recycle.Bin'
)

# We must never be an ancestor of these (would delete a parent folder).
$script:ProtectedAncestor = @(
    $env:SystemRoot
    $env:ProgramFiles
    ${env:ProgramFiles(x86)}
    $env:ProgramData
    $env:USERPROFILE
    $env:LOCALAPPDATA
    $env:APPDATA
    'C:\Users'
)

function Test-ProtectedPath {
    param([string]$Path)
    $p  = $Path.TrimEnd('\')
    $ci = [System.StringComparison]::OrdinalIgnoreCase

    foreach ($x in $script:ProtectedExact) {
        if ($x -and [string]::Equals($p, $x.TrimEnd('\'), $ci)) { return $true }
    }
    foreach ($x in $script:ProtectedPrefix) {
        if ($x -and $p.StartsWith($x.TrimEnd('\') + '\', $ci)) { return $true }
    }
    foreach ($x in $script:ProtectedAncestor) {
        if (-not $x) { continue }
        $xp = $x.TrimEnd('\')
        if ([string]::Equals($xp, $p, $ci) -or $xp.StartsWith($p + '\', $ci)) { return $true }
    }
    return $false
}

# Collect every file under $Root without ever crossing a reparse point.
function Get-SafeFileList {
    param([string]$Root, [string[]]$Filter)

    $files = New-Object System.Collections.Generic.List[string]
    $dirs  = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Root)

    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        $entries = $null
        try { $entries = [System.IO.Directory]::GetFileSystemEntries($dir) } catch { continue }

        foreach ($e in $entries) {
            $a = 0
            try { $a = [int][System.IO.File]::GetAttributes($e) } catch { continue }

            # Reparse points (junctions / symlinks) are never entered.
            if ($a -band [int][System.IO.FileAttributes]::ReparsePoint) { continue }

            if ($a -band [int][System.IO.FileAttributes]::Directory) {
                $dirs.Add($e)
                $stack.Push($e)
                continue
            }

            if ($Filter -and $Filter.Count -gt 0) {
                $name  = [System.IO.Path]::GetFileName($e)
                $match = $false
                foreach ($f in $Filter) { if ($name -like $f) { $match = $true; break } }
                if (-not $match) { continue }
            }

            $files.Add($e)
        }
    }

    return [pscustomobject]@{ Files = $files; Dirs = $dirs }
}

function Invoke-CleanDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Label = $Path,
        [string[]]$Filter
    )

    $r = [pscustomobject]@{
        Label = $Label; Path = $Path
        Freed = [int64]0; Removed = 0; Skipped = 0
        Status = 'ok'; Note = ''
    }

    # -- resolve ------------------------------------------------------------
    try   { $full = [System.IO.Path]::GetFullPath($Path) }
    catch { $r.Status = 'bad'; $r.Note = 'invalid path'; return $r }

    $full = $full.TrimEnd('\')
    $r.Path = $full

    # -- hard guards --------------------------------------------------------
    if (-not [System.IO.Path]::IsPathRooted($full)) {
        $r.Status = 'bad'; $r.Note = 'not an absolute path'; return $r
    }
    if ($full -match '^[A-Za-z]:$') {
        $r.Status = 'bad'; $r.Note = 'refuses to touch a drive root'; return $r
    }
    if (Test-ProtectedPath $full) {
        $r.Status = 'bad'; $r.Note = 'protected location'; return $r
    }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        $r.Status = 'missing'; $r.Note = 'does not exist'; return $r
    }

    try {
        $rootAttr = [int][System.IO.File]::GetAttributes($full)
        if ($rootAttr -band [int][System.IO.FileAttributes]::ReparsePoint) {
            $r.Status = 'bad'; $r.Note = 'is a junction/symlink - not followed'; return $r
        }
    } catch {
        $r.Status = 'bad'; $r.Note = 'cannot read attributes'; return $r
    }

    # -- enumerate ----------------------------------------------------------
    $set = Get-SafeFileList -Root $full -Filter $Filter

    foreach ($f in $set.Files) {
        try { $len = (New-Object System.IO.FileInfo($f)).Length } catch { $len = 0 }

        if ($DryRun) { $r.Freed += $len; $r.Removed++; continue }

        try {
            [System.IO.File]::SetAttributes($f, [System.IO.FileAttributes]::Normal)
            [System.IO.File]::Delete($f)
            $r.Freed += $len
            $r.Removed++
        } catch {
            $r.Skipped++
        }
    }

    # -- remove now-empty subfolders, deepest first, never the root itself ---
    if (-not $DryRun) {
        foreach ($d in ($set.Dirs | Sort-Object -Property Length -Descending)) {
            try {
                if ([System.IO.Directory]::GetFileSystemEntries($d).Length -eq 0) {
                    [System.IO.Directory]::Delete($d, $false)
                }
            } catch { }
        }
    }

    if ($r.Removed -eq 0 -and $r.Skipped -gt 0) { $r.Status = 'locked'; $r.Note = 'files in use' }
    return $r
}

function Invoke-CleanFile {
    param([Parameter(Mandatory)][string]$Path, [string]$Label = $Path)

    $r = [pscustomobject]@{
        Label = $Label; Path = $Path
        Freed = [int64]0; Removed = 0; Skipped = 0
        Status = 'ok'; Note = ''
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $r.Status = 'missing'; $r.Note = 'does not exist'; return $r
    }
    try { $len = (New-Object System.IO.FileInfo($Path)).Length } catch { $len = 0 }

    if ($DryRun) { $r.Freed = $len; $r.Removed = 1; return $r }

    try {
        [System.IO.File]::SetAttributes($Path, [System.IO.FileAttributes]::Normal)
        [System.IO.File]::Delete($Path)
        $r.Freed = $len; $r.Removed = 1
    } catch {
        $r.Skipped = 1; $r.Status = 'locked'; $r.Note = 'in use'
    }
    return $r
}

# ------------------------------------------------------------------ setup ----

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host 'This cleaner must run elevated (Administrator).' -ForegroundColor Red
    Write-Host 'Run Clear.bat directly - it elevates itself.' -ForegroundColor Yellow
    Wait-Close 10
    exit 1
}

$build  = [Environment]::OSVersion.Version.Build
$cv     = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue
$disp   = $cv.DisplayVersion
$ubr    = $cv.UBR

$logPath = $null
if ($Log) {
    $logDir = Join-Path $env:ProgramData 'ClearTool\Logs'
    try {
        New-Item -ItemType Directory -Path $logDir -Force -ErrorAction Stop | Out-Null
        $logPath = Join-Path $logDir ('clear-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
        Start-Transcript -LiteralPath $logPath -Force | Out-Null
    } catch { $logPath = $null }
}

Write-Host ''
Write-Host '  Windows temp / cache cleaner' -ForegroundColor White
Write-Host ('  OS: Windows {0} (build {1}.{2})   Mode: {3}' -f `
    $disp, $build, $ubr, $(if ($DryRun) { 'DRY RUN - nothing is deleted' } else { 'LIVE' })) `
    -ForegroundColor DarkGray
if ($DryRun) { Write-Host '  DRY RUN: this is a preview only.' -ForegroundColor Yellow }

# ---------------------------------------------------------------- targets ----
# Group -> switch that enables it.  'base' is always on.

$targets = @(
    # --- always safe -------------------------------------------------------
    @{ Group='base'; Label='User temp (%TEMP%)';          Path=$env:TEMP }
    @{ Group='base'; Label='Windows temp';                Path=(Join-Path $env:SystemRoot 'Temp') }
    @{ Group='base'; Label='Internet cache (WinINet)';    Path=(Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\INetCache') }
    @{ Group='base'; Label='WER reports (user)';          Path=(Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\WER') }
    @{ Group='base'; Label='WER reports (system)';        Path=(Join-Path $env:ProgramData 'Microsoft\Windows\WER') }
    @{ Group='base'; Label='Crash dumps (user)';          Path=(Join-Path $env:LOCALAPPDATA 'CrashDumps') }
    @{ Group='base'; Label='DirectX shader cache';        Path=(Join-Path $env:LOCALAPPDATA 'D3DSCache') }
    @{ Group='base'; Label='Explorer thumb/icon cache';   Path=(Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'); Filter=@('thumbcache_*.db','iconcache_*.db') }

    # --- Windows Update ----------------------------------------------------
    @{ Group='winupdate'; Label='Windows Update downloads'; Path=(Join-Path $env:SystemRoot 'SoftwareDistribution\Download') }
    @{ Group='winupdate'; Label='Windows Setup logs';       Path=(Join-Path $env:SystemRoot 'Panther') }
    @{ Group='winupdate'; Label='Delivery Optimization';    Path=(Join-Path $env:ProgramData 'Microsoft\Windows\DeliveryOptimization\Cache') }

    # --- crash dumps -------------------------------------------------------
    @{ Group='dumps'; Label='LiveKernelReports'; Path=(Join-Path $env:SystemRoot 'LiveKernelReports') }
    @{ Group='dumps'; Label='Kernel minidumps';  Path=(Join-Path $env:SystemRoot 'Minidump') }

    # --- perf trade-off ----------------------------------------------------
    @{ Group='prefetch'; Label='Prefetch data'; Path=(Join-Path $env:SystemRoot 'Prefetch'); Filter=@('*.pf') }

    # --- opt-in, user data ------------------------------------------------
    @{ Group='roottemp'; Label='C:\Temp (user data!)'; Path=(Join-Path $env:SystemDrive 'Temp') }
)

# Add the default per-user temp folder only when %TEMP% points somewhere else,
# otherwise the same folder would be cleaned (and counted) twice.
$addDefaultTemp = $true
if ($env:TEMP) {
    try {
        $leaf = Split-Path -Leaf ([System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\'))
        if (($leaf -eq 'Temp' -or $leaf -eq 'tmp') -and (Test-Path -LiteralPath $env:TEMP -PathType Container)) {
            $addDefaultTemp = $false
        }
    } catch { }
}
if ($addDefaultTemp) {
    $targets += @{ Group='base'; Label='User temp (LocalAppData)'; Path=(Join-Path $env:LOCALAPPDATA 'Temp') }
}

function Test-Group {
    param([string]$Group)
    switch ($Group) {
        'base'      { return $true }
        'winupdate' { return ($WinUpdate -or $All) }
        'dumps'     { return ($Dumps -or $All) }
        'prefetch'  { return ($Prefetch -or $All) }
        'roottemp'  { return $RootTemp }
        default     { return $false }
    }
}

$active = @($targets | Where-Object { Test-Group $_.Group })

# Browser caches need their own process check, so they are handled separately.
$browserTargets = @()
if ($BrowserCache -or $All) {
    $browsers = @('msedge', 'chrome', 'firefox', 'brave', 'opera')
    $running  = @(Get-Process -Name $browsers -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        $browserTargets = @(@{ Group='browser'; Label='Browser caches'; Path=''; Skipped=$true })
    } else {
        foreach ($root in @(
            (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data')
            (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data')
            (Join-Path $env:LOCALAPPDATA 'BraveSoftware\Brave-Browser\User Data')
            (Join-Path $env:LOCALAPPDATA 'Mozilla\Firefox\Profiles')
        )) {
            if (-not (Test-Path -LiteralPath $root)) { continue }
            foreach ($prof in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
                foreach ($sub in @('Cache', 'Code Cache', 'GPUCache', 'DawnCache', 'GrShaderCache', 'ShaderCache', 'cache2')) {
                    $p = Join-Path $prof.FullName $sub
                    if (Test-Path -LiteralPath $p) {
                        $browserTargets += @{ Group='browser'; Label=('{0} - {1}' -f $prof.Name, $sub); Path=$p; Skipped=$false }
                    }
                }
            }
        }
    }
}

# -- plan preview -------------------------------------------------------------
Write-Head 'Planned targets'

if ($DryRun) {
    $preview = [int64]0
    foreach ($t in $active) {
        if ($t.Filter) {
            $set = $null
            if (Test-Path -LiteralPath $t.Path -PathType Container) {
                $set = Get-SafeFileList -Root $t.Path -Filter $t.Filter
                $sz  = [int64]0
                foreach ($f in $set.Files) { try { $sz += (New-Object System.IO.FileInfo($f)).Length } catch {} }
                Write-Host ('  {0,-38} {1}' -f $t.Label, (Format-Size $sz))
                $preview += $sz
            } else {
                Write-Host ('  {0,-38} {1}' -f $t.Label, '          -')
            }
        } else {
            $sz = [int64]0
            if (Test-Path -LiteralPath $t.Path -PathType Container) {
                try {
                    $sz = [int64](Get-ChildItem -LiteralPath $t.Path -Recurse -Force -File -ErrorAction SilentlyContinue |
                                  Measure-Object -Property Length -Sum).Sum
                } catch { $sz = 0 }
                Write-Host ('  {0,-38} {1}' -f $t.Label, (Format-Size $sz))
            } else {
                Write-Host ('  {0,-38} {1}' -f $t.Label, '          -')
            }
            $preview += $sz
        }
    }
    foreach ($t in $browserTargets) {
        if ($t.Skipped) { Write-Host ('  {0,-38} {1}' -f $t.Label, '  deferred (browser running)') }
    }
    Write-Host ''
    Write-Host ('  Approximate reclaimable: {0}' -f (Format-Size $preview)) -ForegroundColor Cyan
    Write-Host ''
    Write-Host '  Nothing was deleted (dry run).' -ForegroundColor Yellow
    if ($logPath) { Stop-Transcript | Out-Null }
    Wait-Close
    exit 0
}

foreach ($t in $active) { Write-Host ('  - {0}' -f $t.Label) -ForegroundColor DarkGray }
foreach ($t in $browserTargets) {
    if ($t.Skipped) { Write-Host '  - Browser caches (deferred: close the browser first)' -ForegroundColor DarkYellow }
    else            { Write-Host ('  - {0}' -f $t.Label) -ForegroundColor DarkGray }
}

Write-Host ''

# ------------------------------------------------------------------- work ----

$results  = New-Object System.Collections.Generic.List[object]
$total    = [int64]0

# -- Windows Update cache: needs its services stopped -------------------------
$winUpdateTargets = @($active | Where-Object { $_.Group -eq 'winupdate' })
$services = @('wuauserv', 'bits', 'dosvc')
$stopped  = @()

if ($winUpdateTargets.Count -gt 0) {
    Write-Head 'Stopping update services'
    foreach ($svc in $services) {
        $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
        if ($s -and $s.Status -eq 'Running') {
            try {
                Stop-Service -Name $svc -Force -ErrorAction Stop
                $stopped += $svc
                Write-Ok "stopped $svc"
            } catch {
                Write-Skip "could not stop $svc (continuing)"
            }
        }
    }
}

try {
    Write-Head 'Cleaning'

    foreach ($t in $active) {
        $r = Invoke-CleanDirectory -Path $t.Path -Label $t.Label -Filter $t.Filter
        $results.Add($r)

        switch ($r.Status) {
            'ok'      { Write-Ok   ('{0,-38} {1}' -f $r.Label, (Format-Size $r.Freed)) ; $total += $r.Freed }
            'missing' { Write-Host ('  [ -- ] {0,-38} not present' -f $r.Label) -ForegroundColor DarkGray }
            'locked'  { Write-Skip ('{0,-38} {1} (in use)' -f $r.Label, (Format-Size $r.Freed)) ; $total += $r.Freed }
            default   { Write-Skip ('{0,-38} {1}' -f $r.Label, $r.Note) }
        }
    }

    foreach ($t in $browserTargets) {
        if ($t.Skipped) { Write-Skip 'Browser caches - close all browsers and rerun'; continue }
        $r = Invoke-CleanDirectory -Path $t.Path -Label $t.Label
        $results.Add($r)
        if ($r.Status -eq 'ok' -or $r.Status -eq 'locked') {
            Write-Ok ('{0,-38} {1}' -f $r.Label, (Format-Size $r.Freed))
            $total += $r.Freed
        }
    }

    # -- single large files ---------------------------------------------------
    if ($Dumps -or $All) {
        foreach ($f in @((Join-Path $env:SystemRoot 'MEMORY.DMP'), (Join-Path $env:SystemRoot 'temp\MEMORY.DMP'))) {
            $r = Invoke-CleanFile -Path $f -Label (Split-Path $f -Leaf)
            if ($r.Status -ne 'missing') {
                $results.Add($r)
                if ($r.Status -eq 'ok') { Write-Ok ('{0,-38} {1}' -f $r.Label, (Format-Size $r.Freed)); $total += $r.Freed }
                else                    { Write-Skip ('{0,-38} in use' -f $r.Label) }
            }
        }
    }

    # -- Delivery Optimization (cmdlet handles its own service) --------------
    if ($WinUpdate -or $All) {
        if (Get-Command Delete-DeliveryOptimizationCache -ErrorAction SilentlyContinue) {
            try {
                Delete-DeliveryOptimizationCache -Force -ErrorAction Stop
                Write-Ok 'Delivery Optimization cache purged'
            } catch { Write-Skip 'Delivery Optimization cache could not be purged' }
        }
    }

    # -- Recycle Bin ----------------------------------------------------------
    if ($RecycleBin -or $All) {
        try {
            Clear-RecycleBin -Force -ErrorAction Stop
            Write-Ok 'Recycle Bin emptied'
        } catch {
            Write-Skip 'Recycle Bin already empty or in use'
        }
    }
}
finally {
    if ($stopped.Count -gt 0) {
        Write-Head 'Restarting services'
        foreach ($svc in $stopped) {
            try { Start-Service -Name $svc -ErrorAction Stop; Write-Ok "started $svc" }
            catch { Write-Err "could not start $svc - start it manually" }
        }
    }
}

# -- WinSxS component cleanup -------------------------------------------------
if ($ComponentCleanup) {
    Write-Head 'WinSxS component cleanup (this can take a while)'
    $dismArgs = @('/Online', '/Cleanup-Image', '/StartComponentCleanup')
    if ($ResetBase) { $dismArgs += '/ResetBase' }
    try {
        $p = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\Dism.exe') `
                           -ArgumentList $dismArgs -Wait -NoNewWindow -PassThru -ErrorAction Stop
        if ($p.ExitCode -eq 0) { Write-Ok 'WinSxS cleanup completed' }
        else                   { Write-Skip ('DISM returned code {0}' -f $p.ExitCode) }
    } catch {
        Write-Err 'DISM could not be started'
    }
}

# ----------------------------------------------------------------- summary ----

Write-Head 'Summary'
$filesCleared = 0
foreach ($r in $results) { $filesCleared += $r.Removed }
$filesSkipped = 0
foreach ($r in $results) { $filesSkipped += $r.Skipped }

Write-Host ('  Space reclaimed : {0}' -f (Format-Size $total)) -ForegroundColor Green
Write-Host ('  Files removed   : {0:N0}' -f $filesCleared)
if ($filesSkipped -gt 0) {
    Write-Host ('  Files in use    : {0:N0}  (left alone, harmless)' -f $filesSkipped) -ForegroundColor DarkYellow
}
Write-Host ''
Write-Host '  Kept safe: cookies, WebCache, browsing history, C:\Temp, WinSxS.' -ForegroundColor DarkGray
Write-Host '  Bigger cleanup:  Clear.bat -All    (adds Windows Update cache)' -ForegroundColor DarkGray

if ($logPath) {
    Write-Host ''
    Write-Host ("  Log: {0}" -f $logPath) -ForegroundColor DarkGray
    Stop-Transcript | Out-Null
}

Wait-Close
exit 0
