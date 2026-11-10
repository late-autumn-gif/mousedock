using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Controls;

public sealed class CharacterBatteryIcon : FrameworkElement
{
    private static readonly Dictionary<string, BitmapSource> Artwork = new();
    private static readonly Dictionary<string, Rect> VisibleArtwork = new();
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(int),
        typeof(CharacterBatteryIcon), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(nameof(Charging), typeof(bool),
        typeof(CharacterBatteryIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StaleProperty = DependencyProperty.Register(nameof(Stale), typeof(bool),
        typeof(CharacterBatteryIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(DeviceKind),
        typeof(CharacterBatteryIcon), new FrameworkPropertyMetadata(DeviceKind.Mouse, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public bool Stale { get => (bool)GetValue(StaleProperty); set => SetValue(StaleProperty, value); }
    public DeviceKind Kind { get => (DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Character { get; set; } = "rei";
    public string PhotoPath { get; set; } = "";
    public double PhotoZoom { get; set; } = 1;
    public double PhotoOffsetX { get; set; }
    public double PhotoOffsetY { get; set; }
    public bool ShowNumber { get; set; } = true;
    public bool ShowGlyph { get; set; } = true;
    public Brush Accent { get; set; } = new SolidColorBrush(ThemePreset.C("#A6F5BF"));
    public Brush Ink { get; set; } = Brushes.WhiteSmoke;
    public Brush Surface { get; set; } = new SolidColorBrush(ThemePreset.C("#273348"));
    public Brush Low { get; set; } = new SolidColorBrush(ThemePreset.C("#FF687D"));
    public Brush Muted { get; set; } = new SolidColorBrush(ThemePreset.C("#BCC6C1"));
    private IReadOnlyList<DeviceInfo> _devices;
    public IReadOnlyList<DeviceInfo> Devices
    {
        get => _devices;
        set
        {
            if (_devices != null) foreach (var device in _devices) PropertyChangedEventManager.RemoveHandler(device, OnDeviceChanged, "");
            _devices = value;
            if (_devices != null) foreach (var device in _devices) PropertyChangedEventManager.AddHandler(device, OnDeviceChanged, "");
            InvalidateVisual();
        }
    }
    private void OnDeviceChanged(object sender, PropertyChangedEventArgs args) => InvalidateVisual();
    private string _loadedPhoto;
    private BitmapSource _photo;
    public Rect VisibleArtworkBounds
    {
        get
        {
            if (Character == "photo") return new Rect(0, 0, ActualWidth, ActualHeight);
            var image = LoadArtwork(Character);
            if (!VisibleArtwork.TryGetValue(Character, out var pixels))
            {
                var rgba = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                var data = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
                rgba.CopyPixels(data, rgba.PixelWidth * 4, 0);
                int left = rgba.PixelWidth, top = rgba.PixelHeight, right = 0, bottom = 0;
                for (int y = 0; y < rgba.PixelHeight; y++)
                for (int x = 0; x < rgba.PixelWidth; x++)
                    if (data[(y * rgba.PixelWidth + x) * 4 + 3] > 8)
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
                pixels = right > left && bottom > top ? new Rect(left, top, right - left, bottom - top)
                    : new Rect(0, 0, image.PixelWidth, image.PixelHeight);
                VisibleArtwork[Character] = pixels;
            }
            double scale = Math.Min(ActualWidth / image.PixelWidth, ActualHeight / image.PixelHeight);
            return new Rect((ActualWidth - image.PixelWidth * scale) / 2 + pixels.X * scale,
                (ActualHeight - image.PixelHeight * scale) / 2 + pixels.Y * scale, pixels.Width * scale, pixels.Height * scale);
        }
    }

    public static BitmapSource LoadArtwork(string kind)
    {
        if (kind is not ("rei" or "asuka" or "peek")) kind = "rei";
        if (Artwork.TryGetValue(kind, out var image)) return image;
        var resources = new ResourceManager("MouseDock.g", typeof(CharacterBatteryIcon).Assembly);
        using var stream = resources.GetStream($"assets/characters/character-{kind}.png", CultureInfo.InvariantCulture);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image = decoder.Frames[0];
        if (kind == "peek") image = new CroppedBitmap(image, new Int32Rect(266, 84, 1042, 836));
        image.Freeze();
        Artwork[kind] = image;
        return image;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 1 || ActualHeight < 1) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        if (Character == "photo") DrawPhoto(dc);
        else DrawCharacter(dc);
        dc.Pop();
    }

    private void DrawCharacter(DrawingContext dc)
    {
        var image = LoadArtwork(Character);
        double scale = Math.Min(ActualWidth / image.PixelWidth, ActualHeight / image.PixelHeight);
        var bounds = new Rect((ActualWidth - image.PixelWidth * scale) / 2,
            (ActualHeight - image.PixelHeight * scale) / 2, image.PixelWidth * scale, image.PixelHeight * scale);
        dc.PushOpacity(Stale ? .45 : 1);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        dc.DrawImage(image, bounds);
        Rect original = Character switch
        {
            "asuka" => new Rect(380, 834, 601, 131),
            "peek" => new Rect(47, 620, 905, 178),
            _ => new Rect(369, 855, 605, 136)
        };
        var plate = new Rect(bounds.X + original.X * scale, bounds.Y + original.Y * scale,
            original.Width * scale, original.Height * scale);
        if (Level > 0)
        {
            var fill = plate;
            fill.Width *= Math.Clamp(Level, 0, 100) / 100d;
            dc.DrawRoundedRectangle(StatusBrush(Level), null, fill, 2, 2);
        }
        if (ShowNumber)
            Text(dc, Level < 0 ? "\u2014" : Level + "%", plate, Math.Min(plate.Height * .8, 24), StatusBrush(Level, true), true);
        if (Charging && !Stale)
            Text(dc, "\u26A1", new Rect(plate.Right + 2, plate.Top, Math.Max(8, plate.Height), plate.Height),
                Math.Max(8, plate.Height * .7), Accent);
        if (ShowGlyph)
        {
            double side = Math.Min(22, ActualWidth * .19);
            var badge = new Rect(ActualWidth * .03, ActualHeight * .79, side, side);
            dc.DrawRoundedRectangle(Surface, null, badge, 5, 5);
            badge.Inflate(-3, -3);
            DeviceGlyph.Draw(dc, Kind, badge, Brushes.WhiteSmoke, 1.8);
        }
        dc.Pop();
    }

    private void DrawPhoto(DrawingContext dc)
    {
        if (_loadedPhoto != PhotoPath)
        {
            _loadedPhoto = PhotoPath;
            _photo = Palette.Load(PhotoPath, 1024);
        }
        var bounds = new Rect(3, 2, Math.Max(1, ActualWidth - 6), Math.Max(1, ActualHeight - 4));
        dc.PushClip(new RectangleGeometry(bounds, 8, 8));
        dc.DrawRectangle(Surface, null, bounds);
        var image = _photo ?? LoadArtwork("rei");
        double zoom = double.IsFinite(PhotoZoom) ? Math.Clamp(PhotoZoom, 1, 4) : 1;
        double scale = Math.Max(bounds.Width / image.PixelWidth, bounds.Height / image.PixelHeight) * zoom;
        double width = bounds.Width / (image.PixelWidth * scale), height = bounds.Height / (image.PixelHeight * scale);
        double x = double.IsFinite(PhotoOffsetX) ? Math.Clamp(.5 - PhotoOffsetX, 0, 1) : .5;
        double y = double.IsFinite(PhotoOffsetY) ? Math.Clamp(.5 - PhotoOffsetY, 0, 1) : .5;
        dc.DrawRectangle(new ImageBrush(image)
        {
            Viewbox = new Rect((1 - width) * x, (1 - height) * y, width, height),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill
        }, null, bounds);
        if (ShowNumber)
        {
            int count = Math.Max(1, Devices?.Count ?? 1);
            double rowHeight = Math.Min(29, bounds.Height / (count + 1));
            double top = bounds.Bottom - count * rowHeight - 6;
            dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Color.FromArgb(220, 0, 0, 0),
                new Point(0, 0), new Point(0, 1)), null,
                new Rect(bounds.X, Math.Max(bounds.Y, top - rowHeight), bounds.Width, bounds.Bottom - Math.Max(bounds.Y, top - rowHeight)));
            for (int i = 0; i < count; i++)
            {
                var device = Devices != null && i < Devices.Count ? Devices[i] : null;
                int level = device?.Level ?? Level;
                bool stale = device?.IsStale ?? Stale;
                var row = new Rect(bounds.X + 7, top + i * rowHeight, bounds.Width - 14, rowHeight - 3);
                dc.PushOpacity(stale ? .45 : 1);
                DeviceGlyph.Draw(dc, device?.Kind ?? Kind,
                    new Rect(row.X, row.Y, row.Height * .65, row.Height * .65), StatusBrush(level, true), 1.6);
                var number = new Rect(row.X + row.Height, row.Y - 2, row.Width - row.Height, row.Height - 2);
                string value = level < 0 ? "\u2014" : level + "%";
                if ((device?.IsChargingActive ?? Charging) && !stale) value += " \u26A1";
                Text(dc, value, number, row.Height * .7, StatusBrush(level, true), true);
                var bar = new Rect(row.X, row.Bottom - 2, row.Width, 1.6);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), null, bar);
                if (level > 0)
                {
                    bar.Width *= Math.Clamp(level, 0, 100) / 100d;
                    dc.DrawRectangle(StatusBrush(level), null, bar);
                }
                dc.Pop();
            }
        }
        dc.Pop();
    }

    private Brush StatusBrush(int level, bool text = false) => AppearanceColors.Battery(level, text ? Ink : Accent, Low, Muted);

    private void Text(DrawingContext dc, string text, Rect bounds, double size, Brush ink, bool shadow = false)
    {
        FormattedText Format(double points, Brush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), Math.Max(1, points), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var value = Format(size, ink);
        if (value.Width > bounds.Width) { size *= bounds.Width / value.Width; value = Format(size, ink); }
        var at = new Point(bounds.X + (bounds.Width - value.Width) / 2, bounds.Y + (bounds.Height - value.Height) / 2);
        if (shadow) dc.DrawText(Format(size, Brushes.Black), new Point(at.X + .65, at.Y + .65));
        dc.DrawText(value, at);
    }
}
