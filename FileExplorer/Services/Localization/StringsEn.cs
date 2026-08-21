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
        ["Str.InputDialog.Watermark"] = "Name…",
        ["Str.PathPicker.Go"] = "Go",
        ["Str.PathPicker.Select"] = "Select",
        ["Str.SelectPathDialog.Title"] = "Select file or folder",
        ["Str.SelectPathDialog.UncNotSupported"] = "UNC path not supported on this system: mount the network share and use the mount point.",
        ["Str.Settings.Language"] = "Language",
        ["Str.Settings.LanguageItalian"] = "Italiano",
        ["Str.Settings.LanguageEnglish"] = "English",
        ["Str.Nav.Copy"] = "Copy",
        ["Str.Nav.Remote"] = "Remote server",
        ["Str.Nav.Compare"] = "Compare",
        ["Str.Nav.WatchSync"] = "Auto sync",
        ["Str.Nav.Duplicates"] = "Duplicates",
        ["Str.Nav.DiskUsage"] = "Disk usage",
        ["Str.Nav.Settings"] = "Settings",
        ["Str.Nav.ToggleMenu"] = "Expand/collapse menu",
    };
}
