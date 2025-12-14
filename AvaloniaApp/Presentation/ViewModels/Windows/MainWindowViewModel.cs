using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Operations;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaApp.Presentation.Views.UserControls;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Windows
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly PopupService _popupService;
        private readonly CameraViewModelTest _cameraViewModel;
        private readonly ChartViewModel _chartViewModel;
        private readonly CameraConnectViewModel _cameraConnectViewModel;
        private readonly CameraSettingViewModel _cameraSettingViewModel;

        public MainWindowViewModel(PopupService popupService, 
            UiDispatcher uiDispatcher, OperationRunner runner,
            CameraConnectViewModel cameraConnectViewModel,CameraViewModelTest cameraViewModel, ChartViewModel chartViewModel,CameraSettingViewModel cameraSettingViewModel):base(uiDispatcher, runner)
        {
            _popupService = popupService;
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraConnectViewModel = cameraConnectViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
        }
        [RelayCommand]
        public async Task OpenCameraViewAsync()
        {
            await _popupService.ShowModelessAsync(_cameraViewModel);
        }
        [RelayCommand]
        public async Task OpenCameraConnectViewAsync()
        {
            await _popupService.ShowModelessAsync(_cameraConnectViewModel);
        }
        [RelayCommand]
        public async Task OpenChartViewAsync()
        {
            await _popupService.ShowModelessAsync( _chartViewModel);
        }
        [RelayCommand]
        public async Task OpenCameraSettingViewAsync()
        {
            await _popupService.ShowModelessAsync(_cameraSettingViewModel);
        }
    }
}
