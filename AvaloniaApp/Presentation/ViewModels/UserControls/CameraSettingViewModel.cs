using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraSettingViewModel : ViewModelBase
    {
        private readonly CameraPipeline _cameraPipeline;

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
        public CameraSettingViewModel(CameraPipeline cameraPipeline) : base()
        {
            _cameraPipeline = cameraPipeline;
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

            // 200~300ms 정도 디바운스 (원하는 값으로 조정)
            await Task.Delay(250);

            // 그 사이에 값이 또 바뀌었으면 버림
            if (version != _autoApplyVersion)
                return;

            await ApplyAsync();
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            await RunSafeAsync(ct => _cameraPipeline.EnqueueLoadCameraParamsAsync(ct,
                async (exp, g, ga) =>
                {
                    _isUpdatingFromCamera = true;
                    ExposureTime = exp;
                    Gain = g;
                    Gamma = ga;
                    _isUpdatingFromCamera = false;
                    await Task.CompletedTask;
                }));
        }
        // Apply 버튼에 연결
        [RelayCommand]
        public async Task ApplyAsync()
        {
            await RunSafeAsync(ct =>
                _cameraPipeline.EnqueueApplyCameraParamsAsync(
                    ct,
                    ExposureTime,
                    Gain,
                    Gamma,
                    async (appliedExp, appliedGain, appliedGamma) =>
                    {
                        // 카메라가 근처 값으로 보정한 실제 값으로 UI 동기화
                        ExposureTime = appliedExp;
                        Gain = appliedGain;
                        Gamma = appliedGamma;

                        await Task.CompletedTask;
                    }));
        }
    }
}
