using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace MouseDock.Services
{
    internal sealed class TaskbarChangeMonitor : IDisposable
    {
        private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window,
            int objectId, int childId, uint threadId, uint time);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module,
            WinEventCallback callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string title);

        private readonly Dispatcher _dispatcher;
        private readonly Action _update;
        private readonly WinEventCallback _callback;
        private readonly IntPtr _foregroundHook;
        private readonly IntPtr _windowHook;
        private bool _queued, _disposed;

        public TaskbarChangeMonitor(Dispatcher dispatcher, Action update)
        {
            _dispatcher = dispatcher;
            _update = update;
            _callback = OnEvent;
            // Out-of-context callbacks are delivered on this UI thread's message loop.
            _foregroundHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _callback, 0, 0, 0);
            _windowHook = SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, _callback, 0, 0, 0);
        }

        private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId,
            int childId, uint threadId, uint time)
        {
            if (_disposed || _queued || _dispatcher.HasShutdownStarted) return;
            bool foregroundChanged = eventType == 0x0003;
            // Top-level Z-order changes are reported on the desktop's client object.
            bool orderChanged = eventType == 0x8004 &&
                (objectId == 0 || (objectId == -4 && window == GetDesktopWindow()));
            bool taskbarChanged = (eventType == 0x8002 || eventType == 0x8003 || eventType == 0x800B) &&
                objectId == 0 && window != IntPtr.Zero && window == FindWindow("Shell_TrayWnd", null);
            if (!foregroundChanged && !orderChanged && !taskbarChanged) return;
            _queued = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                _queued = false;
                if (!_disposed) _update();
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
            if (_windowHook != IntPtr.Zero) UnhookWinEvent(_windowHook);
            GC.KeepAlive(_callback);
        }
    }
}
