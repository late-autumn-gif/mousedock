using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Controls
{
    public sealed class DeviceBatteryRing : Grid
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DeviceKind), typeof(DeviceBatteryRing), new PropertyMetadata(DeviceKind.Other));
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
            nameof(Level), typeof(int), typeof(DeviceBatteryRing),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(
            nameof(Charging), typeof(bool), typeof(DeviceBatteryRing),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LowBatteryThresholdProperty = DependencyProperty.Register(
            nameof(LowBatteryThreshold), typeof(int), typeof(DeviceBatteryRing),
            new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(DeviceBatteryRing),
            new FrameworkPropertyMetadata(Brushes.WhiteSmoke, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ShowGlyphProperty = DependencyProperty.Register(
            nameof(ShowGlyph), typeof(bool), typeof(DeviceBatteryRing), new PropertyMetadata(true));
        public static readonly DependencyProperty BatteryBrushProperty = DependencyProperty.Register(
            nameof(BatteryBrush), typeof(Brush), typeof(DeviceBatteryRing),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FullProperty = DependencyProperty.Register(
            nameof(Full), typeof(bool), typeof(DeviceBatteryRing), new PropertyMetadata(false));
        public bool Full { get => (bool)GetValue(FullProperty); set => SetValue(FullProperty, value); }
        public static readonly DependencyProperty ShowChargingMarkProperty = DependencyProperty.Register(
            nameof(ShowChargingMark), typeof(bool), typeof(DeviceBatteryRing), new PropertyMetadata(true));

        public DeviceKind Kind { get => (DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
        public int LowBatteryThreshold { get => (int)GetValue(LowBatteryThresholdProperty); set => SetValue(LowBatteryThresholdProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
        public Brush BatteryBrush { get => (Brush)GetValue(BatteryBrushProperty); set => SetValue(BatteryBrushProperty, value); }
        public bool ShowGlyph { get => (bool)GetValue(ShowGlyphProperty); set => SetValue(ShowGlyphProperty, value); }
        public bool ShowChargingMark { get => (bool)GetValue(ShowChargingMarkProperty); set => SetValue(ShowChargingMarkProperty, value); }

        private readonly ChargeSweep _sweep;
        internal bool IsChargeSweepRunning => _sweep.IsRunning;
        public bool AnimateCharge { get; set; } = true;
        public DeviceBatteryRing()
        {
            _sweep = new ChargeSweep(this);
            Width = Height = 40;
            Background = Brushes.Transparent;
            var glyph = new DeviceGlyph
            {
                Width = 21, Height = 21, Thickness = 1.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(Kind)) { Source = this });
            glyph.SetBinding(DeviceGlyph.GlyphBrushProperty, new Binding(nameof(GlyphBrush)) { Source = this });
            glyph.SetBinding(VisibilityProperty, new Binding(nameof(ShowGlyph))
                { Source = this, Converter = new BooleanToVisibilityConverter() });
            Children.Add(glyph);
            var bolt = new TextBlock
            {
                Name = "ChargingMark", FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 12, Width = 12, Height = 14, Margin = new Thickness(0, 0, 1, 4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0, Opacity = 0.85
                }
            };
            var markStyle = new Style(typeof(TextBlock));
            markStyle.Setters.Add(new Setter(TextBlock.TextProperty, "\uE945"));
            var fullTrigger = new DataTrigger { Binding = new Binding(nameof(Full)) { Source = this }, Value = true };
            fullTrigger.Setters.Add(new Setter(TextBlock.TextProperty, "✓"));
            markStyle.Triggers.Add(fullTrigger);
            bolt.Style = markStyle;
            bolt.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(GlyphBrush)) { Source = this });
            var chargingVisibility = new MultiBinding { Converter = new ChargingVisibilityConverter() };
            chargingVisibility.Bindings.Add(new Binding(nameof(Charging)) { Source = this });
            chargingVisibility.Bindings.Add(new Binding(nameof(ShowChargingMark)) { Source = this });
            chargingVisibility.Bindings.Add(new Binding(nameof(Full)) { Source = this });
            bolt.SetBinding(VisibilityProperty, chargingVisibility);
            Children.Add(bolt);
        }

        private sealed class ChargingVisibilityConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                (values[0] is true || values[2] is true) && values[1] is true ? Visibility.Visible : Visibility.Collapsed;
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
            var trackBrush = GlyphBrush.Clone();
            trackBrush.Opacity = 0.28;
            var track = new Pen(trackBrush, 2.5);
            if (Level < 0) track.DashStyle = new DashStyle(new double[] { 1, 2 }, 0);
            dc.DrawEllipse(null, track, center, radius, radius);
            double phase = _sweep.Phase(AnimateCharge && Charging && Level > 0);
            double drawnLevel = _sweep.Follow(0, Level, phase >= 0);
            if (drawnLevel <= 0) return;

            var color = Charging ? Color.FromRgb(56, 189, 248)
                : Level <= LowBatteryThreshold ? Color.FromRgb(248, 93, 104)
                : Color.FromRgb(52, 211, 153);
            var pen = new Pen(BatteryBrush ?? new SolidColorBrush(color), 2.5)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            if (drawnLevel >= 100)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
                ChargeSweep.Arc(dc, center, radius, drawnLevel, pen.Thickness, phase);
                return;
            }
            double angle = drawnLevel * 3.6;
            double radians = (angle - 90) * Math.PI / 180;
            var figure = new PathFigure
            {
                StartPoint = new Point(center.X, center.Y - radius),
                IsClosed = false, IsFilled = false
            };
            figure.Segments.Add(new ArcSegment(
                new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians)),
                new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true));
            dc.DrawGeometry(null, pen, new PathGeometry(new[] { figure }));
            ChargeSweep.Arc(dc, center, radius, drawnLevel, pen.Thickness, phase);
        }
    }
}
