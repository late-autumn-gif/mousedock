using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views;

public sealed class ColorEditorWindow : Window
{
    internal enum Surface { Interface, Mini, Floating, Tray }

    private sealed record Role(string Key, string Label, string Description, Color Default);
    private static readonly BitmapSource WheelImage = CreateWheel();
    private const double WheelSize = 188;
    private const double WheelCenter = WheelSize / 2;
    private readonly List<Role> _roles;
    private readonly List<RadioButton> _choices = new();
    private readonly List<Ellipse> _swatches = new();
    private readonly TextBox _hex;
    private readonly Slider _brightness;
    private readonly TextBlock _message;
    private readonly Ellipse _dim, _marker;
    private readonly Canvas _wheel;
    private readonly Brush _outline;
    private Role _active;
    private bool _syncing;
    private double _hue, _saturation, _value;

    public Dictionary<string, string> Draft { get; }

    internal ColorEditorWindow(Surface surface, Dictionary<string, string> values, ThemePreset palette)
        : this(SurfaceTitle(surface), values, palette, Fields(surface, palette)) { }

    internal ColorEditorWindow(string title, string description, string value, Color fallback, ThemePreset palette)
        : this(title, new() { ["color"] = value }, palette, [new("color", title, description, fallback)]) { }

    internal string SelectedHex => Draft.GetValueOrDefault("color");

    private ColorEditorWindow(string title, Dictionary<string, string> values, ThemePreset palette, List<Role> roles)
    {
        Draft = new(values ?? new());
        _roles = roles;
        Title = title;
        Width = roles.Count == 1 ? 414 : 570;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brush(palette.BgTop);
        var panelColor = Color.FromRgb(palette.Surface.R, palette.Surface.G, palette.Surface.B);
        var text = ColorContrast.Legible(palette.Text, [palette.BgTop, panelColor]);
        var muted = Brush(ColorContrast.Legible(palette.TextDim, [palette.BgTop, panelColor]));
        var accent = ColorContrast.Legible(palette.Accent, [panelColor], 3);
        _outline = Brush(Palette.Mix(panelColor, text, .32));
        Foreground = Brush(text);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        UseLayoutRounding = true;
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        Content = new ScrollViewer { Content = panel, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = roles.Count == 1 ? roles[0].Description : title == "迷你窗配色"
                ? "设置独立底色会关闭主题背景色跟随。"
                : "选择要调整的元素，再用色轮或 HEX 输入配色。",
            Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 15)
        });

        var body = new Grid();
        panel.Children.Add(body);
        if (roles.Count > 1)
        {
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(244) });
            var choices = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            body.Children.Add(choices);
            foreach (var role in roles)
            {
                var content = new DockPanel();
                var swatch = new Ellipse { Width = 22, Height = 22, Stroke = _outline, StrokeThickness = 1,
                    Margin = new Thickness(0, 0, 10, 0), Fill = Brush(Resolve(role)) };
                content.Children.Add(swatch);
                var label = new StackPanel();
                label.Children.Add(new TextBlock { Text = role.Label, FontWeight = FontWeights.SemiBold });
                label.Children.Add(new TextBlock { Text = role.Description, FontSize = 11, Foreground = muted, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(label);
                var choice = new RadioButton { Content = content, GroupName = "ColorRole", Padding = new Thickness(9, 7, 8, 7),
                    Margin = new Thickness(0, 0, 0, 4), Template = SurfaceTemplate(typeof(RadioButton), 8, true),
                    Background = Brush(panelColor), Foreground = Foreground, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1) };
                choice.Resources["Selected"] = Brush(Palette.Mix(panelColor, accent, .16));
                choice.Resources["Hover"] = Brush(Palette.Mix(panelColor, text, .09));
                choice.Resources["Focus"] = Brush(accent);
                AutomationProperties.SetName(choice, role.Label + " · " + role.Description);
                choice.Checked += (_, _) => LoadRole(role);
                _choices.Add(choice);
                _swatches.Add(swatch);
                choices.Children.Add(choice);
            }
        }

        var controls = new StackPanel { Margin = new Thickness(14, 13, 14, 12) };
        var picker = new Border { Child = controls, Background = Brush(panelColor), CornerRadius = new CornerRadius(12) };
        if (roles.Count > 1) Grid.SetColumn(picker, 1);
        body.Children.Add(picker);

        _wheel = new Canvas
        {
            Width = WheelSize, Height = WheelSize, HorizontalAlignment = HorizontalAlignment.Center,
            Focusable = true, Cursor = Cursors.Cross, Clip = new EllipseGeometry(new Point(WheelCenter, WheelCenter), WheelCenter, WheelCenter),
            Background = new ImageBrush(WheelImage)
        };
        AutomationProperties.SetName(_wheel, "色轮。左右调整色相，上下调整饱和度");
        _dim = new Ellipse { Width = WheelSize, Height = WheelSize, Fill = Brushes.Black, IsHitTestVisible = false };
        _marker = new Ellipse { Width = 13, Height = 13, Stroke = Brushes.White, StrokeThickness = 2,
            IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = .9 } };
        _wheel.Children.Add(_dim);
        _wheel.Children.Add(_marker);
        _wheel.MouseLeftButtonDown += (_, e) =>
        {
            _wheel.Focus();
            _wheel.CaptureMouse();
            PointColor(e.GetPosition(_wheel));
            e.Handled = true;
        };
        _wheel.MouseMove += (_, e) => { if (_wheel.IsMouseCaptured) PointColor(e.GetPosition(_wheel)); };
        _wheel.MouseLeftButtonUp += (_, _) => _wheel.ReleaseMouseCapture();
        _wheel.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Left: _hue -= 1d / 72; break;
                case Key.Right: _hue += 1d / 72; break;
                case Key.Up: _saturation = Math.Min(1, _saturation + .025); break;
                case Key.Down: _saturation = Math.Max(0, _saturation - .025); break;
                default: return;
            }
            _hue -= Math.Floor(_hue);
            ApplyWheelColor();
            e.Handled = true;
        };
        var wheelFrame = new Border { Child = _wheel, Width = WheelSize + 4, Height = WheelSize + 4,
            HorizontalAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(WheelCenter + 2), BorderBrush = Brushes.Transparent };
        _wheel.GotKeyboardFocus += (_, _) => wheelFrame.BorderBrush = Brush(accent);
        _wheel.LostKeyboardFocus += (_, _) => wheelFrame.BorderBrush = Brushes.Transparent;
        controls.Children.Add(wheelFrame);

        var levelLabel = new TextBlock { Text = "亮度", Foreground = muted, Margin = new Thickness(0, 10, 0, 0) };
        controls.Children.Add(levelLabel);
        _brightness = new Slider { Minimum = 0, Maximum = 1, SmallChange = .01, LargeChange = .1,
            Margin = new Thickness(0, 4, 0, 9) };
        AutomationProperties.SetName(_brightness, "亮度");
        _brightness.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _value = _brightness.Value;
            ApplyWheelColor();
        };
        controls.Children.Add(_brightness);

        var entry = new DockPanel();
        entry.Children.Add(new TextBlock { Text = "HEX", Foreground = muted, Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center });
        _hex = new TextBox { MaxLength = 7, Padding = new Thickness(7, 4, 7, 4), Background = Background,
            Foreground = Foreground, BorderBrush = _outline, CaretBrush = Foreground, SelectionBrush = Brush(accent),
            FontFamily = new FontFamily("Consolas"), ToolTip = "#RRGGBB" };
        AutomationProperties.SetName(_hex, "HEX 色值");
        _message = new TextBlock { Foreground = muted, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) };
        _hex.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            if (AppearanceColors.TryColor(_hex.Text, out var color))
            {
                SetColor(color);
                SetWheel(color);
            }
            else ShowEntryState(false);
        };
        entry.Children.Add(_hex);
        controls.Children.Add(entry);
        controls.Children.Add(_message);

        var actions = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        panel.Children.Add(actions);
        Button Action(string label, bool primary = false)
        {
            var button = new Button { Content = label, Foreground = primary ? Brush(ColorContrast.Legible(text, [accent])) : Foreground,
                Background = primary ? Brush(accent) : Brush(panelColor), BorderBrush = _outline, BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 0),
                Template = SurfaceTemplate(typeof(Button), 7) };
            button.Resources["Hover"] = Brush(Palette.Mix(primary ? accent : panelColor, text, .09));
            button.Resources["Focus"] = Brush(accent);
            return button;
        }
        var reset = Action("恢复默认");
        reset.Click += (_, _) =>
        {
            foreach (var role in _roles) Draft.Remove(role.Key);
            for (int i = 0; i < _swatches.Count; i++) _swatches[i].Fill = Brush(_roles[i].Default);
            LoadRole(_active);
        };
        actions.Children.Add(reset);
        var done = Action("完成", true);
        done.IsDefault = true;
        done.Click += (_, _) =>
        {
            if (!AppearanceColors.TryColor(_hex.Text, out _)) { _hex.Focus(); return; }
            DialogResult = true;
        };
        DockPanel.SetDock(done, Dock.Right);
        actions.Children.Add(done);
        var cancel = Action("取消");
        cancel.IsCancel = true;
        DockPanel.SetDock(cancel, Dock.Right);
        actions.Children.Add(cancel);

        if (_choices.Count > 0) _choices[0].IsChecked = true;
        else LoadRole(roles[0]);
    }

    private static string SurfaceTitle(Surface surface) => surface switch
    {
        Surface.Interface => "界面主题配色",
        Surface.Mini => "迷你窗配色",
        Surface.Floating => "悬浮图标配色",
        _ => "系统托盘配色"
    };

    private static List<Role> Fields(Surface surface, ThemePreset p) => surface switch
    {
        Surface.Interface =>
        [
            new("background", "窗口底色", "设置窗口、主面板及跟随主题的迷你窗", p.BgTop),
            new("surface", "卡片与菜单", "主面板卡片、设置控件与菜单底色", p.Surface),
            new("text", "主要文字", "窗口标题、设备名称和标签", p.Text),
            new("accent", "主题强调色", "按钮选中态与跟随主题的电量图形", p.Accent),
            new("muted", "次级文字", "主面板的说明与次要标签", p.TextDim)
        ],
        Surface.Mini =>
        [
            new("background", "迷你窗底色", "迷你窗外框", p.BgTop),
            new("text", "设备名称与图标", "迷你窗名称、设备图标与提示", p.Text),
            new("accent", "正常电量数字", "迷你窗的电量百分比", p.Text)
        ],
        Surface.Floating =>
        [
            new("surface", "悬浮图标底色", "启用底色的悬浮图标样式", p.Surface),
            new("text", "图标与文字", "悬浮图标的设备符号与电量文字", p.Text),
            new("accent", "正常电量", "悬浮图标的电量环与进度", p.Accent),
            new("muted", "未知电量", "读不到电量时的悬浮图标", p.TextDim)
        ],
        _ =>
        [
            new("text", "托盘文字", "支持数字的系统托盘样式", p.Text),
            new("accent", "正常电量", "系统托盘电量环或填充", p.Accent),
            new("muted", "未知电量", "读不到电量时的托盘图标", p.TextDim)
        ]
    };

    private Color Resolve(Role role) => AppearanceColors.TryColor(Draft.GetValueOrDefault(role.Key), out var color) ? color : role.Default;

    private void LoadRole(Role role)
    {
        _active = role;
        var color = Resolve(role);
        _syncing = true;
        _hex.Text = Palette.ToHex(color);
        _syncing = false;
        ShowEntryState(true);
        SetWheel(color);
    }

    private void SetColor(Color color)
    {
        Draft[_active.Key] = Palette.ToHex(color);
        int index = _roles.IndexOf(_active);
        if (_swatches.Count > 0) _swatches[index].Fill = Brush(color);
        ShowEntryState(true);
    }

    private void ShowEntryState(bool valid)
    {
        _hex.BorderBrush = valid ? _outline : Brushes.IndianRed;
        _message.Text = !valid ? "输入 #RRGGBB，例如 #367A68" :
            AppearanceColors.TryColor(Draft.GetValueOrDefault(_active.Key), out _) ? "自定义颜色" : "默认颜色";
    }

    private void SetWheel(Color color)
    {
        var rgb = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        byte max = Math.Max(color.R, Math.Max(color.G, color.B));
        byte min = Math.Min(color.R, Math.Min(color.G, color.B));
        _hue = rgb.GetHue() / 360;
        _value = max / 255d;
        _saturation = max == 0 ? 0 : (max - min) / (double)max;
        _syncing = true;
        _brightness.Value = _value;
        _syncing = false;
        UpdateWheel();
    }

    private void PointColor(Point point)
    {
        double x = point.X - WheelCenter, y = point.Y - WheelCenter;
        _hue = (Math.Atan2(y, x) / (2 * Math.PI) + 1) % 1;
        _saturation = Math.Min(1, Math.Sqrt(x * x + y * y) / WheelCenter);
        ApplyWheelColor();
    }

    private void ApplyWheelColor()
    {
        var color = Hsv(_hue, _saturation, _value);
        _syncing = true;
        _hex.Text = Palette.ToHex(color);
        _syncing = false;
        SetColor(color);
        UpdateWheel();
    }

    private void UpdateWheel()
    {
        double angle = _hue * 2 * Math.PI;
        double radius = _saturation * (WheelCenter - 5);
        Canvas.SetLeft(_marker, WheelCenter + Math.Cos(angle) * radius - _marker.Width / 2);
        Canvas.SetTop(_marker, WheelCenter + Math.Sin(angle) * radius - _marker.Height / 2);
        _marker.Fill = Brush(Hsv(_hue, _saturation, _value));
        _dim.Opacity = 1 - _value;
    }

    private static BitmapSource CreateWheel()
    {
        const int size = 256;
        const double radius = size / 2d;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double dx = x + .5 - radius, dy = y + .5 - radius;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > radius) continue;
            var color = Hsv((Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1, distance / radius, 1);
            int i = (y * size + x) * 4;
            pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
        }
        var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        image.Freeze();
        return image;
    }

    private static Color Hsv(double hue, double saturation, double value)
    {
        double sector = hue * 6;
        double chroma = value * saturation;
        double middle = chroma * (1 - Math.Abs(sector % 2 - 1));
        double minimum = value - chroma;
        var (red, green, blue) = (int)sector switch
        {
            0 => (chroma, middle, 0d), 1 => (middle, chroma, 0d),
            2 => (0d, chroma, middle), 3 => (0d, middle, chroma),
            4 => (middle, 0d, chroma), _ => (chroma, 0d, middle)
        };
        byte Channel(double channel) => (byte)Math.Round((channel + minimum) * 255);
        return Color.FromRgb(Channel(red), Channel(green), Channel(blue));
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private static ControlTemplate SurfaceTemplate(Type type, double radius, bool checkable = false)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Surface");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(type) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Hover"), "Surface"));
        template.Triggers.Add(hover);
        if (checkable)
        {
            var selected = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Selected"), "Surface"));
            template.Triggers.Add(selected);
        }
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Focus"), "Surface"));
        template.Triggers.Add(focus);
        return template;
    }
}
