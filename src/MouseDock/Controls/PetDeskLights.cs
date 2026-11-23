using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseDock.Controls;

// Artwork measurements are in a 400 x 400 reference cell. Each skin has a
// different desk perspective and mouse shell. The same polygons paint a cap at
// rest and when pressed, so close neighbours never share a highlight.
internal sealed class PetDeskLights : FrameworkElement
{
    private sealed record Desk(int TopLeft, int TopRight, int Top, int BottomLeft, int BottomRight,
        int Bottom, int MouseX, int MouseY, int MouseRadius, Color Cap);
    private Desk _desk;
    private DrawingGroup _keyboard;
    private readonly Dictionary<int, Geometry> _caps = new();
    private readonly Dictionary<int, long> _litUntil = new();
    private readonly List<int> _expired = new();
    private Geometry _left, _right;
    private long _leftUntil, _rightUntil;
    private bool _leftOn, _rightOn;
    private Brush _fingerMask;
    private const double Scale = BongoPetScene.SceneWidth / 400d;
    private static readonly ScaleTransform ToScene = new(Scale, Scale);
    private static readonly Brush Gold = Brushes.Gold;
    private static readonly Pen GoldEdge = new(Brushes.LightYellow, .85);
    private static readonly Pen Glow = new(new SolidColorBrush(Color.FromArgb(112, 255, 187, 0)), 2.8);
    private static readonly Brush Bed = new SolidColorBrush(Color.FromRgb(39, 36, 47));

    internal PetDeskLights()
    {
        Width = BongoPetScene.SceneWidth;
        Height = BongoPetScene.SceneHeight;
        IsHitTestVisible = false;
    }

    internal void SetSkin(string id)
    {
        _desk = id switch
        {
            "dracena" => new(201, 318, 323, 191, 333, 357, 110, 347, 19, Color.FromRgb(91, 93, 116)),
            "keqing" => new(188, 335, 314, 178, 348, 343, 94, 331, 19, Color.FromRgb(155, 130, 160)),
            "rikka" => new(184, 340, 332, 175, 351, 361, 87, 349, 20, Color.FromRgb(88, 90, 109)),
            "xin-yue-hu" => new(181, 319, 322, 174, 330, 354, 105, 344, 20, Color.FromRgb(122, 105, 98)),
            _ => new(191, 346, 322, 198, 364, 357, 100, 350, 25, Color.FromRgb(104, 100, 115))
        };
        _caps.Clear();
        _keyboard = new DrawingGroup();
        var capBrush = new SolidColorBrush(_desk.Cap);
        using (var dc = _keyboard.Open())
        {
            dc.DrawGeometry(Bed, null, KeyShape(new Rect(0, 0, 22.9, 6), 0));
            foreach (var (code, rect) in PetKeyboardLayout.Caps)
            {
                var cap = KeyShape(rect, .10);
                _caps.Add(code, cap);
                dc.DrawGeometry(capBrush, null, cap);
            }
        }
        _keyboard.Freeze();
        _left = ButtonShape(-1);
        _right = ButtonShape(1);
        InvalidateVisual();
    }

    private Geometry KeyShape(Rect rect, double gap)
    {
        Point Map(double x, double y)
        {
            double t = y / 6;
            double left = _desk.TopLeft + (_desk.BottomLeft - _desk.TopLeft) * t;
            double right = _desk.TopRight + (_desk.BottomRight - _desk.TopRight) * t;
            return new(left + (right - left) * x / 22.9, _desk.Top + (_desk.Bottom - _desk.Top) * t);
        }
        var shape = new StreamGeometry();
        using (var path = shape.Open())
        {
            path.BeginFigure(Map(rect.Left + gap, rect.Top + gap), true, true);
            path.LineTo(Map(rect.Right - gap, rect.Top + gap), true, false);
            path.LineTo(Map(rect.Right - gap, rect.Bottom - gap), true, false);
            path.LineTo(Map(rect.Left + gap, rect.Bottom - gap), true, false);
        }
        shape.Freeze();
        return shape;
    }

    private Geometry ButtonShape(int side)
    {
        double x = _desk.MouseX, y = _desk.MouseY, w = _desk.MouseRadius;
        Point P(double dx, double dy) => new(x + side * dx, y + dy);
        var shape = new StreamGeometry();
        using (var path = shape.Open())
        {
            path.BeginFigure(P(2, -12), true, true);
            path.BezierTo(P(w * .6, -15), P(w, -12), P(w, -4), true, false);
            path.LineTo(P(w - 1, 4), true, false);
            path.QuadraticBezierTo(P(w * .65, 7), P(2, 5), true, false);
        }
        shape.Freeze();
        return shape;
    }

    // Preserve the painted fingers and their outline above the replacement key bed.
    internal Brush CreateFingerMask(BitmapSource pose)
    {
        var scaled = new TransformedBitmap(pose, new ScaleTransform(400d / pose.PixelWidth, 400d / pose.PixelHeight));
        var source = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        const int top = 285, height = 95, width = 400;
        byte[] pixels = new byte[width * height * 4], alpha = new byte[width * height];
        source.CopyPixels(new Int32Rect(0, top, width, height), pixels, width * 4, 0);
        Array.Fill(alpha, (byte)255);
        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            // Limit the skin-tone mask to the hands; bronze trim uses similar pigments.
            if (Math.Abs(x - _desk.MouseX) > 34 && (x < 242 || x > 344)) continue;
            int i = (y * width + x) * 4;
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            if (r < 146 || g < 83 || r - b < 13 || r <= g || g + 12 < b) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    alpha[(y + dy) * width + x + dx] = 0;
        }
        for (int i = 0; i < alpha.Length; i++) pixels[i * 4 + 3] = alpha[i];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var brush = new ImageBrush(bitmap) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, top, width, height) };
        brush.Freeze();
        return brush;
    }

    internal void SetMask(Brush mask)
    {
        if (ReferenceEquals(mask, _fingerMask)) return;
        _fingerMask = mask;
        InvalidateVisual();
    }

    internal void PressKey(int scan, long now)
    {
        if (!_caps.ContainsKey(scan)) return;
        bool alreadyLit = _litUntil.ContainsKey(scan);
        _litUntil[scan] = now + 95;
        if (!alreadyLit) InvalidateVisual();
    }

    internal void PressMouse(int flags, long now)
    {
        if ((flags & 1) != 0) _leftUntil = now + 95;
        if ((flags & 4) != 0) _rightUntil = now + 95;
    }

    internal void Advance(long now, Func<int, bool> held, bool left, bool right)
    {
        foreach (var (scan, until) in _litUntil)
            if (now >= until && !held(scan)) _expired.Add(scan);
        bool changed = _expired.Count != 0;
        foreach (int scan in _expired) _litUntil.Remove(scan);
        _expired.Clear();
        left |= now < _leftUntil;
        right |= now < _rightUntil;
        if (changed || left != _leftOn || right != _rightOn) InvalidateVisual();
        _leftOn = left;
        _rightOn = right;
    }

    internal void Clear()
    {
        if (_litUntil.Count != 0 || _leftOn || _rightOn) InvalidateVisual();
        _litUntil.Clear();
        _leftOn = _rightOn = false;
        _leftUntil = _rightUntil = 0;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_keyboard == null) return;
        dc.PushTransform(ToScene);
        dc.PushOpacityMask(_fingerMask);
        dc.DrawDrawing(_keyboard);
        foreach (int scan in _litUntil.Keys) Light(dc, _caps[scan]);
        if (_leftOn) Light(dc, _left);
        if (_rightOn) Light(dc, _right);
        dc.Pop();
        // Warm spill on an occluding fingertip makes even a covered key readable.
        dc.PushOpacity(.35);
        foreach (int scan in _litUntil.Keys) dc.DrawGeometry(Gold, null, _caps[scan]);
        dc.Pop();
        dc.Pop();
    }

    private static void Light(DrawingContext dc, Geometry shape)
    {
        dc.DrawGeometry(null, Glow, shape);
        dc.DrawGeometry(Gold, GoldEdge, shape);
    }
}
