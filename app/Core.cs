// Clear-WindowsJunk - engine.
// Direct port of the PowerShell script's logic. Every safety guard it grew the hard
// way is reproduced here and covered by SelfTest.cs:
//   * files are judged on their own timestamp, never the parent folder's
//   * reparse points are skipped, never traversed
//   * an empty directory is only removed if it is itself older than the cutoff
//   * closing only ever targets a window owner, never a child process
//   * the process tree that launched us can never be closed
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClearWindowsJunk
{
    public enum Grp { System, User, Advanced }

    public class Target
    {
        public string Name;
        public Grp Group;
        public bool OnByDefault;
        public bool NeedsAdmin;
        public string Note;
        public string[] Patterns = new string[0];
        public bool NoMeasure;                 // DISM: savings only show in the drive delta
        public Action Pre, Post;
        public Action CustomClear;

        // filled in by the scan
        public List<string> Paths = new List<string>();
        public long Bytes;
        public int Files;
        public bool Selected;

        // filled in by the clean
        public long Freed, Pending;
        public string Status = "";
        public List<string> ClosedApps = new List<string>();
    }

    public class LockerInfo
    {
        public int Id;
        public string Name;        // process name
        public string Display;     // friendly name from Restart Manager
        public int Type;           // 3 = service, 1000 = critical
        public string Service;
        public string Reason = ""; // "" means closeable
        public bool Closeable { get { return Reason.Length == 0; } }
    }

    public class ClosedApp
    {
        public string Name, Path, Args;
        public bool Restart = true;
    }

    public class Settings
    {
        public int MinAgeDays = 1;
        public bool DryRun, Unlock, ForceClose, RestartApps;
        public string ThemeName = "system";   // system | dark | light
        public string FontPair = "developer";
        public int WinW, WinH;
        public List<string> NeverCloseApp = new List<string>();

        // Where settings live.
        //
        // Default is the app's own store under HKCU - the .exe stays a single portable
        // file with nothing dropped beside it. If a settings.txt sits next to the exe
        // it wins, which is what lets the PowerShell version share one config.
        const string RegPath = @"Software\ClearWindowsJunk";

        public static string PortablePath
        {
            get
            {
                string dir;
                try { dir = Path.GetDirectoryName(typeof(Settings).Assembly.Location); }
                catch { return null; }
                return Path.Combine(dir, "Clear-WindowsJunk.settings.txt");
            }
        }

        public static bool IsPortable
        {
            get { return PortablePath != null && File.Exists(PortablePath); }
        }

        public static string WhereStored
        {
            get { return IsPortable ? PortablePath : @"HKCU\" + RegPath + "  (inside the app)"; }
        }

        // key = value, # comments, blank lines. Values may contain '='.
        static Dictionary<string, string> ReadFile(string path)
        {
            var map = new Dictionary<string, string>();
            if (path == null || !File.Exists(path)) return map;
            foreach (var raw in File.ReadAllLines(path))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                int i = t.IndexOf('=');
                if (i < 1) continue;
                map[t.Substring(0, i).Trim().ToLowerInvariant()] = t.Substring(i + 1).Trim();
            }
            return map;
        }

        static Dictionary<string, string> ReadRegistry()
        {
            var map = new Dictionary<string, string>();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k == null) return map;
                    foreach (var name in k.GetValueNames())
                    {
                        object v = k.GetValue(name);
                        if (v != null) map[name.ToLowerInvariant()] = v.ToString();
                    }
                }
            }
            catch { }
            return map;
        }

        public static Settings Load()
        {
            return FromMap(IsPortable ? ReadFile(PortablePath) : ReadRegistry());
        }

        // kept so the self-test can parse a specific file
        public static Settings Load(string path) { return FromMap(ReadFile(path)); }

        static Settings FromMap(Dictionary<string, string> map)
        {
            var s = new Settings();
            foreach (var kv in map)
            {
                string v = kv.Value;
                switch (kv.Key)
                {
                    case "minagedays": int n; if (int.TryParse(v, out n)) s.MinAgeDays = n; break;
                    case "dryrun": s.DryRun = On(v); break;
                    case "unlock": s.Unlock = On(v); break;
                    case "forceclose": s.ForceClose = On(v); break;
                    case "restart": s.RestartApps = On(v); break;
                    case "theme": s.ThemeName = v.Trim().ToLowerInvariant(); break;
                    case "fontpair": s.FontPair = v.Trim().ToLowerInvariant(); break;
                    case "windowsize":
                        var wh = v.Split(new[] { 'x', 'X' });
                        int ww, hh;
                        if (wh.Length == 2 && int.TryParse(wh[0].Trim(), out ww) && int.TryParse(wh[1].Trim(), out hh))
                        { s.WinW = ww; s.WinH = hh; }
                        break;
                    case "nevercloseapp":
                        s.NeverCloseApp = v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        break;
                }
            }
            return s;
        }

        Dictionary<string, string> ToMap()
        {
            var m = new Dictionary<string, string>();
            m["Theme"] = ThemeName;
            m["FontPair"] = FontPair;
            m["MinAgeDays"] = MinAgeDays.ToString();
            m["DryRun"] = DryRun ? "true" : "false";
            m["Unlock"] = Unlock ? "true" : "false";
            m["ForceClose"] = ForceClose ? "true" : "false";
            m["Restart"] = RestartApps ? "true" : "false";
            m["NeverCloseApp"] = string.Join(", ", NeverCloseApp.ToArray());
            if (WinW > 0 && WinH > 0) m["WindowSize"] = WinW + "x" + WinH;
            return m;
        }

        public void Save()
        {
            if (IsPortable) { SaveFile(PortablePath); return; }
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegPath))
                    foreach (var kv in ToMap()) k.SetValue(kv.Key, kv.Value);
            }
            catch { }
        }

        public void SaveFile(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Clear-WindowsJunk settings");
            sb.AppendLine("# Present next to the app, so this file wins over the in-app store.");
            sb.AppendLine("# Delete it to go back to keeping settings inside the app.");
            sb.AppendLine();
            foreach (var kv in ToMap()) sb.AppendLine(kv.Key + " = " + kv.Value);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // Move the config between the in-app store and a file beside the exe.
        public void SetPortable(bool portable)
        {
            try
            {
                if (portable) SaveFile(PortablePath);
                else
                {
                    if (PortablePath != null && File.Exists(PortablePath)) File.Delete(PortablePath);
                    Save();
                }
            }
            catch { }
        }

        public static bool On(string v)
        {
            if (v == null) return false;
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on";
        }
    }

    // ---------------------------------------------------------------- unlock ---

    public static class RestartManager
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

        public static List<LockerInfo> GetLockers(string[] files)
        {
            var result = new List<LockerInfo>();
            if (files == null || files.Length == 0) return result;

            uint handle;
            if (RmStartSession(out handle, 0, Guid.NewGuid().ToString("N")) != 0) return result;
            try
            {
                if (RmRegisterResources(handle, (uint)files.Length, files, 0, null, 0, null) != 0) return result;

                uint needed = 0, have = 0, reasons = 0;
                int rc = RmGetList(handle, out needed, ref have, null, ref reasons);
                if (rc == ERROR_MORE_DATA && needed > 0)
                {
                    var info = new RM_PROCESS_INFO[needed];
                    have = needed;
                    if (RmGetList(handle, out needed, ref have, info, ref reasons) == 0)
                    {
                        var seen = new HashSet<int>();
                        for (int i = 0; i < have; i++)
                        {
                            int pid = info[i].Process.dwProcessId;
                            if (!seen.Add(pid)) continue;
                            string procName = null;
                            try { procName = Process.GetProcessById(pid).ProcessName; }
                            catch { procName = info[i].strAppName; }
                            result.Add(new LockerInfo
                            {
                                Id = pid,
                                Name = procName ?? "",
                                Display = string.IsNullOrEmpty(info[i].strAppName) ? procName : info[i].strAppName,
                                Type = info[i].ApplicationType,
                                Service = info[i].strServiceShortName
                            });
                        }
                    }
                }
            }
            finally { RmEndSession(handle); }
            return result;
        }
    }

    public static class Guard
    {
        // Killing any of these means a bugcheck, a wiped logon, or shooting our own host.
        public static readonly string[] NeverClose = {
            "system","idle","registry","memory compression","smss","csrss","wininit","winlogon","logonui",
            "services","lsass","lsaiso","fontdrvhost","dwm","sihost","ctfmon","svchost","wmiprvse","audiodg",
            "msmpeng","nissrv","securityhealthservice","securityhealthsystray","trustedinstaller","tiworker",
            "conhost","openconsole","windowsterminal","powershell","pwsh","cmd","wudfhost","dllhost"
        };

        static List<int> _selfTree;

        // The shell / terminal / editor that launched us. A name blacklist can never know
        // the user is driving "bash.exe" or "T3 Code", so protect them by PID lineage.
        public static List<int> SelfTree()
        {
            if (_selfTree != null) return _selfTree;
            var parent = new Dictionary<int, int>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                foreach (ManagementObject mo in q.Get())
                    parent[Convert.ToInt32(mo["ProcessId"])] = Convert.ToInt32(mo["ParentProcessId"]);
            }
            catch { }

            var ids = new List<int>();
            int cur = Process.GetCurrentProcess().Id, hops = 0;
            while (cur != 0 && parent.ContainsKey(cur) && hops < 64) { ids.Add(cur); cur = parent[cur]; hops++; }
            if (cur != 0 && !ids.Contains(cur)) ids.Add(cur);
            _selfTree = ids;
            return ids;
        }

        // "" means closeable, anything else is the reason it was refused.
        public static string Verdict(int pid, string name, int type, IEnumerable<string> extraNeverClose,
                                     int selfPid, IEnumerable<int> ancestors)
        {
            if (pid <= 4) return "system";
            if (pid == selfPid) return "this app";
            if (ancestors != null && ancestors.Contains(pid)) return "this session";
            if (type == 1000) return "critical";
            if (type == 3) return "service";

            string n = (name ?? "").Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            n = n.ToLowerInvariant();
            if (n.Length == 0) return "unnamed";
            if (NeverClose.Contains(n)) return "protected";
            if (extraNeverClose != null)
                foreach (var e in extraNeverClose)
                {
                    string x = (e ?? "").Trim();
                    if (x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) x = x.Substring(0, x.Length - 4);
                    if (x.ToLowerInvariant() == n) return "on your never-close list";
                }
            return "";
        }

        public static bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------- file work ---

    public static class Disk
    {
        public static string FormatSize(double bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < 4) { bytes /= 1024; i++; }
            return bytes.ToString("N2") + " " + u[i];
        }

        public static long FreeSpace(string drive)
        {
            try { return new DriveInfo(drive).AvailableFreeSpace; } catch { return 0; }
        }

        static bool IsReparse(FileSystemInfo fsi)
        {
            return (fsi.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        // Iterative so a deep tree cannot blow the stack. Junctions are never followed:
        // counting through one would report bytes that Clean can never remove.
        public static void Measure(string path, out long bytes, out int files, CancellationToken ct)
        {
            bytes = 0; files = 0;
            if (File.Exists(path)) { try { var fi = new FileInfo(path); bytes = fi.Length; files = 1; } catch { } return; }
            if (!Directory.Exists(path)) return;

            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                DirectoryInfo di;
                try { di = new DirectoryInfo(stack.Pop()); } catch { continue; }
                FileSystemInfo[] kids;
                try { kids = di.GetFileSystemInfos(); } catch { continue; }
                foreach (var k in kids)
                {
                    try
                    {
                        if (IsReparse(k)) continue;
                        var sub = k as DirectoryInfo;
                        if (sub != null) stack.Push(sub.FullName);
                        else { bytes += ((FileInfo)k).Length; files++; }
                    }
                    catch { }
                }
            }
        }

        // Every FILE is judged on its own timestamp. A folder's mtime says nothing about
        // how fresh its contents are, so recursing blindly would take a file an app wrote
        // seconds ago. A directory is only removed when it is empty AND older than the
        // cutoff itself, so the folder skeleton an app expects on next launch survives.
        public static void RemoveAged(string dir, DateTime cut, CancellationToken ct)
        {
            DirectoryInfo di;
            try { di = new DirectoryInfo(dir); if (!di.Exists) return; } catch { return; }
            FileSystemInfo[] kids;
            try { kids = di.GetFileSystemInfos(); } catch { return; }

            foreach (var k in kids)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (IsReparse(k)) continue;
                    var sub = k as DirectoryInfo;
                    if (sub != null)
                    {
                        RemoveAged(sub.FullName, cut, ct);
                        try
                        {
                            if (sub.LastWriteTime <= cut && sub.GetFileSystemInfos().Length == 0)
                                sub.Delete(false);
                        }
                        catch { }
                    }
                    else if (k.LastWriteTime <= cut)
                    {
                        try { ((FileInfo)k).IsReadOnly = false; } catch { }
                        k.Delete();
                    }
                }
                catch { }
            }
        }

        public static void Clear(string path, int minAgeDays, CancellationToken ct)
        {
            DateTime cut = minAgeDays <= 0 ? DateTime.MaxValue : DateTime.Now.AddDays(-minAgeDays);
            if (File.Exists(path))
            {
                try { var fi = new FileInfo(path); if (fi.LastWriteTime <= cut) { fi.IsReadOnly = false; fi.Delete(); } } catch { }
                return;
            }
            if (Directory.Exists(path)) RemoveAged(path, cut, ct);
        }

        // Deletes one path outright - used only when the user points at a single entry
        // in the breakdown. Still junction-safe: a link is unlinked, never followed.
        public static bool DeletePath(string path, CancellationToken ct)
        {
            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path) { IsReadOnly = false };
                    fi.Delete();
                    return true;
                }
                if (!Directory.Exists(path)) return true;

                var di = new DirectoryInfo(path);
                if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                { di.Delete(false); return true; }     // drop the link, keep the target

                RemoveAged(path, DateTime.MaxValue, ct);
                try { di.Delete(true); } catch { }
                return !Directory.Exists(path);
            }
            catch { return false; }
        }

        // Expands the wildcard in patterns like "...\User Data\*\Cache".
        public static List<string> Resolve(string pattern)
        {
            var outp = new List<string>();
            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0)
            {
                if (Directory.Exists(pattern) || File.Exists(pattern)) outp.Add(pattern);
                return outp;
            }
            int star = pattern.IndexOf('*');
            int slash = pattern.LastIndexOf('\\', star);
            if (slash < 0) return outp;
            string root = pattern.Substring(0, slash);
            string rest = pattern.Substring(slash + 1);
            int nextSlash = rest.IndexOf('\\');
            string seg = nextSlash < 0 ? rest : rest.Substring(0, nextSlash);
            string tail = nextSlash < 0 ? "" : rest.Substring(nextSlash + 1);
            if (!Directory.Exists(root)) return outp;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root, seg); } catch { return outp; }
            foreach (var d in dirs)
            {
                string full = tail.Length == 0 ? d : Path.Combine(d, tail);
                if (full.IndexOf('*') >= 0) outp.AddRange(Resolve(full));
                else if (Directory.Exists(full) || File.Exists(full)) outp.Add(full);
            }
            return outp;
        }
    }
}
