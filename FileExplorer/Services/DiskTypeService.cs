using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Rileva se un percorso risiede su SSD o HDD, per adattare il parallelismo di copia.
/// Non lancia mai eccezioni: qualunque fallimento di rilevamento ritorna <see cref="DiskType.Unknown"/>.
/// </summary>
public static class DiskTypeService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (DiskType Type, DateTime CachedAt)> Cache = new();

    public static async Task<DiskType> GetDiskTypeAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DiskType.Unknown;

        string cacheKey;
        string? linuxMountsContent;
        try
        {
            (cacheKey, linuxMountsContent) = await ResolveCacheKeyAsync(path, ct);
        }
        catch
        {
            cacheKey = FallbackCacheKey(path);
            linuxMountsContent = null;
        }

        if (Cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            return cached.Type;

        DiskType type;
        try
        {
            type = await DetectAsync(path, linuxMountsContent, ct);
        }
        catch
        {
            type = DiskType.Unknown;
        }

        Cache[cacheKey] = (type, DateTime.UtcNow);
        return type;
    }

    /// <summary>
    /// Risolve la chiave di cache per un percorso in base al sistema operativo. Su Windows la
    /// chiave resta la drive letter (già distinta per disco fisico). Su Linux viene risolto il
    /// device montato via /proc/mounts, cosicché percorsi su dischi fisici diversi non collassino
    /// sulla stessa voce di cache (a differenza di Path.GetPathRoot, che ritorna sempre "/").
    /// Ritorna anche il contenuto di /proc/mounts già letto, per evitare una seconda lettura in
    /// caso di cache miss su Linux.
    /// </summary>
    private static async Task<(string CacheKey, string? LinuxMountsContent)> ResolveCacheKeyAsync(string path, CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return (FallbackCacheKey(path), null);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!File.Exists("/proc/mounts"))
                return (FallbackCacheKey(path), null);

            string mountsContent = await File.ReadAllTextAsync("/proc/mounts", ct);
            string fullPath = Path.GetFullPath(path);
            return (ResolveLinuxCacheKey(mountsContent, fullPath), mountsContent);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Nessun modo economico di risalire al device fisico senza shellare di nuovo
            // (come già fa DetectMacAsync). Usiamo il percorso completo come chiave: sacrifica
            // il riuso della cache tra percorsi diversi sullo stesso disco, ma evita che percorsi
            // su dischi fisici diversi collassino sulla stessa voce.
            return (Path.GetFullPath(path), null);
        }

        return (FallbackCacheKey(path), null);
    }

    private static string FallbackCacheKey(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root ? root : path;
        }
        catch
        {
            return path;
        }
    }

    private static Task<DiskType> DetectAsync(string path, string? linuxMountsContent, CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return DetectLinuxAsync(path, linuxMountsContent, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(DetectWindows(path));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectMacAsync(path, ct);

        return Task.FromResult(DiskType.Unknown);
    }

    // ===== Linux =====

    private static async Task<DiskType> DetectLinuxAsync(string path, string? mountsContent, CancellationToken ct)
    {
        string fullPath = Path.GetFullPath(path);

        if (mountsContent is null)
        {
            if (!File.Exists("/proc/mounts"))
                return DiskType.Unknown;

            mountsContent = await File.ReadAllTextAsync("/proc/mounts", ct);
        }

        string? device = ResolveLinuxBlockDevice(mountsContent, fullPath);
        if (device is null)
            return DiskType.Unknown;

        string? diskName = ExtractLinuxDiskName(device);
        if (diskName is null)
            return DiskType.Unknown;

        string rotationalPath = $"/sys/block/{diskName}/queue/rotational";
        if (!File.Exists(rotationalPath))
            return DiskType.Unknown;

        string content = await File.ReadAllTextAsync(rotationalPath, ct);
        return ParseRotationalFlag(content);
    }

    /// <summary>
    /// Risolve la chiave di cache per un percorso Linux dato il contenuto di /proc/mounts già letto:
    /// usa il nome disco (es. "sda") quando riconosciuto da <see cref="ExtractLinuxDiskName"/>,
    /// altrimenti il device grezzo (es. per device mapper/LVM), altrimenti (nessun device
    /// corrispondente, es. mount di rete) ricade su Path.GetPathRoot del percorso.
    /// </summary>
    internal static string ResolveLinuxCacheKey(string mountsContent, string fullPath)
    {
        string? device = ResolveLinuxBlockDevice(mountsContent, fullPath);
        if (device is null)
            return Path.GetPathRoot(fullPath) is { Length: > 0 } root ? root : fullPath;

        string? diskName = ExtractLinuxDiskName(device);
        return diskName ?? device;
    }

    /// <summary>
    /// Trova il device montato con il prefisso più lungo che contiene <paramref name="absolutePath"/>,
    /// leggendo il contenuto di /proc/mounts. Ritorna null se nessun device corrisponde (es. FS di rete).
    /// </summary>
    internal static string? ResolveLinuxBlockDevice(string mountsContent, string absolutePath)
    {
        string? bestDevice = null;
        int bestLength = -1;

        foreach (string line in mountsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
                continue;

            string device = fields[0];
            string mountPoint = fields[1];

            if (!device.StartsWith("/dev/", StringComparison.Ordinal))
                continue;

            bool matches = absolutePath == mountPoint
                || absolutePath.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal)
                || mountPoint == "/";

            if (!matches)
                continue;

            if (mountPoint.Length > bestLength)
            {
                bestLength = mountPoint.Length;
                bestDevice = device;
            }
        }

        return bestDevice;
    }

    /// <summary>
    /// Estrae il nome del disco (per /sys/block) da un device di partizione,
    /// es. "/dev/sda1" -> "sda", "/dev/nvme0n1p1" -> "nvme0n1". Ritorna null per
    /// device non riconosciuti (mapper/LVM, di rete, ecc.).
    /// </summary>
    internal static string? ExtractLinuxDiskName(string device)
    {
        string name = device.StartsWith("/dev/", StringComparison.Ordinal) ? device[5..] : device;

        var nvmeMatch = Regex.Match(name, @"^(nvme\d+n\d+)(p\d+)?$");
        if (nvmeMatch.Success)
            return nvmeMatch.Groups[1].Value;

        var diskMatch = Regex.Match(name, @"^([a-z]+)\d*$");
        if (diskMatch.Success)
            return diskMatch.Groups[1].Value;

        return null;
    }

    /// <summary>Interpreta il contenuto di /sys/block/&lt;disco&gt;/queue/rotational.</summary>
    internal static DiskType ParseRotationalFlag(string content)
    {
        return content.Trim() switch
        {
            "0" => DiskType.Ssd,
            "1" => DiskType.Hdd,
            _ => DiskType.Unknown
        };
    }

    // ===== Windows =====

    private static DiskType DetectWindows(string path)
    {
        string? driveLetter = Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(driveLetter))
            return DiskType.Unknown;

        try
        {
            using var logicalDiskSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");

            foreach (ManagementBaseObject partition in logicalDiskSearcher.Get())
            {
                using var driveSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");

                foreach (ManagementBaseObject drive in driveSearcher.Get())
                {
                    string? index = drive["Index"]?.ToString();
                    if (index is null)
                        continue;

                    using var physicalDiskSearcher = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage",
                        $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{index}'");

                    foreach (ManagementBaseObject physicalDisk in physicalDiskSearcher.Get())
                    {
                        if (physicalDisk["MediaType"] is ushort mediaType)
                            return ParseWindowsMediaType(mediaType);
                    }
                }
            }
        }
        catch
        {
            return DiskType.Unknown;
        }

        return DiskType.Unknown;
    }

    /// <summary>Interpreta MSFT_PhysicalDisk.MediaType (3 = HDD, 4 = SSD).</summary>
    internal static DiskType ParseWindowsMediaType(int mediaType) => mediaType switch
    {
        3 => DiskType.Hdd,
        4 => DiskType.Ssd,
        _ => DiskType.Unknown
    };

    // ===== macOS =====

    private static async Task<DiskType> DetectMacAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("diskutil", $"info \"{Path.GetFullPath(path)}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process is null)
                return DiskType.Unknown;

            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return ParseDiskutilSolidState(output);
        }
        catch
        {
            return DiskType.Unknown;
        }
    }

    /// <summary>Interpreta l'output di "diskutil info" cercando il campo "Solid State".</summary>
    internal static DiskType ParseDiskutilSolidState(string diskutilOutput)
    {
        var match = Regex.Match(diskutilOutput, @"Solid State:\s*(Yes|No)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return DiskType.Unknown;

        return string.Equals(match.Groups[1].Value, "Yes", StringComparison.OrdinalIgnoreCase)
            ? DiskType.Ssd
            : DiskType.Hdd;
    }
}
