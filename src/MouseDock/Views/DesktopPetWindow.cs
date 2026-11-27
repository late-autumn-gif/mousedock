using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Documents;
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
    private enum Pose { Transition, Celebrate, Click, Dizzy }
    private const int ClickDuration = 1200;
    private const string KeqingId = "keqing";
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1d / 60);

    private sealed record Character(string Id, string Name, int HeaderColumn, bool HasBlink = true);

    private static readonly Character[] Characters =
    {
        new("shin-chan", "蜡笔小新", 0),
        new("xin-yue-hu", "心月狐", 2),
        new("rikka", "小鸟游六花", 1),
        new(KeqingId, "刻晴", 0)
    };
    private static readonly Brush CardSurface = Paint(40, 42, 40);
    private static readonly Brush CardLine = Paint(75, 77, 72);
    private static readonly Brush CardInk = Paint(241, 240, 232);
    private static readonly Brush CardMuted = Paint(190, 192, 181);
    private static readonly Brush CardAccent = Paint(185, 207, 171);
    private static readonly Brush ImpactDust = Paint(163, 157, 176);

    private static Brush Paint(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly BatteryService _battery;
    private readonly AppSettings _settings;
    private Character _skin;
    private readonly Border _bubble;
    private readonly Image _headerPortrait = new()
    {
        Width = 128, Height = 128, Stretch = Stretch.Uniform, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 0, 13, 0)
    };
    private readonly Image _character = new() { Width = 144, Height = 144, Stretch = Stretch.Uniform };
    private readonly BongoPetScene _bongo;
    private readonly FreePetScene _free = new();
    private readonly ScaleTransform _size = new(1, 1);
    private readonly RotateTransform _petTilt = new();
    private readonly TranslateTransform _petLift = new();
    private readonly ScaleTransform _petBreath = new(1, 1);
    private readonly StackPanel _deviceList = new() { Width = 248 };
    private DeviceInfo[] _listedDevices = Array.Empty<DeviceInfo>();
    private readonly DispatcherTimer _timer;
    private DesktopPetRawInput _rawInput;
    private DeviceInfo _device;
    private Pose _action;
    private long _actionUntil;
    private long _actionStarted;
    private readonly DesktopPetBehavior _behavior;
    private readonly Random _motionRandom;
    private const double LanePadding = 28;
    private const int CardGapPixels = 2;
    private readonly TranslateTransform _travel = new();
    private readonly Grid _activityLane = new() { ClipToBounds = true };
    private readonly Canvas _impactEffects = new() { IsHitTestVisible = false };
    private readonly Border _cardGap = new();
    private Border _portrait;
    private int _laneRadius;
    private int _roamX;
    private long _lastTapAt = -1;
    private System.Drawing.Point _lastTapPoint;
    private CoopanionMotion _nativeMotion;
    private NativeStage? _nativeStage;
    private long? _lastNativeFrame;
    private bool _nativePressed;
    private Point _nativePointer;
    private DesktopPetCarryOverlay _carryOverlay;
    internal CoopanionMotion.Sample NativeSample { get; private set; }
    private readonly record struct NativeStage(double Offset, double Width, double Height, double Floor, bool Rest);

    private bool _charging;
    private bool _refreshQueued;
    private bool _closed;
    private bool _restoringPosition;
    private bool _dragging;
    private bool _hovering;
    private System.Drawing.Point? _press;
    private bool _doubleClick;
    private Point _dragOrigin;

    internal DesktopPetWindow(BatteryService battery, AppSettings settings)
        : this(battery, settings, Random.Shared)
    {
    }

    internal DesktopPetWindow(BatteryService battery, AppSettings settings, Random random)
    {
        _behavior = new DesktopPetBehavior(random);
        _motionRandom = random;
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

        _bongo = new BongoPetScene(_character);
        var motion = new TransformGroup();
        motion.Children.Add(_petBreath);
        motion.Children.Add(_petTilt);
        motion.Children.Add(_petLift);
        var artwork = new Grid { RenderTransform = motion, RenderTransformOrigin = new Point(.5, FreePetScene.FootAnchor) };
        artwork.Children.Add(_bongo);
        artwork.Children.Add(_free);
        var portrait = _portrait = new Border { Background = Brushes.Transparent, Child = artwork, RenderTransform = _travel };
        portrait.MouseEnter += (_, _) => { _hovering = true; RenderPose(); };
        portrait.MouseLeave += (_, _) =>
        {
            _hovering = false;
            if (_nativeStage.HasValue) _nativeMotion.Pointer("leave", 0, 0, Environment.TickCount64);
            RenderPose();
        };
        portrait.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount > 2) return;
            if (!_settings.DesktopPetDeskMode)
            {
                if (!Motion) return;
                RenderPose();
                if (!_nativeStage.HasValue) return;
                _nativePointer = NativePointer(e.GetPosition(_activityLane));
                if (!_nativeMotion.Pointer("down", _nativePointer.X, _nativePointer.Y, Environment.TickCount64)) return;
                _nativePressed = true;
                if (!portrait.CaptureMouse()) ReleaseNativePointer("drop");
                e.Handled = true;
                return;
            }
            var cursor = System.Windows.Forms.Cursor.Position;
            long now = Environment.TickCount64;
            _doubleClick = e.ClickCount == 2 || (_lastTapAt >= 0 &&
                now - _lastTapAt <= System.Windows.Forms.SystemInformation.DoubleClickTime &&
                Math.Abs(cursor.X - _lastTapPoint.X) <= System.Windows.Forms.SystemInformation.DoubleClickSize.Width / 2 &&
                Math.Abs(cursor.Y - _lastTapPoint.Y) <= System.Windows.Forms.SystemInformation.DoubleClickSize.Height / 2);
            _press = cursor;
            _dragOrigin = PointToScreen(new Point());
            _hovering = false;
            portrait.CaptureMouse();
            RenderPose();
            e.Handled = true;
        };
        portrait.MouseMove += (_, e) =>
        {
            if (!_settings.DesktopPetDeskMode)
            {
                if (!Motion) return;
                if (!_nativeStage.HasValue) return;
                _nativePointer = NativePointer(e.GetPosition(_activityLane));
                if (!_settings.DesktopPetLocked || !_nativePressed)
                    _nativeMotion.Pointer("move", _nativePointer.X, _nativePointer.Y, Environment.TickCount64);
                return;
            }
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
                _actionUntil = 0;
                RenderPose();
            }
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                (int)_dragOrigin.X + dx, (int)_dragOrigin.Y + dy, 0, 0, 0x0015);
        };
        portrait.MouseLeftButtonUp += (_, e) =>
        {
            if (_nativePressed)
            {
                _nativePointer = NativePointer(e.GetPosition(_activityLane));
                ReleaseNativePointer("up");
                portrait.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (_press == null) return;
            bool wasDragging = _dragging;
            EndPointerGesture();
            portrait.ReleaseMouseCapture();
            if (wasDragging) _lastTapAt = -1;
            if (!wasDragging && portrait.IsMouseOver)
            {
                _lastTapAt = _doubleClick ? -1 : Environment.TickCount64;
                _lastTapPoint = System.Windows.Forms.Cursor.Position;
                if (_doubleClick) Play(Pose.Dizzy, DesktopPetBehavior.DizzyDuration);
                else Play(Pose.Click, ClickDuration);
            }
            e.Handled = true;
        };
        portrait.LostMouseCapture += (_, _) =>
        {
            ReleaseNativePointer("drop");
            EndPointerGesture();
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta == 0 || _dragging || _nativePressed) return;
            ChangeSize(_settings.DesktopPetSize + Math.Sign(e.Delta) * 5);
            e.Handled = true;
        };

        _bubble = new Border
        {
            Child = _deviceList, Background = CardSurface, BorderBrush = CardLine,
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 11, 11, 7), Margin = new Thickness(0, 80, 0, 0)
        };
        ApplyCharacter(Characters.FirstOrDefault(item => item.Id == settings.DesktopPetCharacter) ?? Characters[0]);
        var layout = new StackPanel { Orientation = Orientation.Horizontal };
        portrait.VerticalAlignment = VerticalAlignment.Bottom;
        portrait.Margin = new Thickness(0, 2, 0, 1);
        portrait.HorizontalAlignment = HorizontalAlignment.Center;
        _activityLane.Children.Add(portrait);
        _activityLane.Children.Add(_impactEffects);
        _activityLane.LayoutTransform = _size;
        layout.Children.Add(_activityLane);
        layout.Children.Add(_cardGap);
        var card = new Grid { VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, LayoutTransform = _size };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (_settings.DesktopPetLocked || e.GetPosition(card).Y >= 80) return;
            DragMove();
            PlaceOnScreen(remember: true);
            e.Handled = true;
        };
        card.Children.Add(_bubble);
        card.Children.Add(_headerPortrait);
        layout.Children.Add(card);
        Content = layout;

        ContextMenu = new ContextMenu();
        ContextMenu.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/MouseDock;component/Views/DesktopPetMenu.xaml", UriKind.Relative)
        });
        ContextMenu.Opened += (_, _) =>
        {
            _actionUntil = 0;
            BuildMenu();
            RenderPose();
        };
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = IdleInterval };
        _timer.Tick += (_, _) => RenderPose();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
            else
            {
                ReleaseNativePointer("drop");
                CloseCarryOverlay();
                _lastNativeFrame = null;
                SyncInputRegistration();
                _behavior.Reset();
                _timer.Stop();
            }
        };
        StateChanged += (_, _) =>
        {
            if (_closed) return;
            SyncInputRegistration();
            if (IsVisible) Refresh();
        };
        Loaded += (_, _) => PlaceOnScreen(remember: false);
        _battery.Refreshed += OnRefresh;
        _battery.VisibleDevices.CollectionChanged += OnDevicesChanged;
        _battery.ChargeCompleted += OnFull;
        ApplySize();
    }

    private void ApplyCharacter(Character skin)
    {
        _bongo.SetSkin(skin.Id);
        _free.SetSkin(skin.Id);
        _headerPortrait.Source = skin.Id == KeqingId
            ? LoadKeqingCardPortrait()
            : LoadCardPortrait(skin);
        var (width, height, top) = skin.Id switch
        {
            "shin-chan" => (128, 82, 8),
            KeqingId => (128, 84, 6),
            _ => (102, 102, 0)
        };
        _headerPortrait.Width = width;
        _headerPortrait.Height = height;
        _headerPortrait.Margin = new Thickness(0, top, 13, 0);
        _skin = skin;
        _behavior.Reset();
        _actionUntil = 0;
        Title = $"MouseDock · {skin.Name}桌宠";
        _listedDevices = Array.Empty<DeviceInfo>();
        _deviceList.Children.Clear();
    }

    private static BitmapImage LoadArtwork(string file) =>
        new(new Uri($"pack://application:,,,/MouseDock;component/Assets/Characters/{file}"));

    private static ImageSource LoadKeqingCardPortrait()
    {
        var artwork = LoadArtwork("keqing-pet-perched.png");
        var source = new Rect(0, 0, artwork.PixelWidth, artwork.PixelHeight);
        var portrait = new DrawingGroup();
        using (var drawing = portrait.Open())
        {
            // Keep the face behind the rim and place the two existing gloves on top of it.
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 320, 210));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, 320, 185)));
            drawing.DrawImage(artwork, source);
            drawing.Pop();
            DrawGlove("M 87,282 L 91,267 Q 104,252 121,247 Q 125,242 134,245 L 152,253 L 157,267 L 150,279 L 144,283 Z", 17);
            DrawGlove("M 245,282 L 241,264 Q 227,249 217,248 Q 210,241 203,248 L 188,254 L 182,268 L 190,279 L 198,283 Z", -11);

            void DrawGlove(string outline, double x)
            {
                drawing.PushTransform(new TranslateTransform(x, -73));
                drawing.PushClip(Geometry.Parse(outline));
                drawing.DrawImage(artwork, source);
                drawing.Pop();
                drawing.Pop();
            }
        }
        portrait.Freeze();
        return new DrawingImage(portrait);
    }

    private static ImageSource LoadCardPortrait(Character skin)
    {
        var headers = LoadArtwork("desktop-pet-headers.png");
        int headerWidth = headers.PixelWidth / 3;
        int rowHeight = headers.PixelHeight / 2;
        // The sleeve line meets the rim, with the fingers hanging over the front.
        var cell = skin.Id == "shin-chan"
            ? new Int32Rect(64, rowHeight + 176, 416, 264)
            : new Int32Rect(skin.HeaderColumn * headerWidth, 0, headerWidth, rowHeight);
        var portrait = new CroppedBitmap(headers, cell);
        portrait.Freeze();
        return portrait;
    }

    private bool Motion => !_settings.DesktopPetReduceMotion && SystemParameters.ClientAreaAnimation;

    private bool InputReactionEnabled => !_closed && IsVisible && WindowState != WindowState.Minimized &&
        _settings.DesktopPetDeskMode && Motion;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (source == null) return;
        _rawInput = new DesktopPetRawInput(source, OnInputActivity);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SyncInputRegistration();
    }

    private void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        if (Dispatcher.CheckAccess()) RefreshMotion();
        else Dispatcher.BeginInvoke(new Action(RefreshMotion));
    }

    private void RefreshMotion()
    {
        if (!_closed && IsVisible) Refresh(rebuild: true);
    }

    private void SyncInputRegistration()
    {
        _bongo.Visibility = _settings.DesktopPetDeskMode ? Visibility.Visible : Visibility.Collapsed;
        _free.Visibility = _settings.DesktopPetDeskMode ? Visibility.Collapsed : Visibility.Visible;
        bool enabled = InputReactionEnabled;
        _rawInput?.SetEnabled(enabled);

        if (!enabled) _bongo.Clear();
    }

    private void OnInputActivity(DesktopPetInputActivity activity, long now, int detail)
    {
        if (InputReactionEnabled && !_dragging && !_press.HasValue && ContextMenu?.IsOpen != true)
            _bongo.Tap(activity, now, detail);
    }

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
        if (IsVisible && Motion && WindowState != WindowState.Minimized) _timer.Start();
        else _timer.Stop();
        SyncInputRegistration();
        RenderPose();
    }

    private void OnFull(DeviceInfo device)
    {
        if (IsVisible && device.Id == _device?.Id) Play(Pose.Celebrate, 1450);
    }

    private void Play(Pose pose, int duration)
    {
        if (!Motion) return;
        if (!_settings.DesktopPetDeskMode)
        {
            if (_nativeStage.HasValue && pose == Pose.Celebrate) _nativeMotion.Act("happy");
            return;
        }
        long now = Environment.TickCount64;
        if (now < _actionUntil && (_action == pose || _action == Pose.Dizzy && pose == Pose.Click)) return;
        _action = pose;
        _actionStarted = now;
        _actionUntil = now + duration;
        RenderPose();
    }

    private bool IsLow => !_charging && _device is { Connected: true, IsStale: false, IsFullActive: false, Level: >= 0 } &&
        _device.Level <= _settings.LowBatteryWarn;

    private void RenderPose() => RenderPoseAt(Environment.TickCount64);

    internal void RenderPoseAt(long now)
    {
        if (InputReactionEnabled && !_dragging && !_press.HasValue && ContextMenu?.IsOpen != true)
        {
            bool left = DesktopPetRawInput.IsDown(1), right = DesktopPetRawInput.IsDown(2);
            _bongo.SetButtons(_rawInput?.KeyboardDown == true,
                left || right || DesktopPetRawInput.IsDown(4) ||
                DesktopPetRawInput.IsDown(5) || DesktopPetRawInput.IsDown(6));
            if (_rawInput != null) _bongo.UpdateLights(now, _rawInput.IsScanHeld, left, right);
            _bongo.TrackPointer(System.Windows.Forms.Cursor.Position, System.Windows.Forms.SystemInformation.VirtualScreen);
        }
        else if (_settings.DesktopPetDeskMode) _bongo.Clear();

        bool animate = Motion && IsVisible && WindowState != WindowState.Minimized && ContextMenu?.IsOpen != true;
        if (!_settings.DesktopPetDeskMode)
        {
            RenderNativePose(now, animate);
            return;
        }
        _lastNativeFrame = null;
        bool inAction = animate && !_dragging && now >= _actionStarted && now < _actionUntil &&
            (_action != Pose.Transition || _charging) && (_action != Pose.Celebrate || _device?.IsFullActive == true);
        var sample = default(DesktopPetBehavior.Sample);
        if (animate)
        {
            sample = _behavior.Update(now, _hovering || _press.HasValue, _dragging,
                inAction || _charging || IsLow,
                _settings.DesktopPetRandomActions, _settings.DesktopPetRest,
                _skin.HasBlink && _settings.DesktopPetBlink, _settings.DesktopPetMotionSpeed,
                _settings.DesktopPetActionFrequency, 240000, int.MaxValue);
        }
        else _behavior.Reset();

        double tilt = sample.Tilt;
        double lift = sample.Lift;
        var expression = sample.Dizzy ? PetExpression.Dizzy : sample.Wink ? PetExpression.Wink :
            sample.Blink ? PetExpression.Blink :
            sample.State == DesktopPetBehavior.State.Sleep ? PetExpression.Rest : PetExpression.Neutral;
        if (inAction && _action == Pose.Dizzy)
        {
            double progress = (now - _actionStarted) / (double)(_actionUntil - _actionStarted);
            tilt = DesktopPetBehavior.DizzyTilt(progress);
            expression = PetExpression.Dizzy;
        }
        else if (inAction && _action is Pose.Click or Pose.Celebrate)
        {
            expression = PetExpression.Smile;
            double progress = (now - _actionStarted) / (double)(_actionUntil - _actionStarted);
            lift = -(_action == Pose.Celebrate ? 5 : 2) * Math.Pow(Math.Sin(Math.PI * progress), 2);
        }

        _petBreath.ScaleX = _petBreath.ScaleY = 1;
        _petTilt.Angle = tilt;
        _petLift.Y = lift;
        _bongo.ShowPose(expression, now);
    }

    private void RenderNativePose(long now, bool animate)
    {
        if (!animate)
        {
            CloseCarryOverlay();
            _impactEffects.Children.Clear();
            _lastNativeFrame = null;
            if (Motion) return;
            ReleaseNativePointer("drop");
            _nativeStage = null;
            NativeSample = null;
            SetPetPosition(_roamX);
            _petLift.Y = _petTilt.Angle = 0;
            _petBreath.ScaleX = _petBreath.ScaleY = 1;
            _free.SetReaction(null, 0);
            _free.Show(FreePetScene.Pose.Stand, 1, now, false);
            return;
        }

        const double scale = BongoPetScene.SceneWidth / 256d;
        var (left, right) = ActivityBounds();
        bool locked = _settings.DesktopPetLocked;
        if (locked) left = right = _roamX;
        double height = Math.Max(_activityLane.MinHeight, _activityLane.ActualHeight);
        double floor = height - 1 - BongoPetScene.SceneHeight * (1 - FreePetScene.FootAnchor);
        double bodyWidth = 2 * (120 * scale + 8); // Figure extent plus the kit's two edge insets.
        var stage = new NativeStage(
            _activityLane.Width / 2 + left / _size.ScaleX - bodyWidth / 2,
            bodyWidth + (right - left) / _size.ScaleX, height, floor, _settings.DesktopPetRest);
        bool stageChanged = _nativeStage != stage;
        if (stageChanged)
        {
            ReleaseNativePointer("drop");
            _nativeMotion ??= new CoopanionMotion(_motionRandom);
            _nativeStage = stage;
            _nativeMotion.Reset(stage.Width, stage.Height, stage.Floor, scale,
                _activityLane.Width / 2 + _roamX / _size.ScaleX - stage.Offset, stage.Rest);
        }
        double seconds = _lastNativeFrame is long last && !stageChanged
            ? Math.Clamp((now - last) / 1000d, 0, .08) : 0;
        _lastNativeFrame = now;
        if (seconds == 0 && !stageChanged) return;
        double speed = Math.Clamp(_settings.DesktopPetMotionSpeed, 50, 150) / 100d;
        double frequency = Math.Clamp(_settings.DesktopPetActionFrequency, 50, 150) / 100d;
        var frame = NativeSample = _nativeMotion.Advance(seconds, _settings.DesktopPetRandomActions,
            _skin.HasBlink && _settings.DesktopPetBlink, frequency, right > left, speed);
        double x = Math.Clamp(frame.X + stage.Offset - _activityLane.Width / 2, left / _size.ScaleX, right / _size.ScaleX);
        _travel.X = x;
        _roamX = (int)Math.Round(x * _size.ScaleX);
        _petLift.Y = Math.Clamp(frame.Y, -36 / _size.ScaleY, 0);
        _petTilt.Angle = frame.Angle;
        _petBreath.ScaleX = Math.Max(.03, Math.Abs(frame.Width));
        _petBreath.ScaleY = frame.Height;
        bool useLayers = !frame.NativeReaction &&
            (frame.Mode is "walk" or "run" or "dance" or "look" or "crouch" or "air" ||
            frame.Gesture is "wave" or "bow" or "nod" or "shake" or "shiver" or "flap" or "spin");
        _free.SetReaction(frame.NativeReaction ? frame.Face : null, frame.Time,
            frame.NativeReaction ? frame.Sprites : null, useLayers || frame.Mode == "dizzy" ? frame.Rig : null, useLayers);
        _free.Show((FreePetScene.Pose)frame.Pose, frame.Width < 0 ? -1 : 1, now, !frame.NativeReaction);
        DrawImpactDust(frame, stage);
        if (frame.Carried)
        {
            var screenFoot = _activityLane.PointToScreen(new Point(_activityLane.Width / 2 + x, floor + frame.Y));
            var dpi = VisualTreeHelper.GetDpi(_activityLane);
            var pixelScale = new Vector(dpi.DpiScaleX * _size.ScaleX, dpi.DpiScaleY * _size.ScaleY);
            _carryOverlay ??= new DesktopPetCarryOverlay(this, _skin.Id);
            if (_carryOverlay.Show(screenFoot, pixelScale, frame, now)) _free.Opacity = 0;
            else CloseCarryOverlay();
        }
        else CloseCarryOverlay();
    }

    private void DrawImpactDust(CoopanionMotion.Sample frame, NativeStage stage)
    {
        while (_impactEffects.Children.Count > frame.Dust.Length)
            _impactEffects.Children.RemoveAt(_impactEffects.Children.Count - 1);
        for (int i = 0; i < frame.Dust.Length; i++)
        {
            if (i == _impactEffects.Children.Count)
                _impactEffects.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = ImpactDust, StrokeThickness = 2 });
            var ring = (System.Windows.Shapes.Ellipse)_impactEffects.Children[i];
            var dust = frame.Dust[i];
            ring.Width = ring.Height = dust.Radius * 2;
            ring.Opacity = dust.Opacity;
            Canvas.SetLeft(ring, stage.Offset + dust.X - dust.Radius);
            Canvas.SetTop(ring, stage.Floor + dust.Y - dust.Radius);
        }
    }

    private Point NativePointer(Point lanePoint) => new(lanePoint.X - _nativeStage.Value.Offset,
        Math.Min(lanePoint.Y, _nativeStage.Value.Floor));

    private void CloseCarryOverlay()
    {
        _carryOverlay?.Dispose();
        _carryOverlay = null;
        _free.Opacity = 1;
    }

    private void ReleaseNativePointer(string kind)
    {
        if (!_nativePressed) return;
        _nativePressed = false;
        _nativeMotion.Pointer(kind, _nativePointer.X, _nativePointer.Y, Environment.TickCount64);
    }

    private (int Left, int Right) ActivityBounds()
    {
        int radius = (int)Math.Round(_laneRadius * Math.Clamp(_settings.DesktopPetRoamRadius, 0, 192) / 192d);
        return (-radius, radius);
    }

    private void SetPetPosition(int x)
    {
        _roamX = Math.Clamp(x, -_laneRadius, _laneRadius);
        _travel.X = (_settings.DesktopPetDeskMode ? _laneRadius : _roamX) / _size.ScaleX;
    }

    private void SetLaneWidth(int radius)
    {
        _laneRadius = radius;
        _activityLane.MinHeight = BongoPetScene.SceneHeight + 3 + 36 / _size.ScaleY;
        _activityLane.Width = BongoPetScene.SceneWidth + 2 * radius / _size.ScaleX;
        SetPetPosition(_roamX);
    }

    private void RebuildList()
    {
        _deviceList.Children.Clear();
        if (_listedDevices.Length == 0)
        {
            _deviceList.Children.Add(new TextBlock { Text = "未选择显示设备", Foreground = Brushes.LightGray, Margin = new Thickness(9, 8, 0, 8) });
            return;
        }

        var accent = ((SolidColorBrush)CardAccent).Color;
        foreach (var device in _listedDevices)
        {
            var row = new Grid { DataContext = device, Margin = new Thickness(4, 0, 4, 0), MinHeight = 34 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var glyph = new DeviceGlyph { Width = 16, Height = 16, GlyphBrush = device == _device ? CardAccent : CardMuted,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
            row.Children.Add(glyph);
            var name = new TextBlock { Foreground = CardInk, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = device == _device ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 5, 0) };
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Name)));
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var meter = new MeterBar { Height = 4, Width = 24, BarHeight = 4, ShowDot = false,
                TrackBrush = CardLine, Accent = accent, Accent2 = accent, MotionEnabled = Motion };
            meter.SetBinding(MeterBar.ValueProperty, new Binding(nameof(DeviceInfo.LevelFraction)));
            meter.SetBinding(MeterBar.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
            meter.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
            var meterStyle = new Style(typeof(MeterBar));
            var unknown = new DataTrigger { Binding = new Binding(nameof(DeviceInfo.Level)), Value = -1 };
            unknown.Setters.Add(new Setter(VisibilityProperty, Visibility.Hidden));
            meterStyle.Triggers.Add(unknown);
            meter.Style = meterStyle;
            Grid.SetColumn(meter, 2);
            row.Children.Add(meter);

            var reading = new TextBlock { FontSize = 13, FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(reading, FontNumeralAlignment.Tabular);
            reading.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.LevelText)));
            PercentageWarningConverter.Bind(reading, CardInk, _settings.LowBatteryWarn);
            reading.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
            Grid.SetColumn(reading, 3);
            row.Children.Add(reading);
            var item = new Button
            {
                Content = row, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(BackgroundProperty, CardLine));
            template.Triggers.Add(hover);
            item.Template = template;
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
        App.ApplyCompactMenuTheme(ContextMenu);
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

        var display = new MenuItem { Header = "显示设备" };
        foreach (var device in _battery.Devices.Where(d => d.Connected && d.RecognitionEnabled && !d.Hidden))
        {
            var item = new MenuItem { Header = device.Name, IsCheckable = true, IsChecked = device.Selected, StaysOpenOnClick = true };
            item.Click += (_, _) => _battery.SetSelected(device, item.IsChecked);
            display.Items.Add(item);
        }
        display.IsEnabled = display.HasItems;
        ContextMenu.Items.Add(display);

        var sizes = new MenuItem { Header = "桌宠大小" };
        foreach (int size in new[] { 50, 75, 100, 125, 150 })
            Add(sizes.Items, $"{size}%", () => ChangeSize(size), _settings.DesktopPetSize == size);
        ContextMenu.Items.Add(sizes);

        ContextMenu.Items.Add(new Separator());
        Add(ContextMenu.Items, "锁定位置", () => { _settings.DesktopPetLocked = !_settings.DesktopPetLocked; App.SaveSettings(); }, _settings.DesktopPetLocked);
        Add(ContextMenu.Items, "减少动态效果", () => { _settings.DesktopPetReduceMotion = !_settings.DesktopPetReduceMotion; _actionUntil = 0; SaveAndRefresh(); }, _settings.DesktopPetReduceMotion);
        Add(ContextMenu.Items, "键鼠动作联动", () =>
        {
            ReleaseNativePointer("drop");
            CloseCarryOverlay();
            _impactEffects.Children.Clear();
            _nativeStage = null;
            NativeSample = null;
            _settings.DesktopPetDeskMode = !_settings.DesktopPetDeskMode;
            SetPetPosition(_roamX);
            _actionUntil = 0;
            SyncInputRegistration();
            RenderPose();
            App.SaveSettings();
        }, _settings.DesktopPetDeskMode);
        Add(ContextMenu.Items, "满电提醒（全局）", () => { _settings.NotifyChargeCompleted = !_settings.NotifyChargeCompleted; App.SaveSettings(); }, _settings.NotifyChargeCompleted);
        ContextMenu.Items.Add(new Separator());
        App.AddDeviceMenuActions(ContextMenu.Items, this);
        ContextMenu.Items.Add(new Separator());
        Add(ContextMenu.Items, "设置…", () => App.ShowSettings(this));
        Add(ContextMenu.Items, "隐藏桌宠", App.ToggleCurrentSurface);
    }

    internal static void WriteCoopanionFigures(string file) =>
        CoopanionDesktopPet.ExportFigures(file, Characters.Select(character => (character.Id, character.Name)));

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
        _behavior.Land(Environment.TickCount64);
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

    private void ApplySize()
    {
        _size.ScaleX = _size.ScaleY = Math.Clamp(_settings.DesktopPetSize, 50, 150) / 100d;
        _cardGap.Width = CardGapPixels / VisualTreeHelper.GetDpi(this).DpiScaleX;
        SetLaneWidth((int)Math.Round(LanePadding * _size.ScaleX));
    }

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
        var dpi = VisualTreeHelper.GetDpi(this);
        double gap = CardGapPixels / dpi.DpiScaleX;
        int laneRadius = Math.Clamp((int)((area.Width / dpi.DpiScaleX -
            (BongoPetScene.SceneWidth + _bubble.DesiredSize.Width) * _size.ScaleX - CardGapPixels / dpi.DpiScaleX) / 2),
            0, (int)Math.Round(LanePadding * _size.ScaleX));
        if (_laneRadius != laneRadius || Math.Abs(_cardGap.Width - gap) > .001)
        {
            _cardGap.Width = gap;
            SetLaneWidth(laneRadius);
            UpdateLayout();
        }
        Point origin = PointToScreen(new Point());
        Point corner = PointToScreen(new Point(ActualWidth, ActualHeight));
        int spanX = Math.Max(0, area.Width - (int)Math.Ceiling(corner.X - origin.X));
        int spanY = Math.Max(0, area.Height - (int)Math.Ceiling(corner.Y - origin.Y));
        int x = remember ? Math.Clamp((int)origin.X - area.Left, 0, spanX) :
            (int)Math.Round(spanX * Normalized(_settings.DesktopPetX, 1));
        int y = remember ? Math.Clamp((int)origin.Y - area.Top, 0, spanY) :
            (int)Math.Round(spanY * Normalized(_settings.DesktopPetY, .75));
        int snapX = (int)Math.Round(16 * dpi.DpiScaleX);
        int snapY = (int)Math.Round(16 * dpi.DpiScaleY);
        if (x <= snapX) x = 0;
        else if (spanX - x <= snapX) x = spanX;
        if (y <= snapY) y = 0;
        else if (spanY - y <= snapY) y = spanY;

        _restoringPosition = true;
        try
        {
            if (!SetWindowPos(handle, IntPtr.Zero, area.Left + x, area.Top + y, 0, 0, 0x0015)) return;
        }
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
        CloseCarryOverlay();
        SyncInputRegistration();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _timer.Stop();
        _battery.Refreshed -= OnRefresh;
        _battery.VisibleDevices.CollectionChanged -= OnDevicesChanged;
        _battery.ChargeCompleted -= OnFull;
        base.OnClosed(e);
    }
}
