using System;
using System.Windows;

namespace MouseDock.Services
{
    public static class TaskbarPlacement
    {
        public static bool IsFullscreen(Rect window, Rect monitor, bool maximized, string className,
            string processName = null) =>
            !maximized && className != "Progman" && className != "WorkerW" &&
            !IsShellSurface(className, processName) && window.Contains(monitor);

        private static bool IsShellSurface(string className, string processName) =>
            string.Equals(processName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "SearchHost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "SearchApp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) &&
                (className == "Windows.UI.Core.CoreWindow" || className == "XamlExplorerHostIslandWindow"));

        // Both taskbar styles keep their digits inside a single 40-DIP cell.
        public static Rect CalculateBadges(Rect taskbar,uint edge,int count,bool showNumber,double offset,double dpi)
        {
            return Calculate(taskbar, edge, new Size(Math.Max(1, count) * 40, 40), offset,
                double.IsFinite(dpi) ? dpi : 1);
        }

        // Taskbar bounds are physical pixels; content is horizontal length/thickness
        // in DIPs. Offset is a percentage of the remaining taskbar length.
        public static Rect Calculate(Rect taskbar, uint edge, Size content, double offsetPercent, double dpiScale)
        {
            bool vertical = edge == 0 || edge == 2;
            double thickness = vertical ? taskbar.Width : taskbar.Height;
            double length = vertical ? taskbar.Height : taskbar.Width;
            double scale = Math.Min(Math.Max(0.1, dpiScale),
                Math.Min(thickness / Math.Max(1, content.Height), length / Math.Max(1, content.Width)));
            double width = content.Width * scale;
            double height = content.Height * scale;
            double percent = double.IsFinite(offsetPercent) ? Math.Clamp(offsetPercent, 0, 100) : 0;
            double inset = Math.Max(0, length - width) * percent / 100.0;
            inset = Math.Min(inset, Math.Max(0, length - width));
            return vertical
                ? new Rect(taskbar.Left + (thickness - height) / 2, taskbar.Bottom - inset - width, height, width)
                : new Rect(taskbar.Left + inset, taskbar.Top + (thickness - height) / 2, width, height);
        }
    }
}
