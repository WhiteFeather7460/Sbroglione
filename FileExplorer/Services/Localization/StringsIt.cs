using System.Collections.Generic;

namespace FileExplorer.Services;

/// <summary>
/// Catalogo IT delle stringhe UI — lingua di fallback. L'insieme delle chiavi deve
/// combaciare esattamente con <see cref="StringsEn"/> (verificato da
/// LocalizationServiceTests.StringsEn_has_same_keys_as_StringsIt).
/// </summary>
public static class StringsIt
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["Str.Common.Cancel"] = "Annulla",
        ["Str.Common.Ok"] = "OK",
        ["Str.Common.Save"] = "Salva",
        ["Str.Common.Delete"] = "Elimina",
        ["Str.Common.Name"] = "Nome",
        ["Str.Common.Size"] = "Dimensione",
        ["Str.Common.Modified"] = "Ultima modifica",
        ["Str.Common.Ready"] = "Pronto",
        ["Str.Common.Cancelled"] = "Annullato",
        ["Str.Common.ErrorFormat"] = "Errore: {0}",
        ["Str.Common.Browse"] = "Sfoglia",
        ["Str.Common.Analyze"] = "Analizza",
        ["Str.Common.Analyzing"] = "Analisi…",
        ["Str.Common.SelectValidFolder"] = "Selezionare una cartella valida",
        ["Str.Common.AnalyzeFolderWatermark"] = "Cartella da analizzare…",
        ["Str.InputDialog.Watermark"] = "Nome…",
        ["Str.PathPicker.Go"] = "Vai",
        ["Str.PathPicker.Select"] = "Seleziona",
        ["Str.SelectPathDialog.Title"] = "Seleziona file o cartella",
        ["Str.SelectPathDialog.UncNotSupported"] = "Percorso UNC non supportato su questo sistema: montare la condivisione di rete e usare il punto di mount.",
        ["Str.Settings.Language"] = "Lingua",
        ["Str.Settings.LanguageItalian"] = "Italiano",
        ["Str.Settings.LanguageEnglish"] = "English",
        ["Str.Nav.Copy"] = "Copia",
        ["Str.Nav.Remote"] = "Server remoto",
        ["Str.Nav.Compare"] = "Confronto",
        ["Str.Nav.WatchSync"] = "Sync auto",
        ["Str.Nav.Duplicates"] = "Duplicati",
        ["Str.Nav.DiskUsage"] = "Spazio disco",
        ["Str.Nav.Settings"] = "Impostazioni",
        ["Str.Nav.ToggleMenu"] = "Espandi/comprimi menu",
    };
}
