using Avalonia.Controls;

namespace Sbroglione.PluginContracts;

/// <summary>
/// Contratto pubblico implementato da un plugin che aggiunge una tab a Sbroglione.
/// Nessun accesso a service interni dell'host: per file/cartella il plugin usa il
/// <c>StorageProvider</c> nativo di Avalonia, disponibile su qualunque <see cref="Control"/>.
/// </summary>
public interface ITabPlugin
{
    /// <summary>Identificatore univoco del plugin, deve combaciare con l'<c>Id</c> del manifest.</summary>
    string Id { get; }

    /// <summary>Testo mostrato nell'header della tab.</summary>
    string Header { get; }

    /// <summary>Glifo FontAwesome (es. "fa-solid fa-download"), stesso formato delle tab built-in.</summary>
    string IconGlyph { get; }

    /// <summary>Crea la view della tab; la view crea la propria ViewModel nel costruttore.</summary>
    Control CreateView();

    /// <summary>Chiamato allo shutdown dell'app: il plugin ferma thread/connessioni/risorse aperte.</summary>
    void OnUnload();
}
