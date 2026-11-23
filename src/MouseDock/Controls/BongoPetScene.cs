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
    private readonly Image _mouseArm = ArmImage(), _keyArm = ArmImage();
    private readonly PetHandEffect _mouseEffect = new(), _keyEffect = new();
    private readonly PetDeskLights _lights = new();
    private BitmapSource _neutral;
    private DrawingImage _idleExpression;
    private PetArmLayers.Rig _rig;
    private long _quietAfter, _lastFrame;
    internal Point MouseTravel => _mouseEffect.MouseOffset;
    internal Point KeyboardTravel => _keyEffect.MouseOffset;
    internal double KeyboardStroke => _keyEffect.KeyStroke;
    private static Image ArmImage() => new() { Width = SceneWidth, Height = SceneHeight, IsHitTestVisible = false };
    private System.Drawing.Point? _lastCursor;
    private bool _keyboardDown, _mouseDown;
    private long _keyboardUntil, _mouseUntil;
    internal ImageSource Portrait { get; private set; }

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
        _keyArm.Effect = _keyEffect;
        Children.Add(_mouseArm);
        Children.Add(_keyArm);
        Children.Add(_lights.Indicators);
    }

    internal void SetSkin(string id)
    {
        _lights.SetSkin(id);
        var atlas = new BitmapImage(new Uri(
            $"pack://application:,,,/MouseDock;component/Assets/Characters/DeskPoses/{id}.png"));
        int width = atlas.PixelWidth / 2;
        int height = atlas.PixelHeight / 2;
        var idle = new CroppedBitmap(atlas, new Int32Rect(0, 0, width, height));
        _rig = PetArmLayers.For(id);
        var layers = new PetArmLayers(idle, _rig);
        _neutral = layers.Base;
        _mouseArm.Source = layers.Mouse;
        _keyArm.Source = layers.Keys;
        _mouseEffect.Anchors = new Point(_rig.Mouse[0].Y / 400d, _rig.WristY / 400d);
        _keyEffect.Anchors = new Point(_rig.Keys[0].Y / 400d, _rig.WristY / 400d);
        _lights.SetMask(_lights.CreateFingerMask(idle));
        // The idle expression replaces only the head. Both arms and the desk keep their own layers.
        int headHeight = height * 57 / 100;
        var face = new CroppedBitmap(atlas, new Int32Rect(width, height, width, headHeight));
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, headHeight, width, height - headHeight)));
            dc.DrawImage(_neutral, new Rect(0, 0, width, height));
            dc.Pop();
            dc.DrawImage(face, new Rect(0, 0, width, headHeight));
        }
        _idleExpression = new DrawingImage(drawing);
        _idleExpression.Freeze();
        var portrait = new CroppedBitmap(idle, new Int32Rect(width / 5, 0, width * 3 / 5, height * 3 / 5));
        portrait.Freeze();
        Portrait = portrait;
        ShowPose(false);
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
        var offset = new Point((x - .5) * .14, (y - .5) * .028);
        if (_lastCursor is { } previous)
        {
            // Relative travel on a small pad. Ordinary short strokes remain visible on a wide desktop.
            offset.X = Math.Clamp(_mouseEffect.MouseOffset.X + (cursor.X - previous.X) * .00055, -.07, .07);
            offset.Y = Math.Clamp(_mouseEffect.MouseOffset.Y + (cursor.Y - previous.Y) * .00048, -.030, .014);
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

    internal int PoseIndex(bool blink, long now) =>
        _mouseDown || now < _mouseUntil ? 2 : _keyboardDown || now < _keyboardUntil ? 1 : blink ? 3 : 0;

    internal void ShowPose(bool blink, long now = 0)
    {
        double stroke = Math.Clamp((_keyboardUntil - now) / 85d, 0, 1) * .020;
        _keyEffect.KeyStroke = stroke * .35;
        var cap = _lights.ActiveTarget();
        Point target = cap is Point point
            ? new Point((point.X - _rig.Tip.X) / 400, (point.Y - 3 - _rig.Tip.Y) / 400)
            : new Point();
        double blend = _lastFrame == 0 ? 1 : 1 - Math.Exp(-Math.Clamp(now - _lastFrame, 0, 150) / 26d);
        _lastFrame = now;
        var previous = KeyboardTravel;
        if ((target - previous).Length < .0005) _keyEffect.MouseOffset = target;
        else _keyEffect.MouseOffset = previous + (target - previous) * blend;
        if (_keyboardDown || _mouseDown) _quietAfter = Math.Max(_quietAfter, now + 750);
        double click = _mouseDown || now < _mouseUntil ? .005 : 0;
        _mouseEffect.KeyStroke = click;
        var mouse = MouseTravel;
        _lights.MoveMouse(new Point(mouse.X, mouse.Y + click));
        ImageSource face = PoseIndex(blink, now) == 3 && now >= _quietAfter ? _idleExpression : _neutral;
        if (!ReferenceEquals(_artwork.Source, face)) _artwork.Source = face;
    }

    internal void Clear()
    {
        SetButtons(false, false);
        _keyboardUntil = _mouseUntil = 0;
        _lastCursor = null;
        _quietAfter = _lastFrame = 0;
        _mouseEffect.MouseOffset = _keyEffect.MouseOffset = new Point();
        _lights.MoveMouse(new Point());
        _lights.Clear();
        ShowPose(false);
    }
}
