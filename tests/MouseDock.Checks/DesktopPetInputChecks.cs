using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Services;
using MouseDock.Views;
using MouseDock.Controls;

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

            var scene = (BongoPetScene)type.GetField("_bongo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            Require(scene.ActualWidth == 176 && scene.ActualHeight == 176,
                "The default pet should fit a compact square beside the battery card.");
            scene.Sample(k => k == 49);
            Require(scene.PoseIndex(false, 0) == 1, "Held key uses the keyboard pose.");
            scene.Sample(k => k == 49 || k == 1);
            Require(scene.PoseIndex(true, 0) == 2, "Mouse pose has priority while both inputs are active.");
            scene.Sample(k => k == 49);
            Require(scene.PoseIndex(false, 0) == 1, "Mouse release restores the held keyboard pose.");
            scene.Clear();
            Require(scene.PoseIndex(true, 0) == 3, "Idle may blink.");
            long start = Environment.TickCount64;
            var react = type.GetMethod("OnInputActivity", BindingFlags.NonPublic | BindingFlags.Instance)!;
            react.Invoke(pet, [DesktopPetInputActivity.Keyboard, start]);
            Require(scene.PoseIndex(false, start + 30) == 1, "A quick tap remains visible.");
            Require(scene.PoseIndex(false, start + 100) == 0, "Tap expires and restores idle.");
            pet.Hide();
            Require(Usages(window).Length == 0, "Hiding the pet must unregister input.");
            pet.Show();
            Require(Usages(window).Length == 2 && scene.PoseIndex(false, start + 100) == 0, "Showing the pet must restore input without replaying a tap.");
            pet.WindowState = WindowState.Minimized;
            Require(Usages(window).Length == 0, "Minimizing the pet must unregister input.");
            pet.WindowState = WindowState.Normal;
            Require(Usages(window).Length == 2, "Restoring a minimized pet must register input again.");

            Click(motion);
            react.Invoke(pet, [DesktopPetInputActivity.Keyboard, start + 350]);
            Require(Usages(window).Length == 0 && scene.PoseIndex(false, start + 360) == 0, "Reduced motion must clear registration and block input responses.");
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
