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

namespace MouseDock.Views
{
    public sealed class TaskbarWindow : Window
    {
        private readonly StackPanel _devices = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Viewbox _viewport = new Viewbox { Stretch = Stretch.Uniform };
        private readonly DispatcherTimer _anchorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private bool _vertical;
        private bool _closingRequested, _hostLossQueued;
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
            _viewport.Child = _devices;
            Content = _viewport;
            SourceInitialized += (_, __) =>
            {
                TaskbarAnchor.Configure(this);
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
            _devices.Children.Clear();
            TaskbarAnchor.TryGet(out _, out uint edge, out _);
            _vertical = edge == 0 || edge == 2;
            _devices.Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal;
            bool light = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                light = key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
            }
            catch { }
            foreach (var device in App.Battery.VisibleDevices)
            {
                _devices.Children.Add(new DeviceBatteryBadge(device, light ? Brushes.Black : Brushes.WhiteSmoke,
                    App.Settings.ShowBatteryNumber, true, App.Settings.LowBatteryWarn, compact: true));
            }
            UpdateAnchor();
        }

        public void UpdateAnchor()
        {
            if (!IsVisible) return;
            TaskbarAnchor.Detach(this);
            if (!TaskbarAnchor.TryGet(out var bounds, out uint edge, out double dpi))
            {
                Opacity = 0;
                IsHitTestVisible = false;
                return;
            }
            // DPI 上下文对不上时 SetParent 会失败：退回原来的浮窗行为，总要看得见电量。
            Topmost = true;
            Opacity = _devices.Children.Count == 0 ? 0 : 1;
            IsHitTestVisible = Opacity > 0;
            if (_vertical != (edge == 0 || edge == 2)) { UpdateDevices(); return; }
            var placement = TaskbarPlacement.CalculateBadges(bounds, edge, _devices.Children.Count,
                App.Settings.TaskbarOffset, dpi);
            TaskbarAnchor.Move(this, placement);
        }
    }
}
