using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Signature della destinazione esistente: hash debole+forte per ciascun blocco fisso.</summary>
public sealed class DeltaSignature
{
    public DeltaSignature(IReadOnlyDictionary<uint, List<SignatureBlock>> blocksByWeak) => BlocksByWeak = blocksByWeak;

    public IReadOnlyDictionary<uint, List<SignatureBlock>> BlocksByWeak { get; }
}

/// <summary>Calcola la signature a blocchi di un file esistente, per il confronto delta-copy.</summary>
public static class DeltaCopySignatureBuilder
{
    public static async Task<DeltaSignature> BuildAsync(string destPath, int blockSizeBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSizeBytes);

        var blocksByWeak = new Dictionary<uint, List<SignatureBlock>>();

        var stream = new FileStream(destPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[blockSizeBytes];
            long offset = 0;
            int read;
            while ((read = await ReadFullAsync(stream, buffer, blockSizeBytes, ct).ConfigureAwait(false)) > 0)
            {
                var rolling = new RollingChecksum();
                for (int i = 0; i < read; i++)
                    rolling.AddByte(buffer[i]);

                byte[] strong = SHA256.HashData(buffer.AsSpan(0, read));

                if (!blocksByWeak.TryGetValue(rolling.Value, out var list))
                    blocksByWeak[rolling.Value] = list = new List<SignatureBlock>();
                list.Add(new SignatureBlock(offset, read, strong));

                offset += read;
            }
        }

        return new DeltaSignature(blocksByWeak);
    }

    internal static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}
