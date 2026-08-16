using System;

namespace FileExplorer.Models;

/// <summary>Impostazioni utente persistite su disco: parallelismo copia, buffer, checksum, tema.</summary>
public class AppSettings
{
    public bool AutoParallelism { get; set; } = true;
    public int ManualParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount - 1);
    public int BufferSizeBytes { get; set; } = 1024 * 1024;
    public bool VerifyChecksumAfterCopy { get; set; } = true;

    /// <summary>"Default" (segue il sistema), "Light" o "Dark".</summary>
    public string ThemeVariant { get; set; } = "Default";
}
