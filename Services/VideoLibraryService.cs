using System.Globalization;
using System.Text.RegularExpressions;
using DoorPulse.Models;

namespace DoorPulse.Services;

public static class VideoLibraryService
{
    // Example:
    // Front_Door_motion_2026-09-01_13-42-38.mp4
    private static readonly Regex NamePattern = new(
        @"^(?<camera>.+)_(?<event>motion|doorbell)_(?<date>\d{4}-\d{2}-\d{2})_(?<time>\d{2}-\d{2}-\d{2})\.mp4$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<LocalVideoItem> Load(string root)
    {
        var results = new List<LocalVideoItem>();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return results;

        foreach (var file in Directory.EnumerateFiles(root, "*.mp4", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);

                // Skip invalid/tiny files.
                if (info.Length < 100_000)
                    continue;

                var match = NamePattern.Match(info.Name);

                var camera = "Camera";
                var eventType = "motion";
                var eventTime = info.LastWriteTime;

                if (match.Success)
                {
                    camera = match.Groups["camera"].Value.Replace('_', ' ');
                    eventType = match.Groups["event"].Value.ToLowerInvariant();

                    var stamp = match.Groups["date"].Value + " " +
                                match.Groups["time"].Value.Replace('-', ':');

                    if (DateTime.TryParseExact(
                        stamp,
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal,
                        out var parsed))
                    {
                        eventTime = DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime();
                    }
                }

                var thumb = Path.ChangeExtension(file, ".jpg");

                results.Add(new LocalVideoItem
                {
                    VideoPath = file,
                    ThumbnailPath = File.Exists(thumb) ? thumb : "",
                    FileName = info.Name,
                    CameraName = camera,
                    EventType = eventType,
                    EventTime = eventTime,
                    SizeBytes = info.Length
                });
            }
            catch { }
        }

        return results
            .OrderByDescending(v => v.EventTime)
            .ToList();
    }

    public static void Delete(LocalVideoItem video)
    {
        if (File.Exists(video.VideoPath))
            File.Delete(video.VideoPath);

        if (!string.IsNullOrWhiteSpace(video.ThumbnailPath) &&
            File.Exists(video.ThumbnailPath))
            File.Delete(video.ThumbnailPath);
    }
}
