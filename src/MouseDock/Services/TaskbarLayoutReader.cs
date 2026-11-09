using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace MouseDock.Services;

internal readonly record struct TaskbarElement(Rect Bounds, string ClassName, string Id,
    bool Button, bool Offscreen, int ProcessId);

internal sealed record TaskbarLayoutSnapshot(Rect Taskbar, uint Edge, Rect Icons, Rect[] Occupied,
    DateTime CapturedUtc)
{
    public bool Matches(Rect taskbar, uint edge, DateTime now) =>
        taskbar == Taskbar && edge == Edge && now >= CapturedUtc &&
        now - CapturedUtc <= TimeSpan.FromSeconds(3);
}

internal sealed class TaskbarLayoutReader : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private sealed record Reading(IntPtr Host, uint ProcessId, TaskbarLayoutSnapshot Layout);
    private readonly Dispatcher _dispatcher;
    private readonly Action _changed;
    private Reading _reading;
    private int _pending;
    private long _lastRequest;
    private volatile bool _disposed;

    public TaskbarLayoutReader(Dispatcher dispatcher, Action changed)
    {
        _dispatcher = dispatcher;
        _changed = changed;
    }

    public bool TryGet(Rect bounds, uint edge, out TaskbarLayoutSnapshot snapshot)
    {
        snapshot = null;
        var reading = Volatile.Read(ref _reading);
        if (reading == null || !reading.Layout.Matches(bounds, edge, DateTime.UtcNow)) return false;
        var host = FindWindow("Shell_TrayWnd", null);
        GetWindowThreadProcessId(host, out uint processId);
        if (host == IntPtr.Zero || reading.Host != host || reading.ProcessId != processId) return false;
        snapshot = reading.Layout;
        return true;
    }

    public void Request()
    {
        if (_disposed || Environment.TickCount64 - _lastRequest < 250 ||
            Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
        _lastRequest = Environment.TickCount64;
        // Explorer's accessibility provider may stall. Never wait for it on the UI thread,
        // and never queue another request while the previous one is still in progress.
        Task.Run(() =>
        {
            Reading reading = null;
            try { reading = Read(); }
            catch { }
            Publish(reading);
            Interlocked.Exchange(ref _pending, 0);
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!_disposed) _changed();
                }));
            }
            catch (InvalidOperationException) { }
        });
    }

    private void Publish(Reading reading)
    {
        // Keep a recent snapshot through transient provider failures; TryGet still
        // rejects expired geometry and a replaced or moved Explorer taskbar.
        if (!_disposed && reading != null) Volatile.Write(ref _reading, reading);
    }

    private static Reading Read()
    {
        DateTime started = DateTime.UtcNow;
        var host = FindWindow("Shell_TrayWnd", null);
        if (host == IntPtr.Zero || !TaskbarAnchor.TryGet(out var bounds, out uint edge, out _)) return null;
        GetWindowThreadProcessId(host, out uint processId);
        var root = AutomationElement.FromHandle(host);
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeScope = TreeScope.Element };
        foreach (var property in new[] { AutomationElement.BoundingRectangleProperty, AutomationElement.ClassNameProperty,
            AutomationElement.AutomationIdProperty, AutomationElement.ControlTypeProperty,
            AutomationElement.IsOffscreenProperty, AutomationElement.ProcessIdProperty })
            cache.Add(property);
        var condition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ClassNameProperty, "TrayNotifyWnd"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "MSTaskListWClass"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "MSTaskSwWClass"));
        var items = new List<TaskbarElement>();
        using (cache.Activate())
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
            {
                var info = element.Cached;
                items.Add(new TaskbarElement(info.BoundingRectangle, info.ClassName, info.AutomationId,
                    info.ControlType == ControlType.Button, info.IsOffscreen, info.ProcessId));
            }
        var layout = Build(bounds, edge, items.ToArray());
        if (layout == null || !TaskbarAnchor.TryGet(out var current, out uint currentEdge, out _) ||
            current != bounds || currentEdge != edge || host != FindWindow("Shell_TrayWnd", null))
            return null;
        return new Reading(host, processId, layout with { CapturedUtc = started });
    }

    internal static TaskbarLayoutSnapshot Build(Rect bounds, uint edge, TaskbarElement[] elements)
    {
        bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
            double.IsFinite(rect.Left) && double.IsFinite(rect.Top) &&
            double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom) && bounds.IntersectsWith(rect);
        var visible = elements.Where(e => !e.Offscreen && e.ProcessId != Environment.ProcessId && Valid(e.Bounds)).ToArray();
        bool Tray(TaskbarElement e) => e.ClassName == "TrayNotifyWnd" ||
            (e.ClassName ?? "").StartsWith("SystemTray.", StringComparison.Ordinal) ||
            e.Id is "SystemTrayIcon" or "NotifyItemIcon";
        if (!visible.Any(Tray)) return null;
        var buttons = visible.Where(e => e.Button && !Tray(e)).ToArray();
        var apps = buttons.Where(e => (e.ClassName ?? "").Contains("TaskListButton", StringComparison.Ordinal) ||
            (e.Id ?? "").StartsWith("Appid:", StringComparison.Ordinal)).ToArray();
        if (apps.Length == 0)
        {
            var legacy = visible.Where(e => e.ClassName is "MSTaskListWClass" or "MSTaskSwWClass").ToArray();
            apps = buttons.Where(e => legacy.Any(area => area.Bounds.Contains(e.Bounds))).ToArray();
        }
        if (apps.Length == 0) return null;
        var group = apps.Concat(buttons.Where(e => e.Id is "StartButton" or "SearchButton" or "TaskViewButton")).ToArray();
        Rect icons = Rect.Empty;
        foreach (var element in group) icons.Union(Rect.Intersect(bounds, element.Bounds));
        var occupied = visible.Where(e => e.Button || Tray(e))
            .Select(e => Rect.Intersect(bounds, e.Bounds)).ToArray();
        return new TaskbarLayoutSnapshot(bounds, edge, icons, occupied, DateTime.UtcNow);
    }

    public void Dispose() => _disposed = true;
}
