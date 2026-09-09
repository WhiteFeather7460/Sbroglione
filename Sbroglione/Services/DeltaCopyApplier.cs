// Sbroglione/Services/DeltaCopyApplier.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Applica le istruzioni prodotte da <see cref="DeltaCopyScanner"/>: scrive un file
/// temporaneo nella stessa cartella di <paramref name="finalDestPath"/> (letterali dalla
/// sorgente già incorporati nelle istruzioni, blocchi letti dal vecchio <paramref
/// name="oldDestPath"/>), poi lo sostituisce con un <see cref="File.Move"/> atomico.
/// A differenza della copia piena attuale, un crash a metà lascia la destinazione intatta
/// (il file temporaneo viene eliminato, mai la destinazione).
/// </summary>
public static class DeltaCopyApplier
{
    public static async Task ApplyAsync(
        IReadOnlyList<DeltaCopyInstruction> instructions,
        string oldDestPath,
        string finalDestPath,
        Action<long>? onBytesCopied,
        CancellationToken ct)
    {
        string tempPath = finalDestPath + ".sbroglione-delta-tmp";
        try
        {
            var oldDest = new FileStream(oldDestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using (oldDest.ConfigureAwait(false))
            {
                var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using (output.ConfigureAwait(false))
                {
                    foreach (var instruction in instructions)
                    {
                        ct.ThrowIfCancellationRequested();

                        switch (instruction)
                        {
                            case LiteralInstruction literal:
                                await IoThrottleService.WaitAsync(literal.Data.Length, ct).ConfigureAwait(false);
                                await output.WriteAsync(literal.Data, ct).ConfigureAwait(false);
                                onBytesCopied?.Invoke(literal.Data.Length);
                                break;

                            case CopyBlockInstruction block:
                                oldDest.Seek(block.DestOffset, SeekOrigin.Begin);
                                var buffer = new byte[block.Length];
                                int totalRead = 0;
                                while (totalRead < block.Length)
                                {
                                    int read = await oldDest.ReadAsync(
                                        buffer.AsMemory(totalRead, block.Length - totalRead), ct).ConfigureAwait(false);
                                    if (read == 0)
                                    {
                                        throw new IOException(
                                            $"Blocco {block.DestOffset}+{block.Length} non leggibile per intero da " +
                                            $"'{oldDestPath}' (letti {totalRead} byte): la destinazione è cambiata " +
                                            "dopo il calcolo della signature.");
                                    }
                                    totalRead += read;
                                }

                                // CopyBlockInstruction legge dal vecchio dest (I/O locale), non throttlato:
                                // solo i LiteralInstruction (dati nuovi dalla sorgente) passano da WaitAsync.
                                await output.WriteAsync(buffer.AsMemory(0, totalRead), ct).ConfigureAwait(false);
                                onBytesCopied?.Invoke(totalRead);
                                break;
                        }
                    }

                    await output.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            if (!OperatingSystem.IsWindows() && File.Exists(finalDestPath))
            {
                File.SetUnixFileMode(tempPath, File.GetUnixFileMode(finalDestPath));
            }

            File.Move(tempPath, finalDestPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { /* ignora: l'eccezione originale è più informativa */ }
            throw;
        }
    }
}
