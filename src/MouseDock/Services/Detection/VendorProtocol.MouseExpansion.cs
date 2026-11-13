// Protocol adaptation from MouseDock 1.0.3 and Halo Battery (MIT, 2026 contributors).
// The complete reference permission notices are embedded through THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static bool ExtraMouseCandidate(ushort vid, ushort pid, string product) =>
        ReferenceProfiles.Find(vid, pid) == null &&
        !VgnF1Protocol.IsCandidate(vid, pid, product) &&
        !ZeroRainyProtocol.Candidate(vid, pid, product) &&
        !IsReportedX23Se(vid, pid, product) &&
        ReportedReceiverRoute(vid,pid,product)==null &&
        !IsScopedG7(vid,pid,product) && !IsScopedInphic(vid,pid,product) &&
        !IsMchoseA7V2(vid,pid) &&
        !IsScopedModernMouse(vid, pid, product) &&
        !IsRogOmni(vid,pid) && !(vid==0x0B05&&pid==0x1AD0) &&
        !M9ProProtocol.IsCandidate(vid, pid, product) &&
        MouseExpansionFamily(vid, pid, product) != null;

    internal static string ExtraMouseFamily(ushort vid, ushort pid, string product) =>
        ExtraMouseCandidate(vid,pid,product) ? MouseExpansionFamily(vid,pid,product) : null;

    internal static string ExtraMouseTransport(ushort vid, ushort pid, string product) =>
        ExtraMouseFamily(vid,pid,product) switch
        {
            "freq" or "freq-sc311" or "mloong" => vid == 0xA8A4 ? "USB" : "2.4G",
            "nzxt" => pid == 0x2129 ? "USB" : "2.4G",
            "keychron" => pid == 0xD048 ? "USB" : "2.4G",
            "sora-v3" => pid == 0xE010 ? "USB" : "2.4G",
            "exx" => pid == 0x3A87 ? "USB" : "2.4G",
            "vk-m2" => pid is 0xA304 or 0xA307 ? "USB" : "2.4G",
            _ => null
        };

    internal static ReferenceBatteryProtocol.Reading ReadExtraMouse(ushort vid, ushort pid,
        string product, IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, BatteryReplay.Trace trace)
    {
        if (string.IsNullOrEmpty(InstanceScope.Current) || !ExtraMouseCandidate(vid, pid, product))
            return ReferenceBatteryProtocol.Reading.Failed("Mouse expansion: missing authorized physical instance");
        string family = MouseExpansionFamily(vid, pid, product);
        var candidates = (channels ?? Array.Empty<HidDiscovery.VendorCollectionInfo>())
            .Where(c => c.Vid == vid && c.Pid == pid && !string.IsNullOrEmpty(c.Path) &&
                InstanceScope.Allows(c.Path) && MouseExpansionChannel(family, c))
            .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        int maximum=family is "wl83" or "s9" or "vk-m2" ?8:
            family is "gwolves" or "gwolves-old"?4:family is "ipi" or "ipi-uni"?2:16;
        if (candidates.Length <1 || candidates.Length>maximum)
            return ReferenceBatteryProtocol.Reading.Failed("Mouse expansion: missing or ambiguous candidate collections");
        if(family=="nk"&&!candidates.Any(c=>c.UsagePage==0xFF04))
            return ReferenceBatteryProtocol.Reading.Failed("ATK NK: required FF04 anchor missing");
        string key = InstanceScope.Current + ":" + family;
        return ReferenceQueryGate.Run(key, () =>
        {
            var links = new List<MouseExpansionLink>();
            try
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var candidateLink = new MouseExpansionLink(candidate, family, trace);
                        if (MouseExpansionLayout(family, candidateLink)) links.Add(candidateLink);
                        else candidateLink.Dispose();
                    }
                    catch (IOException) { }
                }
                if(family=="rapoo-active")return MouseExpansionRapooActive(links);
                if (links.Count != 1)
                    return ReferenceBatteryProtocol.Reading.Failed("Mouse expansion: no unique descriptor-validated battery channel");
                var link = links[0];
                trace?.Add("mouse-channel", true, result: family);
                return MouseExpansionRead(family, vid, pid, product, link);
            }
            finally { foreach (var link in links) link.Dispose(); }
        }, family is "infinity100" or "bragi" or "infinity97" ? 18000 : 12500);
    }

    private static bool MouseExpansionMi(string path, int expected)
    {
        if (expected < 0) return true;
        return path?.IndexOf("&mi_" + expected.ToString("X2"), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ReferenceBatteryProtocol.Reading MouseExpansionValue(int level, string provenance,
        bool? charging = null, string model = null, string identity = null) =>
        level is < 0 or > 100 ? null : new(level, charging == true, charging.HasValue,
            charging == true ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            provenance + (charging.HasValue ? "" : "; charging unknown"), model, identity);

    private static ReferenceBatteryProtocol.Reading MouseExpansionSleep(string reason) =>
        new(-1, false, false, ReferenceBatteryProtocol.State.Sleep, reason);

    private static byte[] MouseExpansionPacket(int length, params byte[] prefix)
    {
        var packet = new byte[length];
        if (prefix.Length > length) throw new ArgumentException(nameof(prefix));
        prefix.CopyTo(packet, 0);
        return packet;
    }

    private static int MouseExpansionSum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return sum & 255;
    }

    private static bool MouseExpansionPrefix(ReadOnlySpan<byte> bytes, params byte[] prefix) =>
        bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);

    private static ReferenceBatteryProtocol.Reading MouseExpansionRead(string family, ushort vid, ushort pid,
        string product, MouseExpansionLink link) => family switch
    {
        "feature83" or "lamzu83" or "luna83" or "ko83" or "wl83" or "gwolves" or "gwolves-old" or
        "ipi" or "mchose-a5" or "dareu" or "sora-v3" or "sora-v2" or "vaxee" or "sonix" or "vk-m5" or "royuan-mouse" or
        "razer" or "razer-carbon" => MouseExpansionReadFeature(family, vid, pid, link),
        "hechi" or "ausdom" or "sc525" or "scyrox" or "zynoo" or "waizowl" or "nordic54" or "nk" or
        "compx" or "hitscan" or "hts" or "steel" or "akko" or "cube" or "rog-standard" or "rog8k" or
        "vgn-ld" or "vgn-master" or "vgn-se" or "keychron" or "nzxt" or
        "hecate" or "aula-v9" or "delux" or "x7" or "mchose4d" =>
            MouseExpansionReadOutput(family, vid, pid, link),
        "rapoo" or "rapoo-short" or "incott" or "sc900" or "delux-pro" or "x23-pro" =>
            MouseExpansionReadPassive(family, pid, link),
        _ => MouseExpansionReadSession(family, vid, pid, product, link)
    };

    [DllImport("hid.dll", EntryPoint = "HidD_GetInputReport", SetLastError = true)]
    private static extern bool MouseExpansionGetInputReport(IntPtr handle, byte[] buffer, int length);

    private sealed class MouseExpansionLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle, signal;
        internal readonly HidDiscovery.VendorCollectionInfo Channel;
        internal readonly HashSet<byte> Inputs, Outputs, Features;
        internal readonly ushort Revision;
        internal readonly BatteryReplay.Trace Trace;
        internal int Input => Channel.Input;
        internal int Output => Channel.Output;
        internal int Feature => Channel.Feature;
        internal byte InputId => Inputs.Single();
        internal byte OutputId => Outputs.Single();
        internal byte FeatureId => Features.Single();

        internal MouseExpansionLink(HidDiscovery.VendorCollectionInfo channel, string family, BatteryReplay.Trace trace)
        {
            Channel = channel;
            Trace = trace;
            bool passive = family is "incott" or "sc900" or "rapoo" or "rapoo-short" or "delux-pro" or "x23-pro" or "mchose-l7";
            handle = CreateFile(channel.Path, GENERIC_READ | (passive ? 0 : GENERIC_WRITE),
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                handle = IntPtr.Zero;
                throw new IOException("Mouse battery interface open failed");
            }
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != channel.Vid ||
                    attributes.ProductID != channel.Pid) throw new IOException("Opened mouse identity changed");
                Revision = attributes.VersionNumber;
                if(family is "freq" or "mloong" or "s9")
                {
                    string openedProduct=HidDiscovery.ReadHidProduct(handle,channel.Path);
                    if(!ExtraMouseCandidate(channel.Vid,channel.Pid,openedProduct)||
                        MouseExpansionFamily(channel.Vid,channel.Pid,openedProduct)!=family||
                        family=="freq"&&!MouseExpansionName(openedProduct,channel.Product)||
                        family=="mloong"&&MouseExpansionMloong(openedProduct)!=MouseExpansionMloong(channel.Product))
                        throw new IOException("Opened mouse product profile changed");
                }
                var caps = Caps(handle);
                if (caps.UsagePage != channel.UsagePage || caps.Usage != channel.Usage ||
                    caps.InputReportByteLength != Input || caps.OutputReportByteLength != Output ||
                    caps.FeatureReportByteLength != Feature) throw new IOException("Opened mouse caps changed");
                if (!HidD_GetPreparsedData(handle, out var prep)) throw new IOException("Mouse descriptor unavailable");
                try
                {
                    Inputs = ReadDescriptorReportIds(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps);
                    Outputs = ReadDescriptorReportIds(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps);
                    Features = ReadDescriptorReportIds(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps);
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("Mouse input event unavailable");
            }
            catch { Dispose(); throw; }
        }

        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            if (Input is < 1 or > 1024) return null;
            var buffer = new byte[Input];
            if (!Transfer(handle, signal, buffer, false, Math.Max(1, timeoutMs), out int count) || count <= 0)
                return null;
            if (Inputs.SetEquals(new byte[] { 0 }) && count == Input && buffer[0] == 0)
                return buffer.AsSpan(1, count - 1).ToArray();
            if(!Inputs.Contains(buffer[0]))return null;
            return buffer.AsSpan(0, count).ToArray();
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            bool ok = request.Length == Output && request.Length>0 && Outputs.Contains(request[0]) &&
                Transfer(handle, signal, request, true, timeoutMs, out int count) && count == request.Length;
            Trace?.Add("mouse-output", ok, raw: request, result: "battery-read");
            return ok;
        }
        public bool SetFeature(byte[] request)
        {
            bool ok = request.Length <= Feature && request.Length > 0 && Features.Contains(request[0]) &&
                HidD_SetFeature(handle, request, request.Length);
            Trace?.Add("mouse-set-feature", ok, raw: request, result: "battery-read");
            return ok;
        }
        public byte[] GetFeature(byte reportId, int length)
        {
            if (!Features.Contains(reportId) || length < 1 || length > Feature) return null;
            var buffer = new byte[length];
            buffer[0] = reportId;
            return HidD_GetFeature(handle, buffer, length) ? buffer : null;
        }
        internal byte[] GetInput(byte reportId, int length)
        {
            if (!Outputs.Contains(reportId) || length is < 1 or > 1024) return null;
            var buffer = new byte[length];
            buffer[0] = reportId;
            return MouseExpansionGetInputReport(handle, buffer, length) ? buffer : null;
        }
        public void Delay(int milliseconds) => Thread.Sleep(Math.Clamp(milliseconds, 0, 100));
        internal bool Drain()
        {
            for (int count = 0; count < 32; count++) if (Read(1) is not { Length: > 0 }) return true;
            return false;
        }
        internal byte[] Exchange(byte[] request, Func<byte[], bool> match, int budget = 1200)
        {
            if (!Drain() || !Write(request, 300)) return null;
            long deadline = Now + budget;
            for (int count = 0; count < 96 && Now < deadline; count++)
            {
                var raw = Read((int)Math.Min(100, deadline - Now));
                if (raw is not { Length: > 0 } || raw.AsSpan().SequenceEqual(request) ||
                    request[0] == 0 && raw.Length == request.Length - 1 &&
                    raw.AsSpan().SequenceEqual(request.AsSpan(1)) || !match(raw)) continue;
                Trace?.Add("mouse-response", true, raw: raw, result: "validated-battery-frame");
                return raw;
            }
            return null;
        }
        internal byte[] FeatureExchange(byte[] request, Func<byte[], bool> match, int attempts = 4, int delay = 60)
        {
            if (!SetFeature(request)) return null;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Delay(delay);
                var raw = GetFeature(request[0], request.Length);
                if (raw == null || raw.AsSpan().SequenceEqual(request) || !match(raw)) continue;
                Trace?.Add("mouse-feature-response", true, raw: raw, result: "validated-battery-frame");
                return raw;
            }
            return null;
        }
        public void Dispose()
        {
            if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
