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
        public string IconMode { get; set; } = "character";
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
                DockX = DockX, DockY = DockY,
                MiniX = MiniX, MiniY = MiniY,
                IconX = IconX, IconY = IconY
            };
        }
    }
}
