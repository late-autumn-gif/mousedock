using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;

namespace MouseDock.Controls;

// Registration points on the existing sitting and lifted atlas cells. Rikka's covered eye stays covered.
internal static class WhaleFaceLayer
{
    private sealed record Feature(double X, double Y, double Width, double Height, string Backing);
    private sealed record Face(Feature Left, Feature Right, Feature Mouth);
    private static readonly Dictionary<(string Skin, bool Held), Face> Faces = new()
    {
        [("shin-chan", false)] = new(new(103, 113, 16, 16, "#F7C65B"), new(127, 107, 15, 17, "#F7C65B"), new(120, 132, 21, 10, "#FFD2BB")),
        [("shin-chan", true)] = new(new(99, 75, 18, 18, "#FFCF64"), new(126, 68, 17, 18, "#FFCF64"), new(119, 101, 16, 17, "#FFCFB7")),
        [("keqing", false)] = new(new(112, 94, 12, 11, "#F6DDDA"), new(130, 88, 11, 12, "#F6DDDA"), new(124, 102, 9, 7, "#F8E8DB")),
        [("keqing", true)] = new(new(106, 57, 15, 14, "#F7DFDF"), new(130, 54, 13, 13, "#F7DFDF"), new(121, 70, 12, 11, "#FFE5CF")),
        [("rikka", false)] = new(null, new(118, 111, 14, 15, "#FCE4D8"), new(111, 125, 13, 8, "#FCE4D8")),
        [("rikka", true)] = new(null, new(111, 69, 14, 11, "#F9D9D1"), new(103, 85, 15, 13, "#FCDACD")),
        [("xin-yue-hu", false)] = new(new(116, 113, 14, 12, "#F9E0DE"), new(142, 108, 13, 13, "#F9E0DE"), new(132, 121, 9, 7, "#FCE7E5")),
        [("xin-yue-hu", true)] = new(new(110, 92, 14, 13, "#F8DDDC"), new(134, 86, 14, 13, "#F8DDDC"), new(124, 99, 13, 9, "#FBE5E4")),
    };
    private static readonly Dictionary<string, BitmapSource> Textures = new();
    private static readonly Dictionary<string, Brush> Backings = new();
    private static readonly Dictionary<string, Face> CutoutFaces = new()
    {
        ["shin-chan"] = new(new(84, 79, 16, 14, "#F7C65B"), new(115, 77, 16, 14, "#F7C65B"), new(103, 103, 15, 8, "#FAC5B0")),
        ["keqing"] = new(new(111, 57, 13, 11, "#F6DDDA"), new(129, 55, 11, 11, "#F6DDDA"), new(123, 67, 9, 7, "#F8E8DB")),
        ["rikka"] = new(null, new(118, 74, 15, 15, "#FCE4D8"), new(107, 88, 12, 8, "#FCE4D8")),
        ["xin-yue-hu"] = new(new(123, 80, 15, 13, "#F9E0DE"), new(146, 75, 13, 13, "#F9E0DE"), new(137, 92, 11, 7, "#FCE7E5")),
    };
    // The painted pupils and lash lines span more than the replacement eye sprites.
    // Follow the gold openings of the mask, leaving the brows and blue rim intact.
    private static readonly Geometry ShinLeftEye = FrozenMask("M69,76 C71,70 77,69 85,69 C90,69 94,70 96,73 L96,81 C96,87 92,91 86,91 C78,90 72,86 69,76 Z");
    private static readonly Geometry ShinRightEye = FrozenMask("M100,74 C105,69 115,67 124,68 C129,68 133,69 134,73 C132,82 126,88 118,90 C110,91 102,88 100,83 Z");
    private static readonly Brush ShinEyeFill = EyeFill();

    private static Geometry FrozenMask(string path)
    {
        var mask = Geometry.Parse(path);
        mask.Freeze();
        return mask;
    }

    private static Brush EyeFill()
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#F6C457"),
            (Color)ColorConverter.ConvertFromString("#FFDC82"), 90);
        brush.Freeze();
        return brush;
    }

    internal static void Draw(DrawingContext drawing, string skin, FreePetScene.Pose pose, bool left,
        CoopanionMotion.FaceSprite[] sprites, double width, bool cutout = false)
    {
        bool held = pose == FreePetScene.Pose.Lifted;
        if (sprites is not { Length: > 0 }) return;
        Face face;
        if (cutout ? !CutoutFaces.TryGetValue(skin, out face) :
            (!held && pose != FreePetScene.Pose.Sit) || !Faces.TryGetValue((skin, held), out face)) return;
        double unit = width / 220;
        if (left) drawing.PushTransform(new ScaleTransform(-1, 1, width / 2, 0));
        foreach (var sprite in sprites)
        {
            var feature = sprite.Name.EndsWith("_eyeL", StringComparison.Ordinal) ? face.Left :
                sprite.Name.EndsWith("_eyeR", StringComparison.Ordinal) ? face.Right : face.Mouth;
            if (feature == null) continue;
            double x = feature.X * unit, y = feature.Y * unit;
            double w = feature.Width * unit, h = feature.Height * unit;
            if (!Backings.TryGetValue(feature.Backing, out var backing))
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(feature.Backing));
                brush.Freeze();
                Backings.Add(feature.Backing, backing = brush);
            }
            Geometry mask = cutout && skin == "shin-chan" ? sprite.Name switch
            {
                "drag_eyeL" => ShinLeftEye,
                "drag_eyeR" => ShinRightEye,
                _ => null
            } : null;
            if (mask != null)
            {
                drawing.PushTransform(new ScaleTransform(unit, unit));
                drawing.DrawGeometry(ShinEyeFill, null, mask);
                drawing.Pop();
            }
            else
            {
                bool coverShinSmile = cutout && skin == "shin-chan" && sprite.Name == "drag_mouth";
                drawing.DrawEllipse(backing, null, new Point(x, y), coverShinSmile ? 13 * unit : w / 2,
                    coverShinSmile ? 6 * unit : h / 2);
            }
            if (!Textures.TryGetValue(sprite.Name, out var texture))
            {
                var bitmap = new BitmapImage(new Uri($"pack://application:,,,/MouseDock;component/Assets/Coopanion/Motion/WhaleFace/{sprite.Name}.png"));
                bitmap.Freeze();
                Textures.Add(sprite.Name, texture = bitmap);
            }
            double size = Math.Min(w / texture.PixelWidth, h / texture.PixelHeight) * sprite.Scale;
            double tw = texture.PixelWidth * size, th = texture.PixelHeight * size;
            drawing.PushTransform(new RotateTransform(sprite.Angle, x, y));
            drawing.DrawImage(texture, new Rect(x - tw / 2, y - th / 2, tw, th));
            drawing.Pop();
        }
        if (left) drawing.Pop();
    }
}
