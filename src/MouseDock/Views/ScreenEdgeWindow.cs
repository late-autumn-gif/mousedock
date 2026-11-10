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
        private (string Style, bool Numbers, Color Foreground, int Low, string Appearance) _presentation;

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

        public void UpdateDevices()
        {
            var devices = App.Battery.VisibleDevices.Take(5).ToArray();
            var style = TaskbarBatteryStrip.NormalizeStyle(App.Settings.EdgeStyle);
            var color = ResolveGlyphColor(App.Settings.EdgeGlyphColor);
            var presentation = (style, App.Settings.ShowBatteryNumber, color, App.Settings.LowBatteryWarn,
                TaskbarBatteryStrip.AppearanceKey(App.Settings, App.Theme));
            if (presentation != _presentation || !_renderedDevices.SequenceEqual(devices))
            {
                _devices.Children.Clear();
                var foreground = new SolidColorBrush(color);
                if (style == "ring")
                    foreach (var device in devices)
                        _devices.Children.Add(new DeviceBatteryBadge(device, foreground, App.Settings.ShowBatteryNumber, true,
                            App.Settings.LowBatteryWarn) { Margin = new Thickness(0, 4, 0, 4) });
                else if (devices.Length > 0)
                    _devices.Children.Add(new TaskbarBatteryStrip(devices, style, App.Settings.ShowBatteryNumber, true,
                        foreground, App.Settings.LowBatteryWarn, "", App.Settings.EdgeGlyphColor,
                        App.Settings.IconFollowTheme ? new SolidColorBrush(App.Theme.Accent) : null, App.Settings, App.Theme));
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
