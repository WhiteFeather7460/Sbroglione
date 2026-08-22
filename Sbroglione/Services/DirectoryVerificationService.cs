using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Avanzamento della verifica checksum di una cartella.</summary>
public readonly record struct VerifyProgress(int VerifiedFiles, int TotalFiles);

/// <summary>
/// Esito della verifica: elenchi (in path relativi alla sorgente) dei file
/// con checksum diverso e dei file assenti in destinazione.
/// </summary>
public sealed record DirectoryVerifyResult(
    int TotalFiles,
    IReadOnlyList<string> MismatchedFiles,
    IReadOnlyList<string> MissingFiles)
{
    public bool IsSuccess => MismatchedFiles.Count == 0 && MissingFiles.Count == 0;
}

/// <summary>
/// Verifica post-copia di un albero di cartelle: confronta il checksum SHA-256
/// di ogni file sorgente con l'omologo in destinazione, più file in parallelo.
/// </summary>
public static class DirectoryVerificationService
{
    /// <summary>
    /// Enumerazione tollerante, identica a quella della simulazione: ignora i file
    /// inaccessibili e non segue i reparse point (symlink), evitando i loop.
    /// </summary>
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static async Task<DirectoryVerifyResult> VerifyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<VerifyProgress>? onProgress,
        CancellationToken ct,
        ExtensionFilter? extensionFilter = null)
    {
        List<string> files = await Task.Run(() =>
        {
            var list = new List<string>();
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
            {
                ct.ThrowIfCancellationRequested();
                if (extensionFilter is not null && !extensionFilter.Matches(file))
                    continue;
                list.Add(file);
            }
            return list;
        }, ct).ConfigureAwait(false);

        var mismatched = new ConcurrentBag<string>();
        var missing = new ConcurrentBag<string>();
        int verified = 0;

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                string destinationFile = Path.Combine(destinationRoot, relative);

                if (!File.Exists(destinationFile))
                {
                    missing.Add(relative);
                }
                else
                {
                    string sourceHash = await ChecksumService.ComputeSha256Async(sourceFile, ct).ConfigureAwait(false);
                    string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct).ConfigureAwait(false);
                    if (!string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
                        mismatched.Add(relative);
                }
            }
            finally
            {
                semaphore.Release();
                onProgress?.Invoke(new VerifyProgress(Interlocked.Increment(ref verified), files.Count));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return new DirectoryVerifyResult(
            files.Count,
            mismatched.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            missing.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }
}
