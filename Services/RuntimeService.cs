using System.IO.Compression;
using System.Reflection;
using DoorPulse.Models;

namespace DoorPulse.Services;

public sealed record RuntimeStatus(
    bool NodeReady,
    string NodePath,
    bool FfmpegReady,
    string FfmpegPath,
    bool RingRuntimeReady,
    string RingRuntimePath)
{
    public bool AllReady => NodeReady && FfmpegReady && RingRuntimeReady;
}

public static class RuntimeService
{
    private const string NodeResource = "DoorPulse.Runtime.node.exe";
    private const string FfmpegResource = "DoorPulse.Runtime.ffmpeg.exe";
    private const string RingRuntimeResource = "DoorPulse.Runtime.ring-runtime.zip";

    public static RuntimeStatus Detect(string recorderScriptPath)
    {
        // Customer builds prefer DoorPulse-managed components.
        // Development/legacy locations remain fallbacks.
        var nodeCandidates = new[]
        {
            ConfigService.ManagedNodePath,
            @"C:\Program Files\nodejs\node.exe"
        };

        var ffmpegCandidates = new[]
        {
            ConfigService.ManagedFfmpegPath,
            @"C:\ffmpeg\bin\ffmpeg.exe"
        };

        var node = nodeCandidates.FirstOrDefault(File.Exists) ?? "";
        var ffmpeg = ffmpegCandidates.FirstOrDefault(File.Exists) ?? "";

        var engineDir = Path.GetDirectoryName(recorderScriptPath) ?? ConfigService.ManagedEnginePath;
        var ringRuntime = Path.Combine(engineDir, "node_modules", "ring-client-api");

        if (!Directory.Exists(ringRuntime))
        {
            var managed = Path.Combine(
                ConfigService.ManagedEnginePath,
                "node_modules",
                "ring-client-api");

            if (Directory.Exists(managed))
                ringRuntime = managed;
        }

        if (!Directory.Exists(ringRuntime))
        {
            var legacy = @"C:\RingRecorder\node_modules\ring-client-api";

            if (Directory.Exists(legacy))
                ringRuntime = legacy;
        }

        return new RuntimeStatus(
            File.Exists(node), node,
            File.Exists(ffmpeg), ffmpeg,
            Directory.Exists(ringRuntime), ringRuntime);
    }

    public static bool HasEmbeddedCustomerRuntime()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = new HashSet<string>(
            assembly.GetManifestResourceNames(),
            StringComparer.OrdinalIgnoreCase);

        return names.Contains(NodeResource) &&
               names.Contains(FfmpegResource) &&
               names.Contains(RingRuntimeResource);
    }

    public static async Task<RuntimeStatus> EnsureAsync(
        AppConfig config,
        IProgress<string>? progress = null)
    {
        ConfigService.EnsureFolders();

        var status = Detect(config.RecorderScriptPath);

        // A legacy development machine may already have everything.
        // A customer release will carry the exact tested runtime inside the EXE.
        if (!status.AllReady && !HasEmbeddedCustomerRuntime())
        {
            throw new InvalidOperationException(
                "This DoorPulse build does not contain the customer runtime bundle.\n\n" +
                "Build the customer release with build-customer-release.ps1 on the DoorPulse development PC.");
        }

        if (!File.Exists(ConfigService.ManagedNodePath))
        {
            progress?.Report("Preparing DoorPulse Node runtime...");
            await ExtractResourceAsync(NodeResource, ConfigService.ManagedNodePath);
        }

        if (!File.Exists(ConfigService.ManagedFfmpegPath))
        {
            progress?.Report("Preparing DoorPulse video engine...");
            await ExtractResourceAsync(FfmpegResource, ConfigService.ManagedFfmpegPath);
        }

        var managedRingPath = Path.Combine(
            ConfigService.ManagedEnginePath,
            "node_modules",
            "ring-client-api");

        if (!Directory.Exists(managedRingPath))
        {
            progress?.Report("Preparing DoorPulse Ring connection engine...");

            var zipPath = Path.Combine(
                ConfigService.DownloadsPath,
                "ring-runtime-bundled.zip");

            await ExtractResourceAsync(RingRuntimeResource, zipPath);

            // Clean an incomplete prior extraction but preserve DoorPulse scripts.
            var nodeModules = Path.Combine(ConfigService.ManagedEnginePath, "node_modules");

            if (Directory.Exists(nodeModules))
                Directory.Delete(nodeModules, true);

            ZipFile.ExtractToDirectory(
                zipPath,
                ConfigService.ManagedEnginePath,
                overwriteFiles: true);

            try { File.Delete(zipPath); } catch { }
        }

        // Customer installs always use managed paths so the machine does not
        // depend on separately installed Node/FFmpeg/Ring packages.
        config.NodePath = ConfigService.ManagedNodePath;
        config.FfmpegPath = ConfigService.ManagedFfmpegPath;
        config.RecorderScriptPath = ConfigService.DefaultRecorderPath;

        EngineDeployer.Deploy(config.RecorderScriptPath);
        EngineDeployer.DeployAuthHelpers(ConfigService.ManagedEnginePath);

        ConfigService.Save(config);

        progress?.Report("All DoorPulse components are ready.");

        return Detect(config.RecorderScriptPath);
    }

    private static async Task ExtractResourceAsync(
        string resourceName,
        string targetPath)
    {
        var assembly = Assembly.GetExecutingAssembly();

        await using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"DoorPulse bundled component is missing: {resourceName}");

        var directory = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Runtime target path is invalid.");

        Directory.CreateDirectory(directory);

        await using var target = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await source.CopyToAsync(target);
    }
}
