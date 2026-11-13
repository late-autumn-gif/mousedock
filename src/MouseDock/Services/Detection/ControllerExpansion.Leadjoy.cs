using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionLeadjoy
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length == 64 && b[0] == 0xA5 && b[2] == 0xCC, "Invalid LeadJoy framing");
        int size = b[1];
        Require(size is >= 5 and <= 64 && unchecked((byte)b.Take(size - 1).Sum(x => (int)x)) == b[size - 1],
            "LeadJoy checksum mismatch");
        try
        {
            string json = new UTF8Encoding(false, true).GetString(b, 3, size - 4);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object && Unique(root), "LeadJoy JSON object/fields invalid");
            Require(Int(root, "c") == 14 && Int(root, "e") == 0, "LeadJoy command/status mismatch");
            Require(root.TryGetProperty("r", out var r) && r.ValueKind == JsonValueKind.Object && Unique(r),
                "LeadJoy battery object invalid");
            int main = Int(r, "mb"), secondary = Int(r, "sb"), charging = Int(r, "cs");
            Require(main is >= 0 and <= 100 && secondary is >= 0 and <= 255 && charging is >= 0 and <= 255,
                "LeadJoy battery fields out of range");
            return Percentage(main, charging != 0, "Fresh LeadJoy command14 battery response");
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        { throw new System.IO.InvalidDataException("Invalid LeadJoy UTF8/JSON", ex); }
    }

    private static bool Unique(JsonElement obj)
    {
        var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
        {
            if (!keys.Add(p.Name)) return false;
            if (p.Value.ValueKind == JsonValueKind.Object && !Unique(p.Value)) return false;
        }
        return true;
    }
    private static int Int(JsonElement obj, string key)
    {
        Require(obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out _), "LeadJoy integer field missing");
        return value.GetInt32();
    }
    internal static byte[] Request()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"c\":14}");
        var b = new byte[65];
        b[1] = 0xA5; b[2] = (byte)(json.Length + 4); b[3] = 0xCC;
        json.CopyTo(b, 4);
        b[json.Length + 4] = unchecked((byte)b.AsSpan(1, json.Length + 3).ToArray().Sum(x => (int)x));
        return b;
    }
}
