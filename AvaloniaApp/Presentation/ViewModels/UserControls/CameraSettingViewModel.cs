using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraSettingViewModel : ViewModelBase, IPopup
    {
        private readonly VimbaCameraService _cameraService;

        // 값 변경 시 카메라로 전송할지 여부를 결정하는 플래그
        private bool _isUpdatingFromCamera;
        private int _autoApplyVersion;

        [ObservableProperty] private double exposureTime;
        [ObservableProperty] private double gain;
        [ObservableProperty] private double gamma;

        [ObservableProperty] private double exposureMin = 100;
        [ObservableProperty] private double exposureMax = 1000000;
        [ObservableProperty] private double gainMin = 0;
        [ObservableProperty] private double gainMax = 48;
        [ObservableProperty] private double gammaMin = 0.3;
        [ObservableProperty] private double gammaMax = 2.8;
        public string Title { get; set; } = "Camera Settings";
        public int Width { get; set; } = 400;
        public int Height { get; set; } = 500;
        // ★ AppServices 주입 방식으로 변경
        public CameraSettingViewModel(AppService service):base(service)
        {
            _cameraService = service.Camera;
        }
        // 값이 변경되면 자동으로 적용 예약 (플래그가 false일 때만)
        partial void OnExposureTimeChanged(double value) => QueueAutoApply();
        partial void OnGainChanged(double value) => QueueAutoApply();
        partial void OnGammaChanged(double value) => QueueAutoApply();
        private async void QueueAutoApply()
        {
            // 로딩 중이면 무시
            if (_isUpdatingFromCamera) return;
            if(!_cameraService.IsStreaming) return;
            
            var version = ++_autoApplyVersion;

            // 0.25초 대기 (디바운싱)
            try
            {
                await Task.Delay(250);
            }
            catch { return; }

            // 버전이 바뀌었으면(새로운 변경이 있었으면) 취소
            if (version != _autoApplyVersion) return;

            // 실제 적용
            await ApplyAsync();
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            await RunOperationAsync(
                key: "LoadSettings",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("카메라 설정 불러오는 중...");

                    // 백그라운드에서 값 읽기
                    var exp = await _cameraService.GetExposureTimeAsync(ct);
                    var gn = await _cameraService.GetGainAsync(ct);
                    var gm = await _cameraService.GetGammaAsync(ct);

                    await UiInvokeAsync(() =>
                    {
                        try
                        {
                            // ★ 중요: UI 업데이트 중에는 AutoApply가 동작하지 않도록 플래그 설정
                            _isUpdatingFromCamera = true;

                            ExposureTime = exp;
                            Gain = gn;
                            Gamma = gm;
                        }
                        finally
                        {
                            _isUpdatingFromCamera = false;
                        }
                    });
                },
                configure: opt => opt.JobName = "LoadCameraSettings");
        }

        [RelayCommand]
        public async Task ApplyAsync()
        {
            if (!_cameraService.IsStreaming) return;
            // UI 스레드에서 현재 설정값을 캡처 (스레드 안전)
            var targetExp = ExposureTime;
            var targetGain = Gain;
            var targetGamma = Gamma;

            await RunOperationAsync(
                key: "ApplySettings",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("설정 적용 중...");

                    // 하나씩 순차 적용 (카메라 SDK 특성에 따라 병렬 불가할 수 있음)
                    // Set 메서드가 실제 적용된 값을 반환한다고 가정
                    var appliedExp = await _cameraService.SetExposureTimeAsync(targetExp, ct);
                    var appliedGain = await _cameraService.SetGainAsync(targetGain, ct);
                    var appliedGamma = await _cameraService.SetGammaAsync(targetGamma, ct);

                    // 실제 적용된 값으로 UI 보정 (선택 사항)
                    // 카메라가 지원하지 않는 값이라 근사치로 설정되었을 경우 UI도 맞춰줌
                    await UiInvokeAsync(() =>
                    {
                        try
                        {
                            _isUpdatingFromCamera = true;
                            ExposureTime = appliedExp;
                            Gain = appliedGain;
                            Gamma = appliedGamma;
                        }
                        finally
                        {
                            _isUpdatingFromCamera = false;
                        }
                    });
                },
                configure: opt =>
                {
                    opt.JobName = "ApplyCameraSettings";
                    opt.Timeout = TimeSpan.FromSeconds(5); // 타임아웃 설정
                });
        }
    }
}