using System;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Controls
{
    /// <summary>视频里的电量条：圆角细轨 + 渐变填充 + 末端圆点。</summary>
    public class MeterBar : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

        private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
            "DisplayValue", typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not MeterBar bar) return;
            double target = Math.Clamp((double)e.NewValue, 0, 1);
            if (!bar.IsLoaded)
            {
                bar.SetValue(DisplayValueProperty, target);
                return;
            }
            var animation = new System.Windows.Media.Animation.DoubleAnimation(
                target, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            bar.BeginAnimation(DisplayValueProperty, animation);
        }

        private double DisplayValue => (double)GetValue(DisplayValueProperty);

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(MeterBar),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent), typeof(Color), typeof(MeterBar),
            new FrameworkPropertyMetadata(Colors.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty Accent2Property = DependencyProperty.Register(
            nameof(Accent2), typeof(Color), typeof(MeterBar),
            new FrameworkPropertyMetadata(Colors.HotPink, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BarHeightProperty = DependencyProperty.Register(
            nameof(BarHeight), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowDotProperty = DependencyProperty.Register(
            nameof(ShowDot), typeof(bool), typeof(MeterBar),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
        public Color Accent2 { get => (Color)GetValue(Accent2Property); set => SetValue(Accent2Property, value); }
        public double BarHeight { get => (double)GetValue(BarHeightProperty); set => SetValue(BarHeightProperty, value); }
        public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            if (w <= 1) return;

            double h = Math.Max(2, BarHeight);
            double r = h / 2;
            double y = (ActualHeight - h) / 2;

            var track = new Rect(0, y, w, h);
            dc.DrawRoundedRectangle(TrackBrush ?? Brushes.Transparent, null, track, r, r);

            double fraction = Math.Clamp(DisplayValue, 0, 1);
            double fw = Math.Max(0, fraction * w);
            if (fw < 1) return;

            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            fill.GradientStops.Add(new GradientStop(Accent, 0));
            fill.GradientStops.Add(new GradientStop(Accent2, 1));
            fill.Freeze();

            var fillRect = new Rect(0, y, Math.Min(fw, w), h);
            dc.DrawRoundedRectangle(fill, null, fillRect, r, r);

            if (ShowDot && fw > h + 2)
            {
                var glow = new SolidColorBrush(Accent2) { Opacity = 0.9 };
                glow.Freeze();
                dc.DrawEllipse(glow, null, new Point(fillRect.Right - r, y + r), r * 0.62, r * 0.62);
            }
        }
    }

    /// <summary>按设备类型画的小线性图标（鼠标 / 耳机 / 键盘 / 手柄 / 其它）。</summary>
    public class DeviceGlyph : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(Models.DeviceKind), typeof(DeviceGlyph),
            new FrameworkPropertyMetadata(Models.DeviceKind.Mouse, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(DeviceGlyph),
            new FrameworkPropertyMetadata(Brushes.Gainsboro, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(DeviceGlyph),
            new FrameworkPropertyMetadata(1.4, FrameworkPropertyMetadataOptions.AffectsRender));

        public Models.DeviceKind Kind { get => (Models.DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

        private static readonly Geometry MouseGeo = Geometry.Parse(
            "M8,2.5 H16 A5.5,5.5 0 0 1 21.5,8 V16 A5.5,5.5 0 0 1 16,21.5 H8 A5.5,5.5 0 0 1 2.5,16 V8 A5.5,5.5 0 0 1 8,2.5 Z M12,3 V8.5");

        private static readonly Geometry HeadsetGeo = Geometry.Parse(
            "M3.5,14.5 V11.5 A8.5,8.5 0 0 1 20.5,11.5 V14.5 M2.5,14.5 H6.5 V20.5 H2.5 Z M17.5,14.5 H21.5 V20.5 H17.5 Z");

        private static readonly Geometry KeyboardGeo = Geometry.Parse(
            "M2.5,8.5 H21.5 V17.5 H2.5 Z M6,12 H6.01 M9.5,12 H9.51 M13,12 H13.01 M16.5,12 H16.51 M3,15.2 H21");

        private static readonly Geometry ControllerGeo = Geometry.Parse(
            "M7,9 H17 A4.5,4.5 0 0 1 21,13.5 V15 A3,3 0 0 1 15.5,16.4 L14.6,15 H9.4 L8.5,16.4 A3,3 0 0 1 3,15 V13.5 A4.5,4.5 0 0 1 7,9 Z M7.5,11.5 V13.5 M6.5,12.5 H8.5 M16.5,12.2 H16.51");

        private static readonly Geometry OtherGeo = Geometry.Parse(
            "M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,12 H12.01");

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            var geo = Kind switch
            {
                Models.DeviceKind.Mouse => MouseGeo,
                Models.DeviceKind.Headset => HeadsetGeo,
                Models.DeviceKind.Keyboard => KeyboardGeo,
                Models.DeviceKind.Controller => ControllerGeo,
                _ => OtherGeo
            };

            var pen = new Pen(GlyphBrush, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            double scale = Math.Min(w, h) / 24.0;
            dc.PushTransform(new TranslateTransform((w - 24 * scale) / 2, (h - 24 * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(null, pen, geo);
            dc.Pop();
            dc.Pop();
        }
    }
}
