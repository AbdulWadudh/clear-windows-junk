<#
.SYNOPSIS
    Windows 11 / Server 2022+ junk cleaner. Interactive menu, live progress, freed-space report.

.DESCRIPTION
    Replacement for the old XP-era .bat cleaner. Differences that matter:
      * Deletes folder CONTENTS, never the folder itself -> ACLs and ownership survive.
      * Drops dead XP paths (system32\dllcache, "Local Settings\*", Cookies, Recent junctions).
      * Adds the caches that actually hold gigabytes on Windows 11.
      * Measures before/after per location, reports freed vs. pending (locked) bytes.

    Run with no arguments -> interactive: scans everything, shows a checklist, you pick.
    Pass any -Include* switch, -All or -NonInteractive -> unattended, no prompts.

.EXAMPLE
    .\Clear-WindowsJunk.ps1                    # interactive checklist
.EXAMPLE
    .\Clear-WindowsJunk.ps1 -DryRun            # scan + report, delete nothing
.EXAMPLE
    .\Clear-WindowsJunk.ps1 -Unlock            # close apps holding locked files, then retry
.EXAMPLE
    .\Clear-WindowsJunk.ps1 -All -NonInteractive -MinAgeDays 1
#>
[CmdletBinding()]
param(
    [int]$MinAgeDays = 1,
    [switch]$DryRun,
    [switch]$NonInteractive,
    [switch]$IncludeWindowsUpdate,   # SoftwareDistribution + Delivery Optimization (stops services)
    [switch]$IncludeBrowserCache,    # Edge / Chrome / Brave / Firefox (close them first)
    [switch]$IncludeThumbnails,      # thumbcache/iconcache (restarts Explorer)
    [switch]$IncludePrefetch,        # legacy; first boots after are slower
    [switch]$IncludeWindowsOld,      # Windows.old + WINDOWS.~BT/~WS  (blocks rollback)
    [switch]$IncludeComponentStore,  # DISM /StartComponentCleanup (blocks update uninstall)
    [switch]$Unlock,                 # close apps holding locked files, then retry the delete
    [switch]$Restart,                # relaunch everything -Unlock closed, without asking
    [switch]$ForceClose,             # kill apps that ignore the close request (risks unsaved work)
    [int[]]$ProtectPid = @(),        # extra PIDs to never close (set automatically when relaunching elevated)
    [string[]]$NeverCloseApp = @(),  # extra process names to never close, e.g. -NeverCloseApp code,idman
    [switch]$All,
    [switch]$SelfTest
)

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference    = 'Continue'

#==============================================================================
#  0.  SETTINGS FILE
#     when the script is opened by double-click there is no command line,
#     so options come from Clear-WindowsJunk.settings.txt next to it
#==============================================================================

$SettingsPath = Join-Path (Split-Path -Parent $PSCommandPath) 'Clear-WindowsJunk.settings.txt'

$SettingsTemplate = @'
# Clear-WindowsJunk settings
# Read every run. Anything passed on a command line wins over this file.
# Remove the leading # to switch a line on.

# Leave files touched in the last N days alone. 0 = delete everything found.
# MinAgeDays = 1

# Close apps that hold locked files, then retry the delete.
# Unlock = false

# Kill apps that ignore the polite close request. Unsaved work in them is lost.
# ForceClose = false

# Relaunch everything that was closed, without asking.
# Restart = false

# Never close these, whatever they are holding. Comma separated, .exe optional.
# NeverCloseApp = T3 Code, idman, code

# Report only, never delete.
# DryRun = false
'@

# key = value, # comments, blank lines. Values may contain '='.
function Read-Settings {
    param([string]$Path)
    $out = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $out }
    foreach ($line in (Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue)) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $out[$t.Substring(0, $i).Trim().ToLower()] = $t.Substring($i + 1).Trim()
    }
    $out
}

function Test-SettingOn {
    param($Value)
    "$Value" -match '^(1|true|yes|on)$'
}

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    Set-Content -LiteralPath $SettingsPath -Value $SettingsTemplate -Encoding UTF8 -ErrorAction SilentlyContinue
}
$Cfg = Read-Settings $SettingsPath

# a real command-line argument always beats the file
if (-not $PSBoundParameters.ContainsKey('MinAgeDays')    -and $Cfg.ContainsKey('minagedays'))    { $MinAgeDays = [int]$Cfg['minagedays'] }
if (-not $PSBoundParameters.ContainsKey('DryRun')        -and (Test-SettingOn $Cfg['dryrun']))     { $DryRun     = [switch]$true }
if (-not $PSBoundParameters.ContainsKey('Unlock')        -and (Test-SettingOn $Cfg['unlock']))     { $Unlock     = [switch]$true }
if (-not $PSBoundParameters.ContainsKey('ForceClose')    -and (Test-SettingOn $Cfg['forceclose'])) { $ForceClose = [switch]$true }
if (-not $PSBoundParameters.ContainsKey('Restart')       -and (Test-SettingOn $Cfg['restart']))    { $Restart    = [switch]$true }
if (-not $PSBoundParameters.ContainsKey('NeverCloseApp') -and $Cfg.ContainsKey('nevercloseapp')) {
    $NeverCloseApp = @($Cfg['nevercloseapp'] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

if ($All) {
    $IncludeWindowsUpdate = $IncludeBrowserCache = $IncludeThumbnails = $true
    $IncludePrefetch = $IncludeWindowsOld = $IncludeComponentStore = $true
}

$switchUsed = $All -or $IncludeWindowsUpdate -or $IncludeBrowserCache -or $IncludeThumbnails -or
              $IncludePrefetch -or $IncludeWindowsOld -or $IncludeComponentStore
$DryRunOn    = [bool]$DryRun
$UnlockOn    = [bool]$Unlock
$RestartAll   = [bool]$Restart
$ForceCloseOn = [bool]$ForceClose
$ClosedApps   = @()      # what -Unlock closed, so it can be put back afterwards
$Interactive = -not ($NonInteractive -or $switchUsed) -and
               [Environment]::UserInteractive -and -not $SelfTest
$UnlockAsk   = $Interactive

#==============================================================================
#  1.  HELPERS
#     sizes, free space, age-aware deletion, string formatting
#==============================================================================


function Format-Size {
    param([double]$Bytes)
    $u = 'B','KB','MB','GB','TB'; $i = 0
    while ($Bytes -ge 1024 -and $i -lt 4) { $Bytes /= 1024; $i++ }
    '{0,7:N2} {1}' -f $Bytes, $u[$i]
}

function Test-Admin {
    ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-FreeSpace {
    param([string]$Drive = $env:SystemDrive)
    (Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$Drive'").FreeSpace
}

# Size of a path. Skips reparse points so junctions can't send us in circles.
function Measure-Target {
    param([string]$Path)
    $bytes = [int64]0; $files = 0
    if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]@{ Bytes = 0; Files = 0 } }

    $item = Get-Item -LiteralPath $Path -Force
    if ($item -and -not $item.PSIsContainer) {
        return [pscustomobject]@{ Bytes = $item.Length; Files = 1 }
    }
    $stack = New-Object System.Collections.Stack
    $stack.Push($item.FullName)
    while ($stack.Count) {
        foreach ($c in Get-ChildItem -LiteralPath $stack.Pop() -Force -ErrorAction SilentlyContinue) {
            if ($c.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($c.PSIsContainer) { $stack.Push($c.FullName) } else { $bytes += $c.Length; $files++ }
        }
    }
    [pscustomobject]@{ Bytes = $bytes; Files = $files }
}

# Manual recursion on purpose:
#   * every FILE is judged on its own timestamp - a folder's mtime says nothing about
#     how fresh the files inside it are, so `rm -Recurse` on an old-looking folder
#     would take a file an app wrote seconds ago.
#   * reparse points are skipped, never followed - a junction in a temp folder must
#     not turn into a delete of whatever it points at.
function Remove-Aged {
    param([string]$Dir, [datetime]$Cut)
    foreach ($c in Get-ChildItem -LiteralPath $Dir -Force -ErrorAction SilentlyContinue) {
        if ($c.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if ($c.PSIsContainer) {
            Remove-Aged -Dir $c.FullName -Cut $Cut
            # keep the folder skeleton an app expects to find on next launch - only drop a
            # directory that is empty AND older than the cutoff itself
            if ($c.LastWriteTime -le $Cut -and
                -not (Get-ChildItem -LiteralPath $c.FullName -Force -ErrorAction SilentlyContinue)) {
                Remove-Item -LiteralPath $c.FullName -Force -ErrorAction SilentlyContinue
            }
        }
        elseif ($c.LastWriteTime -le $Cut) {
            Remove-Item -LiteralPath $c.FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

# Empties a folder (keeps the folder), or deletes a single file.
function Clear-Target {
    param([string]$Path, [int]$MinAgeDays = 0)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $cut = if ($MinAgeDays -le 0) { [datetime]::MaxValue } else { (Get-Date).AddDays(-$MinAgeDays) }

    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item) { return }
    if (-not $item.PSIsContainer) {
        if ($item.LastWriteTime -le $cut) { Remove-Item -LiteralPath $item.FullName -Force }
        return
    }
    Remove-Aged -Dir $item.FullName -Cut $cut
}

function Resolve-Targets {
    param([string[]]$Patterns)
    $out = @()
    foreach ($p in $Patterns) {
        if ($p -match '[\*\?]') { $out += (Resolve-Path -Path $p).Path }
        else                    { $out += $p }
    }
    $out | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
}

function Write-Row {
    param([int]$Y, [string]$Text, [string]$Color = 'Gray', [string]$Back)
    if ($Y -lt 0 -or $Y -ge [Console]::WindowHeight) { return }
    $w = [Math]::Max(1, [Console]::WindowWidth - 1)
    if ($Text.Length -gt $w) { $Text = $Text.Substring(0, $w) }
    [Console]::SetCursorPosition(0, $Y)
    if ($Back) { Write-Host $Text.PadRight($w) -ForegroundColor $Color -BackgroundColor $Back -NoNewline }
    else       { Write-Host $Text.PadRight($w) -ForegroundColor $Color -NoNewline }
}

# Turns "1,3-5" into zero-based indexes. Pure, so the typed fallback is testable.
function Resolve-Selection {
    # $Text is deliberately untyped: [string] would coerce a null (EOF) into '',
    # which means "all", so a closed stdin would relaunch everything.
    param($Text, [int]$Count)
    if ($null -eq $Text -or $Text -match '^\s*[nN]\s*$') { return @() }
    if ($Text.Trim() -eq '' -or $Text -match '^\s*[aA]\s*$') { return @(0..($Count - 1)) }
    $out = @()
    foreach ($n in Expand-IndexList $Text) {
        if ($n -ge 1 -and $n -le $Count -and $out -notcontains ($n - 1)) { $out += $n - 1 }
    }
    $out
}

# Generic checkbox picker. Draws in place under whatever is already on screen, so the
# log above it survives. Falls back to typed indexes when there is no console to drive.
function Select-Items {
    param([object[]]$Items, [scriptblock]$Label, [string]$Title, [bool]$DefaultOn = $true)

    if (-not $Items) { return @() }
    $n = $Items.Count
    $fits = -not [Console]::IsInputRedirected -and ($n + 4) -lt [Console]::WindowHeight

    if (-not $fits) {
        Write-Host ""
        Write-Host "  $Title" -ForegroundColor Cyan
        for ($i = 0; $i -lt $n; $i++) { Write-Host ("   {0,2}. {1}" -f ($i + 1), (& $Label $Items[$i])) }
        $idx = Resolve-Selection (Read-Line "  [ENTER] all / [n] none / e.g. 1,3-4") $n
        return @($idx | ForEach-Object { $Items[$_] })
    }

    $sel = @{}
    for ($i = 0; $i -lt $n; $i++) { $sel[$i] = $DefaultOn }
    $cur = 0
    $buf = ''
    $rows = $n + 3
    1..$rows | ForEach-Object { Write-Host '' }          # reserve the region, then draw into it
    $top = [Math]::Max(0, [Console]::CursorTop - $rows)

    [Console]::CursorVisible = $false
    try {
        while ($true) {
            Write-Row $top "  $Title" 'Cyan'
            for ($i = 0; $i -lt $n; $i++) {
                $mark = if ($sel[$i]) { 'x' } else { ' ' }
                $text = '{0,3}. [{1}] {2}' -f ($i + 1), $mark, (& $Label $Items[$i])
                if ($i -eq $cur) { Write-Row ($top + 1 + $i) ('>' + $text.Substring(1)) 'White' 'DarkBlue' }
                else             { Write-Row ($top + 1 + $i) $text $(if ($sel[$i]) { 'White' } else { 'Gray' }) }
            }
            Write-Row ($top + 1 + $n) ("  {0} of {1} selected{2}" -f @($sel.Values | Where-Object { $_ }).Count, $n,
                       $(if ($buf -ne '') { "     typing: $buf" } else { '' })) 'Green'
            Write-Row ($top + 2 + $n) '  1-9 pick   up/down move   SPACE toggle   a all   n none   ENTER confirm   q none' 'DarkGray'

            $k = [Console]::ReadKey($true)

            # digits accumulate so lists longer than 9 stay reachable; a number resolves as
            # soon as it cannot grow any further, so single digits still feel instant
            if ("$($k.KeyChar)" -match '^[0-9]$') {
                $buf = [int]("$buf" + $k.KeyChar)
                if ($buf * 10 -gt $n) {
                    if ($buf -ge 1 -and $buf -le $n) { $cur = $buf - 1; $sel[$cur] = -not $sel[$cur] }
                    $buf = ''
                }
                continue
            }
            $buf = ''
            switch -Regex ($k.Key.ToString()) {
                '^UpArrow$'   { if ($cur -gt 0)      { $cur-- }; break }
                '^DownArrow$' { if ($cur -lt $n - 1) { $cur++ }; break }
                '^Home$'      { $cur = 0; break }
                '^End$'       { $cur = $n - 1; break }
                '^Spacebar$'  { $sel[$cur] = -not $sel[$cur]; break }
                '^Enter$'     { return @(0..($n - 1) | Where-Object { $sel[$_] } | ForEach-Object { $Items[$_] }) }
                '^Escape$'    { return @() }
                default {
                    switch -Regex ([string]$k.KeyChar) {
                        '^[jJ]$' { if ($cur -lt $n - 1) { $cur++ } }
                        '^[kK]$' { if ($cur -gt 0)      { $cur-- } }
                        '^[aA]$' { 0..($n - 1) | ForEach-Object { $sel[$_] = $true } }
                        '^[nN]$' { 0..($n - 1) | ForEach-Object { $sel[$_] = $false } }
                        '^[qQ]$' { return @() }
                    }
                }
            }
        }
    }
    finally {
        [Console]::CursorVisible = $true
        [Console]::SetCursorPosition(0, [Math]::Min($top + 3 + $n, [Console]::WindowHeight - 1))
    }
}

# Measure-Object -Property can't read hashtable keys on PS 5.1, so sum by hand.
function Get-Sum {
    param($Items, [string]$Key)
    $t = [int64]0
    foreach ($i in $Items) { $t += [int64]$i.$Key }
    $t
}

#==============================================================================
#  2.  UNLOCK
#     Restart Manager interop: who holds a file, and closing them safely
#==============================================================================

# Restart Manager (rstrtmgr.dll) is what Explorer uses for "the file is open in
# another program". It tells us exactly which PIDs hold a set of files.

$RmSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class JunkLock
{
    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

    const int CCH_RM_MAX_APP_NAME = 255;
    const int CCH_RM_MAX_SVC_NAME = 63;
    const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)] public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);
    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint pSessionHandle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
        uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);
    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

    // each entry: "pid|friendly name|apptype|service short name"   apptype 3 = service, 1000 = critical
    public static string[] GetLockers(string[] files)
    {
        var result = new List<string>();
        if (files == null || files.Length == 0) return result.ToArray();

        uint handle;
        if (RmStartSession(out handle, 0, Guid.NewGuid().ToString("N")) != 0) return result.ToArray();
        try
        {
            if (RmRegisterResources(handle, (uint)files.Length, files, 0, null, 0, null) != 0) return result.ToArray();

            uint needed = 0, have = 0, reasons = 0;
            int rc = RmGetList(handle, out needed, ref have, null, ref reasons);
            if (rc == ERROR_MORE_DATA && needed > 0)
            {
                var info = new RM_PROCESS_INFO[needed];
                have = needed;
                if (RmGetList(handle, out needed, ref have, info, ref reasons) == 0)
                    for (int i = 0; i < have; i++)
                        result.Add(info[i].Process.dwProcessId + "|" + info[i].strAppName + "|"
                                 + info[i].ApplicationType + "|" + info[i].strServiceShortName);
            }
        }
        finally { RmEndSession(handle); }
        return result.ToArray();
    }
}
'@

# Never close these, whatever they are holding. Killing any of them means a bugcheck,
# a logon wipe, or shooting the script's own host out from under it.
$NeverClose = @(
    'system','idle','registry','memory compression','smss','csrss','wininit','winlogon','logonui',
    'services','lsass','lsaiso','fontdrvhost','dwm','sihost','ctfmon','svchost','wmiprvse','audiodg',
    'msmpeng','nissrv','securityhealthservice','securityhealthsystray','trustedinstaller','tiworker',
    'conhost','openconsole','windowsterminal','powershell','pwsh','cmd','wudfhost','dllhost'
)

foreach ($n in $NeverCloseApp) {
    $n = ($n -replace '\.exe$', '').Trim().ToLower()
    if ($n -and $NeverClose -notcontains $n) { $NeverClose += $n }
}

$script:RmReady = $null

function Initialize-Rm {
    if ($null -ne $script:RmReady) { return $script:RmReady }
    try {
        if (-not ('JunkLock' -as [type])) { Add-Type -TypeDefinition $RmSource -ErrorAction Stop }
        $script:RmReady = $true
    } catch {
        $script:RmReady = $false
        Write-Host ("  Restart Manager unavailable: {0}" -f $_.Exception.Message) -ForegroundColor DarkYellow
    }
    $script:RmReady
}

# Every process between us and the root: the shell, the terminal, the editor that
# launched it. A name blacklist can't cover these - "bash.exe", "T3 Code", whatever
# the user happens to be driving - so protect them by PID lineage instead.
function Get-SelfTree {
    if ($null -ne $script:SelfTree) { return $script:SelfTree }
    $parent = @{}
    foreach ($p in Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId -ErrorAction SilentlyContinue) {
        $parent[[int]$p.ProcessId] = [int]$p.ParentProcessId
    }
    $ids = @(); $cur = $PID; $hops = 0
    while ($cur -and $parent.ContainsKey($cur) -and $hops -lt 64) {
        $ids += $cur
        $cur = $parent[$cur]
        $hops++
    }
    if ($cur) { $ids += $cur }
    foreach ($extra in $ProtectPid) { if ($ids -notcontains $extra) { $ids += [int]$extra } }
    $script:SelfTree = $ids
    $ids
}

# Safety gate. Pure, so the self-test can prove the dangerous cases stay refused.
# Returns '' when closeable, otherwise the reason it was refused.
function Get-CloseVerdict {
    param([int]$ProcessId, [string]$Name, [int]$Type, [int]$SelfPid = $PID, $Ancestors = @())
    if ($ProcessId -le 4)                { return 'system' }          # System / Idle
    if ($ProcessId -eq $SelfPid)         { return 'this script' }
    if ($Ancestors -contains $ProcessId) { return 'this session' }    # our shell / terminal / editor
    if ($Type -eq 1000)                  { return 'critical' }        # RmCritical
    if ($Type -eq 3)                     { return 'service' }         # stopping arbitrary services isn't our call
    $n = ($Name -replace '\.exe$', '').Trim().ToLower()
    if (-not $n)                  { return 'unnamed' }
    if ($NeverClose -contains $n) { return 'protected' }
    ''
}

function Test-Closeable {
    param([int]$ProcessId, [string]$Name, [int]$Type, [int]$SelfPid = $PID, $Ancestors = @())
    -not (Get-CloseVerdict -ProcessId $ProcessId -Name $Name -Type $Type -SelfPid $SelfPid -Ancestors $Ancestors)
}

# 38 comma-separated entries is not a list. Collapse to one row per app.
function Format-LockerList {
    param($Procs, [int]$MaxPids = 3)
    $Procs | Group-Object Display | Sort-Object @{ E = { $_.Count }; Descending = $true }, Name | ForEach-Object {
        $ids   = @($_.Group | ForEach-Object { $_.Id } | Sort-Object)
        $shown = if ($ids.Count -le $MaxPids) { $ids -join ', ' }
                 else { (($ids | Select-Object -First $MaxPids) -join ', ') + (", +{0} more" -f ($ids.Count - $MaxPids)) }
        $label = if ($_.Count -eq 1) { 'process ' } else { 'processes' }
        '{0,-32} {1,3} {2}  (pid {3})' -f $_.Name, $_.Count, $label, $shown
    }
}

function Get-LockingProcess {
    param([string[]]$Files)
    if (-not (Initialize-Rm) -or -not $Files) { return @() }

    $seen = @{}
    foreach ($row in [JunkLock]::GetLockers($Files)) {
        $f = $row -split '\|'
        $procId = [int]$f[0]
        if ($seen.ContainsKey($procId)) { continue }
        $p    = Get-Process -Id $procId -ErrorAction SilentlyContinue
        $name = if ($p) { $p.ProcessName } else { $f[1] }
        $seen[$procId] = [pscustomobject]@{
            Id        = $procId
            Name      = $name
            Display   = $(if ($f[1]) { $f[1] } else { $name })
            Type      = [int]$f[2]
            Service   = $f[3]
            Reason    = (Get-CloseVerdict -ProcessId $procId -Name $name -Type ([int]$f[2]) -Ancestors (Get-SelfTree))
        }
        $seen[$procId] | Add-Member -NotePropertyName Closeable -NotePropertyValue (-not $seen[$procId].Reason)
    }
    $seen.Values
}

# Read off how to start a process again. Must run while it is still alive - once it
# exits, the exe path and command line are gone.
function Get-LaunchInfo {
    param([int]$ProcessId)
    $ci   = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    $path = if ($ci -and $ci.ExecutablePath) { $ci.ExecutablePath } elseif ($proc) { $proc.Path } else { $null }

    # strip the exe off the front of the command line, whatever quoting was used
    $cmdArgs = ''
    if ($ci -and $ci.CommandLine) {
        $cl = $ci.CommandLine.Trim()
        if ($cl.StartsWith('"')) { $cmdArgs = ($cl -replace '^"[^"]*"\s*', '') }
        else                     { $cmdArgs = ($cl -replace '^\S+\s*', '') }
    }
    $display = if ($proc -and $proc.MainWindowTitle) { $proc.ProcessName } elseif ($path) { [IO.Path]::GetFileNameWithoutExtension($path) } else { '' }

    [pscustomobject]@{ Path = $path; Args = $cmdArgs.Trim(); Display = $display }
}

# Pick which of the closed apps to bring back. Everything ticked by default.
function Select-RestartApp {
    param($Apps)
    Select-Items -Items $Apps -Title 'Closed to free the files - relaunch which?' -DefaultOn $true -Label {
        param($a) '{0,-26} {1}' -f $a.Name, $a.Path
    }
}

function Restart-ClosedApp {
    param($Apps, [bool]$Elevated)
    foreach ($a in $Apps) {
        if (-not $a.Path -or -not (Test-Path -LiteralPath $a.Path)) {
            Write-Host ("  could not relaunch {0} - executable path unknown" -f $a.Name) -ForegroundColor DarkYellow
            continue
        }
        if ($Elevated) {
            # hand it to Explorer, which runs as the logged-on user, so the app does not
            # come back elevated just because the cleaner was. Arguments are lost this way.
            Start-Process explorer.exe -ArgumentList ('"{0}"' -f $a.Path)
        }
        elseif ($a.Args) { Start-Process -FilePath $a.Path -ArgumentList $a.Args }
        else             { Start-Process -FilePath $a.Path }
        Write-Host ("  restarted {0}" -f $a.Name) -ForegroundColor Green
    }
}

# Clean shutdown only. Two rules, both learned the hard way:
#   * only the WINDOW OWNER is ever signalled. Chrome's renderers are children -
#     killing one makes the tab show RESULT_CODE_KILLED instead of closing.
#   * children are never force-killed at all. Once the owner exits they unwind on
#     their own; we just wait for them.
# An app that refuses to close (unsaved work, a "really quit?" dialog) is left
# running and reported, unless -ForceClose says otherwise.
function Close-Locker {
    param($Procs, [int]$GraceSeconds = 20, [bool]$Force = $false)

    $live = @()
    foreach ($p in $Procs) {
        $proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        if ($proc) { $live += [pscustomobject]@{ Id = $p.Id; Name = $p.Name; Proc = $proc } }
    }
    if (-not $live) { return @() }

    $owners = @()
    foreach ($g in ($live | Group-Object Name)) {
        $win = @($g.Group | Where-Object { $_.Proc.MainWindowHandle -ne 0 })
        if (-not $win) { $win = @($g.Group | Sort-Object { $_.Proc.StartTime } | Select-Object -First 1) }
        foreach ($o in $win) {
            $o | Add-Member -NotePropertyName Launch -NotePropertyValue (Get-LaunchInfo -ProcessId $o.Id) -Force
            $owners += $o
            # CloseMainWindow only reaches a process that owns a visible main window.
            # Tray apps (IDM) and windowless owners need taskkill's graceful WM_CLOSE,
            # which walks the whole process tree. Still graceful - no /F here.
            $asked = $false
            if ($o.Proc.MainWindowHandle -ne 0) { $asked = $o.Proc.CloseMainWindow() }
            if (-not $asked) { & taskkill.exe /PID $o.Id /T 2>&1 | Out-Null }
        }
    }

    # phase 1: wait for the owners to shut themselves down
    $deadline = (Get-Date).AddSeconds($GraceSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not @($owners | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue })) { break }
        Start-Sleep -Milliseconds 250
    }

    $stubborn = @($owners | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue })
    if ($stubborn) {
        $insist = @()
        if ($Force) { $insist = $stubborn }
        elseif ($UnlockAsk) {
            Write-Host ""
            Write-Host ("  {0} app(s) ignored the close request (a dialog, or no window to close)." -f $stubborn.Count) -ForegroundColor DarkYellow
            $insist = Select-Items -Items $stubborn -DefaultOn $false -Title 'Force these? unsaved work in them is lost' -Label {
                param($o) '{0,-24} pid {1}' -f $o.Name, $o.Id
            }
        }
        else {
            foreach ($o in $stubborn) {
                Write-Host ("         {0} would not close - left running (use -ForceClose to insist)" -f $o.Name) -ForegroundColor DarkYellow
            }
        }
        # kill the tree from the owner down. Killing a lone child is what gives Chrome
        # RESULT_CODE_KILLED; taking the parent out is an ordinary crash-and-restore.
        foreach ($o in $insist) {
            & taskkill.exe /PID $o.Id /T /F 2>&1 | Out-Null
            Write-Host ("         forced {0} (pid {1})" -f $o.Name, $o.Id) -ForegroundColor DarkYellow
        }
        if ($insist) { Start-Sleep -Milliseconds 700 }
    }

    # phase 2: let the children follow their parent out. Never killed.
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        if (-not @($live | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue })) { break }
        Start-Sleep -Milliseconds 250
    }

    $gone   = @()
    $linger = @()
    foreach ($p in $live) {
        if (Get-Process -Id $p.Id -ErrorAction SilentlyContinue) { $linger += $p.Name } else { $gone += $p.Name }
    }
    foreach ($nm in ($linger | Sort-Object -Unique)) {
        if ($gone -contains $nm) { continue }
        Write-Host ("         {0} is still running - its files stay locked" -f $nm) -ForegroundColor DarkGray
    }

    # explorer is special: nothing else puts the shell back
    if ($gone -contains 'explorer' -and -not (Get-Process explorer -ErrorAction SilentlyContinue)) {
        Start-Process explorer
    }

    foreach ($o in $owners) {
        if (Get-Process -Id $o.Id -ErrorAction SilentlyContinue) { continue }   # still up, nothing to restore
        if (-not $o.Launch.Path) { continue }
        if ($o.Name -eq 'explorer') { continue }
        if ($script:ClosedApps | Where-Object { $_.Path -eq $o.Launch.Path }) { continue }
        $script:ClosedApps += [pscustomobject]@{
            Name    = $o.Name
            Display = $o.Launch.Display
            Path    = $o.Launch.Path
            Args    = $o.Launch.Args
        }
    }
    $gone | Sort-Object -Unique
}

# Finds the holders of whatever survived a delete pass and closes them. Returns closed names.
function Invoke-Unlock {
    param($T, [bool]$Ask)

    $files = @()
    foreach ($p in $T.Paths) {
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $item = Get-Item -LiteralPath $p -Force
        if ($item -and -not $item.PSIsContainer) { $files += $item.FullName; continue }
        $files += @(Get-ChildItem -LiteralPath $p -Recurse -Force -File -ErrorAction SilentlyContinue |
                    Select-Object -First 150 | ForEach-Object { $_.FullName })
    }
    $files = @($files | Select-Object -First 200)
    if (-not $files) { return @() }

    $procs = @(Get-LockingProcess $files)
    if (-not $procs) { return @() }

    $ok      = @($procs | Where-Object { $_.Closeable })
    $refused = @($procs | Where-Object { -not $_.Closeable })

    if ($refused) {
        Write-Host ("         {0,-32} {1,3} held open, left alone:" -f 'SKIPPED', $refused.Count) -ForegroundColor DarkGray
        foreach ($g in ($refused | Group-Object Reason | Sort-Object Name)) {
            foreach ($line in (Format-LockerList $g.Group)) {
                Write-Host ("           {0}  - {1}" -f $line, $g.Name) -ForegroundColor DarkGray
            }
        }
    }
    if (-not $ok) { return @() }

    Write-Host ("         {0,-32} {1,3} processes holding files:" -f 'LOCKED BY', $ok.Count) -ForegroundColor DarkYellow
    foreach ($line in (Format-LockerList $ok)) { Write-Host "           $line" -ForegroundColor DarkYellow }

    if ($Ask) {
        # choose per app, not per pid - one row for Chrome, not 29
        $groups = @($ok | Group-Object Display | Sort-Object @{ E = { $_.Count }; Descending = $true }, Name)
        $keep   = Select-Items -Items $groups -DefaultOn $false -Label {
            param($g)
            $ids = @($g.Group | ForEach-Object { $_.Id } | Sort-Object)
            $shown = if ($ids.Count -le 3) { $ids -join ', ' } else { (($ids | Select-Object -First 3) -join ', ') + ", +$($ids.Count - 3) more" }
            '{0,-32} {1,3} {2}  (pid {3})' -f $g.Name, $g.Count, $(if ($g.Count -eq 1) { 'process ' } else { 'processes' }), $shown
        } -Title 'Close which apps? nothing is ticked - pick only what you want closed'
        if (-not $keep) { return @() }
        $ok = @($keep | ForEach-Object { $_.Group })
    }
    Close-Locker -Procs $ok -Force $ForceCloseOn
}

# Read-Host returns $null at EOF and .Trim() on it throws - which, under the script-wide
# SilentlyContinue, leaves the caller's variable holding its PREVIOUS value and spins forever.
function Read-Line {
    param([string]$Prompt)
    $s = Read-Host $Prompt
    if ($null -eq $s) { return $null }      # EOF
    $s.Trim()
}

function Clear-KeyBuffer {
    if ([Console]::IsInputRedirected) { return }
    while ([Console]::KeyAvailable) { [void][Console]::ReadKey($true) }
}

function Wait-AnyKey {
    param([string]$Prompt)
    if ([Console]::IsInputRedirected) { return }
    Clear-KeyBuffer
    Write-Host "  $Prompt" -NoNewline -ForegroundColor DarkGray
    [void][Console]::ReadKey($true)
    Write-Host ""
}

# Counts down then lets the caller exit. Any keypress aborts it. $true = user interrupted.
function Wait-Countdown {
    param([int]$Seconds = 5)
    if ([Console]::IsInputRedirected) { return $false }
    Clear-KeyBuffer
    $pad = ' ' * 20
    for ($s = $Seconds; $s -gt 0; $s--) {
        Write-Host ("`r  Closing in {0}s - press any key to stay...{1}" -f $s, $pad) -NoNewline -ForegroundColor DarkGray
        $t = [Diagnostics.Stopwatch]::StartNew()
        while ($t.ElapsedMilliseconds -lt 1000) {
            if ([Console]::KeyAvailable) {
                [void][Console]::ReadKey($true)
                Write-Host ("`r  Timer cancelled - back to the menu.{0}" -f $pad) -ForegroundColor Green
                return $true
            }
            Start-Sleep -Milliseconds 40
        }
    }
    Write-Host ("`r{0}{1}" -f $pad, $pad) -NoNewline
    return $false
}

# Parses "1,3,5-8" into 1,3,5,6,7,8
function Expand-IndexList {
    param([string]$Text)
    $out = @()
    foreach ($part in ($Text -split '[,\s]+' | Where-Object { $_ })) {
        if ($part -match '^(\d+)-(\d+)$') { $out += [int]$Matches[1]..[int]$Matches[2] }
        elseif ($part -match '^\d+$')     { $out += [int]$part }
    }
    $out
}

# Shared row text so the TUI and the plain fallback can't drift apart.
function Get-ItemLine {
    param($T, [int]$Num, [bool]$Admin, [switch]$Numbered)
    $locked = $T.Admin -and -not $Admin
    $mark   = if ($locked) { '-' } elseif ($T.Sel) { 'x' } else { ' ' }
    $size   = if ($T.NoMeasure) { '      n/a' } else { Format-Size $T.Bytes }
    $note   = if ($locked) { 'needs admin' } elseif ($T.Note) { $T.Note } else { '' }
    $lead   = if ($Numbered) { '{0,3}. ' -f $Num } else { '  ' }
    '{0}[{1}] {2,-26} {3}  {4}' -f $lead, $mark, $T.N, $size, $(if ($note) { "- $note" } else { '' })
}

function Get-ItemColor {
    param($T, [bool]$Admin)
    if ($T.Admin -and -not $Admin) { 'DarkGray' }
    elseif ($T.Sel)                { 'White' }
    else                           { 'Gray' }
}

# Pure: turns state into the exact list of lines to blit. Testable without a console.
function Get-Frame {
    param($Targets, [bool]$Admin, [int]$Cur, [int]$Top, [int]$H, [int]$W,
          [int]$MinAge, [bool]$DryRun, [bool]$Unlock, [string]$FreeText, [string]$Msg)

    # flat render list: group headers interleaved with items
    $list = @(); $group = ''
    for ($i = 0; $i -lt $Targets.Count; $i++) {
        if ($Targets[$i].G -ne $group) {
            $group = $Targets[$i].G
            $list += @{ K = 'h'; T = "  $($group.ToUpper())" }
        }
        $list += @{ K = 'i'; I = $i }
    }
    $rowOf = @{}
    for ($r = 0; $r -lt $list.Count; $r++) { if ($list[$r].K -eq 'i') { $rowOf[[int]$list[$r].I] = $r } }

    $view = [Math]::Max(3, $H - 8)          # 4 header rows + 4 footer rows
    $cr   = [int]$rowOf[$Cur]
    if ($cr -lt $Top)         { $Top = $cr }
    if ($cr -ge $Top + $view) { $Top = $cr - $view + 1 }
    $Top = [Math]::Max(0, [Math]::Min($Top, [Math]::Max(0, $list.Count - $view)))

    $sel = @($Targets | Where-Object { $_.Sel })
    $bar = '  ' + ('-' * [Math]::Max(10, [Math]::Min(72, $W - 4)))

    $rows = @()
    $rows += @{ T = '  Windows Junk Cleaner'; C = 'Cyan' }
    $rows += @{ T = ("  {0}  |  {1}  |  free {2}" -f $env:COMPUTERNAME,
                     $(if ($Admin) { 'elevated' } else { 'NOT elevated' }), $FreeText); C = 'DarkGray' }
    $rows += @{ T = $bar; C = 'DarkGray' }
    $rows += @{ T = ("  {0}/{1} selected   {2}   {3} files   keep>{4}d   dry-run {5}   unlock {6}" -f `
                     $sel.Count, $Targets.Count, (Format-Size (Get-Sum $sel 'Bytes')).Trim(),
                     (Get-Sum $sel 'FileCount'), $MinAge, $(if ($DryRun) { 'ON' } else { 'off' }),
                     $(if ($Unlock) { 'ON - closes apps' } else { 'off' }))
                C = $(if ($Unlock) { 'Red' } elseif ($DryRun) { 'Yellow' } else { 'Green' }) }

    for ($r = 0; $r -lt $view; $r++) {
        $idx = $Top + $r
        if ($idx -ge $list.Count) { $rows += @{ T = ''; C = 'Gray' }; continue }
        $e = $list[$idx]
        if ($e.K -eq 'h') { $rows += @{ T = $e.T; C = 'Cyan' }; continue }
        $t    = $Targets[[int]$e.I]
        $line = Get-ItemLine -T $t -Num ([int]$e.I + 1) -Admin $Admin -Numbered
        if ([int]$e.I -eq $Cur) { $rows += @{ T = '>' + $line.Substring(1); C = 'White'; B = 'DarkBlue' } }
        else                    { $rows += @{ T = $line; C = (Get-ItemColor $t $Admin) } }
    }

    $rows += @{ T = $bar; C = 'DarkGray' }
    $rows += @{ T = '  up/down move   SPACE toggle   a all   n none   s safe   r rescan'; C = 'DarkGray' }
    $rows += @{ T = '  d dry-run      u unlock       +/- keep-days   ENTER clean   q quit'; C = 'DarkGray' }
    $rows += @{ T = "  $Msg"; C = 'DarkYellow' }

    @{ Rows = $rows; Top = $Top; View = $view }
}

#==============================================================================
#  3.  SELF-TEST
#     -SelfTest: asserts the safety guards before they are trusted with real data
#==============================================================================


if ($SelfTest) {
    # the script-wide SilentlyContinue swallows `throw` - asserts would pass silently without this
    $ErrorActionPreference = 'Stop'
    $sandbox = Join-Path $env:TEMP ("junktest_" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path "$sandbox\sub" -Force | Out-Null
    [IO.File]::WriteAllBytes("$sandbox\old.bin",   (New-Object byte[] 4096))
    [IO.File]::WriteAllBytes("$sandbox\sub\a.bin", (New-Object byte[] 2048))
    [IO.File]::WriteAllBytes("$sandbox\new.bin",   (New-Object byte[] 1024))
    (Get-Item "$sandbox\old.bin").LastWriteTime = (Get-Date).AddDays(-30)
    (Get-Item "$sandbox\sub").LastWriteTime     = (Get-Date).AddDays(-30)

    $m = Measure-Target $sandbox
    if ($m.Bytes -ne 7168) { throw "measure: expected 7168 bytes, got $($m.Bytes)" }
    if ($m.Files -ne 3)    { throw "measure: expected 3 files, got $($m.Files)" }

    # age filter must spare new.bin AND sub\a.bin - a.bin is fresh even though its
    # parent folder's own timestamp is 30 days old. Deleting it would be the bug that
    # pulls a file out from under a running app.
    Clear-Target -Path $sandbox -MinAgeDays 1
    if (-not (Test-Path "$sandbox\sub\a.bin")) { throw "age filter: deleted a fresh file inside an old folder" }
    if (Test-Path "$sandbox\old.bin")          { throw "age filter: kept a file older than the cutoff" }
    $m = Measure-Target $sandbox
    if ($m.Bytes -ne 3072) { throw "age filter: expected 3072 bytes left, got $($m.Bytes)" }

    # a junction inside a cleaned folder must be skipped, never traversed
    $outside = Join-Path $env:TEMP ("junkoutside_" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $outside -Force | Out-Null
    [IO.File]::WriteAllBytes("$outside\precious.bin", (New-Object byte[] 512))
    (Get-Item "$outside\precious.bin").LastWriteTime = (Get-Date).AddDays(-30)
    New-Item -ItemType Junction -Path "$sandbox\link" -Target $outside -ErrorAction SilentlyContinue | Out-Null
    if (Test-Path "$sandbox\link") {
        Clear-Target -Path $sandbox
        if (-not (Test-Path "$outside\precious.bin")) { throw "junction: deleted through a reparse point" }
    }
    if (Test-Path "$sandbox\link") { [IO.Directory]::Delete("$sandbox\link") }   # reparse point only
    Remove-Item $outside -Recurse -Force -ErrorAction SilentlyContinue

    Clear-Target -Path $sandbox                         # must empty it
    $m = Measure-Target $sandbox
    if ($m.Bytes -ne 0)            { throw "clear: expected 0 bytes, got $($m.Bytes)" }
    if (-not (Test-Path $sandbox)) { throw "clear: folder itself was deleted" }

    $h = @(@{ Bytes = 10 }, @{ Bytes = 32 })
    if ((Get-Sum $h 'Bytes') -ne 42) { throw "Get-Sum on hashtables: got $(Get-Sum $h 'Bytes')" }

    # unlock safety gate - these must never be closeable, whatever holds the file
    foreach ($bad in @(
        @{ n='lsass';    t=1 }, @{ n='svchost';  t=1 }, @{ n='csrss';   t=1 }
        @{ n='winlogon'; t=1 }, @{ n='conhost';  t=1 }, @{ n='Cmd.exe'; t=1 }
        @{ n='chrome';   t=3 },                                    # any service
        @{ n='chrome';   t=1000 })) {                              # RmCritical
        if (Test-Closeable -ProcessId 9999 -Name $bad.n -Type $bad.t) { throw "closeable: $($bad.n)/$($bad.t) must be refused" }
    }
    if (Test-Closeable -ProcessId 4    -Name 'System' -Type 1)              { throw "closeable: pid 4 must be refused" }

    # the shell / terminal / editor that launched us is protected by PID lineage, not by name -
    # a blacklist can never know the user is driving "bash.exe" or "T3 Code"
    if (Test-Closeable -ProcessId 11184 -Name 'bash'    -Type 1 -Ancestors @(5272, 11184))  { throw "closeable: an ancestor pid must be refused" }
    if (Test-Closeable -ProcessId 5248  -Name 'T3 Code' -Type 1 -Ancestors @(5248))         { throw "closeable: launching editor must be refused" }
    if (-not (Test-Closeable -ProcessId 11184 -Name 'bash' -Type 1 -Ancestors @(99, 98)))   { throw "closeable: unrelated bash should stay closeable" }
    if ((Get-CloseVerdict -ProcessId 7 -Name 'x' -Type 3)    -ne 'service')  { throw "verdict: service reason wrong" }
    if ((Get-CloseVerdict -ProcessId 7 -Name 'x' -Type 1000) -ne 'critical') { throw "verdict: critical reason wrong" }
    if ((Get-CloseVerdict -ProcessId 7 -Name 'chrome' -Type 1) -ne '')       { throw "verdict: chrome should be closeable" }

    # typed-fallback selection parser
    if ((Resolve-Selection '1,3-5' 10)  -join ',' -ne '0,2,3,4') { throw "resolve: range wrong -> $((Resolve-Selection '1,3-5' 10) -join ',')" }
    if ((Resolve-Selection ''      3)   -join ',' -ne '0,1,2')   { throw "resolve: ENTER should select all" }
    if ((Resolve-Selection 'a'     3)   -join ',' -ne '0,1,2')   { throw "resolve: 'a' should select all" }
    if (@(Resolve-Selection 'n'    3).Count -ne 0)               { throw "resolve: 'n' should select none" }
    if (@(Resolve-Selection $null  3).Count -ne 0)               { throw "resolve: EOF should select none" }
    if (@(Resolve-Selection '99'   3).Count -ne 0)               { throw "resolve: out-of-range must be dropped" }
    if ((Resolve-Selection '2,2,2' 3)   -join ',' -ne '1')       { throw "resolve: duplicates must collapse" }

    # relaunch capture must read a real path off a live process
    $li = Get-LaunchInfo -ProcessId $PID
    if (-not $li.Path)                       { throw "launch info: no executable path for own pid" }
    if ($li.Path -notmatch 'powershell\.exe$') { throw "launch info: unexpected path $($li.Path)" }
    if (-not (Test-Path -LiteralPath $li.Path)) { throw "launch info: captured path does not exist" }
    if ($li.Args -match 'powershell\.exe')    { throw "launch info: exe not stripped from args -> $($li.Args)" }
    if ($li.Args -notmatch 'SelfTest')       { throw "launch info: args lost -> $($li.Args)" }
    if ((Get-LaunchInfo -ProcessId 999999).Path) { throw "launch info: dead pid should yield no path" }

    # an orphaned elevated relaunch has no parent chain, so the tree must survive
    # being handed over explicitly - otherwise the editor that launched us is fair game
    $script:SelfTree = $null
    $ProtectPid = @(424242, 424243)
    $tree = @(Get-SelfTree)
    if ($tree -notcontains 424242) { throw "self tree: -ProtectPid entries must be protected" }
    if ($tree -notcontains $PID)   { throw "self tree: own pid missing" }
    if (Test-Closeable -ProcessId 424242 -Name 'T3 Code' -Type 1 -Ancestors $tree) { throw "closeable: handed-over pid must be refused" }
    $script:SelfTree = $null
    $ProtectPid = @()

    # settings parser: comments, blanks, '=' inside values, case-insensitive keys
    $cfgFile = Join-Path $env:TEMP ("junkcfg_" + [guid]::NewGuid().ToString('N') + '.txt')
    Set-Content -LiteralPath $cfgFile -Encoding UTF8 -Value @(
        '# a comment', '', '  MinAgeDays = 3  ', 'UNLOCK=true', 'NeverCloseApp = T3 Code, idman',
        'Weird = a=b', 'nokeyline', '# Restart = true')
    $c = Read-Settings $cfgFile
    if ($c['minagedays'] -ne '3')            { throw "settings: trimmed int -> '$($c['minagedays'])'" }
    if ($c['unlock']     -ne 'true')         { throw "settings: key case not normalised" }
    if ($c['weird']      -ne 'a=b')          { throw "settings: value containing '=' truncated -> '$($c['weird'])'" }
    if ($c['nevercloseapp'] -ne 'T3 Code, idman') { throw "settings: list mangled" }
    if ($c.ContainsKey('restart'))           { throw "settings: commented line was read" }
    if ($c.Count -ne 4)                      { throw "settings: expected 4 keys, got $($c.Count)" }
    if (-not (Test-SettingOn 'YES'))         { throw "settings: 'YES' should be on" }
    if (Test-SettingOn 'false')              { throw "settings: 'false' should be off" }
    if (Test-SettingOn $null)                { throw "settings: missing key should be off" }
    if ((Read-Settings (Join-Path $env:TEMP 'no-such-settings-a1b2c3.txt')).Count -ne 0) { throw "settings: missing file should be empty" }
    Remove-Item $cfgFile -Force -ErrorAction SilentlyContinue

    # -NeverCloseApp entries must be honoured, with or without .exe
    if ($NeverClose -notcontains 'lsass') { throw "blacklist: base list lost" }
    foreach ($extra in $NeverCloseApp) {
        $norm = ($extra -replace '\.exe$', '').Trim().ToLower()
        if ($NeverClose -notcontains $norm) { throw "blacklist: -NeverCloseApp '$extra' not applied" }
        if (Test-Closeable -ProcessId 9999 -Name $extra -Type 1) { throw "blacklist: -NeverCloseApp '$extra' still closeable" }
    }

    # 38 lockers must collapse to one readable row per app
    $fakeLock = @()
    1..28 | ForEach-Object { $fakeLock += [pscustomobject]@{ Id = 1000 + $_; Display = 'Google Chrome' } }
    $fakeLock += [pscustomobject]@{ Id = 5248; Display = 'T3 Code (Alpha)' }
    $fakeLock += [pscustomobject]@{ Id = 7408; Display = 'Microsoft Edge' }
    $fakeLock += [pscustomobject]@{ Id = 15244; Display = 'Microsoft Edge' }
    $lines = @(Format-LockerList $fakeLock)
    if ($lines.Count -ne 3)                        { throw "locker list: expected 3 rows, got $($lines.Count)" }
    if ($lines[0] -notmatch '^Google Chrome\s+28 processes') { throw "locker list: busiest app not first -> $($lines[0])" }
    if ($lines[0] -notmatch '\+25 more')           { throw "locker list: pid run-on not truncated -> $($lines[0])" }
    if ($lines[0].Length -gt 80)                   { throw "locker list: row too wide ($($lines[0].Length) chars)" }
    if ($lines -join '' -notmatch 'T3 Code \(Alpha\)\s+1 process') { throw "locker list: singular label wrong" }
    if (Test-Closeable -ProcessId 1234 -Name 'x' -Type 1 -SelfPid 1234)     { throw "closeable: own pid must be refused" }
    if (Test-Closeable -ProcessId 9999 -Name ''  -Type 1)                   { throw "closeable: blank name must be refused" }
    if (-not (Test-Closeable -ProcessId 9999 -Name 'chrome'      -Type 1))  { throw "closeable: chrome should be closeable" }
    if (-not (Test-Closeable -ProcessId 9999 -Name 'Notepad.exe' -Type 2))  { throw "closeable: notepad should be closeable" }

    $ix = Expand-IndexList '1,3,5-8'
    if (($ix -join ',') -ne '1,3,5,6,7,8') { throw "index parse: got $($ix -join ',')" }

    # row text is shared by the TUI and the plain fallback - keep the three states honest
    $row = @{ N='Demo'; Bytes=2048; Sel=$true }
    if ((Get-ItemLine -T $row -Num 1 -Admin $true) -notmatch '^\s+\[x\] Demo\s+2\.00 KB') { throw "row(selected): $(Get-ItemLine -T $row -Num 1 -Admin $true)" }
    $row.Sel = $false
    if ((Get-ItemLine -T $row -Num 1 -Admin $true) -notmatch '\[ \] Demo')               { throw "row(unselected) wrong" }
    $row.Admin = $true
    if ((Get-ItemLine -T $row -Num 1 -Admin $false) -notmatch '\[-\] Demo.*needs admin') { throw "row(locked) wrong" }
    if ((Get-ItemLine -T $row -Num 7 -Admin $false -Numbered) -notmatch '^\s+7\. \[-\]') { throw "row(numbered) wrong" }

    # TUI frame: pure function, so the layout/scroll maths is checkable without a console
    $fake = @(0..29 | ForEach-Object {
        @{ N = "Item$_"; G = $(if ($_ -lt 10) { 'System' } else { 'User' }); Bytes = 1024; FileCount = 1; Sel = ($_ -eq 3) } })
    $f = Get-Frame -Targets $fake -Admin $true -Cur 0 -Top 0 -H 30 -W 100 -MinAge 0 -DryRun $false -Unlock $false -FreeText '1 GB' -Msg ''
    if ($f.View -ne 22)         { throw "frame view: expected 22 rows, got $($f.View)" }
    if ($f.Rows.Count -ne 30)   { throw "frame height: expected 30 lines, got $($f.Rows.Count)" }
    if ($f.Top -ne 0)           { throw "frame top: expected 0, got $($f.Top)" }
    if (@($f.Rows | Where-Object { $_.T -match '^>' }).Count -ne 1) { throw "frame: cursor row not unique" }
    if ($f.Rows[4].T -notmatch 'SYSTEM')  { throw "frame: missing group header" }
    if ($f.Rows[5].T -notmatch '^>\s*1\. \[ \] Item0') { throw "frame: cursor not on item 0 -> $($f.Rows[5].T)" }
    if ($f.Rows[3].T -notmatch '1/30 selected') { throw "frame: bad status line -> $($f.Rows[3].T)" }

    $f = Get-Frame -Targets $fake -Admin $true -Cur 29 -Top 0 -H 30 -W 100 -MinAge 0 -DryRun $false -Unlock $false -FreeText '1 GB' -Msg ''
    if ($f.Top -eq 0)         { throw "frame: viewport did not scroll to the last item" }
    if ($f.Rows.Count -ne 30) { throw "frame: scrolled height changed to $($f.Rows.Count)" }
    if (@($f.Rows | Where-Object { $_.T -match '^>\s*30\. \[.\] Item29\b' }).Count -ne 1) { throw "frame: last item not focused" }

    $f = Get-Frame -Targets $fake -Admin $true -Cur 0 -Top 0 -H 12 -W 100 -MinAge 3 -DryRun $true -Unlock $false -FreeText '1 GB' -Msg ''
    if ($f.Rows.Count -ne 12) { throw "frame: short window gave $($f.Rows.Count) lines" }
    if ($f.Rows[3].T -notmatch 'keep>3d   dry-run ON   unlock off') { throw "frame: flags not shown -> $($f.Rows[3].T)" }
    $f = Get-Frame -Targets $fake -Admin $true -Cur 0 -Top 0 -H 12 -W 100 -MinAge 0 -DryRun $false -Unlock $true -FreeText '1 GB' -Msg ''
    if ($f.Rows[3].T -notmatch 'unlock ON') { throw "frame: unlock flag not shown -> $($f.Rows[3].T)" }

    Remove-Item $sandbox -Recurse -Force
    Write-Host "SelfTest OK" -ForegroundColor Green
    return
}

#==============================================================================
#  4.  TARGETS
#     the cleanup list - paths, groups, admin needs, hazard notes
#==============================================================================

# On   : pre-ticked in the menu / used when no -Include* switch is given.
# Admin: needs elevation. Sw: the -Include* switch that turns it on unattended.

$LA = $env:LOCALAPPDATA; $AD = $env:APPDATA; $PD = $env:ProgramData; $W = $env:windir
$SD = $env:SystemDrive

$targets = @(
  @{ N='Windows Temp';            G='System';   On=$true;  Admin=$true;  P=@("$W\Temp") }
  @{ N='System drive \Temp';      G='System';   On=$true;  Admin=$true;  P=@("$SD\Temp") }
  @{ N='Windows logs';            G='System';   On=$true;  Admin=$true;  P=@("$W\Logs") }
  @{ N='Setup / driver logs';     G='System';   On=$true;  Admin=$true;  P=@("$W\Panther\UnattendGC","$W\inf\setupapi*.log") }
  @{ N='Crash dumps (system)';    G='System';   On=$true;  Admin=$true;  P=@("$W\Minidump","$W\MEMORY.DMP","$W\LiveKernelReports") }
  @{ N='Error reporting (WER)';   G='System';   On=$true;  Admin=$true;  P=@("$PD\Microsoft\Windows\WER\ReportQueue",
                                                                              "$PD\Microsoft\Windows\WER\ReportArchive",
                                                                              "$PD\Microsoft\Windows\WER\Temp") }
  @{ N='Defender scan history';   G='System';   On=$true;  Admin=$true;  P=@("$PD\Microsoft\Windows Defender\Scans\History\Results") }

  @{ N='User Temp';               G='User';     On=$true;  P=@("$LA\Temp") }
  @{ N='INetCache (WinINet)';     G='User';     On=$true;  P=@("$LA\Microsoft\Windows\INetCache") }
  @{ N='Crash dumps (user)';      G='User';     On=$true;  P=@("$LA\CrashDumps") }
  @{ N='GPU / shader caches';     G='User';     On=$true;  P=@("$LA\D3DSCache","$LA\NVIDIA\DXCache","$LA\NVIDIA\GLCache",
                                                                "$LA\AMD\DxCache","$LA\AMD\GLCache","$LA\Intel\ShaderCache")
     Note='rebuilt on next launch, first frames slower' }
  @{ N='Recent / jump lists';     G='User';     On=$true;  P=@("$AD\Microsoft\Windows\Recent") }
  @{ N='Delivery Opt. (user)';    G='User';     On=$true;  P=@("$LA\Microsoft\Windows\DeliveryOptimization") }
  @{ N='Recycle Bin';             G='User';     On=$true;  P=@("$SD\`$Recycle.Bin")
     Clear={ Clear-RecycleBin -Force -Confirm:$false }; Note='unrecoverable' }

  @{ N='Chromium browser caches'; G='Advanced'; On=$false; Sw='IncludeBrowserCache'
     Note='close Edge/Chrome/Brave first'
     P=@("$LA\Microsoft\Edge\User Data\*\Cache"
         "$LA\Microsoft\Edge\User Data\*\Code Cache"
         "$LA\Microsoft\Edge\User Data\*\GPUCache"
         "$LA\Microsoft\Edge\User Data\*\Service Worker\CacheStorage"
         "$LA\Google\Chrome\User Data\*\Cache"
         "$LA\Google\Chrome\User Data\*\Code Cache"
         "$LA\Google\Chrome\User Data\*\GPUCache"
         "$LA\Google\Chrome\User Data\*\Service Worker\CacheStorage"
         "$LA\BraveSoftware\Brave-Browser\User Data\*\Cache"
         "$LA\BraveSoftware\Brave-Browser\User Data\*\Code Cache") }
  @{ N='Firefox cache';           G='Advanced'; On=$false; Sw='IncludeBrowserCache'; Note='close Firefox first'
     P=@("$LA\Mozilla\Firefox\Profiles\*\cache2","$LA\Mozilla\Firefox\Profiles\*\startupCache") }
  @{ N='Thumbnail / icon cache';  G='Advanced'; On=$false; Sw='IncludeThumbnails'; Note='restarts Explorer'
     P=@("$LA\Microsoft\Windows\Explorer")
     Pre={ Stop-Process -Name explorer -Force }
     Post={ if (-not (Get-Process explorer)) { Start-Process explorer } } }
  @{ N='Prefetch';                G='Advanced'; On=$false; Admin=$true; Sw='IncludePrefetch'
     Note='next few boots are slower'; P=@("$W\Prefetch") }
  @{ N='Windows Update cache';    G='Advanced'; On=$false; Admin=$true; Sw='IncludeWindowsUpdate'
     Note='stops wuauserv/bits/cryptsvc, restarts them'
     P=@("$W\SoftwareDistribution\Download","$PD\Microsoft\Network\Downloader")
     Pre={ 'wuauserv','bits','cryptsvc' | ForEach-Object { Stop-Service $_ -Force } }
     Post={ 'cryptsvc','bits','wuauserv' | ForEach-Object { Start-Service $_ } } }
  @{ N='Delivery Optimization';   G='Advanced'; On=$false; Admin=$true; Sw='IncludeWindowsUpdate'
     Note='stops dosvc, restarts it'
     P=@("$W\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization")
     Pre={ Stop-Service dosvc -Force }; Post={ Start-Service dosvc } }
  @{ N='Upgrade leftovers';       G='Advanced'; On=$false; Admin=$true; Sw='IncludeWindowsOld'
     Note='removes the 10-day rollback to your old build'
     P=@("$SD\Windows.old","$SD\`$WINDOWS.~BT","$SD\`$WINDOWS.~WS","$SD\`$SysReset") }
  @{ N='Component store (DISM)';  G='Advanced'; On=$false; Admin=$true; Sw='IncludeComponentStore'
     Note='slow; superseded updates can no longer be uninstalled'; P=@(); NoMeasure=$true
     Clear={ & dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase | Out-Null } }
)

#==============================================================================
#  5.  ELEVATION
#     offer to relaunch as admin; carry on unelevated if declined
#==============================================================================


$admin = Test-Admin

if ($Interactive -and -not $admin) {
    Write-Host ""
    Write-Host "  Not running as administrator - system caches (Windows Temp, WU, WER...) can't be touched." -ForegroundColor Yellow
    $r = Read-Line "  Relaunch elevated? [Y/n]"
    if ($r -eq '' -or $r -match '^[Yy]') {
        $a = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$PSCommandPath`"")
        if ($MinAgeDays) { $a += @('-MinAgeDays', $MinAgeDays) }
        if ($DryRun)     { $a += '-DryRun' }
        # the elevated copy is orphaned the moment this one returns, so its parent walk
        # would dead-end immediately - hand it our tree so it still knows what not to close
        $tree = @(Get-SelfTree)
        if ($tree) { $a += @('-ProtectPid', ($tree -join ',')) }
        try {
            Start-Process powershell -Verb RunAs -ArgumentList $a -ErrorAction Stop
            return
        } catch {
            # UAC declined or blocked by policy - carry on unelevated rather than vanishing
            Write-Host "  Elevation declined - continuing with user targets only." -ForegroundColor DarkYellow
        }
    }
}

#==============================================================================
#  6.  SCAN
#     measure every target up front so the menu can show real sizes
#==============================================================================


$SD_free_before = Get-FreeSpace $SD

Write-Host ""
Write-Host "  Windows Junk Cleaner" -ForegroundColor Cyan
Write-Host "  ------------------------------------------------------------------------"
Write-Host ("  Host            : {0}  ({1})" -f $env:COMPUTERNAME, (Get-CimInstance Win32_OperatingSystem).Caption)
Write-Host ("  Elevated        : {0}" -f $(if ($admin) { 'yes' } else { 'no - system targets unavailable' }))
Write-Host ("  Keep newer than : {0} day(s)" -f $MinAgeDays)
Write-Host ("  Free on {0}      : {1}" -f $SD, (Format-Size $SD_free_before))
Write-Host ""

function Update-Scan {
    param($Targets, [bool]$Admin)
    $i = 0
    foreach ($t in $Targets) {
        $i++
        Write-Progress -Activity 'Scanning' -Status $t.N -PercentComplete ([int]($i / $Targets.Count * 100))
        if (($t.Admin -and -not $Admin) -or $t.NoMeasure) {
            $t.Bytes = 0; $t.FileCount = 0; $t.Paths = @(); continue
        }
        $t.Paths = @(Resolve-Targets $t.P)
        $b = [int64]0; $f = 0
        foreach ($p in $t.Paths) { $m = Measure-Target $p; $b += $m.Bytes; $f += $m.Files }
        $t.Bytes = $b; $t.FileCount = $f
    }
    Write-Progress -Activity 'Scanning' -Completed
}

Write-Host "  Scanning..." -ForegroundColor DarkGray
Update-Scan $targets $admin

#==============================================================================
#  7.  SELECTION
#     default tick state for interactive vs unattended runs
#==============================================================================


# default tick state
foreach ($t in $targets) {
    if ($Interactive) {
        $t.Sel = [bool]$t.On -and -not ($t.Admin -and -not $admin) -and $t.Bytes -gt 0
    } else {
        $on = [bool]$t.On
        if ($t.Sw -and (Get-Variable $t.Sw -ValueOnly)) { $on = $true }
        $t.Sel = $on -and -not ($t.Admin -and -not $admin)
    }
}

#==============================================================================
#  8.  USER INTERFACE
#     full-screen TUI, plus the plain numbered menu for redirected stdin
#==============================================================================

# Full-screen, arrow-key driven. Plain [Console] calls - no modules.

function Invoke-TuiMenu {
    param($Targets, [bool]$Admin)

    $cur = 0; $top = 0; $msg = ''
    $lastW = 0; $lastH = 0
    [Console]::CursorVisible = $false
    Clear-Host

    try {
        while ($true) {
            $h = [Console]::WindowHeight; $w = [Console]::WindowWidth
            if ($w -ne $lastW -or $h -ne $lastH) { Clear-Host; $lastW = $w; $lastH = $h }

            $f = Get-Frame -Targets $Targets -Admin $Admin -Cur $cur -Top $top -H $h -W $w `
                           -MinAge $MinAgeDays -DryRun $DryRunOn -Unlock $UnlockOn -Msg $msg `
                           -FreeText (Format-Size (Get-FreeSpace $SD)).Trim()
            $top  = $f.Top
            $view = $f.View
            for ($y = 0; $y -lt $f.Rows.Count; $y++) {
                Write-Row $y $f.Rows[$y].T $f.Rows[$y].C $f.Rows[$y].B
            }

            $k   = [Console]::ReadKey($true)
            $msg = ''
            $ch  = [string]$k.KeyChar
            switch -Regex ($k.Key.ToString()) {
                '^UpArrow$'   { if ($cur -gt 0) { $cur-- }; break }
                '^DownArrow$' { if ($cur -lt $Targets.Count - 1) { $cur++ }; break }
                '^Home$'      { $cur = 0; break }
                '^End$'       { $cur = $Targets.Count - 1; break }
                '^PageUp$'    { $cur = [Math]::Max(0, $cur - $view); break }
                '^PageDown$'  { $cur = [Math]::Min($Targets.Count - 1, $cur + $view); break }
                '^Spacebar$'  {
                    $t = $Targets[$cur]
                    if ($t.Admin -and -not $Admin) { $msg = 'needs an elevated session' }
                    else { $t.Sel = -not $t.Sel }
                    break
                }
                '^Enter$'     { return $true }
                '^Escape$'    { return $false }
                default {
                    switch -Regex ($ch) {
                        '^[kK]$'  { if ($cur -gt 0) { $cur-- } }
                        '^[jJ]$'  { if ($cur -lt $Targets.Count - 1) { $cur++ } }
                        '^[qQ]$'  { return $false }
                        '^[aA]$'  { foreach ($t in $Targets) { $t.Sel = -not ($t.Admin -and -not $Admin) } }
                        '^[nN]$'  { foreach ($t in $Targets) { $t.Sel = $false } }
                        '^[sS]$'  { foreach ($t in $Targets) { $t.Sel = [bool]$t.On -and -not ($t.Admin -and -not $Admin) -and $t.Bytes -gt 0 } }
                        '^[dD]$'  { $script:DryRunOn = -not $script:DryRunOn }
                        '^[uU]$'  { $script:UnlockOn = -not $script:UnlockOn }
                        '^[rR]$'  { Write-Row ($h - 1) '  rescanning...' 'Cyan'; Update-Scan $Targets $Admin; Clear-Host; $lastH = 0 }
                        '^[\+=]$' { $script:MinAgeDays++ }
                        '^[\-_]$' { if ($script:MinAgeDays -gt 0) { $script:MinAgeDays-- } }
                        default   { $msg = 'keys: arrows, space, a n s r d u +/- enter q' }
                    }
                }
            }
        }
    }
    finally {
        [Console]::CursorVisible = $true
        Clear-Host
    }
}

# Fallback for redirected stdin / hosts without raw key input.
function Invoke-PlainMenu {
    param($Targets, [bool]$Admin)
    while ($true) {
        Write-Host ""
        $group = ''
        for ($i = 0; $i -lt $Targets.Count; $i++) {
            $t = $Targets[$i]
            if ($t.G -ne $group) { $group = $t.G; Write-Host ("  {0}" -f $group.ToUpper()) -ForegroundColor Cyan }
            Write-Host (Get-ItemLine -T $t -Num ($i + 1) -Admin $Admin -Numbered) -ForegroundColor (Get-ItemColor $t $Admin)
        }
        $sel = @($Targets | Where-Object { $_.Sel })
        Write-Host ("  Selected: {0}   {1}   {2} files   keep>{3}d   dry-run: {4}   unlock: {5}" -f `
            $sel.Count, (Format-Size (Get-Sum $sel 'Bytes')).Trim(), (Get-Sum $sel 'FileCount'),
            $MinAgeDays, $(if ($DryRunOn) { 'ON' } else { 'off' }),
            $(if ($UnlockOn) { 'ON' } else { 'off' })) -ForegroundColor Green
        Write-Host "  1,3,5-8 toggle  a all  n none  s safe  r rescan  d dry-run  u unlock  k <days>  ENTER clean  q quit" -ForegroundColor DarkGray

        $in = Read-Line "  >"
        if ($null -eq $in) { return $false }          # EOF - quit instead of looping
        if ($in -eq '')              { return $true }
        elseif ($in -match '^[qQ]$') { return $false }
        elseif ($in -match '^[aA]$') { foreach ($t in $Targets) { $t.Sel = -not ($t.Admin -and -not $Admin) } }
        elseif ($in -match '^[nN]$') { foreach ($t in $Targets) { $t.Sel = $false } }
        elseif ($in -match '^[sS]$') { foreach ($t in $Targets) { $t.Sel = [bool]$t.On -and -not ($t.Admin -and -not $Admin) -and $t.Bytes -gt 0 } }
        elseif ($in -match '^[rR]$') { Update-Scan $Targets $Admin }
        elseif ($in -match '^[dD]$') { $script:DryRunOn = -not $script:DryRunOn }
        elseif ($in -match '^[uU]$') { $script:UnlockOn = -not $script:UnlockOn }
        elseif ($in -match '^[kK]\s*(\d+)$') { $script:MinAgeDays = [int]$Matches[1] }
        else {
            foreach ($n in Expand-IndexList $in) {
                if ($n -ge 1 -and $n -le $Targets.Count) {
                    $t = $Targets[$n - 1]
                    if (-not ($t.Admin -and -not $Admin)) { $t.Sel = -not $t.Sel }
                }
            }
        }
    }
}

#==============================================================================
#  9.  CLEAN AND REPORT
#     delete, optionally unlock and retry, then the freed/pending summary
#==============================================================================


function Invoke-Clean {
    param($Targets)

    $run        = @($Targets | Where-Object { $_.Sel })
    $freeBefore = Get-FreeSpace $SD
    $results    = @()
    $i          = 0

    Write-Host ""
    Write-Host ("  {0}" -f $(if ($DryRunOn) { 'DRY RUN - nothing will be deleted' } else { 'Cleaning' })) -ForegroundColor Cyan
    Write-Host "  ------------------------------------------------------------------------"

    foreach ($t in $run) {
        $i++
        $pct = [int]($i / $run.Count * 100)

        if ($DryRunOn) {
            $results += [pscustomobject]@{ Location=$t.N; Before=$t.Bytes; Freed=0; Pending=$t.Bytes; Files=$t.FileCount; Status='would clean' }
            Write-Host ("  [{0,3}%] {1,-28} would free {2}  ({3} files)" -f $pct, $t.N, (Format-Size $t.Bytes), $t.FileCount)
            continue
        }

        Write-Progress -Activity 'Cleaning' -Status ("{0}  ({1})" -f $t.N, (Format-Size $t.Bytes)) -PercentComplete $pct
        if ($t.Pre) { & $t.Pre }
        if ($t.Clear) { & $t.Clear } else { foreach ($p in $t.Paths) { Clear-Target -Path $p -MinAgeDays $MinAgeDays } }
        if ($t.Post) { & $t.Post }

        if ($t.NoMeasure) {
            $results += [pscustomobject]@{ Location=$t.N; Before=0; Freed=0; Pending=0; Files=0; Status='ran (see drive delta)' }
            Write-Host ("  [{0,3}%] {1,-28} ran - savings show in drive delta" -f $pct, $t.N) -ForegroundColor Green
            continue
        }

        $after = [int64]0
        foreach ($p in $t.Paths) { $after += (Measure-Target $p).Bytes }

        $unlocked = @()
        if ($UnlockOn -and $after -gt 0) {
            $unlocked = @(Invoke-Unlock -T $t -Ask $script:UnlockAsk)
            if ($unlocked) {
                foreach ($p in $t.Paths) { Clear-Target -Path $p -MinAgeDays $MinAgeDays }
                $after = [int64]0
                foreach ($p in $t.Paths) { $after += (Measure-Target $p).Bytes }
            }
        }
        $freed = $t.Bytes - $after

        $status = if ($after -eq 0 -and $unlocked) {
                      if ($unlocked.Count -le 2) { "clean (closed $($unlocked -join ', '))" }
                      else { "clean (closed $($unlocked[0]) +$($unlocked.Count - 1) more)" }
                  }
                  elseif ($after -eq 0)  { 'clean' }
                  elseif ($freed -le 0)  { 'locked / in use' }
                  else                   { 'partial (files in use)' }
        $color  = switch -Regex ($status) { '^clean' { 'Green' } 'locked / in use' { 'DarkYellow' } default { 'Yellow' } }

        $results += [pscustomobject]@{ Location=$t.N; Before=$t.Bytes; Freed=$freed; Pending=$after; Files=$t.FileCount; Status=$status }
        Write-Host ("  [{0,3}%] {1,-28} freed {2}  pending {3}  {4}" -f `
            $pct, $t.N, (Format-Size $freed), (Format-Size $after), $status) -ForegroundColor $color
    }
    Write-Progress -Activity 'Cleaning' -Completed

    # ---- put back whatever -Unlock closed -------------------------------------
    if (-not $DryRunOn -and $script:ClosedApps.Count) {
        $pick = if ($RestartAll)  { $script:ClosedApps }
                elseif ($UnlockAsk) { Select-RestartApp $script:ClosedApps }
                else { @() }
        if ($pick) { Restart-ClosedApp -Apps $pick -Elevated $admin }
        $skipped = @($script:ClosedApps | Where-Object { $pick -notcontains $_ })
        if ($skipped) {
            Write-Host ("  left closed: {0}" -f (($skipped | ForEach-Object { $_.Name }) -join ', ')) -ForegroundColor DarkGray
        }
        $script:ClosedApps = @()
    }

    # ------------------------------------------------------------ report ----

    $freeAfter = Get-FreeSpace $SD
    $delta     = $freeAfter - $freeBefore
    $sumBefore = ($results | Measure-Object Before  -Sum).Sum
    $sumFreed  = ($results | Measure-Object Freed   -Sum).Sum
    $sumPend   = ($results | Measure-Object Pending -Sum).Sum
    $sumFiles  = ($results | Measure-Object Files   -Sum).Sum

    Write-Host ""
    Write-Host "  Summary" -ForegroundColor Cyan
    Write-Host "  ------------------------------------------------------------------------"

    $results | Sort-Object Before -Descending |
        Format-Table @{ L='Location'; E={ $_.Location }; Width=30 },
                     @{ L='Found';    E={ Format-Size $_.Before  }; Align='Right' },
                     @{ L='Freed';    E={ Format-Size $_.Freed   }; Align='Right' },
                     @{ L='Pending';  E={ Format-Size $_.Pending }; Align='Right' },
                     @{ L='Files';    E={ $_.Files }; Align='Right' },
                     @{ L='Status';   E={ $_.Status } } |
        Out-String | Write-Host

    Write-Host ("  Scanned           : {0} across {1} files" -f (Format-Size $sumBefore), $sumFiles)
    Write-Host ("  Reclaimed         : {0}" -f (Format-Size $sumFreed))  -ForegroundColor Green
    Write-Host ("  Still pending     : {0}   (locked, in use, or newer than {1}d)" -f (Format-Size $sumPend), $MinAgeDays) -ForegroundColor Yellow
    if ($sumBefore -gt 0) {
        Write-Host ("  Completion        : {0:N1}% of found junk removed" -f ($sumFreed / $sumBefore * 100))
    }
    Write-Host ("  Free on {0} before : {1}" -f $SD, (Format-Size $freeBefore))
    Write-Host ("  Free on {0} after  : {1}" -f $SD, (Format-Size $freeAfter))
    Write-Host ("  Drive delta       : {0}" -f (Format-Size $delta)) -ForegroundColor Cyan

    $untouched = @($Targets | Where-Object { -not $_.Sel -and $_.Bytes -gt 0 })
    if ($untouched) {
        Write-Host ("  Left on disk      : {0} in {1} unselected location(s)" -f `
            (Format-Size (Get-Sum $untouched 'Bytes')), $untouched.Count) -ForegroundColor DarkGray
    }
    Write-Host ""
}

#==============================================================================
#  10.  MAIN LOOP
#     menu -> clean -> back to the menu; countdown exit after a real run
#==============================================================================

# Interactive runs come back to the menu so a dry run can be repeated for real.
# Redirected stdin gets a single pass - Read-Host at EOF would spin forever.

$loops = $Interactive -and -not [Console]::IsInputRedirected

while ($true) {
    if ($Interactive) {
        $useTui = -not [Console]::IsInputRedirected -and $Host.Name -ne 'Windows PowerShell ISE Host'
        $go = if ($useTui) { Invoke-TuiMenu $targets $admin } else { Invoke-PlainMenu $targets $admin }
        if (-not $go) { Write-Host "  Cancelled." -ForegroundColor DarkGray; return }

        $sel = @($targets | Where-Object { $_.Sel })
        if (-not $sel) {
            Write-Host "  Nothing selected." -ForegroundColor DarkGray
            if (-not $loops) { return }
            Wait-AnyKey 'Press any key to return to the menu...'
            continue
        }

        if (-not $DryRunOn) {
            $risky = @($sel | Where-Object { $_.G -eq 'Advanced' } | ForEach-Object { $_.N })
            Write-Host ""
            Write-Host ("  About to delete {0} from {1} location(s)." -f `
                (Format-Size (Get-Sum $sel 'Bytes')).Trim(), $sel.Count) -ForegroundColor Yellow
            if ($risky) { Write-Host ("  Includes: {0}" -f ($risky -join ', ')) -ForegroundColor DarkYellow }
            $ok = Read-Line "  Type Y to proceed"
            if ($ok -notmatch '^[Yy]') {
                Write-Host "  Cancelled." -ForegroundColor DarkGray
                if (-not $loops) { return }
                continue
            }
        }
    }

    $wasDryRun = $DryRunOn
    Invoke-Clean $targets
    if (-not $loops) { break }

    if ($wasDryRun) {
        # nothing changed on disk, so no rescan needed - go straight back so it can be run for real
        Wait-AnyKey 'Dry run complete - press any key to return to the menu...'
        continue
    }

    if (-not (Wait-Countdown 5)) { break }
    Update-Scan $targets $admin     # sizes moved, refresh before showing the menu again
}
