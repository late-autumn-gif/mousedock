using System;
using System.Collections.Generic;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

/// <summary>
/// 通用厂商读取器表：把原来散在 TryCollect 里的十几次 if-else 串行调用收成一张表。
/// 加一个品牌是往表里加一项，不改扫描主循环；顺序就是表顺序，与原逻辑一致。
/// 表里的 Applies 只做纯判定，Read 只发只读查询；异常由调度器统一记录。
/// </summary>
internal static class VendorReaderTable
{
    internal sealed class Context
    {
        public ushort Vid;
        public ushort Pid;
        public string Product;
        public ushort UsagePage;
        public DeviceKind Kind;
    }

    internal sealed class Entry
    {
        public string Label;
        public Func<Context, bool> Applies;
        public Func<Context, (int Level, bool Charging)> Read;
    }

    internal static readonly Entry[] Readers =
    {
        // 第一批：写一帧读一帧，按 VID 分派
        new()
        {
            Label = "Razer",
            Applies = c => c.Vid == 0x1532,
            Read = c => (VendorProtocol.TryReadRazer(c.Vid, c.Pid), false)
        },
        new()
        {
            Label = "ROG",
            Applies = c => c.Vid == 0x0B05,
            Read = c => (VendorProtocol.TryReadRog(c.Vid, c.Pid), false)
        },
        new()
        {
            Label = "SteelSeries",
            Applies = c => c.Vid == 0x1038,
            Read = c => (VendorProtocol.TryReadSteelSeries(c.Vid, c.Pid), false)
        },
        new()
        {
            Label = "Finalmouse",
            Applies = c => c.Vid == 0x361D,
            Read = c => (VendorProtocol.TryReadFinalmouse(c.Vid, c.Pid), false)
        },
        new()
        {
            Label = "Compx",
            Applies = c => c.Vid is 0x373B or 0x3554 or 0x33E4,
            Read = c => (VendorProtocol.TryReadCompx(c.Vid, c.Pid), false)
        },

        // 第二批：帧形已确认的厂商读取器
        new()
        {
            Label = "厂商 Family",
            Applies = _ => true,
            Read = c => (VendorProtocol.TryReadFamily(c.Vid, c.Pid), false)
        },

        // 第三批：报告 ID / 命令字是变量的，靠试探 + 强校验
        new()
        {
            Label = "厂商 Extra",
            Applies = _ => true,
            Read = c => (VendorProtocol.TryReadExtra(c.Vid, c.Pid), false)
        },

        // 第四批：G-Wolves / ipi / RAWM / passive 型
        new()
        {
            Label = "厂商 Third",
            Applies = _ => true,
            Read = c => (VendorProtocol.TryReadThird(c.Vid, c.Pid), false)
        },

        // 前行者（X87 三模及全系），两套协议
        new()
        {
            Label = "Eweadn",
            Applies = c => c.UsagePage >= 0xFF00 && VendorProtocol.IsEweadnVid(c.Vid),
            Read = c => (VendorProtocol.TryReadEweadn(c.Vid, c.Pid), false)
        },

        // 瓦尔基里（VK87 / VK99 / Valkyrie 75）
        new()
        {
            Label = "Valkyrie",
            Applies = c => VendorProtocol.IsValkyrieDevice(c.Vid, c.Pid, c.Product),
            Read = c => (VendorProtocol.TryReadValkyrie(c.Vid, c.Pid, c.Kind == DeviceKind.Keyboard), false)
        },

        // 英菲克（Inphic），电量靠接收器主动上报
        new()
        {
            Label = "Inphic",
            Applies = c => c.UsagePage >= 0xFF00 && VendorProtocol.IsInphicDevice(c.Vid, c.Pid, c.Product),
            Read = c =>
            {
                int level = VendorProtocol.TryReadInphic(c.Vid, c.Pid, out bool charging);
                return (level, charging);
            }
        }
    };
}

