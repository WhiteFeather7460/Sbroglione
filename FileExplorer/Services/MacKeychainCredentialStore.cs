using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Keyring macOS via CLI 'security' (Keychain). Limite noto: 'add-generic-password -w'
/// espone brevemente la password nella process list; macOS è best-effort in questo progetto.
/// </summary>
public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const string Service = "FileExplorer";

    /// <summary>
    /// Timeout delle singole operazioni: un Keychain bloccato o in attesa di sblocco
    /// non deve appendere il chiamante a tempo indefinito.
    /// </summary>
    private const int OperationTimeoutMs = 10_000;

    /// <summary>Timeout della sonda di disponibilità eseguita alla creazione del backend.</summary>
    private const int ProbeTimeoutMs = 3_000;

    public bool IsAvailable { get; } = ProbeSecurityCli();

    /// <summary>
    /// Verifica non interattiva: 'security help' stampa solo l'uso e non accede al Keychain.
    /// </summary>
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
            if (process is null)
                return false;

            // Le pipe vanno drenate mentre si attende: un output voluminoso riempirebbe
            // il buffer del sistema operativo e bloccherebbe il processo figlio.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(ProbeTimeoutMs))
            {
                KillProcessTree(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string?> GetPasswordAsync(Guid profileId)
    {
        var result = await RunAsync(new ProcessStartInfo
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

        // Timeout o exit code non nullo: nessuna password disponibile per questa chiamata.
        return result.Completed && result.ExitCode == 0 ? result.StandardOutput.TrimEnd('\n') : null;
    }

    public async Task SetPasswordAsync(Guid profileId, string password)
    {
        // Limite noto e accettato: '-w <password>' rende la password visibile nella process list.
        _ = await RunAsync(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList =
            {
                "add-generic-password", "-U",
                "-a", profileId.ToString("N"), "-s", Service, "-w", password
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
    }

    public async Task DeletePasswordAsync(Guid profileId)
    {
        _ = await RunAsync(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList = { "delete-generic-password", "-a", profileId.ToString("N"), "-s", Service },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
    }

    /// <summary>
    /// Avvia 'security' drenando stdout e stderr e applicando <see cref="OperationTimeoutMs"/>.
    /// Allo scadere del timeout l'albero di processi viene terminato e la chiamata risulta
    /// non completata, così il backend si comporta come "non disponibile per questa operazione".
    /// </summary>
    /// <param name="startInfo">Configurazione del processo: stdout e stderr devono essere rediretti.</param>
    private static async Task<(bool Completed, int ExitCode, string StandardOutput)> RunAsync(
        ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
            return (false, -1, string.Empty);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(OperationTimeoutMs));

        // Le letture partono prima dell'attesa per evitare il deadlock delle pipe.
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string output = await standardOutput;
            await standardError;
            return (true, process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            return (false, -1, string.Empty);
        }
    }

    /// <summary>Termina il processo e i suoi figli, ignorando gli errori se è già uscito.</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Processo già terminato o non terminabile: non c'è altro da fare.
        }
    }
}
