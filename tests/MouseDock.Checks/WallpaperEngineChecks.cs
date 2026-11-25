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
        Require(!legacy.DockFollowWallpaperEngine && !legacy.DockWallpaperLiveScene, "Existing settings must not opt in.");
        legacy.DockFollowWallpaperEngine = true;
        legacy.DockWallpaperLiveScene = true;
        legacy.DockWallpaperEngineSelection = "chosen/project.json";
        var jsonOptions = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy.Clone(), jsonOptions), jsonOptions)!;
        Require(restored.DockFollowWallpaperEngine && restored.DockWallpaperLiveScene && restored.DockWallpaperEngineSelection == "chosen/project.json" && restored.DockBackground == "manual.png" && restored.CustomBackground == "theme.jpg",
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

            File.WriteAllText(manifest, $$"""{"type":"Video","file":"{{Path.GetFileName(video)}}","preview":"preview.jpg"}""");
            var active = WallpaperEngineSource.ResolveAsset($" \uFEFF\"{video}\"\r\n");
            Require(active?.Video == video && active.Image == preview, "Direct Unicode video paths should retain a local preview.");
            Require(WallpaperEngineSource.ResolveAsset(manifest)?.Video == video, "A video project's file should play.");

            File.WriteAllText(manifest, """{"type":"scene","file":"scene.pkg","preview":"preview.jpg"}""");
            Require(WallpaperEngineSource.ResolveAsset(scene) is { Video: null } sceneAsset && sceneAsset.Image == preview && sceneAsset.Scene == manifest, "A scene keeps its project path and fallback preview.");
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

    internal static int RunResizeLive()
    {
        using var fixture = new AppearanceFixture();
        var card = new DockWindow { Top = 100, Left = 120, Topmost = true };
        fixture.Set("Dock", card);
        var cursor = System.Windows.Forms.Cursor.Position;
        try
        {
            card.Show();
            PumpUntil(() => false, null, 350);
            double initialWidth = card.ActualWidth;
            var edge = card.PointToScreen(new Point(initialWidth - 3, card.ActualHeight / 2));
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)edge.X, (int)edge.Y);
            PumpUntil(() => false, null, 80);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            PumpUntil(() => false, null, 80);
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)edge.X + 100, (int)edge.Y);
            PumpUntil(() => false, null, 120);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            PumpUntil(() => false, null, 100);
            Require(card.ActualWidth > initialWidth + 25 && App.Settings.DockWidth == card.Width &&
                App.Settings.DockHeight == card.Height, $"Dragging the right edge must resize the real window and save both dimensions. Width {initialWidth} -> {card.ActualWidth}, saved {App.Settings.DockWidth}x{App.Settings.DockHeight}, pointer {System.Windows.Input.Mouse.GetPosition(card)}.");
            double beforeWidth = card.ActualWidth, beforeHeight = card.ActualHeight;
            var corner = card.PointToScreen(new Point(2, 2));
            Point opposite = new(card.Left + beforeWidth, card.Top + beforeHeight);
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)corner.X, (int)corner.Y);
            PumpUntil(() => false, null, 80);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            PumpUntil(() => false, null, 80);
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)corner.X - 65, (int)corner.Y - 55);
            PumpUntil(() => false, null, 120);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            PumpUntil(() => false, null, 100);
            Require(card.ActualWidth > beforeWidth + 25 && card.ActualHeight > beforeHeight + 25 &&
                Math.Abs(card.Left + card.ActualWidth - opposite.X) < 2 &&
                Math.Abs(card.Top + card.ActualHeight - opposite.Y) < 2,
                "Dragging from the transparent rounded corner must adjust both dimensions and keep the opposite corner fixed.");
            Console.WriteLine("PASS native right edge and transparent upper-left corner resize and save.");
            return 0;
        }
        finally
        {
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            System.Windows.Forms.Cursor.Position = cursor;
        }
    }

    internal static void CardResize()
    {
        var area = new Rect(0, 0, 1200, 800);
        var initial = new Rect(300, 220, 330, 320);
        var right = DockWindow.ResizeCard(initial, new Vector(120, 30), 2, area);
        var corner = DockWindow.ResizeCard(initial, new Vector(-40, -70), 5, area);
        Require(right == new Rect(300, 220, 450, 320) && corner == new Rect(260, 150, 370, 390),
            "Edges must resize independently, and a corner drag must pin the opposite corner.");
        Require(DockWindow.ResizeCard(initial, new Vector(-500, -500), 5, area).TopLeft == new Point(0, 0),
            "Resize must remain inside the monitor work area.");

        using var fixture = new AppearanceFixture();
        fixture.Settings.DockWidth = 480;
        fixture.Settings.DockHeight = 270;
        var window = new DockWindow { Left = 150, Top = 130, ShowActivated = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            var list = (ItemsControl)window.FindName("DeviceList");
            Require(list.Parent is ScrollViewer { VerticalScrollBarVisibility: ScrollBarVisibility.Hidden },
                "A resized card must keep scrolling without a visible scrollbar.");
            Require(window.ActualWidth == 480 && window.ActualHeight == 270,
                "The dock must restore both custom dimensions.");
            var reset = window.CreateCardSizeMenu().Items.OfType<MenuItem>().Last();
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.UpdateLayout();
            Require(window.SizeToContent == SizeToContent.WidthAndHeight && App.Settings.DockWidth == 0 &&
                App.Settings.DockHeight == 0, "Reset must restore automatic layout.");
        }
        finally { window.Close(); }
    }

    internal static void WallpaperFraming()
    {
        var image = BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgr32, null, new byte[16 * 9 * 4], 16 * 4);
        foreach (Size size in new[] { new Size(300, 500), new Size(450, 100), new Size(320, 180) })
        {
            var source = new ImageDrawing(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            var brush = PictureBackground.CreateWallpaper(source, size, new AppSettings(), ThemePreset.ById("dusk"));
            var drawing = (DrawingGroup)brush.Drawing;
            var main = (GeometryDrawing)drawing.Children[^2];
            var cover = ((GeometryDrawing)drawing.Children[0]).Geometry.Bounds;
            Require(cover == new Rect(size) && main.Geometry.Bounds == new Rect(size) &&
                ((DrawingBrush)main.Brush).Stretch == Stretch.Fill,
                "The wallpaper must stretch across the full card without padded edges.");
            Require(((DrawingBrush)main.Brush).Viewbox == new Rect(0, 0, 1, 1),
                "The foreground must use the whole source frame.");
        }
    }

    internal static void CatalogAndMenu()
    {
        using var fixture = new AppearanceFixture();
        string root = Path.GetFullPath(Path.Combine("artifacts", "wallpaper-catalog", Guid.NewGuid().ToString("N")));
        string install = Path.Combine(root, "steamapps", "common", "wallpaper_engine");
        string workshop = Path.Combine(root, "other-library", "steamapps", "workshop", "content", "431960");
        string MakeProject(string directory, string type, string name, string file, string preview = "")
        {
            Directory.CreateDirectory(directory);
            string entry = Path.Combine(directory, file);
            if (file.EndsWith(".png")) CreateImage(entry);
            else File.WriteAllBytes(entry, [0]);
            if (preview.Length > 0) CreateImage(Path.Combine(directory, preview));
            string manifest = Path.Combine(directory, "project.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { type, title = name, file, preview }));
            return manifest;
        }
        static void CreateImage(string path)
        {
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 200, 120, 80, 255 }, 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        string photo = MakeProject(Path.Combine(install, "projects", "myprojects", "image"), "image", "测试图片", "image.png");
        string video = MakeProject(Path.Combine(workshop, "100"), "video", "测试视频", "video.mp4");
        MakeProject(Path.Combine(workshop, "101"), "scene", "测试场景", "scene.pkg", "preview.png");
        MakeProject(Path.Combine(install, "projects", "templates", "skip"), "video", "模板", "template.mp4");

        using var request = new CancellationTokenSource();
        var choices = WallpaperEngineSource.ScanCatalog([install], [Path.Combine(root, "other-library")], request.Token);
        Require(choices.Count == 3 && choices.Single(c => c.Path == photo).Kind == "静态图片" &&
            choices.Single(c => c.Path == video).Kind == "动态视频" &&
            choices.Any(c => c.Kind == "实时场景"), "Catalog must classify local and Workshop assets and exclude templates.");

        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var card = new DockWindow((_, _) => Task.FromResult<WallpaperContent?>(null)) { ShowActivated = false };
        var menu = App.CreateQuickMenu(card);
        var index = menu.Items.IndexOf(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Wallpaper Engine 壁纸")));
        menu.Items.RemoveAt(index);
        var wallpaper = card.CreateWallpaperMenu(_ => choices);
        menu.Items.Insert(index, wallpaper);
        try
        {
            card.Show();
            menu.IsOpen = true;
            wallpaper.IsSubmenuOpen = true;
            PumpUntil(() => wallpaper.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "测试图片 · 静态图片")), "Wallpaper submenu did not load.");
            var selected = wallpaper.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "测试图片 · 静态图片"));
            var presenter = (FrameworkElement)((System.Windows.Controls.Primitives.Popup)
                wallpaper.Template.FindName("PART_Popup", wallpaper)).Child;
            presenter.UpdateLayout();
            var surface = new RenderTargetBitmap((int)Math.Ceiling(presenter.ActualWidth),
                (int)Math.Ceiling(presenter.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            surface.Render(presenter);
            var capture = new PngBitmapEncoder();
            capture.Frames.Add(BitmapFrame.Create(surface));
            using (var stream = File.Create(Path.Combine("artifacts", "wallpaper-menu.png"))) capture.Save(stream);
            selected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpUntil(() => ((Border)card.Content).Background is DrawingBrush, "Selecting an image must paint the card.");
            Require(App.Settings.DockWallpaperEngineSelection == photo && !App.Settings.DockFollowWallpaperEngine,
                "A fixed wallpaper must save its own selection.");
            wallpaper.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "恢复卡片原背景"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(App.Settings.DockWallpaperEngineSelection == "" && ((Border)card.Content).Background is not DrawingBrush,
                "Restoring the original background must clear the fixed selection and repaint.");
        }
        finally
        {
            menu.IsOpen = false;
            card.Close();
            SynchronizationContext.SetSynchronizationContext(before);
        }
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

    internal static int RunPlayback(string path)
    {
        using var fixture = new AppearanceFixture();
        fixture.Settings.DockWallpaperEngineSelection = path;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var card = new DockWindow { Top = 80, Left = 80, ShowActivated = false };
        try
        {
            card.ApplyBackground("");
            card.Show();
            var root = (Border)card.Content;
            MediaPlayer? Player() =>
                (root.Background as DrawingBrush)?.Drawing is DrawingGroup group ? group.Children.OfType<GeometryDrawing>()
                    .Select(layer => (layer.Brush as DrawingBrush)?.Drawing).OfType<VideoDrawing>().FirstOrDefault()?.Player : null;
            if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                PumpUntil(() => Player()?.Position > TimeSpan.FromMilliseconds(150), "Short test video did not start.", 10000);
                var first = Player();
                PumpUntil(() => Player() is { } next && next != first, "The predecoded player was not swapped at the loop boundary.", 12000);
                var second = Player();
                PumpUntil(() => Player() == first && Player() != second, "The first player was not recycled for the next loop.", 12000);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(DockWindow).GetField("_videoRecoveryAttempts", flags)!.SetValue(card, 2);
                typeof(DockWindow).GetMethod("DeferVideoRecovery", flags)!.Invoke(card, null);
                PumpUntil(() => Player()?.Position > TimeSpan.FromMilliseconds(150), "Video must recover beyond the previous two-attempt limit.", 15000);
                card.Hide();
                card.Show();
                PumpUntil(() => Player()?.Position > TimeSpan.FromMilliseconds(200), "Video must resume after hiding.", 10000);
                Console.WriteLine("PASS two video loop boundaries, retry beyond two failures, and hide/show recovery.");
            }
            else
            {
                PumpUntil(() => root.Background is DrawingBrush, "The GIF preview did not open.", 10000);
                var first = root.Background;
                PumpUntil(() => !ReferenceEquals(first, root.Background), "The GIF preview did not advance.");
                card.Hide();
                var paused = root.Background;
                PumpUntil(() => false, null, 250);
                Require(ReferenceEquals(paused, root.Background), "The GIF timer did not pause when hidden.");
                Console.WriteLine("PASS installed scene GIF preview advances and pauses on hide.");
            }
            return 0;
        }
        finally { card.Close(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    internal static int RunSceneLive(string path)
    {
        using var trace = new System.Diagnostics.ConsoleTraceListener();
        System.Diagnostics.Trace.Listeners.Add(trace);
        using var fixture = new AppearanceFixture();
        fixture.Settings.DockWallpaperEngineSelection = path;
        fixture.Settings.DockWallpaperLiveScene = true;
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var card = new DockWindow { Left = 160, Top = 120, ShowActivated = false };
        var root = (Border)card.Content;
        WriteableBitmap? SceneFrame() =>
            ((root.Background as DrawingBrush)?.Drawing as DrawingGroup)?.Children.OfType<GeometryDrawing>()
                .Select(layer => (layer.Brush as DrawingBrush)?.Drawing)
                .OfType<ImageDrawing>().Select(image => image.ImageSource).OfType<WriteableBitmap>().FirstOrDefault();
        try
        {
            card.ApplyBackground("");
            card.Show();
            PumpUntil(() => SceneFrame() is { PixelWidth: >= 1920 }, "The original scene did not appear in the card.", 12000);
            var scene = SceneFrame()!;
            var unique = new HashSet<string>();
            for (int i = 0; i < 12; i++)
            {
                var pixels = new byte[32 * 32 * 4];
                scene.CopyPixels(new Int32Rect((scene.PixelWidth - 32) / 2, (scene.PixelHeight - 32) / 2, 32, 32), pixels, 32 * 4, 0);
                unique.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
                PumpUntil(() => false, null, 120);
            }
            Require(unique.Count >= 4, "The scene must keep animating inside the card.");
            var png = new PngBitmapEncoder();
            var preview = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            preview.Render(root);
            png.Frames.Add(BitmapFrame.Create(preview));
            using (var file = File.Create(Path.GetFullPath("scene-live-card.png"))) png.Save(file);

            var field = typeof(DockWindow).GetField("_sceneWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var window = (WallpaperEngineSource.SceneWindow)field.GetValue(card)!;
            card.Hide();
            PumpUntil(() => !IsWindow(window.Handle), "The dedicated engine window was not closed after hiding.", 5000);
            Require(SceneFrame() == null, "Hiding the card must release the captured frame.");
            card.Show();
            PumpUntil(() => SceneFrame() is { PixelWidth: >= 1920 }, "Scene must reopen at full resolution after showing the card.", 20000);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(DockWindow).GetMethod("FallBackToScenePreview", flags)!.Invoke(card, null);
            Require(SceneFrame() == null, "Capture failure must release the stale frame.");
            PumpUntil(() => SceneFrame() is { PixelWidth: >= 1920 }, "Scene must recover automatically after capture failure.", 30000);
            Console.WriteLine($"PASS original scene in card at {scene.PixelWidth}×{scene.PixelHeight} with {unique.Count} distinct samples; dedicated renderer closed on hide.");
            return 0;
        }
        finally
        {
            card.Close();
            SynchronizationContext.SetSynchronizationContext(oldContext);
            System.Diagnostics.Trace.Listeners.Remove(trace);
        }
    }

    internal static int RunLive()
    {
        var catalog = WallpaperEngineSource.ReadCatalog(CancellationToken.None);
        Require(catalog.Count > 0, "No locally installed wallpaper was discovered.");
        Console.WriteLine($"Installed catalog: {catalog.Count} entries, {catalog.Count(item => item.Kind == "动态视频")} playable videos.");
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
            ((root.Background as DrawingBrush)?.Drawing as DrawingGroup)?.Children.OfType<GeometryDrawing>()
                .Select(layer => (layer.Brush as DrawingBrush)?.Drawing).OfType<VideoDrawing>().FirstOrDefault()?.Player;
        try
        {
            card.ApplyBackground("");
            card.Show();
            PumpUntil(() => received.Task.IsCompleted, "The running Wallpaper Engine did not respond.", 12000);
            var content = received.Task.Result ?? throw new InvalidOperationException("The active Wallpaper Engine file did not resolve.");
            PumpUntil(() => root.Background is DrawingBrush, "The current live wallpaper did not resolve and render.", 12000);
            if (content.Video != null)
                PumpUntil(() => Player() != null, "The configured video did not open in the transparent WPF card.", 12000);
            if (catalog.FirstOrDefault(item => item.Kind == "动态视频") is { } video)
            {
                settings.DockWallpaperEngineSelection = video.Path;
                settings.DockFollowWallpaperEngine = false;
                card.ApplyBackground("");
                PumpUntil(() => Player() != null, "Selecting an installed video did not start playback.", 12000);
            }
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
