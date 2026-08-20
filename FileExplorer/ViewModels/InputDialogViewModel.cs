using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Contenuto di un dialog di input testo: titolo, messaggio e testo modificabile.</summary>
public class InputDialogViewModel : ReactiveObject
{
    private string _text;

    public InputDialogViewModel(string title, string message, string? initialText = null)
    {
        Title = title;
        Message = message;
        _text = initialText ?? string.Empty;
    }

    public string Title { get; }
    public string Message { get; }

    /// <summary>Testo digitato dall'utente.</summary>
    public string Text
    {
        get => _text;
        set
        {
            this.RaiseAndSetIfChanged(ref _text, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    /// <summary>True se il testo non è vuoto (abilita OK e la conferma con Invio).</summary>
    public bool CanConfirm => !string.IsNullOrWhiteSpace(Text);
}
