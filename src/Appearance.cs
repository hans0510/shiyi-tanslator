using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Shiyi
{
    internal sealed class Appearance : IDisposable
    {
        private readonly Application app;
        private ThemePreset preset;
        private string mode, custom;
        private bool disposed;
        public bool Dark { get; private set; }
        public event Action Changed;

        public Appearance(Application app)
        {
            this.app = app;
            SystemEvents.UserPreferenceChanged += SystemChanged;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        public void StyleTitleBar(Window window)
        {
            Action update = delegate {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero || window.WindowStyle == WindowStyle.None) return;
                int dark = Dark ? 1 : 0;
                // Windows 10 before 20H1 uses attribute 19 for the dark title bar.
                if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
            };
            window.SourceInitialized += delegate { update(); };
            Changed += update;
            window.Closed += delegate { Changed -= update; };
        }

        public void Apply(Preferences prefs)
        {
            var next = ThemePreset.Find(prefs.Theme);
            string nextMode = ThemePreset.Mode(prefs.AppearanceMode), nextCustom = ThemePreset.Hex(prefs.AccentColor);
            bool dark = nextMode == "dark" || (nextMode == "system" && SystemIsDark());
            if (next == preset && mode == nextMode && custom == nextCustom && Dark == dark) return;
            preset = next; mode = nextMode; custom = nextCustom; Dark = dark;
            Paint();
        }

        private static bool SystemIsDark()
        {
            try { return Equals(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), 0); }
            catch (Exception) { return false; }
        }

        private void SystemChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.BeginInvoke(new Action(delegate {
                if (disposed || mode != "system") return;
                bool next = SystemIsDark();
                if (next != Dark) { Dark = next; Paint(); }
            }));
        }

        internal static Color ColorOf(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        internal static SolidColorBrush Brush(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }

        private static Color Mix(Color a, Color b, double w)
        {
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * w), (byte)Math.Round(a.G + (b.G - a.G) * w), (byte)Math.Round(a.B + (b.B - a.B) * w));
        }
        private static double Linear(byte c)
        {
            double x = c / 255.0;
            return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
        }
        private static double Luminance(Color c) { return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B); }
        private static double Contrast(Color a, Color b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
        }

        private void Paint()
        {
            var p = Dark ? preset.Dark : preset.Light;
            var surface = ColorOf(p.Surface); var ink = ColorOf(p.Text); var line = ColorOf(p.Border);
            var accent = ColorOf(custom.Length == 0 ? p.Accent : custom);
            var tint = Mix(surface, accent, Dark ? .19 : .10);
            var link = accent;
            for (int n = 1; n <= 20 && (Contrast(link, surface) < 4.5 || Contrast(link, tint) < 4.5); n++)
                link = Mix(accent, ink, n / 20.0);
            var onAccent = Contrast(accent, Colors.White) >= Contrast(accent, Colors.Black) ? Colors.White : Colors.Black;
            var values = app.Resources;
            values["Paper"] = Brush(ColorOf(p.Canvas)); values["Surface"] = Brush(surface);
            values["Ink"] = Brush(ink); values["Muted"] = Brush(ColorOf(p.Muted)); values["Line"] = Brush(line);
            values["ControlLine"] = Brush(Mix(line, ink, Dark ? .24 : .17));
            values["Accent"] = Brush(accent); values["OnAccent"] = Brush(onAccent); values["AccentText"] = Brush(link);
            values["Tint"] = Brush(tint); values["Soft"] = Brush(Mix(surface, ink, Dark ? .065 : .033));
            values["Danger"] = Brush(ColorOf(Dark ? "#FF989B" : "#A33132"));
            values[SystemColors.WindowBrushKey] = values["Surface"]; values[SystemColors.WindowTextBrushKey] = values["Ink"];
            values[SystemColors.ControlBrushKey] = values["Soft"]; values[SystemColors.ControlTextBrushKey] = values["Ink"];
            values[SystemColors.HighlightBrushKey] = values["Accent"]; values[SystemColors.HighlightTextBrushKey] = values["OnAccent"];
            values[SystemColors.GrayTextBrushKey] = values["Muted"];
            if (Changed != null) Changed();
        }

        public void Dispose() { disposed = true; SystemEvents.UserPreferenceChanged -= SystemChanged; }
    }
}
