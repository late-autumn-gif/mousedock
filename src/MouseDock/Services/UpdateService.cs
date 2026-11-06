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
    ///   6. 等待旧进程退出，备份并替换程序包文件，失败回滚，成功重启
    ///
    /// 安全约定：
    ///   - 元数据来自GitHub；公开安装包直连失败且有SHA256时才使用GH-Proxy
    ///   - 不做任何遥测，不发送本机信息，请求只带一个固定的 User-Agent
    ///   - 任何一步失败都静默放弃，不打扰用户
    /// </summary>
    internal static partial class UpdateService
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
                var v = typeof(UpdateService).Assembly.GetName().Version;
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
            public string Sha256 { get; set; }

            public override string ToString() => $"{Version} ({AssetName}, {AssetSize / 1024 / 1024} MB)";
        }

        /// <summary>
        /// 检查更新。无网 / 超时 / 解析失败全返回 null，绝不抛异常。
        /// </summary>
        public static Task<UpdateInfo> CheckAsync(CancellationToken ct = default) =>
            CheckLatestAsync(_http,CurrentVersionText,ct);

        internal static async Task<UpdateInfo> CheckLatestAsync(HttpClient client,string currentVersion,CancellationToken ct=default)
        {
            // Public GitHub API can return403 to anonymous desktop clients.
            // The fallback manifest is another asset owned by this same repository,
            // fetched directly from GitHub rather than trusting a mirror's metadata.
            foreach(var url in new[]{ApiLatest,new Uri($"https://github.com/{Owner}/{Repo}/releases/latest/download/update.json")})
            {
                if(ct.IsCancellationRequested)return null;
                try
                {
                    using var response=await client.GetAsync(url,ct).ConfigureAwait(false);
                    if(!response.IsSuccessStatusCode)continue;
                    string json=await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return ParseRelease(json,currentVersion);
                }
                catch { }
            }
            return null;
        }

        internal static UpdateInfo ParseRelease(string json, string currentVersion)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if ((root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) ||
                    (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())) return null;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string ver = tag.TrimStart('v', 'V');
                if (!IsNewer(ver, currentVersion)) return null;
                UpdateInfo best = null;
                int bestRank = 0;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    long size = asset.GetProperty("size").GetInt64();
                    // Exact package names exclude source/audit ZIPs, ARM and x86 builds.
                    int rank = name.Equals($"MouseDock-v{ver}-win-x64.zip", StringComparison.OrdinalIgnoreCase) ? 3 :
                        name.Equals($"MouseDock-v{ver}.zip", StringComparison.OrdinalIgnoreCase) ? 2 :
                        name.Equals("MouseDock.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    if (rank <= bestRank || size <= 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                        uri.Scheme != "https" || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                        !uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                    bestRank = rank;
                    best = new UpdateInfo { Tag=tag, Version=ver, AssetName=name, AssetUrl=url, AssetSize=size,
                        Notes=root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                        Sha256=ReadDigest(asset) };
                }
                return best;
            }
            catch { return null; }
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

        private static string ReadDigest(JsonElement asset)
        {
            string digest=asset.TryGetProperty("digest",out var value)?value.GetString()??"":"";
            return System.Text.RegularExpressions.Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$")?digest.Substring(7).ToLowerInvariant():null;
        }

    }
}
