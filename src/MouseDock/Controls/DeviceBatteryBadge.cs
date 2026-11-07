using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Controls
{
    public sealed class DeviceBatteryBadge : Grid
    {
        public DeviceBatteryRing Ring { get; }

        public DeviceBatteryBadge(DeviceInfo device, Brush foreground, bool showNumber, bool vertical, int lowThreshold, bool compact = false)
        {
            DataContext = device;
            Background = Brushes.Transparent;
            bool below = vertical || compact;
            double ringSize = compact && showNumber ? 26 : 40;
            Width = showNumber && !below ? 72 : 40;
            Height = showNumber && below ? (compact ? 40 : 56) : 40;
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(ringSize) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(showNumber && below ? (compact ? 14 : 16) : 0) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showNumber && !below ? 32 : 0) });

            Ring = new DeviceBatteryRing { GlyphBrush = foreground, LowBatteryThreshold = lowThreshold,
                HorizontalAlignment=HorizontalAlignment.Center };
            Ring.SetBinding(DeviceBatteryRing.KindProperty, new Binding(nameof(DeviceInfo.Kind)) { Source = device });
            Ring.SetBinding(DeviceBatteryRing.LevelProperty, new Binding(nameof(DeviceInfo.Level)) { Source = device });
            // 只认「已连接且不是缓存值」的充电状态，断线时别让动画一直转。
            Ring.SetBinding(DeviceBatteryRing.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)) { Source = device });
            Children.Add(new Viewbox
            {
                Width = ringSize, Height = ringSize, Child = Ring,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var number = new TextBlock
            {
                Name = "BatteryNumber", Foreground = foreground, FontSize = compact ? 10 : 11,
                FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed
            };
            number.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.Level)) { Converter = new NumberConverter() });
            SetRow(number, below ? 1 : 0);
            SetColumn(number, below ? 0 : 1);
            Children.Add(number);
            SetBinding(OpacityProperty, new Binding(nameof(DeviceInfo.ValueOpacity)));
            var description = new MultiBinding { Converter = new DescriptionConverter(), ConverterParameter = "\n" };
            description.Bindings.Add(new Binding(nameof(DeviceInfo.Name)));
            description.Bindings.Add(new Binding(nameof(DeviceInfo.LevelText)));
            description.Bindings.Add(new Binding(nameof(DeviceInfo.StatusText)));
            description.Bindings.Add(new Binding(nameof(DeviceInfo.IsStale)));
            SetBinding(ToolTipProperty, description);
            var accessible = new MultiBinding { Converter = new DescriptionConverter(), ConverterParameter = " · " };
            accessible.Bindings.Add(new Binding(nameof(DeviceInfo.Name)));
            accessible.Bindings.Add(new Binding(nameof(DeviceInfo.LevelText)));
            accessible.Bindings.Add(new Binding(nameof(DeviceInfo.StatusText)));
            accessible.Bindings.Add(new Binding(nameof(DeviceInfo.IsStale)));
            SetBinding(AutomationProperties.NameProperty, accessible);
            ToolTipService.SetInitialShowDelay(this, 300);
        }

        private sealed class DescriptionConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
                $"{values[0]}{parameter}{values[1]} · {values[2]}" +
                (values[3] is true ? " · 上次读数" : "");
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class NumberConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
                value is int level && level >= 0 ? level.ToString(CultureInfo.InvariantCulture) : "\u2014";
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
                Binding.DoNothing;
        }
    }
}
