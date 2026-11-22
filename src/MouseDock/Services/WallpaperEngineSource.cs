using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MouseDock.Services;

internal sealed record WallpaperAsset(string Video, string Image);
internal sealed record WallpaperContent(string Video, BitmapSource Image);
internal readonly record struct WallpaperMonitor(string DeviceName, int DisplayCount);

internal sealed class WallpaperEngineSource
{
    private const int MaxJsonBytes = 2 * 1024 * 1024;
    private const int MaxPathChars = 4096;
    private (string Path, DateTime Modified, BitmapSource Bitmap) _preview;

    internal async Task<WallpaperContent> ReadAsync(WallpaperMonitor monitor, CancellationToken cancellationToken)
    {
        using var image = OpenRunningExecutable();
        if (image == null || cancellationToken.IsCancellationRequested) return null;
        string executable = image.Name;

        string directory = Path.GetDirectoryName(executable);
        using var config = ReadJson(LocalPath(Path.Combine(directory, "config.json")));
        var (index, configuredFile) = config == null
            ? (monitor.DisplayCount == 1 ? (int?)0 : null, null)
            : SelectMonitor(config.RootElement, Environment.UserName, monitor.DeviceName,
                PhysicalMonitorId(monitor.DeviceName), monitor.DisplayCount);
        if (index is not int monitorIndex) return null;

        string activeFile = await QueryAsync(executable, monitorIndex, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return null;
        // Some Wallpaper Engine builds exit successfully without writing redirected output.
        var asset = ResolveAsset(string.IsNullOrWhiteSpace(activeFile) ? configuredFile : activeFile);
        if (asset == null) return null;
        BitmapSource bitmap = null;
        if (asset.Image != null)
        {
            try
            {
                DateTime modified = File.GetLastWriteTimeUtc(asset.Image);
                if (_preview.Path != asset.Image || _preview.Modified != modified)
                    _preview = (asset.Image, modified, Palette.Load(asset.Image, 1024));
                bitmap = _preview.Bitmap;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return asset.Video != null || bitmap != null ? new WallpaperContent(asset.Video, bitmap) : null;
    }

    internal static (int? Index, string File) SelectMonitor(JsonElement root, string username, string deviceName,
        string physicalId, int displayCount)
    {
        var general = Field(Field(root, username), "general");
        var map = Field(Field(general, "user"), "monitormap");
        string identifier = string.IsNullOrEmpty(physicalId) ? deviceName : physicalId;
        string normalized = identifier?.Replace('\\', '/');
        int? index = null;
        if (map.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(normalized))
        {
            foreach (var entry in map.EnumerateObject())
            {
                if (!string.Equals(entry.Name, normalized, StringComparison.OrdinalIgnoreCase))
                    continue;
                var location = Field(entry.Value, "location");
                if (location.ValueKind == JsonValueKind.Number && location.TryGetInt32(out int value) && value >= 0)
                    index = value;
                break;
            }
        }

        index ??= displayCount == 1 ? 0 : null;
        var selected = Field(Field(general, "wallpaperconfig"), "selectedwallpapers");
        return (index, index is int i
            ? Text(Field(Field(selected, "Monitor" + i.ToString(CultureInfo.InvariantCulture)), "file"))
            : null);
    }

    internal static WallpaperAsset ResolveAsset(string rawPath)
    {
        string path = rawPath?.Trim().TrimStart('\uFEFF').Trim();
        if (path is { Length: >= 2 } && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        path = LocalPath(path);
        if (path == null) return null;
        if (Directory.Exists(path))
            path = LocalPath(Path.Combine(path, "project.json"));
        if (path == null || !File.Exists(path)) return null;
        if (IsImage(path)) return new WallpaperAsset(null, path);

        string directory = Path.GetDirectoryName(path);
        string manifest = LocalPath(Path.Combine(directory, "project.json"));
        using var project = ReadJson(manifest);
        if (IsVideo(path))
            return new WallpaperAsset(path, Preview(project, directory));
        if (project == null) return null;

        string type = Text(Field(project.RootElement, "type"));
        var file = ProjectFile(directory, Text(Field(project.RootElement, "file")));
        if (!string.Equals(manifest, path, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(file, path, StringComparison.OrdinalIgnoreCase) &&
            !(type == "scene" && Path.GetFileName(path).Equals("scene.pkg", StringComparison.OrdinalIgnoreCase)))
            return null;
        string preview = Preview(project, directory);
        if (type == "video" && file != null && IsVideo(file)) return new WallpaperAsset(file, preview);
        return preview != null ? new WallpaperAsset(null, preview) : null;
    }

    private static string Preview(JsonDocument project, string directory)
    {
        if (project == null) return null;
        var preview = ProjectFile(directory, Text(Field(project.RootElement, "preview")));
        return preview != null && IsImage(preview) ? preview : null;
    }

    private static string ProjectFile(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        if (relative.Split('\\', '/').Any(segment => segment.TrimEnd() == "..")) return null;
        var candidate = LocalPath(Path.Combine(directory, relative));
        return candidate != null && candidate.StartsWith(directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) ? candidate : null;
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp";

    private static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".wmv" or ".m4v";

    private static string LocalPath(string path)
    {
        if (path == null || path.Length is < 4 or > MaxPathChars || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || path[2] is not ('\\' or '/') || path.AsSpan(2).ContainsAny(":*?\"<>|\r\n\0"))
            return null;
        try
        {
            string full = Path.GetFullPath(path);
            string drive = Path.GetPathRoot(full);
            if (new DriveInfo(drive).DriveType is not (DriveType.Fixed or DriveType.Removable)) return null;
            // Inspect from the drive down so a junction cannot cause even an attribute read on a remote target.
            string current = drive;
            foreach (string segment in full[drive.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            return full;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static JsonDocument ReadJson(string path)
    {
        if (path == null) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is < 1 or > MaxJsonBytes) return null;
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            int start = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            return JsonDocument.Parse(bytes.AsMemory(start), new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static JsonElement Field(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var result) ? result : default;

    private static string Text(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static FileStream OpenRunningExecutable()
    {
        using var current = Process.GetCurrentProcess();
        foreach (string name in new[] { "wallpaper64", "wallpaper32" })
        {
            var candidates = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in candidates)
                {
                    try
                    {
                        if (process.SessionId != current.SessionId) continue;
                        var path = LocalPath(process.MainModule?.FileName);
                        if (path == null || !Path.GetFileName(path).Equals(name + ".exe", StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Keep the verified executable write/delete-locked until the query exits.
                        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        bool accepted = false;
                        try
                        {
                            accepted = VerifyExecutable(path) && !process.HasExited;
                            if (accepted) return stream;
                        }
                        finally { if (!accepted) stream.Dispose(); }
                    }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException) { }
                }
            }
            finally { foreach (var process in candidates) process.Dispose(); }
        }
        return null;
    }

    private static bool VerifyExecutable(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).ProductName == "Wallpaper Engine" &&
                WallpaperExecutableTrust.Verify(path);
        }
        catch (Exception e) when (e is Win32Exception or CryptographicException or IOException or UnauthorizedAccessException) { return false; }
    }

    private static async Task<string> QueryAsync(string executable, int index, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (string argument in new[] { "-control", "getWallpaper", "-monitor", index.ToString(CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!process.Start()) return null;
            var output = new char[MaxPathChars + 1];
            int count = await process.StandardOutput.ReadBlockAsync(output.AsMemory(), timeout.Token).ConfigureAwait(false);
            if (count > MaxPathChars) return null;
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? new string(output, 0, count) : null;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or Win32Exception or InvalidOperationException) { return null; }
        finally
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException) { }
        }
    }

    private static string PhysicalMonitorId(string deviceName)
    {
        var data = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        // EDD_GET_DEVICE_INTERFACE_NAME yields the same monitor interface key stored by Wallpaper Engine.
        if (!EnumDisplayDevices(deviceName, 0, ref data, 1) || (data.StateFlags & 1) == 0)
            return null;
        string id = data.DeviceId;
        data.Size = Marshal.SizeOf<DisplayDevice>();
        return EnumDisplayDevices(deviceName, 1, ref data, 1) && (data.StateFlags & 1) != 0 ? null : id;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice data, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
}

internal static class WallpaperExecutableTrust
{
    internal static bool Verify(string path)
    {
        var file = new TrustFile { Size = Marshal.SizeOf<TrustFile>(), Path = path };
        IntPtr fileInfo = Marshal.AllocHGlobal(file.Size);
        Marshal.StructureToPtr(file, fileInfo, false);
        try
        {
            var data = new TrustData
            {
                Size = Marshal.SizeOf<TrustData>(),
                UiChoice = 2, // WTD_UI_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                FileInfo = fileInfo,
                ProviderFlags = 0x1000 // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
#pragma warning disable SYSLIB0057 // Extracts the signer from a PE, which X509CertificateLoader cannot read.
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var certificate = X509CertificateLoader.LoadCertificate(signer.GetRawCertData());
            return certificate.GetNameInfo(X509NameType.SimpleName, false) == "Skutta Software GmbH";
        }
        finally
        {
            Marshal.DestroyStructure<TrustFile>(fileInfo);
            Marshal.FreeHGlobal(fileInfo);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr FileHandle, Subject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public int Size;
        public IntPtr CallbackData, SipClientData;
        public int UiChoice, RevocationChecks, UnionChoice;
        public IntPtr FileInfo;
        public int StateAction;
        public IntPtr StateData, Url;
        public int ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }
}
