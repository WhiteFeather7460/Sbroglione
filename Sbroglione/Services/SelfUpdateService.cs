using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Applica un aggiornamento sostituendo in-place l'eseguibile self-contained corrente
/// (nessun helper esterno: la pipeline pubblica già un singolo file per piattaforma).
/// Nessun DI container nel progetto: tutte le dipendenze esterne (HTTP, filesystem, avvio
/// processo, uscita) sono campi statici overridabili nei test, stesso pattern di
/// <see cref="UiDispatch.Override"/>.
/// </summary>
public static class SelfUpdateService
{
    /// <summary>Client HTTP per il download dell'asset; sovrascrivibile nei test.</summary>
    public static HttpClient Client { get; set; } = new();

    /// <summary>Se impostato, sostituisce <see cref="Environment.ProcessPath"/> (seam di test).</summary>
    public static string? CurrentExecutablePathOverride { get; set; }

    /// <summary>Avvia un nuovo processo dal path indicato. Sovrascrivibile nei test.</summary>
    public static Action<string> LaunchProcess { get; set; } =
        path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Apre un URL nel browser di sistema. Sovrascrivibile nei test.</summary>
    public static Action<string> OpenUrl { get; set; } =
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>Termina il processo corrente. Sovrascrivibile nei test.</summary>
    public static Action ExitProcess { get; set; } = () => Environment.Exit(0);

    /// <summary>Sposta/sovrascrive un file. Sovrascrivibile nei test per simulare un fallimento nel replace.</summary>
    public static Action<string, string> MoveFileOverwrite { get; set; } =
        (source, destination) => File.Move(source, destination, overwrite: true);

    private static string CurrentExecutablePath =>
        CurrentExecutablePathOverride
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Environment.ProcessPath non disponibile.");

    /// <summary>
    /// Applica l'update: se non c'è un asset per la piattaforma corrente apre solo la pagina
    /// della release (nessun self-replace) e ritorna false. Altrimenti scarica, sostituisce
    /// l'eseguibile corrente, lo rilancia e termina il processo corrente — non ritorna mai in
    /// quel caso (a meno di override di test su <see cref="ExitProcess"/>).
    /// </summary>
    public static async Task<bool> ApplyUpdateAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        RequireHttps(info.ReleaseUrl);

        if (info.AssetDownloadUrl is null)
        {
            OpenUrl(info.ReleaseUrl);
            return false;
        }

        RequireHttps(info.AssetDownloadUrl);

        string currentExePath = CurrentExecutablePath;
        string downloadedPath = currentExePath + ".download";
        string backupPath = currentExePath + ".old";

        await DownloadAsync(info.AssetDownloadUrl, downloadedPath, progress, ct).ConfigureAwait(false);

        MoveFileOverwrite(currentExePath, backupPath);
        try
        {
            MoveFileOverwrite(downloadedPath, currentExePath);
            MakeExecutableIfLinux(currentExePath);
        }
        catch
        {
            MoveFileOverwrite(backupPath, currentExePath);
            throw;
        }

        LaunchProcess(currentExePath);
        ExitProcess();
        return true;
    }

    /// <summary>Rimuove un file .old orfano lasciato da un update precedente. Best-effort: nessuna eccezione propagata.</summary>
    public static void CleanupOrphanBackup()
    {
        try
        {
            string backupPath = CurrentExecutablePath + ".old";
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        catch
        {
            // Best effort: un .old orfano non rimosso non impedisce l'avvio dell'app.
        }
    }

    private static async Task DownloadAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using FileStream destination = File.Create(destinationPath);

        byte[] buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            readTotal += read;
            if (totalBytes is > 0)
                progress?.Report((double)readTotal / totalBytes.Value);
        }

        if (totalBytes is null or 0)
            progress?.Report(1.0);
    }

    /// <summary>Rifiuta URL non-HTTPS: nessun contenuto scaricato o aperto via shell da uno schema diverso (es. javascript:/file:).</summary>
    private static void RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"URL non sicuro rifiutato (richiesto HTTPS): {url}");
    }

    private static void MakeExecutableIfLinux(string path)
    {
        if (!OperatingSystem.IsLinux())
            return;

        UnixFileMode mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
