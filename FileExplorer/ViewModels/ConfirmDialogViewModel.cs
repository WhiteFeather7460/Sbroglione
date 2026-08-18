namespace FileExplorer.ViewModels;

/// <summary>Contenuto di un dialog di conferma: titolo, messaggio e label del bottone di conferma.</summary>
public class ConfirmDialogViewModel
{
    public ConfirmDialogViewModel(string title, string message, string confirmLabel)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
}
