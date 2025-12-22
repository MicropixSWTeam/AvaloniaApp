using AvaloniaApp.Infrastructure;
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
        private readonly VimbaCameraService _cameraService;
        public CameraViewModel CameraVM { get; }

        // 내부 플래그 및 버전 관리
        private bool _isUpdatingFromCamera;
        private int _autoApplyVersion;
        private int _startSyncVersion;

        // --- Properties ---
        [ObservableProperty] private double _exposureTime;
        [ObservableProperty] private double _gain;
        [ObservableProperty] private double _gamma;

        [ObservableProperty] private string _exposureText = "100.0";
        [ObservableProperty] private string _gainText = "0.0";
        [ObservableProperty] private string _gammaText = "1.0";

        // 범위 설정 (필요시 서비스에서 가져오도록 수정 가능)
        [ObservableProperty] private double _exposureMin = 100;
        [ObservableProperty] private double _exposureMax = 1000000;
        [ObservableProperty] private double _gainMin = 0;
        [ObservableProperty] private double _gainMax = 48;
        [ObservableProperty] private double _gammaMin = 0.3;
        [ObservableProperty] private double _gammaMax = 2.8;

        public CameraSettingViewModel(AppService service, CameraViewModel cameraViewModel) : base(service)
        {
            _cameraService = service.Camera;
            CameraVM = cameraViewModel;

            // 카메라 상태 변경 구독 (Start 시점 동기화용)
            _cameraService.StreamingStateChanged += OnStreamingStateChanged;
        }

        // --- Event Handlers ---

        private void OnStreamingStateChanged(bool isStreaming)
        {
            // Stop 시점에는 할 일 없음
            if (!isStreaming) return;

            // Start 이벤트 중복 방지 및 버전 관리
            var version = Interlocked.Increment(ref _startSyncVersion);

            _ = Task.Run(async () =>
            {
                // Feature 접근 안정화를 위한 대기 시간 증가 (50ms -> 200ms)
                // 카메라가 물리적으로 StartAcquisition 후 파라미터를 받을 준비가 될 때까지 충분한 시간을 줍니다.
                await Task.Delay(200).ConfigureAwait(false);

                if (version != Volatile.Read(ref _startSyncVersion)) return;

                // 지연 시간 동안 Stop 되었을 수 있으므로 상태 재확인
                if (!_cameraService.IsStreaming) return;

                // [수정됨] 전략 개선:
                // 기존: ApplyAsync() 호출 후 LoadAsync() 호출
                // 문제: Apply 직후 Load를 호출하면, 카메라가 아직 값을 반영하지 못했거나 초기값을 반환하여
                //      UI에 설정해둔 값이 초기값으로 덮어씌워지는(Rollback) 현상이 발생합니다.
                // 해결: ApplyAsync() 내부에서 이미 Set 후 '실제 적용된 값'을 리턴받아 UI를 갱신하고 있습니다.
                //      따라서 뒤따르는 LoadAsync()는 중복이자 버그의 원인이므로 제거합니다.

                await ApplyAsync().ConfigureAwait(false);

                // await LoadAsync().ConfigureAwait(false); // <--- 삭제함
            });
        }

        // --- Property Changed Handlers (Auto Apply) ---

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

        private async void QueueAutoApply()
        {
            // 카메라에서 읽어오는 중이거나, 스트리밍 중이 아니면 스킵
            if (_isUpdatingFromCamera) return;
            if (!_cameraService.IsStreaming) return;

            var version = ++_autoApplyVersion;
            try
            {
                await Task.Delay(250); // Debounce
            }
            catch
            {
                return;
            }

            if (version != _autoApplyVersion) return;

            await ApplyAsync();
        }

        // --- Commands (Manual Commit) ---

        [RelayCommand]
        private async Task CommitExposureAsync(string text)
        {
            if (double.TryParse(text, out var val)) { ExposureTime = val; await ApplyAsync(); }
            else ExposureText = ExposureTime.ToString("F1"); // 실패 시 롤백
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

        // --- Core Methods (Load / Apply) ---

        /// <summary>
        /// 카메라의 현재 값을 읽어 UI에 반영합니다.
        /// </summary>
        [RelayCommand]
        public async Task LoadAsync()
        {
            if (!_cameraService.IsStreaming) return;

            await RunOperationAsync("LoadSettings", async (ct, ctx) =>
            {
                // ctx.ReportIndeterminate("설정 불러오는 중..."); // 필요 시 주석 해제
                var exp = await _cameraService.GetExposureTimeAsync(ct);
                var gn = await _cameraService.GetGainAsync(ct);
                var gm = await _cameraService.GetGammaAsync(ct);

                await UiInvokeAsync(() =>
                {
                    try
                    {
                        _isUpdatingFromCamera = true; // Setter 트리거 방지
                        ExposureTime = exp;
                        Gain = gn;
                        Gamma = gm;

                        ExposureText = exp.ToString("F1");
                        GainText = gn.ToString("F1");
                        GammaText = gm.ToString("F1");
                    }
                    finally
                    {
                        _isUpdatingFromCamera = false;
                    }
                });
            });
        }

        /// <summary>
        /// UI의 값을 카메라에 적용하고, 실제 적용된 값을 반환받아 UI를 확정합니다.
        /// </summary>
        [RelayCommand]
        public async Task ApplyAsync()
        {
            if (!_cameraService.IsStreaming) return;

            var targetExp = ExposureTime;
            var targetGain = Gain;
            var targetGamma = Gamma;

            await RunOperationAsync("ApplySettings", async (ct, ctx) =>
            {
                // Set API가 실제 적용된 값을 반환한다고 가정
                var appliedExp = await _cameraService.SetExposureTimeAsync(targetExp, ct);
                var appliedGain = await _cameraService.SetGainAsync(targetGain, ct);
                var appliedGamma = await _cameraService.SetGammaAsync(targetGamma, ct);

                await UiInvokeAsync(() =>
                {
                    try
                    {
                        _isUpdatingFromCamera = true;

                        ExposureTime = appliedExp;
                        ExposureText = appliedExp.ToString("F1");

                        Gain = appliedGain;
                        GainText = appliedGain.ToString("F1");

                        Gamma = appliedGamma;
                        GammaText = appliedGamma.ToString("F1");
                    }
                    finally
                    {
                        _isUpdatingFromCamera = false;
                    }
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            _cameraService.StreamingStateChanged -= OnStreamingStateChanged;
            await base.DisposeAsync();
        }
    }
}