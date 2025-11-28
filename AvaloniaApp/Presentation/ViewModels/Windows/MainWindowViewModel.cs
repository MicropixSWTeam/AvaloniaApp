using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Windows
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly PopupService _popupService;
        private readonly CameraViewModel _cameraViewModel;

        public MainWindowViewModel(DialogService? dialogService, PopupService popupService,IUiDispatcher uiDispatcher, IBackgroundJobQueue backgroundJobQueue,
                                    CameraViewModel cameraViewModel)
            :base(dialogService,uiDispatcher,backgroundJobQueue)
        {
            _popupService = popupService;
            _cameraViewModel = cameraViewModel;
        }


        [RelayCommand]
        public async Task OpenCameraConnectViewAsync()
        {
            await _popupService.ShowModelessAsync(ViewType.Camera,_cameraViewModel);
        }
    }
}
