using System.Threading.Tasks;

using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>
/// Apertura dell'editor tema, condivisa tra desktop e Android tramite <see cref="DialogPresenter"/>.
/// </summary>
internal static class ThemeEditorHelper
{
    public static async Task<ColorTheme?> ShowAsync(ThemeEditorViewModel viewModel) =>
        await DialogPresenter.ShowAsync<ThemeEditorContent, ColorTheme?>(
            () => new ThemeEditorWindow(viewModel),
            () => new ThemeEditorContent(),
            viewModel);
}
