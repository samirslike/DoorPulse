namespace DoorPulse.Models;

public sealed class CameraSelection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool MonitorMotion { get; set; } = true;
    public bool MonitorDoorbell { get; set; } = true;
}

public sealed class AppConfig
{
    // Onboarding / product state
    public bool SetupCompleted { get; set; } = false;
    public int SetupStep { get; set; } = 0;
    public int SetupSchemaVersion { get; set; } = 4;
    public string StorageMode { get; set; } = "cloud"; // cloud | local

    // Runtime - normally auto-managed by DoorPulse.
    public string NodePath { get; set; } = @"C:\Program Files\nodejs\node.exe";
    public string FfmpegPath { get; set; } = @"C:\ffmpeg\bin\ffmpeg.exe";
    public string RecorderScriptPath { get; set; } = @"C:\RingRecorder\recorder.mjs";
    public string RecordingDirectory { get; set; } = @"C:\RingRecordings";

    // Ring
    public string RingEmail { get; set; } = "";

    // New multi-camera configuration.
    public List<CameraSelection> Cameras { get; set; } = new();

    // Legacy single-camera values retained for backwards compatibility.
    public string CameraName { get; set; } = "";
    public string CameraId { get; set; } = "";

    // Recording
    public string RecordingPreset { get; set; } = "normal";
    public int CooldownSeconds { get; set; } = 15;
    public int BackupPollSeconds { get; set; } = 60;
    public int RetentionHours { get; set; } = 24;
    public int ThumbnailSecond { get; set; } = 1;

    // Cloud
    public string FtpHost { get; set; } = "";
    public string FtpUsername { get; set; } = "";
    public string FtpRemotePath { get; set; } = "";
    public string ViewerUrl { get; set; } = "";

    public bool AutoStart { get; set; } = true;
}
