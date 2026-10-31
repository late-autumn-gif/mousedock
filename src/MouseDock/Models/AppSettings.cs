using System.Collections.Generic;

namespace MouseDock.Models
{
    /// <summary>全部持久化状态，写到 %APPDATA%\MouseDock\settings.json。</summary>
    public sealed class AppSettings
    {
        public int Version { get; set; } = 2;

        public string ThemeId { get; set; } = "dusk";

        // 自定义背景（取主色）与手动覆盖
        public string CustomBackground { get; set; } = "";
        public string CustomAccent { get; set; } = "";
        public string CustomSurface { get; set; } = "";

        // 监视的设备 id（最多 4 台用于迷你视图，主卡片最多 6 行）
        public List<string> SelectedDeviceIds { get; set; } = new List<string>();

        // 悬浮图标形态：mini | character | photo
        public string IconMode { get; set; } = "photo";
        public string CharacterAsset { get; set; } = "";
        public string PhotoAsset { get; set; } = "";
        public double PhotoZoom { get; set; } = 1.0;
        public double PhotoOffsetX { get; set; }
        public double PhotoOffsetY { get; set; }

        public double Opacity { get; set; } = 0.96;
        public double Scale { get; set; } = 1.0;

        public bool ShowDock { get; set; } = true;
        public bool ShowMini { get; set; } = true;
        public bool ShowIcon { get; set; } = true;

        public bool AutoStart { get; set; }

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

        /// <summary>界面形态三选一：dock（主面板）/ mini（迷你）/ icon（悬浮图标）。</summary>
        public string SurfaceMode { get; set; } = "dock";

        /// <summary>悬浮图标样式：photo（照片）/ tile（圆角方块）/ ring（环形刻度）/ pill（横向胶囊）/ battery（经典电池）。</summary>
        public string IconStyle { get; set; } = "photo";

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
                CustomBackground = CustomBackground,
                CustomAccent = CustomAccent,
                CustomSurface = CustomSurface,
                SelectedDeviceIds = new List<string>(SelectedDeviceIds),
                IconMode = IconMode,
                CharacterAsset = CharacterAsset,
                PhotoAsset = PhotoAsset,
                PhotoZoom = PhotoZoom,
                PhotoOffsetX = PhotoOffsetX,
                PhotoOffsetY = PhotoOffsetY,
                Opacity = Opacity,
                Scale = Scale,
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
                IconStyle = IconStyle,
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
