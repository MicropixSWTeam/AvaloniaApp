using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraSettingViewModel : ViewModelBase
    {
        public CameraViewModel CameraVM { get; }
        private readonly VimbaCameraService _cameraService;

        private bool _isUpdatingFromCamera;
        private int _autoApplyVersion;

        [ObservableProperty] private double exposureTime;
        [ObservableProperty] private double gain;
        [ObservableProperty] private double gamma;

        [ObservableProperty] private string exposureText = "100.0";
        [ObservableProperty] private string gainText = "0.0";
        [ObservableProperty] private string gammaText = "1.0";

        [ObservableProperty] private double exposureMin = 100;
        [ObservableProperty] private double exposureMax = 1000000;
        [ObservableProperty] private double gainMin = 0;
        [ObservableProperty] private double gainMax = 48;
        [ObservableProperty] private double gammaMin = 0.3;
        [ObservableProperty] private double gammaMax = 2.8;

        public CameraSettingViewModel(AppService service, CameraViewModel cameraViewModel) : base(service)
        {
            _cameraService = service.Camera;
            CameraVM = cameraViewModel;
        }

        partial void OnExposureTimeChanged(double value)
        {
            if (!_isUpdatingFromCamera) ExposureText = value.ToString("F1");
            QueueAutoApply();
        }

        partial void OnGainChanged(double value)
        {
            if (!_isUpdatingFromCamera) GainText = value.ToString("F1");
            QueueAutoApply();
        }

        partial void OnGammaChanged(double value)
        {
            if (!_isUpdatingFromCamera) GammaText = value.ToString("F1");
            QueueAutoApply();
        }

        [RelayCommand]
        private async Task CommitExposureAsync(string text)
        {
            if (double.TryParse(text, out var val)) { ExposureTime = val; await ApplyAsync(); }
            else ExposureText = ExposureTime.ToString("F1");
        }

        [RelayCommand]
        private async Task CommitGainAsync(string text)
        {
            if (double.TryParse(text, out var val)) { Gain = val; await ApplyAsync(); }
            else GainText = Gain.ToString("F1");
        }

        [RelayCommand]
        private async Task CommitGammaAsync(string text)
        {
            if (double.TryParse(text, out var val)) { Gamma = val; await ApplyAsync(); }
            else GammaText = Gamma.ToString("F1");
        }

        private async void QueueAutoApply()
        {
            if (_isUpdatingFromCamera) return;
            if (!_cameraService.IsStreaming) return;
            
            var version = ++_autoApplyVersion;
            try { await Task.Delay(250); } catch { return; }
            if (version != _autoApplyVersion) return;

            await ApplyAsync();
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            await RunOperationAsync("LoadSettings", async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("카메라 설정 불러오는 중...");
                var exp = await _cameraService.GetExposureTimeAsync(ct);
                var gn = await _cameraService.GetGainAsync(ct);
                var gm = await _cameraService.GetGammaAsync(ct);

                await UiInvokeAsync(() =>
                {
                    try
                    {
                        _isUpdatingFromCamera = true;
                        ExposureTime = exp; Gain = gn; Gamma = gm;
                        ExposureText = exp.ToString("F1"); GainText = gn.ToString("F1"); GammaText = gm.ToString("F1");
                    }
                    finally { _isUpdatingFromCamera = false; }
                });
            });
        }

        [RelayCommand]
        public async Task ApplyAsync()
        {
            if (!_cameraService.IsStreaming) return;

            var targetExp = ExposureTime;
            var targetGain = Gain;
            var targetGamma = Gamma;

            await RunOperationAsync("ApplySettings", async (ct, ctx) =>
            {
                // [중요] 실제 적용된 값을 반환받음
                var appliedExp = await _cameraService.SetExposureTimeAsync(targetExp, ct);
                var appliedGain = await _cameraService.SetGainAsync(targetGain, ct);
                var appliedGamma = await _cameraService.SetGammaAsync(targetGamma, ct);

                await UiInvokeAsync(() =>
                {
                    try
                    {
                        _isUpdatingFromCamera = true;
                        // [핵심] 실제 카메라에 적용된 값으로 UI 동기화
                        ExposureTime = appliedExp; ExposureText = appliedExp.ToString("F1");
                        Gain = appliedGain; GainText = appliedGain.ToString("F1");
                        Gamma = appliedGamma; GammaText = appliedGamma.ToString("F1");
                    }
                    finally { _isUpdatingFromCamera = false; }
                });
            });
        }
    }
}