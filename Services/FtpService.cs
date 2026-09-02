using System.Diagnostics;
using System.Text;

namespace DoorPulse.Services;

public static class FtpService
{
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static async Task TestAsync(
        string host,
        string username,
        string password,
        string remotePath)
    {
        var temp = Path.Combine(ConfigService.RootPath, "ftp-test.txt");
        await File.WriteAllTextAsync(temp, "DoorPulse FTP test " + DateTime.Now.ToString("O"));

        var basePath = remotePath.TrimEnd('/');
        var url = $"ftp://{host}{(basePath.StartsWith("/") ? "" : "/")}{basePath}/doorpulse-connection-test.txt";

        var psi = new ProcessStartInfo
        {
            FileName = @"C:\Windows\System32\curl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("--ftp-create-dirs");
        psi.ArgumentList.Add("--fail");
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--show-error");
        psi.ArgumentList.Add("--upload-file");
        psi.ArgumentList.Add(temp);
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add(url);

        using var process = new Process { StartInfo = psi };
        process.Start();

        await process.StandardInput.WriteLineAsync(
            $"user = \"{Escape(username + ":" + password)}\"");
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        try { File.Delete(temp); } catch { }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "FTP connection test failed.\n" + error + output);
    }
}
