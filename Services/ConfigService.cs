using System.Text.Json;
using DoorPulse.Models;

namespace DoorPulse.Services;

public static class ConfigService
{
    public static readonly string RootPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DoorPulse");

    public static readonly string ConfigPath = Path.Combine(RootPath, "config.json");
    public static readonly string RingTokenPath = Path.Combine(RootPath, "refresh-token.txt");
    public static readonly string FtpPasswordPath = Path.Combine(RootPath, "ftp-password.txt");
    public static readonly string LogsPath = Path.Combine(RootPath, "logs");
    public static readonly string AgentLogPath = Path.Combine(LogsPath, "recorder.log");
    public static readonly string WatchdogLogPath = Path.Combine(LogsPath, "watchdog.log");
    public static readonly string DownloadsPath = Path.Combine(RootPath, "downloads");
    public static readonly string ManagedRuntimePath = Path.Combine(RootPath, "runtime");
    public static readonly string ManagedNodePath = Path.Combine(ManagedRuntimePath, "node", "node.exe");
    public static readonly string ManagedFfmpegPath = Path.Combine(ManagedRuntimePath, "ffmpeg", "ffmpeg.exe");
    public static readonly string ManagedEnginePath = Path.Combine(RootPath, "engine");
    public static readonly string DefaultRecorderPath = Path.Combine(ManagedEnginePath, "recorder.mjs");
    public static readonly string DefaultRecordingPath = Path.Combine(RootPath, "recordings");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(DownloadsPath);
        Directory.CreateDirectory(ManagedRuntimePath);
        Directory.CreateDirectory(Path.GetDirectoryName(ManagedNodePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ManagedFfmpegPath)!);
        Directory.CreateDirectory(ManagedEnginePath);
    }

    public static AppConfig Load()
    {
        EnsureFolders();

        if (!File.Exists(ConfigPath))
        {
            var cfg = new AppConfig();
            Save(cfg);
            return cfg;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        EnsureFolders();
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
