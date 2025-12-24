using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraSettingViewModel : ViewModelBase
    {
        private readonly VimbaCameraService _cameraService;
        public CameraViewModel CameraVM { get; }

        private bool _isUpdatingFromCamera;
        private int _autoApplyVersion;
        private readonly SemaphoreSlim _syncGate = new(1, 1);
        private int _pendingApply;
        private int _hasUserEdited;

        // Draft Expression
        [ObservableProperty] private string _draftExpression = "";

        [ObservableProperty] private double _exposureTime = 5000;
        [ObservableProperty] private double _gain = 0.0;
        [ObservableProperty] private double _gamma = 1.0;

        [ObservableProperty] private string _exposureText = "5000.0";
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

            // 초기값 동기화
            DraftExpression = CameraVM.ExpressionText;

            _cameraService.StreamingStateChanged += OnStreamingStateChanged;
        }

        private bool CanAccessCamera() => CameraVM.IsPreviewing || _cameraService.IsStreaming;

        private void OnStreamingStateChanged(bool isStreaming)
        {
            if (isStreaming)
            {
                _service.Ui.InvokeAsync(() => LoadAsync());
            }
        }

        partial void OnExposureTimeChanged(double value)
        {
            if (!_isUpdatingFromCamera) ExposureText = value.ToString("F1", CultureInfo.InvariantCulture);
            QueueAutoApply();
        }

        partial void OnGainChanged(double value)
        {
            if (!_isUpdatingFromCamera) GainText = value.ToString("F1", CultureInfo.InvariantCulture);
            QueueAutoApply();
        }

        partial void OnGammaChanged(double value)
        {
            if (!_isUpdatingFromCamera) GammaText = value.ToString("F1", CultureInfo.InvariantCulture);
            QueueAutoApply();
        }

        private async void QueueAutoApply()
        {
            if (_isUpdatingFromCamera) return;

            if (!CanAccessCamera())
            {
                Volatile.Write(ref _pendingApply, 1);
                return;
            }

            var version = ++_autoApplyVersion;
            try { await Task.Delay(250).ConfigureAwait(false); } catch { return; }
            if (version != _autoApplyVersion) return;

            await ApplyAsync().ConfigureAwait(false);
        }

        [RelayCommand]
        private async Task CommitExposureAsync(string text)
        {
            Volatile.Write(ref _hasUserEdited, 1);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                ExposureTime = val;
                await ApplyAsync();
            }
            else
            {
                ExposureText = ExposureTime.ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        [RelayCommand]
        private async Task CommitGainAsync(string text)
        {
            Volatile.Write(ref _hasUserEdited, 1);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                Gain = val;
                await ApplyAsync();
            }
            else
            {
                GainText = Gain.ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        [RelayCommand]
        private async Task CommitGammaAsync(string text)
        {
            Volatile.Write(ref _hasUserEdited, 1);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                Gamma = val;
                await ApplyAsync();
            }
            else
            {
                GammaText = Gamma.ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            if (!CanAccessCamera()) return;

            await _syncGate.WaitAsync().ConfigureAwait(false);
            try
            {
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
                    }).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            finally
            {
                _syncGate.Release();
            }
        }

        [RelayCommand]
        public async Task ApplyAsync()
        {
            if (!_cameraService.IsStreaming) return;

            var targetExp = Math.Clamp(ExposureTime, ExposureMin, ExposureMax);
            var targetGain = Math.Clamp(Gain, GainMin, GainMax);
            var targetGamma = Math.Clamp(Gamma, GammaMin, GammaMax);

            await RunOperationAsync("ApplySettings", async (ct, ctx) =>
            {
                await _cameraService.SetExposureTimeAsync(targetExp, ct);
                await _cameraService.SetGainAsync(targetGain, ct);
                await _cameraService.SetGammaAsync(targetGamma, ct);

                double exp = targetExp, gn = targetGain, gm = targetGamma;
                for (int i = 0; i < 3; i++)
                {
                    exp = await _cameraService.GetExposureTimeAsync(ct);
                    gn = await _cameraService.GetGainAsync(ct);
                    gm = await _cameraService.GetGammaAsync(ct);
                    if (Math.Abs(exp - targetExp) < 1e-6 &&
                        Math.Abs(gn - targetGain) < 1e-6 &&
                        Math.Abs(gm - targetGamma) < 1e-6)
                        break;
                    await Task.Delay(50, ct);
                }

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
            });
        }

        // [핵심 기능] 수식 입력 커맨드: 수식 적용 및 정지 상태 즉시 갱신
        [RelayCommand]
        public void CommitExpression(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            DraftExpression = text;
            CameraVM.ExpressionText = text;

            // 카메라가 Stop 상태일 때도 즉시 반영 (Workspace 이미지 사용)
            if (!CameraVM.IsPreviewing)
            {
                CameraVM.RefreshProcessedImageInStopState();
            }
        }

        private static double ParseOrDefault(string s, double fallback)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public override async ValueTask DisposeAsync()
        {
            _cameraService.StreamingStateChanged -= OnStreamingStateChanged;
            await base.DisposeAsync();
        }
    }
}