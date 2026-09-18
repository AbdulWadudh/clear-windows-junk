// Clear-WindowsJunk - target list, scan/clean driver, unlock and restart.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;

namespace ClearWindowsJunk
{
    public class Progress
    {
        public string Stage = "";
        public string Item = "";
        public int Percent;
        public long Freed, Pending;
    }

    // The GUI supplies these; the engine never prompts on its own.
    public interface IPrompter
    {
        // which apps to close, out of the ones holding files. Empty = close nothing.
        List<LockerInfo> ChooseAppsToClose(List<LockerInfo> closeable, List<LockerInfo> refused);
        // which stubborn apps to force after they ignored the polite request
        List<LockerInfo> ChooseAppsToForce(List<LockerInfo> stubborn);
        void Report(string line);
    }

    public class Engine
    {
        public Settings Cfg = new Settings();
        public List<Target> Targets = new List<Target>();
        public List<ClosedApp> Closed = new List<ClosedApp>();
        public string SystemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

        public Engine() { Targets = BuildTargets(); }

        // Order the list so actionable rows come first. A screen that opens on ten
        // greyed-out 'needs administrator' rows reads as broken.
        public void OrderForUser(bool isAdmin)
        {
            Targets = Targets
                .OrderBy(t => t.Group == Grp.User ? 0
                            : t.Group == Grp.System ? (isAdmin ? 1 : 3) : 2)
                .ToList();
        }

        static string Env(string v) { return Environment.ExpandEnvironmentVariables(v); }

        public static List<Target> BuildTargets()
        {
            string LA = Env("%LOCALAPPDATA%"), AD = Env("%APPDATA%"),
                   PD = Env("%ProgramData%"), W = Env("%windir%"), SD = Env("%SystemDrive%");

            return new List<Target>
            {
                new Target { Name="Windows Temp",        Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ W+@"\Temp" } },
                new Target { Name=@"System drive \Temp", Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ SD+@"\Temp" } },
                new Target { Name="Windows logs",        Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ W+@"\Logs" } },
                new Target { Name="Setup / driver logs", Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ W+@"\Panther\UnattendGC" } },
                new Target { Name="Crash dumps (system)",Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ W+@"\Minidump", W+@"\MEMORY.DMP", W+@"\LiveKernelReports" } },
                new Target { Name="Error reporting (WER)",Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ PD+@"\Microsoft\Windows\WER\ReportQueue",
                                             PD+@"\Microsoft\Windows\WER\ReportArchive",
                                             PD+@"\Microsoft\Windows\WER\Temp" } },
                new Target { Name="Defender scan history",Group=Grp.System, OnByDefault=true, NeedsAdmin=true,
                             Patterns=new[]{ PD+@"\Microsoft\Windows Defender\Scans\History\Results" } },

                new Target { Name="User Temp",           Group=Grp.User, OnByDefault=true,
                             Patterns=new[]{ LA+@"\Temp" } },
                new Target { Name="INetCache (WinINet)", Group=Grp.User, OnByDefault=true,
                             Patterns=new[]{ LA+@"\Microsoft\Windows\INetCache" } },
                new Target { Name="Crash dumps (user)",  Group=Grp.User, OnByDefault=true,
                             Patterns=new[]{ LA+@"\CrashDumps" } },
                new Target { Name="GPU / shader caches", Group=Grp.User, OnByDefault=true,
                             Note="rebuilt on next launch, first frames slower",
                             Patterns=new[]{ LA+@"\D3DSCache", LA+@"\NVIDIA\DXCache", LA+@"\NVIDIA\GLCache",
                                             LA+@"\AMD\DxCache", LA+@"\AMD\GLCache", LA+@"\Intel\ShaderCache" } },
                new Target { Name="Recent / jump lists", Group=Grp.User, OnByDefault=true,
                             Patterns=new[]{ AD+@"\Microsoft\Windows\Recent" } },
                new Target { Name="Delivery Opt. (user)",Group=Grp.User, OnByDefault=true,
                             Patterns=new[]{ LA+@"\Microsoft\Windows\DeliveryOptimization" } },

                new Target { Name="Chromium browser caches", Group=Grp.Advanced,
                             Note="close Edge / Chrome / Brave first",
                             Patterns=new[]{
                                LA+@"\Microsoft\Edge\User Data\*\Cache",
                                LA+@"\Microsoft\Edge\User Data\*\Code Cache",
                                LA+@"\Microsoft\Edge\User Data\*\GPUCache",
                                LA+@"\Google\Chrome\User Data\*\Cache",
                                LA+@"\Google\Chrome\User Data\*\Code Cache",
                                LA+@"\Google\Chrome\User Data\*\GPUCache",
                                LA+@"\BraveSoftware\Brave-Browser\User Data\*\Cache",
                                LA+@"\BraveSoftware\Brave-Browser\User Data\*\Code Cache" } },
                new Target { Name="Firefox cache",       Group=Grp.Advanced, Note="close Firefox first",
                             Patterns=new[]{ LA+@"\Mozilla\Firefox\Profiles\*\cache2",
                                             LA+@"\Mozilla\Firefox\Profiles\*\startupCache" } },
                new Target { Name="Thumbnail / icon cache", Group=Grp.Advanced, Note="restarts Explorer",
                             Patterns=new[]{ LA+@"\Microsoft\Windows\Explorer" },
                             Pre=() => { foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } } },
                             Post=() => { if (Process.GetProcessesByName("explorer").Length == 0) { try { Process.Start("explorer"); } catch { } } } },
                new Target { Name="Prefetch",            Group=Grp.Advanced, NeedsAdmin=true,
                             Note="next few boots are slower", Patterns=new[]{ W+@"\Prefetch" } },
                new Target { Name="Windows Update cache",Group=Grp.Advanced, NeedsAdmin=true,
                             Note="stops wuauserv / bits / cryptsvc, restarts them",
                             Patterns=new[]{ W+@"\SoftwareDistribution\Download", PD+@"\Microsoft\Network\Downloader" },
                             Pre=() => Svc(new[]{"wuauserv","bits","cryptsvc"}, false),
                             Post=() => Svc(new[]{"cryptsvc","bits","wuauserv"}, true) },
                new Target { Name="Delivery Optimization",Group=Grp.Advanced, NeedsAdmin=true,
                             Note="stops dosvc, restarts it",
                             Patterns=new[]{ W+@"\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization" },
                             Pre=() => Svc(new[]{"dosvc"}, false), Post=() => Svc(new[]{"dosvc"}, true) },
                new Target { Name="Upgrade leftovers",   Group=Grp.Advanced, NeedsAdmin=true,
                             Note="removes the 10-day rollback to your old build",
                             Patterns=new[]{ SD+@"\Windows.old", SD+@"\$WINDOWS.~BT", SD+@"\$WINDOWS.~WS" } },
                new Target { Name="Component store (DISM)", Group=Grp.Advanced, NeedsAdmin=true, NoMeasure=true,
                             Note="slow; superseded updates can no longer be uninstalled",
                             CustomClear=() => Run("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /ResetBase") },
            };
        }

        static void Svc(string[] names, bool start)
        {
            foreach (var n in names)
                try
                {
                    var sc = new System.ServiceProcess.ServiceController(n);
                    if (start)
                    {
                        if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                        { sc.Start(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(20)); }
                    }
                    else if (sc.CanStop)
                    { sc.Stop(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
                }
                catch { }
        }

        static void Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit();
            }
            catch { }
        }

        // ------------------------------------------------------------- scan ---

        public void Scan(bool isAdmin, Action<Progress> report, CancellationToken ct)
        {
            int i = 0;
            foreach (var t in Targets)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                if (report != null)
                    report(new Progress { Stage = "Scanning", Item = t.Name, Percent = i * 100 / Targets.Count });

                t.Paths.Clear(); t.Bytes = 0; t.Files = 0;
                if ((t.NeedsAdmin && !isAdmin) || t.NoMeasure) continue;

                foreach (var pat in t.Patterns) t.Paths.AddRange(Disk.Resolve(pat));
                foreach (var p in t.Paths)
                {
                    long b; int f;
                    Disk.Measure(p, out b, out f, ct);
                    t.Bytes += b; t.Files += f;
                }
            }
        }

        // ------------------------------------------------------------ clean ---

        public long CleanFreeBefore, CleanFreeAfter;

        public void Clean(bool isAdmin, IPrompter ui, Action<Progress> report, CancellationToken ct)
        {
            var run = Targets.Where(t => t.Selected).ToList();
            CleanFreeBefore = Disk.FreeSpace(SystemDrive);
            Closed.Clear();

            int i = 0;
            foreach (var t in run)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                int pct = i * 100 / Math.Max(1, run.Count);
                if (report != null) report(new Progress { Stage = Cfg.DryRun ? "Dry run" : "Cleaning", Item = t.Name, Percent = pct });

                t.Freed = 0; t.Pending = t.Bytes; t.Status = ""; t.ClosedApps.Clear();

                if (Cfg.DryRun) { t.Status = "would clean"; continue; }

                if (t.Pre != null) { try { t.Pre(); } catch { } }
                if (t.CustomClear != null) { try { t.CustomClear(); } catch { } t.Status = "ran (see drive delta)"; t.Pending = 0; continue; }
                foreach (var p in t.Paths) Disk.Clear(p, Cfg.MinAgeDays, ct);
                if (t.Post != null) { try { t.Post(); } catch { } }

                long after = Remaining(t, ct);

                if (Cfg.Unlock && after > 0)
                {
                    var names = Unlock(t, ui, ct);
                    if (names.Count > 0)
                    {
                        t.ClosedApps.AddRange(names);
                        foreach (var p in t.Paths) Disk.Clear(p, Cfg.MinAgeDays, ct);
                        after = Remaining(t, ct);
                    }
                }

                t.Pending = after;
                t.Freed = Math.Max(0, t.Bytes - after);
                t.Status = after == 0
                    ? (t.ClosedApps.Count > 0 ? "clean (closed " + string.Join(", ", t.ClosedApps.ToArray()) + ")" : "clean")
                    : (t.Freed <= 0 ? "locked / in use" : "partial (files in use)");

                if (report != null) report(new Progress { Stage = "Cleaning", Item = t.Name, Percent = pct, Freed = t.Freed, Pending = t.Pending });
            }
            CleanFreeAfter = Disk.FreeSpace(SystemDrive);
        }

        long Remaining(Target t, CancellationToken ct)
        {
            long total = 0;
            foreach (var p in t.Paths) { long b; int f; Disk.Measure(p, out b, out f, ct); total += b; }
            return total;
        }

        // ----------------------------------------------------------- unlock ---

        List<string> Unlock(Target t, IPrompter ui, CancellationToken ct)
        {
            var files = new List<string>();
            foreach (var p in t.Paths)
            {
                if (File.Exists(p)) { files.Add(p); continue; }
                if (!Directory.Exists(p)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Take(150))
                        files.Add(f);
                }
                catch { }
                if (files.Count >= 200) break;
            }
            if (files.Count == 0) return new List<string>();

            var lockers = RestartManager.GetLockers(files.Take(200).ToArray());
            int self = Process.GetCurrentProcess().Id;
            var tree = Guard.SelfTree();
            foreach (var l in lockers)
                l.Reason = Guard.Verdict(l.Id, l.Name, l.Type, Cfg.NeverCloseApp, self, tree);

            var ok = lockers.Where(l => l.Closeable).ToList();
            var refused = lockers.Where(l => !l.Closeable).ToList();
            if (ok.Count == 0)
            {
                foreach (var r in refused) ui.Report("   held by " + r.Name + " (pid " + r.Id + ") - " + r.Reason + ", left alone");
                return new List<string>();
            }

            var chosen = ui.ChooseAppsToClose(ok, refused);
            if (chosen == null || chosen.Count == 0) return new List<string>();
            return CloseApps(chosen, ui, ct);
        }

        // Only the window owner of each app is ever signalled. Chrome's renderers are
        // children - killing one shows RESULT_CODE_KILLED instead of closing cleanly.
        List<string> CloseApps(List<LockerInfo> chosen, IPrompter ui, CancellationToken ct)
        {
            var live = new List<Process>();
            foreach (var c in chosen)
                try { live.Add(Process.GetProcessById(c.Id)); } catch { }
            if (live.Count == 0) return new List<string>();

            var owners = new List<Process>();
            var launch = new Dictionary<int, ClosedApp>();
            foreach (var g in live.GroupBy(p => { try { return p.ProcessName; } catch { return "?"; } }))
            {
                var win = g.Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } }).ToList();
                if (win.Count == 0) win = g.OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } }).Take(1).ToList();
                foreach (var o in win)
                {
                    launch[o.Id] = LaunchInfo(o);
                    owners.Add(o);
                    bool asked = false;
                    try { if (o.MainWindowHandle != IntPtr.Zero) asked = o.CloseMainWindow(); } catch { }
                    // tray apps and windowless owners need taskkill's graceful WM_CLOSE - no /F
                    if (!asked) Run("taskkill.exe", "/PID " + o.Id + " /T");
                }
            }

            WaitGone(owners, TimeSpan.FromSeconds(20), ct);

            var stubborn = owners.Where(Alive).ToList();
            if (stubborn.Count > 0)
            {
                var insist = Cfg.ForceClose
                    ? stubborn.Select(p => new LockerInfo { Id = p.Id, Name = Safe(p) }).ToList()
                    : ui.ChooseAppsToForce(stubborn.Select(p => new LockerInfo { Id = p.Id, Name = Safe(p) }).ToList());
                foreach (var f in insist ?? new List<LockerInfo>())
                {
                    // kill the tree from the owner down; killing a lone child is what
                    // produces Chrome's crashed-tab page
                    Run("taskkill.exe", "/PID " + f.Id + " /T /F");
                    ui.Report("   forced " + f.Name + " (pid " + f.Id + ")");
                }
                if (insist != null && insist.Count > 0) Thread.Sleep(700);
            }

            // children are never force-killed; they follow their parent out
            WaitGone(live, TimeSpan.FromSeconds(8), ct);

            var gone = new List<string>();
            foreach (var o in owners)
            {
                if (Alive(o)) { ui.Report("   " + Safe(o) + " is still running - its files stay locked"); continue; }
                string nm = launch.ContainsKey(o.Id) ? launch[o.Id].Name : Safe(o);
                if (!gone.Contains(nm)) gone.Add(nm);
                if (launch.ContainsKey(o.Id))
                {
                    var app = launch[o.Id];
                    if (!string.IsNullOrEmpty(app.Path) && !Closed.Any(c => c.Path == app.Path)
                        && !app.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        Closed.Add(app);
                }
            }
            if (gone.Any(g => g.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                && Process.GetProcessesByName("explorer").Length == 0)
                try { Process.Start("explorer"); } catch { }
            return gone;
        }

        static bool Alive(Process p) { try { return !Process.GetProcessById(p.Id).HasExited; } catch { return false; } }
        static string Safe(Process p) { try { return p.ProcessName; } catch { return "?"; } }

        static void WaitGone(List<Process> ps, TimeSpan grace, CancellationToken ct)
        {
            var end = DateTime.Now + grace;
            while (DateTime.Now < end)
            {
                ct.ThrowIfCancellationRequested();
                if (!ps.Any(Alive)) return;
                Thread.Sleep(250);
            }
        }

        // Must run while the process is alive - the path and command line vanish with it.
        static ClosedApp LaunchInfo(Process p)
        {
            var app = new ClosedApp { Name = Safe(p) };
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE ProcessId=" + p.Id))
                foreach (ManagementObject mo in q.Get())
                {
                    app.Path = mo["ExecutablePath"] as string;
                    string cl = mo["CommandLine"] as string;
                    if (!string.IsNullOrEmpty(cl))
                    {
                        cl = cl.Trim();
                        if (cl.StartsWith("\""))
                        {
                            int close = cl.IndexOf('"', 1);
                            app.Args = close > 0 ? cl.Substring(close + 1).Trim() : "";
                        }
                        else
                        {
                            int sp = cl.IndexOf(' ');
                            app.Args = sp > 0 ? cl.Substring(sp + 1).Trim() : "";
                        }
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(app.Path)) { try { app.Path = p.MainModule.FileName; } catch { } }
            return app;
        }

        public void RestartClosed(IEnumerable<ClosedApp> apps, bool elevated, IPrompter ui)
        {
            foreach (var a in apps)
            {
                if (string.IsNullOrEmpty(a.Path) || !File.Exists(a.Path))
                { ui.Report("   could not relaunch " + a.Name + " - executable path unknown"); continue; }
                try
                {
                    if (elevated)
                        // via Explorer so the app returns at the user's integrity level,
                        // not as admin just because the cleaner was. Arguments are lost.
                        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + a.Path + "\"") { UseShellExecute = true });
                    else if (!string.IsNullOrEmpty(a.Args))
                        Process.Start(new ProcessStartInfo(a.Path, a.Args) { UseShellExecute = true });
                    else
                        Process.Start(new ProcessStartInfo(a.Path) { UseShellExecute = true });
                    ui.Report("   restarted " + a.Name);
                }
                catch (Exception ex) { ui.Report("   could not relaunch " + a.Name + " - " + ex.Message); }
            }
        }
    }
}
