using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza dei temi custom: un file JSON per tema in <see cref="ThemesDirectory"/>,
/// scrittura atomica (tmp + move) e load tollerante, stesso pattern di <see cref="AppSettingsStore"/>.
/// I temi built-in NON passano da qui (generati da <see cref="BuiltInThemes"/>).
/// </summary>
public static class ThemeStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Cartella dei temi. Sovrascrivibile nei test per non toccare l'AppData reale.</summary>
    public static string ThemesDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "themes");

    private static string PathFor(string id) => Path.Combine(ThemesDirectory, id + ".json");

    /// <summary>Carica tutti i temi custom; i file corrotti vengono saltati. Ordinati per nome.</summary>
    public static List<ColorTheme> LoadAll()
    {
        var themes = new List<ColorTheme>();
        if (!Directory.Exists(ThemesDirectory))
            return themes;

        foreach (string file in Directory.EnumerateFiles(ThemesDirectory, "*.json"))
        {
            ColorTheme? theme = ReadFile(file);
            if (theme is not null)
                themes.Add(theme);
        }

        return themes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Carica un singolo tema per Id; null se assente o corrotto.</summary>
    public static ColorTheme? Load(string id) => ReadFile(PathFor(id));

    /// <summary>Salva il tema (sanitizzato) con scrittura atomica, creando la cartella se assente.</summary>
    public static async Task SaveAsync(ColorTheme theme)
    {
        Sanitize(theme);
        Directory.CreateDirectory(ThemesDirectory);

        string path = PathFor(theme.Id);
        string tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, theme, Options).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Elimina il file del tema; nessun errore se già assente.</summary>
    public static void Delete(string id)
    {
        try
        {
            File.Delete(PathFor(id));
        }
        catch (Exception)
        {
            // best effort: un file non eliminabile non deve rompere la UI.
        }
    }

    /// <summary>Esporta il tema come file JSON nel percorso indicato.</summary>
    public static async Task ExportAsync(ColorTheme theme, string path)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, theme, Options).ConfigureAwait(false);
    }

    /// <summary>Importa un tema da file: null se illeggibile. Assegna sempre un nuovo Id.</summary>
    public static ColorTheme? Import(string path)
    {
        ColorTheme? theme = ReadFile(path);
        if (theme is null)
            return null;

        theme.Id = Guid.NewGuid().ToString("N");
        return theme;
    }

    /// <summary>
    /// Normalizza il tema in-place: nome non vuoto, BaseVariant valida, chiavi sconosciute
    /// scartate, hex invalidi e chiavi mancanti sostituiti dal built-in della BaseVariant.
    /// </summary>
    public static void Sanitize(ColorTheme theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name))
            theme.Name = "Tema senza nome";
        if (theme.BaseVariant is not ("Light" or "Dark"))
            theme.BaseVariant = "Light";

        ColorTheme fallback = BuiltInThemes.ForVariant(theme.BaseVariant);
        var clean = new Dictionary<string, string>();
        foreach (string key in ThemeColorKeys.All)
        {
            clean[key] = theme.Colors.TryGetValue(key, out string? hex) && Color.TryParse(hex, out _)
                ? hex
                : fallback.Colors[key];
        }

        theme.Colors = clean;
    }

    private static ColorTheme? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            ColorTheme? theme = JsonSerializer.Deserialize<ColorTheme>(json, Options);
            if (theme is null)
                return null;

            Sanitize(theme);
            return theme;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
