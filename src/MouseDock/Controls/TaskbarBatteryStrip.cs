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
    public static string NormalizeStyle(string style) =>
        style is "panel" or "card" or "tile" or "pill" or "battery" or "plain" or "multi" ? style : "ring";

    /// <summary>卡片样式下单张卡片的逻辑尺寸（未被 Viewbox 缩放前的原始尺寸）。</summary>
    internal static Size CardSize(bool vertical) => vertical ? new Size(40, 58) : new Size(58, 40);
    public static IEnumerable<(string Id, string Label)> Styles(bool vertical) =>
        new[] { ("ring", "环内数字"), ("panel", "紧凑面板"), ("card", "迷你卡片"), ("tile", "柔角方块"),
            ("pill", vertical ? "竖向胶囊" : "横向胶囊"), ("battery", "经典电池"),
            ("plain", "轻量数字"), ("multi", "多电量迷你") };

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
    {
        StyleName = NormalizeStyle(style);
        bool panel = StyleName == "panel";
        bool card = StyleName == "card";
        bool multi = StyleName == "multi";
        var textBrush = ResolveBrush(textColor, foreground);
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
            _ => new Size(40, 40)
        };
        double length = Math.Max(1, items.Length) * ((vertical ? cell.Height : cell.Width) + (card ? 5 : 0))
            + (panel ? 8 + Math.Max(0, items.Length - 1) : 0) + (card ? 4 : 0);
        Width = vertical ? 40 : length;
        Height = vertical ? length : 40;
        if (multi)
        {
            var outline = textBrush.Clone();
            outline.Opacity *= .3;
            var fill = textBrush.Clone();
            fill.Opacity *= .08;
            Background = fill;
            BorderBrush = outline;
            BorderThickness = new Thickness(1);
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
                lowThreshold, textBrush, batteryBrush));
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
            Margin = new Thickness(0)
        };
        card.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));

        // 只有图标和数字。型号名去掉了：任务栏本来就窄，多一行小字既挤又糊，
        // 具体型号放进 ToolTip 和 AutomationProperties 里，悬停或读屏照样能看到。
        var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        if (vertical)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        card.Child = grid;

        var glyph = new DeviceGlyph
        {
            Width = vertical ? 24 : 22, Height = vertical ? 24 : 22, Thickness = 2.1,
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
            Margin = new Thickness(vertical ? 0 : 4, 0, 0, 0),
            Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false
        };
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
        if (vertical) Grid.SetRow(number, 1); else Grid.SetColumn(number, 1);
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
        Brush foreground, int lowThreshold, Brush textBrush, Brush batteryBrush)
    {
        bool panel = style == "panel";
        bool ringStyle = style == "ring";
        bool horizontalPill = style == "pill" && !vertical;
        var badge = new Grid
        {
            Name = "TaskbarDevice", DataContext = device, Background = Brushes.Transparent,
            Width = panel && vertical ? 36 : cell.Width,
            Height = panel && !vertical ? 36 : cell.Height
        };
        badge.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
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
                LowBatteryThreshold = lowThreshold, IsHitTestVisible = false
            };
            shape.SetBinding(ReferenceBatteryIcon.CompactLevelProperty, new Binding(nameof(DeviceInfo.Level)));
            shape.SetBinding(ReferenceBatteryIcon.CompactChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
            badge.Children.Add(shape);
        }
        bool tall = vertical && style is "pill" or "battery";
        var glyph = new DeviceGlyph
        {
            Width = showNumber ? (tall ? 16 : panel ? 14 : 12) : 21,
            Height = showNumber ? (tall ? 16 : panel ? 14 : 12) : 21,
            Thickness = 1.8, GlyphBrush = batteryBrush ?? (panel ? Brushes.White : foreground),
            HorizontalAlignment = horizontalPill && showNumber ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = showNumber ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = !showNumber ? new Thickness(0) : horizontalPill ? new Thickness(11, 14, 0, 0)
                : new Thickness(0, tall ? 13 : panel ? (badge.Height - 32) / 2 : 7, 0, 0),
            IsHitTestVisible = false
        };
        glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
        badge.Children.Add(glyph);
        var number = new TextBlock
        {
            Name = "BatteryNumber", Foreground = textBrush,
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
            FontSize = ringStyle ? 12 : 14, Height = ringStyle ? 15 : 18,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = horizontalPill ? new Thickness(27, 0, 5, 11) : new Thickness(6, 0, 6,
                tall ? 12 : panel ? (badge.Height - 32) / 2 : 5),
            Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false
        };
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
        badge.Children.Add(number);
        // Keep charging separate from the number, including three-digit values.
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
