using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;
using MouseDock.Views;

internal static class DesktopPetInputChecks
{
    internal static void Run()
    {
        using var fixture = new AppearanceFixture();
        fixture.Settings.DesktopPetRandomActions = false;
        var pet = new DesktopPetWindow(fixture.Battery, fixture.Settings);
        var type = typeof(DesktopPetWindow);
        IntPtr window = IntPtr.Zero;
        try
        {
            pet.Show();
            window = new WindowInteropHelper(pet).Handle;
            MenuItem Toggle(string header)
            {
                type.GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
                return pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
            }
            void Click(string header) => Toggle(header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            const string linkage = "键鼠动作联动";
            const string motion = "减少动态效果";

            Require(!Toggle(linkage).IsChecked && Usages(window).Length == 0, "Pet must not subscribe to input by default.");
            Click(linkage);
            Require(Toggle(linkage).IsChecked, "Pet menu must reflect the opt-in.");

            Require(SettingsStore.Load().DesktopPetInputReaction && fixture.Settings.Clone().DesktopPetInputReaction,
                "The opt-in must survive save and cloning.");

            Require(DesktopPetRawInput.Classify(1, 0) == DesktopPetInputActivity.Keyboard &&
                DesktopPetRawInput.Classify(1, 2) == DesktopPetInputActivity.Keyboard &&
                DesktopPetRawInput.Classify(1, 1) == DesktopPetInputActivity.None, "Only keyboard press events should trigger the keyboard cue.");
            Require(new ushort[] { 1, 4, 16, 64, 256 }.All(flag => DesktopPetRawInput.Classify(0, flag) == DesktopPetInputActivity.Mouse),
                "Every standard mouse button press should trigger the mouse cue.");
            Require(new ushort[] { 0, 2, 8, 32, 128, 512, 1024, 2048 }.All(flag => DesktopPetRawInput.Classify(0, flag) == DesktopPetInputActivity.None) &&
                DesktopPetRawInput.Classify(2, 0) == DesktopPetInputActivity.None, "Motion, wheel, release and unrelated HID reports must not trigger a cue.");

            if (!SystemParameters.ClientAreaAnimation)
            {
                Require(Usages(window).Length == 0, "The OS reduced-motion preference must prevent registration.");
                return;
            }

            Require(Usages(window).SequenceEqual(new ushort[] { 2, 6 }), "The shown pet must receive background keyboard and mouse input.");

            var overlay = (Canvas)type.GetField("_inputOverlay", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            var react = type.GetMethod("OnInputActivity", BindingFlags.NonPublic | BindingFlags.Instance)!;
            void Pulse(DesktopPetInputActivity activity, long now) => react.Invoke(pet, [activity, now]);
            (bool Left, bool Right) PaintedSides() => RenderSides(pet, overlay);
            long start = Environment.TickCount64;
            Pulse(DesktopPetInputActivity.Keyboard, start);
            Require(PaintedSides() == (true, false), "Keyboard must draw only the left tap.");
            Pulse(DesktopPetInputActivity.Mouse, start + 10);
            Require(PaintedSides() == (true, true), "Mouse must independently draw the right tap.");
            Pulse(DesktopPetInputActivity.Keyboard, start + 100);
            pet.RenderPoseAt(start + 165);
            Require(PaintedSides() == (false, true), "Repeats must not hold a cue open, and each cue must expire independently.");
            pet.RenderPoseAt(start + 300);
            Require(PaintedSides() == (false, false), "Both sides must return to idle.");
            Pulse(DesktopPetInputActivity.Mouse, start + 320);
            pet.Hide();
            Require(Usages(window).Length == 0, "Hiding the pet must unregister input.");
            pet.Show();
            Require(Usages(window).Length == 2 && PaintedSides() == (false, false), "Showing the pet must restore input without replaying a tap.");
            pet.WindowState = WindowState.Minimized;
            Require(Usages(window).Length == 0, "Minimizing the pet must unregister input.");
            pet.WindowState = WindowState.Normal;
            Require(Usages(window).Length == 2, "Restoring a minimized pet must register input again.");

            Click(motion);
            Pulse(DesktopPetInputActivity.Keyboard, start + 350);
            Require(Usages(window).Length == 0 && PaintedSides() == (false, false), "Reduced motion must clear registration and block input responses.");
            Click(motion);
            Click(linkage);
            Require(Usages(window).Length == 0 && !Toggle(linkage).IsChecked, "Turning linkage off must unregister input.");
            Click(linkage);
            Require(Usages(window).Length == 2, "Re-enabling linkage must register input again.");
        }
        finally { pet.Close(); }
        Require(!Registrations().Any(item => item.Window == window),
            "Closing the pet must release its native registrations.");
    }

    private static (bool Left, bool Right) RenderSides(Window pet, Canvas overlay)
    {
        pet.UpdateLayout();
        int width = (int)Math.Ceiling(overlay.ActualWidth);
        int height = (int)Math.Ceiling(overlay.ActualHeight);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(overlay);
        byte[] pixels = new byte[width * height * 4];
        image.CopyPixels(pixels, width * 4, 0);
        bool left = false, right = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 10) continue;
            if (i / 4 % width < width / 2) left = true;
            else right = true;
        }
        return (left, right);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static ushort[] Usages(IntPtr window) => Registrations()
        .Where(item => item.Window == window && item.Page == 1 && (item.Flags & 0x100) != 0)
        .Select(item => item.Usage).Order().ToArray();

    private static RawDevice[] Registrations()
    {
        uint size = (uint)Marshal.SizeOf<RawDevice>();
        uint count = 0;
        Require(GetRegisteredRawInputDevices(null, ref count, size) == 0, "Could not count current input registrations.");
        var devices = new RawDevice[count];
        if (count != 0)
            Require(GetRegisteredRawInputDevices(devices, ref count, size) == count, "Could not query current input registrations.");
        return devices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice
    {
        internal ushort Page;
        internal ushort Usage;
        internal uint Flags;
        internal IntPtr Window;
    }

    [DllImport("user32.dll")]
    private static extern uint GetRegisteredRawInputDevices([Out] RawDevice[]? devices, ref uint count, uint size);
}
