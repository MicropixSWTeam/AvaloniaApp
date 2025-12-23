using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class LoadViewModel : ViewModelBase
    {
        private readonly Action<string?> _closeAction;
        private readonly StorageService _storageService;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
        private string? _selectedFolder;

        public ObservableCollection<string> FolderList { get; } = new();

        public LoadViewModel(StorageService storageService, Action<string?> closeAction) : base(null)
        {
            _storageService = storageService;
            _closeAction = closeAction;
            LoadFolders();
        }

        private void LoadFolders()
        {
            FolderList.Clear();
            var folders = _storageService.GetSavedFolders();
            foreach (var f in folders) FolderList.Add(f);
        }

        private bool CanLoad() => !string.IsNullOrEmpty(SelectedFolder);

        [RelayCommand(CanExecute = nameof(CanLoad))]
        private void Load() => _closeAction?.Invoke(SelectedFolder);

        [RelayCommand]
        private void Cancel() => _closeAction?.Invoke(null);
    }
}