using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Windows
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly CameraViewModel _cameraViewModel;
        private readonly ChartViewModel _chartViewModel;
        private readonly CameraSettingViewModel _cameraSettingViewModel;
        
        [ObservableProperty] private ViewModelBase _sidebarContent;
        [ObservableProperty] private ViewModelBase _topLeftContent;
        [ObservableProperty] private ViewModelBase _topRightContent;
        [ObservableProperty] private ViewModelBase _bottomLeftContent;
        [ObservableProperty] private ViewModelBase _bottomRightContent;
        public MainWindowViewModel(
            AppService service,
            CameraViewModel cameraViewModel, 
            ChartViewModel chartViewModel,
            CameraSettingViewModel cameraSettingViewModel):base(service)
        {
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
            
            _sidebarContent = _cameraSettingViewModel;
            _topLeftContent = _cameraViewModel;
            _topRightContent = _chartViewModel;
            _bottomLeftContent = _cameraViewModel;
            _bottomRightContent = _chartViewModel;
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
