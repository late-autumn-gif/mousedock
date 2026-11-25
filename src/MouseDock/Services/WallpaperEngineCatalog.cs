using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MouseDock.Services;

internal sealed record WallpaperChoice(string Path, string Title, string Kind)
{
    internal const string LiveScene = "实时场景";
}

internal sealed partial class WallpaperEngineSource
{
    internal static IReadOnlyList<WallpaperChoice> ReadCatalog(CancellationToken token)
    {
        var installs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { "wallpaper64", "wallpaper32" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (LocalPath(process.MainModule?.FileName) is { } path)
                            installs.Add(Path.GetDirectoryName(path));
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
            }
        }
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam &&
            LocalPath(steam) is { } root)
        {
            libraries.Add(root);
            var vdf = LocalPath(Path.Combine(root, "steamapps", "libraryfolders.vdf"));
            try
            {
                if (vdf != null && new FileInfo(vdf).Length <= MaxJsonBytes)
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), """(?im)^\s*"path"\s*"([^"\r\n]+)"\s*$"""))
                        if (LocalPath(match.Groups[1].Value.Replace(@"\\", @"\")) is { } library)
                            libraries.Add(library);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var library in libraries)
        {
            var installed = LocalPath(Path.Combine(library, "steamapps", "common", "wallpaper_engine"));
            if (installed != null) installs.Add(installed);
        }
        return ScanCatalog(installs, libraries, token);
    }

    internal static IReadOnlyList<WallpaperChoice> ScanCatalog(IEnumerable<string> installs,
        IEnumerable<string> libraries, CancellationToken token)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var install in installs)
        {
            roots.Add(Path.Combine(install, "projects"));
            var steamapps = Directory.GetParent(install)?.Parent;
            if (steamapps?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true)
                roots.Add(Path.Combine(steamapps.FullName, "workshop", "content", "431960"));
            using var config = ReadJson(LocalPath(Path.Combine(install, "config.json")));
            if (config == null) continue;
            var selected = Field(Field(Field(Field(config.RootElement, Environment.UserName), "general"),
                "wallpaperconfig"), "selectedwallpapers");
            if (selected.ValueKind == JsonValueKind.Object)
                foreach (var monitor in selected.EnumerateObject())
                    if (LocalPath(Text(Field(monitor.Value, "file"))) is { } file)
                    {
                        var manifest = Directory.Exists(file) ? Path.Combine(file, "project.json") :
                            Path.Combine(Path.GetDirectoryName(file), "project.json");
                        var projectPath = LocalPath(manifest);
                        paths.Add(projectPath != null && ResolveAsset(projectPath) == ResolveAsset(file) ? projectPath : file);
                    }
        }
        foreach (var library in libraries)
            roots.Add(Path.Combine(library, "steamapps", "workshop", "content", "431960"));

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var root in roots)
        {
            if (LocalPath(root) is not { } localRoot) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(localRoot, "project.json", options).Take(5000))
                {
                    token.ThrowIfCancellationRequested();
                    if (!Path.GetRelativePath(localRoot, path).StartsWith(@"templates\", StringComparison.OrdinalIgnoreCase))
                        paths.Add(path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        var results = new List<WallpaperChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            var asset = ResolveAsset(path);
            if (asset == null || !seen.Add(asset.Video ?? asset.Scene ?? asset.Image)) continue;
            var directory = Path.GetDirectoryName(path);
            using var project = ReadJson(LocalPath(Path.Combine(directory, "project.json")));
            var title = project == null ? null : Text(Field(project.RootElement, "title"));
            var type = project == null ? null : Text(Field(project.RootElement, "type"))?.Trim().ToLowerInvariant();
            var kind = asset.Video != null ? "动态视频" : asset.Scene != null ? WallpaperChoice.LiveScene :
                Path.GetExtension(asset.Image).Equals(".gif", StringComparison.OrdinalIgnoreCase) ? "GIF 预览" :
                type is "scene" or "web" or "application" ? "静态预览" : "静态图片";
            title = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(path).Equals("project", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(directory) : Path.GetFileNameWithoutExtension(path)
                : title.Trim();
            title = string.Join(" ", title.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (title.Length > 90) title = title[..89] + "…";
            results.Add(new WallpaperChoice(path, title, kind));
        }
        return results.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
