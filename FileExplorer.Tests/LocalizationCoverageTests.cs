using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public class LocalizationCoverageTests
{
    private static readonly Regex KeyPattern = new(@"\{DynamicResource (Str\.[A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    private static string ViewsDirectory
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileExplorer.sln")))
                dir = dir.Parent;
            if (dir is null)
                throw new InvalidOperationException("FileExplorer.sln non trovato risalendo da " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "FileExplorer", "Views");
        }
    }

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
}
