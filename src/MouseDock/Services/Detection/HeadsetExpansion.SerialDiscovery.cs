using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionSerialDiscovery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DevInfo { internal uint Size; internal Guid ClassGuid; internal uint DevInst; internal IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { internal Guid Format; internal uint Id; }
    private static readonly Guid portsClass = new("4d36e978-e325-11ce-bfc1-08002be10318");
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static extern IntPtr ClassDevices(ref Guid guid, IntPtr enumerator, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiEnumDeviceInfo")]
    private static extern bool NextDevice(IntPtr set, uint index, ref DevInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    private static extern bool InstanceId(IntPtr set, ref DevInfo info, StringBuilder text, uint capacity, out uint needed);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiOpenDevRegKey", SetLastError = true)]
    private static extern IntPtr OpenRegistry(IntPtr set, ref DevInfo info, uint scope, uint profile, uint type, uint access);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiDestroyDeviceInfoList")]
    private static extern bool Destroy(IntPtr set);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
    private static extern int RegistryValue(IntPtr key, string name, IntPtr reserved, out uint type, byte[] data, ref uint size);
    [DllImport("advapi32.dll", EntryPoint = "RegCloseKey")]
    private static extern int CloseRegistry(IntPtr key);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    private static extern int Locate(out uint devInst, string instance, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static extern int Status(out uint status, out uint problem, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static extern int Property(uint devInst, ref PropertyKey key, out uint type, byte[] data, ref uint size, uint flags);

    internal static Guid Container(string instance)
    {
        if (string.IsNullOrEmpty(instance) || Locate(out uint node, instance, 0) != 0 ||
            Status(out uint state, out uint problem, node, 0) != 0 || problem != 0 || (state & 0x400) != 0)
            return Guid.Empty;
        var key = new PropertyKey { Format = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 2 };
        var raw = new byte[16]; uint length = 16;
        return Property(node, ref key, out uint type, raw, ref length, 0) == 0 && type == 13 && length == 16
            ? new Guid(raw) : Guid.Empty;
    }
    internal static IReadOnlyList<(string Port, string Instance, Guid Owner)> Ports(ushort vid, ushort pid)
    {
        Guid guid = portsClass;
        IntPtr set = ClassDevices(ref guid, IntPtr.Zero, IntPtr.Zero, 2);
        Require(set != IntPtr.Zero && set != new IntPtr(-1), "Present COM inventory unavailable");
        var result = new List<(string, string, Guid)>();
        bool complete = false;
        try
        {
            for (uint i = 0; i < 256; i++)
            {
                var info = new DevInfo { Size = (uint)Marshal.SizeOf<DevInfo>() };
                if (!NextDevice(set, i, ref info))
                {
                    Require(Marshal.GetLastWin32Error() == 259, "Present COM enumeration failed");
                    complete = true; break;
                }
                if (Status(out uint state, out uint problem, info.DevInst, 0) != 0 || problem != 0 || (state & 0x400) != 0) continue;
                var name = new StringBuilder(2048);
                Require(InstanceId(set, ref info, name, (uint)name.Capacity, out _), "Present COM instance unavailable");
                if (!System.Text.RegularExpressions.Regex.IsMatch(name.ToString(),
                    $@"^USB\\VID_{vid:X4}&PID_{pid:X4}(?:&|\\)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                IntPtr key = OpenRegistry(set, ref info, 1, 0, 1, 1);
                Require(key != IntPtr.Zero && key != new IntPtr(-1), "Target COM registry metadata unavailable");
                try
                {
                    var b = new byte[128]; uint size = (uint)b.Length;
                    Require(RegistryValue(key, "PortName", IntPtr.Zero, out uint type, b, ref size) == 0 &&
                        type == 1 && size >= 4 && size <= b.Length && size % 2 == 0, "Target COM PortName unavailable");
                    string port = Encoding.Unicode.GetString(b, 0, (int)size).TrimEnd('\0');
                    string instance = name.ToString(); Guid owner = Container(instance);
                    Require(owner != Guid.Empty, "Target COM container unavailable");
                    result.Add((port, instance, owner));
                }
                finally { CloseRegistry(key); }
            }
        }
        finally { Destroy(set); }
        Require(complete, "Present COM inventory exceeded bounded enumeration");
        return result;
    }
}
