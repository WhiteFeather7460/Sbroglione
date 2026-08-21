using System.Collections.Generic;

namespace FileExplorer.Services;

/// <summary>Catalogo EN delle stringhe UI. Chiavi mirror di <see cref="StringsIt"/>.</summary>
public static class StringsEn
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["Str.Common.Cancel"] = "Cancel",
        ["Str.Common.Ok"] = "OK",
        ["Str.Common.Save"] = "Save",
        ["Str.Common.Delete"] = "Delete",
        ["Str.Common.Name"] = "Name",
        ["Str.Common.Size"] = "Size",
        ["Str.Common.Modified"] = "Last modified",
        ["Str.Common.Ready"] = "Ready",
        ["Str.Common.Cancelled"] = "Cancelled",
        ["Str.Common.ErrorFormat"] = "Error: {0}",
        ["Str.Common.Browse"] = "Browse",
        ["Str.Common.Analyze"] = "Analyze",
        ["Str.Common.Analyzing"] = "Analyzing…",
        ["Str.Common.SelectValidFolder"] = "Select a valid folder",
        ["Str.Common.AnalyzeFolderWatermark"] = "Folder to analyze…",
    };
}
