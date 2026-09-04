using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Sbroglione.ViewModels;
using Sbroglione.Views.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Corpo dell'editor profilo, indipendente dall'host: segnala <c>true</c> su salvataggio
/// riuscito, <c>false</c> su annulla — stesso contratto di <see cref="ProfileEditorWindow"/>
/// prima dell'estrazione.
/// </summary>
public partial class ProfileEditorContent : UserControl, IDialogContent<bool>
{
    public ProfileEditorContent()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public event Action<bool>? Completed;

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(false);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileEditorViewModel vm)
            return;

        if (!vm.Validate())
            return;

        // Salvataggio password fallito: il contenuto resta a video con il messaggio
        // d'errore già impostato dalla viewmodel, così l'utente può riprovare o rinunciare.
        if (!await vm.SaveAsync())
            return;

        Completed?.Invoke(true);
    }
}
