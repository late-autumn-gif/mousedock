using System.Reflection;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--audit"))
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            return AuditChecks.Run();
        }
        var checks = new (string Name, Action Check)[]
        {
            ("New settings open the taskbar surface", () =>
                Require(new AppSettings().SurfaceMode == "taskbar", "Expected the taskbar surface.")),
            ("Taskbar placement stays inside a scaled taskbar", CheckPlacement),
            ("Taskbar placement clamps offset and supports vertical taskbars", CheckEdges),
            ("Battery ring preserves transparency and renders full/empty/unknown distinctly", CheckRing),
            ("Each connected selected device keeps its own glyph and battery", CheckDevices),
            ("Saved selection survives asynchronous discovery", CheckSelection),
            ("Maximized apps do not hide a revealed auto-hide taskbar", CheckFullscreen),
            ("Error report lands in the folder the user picked", CheckReportExportPath),
            ("Error report defaults to the app's own folder", CheckReportDefaultFolder),
            ("Mini battery text updates without rebuilding devices", AuditChecks.MiniLiveReading),
            ("Settings can reopen after a system close", AuditChecks.ClosedSettings),
            ("Merged channels preserve charging status", AuditChecks.ChargingMerge),
            ("Failed polls preserve the last reading and recover", AuditChecks.CachedReading),
            ("Unknown devices respect the chosen poll interval", AuditChecks.PollInterval),
            ("All five mini cells stay inside the window", AuditChecks.FifthMiniCell),
            ("Clearing custom backgrounds restores the theme", AuditChecks.ClearBackground),
            ("Taskbar activation cannot permanently cover the rings", TaskbarRegressionChecks.Activation),
        };
        int failures = 0;
        foreach (var (name, check) in checks)
        {
            try { check(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
        }
        Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Type ProductionType(string name)
    {
        var type = typeof(AppSettings).Assembly.GetType(name);
        Require(type != null, $"Missing {name}.");
        return type!;
    }

    private static Rect Place(Rect taskbar, uint edge, Size size, double offset, double dpi)
    {
        var method = ProductionType("MouseDock.Services.TaskbarPlacement").GetMethod("Calculate")!;
        return (Rect)method.Invoke(null, [taskbar, edge, size, offset, dpi])!;
    }

    private static void CheckPlacement()
    {
        // 80x40 DIPs at 150% is 120x60 physical pixels, centered in a 72px taskbar.
        var result = Place(new Rect(0, 1008, 1920, 72), 3, new Size(80, 40), 160, 1.5);
        Require(result == new Rect(240, 1014, 120, 60), $"Unexpected placement: {result}");
    }

    private static void CheckEdges()
    {
        var bottom = Place(new Rect(-1920, 1040, 1920, 40), 3, new Size(80, 40), 9999, 1);
        Require(bottom == new Rect(-80, 1040, 80, 40), "Offset must clamp to the monitor edge.");
        var left = Place(new Rect(-48, 0, 48, 1080), 0, new Size(80, 40), 16, 1);
        Require(left == new Rect(-44, 984, 40, 80), "Vertical taskbars need a vertical strip near the bottom.");
        var small = Place(new Rect(0, 0, 320, 24), 1, new Size(80, 40), 0, 1);
        Require(small == new Rect(0, 0, 48, 24), "A small taskbar must scale the whole strip.");
    }

    private static byte[] RenderRing(int level)
    {
        var type = ProductionType("MouseDock.Controls.DeviceBatteryRing");
        var control = (FrameworkElement)Activator.CreateInstance(type)!;
        type.GetProperty("Level")!.SetValue(control, level);
        type.GetProperty("Kind")!.SetValue(control, DeviceKind.Keyboard);
        control.Measure(new Size(40, 40));
        control.Arrange(new Rect(0, 0, 40, 40));
        control.UpdateLayout();
        var bitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var pixels = new byte[40 * 40 * 4];
        bitmap.CopyPixels(pixels, 160, 0);
        return pixels;
    }

    private static void CheckRing()
    {
        var full = RenderRing(100);
        var empty = RenderRing(0);
        var unknown = RenderRing(-1);
        Require(full[3] == 0 && full[(39 * 40 + 39) * 4 + 3] == 0,
            "The background corners must remain fully transparent.");
        Require(!full.SequenceEqual(empty), "Full and empty battery must look different.");
        Require(!empty.SequenceEqual(unknown), "Unknown battery must not look like a measured zero.");
        Require(full[(20 * 40 + 20) * 4 + 3] > 0, "The keyboard glyph must render inside the ring.");
    }

    private static void CheckDevices()
    {
        using var battery = new BatteryService(new AppSettings { Provider = "json", JsonPath = "nonexistent-test-devices.json" });
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        battery.Devices.Add(new DeviceInfo { Name = "Mouse fixture", Kind = DeviceKind.Mouse, Level = 75 });
        battery.Devices.Add(new DeviceInfo { Name = "Keyboard fixture", Kind = DeviceKind.Keyboard, Level = 30 });
        battery.Devices.Add(new DeviceInfo { Name = "Disconnected", Connected = false });
        battery.Devices.Add(new DeviceInfo { Name = "Deselected", Selected = false });
        battery.VisibleDevices.Add(battery.Devices[0]);
        battery.VisibleDevices.Add(battery.Devices[1]);
        var window = new TaskbarWindow();
        try
        {
            var strip = (StackPanel)((Viewbox)window.Content).Child;
            var rings = strip.Children.Cast<DeviceBatteryRing>().ToArray();
            Require(rings.Length == 2, "Only selected connected devices may appear.");
            Require(rings[0].Kind == DeviceKind.Mouse && rings[0].Level == 75, "Mouse ring lost its identity.");
            Require(rings[1].Kind == DeviceKind.Keyboard && rings[1].Level == 30, "Keyboard ring lost its identity.");
            Require(rings.All(r => r.Children.Count == 1 && r.Children[0] is MouseDock.Controls.DeviceGlyph),
                "Each ring should contain only its device glyph, with no numeric label.");
            window.Show();
            window.UpdateLayout();
            window.UpdateAnchor();
            Console.WriteLine($"Native taskbar window: {window.Left:0},{window.Top:0} {window.ActualWidth:0}x{window.ActualHeight:0}, opacity={window.Opacity:0}.");
            Require(window.ActualWidth > 0 && window.ActualHeight > 0, "The native taskbar window must have a usable size.");
            battery.Devices[0].Level = 12;
            window.UpdateDevices();
            Require(((DeviceBatteryRing)strip.Children[0]).Level == 12, "Battery changes must refresh.");

            strip.Measure(new Size(80, 40));
            strip.Arrange(new Rect(0, 0, 80, 40));
            strip.UpdateLayout();
            var bitmap = new RenderTargetBitmap(320, 160, 384, 384, PixelFormats.Pbgra32);
            bitmap.Render(strip);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.GetFullPath("artifacts");
            Directory.CreateDirectory(directory);
            using var file = File.Create(Path.Combine(directory, "taskbar-rings-test.png"));
            encoder.Save(file);

            battery.Devices[0].Selected = false;
            battery.Devices[1].Selected = false;
            battery.VisibleDevices.Clear();
            window.UpdateDevices();
            Require(strip.Children.Count == 0, "No selection must not display a fabricated device.");
        }
        finally { window.Close(); }
    }

    private static void CheckSelection()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """[{"id":"mouse","name":"Mouse","kind":"mouse","level":60},{"id":"keyboard","name":"Keyboard","kind":"keyboard","level":80}]""");
            foreach (bool emptySelection in new[] { false, true })
            {
                var settings = new AppSettings
                {
                    Provider = "json", JsonPath = path, HasDeviceSelection = true,
                    SelectedDeviceIds = emptySelection ? [] : ["keyboard"]
                };
                using var battery = new BatteryService(settings);
                var frame = new DispatcherFrame();
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timeout.Tick += (_, _) => frame.Continue = false;
                battery.Refreshed += (_, _) => frame.Continue = false;
                timeout.Start();
                Dispatcher.PushFrame(frame);
                timeout.Stop();
                Require(battery.Devices.Count == 2, "Discovery did not finish.");
                Require(!battery.Devices[0].Selected, "An unselected mouse must stay unselected after discovery.");
                Require(battery.Devices[1].Selected == !emptySelection, "Explicitly empty selection must persist.");
            }
        }
        finally { File.Delete(path); }
    }

    private static void CheckFullscreen()
    {
        var method = typeof(TaskbarPlacement).GetMethod("IsFullscreen");
        Require(method != null, "Missing distinction between maximized and fullscreen windows.");
        var monitor = new Rect(0, 0, 1920, 1080);
        bool Evaluate(Rect window, bool maximized, string className) =>
            (bool)method!.Invoke(null, [window, monitor, maximized, className])!;
        Require(!Evaluate(monitor, true, "OrdinaryApp"), "A maximized app must not hide taskbar rings.");
        Require(Evaluate(monitor, false, "GameWindow"), "A fullscreen app must hide taskbar rings.");
        Require(!Evaluate(monitor, false, "WorkerW"), "The desktop must not hide taskbar rings.");
        Require(!Evaluate(new Rect(0, 0, 1920, 1000), false, "OrdinaryApp"), "A normal window must not hide rings.");
    }

    // ── 错误报告落在哪里 ──
    // 要的行为：报告不往 C 盘 AppData 里塞；导出时问用户放哪，默认就是 exe 同目录。

    private static void CheckReportExportPath()
    {
        var service = ProductionType("MouseDock.Services.FeedbackService");
        var export = service.GetMethod("ExportReport", BindingFlags.Public | BindingFlags.Static)!;

        string root = Path.Combine(Path.GetTempPath(), "mousedock-report-check-" + Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(root, "chosen", "MouseDock-report-test.zip");
        try
        {
            var result = export.Invoke(null, [new AppSettings(), new List<DeviceInfo>(), zipPath]) as string;

            Require(result == Path.GetFullPath(zipPath), $"The export must write where it was told, got {result ?? "null"}.");
            Require(File.Exists(zipPath), "The export must create the zip at the chosen path.");

            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries.Select(e => e.FullName).ToHashSet();
            Require(entries.Contains("summary.txt") && entries.Contains("hid-devices.txt"),
                "The zip must carry the generated reports.");
            Require(Directory.GetDirectories(root, "work-*").Length == 0,
                "The export must not leave a scratch folder behind.");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void CheckReportDefaultFolder()
    {
        var service = ProductionType("MouseDock.Services.FeedbackService");
        string appFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        string exeDir = (string)service.GetMethod("ExeDir", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
        Require(PathsEqual(exeDir, appFolder), $"The report folder must be the app's own folder, got {exeDir}.");

        string defaultDir = (string)service.GetProperty("DefaultExportDir", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        string defaultPath = (string)service.GetProperty("DefaultExportPath", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        Require(Directory.Exists(defaultDir), $"The default report folder must exist, got {defaultDir}.");
        Require(Path.GetExtension(defaultPath).Equals(".zip", StringComparison.OrdinalIgnoreCase),
            $"The suggested report name must be a zip, got {defaultPath}.");
        Require(PathsEqual(Path.GetDirectoryName(defaultPath)!, defaultDir),
            "The suggested report path must sit inside the default folder.");

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock");
        Require(!defaultDir.StartsWith(appData, StringComparison.OrdinalIgnoreCase),
            $"The report must not default into {appData}.");

        // 能写就必须用 exe 旁边这个目录；写不进去才退到「文档」
        if (CanWrite(appFolder))
            Require(PathsEqual(defaultDir, appFolder), $"A writable app folder must win, got {defaultDir}.");
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    private static bool CanWrite(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, ".check-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
