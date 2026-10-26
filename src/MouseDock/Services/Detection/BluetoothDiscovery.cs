using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MouseDock.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// WinRT 蓝牙发现：
    /// 1) BLE 设备走 GATT 0x180F / 0x2A19 读真实电量（未连接就是读不到）；
    /// 2) 经典蓝牙设备按 Class of Device 判类型，连接状态来自 Windows。
    /// </summary>
    public static class BluetoothDiscovery
    {
        public static List<string> LastErrors { get; } = new List<string>();

        private static void Trace(string message)
        {
            LastErrors.Add(message);
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mousedock-bt.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// 全部过程放到线程池：WinRT 的 await 续体一旦回到 WPF 的 Dispatcher 上下文，
        /// 在同步等待（GetAwaiter().GetResult()）时会被卡住，所以这里必须 ConfigureAwait(false)。
        /// </summary>
        public static Task<List<DetectedDevice>> EnumerateAsync(int perDeviceTimeoutMs = 1500, Func<string, Models.DeviceKind?> hint = null)
        {
            return Task.Run(async () =>
            {
                LastErrors.Clear();
                Trace("EnumerateAsync 进入(线程池)");
                var result = new List<DetectedDevice>();

                await SafeAsync(() => CollectBle(result, perDeviceTimeoutMs, hint), "BLE").ConfigureAwait(false);
                await SafeAsync(() => CollectClassic(result), "经典蓝牙").ConfigureAwait(false);

                Trace($"EnumerateAsync 结束，命中 {result.Count}");
                return result;
            });
        }

        private static async Task SafeAsync(Func<Task> work, string stage)
        {
            try { await work(); }
            catch (Exception ex) { Trace(stage + " 失败: " + ex.GetType().FullName + " " + ex.Message); }
        }

        private static async Task CollectBle(List<DetectedDevice> sink, int timeoutMs, Func<string, Models.DeviceKind?> hint)
        {
            Trace("BLE 开始枚举");
            string selector = BluetoothLEDevice.GetDeviceSelector();
            Trace("BLE selector 长度=" + (selector?.Length ?? -1));
            var infos = await DeviceInformation.FindAllAsync(selector).AsTask().ConfigureAwait(false);
            Trace("BLE 枚举到 " + (infos == null ? "null" : infos.Count.ToString()) + " 台");

            foreach (var info in infos)
            {
                BluetoothLEDevice device = null;
                try
                {
                    device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
                }
                catch (Exception ex) { LastErrors.Add("BLE 打开失败 " + info.Name + ": " + ex.GetType().Name); }

                if (device == null) continue;

                try
                {
                    var kind = KindFromAppearance(device, info.Name);
                    if (kind == null && hint != null) kind = hint(info.Name);
                    if (kind == null) kind = await KindFromGattAsync(device, timeoutMs).ConfigureAwait(false);

                    // Appearance 常常是 0x0000（国产外设基本不填），不能因此直接丢掉：
                    // 先试读一次 GATT 电量，能读到就按名字推类型照样显示
                    int earlyLevel = -1;
                    if (kind == null && device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                    {
                        earlyLevel = await ReadBatteryAsync(device, timeoutMs).ConfigureAwait(false);
                        if (earlyLevel >= 0)
                        {
                            kind = VendorMap.KindFromName(info.Name) ?? DeviceKind.Mouse;
                            Trace($"BLE {info.Name}：Appearance=0 但读到了电量，按 {kind} 处理");
                        }
                    }

                    if (kind == null)
                    {
                        Trace($"BLE 跳过 {info.Name}（Appearance 认不出，raw=0x{(device.Appearance?.RawValue ?? 0):X4}）");
                        continue;
                    }

                    bool connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    if (!connected)
                    {
                        // 只显示现在连接中的：配对了但没连的一律不列
                        Trace($"BLE 跳过 {info.Name}（当前未连接）");
                        continue;
                    }

                    int level = earlyLevel >= 0 ? earlyLevel : await ReadBatteryAsync(device, timeoutMs).ConfigureAwait(false);

                    sink.Add(new DetectedDevice
                    {
                        Id = "ble-" + device.BluetoothAddress.ToString("X"),
                        Name = string.IsNullOrWhiteSpace(info.Name) ? "BLE 设备" : info.Name,
                        Kind = kind.Value,
                        Transport = "蓝牙 LE",
                        Level = level,
                        Connected = connected,
                        Detail = device.BluetoothAddress.ToString("X12"),
                        PhysicalKey = VendorMap.PhysicalKeyFromName(info.Name),
                        Note = level >= 0 ? "" : (connected
                            ? "已连接但没读到 GATT 电量服务（0x180F）"
                            : "设备待机中，连上后才能读 GATT 电量")
                    });
                }
                catch (Exception ex) { LastErrors.Add("BLE 处理失败 " + info.Name + ": " + ex.GetType().Name); }
                finally { device.Dispose(); }
            }
        }

        private static async Task CollectClassic(List<DetectedDevice> sink)
        {
            Trace("经典蓝牙开始枚举");
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask().ConfigureAwait(false);
            Trace($"经典蓝牙枚举到 {infos.Count} 台");

            foreach (var info in infos)
            {
                BluetoothDevice device = null;
                try { device = await BluetoothDevice.FromIdAsync(info.Id).AsTask().ConfigureAwait(false); }
                catch (Exception ex) { LastErrors.Add("经典打开失败 " + info.Name + ": " + ex.GetType().Name); }

                if (device == null) continue;

                try
                {
                    if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                    {
                        // 只显示现在连接中的：配对了但没连的一律不列
                        Trace($"经典跳过 {info.Name}（当前未连接）");
                        continue;
                    }

                    var kind = UsageMap.FromClassOfDevice(device.ClassOfDevice.RawValue);
                    if (kind != DeviceKind.Headset && kind != DeviceKind.Mouse && kind != DeviceKind.Keyboard)
                    {
                        LastErrors.Add($"经典跳过 {info.Name}（COD=0x{device.ClassOfDevice.RawValue:X6} → {kind}）");
                        continue;
                    }

                    // 已经在 BLE 里出现过的同名设备不重复列
                    if (sink.Any(d => string.Equals(d.Name, info.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // 耳机：走 HFP 的 Apple 扩展（AT+IPHONEACCEV）读电量。
                    // HFP 通道被 Windows 占用时连不上，直接返回 -1，不影响其它设备。
                    int hfpLevel = -1;
                    if (kind == Models.DeviceKind.Headset && !string.IsNullOrWhiteSpace(info.Name))
                    {
                        try { hfpLevel = HfpBattery.TryRead(info.Name, 5200); } catch { }

                        // 第二选择：厂商私有 RFCOMM 服务（配置过协议就直接读）
                        if (hfpLevel < 0)
                        {
                            try { hfpLevel = VendorBt.TryRead(info.Name); } catch { }
                        }

                        // 都没有 → 这台设备第一次出现时，后台自动跑一次协议探测并记住结果
                        if (hfpLevel < 0 && !VendorBt.AlreadyProbed(info.Name))
                        {
                            string devName = info.Name;
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    int v = VendorBt.AutoLearnAsync(devName, Trace).GetAwaiter().GetResult();
                                    Trace($"[自动识别] {devName} 结果 " + (v >= 0 ? v + "%" : "未知"));
                                }
                                catch (Exception ex) { Trace("[自动识别] 异常 " + ex.GetType().Name); }
                            });
                        }
                    }

                    sink.Add(new DetectedDevice
                    {
                        Id = "bt-" + device.BluetoothAddress.ToString("X"),
                        Name = string.IsNullOrWhiteSpace(info.Name) ? "蓝牙设备" : info.Name,
                        Kind = kind,
                        Transport = "蓝牙",
                        Level = hfpLevel,
                        Connected = true,
                        Detail = device.BluetoothAddress.ToString("X12"),
                        PhysicalKey = VendorMap.PhysicalKeyFromName(info.Name),
                        Note = hfpLevel >= 0
                            ? "HFP 扩展上报（AT+IPHONEACCEV）"
                            : (kind == Models.DeviceKind.Headset
                                ? "HFP 通道被 Windows 免提占用，读不到电量（禁用免提节点后可读）"
                                : "经典蓝牙 HID：只有设备另走 BLE 电池服务时才能读到电量")
                    });
                }
                catch { }
                finally { device.Dispose(); }
            }
        }

        private static async Task<int> ReadBatteryAsync(BluetoothLEDevice device, int timeoutMs)
        {
            try
            {
                var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                    .AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);

                if (services.Status != GattCommunicationStatus.Success) return -1;

                foreach (var service in services.Services)
                {
                    var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                        .AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);

                    if (chars.Status != GattCommunicationStatus.Success) continue;

                    foreach (var characteristic in chars.Characteristics)
                    {
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached)
                            .AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);

                        if (value.Status != GattCommunicationStatus.Success) continue;

                        using var reader = DataReader.FromBuffer(value.Value);
                        byte level = reader.ReadByte();
                        if (level <= 100) return level;
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>BLE 没给 Appearance 时，看有没有 HID 服务（0x1812）：有就按输入设备处理。</summary>
        private static async Task<Models.DeviceKind?> KindFromGattAsync(BluetoothLEDevice device, int timeoutMs)
        {
            try
            {
                var hidService = new Guid("00001812-0000-1000-8000-00805F9B34FB");
                var services = await device.GetGattServicesForUuidAsync(hidService, BluetoothCacheMode.Uncached)
                    .AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
                if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
                    return Models.DeviceKind.Mouse;
            }
            catch { }
            return null;
        }

        /// <summary>BLE Appearance 分类（15 = HID）→ 设备类型；认不出就返回 null，避免把手机手表列进来。</summary>
        private static DeviceKind? KindFromAppearance(BluetoothLEDevice device, string name)
        {
            try
            {
                ushort raw = device.Appearance?.RawValue ?? 0;
                int category = raw >> 6;
                int sub = raw & 0x3F;
                if (category == 15)
                {
                    return sub switch
                    {
                        1 => DeviceKind.Keyboard,
                        2 => DeviceKind.Mouse,
                        3 => DeviceKind.Controller,
                        4 => DeviceKind.Controller,
                        _ => DeviceKind.Mouse
                    };
                }
            }
            catch { }

            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("mouse") || n.Contains("鼠标")) return DeviceKind.Mouse;
            if (n.Contains("keyboard") || n.Contains("键盘")) return DeviceKind.Keyboard;
            if (n.Contains("buds") || n.Contains("headset") || n.Contains("耳机") || n.Contains("airpods") || n.Contains("freebuds"))
                return DeviceKind.Headset;
            return null;
        }
    }
}
