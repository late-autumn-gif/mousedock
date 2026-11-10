using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views
{
    public sealed class BackgroundEditorWindow : Window
    {
        public AppSettings Draft { get; }
        private readonly Border _preview;
        private readonly Slider _zoom, _depth;
        private Point _start;
        private double _startX, _startY;
        private BitmapSource _image;
        private ThemePreset _theme;
        private string _themePath;

        public BackgroundEditorWindow(AppSettings settings)
        {
            Draft = settings.Clone();
            Title = "图片背景";
            Width = 376;
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            var theme = Palette.ResolveTheme(Draft, out _);
            Background = new SolidColorBrush(theme.BgTop);
            Foreground = new SolidColorBrush(theme.Text);
            var panel = new StackPanel { Margin = new Thickness(16) };
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _preview = new Border
            {
                Name = "BackgroundPreview", Width = 320, Height = 240, CornerRadius = new CornerRadius(8),
                ClipToBounds = true, Cursor = Cursors.SizeAll
            };
            var main = App.Dock?.Content as FrameworkElement;
            if (main != null)
            {
                if (main.ActualWidth <= 1 || main.ActualHeight <= 1)
                    main.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double width = main.ActualWidth > 1 ? main.ActualWidth : main.DesiredSize.Width;
                double height = main.ActualHeight > 1 ? main.ActualHeight : main.DesiredSize.Height;
                if (width > 0 && height > 0)
                {
                    double scale = Math.Min(320 / width, 300 / height);
                    _preview.Width = width * scale;
                    _preview.Height = height * scale;
                }
            }
            NameScope.SetNameScope(this, new NameScope());
            RegisterName(_preview.Name, _preview);
            _preview.SizeChanged += (_, __) =>
                _preview.Clip = new RectangleGeometry(new Rect(_preview.RenderSize), 8, 8);
            panel.Children.Add(_preview);
            _preview.MouseLeftButtonDown += (_, e) =>
            {
                _start = e.GetPosition(_preview);
                _startX = Draft.BackgroundPositionX;
                _startY = Draft.BackgroundPositionY;
                _preview.CaptureMouse();
                e.Handled = true;
            };
            _preview.MouseLeftButtonUp += (_, __) => _preview.ReleaseMouseCapture();
            _preview.MouseMove += (_, e) =>
            {
                if (!_preview.IsMouseCaptured) return;
                var image = _image;
                if (image == null) return;
                var current = e.GetPosition(_preview);
                double scale = Math.Max(_preview.ActualWidth / image.PixelWidth, _preview.ActualHeight / image.PixelHeight) *
                    PictureBackground.Clamp(Draft.BackgroundZoom, 1, 3, 1);
                double overflowX = image.PixelWidth * scale - _preview.ActualWidth;
                double overflowY = image.PixelHeight * scale - _preview.ActualHeight;
                Draft.BackgroundPositionX = overflowX > .01 ? Math.Clamp(_startX - (current.X - _start.X) / overflowX, 0, 1) : .5;
                Draft.BackgroundPositionY = overflowY > .01 ? Math.Clamp(_startY - (current.Y - _start.Y) / overflowY, 0, 1) : .5;
                RefreshPreview();
            };
            _preview.MouseWheel += (_, e) => { _zoom.Value += e.Delta > 0 ? .1 : -.1; e.Handled = true; };
            _zoom = AddSlider(panel, "缩放", 1, 3, PictureBackground.Clamp(Draft.BackgroundZoom, 1, 3, 1),
                value => Draft.BackgroundZoom = value);
            _depth = AddSlider(panel, "深浅", 0, 80, PictureBackground.Clamp(Draft.BackgroundDepth, 0, .8, .35) * 100,
                value => Draft.BackgroundDepth = value / 100);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            panel.Children.Add(actions);
            var choose = new Button { Content = "选择图片", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
            choose.Click += (_, __) => ChooseImage();
            actions.Children.Add(choose);
            var reset = new Button { Content = "复位", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
            reset.Click += (_, __) => ResetComposition();
            actions.Children.Add(reset);
            var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
            actions.Children.Add(cancel);
            var done = new Button { Content = "完成", IsDefault = true, Padding = new Thickness(10, 5, 10, 5) };
            done.Click += (_, __) => { if (_image != null) DialogResult = true; };
            actions.Children.Add(done);
            Loaded += (_, __) => RefreshPreview();
            RefreshPreview();
        }

        public void ResetComposition()
        {
            Draft.BackgroundPositionX = Draft.BackgroundPositionY = .5;
            Draft.BackgroundZoom = 1;
            Draft.BackgroundDepth = .35;
            _zoom.Value = 1;
            _depth.Value = 35;
            RefreshPreview();
        }

        private Slider AddSlider(Panel panel, string label, double min, double max, double value, Action<double> changed)
        {
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, Width = 48, VerticalAlignment = VerticalAlignment.Center });
            var number = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(number, Dock.Right);
            row.Children.Add(number);
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center };
            number.Text = label == "缩放" ? $"{value:0.0}×" : $"{value:0}%";
            slider.ValueChanged += (_, __) =>
            {
                changed(slider.Value);
                number.Text = label == "缩放" ? $"{slider.Value:0.0}×" : $"{slider.Value:0}%";
                RefreshPreview();
            };
            row.Children.Add(slider);
            panel.Children.Add(row);
            return slider;
        }

        private void ChooseImage()
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            { Title = "选择图片背景", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                Draft.CustomBackground = PictureBackground.Import(picker.FileName);
                Draft.CustomAccent = "";
                Draft.BackgroundPositionX = Draft.BackgroundPositionY = .5;
                _zoom.Value = 1;
                RefreshPreview();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法使用图片", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void RefreshPreview()
        {
            if (_theme == null || _themePath != Draft.CustomBackground)
            {
                _theme = Palette.ResolveTheme(Draft, out _);
                _themePath = Draft.CustomBackground;
            }
            Background = new SolidColorBrush(_theme.BgTop);
            Foreground = new SolidColorBrush(_theme.Text);
            _image = PictureBackground.Apply(_preview, "", new SolidColorBrush(_theme.Surface), Draft, _theme);
        }
    }
}
