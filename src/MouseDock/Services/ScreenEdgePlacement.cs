using System;
using System.Windows;

namespace MouseDock.Services
{
    public static class ScreenEdgePlacement
    {
        public static double NormalizeSizePercent(double value) =>
            double.IsFinite(value) ? Math.Round(Math.Clamp(value, 50, 150)) : 100;

        public static Rect Calculate(Rect workArea, Size content, double position, double inset, double dpiScale)
        {
            double dpi = double.IsFinite(dpiScale) ? Math.Max(0.1, dpiScale) : 1;
            double scale = Math.Min(dpi, Math.Min(workArea.Width / Math.Max(1, content.Width),
                workArea.Height / Math.Max(1, content.Height)));
            double width = content.Width * scale;
            double height = content.Height * scale;
            double distance = double.IsFinite(inset) ? Math.Max(0, inset) * dpi : 12 * dpi;
            distance = Math.Min(distance, Math.Max(0, workArea.Width - width));
            double fraction = double.IsFinite(position) ? Math.Clamp(position, 0, 1) : 0.5;
            return new Rect(workArea.Right - distance - width,
                workArea.Top + (workArea.Height - height) * fraction, width, height);
        }
    }
}
