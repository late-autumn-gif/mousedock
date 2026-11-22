using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Services;

namespace MouseDock.Controls;

internal sealed class DesktopPetInputOverlay : Canvas
{
    private const int TapDuration = 160;
    private readonly Image _left;
    private readonly Image _right;
    private long _keyboardUntil;
    private long _mouseUntil;

    internal DesktopPetInputOverlay()
    {
        IsHitTestVisible = false;
        _left = AddPaw(22, Color.FromRgb(177, 247, 221), -12);
        _right = AddPaw(92, Color.FromRgb(255, 218, 164), 12);
    }

    internal void Flash(DesktopPetInputActivity activity, long now)
    {
        switch (activity)
        {
            case DesktopPetInputActivity.Keyboard when now >= _keyboardUntil:
                _keyboardUntil = now + TapDuration;
                Draw(_left, TapDuration);
                break;
            case DesktopPetInputActivity.Mouse when now >= _mouseUntil:
                _mouseUntil = now + TapDuration;
                Draw(_right, TapDuration);
                break;
        }
    }

    internal void Advance(long now)
    {
        if (_keyboardUntil != 0)
        {
            if (now >= _keyboardUntil) _keyboardUntil = 0;
            Draw(_left, Math.Max(0, _keyboardUntil - now));
        }
        if (_mouseUntil != 0)
        {
            if (now >= _mouseUntil) _mouseUntil = 0;
            Draw(_right, Math.Max(0, _mouseUntil - now));
        }
    }

    internal void Clear()
    {
        _keyboardUntil = _mouseUntil = 0;
        Draw(_left, 0);
        Draw(_right, 0);
    }

    private static void Draw(Image paw, long remaining)
    {
        if (remaining <= 0)
        {
            paw.Visibility = Visibility.Collapsed;
            return;
        }

        double decay = Math.Clamp(remaining / (double)TapDuration, 0, 1);
        ((TranslateTransform)((TransformGroup)paw.RenderTransform).Children[1]).Y = 7 * decay;
        paw.Opacity = Math.Min(1, 2.5 * decay);
        paw.Visibility = Visibility.Visible;
    }

    private Image AddPaw(double left, Color accentColor, double angle)
    {
        var drawing = new DrawingGroup();
        var accent = new SolidColorBrush(accentColor);
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(38, 38, 55)), 1.4);
        using (var dc = drawing.Open())
        {
            dc.DrawEllipse(accent, outline, new Point(15, 13), 10, 7);
            dc.DrawEllipse(accent, outline, new Point(7, 6), 3.2, 3.5);
            dc.DrawEllipse(accent, outline, new Point(15, 4), 3.4, 3.5);
            dc.DrawEllipse(accent, outline, new Point(23, 6), 3.2, 3.5);
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        var paw = new Image
        {
            Source = image, Width = 30, Height = 23, Visibility = Visibility.Collapsed,
            RenderTransform = new TransformGroup { Children = { new RotateTransform(angle), new TranslateTransform() } },
            RenderTransformOrigin = new Point(.5, .5)
        };
        SetLeft(paw, left);
        SetTop(paw, 111);
        Children.Add(paw);
        return paw;
    }
}
