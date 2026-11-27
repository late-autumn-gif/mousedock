using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;

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
            nameof(Full), typeof(bool), typeof(DeviceBatteryRing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public bool Full { get => (bool)GetValue(FullProperty); set => SetValue(FullProperty, value); }
        public DeviceKind Kind { get => (DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
        public int LowBatteryThreshold { get => (int)GetValue(LowBatteryThresholdProperty); set => SetValue(LowBatteryThresholdProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
        public Brush BatteryBrush { get => (Brush)GetValue(BatteryBrushProperty); set => SetValue(BatteryBrushProperty, value); }
        public bool ShowGlyph { get => (bool)GetValue(ShowGlyphProperty); set => SetValue(ShowGlyphProperty, value); }
        private static readonly Brush DefaultFill = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        private const double DefaultSize = 40;
        private const double RingInset = 7;
        private const double DefaultRadius = DefaultSize / 2 - RingInset;
        private readonly ChargeSweep _sweep;
        internal bool IsChargeSweepRunning => _sweep.IsRunning;
        public bool AnimateCharge { get; set; } = true;
        public bool ContinuousRing { get; init; }
        public DeviceBatteryRing()
        {
            _sweep = new ChargeSweep(this);
            Width = Height = DefaultSize;
            Background = Brushes.Transparent;
            var glyph = new DeviceGlyph
            {
                Thickness = 1.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            glyph.SetBinding(DeviceGlyph.KindProperty, new Binding(nameof(Kind)) { Source = this });
            glyph.SetBinding(DeviceGlyph.GlyphBrushProperty, new Binding(nameof(GlyphBrush)) { Source = this });
            glyph.SetBinding(VisibilityProperty, new Binding(nameof(ShowGlyph))
                { Source = this, Converter = new BooleanToVisibilityConverter() });
            Children.Add(glyph);
            var glyphStyle = new Style(typeof(DeviceGlyph));
            glyphStyle.Setters.Add(new Setter(WidthProperty, DefaultRadius * BatteryRingDrawing.GlyphRadiusRatio));
            glyphStyle.Setters.Add(new Setter(HeightProperty, DefaultRadius * BatteryRingDrawing.GlyphRadiusRatio));
            var headset = new Trigger { Property = DeviceGlyph.KindProperty, Value = DeviceKind.Headset };
            headset.Setters.Add(new Setter(WidthProperty, DefaultRadius * BatteryRingDrawing.HeadsetGlyphRadiusRatio));
            headset.Setters.Add(new Setter(HeightProperty, DefaultRadius * BatteryRingDrawing.HeadsetGlyphRadiusRatio));
            glyphStyle.Triggers.Add(headset);
            glyph.Style = glyphStyle;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - RingInset);
            var trackBrush = GlyphBrush.Clone();
            trackBrush.Opacity = 0.28;
            bool active = Charging && !Full && Level >= 0 && AnimateCharge;
            double phase = _sweep.Phase(active);
            double drawnLevel = _sweep.Follow(0, Full ? 100 : Level, phase >= 0, 1050);
            var fill = AppearanceColors.BatteryState(
                BatteryBrush ?? DefaultFill, Level, LowBatteryThreshold, Charging, Full);
            BatteryRingDrawing.Draw(dc, center, radius, drawnLevel, trackBrush, fill, ContinuousRing);
            ChargeSweep.Absorb(dc, center, radius, phase, fill);
        }
    }
}
