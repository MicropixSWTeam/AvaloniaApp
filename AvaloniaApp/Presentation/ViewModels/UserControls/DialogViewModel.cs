using AvaloniaApp.Core.Enums;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public class DialogViewModel : ViewModelBase
    {
        private readonly Action<bool> _closeAction;
        public DialogType Type { get; }
        public string Title { get; }
        public string Message { get; }

        public bool IsCancelVisible => Type == DialogType.Confirm;

        // View에서 바인딩할 헤더 색상
        public string HeaderColor => Type switch
        {
            DialogType.Error => "#D32F2F",   // Red
            DialogType.Complete => "#388E3C",// Green
            DialogType.Confirm => "#1976D2", // Blue
            _ => "Gray"
        };

        public IRelayCommand ConfirmCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public DialogViewModel(DialogType type, string title, string message, Action<bool> closeAction) : base(null)
        {
            Type = type;
            Title = title;
            Message = message;
            _closeAction = closeAction;

            ConfirmCommand = new RelayCommand(() => _closeAction?.Invoke(true));
            CancelCommand = new RelayCommand(() => _closeAction?.Invoke(false));
        }
    }
}
