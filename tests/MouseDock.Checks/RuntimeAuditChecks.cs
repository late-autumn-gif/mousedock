using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using MouseDock.Models;

internal static class RuntimeAuditChecks
{
    internal static void TrayResources()
    {
        var render = typeof(MouseDock.App).GetMethod("RenderTrayIcon", BindingFlags.Static | BindingFlags.NonPublic)!;
        using (var warm = (Icon)render.Invoke(null, [50, false, false, false])!) { }
        using var process = Process.GetCurrentProcess();
        uint before = GetGuiResources(process.Handle, 1);
        for (int i = 0; i < 200; i++)
        {
            using var icon = (Icon)render.Invoke(null, [10, false, i % 2 == 0, false])!;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        uint after = GetGuiResources(process.Handle, 1);
        Require(after <= before + 3, $"Tray rendering leaked {after - before} USER handles over 200 redraws.");
    }

    internal const string UpdateFixtureVariable = "MOUSEDOCK_TEST_UPDATE_MARKER";

    internal static int RunUpdateFixture(string marker, bool wait)
    {
        File.WriteAllText(marker, File.ReadAllText(Path.ChangeExtension(Environment.ProcessPath!, ".build")));
        if (wait)
            SpinWait.SpinUntil(() => File.Exists(marker + ".exit"), 30000);
        return 0;
    }

    internal static void UpdateWaitsForExit()
    {
        string id = Guid.NewGuid().ToString("N");
        string work = Path.Combine(Path.GetTempPath(), "MouseDock-update-" + id);
        string source = Path.Combine(work, "package");
        string installRoot = Path.Combine(Path.GetTempPath(), "MouseDock-install-test-" + id);
        string destination = Path.Combine(installRoot, "user's [app] 中文");
        string current = Path.Combine(destination, "MouseDock.exe");
        string incoming = Path.Combine(source, "MouseDock.exe");
        string marker = Path.Combine(destination, "restarted.txt");
        string entry = Assembly.GetExecutingAssembly().Location;
        string appHost = Path.ChangeExtension(entry, ".exe");
        try
        {
            foreach (string directory in new[] { source, destination })
            {
                Directory.CreateDirectory(directory);
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(entry)!))
                {
                    if (Path.GetExtension(file) is ".dll" or ".json")
                        File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
                }
                File.Copy(appHost, Path.Combine(directory, "MouseDock.exe"));
            }
            // An overlay byte differentiates the two PE files without modifying the executable image.
            using (var exe = File.Open(incoming, FileMode.Append))
                exe.WriteByte(0x42);
            long oldLength = new FileInfo(current).Length;
            File.WriteAllText(Path.ChangeExtension(current, ".build"), "old");
            File.WriteAllText(Path.ChangeExtension(incoming, ".build"), "new");

            var oldStart = new ProcessStartInfo(current) { UseShellExecute = false, CreateNoWindow = true };
            oldStart.ArgumentList.Add("--wait-for-update");
            oldStart.Environment[UpdateFixtureVariable] = marker;
            using var old = Process.Start(oldStart)!;
            try
            {
                Require(SpinWait.SpinUntil(() => File.Exists(marker) && File.ReadAllText(marker) == "old", 7000),
                    "The old PE did not start.");
                File.Delete(marker);
                var start = MouseDock.Services.UpdateService.CreateApplyStartInfo(
                    work, incoming, current, old.Id, restart: true, shellIntegration: false);
                start.Environment[UpdateFixtureVariable] = marker;
                using var installer = Process.Start(start)!;
                try
                {
                    Require(!installer.WaitForExit(4200) && !old.HasExited, "The installer stopped waiting for the running app.");
                    Require(new FileInfo(current).Length == oldLength &&
                        !File.Exists(Path.Combine(work, "backup", "MouseDock.build")) && !File.Exists(marker),
                        "The installer modified the installation before the old process exited.");

                    File.WriteAllText(marker + ".exit", "");
                    Require(old.WaitForExit(5000), "The old app did not exit.");
                    Require(installer.WaitForExit(20000), "The installer did not finish after the old app exited.");
                    string log = Path.Combine(work, "apply.log");
                    Require(installer.ExitCode == 0, "Update failed: " + (File.Exists(log) ? File.ReadAllText(log) : "no log"));
                    Require(new FileInfo(current).Length == oldLength + 1, "The installer did not replace the PE.");
                    Require(SpinWait.SpinUntil(() => File.Exists(marker) && File.ReadAllText(marker) == "new", 7000),
                        "The replacement PE did not start.");
                }
                finally { if (!installer.HasExited) { installer.Kill(); installer.WaitForExit(5000); } }
            }
            finally { if (!old.HasExited) { old.Kill(); old.WaitForExit(5000); } }
        }
        finally
        {
            Require(SpinWait.SpinUntil(() =>
            {
                try
                {
                    if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true);
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    return true;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }, 5000), "The update fixture did not release its temporary directories.");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
