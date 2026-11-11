using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        internal static bool IsNeon75Air(ushort vid, ushort pid) => vid == 0x391D && pid == 0x2A05;
        internal static bool IsNeon75StatusChannel(ushort vid, ushort pid, int page, int usage, int featureLength) =>
            IsNeon75Air(vid, pid) && page == 0xFFFF && usage == 2 && featureLength == 65;

        internal static byte[] BuildNeon75StatusRequest()
        {
            // Official V HUB RyServe.setStatus(): 64-byte Feature payload,
            // F7 + six zeros + (255 - sum(header) % 256), report ID zero.
            var frame = new byte[65];
            frame[1] = 0xF7;
            frame[8] = 0x08;
            return frame;
        }

        internal static bool TryParseNeon75Status(ReadOnlySpan<byte> frame, out int level, out bool charging)
        {
            level = -1; charging = false;
            if (frame.Length != 65 || frame[0] != 0) return false;
            var body = frame[1..];
            // Captured on the exact receiver after F7: 00 52 00 00 01 01 01...
            // Official fields: battery[1], sleep[3], charging[9]. An empty
            // response is NOT 100%, even though the vendor UI defaults to 100.
            if (body[0] != 0 || body[1] < 1 || body[1] > 100 || body[3] != 0 || body[9] > 1) return false;
            level = body[1]; charging = body[9] == 1;
            return true;
        }

        public static int TryReadNeon75(ushort vid, ushort pid, bool allowQuery, out bool charging, out bool stale)
        {
            charging = false; stale = false;
            if (!IsNeon75Air(vid, pid)) return -1;
            LoadBatteryCache();
            string key = InstanceScope.Current ?? $"{vid:X4}:{pid:X4}";
            var scan = BatteryReplay.Current;
            var trace = scan == null ? null : new BatteryReplay.Trace();
            string instance = InstanceScope.Current;
            string channel = null;
            void Record(string status, int level = -1) =>
                scan?.AddQuery(instance ?? key, channel, "Neon75Feature", status, "VGN Neon75 Air", level, trace);
            foreach (string path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    trace?.Add("open", false, nativeError: Marshal.GetLastWin32Error());
                    continue;
                }
                try
                {
                    var caps = Caps(h);
                    if (!IsNeon75StatusChannel(vid, pid, caps.UsagePage, caps.Usage, caps.FeatureReportByteLength)) continue;
                    channel = path;
                    int validReplies = 0;
                    for (int attempt = 0; attempt < (allowQuery ? 3 : 1); attempt++)
                    {
                        if (allowQuery)
                        {
                            var request = BuildNeon75StatusRequest();
                            bool sent = HidD_SetFeature(h, request, request.Length);
                            trace?.Add("set-feature", sent, raw: request, keep: true,
                                nativeError: sent ? null : Marshal.GetLastWin32Error());
                            if (!sent) continue;
                            Thread.Sleep(20);
                        }
                        var response = new byte[65];
                        bool received = HidD_GetFeature(h, response, response.Length);
                        int? error = received ? null : Marshal.GetLastWin32Error();
                        int level = -1;
                        bool charge = false;
                        bool valid = received && TryParseNeon75Status(response, out level, out charge);
                        if (trace != null)
                        {
                            var evidence = received ? new byte[response.Length] : null;
                            if (received)
                                foreach (int index in new[] { 0, 1, 2, 4, 10 }) evidence[index] = response[index];
                            trace.Add("get-feature", valid, raw: evidence, keep: received,
                                result: !received ? "read-failed" : valid ? "accepted" :
                                    response[4] != 0 ? "asleep-or-not-ready" : "invalid-status-frame",
                                nativeError: error);
                        }
                        if (!valid) continue;
                        if (!allowQuery) { stale = true; Record("Cached"); return level == 100 ? -1 : level; }
                        if (++validReplies < 2) continue;
                        // This receiver reports 100 immediately after cable insertion even when
                        // it reported a lower value before insertion. Keep that raw value in evidence,
                        // but wait for an independently verified full/charging percentage path.
                        if (level == 100)
                        {
                            Record("Reported100Unverified", level);
                            if (_lastGood.TryGetValue(key, out int priorLevel) && priorLevel is >= 0 and < 100)
                            { stale = true; return priorLevel; }
                            return -1;
                        }
                        charging = charge;
                        _lastGood[key] = level;
                        SaveBatteryCache();
                        Record(charge ? "Charging" : "Online", level);
                        return level;
                    }
                }
                catch (Exception ex) { trace?.Add("exception", false, result: ex.GetType().Name); }
                finally { CloseHandle(h); }
            }
            Record(channel == null ? "NoMatchingChannel" : "NoValidatedReply");
            if (_lastGood.TryGetValue(key, out int previous) && previous >= 0 && previous < 100)
            {
                stale = true;
                return previous;
            }
            return -1;
        }
    }
}
