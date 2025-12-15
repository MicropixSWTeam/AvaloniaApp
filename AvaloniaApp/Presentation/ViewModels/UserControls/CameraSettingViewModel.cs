using AutoMapper.Configuration.Annotations;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraSettingViewModel : ViewModelBase,IPopup
    {
        private readonly VimbaCameraService _camera;

        [ObservableProperty]
        private double exposureTime; 
        [ObservableProperty]
        private double gain;
        [ObservableProperty]
        private double gamma;
        [ObservableProperty]
        private double exposureMin = 100;
        [ObservableProperty]
        private double exposureMax = 1000000;
        [ObservableProperty]
        private double gainMin = 0;
        [ObservableProperty]
        private double gainMax = 48;
        [ObservableProperty]
        private double gammaMin = 0.3;
        [ObservableProperty]
        private double gammaMax = 2.8;

        private bool _isUpdatingFromCamera;
        private int _autoApplyVersion;
        public string Title { get; set; } = "Camera Setting";
        public int Width { get; set; } = 400;
        public int Height { get; set; } = 500;

        public CameraSettingViewModel(VimbaCameraService vimbaCameraService) : base()
        {
            _camera = vimbaCameraService;
        }
        partial void OnExposureTimeChanged(double value)
        {
            if (_isUpdatingFromCamera) return;
            QueueAutoApply();
        }
        partial void OnGainChanged(double value)
        {
            if (_isUpdatingFromCamera) return;
            QueueAutoApply();
        }
        partial void OnGammaChanged(double value)
        {
            if (_isUpdatingFromCamera) return;
            QueueAutoApply();
        }
        private async void QueueAutoApply()
        {
            var version = ++_autoApplyVersion;

            await Task.Delay(250);

            if (version != _autoApplyVersion)
                return;

            await ApplyAsync();
        }
        [RelayCommand]
        public async Task LoadAsync()
        {
            await RunOperationAsync(
                key: "LoadCameraBrightness",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("밝기 값 불러오는 중");
                    var exposure = await _camera.GetExposureTimeAsync(ct);
                    var gain = await _camera.GetGainAsync(ct);
                    var gamma = await _camera.GetGammaAsync(ct);

                    await UiInvokeAsync(() =>
                    {
                        ExposureTime = exposure;
                        Gamma = gain;
                        Gamma = gamma;
                    }).ConfigureAwait(false);
                },
                configure:opt =>
                {
                    opt.JobName = "CameraBrightnessLoad";
                    opt.Timeout = TimeSpan.FromSeconds(3);
                });
        }
        [RelayCommand]
        public async Task ApplyAsync()
        {
        }
    }
}
