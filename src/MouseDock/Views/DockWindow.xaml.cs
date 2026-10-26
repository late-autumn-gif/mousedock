using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views
{
    public partial class DockWindow : Window
    {
        public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
            nameof(AccentColor), typeof(Color), typeof(DockWindow), new PropertyMetadata(Color.FromRgb(0xB4, 0x9C, 0xFF)));

        public static readonly DependencyProperty Accent2ColorProperty = DependencyProperty.Register(
            nameof(Accent2Color), typeof(Color), typeof(DockWindow), new PropertyMetadata(Color.FromRgb(0xF0, 0xA6, 0xD0)));

        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(DockWindow), new PropertyMetadata(Brushes.Gainsboro));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(DockWindow),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255))));

        public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
        public Color Accent2Color { get => (Color)GetValue(Accent2ColorProperty); set => SetValue(Accent2ColorProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

        public DockWindow()
        {
            InitializeComponent();
            DeviceList.ItemsSource = App.Battery.Devices;
            App.Battery.Refreshed += (_, __) => UpdateSourceChip();
            App.Battery.SourceChanged += (_, __) => UpdateSourceChip();
            UpdateSourceChip();
            Loaded += OnLoaded;
            UpdateLayout();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var slide = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Root.BeginAnimation(OpacityProperty, fade);
            RootTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            text.Freeze();
            TextElement.SetForeground(Root, text);

            GlyphBrush = new SolidColorBrush(Palette.Mix(theme.Text, theme.Accent, 0.35));
            GlyphBrush.Freeze();
            AccentColor = theme.Accent;
            Accent2Color = theme.Accent2;
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
                BgTopStop.Color = theme.BgTop;
                BgBottomStop.Color = theme.BgBottom;
                LogoStop1.Color = theme.Accent;
                LogoStop2.Color = theme.Accent2;
            }



            // 低电量行提示：把 <20% 的百分比染成警示色
            foreach (var tb in FindVisualChildren<TextBlock>(DeviceList))
            {
                if (tb.Name != "Pct") continue;
                if (tb.DataContext is DeviceInfo dev)
                    tb.Foreground = (dev.HasLevel && dev.Level <= 20)
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A))
                        : text;
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

        private void UpdateSourceChip()
        {
            string label = App.Battery.Source?.Label ?? "自动检测";
            int online = App.Battery.Devices.Count(d => d.Connected);
            SourceText.Text = $"{label} · {online} 台在线";
            var accent = new SolidColorBrush(App.Theme.Accent);
            LiveDot.Fill = accent;
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // 点在齿轮等可交互元素上时不拖动
            if (e.OriginalSource is DependencyObject src && IsInside(src, GearButton)) return;

            try { DragMove(); } catch { }
        }

        private static bool IsInside(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (ReferenceEquals(child, parent)) return true;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
            return false;
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

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null) yield break;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }
    }
}
