using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Calcolo dei checksum usati per verificare le copie.
/// </summary>
public static class ChecksumService
{
    /// <summary>
    /// Calcola il checksum SHA-256 del file indicato (esadecimale minuscolo).
    /// </summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();

        byte[] hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
