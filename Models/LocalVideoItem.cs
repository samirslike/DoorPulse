namespace DoorPulse.Models;

public sealed class LocalVideoItem
{
    public string VideoPath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string CameraName { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTime EventTime { get; set; }
    public long SizeBytes { get; set; }

    public string SizeText
    {
        get
        {
            var mb = SizeBytes / 1024d / 1024d;
            return mb >= 1 ? $"{mb:0.0} MB" : $"{SizeBytes / 1024d:0} KB";
        }
    }

    public string TimeText => EventTime.ToString("h:mm tt");
    public string DateText => EventTime.ToString("ddd, MMM d");
    public string EventLabel =>
        EventType.Equals("doorbell", StringComparison.OrdinalIgnoreCase)
            ? "Doorbell"
            : "Motion";
}
