using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Keyring Linux via CLI 'secret-tool' (libsecret: GNOME Keyring, KWallet con bridge).
/// La password passa esclusivamente via stdin, mai come argomento.
/// </summary>
public sealed class SecretToolCredentialStore : ICredentialStore
{
    private const string Service = "FileExplorer";

    /// <summary>
    /// Timeout delle singole operazioni: un keyring bloccato o in attesa di sblocco
    /// non deve appendere il chiamante a tempo indefinito.
    /// </summary>
    private const int OperationTimeoutMs = 10_000;

    /// <summary>Timeout della sonda di disponibilità eseguita alla creazione del backend.</summary>
    private const int ProbeTimeoutMs = 3_000;

    public bool IsAvailable { get; } = ProbeSecretTool();

    /// <summary>
    /// Verifica non interattiva: '--help' non tocca la collezione del keyring, quindi non può
    /// far comparire la finestra di sblocco all'avvio. Basta che il tool esista e termini:
    /// l'exit code viene ignorato di proposito perché secret-tool esce con codice diverso da 0
    /// quando stampa l'uso. Un keyring bloccato viene gestito a runtime dal timeout delle
    /// operazioni (lookup va in timeout, restituisce null, l'app chiede la password).
    /// </summary>
    private static bool ProbeSecretTool()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                ArgumentList = { "--help" },
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

            return true;
        }
        catch (Exception)
        {
            return false; // secret-tool assente o non eseguibile.
        }
    }

    public async Task<string?> GetPasswordAsync(Guid profileId)
    {
        var result = await RunAsync(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList = { "lookup", "service", Service, "profile", profileId.ToString("N") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        // Timeout o exit code non nullo: nessuna password disponibile per questa chiamata.
        return result.Completed && result.ExitCode == 0 ? result.StandardOutput.TrimEnd('\n') : null;
    }

    public async Task SetPasswordAsync(Guid profileId, string password)
    {
        // La password viaggia su stdin: non compare mai tra gli argomenti del processo.
        _ = await RunAsync(
            new ProcessStartInfo
            {
                FileName = "secret-tool",
                ArgumentList =
                {
                    "store", "--label", $"FileExplorer {profileId:N}",
                    "service", Service, "profile", profileId.ToString("N")
                },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            },
            password);
    }

    public async Task DeletePasswordAsync(Guid profileId)
    {
        _ = await RunAsync(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList = { "clear", "service", Service, "profile", profileId.ToString("N") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
    }

    /// <summary>
    /// Avvia secret-tool drenando stdout e stderr e applicando <see cref="OperationTimeoutMs"/>.
    /// Allo scadere del timeout l'albero di processi viene terminato e la chiamata risulta
    /// non completata, così il backend si comporta come "non disponibile per questa operazione".
    /// </summary>
    /// <param name="startInfo">Configurazione del processo: stdout e stderr devono essere rediretti.</param>
    /// <param name="stdinPayload">Testo da inviare su stdin (la password), oppure null.</param>
    private static async Task<(bool Completed, int ExitCode, string StandardOutput)> RunAsync(
        ProcessStartInfo startInfo,
        string? stdinPayload = null)
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
            if (stdinPayload is not null)
            {
                await process.StandardInput.WriteAsync(stdinPayload.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }

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
