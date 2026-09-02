namespace DoorPulse.Services;

public static class SecretService
{
    public static string ReadRingToken() =>
        File.Exists(ConfigService.RingTokenPath)
            ? File.ReadAllText(ConfigService.RingTokenPath).Trim()
            : "";

    public static string ReadFtpPassword() =>
        File.Exists(ConfigService.FtpPasswordPath)
            ? File.ReadAllText(ConfigService.FtpPasswordPath).Trim()
            : "";

    public static async Task SaveRingTokenAsync(string token)
    {
        ConfigService.EnsureFolders();
        await File.WriteAllTextAsync(ConfigService.RingTokenPath, token.Trim());
        await ProtectFileAsync(ConfigService.RingTokenPath);
    }

    public static async Task SaveFtpPasswordAsync(string password)
    {
        ConfigService.EnsureFolders();
        await File.WriteAllTextAsync(ConfigService.FtpPasswordPath, password);
        await ProtectFileAsync(ConfigService.FtpPasswordPath);
    }

    private static async Task ProtectFileAsync(string path)
    {
        // Keep secrets readable only by SYSTEM and local Administrators.
        await ProcessUtil.RunAsync("icacls.exe", path, "/inheritance:r");
        await ProcessUtil.RunAsync(
            "icacls.exe",
            path,
            "/grant:r",
            "SYSTEM:F",
            "Administrators:F");
    }
}
