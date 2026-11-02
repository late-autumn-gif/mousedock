using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MouseDock.Services
{
    /// <summary>
    /// 自动更新。无网时静默跳过，联网后从 GitHub Releases 取最新版。
    ///
    /// 流程：
    ///   1. 启动后延迟 10 秒，后台请求 api.github.com 的 latest release
    ///   2. 比对 tag_name 和当前 AssemblyVersion
    ///   3. 有新版 → 通知界面询问用户
    ///   4. 用户同意 → 下载 asset 到 %TEMP%\MouseDock-update\
    ///   5. 解压（zip）或直接落盘（exe）
    ///   6. 写一个 .bat，退出当前进程，由 bat 覆盖 exe 并重启
    ///
    /// 安全约定：
    ///   - 只访问 api.github.com 和 github.com / objects.githubusercontent.com
    ///   - 不做任何遥测，不发送本机信息，请求只带一个固定的 User-Agent
    ///   - 任何一步失败都静默放弃，不打扰用户
    /// </summary>
    internal static class UpdateService
    {
        private const string Owner = "late-autumn-gif";
        private const string Repo  = "mousedock";

        private static readonly Uri ApiLatest =
            new Uri($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // GitHub API 强制要求 User-Agent
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MouseDock", CurrentVersionText));
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return c;
        }

        /// <summary>当前版本，形如 1.2.2。</summary>
        public static string CurrentVersionText
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version;
                if (v == null) return "0.0.0";
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public sealed class UpdateInfo
        {
            public string Tag { get; set; }
            public string Version { get; set; }
            public string AssetName { get; set; }
            public string AssetUrl { get; set; }
            public long   AssetSize { get; set; }
            public string Notes { get; set; }

            public override string ToString() => $"{Version} ({AssetName}, {AssetSize / 1024 / 1024} MB)";
        }

        /// <summary>
        /// 检查更新。无网 / 超时 / 解析失败全返回 null，绝不抛异常。
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                using var resp = await _http.GetAsync(ApiLatest, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
                string tag = tagEl.GetString() ?? "";
                string ver = tag.TrimStart('v', 'V');

                if (!IsNewer(ver, CurrentVersionText)) return null;

                // 优先 zip，其次裸 exe
                string assetName = null, assetUrl = null;
                long assetSize = 0;

                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    string fallbackName = null, fallbackUrl = null;
                    long fallbackSize = 0;

                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        string url  = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        long size   = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            assetName = name; assetUrl = url; assetSize = size;
                            break;
                        }
                        if (fallbackName == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            fallbackName = name; fallbackUrl = url; fallbackSize = size;
                        }
                    }

                    if (assetName == null && fallbackName != null)
                    {
                        assetName = fallbackName; assetUrl = fallbackUrl; assetSize = fallbackSize;
                    }
                }

                if (string.IsNullOrEmpty(assetUrl)) return null;

                string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                return new UpdateInfo
                {
                    Tag = tag, Version = ver,
                    AssetName = assetName, AssetUrl = assetUrl, AssetSize = assetSize,
                    Notes = notes
                };
            }
            catch
            {
                // 无网、DNS 失败、超时、JSON 变形——一律当没有更新
                return null;
            }
        }

        /// <summary>比较版本，remote 比 current 大返回 true。</summary>
        private static bool IsNewer(string remote, string current)
        {
            try
            {
                var r = new Version(Normalize(remote));
                var c = new Version(Normalize(current));
                return r > c;
            }
            catch { return false; }
        }

        private static string Normalize(string v)
        {
            var parts = v.Split('.');
            if (parts.Length == 1) return v + ".0.0";
            if (parts.Length == 2) return v + ".0";
            return v;
        }

        /// <summary>
        /// 下载并应用更新。返回 true 表示已经把替换脚本排上队，调用方应立即退出进程。
        /// </summary>
        public static async Task<bool> DownloadAndApplyAsync(UpdateInfo info, IProgress<int> progress = null,
            CancellationToken ct = default)
        {
            if (info == null || string.IsNullOrEmpty(info.AssetUrl)) return false;

            string work = Path.Combine(Path.GetTempPath(), "MouseDock-update");
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch { /* 目录被占用就换个名字 */ work = Path.Combine(Path.GetTempPath(), "MouseDock-update-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }
            Directory.CreateDirectory(work);

            string downloaded = Path.Combine(work, info.AssetName);

            try
            {
                using var resp = await _http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return false;

                long total = resp.Content.Headers.ContentLength ?? info.AssetSize;
                long done = 0;

                using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                using (var dst = File.Create(downloaded))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                        done += n;
                        if (total > 0) progress?.Report((int)(done * 100 / total));
                    }
                }
            }
            catch { return false; }

            // 找出待替换的 exe
            string newExe = null;
            try
            {
                if (downloaded.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string extract = Path.Combine(work, "x");
                    ZipFile.ExtractToDirectory(downloaded, extract);

                    // zip 里可能嵌套一层目录，优先在顶层找 MouseDock.exe
                    newExe = Path.Combine(extract, "MouseDock.exe");
                    if (!File.Exists(newExe))
                    {
                        var found = Directory.GetFiles(extract, "MouseDock.exe", SearchOption.AllDirectories);
                        if (found.Length > 0) newExe = found[0];
                    }
                }
                else if (downloaded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    newExe = downloaded;
                }
            }
            catch { return false; }

            if (newExe == null || !File.Exists(newExe)) return false;

            // 当前 exe 路径
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe)) return false;

            try
            {
                // 脚本：等一会儿 → 覆盖 → 启动 → 删自己
                string bat = Path.Combine(work, "apply.bat");
                string log = Path.Combine(work, "apply.log");

                string script =
                    "@echo off\r\n" +
                    "chcp 65001 >nul\r\n" +
                    $"echo waiting > \"{log}\"\r\n" +
                    "ping 127.0.0.1 -n 4 >nul\r\n" +
                    $"copy /Y \"{newExe}\" \"{currentExe}\" >> \"{log}\" 2>&1\r\n" +
                    "if errorlevel 1 (\r\n" +
                    "  echo copy failed >> \"" + log + "\"\r\n" +
                    "  exit /b 1\r\n" +
                    ")\r\n" +
                    $"start \"\" \"{currentExe}\"\r\n" +
                    "ping 127.0.0.1 -n 2 >nul\r\n" +
                    $"rd /s /q \"{work}\" 2>nul\r\n";

                File.WriteAllText(bat, script, System.Text.Encoding.Default);

                Process.Start(new ProcessStartInfo
                {
                    FileName = bat,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = work
                });

                return true;
            }
            catch { return false; }
        }
    }
}
