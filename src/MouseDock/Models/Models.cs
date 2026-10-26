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

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public DeviceKind Kind { get; set; } = DeviceKind.Mouse;
        public string Source { get; set; } = "demo";
        public string Transport { get; set; } = "";
        public string Detail { get; set; } = "";

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
                Raise(nameof(LevelFraction));
                Raise(nameof(HasLevel));
            }
        }

        public bool Charging
        {
            get => _charging;
            set { if (_charging == value) return; _charging = value; Raise(nameof(Charging)); }
        }

        public bool Connected
        {
            get => _connected;
            set { if (_connected == value) return; _connected = value; Raise(nameof(Connected)); Raise(nameof(StatusText)); }
        }

        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; Raise(nameof(Selected)); }
        }

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

        public string StatusText => _connected ? "已连接" + (_charging ? " · 充电中" : "") : "待机";

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
            }
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
