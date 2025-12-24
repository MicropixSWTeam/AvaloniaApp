using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Infrastructure.Service;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class InputDialogViewModel : ViewModelBase, IDialogRequestClose
    {
        public event EventHandler<DialogResultEventArgs>? CloseRequested;

        [ObservableProperty] private string _title = "입력";
        [ObservableProperty] private string _message = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private string _inputText = "";

        public InputDialogViewModel(AppService service) : base(service) { }

        public void Init(string title, string message, string defaultText = "")
        {
            Title = title;
            Message = message;
            InputText = defaultText;
        }
        private bool CanConfirm() => !string.IsNullOrWhiteSpace(InputText);

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseRequested?.Invoke(this, new DialogResultEventArgs(InputText));

        [RelayCommand]
        private void Cancel() => CloseRequested?.Invoke(this, new DialogResultEventArgs(null));
    }
}
