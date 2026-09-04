using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>
/// Host desktop dell'editor profilo. Il corpo vive in <see cref="ProfileEditorContent"/>,
/// condiviso con l'host overlay single-view.
/// </summary>
public partial class ProfileEditorWindow : Window
{
    // Costruttore senza parametri richiesto dal designer Avalonia.
    public ProfileEditorWindow()
        : this(new ProfileEditorViewModel(new Models.ConnectionProfile(), new Services.NullCredentialStore()))
    {
    }

    public ProfileEditorWindow(ProfileEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        DialogContent.Completed += result => Close(result);
    }
}
