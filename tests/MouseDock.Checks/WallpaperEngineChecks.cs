using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class WallpaperEngineChecks
{
    internal static void Preferences()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("""{"DockBackground":"manual.png","CustomBackground":"theme.jpg"}""")!;
        Require(!legacy.DockFollowWallpaperEngine, "Existing settings must not opt in.");
        legacy.DockFollowWallpaperEngine = true;
        var jsonOptions = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy.Clone(), jsonOptions), jsonOptions)!;
        Require(restored.DockFollowWallpaperEngine && restored.DockBackground == "manual.png" && restored.CustomBackground == "theme.jpg",
            "Opt-in and both manual images must survive cloning and serialization.");
    }

    internal static void Monitor()
    {
        using var document = JsonDocument.Parse("""
            {
              "anotherUser":{"general":{"wallpaperconfig":{"selectedwallpapers":{"Monitor0":{"file":"wrong.mp4"}}}}},
              "testUser":{"general":{
                "user":{"monitormap":{
                  "//?/DISPLAY#EXTERNAL#1":{"location":0},
                  "//?/DISPLAY#INTERNAL#2":{"location":1},
                  "//./DISPLAY7":{"location":0}
                }},
                "wallpaperconfig":{"layout":0,"selectedwallpapers":{
                    "Monitor0":{"file":"wide.mp4"},
                    "Monitor1":{"file":"laptop.mp4"}
                }}
              }}
            }
            """);
        (int? Index, string? File) Select(string physical, int displays) =>
            WallpaperEngineSource.SelectMonitor(document.RootElement, "testUser", @"\\.\DISPLAY7", physical, displays);
        Require(Select(@"\\?\DISPLAY#INTERNAL#2", 2) == (1, "laptop.mp4"), "The physical monitor identifier must outrank the adapter name.");
        Require(Select("", 2) == (0, "wide.mp4"), "An exact legacy adapter mapping may be used.");
        Require(Select(@"\\?\DISPLAY#UNKNOWN#3", 2) == (null, null), "Do not apply another screen's wallpaper when a physical display is unrecognized.");
        Require(Select(@"\\?\DISPLAY#UNKNOWN#3", 1) == (0, "wide.mp4"), "A single screen can use monitor zero.");
    }

    internal static void Assets()
    {
        string root = Path.Combine(Path.GetTempPath(), "mousedock-wallpaper-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project");
        Directory.CreateDirectory(project);
        try
        {
            string manifest = Path.Combine(project, "project.json");
            string video = Path.Combine(project, "\u6a2a\u5c4f\u58c1\u7eb8.mp4");
            string preview = Path.Combine(project, "preview.jpg");
            string scene = Path.Combine(project, "scene.pkg");
            string page = Path.Combine(project, "index.html");
            string outside = Path.Combine(root, "outside.jpg");
            foreach (string file in new[] { video, preview, scene, page, outside })
                File.WriteAllBytes(file, [0]);

            File.WriteAllText(manifest, $$"""{"type":"video","file":"{{Path.GetFileName(video)}}","preview":"preview.jpg"}""");
            var active = WallpaperEngineSource.ResolveAsset($" \uFEFF\"{video}\"\r\n");
            Require(active?.Video == video && active.Image == preview, "Direct Unicode video paths should retain a local preview.");
            Require(WallpaperEngineSource.ResolveAsset(manifest)?.Video == video, "A video project's file should play.");

            File.WriteAllText(manifest, """{"type":"scene","file":"scene.pkg","preview":"preview.jpg"}""");
            Require(WallpaperEngineSource.ResolveAsset(scene) is { Video: null } sceneAsset && sceneAsset.Image == preview, "Scenes may show the preview.");
            File.WriteAllText(manifest, """{"type":"web","file":"index.html","preview":"preview.jpg"}""");
            Require(WallpaperEngineSource.ResolveAsset(page) is { Video: null } webAsset && webAsset.Image == preview, "Web wallpapers must remain static previews.");

            foreach (string untrusted in new[] { @"..\outside.jpg", outside, @"\\server\share\image.jpg", @"preview.jpg:alternate" })
            {
                File.WriteAllText(manifest, JsonSerializer.Serialize(new { type = "web", file = "index.html", preview = untrusted }));
                Require(WallpaperEngineSource.ResolveAsset(page) == null, "Project thumbnails must not escape the project's local, ordinary files.");
            }
            Require(WallpaperEngineSource.ResolveAsset("https://example.com/image.jpg") == null &&
                WallpaperEngineSource.ResolveAsset(@"\\server\share\wallpaper.jpg") == null &&
                WallpaperEngineSource.ResolveAsset(video + "\n" + outside) == null &&
                WallpaperEngineSource.ResolveAsset(Path.Combine(project, "missing.png")) == null,
                "Remote, multi-line and missing wallpaper paths must be ignored.");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Card()
    {
        string background = Path.Combine(Path.GetTempPath(), "mousedock-manual-" + Guid.NewGuid().ToString("N") + ".png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[]
        {
            0, 0, 255, 255, 0, 0, 255, 255,
            0, 0, 255, 255, 0, 0, 255, 255
        }, 8);
        bitmap.Freeze();
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(background)) png.Save(file);

        var values = new ConcurrentQueue<(CancellationToken Token, TaskCompletionSource<WallpaperContent?> Reply)>();
        var settings = new AppSettings { DockBackground = background, Provider = "json" };
        var oldSettings = App.Settings;
        var oldBattery = App.Battery;
        using var battery = new BatteryService(settings, null, false);
        typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null, settings);
        typeof(App).GetProperty(nameof(App.Battery))!.SetValue(null, battery);
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var card = new DockWindow((_, token) =>
        {
            var reply = new TaskCompletionSource<WallpaperContent?>(TaskCreationOptions.RunContinuationsAsynchronously);
            values.Enqueue((token, reply));
            return reply.Task;
        }) { Left = -2000, ShowActivated = false };
        var root = (Border)card.Content;
        try
        {
            card.ApplyBackground(background);
            card.Show();
            Require(root.Background is ImageBrush && values.IsEmpty, "The manual background must remain selected before opting in.");
            settings.DockFollowWallpaperEngine = true;
            card.ApplyBackground(background);
            var first = TakeQuery();
            card.Hide();
            Require(first.Token.IsCancellationRequested, "Hiding the card must cancel its query.");
            card.Show();
            first.Reply.SetResult(new WallpaperContent(null, bitmap));
            var second = TakeQuery();
            Require(root.Background is ImageBrush, "A late hidden-window response must not replace the manual image.");
            second.Reply.SetResult(new WallpaperContent(null, bitmap));
            PumpUntil(() => root.Background is DrawingBrush, "An active preview should be drawn on the card.");

            card.ApplyDensity("bare");
            card.UpdateLayout();
            Require(root.Background is SolidColorBrush bare && bare.Color == App.Theme.Surface,
                "Bare density must keep its solid theme color.");
            var bareBackground = root.Background;
            card.Left -= 10;
            card.Hide();
            card.Show();
            card.UpdateLayout();
            Require(ReferenceEquals(root.Background, bareBackground) && values.IsEmpty,
                "Moving, hiding and showing a bare card should retain its brush without querying Wallpaper Engine.");
            card.ApplyDensity("compact");
            var third = TakeQuery();
            settings.DockFollowWallpaperEngine = false;
            card.ApplyBackground(background);
            Require(third.Token.IsCancellationRequested && root.Background is ImageBrush,
                "Turning off the wallpaper must immediately restore the manual picture.");
            third.Reply.SetResult(new WallpaperContent(null, bitmap));
            PumpUntil(() => card.Dispatcher.HasShutdownStarted, null, 100);
            Require(root.Background is ImageBrush && settings.DockBackground == background && values.IsEmpty,
                "A late result must not overwrite the restored manual preference or paint the wallpaper.");
        }
        finally
        {
            card.Close();
            foreach (var pending in values) pending.Reply.TrySetResult(null);
            SynchronizationContext.SetSynchronizationContext(before);
            typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null, oldSettings);
            typeof(App).GetProperty(nameof(App.Battery))!.SetValue(null, oldBattery);
            File.Delete(background);
        }

        (CancellationToken Token, TaskCompletionSource<WallpaperContent?> Reply) TakeQuery()
        {
            PumpUntil(() => !values.IsEmpty, "The visible, enabled card did not query its wallpaper.");
            Require(values.TryDequeue(out var query), "The expected wallpaper query was not captured.");
            return query;
        }
    }

    internal static int RunLive()
    {
        var settings = new AppSettings { DockFollowWallpaperEngine = true, Provider = "json" };
        using var battery = new BatteryService(settings, null, false);
        typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null, settings);
        typeof(App).GetProperty(nameof(App.Battery))!.SetValue(null, battery);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var source = new WallpaperEngineSource();
        var received = new TaskCompletionSource<WallpaperContent?>();
        var card = new DockWindow(async (monitor, token) =>
        {
            var content = await source.ReadAsync(monitor, token).ConfigureAwait(false);
            received.TrySetResult(content);
            return content;
        }) { Top = 80, Left = 80, ShowActivated = false };
        var root = (Border)card.Content;
        MediaPlayer? Player() =>
            (root.Background as DrawingBrush)?.Drawing is DrawingGroup group && group.Children[0] is VideoDrawing frame ? frame.Player : null;
        try
        {
            card.ApplyBackground("");
            card.Show();
            PumpUntil(() => received.Task.IsCompleted, "The running Wallpaper Engine did not respond.", 12000);
            var content = received.Task.Result ?? throw new InvalidOperationException("The active Wallpaper Engine file did not resolve.");
            PumpUntil(() => root.Background is DrawingBrush, "The current live wallpaper did not resolve and render.", 12000);
            if (content.Video != null)
                PumpUntil(() => Player() != null, "The configured video did not open in the transparent WPF card.", 12000);
            if (Player() is { } player)
            {
                Require(player.IsMuted && player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0,
                    "The active video did not open muted.");
                PumpUntil(() => player.Position > TimeSpan.FromMilliseconds(100), "The video did not advance.");
                card.Hide();
                PumpUntil(() => false, null, 200);
                var position = player.Position;
                PumpUntil(() => false, null, 250);
                Require((player.Position - position).Duration() < TimeSpan.FromMilliseconds(120),
                    "The video should pause when the card is hidden.");
                card.Show();
                PumpUntil(() => (player.Position - position).Duration() > TimeSpan.FromMilliseconds(120), "The video did not resume.");
                Console.WriteLine($"PASS installed Wallpaper Engine video opened muted at {player.NaturalVideoWidth}x{player.NaturalVideoHeight}; pause/resume confirmed.");
            }
            else Console.WriteLine("PASS installed Wallpaper Engine preview rendered.");
            return 0;
        }
        finally { card.Close(); }
    }

    private static void PumpUntil(Func<bool> condition, string? message, int milliseconds = 2000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        if (message != null) Require(condition(), message);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
