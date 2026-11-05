using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 厂商私有 RFCOMM 服务的读取器（第三类耳机）。
    ///
    /// 耳机常用 UUID fe010000-1234-5678-abcd-00805f9b34fb 暴露一个厂商通道，
    /// 但帧格式各家用各家芯片方案（BES / 杰理 / 络达 / 高通）的私有协议。
    ///
    /// 这里做两件事：
    ///   1) 探测模式：连上去，先被动听，再发一组常见查询帧，把原始字节全打出来；
    ///   2) 读取模式：如果 %APPDATA%\MouseDock\vendor-bt.json 里配置了协议（请求帧 + 响应偏移），
    ///      就按配置去问电量。没有配置就返回 -1，绝不猜。
    /// </summary>
    internal static class VendorBt
    {
        private static readonly Guid VendorUuid = new Guid("fe010000-1234-5678-abcd-00805f9b34fb");
        private static readonly Guid SppUuid = new Guid("00001101-0000-1000-8000-00805f9b34fb");

        /// <summary>协议配置：哪一帧去问、响应的第几字节是电量。</summary>
        private sealed class Protocol
        {
            public string Name { get; set; }
            public string RequestHex { get; set; }
            public int ResponseOffset { get; set; }
            public int Scale { get; set; } = 1;      // 1 = 0-100；3 = 0-3 档位
        }

        private static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock", "vendor-bt.json");

        /// <summary>读一次电量：只有配置过协议、且设备回了合法值才返回 ≥0。</summary>
        public static int TryRead(string nameHint)
        {
            try
            {
                var proto = LoadProtocol(nameHint);
                if (proto == null) return -1;
                return ReadWith(nameHint, proto).GetAwaiter().GetResult();
            }
            catch { return -1; }
        }

        // ── 自动学习 ────────────────────────────────────────────────
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _learned = new();

        /// <summary>本机这次运行里，这台设备是否已经自动探测过（避免每 5 秒重复打扰）。</summary>
        public static bool AlreadyProbed(string nameHint) => _learned.ContainsKey(nameHint);

        /// <summary>
        /// 新设备连上时自动跑一次：连厂商服务 → 逐帧探测 → 找到稳定响应就推断电量字段
        /// → 写进 vendor-bt.json → 之后直接按配置读取。
        /// 全部使用极短的只读探测帧，且只在首次连接时执行一次。
        /// </summary>
        public static async Task<int> AutoLearnAsync(string nameHint, Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(nameHint)) return -1;
            if (!_learned.TryAdd(nameHint, 1)) return -1;      // 本次运行只探测一次

            log?.Invoke($"[自动识别] {nameHint}：开始探测厂商通道");

            using var socket = await ConnectAsync(nameHint, 3000).ConfigureAwait(false);
            if (socket == null) { log?.Invoke("[自动识别] 厂商服务不可达，跳过"); return -1; }

            var probes = new[]
            {
                "01", "01 00", "01 01", "01 00 00", "01 01 00",
                "02 01", "02 00 00", "03 00", "05 00", "05 00 01",
                "06 00", "0A 00", "AA 55", "AA 55 01 00", "55 AA 01 00",
                "41 54 0D", "AT+BATT?\r", "AT+BATTERY?\r"
            };

            string bestFrame = null;
            byte[] bestResp = null;

            foreach (var p in probes)
            {
                try
                {
                    var w = new DataWriter(socket.OutputStream);
                    w.WriteBytes(ParseHex(p));
                    await w.StoreAsync().AsTask().ConfigureAwait(false);
                    await w.FlushAsync().AsTask().ConfigureAwait(false);

                    var r = await ReadFor(socket, 400).ConfigureAwait(false);
                    if (r == null || r.Length < 2) continue;

                    log?.Invoke($"[自动识别] 命中帧 {p} → {Hex(r)}");
                    bestFrame = p;
                    bestResp = r;
                    break;
                }
                catch { break; }
            }

            if (bestFrame == null) { log?.Invoke("[自动识别] 无任何响应，保持未知"); return -1; }

            // 再发一次，确认响应稳定（避免把版本号当成电量）
            byte[] again = null;
            try
            {
                var w2 = new DataWriter(socket.OutputStream);
                w2.WriteBytes(ParseHex(bestFrame));
                await w2.StoreAsync().AsTask().ConfigureAwait(false);
                await w2.FlushAsync().AsTask().ConfigureAwait(false);
                again = await ReadFor(socket, 400).ConfigureAwait(false);
            }
            catch { }

            if (again == null || again.Length != bestResp.Length) { log?.Invoke("[自动识别] 响应不稳定，放弃"); return -1; }

            // 找「两次一致、范围 1..100」的字节作为电量候选
            for (int offset = 1; offset < bestResp.Length; offset++)
            {
                if (bestResp[offset] != again[offset]) continue;
                int v = bestResp[offset];
                if (v is >= 1 and <= 100)
                {
                    log?.Invoke($"[自动识别] 候选电量：偏移 {offset} = {v}，写入配置");
                    SaveProtocol(nameHint, bestFrame, offset, 1);
                    try { socket.Dispose(); } catch { }
                    return v;
                }
            }

            log?.Invoke("[自动识别] 响应里没有像电量的字节，保持未知");
            try { socket.Dispose(); } catch { }
            return -1;
        }

        private static void SaveProtocol(string nameHint, string requestHex, int offset, int scale)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var all = File.Exists(ConfigPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, Protocol>>(File.ReadAllText(ConfigPath)) ?? new()
                    : new Dictionary<string, Protocol>();

                all[nameHint] = new Protocol
                {
                    Name = "auto-learned",
                    RequestHex = requestHex,
                    ResponseOffset = offset,
                    Scale = scale
                };

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static Protocol LoadProtocol(string nameHint)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                var all = JsonSerializer.Deserialize<Dictionary<string, Protocol>>(File.ReadAllText(ConfigPath));
                if (all == null) return null;
                if (all.TryGetValue(nameHint, out var exact)) return exact;
                return all.FirstOrDefault(kv => nameHint.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;
            }
            catch { return null; }
        }

        private static async Task<int> ReadWith(string nameHint, Protocol proto)
        {
            using var socket = await ConnectAsync(nameHint, 2500).ConfigureAwait(false);
            if (socket == null) return -1;

            var writer = new DataWriter(socket.OutputStream);
            writer.WriteBytes(ParseHex(proto.RequestHex));
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            await writer.FlushAsync().AsTask().ConfigureAwait(false);

            var resp = await ReadFor(socket, 900).ConfigureAwait(false);
            if (resp == null || resp.Length <= proto.ResponseOffset) return -1;

            int raw = resp[proto.ResponseOffset];
            int pct = proto.Scale <= 1 ? raw : (int)Math.Round(raw * 100.0 / proto.Scale);
            return pct is >= 1 and <= 100 ? pct : -1;
        }

        /// <summary>诊断：连上去把一切能拿到的字节打出来，并尝试常见查询帧。</summary>
        public static async Task<string> ProbeAsync(string nameHint, int listenMs = 2000)
        {
            var sb = new StringBuilder();
            using var socket = await ConnectAsync(nameHint, 3000).ConfigureAwait(false);
            if (socket == null) return "连接失败：厂商服务不可达（可能被系统占用或耳机不支持）";

            sb.AppendLine("已连接厂商服务");

            var passive = await ReadFor(socket, listenMs).ConfigureAwait(false);
            sb.AppendLine(passive == null ? "被动 2 秒：无数据" : "被动收到: " + Hex(passive));

            // 常见 TWS 厂商查询帧（都很短，等价于「问一下状态」）
            string[] probes =
            {
                "01", "01 00", "01 01", "01 00 00", "01 01 00",
                "02 01", "02 00 00", "03 00", "05 00", "05 00 01",
                "06 00", "0A 00", "AA 55", "AA 55 01 00", "55 AA 01 00",
                "41 54 0D", "AT+BATT?\r", "AT+BATTERY?\r"
            };

            foreach (var p in probes)
            {
                try
                {
                    var w = new DataWriter(socket.OutputStream);
                    w.WriteBytes(ParseHex(p));
                    await w.StoreAsync().AsTask().ConfigureAwait(false);
                    await w.FlushAsync().AsTask().ConfigureAwait(false);

                    var r = await ReadFor(socket, 450).ConfigureAwait(false);
                    if (r != null) sb.AppendLine($"  > {p}    < {Hex(r)}");
                }
                catch { break; }
            }

            try { socket.Dispose(); } catch { }
            return sb.ToString();
        }

        private static async Task<StreamSocket> ConnectAsync(string nameHint, int timeoutMs)
        {
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);

            var target = infos.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                                   d.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase));
            if (target == null) return null;

            var device = await BluetoothDevice.FromIdAsync(target.Id).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
            if (device == null) return null;

            var services = await device.GetRfcommServicesAsync().AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);

            var svc = services.Services.FirstOrDefault(s => s.ServiceId.Uuid == VendorUuid)
                   ?? services.Services.FirstOrDefault(s => s.ServiceId.Uuid == SppUuid);
            if (svc == null) return null;

            var socket = new StreamSocket();
            try
            {
                await socket.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask()
                    .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
                return socket;
            }
            catch { socket.Dispose(); return null; }
        }

        private static async Task<byte[]> ReadFor(StreamSocket socket, int ms)
        {
            try
            {
                var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                var load = reader.LoadAsync(256).AsTask();
                if (await Task.WhenAny(load, Task.Delay(ms)).ConfigureAwait(false) != load) return null;
                uint n = await load.ConfigureAwait(false);
                if (n == 0) return null;
                var buf = new byte[n];
                reader.ReadBytes(buf);
                return buf;
            }
            catch { return null; }
        }

        private static byte[] ParseHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<byte>();
            s = s.Replace("\\r", "\r").Replace("\\n", "\n");
            var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<byte>();
            foreach (var p in parts)
            {
                if (p.Length == 2 && byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    list.Add(b);
                else
                    list.AddRange(Encoding.ASCII.GetBytes(p));
            }
            return list.ToArray();
        }

        private static string Hex(byte[] b) =>
            string.Join(" ", b.Take(32).Select(x => x.ToString("X2")));
    }
}
