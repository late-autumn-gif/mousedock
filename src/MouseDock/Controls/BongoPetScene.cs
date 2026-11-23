using System;
using System.Runtime.InteropServices;
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

    internal void Advance(bool enabled)
    {
        if (enabled) Sample(IsDown);
        else
        {
            _keyboardDown = _mouseDown = false;
            _keyboardUntil = _mouseUntil = 0;
        }
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    internal void Sample(Func<int, bool> down)
    {
        _keyboardDown = down(0x20) || AnyDown(down, 0x30, 0x39) || AnyDown(down, 0x41, 0x5A);
        _mouseDown = down(1) || down(2) || down(5) || down(6);
    }

    private static bool AnyDown(Func<int, bool> down, int first, int last)
    {
        for (int key = first; key <= last; key++)
            if (down(key)) return true;
        return false;
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
        var pose = _poses[PoseIndex(blink, now)];
        if (!ReferenceEquals(_artwork.Source, pose)) _artwork.Source = pose;
    }

    internal void Clear()
    {
        Advance(false);
        ShowPose(false);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
