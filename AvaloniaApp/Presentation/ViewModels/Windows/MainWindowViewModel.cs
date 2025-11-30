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
        private readonly ChartViewModel _chartViewModel;

        public MainWindowViewModel(DialogService? dialogService, PopupService popupService,IUiDispatcher uiDispatcher, IBackgroundJobQueue backgroundJobQueue,
                                    CameraViewModel cameraViewModel,ChartViewModel chartViewModel)
            :base(dialogService,uiDispatcher,backgroundJobQueue)
        {
            _popupService = popupService;
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
        }


        [RelayCommand]
        public async Task OpenCameraConnectViewAsync()
        {
            await _popupService.ShowModelessAsync(ViewType.Camera,_cameraViewModel);
        }

        [RelayCommand]
        public async Task OpenChartViewCommand()
        {
            await _popupService.ShowModelessAsync(ViewType.SpectrumChart, _chartViewModel);
        }
    }
}
