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
            Loaded += (_, __) =>
            {
                Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                Reload();
            };
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

                PickBgButton.Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));

                var theme = App.Theme;
                AccentSwatch.Background = new SolidColorBrush(theme.Accent);
                if (hasBg && App.ExtractedAccent.HasValue)
                {
                    AccentText.Text = $"已提取主色 {Palette.ToHex(App.ExtractedAccent.Value)} · " +
                                      $"背景{(App.ExtractedAccent.Value.R > 200 && App.ExtractedAccent.Value.G > 200 ? "浅色" : "深色")} · " +
                                      "主题已自动切换为「自定义」";
                }
                else if (hasBg)
                {
                    AccentText.Text = "背景图已选，但没能提取主色（可能是纯色图）";
                }
                else
                {
                    AccentText.Text = "未选择背景图 · 使用预设主题";
                }

                DeviceChecks.ItemsSource = App.Battery.Devices;
                UpdateDeviceHint();

                OpacitySlider.Value = s.Opacity;
                OpacityValue.Text = $"{s.Opacity * 100:0}%";
                ScaleSlider.Value = s.Scale;
                ScaleValue.Text = $"{s.Scale * 100:0}%";
                RefreshSlider.Value = Math.Clamp(s.RefreshSeconds, 5, 120);
                RefreshValue.Text = $"{s.RefreshSeconds}s";
                AutoStartCheck.IsChecked = AutoStart.IsEnabled();

                HighlightProvider(s.Provider);
            }
            finally { _loading = false; }

            if (SafeModeBox != null) SafeModeBox.IsChecked = App.Settings.SafeMode;

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

        private void OnSafeModeChanged(object sender, RoutedEventArgs e)
        {
            if (SafeModeBox == null) return;
            App.Settings.SafeMode = SafeModeBox.IsChecked == true;
            App.SaveSettings();
            App.Battery?.Poll();
        }

        private void UpdateDeviceHint()
        {
            int selected = App.Battery.Devices.Count(d => d.Selected);
            DeviceHint.Text = selected > 4
                ? $"已勾选 {selected} 台，迷你视图只取前 5 台"
                : "勾选后同步到迷你视图；同类设备可多台";
        }

        public void ScrollToBottom()
        {
            RootScroll?.ScrollToEnd();
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            TextElement.SetForeground(Root, text);
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
            App.Mini?.Rebuild();
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

        private void OnProviderAuto(object sender, RoutedEventArgs e) => SetProvider("auto");
        private void OnProviderDemo(object sender, RoutedEventArgs e) => SetProvider("demo");
        private void OnProviderJson(object sender, RoutedEventArgs e) => SetProvider("json");
        private void OnProviderHid(object sender, RoutedEventArgs e) => SetProvider("hid");

        private void SetProvider(string key)
        {
            App.Settings.Provider = key;
            App.Battery.Configure(App.Settings);
            App.SaveSettings();
            App.Mini?.Rebuild();
            HighlightProvider(key);
        }

        private void HighlightProvider(string key)
        {
            key = string.IsNullOrWhiteSpace(key) ? "auto" : key.ToLowerInvariant();
            var on = new SolidColorBrush(Color.FromArgb(0x30, 0xB4, 0x9C, 0xFF));
            var off = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));

            ProviderAuto.Background = key == "auto" ? on : off;
            ProviderDemo.Background = key == "demo" ? on : off;
            ProviderJson.Background = key == "json" ? on : off;
            ProviderHid.Background = key == "hid" ? on : off;

            ProviderHint.Text = key switch
            {
                "demo" => "演示用的假数据，只适合看界面效果",
                "json" => "外部脚本写 devices.json 驱动界面，格式见 samples\\devices.json",
                "hid" => "只读暴露标准 HID 电池用法的设备，多数 2.4G 接收器读不到",
                _ => "扫描本机 HID 与蓝牙设备；读不到电量就显示 —，不编数字"
            };
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

        private async void OnCapture24G(object sender, RoutedEventArgs e)
        {
            CaptureButton.IsEnabled = false;
            CaptureButton.Content = "抓取中… 请把鼠标切到 2.4G 并动一下";
            try
            {
                int known = App.Battery.Devices.FirstOrDefault(d => d.HasLevel)?.Level ?? -1;
                string text = await System.Threading.Tasks.Task.Run(
                    () => Services.Detection.HidCapture.Run(known, 8));

                string path = System.IO.Path.Combine(SettingsStore.Dir, "capture24g.txt");
                System.IO.Directory.CreateDirectory(SettingsStore.Dir);
                System.IO.File.WriteAllText(path, text, System.Text.Encoding.UTF8);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
            finally
            {
                CaptureButton.Content = "抓取 2.4G 报文（8 秒）";
                CaptureButton.IsEnabled = true;
            }
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

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            // 点在关闭按钮上时不拖动（否则 DragMove 会把 click 吃掉）
            if (e.OriginalSource is DependencyObject src && IsInside(src, CloseButton)) return;
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

        private void OnCloseClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Hide();
        }
    }
}
