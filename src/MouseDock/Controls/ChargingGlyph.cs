using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MouseDock.Controls
{
    /// <summary>A three-cell charging cycle. Animation owns no timer and pauses while hidden/unloaded.</summary>
    public sealed class ChargingGlyph : FrameworkElement
    {
        public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
            nameof(Active), typeof(bool), typeof(ChargingGlyph), new PropertyMetadata(false, OnActiveChanged));
        private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
            "Phase", typeof(double), typeof(ChargingGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
        private bool _animating;
        internal bool IsAnimating => _animating;

        public ChargingGlyph()
        {
            Visibility = Visibility.Collapsed;
            ToolTip = "充电中";
            Loaded += (_, __) => UpdateAnimation();
            Unloaded += (_, __) => StopAnimation();
            IsVisibleChanged += (_, __) => UpdateAnimation();
        }
        private static void OnActiveChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var glyph = (ChargingGlyph)target;
            glyph.Visibility = glyph.Active ? Visibility.Visible : Visibility.Collapsed;
            glyph.UpdateAnimation();
        }
        private void UpdateAnimation()
        {
            bool run = Active && IsLoaded && IsVisible;
            if (!run) { StopAnimation(); return; }
            if (_animating) return;
            _animating = true;
            BeginAnimation(PhaseProperty, new DoubleAnimation(0, 3, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        private void StopAnimation()
        {
            BeginAnimation(PhaseProperty, null);
            _animating = false;
        }
        protected override void OnRender(DrawingContext dc)
        {
            if (!Active || ActualWidth <= 0 || ActualHeight <= 0) return;
            double scale = Math.Min(ActualWidth / 24, ActualHeight / 20);
            dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 20 * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            var mint = new SolidColorBrush(Color.FromRgb(0x7F, 0xE7, 0xC4));
            dc.DrawRoundedRectangle(null, new Pen(mint, 1.4), new Rect(1, 4, 19, 12), 2, 2);
            dc.DrawRoundedRectangle(mint, null, new Rect(21, 8, 2, 4), 0.6, 0.6);
            int filled = Math.Clamp((int)(double)GetValue(PhaseProperty) + 1, 1, 3);
            for (int i = 0; i < 3; i++)
            {
                var cell = mint.Clone(); cell.Opacity = i < filled ? 1 : 0.16;
                dc.DrawRoundedRectangle(cell, null, new Rect(4 + i * 5, 7, 3, 6), 0.6, 0.6);
            }
            dc.Pop(); dc.Pop();
        }
    }
}
