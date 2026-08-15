using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Keyring macOS via CLI 'security' (Keychain). Limite noto: 'add-generic-password -w'
/// espone brevemente la password nella process list; macOS è best-effort in questo progetto.
/// </summary>
public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const string Service = "FileExplorer";

    public bool IsAvailable { get; } = ProbeSecurityCli();

    private static bool ProbeSecurityCli()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "security",
                ArgumentList = { "help" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            process!.WaitForExit(3000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string?> GetPasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList =
            {
                "find-generic-password", "-a", profileId.ToString("N"), "-s", Service, "-w"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        string output = await process!.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? output.TrimEnd('\n') : null;
    }

    public async Task SetPasswordAsync(Guid profileId, string password)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList =
            {
                "add-generic-password", "-U",
                "-a", profileId.ToString("N"), "-s", Service, "-w", password
            },
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.WaitForExitAsync();
    }

    public async Task DeletePasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList = { "delete-generic-password", "-a", profileId.ToString("N"), "-s", Service },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.WaitForExitAsync();
    }
}
