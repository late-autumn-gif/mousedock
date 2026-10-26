using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using WinForms = System.Windows.Forms;

namespace MouseDock
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private static WinForms.NotifyIcon _tray;
        private static EventWaitHandle _showSignal;

        public static AppSettings Settings { get; private set; } = new AppSettings();
        public static ThemePreset Theme { get; private set; } = ThemePreset.Presets[2];
        public static Color? ExtractedAccent { get; private set; }
        public static BatteryService Battery { get; private set; }

        public static DockWindow Dock { get; private set; }
        public static MiniWindow Mini { get; private set; }
        public static IconWindow Icon { get; private set; }
        public static SettingsWindow SettingsView { get; private set; }

        public static List<string> CharacterAssets { get; } = new List<string>();
        public static string DefaultPhotoAsset { get; private set; }
        public static string DefaultBackgroundAsset { get; private set; }
        public static bool StartHidden { get; private set; }

        public static DeviceInfo PrimaryDevice =>
            Battery?.Devices.FirstOrDefault(d => d.Selected && d.Connected)
            ?? Battery?.Devices.FirstOrDefault(d => d.Connected);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, "MouseDock.SingleInstance", out bool isNew);
            if (!isNew && !e.Args.Contains("--shot"))
            {
                // 已有实例常驻后台：发唤醒信号让它显示面板，本实例随即退出
                try
                {
                    if (EventWaitHandle.TryOpenExisting("MouseDock.ShowPanel", out var wake))
                    {
                        wake.Set();
                        wake.Dispose();
                    }
                }
                catch { }
                Shutdown();
                return;
            }

            Settings = SettingsStore.Load();

            // 常驻唤醒通道：其它实例（快捷方式/托盘）唤起面板
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "MouseDock.ShowPanel");
            var wakeThread = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (!_showSignal.WaitOne(TimeSpan.FromSeconds(5))) continue;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (Dock != null) BringToFront(Dock);
                            ShowAll();
                        }));
                    }
                    catch { }
                }
            })
            { IsBackground = true, Name = "MouseDock.Wake" };
            wakeThread.Start();
            StartHidden = e.Args.Contains("--tray");
            if (Settings.Version < 4)
            {
                // 老配置默认是演示数据；升级到自动检测真实设备
                Settings.Version = 4;
                Settings.Provider = "auto";
                Settings.RefreshSeconds = 5;
                Settings.SelectedDeviceIds.Clear();
                SettingsStore.Save(Settings);
            }
            ResolveAssets();

            Theme = Palette.ResolveTheme(Settings, out var extracted);
            ExtractedAccent = extracted;

            Battery = new BatteryService(Settings);
            RestoreSelection();
            Battery.Poll();

            if (e.Args.Contains("--shot"))
            {
                RunScreenshots();
                Shutdown();
                return;
            }

            if (e.Args.Contains("--battery"))
            {
                AttachConsole(-1);
                int ok = 0, n = 20;
                for (int i = 0; i < n; i++)
                {
                    int v = Services.Detection.VendorProtocol.TryReadBattery(0x089D, 0x062F);
                    if (v >= 0) { ok++; Console.WriteLine($"  {i + 1}: {v}%"); }
                    else Console.WriteLine($"  {i + 1}: 失败");
                    System.Threading.Thread.Sleep(400);
                }
                Console.WriteLine($"成功 {ok}/{n}");
                Shutdown();
                return;
            }

            if (e.Args.Contains("--detect"))
            {
                RunDetectReport();
                Shutdown();
                return;
            }

            if (e.Args.Contains("--capture24g"))
            {
                Run24GCapture(e.Args);
                Shutdown();
                return;
            }

            BuildWindows();
            BuildTray();
            ApplyTheme(false);
            SaveSettings();

            if (e.Args.Contains("--settings"))
                ShowSettings(Dock);
        }

        private static void ResolveAssets()
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            CharacterAssets.Clear();
            foreach (var name in new[] { "char-a.png", "char-b.png", "char-a.jpg", "char-b.jpg" })
            {
                string p = Path.Combine(assets, name);
                if (File.Exists(p)) CharacterAssets.Add(p);
            }

            string photo = Path.Combine(assets, "demo-photo.png");
            DefaultPhotoAsset = File.Exists(photo) ? photo : CharacterAssets.FirstOrDefault();

            string bg = Path.Combine(assets, "demo-bg.png");
            DefaultBackgroundAsset = File.Exists(bg) ? bg : null;
        }

        private static void RestoreSelection()
        {
            var ids = Settings.SelectedDeviceIds;
            bool hasAny = ids != null && ids.Count > 0;
            foreach (var d in Battery.Devices)
                d.Selected = !hasAny || ids.Contains(d.Id);
        }

        private void BuildWindows()
        {
            Dock = new DockWindow();
            Mini = new MiniWindow();
            Icon = new IconWindow();
            SettingsView = new SettingsWindow();

            PlaceWindow(Dock, Settings.DockX, Settings.DockY, 0.72, 0.30);
            PlaceWindow(Mini, Settings.MiniX, Settings.MiniY, 0.72, 0.62);
            PlaceWindow(Icon, Settings.IconX, Settings.IconY, 0.90, 0.34);

            ApplyIcon(Dock);
            ApplyIcon(Mini);
            ApplyIcon(Icon);
            ApplyIcon(SettingsView);

            if (Settings.ShowDock && !StartHidden) Dock.Show();
            if (Settings.ShowMini && !StartHidden) Mini.Show();
            if (Settings.ShowIcon && !StartHidden) { Icon.Show(); Icon.ApplyMode(Settings.IconMode, false); }

            // 关闭只隐藏到后台，程序继续跑；只有托盘「退出」才真正结束
            Dock.Closing += (_, args) =>
            {
                // 桌件形态：任务栏关闭 / Alt+F4 一律忽略，面板始终留在桌面上
                args.Cancel = true;
                SaveWindowPositions();
            };
            Mini.Closing += (_, args) =>
            {
                args.Cancel = true;
                SaveWindowPositions();
                Mini.Hide();
                NotifyStillRunning();
            };
            Icon.Closing += (_, args) =>
            {
                args.Cancel = true;
                SaveWindowPositions();
                Icon.Hide();
                NotifyStillRunning();
            };

            ApplyOpacity();
            ApplyScale();
        }

        /// <summary>窗口图标从磁盘读，避免 XAML 里当成 pack 资源找不到。</summary>
        private static void ApplyIcon(Window window)
        {
            if (window == null) return;
            try
            {
                string png = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.png");
                if (!File.Exists(png)) return;
                window.Icon = BitmapFrame.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch { }
        }

        private static bool _backgroundHintShown;

        /// <summary>第一次关窗时提示「还在后台」，避免用户以为程序退出了。</summary>
        private static void NotifyStillRunning()
        {
            if (_backgroundHintShown || _tray == null) return;
            _backgroundHintShown = true;
            try
            {
                _tray.BalloonTipTitle = "MouseDock 仍在后台运行";
                _tray.BalloonTipText = "面板没有退出。双击任务栏托盘图标即可重新唤起。";
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(4000);
            }
            catch { }
        }

        private void PlaceWindow(Window w, double savedX, double savedY, double rx, double ry)
        {
            var area = SystemParameters.WorkArea;
            bool valid = !double.IsNaN(savedX) && !double.IsNaN(savedY) &&
                         savedX > area.Left - 300 && savedX < area.Right + 100 &&
                         savedY > area.Top - 300 && savedY < area.Bottom + 100;

            if (valid)
            {
                w.Left = savedX;
                w.Top = savedY;
            }
            else
            {
                w.Left = area.Left + area.Width * rx - 100;
                w.Top = area.Top + area.Height * ry;
            }
        }

        private void BuildTray()
        {
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("显示全部面板（置前）", null, (_, __) => ShowAll());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("显示 / 隐藏 卡片窗", null, (_, __) => Toggle(Dock));
            menu.Items.Add("显示 / 隐藏 迷你窗", null, (_, __) => Toggle(Mini));
            menu.Items.Add("显示 / 隐藏 悬浮图标", null, (_, __) => Toggle(Icon));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("设置…", null, (_, __) => ShowSettings(Dock));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出 MouseDock", null, (_, __) => ExitApp());

            _tray = new WinForms.NotifyIcon
            {
                Icon = BuildTrayIcon(),
                Text = "MouseDock · 电量，一起看见（双击唤起）",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) Toggle(Dock);
            };
            _tray.DoubleClick += (_, __) => ShowAll();
            _tray.BalloonTipClicked += (_, __) => ShowAll();
        }


        /// <summary>资源管理器重启后把托盘图标补回去。</summary>
        public static void RestoreTrayIcon()
        {
            try
            {
                if (_tray == null) return;
                _tray.Visible = false;
                _tray.Visible = true;
            }
            catch { }
        }

        private static System.Drawing.Icon BuildTrayIcon()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app-32.png");
                if (File.Exists(path))
                {
                    using var bmp = new System.Drawing.Bitmap(path);
                    using var sized = new System.Drawing.Bitmap(bmp, 32, 32);
                    return System.Drawing.Icon.FromHandle(sized.GetHicon());
                }
            }
            catch { }

            try
            {
                using var bmp = new System.Drawing.Bitmap(32, 32);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 0x2A, 0x22, 0x48));
                    g.FillEllipse(bg, 1, 1, 30, 30);
                    using var accent = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 0xB4, 0x9C, 0xFF));
                    g.FillRectangle(accent, 9, 13, 14, 8);
                }
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
            catch
            {
                return System.Drawing.SystemIcons.Application;
            }
        }

        private static void Toggle(Window w)
        {
            if (w == null) return;
            if (w.IsVisible) w.Hide(); else BringToFront(w);
            SaveWindowPositions();
        }

        public static void ShowAll()
        {
            BringToFront(Dock);
            BringToFront(Mini);
            ShowIconIfNeeded();
        }

        /// <summary>显示并压到最前（Topmost 短暂切换一下，绕过 Windows 的前台限制）。</summary>
        private static void BringToFront(Window window)
        {
            if (window == null) return;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Topmost = false;          // 普通窗口层级：会被其它窗口盖住
            window.Activate();
            try { window.Focus(); } catch { }
        }

        public static void ShowIconIfNeeded()
        {
            if (Icon == null) return;
            Settings.ShowIcon = true;
            Icon.Show();
            Icon.ApplyMode(Settings.IconMode, false);
            SaveSettings();
        }

        public static void ShowSettings(Window owner)
        {
            if (SettingsView == null) SettingsView = new SettingsWindow();
            SettingsView.Reload();
            SettingsView.Show();
            SettingsView.Activate();
        }

        public static void ShowQuickMenu(Window owner)
        {
            var theme = Theme;
            var menu = new ContextMenu
            {
                Background = new SolidColorBrush(Palette.Mix(theme.BgTop, Colors.Black, 0.2))
            };

            MenuItem Item(string header, Action action)
            {
                var mi = new MenuItem { Header = header, Foreground = new SolidColorBrush(theme.Text) };
                mi.Click += (_, __) => action();
                return mi;
            }

            menu.Items.Add(Item(Dock != null && Dock.IsVisible ? "隐藏卡片窗" : "显示卡片窗", () => Toggle(Dock)));
            menu.Items.Add(Item(Mini != null && Mini.IsVisible ? "隐藏迷你窗" : "显示迷你窗", () => Toggle(Mini)));
            menu.Items.Add(Item(Icon != null && Icon.IsVisible ? "隐藏悬浮图标" : "显示悬浮图标", () => Toggle(Icon)));
            menu.Items.Add(new Separator());
            foreach (var p in ThemePreset.Presets)
            {
                var preset = p;
                menu.Items.Add(Item("主题 · " + preset.Name, () =>
                {
                    Settings.ThemeId = preset.Id;
                    Settings.CustomBackground = "";
                    RefreshTheme();
                    SaveSettings();
                    SettingsView?.Reload();
                }));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("设置…", () => ShowSettings(owner)));
            menu.Items.Add(Item("退出", ExitApp));

            menu.PlacementTarget = owner;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        public static void RefreshTheme()
        {
            Theme = Palette.ResolveTheme(Settings, out var extracted);
            ExtractedAccent = extracted;
            ApplyTheme(true);
            Mini?.Rebuild();
        }

        public static void ApplyTheme(bool animate)
        {
            Dock?.ApplyTheme(Theme, animate);
            Mini?.ApplyTheme(Theme, animate);
            Icon?.ApplyTheme(Theme, animate);
            SettingsView?.ApplyTheme(Theme, animate);
        }

        public static void ApplyOpacity()
        {
            double o = Math.Clamp(Settings.Opacity, 0.4, 1.0);
            if (Dock != null) Dock.Opacity = o;
            if (Mini != null) Mini.Opacity = o;
            if (Icon != null) Icon.Opacity = Math.Max(o, 0.85);
        }

        public static void ApplyScale()
        {
            double s = Math.Clamp(Settings.Scale, 0.7, 1.6);
            if (Dock?.Content is FrameworkElement dockRoot) dockRoot.LayoutTransform = new ScaleTransform(s, s);
            if (Mini?.Content is FrameworkElement miniRoot) miniRoot.LayoutTransform = new ScaleTransform(s, s);
            if (Icon?.Content is FrameworkElement iconRoot) iconRoot.LayoutTransform = new ScaleTransform(s, s);
            if (SettingsView?.Content is FrameworkElement setRoot) setRoot.LayoutTransform = new ScaleTransform(s, s);
        }

        private static DispatcherTimer _deviceDebounce;

        /// <summary>设备插拔后立即刷新（去抖 700ms，等系统枚举完）。</summary>
        public static void ScheduleDeviceRefresh()
        {
            if (_deviceDebounce == null)
            {
                _deviceDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _deviceDebounce.Tick += (_, __) =>
                {
                    _deviceDebounce.Stop();
                    Battery?.Poll();
                };
            }
            _deviceDebounce.Stop();
            _deviceDebounce.Start();
        }

        public static void SaveDeviceSelection()
        {
            Settings.SelectedDeviceIds = Battery.Devices.Where(d => d.Selected).Select(d => d.Id).ToList();
            SaveSettings();
        }

        public static void SaveWindowPositions()
        {
            if (Dock != null && Dock.IsVisible) { Settings.DockX = Dock.Left; Settings.DockY = Dock.Top; }
            if (Mini != null && Mini.IsVisible) { Settings.MiniX = Mini.Left; Settings.MiniY = Mini.Top; }
            if (Icon != null && Icon.IsVisible) { Settings.IconX = Icon.Left; Settings.IconY = Icon.Top; }
            SaveSettings();
        }

        public static void SaveSettings() => SettingsStore.Save(Settings);

        public static void ExitApp()
        {
            SaveWindowPositions();
            SaveDeviceSelection();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Current.Shutdown();
        }

        // ── 探测报告：--detect 打印本机真实外设，不依赖界面 ────────────────

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private void Run24GCapture(string[] args)
        {
            AttachConsole(-1);

            int seconds = 8;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--capture24g" && int.TryParse(args[i + 1], out int s)) seconds = Math.Clamp(s, 3, 60);

            int known = Battery.Devices.FirstOrDefault(d => d.HasLevel)?.Level ?? -1;
            string text;
            try { text = Services.Detection.HidCapture.Run(known, seconds); }
            catch (Exception ex) { text = "抓取失败: " + ex.GetType().Name + " " + ex.Message; }

            string path = Path.Combine(SettingsStore.Dir, "capture24g.txt");
            try
            {
                Directory.CreateDirectory(SettingsStore.Dir);
                File.WriteAllText(path, text, System.Text.Encoding.UTF8);
            }
            catch { }

            Console.WriteLine(text);
            Console.WriteLine("报告已写入: " + path);
        }

        private void RunDetectReport()
        {
            AttachConsole(-1);
            string path = WriteDetectReport(out string content);
            Console.WriteLine(content);
            Console.WriteLine("报告已写入: " + path);
        }

        /// <summary>生成探测报告（HID + 蓝牙），返回文件路径。</summary>
        public static string WriteDetectReport(out string content)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== MouseDock 本机外设探测 ===");
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            var hid = new List<Services.Detection.DetectedDevice>();
            try
            {
                hid = Services.Detection.HidDiscovery.Enumerate();
                sb.AppendLine($"[HID] 命中 {hid.Count} 台");
                foreach (var line in Services.Detection.HidDiscovery.LastDiagnostics) sb.AppendLine("    · " + line);
            }
            catch (Exception ex) { sb.AppendLine("[HID] 失败: " + ex.Message); }

            var bt = new List<Services.Detection.DetectedDevice>();
            try
            {
                bt = Services.Detection.BluetoothDiscovery.EnumerateAsync().GetAwaiter().GetResult();
                sb.AppendLine($"[蓝牙] 命中 {bt.Count} 台");
                foreach (var line in Services.Detection.BluetoothDiscovery.LastErrors) sb.AppendLine("    · " + line);
            }
            catch (Exception ex) { sb.AppendLine("[蓝牙] 失败: " + ex.GetType().Name + ": " + ex.Message); }

            sb.AppendLine();
            foreach (var d in hid.Concat(bt))
            {
                string level = d.Level >= 0 ? d.Level + "%" : "未知";
                sb.AppendLine($"[{d.Kind}] {d.Name}  电量={level}  连接={(d.Connected ? "是" : "否")}  通道={d.Transport}  ({d.Detail})");
                if (!string.IsNullOrWhiteSpace(d.Note)) sb.AppendLine("    说明: " + d.Note);
            }

            content = sb.ToString();
            string path = Path.Combine(SettingsStore.Dir, "detect.txt");
            try
            {
                Directory.CreateDirectory(SettingsStore.Dir);
                File.WriteAllText(path, content, System.Text.Encoding.UTF8);
            }
            catch { }
            return path;
        }

        // ── 截图模式：dotnet run -- --shot 生成自检 PNG ─────────────────────

        private void RunScreenshots()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots");
            Directory.CreateDirectory(dir);

            // 截图要展示真实探测结果
            Settings.Provider = "auto";
            Settings.SelectedDeviceIds.Clear();
            Battery.Configure(Settings);
            Battery.Poll();
            Pump(TimeSpan.FromMilliseconds(1800));

            var dock = new DockWindow();
            var mini = new MiniWindow();
            var iconChar = new IconWindow();
            var iconPhoto = new IconWindow();
            var settings = new SettingsWindow();

            ApplyIcon(dock); ApplyIcon(mini); ApplyIcon(iconChar); ApplyIcon(iconPhoto); ApplyIcon(settings);

            dock.Show();
            mini.Show();
            iconChar.Show();
            iconPhoto.Show();
            settings.Show();

            mini.Rebuild();
            settings.Reload();
            iconChar.ApplyMode("character", false);
            Settings.IconMode = "photo";
            iconPhoto.ApplyMode("photo", false);

            dock.ApplyTheme(Theme, false);
            mini.ApplyTheme(Theme, false);
            iconChar.ApplyTheme(Theme, false);
            iconPhoto.ApplyTheme(Theme, false);

            Pump(TimeSpan.FromMilliseconds(500));

            Capture(dock, Path.Combine(dir, "dock.png"));
            Capture(mini, Path.Combine(dir, "mini.png"));
            Capture(iconChar, Path.Combine(dir, "icon-character.png"));
            Capture(iconPhoto, Path.Combine(dir, "icon-photo.png"));
            Capture(settings, Path.Combine(dir, "settings.png"));
            settings.ScrollToBottom();
            Pump(TimeSpan.FromMilliseconds(220));
            Capture(settings, Path.Combine(dir, "settings-bottom.png"));

            // 浅色主题自检
            var light = ThemePreset.ById("cloud");
            dock.ApplyTheme(light, false);
            Pump(TimeSpan.FromMilliseconds(260));
            Capture(dock, Path.Combine(dir, "dock-light.png"));

            // 自定义背景取主色自检
            if (!string.IsNullOrEmpty(DefaultBackgroundAsset))
            {
                Settings.CustomBackground = DefaultBackgroundAsset;
                var custom = Palette.ResolveTheme(Settings, out var extracted);
                dock.ApplyTheme(custom, false);
                Pump(TimeSpan.FromMilliseconds(260));
                Capture(dock, Path.Combine(dir, "dock-custom.png"));
                File.WriteAllText(Path.Combine(dir, "extracted-accent.txt"),
                    $"{custom.Name} accent={Palette.ToHex(extracted ?? custom.Accent)} light={custom.IsLight} bgTop={Palette.ToHex(custom.BgTop)}");
            }

            foreach (var w in new Window[] { dock, mini, iconChar, iconPhoto, settings }) w.Close();
        }

        /// <summary>开发期截图输出目录：优先 MOUSEDOCK_SHOTS 环境变量，否则用临时目录。</summary>
        private static string ShotDir()
        {
            string dir = Environment.GetEnvironmentVariable("MOUSEDOCK_SHOTS");
            if (string.IsNullOrWhiteSpace(dir))
                dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MouseDock-shots");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Pump(TimeSpan span)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = span };
            timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Capture(Window window, string path, double scale = 2.0)
        {
            if (window?.Content is not FrameworkElement root) return;
            try
            {
                int w = (int)Math.Ceiling(root.ActualWidth * scale);
                int h = (int)Math.Ceiling(root.ActualHeight * scale);
                if (w <= 0 || h <= 0) return;

                var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(root);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                encoder.Save(fs);
            }
            catch { }
        }
    }
}
