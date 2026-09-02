using System.Reflection;

namespace DoorPulse.Services;

public static class EngineDeployer
{
    private static void DeployResource(string resourceName, string targetPath)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource was not found: {resourceName}");

        var directory = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Target path is invalid.");

        Directory.CreateDirectory(directory);

        using var target = File.Create(targetPath);
        source.CopyTo(target);
    }

    public static void Deploy(string targetPath) =>
        DeployResource("DoorPulse.Engine.recorder.mjs", targetPath);

    public static void DeployAuthHelpers(string engineDirectory)
    {
        Directory.CreateDirectory(engineDirectory);

        DeployResource(
            "DoorPulse.Engine.ring-auth-helper.mjs",
            Path.Combine(engineDirectory, "ring-auth-helper.mjs"));

        DeployResource(
            "DoorPulse.Engine.ring-cameras-helper.mjs",
            Path.Combine(engineDirectory, "ring-cameras-helper.mjs"));

        DeployResource(
            "DoorPulse.Engine.ring-push-status-helper.mjs",
            Path.Combine(engineDirectory, "ring-push-status-helper.mjs"));

        DeployResource(
            "DoorPulse.Engine.ring-push-bootstrap.mjs",
            Path.Combine(engineDirectory, "ring-push-bootstrap.mjs"));
    }
}
