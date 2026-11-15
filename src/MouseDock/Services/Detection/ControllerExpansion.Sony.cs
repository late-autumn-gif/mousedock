using System;
using System.Buffers.Binary;
using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionSony
{
    private const int MaximumBand = 10;
    private static readonly uint[] CrcTable = CreateCrcTable();

    internal static ReferenceBatteryProtocol.Reading Decode(byte[] report, bool dualSense,
        bool bluetooth, bool adapter)
    {
        var (id, length, batteryOffset) = (dualSense, bluetooth) switch
        {
            (false, false) => (0x01, 64, 30),
            (false, true)  => (0x11, 78, 32),
            (true, false)  => (0x01, 64, 53),
            (true, true)   => (0x31, 78, 54)
        };
        Require(report.Length == length && report[0] == id, "Invalid Sony controller report");

        if (bluetooth)
        {
            var frame = report.AsSpan();
            Require(Crc(frame[..^4]) == BinaryPrimitives.ReadUInt32LittleEndian(frame[^4..]),
                "Sony Bluetooth CRC mismatch");
        }
        Require(!adapter || (report[31] & 0x04) == 0, "Sony controller adapter is unpaired");

        byte status = report[batteryOffset];
        int band = status & 0x0f;
        if (dualSense)
        {
            int powerState = status >> 4;
            if (powerState == 2) return Full();
            Require(powerState is 0 or 1 && band <= MaximumBand, "Invalid DualSense battery state");
            return Band(band, MaximumBand, powerState == 1, "device");
        }

        bool cableConnected = (status & 0x10) != 0;
        if (cableConnected && band == 11) return Full();
        Require(band <= MaximumBand, "Invalid DualShock battery level");
        return Band(band, MaximumBand, cableConnected, "device");
    }

    private static ReferenceBatteryProtocol.Reading Full() =>
        new(-1, false, true, ReferenceBatteryProtocol.State.Full, "device-band=full");

    internal static uint Crc(ReadOnlySpan<byte> report)
    {
        // IEEE CRC-32 with the Sony Bluetooth input tag prepended.
        uint remainder = Update(uint.MaxValue, 0xa1);
        foreach (byte value in report) remainder = Update(remainder, value);
        return ~remainder;
    }

    private static uint Update(uint remainder, byte value) =>
        (remainder >> 8) ^ CrcTable[(byte)(remainder ^ value)];

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint remainder = i;
            for (int bit = 0; bit < 8; bit++)
                remainder = (remainder >> 1) ^ ((remainder & 1) != 0 ? 0xedb88320u : 0u);
            table[i] = remainder;
        }
        return table;
    }
}
