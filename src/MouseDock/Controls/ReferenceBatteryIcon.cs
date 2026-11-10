using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Controls;

public sealed class ReferenceBatteryIcon : FrameworkElement
{
    public string StyleName = "ring";
    public List<(DeviceKind Kind, int Level, bool Stale, bool Charging)> Devices = new();
    public Brush Accent = Brush("#A6F5BF");
    public Brush Ink = Brush("#EDF3EF");
    public Brush Surface = Brush("#1B1F21");
    public Brush Outline = Brush("#303936");
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
        Outline = Ink.Clone();
        Outline.Opacity = .16;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 1 || ActualHeight < 1) return;
        drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        if (Compact) DrawCompact(drawing);
        else
        {
            string style = TaskbarBatteryStrip.NormalizeStyle(StyleName);
            var items = _models != null
                ? _models.Take(6).Select(d => (d.Kind, d.HasLevel ? d.Level : -1, d.IsStale, d.IsChargingActive)).ToArray()
                : Devices.Take(6).ToArray();
            if (items.Length == 0) items = new[] { (DeviceKind.Mouse, -1, false, false) };
            if (style == "multi") DrawMulti(drawing, items);
            else if (items.Length > 1) DrawRows(drawing, style, items);
            else DrawSingle(drawing, style, items[0]);
        }
        drawing.Pop();
    }

    private void DrawSingle(DrawingContext dc, string style, (DeviceKind Kind, int Level, bool Stale, bool Charging) item)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 96;
        dc.PushTransform(new TranslateTransform((ActualWidth - 96 * scale) / 2, (ActualHeight - 96 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushOpacity(item.Stale ? .45 : 1);
        var background = HideBackground ? Brushes.Transparent : Surface;
        var track = new Pen(Outline, 1.2);
        switch (style)
        {
            case "ring":
                dc.DrawEllipse(background, null, new Point(48, 48), 45, 45);
                DrawRing(dc, new Point(48, 48), 37, item.Level, 3);
                Number(dc, item.Level, 25, new Point(48, 48), 68);
                break;
            case "tile":
                dc.DrawRoundedRectangle(background, HideBackground ? null : track, new Rect(4, 4, 88, 88), 22, 22);
                Number(dc, item.Level, 29, new Point(48, 44), 78);
                Bar(dc, new Rect(23, 73, 50, 2.4), item.Level);
                break;
            case "pill":
                dc.DrawRoundedRectangle(background, null, new Rect(3, 24, 90, 48), 24, 24);
                dc.DrawRoundedRectangle(Accent, null, new Rect(14, 37, 14, 22), 4, 4);
                Number(dc, item.Level, 24, new Point(58, 48), 55);
                break;
            case "battery":
                dc.DrawRoundedRectangle(background, track, new Rect(4, 25, 81, 48), 10, 10);
                dc.DrawRoundedRectangle(Accent, null, new Rect(89, 42, 4, 14), 2, 2);
                if (item.Level >= 0)
                    dc.DrawRoundedRectangle(Alpha(Accent, .25), null, new Rect(9, 30, 71 * Math.Clamp(item.Level, 0, 100) / 100d, 38), 5, 5);
                Number(dc, item.Level, 28, new Point(45, 49), 72);
                break;
            default:
                Number(dc, item.Level, 34, new Point(48, 44), 88);
                Bar(dc, new Rect(18, 76, 60, 2.5), item.Level);
                break;
        }
        // The source single-device styles signal charging through color, with a separate live bolt.
        if (item.Charging && !item.Stale) Bolt(dc, new Point(83, 14), 11);
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
            if (style == "pill")
                dc.DrawRoundedRectangle(HideBackground ? Brushes.Transparent : Surface, null,
                    new Rect(5, 7 + i * rowHeight, width - 10, Math.Max(1, rowHeight - 5)), rowHeight / 2, rowHeight / 2);
            if (i > 0 && style is "ring" or "tile" or "battery")
                dc.DrawLine(track, new Point(16, 6 + i * rowHeight), new Point(width - 16, 6 + i * rowHeight));
            dc.PushOpacity(item.Stale ? .45 : 1);
            double glyphSize = Math.Min(25, rowHeight * .42);
            if (style == "ring") DrawRing(dc, new Point(31, y), Math.Min(17, rowHeight * .3), item.Level, 2.8);
            DeviceGlyph.Draw(dc, item.Kind, new Rect(31 - glyphSize / 2, y - glyphSize / 2, glyphSize, glyphSize),
                item.Level < 0 ? Alpha(Ink, .8) : Accent, 1.8);
            Number(dc, item.Level, Math.Min(32, rowHeight * .6), new Point((width + 46) / 2, y), width - 62);
            if (style is "battery" or "plain") Bar(dc, new Rect(59, y + rowHeight * .35, width - 76, 2), item.Level);
            if (item.Charging && !item.Stale) Bolt(dc, new Point(width - 13, y - rowHeight * .29), 9);
            dc.Pop();
        }
    }

    private void DrawMulti(DrawingContext dc, (DeviceKind Kind, int Level, bool Stale, bool Charging)[] items)
    {
        dc.DrawRoundedRectangle(HideBackground ? Brushes.Transparent : Surface, null,
            new Rect(3, 3, ActualWidth - 6, ActualHeight - 6), 16, 16);
        double cell = (ActualWidth - 16) / items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            double center = 8 + cell * (i + .5);
            if (i > 0) dc.DrawLine(new Pen(Outline, 1),
                new Point(8 + i * cell, 12), new Point(8 + i * cell, ActualHeight - 12));
            dc.PushOpacity(item.Stale ? .45 : 1);
            DeviceGlyph.Draw(dc, item.Kind, new Rect(center - 14, 12, 22, 22), Alpha(Ink, .85), 1.8);
            dc.DrawEllipse(item.Level < 0 ? Brush("#DFD783") : Brush("#7BC9E2"), null, new Point(center + 14, 22), 2.5, 2.5);
            Text(dc, item.Level < 0 ? "\u2014" : item.Level.ToString(CultureInfo.InvariantCulture),
                Math.Min(31, cell * .58), new Point(center, ActualHeight * .68), Ink, cell - 8);
            if (item.Charging && !item.Stale) Bolt(dc, new Point(center + 14, 35), 9);
            dc.Pop();
        }
    }

    private void Number(DrawingContext dc, int level, double size, Point center, double maxWidth)
    {
        string value = level < 0 ? "\u2014" : level.ToString(CultureInfo.InvariantCulture);
        var digits = Format(value, size, Ink);
        var suffix = Format(level < 0 ? "" : "%", size * .45, Ink);
        double width = digits.Width + suffix.Width + 2;
        if (width > maxWidth)
        {
            size *= maxWidth / width;
            digits = Format(value, size, Ink);
            suffix = Format(level < 0 ? "" : "%", size * .45, Ink);
            width = digits.Width + suffix.Width + 2;
        }
        double left = center.X - width / 2;
        dc.DrawText(digits, new Point(left, center.Y - digits.Height / 2));
        dc.DrawText(suffix, new Point(left + digits.Width + 2, center.Y + digits.Height * .36 - suffix.Height));
    }

    private void DrawRing(DrawingContext dc, Point center, double radius, int level, double thickness)
    {
        dc.DrawEllipse(null, new Pen(Outline, thickness), center, radius, radius);
        if (level <= 0) return;
        if (level >= 100) { dc.DrawEllipse(null, new Pen(Accent, thickness), center, radius, radius); return; }
        double angle = level * 3.6 * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            context.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                new Size(radius, radius), 0, level > 50, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(Accent, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }

    private void Bar(DrawingContext dc, Rect bounds, int level)
    {
        dc.DrawRoundedRectangle(Outline, null, bounds, bounds.Height / 2, bounds.Height / 2);
        if (level <= 0) return;
        bounds.Width *= Math.Clamp(level, 0, 100) / 100d;
        dc.DrawRoundedRectangle(Accent, null, bounds, bounds.Height / 2, bounds.Height / 2);
    }
    private FormattedText Format(string text, double size, Brush brush) => new(text, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(1, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private void Text(DrawingContext dc, string text, double size, Point center, Brush brush, double maxWidth)
    {
        var value = Format(text, size, brush);
        if (value.Width > maxWidth) value = Format(text, size * maxWidth / value.Width, brush);
        dc.DrawText(value, new Point(center.X - value.Width / 2, center.Y - value.Height / 2));
    }
    private void Bolt(DrawingContext dc, Point center, double size) =>
        Text(dc, "\u26A1", size, center, Accent, size * 1.5);
    private static Brush Brush(string color) => new SolidColorBrush(ThemePreset.C(color));
    private static Brush Alpha(Brush source, double opacity) { var brush = source.Clone(); brush.Opacity *= opacity; return brush; }

    private void DrawCompact(DrawingContext drawing)
    {
        var outline = Alpha(Ink, .3);
        var background = HideBackground ? Brushes.Transparent : Alpha(Surface, .85);
        Brush accent = UseStatusColors ? Brush(CompactCharging ? "#38BDF8"
            : CompactLevel is >= 0 && CompactLevel <= LowBatteryThreshold ? "#F85D68" : "#34D399") : Accent;
        var track = new Pen(outline, 1);
        if (CompactLevel < 0) track.DashStyle = DashStyles.Dash;
        double width = ActualWidth, height = ActualHeight;
        double fraction = Math.Clamp(CompactLevel, 0, 100) / 100d;
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
                var charged = Vertical
                    ? new Rect(inside.X, inside.Bottom - inside.Height * fraction, inside.Width, inside.Height * fraction)
                    : new Rect(inside.X, inside.Y, inside.Width * fraction, inside.Height);
                drawing.DrawRoundedRectangle(Alpha(accent, .25), null, charged, 2, 2);
            }
        }
        if (StyleName is "tile" or "plain")
        {
            var from = new Point(7, height - 4);
            var to = new Point(width - 7, height - 4);
            drawing.DrawLine(new Pen(outline, 2), from, to);
            if (CompactLevel > 0)
                drawing.DrawLine(new Pen(accent, 2), from, new Point(from.X + (to.X - from.X) * fraction, to.Y));
        }
    }
}
