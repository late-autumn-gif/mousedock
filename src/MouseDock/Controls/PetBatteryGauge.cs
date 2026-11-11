using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Controls;

internal sealed class PetBatteryGauge : FrameworkElement
{
    private readonly ChargeSweep _sweep;
    private DeviceInfo _device;
    private string _style;
    private bool _wave;
    private bool _motion;
    private Brush _accent;
    private Brush _chargingAccent;
    private Color _halo;
    private int _lowThreshold;
    private int _level = -1;
    private long _transitionUntil;

    internal PetBatteryGauge() => _sweep = new ChargeSweep(this);

    internal void Update(DeviceInfo device, string style, bool wave, bool motion, Brush accent, Brush chargingAccent, Color halo, int lowThreshold)
    {
        int level = device?.Level ?? -1;
        if (_device?.Id != device?.Id)
        {
            _sweep.Reset(0);
            _transitionUntil = 0;
        }
        else if (_level != level && level >= 0)
            _transitionUntil = Environment.TickCount64 + 340;
        _device = device;
        _level = level;
        _style = style;
        _wave = wave;
        _motion = motion;
        _accent = accent;
        _chargingAccent = chargingAccent;
        _halo = halo;
        _lowThreshold = lowThreshold;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        bool charging = _device?.IsChargingActive == true;
        bool full = _device?.IsFullActive == true;
        bool stale = _device?.IsStale == true || _device?.Connected == false;
        long completed = _sweep.Completion(0, charging, full);
        bool showCheck = completed < ChargeSweep.CompletionDuration;
        bool halo = _motion && (charging || completed < ChargeSweep.FadeDuration);
        double phase = _sweep.Phase(_motion && !stale && (halo || Environment.TickCount64 < _transitionUntil), showCheck);
        double level = _sweep.Follow(0, full ? 100 : _level, phase >= 0);
        var fill = full ? Brushes.Aquamarine : charging ? _chargingAccent :
            _level >= 0 && _level <= _lowThreshold && !stale ? Brushes.Orange : _accent;
        var track = new SolidColorBrush(Color.FromRgb(54, 69, 88));
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double chargingPhase = charging ? phase : -1;
        if (stale) dc.PushOpacity(.58);
        if (_style == "vertical" || _style == "battery")
        {
            bool vertical = _style == "vertical";
            var bounds = vertical ? new Rect(center.X - 10, 8, 20, 42) : new Rect(12, 14, ActualWidth - 25, 23);
            dc.DrawRoundedRectangle(track, null, bounds, 5, 5);
            if (!vertical) dc.DrawRoundedRectangle(track, null, new Rect(bounds.Right, 20, 4, 11), 1, 1);
            ChargeSweep.Fill(dc, bounds, level / 100, 5, fill, chargingPhase, vertical, _wave);
            center.Y = ActualHeight - 14;
        }
        else
        {
            const double radius = 27;
            const double width = 4;
            dc.DrawEllipse(null, new Pen(track, width), center, radius, radius);
            var pen = new Pen(fill, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (level >= 100)
                dc.DrawEllipse(null, pen, center, radius, radius);
            else if (level > 0)
            {
                double angle = level / 100 * 2 * Math.PI;
                var arc = new StreamGeometry();
                using (var path = arc.Open())
                {
                    path.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                    path.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                        new Size(radius, radius), 0, level > 50, SweepDirection.Clockwise, true, false);
                }
                dc.DrawGeometry(null, pen, arc);
            }
            if (halo) ChargeSweep.RingEffect(dc, center, radius, level, width, phase, completed, _wave, _halo);
        }

        string label = showCheck ? "✓" : full ? "100%" : _level >= 0 ? $"{_level}%" : "—";
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 14, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        if (stale) dc.Pop();
    }
}
