using System.Text;

namespace DoorPulse.Services;

public static class LogService
{
    public static string Tail(int maxLines = 120)
    {
        try
        {
            if (!File.Exists(ConfigService.AgentLogPath))
                return "No DoorPulse recorder log yet.";

            // The recorder writes continuously to this file.
            // Open it with FileShare.ReadWrite so the GUI can read it
            // while the background agent keeps writing.
            using var stream = new FileStream(
                ConfigService.AgentLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            var queue = new Queue<string>();

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine() ?? "";

                queue.Enqueue(line);

                if (queue.Count > maxLines)
                    queue.Dequeue();
            }

            return queue.Count == 0
                ? "DoorPulse recorder log is empty."
                : string.Join(Environment.NewLine, queue);
        }
        catch (Exception ex)
        {
            return $"Could not read log: {ex.Message}";
        }
    }
}
