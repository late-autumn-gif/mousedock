using System;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Controls;

internal enum PetExpression { Neutral, Smile, Blink, Wink, Rest, Dizzy }

internal sealed class PetFaceEffects : FrameworkElement
{
    private sealed record Eyes(Brush Skin, Rect Left, Rect Right);
    private static readonly Eyes ShinChan = new(Paint(254, 192, 144), new(81, 63, 21, 25), new(106, 61, 20, 23));
    private static readonly Eyes Fox = new(Paint(255, 221, 205), new(65, 69, 20, 20), new(97, 68, 20, 21));
    private static readonly Eyes Rikka = new(Paint(250, 216, 203), Rect.Empty, new(96, 71, 19, 21));
    private static readonly Eyes Keqing = new(Paint(247, 211, 208), new(74, 79, 20, 20), new(103, 78, 20, 20));
    private static readonly Pen Lid = Stroke(Paint(66, 45, 56), 1.75);
    private static readonly Pen Spiral = Stroke(Paint(104, 67, 146), 1.65);
    private static readonly Pen SparkleEdge = Stroke(Paint(116, 73, 51), 1);
    private static readonly Brush Sparkle = Paint(255, 221, 104);
    private static readonly Geometry Star = Geometry.Parse("M 0,-7 Q 1,-1 6,0 Q 1,1 0,7 Q -1,1 -6,0 Q -1,-1 0,-7 Z");
    private static readonly Geometry ClosedEye = Geometry.Parse("M -6,0 Q 0,4 6,-1");
    private static readonly Geometry EyeSpiral = MakeSpiral();
    private Eyes _eyes = ShinChan;
    internal PetExpression Expression { get; private set; }

    internal PetFaceEffects()
    {
        Width = BongoPetScene.SceneWidth;
        Height = BongoPetScene.SceneHeight;
        IsHitTestVisible = false;
    }

    internal void SetSkin(string id)
    {
        _eyes = id switch
        {
            "xin-yue-hu" => Fox,
            "rikka" => Rikka,
            "keqing" => Keqing,
            _ => ShinChan
        };
        InvalidateVisual();
    }

    internal void Show(PetExpression expression)
    {
        if (Expression == expression) return;
        Expression = expression;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (Expression == PetExpression.Wink)
        {
            DrawEye(drawing, _eyes.Right);
            return;
        }
        if (Expression is not (PetExpression.Blink or PetExpression.Rest or PetExpression.Dizzy)) return;
        DrawEye(drawing, _eyes.Left);
        DrawEye(drawing, _eyes.Right);
        if (Expression == PetExpression.Dizzy)
        {
            DrawStar(drawing, 23, 45);
            DrawStar(drawing, 149, 34);
        }
    }

    private void DrawEye(DrawingContext drawing, Rect eye)
    {
        if (eye.IsEmpty) return;
        var center = new Point(eye.X + eye.Width / 2, eye.Y + eye.Height / 2);
        drawing.DrawEllipse(_eyes.Skin, null, center, eye.Width / 2, eye.Height / 2);
        drawing.PushTransform(new TranslateTransform(center.X, center.Y));
        bool dizzy = Expression == PetExpression.Dizzy;
        drawing.DrawGeometry(null, dizzy ? Spiral : Lid, dizzy ? EyeSpiral : ClosedEye);
        drawing.Pop();
    }

    private static void DrawStar(DrawingContext drawing, double x, double y)
    {
        drawing.PushTransform(new TranslateTransform(x, y));
        drawing.DrawGeometry(Sparkle, SparkleEdge, Star);
        drawing.Pop();
    }

    private static Geometry MakeSpiral()
    {
        var spiral = new StreamGeometry();
        using (var path = spiral.Open())
        {
            path.BeginFigure(new Point(0, 0), false, false);
            for (int i = 1; i <= 24; i++)
            {
                double radius = i / 4d;
                double angle = i * Math.PI / 8;
                path.LineTo(new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)), true, true);
            }
        }
        spiral.Freeze();
        return spiral;
    }

    private static Brush Paint(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
