using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MouseDock.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

// Regression checks promoted from the original diagnostic probes.
internal static class AuditChecks
{
    internal static int Run()
    {
        ChargingMerge();
        PollInterval();
        CachedReading();
        FifthMiniCell();
        MiniLiveReading();
        ClosedSettings();
        ClearBackground();
        return 0;
    }

    internal static void DesktopPet()
    {
        foreach (string character in new[] { "shin-chan", "xin-yue-hu", "rikka", "keqing" })
            VerifyDesktopPet(character);
        DesktopPetSizing();
        ChargingLineGeometry();
    }

    internal static void DesktopPetBehaviorSchedule()
    {
        var behavior = new DesktopPetBehavior(new Random(413));
        DesktopPetBehavior.Sample Read(long time, bool pointer = false, bool moving = false,
            bool blocked = false, bool autonomous = true, bool rest = true, bool blink = true) =>
            behavior.Update(time, pointer, moving, blocked, autonomous, rest, blink, 100, 100);
        Require(Read(0).State == DesktopPetBehavior.State.Idle, "Start neutral.");
        bool sawBlink = false, sawWink = false, sawGesture = false;
        for (long t = 33; t < 90000; t += 33)
        {
            var sample = Read(t);
            sawBlink |= sample.Blink;
            sawWink |= sample.Wink;
            sawGesture |= sample.State == DesktopPetBehavior.State.RandomAction;
            Require(Math.Abs(sample.Tilt) <= 4 && Math.Abs(sample.Lift) <= 6, "Autonomous poses stay within render bounds.");
        }
        Require(sawBlink && sawWink && sawGesture, "Seeded schedule exercises blink, wink, and gestures.");
        Require(Read(90000, pointer: true).State == DesktopPetBehavior.State.Interaction, "Pointer interrupts idle activity.");
        Require(Read(90010, moving: true).State == DesktopPetBehavior.State.Moving, "Drag has priority.");
        behavior.Land(90020);
        Require(Read(90020).Lift == -6 && Read(90500).Lift == 0, "Landing starts at the drag elevation and resolves.");
        Require(Read(90510, blocked: true).State == DesktopPetBehavior.State.Transition, "Battery and explicit actions suspend autonomy.");
        Read(100000);
        Require(Read(340000).State == DesktopPetBehavior.State.Sleep, "Long idle enters rest.");
        Require(Read(340010, pointer: true).State == DesktopPetBehavior.State.WakeUp, "Pointer wakes the resting pet.");
        Require(Read(340600, pointer: true).State == DesktopPetBehavior.State.Interaction, "Wake resolves to interaction.");
        behavior.Reset();
        for (long t = 0; t < 260000; t += 93)
            Require(Read(t, autonomous: false, rest: false, blink: false).State == DesktopPetBehavior.State.Idle,
                "Disabling each category keeps the idle state.");
    }

    internal static void DesktopPetRoaming()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        using var fixture = new AppearanceFixture();
        fixture.Settings.DesktopPetDeskMode = false;
        fixture.Settings.DesktopPetRandomActions = true;
        fixture.Settings.DesktopPetX = .5;
        var pet = new DesktopPetWindow(fixture.Battery, fixture.Settings, new Random(71));
        try
        {
            pet.Show();
            var type = typeof(DesktopPetWindow);
            ((DispatcherTimer)type.GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!).Stop();
            var portrait = (Border)type.GetField("_portrait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            var card = (Border)type.GetField("_bubble", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            var lane = (Grid)portrait.Parent;
            void VerifyCardGap()
            {
                pet.UpdateLayout();
                double gap = card.PointToScreen(new Point()).X -
                    lane.PointToScreen(new Point(lane.ActualWidth, 0)).X;
                Require(Math.Abs(gap - 2) < .01, $"Expected a 2-pixel gap, got {gap}.");
            }
            VerifyCardGap();
            var size = type.GetMethod("ChangeSize", BindingFlags.NonPublic | BindingFlags.Instance)!;
            size.Invoke(pet, [125]);
            VerifyCardGap();
            size.Invoke(pet, [100]);
            VerifyCardGap();
            var home = portrait.PointToScreen(new Point());
            var cardHome = card.PointToScreen(new Point());
            long start = Environment.TickCount64;
            var strides = new HashSet<int>();
            bool walked = false;
            for (long t = start; t < start + 95000; t += 40)
            {
                pet.RenderPoseAt(t);
                var position = portrait.PointToScreen(new Point());
                Require(position.X >= lane.PointToScreen(new Point()).X - .5 &&
                    portrait.PointToScreen(new Point(portrait.ActualWidth, 0)).X <= lane.PointToScreen(new Point(lane.ActualWidth, 0)).X + .5,
                    "The character's translated portrait must remain in the clipped lane.");
                Require(card.PointToScreen(new Point()) == cardHome, "Autoplay must leave the battery card fixed.");
                walked |= Math.Abs(position.X - home.X) > 3;
                if (pet.NativeSample.Mode is "walk" or "run") strides.Add(pet.NativeSample.Pose);
            }
            Require(walked && strides.Contains(6) && strides.Contains(7) && fixture.Settings.DesktopPetX == .5,
                "Native locomotion alternates both strides and preserves the saved window anchor.");
            fixture.Settings.DesktopPetLocked = true;
            pet.RenderPoseAt(start + 95000);
            var stopped = portrait.PointToScreen(new Point());
            for (long t = start + 95040; t < start + 106000; t += 40) pet.RenderPoseAt(t);
            Require(portrait.PointToScreen(new Point()) == stopped, "Lock position must stop horizontal movement.");
            fixture.Settings.DesktopPetReduceMotion = true;
            pet.RenderPoseAt(start + 106040);
            Require(pet.NativeSample == null && portrait.PointToScreen(new Point()) == stopped,
                "Reduced motion must stop the engine and preserve the character position.");
            Require(fixture.Settings.Clone().DesktopPetRoamRadius == 96, "Cloning keeps the existing movement radius.");
            fixture.Settings.DesktopPetLocked = false;
            fixture.Settings.DesktopPetReduceMotion = false;
            fixture.Settings.DesktopPetRandomActions = false;
            pet.RenderPoseAt(start + 106080);
            var motion = (CoopanionMotion)type.GetField("_nativeMotion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            var scene = (FreePetScene)type.GetField("_free", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            var overlay = type.GetField("_carryOverlay", BindingFlags.NonPublic | BindingFlags.Instance)!;
            double x = pet.NativeSample!.X;
            double floor = lane.ActualHeight - 1 - BongoPetScene.SceneHeight * (1 - FreePetScene.FootAnchor);
            Require(motion.Pointer("down", x, floor - 108, 100), "The displayed pet accepts a drag near its head.");
            motion.Pointer("move", x, floor - 550, 170);
            pet.RenderPoseAt(start + 106130);
            pet.RenderPoseAt(start + 106180);
            Require(pet.NativeSample.Y < -300 && overlay.GetValue(pet) != null && scene.Opacity == 0,
                "A high drag must display on the overlay outside the original activity lane.");
            var overlaySource = (System.Windows.Interop.HwndSource)typeof(DesktopPetCarryOverlay)
                .GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay.GetValue(pet))!;
            var overlayCanvas = (Canvas)overlaySource.RootVisual;
            var carriedScene = (FreePetScene)overlayCanvas.Children[0];
            Require(carriedScene.UsesLayeredRig, "A mouse drag must activate the live layered rig.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Require(overlayCanvas.ActualWidth >= carriedScene.Width && overlayCanvas.ActualHeight >= carriedScene.Height,
                $"The HWND must retain a drawable viewport while carrying. Actual {overlayCanvas.ActualWidth} × {overlayCanvas.ActualHeight}; sizing {overlaySource.SizeToContent}.");
            Point carriedFoot = carriedScene.PointToScreen(new Point(carriedScene.Width / 2,
                carriedScene.Height * FreePetScene.FootAnchor));
            Require(carriedFoot.Y < lane.PointToScreen(new Point()).Y - 100, "The live sprite is above the battery window.");
            if (Environment.GetEnvironmentVariable("MOUSEDOCK_VERIFY_CARRY_PIXELS") == "1")
                VerifyCarryPixels(overlaySource, carriedScene);
            Require(card.PointToScreen(new Point()) == cardHome, "A high drag leaves the battery card in place.");
            motion.Pointer("up", x, floor - 550, 175);
            pet.RenderPoseAt(start + 106220);
            Require(pet.NativeSample is { Carried: true, Y: < -300, Mode: "air" },
                "Releasing from high up begins the fall at the carried height.");
            bool sawSeatedDizzy = false, sawSeatedRecovery = false;
            for (long t = start + 106260; t < start + 112000; t += 40)
            {
                pet.RenderPoseAt(t);
                sawSeatedDizzy |= pet.NativeSample.Face == "dizzy" && scene.CurrentPose == FreePetScene.Pose.Sit;
                sawSeatedRecovery |= pet.NativeSample is { Face: "squeeze", Mode: "dizzy" } &&
                    scene.CurrentPose == FreePetScene.Pose.SeatedEyesClosed;
            }
            Require(sawSeatedDizzy && sawSeatedRecovery && scene.CurrentPose is FreePetScene.Pose.Stand or FreePetScene.Pose.Blink,
                "A high drop renders the sitting and recovery artwork before the pet returns to standing.");
            Require(overlay.GetValue(pet) == null && scene.Opacity == 1 && card.PointToScreen(new Point()) == cardHome,
                "After landing the overlay closes and the same pet returns to the fixed battery card.");
        }
        finally { pet.Close(); }
    }

    private static void VerifyCarryPixels(System.Windows.Interop.HwndSource source, FreePetScene carriedScene)
    {
        Point center = carriedScene.PointToScreen(new Point(carriedScene.Width / 2, carriedScene.Height / 2));
        const int side = 80;
        var crop = new System.Drawing.Rectangle((int)center.X - side / 2, (int)center.Y - side / 2, side, side);
        Require(System.Windows.Forms.Screen.FromRectangle(crop).Bounds.Contains(crop),
            "The carried sprite sample must be on the display.");
        void Capture(System.Drawing.Bitmap bitmap)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            DwmFlush();
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            IntPtr desktop = GetDC(IntPtr.Zero);
            IntPtr target = graphics.GetHdc();
            try
            {
                const uint sourceCopyWithLayeredWindows = 0x40CC0020;
                Require(BitBlt(target, 0, 0, side, side, desktop, crop.X, crop.Y, sourceCopyWithLayeredWindows),
                    "Could not capture the composited desktop crop.");
            }
            finally
            {
                graphics.ReleaseHdc(target);
                ReleaseDC(IntPtr.Zero, desktop);
            }
        }

        using var shown = new System.Drawing.Bitmap(side, side);
        using var hidden = new System.Drawing.Bitmap(side, side);
        Capture(shown);
        ShowWindow(source.Handle, 0);
        try { Capture(hidden); }
        finally { ShowWindow(source.Handle, 8); }
        int changed = 0;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
                if (shown.GetPixel(x, y) != hidden.GetPixel(x, y)) changed++;
        Require(changed >= side * side / 10, $"The carried character must be composited on screen. Changed pixels: {changed}.");
        Console.WriteLine($"Carry overlay changed {changed} pixels while the mouse remained held.");
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);

    internal static void DesktopPetAutoplay()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        using var fixture = new AppearanceFixture();
        fixture.Settings.DesktopPetDeskMode = false;
        fixture.Settings.DesktopPetRandomActions = true;
        foreach (string character in new[] { "shin-chan", "xin-yue-hu", "rikka", "keqing" })
        {
            fixture.Settings.DesktopPetCharacter = character;
            var pet = new DesktopPetWindow(fixture.Battery, fixture.Settings, new Random(107)) { IsHitTestVisible = false };
            try
            {
                pet.Show();
                long start = Environment.TickCount64;
                var seen = new HashSet<string>();
                if (character == "shin-chan")
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    timer.Tick += (_, _) =>
                    {
                        if (pet.NativeSample != null) seen.Add(pet.NativeSample.Mode);
                        if (Environment.TickCount64 - start < 12500 && seen.All(mode => mode == "idle")) return;
                        timer.Stop();
                        frame.Continue = false;
                    };
                    timer.Start();
                    Dispatcher.PushFrame(frame);
                    Require(seen.Any(mode => mode != "idle"), "The real WPF timer drives native autoplay without input.");
                    Require(Environment.TickCount64 - start >= 4500, "The desktop pet should wait before its first autonomous action.");
                    start = Environment.TickCount64;
                }
                bool gesture = false;
                for (long now = start; now < start + 180000; now += 50)
                {
                    pet.RenderPoseAt(now);
                    seen.Add(pet.NativeSample.Mode);
                    gesture |= pet.NativeSample.Gesture != null;
                }
                Require(seen.Contains("sit") && seen.Contains("sleep") && seen.Contains("air") &&
                    (seen.Contains("walk") || seen.Contains("run")) && gesture,
                    $"{character} should autonomously sit, sleep, jump, travel and gesture. Observed {string.Join(", ", seen)}.");
            }
            finally { pet.Close(); }
        }
    }

    internal static void DesktopPetNativeActions()
    {
        const double scale = 176 / 256d;
        var motion = new CoopanionMotion(new Random(413));
        void Start() => motion.Reset(212, 215, 205, scale, 106, true);
        CoopanionMotion.Sample Advance(double seconds = 1 / 60d) => motion.Advance(seconds, false, true, 1, true);
        Start();
        Require(motion.Pointer("down", 106, 112, 100), "The figure's head must accept the native pointer hit.");
        motion.Pointer("up", 106, 112, 130);
        Require(Advance().Pose != 0, "A poke must produce a native expression or hop preparation.");
        Start();
        Require(motion.Pointer("down", 106, 112, 200), "The figure accepts a grab.");
        motion.Pointer("move", 118, 84, 230);
        Require(Advance(.07).Mode == "drag", "Dragging picks the pet up by the scruff.");
        motion.Pointer("move", 135, 65, 245);
        motion.Pointer("up", 135, 65, 250);
        Require(Advance().Mode == "air", "Releasing hands off to native throw physics.");
        var landed = Advance();
        for (int i = 0; i < 180 && landed.Carried; i++) landed = Advance();
        Require(!landed.Carried && Math.Abs(landed.Y) < .001, "The released pet returns to the floor.");
        void VerifyLanding(string release, double releaseY, string firstFace, int firstPose, double faceDuration)
        {
            Start();
            Require(motion.Pointer("down", 106, 105, 100), "The figure accepts the pointer.");
            motion.Pointer("move", 106, releaseY, 130);
            CoopanionMotion.Sample Tick() => motion.Advance(1 / 60d, false, true, 1, true, .5);
            CoopanionMotion.Sample frame = Tick();
            for (int i = 0; i < 15; i++) frame = Tick();
            Require(frame is { Mode: "drag", Face: "dragged", Pose: 3, Width: < 1, Height: > 1 },
                "Coopanion supplies the held pose, scruff stretch and dragged expression.");
            Require(frame.Sprites.Select(sprite => sprite.Name).SequenceEqual(["drag_eyeL", "drag_eyeR", "drag_mouth"]),
                "The native whale face module supplies the held eyes and mouth.");
            motion.Pointer("move", 106, releaseY, 500);
            motion.Pointer("move", 106, releaseY, 510);
            motion.Pointer(release, 106, releaseY, 520);
            frame = Tick();
            Require(frame is { Mode: "air", Carried: true, Face: "surprised", Pose: 10 },
                "The release preserves native flight and the airborne expression.");
            for (int i = 0; i < 180 && frame.Carried; i++) frame = Tick();
            Require(frame.NativeReaction && frame.Face == firstFace && frame.Pose == firstPose &&
                frame.Dust.Length > 0 && frame.Dust.All(particle => Math.Abs(particle.Y) < 45),
                $"Expected the native {firstFace} pose and impact particles at the floor, got {frame.Mode}/{frame.Face}.");
            double contact = frame.Time - frame.ModeTime;
            if (firstFace != "dizzy")
            {
                while (frame.Mode == "land" && frame.ModeTime < .5) frame = Tick();
                Require(frame is { Mode: "idle" } && frame.Face == firstFace,
                    "Landing settles after roughly 0.4 seconds while the native expression continues.");
            }
            for (int i = 0; i < 210 && frame.Face == firstFace; i++) frame = Tick();
            double elapsed = frame.Time - contact;
            Require(Math.Abs(elapsed - faceDuration) < .07,
                $"Expected the native {firstFace} duration of {faceDuration}s, got {elapsed:F3}s.");
            if (firstFace == "dizzy")
            {
                Require(frame is { Face: "squeeze", Pose: 5, Mode: "dizzy" }, "Recovery starts seated with eyes closed.");
                bool stoodUp = false, shook = false;
                for (int i = 0; i < 50 && frame.Mode == "dizzy"; i++)
                {
                    frame = Tick();
                    stoodUp |= frame is { Face: "squeeze", Pose: 1, Mode: "dizzy" };
                    shook |= Math.Abs(frame.Angle) > 2;
                    if (frame is { Pose: 1, Mode: "dizzy" })
                        Require(frame.ModeTime >= 2.6, "The pet stays seated until the kit starts rising.");
                }
                Require(stoodUp && shook && frame.Pose == 0, "The pet shakes and rises before returning to its standing idle pose.");
                Require(Math.Abs(frame.Time - contact - 3) < .07 && !frame.NativeReaction,
                    "The hard landing recovery ends at the native three-second mark.");
            }
        }
        VerifyLanding("up", 20, "surprised", 10, .9);
        VerifyLanding("drop", 20, "happy", 2, 1.6);
        VerifyLanding("up", -550, "dizzy", 4, 2.4);
        Start();
        motion.Act("dizzy");
        var dizzy = Advance();
        Require(dizzy.Sprites.Select(sprite => sprite.Name).SequenceEqual(["dizzy_eyeL", "dizzy_eyeR", "dizzy_mouth"]),
            "The native whale face module supplies both spiral eyes and the dizzy mouth.");
        Require(Advance(.05).Sprites[0].Angle != dizzy.Sprites[0].Angle, "The spiral orientation follows the native clock.");
        var rigFrames = Enumerable.Range(0, 75).Select(_ => Advance().Rig).ToArray();
        Require(rigFrames.All(rig => double.IsFinite(rig.Neck) && double.IsFinite(rig.ArmNear) &&
            double.IsFinite(rig.ArmFar) && double.IsFinite(rig.Tail) && double.IsFinite(rig.Waist)),
            "The original figure must execute its head, arms, tail and waist deformers.");
        Require(rigFrames.Max(rig => rig.Neck) - rigFrames.Min(rig => rig.Neck) > 2 &&
            rigFrames.Max(rig => rig.ArmNear) - rigFrames.Min(rig => rig.ArmNear) > 1,
            "The original model advances both its neck and arm springs.");
        foreach (string skin in new[] { "shin-chan", "keqing", "rikka", "xin-yue-hu" })
        {
            var scene = new FreePetScene();
            scene.SetSkin(skin);
            scene.Show(FreePetScene.Pose.Sit, 1, 0, false);
            scene.Measure(new Size(scene.Width, scene.Height));
            scene.Arrange(new Rect(0, 0, scene.Width, scene.Height));
            byte[] FacePixels(CoopanionMotion.FaceSprite[]? sprites, CoopanionMotion.RigFrame? rig = null)
            {
                scene.SetReaction(dizzy.Face, dizzy.Time, sprites, rig);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                var bitmap = new RenderTargetBitmap((int)scene.Width, (int)scene.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(scene);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                return pixels;
            }
            Require(!FacePixels(null).SequenceEqual(FacePixels(dizzy.Sprites)),
                $"The eyes and mouth for {skin} must visually change independently of the orbiting stars.");
            var left = rigFrames.MinBy(rig => rig.Neck)!;
            var right = rigFrames.MaxBy(rig => rig.Neck)!;
            Require(!FacePixels(dizzy.Sprites, left).SequenceEqual(FacePixels(dizzy.Sprites, right)),
                $"The original neck spring must move the rendered head for {skin}.");

            var neutral = new CoopanionMotion.RigFrame(0, 0, 4, -2, 0, 0, 0, 0, 0);
            byte[] RigPixels(CoopanionMotion.RigFrame rig, int variant, CoopanionMotion.FaceSprite[]? sprites = null)
            {
                scene.SetReaction(sprites == null ? null : "dragged", 0, sprites, rig, useLayers: true);
                scene.Show(FreePetScene.Pose.Stand, 1, 0, false);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                var bitmap = new RenderTargetBitmap((int)scene.Width, (int)scene.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(scene);
                if (Environment.GetEnvironmentVariable("MOUSEDOCK_RIG_PREVIEW_DIR") is { Length: > 0 } directory)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"{skin}-{variant}.png"));
                    encoder.Save(stream);
                }
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                return pixels;
            }
            var baseline = RigPixels(neutral, 0);
            if (skin == "shin-chan")
            {
                var held = RigPixels(neutral, 4, [new("drag_eyeL", 0, 1), new("drag_eyeR", 0, 1), new("drag_mouth", 0, 1)]);
                bool IsGold(byte[] pixels, int x, int y)
                {
                    int index = ((int)(y * scene.Height / 220) * (int)scene.Width + (int)(x * scene.Width / 220)) * 4;
                    return pixels[index + 2] > 200 && pixels[index + 1] > 145;
                }
                Require(!IsGold(baseline, 90, 85) && IsGold(held, 90, 85) &&
                    !IsGold(baseline, 110, 83) && IsGold(held, 110, 83),
                    "Shin-chan's original pupils must be covered on both sides while he is held.");
            }
            Require(baseline.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 128) > 2000,
                $"The assembled rig for {skin} must render the character silhouette.");
            Require(!baseline.SequenceEqual(RigPixels(neutral with { ArmNear = 65 }, 1)) &&
                !baseline.SequenceEqual(RigPixels(neutral with { LegBack = 18, LegFront = -18 }, 2)) &&
                !baseline.SequenceEqual(RigPixels(neutral with { SkirtX = 7, SkirtY = -4 }, 3)),
                $"Arm, leg and hem channels must independently move rendered layers for {skin}.");
        }
        foreach (var (word, expected) in new[] { ("nod", "nod"), ("shake", "shake"), ("wave", "wave"),
            ("bow", "bow"), ("spin", "spin"), ("shiver", "shiver"), ("flap", "flap") })
        {
            Start();
            motion.Act(word);
            Require(Advance().Gesture == expected, $"The built-in {word} gesture must reach the sprite renderer.");
        }
        foreach (var (word, expected) in new[] { ("sit", "sit"), ("sleep", "sleep"), ("dance", "dance"), ("look", "look"),
            ("dizzy", "dizzy"), ("jump", "crouch"), ("hop", "crouch") })
        {
            Start();
            motion.Act(word);
            Require(Advance().Mode == expected, $"The built-in {word} mode must reach the sprite renderer.");
        }
    }

    internal static void DesktopPetHeaderExpressions()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        using var fixture = new AppearanceFixture();
        fixture.Settings.DesktopPetDeskMode = true;
        foreach (string character in new[] { "shin-chan", "xin-yue-hu", "rikka", "keqing" })
        {
            fixture.Settings.DesktopPetCharacter = character;
            var pet = new DesktopPetWindow(fixture.Battery, fixture.Settings, new Random(413));
            try
            {
                pet.Show();
                var header = (Image)typeof(DesktopPetWindow)
                    .GetField("_headerPortrait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                long start = Environment.TickCount64;
                pet.RenderPoseAt(start);
                var neutral = header.Source;
                var size = header.RenderSize;
                var scene = (BongoPetScene)typeof(DesktopPetWindow)
                    .GetField("_bongo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                var face = scene.Children.OfType<PetFaceEffects>().Single();
                var tilt = (RotateTransform)typeof(DesktopPetWindow)
                    .GetField("_petTilt", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                bool blinked = false;
                for (long tick = start; tick < start + 80000; tick += 33)
                {
                    pet.RenderPoseAt(tick);
                    blinked |= face.Expression == PetExpression.Blink;
                    Require(face.Expression != PetExpression.Smile, "Idle scheduling must never select the smile sprite.");
                }
                Require(blinked, "The dedicated blink expression must still appear.");
                Require(ReferenceEquals(header.Source, neutral) && header.RenderSize == size, "The ledge portrait stays fixed.");

                var play = typeof(DesktopPetWindow).GetMethod("Play", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var dizzy = Enum.Parse(play.GetParameters()[0].ParameterType, "Dizzy");
                play.Invoke(pet, [dizzy, DesktopPetBehavior.DizzyDuration]);
                long began = (long)typeof(DesktopPetWindow)
                    .GetField("_actionStarted", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                pet.RenderPoseAt(began + DesktopPetBehavior.DizzyDuration * 3 / 10);
                Require(face.Expression == PetExpression.Dizzy && tilt.Angle < -1, "Dizziness sways left.");
                pet.RenderPoseAt(began + DesktopPetBehavior.DizzyDuration / 2);
                Require(tilt.Angle > 1, "Dizziness sways right.");

                typeof(DesktopPetWindow).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
                var reduce = pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "减少动态效果"));
                reduce.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Require(fixture.Settings.DesktopPetReduceMotion && tilt.Angle == 0 && face.Expression == PetExpression.Neutral,
                    "The menu clears movement and expression immediately.");
                Require(ReferenceEquals(header.Source, neutral), "Reducing motion must keep the ledge artwork stable.");
                fixture.Settings.DesktopPetReduceMotion = false;
            }
            finally { pet.Close(); }
        }
    }

    private static void SavePortraitPreview(string character, string state, BitmapSource image)
    {
        if (Environment.GetEnvironmentVariable("MOUSEDOCK_PET_EXPRESSIONS_PREVIEW") is not { Length: > 0 } directory)
            return;
        Directory.CreateDirectory(directory);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, $"{character}-{state}.png"));
        png.Save(stream);
    }

    private static void DesktopPetSizing()
    {
        using var fixture = new AppearanceFixture();
        var pet = new DesktopPetWindow(fixture.Battery, fixture.Settings);
        try
        {
            pet.Show();
            var transform = (ScaleTransform)((FrameworkElement)((Panel)pet.Content).Children[0]).LayoutTransform;
            Require(transform.ScaleX == 1 && transform.ScaleY == 1, "The desktop pet must default to 100%.");
            void Scroll(int delta) => pet.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent
            });
            Scroll(120);
            Require(transform.ScaleX == 1.05 && transform.ScaleY == 1.05, "One wheel step must scale the whole pet by five points.");
            for (int i = 0; i < 12; i++) Scroll(120);
            Require(transform.ScaleX == 1.5, "The desktop pet wheel must stop at 150%.");
            for (int i = 0; i < 22; i++) Scroll(-120);
            Require(transform.ScaleX == .5 && SettingsStore.Load().DesktopPetSize == 50,
                "The desktop pet wheel must stop at 50% and save the selected scale.");
        }
        finally { pet.Close(); }
    }

    private static void ChargingLineGeometry()
    {
        var track = new Rect(0, 4, 200, 5);
        DrawingGroup Render(double level, double phase)
        {
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
                ChargeSweep.HorizontalLine(dc, track, level, 2.5, Brushes.Aquamarine, phase);
            return drawing;
        }

        var first = Render(.42, 0);
        var next = Render(.42, .25);
        var higher = Render(.68, .25);
        Require(Math.Abs(first.Bounds.Right - 84) < .01 && first.Bounds.Right == next.Bounds.Right,
            "Charging waves must keep the fill boundary fixed while the reading is unchanged.");
        var firstFill = (GeometryDrawing)((DrawingGroup)first.Children[0]).Children[0];
        var nextFill = (GeometryDrawing)((DrawingGroup)next.Children[0]).Children[0];
        Require(firstFill.Geometry.Bounds.Top >= track.Top - .801,
            "The charging wave crest must remain within the reduced amplitude.");
        Require(firstFill.Geometry.ToString() != nextFill.Geometry.ToString(),
            "A new animation phase must move the surface across the same filled region.");
        Require(Math.Abs(higher.Bounds.Right - 136) < .01 && Render(0, .25).Children.Count == 0,
            "The wave must extend with the new reading and leave unknown or empty readings unfilled.");
    }

    private static void VerifyDesktopPet(string character)
    {
        using var battery = EmptyService(out var settings);
        settings.DesktopPetCharacter = character;
        Require(settings.Clone().DesktopPetCharacter == character, "The selected character must survive settings cloning.");
        var source = new SharedSource { Reading = 62 };
        source.Device.Name = "狼蛛 SC580";
        source.ExtraDevices = new[]
        {
            new DeviceInfo { Id = "pet-device-1", Name = "VGN Neon75 Air", Kind = DeviceKind.Keyboard, Level = 88 },
            new DeviceInfo { Id = "pet-device-2", Name = "Air Plus Pro3", Kind = DeviceKind.Headset, Level = 80 }
        };
        if (character == "shin-chan")
            source.ExtraDevices = source.ExtraDevices.Concat(Enumerable.Range(3, 5).Select(i =>
                new DeviceInfo { Id = $"pet-device-{i}", Name = $"测试设备 {i}", Level = 40 + i })).ToArray();
        SetSource(battery, source);
        Wait(battery.PollAsync());
        var pet = new DesktopPetWindow(battery, settings, new Random(413));
        void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        var image = (Image)typeof(DesktopPetWindow).GetField("_character", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
        DeviceInfo Followed() => (DeviceInfo)typeof(DesktopPetWindow).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
        long ActionUntil() => (long)typeof(DesktopPetWindow).GetField("_actionUntil", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
        try
        {
            pet.Show();
            Flush();
            VerifyPetMenu(pet, character);
            Require(image.Source != null, "Desk artwork must load.");
            if (character == "shin-chan")
            {
                var list = (StackPanel)typeof(DesktopPetWindow).GetField("_deviceList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                var bubble = (Border)typeof(DesktopPetWindow).GetField("_bubble", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                Require(ReferenceEquals(bubble.Child, list), "The battery card should contain the device rows directly.");
                string[] Rows() => list.Children.OfType<Button>().Select(item => ((DeviceInfo)((Grid)item.Content).DataContext).Id).ToArray();
                Require(Rows().Length == 6 && Rows().SequenceEqual(battery.VisibleDevices.Select(d => d.Id)),
                    "The desktop pet must respect the same six-device cap and order as other surfaces.");
                typeof(DesktopPetWindow).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
                var displayMenu = pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "显示设备"));
                var last = (MenuItem)displayMenu.Items[5];
                last.IsChecked = false;
                last.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Flush();
                Require(!battery.Devices[5].Selected && Rows().Length == 6 && Rows().Contains(battery.Devices[6].Id), "The next selected device must replace a deselected row.");
                battery.SetSelected(battery.Devices[5], true);
                Flush();
                Require(Rows().Length == 6 && Rows().Contains(battery.Devices[5].Id), "Settings-side selection must restore the pet row.");
            }
            var header = (Image)typeof(DesktopPetWindow).GetField("_headerPortrait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            Require(header.Source != null && header.IsVisible, "The original card portrait must remain visible next to the full-body pet.");
            source.Device.Charging = true;
            Wait(battery.PollAsync());
            Flush();
            Require(Followed().IsChargingActive, "Charging reaches the followed device.");
            if (Environment.GetEnvironmentVariable("MOUSEDOCK_PET_PREVIEW") is { Length: > 0 } preview)
            {
                Wait(Task.Delay(100));
                var surface = (FrameworkElement)pet.Content;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                string output = Path.Combine(Path.GetDirectoryName(preview) ?? "", $"{Path.GetFileNameWithoutExtension(preview)}-{character}.png");
                using var file = File.Create(output);
                png.Save(file);
            }
            long firstAction = ActionUntil();
            source.Reading = 63;
            Wait(battery.PollAsync());
            Flush();
            Require(ActionUntil() == firstAction, "A percentage change must not replay the plug pose.");
            source.Device.Charging = false;
            source.Device.ChargeFull = true;
            source.Reading = 100;
            Wait(battery.PollAsync());
            Flush();
            long completion = ActionUntil();
            Require(Followed().IsFullActive, "Explicit Full must reach the pet.");
            Wait(battery.PollAsync());
            Flush();
            Require(ActionUntil() == completion, "Repeated full reports must not restart the celebration.");
            source.Device.ChargeFull = false;
            source.Reading = -1;
            Wait(battery.PollAsync());
            Flush();
            Require(Followed() is { IsStale: true, Level: 100 }, "The pet must retain the historical level as stale.");
        }
        finally { pet.Close(); }
        Require(!pet.IsVisible, "Closing the desktop pet must release the window.");
    }

    private static void VerifyPetMenu(DesktopPetWindow pet, string character)
    {
        var menu = pet.ContextMenu;
        typeof(DesktopPetWindow).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
        var sections = menu.Items.Cast<object>().Select(item => item is MenuItem command ? (string)command.Header : "|");
        Require(sections.SequenceEqual(new[] {
            "桌宠角色", "显示设备", "桌宠大小", "|",
            "外观模式", "锁定位置", "减少动态效果", "键鼠动作联动", "满电提醒（全局）", "|",
            "刷新设备", "切换输入设备", "切换输出设备", "|", "设置…", "隐藏桌宠"
        }), $"{character} must expose only the condensed, grouped desktop-pet commands.");
        var characters = (MenuItem)menu.Items[0];
        Require(characters.Items.OfType<MenuItem>().Count(item => item.IsChecked) == 1,
            "The character submenu must identify the currently selected character.");
        Require(((MenuItem)menu.Items[2]).Items.OfType<MenuItem>().Any(item => item.IsChecked),
            "The size submenu must identify the current size when it matches a preset.");
        if (character != "shin-chan") return;
        menu.PlacementTarget = pet;
        menu.IsOpen = true;
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            characters = (MenuItem)menu.Items[0];
            characters.IsSubmenuOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var popup = (System.Windows.Controls.Primitives.Popup?)characters.Template.FindName("PART_Popup", characters);
            var selected = characters.Items.OfType<MenuItem>().Single(item => item.IsChecked);
            var check = (FrameworkElement?)selected.Template.FindName("Check", selected);
            Require(menu.ActualWidth >= 176 && popup is { IsOpen: true } && popup.Child is { IsVisible: true } &&
                check is { IsVisible: true }, "The themed WPF popup must expose the submenu and visible selection indicator.");
            if (Environment.GetEnvironmentVariable("MOUSEDOCK_PET_MENU_PREVIEW") is { Length: > 0 } path &&
                VisualTreeHelper.GetChild(menu, 0) is FrameworkElement frame)
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(frame.ActualWidth), (int)Math.Ceiling(frame.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(path);
                png.Save(file);
            }
        }
        finally { menu.IsOpen = false; }
    }

    private static void VerifyKeqingActions(DesktopPetWindow pet, AppSettings settings, Image image, ImageSource charging)
    {
        var type = typeof(DesktopPetWindow);
        var play = type.GetMethod("Play", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var poses = type.GetNestedType("Pose", BindingFlags.NonPublic)!;
        var actionUntil = type.GetField("_actionUntil", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var renderPose = type.GetMethod("RenderPose", BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var (name, field, count) in new[]
        {
            ("Skill", "_skillFrames", 21),
            ("Burst", "_burstFrames", 37)
        })
        {
            var frames = (ImageSource[])type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            Require(frames.Length == count, "Each action must load its complete embedded animation.");
            play.Invoke(pet, [Enum.Parse(poses, name), count * 50]);
            Require(frames.Contains(image.Source), "The selected action must start its animation.");
            actionUntil.SetValue(pet, Environment.TickCount64 + 25);
            renderPose.Invoke(pet, null);
            Require(ReferenceEquals(image.Source, frames[^1]), "The end of the action must show its final frame.");
            actionUntil.SetValue(pet, 0L);
            renderPose.Invoke(pet, null);
            Require(ReferenceEquals(image.Source, charging), "The action must return to the live charging pose.");
        }

        settings.DesktopPetReduceMotion = true;
        play.Invoke(pet, [Enum.Parse(poses, "Skill"), 1050]);
        Require(ReferenceEquals(image.Source, charging), "Reduced motion must leave the charging art visible.");
        settings.DesktopPetReduceMotion = false;
    }

    internal static void ChargingMerge()
    {
        var assembly = typeof(AppSettings).Assembly;
        var detectedType = assembly.GetType("MouseDock.Services.Detection.DetectedDevice")!;
        dynamic detected = Activator.CreateInstance(detectedType)!;
        detected.Id = "audit";
        detected.Name = "Rechargeable mouse";
        detected.Level = 80;
        detected.Connected = true;
        detected.Charging = true;
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(detectedType))!;
        list.Add(detected);
        dynamic merged = assembly.GetType("MouseDock.Services.Detection.AutoDetectSource")!
            .GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [list])!;
        Console.WriteLine($"AUDIT charging merge: input Charging=True, output Charging={merged.Charging}.");
        Require(merged.Charging, "Merging the detection results discarded the charging flag.");
        detected.Charging = false;
        merged = assembly.GetType("MouseDock.Services.Detection.AutoDetectSource")!
            .GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [list])!;
        Require(!merged.Charging, "A non-charging device must remain non-charging.");
        dynamic second = Activator.CreateInstance(detectedType)!;
        second.Id = "audit";
        second.Connected = true;
        second.Level = -1;
        second.Charging = true;
        list.Add(second);
        merged = assembly.GetType("MouseDock.Services.Detection.AutoDetectSource")!
            .GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [list])!;
        Require(!merged.Charging && merged.Level == 80, "A channel without a reading must not override the active channel.");
    }

    internal static void PollInterval()
    {
        using var battery = EmptyService(out _, 120);
        var source = new SharedSource();
        source.Device.Selected = false;
        SetSource(battery, source);
        for (int i = 0; i < 3; i++) Wait(battery.PollAsync());
        var timer = (DispatcherTimer)typeof(BatteryService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(battery)!;
        Console.WriteLine($"AUDIT refresh interval: configured=120s, unknown unselected device, actual={timer.Interval.TotalSeconds}s after 3 polls.");
        Require(timer.Interval == TimeSpan.FromSeconds(120), "An unreadable device overrode the selected poll interval.");
        source.Device.Selected = true;
        battery.Devices.Single().Selected = true;
        Require(battery.Devices.Single().Selected, "The selected-device scenario was not configured.");
        for (int i = 0; i < 6; i++) Wait(battery.PollAsync());
        Require(timer.Interval == TimeSpan.FromSeconds(120), "Selecting an unreadable device caused endless fast polling.");
    }

    internal static void CachedReading()
    {
        using (var service = EmptyService(out _))
        {
            var source = new SharedSource { Reading = 98, ReuseInstance = true };
            SetSource(service, source);
            int notices = 0;
            service.ChargeCompleted += _ => notices++;
            source.Device.Charging = true;
            Wait(service.PollAsync());
            source.Reading = 100;
            Wait(service.PollAsync());
            Require(notices == 0, "100% while charging must not complete the session.");
            source.Device.Charging = false;
            source.Device.ChargeFull = true;
            Wait(service.PollAsync());
            Wait(service.PollAsync());
            Require(notices == 1 && service.Devices.Single().IsFullActive, "An explicit Full must notify only once.");
            source.Device.ChargeFull = false;
            source.Device.Charging = true;
            source.Reading = 98;
            Wait(service.PollAsync());
            source.Device.Connected = false;
            Wait(service.PollAsync());
            source.Device.Connected = true;
            source.Device.Charging = false;
            source.Reading = -1;
            Wait(service.PollAsync());
            Require(notices == 1 && !service.Devices.Single().IsFullActive, "A reconnect with a cached level cannot notify.");
        }
        foreach (bool reuse in new[] { true, false })
        {
        using var battery = EmptyService(out _);
        var source = new SharedSource { Reading = 70, ReuseInstance = reuse };
        SetSource(battery, source);
        Wait(battery.PollAsync());
        int first = battery.Devices.Single().Level;
        source.Reading = -1;
        source.Device.Name = "狼蛛 2.4G 接收器（型号未确认）";
        Wait(battery.PollAsync());
        Console.WriteLine($"AUDIT cached reading: first={first}%, next read failed, displayed={battery.Devices.Single().Level}, stale={battery.Devices.Single().IsStale}.");
        Require(first == 70 && battery.Devices.Single().Level == 70, "A failed read erased the last valid level.");
        Require(battery.Devices.Single() is { Name: "Audit device", ValueOpacity: 0.45 },
            "A sleeping receiver must preserve the last model name and dim the cached value.");
        Require(source.Device.Level == -1, "The cached display value must not be written back to the source.");
        Wait(battery.PollAsync());
        Wait(battery.PollAsync());
        Require(battery.Devices.Single().Level == 70 && battery.Devices.Single().IsStale,
            "Repeated failures must retain the level and mark it stale.");
        source.Reading = 64;
        source.Device.Name = "Awake mouse";
        Wait(battery.PollAsync());
        Require(battery.Devices.Single() is { Name: "Awake mouse", ValueOpacity: 1 },
            "Fresh detection must replace the retained name and restore brightness.");
        Require(battery.Devices.Single().Level == 64 && !battery.Devices.Single().IsStale,
            "A successful reading must replace the cached level and clear stale state.");
        }
    }

    internal static void FifthMiniCell()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        for (int i = 0; i < 5; i++)
            battery.VisibleDevices.Add(new DeviceInfo { Name = $"Device {i + 1}", Level = 50 });
        var mini = new MiniWindow { Left = -2000, ShowActivated = false };
        try
        {
            mini.Show();
            mini.UpdateLayout();
            var list = (ItemsControl)mini.FindName("CellList");
            var fifth = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(4);
            var bounds = fifth.TransformToAncestor(list).TransformBounds(new Rect(fifth.RenderSize));
            Console.WriteLine($"AUDIT mini fifth cell: item count={list.Items.Count}, list height={list.ActualHeight}, fifth bottom={bounds.Bottom}, overflow={bounds.Bottom > list.ActualHeight}.");
            Require(list.Items.Count == 5 && bounds.Bottom <= list.ActualHeight + 1,
                "The fifth selected device extends outside the mini layout.");
        }
        finally { mini.Close(); }
    }

    private sealed class SharedSource : IBatterySource
    {
        public string Key => "audit";
        public string Label => "audit";
        public DeviceInfo Device { get; } = new DeviceInfo { Id = "shared", Name = "Audit device" };
        public int Reading { get; set; } = -1;
        public DeviceInfo[] ExtraDevices { get; set; } = Array.Empty<DeviceInfo>();
        public bool ReuseInstance { get; set; } = true;
        public IEnumerable<DeviceInfo> Read()
        {
            Device.Level = Reading;
            return new[] { ReuseInstance ? Device : new DeviceInfo
            {
                Id = Device.Id, Name = Device.Name, Level = Reading, Selected = Device.Selected
            } }.Concat(ExtraDevices);
        }
    }

    internal static void MiniLiveReading()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        var device = new DeviceInfo { Name = "Audit mouse", Level = 75 };
        battery.VisibleDevices.Add(device);
        var mini = new MiniWindow { Left = -2000, ShowActivated = false };
        try
        {
            mini.Show();
            mini.UpdateLayout();
            string before = string.Join("|", Texts(mini));
            device.Level = 74;
            mini.Rebuild();
            mini.UpdateLayout();
            Console.WriteLine($"AUDIT mini live reading: before={before}, model={device.MiniText}, rendered={string.Join("|", Texts(mini))}.");
            Require(Texts(mini).Contains("74%") && !Texts(mini).Contains("75%"),
                "The mini window still renders the old percentage.");
            device.Level = -1;
            mini.UpdateLayout();
            Require(Texts(mini).Contains(device.MiniText) && !Texts(mini).Contains("74%"),
                "The mini text did not update when the reading became unknown.");
            device.Connected = false;
            mini.UpdateLayout();
            Require(Texts(mini).Contains(device.MiniText), "The mini text did not update on disconnect.");
        }
        finally { mini.Close(); }
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text.Text;
            foreach (var value in Texts(child)) yield return value;
        }
    }

    internal static void ClosedSettings()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        var window = new SettingsWindow { Left = -2000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Close();
            Require(!window.IsVisible, "Closing settings must hide it.");
            window.Show();
            Require(window.IsVisible, "Settings did not reopen.");
        }
        finally { window.Hide(); }
    }

    internal static void ClearBackground()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        string image = FindRepoAsset("src/MouseDock/Assets/app.png");
        foreach (Window window in new Window[] { new DockWindow(), new MiniWindow() })
        {
            try
            {
                var apply = window.GetType().GetMethod("ApplyBackground")!;
                var root = (Border)window.FindName("Root");
                apply.Invoke(window, [image]);
                Require(root.Background is ImageBrush, "The fixture did not install an image background.");
                apply.Invoke(window, [""]);
                Require(root.Background is LinearGradientBrush,
                    $"{window.GetType().Name} retained its image after clearing the path.");
                window.GetType().GetMethod("ApplyTheme")!.Invoke(window, [ThemePreset.Presets[1], false]);
                var gradient = (LinearGradientBrush)root.Background;
                Require(gradient.GradientStops[0].Color == ThemePreset.Presets[1].BgTop,
                    "The restored background no longer follows theme changes.");
            }
            finally { window.Close(); }
        }
    }

    private static BatteryService EmptyService(out AppSettings settings, int refreshSeconds = 30)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        settings = new AppSettings { Provider = "json", JsonPath = "nonexistent-audit-fixture.json", RefreshSeconds = refreshSeconds };
        return (BatteryService)Activator.CreateInstance(typeof(BatteryService),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object?[] { settings, null, false }, null)!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void SetSource(BatteryService battery, IBatterySource source) =>
        typeof(BatteryService).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(battery, source);

    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    /// <summary>Tests can run from the repo root or from bin; walk up until the asset is found.</summary>
    private static string FindRepoAsset(string relative)
    {
        string direct = Path.GetFullPath(relative);
        if (File.Exists(direct)) return direct;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        return direct;
    }
}
