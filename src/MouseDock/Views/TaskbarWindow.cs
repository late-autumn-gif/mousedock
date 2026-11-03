using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MouseDock.Controls;
using MouseDock.Services;

namespace MouseDock.Views
{
    public sealed class TaskbarWindow : Window
    {
        private readonly StackPanel _devices = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Viewbox _viewport = new Viewbox { Stretch = Stretch.Uniform };
        private readonly DispatcherTimer _anchorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public TaskbarWindow()
        {
            Title = "MouseDock 任务栏电量";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 40;
            Height = 40;
            _viewport.Child = _devices;
            Content = _viewport;
            SourceInitialized += (_, __) => TaskbarAnchor.Configure(this);
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
            Closed += (_, __) =>
            {
                _anchorTimer.Stop();
                App.Battery.Refreshed -= OnRefreshed;
            };
            UpdateDevices();
        }

        private void OnRefreshed(object sender, EventArgs e) => UpdateDevices();

        public void UpdateDevices()
        {
            _devices.Children.Clear();
            bool light = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                light = key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
            }
            catch { }
            foreach (var device in App.SelectedDevices.Take(5))
            {
                string status = device.HasLevel ? device.Level + "%" : "电量未知";
                if (device.Charging) status += " · 充电中";
                if (device.IsStale) status += " · 上次读数";
                var ring = new DeviceBatteryRing
                {
                    Kind = device.Kind, Level = device.Level, Charging = device.Charging,
                    LowBatteryThreshold = App.Settings.LowBatteryWarn,
                    GlyphBrush = light ? Brushes.Black : Brushes.WhiteSmoke,
                    Opacity = device.ValueOpacity,
                    ToolTip = device.Name + "\n" + status
                };
                AutomationProperties.SetName(ring, device.KindLabel + " · " + device.Name + " · " + status);
                ToolTipService.SetInitialShowDelay(ring, 300);
                _devices.Children.Add(ring);
            }
            UpdateAnchor();
        }

        public void UpdateAnchor()
        {
            if (!IsVisible) return;
            if (!TaskbarAnchor.TryGet(out var bounds, out uint edge, out double dpi, out bool suppressed))
            {
                Opacity = 0;
                return;
            }
            Opacity = suppressed || _devices.Children.Count == 0 ? 0 : 1;
            IsHitTestVisible = Opacity > 0;
            _devices.Orientation = edge == 0 || edge == 2 ? Orientation.Vertical : Orientation.Horizontal;
            var size = new Size(Math.Max(1, _devices.Children.Count) * 40, 40);
            var placement = TaskbarPlacement.Calculate(bounds, edge, size, App.Settings.TaskbarOffset, dpi);
            TaskbarAnchor.Move(this, placement);
        }
    }
}
