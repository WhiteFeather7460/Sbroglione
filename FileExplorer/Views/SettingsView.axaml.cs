using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>Scheda "Impostazioni": parametri di copia e aspetto.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }
}
