using System;
using System.Buffers.Binary;
using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionSony
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, bool dualSense,
        bool bluetooth, bool adapter)
    {
        Require(b.Length == (bluetooth ? 78 : 64) &&
            b[0] == (bluetooth ? dualSense ? 0x31 : 0x11 : 1), "Invalid Sony controller report");
        if (bluetooth)
            Require(Crc(b.AsSpan(0, b.Length - 4)) ==
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(b.Length - 4)), "Sony Bluetooth CRC mismatch");
        Require(!adapter || (b[31] & 4) == 0, "Sony controller adapter is unpaired");
        byte value = b[dualSense ? bluetooth ? 54 : 53 : bluetooth ? 32 : 30];
        int level = value & 15;
        if (dualSense)
        {
            int state = value >> 4;
            if (state == 2) return new(-1, false, true, ReferenceBatteryProtocol.State.Full, "device-band=full");
            Require(state is 0 or 1 && level <= 10, "Invalid DualSense battery state");
            return Band(level, 10, state == 1, "device");
        }
        bool charging = (value & 16) != 0;
        if (charging && level == 11) return new(-1, false, true, ReferenceBatteryProtocol.State.Full, "device-band=full");
        Require(level <= 10, "Invalid DualShock battery level");
        return Band(level, 10, charging, "device");
    }

    internal static uint Crc(ReadOnlySpan<byte> report)
    {
        uint crc = uint.MaxValue;
        Add(0xA1);
        foreach (byte value in report) Add(value);
        return ~crc;
        void Add(byte value)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0U : 0xEDB88320U);
        }
    }
}
