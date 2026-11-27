using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MouseDock.Services;

internal static class WindowsAudioRouting
{
    internal sealed record Endpoint(string Id, string Name);
    internal sealed record Snapshot(Endpoint[] Devices, string DefaultId, string Error = null);
    private static readonly object SwitchGate = new();
    private static readonly Role[] DefaultRoles = [Role.Console, Role.Multimedia];

    internal static Snapshot List(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string current = ReadDefault(enumerator, flow, Role.Console);
            var endpoints = new List<Endpoint>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        string name = device.FriendlyName;
                        endpoints.Add(new(device.ID, string.IsNullOrWhiteSpace(name) ? "未命名音频设备" : name));
                    }
                    catch (COMException) { /* Endpoint disappeared between enumeration and read. */ }
                }
            }
            return new(MenuOrder(endpoints, current), current);
        }
        catch { return new([], null, "无法读取 Windows 音频设备"); }
    }

    internal static Endpoint[] MenuOrder(IEnumerable<Endpoint> endpoints, string current)
    {
        return endpoints.DistinctBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(e => IsSame(e.Id, current))
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(group => group.Select((endpoint, index) => index == 0 ? endpoint :
                endpoint with { Name = $"{endpoint.Name}（{index + 1}）" }))
            .ToArray();
    }

    internal static AudioActionResult Switch(DataFlow flow, string endpointId)
    {
        if (flow is not (DataFlow.Render or DataFlow.Capture) || string.IsNullOrWhiteSpace(endpointId))
            return new(false, "请选择有效的音频设备");

        lock (SwitchGate)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var target = enumerator.GetDevice(endpointId);
                if (target.DataFlow != flow || target.State != DeviceState.Active)
                    return new(false, "所选设备已断开或方向不匹配，请重新打开菜单");

                string Read(Role role) => ReadDefault(enumerator, flow, role);
                if (DefaultRoles.All(role => IsSame(Read(role), endpointId)))
                    return new(true, "当前已是默认设备");

                // Windows exposes public readback through MMDevice. Writing uses the
                // desktop policy config COM interface used by Windows audio switchers.
                object client = new PolicyConfigClient();
                try
                {
                    var policy = (IPolicyConfig)client;
                    return ApplyDefaults(endpointId, Read, (role, id) =>
                        Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role)));
                }
                finally { Marshal.ReleaseComObject(client); }
            }
            catch { return new(false, "无法切换默认音频设备，请在 Windows 声音设置中重试"); }
        }
    }

    internal static AudioActionResult ApplyDefaults(string id, Func<Role, string> read, Action<Role, string> write)
    {
        var previous = DefaultRoles.ToDictionary(role => role, read);
        var attempted = new List<Role>();
        try
        {
            foreach (var role in DefaultRoles)
            {
                if (IsSame(previous[role], id)) continue;
                attempted.Add(role);
                write(role, id);
            }
            if (DefaultRoles.All(role => IsSame(read(role), id))) return new(true, "已切换默认音频设备");
        }
        catch { /* Restore any role that was already applied before an error. */ }

        bool restored = true;
        foreach (var role in attempted.AsEnumerable().Reverse())
        {
            try
            {
                string current = read(role);
                if (IsSame(current, previous[role])) continue;
                if (!IsSame(current, id) || previous[role] == null) { restored = false; continue; }
                write(role, previous[role]);
                restored &= IsSame(read(role), previous[role]);
            }
            catch { restored = false; }
        }
        return new(false, restored ? "Windows 未确认切换，请重新打开菜单后重试" :
            "默认设备可能只切换了一部分，请在 Windows 声音设置中确认");
    }

    private static string ReadDefault(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            using var current = enumerator.GetDefaultAudioEndpoint(flow, role);
            return current?.ID;
        }
        catch (COMException error) when (error.HResult == unchecked((int)0x80070490)) { return null; }
    }

    internal static bool IsSame(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    // The unused slots are never invoked. Preserve the IUnknown vtable position.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string endpointId, Role role);
    }
}
