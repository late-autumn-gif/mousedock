using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;

internal static class ChargingVisualRegression
{
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flag);

    internal static int Run(string directory)
    {
        Exception? failure = null;
        int checks = 0;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
                void Pump(int milliseconds)
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start(); Dispatcher.PushFrame(frame);
                }
                var root = new StackPanel { Margin = new Thickness(20) };
                root.Children.Add(new TextBlock { Text = "MouseDock · 充电状态预览", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(0, 0, 0, 12) });
                var devices = new[]
                {
                    new DeviceInfo { Name = "正在充电", Level = 42, Charging = true },
                    new DeviceInfo { Name = "正常使用", Level = 65 },
                    new DeviceInfo { Name = "上次读数", Level = 42, Charging = true, IsStale = true },
                    new DeviceInfo { Name = "状态未提供", Level = -1 },
                    new DeviceInfo { Name = "设备断开", Level = 42, Charging = true, Connected = false }
                };
                var glyphs = new List<ChargingGlyph>();
                var rings = new List<DeviceBatteryRing>();
                foreach (var device in devices)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5), DataContext = device };
                    row.Children.Add(new TextBlock { Text = device.Name, Width = 105, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
                    var percent = new TextBlock { Width = 50, FontSize = 17, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                    percent.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.LevelText)));
                    row.Children.Add(percent);
                    var glyph = new ChargingGlyph { Width = 30, Height = 24 };
                    glyph.SetBinding(ChargingGlyph.ActiveProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
                    glyphs.Add(glyph); row.Children.Add(glyph);
                    var ring = new DeviceBatteryRing();
                    ring.SetBinding(DeviceBatteryRing.LevelProperty, new Binding(nameof(DeviceInfo.Level)));
                    ring.SetBinding(DeviceBatteryRing.ChargingProperty, new Binding(nameof(DeviceInfo.IsChargingActive)));
                    rings.Add(ring); row.Children.Add(ring);
                    var state = new TextBlock { Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    state.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceInfo.StatusText)));
                    row.Children.Add(state); root.Children.Add(row);
                }
                var window = new Window { Content = root, Width = 475, Height = 265, Left = -30000, Top = 0,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, Background = new SolidColorBrush(Color.FromRgb(0x18,0x1B,0x27)) };
                byte[] Capture(string name)
                {
                    var image = new RenderTargetBitmap(475, 265, 96, 96, PixelFormats.Pbgra32); image.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
                    var pixels = new byte[475 * 265 * 4]; image.CopyPixels(pixels, 475 * 4, 0); return pixels;
                }
                try
                {
                    Check(!rings[0].IsChargeSweepRunning, "An unloaded glyph must not run an animation clock");
                    window.Show(); Pump(120);
                    Check(rings[0].IsChargeSweepRunning == SystemParameters.ClientAreaAnimation && rings.Skip(1).All(g => !g.IsChargeSweepRunning), "Only a fresh connected charging device animates");
                    var first = Capture("充电动画-帧1.png"); Pump(500);
                    var second = Capture("充电动画-帧2.png");
                    Check(first.SequenceEqual(second) != SystemParameters.ClientAreaAnimation, "Charging animation must visibly advance between frames");
                    devices[0].Charging = false; Pump(50);
                    Check(!rings[0].IsChargeSweepRunning && glyphs[0].Visibility == Visibility.Collapsed, "Stopping charge removes the glyph and animation clock");
                    devices[0].Charging = true; Pump(50);
                    Check(rings[0].IsChargeSweepRunning == SystemParameters.ClientAreaAnimation, "Fresh charging can restart without reconstructing the view");
                    window.Hide(); Pump(30);
                    Check(rings.All(g => !g.IsChargeSweepRunning), "Hidden views do not retain running charging clocks");
                }
                finally { window.Close(); Pump(30); }
                Check(rings.All(g => !g.IsChargeSweepRunning), "Unloaded views release charging clocks");

                var renderer = typeof(MouseDock.App).GetMethod("RenderTrayIcon", BindingFlags.NonPublic | BindingFlags.Static)!;

                // Warm up System.Drawing before measuring native resources.
                using (var icon = (System.Drawing.Icon)renderer.Invoke(null, new object[] { 42, true, false, false })!) { }
                int before = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                for (int i = 0; i < 300; i++)
                {

                    using var icon = (System.Drawing.Icon)renderer.Invoke(null, new object[] { 42, true, false, false })!;
                    if (i < 6 && i % 2 == 0) { using var bitmap = icon.ToBitmap(); bitmap.Save(Path.Combine(directory, $"托盘充电-{i / 2 + 1}.png")); }
                }
                Check(GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= before + 2, "Continuous tray animation must not leak native icon handles");
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
        return checks;
    }
}
