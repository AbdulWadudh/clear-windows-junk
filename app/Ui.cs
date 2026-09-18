// Clear-WindowsJunk - WPF shell. Layout comes from the XAML string in Layout.cs;
// this file wires it up and keeps all the work off the UI thread.
// Visual direction (palette, hierarchy, tabular figures) from the ui-ux-pro-max
// design system: dark slate tech surface + status green accent.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Markup;
using System.Windows.Shapes;

namespace ClearWindowsJunk
{
    public class TargetVm : INotifyPropertyChanged
    {
        public Target T;
        public TargetVm(Target t) { T = t; }

        public string Name { get { return T.Name; } }
        public bool Locked;
        public bool ShowHeader { get; set; }
        public string HeaderText { get; set; }
        public string GroupKey { get; set; }
        public Func<string, bool?> GroupState;     // true all, false none, null mixed
        public Action<string, bool> GroupApply;

        // null renders as a dash: some of the group is ticked, not all of it
        public bool? GroupSelected
        {
            get { return GroupState == null ? (bool?)false : GroupState(GroupKey); }
            set { if (GroupApply != null) GroupApply(GroupKey, value == true); }
        }

        public string SizeText { get { return T.NoMeasure ? "n/a" : Disk.FormatSize(T.Bytes); } }
        public string FirstPath { get { return T.Paths.Count > 0 ? T.Paths[0] : null; } }
        public bool CanOpen { get { return T.Paths.Count > 0; } }
        public bool Expanded;
        public string Chevron { get { return Expanded ? "\uE70D" : "\uE76C"; } }
        public string FilesText { get { return T.NoMeasure || T.Files == 0 ? "" : T.Files.ToString("N0") + " files"; } }
        public bool Enabled { get { return !Locked; } }
        public string LockNote { get { return Locked ? "needs administrator" : (T.Note ?? ""); } }
        public bool HasNote { get { return LockNote.Length > 0; } }

        // dim a zero-byte row so the eye lands on what actually matters
        public Brush SizeBrush
        {
            get
            {
                return Theme.Get(T.Bytes == 0 && !T.NoMeasure ? "ZeroText"
                               : T.Bytes > 1073741824L ? "Accent" : "Text");
            }
        }

        public bool Selected
        {
            get { return T.Selected; }
            set
            {
                if (T.Selected == value) return;
                T.Selected = value;
                Raise("Selected");
                if (Cascade != null) Cascade(this, value);
                if (SelectionChanged != null) SelectionChanged();
            }
        }

        public Action SelectionChanged;
        public Action<TargetVm, bool> Cascade;   // ticking a target ticks its open children
        public Func<TargetVm, bool?> TriState;   // window decides all / none / mixed

        // null draws the dash: some of what is under this row is ticked, not all
        public bool? Checked
        {
            get { return TriState == null ? (bool?)Selected : TriState(this); }
            set { Selected = (value == true); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        public void Raise(string p)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(p));
        }
        public void RaiseAll()
        {
            foreach (var n in new[] { "SizeText", "FilesText", "Selected", "Enabled", "LockNote", "HasNote", "SizeBrush", "GroupSelected", "FirstPath", "CanOpen", "Chevron", "Checked" })
                Raise(n);
        }
    }

    public class NeverCloseVm { public string Name { get; set; } }

    public class MainWindow : Window, IPrompter
    {
        readonly Engine _eng = new Engine();
        readonly ObservableCollection<TargetVm> _vms = new ObservableCollection<TargetVm>();
        readonly ObservableCollection<object> _rows = new ObservableCollection<object>();
        CancellationTokenSource _treeCts;
        readonly ObservableCollection<NeverCloseVm> _never = new ObservableCollection<NeverCloseVm>();
        readonly List<string> _notes = new List<string>();
        CancellationTokenSource _cts;
        bool _syncing;                 // stops the group box and its rows echoing each other
        Border _root;
        bool _admin, _busy;

        // chart series: every slice is also labelled, so hue is never the only cue
        static readonly string[] Palette =
        { "Chart1", "Chart2", "Chart3", "Chart4", "Chart5", "Chart6", "Chart7", "Chart8" };

        public MainWindow()
        {
            // theme must exist before the XAML resolves its DynamicResources
            var boot = Settings.Load();
            Theme.CurrentPair = Theme.ParsePair(boot.FontPair);
            Theme.Apply(Theme.Parse(boot.ThemeName));

            _root = (Border)XamlReader.Parse(Layout.MainXaml);
            Content = _root;
            Width = 1060; Height = 720; MinWidth = 920; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = "Clear Windows Junk";
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            SourceInitialized += (s, e) => RoundCorners();
            PreviewKeyDown += OnKey;

            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            });

            F<Border>("TitleBar").MouseLeftButtonDown += (s, e) =>
            { if (e.ClickCount == 2) ToggleMax(); else DragMove(); };
            F<Button>("MinBtn").Click += (s, e) => WindowState = WindowState.Minimized;
            F<Button>("MaxBtn").Click += (s, e) => ToggleMax();
            F<Button>("CloseBtn").Click += (s, e) => Close();

            F<Button>("SettingsBtn").Click += (s, e) =>
                ShowSettings(F<Border>("SettingsPane").Visibility != Visibility.Visible);

            F<Button>("ScanBtn").Click += async (s, e) => await DoScan();
            F<Button>("CleanBtn").Click += async (s, e) => await DoClean();
            F<Button>("CancelBtn").Click += (s, e) => { if (_cts != null) _cts.Cancel(); };
            F<Button>("BusyCancel").Click += (s, e) => { if (_cts != null) _cts.Cancel(); };
            F<Button>("AllBtn").Click += (s, e) => SetAll(true);
            F<Button>("NoneBtn").Click += (s, e) => SetAll(false);
            F<Button>("SafeBtn").Click += (s, e) => SetSafe();
            F<Button>("SumClose").Click += (s, e) => F<Border>("SummaryCard").Visibility = Visibility.Collapsed;
            F<Button>("SettingsSave").Click += (s, e) => SaveSettings();
            F<Button>("OpenAppFolder").Click += (s, e) =>
            {
                try { OpenInExplorer(System.IO.Path.GetDirectoryName(
                    Process.GetCurrentProcess().MainModule.FileName)); }
                catch { }
            };
            F<Button>("SettingsCancel").Click += (s, e) => { LoadSettingsIntoUi(); GoClean(); };

            F<Button>("AddNeverClose").Click += (s, e) => AddNeverClose();
            F<TextBox>("SetNeverClose").KeyDown += (s, e) =>
            { if (e.Key == System.Windows.Input.Key.Enter) { AddNeverClose(); e.Handled = true; } };
            F<ItemsControl>("NeverCloseList").AddHandler(Button.ClickEvent, new RoutedEventHandler(RemoveNeverClose));
            F<ItemsControl>("NeverCloseList").ItemsSource = _never;

            F<CheckBox>("DryRun").Checked += (s, e) => _eng.Cfg.DryRun = true;
            F<CheckBox>("DryRun").Unchecked += (s, e) => _eng.Cfg.DryRun = false;
            F<CheckBox>("Unlock").Checked += (s, e) => _eng.Cfg.Unlock = true;
            F<CheckBox>("Unlock").Unchecked += (s, e) => _eng.Cfg.Unlock = false;

            _admin = Guard.IsAdmin();
            _eng.Cfg = boot;
            LoadSettingsIntoUi();

            F<RadioButton>("ThemeSystem").Checked += (s, e) => SwitchTheme(ThemeMode.System);
            F<RadioButton>("ThemeDark").Checked   += (s, e) => SwitchTheme(ThemeMode.Dark);
            F<RadioButton>("ThemeLight").Checked  += (s, e) => SwitchTheme(ThemeMode.Light);
            F<RadioButton>("FontDeveloper").Checked  += (s, e) => SwitchFont("developer");
            F<RadioButton>("FontTerminal").Checked   += (s, e) => SwitchFont("terminal");
            F<RadioButton>("FontIndustrial").Checked += (s, e) => SwitchFont("industrial");
            F<RadioButton>("FontSystem").Checked     += (s, e) => SwitchFont("system");

            var badge = F<TextBlock>("AdminBadge");
            badge.Text = _admin ? "administrator" : "limited · click to elevate";
            badge.Foreground = B(_admin ? "Accent" : "Warn");
            var box = F<Button>("BadgeBox");
            if (_admin)
            {
                box.IsHitTestVisible = false;
                box.Cursor = System.Windows.Input.Cursors.Arrow;
            }
            else
            {
                box.ToolTip = "Restart with administrator rights to reach the system caches";
                box.Click += (s, e) => RelaunchElevated();
            }

            if (_eng.Cfg.WinW > 600 && _eng.Cfg.WinH > 400) { Width = _eng.Cfg.WinW; Height = _eng.Cfg.WinH; }
            Closing += (s, e) =>
            {
                if (WindowState == WindowState.Normal)
                { _eng.Cfg.WinW = (int)Width; _eng.Cfg.WinH = (int)Height; }
                try { _eng.Cfg.Save(); } catch { }
            };

            _eng.OrderForUser(_admin);
            BuildRows();
            var list = F<ItemsControl>("TargetList");
            list.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRowButton));
            list.ItemTemplateSelector = new RowTemplateSelector
            {
                TargetRow = (DataTemplate)_root.FindResource("TargetRow"),
                NodeRow = (DataTemplate)_root.FindResource("NodeRow")
            };
            list.ItemsSource = _rows;
            UpdateFree();
            Loaded += async (s, e) => await DoScan();
        }

        // Windows 11 rounds the window; on 10 the call is ignored.
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        void RoundCorners()
        {
            try
            {
                int round = 2;   // DWMWCP_ROUND
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                DwmSetWindowAttribute(h, 33, ref round, sizeof(int));
            }
            catch { }
        }

        async void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
            if (e.Key == System.Windows.Input.Key.F5) { e.Handled = true; await DoScan(); }
            else if (ctrl && e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await DoClean(); }
            else if (ctrl && e.Key == System.Windows.Input.Key.A) { e.Handled = true; SetAll(true); }
            else if (ctrl && e.Key == System.Windows.Input.Key.D) { e.Handled = true; SetAll(false); }
            else if (ctrl && e.Key == System.Windows.Input.Key.OemComma) { e.Handled = true; ShowSettings(true); }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                if (F<Border>("SummaryCard").Visibility == Visibility.Visible)
                    F<Border>("SummaryCard").Visibility = Visibility.Collapsed;
                else if (F<Border>("SettingsPane").Visibility == Visibility.Visible) GoClean();
                else if (_busy && _cts != null) _cts.Cancel();
            }
        }

        T F<T>(string name) where T : class { return _root.FindName(name) as T; }
        static Brush B(string token) { return Theme.Get(token); }
        void ToggleMax() { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        void GoClean() { ShowSettings(false); }

        void BuildRows()
        {
            _vms.Clear();
            Grp? last = null;
            foreach (var t in _eng.Targets)
            {
                var vm = new TargetVm(t) { Locked = t.NeedsAdmin && !_admin };
                if (last == null || last.Value != t.Group)
                {
                    vm.ShowHeader = true;
                    vm.HeaderText = t.Group == Grp.System ? "SYSTEM · needs administrator"
                                  : t.Group == Grp.User ? "YOUR ACCOUNT"
                                  : "ADVANCED · read the note first";
                    last = t.Group;
                }
                vm.GroupKey = t.Group.ToString();
                vm.GroupState = GroupState;
                vm.GroupApply = ApplyGroup;
                vm.SelectionChanged = UpdateSelectionLine;
                vm.Cascade = CascadeFromTarget;
                vm.TriState = TargetState;
                _vms.Add(vm);
            }
            SyncRows();
        }

        // _rows is the flattened view: every target, plus any expanded children
        void SyncRows()
        {
            _rows.Clear();
            foreach (var v in _vms) { v.Expanded = false; _rows.Add(v); }
            OnNodeSelectionChanged();
        }

        // ------------------------------------------------------------ chrome ---

        void UpdateFree()
        {
            long free = Disk.FreeSpace(_eng.SystemDrive);
            long total = 0;
            try { total = new System.IO.DriveInfo(_eng.SystemDrive).TotalSize; } catch { }
            F<TextBlock>("FreeBig").Text = Disk.FormatSize(free);
            F<TextBlock>("FreeSub").Text = "free of " + (total > 0 ? Disk.FormatSize(total) : _eng.SystemDrive);
            if (total > 0)
            {
                var fill = F<Border>("DiskFill");
                double w = Math.Max(0, Math.Min(1, (double)(total - free) / total)) * 150;
                fill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(420))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        void UpdateSelectionLine()
        {
            if (!_syncing) RefreshHeaders();
            var sel = WholeTargets();               // a partial target contributes its picks, not itself
            var nodes = EffectivePicked();          // never double-counts a ticked child
            long bytes = sel.Sum(v => v.T.Bytes) + nodes.Sum(n => n.Bytes);
            F<TextBlock>("SelText").Text = string.Format("{0} of {1} selected{2}  ·  {3}  ·  {4:N0} files",
                sel.Count, _vms.Count,
                nodes.Count == 0 ? "" : "  +  " + nodes.Count + (nodes.Count == 1 ? " entry" : " entries"),
                Disk.FormatSize(bytes), sel.Sum(v => v.T.Files) + nodes.Sum(n => n.Files));
            SetHero(bytes, sel.Count + nodes.Count);
            F<Button>("CleanBtn").IsEnabled = (sel.Count > 0 || SelectedNodes().Count > 0) && !_busy;
        }

        void SetHero(long bytes, int count)
        {
            string s = Disk.FormatSize(bytes);
            int sp = s.LastIndexOf(' ');
            F<TextBlock>("HeroValue").Text = sp > 0 ? s.Substring(0, sp) : s;
            F<TextBlock>("HeroUnit").Text = sp > 0 ? s.Substring(sp + 1) : "";
            F<TextBlock>("HeroSub").Text = count == 0
                ? "Nothing selected yet"
                : "across " + count + (count == 1 ? " location" : " locations")
                  + (_eng.Cfg.DryRun ? "  ·  dry run" : "");

            var sel = _vms.Where(v => v.Selected).ToList();
            F<TextBlock>("StatFilesV").Text = sel.Sum(v => v.T.Files).ToString("N0");
            var big = sel.OrderByDescending(v => v.T.Bytes).FirstOrDefault();
            F<TextBlock>("StatBiggestV").Text = big == null ? "—" : Disk.FormatSize(big.T.Bytes);
            F<TextBlock>("StatBiggestL").Text = big == null ? "biggest" : big.Name.ToUpperInvariant();
            F<TextBlock>("StatAfterV").Text = Disk.FormatSize(Disk.FreeSpace(_eng.SystemDrive) + bytes);
        }

        // every button inside a row arrives here; which one it was decides the action
        void OnRowButton(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            if (b == null) return;
            var fe = b as FrameworkElement;

            if (b.Name == "openBtn")
            {
                if (b.Tag == null) return;
                e.Handled = true;
                OpenInExplorer(b.Tag.ToString());
            }
            else if (b.Name == "expandBtn")
            {
                var vm = fe != null ? fe.DataContext as TargetVm : null;
                if (vm == null) return;
                e.Handled = true;
                ToggleTarget(vm);
            }
            else if (b.Name == "nodeBtn")
            {
                var n = fe != null ? fe.DataContext as Node : null;
                if (n == null || !n.IsDir) return;
                e.Handled = true;
                ToggleNode(n);
            }
            else if (b.Name == "nodeOpen")
            {
                if (b.Tag == null) return;
                e.Handled = true;
                OpenInExplorer(b.Tag.ToString());
            }
            else if (b.Name == "nodeDel")
            {
                var n = fe != null ? fe.DataContext as Node : null;
                if (n == null) return;
                e.Handled = true;
                DeleteNode(n);
            }
        }

        // ---- breakdown tree ------------------------------------------------

        int RowIndex(object o)
        {
            for (int i = 0; i < _rows.Count; i++) if (ReferenceEquals(_rows[i], o)) return i;
            return -1;
        }

        // children sit directly under their parent; collapsing drops everything
        // below it that is deeper than it
        void RemoveDescendants(int from, int deeperThan)
        {
            bool dropped = false;
            while (from + 1 < _rows.Count)
            {
                var n = _rows[from + 1] as Node;
                if (n == null || n.Depth <= deeperThan) break;
                // a row that is no longer on screen must not stay silently queued for
                // deletion - collapsing clears its tick and the totals follow
                if (n.Selected) { n.Selected = false; dropped = true; }
                _rows.RemoveAt(from + 1);
            }
            if (dropped) UpdateSelectionLine();
        }

        void ToggleTarget(TargetVm vm)
        {
            int at = RowIndex(vm);
            if (at < 0) return;
            if (vm.Expanded)
            {
                // Collapsing throws away the child ticks, so a row that was only
                // partly selected must not fall back to its own flag and read as
                // "everything" - that would silently re-include what you cleared.
                bool wasWhole = TargetState(vm) == true;
                RemoveDescendants(at, -1);
                if (!wasWhole) vm.Selected = false;
                vm.Expanded = false;
                vm.Raise("Chevron");
                OnNodeSelectionChanged();
                return;
            }
            vm.Expanded = true;
            vm.Raise("Chevron");
            LoadChildren(at, new List<string>(vm.T.Paths), 0);
        }

        void ToggleNode(Node n)
        {
            int at = RowIndex(n);
            if (at < 0) return;
            if (n.Expanded)
            {
                bool wasWhole = NodeState(n) == true;
                RemoveDescendants(at, n.Depth);
                if (!wasWhole) n.Selected = false;
                n.Expanded = false;
                n.Refresh();
                OnNodeSelectionChanged();
                return;
            }
            n.Expanded = true;
            n.Refresh();
            LoadChildren(at, new List<string> { n.FullPath }, n.Depth + 1);
        }

        // Rolls children's state up into the parent box. With nothing expanded a row
        // just reflects its own tick; once children are visible, a partial set shows
        // the dash so a parent never claims more than is really selected.
        static bool? Roll(bool self, int kids, int ticked)
        {
            if (kids == 0) return self;
            if (self && ticked == kids) return true;   // whole thing
            if (!self && ticked == 0) return false;    // nothing
            return null;                               // partial
        }

        bool? TargetState(TargetVm vm)
        {
            int at = RowIndex(vm), kids = 0, ticked = 0;
            for (int i = at + 1; at >= 0 && i < _rows.Count; i++)
            {
                var n = _rows[i] as Node;
                if (n == null) break;
                if (!n.Selectable) continue;
                kids++;
                if (n.Selected) ticked++;
            }
            return Roll(vm.Selected, kids, ticked);
        }

        bool? NodeState(Node node)
        {
            int at = RowIndex(node), kids = 0, ticked = 0;
            for (int i = at + 1; at >= 0 && i < _rows.Count; i++)
            {
                var n = _rows[i] as Node;
                if (n == null || n.Depth <= node.Depth) break;
                if (!n.Selectable) continue;
                kids++;
                if (n.Selected) ticked++;
            }
            return Roll(node.Selected, kids, ticked);
        }

        void RefreshCheckStates()
        {
            foreach (var r in _rows)
            {
                var v = r as TargetVm;
                if (v != null) { v.Raise("Checked"); continue; }
                var n = r as Node;
                if (n != null) n.Refresh();
            }
        }

        // Ticking a folder ticks everything already open beneath it, and untick clears
        // them again. _syncing stops the two directions echoing each other.
        void CascadeFromTarget(TargetVm vm, bool on)
        {
            if (_syncing) return;
            _syncing = true;
            int at = RowIndex(vm);
            for (int i = at + 1; at >= 0 && i < _rows.Count; i++)
            {
                var n = _rows[i] as Node;
                if (n == null) break;
                if (n.Selectable) n.Selected = on;
            }
            _syncing = false;
            OnNodeSelectionChanged();
        }

        void CascadeFromNode(Node node, bool on)
        {
            if (_syncing) return;
            _syncing = true;
            int at = RowIndex(node);
            for (int i = at + 1; at >= 0 && i < _rows.Count; i++)
            {
                var n = _rows[i] as Node;
                if (n == null || n.Depth <= node.Depth) break;
                if (n.Selectable) n.Selected = on;
            }
            _syncing = false;
            OnNodeSelectionChanged();
        }

        List<Node> SelectedNodes()
        {
            return _rows.OfType<Node>().Where(n => n.Selected && n.Selectable).ToList();
        }

        // A target or folder only "covers" what is under it when it is ticked whole.
        // Once you clear one child it becomes partial, and then only the entries you
        // left ticked count - and only those get deleted.
        List<TargetVm> WholeTargets()
        {
            return _vms.Where(v => v.Selected && TargetState(v) == true).ToList();
        }

        List<Node> EffectivePicked()
        {
            var picked = SelectedNodes();
            if (picked.Count == 0) return picked;

            var roots = new List<string>();
            foreach (var v in WholeTargets()) roots.AddRange(v.T.Paths);
            foreach (var n in picked) if (NodeState(n) == true) roots.Add(n.FullPath);

            var paths = picked.Select(n => n.FullPath).ToList();
            var freeIx = PathCoverage.Free(paths, roots);

            var free = new List<Node>();
            for (int i = 0; i < picked.Count; i++)
            {
                bool covered = !freeIx.Contains(i);
                if (picked[i].Covered != covered) { picked[i].Covered = covered; picked[i].Refresh(); }
                if (!covered) free.Add(picked[i]);
            }
            foreach (var n in _rows.OfType<Node>())
                if (!n.Selected && n.Covered) { n.Covered = false; n.Refresh(); }
            return free;
        }

        void OnNodeSelectionChanged()
        {
            UpdateSelectionLine();
            RefreshCheckStates();
        }

        // Hand-picked entries are removed outright - no keep-recent guard, because
        // you pointed at these exactly. Returns the paths that refused to go.
        async Task<List<string>> DeletePickedNodes(List<Node> sel)
        {
            if (sel.Count == 0) return new List<string>();

            // deepest first, so deleting a parent cannot invalidate a child path
            var paths = sel.OrderByDescending(n => n.Depth).Select(n => n.FullPath).ToList();
            var failed = await Task.Run(() =>
            {
                var bad = new List<string>();
                foreach (var p in paths)
                    if (!Disk.DeletePath(p, CancellationToken.None)) bad.Add(p);
                return bad;
            });

            foreach (var n in sel)
            {
                if (failed.Contains(n.FullPath)) { n.Selected = false; continue; }
                int at = RowIndex(n);
                if (at < 0) continue;
                RemoveDescendants(at, n.Depth);
                _rows.RemoveAt(at);
            }
            return failed;
        }

        // Deletes one entry the user picked out of the breakdown. This ignores the
        // keep-recent guard on purpose: you are pointing at this exact item.
        async void DeleteNode(Node n)
        {
            if (string.IsNullOrEmpty(n.FullPath)) return;

            string what = n.IsDir ? "folder" : "file";
            if (!Dialog.Confirm(this, "Delete this " + what + "?",
                    "This removes " + Disk.FormatSize(n.Bytes).Trim()
                    + (n.IsDir ? " and everything inside it." : ".")
                    + " The keep-recent setting does not apply to a single entry you picked.",
                    n.FullPath, "Delete " + Disk.FormatSize(n.Bytes).Trim(), true))
                return;

            string path = n.FullPath;
            long freed = n.Bytes;
            bool gone = await Task.Run(() => Disk.DeletePath(path, CancellationToken.None));

            if (!gone)
            {
                Dialog.Notice(this, "Could not delete", "Something is still holding this open.", path);
                return;
            }

            int at = RowIndex(n);
            if (at >= 0)
            {
                RemoveDescendants(at, n.Depth);
                _rows.RemoveAt(at);
            }
            F<TextBlock>("Status").Text = "Deleted " + Disk.FormatSize(freed).Trim() + "  ·  " + path;
            RemeasureOwner(at);
        }

        // after a single delete, re-measure the target that row belonged to
        async void RemeasureOwner(int near)
        {
            TargetVm owner = null;
            for (int i = Math.Min(near, _rows.Count - 1); i >= 0; i--)
            {
                owner = _rows[i] as TargetVm;
                if (owner != null) break;
            }
            if (owner == null) return;

            var paths = new List<string>(owner.T.Paths);
            var res = await Task.Run(() =>
            {
                long tot = 0; int files = 0;
                foreach (var p in paths)
                {
                    long bb; int ff;
                    Disk.Measure(p, out bb, out ff, CancellationToken.None);
                    tot += bb; files += ff;
                }
                return new long[] { tot, files };
            });
            owner.T.Bytes = res[0];
            owner.T.Files = (int)res[1];
            owner.RaiseAll();
            BuildDonut();
            UpdateSelectionLine();
            UpdateFree();
        }

        // Measuring a temp tree can take seconds, so it happens off the UI thread
        // behind a placeholder row.
        async void LoadChildren(int at, List<string> roots, int depth)
        {
            // opening under something already ticked should arrive ticked
            bool parentTicked = false;
            if (at >= 0 && at < _rows.Count)
            {
                var pv = _rows[at] as TargetVm;
                var pn = _rows[at] as Node;
                parentTicked = (pv != null && pv.Selected) || (pn != null && pn.Selected);
            }

            var busy = new Node { Name = "measuring…", Depth = depth, IsDir = false, Loading = true };
            _rows.Insert(at + 1, busy);

            if (_treeCts != null) _treeCts.Cancel();
            _treeCts = new CancellationTokenSource();
            var ct = _treeCts.Token;

            List<Node> kids = null;
            try { kids = await Task.Run(() => Breakdown.Children(roots, depth, ct), ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("breakdown failed: " + ex.Message); }

            int slot = RowIndex(busy);
            if (slot >= 0) _rows.RemoveAt(slot);
            if (kids == null || slot < 0) return;

            if (kids.Count == 0)
            {
                _rows.Insert(slot, new Node { Name = "(empty)", Depth = depth, IsDir = false });
                return;
            }
            for (int i = 0; i < kids.Count; i++)
            {
                kids[i].Changed = OnNodeSelectionChanged;
                kids[i].Cascade = CascadeFromNode;
                kids[i].TriState = NodeState;
                if (parentTicked) kids[i].Selected = true;   // opened under a ticked parent
                _rows.Insert(slot + i, kids[i]);
            }
            if (kids.Count >= Breakdown.MaxChildren)
                _rows.Insert(slot + kids.Count,
                    new Node { Name = "… only the largest " + Breakdown.MaxChildren + " shown", Depth = depth });
        }

        void OpenInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (System.IO.Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                else if (System.IO.File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                else
                    Dialog.Notice(this, "Folder is gone", "That location no longer exists.", path);
            }
            catch (Exception ex) { Dialog.Notice(this, "Could not open folder", ex.Message, path); }
        }

        void SetAll(bool on) { foreach (var v in _vms) if (!v.Locked) v.Selected = on; RefreshHeaders(); UpdateSelectionLine(); }

        bool? GroupState(string key)
        {
            var rows = _vms.Where(v => v.GroupKey == key && !v.Locked).ToList();
            if (rows.Count == 0) return false;
            if (rows.All(v => v.Selected)) return true;
            if (rows.Any(v => v.Selected)) return null;      // mixed
            return false;
        }

        // one click ticks or clears an entire section
        void ApplyGroup(string key, bool on)
        {
            if (_syncing) return;
            _syncing = true;
            foreach (var v in _vms.Where(x => x.GroupKey == key && !x.Locked)) v.Selected = on;
            _syncing = false;
            RefreshHeaders();
            UpdateSelectionLine();
        }

        void RefreshHeaders()
        {
            foreach (var v in _vms) if (v.ShowHeader) v.Raise("GroupSelected");
        }
        void SetSafe()
        {
            foreach (var v in _vms) v.Selected = !v.Locked && v.T.OnByDefault && v.T.Bytes > 0;
            RefreshHeaders();
            UpdateSelectionLine();
        }

        void Busy(bool on, string title)
        {
            _busy = on;
            F<Button>("ScanBtn").IsEnabled = !on;
            F<Button>("CleanBtn").IsEnabled = !on && (_vms.Any(v => v.Selected) || SelectedNodes().Count > 0);
            F<Button>("CancelBtn").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            F<Border>("BarBox").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            F<Border>("BusyOverlay").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                F<TextBlock>("BusyTitle").Text = title;
                F<TextBlock>("BusyEyebrow").Text = "WORKING";
                F<TextBlock>("BusyItem").Text = "starting…";
            }
            if (!on) SetProgress(0);
        }

        // one short fade after a scan so numbers arriving feel intentional, not janky
        void FadeIn()
        {
            foreach (var name in new[] { "HeroStats", "Legend" })
            {
                var el = _root.FindName(name) as UIElement;
                if (el == null) continue;
                el.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        void SetProgress(int pct)
        {
            F<Border>("BarFill").BeginAnimation(WidthProperty,
                new DoubleAnimation(96.0 * pct / 100.0, TimeSpan.FromMilliseconds(180)));

            // The overlay bar is sized by its track, not a constant, so the card can be
            // rewidened without the fill going out of step. ActualWidth is 0 until the
            // overlay has laid out once, hence the fallback to the designed width.
            var track = F<Border>("BusyTrack");
            double w = track.ActualWidth > 0 ? track.ActualWidth : 418.0;
            F<Border>("BusyFill").BeginAnimation(WidthProperty,
                new DoubleAnimation(w * pct / 100.0, TimeSpan.FromMilliseconds(180)));
            F<TextBlock>("BusyPct").Text = pct + "%";
        }

        // Both long jobs report the same three things, so they report them the same way.
        void Progress(string stage, string item, int pct)
        {
            SetProgress(pct);
            // The stage is which location is being worked on - the answer to "what is it
            // doing", so it takes the eyebrow rather than being buried in the footer.
            F<TextBlock>("BusyEyebrow").Text = string.IsNullOrEmpty(stage)
                ? "WORKING" : stage.ToUpperInvariant();
            F<TextBlock>("BusyItem").Text = item;
            F<TextBlock>("Status").Text = string.IsNullOrEmpty(stage)
                ? item : stage + " · " + item;
        }

        void Log(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            _notes.Add(line.Trim());
            Dispatcher.Invoke(new Action(() => F<TextBlock>("Status").Text = line.Trim()));
        }

        // -------------------------------------------------------------- scan ---

        async Task DoScan()
        {
            if (_busy) return;
            Busy(true, "Scanning your drive");
            F<Border>("SummaryCard").Visibility = Visibility.Collapsed;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            bool admin = _admin;
            try
            {
                await Task.Run(() => _eng.Scan(admin, p => Dispatcher.Invoke(new Action(() =>
                    Progress("Scanning", p.Item, p.Percent))), ct), ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("scan failed: " + ex.Message); }

            foreach (var v in _vms) { v.Selected = !v.Locked && v.T.OnByDefault && v.T.Bytes > 0; v.RaiseAll(); }
            bool anything = _vms.Any(v => v.T.Bytes > 0);
            F<TextBlock>("EmptyState").Visibility = anything ? Visibility.Collapsed : Visibility.Visible;
            F<ScrollViewer>("ListScroll").Visibility = anything ? Visibility.Visible : Visibility.Collapsed;
            BuildDonut();
            UpdateSelectionLine();
            UpdateFree();
            Busy(false, null);
            FadeIn();
            F<TextBlock>("HeroLabel").Text = "RECLAIMABLE";
            F<TextBlock>("Status").Text = _cts.IsCancellationRequested ? "Scan cancelled" : "";
        }

        // Donut drawn as stroked arcs - a stroke of thickness T on radius R is a ring
        // segment, which avoids building two-arc geometry per slice.
        void BuildDonut()
        {
            var canvas = F<Canvas>("Donut");
            var legend = F<StackPanel>("Legend");
            canvas.Children.Clear();
            legend.Children.Clear();

            var rows = _vms.Where(v => v.T.Bytes > 0).OrderByDescending(v => v.T.Bytes).ToList();
            long total = rows.Sum(v => v.T.Bytes);

            double cx = 58, cy = 58, r = 45, thick = 15;
            canvas.Children.Add(new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = B("Subtle"), StrokeThickness = thick, Fill = Brushes.Transparent,
                Margin = new Thickness(cx - r, cy - r, 0, 0)
            });
            if (total == 0)
            {
                legend.Children.Add(new TextBlock { Text = "Nothing found", FontSize = 11, Foreground = B("TextFaint") });
                return;
            }

            var shown = rows.Take(5).ToList();
            long rest = total - shown.Sum(v => v.T.Bytes);
            double angle = -90;
            int i = 0;
            foreach (var v in shown)
            {
                double sweep = 360.0 * v.T.Bytes / total;
                double share = 100.0 * v.T.Bytes / total;
                AddArc(canvas, cx, cy, r, thick, angle, sweep, B(Palette[i % Palette.Length]),
                       v.Name + "\n" + Disk.FormatSize(v.T.Bytes) + "  \u00b7  " + share.ToString("N1") + "%");
                legend.Children.Add(Layout.LegendRow(B(Palette[i % Palette.Length]), v.Name,
                    Disk.FormatSize(v.T.Bytes), share.ToString("N0") + "%",
                    v.Name + "\n" + Disk.FormatSize(v.T.Bytes) + "  \u00b7  " + share.ToString("N1") + "%"
                    + "\n" + v.T.Files.ToString("N0") + " files"));
                angle += sweep; i++;
            }
            if (rest > 0)
            {
                double restShare = 100.0 * rest / total;
                AddArc(canvas, cx, cy, r, thick, angle, 360.0 * rest / total, B("Chart8"),
                       "everything else\n" + Disk.FormatSize(rest) + "  \u00b7  " + restShare.ToString("N1") + "%");
                legend.Children.Add(Layout.LegendRow(B("Chart8"), "everything else",
                    Disk.FormatSize(rest), restShare.ToString("N0") + "%",
                    "everything else\n" + Disk.FormatSize(rest) + "  \u00b7  " + restShare.ToString("N1") + "%"));
            }

            string tot = Disk.FormatSize(total);
            int gap = tot.LastIndexOf(' ');
            var centre = new TextBlock
            {
                Text = gap > 0 ? tot.Substring(0, gap) : tot,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = B("Text"),
                TextAlignment = TextAlignment.Center,
                Width = 74,
                FontFamily = new FontFamily(Theme.CurrentPair.Mono)
            };
            centre.IsHitTestVisible = false;
            Canvas.SetLeft(centre, cx - 37); Canvas.SetTop(centre, cy - 18);
            canvas.Children.Add(centre);
            var unit = new TextBlock
            {
                Text = (gap > 0 ? tot.Substring(gap + 1) : "") + "  in " + rows.Count,
                FontSize = 9,
                Foreground = B("TextFaint"),
                TextAlignment = TextAlignment.Center,
                Width = 74
            };
            unit.IsHitTestVisible = false;
            Canvas.SetLeft(unit, cx - 37); Canvas.SetTop(unit, cy + 5);
            canvas.Children.Add(unit);
        }

        static void AddArc(Canvas c, double cx, double cy, double r, double thick,
                           double startDeg, double sweepDeg, Brush brush, string tip)
        {
            if (sweepDeg <= 0.35) return;
            double gap = Math.Min(2.5, sweepDeg / 4);
            sweepDeg -= gap;

            var p0 = Polar(cx, cy, r, startDeg);
            var p1 = Polar(cx, cy, r, startDeg + sweepDeg);
            var fig = new PathFigure { StartPoint = p0, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment(p1, new Size(r, r), 0, sweepDeg > 180,
                                            SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);

            var path = new Path
            {
                Data = geo,
                Stroke = brush,
                StrokeThickness = thick,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                ToolTip = tip,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ToolTipService.SetInitialShowDelay(path, 120);
            ToolTipService.SetShowDuration(path, 30000);
            ToolTipService.SetBetweenShowDelay(path, 0);
            // thicken the slice under the pointer so the tooltip has an obvious owner
            path.MouseEnter += delegate { path.StrokeThickness = thick + 3; };
            path.MouseLeave += delegate { path.StrokeThickness = thick; };
            c.Children.Add(path);
        }

        static Point Polar(double cx, double cy, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        // ------------------------------------------------------------- clean ---

        async Task DoClean()
        {
            if (_busy) return;
            int days;
            if (!int.TryParse(F<TextBox>("KeepDays").Text.Trim(), out days) || days < 0) days = 0;
            _eng.Cfg.MinAgeDays = days;
            _eng.Cfg.DryRun = F<CheckBox>("DryRun").IsChecked == true;
            _eng.Cfg.Unlock = F<CheckBox>("Unlock").IsChecked == true;

            var chosen = WholeTargets();        // partial targets are handled by their picks
            var picked = EffectivePicked();     // children of a whole target come with it
            if (!_eng.Cfg.DryRun)
            {
                var risky = chosen.Where(v => v.T.Group == Grp.Advanced).ToList();
                long picks = picked.Sum(n => n.Bytes);
                string msg = "This removes " + Disk.FormatSize(chosen.Sum(v => v.T.Bytes) + picks).Trim()
                           + " across " + chosen.Count + (chosen.Count == 1 ? " location" : " locations")
                           + (picked.Count == 0 ? "."
                              : " and " + picked.Count + (picked.Count == 1 ? " picked entry." : " picked entries."))
                           + (_eng.Cfg.MinAgeDays > 0
                              ? " Files touched in the last " + _eng.Cfg.MinAgeDays
                                + (_eng.Cfg.MinAgeDays == 1 ? " day are left alone." : " days are left alone.")
                              : "  Everything found will go, including files written moments ago.");
                string detail = risky.Count == 0 ? null
                    : "Includes entries with consequences:\n"
                      + string.Join("\n", risky.Select(v => "  \u2022  " + v.Name
                            + (string.IsNullOrEmpty(v.T.Note) ? "" : " \u2014 " + v.T.Note)).ToArray());

                // entries ticked in the breakdown are listed separately: they bypass the
                // keep-recent guard, so the prompt must say which ones they are
                if (picked.Count > 0)
                {
                    string byHand = "Picked by hand (keep-recent does not apply):\n"
                        + string.Join("\n", picked.Take(10).Select(n => "  \u2022  " + n.FullPath).ToArray())
                        + (picked.Count > 10 ? "\n  and " + (picked.Count - 10) + " more" : "");
                    detail = detail == null ? byHand : detail + "\n\n" + byHand;
                }

                if (!Dialog.Confirm(this, "Delete these files?", msg, detail,
                                    "Clean " + Disk.FormatSize(chosen.Sum(v => v.T.Bytes) + picks).Trim(), true))
                    return;
            }

            _notes.Clear();
            Busy(true, _eng.Cfg.DryRun ? "Dry run — nothing will be deleted" : "Cleaning");
            if (!_eng.Cfg.DryRun && picked.Count > 0)
            {
                var failedPicks = await DeletePickedNodes(picked);
                if (failedPicks.Count > 0)
                    Log(failedPicks.Count + " picked entries are still in use and were left alone");
            }
            F<Border>("SummaryCard").Visibility = Visibility.Collapsed;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            bool admin = _admin;
            try
            {
                await Task.Run(() => _eng.Clean(admin, this, p => Dispatcher.Invoke(new Action(() =>
                    Progress(p.Stage, p.Item, p.Percent))), ct), ct);
            }
            catch (OperationCanceledException) { Log("cancelled"); }
            catch (Exception ex) { Log("clean failed: " + ex.Message); }

            if (!_eng.Cfg.DryRun && _eng.Closed.Count > 0)
            {
                var pick = _eng.Cfg.RestartApps
                    ? _eng.Closed
                    : PickWindow.Pick(this, "Closed to free the files — relaunch which?",
                        _eng.Closed.Select(c => new PickItem
                        { Label = c.Name, Detail = c.Path, Tag = c, Checked = true }).ToList())
                        .Select(p => (ClosedApp)p.Tag).ToList();
                if (pick.Count > 0) _eng.RestartClosed(pick, _admin, this);
            }

            ShowSummary(chosen);
            foreach (var v in _vms) v.RaiseAll();
            BuildDonut();
            UpdateFree();
            UpdateSelectionLine();
            Busy(false, null);
        }

        void ShowSummary(List<TargetVm> run)
        {
            var body = F<StackPanel>("SummaryBody");
            body.Children.Clear();

            long freed = run.Sum(v => v.T.Freed), pending = run.Sum(v => v.T.Pending),
                 found = run.Sum(v => v.T.Bytes);
            long delta = _eng.CleanFreeAfter - _eng.CleanFreeBefore;

            F<TextBlock>("SumEyebrow").Text = _eng.Cfg.DryRun ? "DRY RUN" : "RESULT";
            F<TextBlock>("SumBig").Text = _eng.Cfg.DryRun
                ? Disk.FormatSize(found) + " would go"
                : Disk.FormatSize(freed) + " reclaimed";
            F<TextBlock>("SumSub").Text = "found " + Disk.FormatSize(found)
                + "   ·   still pending " + Disk.FormatSize(pending)
                + (_eng.Cfg.DryRun ? "" : "   ·   drive delta " + Disk.FormatSize(delta));

            foreach (var v in run.OrderByDescending(v => v.T.Bytes))
                body.Children.Add(Layout.SummaryRow(v.Name, Disk.FormatSize(v.T.Freed),
                    Disk.FormatSize(v.T.Pending), v.T.Status));

            foreach (var n in _notes.Distinct().Take(8))
                body.Children.Add(new TextBlock
                {
                    Text = n,
                    FontSize = 11,
                    Foreground = B("TextFaint"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 3, 0, 0)
                });

            F<Border>("SummaryCard").Visibility = Visibility.Visible;
        }

        // ----------------------------------------------------------- prompts ---

        public List<LockerInfo> ChooseAppsToClose(List<LockerInfo> closeable, List<LockerInfo> refused)
        {
            return Dispatcher.Invoke(new Func<List<LockerInfo>>(() =>
            {
                foreach (var r in refused) Log(r.Name + " (pid " + r.Id + ") — " + r.Reason + ", left alone");

                var groups = closeable.GroupBy(l => string.IsNullOrEmpty(l.Display) ? l.Name : l.Display)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
                var items = groups.Select(g => new PickItem
                {
                    Label = g.Key,
                    Detail = g.Count() + (g.Count() == 1 ? " process" : " processes") + "   pid " +
                             string.Join(", ", g.Take(3).Select(x => x.Id.ToString()).ToArray()) +
                             (g.Count() > 3 ? ", +" + (g.Count() - 3) + " more" : ""),
                    Tag = g.ToList(),
                    Checked = false               // nothing pre-ticked: you pick deliberately
                }).ToList();

                var picked = PickWindow.Pick(this, "Close which apps to free these files?", items,
                    "Nothing is ticked. Only what you tick gets closed.");
                var result = new List<LockerInfo>();
                foreach (var p in picked) result.AddRange((List<LockerInfo>)p.Tag);
                return result;
            })) as List<LockerInfo>;
        }

        public List<LockerInfo> ChooseAppsToForce(List<LockerInfo> stubborn)
        {
            return Dispatcher.Invoke(new Func<List<LockerInfo>>(() =>
            {
                var items = stubborn.Select(l => new PickItem
                { Label = l.Name, Detail = "pid " + l.Id, Tag = l, Checked = false }).ToList();
                var picked = PickWindow.Pick(this,
                    stubborn.Count + " app(s) ignored the close request",
                    items, "Forcing kills them outright. Unsaved work in them is lost.", true);
                return picked.Select(p => (LockerInfo)p.Tag).ToList();
            })) as List<LockerInfo>;
        }

        public void Report(string line) { Log(line); }

        // ---------------------------------------------------------- settings ---

        void LoadSettingsIntoUi()
        {
            _never.Clear();
            foreach (var n in _eng.Cfg.NeverCloseApp) _never.Add(new NeverCloseVm { Name = n });
            RefreshNeverEmpty();
            F<TextBox>("SetKeepDays").Text = _eng.Cfg.MinAgeDays.ToString();
            F<CheckBox>("SetUnlock").IsChecked = _eng.Cfg.Unlock;
            F<CheckBox>("SetForce").IsChecked = _eng.Cfg.ForceClose;
            F<CheckBox>("SetRestart").IsChecked = _eng.Cfg.RestartApps;
            F<CheckBox>("SetDryRun").IsChecked = _eng.Cfg.DryRun;
            F<TextBlock>("SetPath").Text = Settings.WhereStored;
            F<CheckBox>("SetPortable").IsChecked = Settings.IsPortable;
            var tm = Theme.Parse(_eng.Cfg.ThemeName);
            F<RadioButton>("ThemeSystem").IsChecked = tm == ThemeMode.System;
            F<RadioButton>("ThemeDark").IsChecked = tm == ThemeMode.Dark;
            F<RadioButton>("ThemeLight").IsChecked = tm == ThemeMode.Light;
            var fp = Theme.ParsePair(_eng.Cfg.FontPair);
            F<RadioButton>("FontDeveloper").IsChecked  = fp.Key == "developer";
            F<RadioButton>("FontTerminal").IsChecked   = fp.Key == "terminal";
            F<RadioButton>("FontIndustrial").IsChecked = fp.Key == "industrial";
            F<RadioButton>("FontSystem").IsChecked     = fp.Key == "system";

            F<TextBox>("KeepDays").Text = _eng.Cfg.MinAgeDays.ToString();
            F<CheckBox>("DryRun").IsChecked = _eng.Cfg.DryRun;
            F<CheckBox>("Unlock").IsChecked = _eng.Cfg.Unlock;
        }

        void RefreshNeverEmpty()
        {
            F<TextBlock>("NeverCloseEmpty").Visibility = _never.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void AddNeverClose()
        {
            var box = F<TextBox>("SetNeverClose");
            foreach (var raw in box.Text.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                if (_never.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                _never.Add(new NeverCloseVm { Name = name });
            }
            box.Clear();
            box.Focus();
            RefreshNeverEmpty();
        }

        void RemoveNeverClose(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            if (b == null || b.Tag == null) return;
            string name = b.Tag.ToString();
            var hit = _never.FirstOrDefault(x => x.Name == name);
            if (hit != null) _never.Remove(hit);
            RefreshNeverEmpty();
            e.Handled = true;
        }

        // Repaint anything drawn in code - XAML re-resolves its own DynamicResources.
        void SwitchTheme(ThemeMode m)
        {
            if (Theme.Current == m) return;
            Theme.Apply(m);
            _eng.Cfg.ThemeName = m.ToString().ToLowerInvariant();
            foreach (var v in _vms) v.Raise("SizeBrush");
            BuildDonut();
        }

        void SwitchFont(string key)
        {
            if (Theme.CurrentPair.Key == key) return;
            Theme.CurrentPair = Theme.ParsePair(key);
            Theme.Apply(Theme.Current);          // rebuild the dictionary with the new faces
            _eng.Cfg.FontPair = key;
        }

        void ShowSettings(bool on)
        {
            F<Border>("SettingsPane").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            F<Grid>("MainPane").Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            F<Button>("SettingsBtn").ToolTip = on ? "Back to cleaning" : "Settings";
        }

        void SaveSettings()
        {
            int d;
            _eng.Cfg.NeverCloseApp = _never.Select(x => x.Name).ToList();
            if (int.TryParse(F<TextBox>("SetKeepDays").Text.Trim(), out d) && d >= 0) _eng.Cfg.MinAgeDays = d;
            _eng.Cfg.Unlock = F<CheckBox>("SetUnlock").IsChecked == true;
            _eng.Cfg.ForceClose = F<CheckBox>("SetForce").IsChecked == true;
            _eng.Cfg.RestartApps = F<CheckBox>("SetRestart").IsChecked == true;
            _eng.Cfg.DryRun = F<CheckBox>("SetDryRun").IsChecked == true;
            _eng.Cfg.ThemeName = Theme.Current.ToString().ToLowerInvariant();
            _eng.Cfg.FontPair = Theme.CurrentPair.Key;
            try
            {
                _eng.Cfg.SetPortable(F<CheckBox>("SetPortable").IsChecked == true);
                _eng.Cfg.Save();
            }
            catch (Exception ex) { Dialog.Notice(this, "Could not save settings", ex.Message); return; }

            LoadSettingsIntoUi();
            UpdateSelectionLine();
            GoClean();
        }

        void RelaunchElevated()
        {
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule.FileName; }
            catch (Exception ex)
            {
                Dialog.Notice(this, "Run as administrator", "Could not locate the executable to restart.", ex.Message);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { Verb = "runas", UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // the user cancelled the UAC prompt - stay put and say so
                F<TextBlock>("Status").Text = "Elevation declined — still running with limited rights";
            }
            catch (Exception ex)
            {
                Dialog.Notice(this, "Run as administrator", "Could not restart with elevated rights.", ex.Message);
            }
        }
    }

    public static class Program
    {
        // winexe has no console of its own; borrow the one that launched us so
        // --selftest output is visible when run from a shell.
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                if (!AttachConsole(-1)) AllocConsole();
                int rc = SelfTest.Run();
                Console.Out.Flush();
                Environment.Exit(rc);
                return;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(
                (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Layout.AppStyles));
            try { app.Run(new MainWindow()); }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "Clear Windows Junk — startup failed"); }
        }
    }
}
