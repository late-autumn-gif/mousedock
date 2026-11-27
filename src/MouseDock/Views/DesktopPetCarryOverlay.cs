using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MouseDock.Controls;
using MouseDock.Services;

namespace MouseDock.Views;

// A click-through surface for the held/falling sprite. The battery window keeps its original bounds.
internal sealed class DesktopPetCarryOverlay : IDisposable
{
    private const int PopupStyle = unchecked((int)0x80000000);
    private const int TransparentToolNoActivate = 0x080000A0;
    private const uint ShowWithoutActivateOrZOrder = 0x0054;
    private readonly HwndSource _source;
    private readonly FreePetScene _scene = new() { RenderTransformOrigin = new Point(.5, FreePetScene.FootAnchor) };
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotation = new();

    internal DesktopPetCarryOverlay(Window owner, string skin)
    {
        _source = new HwndSource(new HwndSourceParameters("MouseDock pet carry")
        {
            ParentWindow = new WindowInteropHelper(owner).Handle,
            WindowStyle = PopupStyle,
            ExtendedWindowStyle = TransparentToolNoActivate,
            UsesPerPixelOpacity = true,
            // Canvas reports no desired size. Give the HWND explicit bounds so WPF keeps manual sizing.
            Width = 1,
            Height = 1
        });
        _scene.SetSkin(skin);
        _scene.RenderTransform = new TransformGroup { Children = { _scale, _rotation } };
        _source.RootVisual = new Canvas { IsHitTestVisible = false, Children = { _scene } };
    }

    internal bool Show(Point foot, Vector pixelScale, CoopanionMotion.Sample frame, long now)
    {
        // Allow the full scaled and rotated sprite to fit above the foot pivot.
        int radius = (int)Math.Ceiling(2 * Math.Max(BongoPetScene.SceneWidth * pixelScale.X,
            BongoPetScene.SceneHeight * pixelScale.Y));
        if (!SetWindowPos(_source.Handle, IntPtr.Zero, (int)Math.Round(foot.X) - radius,
            (int)Math.Round(foot.Y) - radius, radius * 2, radius * 2, ShowWithoutActivateOrZOrder))
            return false;
        var toDip = _source.CompositionTarget.TransformFromDevice;
        Canvas.SetLeft(_scene, radius * toDip.M11 - BongoPetScene.SceneWidth / 2d);
        Canvas.SetTop(_scene, radius * toDip.M22 - BongoPetScene.SceneHeight * FreePetScene.FootAnchor);
        _scale.ScaleX = Math.Max(.03, Math.Abs(frame.Width)) * pixelScale.X * toDip.M11;
        _scale.ScaleY = frame.Height * pixelScale.Y * toDip.M22;
        _rotation.Angle = frame.Angle;
        bool held = frame.Face == "dragged";
        _scene.SetReaction(frame.Face, frame.Time, frame.Sprites, held ? frame.Rig : null, held);
        _scene.Show((FreePetScene.Pose)frame.Pose, frame.Width < 0 ? -1 : 1, now, false);
        return true;
    }

    public void Dispose() => _source.Dispose();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
