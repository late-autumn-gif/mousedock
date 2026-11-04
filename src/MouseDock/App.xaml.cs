using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public static TaskbarWindow Taskbar { get; private set; }
        public static SettingsWindow SettingsView { get; private set; }

        public static List<string> CharacterAssets { get; } = new List<string>();
        public static string DefaultPhotoAsset { get; private set; }
        public static string DefaultBackgroundAsset { get; private set; }
        public static bool StartHidden { get; private set; }

        public static DeviceInfo PrimaryDevice =>
            Battery?.VisibleDevices.FirstOrDefault();

        private static SurfaceVisibility Surfaces => new SurfaceVisibility(Dock, Mini, Icon, Taskbar);
        private static DispatcherTimer _trayClickTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, "MouseDock.SingleInstance", out bool isNew);
            if (!isNew && !e.Args.Contains("--shot"))
            {
                // 已有实例常驻后台：发唤醒信号让它显示面板，本实例随即退出。
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

            // Provider 选择已从界面撤掉，统一走自动检测；
            // 旧 settings.json 里残留的 "json"/"demo"/"hid" 会让面板显示假数据，这里强制覆盖。
            Settings.Provider = "auto";

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
                            ShowCurrentSurface();
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
            if (Settings.Version < 5)
            {
                Settings.Version = 5;
                SettingsStore.Save(Settings);
            }
            ResolveAssets();

            Theme = Palette.ResolveTheme(Settings, out var extracted);
            ExtractedAccent = extracted;

            Battery = new BatteryService(Settings);
            Battery.PreferencesChanged += (_, __) =>
            {
                RefreshDeviceViews();
                UpdateTrayIcon();
                SaveSettings();
            };
            Battery.Poll();

            if (e.Args.Contains("--shot"))
            {
                RunScreenshots();
                Shutdown();
                return;
            }

            if (e.Args.Contains("--vendorbt"))
            {
                AttachConsole(-1);
                string hint2 = e.Args.Length > 1 && !e.Args[1].StartsWith("--") ? e.Args[1] : "Air Plus";
                string report = Services.Detection.VendorBt.ProbeAsync(hint2, 2000).GetAwaiter().GetResult();
                Console.WriteLine(report);
                Shutdown();
                return;
            }

            if (e.Args.Contains("--hfpbat"))
            {
                AttachConsole(-1);
                string hint = e.Args.Length > 1 && !e.Args[1].StartsWith("--") ? e.Args[1] : "Air Plus";
                int v = Services.Detection.HfpBattery.TryRead(hint, 8000, Console.WriteLine);
                Console.WriteLine("RESULT " + (v >= 0 ? v + "%" : "璇诲彇澶辫触"));
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
                    else Console.WriteLine($"  {i + 1}: 澶辫触");
                    System.Threading.Thread.Sleep(400);
                }
                Console.WriteLine($"鎴愬姛 {ok}/{n}");
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
            StartLowBatteryWatcher();
            ApplyTheme(false);
            SaveSettings();

            if (e.Args.Contains("--settings"))
                ShowSettings(Dock);
        }

        private static void ResolveAssets()
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            CharacterAssets.Clear();

            string photo = Path.Combine(assets, "demo-photo.png");
            DefaultPhotoAsset = File.Exists(photo) ? photo : null;

            string bg = Path.Combine(assets, "demo-bg.png");
            DefaultBackgroundAsset = File.Exists(bg) ? bg : null;
        }

        private void BuildWindows()
        {
            Dock = new DockWindow();
            Mini = new MiniWindow();
            Icon = new IconWindow();
            Taskbar = new TaskbarWindow();
            SettingsView = new SettingsWindow();

            PlaceWindow(Dock, Settings.DockX, Settings.DockY, 0.72, 0.30);
            PlaceWindow(Mini, Settings.MiniX, Settings.MiniY, 0.72, 0.62);
            PlaceWindow(Icon, Settings.IconX, Settings.IconY, 0.90, 0.34);

            ApplyIcon(Dock);
            ApplyIcon(Mini);
            ApplyIcon(Icon);
            ApplyIcon(SettingsView);
            ApplyAllBackgrounds();
            ApplyAllStyles();
            ApplyWindowBehavior();

            SyncSurfaceFlags();
            if (Settings.ShowDock && !StartHidden) Dock.Show();
            if (Settings.ShowMini && !StartHidden) Mini.Show();
            if (Settings.ShowIcon && !StartHidden) { Icon.Show(); Icon.ApplyStyle(Settings.IconStyle, false); }
            if (Settings.SurfaceMode == "taskbar" && !StartHidden) Taskbar.Show();

            // 第一次启动弹「关于」通告
            Dispatcher.BeginInvoke(new Action(ShowAboutIfFirstRun), DispatcherPriority.ApplicationIdle);

            // 启动后延迟检查更新。无网直接放弃，不打扰用户。
            _ = CheckUpdateOnStartupAsync();

            // 关闭只隐藏到后台，程序继续跑；只有托盘「退出」才真正结束
            Dock.Closing += (_, args) =>
            {
                // 桌面型态：任务栏关闭 / Alt+F4 一律忽略，面板始终留在桌面
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

        /// <summary>窗口图标从磁盘读，避免 XAML 里当嵌入资源找不到。</summary>
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
            menu.Items.Add("显示当前面板", null, (_, __) => ShowCurrentSurface());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("切换到卡片窗", null, (_, __) => SetSurface("dock"));
            menu.Items.Add("切换到迷你窗", null, (_, __) => SetSurface("mini"));
            menu.Items.Add("切换到悬浮图标", null, (_, __) => SetSurface("icon"));
            menu.Items.Add("固定到任务栏左侧", null, (_, __) => SetSurface("taskbar"));
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
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    // Wait for the double-click window so one double-click is not also a toggle.
                    if (_trayClickTimer == null)
                    {
                        _trayClickTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(WinForms.SystemInformation.DoubleClickTime)
                        };
                        _trayClickTimer.Tick += (_, __) =>
                        {
                            _trayClickTimer.Stop();
                            ToggleCurrentSurface();
                        };
                    }
                    _trayClickTimer.Stop();
                    _trayClickTimer.Start();
                }
            };
            _tray.MouseDoubleClick += (_, e) =>
            {
                if (e.Button != WinForms.MouseButtons.Left) return;
                _trayClickTimer?.Stop();
                ShowCurrentSurface();
            };
            _tray.BalloonTipClicked += (_, __) => ShowCurrentSurface();

            // 电量一刷新就把托盘图标重画一遍
            Battery.Refreshed += (_, __) => UpdateTrayIcon();
            UpdateTrayIcon();
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

        private static System.Windows.Forms.Timer _trayBlink;
        private static bool _trayBlinkOn;
        private static int _trayAnimationFrame;
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        /// <summary>托盘图标跟着电量走：正常绿、低电量红（还会闪）、充电绿、未知灰。</summary>
        private static void UpdateTrayIcon()
        {
            if (_tray == null) return;

            var dev = Battery.VisibleDevices
                .OrderByDescending(d => d.IsChargingActive).ThenByDescending(d => d.HasLevel).FirstOrDefault();

            int level = dev?.Level ?? -1;
            bool charging = dev?.IsChargingActive ?? false;
            bool low = level >= 0 && !charging && level <= Settings.LowBatteryWarn;

            try
            {
                var previousIcon = _tray.Icon;
                _tray.Icon = RenderTrayIcon(level, charging, low && _trayBlinkOn);
                previousIcon?.Dispose();
                string trayText = level >= 0
                    ? $"MouseDock · {dev.Name} {level}%" + (charging ? "（充电中）" : "")
                    : charging ? $"MouseDock · {dev.Name}（充电中，电量未知）" : "MouseDock · 电量，一起看见（双击唤起）";
                _tray.Text = trayText.Length <= 63 ? trayText : trayText.Substring(0, 60) + "…";
            }
            catch { }

            if (low || charging)
            {
                if (_trayBlink == null)
                {
                    _trayBlink = new System.Windows.Forms.Timer { Interval = 600 };
                    _trayBlink.Tick += (_, __) =>
                    {
                        _trayBlinkOn = !_trayBlinkOn;
                        _trayAnimationFrame = (_trayAnimationFrame + 1) % 6;
                        UpdateTrayIcon();
                    };
                }
                _trayBlink.Interval = charging ? 300 : 600;
                if (!_trayBlink.Enabled) _trayBlink.Start();
            }
            else
            {
                if (_trayBlink != null) _trayBlink.Stop();
                _trayBlinkOn = false;
                _trayAnimationFrame = 0;
            }
        }

        /// <summary>画一个 32×32 的圆角方块图标，里面写电量数字。</summary>
        private static System.Drawing.Icon RenderTrayIcon(int level, bool charging, bool dim)
        {
            const int size = 32;
            using var bmp = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(System.Drawing.Color.Transparent);

                System.Drawing.Color bg = charging
                    ? System.Drawing.Color.FromArgb(0x7F, 0xE7, 0xC4)
                    : level < 0
                        ? System.Drawing.Color.FromArgb(0x8A, 0x8A, 0x8A)
                        : level <= (Settings?.LowBatteryWarn ?? 20)
                            ? System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x6B)
                            : System.Drawing.Color.FromArgb(0x7F, 0xE7, 0xC4);
                if (dim) bg = System.Drawing.Color.FromArgb(bg.R / 3, bg.G / 3, bg.B / 3);

                using (var path = RoundedRect(new System.Drawing.Rectangle(1, 1, size - 2, size - 2), 7))
                using (var fill = new System.Drawing.SolidBrush(bg))
                    g.FillPath(fill, path);

                if (charging)
                {
                    using var outline = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x12, 0x1A, 0x18), 1.8f);
                    using var cell = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x12, 0x1A, 0x18));
                    g.DrawRectangle(outline, 5, 9, 20, 13);
                    g.FillRectangle(cell, 26, 13, 2, 5);
                    int filled = _trayAnimationFrame / 2 + 1;
                    for (int i = 0; i < filled; i++) g.FillRectangle(cell, 8 + i * 5, 12, 3, 7);
                }
                else
                {
                string txt = level >= 0 ? level.ToString() : "—";
                float px = txt.Length >= 3 ? 12f : 15f;
                using var font = new System.Drawing.Font("Segoe UI", px, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                using var fg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x12, 0x1A, 0x18));
                var sz = g.MeasureString(txt, font);
                g.DrawString(txt, font, fg,
                    (size - sz.Width) / 2f,
                    (size - sz.Height) / 2f + 1f);
                }
            }
            IntPtr handle = bmp.GetHicon();
            try { using var borrowed = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(System.Drawing.Rectangle r, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
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

        public static void ToggleCurrentSurface()
        {
            SyncSurfaceFlags();
            if (Settings.SurfaceMode == "taskbar")
            {
                ShowCurrentSurface();
                return;
            }
            var active = Surfaces.GetWindow(Settings.SurfaceMode);
            bool visible = active?.IsVisible == true;
            SaveWindowPositions();
            if (visible) Surfaces.Apply(Settings.SurfaceMode, false);
            else ShowCurrentSurface();
        }

        public static void ShowCurrentSurface()
        {
            ApplySurface();
            if (Settings.SurfaceMode != "taskbar")
                BringToFront(Surfaces.GetWindow(Settings.SurfaceMode));
        }

        /// <summary>显示并压到最前（Topmost 短暂切换一下，绕过 Windows 的前台限制）。</summary>
        private static void BringToFront(Window window)
        {
            if (window == null) return;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Topmost = Settings.AlwaysOnTop;
            window.Activate();
            try { window.Focus(); } catch { }
        }

        // ── 界面形态三选一：主面板 / 迷你 / 图标，同一时间只露一个 ──
        private static void SyncSurfaceFlags()
        {
            string mode = Settings.SurfaceMode;
            if (mode != "dock" && mode != "mini" && mode != "icon" && mode != "taskbar")
            {
                mode = Settings.ShowMini ? "mini" : Settings.ShowIcon ? "icon" : "dock";
                Settings.SurfaceMode = mode;
            }
            Settings.ShowDock = mode == "dock";
            Settings.ShowMini = mode == "mini";
            Settings.ShowIcon = mode == "icon";
        }

        public static void ApplySurface()
        {
            SyncSurfaceFlags();
            RefreshDeviceViews();
            Surfaces.Apply(Settings.SurfaceMode, true);
            if (Settings.ShowIcon) Icon?.ApplyStyle(Settings.IconStyle, false);
            if (Settings.SurfaceMode == "taskbar") Taskbar?.UpdateAnchor();
        }

        public static void ShowIconIfNeeded()
        {
            if (Icon == null) return;
            SetSurface("icon");
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

            // 显示方式三选一：同一时间只开一个，不再给三个独立开关
            string mode = Settings.SurfaceMode;
            menu.Items.Add(Item((mode == "dock" ? "● " : "○ ") + "主面板", () => SetSurface("dock")));
            menu.Items.Add(Item((mode == "mini" ? "● " : "○ ") + "迷你窗", () => SetSurface("mini")));
            menu.Items.Add(Item((mode == "icon" ? "● " : "○ ") + "悬浮图标", () => SetSurface("icon")));
            menu.Items.Add(Item((mode == "taskbar" ? "● " : "○ ") + "任务栏电量环", () => SetSurface("taskbar")));
            menu.Items.Add(new Separator());
            if (owner == Icon)
            {
                foreach (var (character, label) in new[] { ("otter", "充电水獭"), ("rikka", "六花"), ("roxy", "洛琪希") })
                {
                    menu.Items.Add(Item("动态形象 · " + label, () =>
                    {
                        Settings.AnimatedCharacter = character;
                        Settings.AnimatedPhotoBackground = true;
                        Settings.IconMode = "photo";
                        Icon.ApplyStyle("photo", true);
                        SaveSettings();
                    }));
                }
                menu.Items.Add(Item("背景 · 原照片", () =>
                {
                    Settings.AnimatedPhotoBackground = false;
                    Settings.IconMode = "photo";
                    Icon.ApplyStyle("photo", true);
                    SaveSettings();
                }));
                menu.Items.Add(new Separator());
            }
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

        /// <summary>右键菜单切显示方式：三选一，顺带把另外两个藏起来。</summary>
        public static void SetSurface(string mode)
        {
            Settings.SurfaceMode = mode;
            SaveSettings();
            ApplySurface();
            SettingsView?.Reload();
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
            // 设置窗口不跟面板缩放：它是固定尺寸的配置面板，放大缩小只会让控件挤成一团。
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
            Battery.SaveSelection();
            RefreshDeviceViews();
            UpdateTrayIcon();
            SaveSettings();
        }

        public static void RefreshDeviceViews()
        {
            Dock?.RefreshDevices();
            Mini?.Rebuild();
            Icon?.UpdateValue();
            Taskbar?.UpdateDevices();
        }

        public static void SaveWindowPositions()
        {
            if (Dock != null && Dock.IsVisible) { Settings.DockX = Dock.Left; Settings.DockY = Dock.Top; }
            if (Mini != null && Mini.IsVisible) { Settings.MiniX = Mini.Left; Settings.MiniY = Mini.Top; }
            if (Icon != null && Icon.IsVisible) { Settings.IconX = Icon.Left; Settings.IconY = Icon.Top; }
            SaveSettings();
        }

        public static void SaveSettings() => SettingsStore.Save(Settings);

        // ── 三个窗口各挑各的背景图 ──
        public static void PickBackground(string surface)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "鍥剧墖|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
            };
            if (dlg.ShowDialog() != true) return;
            SetBackground(surface, dlg.FileName);
        }

        public static void SetBackground(string surface, string path)
        {
            path ??= "";
            switch (surface)
            {
                case "dock":
                    Settings.DockBackground = path;
                    Dock?.ApplyBackground(path);
                    break;
                case "mini":
                    Settings.MiniBackground = path;
                    Mini?.ApplyBackground(path);
                    break;
                case "icon":
                    Settings.IconBackground = path;
                    Icon?.LoadPhoto();
                    break;
            }
            SaveSettings();
        }

        /// <summary>启动时把两个窗口的样式和三个窗口的背景贴回去。</summary>
        private static void ApplyAllBackgrounds()
        {
            Dock?.ApplyBackground(Settings.DockBackground);
            Mini?.ApplyBackground(Settings.MiniBackground);
        }

        private static void ApplyAllStyles()
        {
            Dock?.ApplyDensity(Settings.DockStyle);
            Mini?.ApplyDensity(Settings.MiniStyle);
            Icon?.ApplyShape(Settings.IconShape, false);
        }

        /// <summary>设置页改样式后统一重贴。</summary>
        public static void ApplySurfaceStyles()
        {
            Dock?.ApplyDensity(Settings.DockStyle);
            Mini?.ApplyDensity(Settings.MiniStyle);
            Icon?.ApplyShape(Settings.IconShape, false);
            Icon?.ApplyStyle(Settings.IconStyle, false);
        }

        // ── 窗口行为：置顶 / 鼠标穿透 ──
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public static void ApplyWindowBehavior()
        {
            foreach (var w in new Window[] { Dock, Mini, Icon })
            {
                if (w == null) continue;
                w.Topmost = Settings.AlwaysOnTop;
                SetClickThrough(w, Settings.ClickThrough);

                if (Settings.ClickThrough)
                {
                    // 穿透开着就点不到窗口，得留个能关的入口：托盘菜单里能改
                    w.IsHitTestVisible = false;
                }
                else
                {
                    w.IsHitTestVisible = true;
                }
            }
        }

        private static void SetClickThrough(Window w, bool on)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (handle == IntPtr.Zero) return;
            int ex = GetWindowLong(handle, GWL_EXSTYLE);
            ex = on
                ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED)
                : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(handle, GWL_EXSTYLE, ex);
        }

        // ── 低电量提醒：阈值 10/20/30/40，默认 20；同一台设备只提醒一次，回升后重新武装 ──
        private static readonly HashSet<string> _lowWarned = new HashSet<string>();
        private static DispatcherTimer _lowWarnTimer;

        private static void StartLowBatteryWatcher()
        {
            if (_lowWarnTimer != null) return;
            _lowWarnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _lowWarnTimer.Tick += (_, __) => CheckLowBattery();
            _lowWarnTimer.Start();
        }

        private static void CheckLowBattery()
        {
            try
            {
                int threshold = Settings?.LowBatteryWarn ?? 20;
                if (threshold <= 0 || Battery?.Devices == null) return;

                foreach (var d in Battery.Devices)
                {
                    if (!d.Connected || !d.HasLevel)
                    {
                        _lowWarned.Remove(d.Id);
                        continue;
                    }

                    // 正在充电不算低电；回升到阈值以上 5 个点后重新武装
                    if (d.Charging || d.Level < 0)
                    {
                        _lowWarned.Remove(d.Id);
                        continue;
                    }

                    if (d.Level <= threshold)
                    {
                        if (_lowWarned.Add(d.Id) && _tray != null)
                        {
                            _tray.BalloonTipTitle = $"{d.Name} 电量低";
                            _tray.BalloonTipText = $"只剩 {d.Level}%，低于 {threshold}% 提醒线。";
                            _tray.BalloonTipIcon = WinForms.ToolTipIcon.Warning;
                            _tray.ShowBalloonTip(5000);
                        }
                    }
                    else if (d.Level > threshold + 5)
                    {
                        _lowWarned.Remove(d.Id);
                    }
                }
            }
            catch { }
        }

        public static void ExitApp()
        {
            SaveWindowPositions();
            SaveDeviceSelection();
            _trayClickTimer?.Stop();
            _trayBlink?.Stop();
            _trayBlink?.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Icon?.Dispose(); _tray.Dispose(); }
            Current.Shutdown();
        }

        // ── 探测报告：--detect 打印本机真实外设，不依赖界面 ──

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
            catch (Exception ex) { text = "鎶撳彇澶辫触: " + ex.GetType().Name + " " + ex.Message; }

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
            sb.AppendLine("=== MouseDock 鏈満澶栬鎺㈡祴 ===");
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            var hid = new List<Services.Detection.DetectedDevice>();
            try
            {
                hid = Services.Detection.HidDiscovery.Enumerate();
                sb.AppendLine($"[HID] 命中 {hid.Count} 台");
                foreach (var line in Services.Detection.HidDiscovery.LastDiagnostics) sb.AppendLine("    · " + line);
            }
            catch (Exception ex) { sb.AppendLine("[HID] 澶辫触: " + ex.Message); }

            var bt = new List<Services.Detection.DetectedDevice>();
            try
            {
                bt = Services.Detection.BluetoothDiscovery.EnumerateAsync().GetAwaiter().GetResult();
                sb.AppendLine($"[蓝牙] 命中 {bt.Count} 台");
                foreach (var line in Services.Detection.BluetoothDiscovery.LastErrors) sb.AppendLine("    · " + line);
            }
            catch (Exception ex) { sb.AppendLine("[钃濈墮] 澶辫触: " + ex.GetType().Name + ": " + ex.Message); }

            sb.AppendLine();
            foreach (var d in hid.Concat(bt))
            {
                string level = d.Level >= 0 ? d.Level + "%" : "鏈煡";
                string charge = d.Charging ? "  充电=是" : "";
                sb.AppendLine($"[{d.Kind}] {d.Name}  电量={level}{charge}  连接={(d.Connected ? "是" : "否")}  通道={d.Transport}  ({d.Detail})");
                if (!string.IsNullOrWhiteSpace(d.Note)) sb.AppendLine("    璇存槑: " + d.Note);
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

        // ── 截图模式：dotnet run -- --shot 生成自检 PNG ──

        private void RunScreenshots()
        {
            string dir = ShotDir();
            Directory.CreateDirectory(dir);

            // 鎴浘瑕佸睍绀虹湡瀹炴帰娴嬬粨鏋?            Settings.Provider = "auto";
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

        // ---------- 自动更新 ----------

        private Window _updateWin;
        private System.Windows.Controls.ProgressBar _updateBar;

        /// <summary>
        /// 启动后延迟检查更新。断网 / 超时 / 用户关掉开关都静默跳过。
        /// </summary>
        private async Task CheckUpdateOnStartupAsync()
        {
            try
            {
                if (Settings != null && !Settings.AutoUpdate) return;

                await Task.Delay(TimeSpan.FromSeconds(10));

                var info = await Services.UpdateService.CheckAsync();
                if (info == null) return;

                bool go = Dispatcher.Invoke(() =>
                {
                    var msg = "发现新版本 " + info.Version + "（当前 " + Services.UpdateService.CurrentVersionText + "）\n\n" +
                              "安装包：" + info.AssetName + "（" + (info.AssetSize / 1024 / 1024) + " MB）\n\n" +
                              "现在下载并安装吗？安装时程序会自动重启。";
                    return MessageBox.Show(msg, "MouseDock 更新",
                        MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK;
                });

                if (!go) return;

                var progressForm = Dispatcher.Invoke(() => CreateUpdateProgress());
                var progress = progressForm == null ? null : new Progress<int>(p =>
                {
                    try { Dispatcher.Invoke(() => UpdateProgressBar(p)); } catch { }
                });

                bool ok = await Services.UpdateService.DownloadAndApplyAsync(info, progress);

                Dispatcher.Invoke(() =>
                {
                    CloseUpdateProgress();

                    if (ok)
                    {
                        MessageBox.Show("更新已下载，程序即将重启完成安装。", "MouseDock 更新",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                    }
                    else
                    {
                        MessageBox.Show("更新失败，请稍后重试或到 GitHub 手动下载。", "MouseDock 更新",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch
            {
                // 更新失败绝不能影响主程序
            }
        }

        private Window CreateUpdateProgress()
        {
            try
            {
                _updateBar = new System.Windows.Controls.ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = 0,
                    Height = 8, Margin = new Thickness(24, 16, 24, 8)
                };

                var text = new System.Windows.Controls.TextBlock
                {
                    Text = "正在下载更新…",
                    Margin = new Thickness(24, 20, 24, 0),
                    FontSize = 13
                };

                var stack = new System.Windows.Controls.StackPanel();
                stack.Children.Add(text);
                stack.Children.Add(_updateBar);

                _updateWin = new Window
                {
                    Title = "MouseDock 更新",
                    Content = stack,
                    Width = 380, Height = 120,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow,
                    Topmost = true,
                    ShowInTaskbar = true
                };
                _updateWin.Show();
                return _updateWin;
            }
            catch { return null; }
        }

        private void UpdateProgressBar(int percent)
        {
            try { if (_updateBar != null) _updateBar.Value = percent; } catch { }
        }

        private void CloseUpdateProgress()
        {
            try { _updateWin?.Close(); } catch { }
            _updateWin = null;
            _updateBar = null;
        }



        // ---------- 关于通告 ----------

        private void ShowAboutIfFirstRun()
        {
            try
            {
                if (Settings != null && Settings.HasShownAbout) return;

                Views.AboutWindow.Show(
                    MainWindow is Window w && w.IsVisible ? w : null,
                    true,
                    ExportErrorReport,
                    OpenFeedbackMail);

                if (Settings != null)
                {
                    Settings.HasShownAbout = true;
                    SettingsStore.Save(Settings);
                }
            }
            catch { }
        }

        /// <summary>
        /// 导出错误报告：先弹「另存为」问用户放哪，默认就是 exe 同目录。
        /// 用户取消不提示，写入失败才提示。
        /// </summary>
        internal static Services.ReportExportResult ExportErrorReport(Window owner)
        {
            try
            {
                var devices = Battery?.Devices?.ToList() ?? new System.Collections.Generic.List<Models.DeviceInfo>();
                var result = Services.FeedbackService.ExportReportWithPrompt(owner, Settings, devices);

                if (!result.Canceled && !result.Succeeded)
                {
                    MessageBox.Show(owner, "\u5bfc\u51fa\u5931\u8d25\uff1a\u62a5\u544a\u6ca1\u80fd\u5199\u5165\u6240\u9009\u7684\u4f4d\u7f6e\u3002\n\u6362\u4e2a\u76ee\u5f55\u518d\u8bd5\u4e00\u6b21\u3002",
                        "MouseDock", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return result;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(owner, "\u5bfc\u51fa\u5931\u8d25\uff1a" + ex.Message,
                    "MouseDock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return Services.ReportExportResult.Failed;
            }
        }

        /// <summary>写反馈邮件。</summary>
        private void OpenFeedbackMail(string reportPath)
        {
            try
            {
                bool ok = Services.FeedbackService.OpenFeedbackMail(reportPath);

                var msg = ok
                    ? "\u5df2\u6253\u5f00\u7f51\u9875\u7248 QQ \u90ae\u7bb1\u3002\n\n\u6536\u4ef6\u4eba\u3001\u4e3b\u9898\u548c\u6b63\u6587\u90fd\u5df2\u590d\u5236\u5230\u526a\u8d34\u677f\uff0c\n\u5728\u90ae\u4ef6\u9875\u9762\u6309 Ctrl+V \u76f4\u63a5\u7c98\u8d34\u5c31\u884c\u3002"
                    : "\u6253\u5f00\u6d4f\u89c8\u5668\u5931\u8d25\uff0c\u8bf7\u624b\u52a8\u8bbf\u95ee mail.qq.com\u3002\n\n\u5185\u5bb9\u5df2\u590d\u5236\u5230\u526a\u8d34\u677f\u3002";

                if (!string.IsNullOrEmpty(reportPath))
                    msg += "\n\n\u9519\u8bef\u62a5\u544a\uff1a\n" + reportPath + "\n\u8bf7\u628a\u5b83\u4f5c\u4e3a\u9644\u4ef6\u52a0\u5165\u90ae\u4ef6\u3002";

                MessageBox.Show(msg, "MouseDock \u53cd\u9988", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

}
}
