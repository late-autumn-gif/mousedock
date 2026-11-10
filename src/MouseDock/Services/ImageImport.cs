using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseDock.Services;

public static class ImageImport
{
    private const long MaximumBytes = 32L * 1024 * 1024;
    private const long MaximumPixels = 25_000_000;

    public static BitmapSource Normalize(BitmapSource source, int orientation, bool photo, Color? background)
    {
        int width = source.PixelWidth, height = source.PixelHeight;
        if (width < 1 || height < 1 || (long)width * height > MaximumPixels)
            throw new InvalidOperationException("图片像素不能超过 2500 万。");
        var rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(width * height * 4)];
        rgba.CopyPixels(pixels, width * 4, 0);
        if (orientation is < 1 or > 8) orientation = 1;
        int outputWidth = orientation >= 5 ? height : width;
        int outputHeight = orientation >= 5 ? width : height;
        var output = orientation == 1 ? pixels : new byte[pixels.Length];
        if (orientation != 1)
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var at = orientation switch
                {
                    2 => (width - 1 - x, y), 3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y), 5 => (y, x), 6 => (height - 1 - y, x),
                    7 => (height - 1 - y, width - 1 - x), 8 => (y, width - 1 - x), _ => (x, y)
                };
                Buffer.BlockCopy(pixels, (y * width + x) * 4, output, (at.Item2 * outputWidth + at.Item1) * 4, 4);
            }
        int left = outputWidth, top = outputHeight, right = 0, bottom = 0;
        if (photo)
            for (int y = 0; y < outputHeight; y++)
            for (int x = 0; x < outputWidth; x++)
                if (output[(y * outputWidth + x) * 4 + 3] != 0)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        if (photo && (right <= left || bottom <= top))
            throw new InvalidOperationException("照片完全透明，请选择包含可见内容的图片。");
        if (background is Color fill && !photo)
            for (int i = 0; i < output.Length; i += 4)
            {
                double alpha = output[i + 3] / 255d;
                output[i] = (byte)Math.Round(output[i] * alpha + fill.B * (1 - alpha));
                output[i + 1] = (byte)Math.Round(output[i + 1] * alpha + fill.G * (1 - alpha));
                output[i + 2] = (byte)Math.Round(output[i + 2] * alpha + fill.R * (1 - alpha));
                output[i + 3] = 255;
            }
        BitmapSource result = BitmapSource.Create(outputWidth, outputHeight, 96, 96, PixelFormats.Bgra32, null, output, outputWidth * 4);
        if (photo && (left != 0 || top != 0 || right != outputWidth || bottom != outputHeight))
            result = new CroppedBitmap(result, new Int32Rect(left, top, right - left, bottom - top));
        result.Freeze();
        return result;
    }

    private static int Orientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return 1;
        foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            try
            {
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is object value)
                    return Convert.ToInt32(value);
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        return 1;
    }

    private static BitmapSource Decode(Stream input, bool photo, Color? background)
    {
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        return Normalize(frame, Orientation(frame), photo, background);
    }

    public static BitmapSource Load(string path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumBytes) return null;
            using var input = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > MaximumPixels) return null;
            int orientation = Orientation(frame);
            int displayedWidth = orientation >= 5 ? frame.PixelHeight : frame.PixelWidth;
            if (decodePixelWidth > 0 && displayedWidth > decodePixelWidth)
            {
                input.Position = 0;
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.StreamSource = input;
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (orientation >= 5) thumbnail.DecodePixelHeight = decodePixelWidth;
                else thumbnail.DecodePixelWidth = decodePixelWidth;
                thumbnail.EndInit();
                return Normalize(thumbnail, orientation, false, null);
            }
            return Normalize(frame, orientation, false, null);
        }
        catch { return null; }
    }

    public static string Import(string path, string directory, bool photo, Color? background = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumBytes)
            throw new InvalidOperationException("请选择不超过 32 MB 的图片。");
        using var input = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(input));
        input.Position = 0;
        var image = Decode(input, photo, photo ? null : background ?? Color.FromRgb(20, 23, 25));
        double factor = Math.Min(1, 2048d / Math.Max(image.PixelWidth, image.PixelHeight));
        if (factor < 1)
        {
            image = new TransformedBitmap(image, new ScaleTransform(factor, factor));
            image.Freeze();
        }
        Directory.CreateDirectory(directory);
        string suffix = photo ? "-photo-v2" : "-background-" + Palette.ToHex(background ?? Color.FromRgb(20, 23, 25))[1..] + "-v2";
        string destination = Path.Combine(directory, hash + suffix + ".png");
        if (File.Exists(destination)) return destination;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = File.Create(temporary)) encoder.Save(output);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }
}
