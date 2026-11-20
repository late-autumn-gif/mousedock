using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

/// <summary>桌前键盘桌宠：场景几何、键帽跟随、鼠标跟随与截图预览。</summary>
internal static class DesktopPetDeskChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static PetInputCue Key(long at, int virtualKey) =>
        new(PetInputKind.Key, virtualKey, 0, PetKeyboardLayout.LabelOf(virtualKey, 0), at);

    internal static void SceneGeometry()
    {
        var keyboard = DesktopPetDeskScene.KeyboardBounds;
        var mouse = DesktopPetDeskScene.MouseBounds;
        var monitor = DesktopPetDeskScene.MonitorBounds;
        Require(DesktopPetDeskScene.DesignWidth > 0 && DesktopPetDeskScene.DesignHeight > 0, "Scene must declare a design size.");
        Require(keyboard.Left >= 0 && keyboard.Right <= DesktopPetDeskScene.DesignWidth, "Keyboard must stay inside the scene.");
        Require(mouse.Left >= keyboard.Right && mouse.Right <= DesktopPetDeskScene.DesignWidth, "Mouse must sit to the right of the keyboard.");
        Require(monitor.Bottom <= keyboard.Top, "Monitor must sit behind the keyboard.");

        int rows = 0;
        double widest = 0;
        var keys = PetKeyboardLayout.Keys;
        Require(keys.Count == 37, "The compact keyboard keeps ten digits, ten QWERTY keys, nine ASDF keys, seven ZXCV keys and a spacebar.");
        for (int i = 0; i < keys.Count; i++)
        {
            var rect = DesktopPetDeskScene.KeyBounds(i);
            Require(!rect.IsEmpty, $"Key {i} must have a rendered rectangle.");
            Require(rect.Left >= keyboard.Left && rect.Right <= keyboard.Right + .01, $"Key {i} must stay within the keyboard base.");
            Require(rect.Top >= keyboard.Top && rect.Bottom <= keyboard.Bottom + .01, $"Key {i} must stay within the keyboard height.");
            rows = Math.Max(rows, keys[i].Row + 1);
            widest = Math.Max(widest, keys[i].Offset + keys[i].Width);
        }
        Require(rows == 5, "The compact keyboard must render five rows.");
        Require(widest <= DesktopPetDeskScene.KeyboardUnits, "Row offsets must stay inside the layout width.");
    }

    internal static void InputFollow()
    {
        using var fixture = new AppearanceFixture();
        var settings = fixture.Settings;
        settings.DesktopPetCharacter = "shin-chan";
        settings.DesktopPetMirrorInput = true;
        settings.DesktopPetMirrorMouse = true;
        var pet = new DesktopPetWindow(fixture.Battery, settings, new Random(413));
        void Settle(long from, long to)
        {
            for (long t = from; t <= to; t += 16) pet.RenderPoseAt(t);
            pet.RenderPoseAt(to);
        }
        try
        {
            pet.Show();
            Flush();
            long now = Environment.TickCount64;
            int[] typed = { 0x51, 0x42, 0x44, 0x4B, 0x55, 0x58 };
            long typedAt = now - 600;
            for (int i = 0; i < typed.Length; i++)
                pet.InputState.Push(Key(typedAt + i * 70, typed[i]));
            Settle(typedAt, now);

            Require(pet.Scene.MonitorText == "QBDKUX", "The pet monitor must show the same characters the user typed.");
            Require(pet.Scene.HighlightedSlot == PetKeyboardLayout.IndexOf(0x58), "The last key must stay highlighted on the pet keyboard.");
            Require(pet.Scene.HighlightedLabel == "X", "The last key label must be available for the floating keycap.");
            Require(pet.Scene.KeyLabelsVisible, "Key labels default to visible.");

            var expected = DesktopPetDeskScene.KeyBounds(PetKeyboardLayout.IndexOf(0x58));
            var left = pet.Scene.LeftHandPoint;
            Require(Math.Abs(left.X - (expected.X + expected.Width / 2)) < 12,
                "X uses the left hand, so the left hand must travel to the X key.");
            var homeRight = DesktopPetDeskScene.KeyBounds(PetKeyboardLayout.IndexOf(0x4A));
            Require(Math.Abs(pet.Scene.RightHandPoint.X - (homeRight.X + homeRight.Width / 2)) < 12,
                "The right hand must stay on its home key while the left hand types.");

            pet.InputState.Push(new PetInputCue(PetInputKind.MouseLeft, 0, 0, "", now - 240));
            Settle(now - 240, now);
            Require(pet.Scene.MouseLeftPressed, "A left click must press the left mouse button on the pet desk.");
            Require(pet.Scene.RightHandPoint.X > DesktopPetDeskScene.KeyboardBounds.Right,
                "The right hand must move to the pet mouse when the user clicks.");

            settings.DesktopPetMirrorMouse = false;
            pet.RenderPoseAt(now);
            Require(!pet.Scene.MouseLeftPressed, "Disabling mouse mirroring must stop the pet click animation.");
            pet.InputState.Push(new PetInputCue(PetInputKind.MouseRight, 0, 0, "", now - 40));
            pet.RenderPoseAt(now);
            Require(!pet.Scene.MouseRightPressed, "A disabled mouse mirror must ignore new button cues.");

            settings.DesktopPetMirrorMouse = true;
            pet.InputState.Push(new PetInputCue(PetInputKind.MouseRight, 0, 0, "", now - 40));
            pet.RenderPoseAt(now);
            Require(pet.Scene.MouseRightPressed && !pet.Scene.MouseLeftPressed, "A right click must press only the right mouse button.");

            // 更新的右手键必须把右手从鼠标位置拉回键盘，不能继续被 700ms 鼠标窗口覆盖。
            settings.DesktopPetReduceMotion = true;
            pet.InputState.Push(new PetInputCue(PetInputKind.MouseRight, 0, 0, "", now - 120));
            pet.InputState.Push(Key(now - 60, 0x4B));
            pet.RenderPoseAt(now);
            var k = DesktopPetDeskScene.KeyBounds(PetKeyboardLayout.IndexOf(0x4B));
            Require(Math.Abs(pet.Scene.RightHandPoint.X - (k.X + k.Width / 2)) < .01,
                "A newer right-hand key must take the right hand back from the mouse.");
            settings.DesktopPetReduceMotion = false;

            settings.DesktopPetShowKeyLabels = false;
            pet.RenderPoseAt(now);
            Require(!pet.Scene.KeyLabelsVisible, "Hiding key labels must reach the scene.");

            settings.DesktopPetShowKeyLabels = true;
            settings.DesktopPetReduceMotion = true;
            pet.InputState.Push(Key(now - 40, 0x58));
            pet.RenderPoseAt(now);
            var x = DesktopPetDeskScene.KeyBounds(PetKeyboardLayout.IndexOf(0x58));
            Require(Math.Abs(pet.Scene.LeftHandPoint.X - (x.X + x.Width / 2)) < .01,
                "Reduced motion must snap the typing hand without animating.");
            settings.DesktopPetReduceMotion = false;
        }
        finally
        {
            pet.Close();
        }
    }

    internal static void SettingsRoundTrip()
    {
        var settings = new AppSettings();
        Require(settings.DesktopPetMirrorInput && settings.DesktopPetMirrorMouse && settings.DesktopPetShowKeyLabels,
            "Fresh settings must default to following keyboard and mouse with visible key labels.");
        var clone = settings.Clone();
        Require(clone.DesktopPetMirrorInput && clone.DesktopPetMirrorMouse && clone.DesktopPetShowKeyLabels,
            "The pet follow switches must survive cloning.");
        settings.DesktopPetMirrorInput = false;
        settings.DesktopPetMirrorMouse = false;
        settings.DesktopPetShowKeyLabels = false;
        var second = settings.Clone();
        Require(!second.DesktopPetMirrorInput && !second.DesktopPetMirrorMouse && !second.DesktopPetShowKeyLabels,
            "Disabled follow switches must survive cloning.");
    }

    /// <summary>
    /// 可选探针：把 F24 通过 keybd_event 注入系统输入队列，确认低级键盘钩子真实收到 OS 输入。
    /// 属于合成输入验证，不能替代真人敲击验收。
    /// </summary>
    internal static void LiveProbe()
    {
        const int f24 = 0x87;
        using var hook = new DesktopPetInputHook();
        hook.Start();
        if (!hook.KeyboardHooked)
        {
            Console.WriteLine($"SKIP keyboard hook unavailable on this desktop (LastError={hook.LastError}).");
            return;
        }
        var window = new Window
        {
            Width = 300, Height = 120, WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false,
            Title = "MouseDock pet input probe",
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "MouseDock 桌宠输入探针（合成输入）", Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap
            }
        };
        try
        {
            window.Show();
            window.Activate();
            Pump(260);
            keybd_event(f24, 0, 0, UIntPtr.Zero);
            keybd_event(f24, 0, 2, UIntPtr.Zero);

            var buffer = new PetInputCue[16];
            bool observed = false;
            long deadline = Environment.TickCount64 + 1800;
            while (Environment.TickCount64 < deadline && !observed)
            {
                Pump(25);
                int count = hook.Drain(buffer);
                for (int i = 0; i < count; i++)
                    if (buffer[i].Kind == PetInputKind.Key && buffer[i].VirtualKey == f24) observed = true;
            }
            Require(observed, "The low-level keyboard hook must receive the synthetic F24 from the OS input path.");
            Console.WriteLine("PASS live keyboard hook received synthetic F24 (injected input; not physical).");

            hook.Stop();
            while (hook.Drain(buffer) > 0) { }
            keybd_event(f24, 0, 0, UIntPtr.Zero);
            keybd_event(f24, 0, 2, UIntPtr.Zero);
            Pump(180);
            Require(hook.Drain(buffer) == 0, "A stopped hook must not keep collecting system input.");
            Console.WriteLine("PASS stopped hook releases system input.");
        }
        finally
        {
            window.Close();
            hook.Dispose();
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    /// <summary>设置 MOUSEDOCK_PET_SCENE_PREVIEW 目录后，把四个角色的桌前桌宠渲染成 PNG 供人工目检。</summary>
    internal static void Preview()
    {
        string? dir = Environment.GetEnvironmentVariable("MOUSEDOCK_PET_SCENE_PREVIEW");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        string[] characters = { "shin-chan", "xin-yue-hu", "rikka", "keqing" };
        int[] typed = { 0x51, 0x42, 0x44, 0x4B, 0x55, 0x58 };
        foreach (string character in characters)
        {
            using var fixture = new AppearanceFixture();
            var settings = fixture.Settings;
            settings.DesktopPetCharacter = character;
            settings.DesktopPetSize = 100;
            var pet = new DesktopPetWindow(fixture.Battery, settings, new Random(413));
            try
            {
                pet.Show();
                Flush();
                long now = Environment.TickCount64;
                Capture(pet, Path.Combine(dir, character + "-idle.png"));

                for (int i = 0; i < typed.Length; i++)
                    pet.InputState.Push(Key(now - 700 + i * 110, typed[i]));
                pet.InputState.Push(new PetInputCue(PetInputKind.MouseLeft, 0, 0, "", now - 120));
                for (int step = 0; step < 8; step++) pet.RenderPoseAt(now + step * 16);
                Flush();
                Capture(pet, Path.Combine(dir, character + "-typing.png"));
            }
            finally
            {
                pet.Close();
            }
        }
    }

    private static void Capture(DesktopPetWindow pet, string output)
    {
        var root = (FrameworkElement)pet.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, root.DesiredSize.Width, root.DesiredSize.Height));
        root.UpdateLayout();
        int width = (int)Math.Ceiling(root.ActualWidth);
        int height = (int)Math.Ceiling(root.ActualHeight);
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
        Console.WriteLine("pet preview: " + output + $" ({width}x{height} @2x)");
    }
}
