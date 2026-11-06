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

        // Match the screen-edge40 DIP ring and16 DIP number; permit thickness overhang.
        public static Rect CalculateBadges(Rect taskbar,uint edge,int count,bool showNumber,double offset,double dpi)
        {
            bool vertical=edge is 0 or 2;
            double itemHeight=showNumber?56:40;
            double scale=double.IsFinite(dpi)?Math.Max(.1,dpi):1;
            double available=vertical?taskbar.Height:taskbar.Width;
            scale=Math.Min(scale,available/Math.Max(1,count*(vertical?itemHeight:40)));
            double width=(vertical?40:count*40)*scale;
            double height=(vertical?count*itemHeight:itemHeight)*scale;
            double percent=double.IsFinite(offset)?Math.Clamp(offset,0,100):0;
            double inset=Math.Max(0,available-(vertical?height:width))*percent/100;
            return vertical?new Rect(edge==0?taskbar.Left:taskbar.Right-width,taskbar.Bottom-inset-height,width,height):
                new Rect(taskbar.Left+inset,edge==1?taskbar.Top:taskbar.Bottom-height,width,height);
        }

        // taskbar rect and content size are physical pixels; the offset is a
        // percentage (0-100) of the remaining taskbar length. Percent scales with
        // 2K/4K and DPI automatically, so no extra dpi multiplication here.
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
