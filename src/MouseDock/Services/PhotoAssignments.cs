using System;
using System.Linq;
using System.Text.Json;
using MouseDock.Models;

namespace MouseDock.Services;

public static class PhotoAssignments
{
    public static DevicePhoto Resolve(AppSettings settings, DeviceInfo device)
    {
        var photo = device?.Id == null ? null : settings.DevicePhotos?
            .FirstOrDefault(p => string.Equals(p.Key, device.Id, StringComparison.OrdinalIgnoreCase)).Value;
        photo ??= new DevicePhoto
        {
            Path = string.IsNullOrWhiteSpace(settings.IconBackground) ? settings.PhotoAsset : settings.IconBackground,
            Zoom = settings.PhotoZoom, OffsetX = settings.PhotoOffsetX, OffsetY = settings.PhotoOffsetY
        };
        return Normalize(photo);
    }

    public static void Set(AppSettings settings, DeviceInfo device, DevicePhoto photo)
    {
        if (!string.IsNullOrWhiteSpace(device?.Id))
        {
            settings.DevicePhotos = new(settings.DevicePhotos ?? new(), StringComparer.OrdinalIgnoreCase);
            settings.DevicePhotos[device.Id] = photo;
        }
        else
        {
            settings.PhotoAsset = photo.Path;
            settings.IconBackground = "";
            settings.PhotoZoom = photo.Zoom;
            settings.PhotoOffsetX = photo.OffsetX;
            settings.PhotoOffsetY = photo.OffsetY;
        }
    }

    public static string CacheKey(AppSettings settings) => JsonSerializer.Serialize(
        (settings.DevicePhotos ?? new()).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new { p.Key, Value = Normalize(p.Value) }));

    private static DevicePhoto Normalize(DevicePhoto photo)
    {
        photo ??= new();
        return photo with
        {
            Path = photo.Path ?? "",
            Zoom = PictureBackground.Clamp(photo.Zoom, 1, 4, 1),
            OffsetX = PictureBackground.Clamp(photo.OffsetX, -.5, .5, 0),
            OffsetY = PictureBackground.Clamp(photo.OffsetY, -.5, .5, 0)
        };
    }
}
