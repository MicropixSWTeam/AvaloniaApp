using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Infrastructure.Service;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class LoadDialogViewModel : ViewModelBase, IDialogRequestClose
    {
        private readonly StorageService _storageService;
        public event EventHandler<DialogResultEventArgs>? CloseRequested;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
        private string? _selectedFolder;

        public ObservableCollection<string> FolderList { get; } = new();

        public LoadDialogViewModel(StorageService storageService, AppService service) : base(service)
        {
            _storageService = storageService;
            LoadFolders();
        }

        private void LoadFolders()
        {
            FolderList.Clear();
            foreach (var f in _storageService.GetSavedFolders()) FolderList.Add(f);
        }

        private bool CanLoad() => !string.IsNullOrEmpty(SelectedFolder);

        [RelayCommand(CanExecute = nameof(CanLoad))]
        private void Load() => CloseRequested?.Invoke(this, new DialogResultEventArgs(SelectedFolder));

        [RelayCommand]
        private void Cancel() => CloseRequested?.Invoke(this, new DialogResultEventArgs(null));
    }
}