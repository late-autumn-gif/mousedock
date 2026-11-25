using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MouseDock.Services;
using Forms = System.Windows.Forms;

namespace MouseDock.Views;

public partial class DockWindow
{
    private const double MinimumCardWidth = 265;
    private const double MinimumCardHeight = 170;
    private double _defaultCardWidth = 316;
    private int _cardResizeEdges;
    private Rect _cardResizeStart, _cardResizeWork;
    private Point _cardResizePointer;
    private DpiScale _cardResizeDpi;

    private void InitializeCardResize()
    {
        // A near-transparent window background gives the rounded cutouts a native hit target.
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1 && BeginCardResize(e.GetPosition(this))) e.Handled = true;
        };
        PreviewMouseMove += OnCardResizeMove;
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_cardResizeEdges == 0) return;
            e.Handled = true;
            FinishCardResize();
        };
        LostMouseCapture += (_, _) => FinishCardResize();
        Loaded += (_, _) =>
        {
            double width = App.Settings.DockWidth, height = App.Settings.DockHeight;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
            Rect work = CardWorkArea(VisualTreeHelper.GetDpi(this));
            ApplyCardBounds(ResizeCard(new Rect(Left, Top, Math.Min(width, work.Width), Math.Min(height, work.Height)),
                new Vector(), 10, work));
        };
    }

    private Rect CardWorkArea(DpiScale dpi)
    {
        var area = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }

    private int CardEdges(Point pointer) =>
        FloatingIconGeometry.HitTest(pointer, new Rect(0, 0, ActualWidth, ActualHeight), 9);

    private bool BeginCardResize(Point point)
    {
        int edges = CardEdges(point);
        if (edges == 0 || !CaptureMouse()) return false;
        _cardResizeEdges = edges;
        _cardResizeDpi = VisualTreeHelper.GetDpi(this);
        _cardResizePointer = CardPointer(_cardResizeDpi);
        _cardResizeStart = new Rect(Left, Top, ActualWidth, ActualHeight);
        _cardResizeWork = CardWorkArea(_cardResizeDpi);
        return true;
    }

    private static Point CardPointer(DpiScale dpi)
    {
        var position = Forms.Cursor.Position;
        return new Point(position.X / dpi.DpiScaleX, position.Y / dpi.DpiScaleY);
    }

    private void OnCardResizeMove(object sender, MouseEventArgs e)
    {
        if (_cardResizeEdges == 0)
        {
            Cursor = CardEdges(e.GetPosition(this)) switch
            {
                1 or 2 => Cursors.SizeWE, 4 or 8 => Cursors.SizeNS,
                5 or 10 => Cursors.SizeNWSE, 6 or 9 => Cursors.SizeNESW, _ => null
            };
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) { FinishCardResize(); return; }
        Vector delta = CardPointer(_cardResizeDpi) - _cardResizePointer;
        if (delta.Length < .5 && SizeToContent != SizeToContent.Manual) return;
        ApplyCardBounds(ResizeCard(_cardResizeStart, delta, _cardResizeEdges, _cardResizeWork));
        e.Handled = true;
    }

    internal static Rect ResizeCard(Rect start, Vector delta, int edges, Rect work)
    {
        bool west = (edges & 1) != 0, north = (edges & 4) != 0;
        double width = start.Width + (west ? -delta.X : (edges & 2) != 0 ? delta.X : 0);
        double height = start.Height + (north ? -delta.Y : (edges & 8) != 0 ? delta.Y : 0);
        double minWidth = Math.Min(MinimumCardWidth, work.Width), minHeight = Math.Min(MinimumCardHeight, work.Height);
        width = Math.Clamp(width, minWidth, Math.Clamp(west ? start.Right - work.Left : work.Right - start.Left, minWidth, work.Width));
        height = Math.Clamp(height, minHeight, Math.Clamp(north ? start.Bottom - work.Top : work.Bottom - start.Top, minHeight, work.Height));
        return new Rect(
            Math.Clamp(west ? start.Right - width : start.Left, work.Left, work.Right - width),
            Math.Clamp(north ? start.Bottom - height : start.Top, work.Top, work.Bottom - height), width, height);
    }

    private void ApplyCardBounds(Rect bounds)
    {
        Root.Width = double.NaN;
        SizeToContent = SizeToContent.Manual;
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
    }

    private void FinishCardResize()
    {
        if (_cardResizeEdges == 0) return;
        _cardResizeEdges = 0;
        ReleaseMouseCapture();
        Cursor = null;
        SaveCardSize();
    }

    private void SaveCardSize()
    {
        App.Settings.DockWidth = SizeToContent == SizeToContent.Manual ? Width : 0;
        App.Settings.DockHeight = SizeToContent == SizeToContent.Manual ? Height : 0;
        App.SaveWindowPositions();
    }

    internal MenuItem CreateCardSizeMenu()
    {
        var menu = new MenuItem { Header = "卡片尺寸" };
        var source = _wallpaperVideo?.Rect.Size ?? _wallpaperSceneDrawing?.Rect.Size ?? (_wallpaperContent?.Image is { } image
            ? new Size(image.PixelWidth, image.PixelHeight) : Size.Empty);
        var fit = new MenuItem { Header = "按壁纸比例调整", IsEnabled = !source.IsEmpty };
        fit.Click += (_, _) =>
        {
            Rect work = CardWorkArea(VisualTreeHelper.GetDpi(this));
            double preferred = Math.Max(ActualWidth / source.Width, ActualHeight / source.Height);
            double maximum = Math.Min(work.Width / source.Width, work.Height / source.Height);
            double minimum = Math.Max(MinimumCardWidth / source.Width, MinimumCardHeight / source.Height);
            double scale = Math.Clamp(preferred, Math.Min(minimum, maximum), maximum);
            var size = new Size(source.Width * scale, source.Height * scale);
            ApplyCardBounds(new Rect(
                Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - size.Width)),
                Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - size.Height)),
                size.Width, size.Height));
            SaveCardSize();
        };
        menu.Items.Add(fit);
        var reset = new MenuItem { Header = "恢复默认尺寸" };
        reset.Click += (_, _) =>
        {
            Root.Width = _defaultCardWidth;
            Width = Height = double.NaN;
            SizeToContent = SizeToContent.WidthAndHeight;
            SaveCardSize();
        };
        menu.Items.Add(reset);
        return menu;
    }
}
