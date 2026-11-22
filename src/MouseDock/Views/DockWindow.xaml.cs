using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

namespace MouseDock.Views
{
    public partial class DockWindow : Window
    {
        private readonly Brush _themeBackground;
        public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
            nameof(AccentColor), typeof(Color), typeof(DockWindow), new PropertyMetadata(Color.FromRgb(0xB4, 0x9C, 0xFF)));

        public static readonly DependencyProperty Accent2ColorProperty = DependencyProperty.Register(
            nameof(Accent2Color), typeof(Color), typeof(DockWindow), new PropertyMetadata(Color.FromRgb(0xF0, 0xA6, 0xD0)));

        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(DockWindow), new PropertyMetadata(Brushes.Gainsboro));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(DockWindow),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255))));

        public static readonly DependencyProperty LowBatteryThresholdProperty = DependencyProperty.Register(
            nameof(LowBatteryThreshold), typeof(int), typeof(DockWindow), new PropertyMetadata(20));

        public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
        public Color Accent2Color { get => (Color)GetValue(Accent2ColorProperty); set => SetValue(Accent2ColorProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public int LowBatteryThreshold { get => (int)GetValue(LowBatteryThresholdProperty); set => SetValue(LowBatteryThresholdProperty, value); }

        public DockWindow() : this(new WallpaperEngineSource().ReadAsync) { }

        internal DockWindow(Func<WallpaperMonitor, CancellationToken, Task<WallpaperContent>> readWallpaper)
        {
            InitializeComponent();
            _themeBackground = Root.Background;
            InitializeWallpaper(readWallpaper);
            LowBatteryThreshold = App.Settings.LowBatteryWarn;
            App.Battery.Refreshed += (_, __) => RefreshDevices();
            App.Battery.SourceChanged += (_, __) => RefreshDevices();
            RefreshDevices();
            Loaded += OnLoaded;
            PreviewKeyDown += OnDockKeyDown;
            UpdateLayout();
        }

        private Window _legacyDpiWindow;
        private void OnDpiClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: DeviceInfo device }) return;
            if (App.SupportsDpi(device)) { App.ShowDpi(device); return; }
            _legacyDpiWindow?.Close();
            var theme = App.Theme;
            var actionStyle = (Style)FindResource("DpiAction");
            var hoverBrush = new SolidColorBrush(Palette.Mix(theme.Surface, theme.Text, .12));
            var activeBrush = new SolidColorBrush(ColorContrast.Ratio(theme.Accent, theme.Surface) >= 4.5 ? theme.Accent : theme.Text);
            var body = new StackPanel { Margin = new Thickness(22) };
            var status = new TextBlock { Text = device.DpiLabel, FontSize = 24, Margin = new Thickness(0,12,0,16) };
            body.Children.Add(new TextBlock { Text = device.Name + " · DPI 设置", FontSize = 16, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(status);
            var state = device.DpiSettings;
            bool writable = !App.Settings.SafeMode && device.CanChangeDpi && state != null;
            body.Children.Add(new TextBlock { Text = writable ? "选择设备已有的 DPI 档位" : "当前仅显示读数；设备未适配、离线或安全模式下不可更改。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
            var choices = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            body.Children.Add(choices);
            if (state != null)
            for (int index = 0; index < state.Count; index++)
            {
                int target = index;
                var choice = new Button
                {
                    Content = state.OptionLabel(index), IsEnabled = writable, Style = actionStyle,
                    BorderBrush = hoverBrush, Margin = new Thickness(4), Padding = new Thickness(5, 8, 5, 8)
                };
                SetActiveStage(choice, index == state.Index);
                choice.Click += async (_, _) =>
                {
                    if (App.Settings.SafeMode || !device.CanChangeDpi || !ReferenceEquals(device.DpiSettings, state) || !App.Battery.Devices.Contains(device))
                    { status.Text = "设备状态已变化，请重新打开设置"; return; }
                    if (target == state.Index) return;
                    choices.IsEnabled = false;
                    status.Text = "正在确认 DPI…";
                    try
                    {
                        var preferences = App.Battery.Preferences;
                        string id = device.Id;
                        var result = await Task.Run(() => HidDiscovery.ReadOrChangeMouseDpi(preferences, id, new(state, target)));
                        if (result != null)
                        {
                            device.Dpi = result.Dpi;
                            device.DpiSettings = result.DpiSettings;
                            for (int i = 0; i < choices.Children.Count; i++)
                                SetActiveStage((Button)choices.Children[i], i == result.DpiSettings?.Index);
                        }
                        status.Text = result == null ? "未能确认更改，请刷新后重试" : device.DpiLabel;
                    }
                    catch (Exception) { status.Text = "未能确认更改，请刷新后重试"; }
                    finally { choices.IsEnabled = true; App.Battery.Poll(); }
                };
                choices.Children.Add(choice);
            }
            var close = new Button
            {
                Content = "关闭", Style = actionStyle, BorderBrush = hoverBrush,
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            body.Children.Add(close);
            _legacyDpiWindow = new Window { Title = device.Name + " · DPI 设置", Owner = this, Width = 430, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(theme.Surface), Foreground = new SolidColorBrush(theme.Text), Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, FontFamily = FontFamily };
            var popup = _legacyDpiWindow;
            close.Click += (_, _) => popup.Close();
            popup.Closed += (_, _) => { if (ReferenceEquals(_legacyDpiWindow, popup)) _legacyDpiWindow = null; };
            popup.Show();

            void SetActiveStage(Button button, bool active)
            {
                if (active) button.Foreground = activeBrush;
                else button.ClearValue(ForegroundProperty);
                button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "当前 DPI 档位" : "");
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var slide = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Root.BeginAnimation(OpacityProperty, fade);
            RootTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        /// <summary>设置窗口背景图；空路径回到预设外观。</summary>
        public void ApplyBackground(string path)
        {
            ApplyTheme(App.Theme, false);
            PictureBackground.Apply(Root, path, _themeBackground, App.Settings, App.Theme);
            UpdateWallpaperActivation();
            if (_bareDensity) ApplyBareBackground();
            else PaintWallpaper();
        }

        /// <summary>卡片窗样式：standard 标准 / compact 紧凑 / bare 极简（去底色去圆角阴影）。</summary>
        public void ApplyDensity(string style)
        {
            style = (style ?? "standard").ToLowerInvariant();
            _bareDensity = style == "bare";
            Root.Width = style == "compact" ? 288 : 316;
            switch (style)
            {
                case "compact":
                    Root.Padding = new Thickness(9, 7, 9, 6);
                    Root.CornerRadius = new CornerRadius(12);
                    Root.BorderThickness = new Thickness(0);
                    RootShadow.Opacity = 0.35;
                    break;
                case "bare":
                    Root.Padding = new Thickness(10, 8, 10, 7);
                    Root.CornerRadius = new CornerRadius(0);
                    Root.BorderThickness = new Thickness(0);
                    RootShadow.Opacity = 0;
                    UpdateWallpaperActivation();
                    ApplyBareBackground();
                    return;
                default:
                    Root.Padding = new Thickness(15, 12, 15, 10);
                    Root.CornerRadius = new CornerRadius(16);
                    Root.BorderThickness = new Thickness(1);
                    RootShadow.Opacity = 0.45;
                    break;
            }
            ApplyBackground(App.Settings.DockBackground);
        }

        private void ApplyBareBackground()
        {
            var brush = new SolidColorBrush(App.Theme.Surface);
            PictureBackground.SetWallpaper(Root, null, brush);
            Root.Background = brush;
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            text.Freeze();
            Foreground = text;
            LowBatteryThreshold = App.Settings.LowBatteryWarn;
            TextElement.SetForeground(Root, text);

            GlyphBrush = new SolidColorBrush(Palette.Mix(theme.Text, theme.Accent, 0.35));
            GlyphBrush.Freeze();
            AccentColor = theme.Accent;
            Accent2Color = theme.Accent2;
            Root.BorderBrush = new SolidColorBrush(theme.Border.A == 0 ? Palette.Mix(theme.Surface, theme.Text, .15) : theme.Border);
            ThemeNameText.Text = theme.Name;
            ThemeNameText.Foreground = new SolidColorBrush(theme.TextDim);
            FooterText.Foreground = new SolidColorBrush(theme.TextDim);
            SourceText.Foreground = new SolidColorBrush(theme.TextDim);

            var accentBrush = new SolidColorBrush(theme.Accent);
            accentBrush.Freeze();
            LiveDot.Fill = accentBrush;

            RootShadow.Color = theme.IsLight ? Color.FromRgb(0x8B, 0x93, 0xA6) : Colors.Black;
            RootShadow.Opacity = theme.IsLight ? 0.18 : 0.45;

            TrackBrush = new SolidColorBrush(theme.IsLight
                ? Color.FromArgb(0x22, 0, 0, 0)
                : Color.FromArgb(0x1A, 255, 255, 255));

            var dim = theme.IsLight ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x12, 255, 255, 255);
            SourceChip.Background = new SolidColorBrush(dim);
            GearButton.Background = new SolidColorBrush(theme.IsLight
                ? Color.FromArgb(0x18, 0, 0, 0)
                : Color.FromArgb(0x14, 255, 255, 255));

            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(260);
                BgTopStop.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty,
                    new ColorAnimation(theme.BgTop, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                BgBottomStop.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty,
                    new ColorAnimation(theme.BgBottom, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                LogoStop1.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty, new ColorAnimation(theme.Accent, dur));
                LogoStop2.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty, new ColorAnimation(theme.Accent2, dur));
            }
            else
            {
                BgTopStop.BeginAnimation(GradientStop.ColorProperty, null);
                BgBottomStop.BeginAnimation(GradientStop.ColorProperty, null);
                LogoStop1.BeginAnimation(GradientStop.ColorProperty, null);
                LogoStop2.BeginAnimation(GradientStop.ColorProperty, null);
                BgTopStop.Color = theme.BgTop;
                BgBottomStop.Color = theme.BgBottom;
                LogoStop1.Color = theme.Accent;
                LogoStop2.Color = theme.Accent2;
            }
        }

        private const int WM_DEVICECHANGE = 0x0219;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);
        private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                // 桌件形态：不占任务栏、不进 Alt+Tab —— 就没有「关掉任务栏按钮 = 关掉面板」这回事
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

                var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this);
                source?.AddHook(DeviceChangeHook);
            }
            catch { }
        }

        private IntPtr DeviceChangeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE) App.ScheduleDeviceRefresh();
            if (TaskbarCreated != 0 && msg == TaskbarCreated) App.RestoreTrayIcon();
            return IntPtr.Zero;
        }

        public void RefreshDevices()
        {
            
            if (DeviceList.ItemsSource == null) DeviceList.ItemsSource = App.Battery.VisibleDevices;
            string label = App.Battery.Source?.Label ?? "自动检测";
            int displayed = App.Battery.VisibleDevices.Count;
            if (DetailPanel.DataContext is DeviceInfo target && !App.Battery.VisibleDevices.Contains(target))
                DetailPanel.Visibility = Visibility.Collapsed;
            FooterText.Text = $"{displayed} 台设备 · 右键切换样式";
            PinButton.ToolTip = App.Settings.AlwaysOnTop ? "取消始终置顶" : "始终置顶";
            PinButton.Background = App.Settings.AlwaysOnTop ? TrackBrush : Brushes.Transparent;
            SourceText.Text = $"{label} · {displayed} 台显示";
            var accent = new SolidColorBrush(App.Theme.Accent);
            LiveDot.Fill = accent;
        }


        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            App.Settings.AlwaysOnTop = !App.Settings.AlwaysOnTop;
            App.ApplyWindowBehavior();
            App.SaveSettings();
            PinButton.ToolTip = App.Settings.AlwaysOnTop ? "取消始终置顶" : "始终置顶";
            PinButton.Background = App.Settings.AlwaysOnTop ? TrackBrush : Brushes.Transparent;
        }
        private void OnSettingsClick(object sender, RoutedEventArgs e) => App.ShowSettings(this);
        private void OnHideClick(object sender, RoutedEventArgs e) => Hide();
        private void OnCloseDetailClick(object sender, RoutedEventArgs e) => DetailPanel.Visibility = Visibility.Collapsed;
        private void OnDeviceManagementClick(object sender, RoutedEventArgs e)
        {
            App.ShowSettings(this);
            App.SettingsView.SelectSection("devices");
        }
        private void OnDockKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { DetailPanel.Visibility = Visibility.Collapsed; e.Handled = true; }
        }
        private Point? _cardPointerStart;
        private DeviceInfo _cardPointerDevice;

        internal bool BeginCardGesture(DependencyObject source, Point point)
        {
            _cardPointerStart = null;
            _cardPointerDevice = null;
            for (var node = source; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase || node is TextBox ||
                    node is System.Windows.Controls.Primitives.Selector || node is System.Windows.Controls.Primitives.ScrollBar)
                    return false;
                if (node is FrameworkElement { DataContext: DeviceInfo device }) _cardPointerDevice = device;
                if (ReferenceEquals(node, Root)) break;
            }
            _cardPointerStart = point;
            return true;
        }
        internal bool TryStartCardDrag(Point point)
        {
            if (_cardPointerStart is not Point start ||
                (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                 Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)) return false;
            _cardPointerStart = null;
            _cardPointerDevice = null;
            return true;
        }
        internal void CompleteCardGesture()
        {
            if (_cardPointerStart != null && _cardPointerDevice != null)
            {
                DetailPanel.DataContext = _cardPointerDevice;
                DetailPanel.Visibility = Visibility.Visible;
            }
            _cardPointerStart = null;
            _cardPointerDevice = null;
        }
        private void OnCardPointerDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            Point start = e.GetPosition(this);
            if (!BeginCardGesture(e.OriginalSource as DependencyObject, start)) return;
            e.Handled = true;
            Activate();
            double left = Left, top = Top;
            try
            {
                // Enter the native move loop on button-down, before queued mouse-up events.
                DragMove();
            }
            catch (InvalidOperationException)
            {
                _cardPointerStart = null;
                _cardPointerDevice = null;
                return;
            }
            bool moved = TryStartCardDrag(new Point(start.X + Left - left, start.Y + Top - top));
            if (moved && ReferenceEquals(App.Dock, this)) App.SaveWindowPositions();
            CompleteCardGesture();
        }
        private void OnDeviceKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) || sender is not FrameworkElement { DataContext: DeviceInfo device }) return;
            if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;
            DetailPanel.DataContext = device;
            DetailPanel.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        private void OnGearClick(object sender, MouseButtonEventArgs e)
        {
            // 齿轮必须在按下阶段就拦下来，否则会被窗口的 DragMove 吃
            e.Handled = true;
            App.ShowSettings(this);
        }

        private void OnContextMenu(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            App.ShowQuickMenu(this);
        }

    }

    public sealed class PercentageWarningConverter : IMultiValueConverter
    {
        private static readonly Brush WarningBrush = CreateWarningBrush();

        private static Brush CreateWarningBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
            brush.Freeze();
            return brush;
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values[0] is int level && level >= 0 && values[1] is int threshold && level <= threshold
                ? WarningBrush
                : values[2];

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
