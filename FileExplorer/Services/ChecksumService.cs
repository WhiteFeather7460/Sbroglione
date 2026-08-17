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

    /// <summary>
    /// Calcola il checksum SHA-256 dei primi <paramref name="maxBytes"/> byte del file
    /// (dell'intero file se più corto). Usato come pre-filtro veloce nella ricerca duplicati.
    /// </summary>
    public static async Task<string> ComputeSha256Async(string path, long maxBytes, CancellationToken ct = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long remaining = maxBytes;
        int read;
        while (remaining > 0
               && (read = await stream.ReadAsync(
                   buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct)) > 0)
        {
            sha256.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }
}
