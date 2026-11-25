using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MouseDock.Services;

namespace MouseDock.Views;

public partial class DockWindow
{
    private DispatcherTimer _sceneFrames;
    private CancellationTokenSource _sceneOpening;
    private WallpaperEngineSource.SceneWindow _sceneWindow;
    private WallpaperWindowCapture _sceneCapture;
    private ImageDrawing _wallpaperSceneDrawing;
    private int _sceneWidth, _sceneFramesReceived;
    private long _sceneLastFrame;
    private string _sceneFailedPath;
    private long _sceneRetryAfter;

    private void InitializeScenePlayback()
    {
        _sceneFrames = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _sceneFrames.Tick += (_, _) => PaintSceneFrame();
    }

    private void UpdateScenePlayback()
    {
        if (!WallpaperVisible || !App.Settings.DockWallpaperLiveScene || _wallpaperContent?.Scene is not { } path || _sceneOpening != null ||
            (path == _sceneFailedPath && Environment.TickCount64 < _sceneRetryAfter)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        double pixelWidth = Math.Max(ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY * 16 / 9);
        int width = (int)Math.Clamp(Math.Ceiling(pixelWidth / 160) * 160, 1920, 2560);
        if (_sceneCapture != null)
        {
            // Wait until a material DPI or size change before rebuilding the renderer.
            if (width <= _sceneWidth * 1.25 && width >= _sceneWidth * .60) return;
            StopScenePlayback();
        }
        _ = StartScenePlaybackAsync(path, width);
    }

    private async Task StartScenePlaybackAsync(string path, int width)
    {
        using var request = new CancellationTokenSource();
        _sceneOpening = request;
        WallpaperEngineSource.SceneWindow window = null;
        try
        {
            window = await Task.Run(() => WallpaperEngineSource.OpenSceneAsync(path, width, width * 9 / 16, request.Token),
                request.Token);
            if (request.IsCancellationRequested || !WallpaperVisible || path != _wallpaperContent?.Scene) return;
            if (window == null)
            {
                System.Diagnostics.Trace.WriteLine("Wallpaper scene: no dedicated window was available.");
                _sceneFailedPath = path;
                _sceneRetryAfter = Environment.TickCount64 + 10_000;
                return;
            }
            _sceneCapture = new WallpaperWindowCapture(window.Handle);
            _sceneWindow = window;
            window = null;
            _sceneWidth = width;
            _sceneFramesReceived = 0;
            _sceneLastFrame = Environment.TickCount64;
            _sceneFailedPath = null;
            _sceneFrames.Start();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is COMException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.WriteLine($"Wallpaper scene startup failed: 0x{error.HResult:X8}.");
            _sceneFailedPath = path;
            _sceneRetryAfter = Environment.TickCount64 + 10_000;
        }
        finally
        {
            window?.Dispose();
            if (ReferenceEquals(_sceneOpening, request)) _sceneOpening = null;
            if (request.IsCancellationRequested) UpdateScenePlayback();
        }
    }

    private void PaintSceneFrame()
    {
        if (_sceneCapture == null) return;
        try
        {
            if (_sceneCapture.CopyLatestFrame() is { } frame)
            {
                _sceneLastFrame = Environment.TickCount64;
                // The first compositor frame can be captured before the scene is painted.
                if (++_sceneFramesReceived < 2 || ReferenceEquals(_wallpaperSceneDrawing?.ImageSource, frame)) return;
                _wallpaperSceneDrawing = new ImageDrawing(frame, new Rect(0, 0, frame.PixelWidth, frame.PixelHeight));
                PaintWallpaper();
            }
            else if (Environment.TickCount64 - _sceneLastFrame > (_sceneFramesReceived < 2 ? 15_000 : 8000))
            {
                System.Diagnostics.Trace.WriteLine("Wallpaper scene: frame delivery timed out.");
                FallBackToScenePreview();
            }
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.WriteLine($"Wallpaper scene capture failed: 0x{error.HResult:X8}.");
            FallBackToScenePreview();
        }
    }

    private void FallBackToScenePreview()
    {
        StopScenePlayback();
        _sceneFailedPath = _wallpaperContent?.Scene;
        _sceneRetryAfter = Environment.TickCount64 + 10_000;
    }

    private void StopScenePlayback()
    {
        _sceneOpening?.Cancel();
        _sceneFrames.Stop();
        _sceneCapture?.Dispose();
        _sceneWindow?.Dispose();
        _sceneCapture = null;
        _sceneWindow = null;
        if (_wallpaperSceneDrawing == null) return;
        _wallpaperSceneDrawing = null;
        PaintWallpaper();
    }
}
