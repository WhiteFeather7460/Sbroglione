// Sbroglione/Services/DeltaCopyService.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sbroglione.Services;

/// <summary>
/// Orchestratore del delta-copy: se la destinazione esiste ed è abbastanza grande, calcola
/// la signature, scansiona la sorgente e applica il risultato. Ritorna <c>false</c> (nessuna
/// modifica su disco) in ogni caso in cui conviene lasciare al chiamante la copia piena:
/// destinazione assente, source e dest sullo stesso path, sorgente più piccola di un blocco,
/// o un qualunque errore durante signature/scan/apply. <see cref="DeltaCopyApplier"/> garantisce
/// che la destinazione reale non venga mai toccata finché il rename atomico finale non succede
/// (scrive su un file temporaneo), quindi un fallimento in un punto qualsiasi della pipeline è
/// sempre sicuro e indistinguibile, per il chiamante, da "delta non tentato" — la copia piena di
/// ripiego resta sempre corretta. L'unica eccezione che si propaga è
/// <see cref="OperationCanceledException"/> (una cancellazione non deve silenziosamente
/// trasformarsi in una copia piena). <c>onBytesCopied</c> può aver già riportato byte parziali
/// prima di un ritorno <c>false</c>: un'eventuale copia piena di ripiego li conterà di nuovo.
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

        try
        {
            var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes, ct).ConfigureAwait(false);
            var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes, ct).ConfigureAwait(false);
            await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, onBytesCopied, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DeltaCopyApplier garantisce che la destinazione reale non venga toccata finché il
            // rename atomico finale non succede (scrive su file temporaneo), quindi qualunque
            // fallimento qui è sicuro e indistinguibile, per il chiamante, da "delta non tentato":
            // la copia piena di ripiego resta corretta. onBytesCopied potrebbe già aver riportato
            // byte parziali prima del fallimento; un'eventuale copia piena di ripiego li conterà
            // di nuovo nel progresso.
            return false;
        }

        File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
        return true;
    }
}
