using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using Screen = System.Windows.Forms.Screen;

namespace MouseDock.Views;

internal sealed class DesktopPetWindow : Window
{
    private enum Pose { Idle, Wave, Low, Transition, Charging, Celebrate }

    private sealed record Character(string Id, string Name, string Atlas, Brush Surface, Brush Outline,
        Brush Accent, Brush ChargingAccent, Color Halo, bool HasBlink);

    private static readonly Character[] Characters =
    {
        new("shin-chan", "蜡笔小新", "shin-chan-pet.png", Paint(26, 39, 56), Paint(82, 104, 128),
            Brushes.LightSkyBlue, Brushes.Aquamarine, Color.FromRgb(100, 240, 220), false),
        new("xin-yue-hu", "心月狐", "xin-yue-hu-pet.png", Paint(47, 26, 39), Paint(184, 132, 83),
            Paint(252, 218, 161), Paint(160, 235, 203), Color.FromRgb(249, 193, 112), true),
        new("rikka", "小鸟游六花", "rikka-takanashi-pet.png", Paint(29, 28, 57), Paint(135, 113, 192),
            Paint(151, 223, 252), Paint(81, 205, 248), Color.FromRgb(198, 133, 255), true)
    };

    private static Brush Paint(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly BatteryService _battery;
    private readonly AppSettings _settings;
    private ImageSource[] _frames;
    private Character _skin;
    private readonly Border _bubble;
    private readonly Image _character = new() { Width = 144, Height = 144, Stretch = Stretch.Uniform };
    private readonly Canvas _sparks = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock[] _stars = { new() { Text = "✦", FontSize = 13 }, new() { Text = "✦", FontSize = 8 } };
    private readonly ScaleTransform _breath = new(1, 1);
    private readonly ScaleTransform _size = new(1, 1);
    private readonly PetBatteryGauge _gauge = new() { Width = 78, Height = 78 };
    private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _deviceList = new();
    private DeviceInfo[] _listedDevices = Array.Empty<DeviceInfo>();
    private readonly DispatcherTimer _timer;
    private DeviceInfo _device;
    private Pose _action;
    private long _actionUntil;
    private bool _charging;
    private bool _refreshQueued;
    private bool _closed;
    private bool _restoringPosition;
    private bool _dragging;
    private Point _dragStart;

    internal DesktopPetWindow(BatteryService battery, AppSettings settings)
    {
        _battery = battery;
        _settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = settings.AlwaysOnTop;
        UseLayoutRounding = true;

        _character.RenderTransformOrigin = new Point(.5, .98);
        _character.RenderTransform = _breath;
        var artwork = new Grid();
        artwork.Children.Add(_character);
        artwork.Children.Add(_sparks);
        foreach (var star in _stars) _sparks.Children.Add(star);
        var portrait = new Border { Background = Brushes.Transparent, Child = artwork };
        portrait.MouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        portrait.MouseMove += (_, e) =>
        {
            if (_settings.DesktopPetLocked || e.LeftButton != MouseButtonState.Pressed ||
                (e.GetPosition(this) - _dragStart).Length < SystemParameters.MinimumHorizontalDragDistance) return;
            _dragging = true;
            try { DragMove(); }
            catch (InvalidOperationException) { return; }
            finally { _dragging = false; }
            PlaceOnScreen(remember: true);
        };
        portrait.MouseLeftButtonUp += (_, _) => Play(Pose.Wave, 950);

        var details = new StackPanel { Width = 106, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(_name);
        details.Children.Add(_status);
        var summary = new StackPanel { Orientation = Orientation.Horizontal };
        summary.Children.Add(_gauge);
        summary.Children.Add(details);
        var batteryContent = new StackPanel { Width = 205 };
        batteryContent.Children.Add(summary);
        batteryContent.Children.Add(new ScrollViewer
        {
            Content = _deviceList, MaxHeight = 236, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        _bubble = new Border
        {
            Child = batteryContent, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 0, 7, 5), VerticalAlignment = VerticalAlignment.Center
        };
        ApplyCharacter(Characters.FirstOrDefault(item => item.Id == settings.DesktopPetCharacter) ?? Characters[0]);
        var layout = new StackPanel { Orientation = Orientation.Horizontal, LayoutTransform = _size };
        layout.Children.Add(portrait);
        layout.Children.Add(_bubble);
        Content = layout;

        ContextMenu = new ContextMenu();
        ContextMenu.Opened += (_, _) => BuildMenu();
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(85) };
        _timer.Tick += (_, _) => RenderPose();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
            else _timer.Stop();
        };
        Loaded += (_, _) => PlaceOnScreen(remember: false);
        _battery.Refreshed += OnRefresh;
        _battery.VisibleDevices.CollectionChanged += OnDevicesChanged;
        _battery.ChargeCompleted += OnFull;
        ApplySize();
    }

    private void ApplyCharacter(Character skin)
    {
        var atlas = new BitmapImage(new Uri($"pack://application:,,,/MouseDock;component/Assets/Characters/{skin.Atlas}"));
        int width = atlas.PixelWidth / 3, height = atlas.PixelHeight / 2;
        _frames = Enumerable.Range(0, 6).Select(index =>
        {
            var frame = new CroppedBitmap(atlas, new Int32Rect(index % 3 * width, index / 3 * height, width, height));
            frame.Freeze();
            return (ImageSource)frame;
        }).ToArray();

        _character.Source = _frames[0];
        _skin = skin;
        _actionUntil = 0;
        Title = $"MouseDock · {skin.Name}桌宠";
        _bubble.Background = skin.Surface;
        _bubble.BorderBrush = skin.Outline;
        _listedDevices = Array.Empty<DeviceInfo>();
        _deviceList.Children.Clear();
    }

    private bool Motion => !_settings.DesktopPetReduceMotion && SystemParameters.ClientAreaAnimation;

    private void OnRefresh(object sender, EventArgs e) => QueueRefresh();
    private void OnDevicesChanged(object sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_refreshQueued || _closed) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshQueued = false;
            if (!_closed && IsVisible) Refresh();
        }), DispatcherPriority.DataBind);
    }

    private void Refresh()
    {
        var displayed = _battery.Devices.Where(d => d.IsDisplayEligible).ToArray();
        var device = displayed.FirstOrDefault(d => d.Id == _settings.DesktopPetDevice) ?? displayed.FirstOrDefault();
        bool charging = device?.IsChargingActive == true;
        bool changed = device?.Id != _device?.Id;
        if (changed) _actionUntil = 0;
        if (!changed && charging && !_charging && !_skin.HasBlink) Play(Pose.Transition, 1050);
        _device = device;
        _charging = charging;

        _name.Text = device?.Name ?? "未选择设备";
        _name.ToolTip = device?.Name;
        _status.Text = Status(device);
        _bubble.BorderBrush = IsLow ? Brushes.Orange : device?.IsFullActive == true ? Brushes.Aquamarine : _skin.Outline;
        _gauge.Update(device, _settings.DesktopPetStyle, _settings.DesktopPetWave, Motion,
            _skin.Accent, _skin.ChargingAccent, _skin.Halo, _settings.LowBatteryWarn);
        if (!_listedDevices.SequenceEqual(displayed))
        {
            _listedDevices = displayed;
            RebuildList();
            if (IsLoaded)
                Dispatcher.BeginInvoke(new Action(() => PlaceOnScreen(remember: false)), DispatcherPriority.Loaded);
        }
        if (IsVisible && Motion) _timer.Start();
        else _timer.Stop();
        RenderPose();
    }

    private static string Status(DeviceInfo device)
    {
        if (device == null) return "右键选择显示设备";
        if (device.IsFullActive) return "已充满";
        if (device.IsChargingActive) return "充电中";
        if ((device.IsStale || !device.Connected) && device.Level >= 0) return $"上次 {device.Level}%";
        if (!device.Connected) return "离线";
        return device.Level < 0 ? "电量未知" : device.KindLabel;
    }

    private void OnFull(DeviceInfo device)
    {
        if (IsVisible && device.Id == _device?.Id) Play(Pose.Celebrate, 1450);
    }

    private void Play(Pose pose, int duration)
    {
        if (!Motion) return;
        if (_action == Pose.Celebrate && Environment.TickCount64 < _actionUntil) return;
        _action = pose;
        _actionUntil = Environment.TickCount64 + duration;
        RenderPose();
    }

    private bool IsLow => !_charging && _device is { Connected: true, IsStale: false, IsFullActive: false, Level: >= 0 } &&
        _device.Level <= _settings.LowBatteryWarn;

    private void RenderPose()
    {
        long now = Environment.TickCount64;
        bool inAction = Motion && now < _actionUntil &&
            (_action != Pose.Transition || _charging) && (_action != Pose.Celebrate || _device?.IsFullActive == true);
        Pose pose = inAction ? _action : _charging ? Pose.Charging :
            IsLow && (_skin.HasBlink || !Motion || now % 5500 < 1350) ? Pose.Low : Pose.Idle;
        if (pose == Pose.Idle && _skin.HasBlink && Motion && now % 5400 < 170) pose = Pose.Transition;
        var image = _frames[(int)pose];
        if (!ReferenceEquals(_character.Source, image)) _character.Source = image;
        _breath.ScaleY = Motion ? 1 + (pose == Pose.Charging ? .024 : .012) *
            Math.Sin(now / (pose == Pose.Charging ? 230d : 580d)) : 1;
        bool sparkling = _skin.Id == "rikka" && Motion && pose is Pose.Charging or Pose.Celebrate;
        _sparks.Visibility = sparkling ? Visibility.Visible : Visibility.Collapsed;
        if (sparkling)
        {
            double progress = pose == Pose.Celebrate ? 1 - Math.Clamp((_actionUntil - now) / 1450d, 0, 1) : 0;
            for (int i = 0; i < _stars.Length; i++)
            {
                var star = _stars[i];
                star.Foreground = pose == Pose.Celebrate ? Brushes.Gold : Brushes.Violet;
                star.Opacity = (1 - progress) * (.65 + .35 * Math.Sin(now / 220d + i * 2));
                Canvas.SetLeft(star, 108 + i * 12 + progress * (6 + i * 9));
                Canvas.SetTop(star, 13 + i * 13 - progress * 10);
            }
        }
        _status.Text = pose == Pose.Transition && !_skin.HasBlink ? "⚡ 接电" : Status(_device);
    }

    private void RebuildList()
    {
        _deviceList.Children.Clear();
        foreach (var device in _listedDevices)
        {
            var row = new Grid { Width = 175, DataContext = device };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = new DeviceGlyph { Width = 15, Height = 15, GlyphBrush = _skin.Accent, Margin = new Thickness(0, 0, 7, 0) };
            glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
            row.Children.Add(glyph);

            var name = new TextBlock { Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) };
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Name)));
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var reading = new TextBlock { Foreground = _skin.Accent };
            reading.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.LevelText)));
            reading.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
            Grid.SetColumn(reading, 2);
            row.Children.Add(reading);
            var item = new Button { Content = row, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4) };
            item.SetBinding(ToolTipProperty, new Binding(nameof(DeviceInfo.StatusText)) { Source = device });
            item.Click += (_, _) => Follow(device.Id);
            _deviceList.Children.Add(item);
        }
    }

    private void Follow(string id)
    {
        _settings.DesktopPetDevice = id;
        Refresh();
        App.SaveSettings();
    }

    private void BuildMenu()
    {
        ContextMenu.Items.Clear();
        var characters = new MenuItem { Header = "桌宠角色" };
        foreach (var skin in Characters)
            Add(characters.Items, skin.Name, () =>
            {
                if (ReferenceEquals(_skin, skin)) return;
                ApplyCharacter(skin);
                _settings.DesktopPetCharacter = skin.Id;
                SaveAndRefresh();
            }, ReferenceEquals(_skin, skin));
        ContextMenu.Items.Add(characters);

        var display = new MenuItem { Header = "显示设备", IsEnabled = _battery.Devices.Any(d => d.Connected && d.RecognitionEnabled && !d.Hidden) };
        foreach (var device in _battery.Devices.Where(d => d.Connected && d.RecognitionEnabled && !d.Hidden))
        {
            var item = new MenuItem { Header = device.Name, IsCheckable = true, IsChecked = device.Selected, StaysOpenOnClick = true };
            item.Click += (_, _) => _battery.SetSelected(device, item.IsChecked);
            display.Items.Add(item);
        }
        ContextMenu.Items.Add(display);

        var follow = new MenuItem { Header = "跟随设备" };
        Add(follow.Items, "自动选择", () => Follow(""), _settings.DesktopPetDevice == "");
        foreach (var device in _listedDevices)
            Add(follow.Items, device.Name, () => Follow(device.Id), _settings.DesktopPetDevice == device.Id);
        ContextMenu.Items.Add(follow);

        var shapes = new MenuItem { Header = "电量样式" };
        foreach (var (id, label) in new[] { ("ring", "电量环"), ("vertical", "竖向电量线"), ("battery", "经典电池") })
            Add(shapes.Items, label, () => { _settings.DesktopPetStyle = id; SaveAndRefresh(); }, _settings.DesktopPetStyle == id);
        ContextMenu.Items.Add(shapes);
        var effects = new MenuItem { Header = "充电动效" };
        Add(effects.Items, "流光／波浪", () => { _settings.DesktopPetWave = true; SaveAndRefresh(); }, _settings.DesktopPetWave);
        Add(effects.Items, "扫光", () => { _settings.DesktopPetWave = false; SaveAndRefresh(); }, !_settings.DesktopPetWave);
        ContextMenu.Items.Add(effects);
        var sizes = new MenuItem { Header = "桌宠大小" };
        foreach (int size in new[] { 75, 100, 125, 150 })
            Add(sizes.Items, $"{size}%", () =>
            {
                _settings.DesktopPetSize = size;
                ApplySize();
                Dispatcher.BeginInvoke(new Action(() => PlaceOnScreen(remember: true)), DispatcherPriority.Loaded);
                App.SaveSettings();
            }, _settings.DesktopPetSize == size);
        ContextMenu.Items.Add(sizes);
        Add(ContextMenu.Items, "锁定位置", () => { _settings.DesktopPetLocked = !_settings.DesktopPetLocked; App.SaveSettings(); }, _settings.DesktopPetLocked);
        Add(ContextMenu.Items, "减少动态效果", () => { _settings.DesktopPetReduceMotion = !_settings.DesktopPetReduceMotion; _actionUntil = 0; SaveAndRefresh(); }, _settings.DesktopPetReduceMotion);
        Add(ContextMenu.Items, "满电提醒（全局）", () => { _settings.NotifyChargeCompleted = !_settings.NotifyChargeCompleted; App.SaveSettings(); }, _settings.NotifyChargeCompleted);
        Add(ContextMenu.Items, "隐藏桌宠", () => App.SetDesktopPetVisible(false));
    }

    private static void Add(ItemCollection items, string text, Action action, bool? selected = null)
    {
        var item = new MenuItem { Header = text, IsCheckable = selected.HasValue, IsChecked = selected == true };
        item.Click += (_, _) => action();
        items.Add(item);
    }

    private void SaveAndRefresh()
    {
        Refresh();
        App.SaveSettings();
    }

    private void ApplySize() => _size.ScaleX = _size.ScaleY = Math.Clamp(_settings.DesktopPetSize, 75, 150) / 100d;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    private void PlaceOnScreen(bool remember)
    {
        if (_closed || !IsLoaded || _restoringPosition || _dragging) return;
        var handle = new WindowInteropHelper(this).Handle;
        var screen = remember ? Screen.FromHandle(handle) :
            Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.DesktopPetMonitor) ?? Screen.PrimaryScreen;
        if (screen == null) return;
        var area = screen.WorkingArea;
        Point origin = PointToScreen(new Point());
        Point corner = PointToScreen(new Point(ActualWidth, ActualHeight));
        int spanX = Math.Max(0, area.Width - (int)Math.Ceiling(corner.X - origin.X));
        int spanY = Math.Max(0, area.Height - (int)Math.Ceiling(corner.Y - origin.Y));
        int x = remember ? Math.Clamp((int)origin.X - area.Left, 0, spanX) :
            (int)Math.Round(spanX * Normalized(_settings.DesktopPetX, 1));
        int y = remember ? Math.Clamp((int)origin.Y - area.Top, 0, spanY) :
            (int)Math.Round(spanY * Normalized(_settings.DesktopPetY, .75));
        var dpi = VisualTreeHelper.GetDpi(this);
        int snapX = (int)Math.Round(16 * dpi.DpiScaleX);
        int snapY = (int)Math.Round(16 * dpi.DpiScaleY);
        if (x <= snapX) x = 0;
        else if (spanX - x <= snapX) x = spanX;
        if (y <= snapY) y = 0;
        else if (spanY - y <= snapY) y = spanY;

        _restoringPosition = true;
        try { SetWindowPos(handle, IntPtr.Zero, area.Left + x, area.Top + y, 0, 0, 0x0015); }
        finally { _restoringPosition = false; }

        if (!remember) return;
        _settings.DesktopPetMonitor = screen.DeviceName;
        _settings.DesktopPetX = spanX > 0 ? (double)x / spanX : 0;
        _settings.DesktopPetY = spanY > 0 ? (double)y / spanY : 0;
        App.SaveSettings();
    }

    private static double Normalized(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        bool remember = !_restoringPosition;
        Dispatcher.BeginInvoke(new Action(() => PlaceOnScreen(remember)), DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _battery.Refreshed -= OnRefresh;
        _battery.VisibleDevices.CollectionChanged -= OnDevicesChanged;
        _battery.ChargeCompleted -= OnFull;
        base.OnClosed(e);
    }
}
