using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using Avalonia.Controls.Shapes;
using DynamicData;
using GetStartedApp.Utils;
using ReactiveUI;

namespace GetStartedApp.ViewModels {

    public class SelectPathDialogViewModel : ReactiveObject
    {

        // Proprietà della barra con il path
        private string _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentPath, value);
                //LoadItems();
            }
        }

        public ObservableCollection<FileSystemItem> Items { get; } = new();

        private FileSystemItem? _selectedItem;
        public FileSystemItem? SelectedItem
        {
            get => _selectedItem;
            set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
        }


        //private bool _pathExists = true;
        //public bool PathExists
        //{
        //    get => _pathExists;
        //    set => this.RaiseAndSetIfChanged(ref _pathExists, value);
        //}




        public string? DialogResult { get; set; }
        public bool isDest { get; set; }

        public ReactiveCommand<Unit, Unit> NavigateCommand { get; }
        public ReactiveCommand<string, Unit> SelectCommand { get; }
        public ReactiveCommand<Unit, string?> CancelCommand { get; }

        public SelectPathDialogViewModel(bool isDest, string currentPath)
        {
            NavigateCommand = ReactiveCommand.Create(() => {
                LoadItems(isDest);
            });

            SelectCommand = ReactiveCommand.Create<string>(path =>
            {
                CurrentPath = path;
                LoadItems(isDest);
            });

            CancelCommand = ReactiveCommand.Create(() => DialogResult = null);

            CurrentPath = currentPath;
            LoadItems(isDest);
        }

        private void LoadItems(bool isDest)
        {
            Items.Clear();
            Items.AddRange(FileUtils.PopolaTabellaFS(CurrentPath, isDest));
        }

    }


}