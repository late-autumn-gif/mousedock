using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MouseDock.Controls;

internal sealed class ChargeSweep
{
    private readonly FrameworkElement _owner;
    private readonly DispatcherTimer _timer;

    internal ChargeSweep(FrameworkElement owner)
    {
        _owner = owner;
        _timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += (_, _) => _owner.InvalidateVisual();
        _owner.Loaded += (_, _) => _owner.InvalidateVisual();
        _owner.Unloaded += (_, _) => _timer.Stop();
        _owner.IsVisibleChanged += (_, _) =>
        {
            if (!_owner.IsVisible) _timer.Stop();
            else _owner.InvalidateVisual();
        };
    }

    internal bool IsRunning => _timer.IsEnabled;

    internal double Phase(bool active)
    {
        if (!active || !_owner.IsLoaded || !_owner.IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            _timer.Stop();
            return -1;
        }
        if (!_timer.IsEnabled) _timer.Start();
        return (Environment.TickCount64 % 1800) / 1800d;
    }

    private readonly System.Collections.Generic.Dictionary<int, (double From, double Target, long At)> _levels = new();

    internal double Follow(int slot, double target, bool smooth)
    {
        long now = Environment.TickCount64;
        if (!smooth || !_owner.IsLoaded || !SystemParameters.ClientAreaAnimation ||
            !_levels.TryGetValue(slot, out var old) || old.Target < 0 || target < 0)
        {
            _levels[slot] = (target, target, now);
            return target;
        }
        double current = Interpolate(old.From, old.Target, now - old.At);
        if (old.Target != target) _levels[slot] = (current, target, now);
        return current;
    }

    internal void Reset(int slot) => _levels.Remove(slot);

    internal static double Interpolate(double from, double target, long elapsed) =>
        from + (target - from) * Math.Clamp(elapsed / 300d, 0, 1);

    internal static bool Wave => string.Equals(App.Settings?.ChargeEffect, "wave", StringComparison.Ordinal);

    internal static void Fill(DrawingContext dc, Rect track, double fraction, double radius,
        Brush brush, double phase, bool vertical = false, bool wave = true)
    {
        if (fraction <= 0 || track.Width <= 0 || track.Height <= 0) return;
        fraction = Math.Min(1, fraction);
        var fill = vertical
            ? new Rect(track.X, track.Bottom - track.Height * fraction, track.Width, track.Height * fraction)
            : new Rect(track.X, track.Y, track.Width * fraction, track.Height);
        double span = vertical ? track.Height : track.Width;
        double amplitude = phase >= 0 && wave
            ? Math.Min(3, span * Math.Min(fraction, 1 - fraction) * .45) : 0;
        if (amplitude <= 0)
        {
            dc.DrawRoundedRectangle(brush, null, fill, radius, radius);
            if (phase >= 0 && !wave)
            {
                var shine = new LinearGradientBrush(Colors.Transparent, Colors.Transparent, vertical ? 90 : 0);
                shine.GradientStops.Insert(1, new GradientStop(Color.FromArgb(155, 255, 255, 255), .5));
                dc.PushClip(new RectangleGeometry(fill, radius, radius));
                var highlight = vertical
                    ? new Rect(fill.X, fill.Bottom - (fill.Height + 9) * phase, fill.Width, 9)
                    : new Rect(fill.X + (fill.Width + 9) * phase - 9, fill.Y, 9, fill.Height);
                dc.DrawRectangle(shine, null, highlight);
                dc.Pop();
            }
            return;
        }
        var geometry = new StreamGeometry();
        var edge = new StreamGeometry();
        Point At(int i)
        {
            double part = i / 16d;
            double offset = amplitude * Math.Sin((part + phase) * 2 * Math.PI);
            return vertical ? new Point(track.X + part * track.Width, fill.Top + offset)
                : new Point(fill.Right + offset, track.Y + part * track.Height);
        }
        using (var body = geometry.Open())
        using (var line = edge.Open())
        {
            body.BeginFigure(vertical ? track.BottomLeft : track.TopLeft, true, true);
            line.BeginFigure(At(0), false, false);
            for (int i = 0; i <= 16; i++)
            {
                body.LineTo(At(i), true, false);
                if (i > 0) line.LineTo(At(i), true, false);
            }
            body.LineTo(vertical ? track.BottomRight : track.BottomLeft, true, false);
        }
        dc.PushClip(new RectangleGeometry(track, radius, radius));
        dc.DrawGeometry(brush, null, geometry);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(95, 120, 255, 230)), 3), edge);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(185, 220, 255, 247)), 1), edge);
        dc.Pop();
    }

    internal static void Pulse(DrawingContext dc, Point center, double radius, double phase)
    {
        if (phase < 0) return;
        double pulse = .5 + .5 * Math.Sin(Environment.TickCount64 / 2400d * 2 * Math.PI);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(100 + 90 * pulse), 88, 230, 210)),
            new Pen(App.Theme.IsLight ? Brushes.Black : Brushes.WhiteSmoke, .7), center, radius, radius);
    }

    internal static void Arc(DrawingContext dc, Point center, double radius, double level, double thickness, double phase, bool? wave = null, Color? haloColor = null)
    {
        if (phase < 0 || radius <= 0) return;
        if (wave ?? Wave)
        {
            var halo = haloColor ?? Color.FromRgb(100, 240, 220);
            var highlight = haloColor.HasValue ? Color.FromRgb(
                (byte)((halo.R + 255) / 2), (byte)((halo.G + 255) / 2), (byte)((halo.B + 255) / 2))
                : Color.FromRgb(170, 255, 238);
            double pulse = .5 + .5 * Math.Sin(Environment.TickCount64 / 2400d * 2 * Math.PI);
            double outer = radius + thickness * 1.25;
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(24 + 18 * pulse), halo.R, halo.G, halo.B)),
                thickness * (1.5 + pulse)), center, outer, outer);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(70, halo.R, halo.G, halo.B)), .8),
                center, outer, outer);
            double orbitHead = Environment.TickCount64 / 2000d * 2 * Math.PI;
            for (int i = 18; i >= 0; i--)
            {
                double angle = orbitHead - i * .045;
                var point = new Point(center.X + outer * Math.Sin(angle), center.Y - outer * Math.Cos(angle));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(170 * (19 - i) / 19), highlight.R, highlight.G, highlight.B)),
                    new Pen(new SolidColorBrush(Color.FromArgb((byte)(70 * (19 - i) / 19), 8, 28, 34)), .5), point, Math.Max(.6, thickness * .36), Math.Max(.6, thickness * .36));
            }
            return;
        }
        if (level <= 0) return;
        double extent = Math.Clamp(level, 0, 100) / 100d * 2 * Math.PI;
        double length = Math.Min(extent, .48);

        double head = (extent + length) * phase;
        double start = Math.Max(0, head - length), end = Math.Min(extent, head);
        if (end <= start) return;
        Point At(double angle) => new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            context.BeginFigure(At(start), false, false);
            context.ArcTo(At(end), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(155, 255, 255, 255)), thickness), path);
    }
}