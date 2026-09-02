using DoorPulse.Models;

namespace DoorPulse.Services;

public sealed record DiagnosticItem(string Name, bool Ok, string Detail);

public static class DiagnosticsService
{
    public static async Task<List<DiagnosticItem>> RunAsync(AppConfig config)
    {
        var items = new List<DiagnosticItem>();

        items.Add(new(
            "Setup",
            config.SetupCompleted,
            config.SetupCompleted ? "Completed" : $"Incomplete - step {config.SetupStep + 1}"));

        items.Add(new(
            "Node.js",
            File.Exists(config.NodePath),
            File.Exists(config.NodePath) ? config.NodePath : "Not found"));

        items.Add(new(
            "FFmpeg",
            File.Exists(config.FfmpegPath),
            File.Exists(config.FfmpegPath) ? config.FfmpegPath : "Not found"));

        items.Add(new(
            "Recorder engine",
            File.Exists(config.RecorderScriptPath),
            File.Exists(config.RecorderScriptPath) ? config.RecorderScriptPath : "Not deployed"));

        var nodeModules = Path.Combine(
            Path.GetDirectoryName(config.RecorderScriptPath) ?? "",
            "node_modules",
            "ring-client-api");

        if (!Directory.Exists(nodeModules) &&
            Directory.Exists(@"C:\RingRecorder\node_modules\ring-client-api"))
            nodeModules = @"C:\RingRecorder\node_modules\ring-client-api";

        items.Add(new(
            "Ring API runtime",
            Directory.Exists(nodeModules),
            Directory.Exists(nodeModules) ? nodeModules : "ring-client-api not found"));

        items.Add(new(
            "Ring token",
            File.Exists(ConfigService.RingTokenPath) && new FileInfo(ConfigService.RingTokenPath).Length > 100,
            File.Exists(ConfigService.RingTokenPath) ? "Saved" : "Not saved"));

        try
        {
            var engineDirectory =
                Path.GetDirectoryName(config.RecorderScriptPath)
                ?? ConfigService.ManagedEnginePath;

            EngineDeployer.DeployAuthHelpers(engineDirectory);

            var pushStatus =
                await PushStatusService.CheckAsync(
                    config.NodePath,
                    engineDirectory);

            items.Add(new(
                "Ring Push Credentials",
                pushStatus.Ready,
                pushStatus.Ready
                    ? "Saved and reusable"
                    : pushStatus.Message));
        }
        catch (Exception ex)
        {
            items.Add(new(
                "Ring Push Credentials",
                false,
                ex.Message));
        }

        var configuredCameras = config.Cameras?
            .Where(c => c.Enabled)
            .ToList() ?? new List<CameraSelection>();

        items.Add(new(
            "Cameras",
            configuredCameras.Count > 0 || !string.IsNullOrWhiteSpace(config.CameraName),
            configuredCameras.Count > 0
                ? $"{configuredCameras.Count} selected: {string.Join(", ", configuredCameras.Select(c => c.Name))}"
                : (string.IsNullOrWhiteSpace(config.CameraName) ? "No cameras selected" : config.CameraName)));

        if (config.StorageMode.Equals("cloud", StringComparison.OrdinalIgnoreCase) ||
            config.StorageMode.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new(
                "FTP password",
                File.Exists(ConfigService.FtpPasswordPath) && new FileInfo(ConfigService.FtpPasswordPath).Length > 0,
                File.Exists(ConfigService.FtpPasswordPath) ? "Saved" : "Not saved"));
        }
        else
        {
            items.Add(new("Storage", true, "Local PC"));
        }

        var taskStatus = await TaskService.GetStatusAsync();

        items.Add(new(
            "Startup task",
            !string.Equals(taskStatus, "Not Installed", StringComparison.OrdinalIgnoreCase),
            taskStatus));

        var watchdogInstalled =
            await TaskService.WatchdogExistsAsync();

        items.Add(new(
            "Automatic Recovery",
            watchdogInstalled,
            watchdogInstalled
                ? "Enabled - watchdog checks every 5 minutes"
                : "Watchdog not installed"));

        items.Add(new(
            "Recording folder",
            Directory.Exists(config.RecordingDirectory),
            config.RecordingDirectory));

        return items;
    }
}
