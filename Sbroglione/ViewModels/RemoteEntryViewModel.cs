using System.Globalization;
using Sbroglione.Models;
using ReactiveUI;

namespace Sbroglione.ViewModels;

/// <summary>Voce remota mostrata nella lista, con stato locale calcolato.</summary>
public class RemoteEntryViewModel : ViewModelBase
{
    public RemoteItem Item { get; }

    public string Name => Item.Name;
    public bool IsDirectory => Item.IsDirectory;
    public string SizeDisplay => Item.IsDirectory ? "" : $"{Item.Size / 1024} KB";
    public string ModifiedDisplay => Item.Modified.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private LocalFileStatus? _localStatus;

    /// <summary>Null per le directory o quando non c'è una destinazione impostata.</summary>
    public LocalFileStatus? LocalStatus
    {
        get => _localStatus;
        set => this.RaiseAndSetIfChanged(ref _localStatus, value);
    }

    public RemoteEntryViewModel(RemoteItem item)
    {
        Item = item;
    }
}
