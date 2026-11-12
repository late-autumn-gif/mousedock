using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Controls;

public sealed class CharacterDeviceBadge : Grid
{
    public CharacterDeviceBadge(DeviceInfo device, CharacterBatteryIcon artwork, Brush ink,
        bool compact = false, bool vertical = false, bool showNumber = true)
    {
        DataContext = device;
        Background = Brushes.Transparent;
        Width = compact ? vertical ? 40 : 76 : 120;
        Height = compact ? vertical ? 68 : 40 : 158;
        double artHeight = compact ? vertical ? 36 : 18 : 112;
        double nameHeight = compact ? vertical ? 12 : 10 : 18;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(artHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(nameHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(Height - artHeight - nameHeight) });
        SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));

        artwork.DataContext = device;
        artwork.Width = artwork.Height = artHeight;
        artwork.ShowNumber = artwork.ShowGlyph = false;
        artwork.HorizontalAlignment = HorizontalAlignment.Center;
        artwork.SetBinding(CharacterBatteryIcon.LevelProperty, new Binding(nameof(DeviceInfo.Level)));
        artwork.SetBinding(CharacterBatteryIcon.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
        artwork.SetBinding(CharacterBatteryIcon.FullProperty, new Binding(nameof(DeviceInfo.IsFullActive)));
        artwork.SetBinding(CharacterBatteryIcon.KindProperty, new Binding(nameof(DeviceInfo.Kind)));
        Children.Add(artwork);

        var name = new TextBlock
        {
            Name = "CharacterDeviceName", FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = compact ? vertical ? 9 : 8 : 11, Foreground = ink,
            TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0)
        };
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Name)));
        name.SetBinding(ToolTipProperty, new Binding(nameof(DeviceInfo.Name)));
        SetRow(name, 1);
        Children.Add(name);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        SetRow(row, 2);
        Children.Add(row);
        var number = new TextBlock
        {
            Name = "BatteryNumber", FontFamily = new FontFamily("Segoe UI"), Foreground = ink,
            FontSize = compact ? vertical ? 15 : 10 : 19, FontWeight = FontWeights.SemiBold,
            LineHeight = compact && !vertical ? 12 : double.NaN, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed
        };
        number.SetBinding(TextBlock.TextProperty, compact
            ? new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() }
            : new Binding(nameof(DeviceInfo.LevelText)));
        if (!compact)
            number.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(DeviceInfo.Level))
                { Converter = new StateBrush(ink, artwork.Low, artwork.Muted) });
        row.Children.Add(number);
        var full = new TextBlock { Name = "FullMark", Foreground = ink, FontSize = number.FontSize,
            LineHeight = number.LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(2, 0, 0, 0) };
        full.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.ChargeMark)));
        full.SetBinding(VisibilityProperty, new Binding(nameof(DeviceInfo.IsFullActive))
            { Converter = new BooleanToVisibilityConverter() });
        row.Children.Add(full);
        var charging = new TextBlock { Name = "ChargingMark", Text = "\uE945",
            FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = ink,
            FontSize = compact ? 8 : 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        charging.SetBinding(VisibilityProperty, new Binding(nameof(DeviceInfo.IsChargingActive))
            { Converter = new BooleanToVisibilityConverter() });
        row.Children.Add(charging);
    }

    private sealed class NumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int level && level >= 0 ? level.ToString(CultureInfo.InvariantCulture) : "\u2014";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private sealed class StateBrush(Brush ink, Brush low, Brush muted) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            AppearanceColors.Battery(value is int level ? level : -1, ink, low, muted);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
