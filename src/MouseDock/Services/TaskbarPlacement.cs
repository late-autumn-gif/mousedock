using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MouseDock.Services
{
    public static class TaskbarPlacement
    {
        public static bool TryFollow(Rect taskbar, uint edge, Size content, double dpiScale,
            Rect icons, IEnumerable<Rect> occupied, out Rect placement, double extraSpace = 0)
        {
            placement = Rect.Empty;
            if (!Valid(taskbar) || !Valid(icons) || !taskbar.IntersectsWith(icons) ||
                !double.IsFinite(content.Width) || !double.IsFinite(content.Height) ||
                content.Width <= 0 || content.Height <= 0 || !double.IsFinite(dpiScale) || dpiScale <= 0)
                return false;
            bool vertical = edge == 0 || edge == 2;
            double start = vertical ? taskbar.Top : taskbar.Left;
            double end = vertical ? taskbar.Bottom : taskbar.Right;
            double thickness = vertical ? taskbar.Width : taskbar.Height;
            double scale = Math.Min(dpiScale, thickness / content.Height);
            double length = content.Width * scale, height = content.Height * scale;
            double gap = 8 * dpiScale;
            double reserve = double.IsFinite(extraSpace) ? Math.Max(0, extraSpace) * dpiScale : 0;
            if (length + gap * 2 + reserve > end - start) return false;
            var blocked = new List<(double Start, double End)>();
            foreach (var rect in (occupied ?? Array.Empty<Rect>()).Append(icons))
            {
                if (rect.IsEmpty) continue;
                if (!Valid(rect)) return false;
                if (!taskbar.IntersectsWith(rect)) continue;
                blocked.Add((Math.Max(start, (vertical ? rect.Top : rect.Left) - gap),
                    Math.Min(end, (vertical ? rect.Bottom : rect.Right) + gap)));
            }
            var free = new List<(double Start, double End)>();
            double cursor = start + gap;
            foreach (var block in blocked.OrderBy(b => b.Start))
            {
                if (block.Start > cursor) free.Add((cursor, Math.Min(block.Start, end - gap)));
                cursor = Math.Max(cursor, block.End);
            }
            if (cursor < end - gap) free.Add((cursor, end - gap));
            double iconStart = vertical ? icons.Top : icons.Left;
            double iconEnd = vertical ? icons.Bottom : icons.Right;
            double position = double.NaN;
            // Prefer following the final app, then search back toward the start button.
            foreach (var slot in free)
            {
                double candidate = Math.Max(slot.Start, iconEnd + gap);
                if (candidate + length + reserve <= slot.End) { position = candidate; break; }
            }
            if (double.IsNaN(position))
                foreach (var slot in free.AsEnumerable().Reverse())
                {
                    double candidate = Math.Min(slot.End, iconStart - gap) - length;
                    if (candidate - reserve >= slot.Start) { position = candidate; break; }
                }
            if (double.IsNaN(position)) return false;
            placement = vertical
                ? new Rect(taskbar.Left + (thickness - height) / 2, position, height, length)
                : new Rect(position, taskbar.Top + (thickness - height) / 2, length, height);
            return true;
        }

        private static bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
            double.IsFinite(rect.Left) && double.IsFinite(rect.Top) &&
            double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);

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
