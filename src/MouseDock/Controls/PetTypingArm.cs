using System;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Controls;

/// <summary>Moves the painted hand as a rigid piece, with an attached forearm of constant width.</summary>
internal sealed class PetTypingArm : FrameworkElement
{
    private const double Scale = BongoPetScene.SceneWidth / 400d;
    private static readonly Rect Artwork = new(0, 0, 400, 400);
    private static readonly ScaleTransform ToScene = new(Scale, Scale);
    private ImageSource _source;
    private Point _shoulder, _cuff, _tip, _contact;
    private Vector _axis, _normal;
    private Geometry _forearm, _hand;

    internal PetTypingArm()
    {
        Width = Height = BongoPetScene.SceneWidth;
        IsHitTestVisible = false;
    }

    internal Point Fingertip { get; private set; }
    internal Point RestTip => _tip;

    internal void SetRig(PetArmLayers.Rig rig)
    {
        var shoulder = rig.Keys[0];
        _shoulder = new Point((shoulder.Left + shoulder.Right) / 2d, shoulder.Y);
        _cuff = rig.KeyboardCuff;
        _tip = rig.Tip;
        _axis = _cuff - _shoulder;
        _axis.Normalize();
        _normal = new Vector(-_axis.Y, _axis.X);
        _forearm = HalfPlane(-_axis);
        _hand = HalfPlane(_axis);
        _contact = _tip;
        Fingertip = _tip;
        InvalidateVisual();
    }

    internal void Update(ImageSource source, Point contact)
    {
        if (ReferenceEquals(_source, source) && _contact == contact) return;
        _source = source;
        _contact = contact;
        Fingertip = Transforms().Hand.Transform(_tip);
        InvalidateVisual();
    }

    private Geometry HalfPlane(Vector side)
    {
        // Both parts use the same edge through the cuff, so the joint meets after transforming.
        Vector across = _normal * 600;
        var shape = new StreamGeometry();
        using (var figure = shape.Open())
        {
            figure.BeginFigure(_cuff - across, true, true);
            figure.LineTo(_cuff + across, false, false);
            figure.LineTo(_cuff + across + side * 600, false, false);
            figure.LineTo(_cuff - across + side * 600, false, false);
        }
        shape.Freeze();
        return shape;
    }

    internal (Matrix Forearm, Matrix Hand) Transforms()
    {
        Vector reach = _contact - _shoulder;
        double across = (_tip - _cuff) * _normal;
        double correction = Math.Asin(Math.Clamp(across / Math.Max(reach.Length, .01), -1, 1)) * 180 / Math.PI;
        double turn = Vector.AngleBetween(_axis, reach) - correction;
        // Let the hand bend a little less than the arm so the fingertips still face the keyboard.
        var rotation = Matrix.Identity;
        rotation.Rotate(turn - Math.Clamp(turn * .18, -16, 16));
        Point wrist = _contact - rotation.Transform(_tip - _cuff);
        Vector along = (wrist - _shoulder) / (_cuff - _shoulder).Length;
        Vector acrossCuff = rotation.Transform(_normal);

        var x = along * _axis.X + acrossCuff * _normal.X;
        var y = along * _axis.Y + acrossCuff * _normal.Y;
        var forearm = new Matrix(x.X, x.Y, y.X, y.Y,
            _shoulder.X - _shoulder.X * x.X - _shoulder.Y * y.X,
            _shoulder.Y - _shoulder.X * x.Y - _shoulder.Y * y.Y);
        var hand = rotation;
        hand.Translate(wrist.X - hand.Transform(_cuff).X, wrist.Y - hand.Transform(_cuff).Y);
        return (forearm, hand);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_source == null) return;
        dc.PushTransform(ToScene);
        if (_contact == _tip) dc.DrawImage(_source, Artwork);
        else
        {
            var (forearm, hand) = Transforms();
            DrawPart(_forearm, forearm);
            DrawPart(_hand, hand);
        }
        dc.Pop();

        void DrawPart(Geometry clip, Matrix transform)
        {
            dc.PushTransform(new MatrixTransform(transform));
            dc.PushClip(clip);
            dc.DrawImage(_source, Artwork);
            dc.Pop();
            dc.Pop();
        }
    }
}
