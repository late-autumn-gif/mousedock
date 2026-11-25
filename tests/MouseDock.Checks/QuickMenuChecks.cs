using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;

internal static class QuickMenuChecks
{
    internal static void Run()
    {
        using var fixture = new AppearanceFixture();
        string[] modes = ["dock", "mini", "icon", "taskbar", "edge", "pet"];
        string[] labels = ["主面板", "迷你窗", "悬浮图标", "任务栏电量", "屏幕右侧电量", "桌宠", "设置…", "退出"];
        var owner = new Window { Width = 200, Height = 100, ShowInTaskbar = false, ShowActivated = false };
        owner.Show();
        try
        {
            foreach (string theme in new[] { "dusk", "cloud" })
            {
                fixture.Set("Theme", ThemePreset.ById(theme));
                for (int index = 0; index < modes.Length; index++)
                {
                    fixture.Settings.SurfaceMode = modes[index];
                    var menu = MouseDock.App.CreateQuickMenu(owner);
                    var items = menu.Items.OfType<MenuItem>().ToArray();
                    if (!labels.SequenceEqual(items.Select(item => (string)item.Header)) ||
                        menu.Items.Count != labels.Length + 1 || menu.Items[6] is not Separator ||
                        !items.Take(modes.Length).All(item => item.IsCheckable) ||
                        !items.Where(item => item.IsChecked).SequenceEqual([items[index]]))
                        throw new InvalidOperationException("Quick menu entries or active mode are incorrect.");

                    if (index != 3) continue;
                    menu.IsOpen = true;
                    try
                    {
                        TaskbarEmbeddingChecks.Pump(100);
                        menu.UpdateLayout();
                        var markedRow = items[index];
                        if (PresentationSource.FromVisual(markedRow) == null ||
                            markedRow.Template.FindName("Check", markedRow) is not FrameworkElement { IsVisible: true } ||
                            menu.ActualHeight <= 0 || menu.ActualHeight > 320)
                            throw new InvalidOperationException("Quick menu did not open with the compact template and visible checkmark.");
                        markedRow.Focus();
                        TaskbarEmbeddingChecks.Pump(60);
                        var dpi = VisualTreeHelper.GetDpi(menu);
                        var bitmap = new RenderTargetBitmap(
                            (int)Math.Ceiling(menu.ActualWidth * dpi.DpiScaleX),
                            (int)Math.Ceiling(menu.ActualHeight * dpi.DpiScaleY),
                            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        bitmap.Render(menu);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        Directory.CreateDirectory("artifacts");
                        using var output = File.Create(Path.Combine("artifacts", $"quick-menu-{theme}.png"));
                        encoder.Save(output);
                    }
                    finally { menu.IsOpen = false; }
                }
            }
        }
        finally { owner.Close(); }
    }
}
