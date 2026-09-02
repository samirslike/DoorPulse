using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DoorPulse.Services;

public static class SelfInstallService
{
    public static readonly string InstallDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "DoorPulse");

    public static readonly string InstalledExe =
        Path.Combine(InstallDirectory, "DoorPulse.exe");

    public static bool IsSetupExecutable()
    {
        var path = Environment.ProcessPath ?? "";
        var name = Path.GetFileNameWithoutExtension(path);

        return name.Equals(
            "DoorPulseSetup",
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInstalledLocation()
    {
        var current = Environment.ProcessPath ?? "";

        if (string.IsNullOrWhiteSpace(current))
            return false;

        return string.Equals(
            Path.GetFullPath(current),
            Path.GetFullPath(InstalledExe),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Make/update the permanent Program Files copy.
    ///
    /// IMPORTANT:
    /// This method is intentionally synchronous and does NOT call async code
    /// with .GetAwaiter().GetResult(). The previous customer build could
    /// deadlock the WPF UI thread here, leaving DoorPulse visible only in
    /// Task Manager with no window.
    ///
    /// This method also does NOT relaunch or exit the current Setup EXE.
    /// </summary>
    public static string EnsureInstalledCopy()
    {
        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Could not determine the DoorPulse executable path.");

        LogInstall($"EnsureInstalledCopy start. Current={current}");

        if (IsInstalledLocation())
        {
            LogInstall("Already running from permanent install location.");
            return InstalledExe;
        }

        Directory.CreateDirectory(InstallDirectory);

        // If this is an update, stop the old scheduled task synchronously.
        // No async wait on the WPF UI thread.
        RunProcessQuietly(
            "schtasks.exe",
            new[] { "/End", "/TN", TaskService.TaskName },
            timeoutMs: 5000);

        // Stop only OLD processes running from Program Files\DoorPulse.
        try
        {
            foreach (var process in Process.GetProcessesByName("DoorPulse"))
            {
                if (process.Id == Environment.ProcessId)
                    continue;

                try
                {
                    var processPath = process.MainModule?.FileName ?? "";

                    if (processPath.StartsWith(
                        InstallDirectory,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        LogInstall($"Stopping old installed DoorPulse PID {process.Id}.");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    LogInstall($"Could not inspect/stop old DoorPulse PID {process.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogInstall("Process scan warning: " + ex.Message);
        }

        // Give Windows a brief moment to release an old installed EXE.
        Thread.Sleep(500);

        File.Copy(
            current,
            InstalledExe,
            overwrite: true);

        LogInstall($"Permanent copy created: {InstalledExe}");

        return InstalledExe;
    }

    public static void FinalizeInstallation()
    {
        LogInstall("FinalizeInstallation start.");

        CreateShortcut(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonStartMenu),
                "Programs",
                "DoorPulse.lnk"),
            InstalledExe);

        CreateShortcut(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonDesktopDirectory),
                "DoorPulse.lnk"),
            InstalledExe);

        LogInstall("FinalizeInstallation complete.");
    }

    public static string GetBackgroundExecutablePath()
    {
        if (File.Exists(InstalledExe))
            return InstalledExe;

        return Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Could not determine DoorPulse executable path.");
    }

    private static void RunProcessQuietly(
        string fileName,
        IEnumerable<string> args,
        int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);

            if (process is null)
                return;

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            // On a fresh PC the task usually doesn't exist. That is harmless.
            LogInstall($"{fileName} warning: {ex.Message}");
        }
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath)
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(shortcutPath)!);

            var shellType =
                Type.GetTypeFromProgID("WScript.Shell");

            if (shellType is null)
                return;

            dynamic? shell =
                Activator.CreateInstance(shellType);

            if (shell is null)
                return;

            dynamic shortcut =
                shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = InstallDirectory;
            shortcut.IconLocation = targetPath + ",0";
            shortcut.Description = "DoorPulse Recorder";
            shortcut.Save();

            try { Marshal.FinalReleaseComObject(shortcut); } catch { }
            try { Marshal.FinalReleaseComObject(shell); } catch { }
        }
        catch (Exception ex)
        {
            LogInstall("Shortcut warning: " + ex.Message);
        }
    }

    public static void LogInstall(string message)
    {
        try
        {
            ConfigService.EnsureFolders();

            File.AppendAllText(
                Path.Combine(
                    ConfigService.LogsPath,
                    "install.log"),
                $"[{DateTime.Now:G}] {message}\r\n");
        }
        catch { }
    }
}
