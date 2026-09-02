namespace DoorPulse.Services;

public static class TaskService
{
    public const string TaskName = "DoorPulse Recorder";
    public const string WatchdogTaskName = "DoorPulse Watchdog";
    public const string LegacyTaskName = "Ring Camera Recorder";

    public static async Task<string> GetStatusAsync()
    {
        var result = await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Query",
            "/TN",
            TaskName,
            "/V",
            "/FO",
            "LIST");

        if (result.ExitCode != 0)
            return "Not Installed";

        foreach (var line in result.Output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(
                "Status:",
                StringComparison.OrdinalIgnoreCase))
            {
                return line.Split(':', 2)[1].Trim();
            }
        }

        return "Installed";
    }

    public static async Task<bool> ExistsAsync()
    {
        var result = await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Query",
            "/TN",
            TaskName);

        return result.ExitCode == 0;
    }

    public static async Task<bool> WatchdogExistsAsync()
    {
        var result = await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Query",
            "/TN",
            WatchdogTaskName);

        return result.ExitCode == 0;
    }

    public static async Task InstallOrUpdateAsync(
        string exePath)
    {
        // Stop/disable old recorder name to prevent duplicate agents.
        await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/End",
            "/TN",
            LegacyTaskName);

        await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Change",
            "/TN",
            LegacyTaskName,
            "/DISABLE");

        var taskRun =
            $"\"{exePath}\" --agent";

        var result = await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Create",
            "/TN",
            TaskName,
            "/TR",
            taskRun,
            "/SC",
            "ONSTART",
            "/RU",
            "SYSTEM",
            "/RL",
            "HIGHEST",
            "/F");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                result.Error +
                Environment.NewLine +
                result.Output);
        }

        await ConfigureRecorderRecoveryAsync();

        // Independent watchdog. If the main recorder task is ever no longer
        // running, this task starts it again. The customer never runs commands.
        var watchdogRun =
            $"\"{exePath}\" --watchdog";

        var watchdog = await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Create",
            "/TN",
            WatchdogTaskName,
            "/TR",
            watchdogRun,
            "/SC",
            "MINUTE",
            "/MO",
            "5",
            "/RU",
            "SYSTEM",
            "/RL",
            "HIGHEST",
            "/F");

        if (watchdog.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "DoorPulse Recorder was installed, but the automatic recovery watchdog could not be created." +
                Environment.NewLine +
                watchdog.Error +
                Environment.NewLine +
                watchdog.Output);
        }

        await ConfigureWatchdogRecoveryAsync();
    }

    /// <summary>
    /// Re-applies recovery settings and watchdog. This makes upgrades repair
    /// older DoorPulse installations automatically when the GUI is opened.
    /// </summary>
    public static async Task EnsureResilienceAsync(
        string exePath)
    {
        if (!await ExistsAsync())
        {
            await InstallOrUpdateAsync(exePath);
            return;
        }

        await ConfigureRecorderRecoveryAsync();

        if (!await WatchdogExistsAsync())
        {
            var watchdogRun =
                $"\"{exePath}\" --watchdog";

            await ProcessUtil.RunAsync(
                "schtasks.exe",
                "/Create",
                "/TN",
                WatchdogTaskName,
                "/TR",
                watchdogRun,
                "/SC",
                "MINUTE",
                "/MO",
                "5",
                "/RU",
                "SYSTEM",
                "/RL",
                "HIGHEST",
                "/F");
        }

        await ConfigureWatchdogRecoveryAsync();
    }

    private static async Task ConfigureRecorderRecoveryAsync()
    {
        // Configure the task exactly the way a 24/7 recorder should behave:
        // - never time out
        // - restart after failure every minute
        // - start when a startup trigger was missed
        // - ignore duplicate launches
        // - do not stop for battery state
        var ps =
            "$task=Get-ScheduledTask -TaskName 'DoorPulse Recorder';" +
            "$s=$task.Settings;" +
            "$s.ExecutionTimeLimit='PT0S';" +
            "$s.RestartCount=999;" +
            "$s.RestartInterval='PT1M';" +
            "$s.StartWhenAvailable=$true;" +
            "$s.DisallowStartIfOnBatteries=$false;" +
            "$s.StopIfGoingOnBatteries=$false;" +
            "$s.MultipleInstances='IgnoreNew';" +
            "Set-ScheduledTask -TaskName 'DoorPulse Recorder' -Settings $s | Out-Null";

        var result = await ProcessUtil.RunAsync(
            "powershell.exe",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            ps);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Could not configure DoorPulse automatic recovery." +
                Environment.NewLine +
                result.Error +
                Environment.NewLine +
                result.Output);
        }
    }

    private static async Task ConfigureWatchdogRecoveryAsync()
    {
        var ps =
            "$task=Get-ScheduledTask -TaskName 'DoorPulse Watchdog';" +
            "$s=$task.Settings;" +
            "$s.ExecutionTimeLimit='PT1M';" +
            "$s.StartWhenAvailable=$true;" +
            "$s.DisallowStartIfOnBatteries=$false;" +
            "$s.StopIfGoingOnBatteries=$false;" +
            "$s.MultipleInstances='IgnoreNew';" +
            "Set-ScheduledTask -TaskName 'DoorPulse Watchdog' -Settings $s | Out-Null";

        var result = await ProcessUtil.RunAsync(
            "powershell.exe",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            ps);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Could not configure DoorPulse watchdog." +
                Environment.NewLine +
                result.Error +
                Environment.NewLine +
                result.Output);
        }
    }

    public static async Task EnsureRecorderRunningAsync()
    {
        var status = await GetStatusAsync();

        if (string.Equals(
            status,
            "Running",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ConfigService.EnsureFolders();

        try
        {
            await File.AppendAllTextAsync(
                ConfigService.WatchdogLogPath,
                $"[{DateTime.Now:G}] Recorder status was '{status}'. Starting DoorPulse Recorder.\r\n");
        }
        catch { }

        await StartAsync();
    }

    public static async Task StartAsync()
    {
        await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/Run",
            "/TN",
            TaskName);
    }

    public static async Task StopAsync()
    {
        await ProcessUtil.RunAsync(
            "schtasks.exe",
            "/End",
            "/TN",
            TaskName);
    }

    public static async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1200);
        await StartAsync();
    }
}
