using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MouseDock.Controls;
using MouseDock.Services;
using MouseDock.Models;

namespace MouseDock.Views
{
    public sealed class TaskbarWindow : Window
    {
        private TaskbarBatteryStrip _strip;
        private readonly Viewbox _viewport = new Viewbox { Stretch = Stretch.Uniform };
        private readonly DispatcherTimer _anchorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private bool _vertical;
        private bool _closingRequested, _hostLossQueued;
        private TaskbarChangeMonitor _monitor;
        private DeviceInfo[] _renderedDevices = Array.Empty<DeviceInfo>();
        private (string Style, bool Numbers, bool Vertical, Color Foreground, int Low,
            string Text, string Battery, bool Follow, Color Accent) _presentation;
        public event EventHandler HostLost;

        public TaskbarWindow()
        {
            Title = "MouseDock 任务栏电量";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = false;
            Width = 40;
            Height = 40;
            Content = _viewport;
            SourceInitialized += (_, __) =>
            {
                TaskbarAnchor.Configure(this);
                _monitor = new TaskbarChangeMonitor(Dispatcher, UpdateAnchor);
                var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null) source.Disposed += (_, __) => OnHostDisposed();
            };
            Loaded += (_, __) => { UpdateDevices(); UpdateAnchor(); };
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible)
                {
                    _anchorTimer.Start();
                    UpdateAnchor();
                }
                else
                {
                    _anchorTimer.Stop();
                }
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
                _monitor?.Dispose();
                App.Battery.Refreshed -= OnRefreshed;
            };
            UpdateDevices();
        }

        private void OnRefreshed(object sender, EventArgs e) => UpdateDevices();

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            _closingRequested = !e.Cancel;
        }

        private void OnHostDisposed()
        {
            _anchorTimer.Stop();
            _monitor?.Dispose();
            App.Battery.Refreshed -= OnRefreshed;
            if (!_closingRequested && !_hostLossQueued && !Dispatcher.HasShutdownStarted)
            {
                _hostLossQueued = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!_closingRequested) HostLost?.Invoke(this, EventArgs.Empty);
                }));
            }
        }

        public void UpdateDevices()
        {
            TaskbarAnchor.TryGet(out _, out uint edge, out _);
            _vertical = edge == 0 || edge == 2;
            bool light = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                light = key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
            }
            catch { }
            var devices = App.Battery.VisibleDevices.ToArray();
            var foreground = light ? Colors.Black : Colors.WhiteSmoke;
            var presentation = (App.Settings.TaskbarStyle, App.Settings.ShowBatteryNumber, _vertical,
                foreground, App.Settings.LowBatteryWarn, App.Settings.TaskbarTextColor,
                App.Settings.TaskbarBatteryColor, App.Settings.IconFollowTheme, App.Theme.Accent);
            if (_strip == null || presentation != _presentation || !_renderedDevices.SequenceEqual(devices))
            {
                _strip = new TaskbarBatteryStrip(devices, App.Settings.TaskbarStyle,
                    App.Settings.ShowBatteryNumber, _vertical, new SolidColorBrush(foreground),
                    App.Settings.LowBatteryWarn, App.Settings.TaskbarTextColor, App.Settings.TaskbarBatteryColor,
                    App.Settings.IconFollowTheme ? new SolidColorBrush(App.Theme.Accent) : null);
                _viewport.Child = _strip;
                _renderedDevices = devices;
                _presentation = presentation;
            }
            UpdateAnchor();
        }

        public void UpdateAnchor()
        {
            if (!IsVisible) return;
            if (!TaskbarAnchor.TryGet(out var bounds, out uint edge, out double dpi))
            {
                Opacity = 0;
                IsHitTestVisible = false;
                return;
            }
            bool attached = TaskbarAnchor.AttachToTaskbar(this);
            if (!attached) TaskbarAnchor.Detach(this);
            Opacity = _strip == null || _strip.DeviceCount == 0 ? 0 : 1;
            IsHitTestVisible = Opacity > 0;
            if (_vertical != (edge == 0 || edge == 2)) { UpdateDevices(); return; }
            if (_strip == null) return;
            var contentSize = _vertical ? new Size(_strip.Height, _strip.Width) : new Size(_strip.Width, _strip.Height);
            var placement = TaskbarPlacement.Calculate(bounds, edge, contentSize, App.Settings.TaskbarOffset, dpi);
            TaskbarAnchor.Move(this, placement);
            TaskbarAnchor.EnsureOrder(this, attached);
        }
    }
}
