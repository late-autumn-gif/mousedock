using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views
{
    public partial class MiniWindow : Window
    {
        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(MiniWindow), new PropertyMetadata(Brushes.Gainsboro));

        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

        private readonly List<DeviceInfo> _visible = new List<DeviceInfo>();

        public MiniWindow()
        {
            InitializeComponent();
            CellList.ItemsSource = _visible;
            App.Battery.Refreshed += (_, __) => Rebuild();
            App.Battery.SourceChanged += (_, __) => Rebuild();
            Rebuild();
            Loaded += (_, __) =>
            {
                Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            };
        }

        /// <summary>迷你视图只显示被勾选的设备，最多 5 台。</summary>
        public void Rebuild()
        {
            var selected = App.Battery.Devices
                .Where(d => d.Selected && d.Connected)
                .Take(5)
                .ToList();

            if (selected.Count == 0)
                selected = App.Battery.Devices.Where(d => d.Connected).Take(5).ToList();

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
        public void ApplyBackground(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 2048;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();

                RenderOptions.SetBitmapScalingMode(Root, BitmapScalingMode.HighQuality);
                Root.Background = new System.Windows.Media.ImageBrush(bmp)
                {
                    Stretch = Stretch.UniformToFill
                };
                return;
            }
            ApplyTheme(App.Theme, false);
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
                    Root.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x0D, 0x1C));
                    return;
                default:
                    Root.Padding = new Thickness(12, 10, 12, 10);
                    Root.CornerRadius = new CornerRadius(18);
                    RootShadow.Opacity = 0.42;
                    break;
            }
            ApplyBackground(App.Settings.MiniBackground);
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            TextElement.SetForeground(Root, text);
            GlyphBrush = new SolidColorBrush(Palette.Mix(theme.Text, theme.Accent, 0.35));
            RootShadow.Color = theme.IsLight ? Color.FromRgb(0x8B, 0x93, 0xA6) : Colors.Black;
            RootShadow.Opacity = theme.IsLight ? 0.16 : 0.42;

            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(240);
                BgTopStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgTop, dur));
                BgBottomStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgBottom, dur));
            }
            else
            {
                BgTopStop.Color = theme.BgTop;
                BgBottomStop.Color = theme.BgBottom;
            }
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
        }

        private void OnContextMenu(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            App.ShowQuickMenu(this);
        }
    }
}
