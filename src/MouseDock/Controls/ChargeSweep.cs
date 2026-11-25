using MouseDock.Services;
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
        if (!active || !SystemParameters.ClientAreaAnimation || !_owner.IsLoaded || !_owner.IsVisible)
        {
            _timer.Stop();
            return -1;
        }
        if (!_timer.IsEnabled) _timer.Start();
        return (Environment.TickCount64 % 1800) / 1800d;
    }

    private readonly System.Collections.Generic.Dictionary<int, (double From, double Target, long At)> _levels = new();

    internal double Follow(int slot, double target, bool smooth, int duration = 300)
    {
        long now = Environment.TickCount64;
        if (!smooth || !_owner.IsLoaded || !SystemParameters.ClientAreaAnimation ||
            !_levels.TryGetValue(slot, out var old) || old.Target < 0 || target < 0)
        {
            _levels[slot] = (target, target, now);
            return target;
        }
        double current = Interpolate(old.From, old.Target, now - old.At, duration);
        if (old.Target != target) _levels[slot] = (current, target, now);
        return current;
    }

    internal void Reset(int slot) => _levels.Remove(slot);

    internal static double Interpolate(double from, double target, long elapsed, int duration = 300) =>
        from + (target - from) * Math.Clamp(elapsed / (double)duration, 0, 1);

    private static readonly Pen LineGlow = ChargingLinePen(Color.FromArgb(74, 130, 240, 250), 1.35);
    private static readonly Pen LineCrest = ChargingLinePen(Color.FromArgb(210, 228, 253, 255), .65);

    private static readonly Pen BatteryWaveGlow = ChargingLinePen(Color.FromArgb(80, 120, 255, 230), 1.8);
    private static readonly Pen BatteryWaveCrest = ChargingLinePen(Color.FromArgb(185, 220, 255, 247), .75);

    private static Pen ChargingLinePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    internal static void HorizontalLine(DrawingContext dc, Rect track, double fraction, double radius,
        Brush brush, double phase)
    {
        if (fraction <= 0 || track.Width <= 0 || track.Height <= 0) return;
        double width = track.Width * Math.Min(1, fraction);
        if (phase < 0)
        {
            dc.DrawRoundedRectangle(brush, null, new Rect(track.X, track.Y, width, track.Height), radius, radius);
            return;
        }

        double amplitude = Math.Min(.8, Math.Min(track.Height * .21, width * .10));
        double wavelength = Math.Clamp(track.Width / 7, 10, 32);
        int segments = Math.Max(2, (int)Math.Ceiling(width / wavelength * 14));
        Point At(int i)
        {
            double x = width * i / segments;
            return new Point(track.X + x, track.Top + amplitude * Math.Sin((x / wavelength - phase) * 2 * Math.PI));
        }

        var body = new StreamGeometry();
        var edge = new StreamGeometry();
        using (var fill = body.Open())
        using (var crest = edge.Open())
        {
            fill.BeginFigure(At(0), true, true);
            crest.BeginFigure(At(0), false, false);
            for (int i = 1; i <= segments; i++)
            {
                Point point = At(i);
                fill.LineTo(point, true, false);
                crest.LineTo(point, true, false);
            }
            fill.LineTo(new Point(track.X + width, track.Bottom), true, false);
            fill.LineTo(track.BottomLeft, true, false);
        }
        dc.PushClip(new RectangleGeometry(new Rect(track.X, track.Top - amplitude - 1, width, track.Height + amplitude + 1)));
        dc.DrawGeometry(brush, null, body);
        dc.DrawGeometry(null, LineGlow, edge);
        dc.DrawGeometry(null, LineCrest, edge);
        dc.Pop();
    }

    internal static void Fill(DrawingContext dc, Rect track, double fraction, double radius,
        Brush brush, double phase, bool vertical = false)
    {
        if (fraction <= 0 || track.Width <= 0 || track.Height <= 0) return;
        fraction = Math.Min(1, fraction);
        var fill = vertical
            ? new Rect(track.X, track.Bottom - track.Height * fraction, track.Width, track.Height * fraction)
            : new Rect(track.X, track.Y, track.Width * fraction, track.Height);
        double span = vertical ? track.Height : track.Width;
        double amplitude = phase >= 0
            ? Math.Min(1.2, span * Math.Min(fraction, fraction == 1 ? .06 : 1 - fraction) * .28) : 0;
        if (amplitude <= 0)
        {
            dc.DrawRoundedRectangle(brush, null, fill, radius, radius);
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
        dc.DrawGeometry(null, BatteryWaveGlow, edge);
        dc.DrawGeometry(null, BatteryWaveCrest, edge);
        dc.Pop();
    }

    internal static void Absorb(DrawingContext dc, Point center, double radius, double phase, Brush fill)
    {
        if (phase < 0 || radius <= 0) return;
        var color = fill is SolidColorBrush solid ? solid.Color : Colors.Aquamarine;
        var light = new SolidColorBrush(Palette.Mix(color, Colors.White, .5));
        light.Freeze();
        for (int i = 0; i < 7; i++)
        {
            double progress = (phase + i / 7d) % 1;
            double angle = i * 2.39996 + .12 * (1 - progress);
            double distance = radius + radius * .40 * (1 - progress);
            var point = center + new Vector(Math.Cos(angle), Math.Sin(angle)) * distance;
            double size = Math.Max(.65, radius * .10) * (.55 + .45 * progress);
            dc.PushOpacity(Math.Sin(Math.PI * progress) * .94);
            dc.DrawEllipse(light, null, point, size, size);
            dc.Pop();
        }
    }
}