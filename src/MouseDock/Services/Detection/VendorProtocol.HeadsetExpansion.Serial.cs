using System;
using System.Runtime.InteropServices;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpansionSerialDcb
    {
        internal uint Size, Baud, Flags;
        internal ushort Reserved, XonLimit, XoffLimit;
        internal byte ByteSize, Parity, StopBits, Xon, Xoff, Error, Eof, Event;
        internal ushort Reserved1;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpansionSerialTimeouts
    { internal uint Interval, ReadMultiplier, ReadConstant, WriteMultiplier, WriteConstant; }
    [DllImport("kernel32.dll", EntryPoint = "GetCommState", SetLastError = true)]
    private static extern bool ExpansionGetCommState(IntPtr handle, ref ExpansionSerialDcb state);
    [DllImport("kernel32.dll", EntryPoint = "SetCommState", SetLastError = true)]
    private static extern bool ExpansionSetCommState(IntPtr handle, ref ExpansionSerialDcb state);
    [DllImport("kernel32.dll", EntryPoint = "SetCommTimeouts", SetLastError = true)]
    private static extern bool ExpansionSetCommTimeouts(IntPtr handle, ref ExpansionSerialTimeouts timeouts);
    [DllImport("kernel32.dll", EntryPoint = "PurgeComm", SetLastError = true)]
    private static extern bool ExpansionPurgeComm(IntPtr handle, uint flags);

    private sealed class ExpansionSerialPort : IHeadsetExpansionSerialLink, IDisposable
    {
        private IntPtr handle;
        internal ExpansionSerialPort(string port, bool sony)
        {
            Require(System.Text.RegularExpressions.Regex.IsMatch(port, @"^COM[1-9][0-9]{0,4}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase), "Invalid native COM name");
            handle = CreateFile(@"\\.\" + port, GENERIC_READ | GENERIC_WRITE, 0,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            { handle = IntPtr.Zero; throw new System.IO.IOException("Battery COM port unavailable or already in use"); }
            try
            {
                var dcb = new ExpansionSerialDcb { Size = (uint)Marshal.SizeOf<ExpansionSerialDcb>() };
                Require(dcb.Size == 28 && ExpansionGetCommState(handle, ref dcb), "Serial configuration unavailable");
                dcb.Baud = sony ? 460800U : 9600U; dcb.Flags = sony ? 4113U : 1U;
                dcb.ByteSize = 8; dcb.Parity = 0; dcb.StopBits = 0;
                Require(ExpansionSetCommState(handle, ref dcb), "Serial 8N1 configuration failed");
                var timeouts = new ExpansionSerialTimeouts
                { Interval = uint.MaxValue, ReadConstant = 50, WriteConstant = 500 };
                Require(ExpansionSetCommTimeouts(handle, ref timeouts), "Bounded serial timeouts unavailable");
            }
            catch { Dispose(); throw; }
        }
        public long Now => Environment.TickCount64;
        public void Purge() => Require(ExpansionPurgeComm(handle, 10), "Serial input queue purge failed");
        public bool Write(byte[] packet)
        {
            Require(packet?.Length is > 0 and <= 128, "Invalid serial output size");
            IntPtr buffer = Marshal.AllocHGlobal(packet.Length), count = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.Copy(packet, 0, buffer, packet.Length); Marshal.WriteInt32(count, 0);
                return WriteFile(handle, buffer, packet.Length, count, IntPtr.Zero) && Marshal.ReadInt32(count) == packet.Length;
            }
            finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(count); }
        }
        public byte[] Read(int maximum)
        {
            Require(maximum is > 0 and <= 128, "Invalid serial read bound");
            IntPtr buffer = Marshal.AllocHGlobal(maximum), count = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(count, 0);
                Require(ReadFile(handle, buffer, maximum, count, IntPtr.Zero), "Serial read failed");
                int size = Marshal.ReadInt32(count); Require(size >= 0 && size <= maximum, "Serial read exceeded buffer");
                var b = new byte[size]; Marshal.Copy(buffer, b, 0, size); return b;
            }
            finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(count); }
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    private static ReferenceBatteryProtocol.Reading ReadSerialHeadsetNative(string family,
        HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace)
    {
        string anchor = HeadsetExpansionSerial.AnchorInstance(channel.Path);
        Guid owner = HeadsetExpansionSerialDiscovery.Container(anchor);
        var selected = HeadsetExpansionSerial.SelectPort(anchor, owner, channel.Vid, channel.Pid,
            HeadsetExpansionSerialDiscovery.Ports(channel.Vid, channel.Pid));
        void Verify()
        {
            Require(owner != Guid.Empty && HeadsetExpansionSerialDiscovery.Container(anchor) == owner &&
                HeadsetExpansionSerialDiscovery.Container(selected.Instance) == owner,
                "Receiver/COM container changed");
            var current = HeadsetExpansionSerial.SelectPort(anchor, owner, channel.Vid, channel.Pid,
                HeadsetExpansionSerialDiscovery.Ports(channel.Vid, channel.Pid));
            Require(string.Equals(current.Port, selected.Port, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.Instance, selected.Instance, StringComparison.OrdinalIgnoreCase),
                "Present serial association changed");
        }
        Verify();
        using var port = new ExpansionSerialPort(selected.Port, family == "SonyH9Serial");
        var result = family == "SonyH9Serial" ? HeadsetExpansionSerial.ReadSony(port, trace) :
            HeadsetExpansionSerial.ReadAtk(port, trace);
        Verify(); return result;
    }
}
