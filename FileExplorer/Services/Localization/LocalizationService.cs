using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace FileExplorer.Services;

/// <summary>
/// Applica la lingua dell'interfaccia a runtime, stesso pattern di <see cref="ThemeService"/>:
/// le stringhe tradotte sono registrate come ResourceDictionary in
/// Application.Resources.MergedDictionaries (chiavi "Str.*"), così ogni
/// {DynamicResource Str.*} nelle view si aggiorna subito al cambio lingua.
/// </summary>
public static class LocalizationService
{
    public const string Italian = "it";
    public const string English = "en";

    private static ResourceDictionary? _activeDictionary;
    private static IReadOnlyDictionary<string, string> _active = StringsEn.All;

    public static string CurrentLanguage { get; private set; } = English;

    /// <summary>
    /// Sollevato dopo un cambio lingua: le viewmodel con testo costruito in C# (non da
    /// DynamicResource, es. liste build una tantum) lo usano per rinfrescarsi.
    /// </summary>
    public static event Action? LanguageChanged;

    /// <summary>Registra il dizionario di stringhe della lingua richiesta e lo attiva.</summary>
    public static void Apply(string language)
    {
        CurrentLanguage = language == English ? English : Italian;
        _active = CurrentLanguage == English ? StringsEn.All : StringsIt.All;

        if (Application.Current is { } app)
        {
            var dict = new ResourceDictionary();
            foreach (KeyValuePair<string, string> kvp in _active)
                dict[kvp.Key] = kvp.Value;

            if (_activeDictionary is { } old)
                app.Resources.MergedDictionaries.Remove(old);
            app.Resources.MergedDictionaries.Add(dict);
            _activeDictionary = dict;
        }

        LanguageChanged?.Invoke();
    }

    /// <summary>Traduce una chiave per l'uso lato C# (status/errori/dialog); chiave mancante ripiega su sé stessa.</summary>
    public static string Tr(string key) => _active.TryGetValue(key, out string? value) ? value : key;
}
