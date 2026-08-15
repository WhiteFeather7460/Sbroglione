using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Keyring Linux via CLI 'secret-tool' (libsecret: GNOME Keyring, KWallet con bridge).
/// La password passa esclusivamente via stdin, mai come argomento.
/// </summary>
public sealed class SecretToolCredentialStore : ICredentialStore
{
    private const string Service = "FileExplorer";

    public bool IsAvailable { get; } = ProbeSecretTool();

    private static bool ProbeSecretTool()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                ArgumentList = { "lookup", "service", "FileExplorer", "probe", "probe" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            process!.WaitForExit(3000);
            // Exit code 1 = "non trovato" ma il tool e il keyring funzionano.
            return process.HasExited && process.ExitCode is 0 or 1;
        }
        catch (Exception)
        {
            return false; // secret-tool assente o keyring non attivo.
        }
    }

    public async Task<string?> GetPasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList = { "lookup", "service", Service, "profile", profileId.ToString("N") },
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
            FileName = "secret-tool",
            ArgumentList =
            {
                "store", "--label", $"FileExplorer {profileId:N}",
                "service", Service, "profile", profileId.ToString("N")
            },
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.StandardInput.WriteAsync(password);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
    }

    public async Task DeletePasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList = { "clear", "service", Service, "profile", profileId.ToString("N") },
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.WaitForExitAsync();
    }
}
