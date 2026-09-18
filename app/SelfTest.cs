// Clear-WindowsJunk - the same guards the PowerShell -SelfTest proves, ported.
// Run with:  Clear Windows Junk.exe --selftest
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ClearWindowsJunk
{
    public static class SelfTest
    {
        static int _fails;

        static void Check(bool ok, string what)
        {
            if (ok) { Console.WriteLine("  ok   " + what); return; }
            _fails++;
            Console.WriteLine("  FAIL " + what);
        }

        public static int Run()
        {
            Console.WriteLine("Clear-WindowsJunk self-test");
            var ct = CancellationToken.None;

            // ---- sizes -----------------------------------------------------
            Check(Disk.FormatSize(0) == "0.00 B", "FormatSize zero");
            Check(Disk.FormatSize(1024) == "1.00 KB", "FormatSize KB");
            Check(Disk.FormatSize(1610612736L) == "1.50 GB", "FormatSize GB");

            // ---- age filter, junctions, folder skeleton --------------------
            string sandbox = Path.Combine(Path.GetTempPath(), "junktest_" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(Path.GetTempPath(), "junkout_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sandbox, "sub"));
            Directory.CreateDirectory(outside);

            File.WriteAllBytes(Path.Combine(sandbox, "old.bin"), new byte[4096]);
            File.WriteAllBytes(Path.Combine(sandbox, "sub", "a.bin"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(sandbox, "new.bin"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(outside, "precious.bin"), new byte[512]);
            var old = DateTime.Now.AddDays(-30);
            File.SetLastWriteTime(Path.Combine(sandbox, "old.bin"), old);
            Directory.SetLastWriteTime(Path.Combine(sandbox, "sub"), old);
            File.SetLastWriteTime(Path.Combine(outside, "precious.bin"), old);

            long b; int f;
            Disk.Measure(sandbox, out b, out f, ct);
            Check(b == 7168 && f == 3, "Measure counts 7168 bytes in 3 files");

            Disk.Clear(sandbox, 1, ct);
            Check(File.Exists(Path.Combine(sandbox, "sub", "a.bin")),
                  "age filter spares a fresh file inside a 30-day-old folder");
            Check(!File.Exists(Path.Combine(sandbox, "old.bin")), "age filter removes the old file");
            Check(Directory.Exists(Path.Combine(sandbox, "sub")),
                  "folder skeleton survives so an app finds it on next launch");
            Disk.Measure(sandbox, out b, out f, ct);
            Check(b == 3072, "age filter leaves 3072 bytes, got " + b);

            // junction must be skipped, never traversed
            string link = Path.Combine(sandbox, "link");
            bool madeLink = MakeJunction(link, outside);
            if (madeLink)
            {
                Disk.Measure(sandbox, out b, out f, ct);
                Check(b == 3072, "Measure does not count through a junction, got " + b);
                Disk.Clear(sandbox, 0, ct);
                Check(File.Exists(Path.Combine(outside, "precious.bin")),
                      "Clear does not delete through a junction");
                try { Directory.Delete(link); } catch { }
            }
            else Console.WriteLine("  skip junction test (could not create link)");

            Disk.Clear(sandbox, 0, ct);
            Disk.Measure(sandbox, out b, out f, ct);
            Check(b == 0, "full clear empties the folder");
            Check(Directory.Exists(sandbox), "full clear keeps the target folder itself");

            try { Directory.Delete(sandbox, true); } catch { }
            try { Directory.Delete(outside, true); } catch { }

            // ---- single-entry delete (the breakdown's trash action) --------
            string del = Path.Combine(Path.GetTempPath(), "junkdel_" + Guid.NewGuid().ToString("N"));
            string keep = Path.Combine(Path.GetTempPath(), "junkkeep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(del, "nested"));
            Directory.CreateDirectory(keep);
            File.WriteAllBytes(Path.Combine(del, "a.bin"), new byte[64]);
            File.WriteAllBytes(Path.Combine(del, "nested", "b.bin"), new byte[64]);
            File.WriteAllBytes(Path.Combine(keep, "survivor.bin"), new byte[64]);

            string lone = Path.Combine(del, "lone.bin");
            File.WriteAllBytes(lone, new byte[32]);
            Check(Disk.DeletePath(lone, ct) && !File.Exists(lone), "delete: removes a single file");
            Check(Disk.DeletePath(Path.Combine(del, "nope.bin"), ct), "delete: missing path reports success");

            string linkD = Path.Combine(del, "tolink");
            if (MakeJunction(linkD, keep))
            {
                Check(Disk.DeletePath(linkD, ct), "delete: unlinks a junction");
                Check(!Directory.Exists(linkD), "delete: the link itself is gone");
                Check(File.Exists(Path.Combine(keep, "survivor.bin")),
                      "delete: junction target is untouched");
            }
            else Console.WriteLine("  skip junction delete test (could not create link)");

            Check(Disk.DeletePath(del, ct) && !Directory.Exists(del),
                  "delete: removes a folder and everything inside");
            try { Directory.Delete(keep, true); } catch { }

            // ---- breakdown coverage (stops a ticked child double-counting) --
            Check(PathCoverage.Under(@"C:\Temp\sub", @"C:\Temp"), "under: direct child");
            Check(PathCoverage.Under(@"C:\Temp\a\b.bin", @"C:\Temp"), "under: deep descendant");
            Check(PathCoverage.Under(@"C:\TEMP\Sub", @"c:\temp"), "under: case-insensitive");
            Check(PathCoverage.Under(@"C:\Temp\sub", @"C:\Temp\"), "under: trailing slash on parent");
            Check(!PathCoverage.Under(@"C:\TempFiles\x", @"C:\Temp"), "under: sibling prefix is not a child");
            Check(!PathCoverage.Under(@"C:\Other\x", @"C:\Temp"), "under: unrelated path");
            Check(!PathCoverage.Under(null, @"C:\Temp"), "under: null child");

            var pick = new List<string> { @"C:\Temp\a", @"C:\Temp\b", @"C:\Other\c" };
            var roots = new List<string> { @"C:\Temp" };
            var freeIx = PathCoverage.Free(pick, roots);
            Check(freeIx.Count == 1 && freeIx[0] == 2,
                  "coverage: children of a ticked target stop counting, outsiders keep counting");

            Check(PathCoverage.Free(pick, new List<string>()).Count == 3,
                  "coverage: nothing ticked above means every pick counts");

            Check(PathCoverage.Inside(@"C:\Temp\a", @"C:\Temp"), "inside: strict descendant");
            Check(!PathCoverage.Inside(@"C:\Temp", @"C:\Temp"), "inside: a path never covers itself");
            Check(PathCoverage.Free(new List<string> { @"C:\Temp" }, new List<string> { @"C:\Temp" }).Count == 1,
                  "coverage: an entry that is itself the root still counts once");

            // a folder only absorbs its contents while it is ticked whole; once it goes
            // partial it stops being a covering root and its picks count individually
            var nested = new List<string> { @"C:\Temp\a", @"C:\Temp\a\deep", @"C:\Temp\a\deep\x.bin" };
            var whole = PathCoverage.Free(nested, new List<string> { @"C:\Temp\a" });
            Check(whole.Count == 1 && whole[0] == 0, "coverage: a whole folder absorbs everything under it");
            Check(PathCoverage.Free(nested, new List<string>()).Count == 3,
                  "coverage: a partial folder absorbs nothing, each pick counts");

            var dup = new List<string> { @"C:\Temp\a", @"C:\Temp\a" };
            Check(PathCoverage.Free(dup, new List<string>()).Count == 2,
                  "coverage: identical paths do not cancel each other out");

            // ---- close safety gate ----------------------------------------
            int self = Process.GetCurrentProcess().Id;
            var tree = new List<int> { 5272, 11184, 5248 };
            var never = new List<string> { "T3 Code", "idman.exe" };

            foreach (var bad in new[] { "lsass", "svchost", "csrss", "winlogon", "conhost", "Cmd.exe" })
                Check(Guard.Verdict(9999, bad, 1, never, self, tree).Length > 0, "refuses " + bad);
            Check(Guard.Verdict(9999, "chrome", 3, never, self, tree) == "service", "refuses any service");
            Check(Guard.Verdict(9999, "chrome", 1000, never, self, tree) == "critical", "refuses RmCritical");
            Check(Guard.Verdict(4, "System", 1, never, self, tree) == "system", "refuses pid 4");
            Check(Guard.Verdict(self, "x", 1, never, self, tree) == "this app", "refuses our own pid");
            Check(Guard.Verdict(11184, "bash", 1, never, self, tree) == "this session",
                  "refuses an ancestor - a name list cannot know you are driving bash.exe");
            Check(Guard.Verdict(5248, "T3 Code", 1, never, self, tree) == "this session", "refuses the launching editor");
            Check(Guard.Verdict(9999, "T3 Code", 1, never, self, new List<int>()) == "on your never-close list",
                  "honours the never-close list when not an ancestor");
            Check(Guard.Verdict(9999, "IDMan", 1, never, self, new List<int>()) == "on your never-close list",
                  "never-close matches with or without .exe");
            Check(Guard.Verdict(9999, "bash", 1, never, self, new List<int> { 98, 99 }) == "",
                  "an unrelated bash stays closeable");
            Check(Guard.Verdict(9999, "chrome", 1, never, self, tree) == "", "an ordinary app is closeable");
            Check(Guard.SelfTree().Contains(self), "self tree contains our own pid");

            // ---- settings round trip --------------------------------------
            string cfg = Path.Combine(Path.GetTempPath(), "junkcfg_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(cfg, new[] {
                "# a comment", "", "  MinAgeDays = 3  ", "UNLOCK=true",
                "NeverCloseApp = T3 Code, idman", "Weird = a=b", "nokeyline", "# Restart = true" });
            var s = Settings.Load(cfg);
            Check(s.MinAgeDays == 3, "settings: int parsed and trimmed");
            Check(s.Unlock, "settings: key case ignored");
            Check(!s.RestartApps, "settings: commented line ignored");
            Check(s.NeverCloseApp.Count == 2 && s.NeverCloseApp[1] == "idman", "settings: list split");
            Check(Settings.On("YES") && !Settings.On("false") && !Settings.On(null), "settings: truthiness");
            Check(Settings.Load(Path.Combine(Path.GetTempPath(), "no-such-a1b2c3.txt")).MinAgeDays == 1,
                  "settings: missing file falls back to defaults");

            var rt = new Settings { MinAgeDays = 7, Unlock = true, NeverCloseApp = new List<string> { "code", "idman" } };
            rt.SaveFile(cfg);
            var back = Settings.Load(cfg);
            Check(back.MinAgeDays == 7 && back.Unlock && back.NeverCloseApp.Count == 2, "settings: file round trip");
            try { File.Delete(cfg); } catch { }

            // in-app store: no file anywhere, values survive a save/load cycle
            Check(!Settings.IsPortable || File.Exists(Settings.PortablePath),
                  "settings: portable flag agrees with the file on disk");
            var before = Settings.Load();
            var probe = new Settings
            {
                MinAgeDays = 9, ThemeName = "light", FontPair = "terminal",
                NeverCloseApp = new List<string> { "selftest-probe" }
            };
            if (!Settings.IsPortable)
            {
                probe.Save();
                var got = Settings.Load();
                Check(got.MinAgeDays == 9 && got.ThemeName == "light" && got.FontPair == "terminal"
                      && got.NeverCloseApp.Count == 1 && got.NeverCloseApp[0] == "selftest-probe",
                      "settings: in-app store round trip");
                Check(!File.Exists(Settings.PortablePath), "settings: in-app store writes no file beside the exe");
                before.Save();                                  // put the user's values back
                Check(Settings.Load().MinAgeDays == before.MinAgeDays, "settings: original values restored");
            }
            else Console.WriteLine("  skip in-app store test (running in portable mode)");

            // ---- launch capture -------------------------------------------
            var me = Process.GetCurrentProcess();
            Check(Disk.Resolve(Path.GetTempPath() + "*").Count >= 0, "Resolve tolerates a wildcard tail");
            Check(Engine.BuildTargets().Count > 15, "target list built");
            Check(Engine.BuildTargets().Any(t => t.Name == "User Temp" && !t.NeedsAdmin), "User Temp needs no admin");
            Check(Engine.BuildTargets().Any(t => t.Name == "Windows Temp" && t.NeedsAdmin), "Windows Temp needs admin");

            // ---- XAML parses, and every window carries the themed chrome ---
            // A XAML mistake is a runtime exception, not a compile error, and a window
            // that forgets Layout.SharedStyles silently falls back to stock Win32
            // chrome - a native checkbox and black text on the dark surface.
            CheckWindowChrome("main window", Layout.MainXaml);
            CheckWindowChrome("app picker", PickWindow.Xaml);
            CheckWindowChrome("confirm dialog", Dialog.Xaml);

            // Tooltips live in popups, so they are styled once at app scope; without this
            // they fall back to the pale Win32 box that ignores the theme entirely.
            var appStyles = (System.Windows.ResourceDictionary)
                System.Windows.Markup.XamlReader.Parse(Layout.AppStyles);
            var tip = appStyles[typeof(System.Windows.Controls.ToolTip)] as System.Windows.Style;
            Check(tip != null && HasTemplate(tip), "tooltips are themed, not the stock Win32 box");

            // Ui.F<T>() throws on a missing name, so a typo in the overlay markup is a
            // crash the first time a scan starts - not something the compiler sees.
            CheckNames("progress overlay", Layout.MainXaml,
                       "BusyOverlay", "BusyTitle", "BusyItem", "BusyTrack", "BusyFill",
                       "BusyPct", "BusyCancel");

            Console.WriteLine();
            Console.WriteLine(_fails == 0 ? "SelfTest OK" : _fails + " FAILURE(S)");
            return _fails == 0 ? 0 : 1;
        }

        // Parsing proves the markup is well-formed and that every StaticResource in it
        // resolves; the style probes prove the shared templates actually landed.
        static void CheckWindowChrome(string who, string xaml)
        {
            System.Windows.Controls.Border root;
            try { root = (System.Windows.Controls.Border)System.Windows.Markup.XamlReader.Parse(xaml); }
            catch (Exception ex)
            {
                Check(false, who + ": XAML parses (" + ex.Message + ")");
                return;
            }
            Check(true, who + ": XAML parses");
            Check(Templated(root, typeof(System.Windows.Controls.CheckBox)),
                  who + ": checkbox is themed, not stock Win32 chrome");
            Check(root.Resources.Contains(typeof(System.Windows.Controls.TextBlock)),
                  who + ": text picks up the theme brush instead of defaulting to black");
        }

        static bool Templated(System.Windows.Controls.Border root, Type control)
        {
            if (!root.Resources.Contains(control)) return false;
            return HasTemplate(root.Resources[control] as System.Windows.Style);
        }

        static bool HasTemplate(System.Windows.Style st)
        {
            if (st == null) return false;
            foreach (var sb in st.Setters)
            {
                var set = sb as System.Windows.Setter;
                if (set != null && set.Property == System.Windows.Controls.Control.TemplateProperty)
                    return true;
            }
            return false;
        }

        static void CheckNames(string who, string xaml, params string[] names)
        {
            var root = (System.Windows.Controls.Border)System.Windows.Markup.XamlReader.Parse(xaml);
            foreach (var n in names)
                Check(root.FindName(n) != null, who + ": '" + n + "' exists in the markup");
        }

        static bool MakeJunction(string link, string target)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { p.WaitForExit(); }
                return Directory.Exists(link);
            }
            catch { return false; }
        }
    }
}
