using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace MouseDock.Services;

internal sealed record WallpaperAnimation(BitmapSource[] Frames, int[] Delays)
{
    internal static WallpaperAnimation Read(string path)
    {
        if (!Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 16 * 1024 * 1024) return null;
            using var source = Drawing.Image.FromStream(stream);
            if ((long)source.Width * source.Height > 8_500_000) return null;
            var dimension = FrameDimension.Time;
            int count = source.GetFrameCount(dimension);
            if (count is < 2 or > 500) return null;
            double scale = Math.Min(1, Math.Sqrt((48d * 1024 * 1024) / (count * 4d * source.Width * source.Height)));
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));
            var frames = new BitmapSource[count];
            var delays = new int[count];
            var metadata = Array.IndexOf(source.PropertyIdList, 0x5100) >= 0
                ? source.GetPropertyItem(0x5100)?.Value : null;
            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var bounds = new Drawing.Rectangle(0, 0, width, height);
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < count; i++)
            {
                source.SelectActiveFrame(dimension, i);
                using (var canvas = Graphics.FromImage(bitmap))
                {
                    canvas.Clear(Drawing.Color.Transparent);
                    canvas.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    canvas.DrawImage(source, bounds);
                }
                var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * width * 4, width * 4);
                }
                finally { bitmap.UnlockBits(data); }

                var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, bytes, width * 4);
                frame.Freeze();
                frames[i] = frame;
                int delay = metadata?.Length >= (i + 1) * 4 ? BitConverter.ToInt32(metadata, i * 4) : 10;
                delays[i] = delay >= 2 ? Math.Min(delay, 1000) * 10 : 100;
            }
            return new WallpaperAnimation(frames, delays);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }
}
