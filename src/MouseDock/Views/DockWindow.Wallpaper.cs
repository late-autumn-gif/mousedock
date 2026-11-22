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
    private WallpaperMonitor? _wallpaperMonitor;
    private WallpaperContent _wallpaperContent;
    private MediaPlayer _wallpaperPlayer;
    private Brush _wallpaperVideo;
    private bool _wallpaperClosed;
    private bool _bareDensity;

    private void InitializeWallpaper(Func<WallpaperMonitor, CancellationToken, Task<WallpaperContent>> readWallpaper)
    {
        _readWallpaper = readWallpaper;
        _wallpaperTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _wallpaperTimer.Tick += (_, _) => UpdateWallpaperActivation(refresh: true);
        IsVisibleChanged += (_, _) => UpdateWallpaperActivation();
        StateChanged += (_, _) => UpdateWallpaperActivation();
        LocationChanged += (_, _) => UpdateWallpaperActivation();
        Closed += (_, _) =>
        {
            _wallpaperClosed = true;
            UpdateWallpaperActivation();
        };
    }

    private bool WallpaperEnabled => !_wallpaperClosed && !_bareDensity && App.Settings.DockFollowWallpaperEngine;
    private bool WallpaperVisible => WallpaperEnabled && IsVisible && WindowState != WindowState.Minimized;

    private void UpdateWallpaperActivation(bool refresh = false)
    {
        if (!WallpaperVisible)
        {
            _wallpaperTimer.Stop();
            _wallpaperQuery?.Cancel();
            if (WallpaperEnabled) _wallpaperPlayer?.Pause();
            else if (_wallpaperMonitor != null || _wallpaperContent != null)
            {
                _wallpaperMonitor = null;
                ApplyWallpaper(null);
            }
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = new WallpaperMonitor(Forms.Screen.FromHandle(handle).DeviceName, Forms.Screen.AllScreens.Length);
        bool monitorChanged = _wallpaperMonitor != monitor;
        if (monitorChanged)
        {
            _wallpaperQuery?.Cancel();
            _wallpaperMonitor = monitor;
            ApplyWallpaper(null);
        }

        if (!_wallpaperTimer.IsEnabled)
        {
            _wallpaperTimer.Start();
            _wallpaperPlayer?.Play();
            refresh = true;
        }
        if ((refresh || monitorChanged) && _wallpaperQuery == null)
            _ = RefreshWallpaperAsync(monitor);
    }

    private async Task RefreshWallpaperAsync(WallpaperMonitor monitor)
    {
        using var query = new CancellationTokenSource();
        _wallpaperQuery = query;
        try
        {
            var result = await Task.Run(() => _readWallpaper(monitor, query.Token), query.Token);
            if (!query.IsCancellationRequested && WallpaperVisible && _wallpaperMonitor == monitor)
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
        if (_wallpaperContent?.Video != content?.Video)
        {
            CloseWallpaperVideo();
            if (content?.Video is { } path) OpenWallpaperVideo(path);
        }
        _wallpaperContent = content;
        PaintWallpaper();
    }

    private void OpenWallpaperVideo(string path)
    {
        var player = new MediaPlayer { IsMuted = true, Volume = 0 };
        _wallpaperPlayer = player;
        player.MediaOpened += (_, _) =>
        {
            if (!ReferenceEquals(_wallpaperPlayer, player)) return;
            if (player.NaturalVideoWidth <= 0 || player.NaturalVideoHeight <= 0)
            {
                CloseWallpaperVideo();
                PaintWallpaper();
                return;
            }
            if (!WallpaperVisible) player.Pause();
            PaintWallpaper();
        };
        player.MediaEnded += (_, _) =>
        {
            if (!ReferenceEquals(_wallpaperPlayer, player)) return;
            player.Position = TimeSpan.Zero;
            if (WallpaperVisible) player.Play();
        };
        player.MediaFailed += (_, _) =>
        {
            if (!ReferenceEquals(_wallpaperPlayer, player)) return;
            CloseWallpaperVideo();
            PaintWallpaper();
        };
        try
        {
            player.Open(new Uri(path, UriKind.Absolute));
            if (WallpaperVisible) player.Play();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            CloseWallpaperVideo();
        }
    }

    private void CloseWallpaperVideo()
    {
        var player = _wallpaperPlayer;
        _wallpaperPlayer = null;
        _wallpaperVideo = null;
        player?.Close();
    }

    private void PaintWallpaper()
    {
        if (_wallpaperPlayer is { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 } player)
        {
            var bounds = new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight);
            var frame = new VideoDrawing { Player = player, Rect = bounds };
            byte opacity = (byte)Math.Round(255 * PictureBackground.Clamp(App.Settings.BackgroundDepth, 0, .8, .35));
            var tint = new SolidColorBrush(Palette.WithAlpha(App.Theme.BgTop, opacity));
            var layers = new DrawingGroup();
            layers.Children.Add(frame);
            layers.Children.Add(new GeometryDrawing(tint, null, new RectangleGeometry(bounds)));
            _wallpaperVideo = new DrawingBrush(layers) { Stretch = Stretch.UniformToFill };
        }
        PictureBackground.SetWallpaper(Root, _wallpaperContent?.Image, _wallpaperVideo);
    }
}
