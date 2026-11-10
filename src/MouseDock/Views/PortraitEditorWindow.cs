using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views;

public sealed class PortraitEditorWindow : Window
{
    public AppSettings Draft { get; }
    private readonly CharacterBatteryIcon _preview;
    private readonly Slider _zoom;
    private Point _start, _origin;
    private BitmapSource _photo;

    public PortraitEditorWindow(AppSettings settings)
    {
        Draft = settings.Clone();
        Title = "照片";
        Width = 312;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 48);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var theme = App.Theme;
        Background = new SolidColorBrush(theme.BgTop);
        Foreground = new SolidColorBrush(theme.Text);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _preview = new CharacterBatteryIcon { Width = 240, Height = 260, Character = "photo", Level = 76, Cursor = Cursors.SizeAll };
        panel.Children.Add(_preview);
        _preview.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_preview);
            _origin = new Point(Draft.PhotoOffsetX, Draft.PhotoOffsetY);
            _preview.CaptureMouse();
        };
        _preview.MouseLeftButtonUp += (_, __) => _preview.ReleaseMouseCapture();
        _preview.MouseMove += (_, e) =>
        {
            if (!_preview.IsMouseCaptured || _photo == null) return;
            var delta = e.GetPosition(_preview) - _start;
            double width = _preview.ActualWidth - 6, height = _preview.ActualHeight - 4;
            double scale = Math.Max(width / _photo.PixelWidth, height / _photo.PixelHeight) * Draft.PhotoZoom;
            double extraX = _photo.PixelWidth * scale - width, extraY = _photo.PixelHeight * scale - height;
            Draft.PhotoOffsetX = extraX > .01 ? Math.Clamp(_origin.X + delta.X / extraX, -.5, .5) : 0;
            Draft.PhotoOffsetY = extraY > .01 ? Math.Clamp(_origin.Y + delta.Y / extraY, -.5, .5) : 0;
            Refresh();
        };
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(new TextBlock { Text = "缩放", Width = 42, VerticalAlignment = VerticalAlignment.Center });
        _zoom = new Slider { Minimum = 1, Maximum = 4,
            Value = double.IsFinite(Draft.PhotoZoom) ? Math.Clamp(Draft.PhotoZoom, 1, 4) : 1 };
        _zoom.ValueChanged += (_, __) => { Draft.PhotoZoom = _zoom.Value; Refresh(); };
        row.Children.Add(_zoom);
        panel.Children.Add(row);
        _preview.MouseWheel += (_, e) => { _zoom.Value += e.Delta > 0 ? .1 : -.1; e.Handled = true; };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        var choose = Action("选择照片");
        choose.Click += (_, __) => Choose();
        actions.Children.Add(choose);
        var cancel = Action("取消");
        cancel.IsCancel = true;
        actions.Children.Add(cancel);
        var done = Action("完成");
        done.IsDefault = true;
        done.Click += (_, __) => DialogResult = true;
        actions.Children.Add(done);
        panel.Children.Add(actions);
        LoadPhoto();
    }

    private static Button Action(string label) => new()
    { Content = label, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(4, 0, 0, 0) };

    private void Choose()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择照片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock", "portraits");
            Draft.PhotoAsset = ImageImport.Import(picker.FileName, directory, true);
            Draft.IconBackground = "";
            Draft.PhotoOffsetX = Draft.PhotoOffsetY = 0;
            Draft.PhotoZoom = _zoom.Value = 1;
            LoadPhoto();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法使用照片", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void LoadPhoto()
    {
        _preview.PhotoPath = string.IsNullOrWhiteSpace(Draft.IconBackground) ? Draft.PhotoAsset : Draft.IconBackground;
        _photo = Palette.Load(_preview.PhotoPath, 1024);
        Refresh();
    }

    private void Refresh()
    {
        _preview.PhotoZoom = Draft.PhotoZoom;
        _preview.PhotoOffsetX = Draft.PhotoOffsetX;
        _preview.PhotoOffsetY = Draft.PhotoOffsetY;
        _preview.InvalidateVisual();
    }
}
