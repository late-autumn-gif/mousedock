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
using MouseDock.Controls;
using System.Windows.Data;

namespace MouseDock.Views
{
    /// <summary>悬浮图标：角色款 / 照片款 / 迷你款三种形态，共用同一个 96×96 的圆角外壳。</summary>
    public partial class IconWindow : Window
    {
        private readonly ScaleTransform _photoScale = new ScaleTransform(1, 1, 0.5, 0.5);
        private readonly TranslateTransform _photoOffset = new TranslateTransform(0, 0);
        private readonly TransformGroup _photoTransform = new TransformGroup();
        private ThemePreset _theme = App.Theme;

        private string _activeStyle = "photo";
        private bool _dragging;
        private Point _dragStart;
        private double _dragOriginX, _dragOriginY;

        public IconWindow()
        {
            InitializeComponent();
            PreviewMouseWheel+=(_,e)=>{
                if((Keyboard.Modifiers&ModifierKeys.Control)!=0 && _activeStyle is "photo" or "portrait")return;
                SetSize(App.Settings.IconScale*(e.Delta>0?1.08:1/1.08),true);e.Handled=true;
            };
            _photoTransform.Children.Add(_photoScale);
            _photoTransform.Children.Add(_photoOffset);
            PhotoBrush.RelativeTransform = _photoTransform;
            OtterScene.IsVisibleChanged += (_, __) => UpdateCharacterAnimation();

            App.Battery.Refreshed += (_, __) => UpdateValue();
            Loaded += (_, __) =>
            {
                ApplyStyle(App.Settings.IconStyle, false);
                UpdateValue();
                SetSize(App.Settings.IconScale,false);
                RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
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

        public void SetSize(double scale,bool persist)
        {
            scale=double.IsFinite(scale)?Math.Clamp(scale,.5,3):1;
            RootGrid.LayoutTransform=new ScaleTransform(scale,scale);
            if(persist){App.Settings.IconScale=scale;App.SaveSettings();App.SettingsView?.SyncIconScale();}
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
            if (style is "digits" or "compact") style = TaskbarBatteryStrip.NormalizeStyle(style);
            _activeStyle = style;
            if (persist) App.Settings.IconStyle = style;

            CharLayer.Visibility = Visibility.Collapsed;
            PhotoLayer.Visibility = style == "photo" ? Visibility.Visible : Visibility.Collapsed;
            MiniLayer.Visibility = style == "tile" ? Visibility.Visible : Visibility.Collapsed;
            RingLayer.Visibility = style == "ring" ? Visibility.Visible : Visibility.Collapsed;
            PillLayer.Visibility = style == "pill" ? Visibility.Visible : Visibility.Collapsed;
            BatteryLayer.Visibility = style == "battery" ? Visibility.Visible : Visibility.Collapsed;
            PlainLayer.Visibility = style == "plain" ? Visibility.Visible : Visibility.Collapsed;
            MultiLayer.Visibility = style == "multi" ? Visibility.Visible : Visibility.Collapsed;

            bool animatedPhoto = style == "photo" && App.Settings.AnimatedPhotoBackground;
            RikkaArt.Visibility = App.Settings.AnimatedCharacter == "rikka" ? Visibility.Visible : Visibility.Collapsed;
            RoxyArt.Visibility = App.Settings.AnimatedCharacter == "roxy" ? Visibility.Visible : Visibility.Collapsed;
            OtterArt.Visibility = RikkaArt.Visibility == Visibility.Visible || RoxyArt.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            OtterScene.Visibility = animatedPhoto ? Visibility.Visible : Visibility.Collapsed;
            CharacterDevicePanel.Visibility = OtterScene.Visibility;
            PhotoLayer.Background = animatedPhoto ? Brushes.Transparent : PhotoBrush;
            if (style == "photo" && !animatedPhoto) LoadPhoto();
            UpdateCharacterAnimation();

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

        private void UpdateCharacterAnimation()
        {
            OtterEyes.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RikkaClosedEye.BeginAnimation(OpacityProperty, null);
            RoxyClosedEyes.BeginAnimation(OpacityProperty, null);
            RikkaLeftEar.BeginAnimation(RotateTransform.AngleProperty, null);
            RikkaRightEar.BeginAnimation(RotateTransform.AngleProperty, null);
            if (!OtterScene.IsVisible || !SystemParameters.ClientAreaAnimation) return;

            if (OtterArt.Visibility == Visibility.Visible)
            {
                OtterEyes.BeginAnimation(ScaleTransform.ScaleYProperty, MakeBlink(1, 0.12));
                return;
            }

            if (RikkaArt.Visibility == Visibility.Visible)
            {
                RikkaClosedEye.BeginAnimation(OpacityProperty, MakeBlink(0, 1));
                RikkaLeftEar.BeginAnimation(RotateTransform.AngleProperty, MakeSway(-6, 1.5));
                RikkaRightEar.BeginAnimation(RotateTransform.AngleProperty, MakeSway(7, 1.8));
            }
            else
            {
                RoxyClosedEyes.BeginAnimation(OpacityProperty, MakeBlink(0, 1));
            }
        }

        private static DoubleAnimation MakeSway(double amount, double seconds) => new DoubleAnimation(0, amount, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        private static DoubleAnimationUsingKeyFrames MakeBlink(double open, double closed)
        {
            var blink = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(4.7),
                RepeatBehavior = RepeatBehavior.Forever
            };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(open, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.3))));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(closed, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.38))));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(open, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.51))));
            return blink;
        }

        public void LoadPhoto()
        {
            string path = App.Settings.IconBackground;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                path = App.DefaultPhotoAsset;

            var bmp = Palette.Load(path, 2048);
            PhotoBrush.ImageSource = bmp;
            if (bmp == null) return;

            RenderOptions.SetBitmapScalingMode(PhotoLayer, BitmapScalingMode.HighQuality);
            PhotoBrush.Stretch = Stretch.UniformToFill;
            ApplyPhotoTransform();
        }

        public void UpdateValue()
        {
            var devices = App.Battery.VisibleDevices.ToList();
            var dev = App.PrimaryDevice;

            if (DeviceList != null) DeviceList.ItemsSource = devices;
            bool reference = new[] {"ring","tile","pill","battery","plain","multi","character","portrait"}.Contains(_activeStyle);
            LegacyLayers.Visibility=reference?Visibility.Collapsed:Visibility.Visible;
            DeviceList.Visibility=reference?Visibility.Collapsed:Visibility.Visible;
            ReferenceIcons.Visibility=reference?Visibility.Visible:Visibility.Collapsed;
            if(reference){UpdateReferenceIcons(devices);return;}


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
                var multi = devices;
                if (multi.Count == 0)
                {
                    MultiPanel.Children.Add(new TextBlock
                    {
                        Text = "--",
                        FontSize = 22,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(_theme.Text),
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
                                : _theme.Text),
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

        private void UpdateReferenceIcons(System.Collections.Generic.List<DeviceInfo> devices)
        {
            ReferenceIcons.Children.Clear();
            var shown = devices.Take(6).ToList();
            ReferenceIcons.Columns = _activeStyle == "character" ? Math.Min(2, Math.Max(1, shown.Count)) : 1;
            if (_activeStyle is "character" or "portrait")
            {
                int count = _activeStyle == "portrait" ? 1 : Math.Max(1, shown.Count);
                for (int i = 0; i < count; i++)
                {
                    var item = i < shown.Count ? shown[i] : null;
                    var portrait = new CharacterBatteryIcon
                    {
                        Width = _activeStyle == "portrait" ? 160 : 120,
                        Height = _activeStyle == "portrait" ? Math.Max(192, 80 + shown.Count * 30) : 120,
                        Character = _activeStyle == "portrait" ? "photo" : App.Settings.ReferenceCharacter,
                        PhotoPath = string.IsNullOrWhiteSpace(App.Settings.IconBackground) ? App.Settings.PhotoAsset : App.Settings.IconBackground,
                        PhotoZoom = App.Settings.PhotoZoom, PhotoOffsetX = App.Settings.PhotoOffsetX, PhotoOffsetY = App.Settings.PhotoOffsetY,
                        Accent = new SolidColorBrush(AppearanceColors.IconPalette(App.Settings, _theme, App.Settings.IconFollowTheme).Accent),
                        Ink = new SolidColorBrush(AppearanceColors.IconPalette(App.Settings, _theme, App.Settings.IconFollowTheme).Text),
                        Surface = new SolidColorBrush(AppearanceColors.IconPalette(App.Settings, _theme, App.Settings.IconFollowTheme).Surface),
                        Devices = _activeStyle == "portrait" ? shown : null, DataContext = item
                    };
                    if (item != null)
                    {
                        portrait.SetBinding(CharacterBatteryIcon.LevelProperty, new Binding(nameof(DeviceInfo.Level)));
                        portrait.SetBinding(CharacterBatteryIcon.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
                        portrait.SetBinding(CharacterBatteryIcon.StaleProperty, new Binding(nameof(DeviceInfo.IsStale)));
                        portrait.SetBinding(CharacterBatteryIcon.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
                    }
                    Attach(portrait, item);
                }
                RootGrid.Width = _activeStyle == "portrait" ? 160 : ReferenceIcons.Columns * 120;
                RootGrid.Height = _activeStyle == "portrait" ? Math.Max(192, 80 + shown.Count * 30)
                    : Math.Ceiling(count / (double)ReferenceIcons.Columns) * 120;
                return;
            }
            bool multi = _activeStyle == "multi";
            double width = multi ? Math.Max(72, shown.Count * 58 + 16) : shown.Count > 1 ? 144 : 112;
            double height = multi ? 80 : shown.Count > 1 ? shown.Count * 64 + 12 : 112;
            var icon = new ReferenceBatteryIcon
            {
                StyleName = _activeStyle, Width = width, Height = height,
                HideBackground = App.Settings.IconHideBackground, DeviceModels = shown
            };
            icon.ApplyPalette(_theme, App.Settings);
            Attach(icon, null);
            RootGrid.Width = width;
            RootGrid.Height = height;

            void Attach(FrameworkElement icon, DeviceInfo item)
            {
                icon.ToolTip = item == null ? string.Join("\n", shown.Select(d => d.Name + " · " + d.LevelText))
                    : item.Name + " · " + item.LevelText;
                System.Windows.Automation.AutomationProperties.SetName(icon, icon.ToolTip.ToString());
                icon.MouseLeftButtonDown += OnDragWindow;
                icon.MouseMove += OnDragMove;
                icon.MouseLeftButtonUp += OnDragEnd;
                icon.MouseRightButtonUp += OnContextMenu;
                icon.MouseWheel += OnWheel;
                ReferenceIcons.Children.Add(icon);
            }
        }

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

            int rows = (count + 1) / 2;
            bool character = OtterScene.Visibility == Visibility.Visible;
            if (character) width = Math.Max(144, width);
            double height = character
                ? 61 + rows * 23
                : Math.Clamp(baseH + Math.Max(0, rows - 1) * 21, baseH, 200);

            RootGrid.Width = width;
            RootGrid.Height = height;
            if (CharLayer != null) { CharLayer.Width = width; CharLayer.Height = height; }
            if (PhotoLayer != null) { PhotoLayer.Width = width; PhotoLayer.Height = height; }
            OtterScene.Height = 57;
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
            _theme = theme;
            var text = new SolidColorBrush(theme.Text);
            Foreground = text;
            TextElement.SetForeground(MiniLayer, text);
            foreach (var label in new[] { MiniText, RingText, PillText, BatteryText, PlainText })
                label.Foreground = text;
            MiniGlyph.GlyphBrush = new SolidColorBrush(Palette.Mix(theme.Text, theme.Accent, 0.35));
            PillGlyph.GlyphBrush = MiniGlyph.GlyphBrush;
            foreach (var (top, bottom) in new[]
            {
                (MiniTop, MiniBottom), (PillTop, PillBottom),
                (BatteryTop, BatteryBottom), (MultiTop, MultiBottom)
            })
            {
                SetGradient(top, theme.BgTop);
                SetGradient(bottom, theme.BgBottom);
            }
            UpdateValue();

            void SetGradient(GradientStop stop, Color target)
            {
                var previous = stop.Color;
                stop.BeginAnimation(GradientStop.ColorProperty, null);
                stop.Color = target;
                if (animate)
                    stop.BeginAnimation(GradientStop.ColorProperty,
                        new ColorAnimation(previous, target, TimeSpan.FromMilliseconds(240))
                        { FillBehavior = FillBehavior.Stop });
            }
        }

        private bool _iconDrag;
        private bool _photoCropDrag;
        private Point _iconDragStart;

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _iconDragStart = e.GetPosition(this);
            _iconDrag = true;
            _photoCropDrag = _activeStyle == "portrait" && (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _dragOriginX = App.Settings.PhotoOffsetX;
            _dragOriginY = App.Settings.PhotoOffsetY;
            (sender as UIElement)?.CaptureMouse();
            e.Handled = true;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_iconDrag) return;
            var p = e.GetPosition(this);
            if (_photoCropDrag && sender is CharacterBatteryIcon portrait)
            {
                App.Settings.PhotoOffsetX = Math.Clamp(_dragOriginX + (p.X - _iconDragStart.X) / Math.Max(1, portrait.ActualWidth), -.5, .5);
                App.Settings.PhotoOffsetY = Math.Clamp(_dragOriginY + (p.Y - _iconDragStart.Y) / Math.Max(1, portrait.ActualHeight), -.5, .5);
                portrait.PhotoOffsetX = App.Settings.PhotoOffsetX;
                portrait.PhotoOffsetY = App.Settings.PhotoOffsetY;
                portrait.InvalidateVisual();
                return;
            }
            Left += p.X - _iconDragStart.X;
            Top += p.Y - _iconDragStart.Y;
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!_iconDrag) return;
            _iconDrag = false;
            (sender as UIElement)?.ReleaseMouseCapture();
            e.Handled = true;
            if (_photoCropDrag) { _photoCropDrag = false; App.SaveSettings(); App.Taskbar?.UpdateDevices(); App.Edge?.UpdateDevices(); }
            else App.SaveWindowPositions();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_activeStyle == "portrait")
            {
                e.Handled = true;
                App.Settings.PhotoZoom = Math.Clamp(App.Settings.PhotoZoom * (e.Delta > 0 ? 1.08 : 1 / 1.08), 1, 4);
                UpdateValue();
                App.Taskbar?.UpdateDevices();
                App.Edge?.UpdateDevices();
                App.SaveSettings();
                return;
            }
            if (PhotoLayer.Visibility != Visibility.Visible || App.Settings.AnimatedPhotoBackground) return;
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
            var menu=new ContextMenu();
            foreach(var entry in new[] {("ring","环形刻度"),("tile","柔角方块"),("pill","横向胶囊"),("battery","经典电池"),("plain","轻量数字"),("multi","多电量迷你"),("character","角色电量"),("portrait","我的照片")}) {
                var item=new MenuItem {Header=entry.Item2,IsCheckable=true,IsChecked=App.Settings.IconStyle==entry.Item1};
                item.Click+=(_,__)=>{ApplyStyle(entry.Item1,true);App.SaveSettings();};menu.Items.Add(item);
            }
            var follow=new MenuItem {Header="跟随界面主题色",IsCheckable=true,IsChecked=App.Settings.IconFollowTheme};
            follow.Click+=(_,__)=>{App.Settings.IconFollowTheme=follow.IsChecked;App.SaveSettings();UpdateValue();};menu.Items.Add(follow);
            var hide=new MenuItem {Header="隐藏图标底色",IsCheckable=true,IsChecked=App.Settings.IconHideBackground};
            hide.Click+=(_,__)=>{App.Settings.IconHideBackground=hide.IsChecked;App.SaveSettings();UpdateValue();};menu.Items.Add(hide);
            var settings=new MenuItem {Header="更多设置"};settings.Click+=(_,__)=>App.ShowQuickMenu(this);menu.Items.Add(settings);
            menu.PlacementTarget=this;menu.IsOpen=true;
        }
    }
}

