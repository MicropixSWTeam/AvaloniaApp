using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Windows
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly RawImageViewModel _rawImageViewModel;
        private readonly RgbImageViewModel _rgbImageViewModel;
        private readonly CameraViewModel _cameraViewModel;
        private readonly ChartViewModel _chartViewModel;
        private readonly CameraSettingViewModel _cameraSettingViewModel;
        private readonly ProcessViewModel _processViewModel;

        [ObservableProperty] private ViewModelBase _topLeftContent;
        [ObservableProperty] private ViewModelBase _topCenterContent;
        [ObservableProperty] private ViewModelBase _topRightContent;
        [ObservableProperty] private ViewModelBase _bottomLeftContent;
        [ObservableProperty] private ViewModelBase _bottomCenterContent;
        [ObservableProperty] private ViewModelBase _bottomRightContent;

        public MainWindowViewModel(
            AppService service,
            RawImageViewModel rawImage,
            RgbImageViewModel rgbImage,
            CameraViewModel cameraViewModel,
            ChartViewModel chartViewModel,
            CameraSettingViewModel cameraSettingViewModel,
            ProcessViewModel processViewModel) : base(service)
        {
            _rawImageViewModel = rawImage;
            _rgbImageViewModel = rgbImage;
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
            _processViewModel = processViewModel;

            _topLeftContent = _cameraSettingViewModel;
            _topCenterContent = _rawImageViewModel;
            _topRightContent = _chartViewModel;
            _bottomLeftContent = null;
            _bottomCenterContent = _rgbImageViewModel;
            _bottomRightContent = _processViewModel;
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
        public async Task SaveAsync()
        {
            await _cameraViewModel.SaveCommand.ExecuteAsync(null);
        }
    }
}