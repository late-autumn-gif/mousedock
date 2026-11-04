using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(SettingsWindow),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xB4, 0x9C, 0xFF))));

        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        public SettingsWindow()
        {
            InitializeComponent();
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

        public void Reload()
        {
            _loading = true;
            try
            {
                var s = App.Settings;

                var rows = ThemePreset.Presets
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
                TaskbarOffsetSlider.Value = Math.Clamp(s.TaskbarOffset, 0, 1200);
                TaskbarOffsetValue.Text = $"{TaskbarOffsetSlider.Value:0}";
                BatteryNumberBox.IsChecked = s.ShowBatteryNumber;
                EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveGlyphColor(s.EdgeGlyphColor));
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
            DeviceHint.Text = selected > 4
                ? $"已勾选 {selected} 台，任务栏与迷你视图显示前 5 台"
                : $"已勾选 {selected} 台";
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
                BgTopStop.Color = theme.BgTop;
                BgBottomStop.Color = theme.BgBottom;
            }
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
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择背景图（会自动提取主色）",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif"
            };
            if (dlg.ShowDialog(this) != true) return;

            App.Settings.CustomBackground = dlg.FileName;
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
            App.SaveDeviceSelection();
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
        private void OnExportReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var devices = App.Battery?.Devices?.ToList()
                              ?? new System.Collections.Generic.List<Models.DeviceInfo>();
                var export = Services.FeedbackService.ExportReportWithPrompt(this, App.Settings, devices);

                if (export.Canceled) return;

                if (!export.Succeeded)
                {
                    MessageBox.Show(this, "导出失败：报告没能写入所选的位置。\n换个目录再试一次。", "MouseDock",
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
        }

        /// <summary>写反馈邮件：先导出报告（问一次放哪），再打开邮件客户端。</summary>
        private void OnFeedbackClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var devices = App.Battery?.Devices?.ToList()
                              ?? new System.Collections.Generic.List<Models.DeviceInfo>();
                var export = Services.FeedbackService.ExportReportWithPrompt(this, App.Settings, devices);

                // 取消就是取消，别顺手把邮件也开了
                if (export.Canceled) return;

                if (export.Succeeded)
                    Services.FeedbackService.OpenReportFolder(export.Path);

                // 报告写不进去也得让人说话
                Services.FeedbackService.OpenFeedbackMail(export.Path);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "打开邮件失败：" + ex.Message, "MouseDock",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
            App.Taskbar?.UpdateDevices();
            App.Edge?.UpdateDevices();
            ApplyWarnButtons(pct);
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

        private void OnSurfaceClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is string mode)) return;
            App.SetSurface(mode);
        }

        private void OnPickIconBackground(object sender, RoutedEventArgs e) => App.PickBackground("icon");

        private void OnTaskbarOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TaskbarOffsetValue == null) return;
            App.Settings.TaskbarOffset = Math.Round(e.NewValue);
            TaskbarOffsetValue.Text = $"{App.Settings.TaskbarOffset:0}";
            App.Taskbar?.UpdateAnchor();
            App.SaveSettings();
        }

        private void OnBatteryNumberChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.ShowBatteryNumber = BatteryNumberBox.IsChecked == true;
            App.Taskbar?.UpdateDevices();
            App.Edge?.UpdateDevices();
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

        private void OnPickEdgeColor(object sender, RoutedEventArgs e)
        {
            var color = ScreenEdgeWindow.ResolveGlyphColor(App.Settings.EdgeGlyphColor);
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
            };
            if (dialog.ShowDialog(new DialogOwner(this)) != System.Windows.Forms.DialogResult.OK) return;
            ApplyEdgeColor($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        }

        private void OnResetEdgeColor(object sender, RoutedEventArgs e) => ApplyEdgeColor("#F5F5F5");

        private void ApplyEdgeColor(string color)
        {
            App.Settings.EdgeGlyphColor = color;
            EdgeColorSwatch.Background = new SolidColorBrush(ScreenEdgeWindow.ResolveGlyphColor(color));
            App.Edge?.UpdateDevices();
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
            TaskbarPositionRow.Visibility = mode == "taskbar" ? Visibility.Visible : Visibility.Collapsed;
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
