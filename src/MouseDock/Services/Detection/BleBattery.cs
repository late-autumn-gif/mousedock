using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// BLE 标准电池服务：0x180F 服务里的 0x2A19 特性，一个字节 0-100。
    /// 只对已配对的 BLE 设备有效；经典蓝牙（BR/EDR）耳机走不到这条。
    /// </summary>
    public static class BleBattery
    {
        private static readonly Guid BatteryService = new("0000180f-0000-1000-8000-00805f9b34fb");
        private static readonly Guid BatteryLevel = new("00002a19-0000-1000-8000-00805f9b34fb");

        public sealed class Reading
        {
            public ulong Address;
            public string Name = "";
            public int Level = -1;
            public string Reason = "";
        }

        /// <summary>带超时地读一台设备的电池；读不到返回 -1 并给出原因。</summary>
        public static async Task<Reading> ReadAsync(ulong address, string name, int timeoutMs = 5000)
        {
            var reading = new Reading { Address = address, Name = name ?? "" };

            var work = ReadCore(reading);
            var finished = await Task.WhenAny(work, Task.Delay(timeoutMs));
            if (finished != work)
            {
                reading.Level = -1;
                reading.Reason = "超时";
                return reading;
            }

            await work;
            return reading;
        }

        private static async Task ReadCore(Reading reading)
        {
            BluetoothLEDevice dev = null;
            try
            {
                dev = await BluetoothLEDevice.FromBluetoothAddressAsync(reading.Address);
                if (dev == null)
                {
                    reading.Reason = "拿不到设备对象（未配对或地址是随机的）";
                    return;
                }

                var services = await dev.GetGattServicesForUuidAsync(BatteryService, BluetoothCacheMode.Uncached);
                if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                {
                    reading.Reason = $"没有 0x180F 服务（{services.Status}）";
                    return;
                }

                var service = services.Services[0];
                var chars = await service.GetCharacteristicsForUuidAsync(BatteryLevel, BluetoothCacheMode.Uncached);
                if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                {
                    reading.Reason = $"没有 0x2A19 特性（{chars.Status}）";
                    return;
                }

                var chr = chars.Characteristics[0];
                var result = await chr.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (result.Status != GattCommunicationStatus.Success)
                {
                    reading.Reason = $"读失败（{result.Status}）";
                    return;
                }

                var buffer = result.Value;
                var bytes = new byte[buffer.Length];
                using (var reader = DataReader.FromBuffer(buffer))
                    reader.ReadBytes(bytes);

                if (bytes.Length == 0)
                {
                    reading.Reason = "响应为空";
                    return;
                }

                int level = bytes[0];
                reading.Level = level <= 100 ? level : -1;
                reading.Reason = level <= 100 ? "ok" : $"越界 {level}";
            }
            catch (Exception ex)
            {
                reading.Reason = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                try { dev?.Dispose(); } catch { }
            }
        }

        /// <summary>扫一圈广播，把所有能连上的设备挨个试一遍电池服务。</summary>
        public static async Task<List<Reading>> ScanAndReadAsync(int scanSeconds, int perDeviceTimeoutMs = 5000)
        {
            var hits = BleScanner.Scan(scanSeconds);
            var results = new List<Reading>();

            foreach (var hit in hits)
            {
                // 只试有名字的，没名字的多半是手机/信标，试了也是白等
                if (string.IsNullOrWhiteSpace(hit.LocalName)) continue;

                var r = await ReadAsync(hit.Address, hit.LocalName, perDeviceTimeoutMs);
                results.Add(r);
            }

            return results.OrderByDescending(r => r.Level).ThenBy(r => r.Name).ToList();
        }

        /// <summary>
        /// 逐条试探私有命令通道：订阅通知，按顺序往可写特性发单字节命令，
        /// 记录每条命令之后收到什么。只发查询类短帧，遇到任意回包就停。
        /// </summary>
        public static async Task<string> ProbeAsync(ulong address, byte from, byte to, int gapMs = 900, int timeoutMs = 90000)
        {
            var lines = new List<string>();
            var hits = new List<string>();
            BluetoothLEDevice dev = null;
            GattCharacteristic writeChr = null;
            GattCharacteristic notifyChr = null;
            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = null;

            var work = Task.Run(async () =>
            {
                try
                {
                    dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                    if (dev == null) { lines.Add("拿不到设备对象"); return; }

                    var services = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    foreach (var svc in services.Services)
                    {
                        // 只看厂商自定义服务（0xAE00 这类短 UUID）
                        if (svc.Uuid.ToString().StartsWith("0000ae", StringComparison.OrdinalIgnoreCase))
                        {
                            var chars = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                            foreach (var chr in chars.Characteristics)
                            {
                                var props = chr.CharacteristicProperties;
                                if ((props & GattCharacteristicProperties.Notify) != 0) notifyChr = chr;
                                else if ((props & (GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse)) != 0) writeChr = chr;
                            }
                        }
                    }

                    if (writeChr == null || notifyChr == null)
                    {
                        lines.Add($"私有通道没找全：写={writeChr?.Uuid} 通知={notifyChr?.Uuid}");
                        return;
                    }

                    lines.Add($"写 {writeChr.Uuid}  →  通知 {notifyChr.Uuid}");
                    lines.Add("");

                    handler = (_, e) =>
                    {
                        try
                        {
                            var buf = e.CharacteristicValue;
                            var bytes = new byte[buf.Length];
                            using (var reader = DataReader.FromBuffer(buf))
                                reader.ReadBytes(bytes);
                            lock (hits)
                                hits.Add(BitConverter.ToString(bytes));
                        }
                        catch { }
                    };
                    notifyChr.ValueChanged += handler;

                    var sub = await notifyChr.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    lines.Add($"订阅: {sub}");

                    for (int cmd = from; cmd <= to; cmd++)
                    {
                        int before;
                        lock (hits) before = hits.Count;

                        var payload = new byte[] { (byte)cmd };
                        var status = await writeChr.WriteValueWithResultAsync(
                            CryptographicBuffer.CreateFromByteArray(payload),
                            GattWriteOption.WriteWithoutResponse);

                        await Task.Delay(gapMs);

                        int after;
                        string got;
                        lock (hits)
                        {
                            after = hits.Count;
                            got = after > before ? string.Join(" | ", hits.Skip(before)) : "";
                        }

                        lines.Add($"命令 0x{cmd:X2}  写={status.Status,-16} 回包={got}");

                        if (after > before)
                        {
                            lines.Add("");
                            lines.Add($">>> 0x{cmd:X2} 有响应，停止试探");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lines.Add($"异常: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (notifyChr != null && handler != null)
                        {
                            notifyChr.ValueChanged -= handler;
                            await notifyChr.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.None);
                        }
                    }
                    catch { }
                    try { dev?.Dispose(); } catch { }
                }
            });

            var finished = await Task.WhenAny(work, Task.Delay(timeoutMs));
            if (finished != work) lines.Add($"整体超时（{timeoutMs}ms）");
            else await work;

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>订阅一台设备所有 Notify 特性，纯被动收帧，不写任何命令。</summary>
        public static async Task<string> ListenAsync(ulong address, int seconds, int timeoutMs = 30000)
        {
            var lines = new List<string>();
            var received = new List<string>();
            BluetoothLEDevice dev = null;
            var subs = new List<(GattCharacteristic Chr, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> Handler)>();
            var handlers = new Dictionary<GattCharacteristic, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>>();

            var work = Task.Run(async () =>
            {
                try
                {
                    dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                    if (dev == null) { lines.Add("拿不到设备对象"); return; }

                    var services = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    foreach (var svc in services.Services)
                    {
                        var chars = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        foreach (var chr in chars.Characteristics)
                        {
                            if ((chr.CharacteristicProperties & GattCharacteristicProperties.Notify) == 0) continue;

                            var local = chr;
                            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (_, e) =>
                            {
                                try
                                {
                                    var buf = e.CharacteristicValue;
                                    var bytes = new byte[buf.Length];
                                    using (var reader = DataReader.FromBuffer(buf))
                                        reader.ReadBytes(bytes);
                                    lock (received)
                                        received.Add($"{DateTime.Now:HH:mm:ss.fff}  {local.Uuid}  {BitConverter.ToString(bytes)}");
                                }
                                catch { }
                            };

                            var status = await chr.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            if (status != GattCommunicationStatus.Success)
                            {
                                lines.Add($"订阅失败 {chr.Uuid}: {status}");
                                continue;
                            }

                            chr.ValueChanged += handler;
                            subs.Add((chr, handler));
                            lines.Add($"已订阅 {svc.Uuid} / {chr.Uuid}");
                        }
                    }

                    if (subs.Count == 0) lines.Add("这台设备没有 Notify 特性。");

                    await Task.Delay(seconds * 1000);
                }
                catch (Exception ex)
                {
                    lines.Add($"异常: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    foreach (var (chr, handler) in subs)
                    {
                        try
                        {
                            chr.ValueChanged -= handler;
                            await chr.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.None);
                        }
                        catch { }
                    }
                    try { dev?.Dispose(); } catch { }
                }
            });

            var finished = await Task.WhenAny(work, Task.Delay(timeoutMs));
            if (finished != work) lines.Add($"整体超时（{timeoutMs}ms）");
            else await work;

            lines.Add("");
            lines.Add($"收到 {received.Count} 帧：");
            lines.AddRange(received);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>把一台设备能连上的所有 GATT 服务和特性全打出来，用来找私有电量服务。</summary>
        public static async Task<string> DumpServicesAsync(ulong address, int timeoutMs = 15000)
        {
            var lines = new List<string>();
            BluetoothLEDevice dev = null;

            var work = Task.Run(async () =>
            {
                try
                {
                    dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                    if (dev == null)
                    {
                        lines.Add("拿不到设备对象（未配对或随机地址）");
                        return;
                    }

                    lines.Add($"设备名: {dev.Name}");
                    lines.Add($"连接状态: {dev.ConnectionStatus}");
                    lines.Add("");

                    var services = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    lines.Add($"服务数: {services.Services.Count}（状态 {services.Status}）");

                    foreach (var svc in services.Services)
                    {
                        lines.Add("");
                        lines.Add($"服务 {svc.Uuid}");

                        var chars = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        foreach (var chr in chars.Characteristics)
                        {
                            lines.Add($"   特性 {chr.Uuid}  属性={chr.CharacteristicProperties}");

                            if ((chr.CharacteristicProperties & GattCharacteristicProperties.Read) != 0)
                            {
                                try
                                {
                                    var r = await chr.ReadValueAsync(BluetoothCacheMode.Uncached);
                                    if (r.Status == GattCommunicationStatus.Success)
                                    {
                                        var buf = r.Value;
                                        var bytes = new byte[buf.Length];
                                        using (var reader = DataReader.FromBuffer(buf))
                                            reader.ReadBytes(bytes);
                                        lines.Add($"      读值 {BitConverter.ToString(bytes)}");
                                    }
                                    else lines.Add($"      读失败 {r.Status}");
                                }
                                catch (Exception ex) { lines.Add($"      读异常 {ex.GetType().Name}"); }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    lines.Add($"异常: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { dev?.Dispose(); } catch { }
                }
            });

            var finished = await Task.WhenAny(work, Task.Delay(timeoutMs));
            if (finished != work) lines.Add($"超时（{timeoutMs}ms）");
            else await work;

            return string.Join(Environment.NewLine, lines);
        }

        public static string Describe(List<Reading> readings)
        {
            if (readings.Count == 0) return "没有可尝试的 BLE 设备（扫描结果里没有带名字的）。";

            var lines = new List<string> { $"BLE 电池读取：{readings.Count} 台", "" };
            foreach (var r in readings)
            {
                string level = r.Level >= 0 ? $"{r.Level}%" : "—";
                lines.Add($"{r.Address:X12}  {r.Name,-28}  {level,5}  {r.Reason}");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
