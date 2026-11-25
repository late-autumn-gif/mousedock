using System;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Controls;

internal static class BatteryRingDrawing
{
    // Leave room between the ring and the widest glyphs, including the headset earcups.
    internal const double GlyphRadiusRatio = 1.6;
    internal const double HeadsetGlyphRadiusRatio = 1.3;

    internal static void Draw(DrawingContext dc, Point center, double radius, double level,
        Brush track, Brush fill)
    {
        if (radius <= 0) return;
        if (level < 0)
        {
            track = track.CloneCurrentValue();
            track.Opacity *= .45;
        }
        var trackPen = RoundedPen(track, radius * .18);
        var fillPen = RoundedPen(fill, trackPen.Thickness);
        double progress = Math.Clamp(level, 0, 100) * 2.44;
        DrawSegment(28, 92);
        DrawSegment(150, 60);
        DrawSegment(240, 92);

        void DrawSegment(double start, double sweep)
        {
            DrawArc(dc, center, radius, start, sweep, trackPen);
            double covered = Math.Min(progress, sweep);
            if (covered > 0) DrawArc(dc, center, radius, start, covered, fillPen);
            progress -= covered;
        }
    }

    private static Pen RoundedPen(Brush brush, double thickness) => new(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round
    };

    private static void DrawArc(DrawingContext dc, Point center, double radius, double start, double sweep, Pen pen)
    {
        Point PointAt(double angle)
        {
            double radians = angle * Math.PI / 180;
            return new Point(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(start), false, false);
            context.ArcTo(PointAt(start + sweep), new Size(radius, radius), 0, false,
                SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }
}
