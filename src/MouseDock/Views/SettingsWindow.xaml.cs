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
            Loaded += (_, _) => App.SettingsSaveCompleted += OnSettingsSaveCompleted;
            Unloaded += (_, _) => App.SettingsSaveCompleted -= OnSettingsSaveCompleted;
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
            _themeBackground = Root.Background;
            App.Battery.Refreshed += (_, __) =>
            {
                if (IsVisible && !_loading)
                {
                    UpdateDeviceHint();
                    ReloadTraySettings();
                }
            };
            Loaded += (_, __) =>
            {
                Width = Math.Min(760, Math.Max(320, SystemParameters.WorkArea.Width - 32));
                Height = Math.Min(620, Math.Max(360, SystemParameters.WorkArea.Height - 32));
                MinWidth = Math.Min(680, Width);
                MinHeight = Math.Min(500, Height);
                SelectSection(_section);
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
                CurrentThemeText.Text = "当前 · " + current.Name;
                foreach (var r in rows) r.IsSelected = r == current;
                ThemeDefault.IsChecked = s.ThemeMode != "dark" && s.ThemeMode != "light";
                ThemeDark.IsChecked = s.ThemeMode == "dark";
                ThemeLight.IsChecked = s.ThemeMode == "light";

                var theme = App.Theme;

                DeviceChecks.ItemsSource = App.Battery.Devices;
                UpdateDeviceHint();
                ReloadTraySettings();

                OpacitySlider.Value = s.Opacity;
                OpacityValue.Text = $"{s.Opacity * 100:0}%";
                ScaleSlider.Value = s.Scale;
                ScaleValue.Text = $"{s.Scale * 100:0}%";
                RefreshSlider.Value = Math.Clamp(s.RefreshSeconds, 5, 120);
                RefreshValue.Text = $"{s.RefreshSeconds}s";
                AutoStartCheck.IsChecked = App.IsTestProfile ? s.AutoStart : AutoStart.IsEnabled();
                AutoStartCheck.Content = App.IsTestProfile ? "开机自启（仅测试交互）" : "开机自启";
                AutoStartCheck.ToolTip = App.IsTestProfile ? "开关仅保存在隔离配置中，不修改 Windows 启动项" : null;
                SafeModeChoice.IsChecked = s.SafeMode;
                ApplyWarnButtons(s.LowBatteryWarn);
                SelectSection(_section);
            AutoUpdateBox.IsChecked = !App.IsTestProfile && s.AutoUpdate;
                AutoUpdateBox.IsEnabled = !App.IsTestProfile;
                AutoUpdateBox.ToolTip = App.IsTestProfile ? "隔离测试版不访问更新源" : null;
                ApplySurfaceButtons(s.SurfaceMode);
                DockWallpaperEngineBox.IsChecked = s.DockFollowWallpaperEngine && string.IsNullOrEmpty(s.DockWallpaperEngineSelection);
                IconFollowThemeBox.IsChecked=s.IconFollowTheme;
                IconHideBackgroundBox.IsChecked=s.IconHideBackground;
                ReferenceCharacterChoices.SelectedItem = ReferenceCharacterChoices.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (string)item.Tag == s.ReferenceCharacter) ?? ReferenceCharacterChoices.Items[0];
                SyncIconScale();
                RebuildIconStyles();
                TaskbarOffsetSlider.Value = double.IsFinite(s.TaskbarOffset) ? Math.Clamp(s.TaskbarOffset, 0, 100) : 8;
                TaskbarOffsetValue.Text = $"{TaskbarOffsetSlider.Value:0}%";
                TaskbarPositionChoice.SelectedValue = s.TaskbarPinToTray ? "tray" : s.TaskbarFollowIcons ? "follow" : "manual";
                TaskbarIconOffsetSlider.Value = TaskbarPlacement.NormalizeIconOffset(s.TaskbarIconOffset);
                TaskbarIconOffsetInput.Text = TaskbarIconOffsetSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
                RebuildTaskbarStyles();
                RebuildEdgeStyles();
                EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveForeground(s, App.Theme));
                EdgePositionSlider.Value = double.IsFinite(s.EdgeVerticalPosition) ? Math.Clamp(s.EdgeVerticalPosition * 100, 0, 100) : 50;
                EdgePositionValue.Text = $"{EdgePositionSlider.Value:0}";
                EdgeInsetSlider.Value = double.IsFinite(s.EdgeInset) ? Math.Clamp(s.EdgeInset, 0, 160) : 12;
                EdgeInsetValue.Text = $"{EdgeInsetSlider.Value:0}";
                SyncTaskbarSize();
                SyncEdgeSize();
                if (TopmostBox != null) TopmostBox.IsChecked = s.AlwaysOnTop;
                if (ClickThroughBox != null) ClickThroughBox.IsChecked = s.ClickThrough;

            }
            finally { _loading = false; }

            ApplyTheme(App.Theme, false);
        }


        private string _section = "display";
        public void SelectSection(string section)
        {
            var pages = new[] { ("display", PageDisplay), ("devices", PageDevices), ("appearance", PageAppearance), ("behavior", PageBehavior), ("battery", PageBattery), ("help", PageHelp) };
            if (!pages.Any(p => p.Item1 == section)) section = "display";
            _section = section;
            foreach (var (key, panel) in pages) panel.Visibility = key == section ? Visibility.Visible : Visibility.Collapsed;
            foreach (var button in SectionNavigation.Children.OfType<Button>())
            {
                bool selected = (string)button.Tag == section;
                button.Background = selected ? new SolidColorBrush(Palette.Mix(App.Theme.Surface, App.Theme.Accent, .18)) : Brushes.Transparent;
                button.BorderThickness = new Thickness(0);
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
            RootScroll.ScrollToTop();
        }
        private void OnSectionClick(object sender, RoutedEventArgs e) => SelectSection((string)((Button)sender).Tag);
        private void OnCloseButtonClick(object sender, RoutedEventArgs e) => Hide();
        private void OnSettingsSaveCompleted(bool success) => SaveStateText.Text = success ? $"已保存 · {DateTime.Now:HH:mm:ss}" : "保存失败，请检查数据目录是否可写";
        private void OnSafeModeChoice(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.SafeMode = SafeModeChoice.IsChecked == true;
            App.Battery.Configure(App.Settings);
            App.ScheduleDeviceRefresh();
            App.SaveSettings();
        }
        private void OnCheckUpdateClick(object sender, RoutedEventArgs e) => App.CheckUpdatesFromSettings();
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
                ? $"已勾选 {selected} 台，各显示方式最多显示 {BatteryService.DisplayDeviceLimit} 台；右键可管理设备"
                : "勾选同步到所有视图；隐藏或取消识别后，仍可在这里恢复";
        }

        public void ScrollToBottom()
        {
            RootScroll?.ScrollToEnd();
        }

        private static Color ReadableControl(Color candidate, Color text, params Color[] surfaces)
        {
            for (int step = 0; step <= 100; step++)
            {
                Color color = Palette.Mix(candidate, text, step / 100.0);
                if (surfaces.All(surface => ColorContrast.Ratio(color, surface) >= 3.2)) return color;
            }
            return text;
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            TextElement.SetForeground(Root, text);

            // 资源画刷跟着主题走：浅色主题用深字、深卡片，免得白字糊在白底上
            bool isLight = theme.IsLight;
            Resources["TextBrush"] = new SolidColorBrush(theme.Text);
            Resources["MenuSurfaceBrush"] = new SolidColorBrush(theme.Surface);
            Resources["ThemeChoiceBrush"] = new SolidColorBrush(Palette.Mix(theme.Surface, theme.Accent, .10));
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
            var cardColor = ((SolidColorBrush)Resources["CardBrush"]).Color;
            Color CardOver(Color background) => Palette.Mix(background, Color.FromRgb(cardColor.R, cardColor.G, cardColor.B), cardColor.A / 255.0);
            var surfaces = new[] { theme.BgTop, theme.BgBottom, CardOver(theme.BgTop), CardOver(theme.BgBottom) };
            Color outline = ReadableControl(Palette.Mix(theme.Surface, theme.Text, .38), theme.Text, surfaces);
            Color controlAccent = ReadableControl(theme.Accent, theme.Text, surfaces);
            Resources["ControlOutlineBrush"] = new SolidColorBrush(outline);
            Resources["ControlFillBrush"] = new SolidColorBrush(Palette.Mix(theme.Surface, theme.Text, .06));
            Resources["ControlAccentBrush"] = new SolidColorBrush(controlAccent);
            Resources["ControlMarkBrush"] = new SolidColorBrush(ColorContrast.Ratio(Colors.White, controlAccent) > ColorContrast.Ratio(Colors.Black, controlAccent) ? Colors.White : Colors.Black);
            foreach (var button in SectionNavigation.Children.OfType<Button>())
                button.Background = (string)button.Tag == _section ? new SolidColorBrush(Palette.Mix(theme.Surface, theme.Accent, .10)) : Brushes.Transparent;
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

        private void OnEditColors(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string target }) return;
            var settings = App.Settings;
            var values = target == "theme" ? settings.ThemeColors : target == "icon" ? settings.IconColors : settings.TrayColors;
            var palette = target == "theme" ? App.Theme : target == "icon"
                ? AppearanceColors.IconPalette(settings, App.Theme, settings.IconFollowTheme)
                : AppearanceColors.TrayPalette(settings, App.Theme);
            var editor = new ColorEditorWindow(target == "theme" ? "主题配色" : target == "icon" ? "悬浮配色" : "托盘配色",
                values, palette, target == "theme") { Owner = this };
            if (editor.ShowDialog() != true) return;
            if (target == "theme") settings.ThemeColors = editor.Draft;
            else if (target == "icon") settings.IconColors = editor.Draft;
            else settings.TrayColors = editor.Draft;
            App.RefreshTheme();
            App.SaveSettings();
            Reload();
        }

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

        private void OnDockWallpaperEngineChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.DockFollowWallpaperEngine = DockWallpaperEngineBox.IsChecked == true;
            App.Settings.DockWallpaperEngineSelection = "";
            App.Settings.DockWallpaperLiveScene = false;
            App.Dock?.ApplyBackground(App.Settings.DockBackground);
            App.SaveSettings();
        }

        private void OnPickBackground(object sender, RoutedEventArgs e)
        {
            var editor = new BackgroundEditorWindow(App.Settings) { Owner = this };
            if (editor.ShowDialog() != true) return;
            var draft = editor.Draft;
            if (!string.Equals(App.Settings.CustomBackground, draft.CustomBackground, StringComparison.OrdinalIgnoreCase))
            {
                App.Settings.DockBackground = "";
                App.Settings.DockWallpaperEngineSelection = "";
                App.Settings.DockWallpaperLiveScene = false;
                App.Settings.DockFollowWallpaperEngine = false;
            }
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
            App.Settings.DockBackground = "";
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
            App.Settings.IconStyle = mode switch { "character" => "photo", "mini" => "tile", _ => mode };
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
            App.Settings.IconMode = "character";
            App.Settings.IconStyle = "photo";
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
            App.Settings.IconMode = "photo";
            App.Settings.IconStyle = "photo";
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
            if (App.IsTestProfile)
            {
                bool previous = App.Settings.AutoStart;
                App.Settings.AutoStart = on;
                if (!SettingsStore.TrySave(App.Settings))
                {
                    App.Settings.AutoStart = previous;
                    AutoStartCheck.IsChecked = previous;
                    MessageBox.Show(this, "无法保存开机自启测试设置。", "MouseDock");
                }
                return;
            }
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

                Services.FeedbackService.OpenFeedbackMail(export);
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
                    Services.FeedbackService.OpenFeedbackMail);
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
            App.Icon?.UpdateValue();
            App.Taskbar?.UpdateDevices();
            App.Edge?.UpdateDevices();
            ApplyWarnButtons(pct);
            RebuildIconStyles();
            RebuildTaskbarStyles();
            RebuildEdgeStyles();
            App.SaveSettings();
        }

        /// <summary>高亮选中的提醒档位，包括 0%（不提醒）。</summary>
        private void ApplyWarnButtons(int selected)
        {
            var buttons = new[] { (Warn0, 0), (Warn10, 10), (Warn20, 20), (Warn30, 30), (Warn40, 40) };
            foreach (var (btn, pct) in buttons)
            {
                if (btn == null) continue;
                bool on = pct == selected;
                btn.Background = new SolidColorBrush(on ? Palette.Mix(App.Theme.Surface, App.Theme.Accent, .20) : App.Theme.Surface);
                btn.BorderBrush = on ? AccentBrush : Brushes.Transparent;
                btn.BorderThickness = new Thickness(on ? 1 : 0);
                btn.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        public void SyncIconScale()
        {
            bool prior=_loading;_loading=true;
            try{double value=FloatingIconGeometry.Scale(App.Settings,App.Settings.IconStyle);
                IconScaleSlider.Value=value;IconScaleValue.Text=$"{value*100:0}%";}
            finally{_loading=prior;}
        }
        private void OnIconScaleChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
        {
            if(_loading||IconScaleValue==null)return;
            FloatingIconGeometry.StoreScale(App.Settings,App.Settings.IconStyle,e.NewValue);IconScaleValue.Text=$"{e.NewValue*100:0}%";
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
                {
                    var photo = PhotoAssignments.Resolve(App.Settings, App.Battery.VisibleDevices.FirstOrDefault());
                    preview = new CharacterBatteryIcon
                    {
                        Width = 120, Height = 120, Level = 76,
                        Character = entry.Item1 == "portrait" ? "photo" : App.Settings.ReferenceCharacter,
                        PhotoPath = photo.Path, PhotoZoom = photo.Zoom, PhotoOffsetX = photo.OffsetX, PhotoOffsetY = photo.OffsetY,
                        Accent = new SolidColorBrush(App.Settings.IconFollowTheme ? App.Theme.Accent : ThemePreset.C("#A6F5BF"))
                    };
                }
                else
                {
                    var reference = new ReferenceBatteryIcon { StyleName=entry.Item1, Width=144,
                        Height=entry.Item1=="multi"?76:144,
                        Devices=new(){(DeviceKind.Mouse,76,false,false),(DeviceKind.Headset,-1,false,false)},
                        HideBackground=App.Settings.IconHideBackground };
                    reference.ApplyPalette(App.Theme, App.Settings);
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
            SyncIconScale();
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
            EditCharacter("portrait");
        }

        private void OnEditCharacter(object sender, RoutedEventArgs e) => EditCharacter(null);

        private void EditCharacter(string mode)
        {
            var editor = new CharacterEditorWindow(App.Settings, mode) { Owner = this };
            if (editor.ShowDialog() != true) return;
            ApplyCharacterEdit(editor);
        }

        private void ApplyCharacterEdit(CharacterEditorWindow editor)
        {
            editor.ApplyTo(App.Settings);
            if (editor.SurfaceMode == "icon") App.Icon?.ApplyStyle(App.Settings.IconStyle, false);
            if (editor.ShowAfterApply) App.SetSurface(editor.SurfaceMode);
            App.RefreshDeviceViews();
            App.SaveSettings();
            Reload();
        }

        private void OnClearReferencePhoto(object sender, RoutedEventArgs e)
        {
            App.Settings.PhotoAsset = "";
            App.Settings.IconBackground = "";
            App.Settings.DevicePhotos?.Clear();
            App.RefreshDeviceViews();
            App.SaveSettings();
            RebuildIconStyles(); RebuildTaskbarStyles(); RebuildEdgeStyles();
        }

        private void OnSurfaceClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is string mode)) return;
            App.SetSurface(mode);
        }


        private void OnTaskbarOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TaskbarOffsetValue == null) return;
            App.Settings.TaskbarOffset = Math.Clamp(Math.Round(e.NewValue), 0, 100);
            TaskbarOffsetValue.Text = $"{App.Settings.TaskbarOffset:0}%";
            App.Taskbar?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnTaskbarPositionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TaskbarPositionChoice?.SelectedValue is not string mode) return;
            App.Settings.TaskbarPinToTray = mode == "tray";
            if (!App.Settings.TaskbarPinToTray)
                App.Settings.TaskbarFollowIcons = mode == "follow";
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
            TaskbarPositionChoiceRow.Visibility = taskbar ? Visibility.Visible : Visibility.Collapsed;
            bool showOffset = taskbar && !App.Settings.TaskbarPinToTray;
            TaskbarIconOffsetRow.Visibility = showOffset && App.Settings.TaskbarFollowIcons ? Visibility.Visible : Visibility.Collapsed;
            TaskbarPositionRow.Visibility = showOffset && !App.Settings.TaskbarFollowIcons ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnEdgePositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || EdgePositionValue == null) return;
            App.Settings.EdgeVerticalPosition = Math.Round(e.NewValue) / 100;
            EdgePositionValue.Text = $"{e.NewValue:0}";
            App.Edge?.UpdateAnchor();
            App.SaveSettings();
        }

        private bool _syncingTaskbarSize;
        private bool _syncingEdgeSize;

        internal void SyncTaskbarSize()
        {
            double value = TaskbarPlacement.NormalizeSizePercent(App.Settings.TaskbarSizePercent);
            _syncingTaskbarSize = true;
            try { TaskbarSizeSlider.Value = value; }
            finally { _syncingTaskbarSize = false; }
            TaskbarSizeValue.Text = $"{value:0}%";
        }

        private void OnTaskbarSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || _syncingTaskbarSize || TaskbarSizeValue == null) return;
            ApplyTaskbarSize(e.NewValue);
        }

        private void ApplyTaskbarSize(double value)
        {
            App.Settings.TaskbarSizePercent = TaskbarPlacement.NormalizeSizePercent(value);
            SyncTaskbarSize();
            App.Taskbar?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnResetTaskbarSize(object sender, RoutedEventArgs e) => ApplyTaskbarSize(100);

        internal void SyncEdgeSize()
        {
            double value = ScreenEdgePlacement.NormalizeSizePercent(App.Settings.EdgeSizePercent);
            _syncingEdgeSize = true;
            try { EdgeSizeSlider.Value = value; }
            finally { _syncingEdgeSize = false; }
            EdgeSizeValue.Text = $"{value:0}%";
        }

        private void OnEdgeSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || _syncingEdgeSize || EdgeSizeValue == null) return;
            ApplyEdgeSize(e.NewValue);
        }

        private void ApplyEdgeSize(double value)
        {
            App.Settings.EdgeSizePercent = ScreenEdgePlacement.NormalizeSizePercent(value);
            SyncEdgeSize();
            App.Edge?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnResetEdgeSize(object sender, RoutedEventArgs e) => ApplyEdgeSize(100);

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
                }, style, true, false, new SolidColorBrush(theme.Text),
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
            var theme = App.Theme;
            var settings = App.Settings;
            var backdrop = Palette.Mix(theme.Surface, theme.Text, .035);
            var ink = ScreenEdgeWindow.ResolveForeground(settings, theme);
            var foreground = new SolidColorBrush(ColorContrast.Legible(ink, new[] { backdrop }, 4.5));
            var accent = ScreenEdgeWindow.ResolveBatteryBrush(settings, theme) as SolidColorBrush;
            var battery = new SolidColorBrush(ColorContrast.Legible(accent?.Color ?? ink, new[] { backdrop }, 3));
            var previewBackground = new SolidColorBrush(backdrop);
            foreach (var (style, label) in TaskbarBatteryStrip.Styles(true))
            {
                var device = new DeviceInfo { Kind = DeviceKind.Keyboard, Level = 80 };
                FrameworkElement preview;
                if (style == "ring")
                {
                    var badge = new DeviceBatteryBadge(device, foreground, true, true, settings.LowBatteryWarn);
                    badge.Ring.BatteryBrush = battery;
                    preview = badge;
                }
                else
                {
                    preview = new TaskbarBatteryStrip(new[] { device }, style, true, true,
                        foreground, settings.LowBatteryWarn, "", "", battery, settings, theme, screenEdge: true);
                }

                var name = style == "ring" ? "电量圆环" : label;
                var content = new DockPanel { IsHitTestVisible = false };
                content.Children.Add(new Border
                {
                    Width = 46, Height = 48, CornerRadius = new CornerRadius(7),
                    Background = previewBackground,
                    Child = new Viewbox { Margin = new Thickness(3), Child = preview }
                });
                content.Children.Add(new TextBlock
                {
                    Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(7, 0, 9, 0), TextWrapping = TextWrapping.Wrap
                });
                var choice = new RadioButton
                {
                    Tag = style, GroupName = "EdgeStyle", Content = content,
                    Style = (Style)FindResource("EdgeStyleChoice"),
                    IsChecked = TaskbarBatteryStrip.NormalizeStyle(settings.EdgeStyle) == style
                };
                System.Windows.Automation.AutomationProperties.SetName(choice, name);
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
        }

        /// <summary>三个形态按钮里高亮选中的那个，其余压暗。</summary>
        private void ApplySurfaceButtons(string mode)
        {
            if (string.IsNullOrEmpty(mode)) mode = "dock";
            ApplyTaskbarPositionControls(mode);
            IconOptionsPanel.Visibility = mode == "icon" ? Visibility.Visible : Visibility.Collapsed;
            ReferenceAppearanceOptions.Visibility = mode is "icon" or "taskbar" or "edge" ? Visibility.Visible : Visibility.Collapsed;
            TaskbarStylePanel.Visibility = mode == "taskbar" ? Visibility.Visible : Visibility.Collapsed;
            EdgeOptionsPanel.Visibility = mode == "edge" ? Visibility.Visible : Visibility.Collapsed;
            var buttons = new[] { (SurfaceDock, "dock"), (SurfaceMini, "mini"), (SurfaceIcon, "icon"),
                (SurfaceTaskbar, "taskbar"), (SurfaceEdge, "edge"), (SurfacePet, "pet") };
            foreach (var (btn, m) in buttons)
            {
                if (btn == null) continue;
                bool on = m == mode;
                btn.SetResourceReference(Control.ForegroundProperty, on ? "ControlAccentBrush" : "TextBrush");
                System.Windows.Automation.AutomationProperties.SetItemStatus(btn, on ? "当前显示方式" : "");
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


