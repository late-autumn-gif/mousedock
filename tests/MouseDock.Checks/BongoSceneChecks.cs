using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Views;
using MouseDock.Services;

internal static class BongoSceneChecks
{
    internal static void Run(string output)
    {
        using var fixture = new AppearanceFixture();
        Directory.CreateDirectory(output);
        fixture.Settings.DesktopPetDeskMode = true;
        fixture.Battery.VisibleDevices[0].Name = "MCHOSE A7 V3 Pro";
        fixture.Battery.VisibleDevices.Add(new MouseDock.Models.DeviceInfo { Id = "key", Name = "VGN Neon75 Air", Kind = MouseDock.Models.DeviceKind.Keyboard, Level = 79, IsStale = true });
        fixture.Battery.VisibleDevices.Add(new MouseDock.Models.DeviceInfo { Id = "unknown", Name = "ZhuAudio ZG2", Kind = MouseDock.Models.DeviceKind.Headset, Level = -1 });
        foreach(var id in new[]{"shin-chan","xin-yue-hu","rikka","keqing"})
        {
            fixture.Settings.DesktopPetCharacter=id;
            var pet=new DesktopPetWindow(fixture.Battery,fixture.Settings);
            try
            {
                pet.Show(); pet.UpdateLayout();
                var timer = (System.Windows.Threading.DispatcherTimer)typeof(DesktopPetWindow)
                    .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                timer.Stop();
                var scene=(BongoPetScene)typeof(DesktopPetWindow).GetField("_bongo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(pet)!;
                if (id == "shin-chan")
                {
                    typeof(DesktopPetWindow).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
                    var skins = pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "桌宠角色"));
                    Require(skins.Items.OfType<MenuItem>().Select(item => item.Header).SequenceEqual(new[] { "蜡笔小新", "心月狐", "小鸟游六花", "刻晴" }),
                        "Only the four supported skins should be selectable.");
                    var header = (Image)typeof(DesktopPetWindow).GetField("_headerPortrait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                    Require(header.Source is CroppedBitmap { SourceRect: { X: 64, Y: 688, Width: 416, Height: 264 } },
                        "Use the smiling ledge pose from the shared portrait atlas.");
                    double scale = header.ActualHeight / 264;
                    double sleeve = header.Margin.Top + (404 - 176) * scale;
                    double fingers = header.Margin.Top + (429 - 176) * scale;
                    Require(sleeve <= 80 && fingers > 80, "The rim must pass between the sleeves and fingertips.");
                    SaveCardPreview(pet, header, output, id);
                }
                if (id == "keqing")
                {
                    var header = (Image)typeof(DesktopPetWindow).GetField("_headerPortrait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                    var art = ((DrawingImage)header.Source).Drawing as DrawingGroup;
                    Require(art?.Children.Count == 4 && art.Children[1] is DrawingGroup face &&
                        face.ClipGeometry.Bounds.Bottom == 185, "Keqing's face is clipped at the card rim.");
                    var card = (Grid)header.Parent;
                    var rim = card.Children.OfType<Border>().Single().TranslatePoint(new Point(), card).Y;
                    var faceBottom = header.Margin.Top + 185 * header.ActualWidth / 320;
                    Require(Math.Abs(faceBottom - rim) < 1, "Keqing's face meets the card rim.");
                    foreach (var glove in art!.Children.Skip(2))
                    {
                        double scale = header.ActualWidth / 320;
                        Require(header.Margin.Top + glove.Bounds.Top * scale < rim &&
                            header.Margin.Top + glove.Bounds.Bottom * scale > rim,
                            "Both gloves overlap the card rim.");
                    }
                    SaveCardPreview(pet, header, output, id);
                }
                var renders = new HashSet<string>();
                for (int pose = 0; pose < 4; pose++)
                {
                    scene.SetButtons(pose == 1, pose == 2);
                    Require(scene.PoseIndex(pose == 3, 0) == pose, "Input selects complete pose.");
                    if (pose == 1) scene.Tap(DesktopPetInputActivity.Keyboard, 0, 0x1e);
                    if (pose == 2) scene.UpdateLights(10, _ => false, true, false);
                    scene.ShowPose(pose == 3, 100);
                    scene.UpdateLayout();
                    scene.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    int width = BongoPetScene.SceneWidth;
                    int height = BongoPetScene.SceneHeight;
                    var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(scene);
                    var pixels = new byte[width * height * 4];
                    bmp.CopyPixels(pixels, width * 4, 0);
                    if (id == "shin-chan" && pose == 3) CheckSmile(scene);
                    Require(renders.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels))),
                        "Every pose must render different pixels.");
                    var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var stream=File.Create(Path.Combine(output,id+"-"+pose+".png")); encoder.Save(stream);

                    var desktop = new System.Drawing.Rectangle(-1000, -500, 2001, 1001);
                    byte[] RenderPointer(int x, int y)
                    {
                        scene.TrackPointer(new System.Drawing.Point(x,y), desktop);
                        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        frame.Render(scene);
                        var bytes = new byte[pixels.Length];
                        frame.CopyPixels(bytes, width * 4, 0);
                        return bytes;
                    }
                    var left = RenderPointer(-1000, 0);
                    var right = RenderPointer(1000, 0);
                    var top = RenderPointer(0, -500);
                    var bottom = RenderPointer(0, 500);
                    Require(!left.SequenceEqual(right) && !top.SequenceEqual(bottom), "Mouse hand tracks both axes.");
                    Require(left.AsSpan(0, width * 4 * height / 2).SequenceEqual(right.AsSpan(0, width * 4 * height / 2)),
                        "The face stays fixed while the mouse moves.");
                    Require(top.AsSpan(width * 4 * 169).SequenceEqual(bottom.AsSpan(width * 4 * 169)),
                        "The front desk rim must stay pixel-identical throughout mouse travel.");
                    scene.Clear();
                }
                CheckLights(scene, output, id);
                scene.Clear();
                foreach (var expression in new[] { PetExpression.Blink, PetExpression.Wink, PetExpression.Dizzy })
                {
                    scene.ShowPose(expression, 1000);
                    scene.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    var frame = new RenderTargetBitmap(BongoPetScene.SceneWidth, BongoPetScene.SceneHeight, 96, 96, PixelFormats.Pbgra32);
                    frame.Render(scene);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    using var file = File.Create(Path.Combine(output, $"{id}-{expression}.png"));
                    encoder.Save(file);
                }
                Console.WriteLine($"PASS Bongo {id}: four poses, independent mouse buttons and physical key lights.");
            }
            finally {pet.Close();}
        }
    }
    private static void SaveCardPreview(DesktopPetWindow pet, Image header, string output, string id)
    {
        var card = (Grid)header.Parent;
        var root = (FrameworkElement)pet.Content;
        var preview = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),
            (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        preview.Render(root);
        var origin = card.TranslatePoint(new Point(), root);
        var crop = new Int32Rect((int)origin.X, (int)origin.Y, (int)card.ActualWidth, (int)card.ActualHeight);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(preview, crop)));
        using var file = File.Create(Path.Combine(output, id + "-card.png"));
        encoder.Save(file);
        if (id == "shin-chan")
        {
            var layoutEncoder = new PngBitmapEncoder();
            layoutEncoder.Frames.Add(BitmapFrame.Create(preview));
            using var layoutFile = File.Create(Path.Combine(output, "layout.png"));
            layoutEncoder.Save(layoutFile);
        }
    }

    private static void CheckSmile(BongoPetScene scene)
    {
        var art = (Image)scene.Children[0];
        var smile = art.Source;
        scene.ShowPose(false, 1000);
        Require(smile is DrawingImage && art.Source is DrawingImage && !ReferenceEquals(smile, art.Source),
            "The anime portrait must use a distinct head for a desk-mode expression.");
        scene.ShowPose(true, 1000);
    }

    private static void CheckLights(BongoPetScene scene, string output, string id)
    {
        Require(PetKeyboardLayout.Caps.Count == 104 && PetKeyboardLayout.Caps.Values.Distinct().Count() == 104,
            "Each physical key requires its own cap.");
        Require(DesktopPetRawInput.PhysicalKey(0x1c, 2, 13) == 0x11c &&
            DesktopPetRawInput.PhysicalKey(0x1c, 0, 13) == 0x1c, "Main and keypad Enter must be independent.");
        const int size = BongoPetScene.SceneWidth;
        var lights = scene.Children.OfType<PetDeskLights>().Single();
        byte[] Frame(string? save = null, bool keyboardOnly = false)
        {
            scene.UpdateLayout();
            var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            image.Render(keyboardOnly ? lights : (Visual)scene);
            var pixels = new byte[size * size * 4];
            image.CopyPixels(pixels, size * 4, 0);
            if (save != null)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(output, id + "-" + save + ".png"));
                encoder.Save(stream);
            }
            return pixels;
        }
        void Start(bool mouse)
        {
            scene.Clear();
            scene.SetButtons(!mouse, mouse);
            scene.TrackPointer(new(50, 50), new(0, 0, 101, 101));
            scene.ShowPose(false, 100);
        }
        // Compare identical poses, so a change in expression cannot pass a lamp test.
        HashSet<int> Yellow(byte[] unlit, byte[] lit, int minX, int maxX)
        {
            var changed = new HashSet<int>();
            for (int y = 130; y < size; y++)
            for (int x = minX; x < maxX; x++)
            {
                int i = (y * size + x) * 4;
                if (lit[i + 2] > 160 && lit[i + 1] > 135 && lit[i + 1] - lit[i] > 55 &&
                    lit[i + 1] - unlit[i + 1] > 30) changed.Add(y * size + x);
            }
            return changed;
        }
        Start(true);
        var idleMouse = Frame();
        scene.Tap(DesktopPetInputActivity.Mouse, 100, 1);
        scene.UpdateLights(110, _ => false, false, false);
        var left = Yellow(idleMouse, Frame("left"), 20, 65);
        scene.UpdateLights(210, _ => false, false, true);
        var right = Yellow(idleMouse, Frame("right"), 20, 65);
        Require(left.Count > 3 && right.Count > 3 && left.Average(i => i % size) + 2 < right.Average(i => i % size),
            "Left and right lights must appear on their respective mouse halves.");
        Start(false);
        var idleKeys = Frame();
        var unlitCaps = Frame(keyboardOnly: true);
        scene.Tap(DesktopPetInputActivity.Keyboard, 100, 0x1e);
        scene.UpdateLights(210, k => k == 0x1e, false, false);
        var a = Yellow(unlitCaps, Frame(keyboardOnly: true), 72, 170);
        scene.Tap(DesktopPetInputActivity.Keyboard, 212, 0x39);
        scene.UpdateLights(215, k => k == 0x1e, false, false);
        scene.ShowPose(false, 215);
        Frame("chord");
        var chord = Yellow(unlitCaps, Frame(keyboardOnly: true), 72, 170);
        Require(a.Count >= 2 && chord.IsSupersetOf(a) && chord.Count > a.Count + 4,
            "A held letter and a tapped spacebar must both light distinct caps.");
        scene.UpdateLights(400, _ => false, false, false);
        scene.ShowPose(false, 400);
        Require(Frame().SequenceEqual(idleKeys), "Released lights must return to the exact original pixels.");
        scene.Tap(DesktopPetInputActivity.Keyboard, 500, 0x1e);
        scene.ShowPose(true, 560);
        Point letterTip = scene.KeyboardFingertip;
        var letter = Frame("letter");
        scene.Tap(DesktopPetInputActivity.Keyboard, 600, 0x11c);
        scene.ShowPose(true, 680);
        var enter = Frame("pad-enter");
        Require((letterTip - lights.KeyCenter(0x1e)).Length < 1.7 &&
            (scene.KeyboardFingertip - lights.KeyCenter(0x11c)).Length < 1.7,
            "The fingertip lands over the center of the physical cap.");
        Require(lights.KeyCenter(0x39).Y < lights.KeyCenter(0x01).Y &&
            lights.KeyCenter(0x11c).X < lights.KeyCenter(0x1e).X,
            "The spacebar and numeric pad must face the pet.");
        scene.SetButtons(false, false);
        scene.ShowPose(true, 850);
        Require(letter.AsSpan(0, size * 4 * 100).SequenceEqual(enter.AsSpan(0, size * 4 * 100)) &&
            Frame().AsSpan(0, size * 4 * 100).SequenceEqual(idleKeys.AsSpan(0, size * 4 * 100)),
            "Typing and its inter-key gap must preserve the neutral face even during an idle smile request.");
        var arm = scene.Children.OfType<PetTypingArm>().Single();
        var rig = PetArmLayers.For(id);
        var firstSpan = rig.Keys[0];
        Vector axis = rig.KeyboardCuff - new Point((firstSpan.Left + firstSpan.Right) / 2d, firstSpan.Y);
        axis.Normalize();
        var normal = new Vector(-axis.Y, axis.X);
        foreach (int scan in PetKeyboardLayout.Caps.Keys)
        {
            scene.Tap(DesktopPetInputActivity.Keyboard, 900, scan);
            scene.ShowPose(false, 900);
            Require((scene.KeyboardFingertip - lights.KeyCenter(scan)).Length < .01,
                $"Key {scan:X} must land correctly even when taps share a timestamp.");
            var (forearm, hand) = arm.Transforms();
            double width = forearm.Determinant / forearm.Transform(axis).Length;
            Require(width > .9 && Math.Abs(forearm.Transform(normal).Length - 1) < .001 &&
                Math.Abs(hand.Transform(new Vector(1, 0)).Length - 1) < .001 &&
                Math.Abs(hand.Transform(new Vector(0, 1)).Length - 1) < .001,
                $"Key {scan:X} must keep the hand rigid and the forearm joint facing forward.");
        }

    }
    private static void Require(bool ok,string message) {if(!ok) throw new InvalidOperationException(message);}
}
