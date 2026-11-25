using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using MouseDock.Services;

namespace MouseDock.Views;

public partial class DockWindow
{
    internal MenuItem CreateWallpaperMenu(Func<CancellationToken, IReadOnlyList<WallpaperChoice>> readCatalog = null)
    {
        readCatalog ??= WallpaperEngineSource.ReadCatalog;
        var entry = new MenuItem { Header = "Wallpaper Engine 壁纸" };
        string selected = App.Settings.DockWallpaperEngineSelection ?? "";
        bool follows = App.Settings.DockFollowWallpaperEngine && selected.Length == 0;
        AddChoice("跟随当前桌面壁纸", follows, () => ChooseWallpaper(true, ""));
        AddChoice("恢复卡片原背景", !follows && selected.Length == 0, () => ChooseWallpaper(false, ""));
        entry.Items.Add(new Separator());
        var status = new MenuItem { Header = "正在查找本地壁纸…", IsEnabled = false };
        entry.Items.Add(status);
        CancellationTokenSource scan = null;
        entry.SubmenuOpened += async (_, _) =>
        {
            if (scan != null || !entry.Items.Contains(status)) return;
            using var request = new CancellationTokenSource();
            scan = request;
            status.Header = "正在查找本地壁纸…";
            IReadOnlyList<WallpaperChoice> wallpapers = null;
            try
            {
                wallpapers = await Task.Run(() => readCatalog(request.Token), request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

            if (entry.Dispatcher.HasShutdownStarted) return;
            var operation = entry.Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(scan, request)) scan = null;
                if (request.IsCancellationRequested || !entry.IsSubmenuOpen) return;
                if (wallpapers == null || wallpapers.Count == 0)
                {
                    status.Header = wallpapers == null ? "暂时无法读取壁纸库" : "未找到可用的本地壁纸";
                    return;
                }
                entry.Items.Remove(status);
                foreach (var wallpaper in wallpapers)
                    AddChoice($"{wallpaper.Title} · {wallpaper.Kind}",
                        string.Equals(selected, wallpaper.Path, StringComparison.OrdinalIgnoreCase),
                        () => ChooseWallpaper(false, wallpaper.Path, wallpaper.Kind == WallpaperChoice.LiveScene));
            });
            try { await operation.Task.ConfigureAwait(false); }
            catch (TaskCanceledException) when (entry.Dispatcher.HasShutdownStarted) { }
        };
        entry.SubmenuClosed += (_, _) =>
        {
            scan?.Cancel();
            scan = null;
        };
        return entry;

        void AddChoice(string label, bool active, Action select)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = active };
            item.Click += (_, _) => select();
            entry.Items.Add(item);
        }
    }

    private void ChooseWallpaper(bool follow, string path, bool liveScene = false)
    {
        App.Settings.DockFollowWallpaperEngine = follow;
        App.Settings.DockWallpaperEngineSelection = path;
        App.Settings.DockWallpaperLiveScene = liveScene;
        _sceneFailedPath = null;
        if (_bareDensity && (follow || path.Length > 0))
            ApplyDensity("standard");
        else
            ApplyBackground(App.Settings.DockBackground);
        App.SaveSettings();
        App.SettingsView?.Reload();
    }
}
