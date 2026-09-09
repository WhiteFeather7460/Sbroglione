// Sbroglione/Services/DeltaCopyService.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sbroglione.Services;

/// <summary>
/// Orchestratore del delta-copy: se la destinazione esiste ed è abbastanza grande, calcola
/// la signature, scansiona la sorgente e applica il risultato. Ritorna <c>false</c> (nessuna
/// modifica su disco) in tutti i casi in cui conviene lasciare al chiamante la copia piena:
/// destinazione assente, source e dest sullo stesso path, sorgente più piccola di un blocco,
/// o un errore di lettura/accesso durante il calcolo della signature della destinazione
/// esistente (quest'ultimo caso non ha ancora toccato il disco, quindi il fallback a copia
/// piena è sicuro).
///
/// Da <see cref="DeltaCopyScanner.ScanAsync"/> e <see cref="DeltaCopyApplier.ApplyAsync"/> in
/// poi, invece, le eccezioni (incluso <see cref="OperationCanceledException"/> e la
/// <see cref="IOException"/> sollevata da <see cref="DeltaCopyApplier"/> su una lettura a
/// blocchi troncata) si propagano al chiamante senza essere intercettate: a quel punto la
/// signature è stata costruita sul file esistente e uno scan/apply che fallisce indica che la
/// destinazione è cambiata concorrentemente sotto i piedi dell'operazione, non un caso in cui
/// una copia piena "di ripiego" sarebbe sicura o corretta — <see cref="DeltaCopyApplier"/>
/// garantisce comunque che la destinazione reale non venga toccata (scrive su un file
/// temporaneo, mai troncata/sovrascritta finché il rename atomico finale non succede), quindi
/// propagare non rischia di corrompere nulla: semplicemente niente viene copiato e il
/// chiamante (Task 6) deve intercettare l'eccezione se vuole comunque tentare una copia piena.
/// In sintesi: <c>false</c> = "nessun tentativo di delta, fai la copia piena tu"; eccezione =
/// "il delta è stato tentato ed è fallito, decidi tu se e come ritentare".
/// </summary>
public static class DeltaCopyService
{
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

        int blockSizeBytes = Math.Max(1, AppSettingsStore.Current.DeltaBlockSizeKB) * 1024;

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

        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes, ct).ConfigureAwait(false);
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, onBytesCopied, ct).ConfigureAwait(false);

        File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
        return true;
    }
}
