using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Scorre la sorgente con una finestra scorrevole di <c>blockSizeBytes</c> byte, cercando
/// corrispondenze contro la <see cref="DeltaSignature"/> della destinazione esistente.
/// Su match: emette <see cref="CopyBlockInstruction"/> e salta la finestra in avanti di un
/// blocco intero (lettura fresca, nessun rolling). Su mancata corrispondenza: il primo byte
/// della finestra diventa letterale e la finestra scorre di un byte (rolling O(1)) — questo
/// gestisce anche gli shift da inserimento/rimozione byte che romperebbero un confronto a
/// blocchi allineati.
/// </summary>
public static class DeltaCopyScanner
{
    public static async Task<IReadOnlyList<DeltaCopyInstruction>> ScanAsync(
        string sourcePath, DeltaSignature signature, int blockSizeBytes, CancellationToken ct)
    {
        var instructions = new List<DeltaCopyInstruction>();
        var literalBuffer = new List<byte>();

        var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            var window = new byte[blockSizeBytes];
            int windowLen = await DeltaCopySignatureBuilder.ReadFullAsync(stream, window, blockSizeBytes, ct).ConfigureAwait(false);
            if (windowLen == 0)
                return instructions;

            var rolling = new RollingChecksum();
            for (int i = 0; i < windowLen; i++)
                rolling.AddByte(window[i]);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (TryMatch(window, windowLen, rolling.Value, signature, out var matched))
                {
                    FlushLiteral(literalBuffer, instructions);
                    instructions.Add(new CopyBlockInstruction(matched!.Offset, matched.Length));

                    windowLen = await DeltaCopySignatureBuilder.ReadFullAsync(stream, window, blockSizeBytes, ct).ConfigureAwait(false);
                    if (windowLen == 0)
                        break;

                    rolling.Reset();
                    for (int i = 0; i < windowLen; i++)
                        rolling.AddByte(window[i]);
                    continue;
                }

                literalBuffer.Add(window[0]);
                if (literalBuffer.Count >= blockSizeBytes)
                    FlushLiteral(literalBuffer, instructions);

                int nextByte = stream.ReadByte();
                if (nextByte < 0)
                {
                    for (int i = 1; i < windowLen; i++)
                        literalBuffer.Add(window[i]);
                    break;
                }

                byte outgoing = window[0];
                Array.Copy(window, 1, window, 0, windowLen - 1);
                window[windowLen - 1] = (byte)nextByte;
                rolling.Roll(outgoing, (byte)nextByte);
            }
        }

        FlushLiteral(literalBuffer, instructions);
        return instructions;
    }

    private static bool TryMatch(
        byte[] window, int windowLen, uint weak, DeltaSignature signature, out SignatureBlock? matched)
    {
        matched = null;
        if (!signature.BlocksByWeak.TryGetValue(weak, out var candidates))
            return false;

        byte[]? strong = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Length != windowLen)
                continue;

            strong ??= SHA256.HashData(window.AsSpan(0, windowLen));
            if (strong.AsSpan().SequenceEqual(candidate.StrongHash))
            {
                matched = candidate;
                return true;
            }
        }
        return false;
    }

    private static void FlushLiteral(List<byte> buffer, List<DeltaCopyInstruction> instructions)
    {
        if (buffer.Count == 0)
            return;
        instructions.Add(new LiteralInstruction(buffer.ToArray()));
        buffer.Clear();
    }
}
