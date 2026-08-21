using System.Linq;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void StringsEn_has_same_keys_as_StringsIt()
    {
        Assert.Equal(
            StringsIt.All.Keys.OrderBy(k => k),
            StringsEn.All.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Apply_sets_current_language_and_translates()
    {
        LocalizationService.Apply(LocalizationService.English);
        Assert.Equal(LocalizationService.English, LocalizationService.CurrentLanguage);
        Assert.Equal("Cancel", LocalizationService.Tr("Str.Common.Cancel"));

        LocalizationService.Apply(LocalizationService.Italian);
        Assert.Equal(LocalizationService.Italian, LocalizationService.CurrentLanguage);
        Assert.Equal("Annulla", LocalizationService.Tr("Str.Common.Cancel"));
    }

    [Fact]
    public void Apply_unknown_language_falls_back_to_italian()
    {
        LocalizationService.Apply("fr");
        Assert.Equal(LocalizationService.Italian, LocalizationService.CurrentLanguage);
    }

    [Fact]
    public void Tr_missing_key_returns_key_itself()
    {
        Assert.Equal("Str.Does.Not.Exist", LocalizationService.Tr("Str.Does.Not.Exist"));
    }

    [Fact]
    public void Apply_without_application_does_not_throw()
    {
        // nei test Application.Current è null, come in ThemeServiceTests.
        var ex = Record.Exception(() => LocalizationService.Apply(LocalizationService.English));
        Assert.Null(ex);
    }

    [Fact]
    public void LanguageChanged_event_fires_on_apply()
    {
        int count = 0;
        void Handler() => count++;
        LocalizationService.LanguageChanged += Handler;
        try
        {
            LocalizationService.Apply(LocalizationService.English);
            Assert.Equal(1, count);
        }
        finally
        {
            LocalizationService.LanguageChanged -= Handler;
        }
    }
}
