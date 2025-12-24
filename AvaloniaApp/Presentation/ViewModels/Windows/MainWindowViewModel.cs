using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure.Service;
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
        private readonly LoadDialogViewModel _loadViewModel;
        private readonly DialogViewModel _dialogViewModel;

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
            ProcessViewModel processViewModel,
            LoadDialogViewModel loadViewModel,
            DialogViewModel dialogViewModel
            ) : base(service)
        {
            _rawImageViewModel = rawImage;
            _rgbImageViewModel = rgbImage;
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
            _processViewModel = processViewModel;
            _loadViewModel = loadViewModel;
            _dialogViewModel = dialogViewModel;

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
        public async Task OnClickSaveButtonAsync()
        {
            string? fileName = await _service.Popup.ShowCustomAsync<InputDialogViewModel, string>(vm =>
            {
                vm.Init("데이터 저장", "저장할 폴더(파일) 이름을 입력하세요.", $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}");
            });
        }
        [RelayCommand]
        public async Task OnClickLoadButtonAsync()
        {
            // 1. LoadViewModel 팝업 띄우기 (Factory 생성)
            string? folderName = await _service.Popup.ShowCustomAsync<LoadDialogViewModel, string>();

            if (string.IsNullOrEmpty(folderName)) return;
        }
    }
}