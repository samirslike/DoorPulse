using System.Diagnostics;
using System.Text;

namespace DoorPulse.Services;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessUtil
{
    public static Task<ProcessResult> RunAsync(
        string fileName,
        params string[] args) =>
        RunInternalAsync(fileName, null, args);

    public static Task<ProcessResult> RunInDirectoryAsync(
        string fileName,
        string workingDirectory,
        params string[] args) =>
        RunInternalAsync(fileName, workingDirectory, args);

    private static async Task<ProcessResult> RunInternalAsync(
        string fileName,
        string? workingDirectory,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }
}
