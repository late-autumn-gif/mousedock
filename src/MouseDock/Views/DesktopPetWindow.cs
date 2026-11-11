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
    private enum Pose { Idle, Wave, Low, Transition, Charging, Celebrate, Hover, Click, Drag }
    private const int ClickDuration = 1200;

    private sealed record Character(string Id, string Name, string Atlas, int InteractionRow, int HeaderColumn, Brush Surface, Brush Outline, Brush Accent, bool HasBlink);

    private static readonly Character[] Characters =
    {
        new("shin-chan", "蜡笔小新", "shin-chan-pet.png", 0, 0, Paint(22, 34, 51), Paint(85, 117, 148), Paint(147, 224, 249), false),
        new("xin-yue-hu", "心月狐", "xin-yue-hu-pet.png", 1, 2, Paint(40, 24, 36), Paint(239, 202, 139), Paint(160, 235, 203), true),
        new("rikka", "小鸟游六花", "rikka-takanashi-pet.png", 2, 1, Paint(23, 25, 50), Paint(170, 143, 237), Paint(144, 235, 249), true)
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
    private readonly Image _headerPortrait = new()
    {
        Width = 128, Height = 128, Stretch = Stretch.Uniform, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 0, 13, 0)
    };
    private readonly Image _character = new() { Width = 144, Height = 144, Stretch = Stretch.Uniform };
    private readonly Canvas _sparks = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock[] _stars = { new() { Text = "✦", FontSize = 13 }, new() { Text = "✦", FontSize = 8 } };
    private readonly ScaleTransform _breath = new(1, 1);
    private readonly RotateTransform _tilt = new();
    private readonly TranslateTransform _lift = new();
    private readonly ScaleTransform _size = new(1, 1);
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
    private bool _hovering;
    private System.Drawing.Point? _press;
    private Point _dragOrigin;
    private long _dragStartedAt;
    private double _look;

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
        _character.RenderTransform = new TransformGroup { Children = { _breath, _tilt, _lift } };
        var artwork = new Grid();
        artwork.Children.Add(_character);
        artwork.Children.Add(_sparks);
        foreach (var star in _stars) _sparks.Children.Add(star);
        var portrait = new Border { Background = Brushes.Transparent, Child = artwork };
        portrait.MouseEnter += (_, _) => { _hovering = true; RenderPose(); };
        portrait.MouseLeave += (_, _) => { _hovering = false; RenderPose(); };
        portrait.MouseLeftButtonDown += (_, e) =>
        {
            _press = System.Windows.Forms.Cursor.Position;
            _dragOrigin = PointToScreen(new Point());
            _hovering = false;
            portrait.CaptureMouse();
            e.Handled = true;
        };
        portrait.MouseMove += (_, e) =>
        {
            var point = e.GetPosition(portrait);
            _look = Math.Clamp((point.X - portrait.ActualWidth / 2) / (portrait.ActualWidth / 2), -1, 1);
            if (_press is not System.Drawing.Point start || e.LeftButton != MouseButtonState.Pressed || _settings.DesktopPetLocked)
                return;

            var cursor = System.Windows.Forms.Cursor.Position;
            int dx = cursor.X - start.X, dy = cursor.Y - start.Y;
            var dpi = VisualTreeHelper.GetDpi(this);
            if (!_dragging)
            {
                if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX &&
                    Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY)
                    return;
                _dragging = true;
                _dragStartedAt = Environment.TickCount64;
                _actionUntil = 0;
                RenderPose();
            }
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                (int)_dragOrigin.X + dx, (int)_dragOrigin.Y + dy, 0, 0, 0x0015);
        };
        portrait.MouseLeftButtonUp += (_, e) =>
        {
            if (_press == null) return;
            bool wasDragging = _dragging;
            EndPointerGesture();
            portrait.ReleaseMouseCapture();
            if (!wasDragging && portrait.IsMouseOver) Play(Pose.Click, ClickDuration);
            e.Handled = true;
        };
        portrait.LostMouseCapture += (_, _) => EndPointerGesture();
        PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta == 0 || _dragging) return;
            ChangeSize(_settings.DesktopPetSize + Math.Sign(e.Delta) * 5);
            e.Handled = true;
        };

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 8, 0, 7) };
        heading.Children.Add(new TextBlock { Text = "✦", FontSize = 17, Foreground = Brushes.Gold, Margin = new Thickness(0, 0, 8, 0) });
        heading.Children.Add(new TextBlock { Text = "设备电量", FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        var batteryContent = new StackPanel { Width = 300 };
        batteryContent.Children.Add(heading);
        batteryContent.Children.Add(_deviceList);
        _bubble = new Border
        {
            Child = batteryContent, CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(1.5),
            Padding = new Thickness(9, 0, 9, 9), Margin = new Thickness(0, 80, 0, 0)
        };
        ApplyCharacter(Characters.FirstOrDefault(item => item.Id == settings.DesktopPetCharacter) ?? Characters[0]);
        var layout = new StackPanel { Orientation = Orientation.Horizontal, LayoutTransform = _size };
        portrait.VerticalAlignment = VerticalAlignment.Center;
        portrait.Margin = new Thickness(4, 9, 8, 4);
        layout.Children.Add(portrait);
        var card = new Grid { VerticalAlignment = VerticalAlignment.Center };
        card.Children.Add(_bubble);
        card.Children.Add(_headerPortrait);
        layout.Children.Add(card);
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
        var interactions = new BitmapImage(new Uri("pack://application:,,,/MouseDock;component/Assets/Characters/desktop-pet-interactions.png"));
        // The transparent gutters in the gesture atlas are not evenly spaced.
        int[] columns = { 0, 399, 837, interactions.PixelWidth };
        int[] rows = { 0, 388, 793, interactions.PixelHeight };
        int gestureTop = rows[skin.InteractionRow];
        int gestureHeight = rows[skin.InteractionRow + 1] - gestureTop;
        _frames = Enumerable.Range(0, 9).Select(index =>
        {
            int gesture = index - 6;
            var frame = index < 6
                ? new CroppedBitmap(atlas, new Int32Rect(index % 3 * width, index / 3 * height, width, height))
                : new CroppedBitmap(interactions, new Int32Rect(columns[gesture], gestureTop, columns[gesture + 1] - columns[gesture], gestureHeight));
            frame.Freeze();
            return (ImageSource)frame;
        }).ToArray();

        var headers = new BitmapImage(new Uri("pack://application:,,,/MouseDock;component/Assets/Characters/desktop-pet-headers.png"));
        int headerWidth = headers.PixelWidth / 3;
        _headerPortrait.Source = new CroppedBitmap(headers,
            new Int32Rect(skin.HeaderColumn * headerWidth, 0, headerWidth, headers.PixelHeight / 2));
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

    private void Refresh(bool rebuild = false)
    {
        var displayed = _battery.VisibleDevices.ToArray();
        var device = displayed.FirstOrDefault(d => d.Id == _settings.DesktopPetDevice) ?? displayed.FirstOrDefault();
        bool charging = device?.IsChargingActive == true;
        bool changed = device?.Id != _device?.Id;
        if (changed) _actionUntil = 0;
        if (!changed && charging && !_charging && !_skin.HasBlink) Play(Pose.Transition, 1050);
        _device = device;
        _charging = charging;

        if (rebuild || changed || _deviceList.Children.Count == 0 || !_listedDevices.SequenceEqual(displayed))
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

    private void OnFull(DeviceInfo device)
    {
        if (IsVisible && device.Id == _device?.Id) Play(Pose.Celebrate, 1450);
    }

    private void Play(Pose pose, int duration)
    {
        if (!Motion) return;
        if ((_action == Pose.Celebrate || _action == pose) && Environment.TickCount64 < _actionUntil) return;
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
        if (Motion && _dragging) pose = Pose.Drag;
        else if (Motion && !inAction && _hovering) pose = Pose.Hover;
        var image = _frames[(int)pose];
        if (!ReferenceEquals(_character.Source, image)) _character.Source = image;
        _breath.ScaleY = Motion ? 1 + (pose == Pose.Charging ? .024 : .012) *
            Math.Sin(now / (pose == Pose.Charging ? 230d : 580d)) : 1;
        double dragTime = Math.Min(700, now - _dragStartedAt);
        double clickProgress = pose == Pose.Click ? 1 - Math.Clamp((_actionUntil - now) / (double)ClickDuration, 0, 1) : 0;
        double clickMotion = Math.Sin(clickProgress * Math.PI);
        _tilt.Angle = pose switch
        {
            Pose.Drag => Math.Sin(dragTime * Math.PI / 175) * 7,
            Pose.Hover => _look * 5,
            Pose.Click => (_skin.Id == "rikka" ? 5 : -3) * clickMotion,
            _ => 0
        };
        _lift.Y = pose == Pose.Drag ? -6 : -4 * clickMotion;
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

    }

    private void RebuildList()
    {
        _deviceList.Children.Clear();
        if (_listedDevices.Length == 0)
        {
            _deviceList.Children.Add(new TextBlock { Text = "未选择显示设备", Foreground = Brushes.LightGray, Margin = new Thickness(9, 8, 0, 8) });
            return;
        }

        var accent = ((SolidColorBrush)_skin.Accent).Color;
        foreach (var device in _listedDevices)
        {
            _deviceList.Children.Add(new Border { Height = 1, Background = _skin.Outline, Opacity = .46, Margin = new Thickness(4, 0, 4, 0) });
            var row = new Grid { DataContext = device, Margin = new Thickness(9, 5, 9, 5), MinHeight = 38 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            var glyph = new DeviceGlyph { Width = 22, Height = 22, GlyphBrush = _skin.Accent, HorizontalAlignment = HorizontalAlignment.Left };
            glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
            row.Children.Add(glyph);

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var name = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Name)));
            details.Children.Add(name);
            var meter = new MeterBar
            {
                Height = 6, BarHeight = 5, Margin = new Thickness(0, 4, 0, 0), ShowDot = false,
                Accent = accent, Accent2 = accent, MotionEnabled = Motion
            };
            meter.SetBinding(MeterBar.ValueProperty, new Binding(nameof(DeviceInfo.LevelFraction)));
            meter.SetBinding(MeterBar.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
            meter.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
            details.Children.Add(meter);
            Grid.SetColumn(details, 1);
            row.Children.Add(details);

            var reading = new TextBlock
            {
                Foreground = _skin.Accent, FontSize = 17, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            reading.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.LevelText)));
            reading.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
            Grid.SetColumn(reading, 2);
            row.Children.Add(reading);
            var item = new Button
            {
                Content = row, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand,
                Background = device == _device ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) : Brushes.Transparent
            };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            item.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
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

        var sizes = new MenuItem { Header = "桌宠大小" };
        foreach (int size in new[] { 50, 75, 100, 125, 150 })
            Add(sizes.Items, $"{size}%", () => ChangeSize(size), _settings.DesktopPetSize == size);
        ContextMenu.Items.Add(sizes);
        Add(ContextMenu.Items, "锁定位置", () => { _settings.DesktopPetLocked = !_settings.DesktopPetLocked; App.SaveSettings(); }, _settings.DesktopPetLocked);
        Add(ContextMenu.Items, "减少动态效果", () => { _settings.DesktopPetReduceMotion = !_settings.DesktopPetReduceMotion; _actionUntil = 0; SaveAndRefresh(); }, _settings.DesktopPetReduceMotion);
        Add(ContextMenu.Items, "满电提醒（全局）", () => { _settings.NotifyChargeCompleted = !_settings.NotifyChargeCompleted; App.SaveSettings(); }, _settings.NotifyChargeCompleted);
        Add(ContextMenu.Items, "设置…", () => App.ShowSettings(this));
        Add(ContextMenu.Items, "隐藏桌宠", App.ToggleCurrentSurface);
    }

    private static void Add(ItemCollection items, string text, Action action, bool? selected = null)
    {
        var item = new MenuItem { Header = text, IsCheckable = selected.HasValue, IsChecked = selected == true };
        item.Click += (_, _) => action();
        items.Add(item);
    }

    private void SaveAndRefresh()
    {
        Refresh(rebuild: true);
        App.SaveSettings();
    }

    private void EndPointerGesture()
    {
        _press = null;
        if (!_dragging) return;
        _dragging = false;
        _hovering = false;
        PlaceOnScreen(remember: true);
        RenderPose();
    }

    private void ChangeSize(int requested)
    {
        int size = Math.Clamp(requested, 50, 150);
        if (_settings.DesktopPetSize == size) return;
        _settings.DesktopPetSize = size;
        ApplySize();
        Dispatcher.BeginInvoke(new Action(() => PlaceOnScreen(remember: false)), DispatcherPriority.Loaded);
        App.SaveSettings();
    }

    private void ApplySize() => _size.ScaleX = _size.ScaleY = Math.Clamp(_settings.DesktopPetSize, 50, 150) / 100d;

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
