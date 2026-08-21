using System;

namespace Sbroglione.Models;

/// <summary>Impostazioni utente persistite su disco: parallelismo copia, buffer, checksum, tema.</summary>
public class AppSettings
{
    public bool AutoParallelism { get; set; } = true;
    public int ManualParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount - 1);
    public int BufferSizeBytes { get; set; } = 1024 * 1024;
    public bool VerifyChecksumAfterCopy { get; set; } = true;

    /// <summary>Limite di banda della copia attivo (toggle rapido nella scheda Copia).</summary>
    public bool ThrottleEnabled { get; set; }

    /// <summary>Limite di banda in MB/s (usato solo se <see cref="ThrottleEnabled"/>).</summary>
    public int ThrottleMBps { get; set; } = 50;

    /// <summary>"Default" (segue il sistema), "Light" o "Dark".</summary>
    public string ThemeVariant { get; set; } = "Default";

    /// <summary>Id del tema custom attivo (file in AppData/themes); null = usa ThemeVariant.</summary>
    public string? CustomThemeId { get; set; }

    /// <summary>Lingua dell'interfaccia: "it" o "en".</summary>
    public string Language { get; set; } = "en";

    /// <summary>Pannello di navigazione laterale espanso (icone + etichette) o collassato (solo icone).</summary>
    public bool NavExpanded { get; set; } = true;
}
