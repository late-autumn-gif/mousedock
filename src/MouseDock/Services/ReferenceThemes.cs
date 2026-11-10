using System;
using MouseDock.Models;

namespace MouseDock.Services
{
    public static class ReferenceThemes
    {
        // Background, surface, text, secondary text, accent, readable accent, hover, border, low battery.
        private static readonly (string Id, string Name, bool Light, string Dark, string Bright)[] Colors =
        {
            ("reference-mint", "默认主题", false,
                "141719 1b1f21 edf3ef bcc6c1 a6f5bf a6f5bf 29312d 303936 ff687d",
                "f6f7f7 ffffff 202b29 3e4b4c 53b89e 337161 e8eeea dde4df b42342"),
            ("pine", "松墨绿", false,
                "111916 1c2923 eaf2eb c8d3ca accbb3 c0d9c5 2c3c33 40584a ffb7a0",
                "edf1ec f9fcf7 273a2d 384944 52785d 355c40 dfe7dd bacaba a33a28"),
            ("ocean", "深海青", false,
                "101c20 1a2a30 e7f5f7 c6dade 7bc9d3 a8e0e6 293f48 3d5d68 ffb3a3",
                "e9f4f5 f9ffff 203e45 314853 247783 22636e d6e9eb aecdd2 a33b2f"),
            ("rose", "雾樱粉", true,
                "21191d 30242b f9edf2 decbd4 e4afc8 f0cada 44343e 654b5a ffba91",
                "f8eef2 fffbfd 462b38 543c4e a14f76 904366 efe0e7 dfc3d0 9c461d"),
            ("plum", "烟紫", false,
                "1b1720 29222f f4edf7 d1c6d8 cdb0dd dbc5e6 3b3043 584662 ffb29c",
                "f2edf5 fdfaff 382b42 473d58 86519f 704183 e7ddeb cebbd7 9b3923"),
            ("amber", "琥珀橙", false,
                "201912 30251b faeee0 deccb9 f1b77e f6cea2 443526 66503a ff9c9c",
                "f7efe6 fffcf7 453320 503f30 a15e24 8b4d1e ede0d0 d9c1a4 a52536")
        };

        public static ThemePreset Resolve(string id, string mode)
        {
            foreach (var item in Colors)
            {
                if (!string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase)) continue;
                bool light = mode == "light" || (mode != "dark" && item.Light);
                var c = (light ? item.Bright : item.Dark).Split(' ');
                return new ThemePreset
                {
                    Id = item.Id, Name = item.Name, IsLight = light,
                    BgTop = ThemePreset.C("#" + c[0]), BgBottom = ThemePreset.C("#" + c[0]),
                    Surface = ThemePreset.C("#" + c[1]), Text = ThemePreset.C("#" + c[2]),
                    TextDim = ThemePreset.C("#" + c[3]), Accent = ThemePreset.C("#" + c[4]),
                    Accent2 = ThemePreset.C("#" + c[5]), Hover = ThemePreset.C("#" + c[6]),
                    Border = ThemePreset.C("#" + c[7]), Low = ThemePreset.C("#" + c[8])
                };
            }
            return null;
        }
    }
}
