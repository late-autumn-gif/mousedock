using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Controls;

public sealed class ReferenceBatteryIcon : FrameworkElement
{
    public string StyleName = "ring";
    public List<(DeviceKind Kind, int Level, bool Stale, bool Charging)> Devices = new();
    public Brush Accent = Brush("#A6F5BF");
    public Brush Ink = Brush("#EDF3EF");
    public Brush Surface = Brush("#1B1F21");
    public Brush Outline = Brush("#303936");
    public Brush Low = Brush("#FF687D");
    public Brush Muted = Brush("#BCC6C1");
    private bool TransparentFloating => !Compact && (HideBackground || TaskbarBatteryStrip.NormalizeStyle(StyleName) == "plain");
    private static readonly Color HaloColor = Color.FromRgb(21, 26, 32);
    private static readonly Brush HaloInk = new SolidColorBrush(HaloColor);
    private static readonly Brush FloatingInk = Brush("#F7FAFC");
    private readonly Dictionary<Color, Brush> _floatingColors = new();
    private Brush FloatingContrast(Brush brush)
    {
        if (!TransparentFloating || brush is not SolidColorBrush solid || ColorContrast.Ratio(solid.Color, HaloColor) >= 4.5) return brush;
        if (ReferenceEquals(brush, Ink) || ReferenceEquals(brush, Muted)) return FloatingInk;
        if (_floatingColors.TryGetValue(solid.Color, out var cached)) return cached;
        if (_floatingColors.Count >= 32) _floatingColors.Clear();
        var corrected = new SolidColorBrush(Palette.Mix(solid.Color, Colors.White, .55)); corrected.Freeze();
        _floatingColors[solid.Color] = corrected;
        return corrected;
    }
    private static double StaleOpacity(bool stale) => stale ? .45 : 1;
    private void LegibleText(DrawingContext dc, FormattedText value, Point point, bool outline = false)
    {
        if (outline || TransparentFloating)
            dc.DrawGeometry(HaloInk, new Pen(HaloInk, 2.2) { LineJoin = PenLineJoin.Round }, value.BuildGeometry(point));
        dc.DrawText(value, point);
    }
    public bool HideBackground;
    public bool Compact, Vertical, UseStatusColors;
    public int LowBatteryThreshold = 20;
    private IReadOnlyList<DeviceInfo> _models;
    public IReadOnlyList<DeviceInfo> DeviceModels
    {
        get => _models;
        set
        {
            if (_models != null) foreach (var model in _models) PropertyChangedEventManager.RemoveHandler(model, OnDeviceChanged, "");
            if (_sweep != null)
                for (int i = 0; i < 6; i++)
                    if ((_models != null && i < _models.Count ? _models[i].Id : null) !=
                        (value != null && i < value.Count ? value[i].Id : null))
                        _sweep.Reset(i);
            _models = value;
            if (_models != null) foreach (var model in _models) PropertyChangedEventManager.AddHandler(model, OnDeviceChanged, "");
            InvalidateVisual();
        }
    }
    private void OnDeviceChanged(object sender, PropertyChangedEventArgs args) => InvalidateVisual();
    public static readonly DependencyProperty CompactLevelProperty = DependencyProperty.Register(
        nameof(CompactLevel), typeof(int), typeof(ReferenceBatteryIcon),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CompactChargingProperty = DependencyProperty.Register(
        nameof(CompactCharging), typeof(bool), typeof(ReferenceBatteryIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public int CompactLevel { get => (int)GetValue(CompactLevelProperty); set => SetValue(CompactLevelProperty, value); }
    public bool CompactCharging { get => (bool)GetValue(CompactChargingProperty); set => SetValue(CompactChargingProperty, value); }

    public void ApplyPalette(ThemePreset theme, bool follow)
    {
        bool light = theme.IsLight;
        Ink = new SolidColorBrush(follow ? theme.Text : ThemePreset.C(light ? "#202B29" : "#EDF3EF"));
        Accent = new SolidColorBrush(follow ? theme.Accent : ThemePreset.C(light ? "#53B89E" : "#A6F5BF"));
        Surface = new SolidColorBrush(follow ? theme.Surface : ThemePreset.C(light ? "#FFFFFF" : "#1B1F21"));
        Low = new SolidColorBrush(theme.Low.A == 0 ? ThemePreset.C("#FF687D") : theme.Low);
        Muted = new SolidColorBrush(theme.TextDim.A == 0 ? ThemePreset.C("#BCC6C1") : theme.TextDim);
        Outline = Ink.Clone();
        Outline.Opacity = .16;
    }

    public void ApplyPalette(ThemePreset theme, AppSettings settings)
    {
        LowBatteryThreshold = settings.LowBatteryWarn;
        var palette = AppearanceColors.IconPalette(settings, theme, settings.IconFollowTheme);
        ApplyPalette(palette, true);
    }

    public bool AnimateCompactCharge { get; set; }
    private ChargeSweep _sweep;
    private double _phase;

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 1 || ActualHeight < 1) return;
        drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        if (Compact)
        {
            _sweep ??= new ChargeSweep(this);
            _phase = _sweep.Phase(AnimateCompactCharge && CompactCharging && CompactLevel > 0 &&
                StyleName is "battery" or "plain" or "tile");
            DrawCompact(drawing);
        }
        else
        {
            string style = TaskbarBatteryStrip.NormalizeStyle(StyleName);
            (DeviceKind Kind, int Level, bool Stale, bool Charging)[] items = _models != null
                ? _models.Take(6).Select(d => (d.Kind, d.HasLevel ? d.Level : -1, d.IsStale, d.IsChargingActive)).ToArray()
                : Devices.Take(6).ToArray();
            if (items.Length == 0) items = new[] { (DeviceKind.Mouse, -1, false, false) };
            _sweep ??= new ChargeSweep(this);
            _phase = _sweep.Phase(items.Any(item => item.Charging && !item.Stale && item.Level >= 0));
            if (style == "multi") DrawMulti(drawing, items);
            else if (items.Length > 1) DrawRows(drawing, style, items);
            else DrawSingle(drawing, style, items[0]);
        }
        drawing.Pop();
    }

    private bool IsFull(int slot) => _models != null && slot < _models.Count && _models[slot].IsFullActive;

    private string Digits(int level, int slot) => level < 0 ? "—" :
        _models != null && slot < _models.Count && _models[slot].IsBatteryEstimated
            ? _models[slot].CompactLevelText : level.ToString(CultureInfo.InvariantCulture);

    private double DrawnLevel(int slot, int target, bool charging, bool ring = false) =>
        _sweep.Follow(slot, target, charging && _phase >= 0, ring ? 1050 : 300);

    private void DrawSingle(DrawingContext dc, string style, (DeviceKind Kind, int Level, bool Stale, bool Charging) item, int slot = 0)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 96;
        dc.PushTransform(new TranslateTransform((ActualWidth - 96 * scale) / 2, (ActualHeight - 96 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushOpacity(StaleOpacity(item.Stale));
        var background = HideBackground ? Brushes.Transparent : Surface;
        var track = new Pen(Outline, 1.2);
        double drawn = DrawnLevel(slot, item.Level, item.Charging && !item.Stale, style == "ring");
        switch (style)
        {
            case "ring":
                dc.DrawEllipse(background, null, new Point(48, 48), 45, 45);
                DrawRing(dc, new Point(48, 48), 37, IsFull(slot) ? 100 : drawn, item.Charging && !item.Stale && !IsFull(slot));
                Number(dc, IsFull(0) ? 100 : item.Level, 25, new Point(48, 48), 68, charging: item.Charging && !item.Stale, full: IsFull(slot), slot: slot);
                break;
            case "tile":
                dc.DrawRoundedRectangle(background, HideBackground ? null : track, new Rect(4, 4, 88, 88), 22, 22);
                Number(dc, item.Level, 29, new Point(48, 44), 78, charging: item.Charging && !item.Stale, full: IsFull(slot), slot: slot);
                Bar(dc, new Rect(23, 73, 50, 2.4), drawn, item.Charging && !item.Stale ? _phase : -1,
                    StatusBrush(item.Level, charging: item.Charging && !item.Stale, full: IsFull(slot)));
                break;
            case "pill":
                dc.DrawRoundedRectangle(background, null, new Rect(3, 24, 90, 48), 24, 24);
                dc.DrawRoundedRectangle(StatusBrush(item.Level), null, new Rect(14, 37, 14, 22), 4, 4);
                Number(dc, item.Level, 24, new Point(58, 48), 55,
                    breath: item.Charging && !item.Stale && item.Level >= 0 ? _phase : -1,
                    charging: item.Charging && !item.Stale, full: IsFull(slot), slot: slot);
                break;
            case "battery":
                bool lowBattery = AppearanceColors.IsLowBattery(item.Level, LowBatteryThreshold);
                var fill = BatteryStyleFill(item.Level, item.Charging && !item.Stale, IsFull(slot));
                dc.DrawRoundedRectangle(background, track, new Rect(4, 25, 81, 48), 10, 10);
                dc.DrawRoundedRectangle(fill, null, new Rect(89, 42, 4, 14), 2, 2);
                ChargeSweep.Fill(dc, new Rect(9, 30, 71, 38), drawn / 100, 5,
                    lowBattery ? fill : Alpha(fill, .7), item.Charging && !item.Stale ? _phase : -1);
                Number(dc, item.Level, 28, new Point(45, 49), 72, batteryStyle: true, slot: slot);
                break;
            default:
                Number(dc, item.Level, 34, new Point(48, 44), 88, charging: item.Charging && !item.Stale, full: IsFull(slot), slot: slot);
                Bar(dc, new Rect(18, 76, 60, 2.5), drawn, item.Charging && !item.Stale ? _phase : -1,
                    StatusBrush(item.Level, charging: item.Charging && !item.Stale, full: IsFull(slot)));
                break;
        }
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private void DrawRows(DrawingContext dc, string style,
        (DeviceKind Kind, int Level, bool Stale, bool Charging)[] items)
    {
        double width = ActualWidth, height = ActualHeight;
        double rowHeight = (height - 12) / items.Length;
        var body = new Rect(4, 4, width - 8, height - 8);
        var track = new Pen(Outline, 1.2);
        if (style is "ring" or "tile" or "battery")
            dc.DrawRoundedRectangle(HideBackground ? Brushes.Transparent : Surface, HideBackground ? null : track, body, 24, 24);
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            double y = 6 + rowHeight * (i + .5);
            double drawn = DrawnLevel(i, IsFull(i) ? 100 : item.Level, item.Charging && !item.Stale, style == "ring");
            if (style == "pill")
                dc.DrawRoundedRectangle(HideBackground ? Brushes.Transparent : Surface, null,
                    new Rect(5, 7 + i * rowHeight, width - 10, Math.Max(1, rowHeight - 5)), rowHeight / 2, rowHeight / 2);
            if (i > 0 && style is "ring" or "tile" or "battery")
                dc.DrawLine(track, new Point(16, 6 + i * rowHeight), new Point(width - 16, 6 + i * rowHeight));
            dc.PushOpacity(StaleOpacity(item.Stale));
            double glyphSize = Math.Min(25, rowHeight * .42);
            if (style == "ring")
            {
                double radius = Math.Min(17, rowHeight * .3);
                double glyphRatio = item.Kind == DeviceKind.Headset
                    ? BatteryRingDrawing.HeadsetGlyphRadiusRatio : BatteryRingDrawing.GlyphRadiusRatio;
                glyphSize = Math.Min(glyphSize, radius * glyphRatio);
                DrawRing(dc, new Point(31, y), radius, drawn, item.Charging && !item.Stale);
            }
            {
                var glyphBounds = new Rect(31 - glyphSize / 2, y - glyphSize / 2, glyphSize, glyphSize);
                bool batteryStyle = style == "battery";
                if (TransparentFloating || (batteryStyle && AppearanceColors.IsLowBattery(item.Level, LowBatteryThreshold)))
                    DeviceGlyph.Draw(dc, item.Kind, glyphBounds, HaloInk, 4);
                DeviceGlyph.Draw(dc, item.Kind, glyphBounds,
                    batteryStyle ? BatteryStyleInk(item.Level) : StatusBrush(item.Level), 1.8);
            }
            Number(dc, item.Level, Math.Min(32, rowHeight * .6), new Point((width + 46) / 2, y), width - 62, batteryStyle: style == "battery",
                breath: style is not ("ring" or "battery" or "plain") && item.Charging && !item.Stale && item.Level >= 0 ? _phase : -1,
                charging: item.Charging && !item.Stale, full: IsFull(i), slot: i);
            if (style is "battery" or "plain")
            {
                bool lowBattery = style == "battery" && AppearanceColors.IsLowBattery(item.Level, LowBatteryThreshold);
                Bar(dc, new Rect(59, y + rowHeight * .35, Math.Max(0, width - 76), 2), drawn,
                    item.Charging && !item.Stale ? _phase : -1,
                    style == "battery" ? BatteryStyleFill(item.Level, item.Charging && !item.Stale, IsFull(i)) : StatusBrush(item.Level, charging: item.Charging && !item.Stale, full: IsFull(i)));
            }
            dc.Pop();
        }
    }

    private void DrawMulti(DrawingContext dc, (DeviceKind Kind, int Level, bool Stale, bool Charging)[] items)
    {
        dc.DrawRoundedRectangle(HideBackground ? Brushes.Transparent : Surface, null,
            new Rect(3, 3, ActualWidth - 6, ActualHeight - 6), 16, 16);
        var cells = IconComposition.CompactCells(items.Length, RenderSize);
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var cell = cells[i];
            double center = cell.X + cell.Width / 2;
            dc.PushOpacity(StaleOpacity(item.Stale));
            var identity = _models != null && i < _models.Count
                ? new SolidColorBrush(IconComposition.IdentityColor(_models[i].Id)) : Alpha(Ink, .85);
            var glyphBounds = new Rect(center - 11, cell.Y + 3, 22, 22);
            if (TransparentFloating) DeviceGlyph.Draw(dc, item.Kind, glyphBounds, HaloInk, 4);
            DeviceGlyph.Draw(dc, item.Kind, glyphBounds, FloatingContrast(identity), 1.8);
            dc.PushOpacity(ChargeBreath.OpacityAt(item.Charging && !item.Stale && item.Level >= 0 ? _phase : -1));
            Text(dc, Digits(item.Level, i),
                Math.Min(31, cell.Width * .58), new Point(center, cell.Y + cell.Height * .73),
                StatusBrush(item.Level, true, item.Charging && !item.Stale, IsFull(i)), cell.Width - 4);
            dc.Pop();
            dc.Pop();
        }
    }

    private void Number(DrawingContext dc, int level, double size, Point center, double maxWidth, bool batteryStyle = false, double breath = -1, bool charging = false, bool full = false, int slot = 0)
    {
        string value = Digits(level, slot);
        bool outline = batteryStyle && AppearanceColors.IsLowBattery(level, LowBatteryThreshold);
        var ink = batteryStyle ? BatteryStyleInk(level) : StatusBrush(level, true, charging, full);
        var digits = Format(value, size, ink);
        var suffix = Format(level < 0 ? "" : "%", size * .45, ink);
        double width = digits.Width + suffix.Width + 2;
        if (width > maxWidth)
        {
            size *= maxWidth / width;
            digits = Format(value, size, ink);
            suffix = Format(level < 0 ? "" : "%", size * .45, ink);
            width = digits.Width + suffix.Width + 2;
        }
        double left = center.X - width / 2;
        dc.PushOpacity(ChargeBreath.OpacityAt(breath));
        LegibleText(dc, digits, new Point(left, center.Y - digits.Height / 2), outline);
        LegibleText(dc, suffix, new Point(left + digits.Width + 2, center.Y + digits.Height * .36 - suffix.Height), outline);
        dc.Pop();
    }

    private void DrawRing(DrawingContext dc, Point center, double radius, double level, bool charging)
    {
        var fill = StatusBrush((int)Math.Round(level), charging: charging);
        BatteryRingDrawing.Draw(dc, center, radius, level, Outline, fill);
        ChargeSweep.Absorb(dc, center, radius, charging && level >= 0 ? _phase : -1, fill);
    }

    private void Bar(DrawingContext dc, Rect bounds, double level, double phase, Brush fill)
    {
        dc.DrawRoundedRectangle(Outline, null, bounds, bounds.Height / 2, bounds.Height / 2);
        if (level <= 0) return;
        ChargeSweep.HorizontalLine(dc, bounds, level / 100, bounds.Height / 2, fill, phase);
    }
    private Brush BatteryStyleFill(int level, bool charging, bool full) => level < 0 ? FloatingContrast(Muted)
        : FloatingContrast(AppearanceColors.BatteryState(Accent, level, LowBatteryThreshold, charging, full));

    private Brush BatteryStyleInk(int level) => level < 0 ? FloatingContrast(Muted)
        : AppearanceColors.IsLowBattery(level, LowBatteryThreshold) ? Brushes.White : FloatingContrast(Ink);

    private Brush StatusBrush(int level, bool text = false, bool charging = false, bool full = false) =>
        FloatingContrast(level < 0 ? Muted : AppearanceColors.BatteryState(text ? Ink : Accent,
            level, LowBatteryThreshold, charging, full, text));
    private FormattedText Format(string text, double size, Brush brush) => new(text, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(1, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private void Text(DrawingContext dc, string text, double size, Point center, Brush brush, double maxWidth)
    {
        var value = Format(text, size, brush);
        if (value.Width > maxWidth) value = Format(text, size * maxWidth / value.Width, brush);
        LegibleText(dc, value, new Point(center.X - value.Width / 2, center.Y - value.Height / 2));
    }
    private static Brush Brush(string color) => new SolidColorBrush(ThemePreset.C(color));
    private static Brush Alpha(Brush source, double opacity) { var brush = source.Clone(); brush.Opacity *= opacity; return brush; }

    private void DrawCompact(DrawingContext drawing)
    {
        var outline = Alpha(Ink, .3);
        var background = HideBackground ? Brushes.Transparent : Alpha(Surface, .85);
        bool low = AppearanceColors.IsLowBattery(CompactLevel, LowBatteryThreshold);
        Brush accent = AppearanceColors.BatteryState(UseStatusColors ? Brush("#34D399") : Accent,
            CompactLevel, LowBatteryThreshold, CompactCharging, DataContext is DeviceInfo { IsFullActive: true });
        var track = new Pen(outline, 1);
        if (CompactLevel < 0) track.DashStyle = DashStyles.Dash;
        double width = ActualWidth, height = ActualHeight;
        double fraction = Math.Clamp(DrawnLevel(0, CompactLevel, CompactCharging), 0, 100) / 100d;
        var body = new Rect(2, 2, Math.Max(0, width - 4), Math.Max(0, height - 4));
        if (StyleName is "tile" or "pill")
        {
            double radius = StyleName == "pill" ? Math.Min(width, height) / 2 : 7;
            drawing.DrawRoundedRectangle(background, HideBackground ? null : track, body, radius, radius);
        }
        if (StyleName == "battery")
        {
            body = Vertical ? new Rect(4, 7, width - 8, height - 10) : new Rect(3, 4, width - 10, height - 8);
            drawing.DrawRoundedRectangle(background, track, body, 4, 4);
            var cap = Vertical ? new Rect(width / 2 - 5, 2, 10, 3) : new Rect(width - 5, height / 2 - 5, 3, 10);
            drawing.DrawRoundedRectangle(accent, null, cap, 1, 1);
            if (CompactLevel > 0)
            {
                var inside = body;
                inside.Inflate(-3, -3);
                ChargeSweep.Fill(drawing, inside, fraction, 2, low ? accent : Alpha(accent, .65), _phase, Vertical);
            }
        }
        if (StyleName is "tile" or "plain")
        {
            var from = new Point(7, height - 4);
            var to = new Point(width - 7, height - 4);
            drawing.DrawLine(new Pen(outline, 2), from, to);
            if (CompactLevel > 0)
            {
                ChargeSweep.HorizontalLine(drawing, new Rect(from.X, from.Y - 1, Math.Max(1, to.X - from.X), 2),
                    fraction, 1, accent, _phase);
            }
        }
    }
}
