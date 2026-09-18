// Clear-WindowsJunk - theme tokens.
//
// Every colour and font in the XAML is a DynamicResource looked up from here, so
// switching theme at runtime restyles the whole window with no reload. (The WPF
// guidance to prefer StaticResource applies to values that never change; these do.)
//
// Type follows the ui-ux-pro-max "Mono + Sans" dashboard pairing, mapped onto fonts
// that ship with Windows 11: Cascadia Mono for data, Segoe UI Variable for chrome.
using System;
using System.Windows;
using System.Windows.Media;

namespace ClearWindowsJunk
{
    public enum ThemeMode { System, Dark, Light }

    public static class Theme
    {
        // Mono + Sans, per the ui-ux-pro-max dashboard pairing. Data always gets a
        // monospaced face so columns line up; the UI face is the user's call.
        public class FontPair
        {
            public string Key, Label, Ui, Mono, Display;
        }

        public static readonly FontPair[] Pairs =
        {
            new FontPair { Key = "developer", Label = "Developer",
                           Ui = "JetBrains Mono, Cascadia Mono, Consolas",
                           Mono = "JetBrains Mono, Cascadia Mono, Consolas",
                           Display = "JetBrains Mono, Cascadia Mono, Consolas" },
            new FontPair { Key = "terminal", Label = "Terminal",
                           Ui = "Cascadia Code, Cascadia Mono, Consolas",
                           Mono = "Cascadia Mono, Consolas",
                           Display = "Cascadia Code, Cascadia Mono, Consolas" },
            new FontPair { Key = "industrial", Label = "Industrial",
                           Ui = "Bahnschrift, Segoe UI",
                           Mono = "JetBrains Mono, Cascadia Mono, Consolas",
                           Display = "Bahnschrift, Segoe UI" },
            new FontPair { Key = "system", Label = "System",
                           Ui = "Segoe UI Variable Text, Segoe UI",
                           Mono = "Cascadia Mono, Consolas",
                           Display = "Segoe UI Variable Display, Segoe UI" },
        };

        public static FontPair CurrentPair = Pairs[0];

        public static FontPair ParsePair(string key)
        {
            foreach (var p in Pairs)
                if (p.Key.Equals((key ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return p;
            return Pairs[0];
        }

        public static ThemeMode Current = ThemeMode.System;
        static ResourceDictionary _dict;

        public static bool WindowsPrefersLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    return v != null && Convert.ToInt32(v) == 1;
                }
            }
            catch { return false; }
        }

        public static bool IsLight(ThemeMode m)
        {
            return m == ThemeMode.Light || (m == ThemeMode.System && WindowsPrefersLight());
        }

        public static ThemeMode Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return ThemeMode.System;
            switch (s.Trim().ToLowerInvariant())
            {
                case "light": return ThemeMode.Light;
                case "dark": return ThemeMode.Dark;
                default: return ThemeMode.System;
            }
        }

        static SolidColorBrush S(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        static LinearGradientBrush G(string a, string b, double x2, double y2)
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(x2, y2)
            };
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(a), 0));
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(b), 1));
            g.Freeze();
            return g;
        }

        public static Brush Get(string key)
        {
            if (_dict != null && _dict.Contains(key)) return (Brush)_dict[key];
            return Brushes.Gray;
        }

        public static void Apply(ThemeMode mode)
        {
            Current = mode;
            bool light = IsLight(mode);
            var d = new ResourceDictionary();

            if (light)
            {
                // Paper-neutral surface, one green accent. Text tuned for >=4.5:1 on white.
                d["WindowBg"] = G("#F7F8FA", "#EDF0F4", 0.6, 1);
                d["Surface"] = S("#FFFFFF");
                d["SurfaceAlt"] = S("#F3F5F8");
                d["Subtle"] = S("#EAEEF3");
                d["Hover"] = S("#EEF2F7");
                d["Line"] = S("#DCE2EA");
                d["LineSoft"] = S("#E8ECF1");
                d["Well"] = S("#FFFFFF");
                d["WindowBorder"] = S("#CBD3DD");

                d["Text"] = S("#0B1220");
                d["TextMuted"] = S("#5A6674");
                d["TextFaint"] = S("#8A95A3");

                d["Accent"] = S("#16A34A");
                d["AccentHi"] = S("#22C55E");
                d["OnAccent"] = S("#FFFFFF");
                d["AccentGrad"] = G("#22C55E", "#15803D", 1, 1);
                d["AccentGlow"] = (Color)ColorConverter.ConvertFromString("#16A34A");

                d["Warn"] = S("#B45309");
                d["Danger"] = S("#DC2626");
                d["HeroBg"] = G("#FFFFFF", "#F1F5F9", 1, 1);
                d["SummaryBg"] = S("#F6FBF7");
                d["SummaryLine"] = S("#BBE7C8");
                d["TrackBg"] = S("#E3E8EF");

                d["Chart1"] = S("#16A34A"); d["Chart2"] = S("#0284C7"); d["Chart3"] = S("#7C3AED");
                d["Chart4"] = S("#D97706"); d["Chart5"] = S("#DB2777"); d["Chart6"] = S("#0D9488");
                d["Chart7"] = S("#E11D48"); d["Chart8"] = S("#64748B");
                d["ZeroText"] = S("#A6B0BD");
                d["Mixed"] = S("#0284C7");
                d["MixedHi"] = S("#38BDF8");
                d["ChipBg"] = S("#E7F6EC");
                d["ChipLine"] = S("#BBE3C8");
            }
            else
            {
                // Deep slate, not black: keeps card edges readable without hard borders.
                d["WindowBg"] = G("#0B1120", "#0F172A", 0.6, 1);
                d["Surface"] = S("#151D2E");
                d["SurfaceAlt"] = S("#1B2336");
                d["Subtle"] = S("#222C42");
                d["Hover"] = S("#202940");
                d["Line"] = S("#26324A");
                d["LineSoft"] = S("#1D2639");
                d["Well"] = S("#0D1425");
                d["WindowBorder"] = S("#28344C");

                d["Text"] = S("#F1F5F9");
                d["TextMuted"] = S("#94A3B8");
                d["TextFaint"] = S("#64748B");

                d["Accent"] = S("#22C55E");
                d["AccentHi"] = S("#4ADE80");
                d["OnAccent"] = S("#05140B");
                d["AccentGrad"] = G("#22C55E", "#15803D", 1, 1);
                d["AccentGlow"] = (Color)ColorConverter.ConvertFromString("#22C55E");

                d["Warn"] = S("#F59E0B");
                d["Danger"] = S("#EF4444");
                d["HeroBg"] = G("#1A2438", "#131B2C", 1, 1);
                d["SummaryBg"] = S("#0F1B18");
                d["SummaryLine"] = S("#1F5133");
                d["TrackBg"] = S("#222C42");

                d["Chart1"] = S("#22C55E"); d["Chart2"] = S("#38BDF8"); d["Chart3"] = S("#A78BFA");
                d["Chart4"] = S("#F59E0B"); d["Chart5"] = S("#F472B6"); d["Chart6"] = S("#2DD4BF");
                d["Chart7"] = S("#FB7185"); d["Chart8"] = S("#64748B");
                d["ZeroText"] = S("#4B5872");
                d["Mixed"] = S("#38BDF8");
                d["MixedHi"] = S("#7DD3FC");
                d["ChipBg"] = S("#1B3327");
                d["ChipLine"] = S("#2F6B47");
            }

            d["FontDisplay"] = new FontFamily(CurrentPair.Display);
            d["FontUi"] = new FontFamily(CurrentPair.Ui);
            d["FontMono"] = new FontFamily(CurrentPair.Mono);

            var app = Application.Current;
            if (app == null) { _dict = d; return; }
            if (_dict != null) app.Resources.MergedDictionaries.Remove(_dict);
            app.Resources.MergedDictionaries.Add(d);
            _dict = d;
        }
    }
}
