using System.Reflection;
using MouseDock.Models;
using Forms = System.Windows.Forms;

internal static class TrayInteractionChecks
{
    internal static void Hover()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TrayHoverState");
        Require(type != null, "Tray hover ownership is missing.");
        object state = Activator.CreateInstance(type!, true)!;
        var tick = type!.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pin = type.GetMethod("Pin", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string Tick(long time, bool tray, bool window = false, bool busy = false, bool visible = false,
            bool enabled = true) => tick.Invoke(state, [time, enabled, tray, window, busy, visible])!.ToString()!;
        Require(Tick(0, true) == "None" && Tick(349, true) == "None" && Tick(350, true) == "Open",
            "Hover did not honor its delay.");
        Require(Tick(500, false, true, visible: true) == "None", "Entering the preview closed it.");
        Require(Tick(800, false, visible: true) == "None" &&
            Tick(1400, false, busy: true, visible: true) == "None", "An open menu failed to hold the preview.");
        Require(Tick(1500, false, visible: true) == "None" && Tick(2150, false, visible: true) == "Close",
            "Leaving the preview did not close only the temporary window.");
        Require(Tick(2300, true, visible: true) == "None" &&
            Tick(3000, false, visible: true) == "None", "A manual visible window was auto-hidden.");
        Tick(4000, true);
        Require(Tick(4350, true) == "Open" && (bool)pin.Invoke(state, null)!,
            "Clicking did not transfer preview ownership.");
        Require(Tick(6000, false, visible: true) == "None", "A pinned preview was auto-hidden.");
        Tick(7000, true);
        Tick(7350, true);
        Require(Tick(7400, false, enabled: false) == "Close", "Disabling hover left its temporary window.");
        Tick(8000, true);
        Require(Tick(8500, false) == "None" && Tick(9000, true) == "None",
            "A canceled hover reused an old enter deadline.");
    }

    internal static void Menu()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TrayMenuRenderer");
        Require(type != null, "Themed tray renderer is missing.");
        using var menu = new Forms.ContextMenuStrip();
        int clicks = 0;
        var command = new Forms.ToolStripMenuItem("Device") { Checked = true };
        command.Click += (_, _) => clicks++;
        var child = new Forms.ToolStripMenuItem("Child") { Enabled = false };
        command.DropDownItems.Add(child);
        menu.Items.Add(command);
        var apply = type!.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var theme in new[] { ThemePreset.ById("cloud"), ThemePreset.ById("reference-mint") })
        {
            apply.Invoke(null, [menu, theme]);
            Require(menu.BackColor.R == theme.Surface.R && menu.ForeColor.G == theme.Text.G,
                "The tray menu did not follow the active palette.");
            Require(command.Checked && !child.Enabled && command.DropDown.Renderer.GetType() == type,
                "The theme replaced command state or skipped submenus.");
            menu.Size = menu.GetPreferredSize(System.Drawing.Size.Empty);
            using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, menu.ClientRectangle);
            Require(bitmap.GetPixel(2, 2).A != 0, "The tray menu rendered blank.");
        }
        command.PerformClick();
        Require(clicks == 1, "The renderer changed command invocation.");
        var settings = new AppSettings();
        typeof(AppSettings).GetProperty("TrayHover")!.SetValue(settings, true);
        Require((bool)typeof(AppSettings).GetProperty("TrayHover")!.GetValue(settings.Clone())!,
            "Hover was not retained in the settings draft.");
    }

    internal static void Controller()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TrayHoverController")!;
        var window = new System.Windows.Window
        {
            Width = 180, Height = 90, Left = 180, Top = 180, ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None, ShowActivated = false
        };
        using var icon = new Forms.NotifyIcon
        {
            Icon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone(), Visible = true
        };
        bool enabled = true;
        using var controller = (IDisposable)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new Func<bool>(() => enabled), new Func<System.Windows.Window>(() => window), new Func<bool>(() => false)], null)!;
        object? Call(string method, params object[] args) => type.GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, args);
        try
        {
            Call("Attach", icon);
            Call("Move", icon, new Forms.MouseEventArgs(Forms.MouseButtons.None, 0, 0, 0, 0));
            var state = type.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            var tick = state.GetType().GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            tick.Invoke(state, [Environment.TickCount64 - 400, true, true, false, false, false]);
            Call("Tick", controller, EventArgs.Empty);
            Require(window.IsVisible && (bool)Call("Owns", window)!, "Hover did not open a temporary native window.");
            Require((bool)Call("Pin")! && window.IsVisible && !(bool)Call("Owns", window)!,
                "Pinning failed to retain the manually owned window.");
            Call("Detach", icon);
            controller.Dispose();
            Require(window.IsVisible, "Controller disposal hid a manually pinned window.");
        }
        finally
        {
            window.Close();
            var image = icon.Icon;
            icon.Visible = false;
            icon.Icon = null;
            image?.Dispose();
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
