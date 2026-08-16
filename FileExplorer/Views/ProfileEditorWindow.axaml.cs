using Avalonia.Controls;
using Avalonia.Interactivity;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>
/// Finestra di modifica profilo. Chiude con true se il profilo è stato salvato.
/// </summary>
public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel _viewModel;

    // Costruttore senza parametri richiesto dal designer Avalonia.
    public ProfileEditorWindow()
        : this(new ProfileEditorViewModel(new Models.ConnectionProfile(), new Services.NullCredentialStore()))
    {
    }

    public ProfileEditorWindow(ProfileEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate())
            return;

        // Salvataggio password fallito: la finestra resta aperta con il messaggio d'errore,
        // così l'utente può riprovare o rinunciare a salvare la password.
        if (!await _viewModel.SaveAsync())
            return;

        Close(true);
    }
}
