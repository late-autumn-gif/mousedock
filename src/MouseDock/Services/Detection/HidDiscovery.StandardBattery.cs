using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    private const string StandardBatteryProtocol = "HID Battery Strength";
    private const string StandardBatterySource = "HID 标准电量（Battery Strength）";
    private const ushort DeviceControlsPage = 0x06;
    private const ushort BatteryStrengthUsage = 0x20;
    private const int FeatureReport = 2;
    private const int HidParserSuccess = 0x110000;
    private const int StandardReadTimeoutMs = 250;

    // The final union starts with Usage or UsageMin at the same offset.
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(3)] public byte IsAlias;
        [FieldOffset(4)] public ushort BitField;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
    }

    [DllImport("hid.dll")]
    private static extern int HidP_GetSpecificValueCaps(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, [Out] HIDP_VALUE_CAPS[] valueCaps, ref ushort valueCount, IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, out uint value, IntPtr preparsed, byte[] report, uint reportLength);

    internal readonly record struct StandardBatteryField(byte ReportId, ushort LinkCollection, ushort ReportLength,
        ushort BitSize, int LogicalMin, int LogicalMax)
    {
        internal byte[] CreateRequest()
        {
            var report = new byte[ReportLength];
            report[0] = ReportId;
            return report;
        }

        internal bool MatchesReport(byte[] report) => report?.Length == ReportLength && report[0] == ReportId;

        internal int Decode(byte[] report, uint raw)
        {
            if (!MatchesReport(report)) return -1;
            long value = raw;
            if (LogicalMin < 0)
            {
                // HIDP returns the field bits without sign extension.
                long signBit = 1L << (BitSize - 1);
                if ((value & signBit) != 0) value -= signBit * 2;
            }
            if (value < LogicalMin || value > LogicalMax) return -1;

            // USB HID Usage Tables, Generic Device Controls: the logical range spans empty to full.
            long range = (long)LogicalMax - LogicalMin;
            return (int)(((value - LogicalMin) * 100 + range / 2) / range);
        }
    }

    internal readonly record struct StandardBatteryChannel(string Path, ushort Vid, ushort Pid,
        ushort Revision, ushort UsagePage, ushort Usage, StandardBatteryField Field);

    internal static StandardBatteryField? FindStandardBatteryField(ReadOnlySpan<HIDP_VALUE_CAPS> values, ushort reportLength)
    {
        StandardBatteryField? field = null;
        foreach (var value in values)
        {
            if (value.UsagePage != DeviceControlsPage || (value.IsRange == 0
                ? value.UsageMin != BatteryStrengthUsage
                : value.UsageMin > BatteryStrengthUsage || value.UsageMax < BatteryStrengthUsage))
                continue;

            if (field != null || value.IsAlias != 0 ||
                (value.IsRange != 0 && value.UsageMin != value.UsageMax) ||
                (value.BitField & 0x07) != 0x02 || value.ReportCount != 1 ||
                value.BitSize is 0 or > 32 || reportLength < 2 ||
                value.BitSize > (reportLength - 1) * 8 || value.LogicalMax <= value.LogicalMin)
                return null;

            int magnitude = value.LogicalMin < 0 ? value.BitSize - 1 : value.BitSize;
            long limit = 1L << magnitude;
            if (value.LogicalMin < -limit || value.LogicalMax >= limit) return null;
            field = new(value.ReportId, value.LinkCollection, reportLength, value.BitSize, value.LogicalMin, value.LogicalMax);
        }
        return field;
    }

    private static StandardBatteryField? StandardField(IntPtr preparsed, HIDP_CAPS caps)
    {
        if (caps.FeatureReportByteLength < 2 || caps.NumberFeatureValueCaps == 0) return null;
        var values = new HIDP_VALUE_CAPS[Math.Min(2, (int)caps.NumberFeatureValueCaps)];
        ushort count = (ushort)values.Length;
        if (HidP_GetSpecificValueCaps(FeatureReport, DeviceControlsPage, 0, BatteryStrengthUsage,
                values, ref count, preparsed) != HidParserSuccess || count != 1)
            return null;
        return FindStandardBatteryField(values.AsSpan(0, count), caps.FeatureReportByteLength);
    }

    private static bool StandardTopLevel(ushort page, ushort usage) =>
        page == DeviceControlsPage || UsageMap.TryKind(page, usage, out _);

    internal static void QueryStandardBattery(Bucket bucket, IReadOnlyList<StandardBatteryChannel> channels,
        bool safeMode, DevicePreferences preferences, Func<StandardBatteryChannel, int> read)
    {
        if (bucket.Battery >= 0 || bucket.HandledBy != HandledBy.None || bucket.ExtraRoute != null ||
            DeviceRoutes.IsKnown(bucket.Vid, bucket.Pid, bucket.Product) || bucket.Counts.Count != 1)
            return;

        var matching = channels.Where(channel =>
            channel.Vid == bucket.Vid && channel.Pid == bucket.Pid && channel.Revision == bucket.Revision &&
            InstanceScope.MatchesUsbInstance(bucket.InstanceId, channel.Path, channel.Vid, channel.Pid) &&
            (UsageMap.TryKind(channel.UsagePage, channel.Usage, out var kind) && kind == bucket.Kind ||
             channel.UsagePage == DeviceControlsPage))
            .Take(2).ToArray();
        if (matching.Length != 1) return;

        var selected = matching[0];
        bool enabled = preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
            VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true;
        int level = safeMode || !enabled ? -1 : read(selected);
        string status = !enabled ? "RecognitionDisabled" : safeMode ? "SafeMode" :
            (level is >= 0 and <= 100) ? "Read" : "NoVerifiedLevel";
        BatteryReplay.Current?.AddQuery(bucket.InstanceId, selected.Path, StandardBatteryProtocol, status, null,
            status == "Read" ? level : -1);
        LastDiagnostics.Add($"{bucket.InstanceId} {StandardBatteryProtocol} {status} report={selected.Field.ReportId:X2}");
        if (status != "Read")
        {
            bucket.StandardBatteryNote = !enabled ? "用户已取消识别，未查询标准 HID 电量" :
                safeMode ? "安全模式已跳过标准 HID 电量查询" : "已发现标准 HID 电量字段，但未取得有效读数";
            return;
        }

        bucket.Battery = level;
        bucket.Charging = false;
        bucket.ChargeFull = false;
        bucket.ReadStale = false;
        bucket.StandardBattery = true;
        bucket.BatterySource = StandardBatterySource;
    }

    private static int ReadStandardBattery(string instance, StandardBatteryChannel channel)
    {
        var lease = HidIoLease.TryAcquire(instance);
        if (lease == null) return -1;
        int level = -1;
        var worker = new Thread(() =>
        {
            // A driver may outlive the scan deadline. Its worker still owns its handle and lease.
            using (lease)
            {
                try { level = ReadStandardBatteryCore(channel); }
                catch { }
            }
        }) { IsBackground = true };
        try { worker.Start(); }
        catch
        {
            lease.Dispose();
            return -1;
        }
        return worker.Join(StandardReadTimeoutMs) ? level : -1;
    }

    private static int ReadStandardBatteryCore(StandardBatteryChannel channel)
    {
        // GET_FEATURE can use a zero-access handle even for a system-owned keyboard or mouse.
        IntPtr handle = CreateFile(channel.Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return -1;
        try
        {
            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(handle, ref attr) || attr.VendorID != channel.Vid ||
                attr.ProductID != channel.Pid || attr.VersionNumber != channel.Revision ||
                !HidD_GetPreparsedData(handle, out var preparsed))
                return -1;
            try
            {
                HIDP_CAPS caps = default;
                if (HidP_GetCaps(preparsed, ref caps) != HidParserSuccess || caps.UsagePage != channel.UsagePage ||
                    caps.Usage != channel.Usage || StandardField(preparsed, caps) != channel.Field)
                    return -1;

                var report = channel.Field.CreateRequest();
                if (!HidD_GetFeature(handle, report, report.Length) || !channel.Field.MatchesReport(report) ||
                    HidP_GetUsageValue(FeatureReport, DeviceControlsPage, channel.Field.LinkCollection,
                        BatteryStrengthUsage, out uint raw, preparsed, report, (uint)report.Length) != HidParserSuccess)
                    return -1;
                return channel.Field.Decode(report, raw);
            }
            finally { HidD_FreePreparsedData(preparsed); }
        }
        finally { CloseHandle(handle); }
    }
}
