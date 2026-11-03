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

        public DeviceKind Kind { get => (DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
        public int LowBatteryThreshold { get => (int)GetValue(LowBatteryThresholdProperty); set => SetValue(LowBatteryThresholdProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

        public DeviceBatteryRing()
        {
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
            Children.Add(glyph);
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
            if (Level <= 0) return;

            var color = Charging ? Color.FromRgb(56, 189, 248)
                : Level <= LowBatteryThreshold ? Color.FromRgb(248, 93, 104)
                : Color.FromRgb(52, 211, 153);
            var pen = new Pen(new SolidColorBrush(color), 2.5)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            if (Level >= 100)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
                return;
            }
            double angle = Level * 3.6;
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
        }
    }
}
