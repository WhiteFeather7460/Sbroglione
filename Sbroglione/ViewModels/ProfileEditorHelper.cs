using System.Threading.Tasks;

using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>
/// Apertura dell'editor profilo, condivisa tra desktop e Android tramite
/// <see cref="DialogPresenter"/>.
/// </summary>
internal static class ProfileEditorHelper
{
    public static async Task<bool> ShowAsync(ConnectionProfile profile, ICredentialStore credentialStore)
    {
        var viewModel = new ProfileEditorViewModel(profile, credentialStore);

        return await DialogPresenter.ShowAsync<ProfileEditorContent, bool>(
            () => new ProfileEditorWindow(viewModel),
            () => new ProfileEditorContent(),
            viewModel);
    }
}
