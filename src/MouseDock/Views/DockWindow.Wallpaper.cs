using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MouseDock.Services;
using Forms = System.Windows.Forms;

namespace MouseDock.Views;

public partial class DockWindow
{
    private Func<WallpaperMonitor, CancellationToken, Task<WallpaperContent>> _readWallpaper;
    private DispatcherTimer _wallpaperTimer;
    private CancellationTokenSource _wallpaperQuery;
    private readonly WallpaperEngineSource _wallpaperLibrary = new();
    private WallpaperMonitor? _wallpaperMonitor;
    private string _wallpaperSelection;
    private WallpaperContent _wallpaperContent;
    private MediaPlayer _wallpaperPlayer, _wallpaperNext;
    private DispatcherTimer _wallpaperLoop;
    private bool _wallpaperNextReady;
    private TimeSpan _lastVideoPosition;
    private long _lastVideoAdvance, _videoRetryAfter;
    private int _videoRecoveryAttempts;
    private int _wallpaperFrame;
    private long _wallpaperFrameDue;
    private VideoDrawing _wallpaperVideo;
    private bool _wallpaperClosed;
    private bool _bareDensity;

    private void InitializeWallpaper(Func<WallpaperMonitor, CancellationToken, Task<WallpaperContent>> readWallpaper)
    {
        _readWallpaper = readWallpaper;
        InitializeScenePlayback();
        _wallpaperTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _wallpaperTimer.Tick += (_, _) => UpdateWallpaperActivation(refresh: true);
        _wallpaperLoop = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _wallpaperLoop.Tick += (_, _) => AdvanceWallpaperLoop();
        IsVisibleChanged += (_, _) => UpdateWallpaperActivation();
        StateChanged += (_, _) => UpdateWallpaperActivation();
        LocationChanged += (_, _) => UpdateWallpaperActivation();
        Closed += (_, _) =>
        {
            _wallpaperClosed = true;
            UpdateWallpaperActivation();
        };
    }

    private bool WallpaperEnabled => !_wallpaperClosed && !_bareDensity &&
        (App.Settings.DockFollowWallpaperEngine || !string.IsNullOrWhiteSpace(App.Settings.DockWallpaperEngineSelection));
    private bool WallpaperVisible => WallpaperEnabled && IsVisible && WindowState != WindowState.Minimized;

    private void UpdateWallpaperActivation(bool refresh = false)
    {
        if (!WallpaperVisible)
        {
            StopScenePlayback();
            _wallpaperTimer.Stop();
            _wallpaperLoop.Stop();
            _wallpaperNext?.Pause();
            _wallpaperQuery?.Cancel();
            if (WallpaperEnabled) _wallpaperPlayer?.Pause();
            else if (_wallpaperMonitor != null || _wallpaperContent != null)
            {
                _wallpaperMonitor = null;
                _wallpaperSelection = null;
                ApplyWallpaper(null);
            }
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = new WallpaperMonitor(Forms.Screen.FromHandle(handle).DeviceName, Forms.Screen.AllScreens.Length);
        string selection = App.Settings.DockWallpaperEngineSelection ?? "";
        bool sourceChanged = _wallpaperMonitor != monitor || selection != _wallpaperSelection;
        if (sourceChanged)
        {
            _wallpaperQuery?.Cancel();
            _wallpaperMonitor = monitor;
            _wallpaperSelection = selection;
            ApplyWallpaper(null);
        }

        if (!_wallpaperTimer.IsEnabled)
        {
            _wallpaperTimer.Start();
            _wallpaperPlayer?.Play();
            if (!_wallpaperNextReady) _wallpaperNext?.Play();
            _videoRecoveryAttempts = 0;
            _videoRetryAfter = 0;
            TrackVideoProgress();
            if (_wallpaperContent?.Video != null || _wallpaperContent?.Animation != null) _wallpaperLoop.Start();
            refresh = true;
        }
        if ((refresh || sourceChanged) && _wallpaperQuery == null)
            _ = RefreshWallpaperAsync(monitor);
        UpdateScenePlayback();
    }

    private async Task RefreshWallpaperAsync(WallpaperMonitor monitor)
    {
        using var query = new CancellationTokenSource();
        _wallpaperQuery = query;
        string selection = _wallpaperSelection;
        try
        {
            var result = await Task.Run(() => string.IsNullOrEmpty(selection)
                ? _readWallpaper(monitor, query.Token)
                : Task.FromResult(_wallpaperLibrary.ReadSelected(selection)), query.Token);
            if (!query.IsCancellationRequested && WallpaperVisible && _wallpaperMonitor == monitor &&
                _wallpaperSelection == selection)
                ApplyWallpaper(result);
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        finally
        {
            _wallpaperQuery = null;
            if (query.IsCancellationRequested && WallpaperVisible && _wallpaperMonitor is { } current)
                _ = RefreshWallpaperAsync(current);
        }
    }

    private void ApplyWallpaper(WallpaperContent content)
    {
        if (content == _wallpaperContent) return;
        if (content?.Scene != _wallpaperContent?.Scene) StopScenePlayback();
        if (_wallpaperContent?.Video != content?.Video)
        {
            CloseWallpaperVideo();
            _videoRecoveryAttempts = 0;
            _videoRetryAfter = 0;
            if (content?.Video is { } path) OpenWallpaperVideo(path);
        }
        _wallpaperContent = content;
        UpdateScenePlayback();
        _wallpaperFrame = 0;
        _wallpaperFrameDue = Environment.TickCount64 + (content?.Animation?.Delays[0] ?? 0);
        if (WallpaperVisible && (content?.Animation != null || content?.Video != null)) _wallpaperLoop.Start();
        else _wallpaperLoop.Stop();
        PaintWallpaper();
    }

    private void OpenWallpaperVideo(string path)
    {
        var active = new MediaPlayer { IsMuted = true, Volume = 0 };
        var standby = new MediaPlayer { IsMuted = true, Volume = 0 };
        _wallpaperPlayer = active;
        _wallpaperNext = standby;
        TrackVideoProgress();
        foreach (var player in new[] { active, standby })
        {
            player.MediaOpened += (_, _) =>
            {
                if (!ReferenceEquals(_wallpaperPlayer, player)) return;
                if (player.NaturalVideoWidth <= 0 || player.NaturalVideoHeight <= 0)
                {
                    DeferVideoRecovery();
                    return;
                }
                if (!WallpaperVisible) player.Pause();
                TrackVideoProgress();
                PaintWallpaper();
            };
            player.MediaEnded += (_, _) =>
            {
                if (!ReferenceEquals(_wallpaperPlayer, player) || !WallpaperVisible) return;
                if (_wallpaperNextReady) SwapWallpaperPlayers();
                else
                {
                    player.Position = TimeSpan.Zero;
                    player.Play();
                    TrackVideoProgress();
                }
            };
            player.MediaFailed += (_, _) =>
            {
                if (ReferenceEquals(_wallpaperPlayer, player))
                    DeferVideoRecovery();
                else if (ReferenceEquals(_wallpaperNext, player))
                {
                    _wallpaperNext = null;
                    _wallpaperNextReady = false;
                    player.Close();
                }
            };
        }
        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            active.Open(uri);
            if (WallpaperVisible) active.Play();
            try
            {
                standby.Open(uri);
                if (WallpaperVisible) standby.Play();
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                _wallpaperNext = null;
                standby.Close();
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            DeferVideoRecovery();
        }
    }

    private void TrackVideoProgress()
    {
        _lastVideoPosition = _wallpaperPlayer?.Position ?? TimeSpan.Zero;
        _lastVideoAdvance = Environment.TickCount64;
    }

    private void DeferVideoRecovery()
    {
        CloseWallpaperVideo();
        _videoRetryAfter = Environment.TickCount64 + Math.Min(30_000, 1500L << Math.Min(_videoRecoveryAttempts, 4));
        PaintWallpaper();
    }

    private void AdvanceWallpaperLoop()
    {
        if (!WallpaperVisible) return;
        if (_wallpaperVideo == null && _wallpaperSceneDrawing == null && _wallpaperContent?.Animation is { } animation)
        {
            long now = Environment.TickCount64;
            if (now < _wallpaperFrameDue) return;
            _wallpaperFrame = (_wallpaperFrame + 1) % animation.Frames.Length;
            _wallpaperFrameDue = now + animation.Delays[_wallpaperFrame];
            PaintWallpaper();
        }
        if (_wallpaperContent?.Video is not { } path) return;
        long tick = Environment.TickCount64;
        if (_wallpaperPlayer == null)
        {
            if (tick >= _videoRetryAfter)
            {
                _videoRecoveryAttempts++;
                OpenWallpaperVideo(path);
            }
            return;
        }

        if (!_wallpaperNextReady && _wallpaperNext is { NaturalVideoWidth: > 0 } standby &&
            standby.Position.TotalMilliseconds >= 65)
        {
            // A late dispatcher tick can miss the opening interval. Re-prime instead of
            // leaving a decoder playing to the end with no opening frame available.
            if (standby.Position.TotalMilliseconds > 400)
            {
                standby.Position = TimeSpan.Zero;
                standby.Play();
            }
            else
            {
                standby.Pause();
                _wallpaperNextReady = true;
            }
        }

        TimeSpan position = _wallpaperPlayer.Position;
        if (position != _lastVideoPosition)
        {
            _lastVideoAdvance = tick;
            if (position.TotalMilliseconds > 400) _videoRecoveryAttempts = 0;
            _lastVideoPosition = position;
        }
        int timeout = _wallpaperPlayer.NaturalVideoWidth > 0 ? 2600 : 10_000;
        bool stalled = tick - _lastVideoAdvance > timeout;
        if (_wallpaperNextReady && (stalled ||
            (_wallpaperPlayer.NaturalDuration.HasTimeSpan &&
             _wallpaperPlayer.NaturalDuration.TimeSpan - position <= TimeSpan.FromMilliseconds(75))))
            SwapWallpaperPlayers();
        else if (stalled) DeferVideoRecovery();
    }

    private void SwapWallpaperPlayers()
    {
        var previous = _wallpaperPlayer;
        _wallpaperPlayer = _wallpaperNext;
        _wallpaperNext = previous;
        _wallpaperNextReady = false;
        _wallpaperPlayer.Play();
        TrackVideoProgress();
        PaintWallpaper();
        previous.Position = TimeSpan.Zero;
        previous.Play();
    }

    private void CloseWallpaperVideo()
    {
        var player = _wallpaperPlayer;
        var standby = _wallpaperNext;
        _wallpaperPlayer = _wallpaperNext = null;
        _wallpaperNextReady = false;
        _wallpaperVideo = null;
        player?.Close();
        standby?.Close();
    }

    private void PaintWallpaper()
    {
        if (_wallpaperPlayer is { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 } player)
        {
            var bounds = new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight);
            _wallpaperVideo ??= new VideoDrawing();
            if (_wallpaperVideo.Player != player) _wallpaperVideo.Player = player;
            if (_wallpaperVideo.Rect != bounds) _wallpaperVideo.Rect = bounds;
        }
        var image = _wallpaperContent?.Animation is { } animation
            ? animation.Frames[_wallpaperFrame] : _wallpaperContent?.Image;
        PictureBackground.SetWallpaper(Root, image, (Drawing)_wallpaperVideo ?? _wallpaperSceneDrawing);
    }
}
