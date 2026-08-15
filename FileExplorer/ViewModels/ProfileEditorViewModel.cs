using System.Globalization;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Editor di un profilo di connessione. Applica i campi solo al SaveAsync.</summary>
public class ProfileEditorViewModel : ViewModelBase
{
    private readonly ConnectionProfile _profile;
    private readonly ICredentialStore _credentialStore;

    private string _name;
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    private string _host;
    public string Host
    {
        get => _host;
        set => this.RaiseAndSetIfChanged(ref _host, value);
    }

    private string _portText;
    public string PortText
    {
        get => _portText;
        set => this.RaiseAndSetIfChanged(ref _portText, value);
    }

    private string _username;
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private RemoteProtocol _protocol;
    public RemoteProtocol Protocol
    {
        get => _protocol;
        set
        {
            var previous = _protocol;
            this.RaiseAndSetIfChanged(ref _protocol, value);
            if (previous != value && PortText == DefaultPort(previous).ToString(CultureInfo.InvariantCulture))
                PortText = DefaultPort(value).ToString(CultureInfo.InvariantCulture);
            this.RaisePropertyChanged(nameof(ShowFtpWarning));
        }
    }

    private string? _password;

    /// <summary>Vuoto o null = lascia invariata la password salvata.</summary>
    public string? Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public bool CanSavePassword => _credentialStore.IsAvailable;

    /// <summary>FTP semplice trasmette le credenziali in chiaro.</summary>
    public bool ShowFtpWarning => Protocol == RemoteProtocol.Ftp;

    private string? _validationError;
    public string? ValidationError
    {
        get => _validationError;
        private set => this.RaiseAndSetIfChanged(ref _validationError, value);
    }

    public ProfileEditorViewModel(ConnectionProfile profile, ICredentialStore credentialStore)
    {
        _profile = profile;
        _credentialStore = credentialStore;
        _name = profile.Name;
        _host = profile.Host;
        _portText = profile.Port.ToString(CultureInfo.InvariantCulture);
        _username = profile.Username;
        _protocol = profile.Protocol;
    }

    private static int DefaultPort(RemoteProtocol protocol) =>
        protocol == RemoteProtocol.Sftp ? 22 : 21;

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Il nome del profilo è obbligatorio.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Host))
        {
            ValidationError = "L'host è obbligatorio.";
            return false;
        }
        if (!int.TryParse(PortText, out int port) || port is < 1 or > 65535)
        {
            ValidationError = "La porta deve essere un numero tra 1 e 65535.";
            return false;
        }
        ValidationError = null;
        return true;
    }

    /// <summary>Applica i campi al profilo e salva l'eventuale nuova password nel keyring.</summary>
    public async Task<ConnectionProfile> SaveAsync()
    {
        _profile.Name = Name.Trim();
        _profile.Host = Host.Trim();
        _profile.Port = int.Parse(PortText, CultureInfo.InvariantCulture);
        _profile.Username = Username.Trim();
        _profile.Protocol = Protocol;

        if (!string.IsNullOrEmpty(Password) && _credentialStore.IsAvailable)
            await _credentialStore.SetPasswordAsync(_profile.Id, Password);

        return _profile;
    }
}
