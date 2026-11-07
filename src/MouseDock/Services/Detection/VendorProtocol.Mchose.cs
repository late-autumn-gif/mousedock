using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        internal static bool IsMchoseModern(ushort vid, ushort pid) =>
            vid == 0x3837 && (pid == 0x1014 || pid == 0x1018);

        // 100B = A7 V2 Ultra+（官方型号 4021）；100A = A7 V2 Pro（实测型号 4018）。
        // 两者共用 11/F9 Legacy Feature 查询，只有接收器 PID 和型号码不同。
        internal static bool IsMchoseA7V2(ushort vid, ushort pid) =>
            vid == 0x3837 && (pid == 0x100A || pid == 0x100B);

        internal static string MchoseA7V2ModelName(ushort pid) =>
            pid == 0x100A ? "MCHOSE A7 V2 Pro" : "MCHOSE A7 V2 Ultra+";

        internal static bool IsMchoseA7V2Interface(HidProbe.Node node) =>
            IsMchoseA7V2(node.Vid, node.Pid) &&
            string.Equals(node.Product, MchoseA7V2ModelName(node.Pid), StringComparison.OrdinalIgnoreCase) &&
            node.Caps.UsagePage == 0xFF01 && node.Caps.Usage == 1 &&
            node.Caps.FeatureReportByteLength == 65 &&
            node.Caps.InputReportByteLength == 65 && node.Caps.OutputReportByteLength == 65;

        public static int TryReadMchose(ushort vid, ushort pid) => TryReadMchose(vid, pid, out _, out _);

        public static int TryReadMchose(ushort vid, ushort pid, out bool charging, out string diagnostic)
        {
            charging = false;
            diagnostic = "";
            // Different MHUB generations use different commands. Never probe arbitrary vendor collections.
            if (IsMchoseA7V2(vid, pid))
                return ReadMchoseA7V2().CopyTo(out charging, out diagnostic);
            if (IsMchoseModern(vid, pid)) { diagnostic="MHUB需本实例专用读取，不走旧编号全局枚举"; return -1; }
            if (vid == 0x5253 && pid == 0x1021)
            {
                int level = TryReadMchoseLegacy(vid, pid, out charging);
                diagnostic = level >= 0 ? "MCHOSE Legacy Feature 11/F9" : "MCHOSE Legacy：未取得有效应答（可能休眠或通道不可用）";
                return level;
            }
            return -1;
        }

        private readonly record struct MchoseResult(int Level, bool Charging, string Diagnostic)
        {
            public int CopyTo(out bool charging, out string diagnostic)
            {
                charging = Charging;
                diagnostic = Diagnostic;
                return Level;
            }
        }

        internal static byte[] BuildMchoseModernRequest()
        {
            // Official MHUB v2: report 0x4D, command 0x0900 (device info), empty payload.
            var request = new byte[64];
            request[0] = 0x4D;
            request[1] = 0x01;
            request[2] = 0x01;
            request[5] = 0x09;
            request[8] = 0x08; // XOR of bytes [2..7].
            return request;
        }

        internal static bool TryParseMchoseModern(ReadOnlySpan<byte> report, ushort vid, ushort pid,
            out int level, out bool charging)
        {
            level = -1;
            charging = false;
            if (!IsMchoseModern(vid, pid) || report.Length != 64 || report[0] != 0x4D || report[1] != 1 ||
                report[4] != 0 || report[5] != 9) return false;
            int length = report[3];
            if (length < 13 || length > 55) return false;
            if ((report[2] & 1) != 0)
            {
                byte checksum = 0;
                for (int i = 2; i < 8 + length; i++) checksum ^= report[i];
                if (checksum != report[8 + length]) return false;
            }
            var body = report.Slice(8, length);
            int modelPid = body[2] | body[3] << 8;
            // 1014/1018 are receivers; the payload can identify the paired A7 V3 model instead.
            bool matchesModel = modelPid == 0x1014 || modelPid == 0x1018 ||
                modelPid == 0x4030 || modelPid == 0x4031 || modelPid == 0x4032 ||
                modelPid == 0x403D || modelPid == 0x4033 || modelPid == 0x403A || modelPid == 0x403F;
            if ((body[0] | body[1] << 8) != vid || !matchesModel ||
                body[11] > 1 || body[12] > 100) return false;
            level = body[12];
            charging = body[11] == 1;
            return true;
        }

        internal static bool TryParseMchoseLegacy(ReadOnlySpan<byte> report, ushort vid, ushort pid,
            out int level, out bool charging)
        {
            level = -1;
            charging = false;
            bool a7V2 = IsMchoseA7V2(vid, pid);
            if ((!a7V2 && (vid != 0x5253 || pid != 0x1021)) ||
                report.Length < 21 || report[0] != 0x11 || report[1] != 0xF9)
                return false;
            Span<byte> body = stackalloc byte[19];
            for (int i = 0; i < body.Length; i++) body[i] = (byte)(report[i + 2] ^ 0xFF);
            int bodyVid = body[0] | body[1] << 8;
            // 100B 是 A7 V2 Ultra+，官方型号 4021；100A 是 A7 V2 Pro，本机实测型号 4018。
            // 型号与接收器 PID 绑定，避免同系列其它型号借用这条通道。
            int modelId = body[2] | body[3] << 8;
            int expectedModel = pid == 0x100A ? 0x4018 : 0x4021;
            if (a7V2 && (report.Length != 21 || bodyVid != 0x3837 ||
                modelId != expectedModel || (body[8] & 0x0F) != 9 || body[10] > 1)) return false;
            if ((bodyVid != 0x5253 && bodyVid != 0x3837) ||
                ((body[8] & 7) == 1 && (body[8] & 8) == 0) || body[9] > 100) return false;
            level = body[9];
            charging = body[10] == 1;
            return true;
        }

        internal static byte[] BuildMchoseA7V2Request()
        {
            // Report 11 contains 20 bytes even though another report makes the collection 65 bytes.
            var request = new byte[21];
            request[0] = 0x11;
            request[1] = 0xF9;
            Array.Fill(request, (byte)0xFF, 2, 19);
            return request;
        }

        private static MchoseResult ReadMchoseA7V2()
        {
            string reason = "未找到 A7 V2 系列的 FF01/0001 接口";
            foreach (ushort receiver in new ushort[] { 0x100B, 0x100A })
            {
                string model = MchoseA7V2ModelName(receiver);
                foreach (var node in HidProbe.Enumerate(0x3837, receiver))
                {
                    if (!IsMchoseA7V2Interface(node)) continue;
                    IntPtr raw = CreateFile(node.Path, GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (raw == IntPtr.Zero || raw == new IntPtr(-1))
                    {
                        reason = model + "：无法打开电量接口";
                        continue;
                    }
                    using var handle = new SafeFileHandle(raw, ownsHandle: true);
                    var request = BuildMchoseA7V2Request();
                    reason = model + "：未取得有效状态应答（设备可能休眠或接口被占用）";
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        // 每次重试都重新发查询。本机实测第一次 GetFeature 常拿到缓存/垃圾帧，
                        // 第二次才回有效状态；只在循环外发一次 SetFeature 会让重试永远读到旧内容。
                        if (!HidD_SetFeature(raw, request, request.Length))
                        {
                            reason = model + "：11/F9 电量查询发送失败";
                            Thread.Sleep(80);
                            continue;
                        }
                        Thread.Sleep(100);
                        // 集合的 Feature 报告是 65 字节；HidD_GetFeature 的缓冲区必须与
                        // FeatureReportByteLength 一致，否则直接返回 Win32=87。
                        var response = new byte[Math.Max(21, (int)node.Caps.FeatureReportByteLength)];
                        response[0] = 0x11;
                        if (HidD_GetFeature(raw, response, response.Length) &&
                            TryParseMchoseLegacy(response.AsSpan(0, Math.Min(21, response.Length)),
                                node.Vid, node.Pid, out int level, out bool charging))
                            return new MchoseResult(level, charging, model + " Feature 11/F9");
                    }
                }
            }
            return new MchoseResult(-1, false, "MCHOSE " + reason);
        }

        private static async Task<MchoseResult> ReadMchoseModernAsync(ushort vid, ushort pid)
        {
            string reason = "未找到 FF01/0001 的 64 字节 MHUB v2 接口";
            string lastRejected = "";
            foreach (string path in Paths(vid, pid))
            {
                IntPtr raw = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (raw == IntPtr.Zero || raw == new IntPtr(-1)) continue;
                using var handle = new SafeFileHandle(raw, ownsHandle: true);
                var caps = Caps(raw);
                // FF0B/00C4 is 61 bytes on A7 V3. A 64-byte 0x4D frame belongs only to FF01/0001.
                if (caps.UsagePage != 0xFF01 || caps.Usage != 1 || caps.OutputReportByteLength != 64 ||
                    caps.InputReportByteLength != 64) continue;
                try
                {
                    using var stream = new FileStream(handle, FileAccess.ReadWrite, 64, isAsync: true);
                    using var deadline = new CancellationTokenSource(900);
                    await stream.WriteAsync(BuildMchoseModernRequest(), deadline.Token).ConfigureAwait(false);
                    var response = new byte[64];
                    // Ignore queued notifications and unrelated replies until our bounded deadline.
                    while (!deadline.IsCancellationRequested)
                    {
                        int count = await stream.ReadAsync(response, deadline.Token).ConfigureAwait(false);
                        if (count == 0) break;
                        if (TryParseMchoseModern(response.AsSpan(0, count), vid, pid, out int level, out bool charging))
                            return new MchoseResult(level, charging, "MCHOSE MHUB v2 Output/Input 4D/0900");
                        lastRejected = "；最近未匹配应答=" + Convert.ToHexString(response.AsSpan(0, count));
                        reason = "收到数据，但命令、长度、校验或设备标识不匹配";
                    }
                }
                catch (OperationCanceledException) { reason = "MHUB v2 查询超时（设备可能休眠、驱动占用或未返回有效应答）"; }
                catch (Exception ex) { reason = "MHUB v2 " + ex.GetType().Name + ": " + ex.Message; }
            }
            return new MchoseResult(-1, false, "MCHOSE " + reason + lastRejected);
        }
    }
}

