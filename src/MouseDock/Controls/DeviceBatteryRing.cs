using System;
using System.Globalization;
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
            nameof(Full), typeof(bool), typeof(DeviceBatteryRing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public bool Full { get => (bool)GetValue(FullProperty); set => SetValue(FullProperty, value); }
        public static readonly DependencyProperty ShowChargingMarkProperty = DependencyProperty.Register(
            nameof(ShowChargingMark), typeof(bool), typeof(DeviceBatteryRing), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public DeviceKind Kind { get => (DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
        public int LowBatteryThreshold { get => (int)GetValue(LowBatteryThresholdProperty); set => SetValue(LowBatteryThresholdProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
        public Brush BatteryBrush { get => (Brush)GetValue(BatteryBrushProperty); set => SetValue(BatteryBrushProperty, value); }
        public bool ShowGlyph { get => (bool)GetValue(ShowGlyphProperty); set => SetValue(ShowGlyphProperty, value); }
        public bool ShowChargingMark { get => (bool)GetValue(ShowChargingMarkProperty); set => SetValue(ShowChargingMarkProperty, value); }

        private static readonly DependencyPropertyKey CompletionVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CompletionVisible), typeof(bool), typeof(DeviceBatteryRing), new PropertyMetadata(false));
        public static readonly DependencyProperty CompletionVisibleProperty = CompletionVisiblePropertyKey.DependencyProperty;
        public bool CompletionVisible => (bool)GetValue(CompletionVisibleProperty);
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
            var glyphStyle = new Style(typeof(DeviceGlyph));
            var completing = new DataTrigger { Binding = new Binding(nameof(CompletionVisible)) { Source = this }, Value = true };
            completing.Setters.Add(new Setter(OpacityProperty, 0d));
            glyphStyle.Triggers.Add(completing);
            glyph.Style = glyphStyle;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 7);
            var trackBrush = GlyphBrush.Clone();
            trackBrush.Opacity = 0.28;
            long completed = _sweep.Completion(0, Charging, Full);
            bool showCheck = ShowChargingMark && completed < ChargeSweep.CompletionDuration;
            if (CompletionVisible != showCheck) SetValue(CompletionVisiblePropertyKey, showCheck);
            double drawnLevel = _sweep.Follow(0, Full ? 100 : Level, AnimateCharge);
            _sweep.Phase(_sweep.IsFollowing(0), showCheck);
            var color = !Full && Level <= LowBatteryThreshold ? Color.FromRgb(248, 93, 104)
                : Color.FromRgb(52, 211, 153);
            var fill = BatteryBrush ?? new SolidColorBrush(color);
            var bottom = BatteryBrush ?? (!Full && Level <= LowBatteryThreshold ? fill : Brushes.DodgerBlue);
            BatteryRingDrawing.Draw(dc, center, radius, drawnLevel, trackBrush, fill,
                ShowChargingMark && Charging ? GlyphBrush : null, bottom);
            if (showCheck)
            {
                var check = new FormattedText("✓", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Symbol"), 16, GlyphBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(check, new Point(center.X - check.Width / 2, center.Y - check.Height / 2));
            }
        }

    }
}
