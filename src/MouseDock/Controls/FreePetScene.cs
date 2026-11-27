using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;

namespace MouseDock.Controls;

internal sealed class FreePetScene : FrameworkElement
{
    internal enum Pose { Stand, Blink, Greet, Lifted, Sit, SeatedEyesClosed, StepA, StepB, Attentive, Pat, Surprise }
    internal const double FootAnchor = .947;

    private const int Columns = 4;
    private static readonly Dictionary<string, BitmapSource[]> Atlases = new();
    private BitmapSource[] _frames = [];
    private int _walkFacing;
    private Pose _outgoing;
    private bool _facingLeft, _outgoingLeft, _hasFrame;
    private long _changedAt;
    private double _blend = 1;
    internal double TransitionProgress => _blend;
    internal Pose CurrentPose { get; private set; }
    private string _reactionFace;
    private double _reactionTime;
    private string _skin;
    private CoopanionMotion.FaceSprite[] _faceSprites;
    private CoopanionMotion.RigFrame _rig;
    private bool _useLayers;
    internal bool UsesLayeredRig => _useLayers;
    // Coopanion v0.2.23 reaction symbols, positioned on the existing character atlas.
    private static readonly Geometry Sweat = Geometry.Parse("M0 -9C4 -3 6 0 6 3.5A6 6 0 0 1 -6 3.5C-6 0 -4 -3 0 -9Z");
    private static readonly Geometry Star = Geometry.Parse("M0 -7L2 -2L7 -2L3 1L4.5 6.5L0 3.3L-4.5 6.5L-3 1L-7 -2L-2 -2Z");
    private static readonly Pen BangStroke = new(Brushes.DarkSlateBlue, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    internal void SetReaction(string face, double time, CoopanionMotion.FaceSprite[] sprites = null,
        CoopanionMotion.RigFrame rig = null, bool useLayers = false)
    {
        if (face is not ("dragged" or "dizzy" or "surprised")) face = null;
        if (_reactionFace == face && _faceSprites == sprites && _rig == rig && _useLayers == useLayers &&
            (face is null or "surprised" || time == _reactionTime)) return;
        _reactionFace = face;
        _reactionTime = time;
        _faceSprites = sprites;
        _rig = rig;
        _useLayers = useLayers;
        InvalidateVisual();
    }

    internal FreePetScene()
    {
        Width = BongoPetScene.SceneWidth;
        Height = BongoPetScene.SceneHeight;
    }

    internal void SetSkin(string id)
    {
        if (!Atlases.TryGetValue(id, out var frames))
        {
            var atlas = new BitmapImage(new Uri($"pack://application:,,,/MouseDock;component/Assets/Characters/FreePoses/{id}.png"));
            int cell = atlas.PixelWidth / Columns;
            frames = new BitmapSource[11];
            for (int i = 0; i < frames.Length; i++)
            {
                var frame = new CroppedBitmap(atlas, new Int32Rect(i % Columns * cell, i / Columns * cell, cell, cell));
                frame.Freeze();
                frames[i] = frame;
            }
            Atlases.Add(id, frames);
        }
        _frames = frames;
        _skin = id;
        _walkFacing = id == "rikka" ? -1 : 1;
        _hasFrame = false;
        Show(Pose.Stand, 0, 0, false);
    }

    internal void Show(Pose pose, int direction, long now, bool animate)
    {
        bool facingLeft = direction * (IsStride(pose) ? _walkFacing : 1) < 0;
        bool changed = !_hasFrame || pose != CurrentPose || _facingLeft != facingLeft;
        if (changed)
        {
            _outgoing = CurrentPose;
            _outgoingLeft = _facingLeft;
            CurrentPose = pose;
            _facingLeft = facingLeft;
            _changedAt = _hasFrame ? now : now - 160;
            _hasFrame = true;
        }
        int duration = _outgoingLeft != _facingLeft ? 0 :
            IsSeated(_outgoing) != IsSeated(pose) ? 210 :
            IsStride(_outgoing) || IsStride(pose) ? 55 :
            _outgoing == Pose.Blink || pose == Pose.Blink ? 35 : 90;
        double linear = animate && duration > 0 ? Math.Clamp((now - _changedAt) / (double)duration, 0, 1) : 1;
        double blend = linear * linear * (3 - 2 * linear);
        if (changed || _blend != blend)
        {
            _blend = blend;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (_useLayers && _rig != null)
            LayeredPetRig.Get(_skin).Draw(drawing, Width, _facingLeft, _rig, _skin, _faceSprites);
        else if (_rig != null && IsSeated(CurrentPose)) DrawSeatedHead(drawing);
        else
        {
            DrawPose(drawing);
            WhaleFaceLayer.Draw(drawing, _skin, CurrentPose, _facingLeft, _faceSprites, Width);
        }
        if (_reactionFace == "dragged")
        {
            double x = _facingLeft ? 41 : Width - 41;
            drawing.PushTransform(new TranslateTransform(x, 48 + 2 * Math.Sin(_reactionTime * 7)));
            drawing.DrawGeometry(Brushes.LightSkyBlue, null, Sweat);
            drawing.Pop();
        }
        else if (_reactionFace == "dizzy")
        {
            for (int i = 0; i < 3; i++)
            {
                double angle = _reactionTime * 3.2 + i * Math.Tau / 3;
                double x = Width / 2 + Math.Cos(angle) * 39;
                double y = (IsSeated(CurrentPose) ? 39 : 18) + Math.Sin(angle) * 8;
                drawing.PushOpacity(Math.Sin(angle) < 0 ? .55 : 1);
                drawing.PushTransform(new TranslateTransform(x, y));
                drawing.DrawGeometry(Brushes.Gold, null, Star);
                drawing.Pop();
                drawing.Pop();
            }
        }
        else if (_reactionFace == "surprised")
        {
            double x = _facingLeft ? 37 : Width - 37;
            drawing.DrawLine(BangStroke, new Point(x, 20), new Point(x, 30));
            drawing.DrawEllipse(Brushes.DarkSlateBlue, null, new Point(x, 36), 2, 2);
        }
    }

    private void DrawSeatedHead(DrawingContext drawing)
    {
        // The existing atlas is flat. Separate its top from the seated body at the collar,
        // with a narrow overlap so the native neck rotation cannot open a horizontal gap.
        var hinge = _skin switch
        {
            "keqing" => new Point(116, 116),
            "rikka" => new Point(111, 143),
            "shin-chan" => new Point(113, 152),
            _ => new Point(127, 139)
        };
        double scale = Width / 220;
        double x = hinge.X * scale, y = hinge.Y * scale;
        if (_facingLeft) x = Width - x;
        drawing.PushClip(new RectangleGeometry(new Rect(0, y - 4, Width, Height - y + 4)));
        DrawPose(drawing);
        drawing.Pop();
        drawing.PushTransform(new TranslateTransform(0, _rig.NeckY * Width / 256));
        drawing.PushTransform(new RotateTransform(_rig.Neck * (_facingLeft ? -1 : 1), x, y));
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, Width, y + 4)));
        DrawPose(drawing);
        WhaleFaceLayer.Draw(drawing, _skin, CurrentPose, _facingLeft, _faceSprites, Width);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }

    private void DrawPose(DrawingContext drawing)
    {
        if (!_hasFrame) return;
        if (_blend < 1 && IsSeated(_outgoing) != IsSeated(CurrentPose))
        {
            // Match apparent height at the switch so the body lowers or rises as one silhouette.
            bool outgoing = _blend < .5;
            Pose pose = outgoing ? _outgoing : CurrentPose;
            double seatedRatio = 236d / 282;
            double middle = Math.Sqrt(seatedRatio);
            double scale = IsSeated(pose) ? 1 / middle : middle;
            double closeness = outgoing ? _blend * 2 : (1 - _blend) * 2;
            DrawFrame(drawing, pose, outgoing ? _outgoingLeft : _facingLeft, 1, 1 + (scale - 1) * closeness);
            return;
        }
        if (_blend < 1 && IsBlinkPair(_outgoing, CurrentPose))
        {
            DrawFrame(drawing, _outgoing, _outgoingLeft, 1, 1);
            DrawFrame(drawing, CurrentPose, _facingLeft, _blend, 1);
            return;
        }
        if (_blend < .5)
            DrawFrame(drawing, _outgoing, _outgoingLeft, 1, 1);
        else
            DrawFrame(drawing, CurrentPose, _facingLeft, 1, 1);
    }

    private void DrawFrame(DrawingContext drawing, Pose pose, bool left, double opacity, double stretch)
    {
        drawing.PushOpacity(opacity);
        drawing.PushTransform(new ScaleTransform(left ? -1 : 1, stretch, Width / 2, Height * FootAnchor));
        drawing.DrawImage(_frames[(int)pose], new Rect(0, 0, Width, Height));
        drawing.Pop();
        drawing.Pop();
    }

    private static bool IsStride(Pose pose) => pose is Pose.StepA or Pose.StepB;
    private static bool IsSeated(Pose pose) => pose is Pose.Sit or Pose.SeatedEyesClosed;
    private static bool IsBlinkPair(Pose from, Pose to) =>
        (from, to) is (Pose.Stand, Pose.Blink) or (Pose.Blink, Pose.Stand) or
            (Pose.Sit, Pose.SeatedEyesClosed) or (Pose.SeatedEyesClosed, Pose.Sit);
}
