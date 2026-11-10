using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Services;
using MouseDock.Models;

namespace MouseDock.Views
{
    public sealed class ScreenEdgeWindow : Window
    {
        private readonly StackPanel _devices = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        private readonly DispatcherTimer _anchorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private DeviceInfo[] _renderedDevices = Array.Empty<DeviceInfo>();
        private (string Style, bool Numbers, Color Foreground, string Battery, int Low, string Appearance) _presentation;

        public ScreenEdgeWindow()
        {
            Title = "MouseDock 屏幕右侧电量";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = App.Settings.AlwaysOnTop;
            Width = 48;
            Height = 64;
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = _devices };
            SourceInitialized += (_, __) => { TaskbarAnchor.Configure(this); App.ApplyWindowBehavior(); };
            Loaded += (_, __) => { UpdateDevices(); UpdateAnchor(); };
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible) { _anchorTimer.Start(); UpdateAnchor(); }
                else _anchorTimer.Stop();
            };
            _anchorTimer.Tick += (_, __) => UpdateAnchor();
            App.Battery.Refreshed += OnRefreshed;
            MouseRightButtonUp += (_, e) => { e.Handled = true; App.ShowQuickMenu(this); };
            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) App.ShowSettings(this);
                e.Handled = true;
            };
            Closed += (_, __) => { _anchorTimer.Stop(); App.Battery.Refreshed -= OnRefreshed; };
            UpdateDevices();
        }

        private void OnRefreshed(object sender, EventArgs args) => UpdateDevices();

        public static Color ResolveGlyphColor(string value)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(value);
                return Color.FromRgb(color.R, color.G, color.B);
            }
            catch { return Colors.WhiteSmoke; }
        }

        private static bool HasCustomColor(AppSettings settings) =>
            settings.EdgeGlyphColorCustomized || (!string.IsNullOrWhiteSpace(settings.EdgeGlyphColor) &&
                !string.Equals(settings.EdgeGlyphColor, "#F5F5F5", StringComparison.OrdinalIgnoreCase));

        public static Color ResolveForeground(AppSettings settings, ThemePreset theme) =>
            HasCustomColor(settings) || !settings.IconFollowTheme
                ? ResolveGlyphColor(settings.EdgeGlyphColor) : theme.Text;

        public static Brush ResolveBatteryBrush(AppSettings settings, ThemePreset theme) =>
            HasCustomColor(settings) ? new SolidColorBrush(ResolveGlyphColor(settings.EdgeGlyphColor)) :
                settings.IconFollowTheme ? new SolidColorBrush(theme.Accent) : null;

        public static DeviceBatteryBadge CreateRingBadge(DeviceInfo device, AppSettings settings, ThemePreset theme)
        {
            var badge = new DeviceBatteryBadge(device, new SolidColorBrush(ResolveForeground(settings, theme)),
                settings.ShowBatteryNumber, true, settings.LowBatteryWarn);
            var battery = ResolveBatteryBrush(settings, theme);
            badge.Ring.BatteryBrush = battery;
            if (battery != null) badge.Ring.GlyphBrush = battery;
            return badge;
        }

        public void UpdateDevices()
        {
            var devices = App.Battery.VisibleDevices.Take(5).ToArray();
            var style = TaskbarBatteryStrip.NormalizeStyle(App.Settings.EdgeStyle);
            var color = ResolveForeground(App.Settings, App.Theme);
            var battery = ResolveBatteryBrush(App.Settings, App.Theme);
            var presentation = (style, App.Settings.ShowBatteryNumber, color, battery?.ToString() ?? "", App.Settings.LowBatteryWarn,
                TaskbarBatteryStrip.AppearanceKey(App.Settings, App.Theme));
            if (presentation != _presentation || !_renderedDevices.SequenceEqual(devices))
            {
                _devices.Children.Clear();
                var foreground = new SolidColorBrush(color);
                if (style == "ring")
                    foreach (var device in devices)
                    {
                        var badge = CreateRingBadge(device, App.Settings, App.Theme);
                        badge.Margin = new Thickness(0, 4, 0, 4);
                        _devices.Children.Add(badge);
                    }
                else if (devices.Length > 0)
                    _devices.Children.Add(new TaskbarBatteryStrip(devices, style, App.Settings.ShowBatteryNumber, true,
                        foreground, App.Settings.LowBatteryWarn, "", "",
                        battery, App.Settings, App.Theme));
                _renderedDevices = devices;
                _presentation = presentation;
            }
            UpdateAnchor();
        }

        public void UpdateAnchor()
        {
            if (!IsVisible) return;
            if (_devices.Children.Count == 0) { Opacity = 0; IsHitTestVisible = false; return; }
            _devices.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            ScreenEdgeAnchor.Update(this, _devices.DesiredSize);
        }
    }
}
