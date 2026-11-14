using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class MchoseFeedbackUiChecks
{
    internal static void Run(string? report = null, string? output = null)
    {
        byte[] frame;
        if (report != null)
        {
            using var zip = ZipFile.OpenRead(report);
            using var stream = zip.GetEntry(BatteryReplay.EntryName)!.Open();
            var capture = JsonSerializer.Deserialize<BatteryReplay.Capture>(stream)!;
            var query = capture.Queries.Single(q => q.Protocol == "ExtraKeyboard:MchoseBy");
            var samples = query.Samples.Where(s => s.Stage == "keyboard-input" && s.Ok).ToArray();
            if (samples.Length != 2) throw new Exception("Report requires two captured keyboard responses");
            foreach (var sample in samples) KeyboardExpansionMchoseBy.Decode(Convert.FromHexString(sample.Hex!));
            frame = Convert.FromHexString(samples[^1].Hex!);
        }
        else frame = Convert.FromHexString("134A0100026401000000000000000000000000C5");
        var reading = KeyboardExpansionMchoseBy.Decode(frame);
        using var fixture = new AppearanceFixture();
        // Exercise the real polling/selection collection path, not direct VisibleDevices insertion.
        using var service = new BatteryService(fixture.Settings, new ReportedSource(reading), false);
        fixture.Set("Battery", service);
        Wait(service.PollAsync());
        var device = service.Devices.Single(d => d.Id == "reported-keyboard");
        var headset = service.Devices.Single(d => d.Id == "reported-v9");
        if (service.VisibleDevices.Count != 2 || !device.IsDisplayEligible || !headset.IsDisplayEligible)
            throw new Exception("Polling or default selection lost a reported device");
        var window = new DockWindow { ShowActivated = false, Left = -2000 };
        fixture.Set("Dock", window);
        window.Show(); window.ApplyTheme(MouseDock.App.Theme, false); window.UpdateLayout();
        window.BeginAnimation(UIElement.OpacityProperty, null); window.Opacity = 1;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        IEnumerable<TextBlock> Texts(DependencyObject element)
        {
            if (element is TextBlock text) yield return text;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Texts(VisualTreeHelper.GetChild(element,i))) yield return child;
        }
        var pct = Texts(window).Single(t => t.Name == "Pct" && t.DataContext == device);
        if (!pct.IsVisible || pct.Opacity <= 0 || pct.Text != device.LevelText || !pct.Text.Contains(reading.Level.ToString()))
            throw new Exception("Valid reported keyboard battery was hidden by the normal card binding");
        if (device.Charging) throw new Exception("Low-nibble full flag became charging");
        var unknown = Texts(window).Single(t => t.Name == "Pct" && t.DataContext == headset);
        if (!unknown.IsVisible || unknown.Text != "—")
            throw new Exception("Unknown V9 battery hid the device instead of showing a dash");
        if (output != null)
        {
            var root = (FrameworkElement)window.Content;
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
            bmp.Render(root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(output); png.Save(file);
        }
        Console.WriteLine($"Captured keyboard response -> BatteryService.PollAsync -> selection -> visible card: {pct.Text}; charging={device.Charging}; V9 unavailable -> {unknown.Text}");
    }

    private sealed class ReportedSource(ReferenceBatteryProtocol.Reading reading) : IBatterySource
    {
        public string Key => "reported-offline";
        public string Label => "Reported offline input";
        private readonly AutoDetectSource source = new();
        public IEnumerable<DeviceInfo> Read() => source.UpdateFromDetection(new()
        {
            new() { Id = "reported-keyboard", Name = "MCHOSE 2.4G Wireless", Kind = DeviceKind.Keyboard,
                Connected = true, Level = reading.Level, Charging = reading.Charging },
            // The supplied V9 report has no battery input. Never fabricate its percentage.
            new() { Id = "reported-v9", Name = "MCHOSE V9 PRO", Kind = DeviceKind.Headset, Connected = true, Level = -1 }
        });
    }

    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            timeout.Stop();
        }
        if (!task.IsCompleted) throw new TimeoutException("Offline service polling did not finish");
        task.GetAwaiter().GetResult();
    }
}
