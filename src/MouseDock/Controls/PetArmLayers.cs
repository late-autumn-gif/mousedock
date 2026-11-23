using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseDock.Controls;

// Horizontal silhouettes traced in a 400-unit atlas cell. The shoulder stays pinned;
// below the cuff the mouse, fingers and palm move together as one rigid piece.
internal sealed class PetArmLayers
{
    internal record struct Span(int Y, int Left, int Right);
    internal sealed record Rig(int DeskY, int WristY, Point Tip, Span[] Mouse, Span[] Keys, bool OpenSleeveEdge = true, bool HasChair = false);
    internal static Rig For(string skin) => skin switch
    {
        "rikka" => new(333, 311, new(261,341),
            [new(259,116,141),new(278,94,135),new(302,64,128),new(318,55,125),new(334,61,121),new(348,64,116),new(367,67,114),new(374,74,109)],
            [new(260,263,282),new(278,259,305),new(306,252,330),new(325,251,332),new(333,247,317),new(341,250,306),new(346,263,300)]),
        "keqing" => new(354, 333, new(292,352),
            [new(292,139,160),new(314,121,164),new(330,80,157),new(348,64,135),new(359,61,124),new(379,64,120),new(382,77,110)],
            [new(294,272,288),new(314,264,304),new(331,267,328),new(349,275,333),new(359,287,327)], OpenSleeveEdge: false),
        "xin-yue-hu" => new(321, 303, new(262,325),
            [new(255,110,151),new(279,90,156),new(301,80,153),new(320,78,145),new(330,81,137),new(354,79,131),new(367,85,120)],
            [new(255,259,285),new(278,256,302),new(298,250,313),new(314,244,310),new(323,248,304),new(331,258,288)], OpenSleeveEdge: false),
        _ => new(321, 303, new(298,332),
            [new(258,132,157),new(272,111,160),new(282,100,157),new(303,84,146),new(317,72,137),new(332,67,137),new(347,68,137),new(363,69,136),new(372,80,127)],
            [new(288,297,303),new(293,288,310),new(301,281,319),new(309,277,335),new(317,277,344),new(326,282,347),new(333,289,345),new(337,300,336)], OpenSleeveEdge: false, HasChair: true)
    };

    internal BitmapSource Base { get; }
    internal BitmapSource Mouse { get; }
    internal BitmapSource Keys { get; }
    internal PetArmLayers(BitmapSource pose, Rig rig, BitmapSource stationaryDesk = null)
    {
        // A fixed-resolution rig also keeps skin swaps bounded at high display DPI.
        const int size = 512;
        var scaled = new TransformedBitmap(pose, new ScaleTransform((double)size / pose.PixelWidth, (double)size / pose.PixelHeight));
        var src = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
        byte[] original = new byte[size * size * 4];
        src.CopyPixels(original, size * 4, 0);
        byte[] back = (byte[])original.Clone();
        BitmapSource Bitmap(byte[] data)
        {
            var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, data, size * 4);
            bmp.Freeze();
            return bmp;
        }

        BitmapSource Cut(Span[] spans, bool mouse)
        {
            var layer = new byte[back.Length];
            for (int y = 0; y < size; y++)
            {
                double v = (y + .5) * 400 / size;
                if (v < spans[0].Y || v >= spans[^1].Y) continue;
                int j = 1;
                while (spans[j].Y < v) j++;
                var a = spans[j - 1]; var b = spans[j];
                double t = (v - a.Y) / (b.Y - a.Y);
                int left = (int)Math.Floor((a.Left + t * (b.Left - a.Left)) * size / 400);
                int right = (int)Math.Ceiling((a.Right + t * (b.Right - a.Right)) * size / 400);
                bool aboveDesk = v < rig.DeskY - 1;
                if (aboveDesk && rig.OpenSleeveEdge)
                {
                    // Include the antialiased outer sleeve edge on characters with open air behind the arm.
                    if (mouse)
                        while (left > 4 && original[(y * size + left - 1) * 4 + 3] > 0) left--;
                    else
                        while (right < size - 5 && original[(y * size + right + 1) * 4 + 3] > 0) right++;
                }
                int offset = (y * size + left) * 4;
                Buffer.BlockCopy(original, offset, layer, offset, (right-left+1)*4);
                // Continue the stationary row across the area normally hidden by the limb.
                // Above the desktop use the nearer side to retain silhouette transparency.
                for (int x = left; x <= right; x++)
                {
                    double f = (x-left+1d)/(right-left+2);

                    int i = (y * size + x) * 4;
                    if (aboveDesk)
                    {
                        // Continue the torso only as far as the shoulder silhouette. Sampling
                        // the outer sleeve horizontally would leave stripes in transparent air.
                        double depth = (v - spans[0].Y) / (rig.DeskY - spans[0].Y);
                        double edge = mouse ? spans[0].Right - 6 * depth : spans[0].Left + 9 * depth;
                        bool inside = mouse ? x >= edge * size / 400 : x <= edge * size / 400;
                        int sample = mouse ? right + 3 : left - 3;
                        if (inside)
                            Buffer.BlockCopy(original, (y * size + sample) * 4, back, i, 4);
                        else if (rig.HasChair && v < 316 && x * 400d / size is > 88 and < 312)
                        {
                            // The red-shirt skin has a dark chair behind both sleeves.
                            Buffer.BlockCopy(original, ((int)(257d * size / 400) * size + (int)(309d * size / 400)) * 4,
                                back, i, 4);
                        }
                        else Array.Clear(back, i, 4);
                        continue;
                    }
                    for (int c = 0; c < 4; c++)
                        back[i+c] = (byte)Math.Round(original[offset-4+c]*(1-f) + original[(y*size+right+1)*4+c]*f);
                }
            }
            return Bitmap(layer);
        }
        Mouse = Cut(rig.Mouse, true);
        Keys = Cut(rig.Keys, false);
        if (stationaryDesk != null)
        {
            int deskTop = (int)Math.Ceiling(rig.DeskY * size / 400d);
            var desk = new Int32Rect(0, deskTop, size, size - deskTop);
            stationaryDesk.CopyPixels(desk, back, size * 4, deskTop * size * 4);
        }
        Base = Bitmap(back);
    }
}
