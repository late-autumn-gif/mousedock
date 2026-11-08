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
    public static string NormalizeStyle(string style) => style == "panel" ? "panel" : "ring";

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
    {
        StyleName = NormalizeStyle(style);
        bool panel = StyleName == "panel";
        var textBrush = ResolveBrush(textColor, foreground);
        var batteryBrush = ResolveBrush(batteryColor, null);
        var items = devices.ToArray();
        DeviceCount = items.Length;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
        Child = stack;
        if (panel)
        {
            Padding = vertical ? new Thickness(1, 3, 1, 3) : new Thickness(3, 1, 3, 1);
        }
        double length = Math.Max(1, items.Length) * 40 + (panel ? 8 + Math.Max(0, items.Length - 1) : 0);
        Width = vertical ? 40 : length;
        Height = vertical ? length : 40;
        for (int i = 0; i < items.Length; i++)
        {
            if (panel && i > 0)
                stack.Children.Add(new Border
                {
                    Background = textBrush, Opacity = .4,
                    Width = vertical ? 24 : 1, Height = vertical ? 1 : 24,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            stack.Children.Add(CreateBadge(items[i], panel, showNumber, vertical, foreground,
                lowThreshold, textBrush, batteryBrush));
        }
    }

    private static Grid CreateBadge(DeviceInfo device, bool panel, bool showNumber, bool vertical,
        Brush foreground, int lowThreshold, Brush textBrush, Brush batteryBrush)
    {
        var badge = new Grid
        {
            Name = "TaskbarDevice", DataContext = device, Background = Brushes.Transparent,
            Width = panel && vertical ? 36 : 40,
            Height = panel && !vertical ? 36 : 40
        };
        badge.SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
        if (!panel)
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
        var glyph = new DeviceGlyph
        {
            Width = showNumber ? (panel ? 14 : 12) : 21,
            Height = showNumber ? (panel ? 14 : 12) : 21,
            Thickness = 1.8, GlyphBrush = batteryBrush ?? (panel ? Brushes.White : foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = showNumber ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = showNumber ? new Thickness(0, panel ? (badge.Height - 32) / 2 : 7, 0, 0) : new Thickness(0),
            IsHitTestVisible = false
        };
        glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
        badge.Children.Add(glyph);
        var number = new TextBlock
        {
            Name = "BatteryNumber", Foreground = textBrush,
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
            FontSize = panel ? 14 : 12, Height = panel ? 18 : 15,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 6, panel ? (badge.Height - 32) / 2 : 5),
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
