using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MouseDock.Controls
{
    public sealed class ChargingGlyph : FrameworkElement
    {
        public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
            nameof(Active), typeof(bool), typeof(ChargingGlyph), new PropertyMetadata(false, UpdateMark));
        public static readonly DependencyProperty FullProperty = DependencyProperty.Register(
            nameof(Full), typeof(bool), typeof(ChargingGlyph), new PropertyMetadata(false, UpdateMark));

        public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
        public bool Full { get => (bool)GetValue(FullProperty); set => SetValue(FullProperty, value); }

        private readonly ChargeSweep sweep;

        public ChargingGlyph()
        {
            sweep = new ChargeSweep(this);
            Visibility = Visibility.Collapsed;
            IsVisibleChanged += (_, _) => { if (!IsVisible) BeginAnimation(OpacityProperty, null); };
        }

        private static void UpdateMark(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var glyph = (ChargingGlyph)target;
            glyph.Visibility = glyph.Full || glyph.Active ? Visibility.Visible : Visibility.Collapsed;
            glyph.ToolTip = glyph.Full ? "已充满" : "充电中";
            if (e.Property == FullProperty)
            {
                glyph.BeginAnimation(OpacityProperty, null);
                if (glyph.Full && glyph.IsLoaded && glyph.IsVisible && SystemParameters.ClientAreaAnimation)
                    glyph.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(450))
                    { FillBehavior = FillBehavior.Stop });
            }
            glyph.InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if ((!Active && !Full) || ActualWidth <= 0 || ActualHeight <= 0) return;
            if (!Full)
            {
                ChargeSweep.Pulse(dc, new Point(ActualWidth / 2, ActualHeight / 2),
                    Math.Min(ActualWidth, ActualHeight) * .22, sweep.Phase(Active));
                return;
            }
            sweep.Phase(false);
            var text = new FormattedText("✓", CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(Full ? "Segoe UI Symbol" : "Segoe MDL2 Assets"),
                Math.Min(ActualWidth, ActualHeight) * .82, Brushes.Aquamarine, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
        }
    }
}