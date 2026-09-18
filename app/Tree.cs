// Clear-WindowsJunk - expandable breakdown rows.
//
// A target row can expand to show what is actually inside it, folder by folder,
// each with its own measured size. Children expand recursively. Sizes are measured
// off the UI thread because a single temp folder can hold six figures of files.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClearWindowsJunk
{
    public class Node : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDir { get; set; }
        public int Depth { get; set; }
        public long Bytes;
        public int Files;

        public bool Expanded;
        public bool Loaded;
        public bool Loading;
        public bool Covered;                 // already inside something else that is ticked
        public Action<Node, bool> Cascade;   // ticking a folder ticks what is under it
        public Func<Node, bool?> TriState;   // window decides all / none / mixed

        // What the box draws: null is the dash, meaning only part of this is ticked.
        public bool? Checked
        {
            get { return TriState == null ? (bool?)Selected : TriState(this); }
            set { Selected = (value == true); }
        }

        // a placeholder row ("measuring…", "(empty)") must not be tickable
        public bool Selectable { get { return !Loading && !string.IsNullOrEmpty(FullPath); } }
        public Visibility BoxShown { get { return Selectable ? Visibility.Visible : Visibility.Hidden; } }

        public Action Changed;
        bool _selected;
        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Selected"));
                if (Cascade != null) Cascade(this, value);
                if (Changed != null) Changed();
            }
        }

        // rows are a flat list; the indent is what makes it read as a tree
        public Thickness Indent { get { return new Thickness(14 + Depth * 17, 0, 0, 0); } }

        public string SizeText { get { return Loading ? "…" : Disk.FormatSize(Bytes); } }
        public string FilesText
        {
            get
            {
                if (Loading) return "measuring";
                if (Covered && Selected) return "counted with parent";
                if (!IsDir) return "file";
                return Files.ToString("N0") + (Files == 1 ? " file" : " files");
            }
        }

        public string Chevron { get { return Expanded ? "" : ""; } }   // down / right
        public Visibility ChevronShown { get { return IsDir ? Visibility.Visible : Visibility.Hidden; } }

        public Brush Tint
        {
            get
            {
                if (Covered && Selected) return Theme.Get("ZeroText");
                return Theme.Get(Bytes == 0 ? "ZeroText" : Bytes > 1073741824L ? "Accent" : "Text");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void Refresh()
        {
            if (PropertyChanged == null) return;
            foreach (var p in new[] { "SizeText", "FilesText", "Chevron", "Tint", "Indent", "Selected", "BoxShown", "Covered", "Checked" })
                PropertyChanged(this, new PropertyChangedEventArgs(p));
        }
    }

    // Which hand-picked entries still count on their own.
    //
    // A ticked child of a ticked target is already inside that target's total, and a
    // ticked child of a ticked folder is inside that folder's. Counting either again
    // is what made the headline figure balloon. Pure and static so SelfTest can prove
    // it without a window.
    public static class PathCoverage
    {
        // strictly inside: a path never covers itself
        public static bool Inside(string child, string parent)
        {
            return Under(child, parent) && !Under(parent, child);
        }

        public static bool Under(string child, string parent)
        {
            if (string.IsNullOrEmpty(child) || string.IsNullOrEmpty(parent)) return false;
            string c = child.TrimEnd('\\'), p = parent.TrimEnd('\\');
            if (c.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
            return c.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // returns the indexes of picked[] that no ticked ancestor already covers
        public static List<int> Free(IList<string> picked, IList<string> coveringRoots)
        {
            var free = new List<int>();
            for (int i = 0; i < picked.Count; i++)
            {
                // A root only covers what is strictly beneath it, so an entry that is
                // itself a root still counts for itself.
                bool covered = false;
                for (int r = 0; r < coveringRoots.Count && !covered; r++)
                    covered = Inside(picked[i], coveringRoots[r]);

                if (!covered) free.Add(i);
            }
            return free;
        }
    }

    // Picks the target-row template or the breakdown-row template per item.
    public class RowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TargetRow, NodeRow;
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item is Node ? NodeRow : TargetRow;
        }
    }

    public static class Breakdown
    {
        public const int MaxChildren = 300;

        // Immediate children of the given roots, each measured, biggest first.
        // Reparse points are listed but never followed - same rule the cleaner uses.
        public static List<Node> Children(IEnumerable<string> roots, int depth, CancellationToken ct)
        {
            var list = new List<Node>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                FileSystemInfo[] kids;
                try { kids = new DirectoryInfo(root).GetFileSystemInfos(); }
                catch { continue; }

                foreach (var k in kids)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        bool link = (k.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                        var dir = k as DirectoryInfo;
                        var n = new Node
                        {
                            Name = k.Name + (link ? "  (link, not followed)" : ""),
                            FullPath = k.FullName,
                            IsDir = dir != null && !link,
                            Depth = depth
                        };
                        if (n.IsDir)
                        {
                            long b; int f;
                            Disk.Measure(k.FullName, out b, out f, ct);
                            n.Bytes = b; n.Files = f;
                        }
                        else if (dir == null)
                        {
                            n.Bytes = ((FileInfo)k).Length;
                            n.Files = 1;
                        }
                        list.Add(n);
                    }
                    catch { }
                }
            }
            return list.OrderByDescending(n => n.Bytes).ThenBy(n => n.Name).Take(MaxChildren).ToList();
        }
    }
}
