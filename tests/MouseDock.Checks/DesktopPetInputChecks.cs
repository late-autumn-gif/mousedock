using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Services;
using MouseDock.Views;
using MouseDock.Controls;

internal static class DesktopPetInputChecks
{
    // Run explicitly on an interactive desktop. Deliver harmless inputs to our blank foreground
    // window while the real pet receives background WM_INPUT and renders on its normal timer.
    internal static void RunLive()
    {
        using var fixture = new AppearanceFixture();
        fixture.Settings.DesktopPetBlink = fixture.Settings.DesktopPetRest = fixture.Settings.DesktopPetRandomActions = false;
        var pet = new DesktopPetWindow(fixture.Battery, fixture.Settings);
        var target = new Window { Title = "MouseDock input check", Width = 180, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.White, Topmost = true };
        var oldCursor = System.Windows.Forms.Cursor.Position;
        try
        {
            pet.Show();
            target.Show();
            target.Activate();
            target.UpdateLayout();
            var hwnd = new WindowInteropHelper(target).Handle;
            var scene = (BongoPetScene)typeof(DesktopPetWindow).GetField("_bongo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            var art = (Image)scene.Children[0];
            void Pump(int ms)
            {
                var loop = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                timer.Tick += (_, _) => { timer.Stop(); loop.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(loop);
            }
            void Guard() => Require(GetForegroundWindow() == hwnd, "Input test requires its own foreground window.");
            void WaitForIdle(ImageSource expected)
            {
                long deadline = Environment.TickCount64 + 1000;
                do { Pump(30); } while (scene.PoseIndex(false, Environment.TickCount64) != 0 && Environment.TickCount64 < deadline);
                Require(scene.PoseIndex(false, Environment.TickCount64) == 0 && art.Source == expected, "After release, the dispatcher must restore the idle frame.");
            }
            var pos = target.PointToScreen(new Point(40, 60));
            System.Windows.Forms.Cursor.Position = new((int)pos.X, (int)pos.Y);
            if (GetForegroundWindow() != hwnd)
            {
                Require(WindowFromPoint(System.Windows.Forms.Cursor.Position) == hwnd, "Foreground fixture must be visible.");
                mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
                mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
            }
            Pump(120);
            Guard();
            var idle = art.Source;

            var offset = scene.MouseTravel;
            // Move toward the centre of the illustrated pad, away from either travel stop.
            int dx = offset.X > 0 ? -32 : 32, dy = offset.Y > 0 ? -20 : 20;
            System.Windows.Forms.Cursor.Position = new((int)pos.X + dx, (int)pos.Y + dy);
            Pump(100);
            Require((scene.MouseTravel.X - offset.X) * Math.Sign(dx) > .014 &&
                (scene.MouseTravel.Y - offset.Y) * Math.Sign(dy) > .008, "A short physical stroke must move the displayed hand visibly.");
            Guard();
            keybd_event(0x87, 0, 0, UIntPtr.Zero); // F24
            try
            {
                Pump(180);
                Require(scene.PoseIndex(false, Environment.TickCount64) == 1 && art.Source == idle,
                    "A background held function key must reach the displayed typing pose.");
            }
            finally { keybd_event(0x87, 0, 2, UIntPtr.Zero); }
            WaitForIdle(idle);
            Guard();
            Require(WindowFromPoint(System.Windows.Forms.Cursor.Position) == hwnd, "Test click must land on the blank fixture.");
            mouse_event(0x20, 0, 0, 0, UIntPtr.Zero); // Middle button down
            try
            {
                Pump(160);
                Require(scene.PoseIndex(false, Environment.TickCount64) == 2 && art.Source == idle, "Background middle click must reach the displayed pose.");
            }
            finally { mouse_event(0x40, 0, 0, 0, UIntPtr.Zero); }
            WaitForIdle(idle);
            var raw = (DesktopPetRawInput)typeof(DesktopPetWindow).GetField("_rawInput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            Guard();
            keybd_event(0x41, 0x1e, 0, UIntPtr.Zero);
            keybd_event(0x44, 0x20, 0, UIntPtr.Zero);
            try
            {
                Pump(140);
                Require(raw.IsScanHeld(0x1e) && raw.IsScanHeld(0x20), "Background A+D must retain both physical cap identities.");
            }
            finally
            {
                keybd_event(0x44, 0x20, 2, UIntPtr.Zero);
                keybd_event(0x41, 0x1e, 2, UIntPtr.Zero);
            }
            Pump(120);
            Require(!raw.IsScanHeld(0x1e) && !raw.IsScanHeld(0x20), "Both physical caps must release.");
        }
        finally
        {
            target.Close();
            pet.Close();
            System.Windows.Forms.Cursor.Position = oldCursor;
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

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

            Require(Toggle(linkage).IsChecked, "New settings must enable linkage on first show.");
            Click(linkage);
            Require(!SettingsStore.Load().DesktopPetInputReaction && Usages(window).Length == 0, "Opt-out must persist and release input.");
            Click(linkage);
            Require(Toggle(linkage).IsChecked, "Pet menu must reflect re-enabled linkage.");

            Require(SettingsStore.Load().DesktopPetInputReaction && fixture.Settings.Clone().DesktopPetInputReaction,
                "The preference must survive save and cloning.");

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
            scene.SetButtons(true, false);
            Require(scene.PoseIndex(false, 0) == 1, "Held key uses the keyboard pose.");
            scene.SetButtons(true, true);
            Require(scene.PoseIndex(true, 0) == 2, "Mouse pose has priority while both inputs are active.");
            scene.SetButtons(true, false);
            Require(scene.PoseIndex(false, 0) == 1, "Mouse release restores the held keyboard pose.");
            scene.Clear();
            Require(scene.PoseIndex(true, 0) == 3, "Idle may blink.");
            long start = Environment.TickCount64;
            var react = type.GetMethod("OnInputActivity", BindingFlags.NonPublic | BindingFlags.Instance)!;
            react.Invoke(pet, [DesktopPetInputActivity.Keyboard, start, 0x1e]);
            Require(scene.PoseIndex(false, start + 30) == 1, "A quick tap remains visible.");
            Require(scene.PoseIndex(false, start + 100) == 0, "Tap expires and restores idle.");
            scene.TrackPointer(new(50, 50), new(0, 0, 101, 101));

            scene.SetButtons(false, true);
            scene.ShowPose(false, start + 60);
            var previousStroke = scene.KeyboardStroke;
            react.Invoke(pet, [DesktopPetInputActivity.Keyboard, start + 62, 0x1e]);
            scene.ShowPose(false, start + 64);
            Require(scene.PoseIndex(false, start + 64) == 2 && scene.KeyboardStroke > previousStroke,
                "Another quick keystroke must visibly depress the keyboard hand while the mouse stays held.");
            pet.Hide();
            Require(Usages(window).Length == 0, "Hiding the pet must unregister input.");
            pet.Show();
            Require(Usages(window).Length == 2 && scene.PoseIndex(false, start + 100) == 0, "Showing the pet must restore input without replaying a tap.");
            pet.WindowState = WindowState.Minimized;
            Require(Usages(window).Length == 0, "Minimizing the pet must unregister input.");
            pet.WindowState = WindowState.Normal;
            Require(Usages(window).Length == 2, "Restoring a minimized pet must register input again.");

            Click(motion);
            react.Invoke(pet, [DesktopPetInputActivity.Keyboard, start + 350, 0x1e]);
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
