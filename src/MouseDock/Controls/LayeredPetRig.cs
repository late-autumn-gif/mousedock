using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;

namespace MouseDock.Controls;

internal sealed class LayeredPetRig
{
    private enum Bone { Hair, Head, Body, NearArm, FarArm, NearLeg, FarLeg, Waist, Umbrella }
    private sealed record Binding(Bone Bone, Int32Rect Source, Rect Destination, Point Pivot);
    private sealed record Layer(Bone Bone, BitmapSource Image, Rect Destination, Point Pivot);
    private const double ArtSize = 320;
    private readonly Layer[] _layers;
    private readonly Point _nearArm;
    private static readonly Dictionary<string, LayeredPetRig> Assets = new();

    // Target coordinates and pivots are registered to each character's original 320px standing cell.
    private static Binding Part(Bone bone, int l, int t, int r, int b, int x, int y, int w, int h, int px, int py) =>
        new(bone, new Int32Rect(l, t, r - l, b - t), new Rect(x, y, w, h), new Point(px, py));

    private static Binding[] Bindings(string skin) => skin switch
    {
        "shin-chan" =>
        [
            Part(Bone.FarLeg, 550, 930, 704, 1137, 151, 237, 51, 66, 171, 243),
            Part(Bone.NearLeg, 140, 927, 289, 1137, 99, 237, 51, 66, 130, 243),
            Part(Bone.Body, 509, 173, 758, 414, 104, 167, 100, 86, 155, 208),
            Part(Bone.FarArm, 542, 548, 687, 780, 179, 177, 31, 67, 186, 184),
            Part(Bone.NearArm, 139, 550, 285, 784, 91, 177, 34, 69, 117, 183),
            Part(Bone.Head, 69, 66, 418, 439, 83, 21, 153, 161, 157, 173)
        ],
        "keqing" =>
        [
            Part(Bone.Hair, 871, 48, 1200, 441, 78, 30, 163, 174, 167, 62),
            Part(Bone.FarLeg, 556, 843, 675, 1208, 165, 177, 35, 126, 179, 188),
            Part(Bone.NearLeg, 157, 843, 280, 1208, 119, 178, 45, 125, 146, 188),
            Part(Bone.FarArm, 550, 489, 729, 786, 178, 115, 35, 79, 184, 124),
            Part(Bone.Body, 502, 93, 765, 413, 112, 103, 102, 128, 162, 145),
            Part(Bone.NearArm, 122, 496, 333, 792, 104, 115, 60, 80, 153, 124),
            Part(Bone.Waist, 925, 505, 1138, 757, 153, 141, 55, 72, 178, 148),
            Part(Bone.Head, 68, 76, 402, 373, 104, 21, 117, 100, 164, 112)
        ],
        "rikka" =>
        [
            Part(Bone.Hair, 892, 43, 1215, 398, 95, 21, 136, 128, 159, 74),
            Part(Bone.FarLeg, 573, 867, 678, 1210, 158, 215, 31, 88, 174, 226),
            Part(Bone.NearLeg, 168, 865, 276, 1211, 130, 215, 31, 88, 145, 226),
            Part(Bone.FarArm, 161, 502, 355, 785, 183, 147, 35, 77, 188, 157),
            Part(Bone.Body, 505, 118, 788, 413, 117, 137, 85, 96, 159, 183),
            Part(Bone.Umbrella, 976, 459, 1095, 819, 88, 179, 33, 123, 114, 159),
            Part(Bone.NearArm, 481, 505, 729, 725, 94, 147, 52, 53, 134, 156),
            Part(Bone.Head, 47, 39, 423, 399, 94, 21, 137, 128, 157, 140)
        ],
        "xin-yue-hu" =>
        [
            Part(Bone.Hair, 966, 40, 1423, 382, 31, 84, 255, 218, 185, 125),
            Part(Bone.FarLeg, 671, 762, 759, 1061, 207, 238, 24, 65, 220, 244),
            Part(Bone.NearLeg, 208, 762, 302, 1057, 182, 238, 25, 65, 196, 244),
            Part(Bone.Body, 581, 94, 884, 380, 128, 144, 142, 152, 198, 196),
            Part(Bone.NearArm, 618, 487, 797, 721, 125, 163, 80, 114, 187, 173),
            Part(Bone.Head, 96, 7, 435, 360, 96, 20, 160, 151, 195, 160),
            Part(Bone.FarArm, 141, 420, 391, 719, 187, 144, 85, 111, 226, 173),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(skin))
    };

    private LayeredPetRig(string skin)
    {
        var sheet = new BitmapImage(new Uri($"pack://application:,,,/MouseDock;component/Assets/Characters/RigParts/{skin}.png"));
        _layers = Bindings(skin).Select(binding =>
        {
            var image = new CroppedBitmap(sheet, binding.Source);
            image.Freeze();
            return new Layer(binding.Bone, image, binding.Destination, binding.Pivot);
        }).ToArray();
        _nearArm = _layers.Single(layer => layer.Bone == Bone.NearArm).Pivot;
    }

    internal static LayeredPetRig Get(string skin)
    {
        if (!Assets.TryGetValue(skin, out var rig)) Assets.Add(skin, rig = new LayeredPetRig(skin));
        return rig;
    }

    internal void Draw(DrawingContext drawing, double width, bool facingLeft, CoopanionMotion.RigFrame rig,
        string skin, CoopanionMotion.FaceSprite[] sprites)
    {
        double size = width / ArtSize;
        drawing.PushTransform(new ScaleTransform(facingLeft ? -size : size, size));
        drawing.PushTransform(new TranslateTransform(facingLeft ? -ArtSize : 0, 0));
        foreach (var layer in _layers)
        {
            double angle = layer.Bone switch
            {
                Bone.Head => rig.Neck,
                Bone.Hair => rig.Neck - Math.Atan2(rig.HairX, 85) * 180 / Math.PI + rig.Tail * .32,
                Bone.NearArm or Bone.Umbrella => rig.ArmNear - 4,
                Bone.FarArm => rig.ArmFar + 2,
                Bone.NearLeg => rig.LegFront,
                Bone.FarLeg => rig.LegBack,
                Bone.Waist => rig.Waist + rig.Tail * .16,
                _ => rig.Waist
            };
            var pivot = layer.Bone == Bone.Umbrella ? _nearArm : layer.Pivot;
            if (layer.Bone is Bone.Head or Bone.Hair)
                drawing.PushTransform(new TranslateTransform(0, rig.NeckY));
            drawing.PushTransform(new RotateTransform(angle, pivot.X, pivot.Y));
            if (layer.Bone == Bone.Body) DrawClothes(drawing, layer, rig);
            else drawing.DrawImage(layer.Image, layer.Destination);
            if (layer.Bone == Bone.Head)
                WhaleFaceLayer.Draw(drawing, skin, FreePetScene.Pose.Lifted, false, sprites, ArtSize, cutout: true);
            drawing.Pop();
            if (layer.Bone is Bone.Head or Bone.Hair) drawing.Pop();
        }
        drawing.Pop();
        drawing.Pop();
    }

    private static void DrawClothes(DrawingContext drawing, Layer layer, CoopanionMotion.RigFrame rig)
    {
        var bounds = layer.Destination;
        double seam = layer.Pivot.Y;
        drawing.PushClip(new RectangleGeometry(new Rect(bounds.Left, bounds.Top, bounds.Width, seam - bounds.Top + 4)));
        drawing.DrawImage(layer.Image, bounds);
        drawing.Pop();

        // The cutout extends behind the waist. Pivot at that overlap and map the native hem displacement.
        double tilt = -Math.Atan2(rig.SkirtX, bounds.Bottom - seam) * 180 / Math.PI;
        double scale = 1 + rig.SkirtY / (bounds.Bottom - seam);
        drawing.PushTransform(new RotateTransform(tilt, layer.Pivot.X, seam));
        drawing.PushTransform(new ScaleTransform(1, scale, layer.Pivot.X, seam));
        drawing.PushClip(new RectangleGeometry(new Rect(bounds.Left, seam - 4, bounds.Width, bounds.Bottom - seam + 4)));
        drawing.DrawImage(layer.Image, bounds);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }
}
