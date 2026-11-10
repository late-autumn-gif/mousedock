using System;
using System.ComponentModel;
using System.Windows.Media;

namespace MouseDock.Models
{
    public enum DeviceKind
    {
        Mouse,
        Headset,
        Keyboard,
        Controller,
        Other
    }

    /// <summary>一台被监视的设备。Level / Charging 变化会即时推给界面。</summary>
    public sealed class DeviceInfo : INotifyPropertyChanged
    {
        private int _level = -1;
        private bool _charging;
        private bool _connected = true;
        private bool _selected = true;
        private bool _stale;
        private bool _hidden;
        private bool _recognitionEnabled = true;
        public string RawName { get; set; } = "";
        public string ConfirmedModel { get; set; } = "";
        public bool InterfaceDiscovered { get; set; }
        public bool ReceiverDiscovered { get; set; }
        public bool PeripheralConfirmed { get; set; }
        public string[] DetectionIds { get; set; } = Array.Empty<string>();
        /// <summary>接管这台设备的读取器名（HandledBy 枚举的字符串形式），报告里用。</summary>
        public string HandledByLabel { get; set; } = "";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        private string _name = "";
        private DeviceKind _kind = DeviceKind.Mouse;
        private string _transport = "";
        public string Name { get => _name; set { if (_name == value) return; _name = value; Raise(nameof(Name)); } }
        public DeviceKind Kind { get => _kind; set { if (_kind == value) return; _kind = value; Raise(nameof(Kind)); Raise(nameof(KindLabel)); Raise(nameof(MetaLabel)); } }
        public string Source { get; set; } = "demo";
        public string Transport { get => _transport; set { if (_transport == value) return; _transport = value; Raise(nameof(Transport)); Raise(nameof(MetaLabel)); } }
        public string Detail { get; set; } = "";

        private int _dpi = -1;
        /// <summary>硬件 DPI；-1 表示没读到或设备不提供。</summary>
        public int Dpi
        {
            get => _dpi;
            set { if (_dpi == value) return; _dpi = value; Raise(nameof(Dpi)); Raise(nameof(DpiLabel)); }
        }
        public string DpiLabel => _dpi > 0 ? _dpi + " DPI" : "—";

        private string _note = "";
        public string Note
        {
            get => _note;
            set { if (_note == value) return; _note = value; Raise(nameof(Note)); Raise(nameof(HasNote)); }
        }

        public bool HasNote => !string.IsNullOrWhiteSpace(_note);

        public int Level
        {
            get => _level;
            set
            {
                int v = value < 0 ? -1 : Math.Clamp(value, 0, 100);
                if (_level == v) return;
                _level = v;
                Raise(nameof(Level));
                Raise(nameof(LevelText));
                Raise(nameof(MiniText));
                Raise(nameof(LevelFraction));
                Raise(nameof(HasLevel));
            }
        }

        public bool Charging
        {
            get => _charging;
            set { if (_charging == value) return; _charging = value; Raise(nameof(Charging)); Raise(nameof(StatusText)); Raise(nameof(IsChargingActive)); }
        }

        public bool Connected
        {
            get => _connected;
            set { if (_connected == value) return; _connected = value; Raise(nameof(Connected)); Raise(nameof(LevelText)); Raise(nameof(StatusText)); Raise(nameof(IsChargingActive)); Raise(nameof(IsDisplayEligible)); Raise(nameof(MiniText)); }
        }

        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; Raise(nameof(Selected)); Raise(nameof(IsDisplayEligible)); Raise(nameof(ManagementStatus)); }
        }

        public bool Hidden
        {
            get => _hidden;
            set { if (_hidden == value) return; _hidden = value; Raise(nameof(Hidden)); Raise(nameof(IsDisplayEligible)); Raise(nameof(ManagementStatus)); }
        }
        public bool RecognitionEnabled
        {
            get => _recognitionEnabled;
            set { if (_recognitionEnabled == value) return; _recognitionEnabled = value; Raise(nameof(RecognitionEnabled)); Raise(nameof(IsDisplayEligible)); Raise(nameof(ManagementStatus)); }
        }
        public bool IsDisplayEligible => Connected && Selected && !Hidden && RecognitionEnabled;
        public string ManagementStatus => !RecognitionEnabled ? "已取消识别 · 右键可重新识别" : Hidden ? "已隐藏 · 勾选可恢复" : Selected ? "已显示" : "未显示";

        public bool HasLevel => _level >= 0;

        public string LevelText => !_connected ? "--" : (_level >= 0 ? _level + "%" : "—");

        public double LevelFraction => _level >= 0 ? Math.Clamp(_level, 0, 100) / 100.0 : 0;

        /// <summary>数值来自上一次成功读取（设备休眠/通道忙），显示时降透明度表示「不是刚刚的读数」。</summary>
        public bool IsStale
        {
            get => _stale;
            set
            {
                if (_stale == value) return;
                _stale = value;
                Raise(nameof(IsStale));
                Raise(nameof(ValueOpacity));
                Raise(nameof(IsChargingActive));
                Raise(nameof(StatusText));
            }
        }

        public double ValueOpacity => _stale ? 0.45 : 1.0;

        /// <summary>迷你格用的短文案：有电量为 86%，否则明确写「未知」。</summary>
        public string MiniText => !_connected ? "离线" : (_level >= 0 ? _level + "%" : "未知");

        public string KindLabel => Kind switch
        {
            DeviceKind.Mouse => "鼠标",
            DeviceKind.Headset => "耳机",
            DeviceKind.Keyboard => "键盘",
            DeviceKind.Controller => "手柄",
            _ => "设备"
        };

        public bool IsChargingActive => _connected && _charging && !_stale;
        public string StatusText => !_connected ? "待机" : _stale ? "已连接 · 状态待刷新" : _charging ? "已连接 · 充电中" : "已连接";

        public string MetaLabel => KindLabel + (string.IsNullOrWhiteSpace(Transport) ? "" : " · " + Transport);

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>主题：卡片渐变 + 强调色 + 文字层级。</summary>
    public sealed class ThemePreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Color Accent { get; set; }
        public Color Accent2 { get; set; }
        public Color BgTop { get; set; }
        public Color BgBottom { get; set; }
        public Color Surface { get; set; }
        public Color Text { get; set; }
        public Color TextDim { get; set; }
        public Color Hover { get; set; }
        public Color Border { get; set; }
        public Color Low { get; set; }
        public bool IsLight { get; set; }

        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        /// <summary>视频里出现的四款预设。</summary>
        public static readonly ThemePreset[] Presets =
        {
            new ThemePreset
            {
                Id = "mint", Name = "深色薄荷",
                BgTop = C("#13211D"), BgBottom = C("#0A1411"), Surface = C("#16221F"),
                Accent = C("#7FE7C4"), Accent2 = C("#3FB894"),
                Text = C("#E7F4EF"), TextDim = C("#8FA8A0"), IsLight = false
            },
            new ThemePreset
            {
                Id = "cloud", Name = "云雾浅色",
                BgTop = C("#FBFCFE"), BgBottom = C("#E9EDF5"), Surface = C("#FFFFFF"),
                Accent = C("#7C8CF8"), Accent2 = C("#A9B4FF"),
                Text = C("#1E2230"), TextDim = C("#7C8496"), IsLight = true
            },
            new ThemePreset
            {
                Id = "dusk", Name = "暮色蓝紫",
                BgTop = C("#221B3B"), BgBottom = C("#120F22"), Surface = C("#1B1730"),
                Accent = C("#B49CFF"), Accent2 = C("#F0A6D0"),
                Text = C("#ECE7FA"), TextDim = C("#9A92B8"), IsLight = false
            },
            new ThemePreset
            {
                Id = "frost", Name = "冰雾磨砂",
                BgTop = C("#212A3B"), BgBottom = C("#131A27"), Surface = C("#1A2130"),
                Accent = C("#A8D8FF"), Accent2 = C("#8FE8DA"),
                Text = C("#E8F1FA"), TextDim = C("#8E9CB0"), IsLight = false
            },
            Services.ReferenceThemes.Resolve("reference-mint", ""),
            Services.ReferenceThemes.Resolve("pine", ""),
            Services.ReferenceThemes.Resolve("ocean", ""),
            Services.ReferenceThemes.Resolve("rose", ""),
            Services.ReferenceThemes.Resolve("plum", ""),
            Services.ReferenceThemes.Resolve("amber", "")
        };

        public static ThemePreset ById(string id)
        {
            foreach (var p in Presets)
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                    return p;
            return Presets[2];
        }

        public ThemePreset Clone() => (ThemePreset)MemberwiseClone();
    }
}

