using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Views;

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
                    scene.Sample(k => pose == 1 && k == 49 || pose == 2 && k == 1);
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
                }
                Console.WriteLine($"PASS Bongo {id}: four complete poses rendered.");
            }
            finally {pet.Close();}
        }
    }
    private static void Require(bool ok,string message) {if(!ok) throw new InvalidOperationException(message);}
}
