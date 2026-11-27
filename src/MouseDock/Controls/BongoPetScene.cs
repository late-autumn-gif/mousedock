using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;

namespace MouseDock.Controls;

/// <summary>Stationary desk and face with two independently anchored arm layers.</summary>
internal sealed class BongoPetScene : Canvas
{
    // The pose sprites are square. A square viewport avoids empty columns next to the battery card.
    internal const int SceneWidth = 176, SceneHeight = 176;
    private readonly Image _artwork;
    private readonly Image _mouseArm = ArmImage();
    private readonly PetTypingArm _keyArm = new();
    private readonly PetHandEffect _mouseEffect = new();
    private readonly PetDeskLights _lights = new();
    private readonly PetFaceEffects _face = new();
    private PetArmLayers _neutral, _smiling;
    private ImageSource _idleExpression, _animeDesk, _animeDeskBlink;
    private PetArmLayers.Rig _rig;
    private long _quietAfter;
    internal Point MouseTravel => _mouseEffect.MouseOffset;
    internal Point KeyboardFingertip => _keyArm.Fingertip;
    internal double KeyboardStroke { get; private set; }
    private static Image ArmImage() => new() { Width = SceneWidth, Height = SceneHeight, IsHitTestVisible = false };
    private System.Drawing.Point? _lastCursor;
    private bool _keyboardDown, _mouseDown;
    private long _keyboardUntil, _mouseUntil;
    private double _mouseForwardLimit;

    internal BongoPetScene(Image artwork)
    {
        _artwork = artwork;
        Width = SceneWidth;
        Height = SceneHeight;
        artwork.Width = SceneWidth;
        artwork.Height = SceneHeight;
        Children.Add(artwork);
        Children.Add(_lights);
        _mouseArm.Effect = _mouseEffect;
        Children.Add(_mouseArm);
        Children.Add(_keyArm);
        Children.Add(_lights.Indicators);
        Children.Add(_face);
    }

    internal void SetSkin(string id)
    {
        _lights.SetSkin(id);
        _face.SetSkin(id);
        var atlas = new BitmapImage(new Uri(
            $"pack://application:,,,/MouseDock;component/Assets/Characters/DeskPoses/{id}.png"));
        int width = atlas.PixelWidth / 2;
        int height = atlas.PixelHeight / 2;
        var idle = new CroppedBitmap(atlas, new Int32Rect(0, 0, width, height));
        _rig = PetArmLayers.For(id);
        // Leave the desk rim and two units of click travel clear of the shell.
        _mouseForwardLimit = Math.Min(.014, (380 - _rig.Mouse[^1].Y) / 400d);
        _keyArm.SetRig(_rig);
        _neutral = new PetArmLayers(idle, _rig);
        _mouseEffect.Anchors = new Point(_rig.Mouse[0].Y / 400d, _rig.WristY / 400d);
        _lights.SetMask(_lights.CreateFingerMask(idle));
        if (id is "shin-chan" or "keqing")
        {
            var smile = new CroppedBitmap(atlas, new Int32Rect(width, height, width, height));
            _smiling = new PetArmLayers(smile, _rig, _neutral.Base);
            _idleExpression = _smiling.Base;
        }
        else
        {
            _smiling = _neutral;
            _idleExpression = HeadExpression(atlas, width, height, _neutral.Base);
        }
        _animeDesk = id == "shin-chan" ? AnimeDeskHead(_neutral.Base, closed: false) : null;
        _animeDeskBlink = id == "shin-chan" ? AnimeDeskHead(_neutral.Base, closed: true) : null;
        ShowPose(false);
    }

    private static DrawingImage AnimeDeskHead(BitmapSource desk, bool closed)
    {
        const int cell = 320;
        var atlas = new BitmapImage(new Uri("pack://application:,,,/MouseDock;component/Assets/Characters/FreePoses/shin-chan.png"));
        var head = new CroppedBitmap(atlas, new Int32Rect((closed ? cell : 0) + 76, 19, 166, 157));
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            // Keep the keyboard, mouse, arms and torso at their calibrated coordinates.
            dc.PushClip(Geometry.Parse("M0,98 L34,98 L43,98 57,97 66,103 82,110 105,111 127,108 145,108 176,108 V176 H0 Z"));
            dc.DrawImage(desk, new Rect(0, 0, SceneWidth, SceneHeight));
            dc.Pop();
            dc.DrawImage(head, new Rect(37, 1, 111, 108));
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static DrawingImage HeadExpression(BitmapSource atlas, int width, int height, BitmapSource neutral)
    {
        int headHeight = height * 57 / 100;
        var face = new CroppedBitmap(atlas, new Int32Rect(width, height, width, headHeight));
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, headHeight, width, height - headHeight)));
            dc.DrawImage(neutral, new Rect(0, 0, width, height));
            dc.Pop();
            dc.DrawImage(face, new Rect(0, 0, width, headHeight));
        }
        var expression = new DrawingImage(drawing);
        expression.Freeze();
        return expression;
    }

    internal void SetButtons(bool keyboard, bool mouse)
    {
        _keyboardDown = keyboard;
        _mouseDown = mouse;
    }

    internal void TrackPointer(System.Drawing.Point cursor, System.Drawing.Rectangle desktop)
    {
        double x = Math.Clamp((cursor.X - desktop.Left) / (double)Math.Max(1, desktop.Width - 1), 0, 1);
        double y = Math.Clamp((cursor.Y - desktop.Top) / (double)Math.Max(1, desktop.Height - 1), 0, 1);
        var offset = new Point((x - .5) * .14, Math.Min((y - .5) * .028, _mouseForwardLimit));
        if (_lastCursor is { } previous)
        {
            // Relative travel on a small pad. Ordinary short strokes remain visible on a wide desktop.
            offset.X = Math.Clamp(_mouseEffect.MouseOffset.X + (cursor.X - previous.X) * .00055, -.07, .07);
            offset.Y = Math.Clamp(_mouseEffect.MouseOffset.Y + (cursor.Y - previous.Y) * .00048, -.030, _mouseForwardLimit);
        }
        _lastCursor = cursor;
        if (_mouseEffect.MouseOffset != offset) _mouseEffect.MouseOffset = offset;

    }

    internal void Tap(DesktopPetInputActivity activity, long now, int detail)
    {
        _quietAfter = now + 750;
        if (activity == DesktopPetInputActivity.Keyboard)
        {
            _keyboardUntil = now + 85;
            _lights.PressKey(detail, now);
        }
        if (activity == DesktopPetInputActivity.Mouse)
        {
            _mouseUntil = now + 85;
            _lights.PressMouse(detail, now);
        }
    }

    internal void UpdateLights(long now, Func<int, bool> held, bool left, bool right) =>
        _lights.Advance(now, held, left, right);

    internal int PoseIndex(bool smile, long now) =>
        _mouseDown || now < _mouseUntil ? 2 : _keyboardDown || now < _keyboardUntil ? 1 : smile ? 3 : 0;

    internal void ShowPose(bool smile, long now = 0) =>
        ShowPose(smile ? PetExpression.Smile : PetExpression.Neutral, now);

    internal void ShowPose(PetExpression expression, long now)
    {
        KeyboardStroke = Math.Clamp((_keyboardUntil - now) / 85d, 0, 1);
        var cap = _lights.ActiveTarget();
        Point contact = cap is Point key
            ? key - new Vector(0, (1 - KeyboardStroke) * 1.7)
            : _keyArm.RestTip;
        if (_keyboardDown || _mouseDown) _quietAfter = Math.Max(_quietAfter, now + 750);
        double click = _mouseDown || now < _mouseUntil ? .005 : 0;
        _mouseEffect.KeyStroke = click;
        var mouse = MouseTravel;
        _lights.MoveMouse(new Point(mouse.X, mouse.Y + click));
        bool quiet = PoseIndex(false, now) == 0 && now >= _quietAfter;
        bool smiling = quiet && expression == PetExpression.Smile;
        _face.Show(quiet || expression == PetExpression.Dizzy ? expression : PetExpression.Neutral);
        _face.Opacity = _animeDesk != null && expression != PetExpression.Dizzy ? 0 : 1;
        ImageSource face = _animeDesk != null
            ? quiet && expression is PetExpression.Blink or PetExpression.Rest or PetExpression.Wink or PetExpression.Smile
                ? _animeDeskBlink : _animeDesk
            : smiling ? _idleExpression : _neutral.Base;
        var arms = smiling && _animeDesk == null ? _smiling : _neutral;
        _keyArm.Update(arms.Keys, contact);
        if (!ReferenceEquals(_artwork.Source, face))
        {
            _artwork.Source = face;
            _mouseArm.Source = arms.Mouse;
        }
    }

    internal void Clear()
    {
        SetButtons(false, false);
        _keyboardUntil = _mouseUntil = 0;
        _lastCursor = null;
        _quietAfter = 0;
        _mouseEffect.MouseOffset = new Point();
        _lights.MoveMouse(new Point());
        _lights.Clear();
    }
}
