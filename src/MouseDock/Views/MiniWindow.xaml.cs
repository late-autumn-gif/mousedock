using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views
{
    public partial class MiniWindow : Window
    {
        public int LowBatteryThreshold => App.Settings?.LowBatteryWarn ?? 20;
        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(MiniWindow), new PropertyMetadata(Brushes.Gainsboro));

        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

        public static readonly DependencyProperty BatteryBrushProperty = DependencyProperty.Register(
            nameof(BatteryBrush), typeof(Brush), typeof(MiniWindow), new PropertyMetadata(Brushes.White));

        public Brush BatteryBrush { get => (Brush)GetValue(BatteryBrushProperty); private set => SetValue(BatteryBrushProperty, value); }

        private readonly List<DeviceInfo> _visible = new List<DeviceInfo>();
        private readonly Brush _themeBackground;

        public MiniWindow()
        {
            InitializeComponent();
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2 || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                App.ExpandDock();
                e.Handled = true;
            };
            _themeBackground = Root.Background;
            CellList.ItemsSource = _visible;
            App.Battery.Refreshed += (_, __) => Rebuild();
            App.Battery.SourceChanged += (_, __) => Rebuild();
            Rebuild();
            Loaded += (_, __) =>
            {
                Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            };
        }

        /// <summary>迷你视图只显示被勾选的设备，最多 6 台。</summary>
        public void Rebuild()
        {
            var selected = App.Battery.VisibleDevices.ToList();

            if (selected.Count == _visible.Count && selected.SequenceEqual(_visible)) return;

            _visible.Clear();
            foreach (var d in selected) _visible.Add(d);

            CellList.ItemsSource = null;
            CellList.ItemsSource = _visible;

            if (IsVisible) Pulse();
        }

        private void Pulse()
        {
            var anim = new DoubleAnimation(1, 0.72, TimeSpan.FromMilliseconds(120))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Root.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>设置窗口背景图；空路径回到预设外观。</summary>
        public void ApplyBackground(string path) => PaintBackground(path);

        internal void RefreshBackground()
        {
            var settings = App.Settings;
            bool follow = settings.FollowsMiniThemeBackground;
            bool solid = TryMiniColor("background", out _);
            PaintBackground(follow || solid ? "" : settings.MiniBackground);
        }

        private void PaintBackground(string path)
        {
            ApplyTheme(App.Theme, false);
            var bmp = PictureBackground.Apply(Root, path, _themeBackground, App.Settings, App.Theme, DecodeWidthForWindow());
            if (bmp != null) ApplyContrastFor(AverageBrightness(bmp));
        }

        /// <summary>按窗口实际像素宽解码，避免原图在解码阶段就被压掉细节。</summary>
        private int DecodeWidthForWindow()
        {
            double width = Root.ActualWidth > 1 ? Root.ActualWidth : 200;
            double scale = 1;
            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null) scale = source.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            return Math.Max(64, (int)Math.Ceiling(width * Math.Max(scale, 1) * 2));
        }

        /// <summary>缩到 24×24 采样平均亮度，0 全黑、1 全白。</summary>
        private static double AverageBrightness(System.Windows.Media.Imaging.BitmapSource bmp)
        {
            try
            {
                var gray = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
                int w = Math.Min(24, gray.PixelWidth), h = Math.Min(24, gray.PixelHeight);
                if (w <= 0 || h <= 0) return .5;
                var scaled = new System.Windows.Media.Imaging.TransformedBitmap(gray,
                    new ScaleTransform((double)w / gray.PixelWidth, (double)h / gray.PixelHeight));
                var pixels = new byte[w * h];
                scaled.CopyPixels(pixels, w, 0);
                long sum = 0;
                foreach (byte value in pixels) sum += value;
                return sum / (double)(pixels.Length * 255);
            }
            catch { return .5; }
        }

        /// <summary>按背景亮度挑文字色：亮底用深色，暗底用浅色。</summary>
        private void ApplyContrastFor(double brightness)
        {
            bool light = brightness > 0.55;
            var text = light ? Color.FromRgb(0x1A, 0x1A, 0x22) : Color.FromRgb(0xF5, 0xF7, 0xFA);
            ApplyForeground(text, text, light);
        }

        private static bool TryMiniColor(string key, out Color color)
        {
            color = default;
            return App.Settings.MiniColors?.TryGetValue(key, out string value) == true &&
                AppearanceColors.TryColor(value, out color);
        }

        private void ApplyForeground(Color text, Color glyph, bool light)
        {
            if (TryMiniColor("text", out var customText)) text = glyph = customText;
            Foreground = new SolidColorBrush(text);
            GlyphBrush = new SolidColorBrush(glyph);
            BatteryBrush = TryMiniColor("accent", out var accent) ? new SolidColorBrush(accent) : Foreground;
            RootShadow.Color = light ? Color.FromRgb(0x8B, 0x93, 0xA6) : Colors.Black;
            string style = App.Settings.MiniStyle?.ToLowerInvariant();
            RootShadow.Opacity = style == "bare" ? 0 : light ? 0.16 : style == "compact" ? 0.32 : 0.42;
        }

        /// <summary>迷你窗样式：standard 标准 / compact 紧凑 / bare 极简。</summary>
        public void ApplyDensity(string style)
        {
            style = (style ?? "standard").ToLowerInvariant();
            switch (style)
            {
                case "compact":
                    Root.Padding = new Thickness(8, 6, 8, 6);
                    Root.CornerRadius = new CornerRadius(12);
                    RootShadow.Opacity = 0.32;
                    break;
                case "bare":
                    Root.Padding = new Thickness(9, 7, 9, 7);
                    Root.CornerRadius = new CornerRadius(0);
                    RootShadow.Opacity = 0;
                    break;
                default:
                    Root.Padding = new Thickness(12, 10, 12, 10);
                    Root.CornerRadius = new CornerRadius(18);
                    RootShadow.Opacity = 0.42;
                    break;
            }
            RefreshBackground();
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var top = theme.BgTop;
            var bottom = theme.BgBottom;
            bool light = theme.IsLight;
            var text = theme.Text;
            if (!App.Settings.FollowsMiniThemeBackground && TryMiniColor("background", out var background))
            {
                top = bottom = background;
                light = ColorContrast.Ratio(Colors.Black, background) > ColorContrast.Ratio(Colors.White, background);
                text = ColorContrast.Legible(text, [background]);
            }
            ApplyForeground(text, Palette.Mix(text, theme.Accent, 0.35), light);

            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(240);
                BgTopStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(top, dur));
                BgBottomStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(bottom, dur));
            }
            else
            {
                BgTopStop.BeginAnimation(GradientStop.ColorProperty, null);
                BgBottomStop.BeginAnimation(GradientStop.ColorProperty, null);
                BgTopStop.Color = top;
                BgBottomStop.Color = bottom;
            }
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
            if (ReferenceEquals(App.Mini, this)) App.SaveWindowPositions();
        }

        private void OnContextMenu(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            App.ShowQuickMenu(this);
        }
    }
}



