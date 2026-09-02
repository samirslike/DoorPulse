using System.Diagnostics;

namespace DoorPulse.Services;

public sealed class RecorderHost
{
    public async Task RunAsync()
    {
        ConfigService.EnsureFolders();

        using var mutex = new Mutex(
            initiallyOwned: true,
            name: @"Global\DoorPulseRecorderAgent",
            createdNew: out var createdNew);

        if (!createdNew)
        {
            await File.AppendAllTextAsync(
                ConfigService.AgentLogPath,
                $"{DateTime.Now:G} Another DoorPulse agent is already running. Exiting.\r\n");
            return;
        }

        while (true)
        {
            var config = ConfigService.Load();

            try
            {
                if (!File.Exists(config.NodePath))
                    throw new FileNotFoundException("Node.js executable not found.", config.NodePath);

                if (!File.Exists(config.RecorderScriptPath))
                    throw new FileNotFoundException("DoorPulse recorder script not found.", config.RecorderScriptPath);

                await File.AppendAllTextAsync(
                    ConfigService.AgentLogPath,
                    $"\r\n[{DateTime.Now:G}] DoorPulse agent starting recorder...\r\n");

                var psi = new ProcessStartInfo
                {
                    FileName = config.NodePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(config.RecorderScriptPath) ?? Environment.CurrentDirectory
                };

                psi.ArgumentList.Add(config.RecorderScriptPath);
                psi.Environment["DOORPULSE_CONFIG"] = ConfigService.ConfigPath;
                psi.Environment["DOORPULSE_TOKEN_FILE"] = ConfigService.RingTokenPath;
                psi.Environment["DOORPULSE_FTP_PASSWORD_FILE"] = ConfigService.FtpPasswordPath;

                using var process = new Process { StartInfo = psi };

                using var writer = new StreamWriter(
                    new FileStream(
                        ConfigService.AgentLogPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                {
                    AutoFlush = true
                };

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        lock (writer)
                            writer.WriteLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        lock (writer)
                            writer.WriteLine("ERROR: " + e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                lock (writer)
                    writer.WriteLine($"[{DateTime.Now:G}] Recorder exited with code {process.ExitCode}. Restarting in 10 seconds...");
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(
                    ConfigService.AgentLogPath,
                    $"[{DateTime.Now:G}] Recorder start failed: {ex}\r\n");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}
