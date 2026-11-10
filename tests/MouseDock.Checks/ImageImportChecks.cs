using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class ImageImportChecks
{
    internal static void Normalization()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.ImageImport");
        Require(type != null, "Image orientation and transparent-bound normalization is missing.");
        var normalize = type!.GetMethod("Normalize")!;
        byte[] source = Enumerable.Range(1, 6).SelectMany(n => new byte[] { 0, 0, (byte)(n * 30), 255 }).ToArray();
        var image = BitmapSource.Create(2, 3, 96, 96, PixelFormats.Bgra32, null, source, 8);
        int[][] expected = [[1,2,3,4,5,6], [2,1,4,3,6,5], [6,5,4,3,2,1], [5,6,3,4,1,2],
            [1,3,5,2,4,6], [5,3,1,6,4,2], [6,4,2,5,3,1], [2,4,6,1,3,5]];
        for (int orientation = 1; orientation <= 8; orientation++)
        {
            var result = (BitmapSource)normalize.Invoke(null, [image, orientation, false, null])!;
            var pixels = Pixels(result);
            Require(result.PixelWidth == (orientation <= 4 ? 2 : 3) &&
                expected[orientation - 1].SequenceEqual(Enumerable.Range(0, 6).Select(i => pixels[i * 4 + 2] / 30)),
                $"EXIF orientation {orientation} is incorrect.");
        }
        var transparent = new byte[4 * 4 * 4]; transparent[(1 * 4 + 2) * 4 + 3] = 255;
        var sparse = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, transparent, 16);
        var cropped = (BitmapSource)normalize.Invoke(null, [sparse, 1, true, null])!;
        Require(cropped.PixelWidth == 1 && cropped.PixelHeight == 1, "Photo transparent borders were retained.");
        var empty = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16);
        bool rejected = false;
        try { normalize.Invoke(null, [empty, 1, true, null]); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
        Require(rejected, "All-transparent photos are accepted.");
        var background = (BitmapSource)normalize.Invoke(null, [empty, 1, false, ThemePreset.C("#123456")])!;
        Require(Pixels(background).Chunk(4).All(p => p[0] == 0x56 && p[1] == 0x34 && p[2] == 0x12 && p[3] == 255),
            "Transparent backgrounds are not composited onto the base color.");
        Directory.CreateDirectory("artifacts/reference-interaction/images");
        string path = Path.GetFullPath("artifacts/reference-interaction/images/oriented.jpg");
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var loaded = Palette.Load(path)!;
        Require(loaded.PixelWidth == 3 && loaded.PixelHeight == 2, "Normal image loading ignores EXIF rotation.");
    }

    internal static void BackgroundPreview()
    {
        using var fixture = new AppearanceFixture();
        var dock = new DockWindow();
        fixture.Set("Dock", dock);
        var root = (FrameworkElement)dock.Content;
        root.Measure(new Size(268, 420)); root.Arrange(new Rect(0, 0, 268, 420));
        fixture.Settings.BackgroundZoom = 2; fixture.Settings.BackgroundPositionX = .8; fixture.Settings.BackgroundDepth = .7;
        var editor = new BackgroundEditorWindow(fixture.Settings);
        try
        {
            var preview = editor.FindName("BackgroundPreview") as FrameworkElement;
            Require(preview != null && Math.Abs(preview.Width / preview.Height - root.ActualWidth / root.ActualHeight) < .001,
                "Background preview does not match the main window aspect.");
            var reset = typeof(BackgroundEditorWindow).GetMethod("ResetComposition");
            Require(reset != null, "Background editor cannot reset composition.");
            reset!.Invoke(editor, null);
            Require(editor.Draft.BackgroundZoom == 1 && editor.Draft.BackgroundPositionX == .5 &&
                editor.Draft.BackgroundPositionY == .5 && editor.Draft.BackgroundDepth == .35 &&
                fixture.Settings.BackgroundZoom == 2, "Reset failed or edited the original settings.");
        }
        finally { editor.Close(); }
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
