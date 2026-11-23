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
        foreach(var id in new[]{"shin-chan","xin-yue-hu","rikka","keqing","dracena"})
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
                var renders = new HashSet<string>();
                for (int pose = 0; pose < 4; pose++)
                {
                    scene.SetButtons(pose == 1, pose == 2);
                    Require(scene.PoseIndex(pose == 3, 0) == pose, "Input selects complete pose.");
                    scene.ShowPose(pose == 3);
                    scene.UpdateLayout();
                    scene.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    int width = BongoPetScene.SceneWidth;
                    int height = BongoPetScene.SceneHeight;
                    var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(scene);
                    var pixels = new byte[width * height * 4];
                    bmp.CopyPixels(pixels, width * 4, 0);
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
                    scene.Clear();
                }
                CheckLights(scene, output, id);
                Console.WriteLine($"PASS Bongo {id}: four poses, independent mouse buttons and physical key lights.");
            }
            finally {pet.Close();}
        }
    }
    private static void CheckLights(BongoPetScene scene, string output, string id)
    {
        Require(PetKeyboardLayout.Caps.Count == 104 && PetKeyboardLayout.Caps.Values.Distinct().Count() == 104,
            "Each physical key requires its own cap.");
        Require(DesktopPetRawInput.PhysicalKey(0x1c, 2, 13) == 0x11c &&
            DesktopPetRawInput.PhysicalKey(0x1c, 0, 13) == 0x1c, "Main and keypad Enter must be independent.");
        const int size = BongoPetScene.SceneWidth;
        byte[] Frame(string? save = null)
        {
            scene.UpdateLayout();
            var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            image.Render(scene);
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
        scene.Tap(DesktopPetInputActivity.Keyboard, 100, 0x1e);
        scene.UpdateLights(210, k => k == 0x1e, false, false);
        var a = Yellow(idleKeys, Frame(), 72, 170);
        scene.Tap(DesktopPetInputActivity.Keyboard, 212, 0x39);
        scene.UpdateLights(215, k => k == 0x1e, false, false);
        var chord = Yellow(idleKeys, Frame("chord"), 72, 170);
        Require(a.Count >= 2 && chord.IsSupersetOf(a) && chord.Count > a.Count + 4,
            "A held letter and a tapped spacebar must both light distinct caps.");
        scene.UpdateLights(400, _ => false, false, false);
        Require(Frame().SequenceEqual(idleKeys), "Released lights must return to the exact original pixels.");
    }
    private static void Require(bool ok,string message) {if(!ok) throw new InvalidOperationException(message);}
}
