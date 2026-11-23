using System.Collections.Generic;
using System.Windows;

namespace MouseDock.Controls;

// Physical positions on a 104-key ANSI board, in key-width units. E0 make codes
// carry bit 8; Pause has its own E1 slot. Layout is independent of input language.
internal static class PetKeyboardLayout
{
    internal static readonly IReadOnlyDictionary<int, Rect> Caps = Build();

    private static Dictionary<int, Rect> Build()
    {
        var caps = new Dictionary<int, Rect>();
        void Key(int scan, double x, int y, double width = 1, double height = 1) =>
            caps.Add(scan, new Rect(x, y, width, height));
        void Row(int y, double x, params int[] scans)
        {
            foreach (int scan in scans) Key(scan, x++, y);
        }

        Key(0x01, 0, 0);
        Row(0, 2, 0x3b, 0x3c, 0x3d, 0x3e);
        Row(0, 6.5, 0x3f, 0x40, 0x41, 0x42);
        Row(0, 11, 0x43, 0x44, 0x57, 0x58);
        Row(1, 0, 0x29, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
        Key(0x0e, 13, 1, 2);
        Key(0x0f, 0, 2, 1.5);
        Row(2, 1.5, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b);
        Key(0x2b, 13.5, 2, 1.5);
        Key(0x3a, 0, 3, 1.75);
        Row(3, 1.75, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28);
        Key(0x1c, 12.75, 3, 2.25);
        Key(0x2a, 0, 4, 2.25);
        Row(4, 2.25, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35);
        Key(0x36, 12.25, 4, 2.75);
        Key(0x1d, 0, 5, 1.25);
        Key(0x15b, 1.25, 5, 1.25);
        Key(0x38, 2.5, 5, 1.25);
        Key(0x39, 3.75, 5, 6.25);
        Key(0x138, 10, 5, 1.25);
        Key(0x15c, 11.25, 5, 1.25);
        Key(0x15d, 12.5, 5, 1.25);
        Key(0x11d, 13.75, 5, 1.25);
        const double nav = 15.45, pad = 18.9;
        Row(0, nav, 0x137, 0x46, 0x245);
        Row(1, nav, 0x152, 0x147, 0x149);
        Row(2, nav, 0x153, 0x14f, 0x151);
        Key(0x148, nav + 1, 4);
        Row(5, nav, 0x14b, 0x150, 0x14d);
        Row(1, pad, 0x45, 0x135, 0x37, 0x4a);
        Row(2, pad, 0x47, 0x48, 0x49);
        Key(0x4e, pad + 3, 2, 1, 2);
        Row(3, pad, 0x4b, 0x4c, 0x4d);
        Row(4, pad, 0x4f, 0x50, 0x51);
        Key(0x11c, pad + 3, 4, 1, 2);
        Key(0x52, pad, 5, 2);
        Key(0x53, pad + 2, 5);
        return caps;
    }
}
