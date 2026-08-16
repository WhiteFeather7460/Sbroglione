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

        string cacheKey = Path.GetPathRoot(path) is { Length: > 0 } root ? root : path;

        if (Cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            return cached.Type;

        DiskType type;
        try
        {
            type = await DetectAsync(path, ct);
        }
        catch
        {
            type = DiskType.Unknown;
        }

        Cache[cacheKey] = (type, DateTime.UtcNow);
        return type;
    }

    private static Task<DiskType> DetectAsync(string path, CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return DetectLinuxAsync(path, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(DetectWindows(path));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectMacAsync(path, ct);

        return Task.FromResult(DiskType.Unknown);
    }

    // ===== Linux =====

    private static async Task<DiskType> DetectLinuxAsync(string path, CancellationToken ct)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists("/proc/mounts"))
            return DiskType.Unknown;

        string mountsContent = await File.ReadAllTextAsync("/proc/mounts", ct);
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
