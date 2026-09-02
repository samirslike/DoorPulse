using System.Diagnostics;
using System.Text.Json;

namespace DoorPulse.Services;

public sealed record RingAuthReply(string Type, string Message, string Token);

public sealed class RingAuthSession : IDisposable
{
    private readonly Process _process;

    private RingAuthSession(Process process)
    {
        _process = process;
    }

    public static async Task<(RingAuthSession Session, RingAuthReply Reply)> StartAsync(
        string nodePath,
        string helperPath,
        string email,
        string password)
    {
        var psi = new ProcessStartInfo
        {
            FileName = nodePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? Environment.CurrentDirectory
        };

        psi.ArgumentList.Add(helperPath);

        var process = new Process { StartInfo = psi };
        process.Start();

        var session = new RingAuthSession(process);

        await process.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize(new { email, password }));

        await process.StandardInput.FlushAsync();

        var reply = await session.ReadReplyAsync();
        return (session, reply);
    }

    public async Task<RingAuthReply> SubmitCodeAsync(string code)
    {
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { code }));
        await _process.StandardInput.FlushAsync();
        return await ReadReplyAsync();
    }

    private async Task<RingAuthReply> ReadReplyAsync()
    {
        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync();

            if (line is null)
            {
                var error = await _process.StandardError.ReadToEndAsync();
                return new RingAuthReply("error", string.IsNullOrWhiteSpace(error) ? "Ring authentication stopped." : error, "");
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                var token = root.TryGetProperty("token", out var tok) ? tok.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(type))
                    return new RingAuthReply(type, message, token);
            }
            catch
            {
                // Ignore non-JSON diagnostics from dependencies.
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        _process.Dispose();
    }
}

public sealed record RingCameraInfo(string Id, string Name);

public sealed class RingCameraChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSelected { get; set; } = true;
    public bool MonitorMotion { get; set; } = true;
    public bool MonitorDoorbell { get; set; } = true;
}


public static class RingCameraService
{
    public static async Task<List<RingCameraInfo>> GetCamerasAsync(
        string nodePath,
        string helperPath,
        string tokenFile)
    {
        var result = await ProcessUtil.RunInDirectoryAsync(
            nodePath,
            Path.GetDirectoryName(helperPath) ?? Environment.CurrentDirectory,
            helperPath,
            tokenFile);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                "Could not load Ring cameras.\n" + result.Error + result.Output);

        var list = new List<RingCameraInfo>();

        foreach (var line in result.Output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var type) &&
                    type.GetString() == "camera")
                {
                    list.Add(new RingCameraInfo(
                        root.GetProperty("id").ToString(),
                        root.GetProperty("name").GetString() ?? "Camera"));
                }
            }
            catch { }
        }

        return list;
    }
}
