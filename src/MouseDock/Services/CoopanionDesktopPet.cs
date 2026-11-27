using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MouseDock.Services;

internal static class CoopanionDesktopPet
{
    internal const string Executable = "Coopanion.exe";

    internal static string FindExecutable(string savedPath)
    {
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Coopanion", Executable);
        string adjacent = Path.Combine(AppContext.BaseDirectory, "Coopanion", Executable);
        foreach (string path in new[] { savedPath, adjacent, installed })
            if (Path.IsPathFullyQualified(path ?? "") &&
                Path.GetFileName(path).Equals(Executable, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                return path;
        return null;
    }

    internal static void Launch(string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path, WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true
        });
        if (process == null) throw new InvalidOperationException("Coopanion 未能启动。");
    }

    internal static void ExportFigures(string target, IEnumerable<(string Id, string Name)> figures)
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (id, name) in figures)
                AddFigure(zip, id, name);
        }
        archive.Position = 0;
        using var output = File.Create(target);
        archive.CopyTo(output);
    }

    private static void AddFigure(ZipArchive zip, string id, string name)
    {
        string key = "mousedock-" + id;
        using (var manifest = zip.CreateEntry(key + "/figure.json").Open())
        {
            JsonSerializer.Serialize(manifest, new
            {
                manifest = 2, api = 2, id = key, version = "1.1.0",
                name = new { zh = name, en = name },
                about = new { zh = "MouseDock 的全身桌宠", en = "MouseDock desktop pet" },
                author = "MouseDock",
                license = "素材沿用 MouseDock 现有桌宠素材说明",
                credits = new[] { new { role = "素材来源", name = "MouseDock 桌宠图集及原角色权利人" } },
                entry = "figure.js",
                @export = "createMouseDockBody",
                thumb = "thumb.png",
                vocab = Words()
            });
        }

        using (var script = Resource("Assets/Coopanion/figure.js"))
        using (var entry = zip.CreateEntry(key + "/figure.js").Open())
            script.CopyTo(entry);

        string poses = $"Assets/Characters/FreePoses/{id}.png";
        using (var source = Resource(poses))
        using (var entry = zip.CreateEntry(key + "/poses.png", CompressionLevel.NoCompression).Open())
            source.CopyTo(entry);

        using var preview = Resource(poses);
        var atlas = BitmapFrame.Create(preview, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var crop = new CroppedBitmap(atlas, new Int32Rect(0, 0, atlas.PixelWidth / 4, atlas.PixelHeight / 3));
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(crop));
        using var thumb = zip.CreateEntry(key + "/thumb.png", CompressionLevel.NoCompression).Open();
        using var encoded = new MemoryStream();
        png.Save(encoded);
        encoded.Position = 0;
        encoded.CopyTo(thumb);
    }

    private static IEnumerable<object> Words()
    {
        (string Id, string Name)[] motions =
        [
            ("stand", "站立"), ("jump", "跳跃"), ("hop", "轻跳"), ("look", "张望"),
            ("turn", "转身"), ("nod", "点头"), ("shake", "摇头"), ("spin", "转圈"),
            ("sit", "坐下"), ("sleep", "睡觉"), ("dizzy", "晕眩"), ("walk", "走路"),
            ("run", "跑步"), ("wave", "挥手"), ("bow", "鞠躬"), ("shiver", "发抖"),
            ("flap", "扑腾"), ("dance", "跳舞")
        ];
        (string Id, string Name)[] expressions =
        [
            ("neutral", "平静"), ("happy", "开心"), ("wink", "眨眼"), ("love", "喜欢"),
            ("shy", "害羞"), ("surprised", "惊讶"), ("angry", "生气"), ("sad", "难过"),
            ("sleepy", "困倦"), ("thinking", "思考"), ("smug", "得意"), ("pout", "噘嘴"),
            ("worried", "担心"), ("determined", "坚定"), ("flustered", "慌张"),
            ("scared", "害怕"), ("excited", "兴奋"), ("cry", "哭泣"), ("confused", "疑惑")
        ];
        foreach (var (id, name) in motions)
            yield return new { id, kind = "motion", names = new { zh = new[] { name } }, about = new { zh = name, en = id },
                seconds = id switch { "dance" => 3.2, "wave" or "bow" or "flap" or "shiver" => 1.8, "dizzy" => 2.4, _ => 1.0 },
                lasting = id is "stand" or "sit" or "sleep" };
        foreach (var (id, name) in expressions)
            yield return new { id, kind = "expression", names = new { zh = new[] { name } }, about = new { zh = name, en = id }, seconds = 1.0 };
    }

    private static Stream Resource(string path) =>
        Application.GetResourceStream(new Uri($"/MouseDock;component/{path}", UriKind.Relative))?.Stream ??
        throw new FileNotFoundException(path);
}
