using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;

namespace MouseDock.Services
{
    public static class PictureBackground
    {
        private sealed class State
        {
            public string Path = "";
            public DateTime Modified;
            public BitmapSource Image;
            public int DecodeWidth;
            public string OverridePath;
            public Brush Fallback;
            public AppSettings Settings;
            public ThemePreset Theme;
        }

        private static readonly ConditionalWeakTable<Border, State> States = new();

        public static double Clamp(double value, double min, double max, double fallback) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

        public static Rect Crop(Size source, Size target, double zoom, double x, double y)
        {
            if (source.IsEmpty || target.IsEmpty || source.Width <= 0 || source.Height <= 0 ||
                target.Width <= 0 || target.Height <= 0 ||
                !double.IsFinite(source.Width + source.Height + target.Width + target.Height))
                return new Rect(0, 0, 1, 1);
            zoom = Clamp(zoom, 1, 3, 1);
            double scale = Math.Max(target.Width / source.Width, target.Height / source.Height) * zoom;
            double width = Math.Min(1, target.Width / (source.Width * scale));
            double height = Math.Min(1, target.Height / (source.Height * scale));
            return new Rect((1 - width) * Clamp(x, 0, 1, .5), (1 - height) * Clamp(y, 0, 1, .5), width, height);
        }

        public static Brush Create(BitmapSource image, Size target, AppSettings settings, ThemePreset theme)
        {
            var crop = Crop(new Size(image.PixelWidth, image.PixelHeight), target,
                settings.BackgroundZoom, settings.BackgroundPositionX, settings.BackgroundPositionY);
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                var bounds = new Rect(0, 0, 1, 1);
                dc.DrawRectangle(new SolidColorBrush(theme.BgTop), null, bounds);
                dc.DrawRectangle(new ImageBrush(image)
                {
                    Viewbox = crop, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = Stretch.Fill
                }, null, bounds);
                dc.DrawRectangle(new SolidColorBrush(Palette.WithAlpha(theme.BgTop,
                    (byte)Math.Round(255 * Clamp(settings.BackgroundDepth, 0, .8, .35)))), null, bounds);
            }
            var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }

        public static BitmapSource Apply(Border root, string overridePath, Brush fallback, AppSettings settings, ThemePreset theme,
            int decodeWidth = 2048)
        {
            if (!States.TryGetValue(root, out var state))
            {
                state = new State();
                States.Add(root, state);
                root.SizeChanged += (_, __) => Paint(root, state);
            }
            state.OverridePath = overridePath;
            state.Fallback = fallback;
            state.Settings = settings;
            state.Theme = theme;
            string path = !string.IsNullOrWhiteSpace(overridePath) ? overridePath : settings.CustomBackground;
            DateTime modified = DateTime.MinValue;
            try { modified = File.GetLastWriteTimeUtc(path ?? ""); } catch { }
            if (state.Path != path || state.Modified != modified || state.DecodeWidth != decodeWidth)
            {
                state.Path = path;
                state.Modified = modified;
                state.DecodeWidth = decodeWidth;
                state.Image = Palette.Load(path, decodeWidth);
            }
            Paint(root, state);
            return state.Image;
        }

        private static void Paint(Border root, State state)
        {
            if (state.Image == null) { root.Background = state.Fallback; return; }
            RenderOptions.SetBitmapScalingMode(root, BitmapScalingMode.HighQuality);
            // Existing per-window pictures retain their original untinted aspect-fill rendering.
            root.Background = !string.IsNullOrWhiteSpace(state.OverridePath)
                ? new ImageBrush(state.Image) { Stretch = Stretch.UniformToFill }
                : Create(state.Image, new Size(Math.Max(1, root.ActualWidth), Math.Max(1, root.ActualHeight)),
                    state.Settings, state.Theme);
        }

        public static string Import(string path, string directory = null)
        {
            directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock", "backgrounds");
            return ImageImport.Import(path, directory, false, App.Theme?.BgTop);
        }
    }
}
