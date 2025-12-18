using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Windows
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly CameraViewModel _cameraViewModel;
        private readonly ChartViewModel _chartViewModel;
        private readonly CameraSettingViewModel _cameraSettingViewModel;

        public MainWindowViewModel(
            AppService service,
            CameraConnectViewModel cameraConnectViewModel,
            CameraViewModel cameraViewModel, 
            ChartViewModel chartViewModel,
            CameraSettingViewModel cameraSettingViewModel):base(service)
        {
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
        }
        [RelayCommand]
        public async Task StartCameraAsync()
        {
            await _cameraViewModel.StartPreviewCommand.ExecuteAsync(null);
        }
        [RelayCommand]
        public async Task StopCameraAsync()
        {
            await _cameraViewModel.StopPreviewCommand.ExecuteAsync(null);
        }
        [RelayCommand]
        public async Task OpenCameraViewAsync()
        {
            await _service.Popup.ShowModelessAsync(_cameraViewModel);
        }
        [RelayCommand]
        public async Task OpenChartViewAsync()
        {
            await _service.Popup.ShowModelessAsync(_chartViewModel);
        }
        [RelayCommand]
        public async Task OpenCameraSettingViewAsync()
        {
            await _service.Popup.ShowModelessAsync(_cameraSettingViewModel);
        }
    }
}
