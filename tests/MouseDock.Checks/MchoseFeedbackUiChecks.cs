using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
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
        fixture.Battery.VisibleDevices.Clear();
        var device = new DeviceInfo { Id = "reported-keyboard", Name = "MCHOSE 2.4G Wireless",
            Kind = DeviceKind.Keyboard, Connected = true, Level = reading.Level, Charging = reading.Charging };
        fixture.Battery.VisibleDevices.Add(device);
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
        if (output != null)
        {
            var root = (FrameworkElement)window.Content;
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
            bmp.Render(root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(output); png.Save(file);
        }
        Console.WriteLine($"Captured keyboard response -> visible card: {pct.Text}; charging={device.Charging}");
    }
}
