using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FileExplorer.Models;

/// <summary>Tema colore nominato, serializzato in JSON (un file per tema in AppData/themes).</summary>
public class ColorTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>"Light" o "Dark": variante ereditata per i controlli nativi e fallback colori.</summary>
    public string BaseVariant { get; set; } = "Light";

    /// <summary>Chiave logica (<see cref="ThemeColorKeys"/>) → colore hex "#RRGGBB"/"#AARRGGBB".</summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>True solo per Chiaro/Scuro generati in codice: non modificabili né eliminabili.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    public ColorTheme Clone() => new()
    {
        Id = Id,
        Name = Name,
        BaseVariant = BaseVariant,
        Colors = new Dictionary<string, string>(Colors),
        IsBuiltIn = IsBuiltIn
    };
}
