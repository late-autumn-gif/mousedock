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

    internal static void Bar(DrawingContext dc, Rect fill, double radius, double phase)
    {
        if (phase < 0 || fill.Width <= 0 || fill.Height <= 0) return;
        double width = Math.Max(3, fill.Width * .28);
        var highlight = new Rect(fill.X + (fill.Width + width) * phase - width, fill.Y, width, fill.Height);
        var brush = new LinearGradientBrush(Colors.Transparent, Colors.Transparent, 0);
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(145, 255, 255, 255), .5));
        dc.PushClip(new RectangleGeometry(fill, radius, radius));
        dc.DrawRectangle(brush, null, highlight);
        dc.Pop();
    }

    internal static void Arc(DrawingContext dc, Point center, double radius, int level, double thickness, double phase)
    {
        if (phase < 0 || level <= 0 || radius <= 0) return;
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