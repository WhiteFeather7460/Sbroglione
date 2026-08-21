using System;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Decide il grado di parallelismo per la copia di una cartella, in base alle
/// impostazioni utente e al tipo di disco di sorgente/destinazione.
/// </summary>
public static class CopyParallelismResolver
{
    /// <summary>
    /// In automatico: 1 (sequenziale) se sorgente o destinazione sono su HDD, altrimenti
    /// ProcessorCount-1. In manuale: il valore impostato dall'utente (clampato a >= 1).
    /// </summary>
    public static int Resolve(AppSettings settings, DiskType sourceType, DiskType destinationType)
    {
        if (!settings.AutoParallelism)
            return Math.Max(1, settings.ManualParallelism);

        bool eitherHdd = sourceType == DiskType.Hdd || destinationType == DiskType.Hdd;
        return eitherHdd ? 1 : Math.Max(2, Environment.ProcessorCount - 1);
    }
}
