using System.Drawing;
using System.Drawing.Drawing2D;

using MouseDock.Models;
using Forms = System.Windows.Forms;

namespace MouseDock.Services;

internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private sealed class ThemeTable(ThemePreset theme) : Forms.ProfessionalColorTable
    {
        internal static Color C(System.Windows.Media.Color color) => Color.FromArgb(255, color.R, color.G, color.B);
        public override Color ToolStripDropDownBackground => C(theme.Surface);
        public override Color ImageMarginGradientBegin => C(theme.Surface);
        public override Color ImageMarginGradientMiddle => C(theme.Surface);
        public override Color ImageMarginGradientEnd => C(theme.Surface);
        public override Color MenuItemSelected => C(theme.Hover.A == 0 ? Palette.Mix(theme.Surface, theme.Text, 0.07) : theme.Hover);
        public override Color MenuItemBorder => MenuItemSelected;
        public override Color MenuBorder => C(theme.Border.A == 0 ? Palette.Mix(theme.Surface, theme.Text, 0.25) : theme.Border);
        public override Color SeparatorDark => MenuBorder;
        public override Color SeparatorLight => C(theme.Surface);
        public override Color CheckBackground => MenuItemSelected;
        public override Color CheckSelectedBackground => MenuItemSelected;
    }
    private readonly ThemePreset theme;
    private readonly Color checkColor;
    private TrayMenuRenderer(ThemePreset theme) : base(new ThemeTable(theme))
    {
        this.theme = theme;
        checkColor = ThemeTable.C(ColorContrast.Legible(theme.Accent, new[] { theme.Surface }, 3));
        RoundedEdges = false;
    }
    internal static void Apply(Forms.ContextMenuStrip menu, ThemePreset theme)
    {
        menu.Renderer = new TrayMenuRenderer(theme);
        menu.BackColor = ThemeTable.C(theme.Surface);
        menu.ForeColor = ThemeTable.C(theme.Text);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.Padding = new Forms.Padding(6, 5, 6, 5);
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = ThemeTable.C(item.Enabled ? theme.Text : theme.TextDim);
            if (item is Forms.ToolStripMenuItem) item.Padding = new Forms.Padding(7, 3, 7, 3);
            if (item is Forms.ToolStripLabel) { item.Padding = new Forms.Padding(9, 4, 9, 4); item.ForeColor = ThemeTable.C(theme.TextDim); }
            if (item is Forms.ToolStripMenuItem parent && parent.HasDropDownItems)
            {
                parent.DropDown.Renderer = new TrayMenuRenderer(theme);
                parent.DropDown.BackColor = ThemeTable.C(theme.Surface);
                parent.DropDown.ForeColor = ThemeTable.C(theme.Text);
                foreach (Forms.ToolStripItem child in parent.DropDownItems)
                    child.ForeColor = ThemeTable.C(child.Enabled ? theme.Text : theme.TextDim);
            }
        }
    }
    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
        using var path = Rounded(new Rectangle(3, 1, System.Math.Max(12, e.Item.Width - 6), System.Math.Max(12, e.Item.Height - 2)), 6);
        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(ThemeTable.C(Palette.Mix(theme.Surface, theme.Text, e.Item.Pressed ? .11 : .07)));
        e.Graphics.FillPath(fill, path);
        e.Graphics.SmoothingMode = previous;
    }
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        // Draw only a small check. Do not invoke the OS blue check-box renderer.
        using var pen = new Pen(e.Item.Enabled ? checkColor : ThemeTable.C(theme.TextDim), 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = e.ImageRectangle.Left + e.ImageRectangle.Width / 2f;
        float y = e.Item.Height / 2f;
        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[] { new PointF(x - 4, y), new PointF(x - 1, y + 3), new PointF(x + 5, y - 4) });
        e.Graphics.SmoothingMode = previous;
    }
    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(ThemeTable.C(Palette.Mix(theme.Surface, theme.Text, .12)));
        float y = e.Item.Height / 2f;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ThemeTable.C(e.Item is Forms.ToolStripLabel || !e.Item.Enabled ? theme.TextDim : theme.Text);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = ThemeTable.C(e.Item.Enabled ? theme.Text : theme.TextDim);
        base.OnRenderArrow(e);
    }
}
