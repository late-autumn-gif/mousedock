using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MouseDock.Models;

namespace MouseDock.Services;

public static class CharacterAssignments
{
    private static readonly string[] Characters = { "rei", "asuka", "peek" };
    private static bool Valid(string character) => Characters.Contains(character);

    public static bool Ensure(AppSettings settings, IEnumerable<DeviceInfo> devices)
    {
        var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.DeviceCharacters ?? new())
            if (!string.IsNullOrWhiteSpace(pair.Key) && Valid(pair.Value)) saved[pair.Key] = pair.Value;
        bool changed = saved.Count != (settings.DeviceCharacters?.Count ?? 0);
        settings.DeviceCharacters = saved;
        var items = devices.Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .DistinctBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var order = Characters.OrderBy(c => c == settings.ReferenceCharacter ? 0 : 1).ToArray();
        var usage = Characters.ToDictionary(c => c,
            c => items.Count(d => saved.TryGetValue(d.Id, out var character) && character == c));
        foreach (var device in items.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (saved.ContainsKey(device.Id)) continue;
            string character = order.OrderBy(c => usage[c]).First();
            saved[device.Id] = character;
            usage[character]++;
            changed = true;
        }
        return changed;
    }

    public static string Resolve(AppSettings settings, DeviceInfo device) =>
        device?.Id != null && settings.DeviceCharacters != null &&
        settings.DeviceCharacters.TryGetValue(device.Id, out var character) && Valid(character)
            ? character : Valid(settings.ReferenceCharacter) ? settings.ReferenceCharacter : "rei";

    public static string CacheKey(AppSettings settings) => JsonSerializer.Serialize(
        (settings.DeviceCharacters ?? new()).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase));
}
