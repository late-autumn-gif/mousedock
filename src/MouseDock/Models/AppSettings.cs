using System.Collections.Generic;

namespace MouseDock.Models
{
    /// <summary>全部持久化状态，写到 %APPDATA%\MouseDock\settings.json。</summary>
    public sealed class AppSettings
    {
        public int Version { get; set; } = 6;

        public string ThemeId { get; set; } = "dusk";
        public string ThemeMode { get; set; } = "";

        // 自定义背景（取主色）与手动覆盖
        public string CustomBackground { get; set; } = "";
        public string CustomAccent { get; set; } = "";
        public string CustomSurface { get; set; } = "";
        public Dictionary<string, string> ThemeColors { get; set; } = new();
        public Dictionary<string, string> IconColors { get; set; } = new();
        public Dictionary<string, string> TrayColors { get; set; } = new();
        public double BackgroundZoom { get; set; } = 1;
        public double BackgroundPositionX { get; set; } = .5;
        public double BackgroundPositionY { get; set; } = .5;
        public double BackgroundDepth { get; set; } = .35;

        // 监视的设备 id（最多 4 台用于迷你视图，主卡片最多 6 行）
        public List<string> SelectedDeviceIds { get; set; } = new List<string>();
        public bool DeviceSelectionInitialized { get; set; }
        public bool HasDeviceSelection { get; set; }
        public List<string> HiddenDeviceIds { get; set; } = new List<string>();
        public List<string> DisabledRecognitionDeviceIds { get; set; } = new List<string>();
        public string HeadsetOutputId { get; set; } = "";
        public Dictionary<string, string> HeadsetMicrophoneBindings { get; set; } = new();

        // 悬浮图标形态：mini | character | photo
        public string IconMode { get; set; } = "photo";
        public string CharacterAsset { get; set; } = "";
        public string AnimatedCharacter { get; set; } = "otter";
        public string ReferenceCharacter { get; set; } = "rei";
        public bool AnimatedPhotoBackground { get; set; }
        public string PhotoAsset { get; set; } = "";
        public double PhotoZoom { get; set; } = 1.0;
        public double PhotoOffsetX { get; set; }
        public double PhotoOffsetY { get; set; }

        public double Opacity { get; set; } = 0.96;
        public double Scale { get; set; } = 1.0;
        public double IconScale { get; set; } = 1.0;
        public double? CharacterIconScale { get; set; }

        public bool ShowDock { get; set; } = true;
        public bool ShowMini { get; set; } = true;
        public bool ShowIcon { get; set; } = true;

        // 默认开启：绿色版解压就能用的人，通常就是希望它一直在托盘里显示电量。
        // 启动时会由 AutoStart.Sync 把 Run 项对齐到当前 exe，搬目录也跟得上。
        public bool AutoStart { get; set; } = true;

        // 面板背景：theme（主题渐变）/ image（自定义壁纸，可来自 Wallpaper Engine 项目预览图）
        // 安全模式：关闭所有厂商通道写操作，只走标准 HID / GATT 读取
        public bool SafeMode { get; set; }

        public string BackgroundMode { get; set; } = "theme";
        public string WallpaperPath { get; set; } = "";

        // 数据源：auto（自动检测真实设备）| demo | json | hid
        public string Provider { get; set; } = "auto";
        public string JsonPath { get; set; } = "";
        public int RefreshSeconds { get; set; } = 30;

        /// <summary>低电量提醒阈值（百分比）。档位 10/20/30/40，默认 20。</summary>
        public int LowBatteryWarn { get; set; } = 20;

        /// <summary>启动时自动检查更新。默认开；断网时静默跳过。</summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>是否已经展示过「关于」通告。只在第一次启动弹一次。</summary>
        public bool HasShownAbout { get; set; }

        /// <summary>界面形态三选一：dock（主面板）/ mini（迷你）/ icon（悬浮图标）。</summary>
        public string SurfaceMode { get; set; } = "taskbar";

        /// <summary>任务栏左侧/底部偏移，相对任务栏可用长度的百分比（0-100，v6 起；旧版是 DIP，启动时迁移）。</summary>
        public double TaskbarOffset { get; set; } = 8;
        public bool TaskbarFollowIcons { get; set; } = true;
        /// <summary>图标组外的逻辑像素间距；负数在前，非负数在后，0 紧贴右边缘。</summary>
        public double TaskbarIconOffset { get; set; }
        /// <summary>ring = 圆环；panel = 胶囊条；card = 迷你卡片。</summary>
        public string TaskbarStyle { get; set; } = "ring";
        public string TaskbarTextColor { get; set; } = "";
        public string TaskbarBatteryColor { get; set; } = "";
        public bool ShowBatteryNumber { get; set; } = true;
        public string EdgeGlyphColor { get; set; } = "#F5F5F5";
        public bool EdgeGlyphColorCustomized { get; set; }
        public string EdgeStyle { get; set; } = "ring";
        public double EdgeVerticalPosition { get; set; } = 0.5;
        public double EdgeInset { get; set; } = 12;

        /// <summary>悬浮图标样式：photo（照片）/ tile（圆角方块）/ ring（环形刻度）/ pill（横向胶囊）/ battery（经典电池）。</summary>
        public string IconStyle { get; set; } = "ring";
        public bool IconFollowTheme { get; set; }
        public bool IconHideBackground { get; set; }

        /// <summary>悬浮图标外形：soft（圆角）/ circle（圆形）/ square（方角）/ pill（横向胶囊）。</summary>
        public string IconShape { get; set; } = "soft";

        // 三个窗口各自的背景图，空字符串表示用预设主题渐变
        public string DockBackground { get; set; } = "";
        public string MiniBackground { get; set; } = "";
        public string IconBackground { get; set; } = "";

        /// <summary>窗口置顶。</summary>
        public bool AlwaysOnTop { get; set; } = false;

        /// <summary>鼠标穿透：点不到窗口，直接落到下面的程序上。</summary>
        public bool ClickThrough { get; set; } = false;

        /// <summary>卡片窗样式：standard（标准）/ compact（紧凑）/ bare（极简无底色）。</summary>
        public string DockStyle { get; set; } = "standard";

        /// <summary>迷你窗样式：standard / compact / bare。</summary>
        public string MiniStyle { get; set; } = "standard";

        // 窗口位置（NaN 表示还没放过）
        public double DockX { get; set; } = double.NaN;
        public double DockY { get; set; } = double.NaN;
        public double MiniX { get; set; } = double.NaN;
        public double MiniY { get; set; } = double.NaN;
        public double IconX { get; set; } = double.NaN;
        public double IconY { get; set; } = double.NaN;

        public AppSettings Clone()
        {
            return new AppSettings
            {
                ThemeId = ThemeId,
                ThemeMode = ThemeMode,
                CustomBackground = CustomBackground,
                CustomAccent = CustomAccent,
                CustomSurface = CustomSurface,
                ThemeColors = new(ThemeColors ?? new()),
                IconColors = new(IconColors ?? new()),
                TrayColors = new(TrayColors ?? new()),
                BackgroundZoom = BackgroundZoom,
                BackgroundPositionX = BackgroundPositionX,
                BackgroundPositionY = BackgroundPositionY,
                BackgroundDepth = BackgroundDepth,
                SelectedDeviceIds = new List<string>(SelectedDeviceIds),
                DeviceSelectionInitialized = DeviceSelectionInitialized,
                HiddenDeviceIds = new List<string>(HiddenDeviceIds),
                DisabledRecognitionDeviceIds = new List<string>(DisabledRecognitionDeviceIds),
                IconMode = IconMode,
                CharacterAsset = CharacterAsset,
                PhotoAsset = PhotoAsset,
                AnimatedCharacter = AnimatedCharacter,
                ReferenceCharacter = ReferenceCharacter,
                AnimatedPhotoBackground = AnimatedPhotoBackground,
                PhotoZoom = PhotoZoom,
                PhotoOffsetX = PhotoOffsetX,
                PhotoOffsetY = PhotoOffsetY,
                Opacity = Opacity,
                Scale = Scale,
                IconScale = IconScale,
                CharacterIconScale = CharacterIconScale,
                ShowDock = ShowDock,
                ShowMini = ShowMini,
                ShowIcon = ShowIcon,
                AutoStart = AutoStart,
                SafeMode = SafeMode,

                Provider = Provider,
                JsonPath = JsonPath,
                RefreshSeconds = RefreshSeconds,
                LowBatteryWarn = LowBatteryWarn,
                SurfaceMode = SurfaceMode,
                TaskbarOffset = TaskbarOffset,
                TaskbarFollowIcons = TaskbarFollowIcons,
                TaskbarIconOffset = TaskbarIconOffset,
            TaskbarStyle = TaskbarStyle,
            TaskbarTextColor = TaskbarTextColor,
            TaskbarBatteryColor = TaskbarBatteryColor,
                ShowBatteryNumber = ShowBatteryNumber,
                EdgeGlyphColor = EdgeGlyphColor,
                EdgeGlyphColorCustomized = EdgeGlyphColorCustomized,
                EdgeStyle = EdgeStyle,
                EdgeVerticalPosition = EdgeVerticalPosition,
                EdgeInset = EdgeInset,
                IconStyle = IconStyle,
                IconFollowTheme=IconFollowTheme, IconHideBackground=IconHideBackground,
                IconShape = IconShape,
                DockBackground = DockBackground,
                MiniBackground = MiniBackground,
                IconBackground = IconBackground,
                AlwaysOnTop = AlwaysOnTop,
                ClickThrough = ClickThrough,
                DockStyle = DockStyle,
                MiniStyle = MiniStyle,
                DockX = DockX, DockY = DockY,
                MiniX = MiniX, MiniY = MiniY,
                IconX = IconX, IconY = IconY
            };
        }
    }
}


