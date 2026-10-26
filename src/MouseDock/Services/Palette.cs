using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;

namespace MouseDock.Services
{
    /// <summary>从背景图里提主色，并按主色生成一整套主题（视频里的「配色自动跟随」）。</summary>
    public static class Palette
    {
        public static BitmapSource Load(string path, int decodePixelWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>主色抽取：忽略近白/近黑像素，按饱和度加权，最后做一次饱和度提升。</summary>
        public static bool TryExtractAccent(string imagePath, out Color accent, out double luma)
        {
            accent = Colors.Gray;
            luma = 0.5;
            var src = Load(imagePath, 48);
            if (src == null) return false;

            var f = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            f.Freeze();
            int w = f.PixelWidth, h = f.PixelHeight;
            if (w <= 0 || h <= 0) return false;

            var buf = new byte[w * h * 4];
            f.CopyPixels(buf, w * 4, 0);

            double sumR = 0, sumG = 0, sumB = 0, sumW = 0, sumLuma = 0;
            for (int i = 0; i < buf.Length; i += 4)
            {
                byte b = buf[i], g = buf[i + 1], r = buf[i + 2];
                double l = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                sumLuma += l;
                if (l > 0.94 || l < 0.06) continue;                 // 去掉纸白与死黑
                double mx = Math.Max(r, Math.Max(g, b)) / 255.0;
                double mn = Math.Min(r, Math.Min(g, b)) / 255.0;
                double sat = mx <= 0 ? 0 : (mx - mn) / mx;
                double weight = 0.25 + sat * 2.0;                    // 有色区域权重更高
                sumR += r * weight; sumG += g * weight; sumB += b * weight; sumW += weight;
            }

            int count = Math.Max(1, w * h);
            luma = sumLuma / count;
            if (sumW <= 0.001)
            {
                // 纯灰图：视频里说明「黑白图 → 中性灰」
                accent = Color.FromRgb(0x9A, 0x9A, 0xA6);
                return true;
            }

            var baseColor = Color.FromRgb(
                (byte)Math.Clamp(sumR / sumW, 0, 255),
                (byte)Math.Clamp(sumG / sumW, 0, 255),
                (byte)Math.Clamp(sumB / sumW, 0, 255));

            accent = Boost(baseColor, 0.55, 0.62);
            return true;
        }

        /// <summary>提高饱和度并给一个固定亮度，保证强调色在小尺寸下可读。</summary>
        public static Color Boost(Color c, double saturationBoost, double lightness)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            double l = (mx + mn) / 2.0;
            double d = mx - mn;
            double s = d == 0 ? 0 : (l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn));
            double h = 0;
            if (d > 0)
            {
                if (mx == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
                else if (mx == g) h = ((b - r) / d + 2) / 6.0;
                else h = ((r - g) / d + 4) / 6.0;
            }
            s = Math.Clamp(s * (1 + saturationBoost) + 0.18, 0.15, 0.92);
            l = Math.Clamp(lightness, 0.35, 0.78);
            return FromHsl(h, s, l);
        }

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

        /// <summary>按主色推导整套主题（自定义背景走这条路）。</summary>
        public static ThemePreset FromAccent(Color accent, bool lightSurface)
        {
            if (lightSurface)
            {
                return new ThemePreset
                {
                    Id = "custom",
                    Name = "自定义",
                    Accent = accent,
                    Accent2 = Mix(accent, Colors.White, 0.35),
                    BgTop = Mix(Colors.White, accent, 0.06),
                    BgBottom = Mix(Color.FromRgb(0xED, 0xEF, 0xF4), accent, 0.10),
                    Surface = Colors.White,
                    Text = Color.FromRgb(0x1E, 0x22, 0x30),
                    TextDim = Color.FromRgb(0x78, 0x80, 0x90),
                    IsLight = true
                };
            }

            var deep = Mix(Color.FromRgb(0x12, 0x10, 0x1B), accent, 0.16);
            return new ThemePreset
            {
                Id = "custom",
                Name = "自定义",
                Accent = accent,
                Accent2 = Mix(accent, Colors.White, 0.30),
                BgTop = deep,
                BgBottom = Mix(deep, Colors.Black, 0.45),
                Surface = Mix(deep, Colors.White, 0.06),
                Text = Color.FromRgb(0xEC, 0xE8, 0xF6),
                TextDim = Mix(Color.FromRgb(0x9A, 0x92, 0xB8), accent, 0.25),
                IsLight = false
            };
        }

        /// <summary>根据设置解析当前主题：有自定义背景就跟随它，否则用预设。</summary>
        public static ThemePreset ResolveTheme(AppSettings settings, out Color? extracted)
        {
            extracted = null;
            if (!string.IsNullOrWhiteSpace(settings.CustomBackground) && File.Exists(settings.CustomBackground))
            {
                if (TryExtractAccent(settings.CustomBackground, out var accent, out var luma))
                {
                    extracted = accent;
                    bool lightSurface = luma > 0.55;
                    var theme = FromAccent(accent, lightSurface);
                    if (!string.IsNullOrWhiteSpace(settings.CustomAccent) &&
                        TryParseColor(settings.CustomAccent, out var manual))
                    {
                        theme.Accent = manual;
                        theme.Accent2 = Mix(manual, Colors.White, 0.30);
                    }
                    return theme;
                }
            }
            return ThemePreset.ById(settings.ThemeId);
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
