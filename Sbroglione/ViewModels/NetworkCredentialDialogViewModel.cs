using ReactiveUI;
using Sbroglione.Services;

namespace Sbroglione.ViewModels;

/// <summary>Contenuto del dialog credenziali di rete: server (sola lettura), utente, password, ricorda.</summary>
public class NetworkCredentialDialogViewModel : ReactiveObject
{
    public NetworkCredentialDialogViewModel(string server)
    {
        Server = server;
    }

    public string Server { get; }

    /// <summary>Messaggio del dialog, con il nome server/condivisione già interpolato.</summary>
    public string Message =>
        string.Format(LocalizationService.Tr("Str.NetworkCredential.MessageFormat"), Server);

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set
        {
            this.RaiseAndSetIfChanged(ref _username, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set
        {
            this.RaiseAndSetIfChanged(ref _password, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    private bool _remember;
    public bool Remember
    {
        get => _remember;
        set => this.RaiseAndSetIfChanged(ref _remember, value);
    }

    /// <summary>True se utente e password sono entrambi non vuoti (abilita Connetti).</summary>
    public bool CanConfirm => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
