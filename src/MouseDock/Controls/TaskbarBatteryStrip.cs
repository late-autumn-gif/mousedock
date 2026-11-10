using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Controls;

public sealed class TaskbarBatteryStrip : Border
{
    public string StyleName { get; }
    public int DeviceCount { get; }
    public static string NormalizeStyle(string style) => style?.ToLowerInvariant() switch
    {
        "digits" => "plain",
        "compact" => "multi",
        "panel" or "card" or "tile" or "pill" or "battery" or "plain" or "multi" or "character" or "portrait" => style.ToLowerInvariant(),
        _ => "ring"
    };
    public static string AppearanceKey(AppSettings settings, ThemePreset theme) =>
        $"{settings.ReferenceCharacter}|{settings.IconBackground}|{settings.PhotoAsset}|{settings.PhotoZoom}|" +
        $"{settings.PhotoOffsetX}|{settings.PhotoOffsetY}|{settings.IconHideBackground}|{settings.IconFollowTheme}|" +
        $"{theme.Surface}|{theme.Text}|{theme.Accent}";

    /// <summary>卡片样式下单张卡片的逻辑尺寸（未被 Viewbox 缩放前的原始尺寸）。</summary>
    internal static Size CardSize(bool vertical) => vertical ? new Size(40, 62) : new Size(58, 40);
    public static IEnumerable<(string Id, string Label)> Styles(bool vertical) =>
        new[] { ("ring", "环内数字"), ("panel", "紧凑面板"), ("card", "迷你卡片"), ("tile", "柔角方块"),
            ("pill", vertical ? "竖向胶囊" : "横向胶囊"), ("battery", "经典电池"),
            ("plain", "轻量数字"), ("multi", "多电量迷你"), ("character", "角色电量"), ("portrait", "我的照片") };

    public TaskbarBatteryStrip(IEnumerable<DeviceInfo> devices, string style, bool showNumber,
        bool vertical, Brush foreground, int lowThreshold)
        : this(devices, style, showNumber, vertical, foreground, lowThreshold, "", "") { }

    public static Brush ResolveBrush(string value, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }
        catch { return fallback; }
    }

    public TaskbarBatteryStrip(IEnumerable<DeviceInfo> devices, string style, bool showNumber,
        bool vertical, Brush foreground, int lowThreshold, string textColor, string batteryColor)
        : this(devices, style, showNumber, vertical, foreground, lowThreshold, textColor, batteryColor, null) { }

    public TaskbarBatteryStrip(IEnumerable<DeviceInfo> devices, string style, bool showNumber,
        bool vertical, Brush foreground, int lowThreshold, string textColor, string batteryColor, Brush themeAccent)
        : this(devices, style, showNumber, vertical, foreground, lowThreshold, textColor, batteryColor, themeAccent, null, null) { }

    public TaskbarBatteryStrip(IEnumerable<DeviceInfo> devices, string style, bool showNumber,
        bool vertical, Brush foreground, int lowThreshold, string textColor, string batteryColor, Brush themeAccent,
        AppSettings appearance, ThemePreset theme)
    {
        StyleName = NormalizeStyle(style);
        bool panel = StyleName == "panel";
        bool card = StyleName == "card";
        bool multi = StyleName == "multi";
        appearance ??= new AppSettings();
        if (!appearance.IconHideBackground && theme != null && StyleName is "tile" or "pill" or "battery" or "multi")
            foreground = new SolidColorBrush(theme.Text);
        var textBrush = ResolveBrush(textColor, appearance.IconFollowTheme && theme != null
            ? new SolidColorBrush(theme.Text) : foreground);
        var batteryBrush = ResolveBrush(batteryColor, themeAccent);
        var items = devices.ToArray();
        DeviceCount = items.Length;
        Background = Brushes.Transparent;
        // 矢量绘制：不做位图缩放，任务栏尺寸下文字与图标保持清晰。
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
        Child = stack;
        if (panel)
            Padding = vertical ? new Thickness(1, 3, 1, 3) : new Thickness(3, 1, 3, 1);
        if (card)
            Padding = vertical ? new Thickness(1, 2, 1, 2) : new Thickness(2, 1, 2, 1);

        var cell = StyleName switch
        {
            "card" => CardSize(vertical),
            "pill" => vertical ? new Size(40, 64) : new Size(68, 40),
            "battery" => vertical ? new Size(40, 60) : new Size(64, 40),
            "plain" => vertical ? new Size(40, 52) : new Size(64, 40),
            "character" or "portrait" => vertical ? new Size(40, 68) : new Size(76, 40),
            _ => new Size(40, 40)
        };
        // 每张卡片占「本体 + 15px 间隔」，首尾各多出半个间隔，所以整体再加一个间隔。
        double length = Math.Max(1, items.Length) * ((vertical ? cell.Height : cell.Width) + (card ? 15 : 15))
            + (panel ? 8 + Math.Max(0, items.Length - 1) : 0);
        Width = vertical ? 40 : length;
        Height = vertical ? length : 40;
        if (multi)
        {
            Background = appearance.IconHideBackground ? Brushes.Transparent :
                new SolidColorBrush(theme?.Surface ?? ThemePreset.C("#1B1F21"));
            BorderThickness = new Thickness(0);
            CornerRadius = new CornerRadius(6);
            Width += vertical ? 0 : 2;
            Height += vertical ? 2 : 0;
            cell = vertical ? new Size(38, 40) : new Size(40, 38);
        }
        for (int i = 0; i < items.Length; i++)
        {
            if (panel && i > 0)
                stack.Children.Add(new Border
                {
                    Background = textBrush, Opacity = .4,
                    Width = vertical ? 24 : 1, Height = vertical ? 1 : 24,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            if (card)
            {
                stack.Children.Add(CreateCard(items[i], showNumber, vertical, foreground, lowThreshold,
                    textBrush, batteryBrush));
                continue;
            }
            stack.Children.Add(CreateBadge(items[i], StyleName, cell, showNumber, vertical, foreground,
                lowThreshold, textBrush, batteryBrush, appearance, theme));
        }
    }

    /// <summary>
    /// 迷你卡片：半透明圆角底（任务栏透出来）、左上角图标、粗体百分比、底部型号。
    /// 全部用矢量元素，不做图片缩放，所以缩小后不糊。
    /// </summary>
    private static Border CreateCard(DeviceInfo device, bool showNumber, bool vertical, Brush foreground,
        int lowThreshold, Brush textBrush, Brush batteryBrush)
    {
        var size = CardSize(vertical);
        var card = new Border
        {
            Name = "TaskbarDevice", DataContext = device,
            Width = size.Width, Height = size.Height,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0x59, 0x10, 0x10, 0x12)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            // 相邻卡片之间留 15px：两边各 7.5，合起来就是用户要的间隔。
            Margin = vertical ? new Thickness(0, 7.5, 0, 7.5) : new Thickness(7.5, 0, 7.5, 0)
        };
        card.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));

        // 只有图标和数字。型号名去掉了：任务栏本来就窄，多一行小字既挤又糊，
        // 具体型号放进 ToolTip 和 AutomationProperties 里，悬停或读屏照样能看到。
        // 图标和数字紧挨着排，中间固定 5px，整组在卡片里居中。
        // 之前贴左右边对齐，中间被拉开十几像素，看着像两块不相干的东西。
        var grid = new Grid
        {
            Margin = new Thickness(4, 4, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (vertical)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        card.Child = grid;

        var glyph = new DeviceGlyph
        {
            Width = vertical ? 22 : 20, Height = vertical ? 22 : 20, Thickness = 2.1,
            GlyphBrush = batteryBrush ?? foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
        if (vertical) Grid.SetRow(glyph, 0); else Grid.SetColumn(glyph, 0);
        grid.Children.Add(glyph);

        var number = new TextBlock
        {
            Name = "BatteryNumber", Foreground = textBrush,
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold,
            FontSize = 16, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false
        };
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
        if (vertical) Grid.SetRow(number, 2); else Grid.SetColumn(number, 2);
        grid.Children.Add(number);

        var charging = new TextBlock
        {
            Name = "ChargingMark", Text = "\uE945", FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = batteryBrush ?? textBrush, FontSize = 8, Width = 9, Height = 10,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0), IsHitTestVisible = false
        };
        charging.SetBinding(VisibilityProperty, new Binding(nameof(DeviceInfo.IsChargingActive))
            { Converter = new BooleanToVisibilityConverter() });
        grid.Children.Add(charging);

        foreach (var property in new[] { ToolTipProperty, AutomationProperties.NameProperty })
        {
            var description = new MultiBinding { Converter = new DescriptionConverter() };
            foreach (string name in new[] { nameof(DeviceInfo.Name), nameof(DeviceInfo.LevelText),
                nameof(DeviceInfo.StatusText), nameof(DeviceInfo.IsStale) })
                description.Bindings.Add(new Binding(name));
            card.SetBinding(property, description);
        }
        ToolTipService.SetInitialShowDelay(card, 300);
        return card;
    }

    private static Grid CreateBadge(DeviceInfo device, string style, Size cell, bool showNumber, bool vertical,
        Brush foreground, int lowThreshold, Brush textBrush, Brush batteryBrush, AppSettings appearance, ThemePreset theme)
    {
        bool panel = style == "panel";
        bool ringStyle = style == "ring";
        bool horizontalPill = style == "pill" && !vertical;
        var badge = new Grid
        {
            Name = "TaskbarDevice", DataContext = device, Background = Brushes.Transparent,
            Width = panel && vertical ? 36 : cell.Width,
            Height = panel && !vertical ? 36 : cell.Height,
            // 相邻设备的间隔统一 15px：两边各 7.5。
            Margin = vertical ? new Thickness(0, 7.5, 0, 7.5) : new Thickness(7.5, 0, 7.5, 0)
        };
        badge.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
        if (style is "character" or "portrait")
        {
            var content = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var portrait = new CharacterBatteryIcon
            {
                Width = 36, Height = 36, Character = style == "portrait" ? "photo" : appearance.ReferenceCharacter,
                PhotoPath = string.IsNullOrWhiteSpace(appearance.IconBackground) ? appearance.PhotoAsset : appearance.IconBackground,
                PhotoZoom = appearance.PhotoZoom, PhotoOffsetX = appearance.PhotoOffsetX, PhotoOffsetY = appearance.PhotoOffsetY,
                ShowNumber = false, ShowGlyph = false, Accent = batteryBrush ?? foreground
            };
            portrait.SetBinding(CharacterBatteryIcon.LevelProperty, new Binding(nameof(DeviceInfo.Level)));
            portrait.SetBinding(CharacterBatteryIcon.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
            content.Children.Add(portrait);
            var value = new TextBlock
            {
                Name = "BatteryNumber", FontFamily = new FontFamily("Segoe UI"), FontSize = 16,
                FontWeight = FontWeights.SemiBold, Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed
            };
            value.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
            if (vertical)
            {
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(value, 1);
            }
            else
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(value, 2);
            }
            content.Children.Add(value);
            badge.Children.Add(content);
            AttachCharging(badge, device, textBrush);
            return Finish(badge, device);
        }
        if (ringStyle)
        {
            var ring = new DeviceBatteryRing
            {
                GlyphBrush = batteryBrush ?? foreground, BatteryBrush = batteryBrush, LowBatteryThreshold = lowThreshold,
                ShowGlyph = false, ShowChargingMark = false
            };
            ring.SetBinding(DeviceBatteryRing.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
            ring.SetBinding(DeviceBatteryRing.LevelProperty, new Binding(nameof(DeviceInfo.Level)));
            ring.SetBinding(DeviceBatteryRing.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
            badge.Children.Add(ring);
        }
        else if (style is "tile" or "pill" or "battery" or "plain")
        {
            var shape = new ReferenceBatteryIcon
            {
                StyleName = style, Compact = true, Vertical = vertical, Ink = foreground,
                Accent = batteryBrush ?? Brushes.MediumSeaGreen, UseStatusColors = batteryBrush == null,
                LowBatteryThreshold = lowThreshold, IsHitTestVisible = false,
                Surface = theme != null ? new SolidColorBrush(theme.Surface) : new SolidColorBrush(ThemePreset.C("#1B1F21")),
                HideBackground = appearance.IconHideBackground
            };
            shape.SetBinding(ReferenceBatteryIcon.CompactLevelProperty, new Binding(nameof(DeviceInfo.Level)));
            shape.SetBinding(ReferenceBatteryIcon.CompactChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
            badge.Children.Add(shape);
        }
        // 环样式保持原样：数字居中落在环里，不放图标。环本身就是进度指示，
        // 再塞一个设备图标进去会把两者都挤变形，所以这里单独走一条路。
        if (ringStyle)
        {
            var ringNumber = new TextBlock
            {
                Name = "BatteryNumber", Foreground = textBrush,
                FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
                FontSize = 12, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed,
                IsHitTestVisible = false
            };
            ringNumber.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
            badge.Children.Add(ringNumber);
            AttachCharging(badge, device, textBrush);
            return Finish(badge, device);
        }

        // 图标和数字排成一组，中间固定 5px，整组在格子内居中。
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        bool stacked = vertical || style is "tile" or "multi";
        if (stacked)
        {
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var glyph = new DeviceGlyph
        {
            Width = showNumber ? (panel ? 13 : 14) : 21,
            Height = showNumber ? (panel ? 13 : 14) : 21,
            Thickness = 1.8, GlyphBrush = batteryBrush ?? (panel ? Brushes.White : foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
        Grid.SetColumn(glyph, 0);
        row.Children.Add(glyph);

        var number = new TextBlock
        {
            Name = "BatteryNumber", Foreground = textBrush,
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
            FontSize = ringStyle ? 12 : 14,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false
        };
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
        if (stacked) Grid.SetRow(number, 2); else Grid.SetColumn(number, 2);
        row.Children.Add(number);
        badge.Children.Add(row);
        AttachCharging(badge, device, textBrush);
        return Finish(badge, device);
    }

    /// <summary>充电标记叠在右上角，不占图标和数字的位置。</summary>
    private static void AttachCharging(Grid badge, DeviceInfo device, Brush textBrush)
    {
        var charging = new TextBlock
        {
            Name = "ChargingMark", Text = "\uE945", FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = textBrush, FontSize = 9, Width = 9, Height = 11,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 1, 0), IsHitTestVisible = false
        };
        charging.SetBinding(VisibilityProperty, new Binding(nameof(DeviceInfo.IsChargingActive))
            { Converter = new BooleanToVisibilityConverter() });
        badge.Children.Add(charging);
    }

    /// <summary>挂上悬停提示与读屏名称，返回格子本身。</summary>
    private static Grid Finish(Grid badge, DeviceInfo device)
    {
        foreach (var property in new[] { ToolTipProperty, AutomationProperties.NameProperty })
        {
            var description = new MultiBinding { Converter = new DescriptionConverter() };
            foreach (string name in new[] { nameof(DeviceInfo.Name), nameof(DeviceInfo.LevelText),
                nameof(DeviceInfo.StatusText), nameof(DeviceInfo.IsStale) })
                description.Bindings.Add(new Binding(name));
            badge.SetBinding(property, description);
        }
        ToolTipService.SetInitialShowDelay(badge, 300);
        return badge;
    }

    private sealed class NumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int level && level >= 0 ? level.ToString(CultureInfo.InvariantCulture) : "\u2014";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class DescriptionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            $"{values[0]} · {values[1]} · {values[2]}" + (values[3] is true ? " · 上次读数" : "");
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

}

