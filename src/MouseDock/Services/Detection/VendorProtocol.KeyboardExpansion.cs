using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static bool ExtraKeyboardCandidate(ushort vid, ushort pid, string product) =>
        KeyboardExpansionProfiles.Find(vid, pid, product) != null;

    internal static string ExtraKeyboardFamily(ushort vid, ushort pid, string product) =>
        KeyboardExpansionProfiles.Find(vid, pid, product)?.Family;

    internal static ReferenceBatteryProtocol.Reading ReadExtraKeyboard(ushort vid, ushort pid,
        string product, IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, BatteryReplay.Trace trace)
    {
        var profile = KeyboardExpansionProfiles.Find(vid, pid, product);
        if (profile == null || string.IsNullOrEmpty(InstanceScope.Current) || channels == null)
            return ReferenceBatteryProtocol.Reading.Failed("No scoped keyboard profile");
        // Capture the approved paths before crossing the ThreadStatic instance-scope boundary.
        var selected = channels.Where(c => c != null && InstanceScope.Allows(c.Path) &&
            profile.Matches(c)).ToArray();
        bool split = profile.Family is "MchoseBy" or "Shared024F" or "RogOmni" or "Logi";
        if (selected.Length == 0 || selected.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length ||
            !split && selected.Length != 1 ||
            split && selected.Length > (profile.Family is "MchoseBy" or "Shared024F" ? 3 : 2))
            return ReferenceBatteryProtocol.Reading.Failed("Missing or ambiguous keyboard collection");
        string key = InstanceScope.Current + ":keyboard-expansion";
        return ReferenceQueryGate.Run(key, () =>
        {
            var links = new List<ExtraKeyboardNativeLink>();
            try
            {
                foreach (var channel in selected)
                    links.Add(new ExtraKeyboardNativeLink(profile, channel, trace));
                if (profile.Family == "RogOmni")
                {
                    var pair = links.Where(l => l.InputId == 1 && l.Channel.UsagePage == 0xFF02).ToArray();
                    var power = links.Where(l => l.InputId == 2 && l.Channel.UsagePage == 0xFF00).ToArray();
                    Require(pair.Length == 1 && power.Length == 1 && links.Count == 2,
                        "ROG requires unique paired identity and power channels");
                    return KeyboardExpansionRog.ReadOmni(pair[0].Wire(), power[0].Wire(), pid == 0x1AD0);
                }
                if (profile.Family == "MchoseBy" || profile.Family == "Shared024F" &&
                    links.Any(l => l.Channel.UsagePage is 0xFF02 or 0xFF04))
                {
                    var rx = links.Where(l => l.HasInput19).ToArray();
                    var tx = links.Where(l => l.HasOutput19).ToArray();
                    Require(rx.Length == 1 && tx.Length == 1, "MCHOSE requires unique report19 input and output");
                    Require(links.All(l => l.Channel.UsagePage is 0xFF02 or 0xFF04),
                        "Shared 024F receiver has conflicting keyboard protocol collections");
                    var combined = new ExtraKeyboardSplitLink(rx[0], tx[0]);
                    return KeyboardExpansionMchoseBy.Read(new(combined, rx[0].Channel.Input,
                        tx[0].Channel.Output, 0, 19, 19, 0, trace: trace));
                }
                Require(profile.Family != "Shared024F" || links.Count == 1,
                    "Shared 024F receiver has ambiguous NOVA collections");
                if (profile.Family == "Logi")
                {
                    var shortLink = links.SingleOrDefault(l => l.InputId == 0x10);
                    var longLink = links.SingleOrDefault(l => l.InputId == 0x11);
                    Require(shortLink != null && longLink != null && links.Count == 2,
                        "HID++ requires unique short and long channels");
                    return KeyboardExpansionLogitech.Read(shortLink.Wire(), longLink.Wire());
                }
                return KeyboardExpansionProfiles.Read(profile, links[0].Wire());
            }
            catch (Exception ex)
            { return ReferenceBatteryProtocol.Reading.Failed(profile.Family + ": " + ex.Message); }
            finally { foreach (var link in links) link.Dispose(); }
        }, profile.Family switch { "Lofree" => 17000, "Pmo" => 15500,
            "Logi" or "RogOmni" => 13000, _ => 11000 });
    }

    private sealed class ExtraKeyboardSplitLink(ExtraKeyboardNativeLink input,
        ExtraKeyboardNativeLink output) : ReferenceBatteryProtocol.ILink
    {
        public long Now => input.Now;
        public byte[] Read(int timeoutMs) => input.Read(timeoutMs);
        public bool Write(byte[] request, int timeoutMs) => output.Write(request, timeoutMs);
        public bool SetFeature(byte[] request) => false;
        public byte[] GetFeature(byte reportId, int length) => null;
        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
    }

    private sealed class ExtraKeyboardNativeLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle, signal;
        internal readonly HidDiscovery.VendorCollectionInfo Channel;
        internal byte InputId, OutputId, FeatureId;
        internal bool HasInput19, HasOutput19;
        internal ushort Revision;
        private readonly KeyboardExpansionProfiles.Profile profile;
        private readonly BatteryReplay.Trace trace;

        internal ExtraKeyboardNativeLink(KeyboardExpansionProfiles.Profile profile,
            HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace)
        {
            this.profile = profile; Channel = channel; this.trace = trace;
            bool passive = profile.Family == "RapooEvent";
            handle = CreateFile(channel.Path, passive ? GENERIC_READ : GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            { handle = IntPtr.Zero; throw new IOException("Keyboard interface open failed"); }
            try
            {
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                Require(HidD_GetAttributes(handle, ref a) && a.VendorID == channel.Vid && a.ProductID == channel.Pid,
                    "Opened keyboard VID/PID changed");
                var opened = new HidDiscovery.VendorCollectionInfo
                {
                    Path = channel.Path, Vid = a.VendorID, Pid = a.ProductID,
                    Product = HidDiscovery.ReadHidProduct(handle, channel.Path),
                    UsagePage = caps.UsagePage, Usage = caps.Usage,
                    Input = caps.InputReportByteLength, Output = caps.OutputReportByteLength,
                    Feature = caps.FeatureReportByteLength
                };
                Require(profile.Matches(opened) && KeyboardExpansionProfiles.Name(opened.Product) ==
                    KeyboardExpansionProfiles.Name(channel.Product) &&
                    channel.UsagePage == opened.UsagePage && channel.Usage == opened.Usage &&
                    channel.Input == opened.Input && channel.Output == opened.Output && channel.Feature == opened.Feature,
                    "Opened keyboard product or collection capabilities changed");
                Revision = a.VersionNumber;
                Require(profile.RevisionAllowed(Revision, opened.Product), "Keyboard firmware/profile mismatch");
                if (profile.Family == "Shared024F" && opened.UsagePage is 0xFF02 or 0xFF04)
                    Require(Revision == 0x2007, "Generic K99 name requires the documented revision");
                Require(HidD_GetPreparsedData(handle, out IntPtr prep), "Keyboard descriptor unavailable");
                try
                {
                    var input = ReadDescriptorReportIds(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps);
                    var output = ReadDescriptorReportIds(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps);
                    var feature = ReadDescriptorReportIds(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps);
                    var inputSizes = ReadExtraKeyboardReportLengths(input, caps.InputReportByteLength);
                    var outputSizes = ReadExtraKeyboardReportLengths(output, caps.OutputReportByteLength);
                    HasInput19 = inputSizes.TryGetValue(19,out int in19) && in19 == 20;
                    HasOutput19 = outputSizes.TryGetValue(19,out int out19) && out19 == 20;
                    if (profile.Family == "MchoseBy" || profile.Family == "Shared024F" &&
                        opened.UsagePage is 0xFF02 or 0xFF04)
                        Require(HasInput19 || HasOutput19,"MCHOSE report19 length was not proven");
                    if (profile.Family == "VgnV3")
                        Require(HasInput19 && HasOutput19,"VGN V3 report19 length was not proven");
                    if (profile.Family is "VgnV4" or "Exx")
                        Require(input.Count == 1 && output.Count == 1 &&
                            inputSizes.TryGetValue(input.Single(),out int rx) && rx == 64 &&
                            outputSizes.TryGetValue(output.Single(),out int tx) && tx == 64,
                            "VGN V4 report length was not proven");
                    if (profile.Family == "Fa09" && Revision is 0x2005 or 0x2007)
                        Require(channel.Output == 20 && channel.Feature == 0 &&
                            output.SetEquals(new byte[] {19}) && feature.Count == 0,
                            "Dedicated FA09 profile requires exact report19 output and no Feature report");
                    profile.ValidateIds(opened, input, output, feature, out InputId, out OutputId, out FeatureId);
                    trace?.Add("keyboard-descriptor", true, result:
                        $"input={string.Join(",", input)};output={string.Join(",", output)};feature={string.Join(",", feature)};revision={Revision:X4}");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                Require(signal != IntPtr.Zero, "Keyboard event creation failed");
                trace?.Add("keyboard-open", true);
            }
            catch { Dispose(); throw; }
        }
        internal KeyboardExpansionWire Wire() => new(this,
            InputId == 0 && Channel.Input > 0 ? Channel.Input - 1 : Channel.Input,
            Channel.Output, Channel.Feature, InputId, OutputId, FeatureId, Revision, trace,
            profile.Family switch { "Relay" => 2000, "Lofree" => 16000, "Pmo" => 14000, _ => 12000 });
        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            if (Channel.Input == 0) return null;
            var b = new byte[Channel.Input];
            if (!Transfer(handle, signal, b, false, Math.Max(1, timeoutMs), out int count) || count <= 0) return null;
            if (InputId == 0)
            {
                if (count != b.Length || b[0] != 0) return null;
                return b.AsSpan(1, count - 1).ToArray();
            }
            return b.AsSpan(0, count).ToArray();
        }
        public bool Write(byte[] packet, int timeoutMs) =>
            packet?.Length == Channel.Output && Channel.Output > 0 &&
            Transfer(handle, signal, packet, true, Math.Max(1, timeoutMs), out int count) && count == packet.Length;
        public bool SetFeature(byte[] packet) =>
            packet?.Length == Channel.Feature && Channel.Feature > 0 &&
            packet[0] == FeatureId && HidD_SetFeature(handle, packet, packet.Length);
        public byte[] GetFeature(byte reportId, int length)
        {
            if (reportId != FeatureId || length != Channel.Feature || length == 0) return null;
            var b = new byte[length]; b[0] = reportId;
            return HidD_GetFeature(handle, b, length) ? b : null;
        }
        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
        public void Dispose()
        {
            if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }

    private static Dictionary<byte,int> ReadExtraKeyboardReportLengths(HashSet<byte> ids, int nativeLength)
    {
        var lengths = new Dictionary<byte,int>();
        // HIDP_VALUE_CAPS does not describe constant padding or alias overlap.
        // Only the sole report ID can inherit the exact global native length.
        if (ids.Count == 1 && nativeLength > 0)
        { byte id = ids.Single(); lengths[id] = nativeLength - (id == 0 ? 1 : 0); }
        return lengths;
    }
}
