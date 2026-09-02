using System.Text.Json;

namespace DoorPulse.Services;

public sealed record PushCredentialStatus(
    bool Ready,
    string Message);

public static class PushStatusService
{
    public static async Task<PushCredentialStatus> CheckAsync(
        string nodePath,
        string engineDirectory)
    {
        try
        {
            var helper = Path.Combine(
                engineDirectory,
                "ring-push-status-helper.mjs");

            if (!File.Exists(helper))
            {
                return new PushCredentialStatus(
                    false,
                    "Push status helper is not deployed.");
            }

            if (!File.Exists(ConfigService.RingTokenPath))
            {
                return new PushCredentialStatus(
                    false,
                    "Ring token is not saved.");
            }

            var result = await ProcessUtil.RunInDirectoryAsync(
                nodePath,
                engineDirectory,
                helper,
                ConfigService.RingTokenPath);

            foreach (var line in result.Output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var type) &&
                        type.GetString() == "pushStatus")
                    {
                        var ready =
                            root.TryGetProperty("ready", out var r) &&
                            r.GetBoolean();

                        var message =
                            root.TryGetProperty("message", out var m)
                                ? m.GetString() ?? ""
                                : "";

                        return new PushCredentialStatus(
                            ready,
                            message);
                    }
                }
                catch { }
            }

            return new PushCredentialStatus(
                false,
                string.IsNullOrWhiteSpace(result.Error)
                    ? "Could not determine Ring push status."
                    : result.Error.Trim());
        }
        catch (Exception ex)
        {
            return new PushCredentialStatus(
                false,
                ex.Message);
        }
    }
}
