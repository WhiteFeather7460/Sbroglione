namespace Sbroglione.Models;

/// <summary>Modalità del filtro per estensione applicato durante la copia di una cartella.</summary>
public enum ExtensionFilterMode
{
    /// <summary>Nessun filtro: copia tutti i file.</summary>
    None,

    /// <summary>Copia solo i file con estensione nell'elenco.</summary>
    Whitelist,

    /// <summary>Copia tutti i file tranne quelli con estensione nell'elenco.</summary>
    Blacklist
}
