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
        private TaskbarLayoutReader _layout;
        private DeviceInfo[] _renderedDevices = Array.Empty<DeviceInfo>();
        private (string Style, bool Numbers, bool Vertical, Color Foreground, int Low,
            string Text, string Battery, bool Follow, Color Accent, string Appearance) _presentation;
        public event EventHandler HostLost;
        public event EventHandler OverflowChanged;
        public bool UsesScreenEdge { get; private set; }

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
                _layout = new TaskbarLayoutReader(Dispatcher, UpdateAnchor);
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
                    SetOverflow(false);
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
                _layout?.Dispose();
                SetOverflow(false);
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
            _layout?.Dispose();
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
                App.Settings.TaskbarBatteryColor, App.Settings.IconFollowTheme, App.Theme.Accent,
                TaskbarBatteryStrip.AppearanceKey(App.Settings, App.Theme));
            if (_strip == null || presentation != _presentation || !_renderedDevices.SequenceEqual(devices))
            {
                _strip = new TaskbarBatteryStrip(devices, App.Settings.TaskbarStyle,
                    App.Settings.ShowBatteryNumber, _vertical, new SolidColorBrush(foreground),
                    App.Settings.LowBatteryWarn, App.Settings.TaskbarTextColor, App.Settings.TaskbarBatteryColor,
                    App.Settings.IconFollowTheme ? new SolidColorBrush(App.Theme.Accent) : null, App.Settings, App.Theme);
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
                SetOverflow(App.Settings.TaskbarFollowIcons && _strip?.DeviceCount > 0);
                return;
            }
            if (_vertical != (edge == 0 || edge == 2)) { UpdateDevices(); return; }
            if (_strip == null || _strip.DeviceCount == 0)
            {
                Opacity = 0;
                IsHitTestVisible = false;
                SetOverflow(false);
                return;
            }
            var contentSize = _vertical ? new Size(_strip.Height, _strip.Width) : new Size(_strip.Width, _strip.Height);
            Rect placement;
            if (App.Settings.TaskbarFollowIcons)
            {
                _layout?.Request();
                if (_layout == null || !_layout.TryGet(bounds, edge, out var snapshot) ||
                    !TaskbarPlacement.TryFollow(bounds, edge, contentSize, dpi, snapshot.Icons, snapshot.Occupied,
                        out placement, UsesScreenEdge ? 8 : 0, App.Settings.TaskbarIconOffset))
                {
                    Opacity = 0;
                    IsHitTestVisible = false;
                    SetOverflow(true);
                    return;
                }
            }
            else placement = TaskbarPlacement.Calculate(bounds, edge, contentSize, App.Settings.TaskbarOffset, dpi);
            bool attached = TaskbarAnchor.AttachToTaskbar(this);
            if (!attached) TaskbarAnchor.Detach(this);
            TaskbarAnchor.Move(this, placement);
            TaskbarAnchor.EnsureOrder(this, attached);
            Opacity = 1;
            IsHitTestVisible = true;
            SetOverflow(false);
        }

        private void SetOverflow(bool value)
        {
            if (UsesScreenEdge == value) return;
            UsesScreenEdge = value;
            OverflowChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
