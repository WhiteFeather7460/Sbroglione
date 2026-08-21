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
    };
}
