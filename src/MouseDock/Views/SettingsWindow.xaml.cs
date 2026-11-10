using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseDock.Models;
using MouseDock.Controls;
using MouseDock.Services;

namespace MouseDock.Views
{
    public partial class SettingsWindow : Window
    {
        public sealed class ThemeRow : INotifyPropertyChanged
        {
            private bool _selected;
            public string Id { get; set; }
            public string Name { get; set; }
            public Brush Swatch { get; set; }
            public Brush AccentBrush { get; set; }

            public bool IsSelected
            {
                get => _selected;
                set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private bool _loading;
        private readonly Brush _themeBackground;

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(SettingsWindow),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xB4, 0x9C, 0xFF))));

        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        public SettingsWindow()
        {
            InitializeComponent();
            _themeBackground = Root.Background;
            App.Battery.Refreshed += (_, __) =>
            {
                if (IsVisible && !_loading) UpdateDeviceHint();
            };
            Loaded += (_, __) =>
            {
                Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                Reload();
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        private void OnHeadsetAudioClick(object sender, RoutedEventArgs e) => App.ShowHeadsets();

        public void Reload()
        {
            VersionText.Text = "  v" + typeof(App).Assembly.GetName().Version?.ToString(3);
            _loading = true;
            try
            {
                var s = App.Settings;

                var rows = ThemePreset.Presets
                    .Select(p => ReferenceThemes.Resolve(p.Id, s.ThemeMode) ?? p)
                    .Select(p => MakeRow(p.Id, p.Name, p.Accent, p.BgTop, p.BgBottom))
                    .ToList();

                bool hasBg = !string.IsNullOrWhiteSpace(s.CustomBackground) && File.Exists(s.CustomBackground);
                if (hasBg)
                {
                    var custom = App.Theme;
                    rows.Add(MakeRow("custom", "自定义 · 跟随背景图", custom.Accent, custom.BgTop, custom.BgBottom));
                }

                ThemeList.ItemsSource = rows;
                var current = rows.FirstOrDefault(r => r.Id == (hasBg ? "custom" : s.ThemeId)) ?? rows[2];
                ThemeList.SelectedItem = current;
                foreach (var r in rows) r.IsSelected = r == current;
                ThemeDefault.IsChecked = s.ThemeMode != "dark" && s.ThemeMode != "light";
                ThemeDark.IsChecked = s.ThemeMode == "dark";
                ThemeLight.IsChecked = s.ThemeMode == "light";

                var theme = App.Theme;

                DeviceChecks.ItemsSource = App.Battery.Devices;
                UpdateDeviceHint();

                OpacitySlider.Value = s.Opacity;
                OpacityValue.Text = $"{s.Opacity * 100:0}%";
                ScaleSlider.Value = s.Scale;
                ScaleValue.Text = $"{s.Scale * 100:0}%";
                RefreshSlider.Value = Math.Clamp(s.RefreshSeconds, 5, 120);
                RefreshValue.Text = $"{s.RefreshSeconds}s";
                AutoStartCheck.IsChecked = AutoStart.IsEnabled();
                ApplyWarnButtons(s.LowBatteryWarn);
            AutoUpdateBox.IsChecked = s.AutoUpdate;
                ApplySurfaceButtons(s.SurfaceMode);
                IconFollowThemeBox.IsChecked=s.IconFollowTheme;
                IconHideBackgroundBox.IsChecked=s.IconHideBackground;
                ReferenceCharacterChoices.SelectedItem = ReferenceCharacterChoices.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (string)item.Tag == s.ReferenceCharacter) ?? ReferenceCharacterChoices.Items[0];
                SyncIconScale();
                RebuildIconStyles();
                TaskbarOffsetSlider.Value = double.IsFinite(s.TaskbarOffset) ? Math.Clamp(s.TaskbarOffset, 0, 100) : 8;
                TaskbarOffsetValue.Text = $"{TaskbarOffsetSlider.Value:0}%";
                TaskbarFollowIconsBox.IsChecked = s.TaskbarFollowIcons;
                TaskbarIconOffsetSlider.Value = TaskbarPlacement.NormalizeIconOffset(s.TaskbarIconOffset);
                TaskbarIconOffsetInput.Text = TaskbarIconOffsetSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
                RebuildTaskbarStyles();
                BatteryNumberBox.IsChecked = s.ShowBatteryNumber;
                RebuildEdgeStyles();
                EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveForeground(s, App.Theme));
                EdgePositionSlider.Value = double.IsFinite(s.EdgeVerticalPosition) ? Math.Clamp(s.EdgeVerticalPosition * 100, 0, 100) : 50;
                EdgePositionValue.Text = $"{EdgePositionSlider.Value:0}";
                EdgeInsetSlider.Value = double.IsFinite(s.EdgeInset) ? Math.Clamp(s.EdgeInset, 0, 160) : 12;
                EdgeInsetValue.Text = $"{EdgeInsetSlider.Value:0}";
                if (TopmostBox != null) TopmostBox.IsChecked = s.AlwaysOnTop;
                if (ClickThroughBox != null) ClickThroughBox.IsChecked = s.ClickThrough;

            }
            finally { _loading = false; }

            ApplyTheme(App.Theme, false);
        }

        private static ThemeRow MakeRow(string id, string name, Color accent, Color bgTop, Color bgBottom)
        {
            var swatch = new LinearGradientBrush(bgTop, bgBottom, new Point(0, 0), new Point(1, 1));
            swatch.Freeze();
            var accentBrush = new SolidColorBrush(accent);
            accentBrush.Freeze();
            return new ThemeRow { Id = id, Name = name, Swatch = swatch, AccentBrush = accentBrush };
        }

        private void UpdateDeviceHint()
        {
            int selected = App.Battery.Devices.Count(d => d.Selected);
            DeviceHint.Text = selected > BatteryService.DisplayDeviceLimit
                ? $"已勾选 {selected} 台，各视图只显示前 {BatteryService.DisplayDeviceLimit} 台；右键可管理设备"
                : "勾选同步到所有视图；隐藏或取消识别后，仍可在这里恢复";
        }

        public void ScrollToBottom()
        {
            RootScroll?.ScrollToEnd();
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            TextElement.SetForeground(Root, text);

            // 资源画刷跟着主题走：浅色主题用深字、深卡片，免得白字糊在白底上
            bool isLight = theme.IsLight;
            Resources["TextBrush"] = new SolidColorBrush(theme.Text);
            Resources["CardBrush"] = new SolidColorBrush(isLight
                ? Color.FromArgb(0x14, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
            Resources["ButtonBrush"] = new SolidColorBrush(isLight
                ? Color.FromArgb(0x12, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
            Resources["ButtonHoverBrush"] = new SolidColorBrush(isLight
                ? Color.FromArgb(0x24, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
            if (theme.Hover.A != 0)
            {
                Resources["CardBrush"] = new SolidColorBrush(theme.Surface);
                Resources["ButtonBrush"] = new SolidColorBrush(theme.Surface);
                Resources["ButtonHoverBrush"] = new SolidColorBrush(theme.Hover);
            }
            var accent = new SolidColorBrush(theme.Accent);
            accent.Freeze();
            AccentBrush = accent;
            RootShadow.Color = theme.IsLight ? Color.FromRgb(0x8B, 0x93, 0xA6) : Colors.Black;
            RootShadow.Opacity = theme.IsLight ? 0.2 : 0.5;

            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(240);
                BgTopStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgTop, dur));
                BgBottomStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgBottom, dur));
            }
            else
            {
                BgTopStop.BeginAnimation(GradientStop.ColorProperty, null);
                BgBottomStop.BeginAnimation(GradientStop.ColorProperty, null);
                BgTopStop.Color = theme.BgTop;
                BgBottomStop.Color = theme.BgBottom;
            }
            RebuildTaskbarStyles(theme);
            RebuildEdgeStyles();
            EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveForeground(App.Settings, theme));
            PictureBackground.Apply(Root, "", _themeBackground, App.Settings, theme);
        }

        // ── 交互 ───────────────────────────────────────────────────────────

        private void OnThemeSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var row = ThemeList.SelectedItem as ThemeRow;
            if (row == null) return;

            foreach (var r in (ThemeList.ItemsSource as IEnumerable<ThemeRow>) ?? Enumerable.Empty<ThemeRow>())
                r.IsSelected = r == row;

            if (row.Id == "custom")
            {
                // 自定义项本身就是「跟随背景图」，不需要额外动作
            }
            else
            {
                App.Settings.ThemeId = row.Id;
                App.Settings.CustomBackground = "";
                App.Settings.CustomAccent = "";
            }

            App.RefreshTheme();
            App.SaveSettings();
            Reload();
        }

        private void OnPickBackground(object sender, RoutedEventArgs e)
        {
            var editor = new BackgroundEditorWindow(App.Settings) { Owner = this };
            if (editor.ShowDialog() != true) return;
            var draft = editor.Draft;
            App.Settings.CustomBackground = draft.CustomBackground;
            App.Settings.CustomAccent = draft.CustomAccent;
            App.Settings.BackgroundZoom = draft.BackgroundZoom;
            App.Settings.BackgroundPositionX = draft.BackgroundPositionX;
            App.Settings.BackgroundPositionY = draft.BackgroundPositionY;
            App.Settings.BackgroundDepth = draft.BackgroundDepth;
            App.RefreshTheme();
            App.SaveSettings();
            Reload();
        }

        private void OnThemeModeChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not RadioButton { IsChecked: true } button) return;
            App.Settings.ThemeMode = (string)button.Tag;
            App.RefreshTheme();
            App.SaveSettings();
            Reload();
        }

        private void OnClearBackground(object sender, RoutedEventArgs e)
        {
            App.Settings.CustomBackground = "";
            App.Settings.CustomAccent = "";
            App.RefreshTheme();
            App.SaveSettings();
            Reload();
        }

        private void OnDeviceChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender is CheckBox check && check.DataContext is DeviceInfo device)
                App.Battery.SetSelected(device, check.IsChecked == true);
            UpdateDeviceHint();
        }

        private void OnModeCharacter(object sender, RoutedEventArgs e) => SetMode("character");
        private void OnModePhoto(object sender, RoutedEventArgs e) => SetMode("photo");
        private void OnModeMini(object sender, RoutedEventArgs e) => SetMode("mini");

        private void SetMode(string mode)
        {
            App.Settings.IconMode = mode;
            App.SaveSettings();
            App.Icon?.ApplyMode(mode, true);
            App.ShowIconIfNeeded();
        }

        private void OnPickCharacter(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择角色图（正方形最好）",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
            };
            if (dlg.ShowDialog(this) != true) return;
            App.Settings.CharacterAsset = dlg.FileName;
            App.SaveSettings();
            App.Icon?.LoadCharacter();
            App.Icon?.ApplyMode("character", true);
            App.ShowIconIfNeeded();
        }

        private void OnPickPhoto(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择照片（滚轮缩放、拖动取景）",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
            };
            if (dlg.ShowDialog(this) != true) return;
            App.Settings.AnimatedPhotoBackground = false;
            App.Settings.PhotoAsset = dlg.FileName;
            App.Settings.PhotoZoom = 1.0;
            App.Settings.PhotoOffsetX = 0;
            App.Settings.PhotoOffsetY = 0;
            App.SaveSettings();
            App.Icon?.LoadPhoto();
            App.Icon?.ApplyMode("photo", true);
            App.ShowIconIfNeeded();
        }

        private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || OpacityValue == null) return;
            App.Settings.Opacity = e.NewValue;
            OpacityValue.Text = $"{e.NewValue * 100:0}%";
            App.ApplyOpacity();
            App.SaveSettings();
        }

        private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || ScaleValue == null) return;
            App.Settings.Scale = e.NewValue;
            ScaleValue.Text = $"{e.NewValue * 100:0}%";
            App.ApplyScale();
            App.SaveSettings();
        }

        private void OnRefreshChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || RefreshValue == null) return;
            App.Settings.RefreshSeconds = (int)Math.Round(e.NewValue);
            RefreshValue.Text = $"{App.Settings.RefreshSeconds}s";
            App.Battery.Configure(App.Settings);
            App.SaveSettings();
        }

        private void OnAutoStartClick(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = AutoStartCheck.IsChecked == true;
            AutoStart.Set(on);
            App.Settings.AutoStart = on;
            App.SaveSettings();
        }

        private void OnOpenDataDir(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.Dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SettingsStore.Dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OnOpenDetectReport(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = App.WriteDetectReport(out _);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        /// <summary>
        /// 导出错误报告。先弹「另存为」问用户放哪，
        /// 默认目录是 exe 同目录、文件名也填好了，直接回车就收下。
        /// </summary>
        private async void OnExportReportClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;

            try
            {
                var devices = App.Battery?.Devices?.ToList()
                              ?? new System.Collections.Generic.List<Models.DeviceInfo>();
                var export = await Services.FeedbackService.ExportReportWithPromptAsync(this, App.Settings, devices);

                if (export.Canceled) return;

                if (!export.Succeeded)
                {
                    MessageBox.Show(this, Services.FeedbackService.BuildFailureMessage(export), "MouseDock",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var r = MessageBox.Show(this,
                    "报告已生成：\n" + export.Path + "\n\n现在打开它所在的文件夹吗？",
                    "导出成功", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes) Services.FeedbackService.OpenReportFolder(export.Path);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "MouseDock",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        /// <summary>写反馈邮件：先导出报告（问一次放哪），再打开邮件客户端。</summary>
        private async void OnFeedbackClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;

            try
            {
                var devices = App.Battery?.Devices?.ToList()
                              ?? new System.Collections.Generic.List<Models.DeviceInfo>();
                var export = await Services.FeedbackService.ExportReportWithPromptAsync(this, App.Settings, devices);

                // 取消就是取消，别顺手把邮件也开了
                if (export.Canceled) return;

                if (export.Succeeded)
                {
                    Services.FeedbackService.OpenReportFolder(export.Path);
                }
                else
                {
                    // 报告写不进去也得让人说话，但得先告诉他报告为什么没生成
                    MessageBox.Show(this, Services.FeedbackService.BuildFailureMessage(export), "MouseDock",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                Services.FeedbackService.OpenFeedbackMail(export.Path);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "打开邮件失败：" + ex.Message, "MouseDock",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        /// <summary>再次查看关于通告。导出仍走 App 那一份，行为跟首次启动一致。</summary>
        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Views.AboutWindow.Show(this, false,
                    owner => App.ExportErrorReport(owner ?? this),
                    p => Services.FeedbackService.OpenFeedbackMail(p));
            }
            catch { }
        }

        private void OnAutoUpdateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Settings.AutoUpdate = AutoUpdateBox.IsChecked == true;
                SettingsStore.Save(App.Settings);
            }
            catch { }
        }

        private void OnWarnClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is string tag) || !int.TryParse(tag, out int pct)) return;
            App.Settings.LowBatteryWarn = pct;
            App.Dock?.ApplyTheme(App.Theme, false);
            App.Taskbar?.UpdateDevices();
            App.Edge?.UpdateDevices();
            ApplyWarnButtons(pct);
            RebuildTaskbarStyles();
            RebuildEdgeStyles();
            App.SaveSettings();
        }

        /// <summary>四个档位里高亮选中的那个，其余压暗。</summary>
        private void ApplyWarnButtons(int selected)
        {
            var buttons = new[] { (Warn10, 10), (Warn20, 20), (Warn30, 30), (Warn40, 40) };
            foreach (var (btn, pct) in buttons)
            {
                if (btn == null) continue;
                bool on = pct == selected;
                btn.Background = new SolidColorBrush(Color.FromArgb(
                    on ? (byte)0x46 : (byte)0x16, 0xFF, 0xFF, 0xFF));
                btn.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        public void SyncIconScale()
        {
            bool prior=_loading;_loading=true;
            try{double value=double.IsFinite(App.Settings.IconScale)?Math.Clamp(App.Settings.IconScale,.5,3):1;
                IconScaleSlider.Value=value;IconScaleValue.Text=$"{value*100:0}%";}
            finally{_loading=prior;}
        }
        private void OnIconScaleChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
        {
            if(_loading||IconScaleValue==null)return;
            App.Settings.IconScale=e.NewValue;IconScaleValue.Text=$"{e.NewValue*100:0}%";
            App.Icon?.SetSize(e.NewValue,false);App.SaveSettings();
        }
        private void OnResetIconScale(object sender,RoutedEventArgs e)=>IconScaleSlider.Value=1;

        private void RebuildIconStyles()
        {
            IconStyleChoices.Children.Clear();
            foreach(var entry in new[]{("ring","环形刻度"),("tile","柔角方块"),("pill","横向胶囊"),("battery","经典电池"),("plain","轻量数字"),("multi","多电量迷你"),("character","角色电量"),("portrait","我的照片")})
            {
                FrameworkElement preview;
                if (entry.Item1 is "character" or "portrait")
                    preview = new CharacterBatteryIcon
                    {
                        Width = 120, Height = 120, Level = 76,
                        Character = entry.Item1 == "portrait" ? "photo" : App.Settings.ReferenceCharacter,
                        PhotoPath = string.IsNullOrWhiteSpace(App.Settings.IconBackground) ? App.Settings.PhotoAsset : App.Settings.IconBackground,
                        PhotoZoom = App.Settings.PhotoZoom, PhotoOffsetX = App.Settings.PhotoOffsetX, PhotoOffsetY = App.Settings.PhotoOffsetY,
                        Accent = new SolidColorBrush(App.Settings.IconFollowTheme ? App.Theme.Accent : ThemePreset.C("#A6F5BF"))
                    };
                else
                {
                    var reference = new ReferenceBatteryIcon { StyleName=entry.Item1, Width=144,
                        Height=entry.Item1=="multi"?76:144,
                        Devices=new(){(DeviceKind.Mouse,76,false,false),(DeviceKind.Headset,-1,false,false)},
                        HideBackground=App.Settings.IconHideBackground };
                    reference.ApplyPalette(App.Theme, App.Settings.IconFollowTheme);
                    preview = reference;
                }
                var content=new StackPanel();
                content.Children.Add(new Viewbox{Width=82,Height=55,Child=preview});
                content.Children.Add(new TextBlock{Text=entry.Item2,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,4,0,0)});
                bool selected=App.Settings.IconStyle==entry.Item1;
                var button=new Button { Tag=entry.Item1,Content=content,Margin=new Thickness(3),Padding=new Thickness(3,6,3,6),
                    Style=(Style)FindResource("GhostButton"),ToolTip="切换到"+entry.Item2,
                    BorderBrush=new SolidColorBrush(App.Theme.Accent),BorderThickness=new Thickness(selected?2:0),
                    FontWeight=selected?FontWeights.Bold:FontWeights.Normal };
                button.Click+=OnIconStyleClick;IconStyleChoices.Children.Add(button);
            }
        }

        private void OnIconStyleClick(object sender,RoutedEventArgs e)
        {
            if (_loading || sender is not Button {Tag:string style}) return;
            App.Settings.IconStyle=style;
            App.Icon?.ApplyStyle(style,false);
            App.SetSurface("icon");
            ApplySurfaceButtons("icon");
            RebuildIconStyles();
        }

        private void OnIconOptionsChanged(object sender,RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.IconFollowTheme=IconFollowThemeBox.IsChecked==true;
            App.Settings.IconHideBackground=IconHideBackgroundBox.IsChecked==true;
            EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveForeground(App.Settings, App.Theme));
            App.RefreshDeviceViews();
            App.SaveSettings();RebuildIconStyles();RebuildTaskbarStyles();RebuildEdgeStyles();
        }

        private void OnReferenceCharacterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ReferenceCharacterChoices.SelectedItem is not ComboBoxItem { Tag: string kind }) return;
            App.Settings.ReferenceCharacter = kind;
            App.RefreshDeviceViews();
            App.SaveSettings();
            RebuildIconStyles(); RebuildTaskbarStyles(); RebuildEdgeStyles();
        }

        private void OnPickReferencePhoto(object sender, RoutedEventArgs e)
        {
            var editor = new PortraitEditorWindow(App.Settings) { Owner = this };
            if (editor.ShowDialog() != true) return;
            App.Settings.PhotoAsset = editor.Draft.PhotoAsset;
            App.Settings.IconBackground = editor.Draft.IconBackground;
            App.Settings.PhotoZoom = editor.Draft.PhotoZoom;
            App.Settings.PhotoOffsetX = editor.Draft.PhotoOffsetX;
            App.Settings.PhotoOffsetY = editor.Draft.PhotoOffsetY;
            App.RefreshDeviceViews();
            App.SaveSettings();
            RebuildIconStyles(); RebuildTaskbarStyles(); RebuildEdgeStyles();
        }

        private void OnClearReferencePhoto(object sender, RoutedEventArgs e)
        {
            App.Settings.PhotoAsset = "";
            App.Settings.IconBackground = "";
            App.RefreshDeviceViews();
            App.SaveSettings();
            RebuildIconStyles(); RebuildTaskbarStyles(); RebuildEdgeStyles();
        }

        private void OnSurfaceClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is string mode)) return;
            App.SetSurface(mode);
        }

        private void OnPickIconBackground(object sender, RoutedEventArgs e) => App.PickBackground("icon");

        private void OnTaskbarOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TaskbarOffsetValue == null) return;
            App.Settings.TaskbarOffset = Math.Clamp(Math.Round(e.NewValue), 0, 100);
            TaskbarOffsetValue.Text = $"{App.Settings.TaskbarOffset:0}%";
            App.Taskbar?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnTaskbarFollowIconsChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.TaskbarFollowIcons = TaskbarFollowIconsBox.IsChecked == true;
            ApplyTaskbarPositionControls(App.Settings.SurfaceMode);
            App.Taskbar?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnTaskbarIconOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TaskbarIconOffsetInput == null) return;
            App.Settings.TaskbarIconOffset = TaskbarPlacement.NormalizeIconOffset(e.NewValue);
            TaskbarIconOffsetInput.Text = App.Settings.TaskbarIconOffset.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            App.Taskbar?.UpdateAnchor();
            App.SaveSettings();
        }

        private void ApplyTaskbarIconOffsetInput()
        {
            if (_loading) return;
            if (int.TryParse(TaskbarIconOffsetInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                TaskbarIconOffsetSlider.Value = TaskbarPlacement.NormalizeIconOffset(value);
            TaskbarIconOffsetInput.Text = TaskbarIconOffsetSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        }

        private void OnTaskbarIconOffsetInputLostFocus(object sender, RoutedEventArgs e) => ApplyTaskbarIconOffsetInput();

        private void OnTaskbarIconOffsetInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyTaskbarIconOffsetInput();
            e.Handled = true;
        }

        private void ApplyTaskbarPositionControls(string mode)
        {
            bool taskbar = mode == "taskbar";
            TaskbarFollowIconsBox.Visibility = taskbar ? Visibility.Visible : Visibility.Collapsed;
            TaskbarIconOffsetRow.Visibility = taskbar && App.Settings.TaskbarFollowIcons ? Visibility.Visible : Visibility.Collapsed;
            TaskbarPositionRow.Visibility = taskbar && !App.Settings.TaskbarFollowIcons ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnBatteryNumberChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.ShowBatteryNumber = BatteryNumberBox.IsChecked == true;
            App.Taskbar?.UpdateDevices();
            App.Edge?.UpdateDevices();
            RebuildTaskbarStyles();
            RebuildEdgeStyles();
            App.SaveSettings();
        }

        private void OnEdgePositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || EdgePositionValue == null) return;
            App.Settings.EdgeVerticalPosition = Math.Round(e.NewValue) / 100;
            EdgePositionValue.Text = $"{e.NewValue:0}";
            App.Edge?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnEdgeInsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || EdgeInsetValue == null) return;
            App.Settings.EdgeInset = Math.Round(e.NewValue);
            EdgeInsetValue.Text = $"{e.NewValue:0}";
            App.Edge?.UpdateAnchor();
            App.SaveSettings();
        }

        private sealed class DialogOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public DialogOwner(Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        }

        private void OnPickTaskbarColor(object sender, RoutedEventArgs e)
        {
            bool text = (sender as FrameworkElement)?.Tag as string == "text";
            string current = text ? App.Settings.TaskbarTextColor : App.Settings.TaskbarBatteryColor;
            var fallback = text ? App.Theme.Text : App.Theme.Accent;
            var brush = TaskbarBatteryStrip.ResolveBrush(current, new SolidColorBrush(fallback)) as SolidColorBrush;
            var baseColor = brush?.Color ?? fallback;
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true, Color = System.Drawing.Color.FromArgb(baseColor.R, baseColor.G, baseColor.B)
            };
            if (dialog.ShowDialog(new DialogOwner(this)) != System.Windows.Forms.DialogResult.OK) return;
            ApplyTaskbarColor(text, $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        }

        private void ApplyTaskbarColor(bool text, string color)
        {
            if (text) App.Settings.TaskbarTextColor = color;
            else App.Settings.TaskbarBatteryColor = color;
            RebuildTaskbarStyles();
            App.Taskbar?.UpdateDevices();
            App.SaveSettings();
        }

        private void RebuildTaskbarStyles(ThemePreset theme = null)
        {
            if (TaskbarTextColorSwatch == null || TaskbarStyleChoices == null) return;
            theme ??= App.Theme;
            TaskbarTextColorSwatch.Background = TaskbarBatteryStrip.ResolveBrush(App.Settings.TaskbarTextColor,
                new SolidColorBrush(theme.Text));
            TaskbarBatteryColorSwatch.Background = TaskbarBatteryStrip.ResolveBrush(App.Settings.TaskbarBatteryColor,
                new SolidColorBrush(theme.Accent));
            TaskbarTextColorLabel.Text = string.IsNullOrWhiteSpace(App.Settings.TaskbarTextColor)
                ? "跟随主题" : App.Settings.TaskbarTextColor;
            TaskbarBatteryColorLabel.Text = string.IsNullOrWhiteSpace(App.Settings.TaskbarBatteryColor)
                ? "跟随主题" : App.Settings.TaskbarBatteryColor;
            TaskbarStyleChoices.Children.Clear();
            foreach (var (style, label) in TaskbarBatteryStrip.Styles(false))
            {
                var preview = new TaskbarBatteryStrip(new[]
                {
                    new MouseDock.Models.DeviceInfo { Kind = MouseDock.Models.DeviceKind.Keyboard, Level = 28 },
                    new MouseDock.Models.DeviceInfo { Kind = MouseDock.Models.DeviceKind.Mouse, Level = 90 }
                }, style, App.Settings.ShowBatteryNumber, false, new SolidColorBrush(theme.Text),
                    App.Settings.LowBatteryWarn, App.Settings.TaskbarTextColor, App.Settings.TaskbarBatteryColor,
                    new SolidColorBrush(theme.Accent), App.Settings, theme);
                var content = new StackPanel();
                content.Children.Add(new Viewbox { Width = 72, Height = 36, Child = preview, IsHitTestVisible = false });
                content.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0)
                });
                var choice = new RadioButton
                {
                    Tag = style, GroupName = "TaskbarStyle", Content = content,
                    Margin = new Thickness(3), Padding = new Thickness(4),
                    IsChecked = TaskbarBatteryStrip.NormalizeStyle(App.Settings.TaskbarStyle) == style
                };
                choice.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                choice.Checked += (_, __) => { if (!_loading) App.SetTaskbarStyle(style); };
                TaskbarStyleChoices.Children.Add(choice);
            }
        }

        private void RebuildEdgeStyles()
        {
            EdgeStyleChoices.Children.Clear();
            var foreground = new SolidColorBrush(ScreenEdgeWindow.ResolveForeground(App.Settings, App.Theme));
            foreach (var (style, label) in TaskbarBatteryStrip.Styles(true))
            {
                var device = new DeviceInfo { Kind = DeviceKind.Keyboard, Level = 80 };
                FrameworkElement preview = style == "ring"
                    ? ScreenEdgeWindow.CreateRingBadge(device, App.Settings, App.Theme)
                    : new TaskbarBatteryStrip(new[] { device }, style, App.Settings.ShowBatteryNumber, true,
                        foreground, App.Settings.LowBatteryWarn, "", "",
                        ScreenEdgeWindow.ResolveBatteryBrush(App.Settings, App.Theme), App.Settings, App.Theme);
                var content = new StackPanel();
                bool lightInk = foreground.Color.R * .2126 + foreground.Color.G * .7152 + foreground.Color.B * .0722 > 128;
                content.Children.Add(new Border
                {
                    Width = 64, Height = 64, CornerRadius = new CornerRadius(4),
                    Background = lightInk ? new SolidColorBrush(Color.FromRgb(48, 51, 55)) : Brushes.WhiteSmoke,
                    Child = new Viewbox { Margin = new Thickness(4), Child = preview, IsHitTestVisible = false }
                });
                content.Children.Add(new TextBlock { Text = style == "ring" ? "电量圆环" : label, FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
                var choice = new RadioButton
                {
                    Tag = style, GroupName = "EdgeStyle", Content = content,
                    Margin = new Thickness(3), Padding = new Thickness(4),
                    IsChecked = TaskbarBatteryStrip.NormalizeStyle(App.Settings.EdgeStyle) == style
                };
                choice.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                choice.Checked += (_, __) =>
                {
                    if (!_loading) App.SetEdgeStyle(style);
                };
                EdgeStyleChoices.Children.Add(choice);
            }
        }
        private void OnPickEdgeColor(object sender, RoutedEventArgs e)
        {
            var color = ScreenEdgeWindow.ResolveForeground(App.Settings, App.Theme);
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
            };
            if (dialog.ShowDialog(new DialogOwner(this)) != System.Windows.Forms.DialogResult.OK) return;
            ApplyEdgeColor($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        }

        private void OnResetEdgeColor(object sender, RoutedEventArgs e)
        {
            App.Settings.EdgeGlyphColor = "#F5F5F5";
            App.Settings.EdgeGlyphColorCustomized = false;
            RefreshEdgeColor();
        }

        private void ApplyEdgeColor(string color)
        {
            App.Settings.EdgeGlyphColor = color;
            App.Settings.EdgeGlyphColorCustomized = true;
            RefreshEdgeColor();
        }

        private void RefreshEdgeColor()
        {
            EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveForeground(App.Settings, App.Theme));
            App.Edge?.UpdateDevices();
            RebuildEdgeStyles();
            App.SaveSettings();
        }

        private void OnTopmostChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.AlwaysOnTop = TopmostBox.IsChecked == true;
            App.ApplyWindowBehavior();
            App.SaveSettings();
        }

        private void OnClickThroughChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.ClickThrough = ClickThroughBox.IsChecked == true;
            App.ApplyWindowBehavior();
            App.SaveSettings();
        }        private void OnClearIconBackground(object sender, RoutedEventArgs e) => App.SetBackground("icon", "");
        private void OnPickDockBg(object sender, RoutedEventArgs e) => App.PickBackground("dock");
        private void OnClearDockBg(object sender, RoutedEventArgs e) => App.SetBackground("dock", "");
        private void OnPickMiniBg(object sender, RoutedEventArgs e) => App.PickBackground("mini");
        private void OnClearMiniBg(object sender, RoutedEventArgs e) => App.SetBackground("mini", "");

        /// <summary>三个形态按钮里高亮选中的那个，其余压暗。</summary>
        private void ApplySurfaceButtons(string mode)
        {
            if (string.IsNullOrEmpty(mode)) mode = "dock";
            ApplyTaskbarPositionControls(mode);
            IconOptionsPanel.Visibility = mode == "icon" ? Visibility.Visible : Visibility.Collapsed;
            ReferenceAppearanceOptions.Visibility = mode is "icon" or "taskbar" or "edge" ? Visibility.Visible : Visibility.Collapsed;
            TaskbarStylePanel.Visibility = mode == "taskbar" ? Visibility.Visible : Visibility.Collapsed;
            EdgeOptionsPanel.Visibility = mode == "edge" ? Visibility.Visible : Visibility.Collapsed;
            BatteryNumberBox.Visibility = mode == "edge" || mode == "taskbar" ? Visibility.Visible : Visibility.Collapsed;
            var buttons = new[] { (SurfaceDock, "dock"), (SurfaceMini, "mini"), (SurfaceIcon, "icon"),
                (SurfaceTaskbar, "taskbar"), (SurfaceEdge, "edge") };
            foreach (var (btn, m) in buttons)
            {
                if (btn == null) continue;
                bool on = m == mode;
                btn.Background = new SolidColorBrush(Color.FromArgb(
                    on ? (byte)0x46 : (byte)0x16, 0xFF, 0xFF, 0xFF));
                btn.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                App.ScheduleDeviceRefresh();
                DeviceHint.Text = $"已请求刷新 · {DateTime.Now:HH:mm:ss}";
            }
            catch
            {
                DeviceHint.Text = "刷新失败，看看探测报告";
            }
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject src)
            {
                // 点在关闭按钮或任何交互控件上时不拖动，否则会把点击吃掉
                if (IsInside(src, CloseButton)) return;
                if (IsInteractive(src)) return;
            }
            e.Handled = true;
            try { DragMove(); } catch { }
        }

        /// <summary>按钮、滑块、列表、勾选框这些控件自己处理鼠标，别拿来拖窗口。</summary>
        private static bool IsInteractive(DependencyObject node)
        {
            while (node != null)
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase ||
                    node is Slider || node is TextBox || node is ListBox || node is ListBoxItem ||
                    node is CheckBox || node is System.Windows.Controls.Primitives.ScrollBar ||
                    node is System.Windows.Controls.Primitives.Thumb)
                    return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
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

        private void OnCloseClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Hide();
        }
    }
}


