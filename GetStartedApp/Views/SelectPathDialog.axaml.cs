using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using GetStartedApp.Utils;
using GetStartedApp.ViewModels;

namespace GetStartedApp.Views {

    public partial class SelectPathDialog : Window
    {
        public SelectPathDialog()
        {
            InitializeComponent();
            //this.DataContext = new SelectPathDialogViewModel();
        }

        /// <summary>
        /// Permette il doppio click
        /// Se è una cartella allora entro dentro
        /// Se è un file lo seleziono
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
        {
            CloseAfterSelectElement(true);
        }

        public void SelectElement_Click(object? sender, RoutedEventArgs e)
        {
            CloseAfterSelectElement();
        }

        private void CloseAfterSelectElement(bool isDoubleTap = false) {
            if (this.DataContext is SelectPathDialogViewModel vm)
            {
                // Se clicco due volte su una cartella allora la apro
                if (isDoubleTap && FileUtils.GetPathType(vm.SelectedItem.FullPath) == PathType.Directory)
                {
                    vm.SelectCommand.Execute(vm.SelectedItem.FullPath).Subscribe();
                    return;
                }

                // Se seleziono con il doppio click o il tasto seleziona allora devo recuperare il path selezionato
                vm.DialogResult = vm.SelectedItem?.FullPath ?? vm.CurrentPath;
                Close(vm.SelectedItem?.FullPath ?? vm.CurrentPath);
            }
        }

        // Quando premo invio attivo la barra del path
        public void PathTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && this.DataContext is SelectPathDialogViewModel vm)
            {
                GoButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        public void GoButton_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is SelectPathDialogViewModel vm) {
                // Se il path esiste allora lo mostro
                if (FileUtils.GetPathType(vm.CurrentPath) != PathType.Unknown)
                {
                    PathTextBar.Background = Brushes.White;

                    vm.SelectCommand.Execute(vm.SelectedItem?.FullPath ?? vm.CurrentPath).Subscribe();

                    e.Handled = true;

                    return;
                }

                PathTextBar.Background = Brushes.Red;
            }
        }

        public void GoBack_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is SelectPathDialogViewModel vm)
            {
                vm.SelectCommand.Execute(FileUtils.GoBackOneLevel(vm.CurrentPath)).Subscribe();
            }
        }

    }

}
