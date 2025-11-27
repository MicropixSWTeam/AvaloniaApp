using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Presentation.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsBox.Avalonia.Enums;
using System.Threading;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.ViewModels
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
