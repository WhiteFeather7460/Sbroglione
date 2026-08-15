namespace FileExplorer.Models;

/// <summary>Stato di un file remoto rispetto alla cartella locale di destinazione.</summary>
public enum LocalFileStatus
{
    /// <summary>Non esiste in locale.</summary>
    Missing,

    /// <summary>Esiste con stessa dimensione e stessa data (tolleranza 2 s).</summary>
    Present,

    /// <summary>Esiste ma dimensione o data differiscono.</summary>
    Different
}
