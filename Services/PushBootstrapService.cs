using System.Text.Json;

namespace DoorPulse.Services;

public sealed record PushBootstrapResult(
    bool Ready,
    string Message,
    string RawError);

public static class PushBootstrapService
{
    public static async Task<PushBootstrapResult> RunAsync(
        string nodePath,
        string engineDirectory,
        int timeoutSeconds = 25)
    {
        var helper = Path.Combine(
            engineDirectory,
            "ring-push-bootstrap.mjs");

        if (!File.Exists(helper))
        {
            return new PushBootstrapResult(
                false,
                "Ring Push bootstrap helper is not available.",
                "");
        }

        if (!File.Exists(ConfigService.RingTokenPath))
        {
            return new PushBootstrapResult(
                false,
                "Ring account is not connected.",
                "");
        }

        var result = await ProcessUtil.RunInDirectoryAsync(
            nodePath,
            engineDirectory,
            helper,
            ConfigService.RingTokenPath,
            timeoutSeconds.ToString());

        PushBootstrapResult? parsed = null;

        foreach (var line in result.Output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var type) ||
                    type.GetString() != "pushBootstrap")
                    continue;

                var state =
                    root.TryGetProperty("state", out var s)
                        ? s.GetString() ?? ""
                        : "";

                var message =
                    root.TryGetProperty("message", out var m)
                        ? m.GetString() ?? ""
                        : "";

                parsed = new PushBootstrapResult(
                    state.Equals(
                        "ready",
                        StringComparison.OrdinalIgnoreCase),
                    message,
                    result.Error.Trim());
            }
            catch
            {
                // ring-client-api/push-receiver can also print normal text.
                // Only the JSON status records belong to DoorPulse.
            }
        }

        if (parsed is not null)
            return parsed;

        var fallback =
            string.IsNullOrWhiteSpace(result.Error)
                ? "Ring Push registration did not complete."
                : result.Error.Trim();

        return new PushBootstrapResult(
            false,
            fallback,
            result.Error.Trim());
    }
}
