using System;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Controls;

internal static class BatteryRingDrawing
{
    private static readonly Geometry Plug = Geometry.Parse(
        "M -5,-8 Q -6,-8 -6,-7 L -6,-3 L -7,-3 Q -9,-3 -9,-1 L -9,2 Q -9,6 -4,10 L -4,13 Q -4,16 0,16 Q 4,16 4,13 L 4,10 Q 9,6 9,2 L 9,-1 Q 9,-3 7,-3 L 6,-3 L 6,-7 Q 6,-8 5,-8 Q 3,-8 3,-7 L 3,-3 L -3,-3 L -3,-7 Q -3,-8 -5,-8 Z");

    internal static void Draw(DrawingContext dc, Point center, double radius, double level,
        Brush track, Brush fill, Brush plug, Brush bottomFill = null, bool headset = false, int? volume = null)
    {
        if (radius <= 0) return;
        if (level < 0)
        {
            track = track.CloneCurrentValue();
            track.Opacity *= .45;
        }
        var trackPen = RoundedPen(track, radius * .18);
        var fillPen = RoundedPen(fill, trackPen.Thickness);
        var bottomPen = bottomFill == null ? fillPen : RoundedPen(bottomFill, trackPen.Thickness);
        double progress = Math.Clamp(level, 0, 100) * (headset ? 1.84 : 2.44);
        DrawSegment(28, 92, fillPen);
        if (!headset) DrawSegment(150, 60, bottomPen);
        DrawSegment(240, 92, fillPen);
        if (headset)
        {
            var bar = VolumeBounds(center, radius);
            double thickness = Math.Max(.8, bar.Height * .36);
            var rail = new Rect(bar.X, bar.Y + (bar.Height - thickness) / 2, bar.Width, thickness);
            dc.DrawRoundedRectangle(track, null, rail, thickness / 2, thickness / 2);
            if (volume > 0)
            {
                rail.Width *= volume.Value / 100d;
                dc.DrawRoundedRectangle(bottomFill ?? fill, null, rail, thickness / 2, thickness / 2);
            }
        }

        if (plug != null)
        {
            dc.PushTransform(new TranslateTransform(center.X, center.Y - radius * 1.04));
            dc.PushTransform(new ScaleTransform(radius / 46, radius / 46));
            dc.DrawGeometry(plug, null, Plug);
            dc.Pop();
            dc.Pop();
        }

        void DrawSegment(double start, double sweep, Pen pen)
        {
            DrawArc(dc, center, radius, start, sweep, trackPen);
            double covered = Math.Min(progress, sweep);
            if (covered > 0) DrawArc(dc, center, radius, start, covered, pen);
            progress -= covered;
        }
    }

    internal static Rect VolumeBounds(Point center, double radius) =>
        new(center.X - radius * .55, center.Y + radius * .63, radius * 1.1, radius * .47);

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
