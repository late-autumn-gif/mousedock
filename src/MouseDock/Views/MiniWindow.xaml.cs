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
