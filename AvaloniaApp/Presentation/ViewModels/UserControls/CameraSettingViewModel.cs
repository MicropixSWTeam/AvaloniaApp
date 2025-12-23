using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraSettingViewModel : ViewModelBase
    {
        private readonly VimbaCameraService _cameraService;
        public CameraViewModel CameraVM { get; }

        private bool _isUpdatingFromCamera;
        private bool _hasUserEdits;
        private int _autoApplyVersion;
        private int _startSyncVersion;
        private int _isApplying;

        // 숫자/텍스트 초기값을 반드시 일치시킴 (기존에는 숫자=0, 텍스트만 100/0/1)
        [ObservableProperty] private double _exposureTime = 100.0;
        [ObservableProperty] private double _gain = 0.0;
        [ObservableProperty] private double _gamma = 1.0;

        [ObservableProperty] private string _exposureText = "100.0";
        [ObservableProperty] private string _gainText = "0.0";
        [ObservableProperty] private string _gammaText = "1.0";

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

            _cameraService.StreamingStateChanged += OnStreamingStateChanged;
        }

        private void OnStreamingStateChanged(bool isStreaming)
        {
            if (!isStreaming) return;

            var version = Interlocked.Increment(ref _startSyncVersion);

            _ = Task.Run(async () =>
            {
                // 단순 200ms 1회 시도 대신, 최대 1초 정도 준비 대기
                for (int i = 0; i < 10; i++)
                {
                    if (version != Volatile.Read(ref _startSyncVersion)) return;
                    if (_cameraService.IsStreaming) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }

                if (version != Volatile.Read(ref _startSyncVersion)) return;
                if (!_cameraService.IsStreaming) return;

                // 사용자가 Start 전에 값을 건드렸으면 그 값 적용
                // 안 건드렸으면 카메라 현재값을 Load해서 UI 동기화
                if (_hasUserEdits)
                    await ApplyAsync().ConfigureAwait(false);
                else
                    await LoadAsync().ConfigureAwait(false);
            });
        }

        partial void OnExposureTimeChanged(double value)
        {
            if (!_isUpdatingFromCamera)
            {
                _hasUserEdits = true;
                ExposureText = value.ToString("F1", CultureInfo.InvariantCulture);
            }
            QueueAutoApply();
        }

        partial void OnGainChanged(double value)
        {
            if (!_isUpdatingFromCamera)
            {
                _hasUserEdits = true;
                GainText = value.ToString("F1", CultureInfo.InvariantCulture);
            }
            QueueAutoApply();
        }

        partial void OnGammaChanged(double value)
        {
            if (!_isUpdatingFromCamera)
            {
                _hasUserEdits = true;
                GammaText = value.ToString("F1", CultureInfo.InvariantCulture);
            }
            QueueAutoApply();
        }

        private async void QueueAutoApply()
        {
            if (_isUpdatingFromCamera) return;
            if (!_cameraService.IsStreaming) return;

            var version = ++_autoApplyVersion;

            try { await Task.Delay(250).ConfigureAwait(false); }
            catch { return; }

            if (version != _autoApplyVersion) return;

            await ApplyAsync().ConfigureAwait(false);
        }

        private static bool TryParseClamp(string text, double min, double max, out double value)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                value = default;
                return false;
            }

            value = Math.Clamp(v, min, max);
            return true;
        }

        [RelayCommand]
        private async Task CommitExposureAsync(string text)
        {
            if (!TryParseClamp(text, ExposureMin, ExposureMax, out var val))
            {
                ExposureText = ExposureTime.ToString("F1", CultureInfo.InvariantCulture);
                return;
            }

            ExposureTime = val;
            await ApplyAsync().ConfigureAwait(false);
        }

        [RelayCommand]
        private async Task CommitGainAsync(string text)
        {
            if (!TryParseClamp(text, GainMin, GainMax, out var val))
            {
                GainText = Gain.ToString("F1", CultureInfo.InvariantCulture);
                return;
            }

            Gain = val;
            await ApplyAsync().ConfigureAwait(false);
        }

        [RelayCommand]
        private async Task CommitGammaAsync(string text)
        {
            if (!TryParseClamp(text, GammaMin, GammaMax, out var val))
            {
                GammaText = Gamma.ToString("F1", CultureInfo.InvariantCulture);
                return;
            }

            Gamma = val;
            await ApplyAsync().ConfigureAwait(false);
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            if (!_cameraService.IsStreaming) return;

            await RunOperationAsync("LoadSettings", async (ct, ctx) =>
            {
                var exp = await _cameraService.GetExposureTimeAsync(ct).ConfigureAwait(false);
                var gn = await _cameraService.GetGainAsync(ct).ConfigureAwait(false);
                var gm = await _cameraService.GetGammaAsync(ct).ConfigureAwait(false);

                await UiInvokeAsync(() =>
                {
                    try
                    {
                        _isUpdatingFromCamera = true;

                        ExposureTime = exp;
                        Gain = gn;
                        Gamma = gm;

                        ExposureText = exp.ToString("F1", CultureInfo.InvariantCulture);
                        GainText = gn.ToString("F1", CultureInfo.InvariantCulture);
                        GammaText = gm.ToString("F1", CultureInfo.InvariantCulture);
                    }
                    finally
                    {
                        _isUpdatingFromCamera = false;
                    }
                });
            }).ConfigureAwait(false);
        }

        [RelayCommand]
        public async Task ApplyAsync()
        {
            if (!_cameraService.IsStreaming) return;

            // 중복 Apply(슬라이더/Commit/AutoApply)가 겹치면 카메라 쪽이 무시/충돌할 수 있으니 단일화
            if (Interlocked.Exchange(ref _isApplying, 1) == 1) return;

            try
            {
                var targetExp = Math.Clamp(ExposureTime, ExposureMin, ExposureMax);
                var targetGain = Math.Clamp(Gain, GainMin, GainMax);
                var targetGamma = Math.Clamp(Gamma, GammaMin, GammaMax);

                await RunOperationAsync("ApplySettings", async (ct, ctx) =>
                {
                    // 1) Set
                    await _cameraService.SetExposureTimeAsync(targetExp, ct).ConfigureAwait(false);
                    await _cameraService.SetGainAsync(targetGain, ct).ConfigureAwait(false);
                    await _cameraService.SetGammaAsync(targetGamma, ct).ConfigureAwait(false);

                    // 2) “실제 적용값” 재조회(핵심: Set 리턴값 가정 제거)
                    // 카메라가 반영에 약간 시간이 걸리면 필요시 짧은 Delay를 넣어도 됨
                    // await Task.Delay(30, ct).ConfigureAwait(false);

                    var appliedExp = await _cameraService.GetExposureTimeAsync(ct).ConfigureAwait(false);
                    var appliedGain = await _cameraService.GetGainAsync(ct).ConfigureAwait(false);
                    var appliedGamma = await _cameraService.GetGammaAsync(ct).ConfigureAwait(false);

                    await UiInvokeAsync(() =>
                    {
                        try
                        {
                            _isUpdatingFromCamera = true;

                            ExposureTime = appliedExp;
                            ExposureText = appliedExp.ToString("F1", CultureInfo.InvariantCulture);

                            Gain = appliedGain;
                            GainText = appliedGain.ToString("F1", CultureInfo.InvariantCulture);

                            Gamma = appliedGamma;
                            GammaText = appliedGamma.ToString("F1", CultureInfo.InvariantCulture);
                        }
                        finally
                        {
                            _isUpdatingFromCamera = false;
                        }
                    });
                }).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _isApplying, 0);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            _cameraService.StreamingStateChanged -= OnStreamingStateChanged;
            await base.DisposeAsync();
        }
    }
}
