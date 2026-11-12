namespace MouseDock.Models;

public sealed record DevicePhoto
{
    public string Path { get; init; } = "";
    public double Zoom { get; init; } = 1;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}
