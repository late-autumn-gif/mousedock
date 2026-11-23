using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;

namespace MouseDock.Controls;

/// <summary>Complete poses keep shoulders, hands and the desk aligned.</summary>
internal sealed class BongoPetScene : Canvas
{
    // The pose sprites are square. A square viewport avoids empty columns next to the battery card.
    internal const int SceneWidth = 176, SceneHeight = 176;
    private readonly Image _artwork;
    private readonly ImageSource[] _poses = new ImageSource[4];
    private readonly PetHandEffect _hands = new();
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
    }

    internal void SetSkin(string id)
    {
        _hands.Anchors = id switch
        {
            "rikka" => new Point(.22, .71),
            "xin-yue-hu" or "dracena" => new Point(.27, .71),
            _ => new Point(.24, .69)
        };
        var atlas = new BitmapImage(new Uri(
            $"pack://application:,,,/MouseDock;component/Assets/Characters/DeskPoses/{id}.png"));
        int width = atlas.PixelWidth / 2;
        int height = atlas.PixelHeight / 2;
        for (int i = 0; i < _poses.Length; i++)
        {
            var pose = new CroppedBitmap(atlas, new Int32Rect(i % 2 * width, i / 2 * height, width, height));
            pose.Freeze();
            _poses[i] = pose;
        }
        var portrait = new CroppedBitmap((BitmapSource)_poses[0],
            new Int32Rect(width / 5, 0, width * 3 / 5, height * 3 / 5));
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
        var offset = new Point((x - .5) * .084, (y - .5) * .044);
        if (_hands.MouseOffset != offset) _hands.MouseOffset = offset;
        if (_artwork.Effect == null) _artwork.Effect = _hands;
    }

    internal void Tap(DesktopPetInputActivity activity, long now)
    {
        if (activity == DesktopPetInputActivity.Keyboard) _keyboardUntil = now + 85;
        if (activity == DesktopPetInputActivity.Mouse) _mouseUntil = now + 85;
    }

    internal int PoseIndex(bool blink, long now) =>
        _mouseDown || now < _mouseUntil ? 2 : _keyboardDown || now < _keyboardUntil ? 1 : blink ? 3 : 0;

    internal void ShowPose(bool blink, long now = 0)
    {
        double stroke = Math.Clamp((_keyboardUntil - now) / 85d, 0, 1) * .020;
        if (_hands.KeyStroke != stroke) _hands.KeyStroke = stroke;
        var pose = _poses[PoseIndex(blink, now)];
        if (!ReferenceEquals(_artwork.Source, pose)) _artwork.Source = pose;
    }

    internal void Clear()
    {
        SetButtons(false, false);
        _keyboardUntil = _mouseUntil = 0;
        _artwork.Effect = null;
        ShowPose(false);
    }
}
