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
        using (var warm = (Icon)render.Invoke(null, [50, false, false])!) { }
        using var process = Process.GetCurrentProcess();
        uint before = GetGuiResources(process.Handle, 1);
        for (int i = 0; i < 200; i++)
        {
            using var icon = (Icon)render.Invoke(null, [10, false, i % 2 == 0])!;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        uint after = GetGuiResources(process.Handle, 1);
        Require(after <= before + 3, $"Tray rendering leaked {after - before} USER handles over 200 redraws.");
    }

    internal static void UpdateWaitsForExit()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.UpdateService")!;
        var prepare = type.GetMethod("CreateApplyStartInfo", BindingFlags.Static | BindingFlags.NonPublic);
        Require(prepare != null, "Update replacement has no process-exit-aware installer.");
        string root = Path.Combine(Path.GetTempPath(), "MouseDock-update-test-" + Guid.NewGuid().ToString("N"));
        string work = Path.Combine(root, "work");
        string destination = Path.Combine(root, "user's [app] 中文");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(destination);
        string current = Path.Combine(destination, "fixture.cmd");
        string incoming = Path.Combine(work, "new.cmd");
        string marker = Path.Combine(destination, "restarted.txt");
        File.WriteAllText(current, "@echo old\r\n");
        File.WriteAllText(incoming, "@echo off\r\necho updated>\"%~dp0restarted.txt\"\r\n");
        string expected = File.ReadAllText(incoming);
        Process? parent = null, installer = null;
        try
        {
            var parentStart = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            parentStart.ArgumentList.Add("-NoProfile");
            parentStart.ArgumentList.Add("-NonInteractive");
            parentStart.ArgumentList.Add("-Command");
            parentStart.ArgumentList.Add("Start-Sleep -Seconds 30");
            parent = Process.Start(parentStart)!;
            var start = (ProcessStartInfo)prepare!.Invoke(null, [work, incoming, current, parent.Id, true])!;
            installer = Process.Start(start)!;
            // Longer than the previous fixed three-second delay: the old app still owns its file.
            using (var heldExe = File.Open(current, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(!installer.WaitForExit(4200), "Installer stopped while the old app was still running.");
                Require(File.ReadAllText(current) == "@echo old\r\n", "Installer replaced a running application's file.");
            }
            parent.Kill();
            Require(parent.WaitForExit(5000), "Fixture process failed to exit.");
            Require(installer.WaitForExit(15000), "Installer did not finish after the old process exited.");
            string log = Path.Combine(work, "apply.log");
            Require(installer.ExitCode == 0, "Installer failed after the old process exited: " +
                (File.Exists(log) ? File.ReadAllText(log) : "no diagnostic log"));
            Require(File.ReadAllText(current) == expected, "Installer did not copy the new build.");
            Require(SpinWait.SpinUntil(() => File.Exists(marker), 5000), "Installer did not restart the replacement.");
            Require(File.ReadAllText(marker).Trim() == "updated", "Installer restarted the wrong file.");
        }
        finally
        {
            foreach (var process in new[] { installer, parent })
            {
                if (process == null) continue;
                if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                process.Dispose();
            }
            // The restarted command can briefly retain its working directory after writing the marker.
            Require(SpinWait.SpinUntil(() =>
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                    return true;
                }
                catch (IOException) { return false; }
            }, 5000), "The restarted fixture did not release its temporary directory.");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
