using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MouseDock.Services;

internal sealed partial class WallpaperEngineSource
{
    private static readonly SemaphoreSlim SceneGate = new(1, 1);
    private static IntPtr _stalledScene;

    internal sealed class SceneWindow : IDisposable
    {
        private readonly string _name;
        private FileStream _engine;

        internal SceneWindow(string name, IntPtr handle, FileStream engine)
        {
            _name = name;
            Handle = handle;
            _engine = engine;
        }

        internal IntPtr Handle { get; }

        public void Dispose()
        {
            using var engine = Interlocked.Exchange(ref _engine, null);
            if (engine == null) return;
            SendSceneCommand(engine.Name, "closeWallpaper", "-location", _name);
            _ = WaitForSceneCloseAsync(Handle);
        }
    }

    internal static async Task<SceneWindow> OpenSceneAsync(string scene, int width, int height, CancellationToken token)
    {
        if (token.IsCancellationRequested) return null;
        if (ResolveAsset(scene)?.Scene != scene)
        {
            Trace.WriteLine("Wallpaper Engine: scene manifest did not resolve.");
            return null;
        }
        await SceneGate.WaitAsync(token).ConfigureAwait(false);
        FileStream engine = null;
        SceneWindow result = null;
        IntPtr handle = IntPtr.Zero;
        string name = "MouseDock Scene " + Guid.NewGuid().ToString("N");
        bool issued = false;
        try
        {
            if (_stalledScene != IntPtr.Zero && IsWindow(_stalledScene)) return null;
            _stalledScene = IntPtr.Zero;
            engine = OpenRunningExecutable(out int pid);
            if (engine == null)
            {
                Trace.WriteLine("Wallpaper Engine: no verified engine process is available.");
                return null;
            }
            if (token.IsCancellationRequested) return null;
            int left = GetSystemMetrics(76);
            int top = GetSystemMetrics(77);
            issued = SendSceneCommand(engine.Name, "openWallpaper", "-file", scene, "-playInWindow", name,
                "-width", width.ToString(CultureInfo.InvariantCulture), "-height", height.ToString(CultureInfo.InvariantCulture),
                "-x", (left - 4096).ToString(CultureInfo.InvariantCulture), "-y", top.ToString(CultureInfo.InvariantCulture),
                "-borderless");
            if (!issued)
            {
                Trace.WriteLine("Wallpaper Engine: could not issue the private window command.");
                return null;
            }

            long deadline = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < deadline)
            {
                var candidate = FindWindow(null, name);
                if (candidate != IntPtr.Zero && GetWindowThreadProcessId(candidate, out var actual) != 0 && actual == pid)
                {
                    handle = candidate;
                    // The engine may clamp its first placement into the work area. Move only the newly named window.
                    int style = GetWindowLong(handle, -20);
                    SetWindowLong(handle, -20, (style & ~0x00040000) | 0x08000080);
                    if (!SetWindowPos(handle, IntPtr.Zero, left - 8192, top, 0, 0, 0x0035))
                    {
                        Trace.WriteLine("Wallpaper Engine: the private window could not be moved off screen.");
                        return null;
                    }
                    if (token.IsCancellationRequested) return null;
                    return result = new SceneWindow(name, handle, engine);
                }
                if (candidate != IntPtr.Zero)
                {
                    Trace.WriteLine("Wallpaper Engine: the private window did not match the verified process.");
                    return null;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            Trace.WriteLine("Wallpaper Engine: the private window did not open before the deadline.");
            return null;
        }
        finally
        {
            if (result == null)
            {
                if (issued) SendSceneCommand(engine.Name, "closeWallpaper", "-location", name);
                engine?.Dispose();
                if (issued) await WaitForSceneCloseAsync(handle).ConfigureAwait(false);
                else SceneGate.Release();
            }
        }
    }

    private static async Task WaitForSceneCloseAsync(IntPtr handle)
    {
        try
        {
            long deadline = Environment.TickCount64 + 2500;
            while (handle != IntPtr.Zero && IsWindow(handle) && Environment.TickCount64 < deadline)
                await Task.Delay(50).ConfigureAwait(false);
            if (handle != IntPtr.Zero && IsWindow(handle)) _stalledScene = handle;
        }
        finally { SceneGate.Release(); }
    }

    private static bool SendSceneCommand(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-control");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        try { using var process = Process.Start(start); return process != null; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { return false; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string name);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
