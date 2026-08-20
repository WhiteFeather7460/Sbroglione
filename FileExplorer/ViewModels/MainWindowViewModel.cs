using System;
using System.Reactive;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Stato della shell: pannello di navigazione laterale espanso/collassato (persistito).</summary>
public class MainWindowViewModel : ViewModelBase
{
    private bool _isNavExpanded;

    public MainWindowViewModel()
    {
        _isNavExpanded = AppSettingsStore.Current.NavExpanded;
        ToggleNavCommand = ReactiveCommand.CreateFromTask(ToggleNavAsync);
    }

    public bool IsNavExpanded
    {
        get => _isNavExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isNavExpanded, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleNavCommand { get; }

    internal async Task ToggleNavAsync()
    {
        IsNavExpanded = !IsNavExpanded;
        AppSettingsStore.Current.NavExpanded = IsNavExpanded;
        try
        {
            await AppSettingsStore.SaveCurrentAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: lo stato resta valido in memoria anche se il salvataggio su disco fallisce.
        }
    }
}
