using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public class LocalizationCoverageTests
{
    private static readonly Regex KeyPattern = new(@"\{DynamicResource (Str\.[A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Cattura la stringa letterale passata a <c>LocalizationService.Tr("Str....")</c>: solo
    /// chiamate con un letterale, non con una variabile/espressione (che comunque non potrebbe
    /// essere verificata staticamente).
    /// </summary>
    private static readonly Regex TrCallPattern = new(@"LocalizationService\.Tr\(\s*""(Str\.[A-Za-z0-9_.]+)""", RegexOptions.Compiled);

    private static string ProjectRoot
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileExplorer.sln")))
                dir = dir.Parent;
            if (dir is null)
                throw new InvalidOperationException("FileExplorer.sln non trovato risalendo da " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "FileExplorer");
        }
    }

    private static string ViewsDirectory => Path.Combine(ProjectRoot, "Views");

    [Fact]
    public void Every_DynamicResource_Str_key_used_in_views_exists_in_both_catalogs()
    {
        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(ViewsDirectory, "*.axaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match match in KeyPattern.Matches(text))
            {
                string key = match.Groups[1].Value;
                if (!StringsIt.All.ContainsKey(key) || !StringsEn.All.ContainsKey(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.True(missing.Count == 0, "Chiavi Str.* usate in axaml ma assenti dal catalogo:\n" + string.Join('\n', missing));
    }

    /// <summary>
    /// Lato C#: scansiona ViewModels/*.cs, Views/*.axaml.cs e Services/*.cs (ricorsivamente,
    /// per coprire Services/Localization) alla ricerca di ogni <c>LocalizationService.Tr("Str....")</c>
    /// e verifica che la chiave esista in entrambi i cataloghi. Copre il gap lasciato dal test
    /// sopra (solo axaml): stringhe hardcoded in C# o chiavi Tr() sbagliate non emergerebbero
    /// altrimenti finché qualcuno non prova manualmente l'app in inglese.
    /// </summary>
    [Fact]
    public void Every_LocalizationService_Tr_key_used_in_code_exists_in_both_catalogs()
    {
        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(Path.Combine(ProjectRoot, "ViewModels"), "*.cs", SearchOption.AllDirectories));
        files.AddRange(Directory.EnumerateFiles(ViewsDirectory, "*.axaml.cs", SearchOption.AllDirectories));
        files.AddRange(Directory.EnumerateFiles(Path.Combine(ProjectRoot, "Services"), "*.cs", SearchOption.AllDirectories));

        var missing = new List<string>();
        var keysFound = 0;
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (Match match in TrCallPattern.Matches(text))
            {
                keysFound++;
                string key = match.Groups[1].Value;
                if (!StringsIt.All.ContainsKey(key) || !StringsEn.All.ContainsKey(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        // Rete di sicurezza: se il pattern smettesse di matchare (es. Tr() rinominato) il test
        // passerebbe vuoto senza dirlo — un falso positivo silenzioso peggiore di un fallimento.
        Assert.True(keysFound > 0, "Nessuna chiamata a LocalizationService.Tr(\"Str....\") trovata: il pattern di scansione è probabilmente rotto.");
        Assert.True(missing.Count == 0, "Chiavi Str.* usate in LocalizationService.Tr(...) ma assenti dal catalogo:\n" + string.Join('\n', missing));
    }
}
