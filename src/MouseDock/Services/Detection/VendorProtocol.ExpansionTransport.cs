using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal sealed class ExpansionHidLink : ReferenceBatteryProtocol.ILink, IExpansionOutputReport, IDisposable
    {
        private IntPtr handle, signal;
        internal readonly HidDiscovery.VendorCollectionInfo Channel;
        internal readonly ExpansionHidMetadata Metadata;
        private readonly ExpansionHidRules rules;
        private readonly BatteryReplay.Trace trace;

        internal ExpansionHidLink(ExpansionHidRules rules, HidDiscovery.VendorCollectionInfo channel,
            BatteryReplay.Trace trace)
        {
            this.rules = rules; Channel = channel; this.trace = trace;
            handle = CreateFile(channel.Path, rules.Passive ? GENERIC_READ : GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            { handle = IntPtr.Zero; throw new IOException("Device interface open failed"); }
            try
            {
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                Require(HidD_GetAttributes(handle, ref a) && a.VendorID == channel.Vid && a.ProductID == channel.Pid,
                    "Opened device VID/PID changed");
                var opened = new HidDiscovery.VendorCollectionInfo
                {
                    Path = channel.Path, Vid = a.VendorID, Pid = a.ProductID,
                    Product = HidDiscovery.ReadHidProduct(handle, channel.Path),
                    UsagePage = caps.UsagePage, Usage = caps.Usage, Input = caps.InputReportByteLength,
                    Output = caps.OutputReportByteLength, Feature = caps.FeatureReportByteLength
                };
                Require(rules.Matches(opened) && Name(opened.Product) == Name(channel.Product) &&
                    opened.UsagePage == channel.UsagePage && opened.Usage == channel.Usage &&
                    opened.Input == channel.Input && opened.Output == channel.Output && opened.Feature == channel.Feature,
                    "Opened device product or collection metadata changed");
                Require(HidD_GetPreparsedData(handle, out IntPtr prep), "Device descriptor unavailable");
                try
                {
                    Metadata = new()
                    {
                        Revision = a.VersionNumber,
                        InputIds = ReadDescriptorReportIds(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps),
                        OutputIds = ReadDescriptorReportIds(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps),
                        FeatureIds = ReadDescriptorReportIds(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps),
                        InputSizes = new(), OutputSizes = new(), FeatureSizes = new()
                    };
                    Metadata.InputSizes = ExpansionReportSizes(Metadata.InputIds, caps.InputReportByteLength);
                    Metadata.OutputSizes = ExpansionReportSizes(Metadata.OutputIds, caps.OutputReportByteLength);
                    Metadata.FeatureSizes = ExpansionReportSizes(Metadata.FeatureIds, caps.FeatureReportByteLength);
                    rules.Validate(opened, Metadata);
                    trace?.Add("expansion-descriptor", true, result:
                        $"family={rules.Family};revision={Metadata.Revision:X4};input={string.Join(",", Metadata.InputIds)};" +
                        $"output={string.Join(",", Metadata.OutputIds)};feature={string.Join(",", Metadata.FeatureIds)}");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                Require(signal != IntPtr.Zero, "Device event creation failed");
                trace?.Add("expansion-open", true);
            }
            catch { Dispose(); throw; }
        }
        internal ExpansionWire Wire(int budget = 10000) => new(this,
            rules.UnnumberedInput || Metadata.UnnumberedInput ? Channel.Input - 1 : Channel.Input,
            Channel.Output, Channel.Feature, Metadata.Revision, Metadata, trace, budget);
        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            if (Channel.Input <= 0) return null;
            var b = new byte[Channel.Input];
            if (!Transfer(handle, signal, b, false, Math.Max(1, timeoutMs), out int count) || count <= 0) return null;
            if (rules.UnnumberedInput || Metadata.UnnumberedInput)
            {
                if (count != b.Length || b[0] != 0) return null;
                return b.AsSpan(1, count - 1).ToArray();
            }
            return b.AsSpan(0, count).ToArray();
        }
        public bool Write(byte[] packet, int timeoutMs) =>
            !rules.Passive && packet?.Length == Channel.Output && Channel.Output > 0 &&
            Transfer(handle, signal, packet, true, Math.Max(1, timeoutMs), out int count) && count == packet.Length;
        public bool SetOutputReport(byte[] packet) => !rules.Passive && packet?.Length == Channel.Output &&
            Channel.Output > 0 && Metadata.OutputIds.Contains(packet[0]) && HidD_SetOutputReport(handle, packet, packet.Length);
        public bool SetFeature(byte[] packet) => !rules.Passive && packet?.Length == Channel.Feature &&
            Channel.Feature > 0 && Metadata.FeatureIds.Contains(packet[0]) && HidD_SetFeature(handle, packet, packet.Length);
        public byte[] GetFeature(byte reportId, int length)
        {
            if (rules.Passive || length != Channel.Feature || length == 0 || !Metadata.FeatureIds.Contains(reportId)) return null;
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

    private static Dictionary<byte, int> ExpansionReportSizes(HashSet<byte> ids, int nativeLength)
    {
        var result = new Dictionary<byte, int>();
        if (ids.Count == 1 && nativeLength > 0)
        { byte id = ids.Single(); result[id] = nativeLength - (id == 0 ? 1 : 0); }
        return result;
    }
}
