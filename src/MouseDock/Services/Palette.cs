using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;

namespace MouseDock.Services
{
    /// <summary>解析主题预设并提供颜色混合与转换。</summary>
    public static class Palette
    {
        public static BitmapSource Load(string path, int decodePixelWidth = 0)
            => ImageImport.Load(path, decodePixelWidth);

        public static Color FromHsl(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0) { r = g = b = l; }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb(
                (byte)Math.Round(Math.Clamp(r, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(g, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(b, 0, 1) * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        /// <summary>根据预设及自定义颜色解析主题。</summary>
        public static ThemePreset ResolveTheme(AppSettings settings)
        {
            var preset = ReferenceThemes.Resolve(settings.ThemeId, "") ?? ThemePreset.ById(settings.ThemeId);
            var theme = AppearanceColors.Apply(preset, settings.ThemeColors);
            if (AppearanceColors.TryColor(AppearanceColors.LowBatteryOverride(settings), out var low))
                theme.Low = low;
            return theme;
        }

        public static bool TryParseColor(string hex, out Color color)
        {
            color = Colors.Gray;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex.StartsWith("#") ? hex : "#" + hex);
                color = c;
                return true;
            }
            catch { return false; }
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
