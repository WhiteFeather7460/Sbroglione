// Sbroglione/Services/DeltaCopyService.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sbroglione.Services;

/// <summary>
/// Orchestratore del delta-copy: se la destinazione esiste ed è abbastanza grande, calcola
/// la signature, scansiona la sorgente e applica il risultato. Ritorna <c>false</c> (nessuna
/// modifica su disco) SOLO nei casi pre-flight in cui conviene lasciare al chiamante la copia
/// piena, prima che qualunque contenuto reale della destinazione venga toccato: destinazione
/// assente, source e dest sullo stesso path, sorgente più piccola di un blocco, o un errore di
/// lettura/accesso (<see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>) durante
/// il calcolo della signature della destinazione esistente.
///
/// Da <see cref="DeltaCopyScanner.ScanAsync"/> e <see cref="DeltaCopyApplier.ApplyAsync"/> in
/// poi, invece, le eccezioni si propagano al chiamante senza essere intercettate: a quel punto
/// la signature è stata costruita sul contenuto reale della destinazione, quindi un fallimento
/// lì (es. la destinazione è cambiata concorrentemente, causando una lettura a blocchi troncata
/// in <see cref="DeltaCopyApplier"/>) indica un problema genuino, non un caso "delta non
/// tentato". <see cref="DeltaCopyApplier"/> garantisce comunque che la destinazione reale non
/// venga mai corrotta (scrive su un file temporaneo, mai sovrascritta finché il rename atomico
/// finale non succede), quindi propagare è sicuro: il chiamante (Task 6) deve intercettare
/// l'eccezione se vuole comunque tentare una copia piena di ripiego, invece di farlo
/// silenziosamente su un file la cui destinazione è cambiata per un motivo sconosciuto.
/// In sintesi: <c>false</c> = "nessun tentativo di delta, fai la copia piena tu"; eccezione =
/// "il delta è stato tentato ed è fallito dopo aver letto la destinazione reale, decidi tu se e
/// come ritentare". <c>onBytesCopied</c> può aver già riportato byte parziali prima di
/// un'eccezione: un'eventuale copia piena di ripiego li conterà di nuovo.
/// </summary>
public static class DeltaCopyService
{
    /// <summary>Limiti difensivi su DeltaBlockSizeKB, indipendenti dal clamp di AppSettingsStore
    /// (Current può essere impostato direttamente, es. nei test, bypassando quel clamp).</summary>
    private const int MinDeltaBlockSizeKB = 1;
    private const int MaxDeltaBlockSizeKB = 32768;

    public static async Task<bool> TryDeltaCopyAsync(
        string sourcePath, string destPath, Action<long>? onBytesCopied, CancellationToken ct)
    {
        if (!File.Exists(destPath))
            return false;

        StringComparison pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), pathComparison))
            return false;

        int blockSizeKB = Math.Clamp(AppSettingsStore.Current.DeltaBlockSizeKB, MinDeltaBlockSizeKB, MaxDeltaBlockSizeKB);
        int blockSizeBytes = blockSizeKB * 1024;

        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length < blockSizeBytes)
            return false;

        DeltaSignature signature;
        try
        {
            signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        // Da qui in poi la signature è stata costruita sul contenuto reale della destinazione:
        // un fallimento in ScanAsync/ApplyAsync non viene intercettato e si propaga (vedi doc
        // di classe) — solo il pre-flight sopra può risolversi in un `false` silenzioso.
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes, ct).ConfigureAwait(false);
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, onBytesCopied, ct).ConfigureAwait(false);

        File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
        return true;
    }
}
