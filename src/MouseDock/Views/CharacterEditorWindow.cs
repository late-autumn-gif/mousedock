using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views;

public sealed class CharacterEditorWindow : Window
{
    public AppSettings Draft { get; }
    public bool ShowAfterApply { get; private set; }
    private readonly CharacterBatteryIcon _preview;
    private readonly Slider _zoom;
    private readonly ComboBox _characters;
    private Point _start, _origin;
    private BitmapSource _photo;
    private string _photoPath;

    public CharacterEditorWindow(AppSettings settings) : this(settings, null) { }

    public CharacterEditorWindow(AppSettings settings, string mode)
    {
        Draft = settings.Clone();
        if (mode == "portrait") Draft.IconStyle = "portrait";
        else if (Draft.IconStyle is not ("character" or "portrait")) Draft.IconStyle = "character";
        Title = "角色与照片";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var theme = App.Theme;
        Background = new SolidColorBrush(theme.BgTop);
        Foreground = new SolidColorBrush(theme.Text);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = new ScrollViewer { Content = panel, Background = Background, Foreground = Foreground,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _preview = new CharacterBatteryIcon { Width = 120, Height = 120, Level = 76, Cursor = Cursors.SizeAll };
        var devices = App.Battery?.VisibleDevices.Take(CharacterBatteryIcon.MaximumPhotoDevices).ToList();
        if (devices?.Count > 0)
        {
            _preview.Devices = devices;
            var first = devices[0];
            _preview.SetBinding(CharacterBatteryIcon.LevelProperty, new System.Windows.Data.Binding(nameof(DeviceInfo.Level)) { Source = first });
            _preview.SetBinding(CharacterBatteryIcon.ChargingProperty, new System.Windows.Data.Binding(nameof(DeviceInfo.IsChargingActive)) { Source = first });
            _preview.SetBinding(CharacterBatteryIcon.StaleProperty, new System.Windows.Data.Binding(nameof(DeviceInfo.IsStale)) { Source = first });
            _preview.SetBinding(CharacterBatteryIcon.KindProperty, new System.Windows.Data.Binding(nameof(DeviceInfo.Kind)) { Source = first });
        }
        panel.Children.Add(new Border { Child = new Viewbox { Width = 320, Height = 300, Stretch = Stretch.Uniform, Child = _preview },
            Background = new SolidColorBrush(theme.Surface), CornerRadius = new CornerRadius(8) });
        _characters = new ComboBox { Margin = new Thickness(0, 12, 0, 6) };
        foreach (var (key, label) in new[] { ("rei", "绫波丽"), ("asuka", "明日香"), ("peek", "趴趴款"), ("photo", "我的照片") })
            _characters.Items.Add(new ComboBoxItem { Tag = key, Content = label });
        _characters.SelectedItem = _characters.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            (string)item.Tag == (Draft.IconStyle == "portrait" ? "photo" : Draft.ReferenceCharacter)) ?? _characters.Items[0];
        _characters.SelectionChanged += (_, __) =>
        {
            if (_characters.SelectedItem is not ComboBoxItem { Tag: string key }) return;
            if (key == "photo") Draft.IconStyle = "portrait";
            else { Draft.IconStyle = "character"; Draft.ReferenceCharacter = key; }
            Refresh();
        };
        panel.Children.Add(_characters);
        var tools = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var transparent = new CheckBox { Content = "透明背景", IsChecked = Draft.CharacterTransparentBackground,
            Foreground = Foreground, VerticalAlignment = VerticalAlignment.Center };
        transparent.Checked += (_, __) => { Draft.CharacterTransparentBackground = true; Refresh(); };
        transparent.Unchecked += (_, __) => { Draft.CharacterTransparentBackground = false; Refresh(); };
        tools.Children.Add(transparent);
        var choose = Tool("\uE91B", "选择照片"); choose.Click += (_, __) => ChoosePhoto(); DockPanel.SetDock(choose, Dock.Right); tools.Children.Add(choose);
        var color = Tool("\uE790", "背景颜色"); DockPanel.SetDock(color, Dock.Right); tools.Children.Add(color);
        color.Click += (_, __) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (AppearanceColors.TryColor(Draft.CharacterBackgroundColor, out var current))
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            Draft.CharacterBackgroundColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            transparent.IsChecked = false;
            Refresh();
        };
        var reset = Tool("\uE777", "恢复构图"); DockPanel.SetDock(reset, Dock.Right); tools.Children.Add(reset);
        reset.Click += (_, __) =>
        {
            Draft.PhotoOffsetX = Draft.PhotoOffsetY = 0;
            Draft.PhotoZoom = _zoom.Value = 1;
            Refresh();
        };
        panel.Children.Add(tools);
        _zoom = new Slider { Minimum = 1, Maximum = 4, Value = PictureBackground.Clamp(Draft.PhotoZoom, 1, 4, 1),
            Margin = new Thickness(0, 6, 0, 6), ToolTip = "照片缩放" };
        _zoom.ValueChanged += (_, __) => { Draft.PhotoZoom = _zoom.Value; Refresh(); };
        panel.Children.Add(_zoom);
        _preview.MouseLeftButtonDown += (_, e) =>
        {
            if (Draft.IconStyle != "portrait") return;
            _start = e.GetPosition(_preview); _origin = new Point(Draft.PhotoOffsetX, Draft.PhotoOffsetY);
            _preview.CaptureMouse(); e.Handled = true;
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
        _preview.MouseWheel += (_, e) =>
        { if (Draft.IconStyle == "portrait") { _zoom.Value += e.Delta > 0 ? .1 : -.1; e.Handled = true; } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        var cancel = Action("取消"); cancel.IsCancel = true; actions.Children.Add(cancel);
        var done = Action("完成"); done.IsDefault = true; done.Click += (_, __) => DialogResult = true; actions.Children.Add(done);
        var show = Action("使用并显示"); show.Click += (_, __) => { ShowAfterApply = true; DialogResult = true; }; actions.Children.Add(show);
        panel.Children.Add(actions);
        Refresh();
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.IconStyle = Draft.IconStyle;
        settings.ReferenceCharacter = Draft.ReferenceCharacter;
        settings.CharacterTransparentBackground = Draft.CharacterTransparentBackground;
        settings.CharacterBackgroundColor = Draft.CharacterBackgroundColor;
        settings.PhotoAsset = Draft.PhotoAsset; settings.IconBackground = Draft.IconBackground;
        settings.PhotoZoom = Draft.PhotoZoom; settings.PhotoOffsetX = Draft.PhotoOffsetX; settings.PhotoOffsetY = Draft.PhotoOffsetY;
    }

    private static Button Action(string content) => new() { Content = content, Padding = new Thickness(9, 5, 9, 5),
        Margin = new Thickness(4, 0, 0, 0) };
    private static Button Tool(string glyph, string label) => new() { Content = new TextBlock { Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets") }, ToolTip = label, Width = 32, Height = 28, Margin = new Thickness(4, 0, 0, 0) };

    private void ChoosePhoto()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择照片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock", "portraits");
            Draft.PhotoAsset = ImageImport.Import(picker.FileName, directory, true);
            Draft.IconBackground = "";
            Draft.PhotoOffsetX = Draft.PhotoOffsetY = 0; Draft.PhotoZoom = _zoom.Value = 1;
            _characters.SelectedItem = _characters.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "photo");
            Refresh();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法使用照片", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Refresh()
    {
        var palette = AppearanceColors.IconPalette(Draft, App.Theme, Draft.IconFollowTheme);
        _preview.Character = Draft.IconStyle == "portrait" ? "photo" : Draft.ReferenceCharacter;
        var size = _preview.Character == "photo" ? CharacterBatteryIcon.PhotoSize(_preview.Devices?.Count ?? 0) : new Size(120, 120);
        _preview.Width = size.Width; _preview.Height = size.Height;
        _preview.PhotoPath = string.IsNullOrWhiteSpace(Draft.IconBackground) ? Draft.PhotoAsset : Draft.IconBackground;
        if (_photoPath != _preview.PhotoPath)
        {
            _photoPath = _preview.PhotoPath;
            _photo = Palette.Load(_photoPath, 1024);
        }
        _preview.PhotoZoom = Draft.PhotoZoom; _preview.PhotoOffsetX = Draft.PhotoOffsetX; _preview.PhotoOffsetY = Draft.PhotoOffsetY;
        _preview.TransparentBackground = Draft.CharacterTransparentBackground;
        _preview.Accent = new SolidColorBrush(palette.Accent); _preview.Ink = new SolidColorBrush(palette.Text);
        _preview.Low = new SolidColorBrush(palette.Low); _preview.Muted = new SolidColorBrush(palette.TextDim);
        _preview.Surface = new SolidColorBrush(AppearanceColors.TryColor(Draft.CharacterBackgroundColor, out var color) ? color : palette.Surface);
        if (_zoom != null) _zoom.IsEnabled = Draft.IconStyle == "portrait";
        _preview.InvalidateVisual();
    }
}
