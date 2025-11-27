using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Window
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel(DialogService? dialogService, IUiDispatcher uiDispatcher, IBackgroundJobQueue backgroundJobQueue)
            :base(dialogService,uiDispatcher,backgroundJobQueue)
        {
        }

        [RelayCommand]
        public async Task TestMsgDialog()
        {
            if(_dialogService is null)
                return;

            var result = await _dialogService.ShowMessageAsync(
                "TestDialogTitle",
                "TestDialogMessage",
                ButtonEnum.YesNo);

            if (result != ButtonResult.Yes)
                return;
        }
        [RelayCommand]
        public async Task TestPopupHostWindow()
        {
            
        }
    }
}
