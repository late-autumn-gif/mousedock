using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 通用 HID 探测：按 VID:PID 找到设备，打印能力和报告长度，再发一组试探帧收回包。
    ///
    /// 存在的意义是「不猜协议」。竹林鸟玄武（ZhuAudio ZG2，VID_4C4A/PID_4155）目前没有公开
    /// 的资料，与其照鼠标那套硬编一版，不如把实际报告长度和应答原样抓出来，按真数据写。
    /// </summary>
    internal static class HidProbe
    {
        private static readonly Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder buf, int len);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumPtr, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr templ);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int count, out int read, IntPtr ov);

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_RW = 0x03;
        private const uint OPEN_EXISTING = 3;

        public sealed class Node
        {
            public string Path = "";
            public string Product = "";
            public ushort Vid, Pid, Version;
            public HIDP_CAPS Caps;
        }

        public static List<Node> Enumerate(ushort? vid, ushort? pid)
        {
            var result = new List<Node>();
            HidD_GetHidGuid(out Guid hidGuid);

            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

            try
            {
                for (uint i = 0; ; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did)) break;

                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                    if (required <= 0) continue;

                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, required, out _, IntPtr.Zero)) continue;

                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? "";
                        if (string.IsNullOrEmpty(path)) continue;

                        using var h = CreateFile(path, 0, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (h.IsInvalid) continue;

                        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (!HidD_GetAttributes(h, ref attr)) continue;

                        if (vid.HasValue && attr.VendorID != vid.Value) continue;
                        if (pid.HasValue && attr.ProductID != pid.Value) continue;

                        var node = new Node { Path = path, Vid = attr.VendorID, Pid = attr.ProductID, Version = attr.VersionNumber };

                        var sb = new StringBuilder(256);
                        if (HidD_GetProductString(h, sb, sb.Capacity * 2)) node.Product = sb.ToString();

                        if (HidD_GetPreparsedData(h, out IntPtr pre))
                        {
                            var caps = new HIDP_CAPS();
                            if (HidP_GetCaps(pre, ref caps) == 0x110000) node.Caps = caps;
                            HidD_FreePreparsedData(pre);
                        }

                        result.Add(node);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }

            return result;
        }

        /// <summary>对一台设备发试探帧，把每次的应答录下来。</summary>
        public static string Run(Node node, IEnumerable<byte[]> frames, int perFrameMs = 350)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"设备: {node.Product}");
            sb.AppendLine($"VID:PID = {node.Vid:X4}:{node.Pid:X4}  版本 {node.Version:X4}");
            sb.AppendLine($"路径: {node.Path}");
            sb.AppendLine($"能力: in={node.Caps.InputReportByteLength}  out={node.Caps.OutputReportByteLength}  feature={node.Caps.FeatureReportByteLength}");
            sb.AppendLine($"用途: usagePage=0x{node.Caps.UsagePage:X4} usage=0x{node.Caps.Usage:X4}");
            sb.AppendLine();

            if (node.Caps.OutputReportByteLength <= 0)
            {
                sb.AppendLine("这台设备没有输出报告，发不了命令（只能被动听输入报告）。");
                return sb.ToString();
            }

            using var h = CreateFile(node.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                sb.AppendLine($"打不开设备，Win32 错误 {Marshal.GetLastWin32Error()}");
                return sb.ToString();
            }

            int outLen = node.Caps.OutputReportByteLength;
            int inLen = Math.Max(1, (int)node.Caps.InputReportByteLength);
            var readBuf = new byte[inLen];

            foreach (var frame in frames)
            {
                var report = new byte[outLen];
                Array.Copy(frame, report, Math.Min(frame.Length, outLen - (outLen > frame.Length ? 0 : 0)));

                bool w = WriteFile(h, report, outLen, out int written, IntPtr.Zero);
                string sent = BitConverter.ToString(frame);

                Thread.Sleep(perFrameMs);

                string got = "";
                int rounds = 0;
                while (rounds++ < 4)
                {
                    if (!ReadFile(h, readBuf, inLen, out int read, IntPtr.Zero) || read <= 0) break;
                    var slice = new byte[read];
                    Array.Copy(readBuf, slice, read);
                    got += BitConverter.ToString(slice) + " ";
                }

                sb.AppendLine($"发 {sent,-32} 写={(w ? "ok" : "失败")}  收={(string.IsNullOrEmpty(got) ? "（无）" : got.Trim())}");
            }

            return sb.ToString();
        }

        /// <summary>默认试探集：常见报告 ID + 常见查询帧前缀，都补零到输出长度。</summary>
        public static List<byte[]> DefaultFrames()
        {
            var list = new List<byte[]>();
            for (byte id = 0x00; id <= 0x10; id++)
                list.Add(new[] { id });

            // 几家常见的「命令头 + 查询码」
            list.Add(new byte[] { 0x03, 0x01 });
            list.Add(new byte[] { 0x04, 0x01 });
            list.Add(new byte[] { 0x05, 0x01 });
            list.Add(new byte[] { 0x08, 0x04, 0x00 });
            list.Add(new byte[] { 0x0B, 0x13 });
            list.Add(new byte[] { 0x0B, 0x14 });
            return list;
        }
    }
}
