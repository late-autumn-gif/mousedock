using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views
{
    /// <summary>悬浮图标：角色款 / 照片款 / 迷你款三种形态，共用同一个 96×96 的圆角外壳。</summary>
    public partial class IconWindow : Window
    {
        private readonly ScaleTransform _photoScale = new ScaleTransform(1, 1, 0.5, 0.5);
        private readonly TranslateTransform _photoOffset = new TranslateTransform(0, 0);
        private readonly TransformGroup _photoTransform = new TransformGroup();

        private bool _dragging;
        private Point _dragStart;
        private double _dragOriginX, _dragOriginY;

        public IconWindow()
        {
            InitializeComponent();
            _photoTransform.Children.Add(_photoScale);
            _photoTransform.Children.Add(_photoOffset);
            PhotoBrush.RelativeTransform = _photoTransform;

            App.Battery.Refreshed += (_, __) => UpdateValue();
            Loaded += (_, __) =>
            {
                ApplyMode(App.Settings.IconMode, false);
                UpdateValue();
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            };
        }

        public void ApplyMode(string mode, bool persist)
        {
            // 老的三款（character/photo/mini）映射到新样式名，启动路径不用改
            mode = (mode ?? "photo").ToLowerInvariant();
            if (persist) App.Settings.IconMode = mode;
            ApplyStyle(mode switch
            {
                "character" => "photo",
                "mini" => "tile",
                _ => mode
            }, persist);
        }

        private string _shape = "soft";

        /// <summary>外壳形状：soft 圆角 / circle 圆形 / square 方角 / pill 横向胶囊。</summary>
        public void ApplyShape(string shape, bool persist)
        {
            _shape = (shape ?? "soft").ToLowerInvariant();
            if (persist) App.Settings.IconShape = _shape;

            var radius = _shape switch
            {
                "circle" => new CornerRadius(48),
                "square" => new CornerRadius(4),
                "pill" => new CornerRadius(32),
                _ => new CornerRadius(20)
            };

            foreach (var b in new[] { CharLayer, PhotoLayer, MiniLayer, RingLayer,
                                      PillLayer, BatteryLayer, PlainLayer, MultiLayer })
            {
                if (b != null) b.CornerRadius = radius;
            }

            ApplyStyle(App.Settings.IconStyle, false);
        }

        /// <summary>当前形状对应的宽高。</summary>
        private (double w, double h) ShapeSize()
            => _shape == "pill" ? (152, 64) : (96, 96);

        /// <summary>切换悬浮图标样式：photo / tile / ring / pill / battery。</summary>
        public void ApplyStyle(string style, bool persist)
        {
            style = (style ?? "photo").ToLowerInvariant();
            if (persist) App.Settings.IconStyle = style;

            CharLayer.Visibility = Visibility.Collapsed;
            PhotoLayer.Visibility = style == "photo" ? Visibility.Visible : Visibility.Collapsed;
            MiniLayer.Visibility = style == "tile" ? Visibility.Visible : Visibility.Collapsed;
            RingLayer.Visibility = style == "ring" ? Visibility.Visible : Visibility.Collapsed;
            PillLayer.Visibility = style == "pill" ? Visibility.Visible : Visibility.Collapsed;
            BatteryLayer.Visibility = style == "battery" ? Visibility.Visible : Visibility.Collapsed;
            PlainLayer.Visibility = style == "plain" ? Visibility.Visible : Visibility.Collapsed;
            MultiLayer.Visibility = style == "multi" ? Visibility.Visible : Visibility.Collapsed;

            if (style == "photo") LoadPhoto();

            // 非照片款按形状收放，照片款按设备名撑宽
            if (RootGrid != null && style != "photo")
            {
                var (sw, sh) = ShapeSize();
                RootGrid.Width = sw;
                RootGrid.Height = sh;
                foreach (var b in new[] { RingLayer, PillLayer, BatteryLayer, MiniLayer, PlainLayer, MultiLayer })
                {
                    if (b == null) continue;
                    b.Width = sw;
                    b.Height = sh;
                }
            }

            UpdateValue();
        }

        public void LoadCharacter()
        {
            string path = App.Settings.CharacterAsset;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                path = App.CharacterAssets.Count > 0 ? App.CharacterAssets[0] : null;

            var bmp = Palette.Load(path);
            CharBrush.ImageSource = bmp;
            CharBrush.Stretch = Stretch.UniformToFill;
        }

        public void LoadPhoto()
        {
            string path = App.Settings.IconBackground;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                path = App.DefaultPhotoAsset;

            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                PhotoBrush.ImageSource = null;
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 2048;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();

            RenderOptions.SetBitmapScalingMode(PhotoLayer, BitmapScalingMode.HighQuality);
            PhotoBrush.ImageSource = bmp;
            PhotoBrush.Stretch = Stretch.UniformToFill;
            ApplyPhotoTransform();
        }

        public void UpdateValue()
        {
            // 连着的都列出来，最多五行
            var devices = App.Battery.Devices.Where(d => d.Connected).Take(5).ToList();
            var dev = App.PrimaryDevice;

            if (DeviceList != null) DeviceList.ItemsSource = devices;

            string text = dev == null ? "--" : dev.LevelText;
            CharPlateText.Text = text;
            PhotoPlateText.Text = text;
            MiniText.Text = text;
            if (dev != null) MiniGlyph.Kind = dev.Kind;

            // 新样式的数字、环形弧、电池填充一起更新
            int pct = dev != null && dev.HasLevel ? dev.Level : -1;
            string bare = pct >= 0 ? pct.ToString() : "--";

            if (RingText != null) RingText.Text = bare;
            if (PillText != null) PillText.Text = pct >= 0 ? pct + "%" : "--";
            if (BatteryText != null) BatteryText.Text = bare;
            if (dev != null && PillGlyph != null) PillGlyph.Kind = dev.Kind;

            if (RingArc != null)
            {
                RingArc.HorizontalAlignment = HorizontalAlignment.Left;
                RingArc.VerticalAlignment = VerticalAlignment.Top;
                RingArc.Data = ArcGeometry(48, 48, 28, pct);
                RingArc.Stroke = new SolidColorBrush(pct >= 0 && pct <= App.Settings.LowBatteryWarn
                    ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                    : Color.FromRgb(0x7F, 0xE7, 0xC4));
            }

            if (BatteryFill != null)
            {
                double usable = 53;
                BatteryFill.Width = pct < 0 ? 0 : Math.Clamp(pct / 100.0 * usable, 0, usable);
                BatteryFill.Background = new SolidColorBrush(pct >= 0 && pct <= App.Settings.LowBatteryWarn
                    ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                    : Color.FromRgb(0x7F, 0xE7, 0xC4));
            }

            // 轻量数字：大数字 + 下划线，颜色跟电量走
            if (PlainText != null) PlainText.Text = bare;
            if (PlainBar != null)
            {
                PlainBar.Background = new SolidColorBrush(pct >= 0 && pct <= App.Settings.LowBatteryWarn
                    ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                    : Color.FromRgb(0x7F, 0xE7, 0xC4));
            }

            // 多电量迷你：每台连接设备一行「名字缩写 数字」
            if (MultiPanel != null)
            {
                MultiPanel.Children.Clear();
                var multi = devices.Take(4).ToList();
                if (multi.Count == 0)
                {
                    MultiPanel.Children.Add(new TextBlock
                    {
                        Text = "--",
                        FontSize = 22,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xE7, 0xFA)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
                else
                {
                    // 官方这款只显示几行纯数字，不带设备名
                    foreach (var d in multi)
                    {
                        int lv = d.HasLevel ? d.Level : -1;
                        bool lowV = lv >= 0 && lv <= App.Settings.LowBatteryWarn && !d.Charging;

                        MultiPanel.Children.Add(new TextBlock
                        {
                            Text = lv >= 0 ? lv.ToString() : "—",
                            FontSize = 15,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = new SolidColorBrush(lowV
                                ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                                : Color.FromRgb(0xEC, 0xE7, 0xFA)),
                            Margin = new Thickness(0, 1, 0, 1)
                        });
                    }
                }
            }

            // 尺寸按「最长名字 + 设备台数」算，一台和五台不是一个高度
            string longest = devices
                .Select(d => d.Name ?? "")
                .OrderByDescending(n => n.Length)
                .FirstOrDefault() ?? "";

            FitSize(longest, devices.Count);
        }

        /// <summary>宽度跟最长设备名走，高度跟台数走；外壳形状决定基准尺寸。</summary>
        private void FitSize(string longestName, int count)
        {
            if (RootGrid == null) return;

            var (baseW, baseH) = ShapeSize();

            double width = baseW;
            if (!string.IsNullOrWhiteSpace(longestName))
            {
                double text = 0;
                foreach (char ch in longestName) text += ch > 0x2E80 ? 8.0 : 4.6;
                width = Math.Clamp(Math.Max(baseW, text + 74), 96, 250);
            }

            double height = Math.Clamp(baseH + Math.Max(0, count - 1) * 20, baseH, 200);

            RootGrid.Width = width;
            RootGrid.Height = height;
            if (CharLayer != null) { CharLayer.Width = width; CharLayer.Height = height; }
            if (PhotoLayer != null) { PhotoLayer.Width = width; PhotoLayer.Height = height; }
            if (MiniLayer != null) { MiniLayer.Width = width; MiniLayer.Height = height; }
        }

        /// <summary>设备名太长塞不进 96px，砍到 4 个字。</summary>
        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string n = name.Trim();
            // 去掉常见后缀，只留型号
            foreach (var cut in new[] { " 无线鼠标", " 无线键盘", " 游戏鼠标", " 鼠标", " 键盘" })
            {
                if (n.EndsWith(cut, StringComparison.Ordinal))
                {
                    n = n.Substring(0, n.Length - cut.Length);
                    break;
                }
            }
            return n.Length <= 5 ? n : n.Substring(0, 5);
        }

        /// <summary>按百分比画一段圆弧，起点在正上方，顺时针。</summary>
        private static Geometry ArcGeometry(double cx, double cy, double r, int percent)
        {
            if (percent < 0) return Geometry.Empty;
            double angle = Math.Clamp(percent, 0, 100) * 3.6;
            if (angle < 1) return Geometry.Empty;
            if (angle > 359.5) angle = 359.5;

            double rad = (angle - 90) * Math.PI / 180;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));

            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0,
                angle > 180, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            return geo;
        }

        public void ApplyPhotoTransform()
        {
            double zoom = Math.Clamp(App.Settings.PhotoZoom, 1, 4);
            _photoScale.ScaleX = zoom;
            _photoScale.ScaleY = zoom;

            double limit = (zoom - 1) / 2.0;
            App.Settings.PhotoOffsetX = Math.Clamp(App.Settings.PhotoOffsetX, -limit, limit);
            App.Settings.PhotoOffsetY = Math.Clamp(App.Settings.PhotoOffsetY, -limit, limit);
            _photoOffset.X = App.Settings.PhotoOffsetX;
            _photoOffset.Y = App.Settings.PhotoOffsetY;
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            TextElement.SetForeground(MiniLayer, text);
            MiniText.Foreground = text;
            MiniGlyph.GlyphBrush = new SolidColorBrush(Palette.Mix(theme.Text, theme.Accent, 0.35));

            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(240);
                MiniTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgTop, dur));
                MiniBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgBottom, dur));
            }
            else
            {
                MiniTop.Color = theme.BgTop;
                MiniBottom.Color = theme.BgBottom;
            }
        }

        private bool _iconDrag;
        private Point _iconDragStart;

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _iconDragStart = e.GetPosition(this);
            _iconDrag = true;
            (sender as UIElement)?.CaptureMouse();
            e.Handled = true;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_iconDrag) return;
            var p = e.GetPosition(this);
            Left += p.X - _iconDragStart.X;
            Top += p.Y - _iconDragStart.Y;
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!_iconDrag) return;
            _iconDrag = false;
            (sender as UIElement)?.ReleaseMouseCapture();
            e.Handled = true;
            App.SaveWindowPositions();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (PhotoLayer.Visibility != Visibility.Visible) return;
            e.Handled = true;
            double factor = e.Delta > 0 ? 1.08 : 1 / 1.08;
            App.Settings.PhotoZoom = Math.Clamp(App.Settings.PhotoZoom * factor, 1.0, 4.0);
            ApplyPhotoTransform();
            App.SaveSettings();
        }

        private void OnPhotoMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _dragStart = e.GetPosition(this);
            _dragOriginX = App.Settings.PhotoOffsetX;
            _dragOriginY = App.Settings.PhotoOffsetY;
            _dragging = true;
            PhotoLayer.CaptureMouse();
            e.Handled = true;
        }

        private void OnPhotoMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || App.Settings.PhotoZoom <= 1.001) return;
            var p = e.GetPosition(this);
            App.Settings.PhotoOffsetX = _dragOriginX + (p.X - _dragStart.X) / 96.0;
            App.Settings.PhotoOffsetY = _dragOriginY + (p.Y - _dragStart.Y) / 96.0;
            ApplyPhotoTransform();
        }

        private void OnPhotoMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            PhotoLayer.ReleaseMouseCapture();
            App.SaveSettings();
        }

        private void OnContextMenu(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            App.ShowQuickMenu(this);
        }
    }
}
