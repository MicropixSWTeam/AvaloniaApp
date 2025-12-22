using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
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

        private readonly WorkspaceService _workspaceService;
        private readonly ImageProcessService _imageProcessService;
        private readonly StorageService _storageService;
        private readonly VimbaCameraService _vimbaCameraService;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveExperimentCommand))]
        private string _saveFolderName = $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveExperimentCommand))]
        private bool _isSaving;

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
            ProcessViewModel processViewModel):base(service)
        {
            _rawImageViewModel = rawImage;
            _rgbImageViewModel = rgbImage;
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
            _processViewModel = processViewModel;

            _workspaceService = service.WorkSpace;
            _imageProcessService = service.ImageProcess;
            _storageService = service.Storage;
            _vimbaCameraService = service.Camera;

            _topLeftContent = _cameraSettingViewModel;
            _topCenterContent = _rawImageViewModel;
            _topRightContent = _chartViewModel;
            _bottomLeftContent = _processViewModel;
            _bottomCenterContent = _rgbImageViewModel;
            _bottomRightContent = _chartViewModel;

            _vimbaCameraService.StreamingStateChanged += OnStreamingStateChanged;
        }
        private void OnStreamingStateChanged(bool isStreaming)
        {
            // UI 스레드에서 커맨드 상태 갱신
            _service.Ui.InvokeAsync(() =>
            {
                SaveExperimentCommand.NotifyCanExecuteChanged();
            });
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
        private bool CanSaveExperiment()
        {
            return !_vimbaCameraService.IsStreaming &&
                   !IsSaving &&
                   !string.IsNullOrWhiteSpace(SaveFolderName);
        }
        [RelayCommand(CanExecute = nameof(CanSaveExperiment))]
        private async Task SaveExperimentAsync()
        {
            var ws = _workspaceService.Current;
            if (ws?.EntireFrameData is null) return;

            IsSaving = true;

            await RunOperationAsync("SaveExperiment", async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("데이터 변환 및 저장 중...");

                // [중요] 현재 Workspace에 설정된 거리(WD)를 가져옵니다.
                // CameraViewModel에서 WD 변경 시 Workspace에 업데이트해줍니다.
                int currentWd = ws.WorkingDistance;

                var disposables = new List<IDisposable>();

                try
                {
                    // 1. Full Image (Mono8) -> "FullImage.png"
                    using var fullBmp = _imageProcessService.CreateBitmapFromFrame(ws.EntireFrameData);

                    // 2. Color Image (RGB) -> "ColorImage.png"
                    // [중요] currentWd를 전달하여 거리에 맞는 좌표로 RGB를 합성합니다.
                    var rgbFrame = _imageProcessService.GetRgbFrameData(ws.EntireFrameData, currentWd);
                    disposables.Add(rgbFrame);
                    using var rgbBmp = _imageProcessService.CreateBitmapFromFrame(rgbFrame, PixelFormats.Bgr24);

                    // 3. Crop Images (Wavelength별) -> "{nm}.png"
                    var wavelengths = Options.GetWavelengthList();
                    var waveToIndex = Options.GetWavelengthIndexMap();
                    var cropBitmaps = new Dictionary<string, Bitmap>();

                    foreach (var wave in wavelengths)
                    {
                        int tileIndex = waveToIndex[wave];
                        // [중요] currentWd 전달
                        var cropFrame = _imageProcessService.GetCropFrameData(ws.EntireFrameData, tileIndex, currentWd);
                        disposables.Add(cropFrame);

                        var cropBmp = _imageProcessService.CreateBitmapFromFrame(cropFrame);
                        cropBitmaps[$"{wave}nm"] = cropBmp;
                    }

                    // 4. CSV 데이터 생성
                    var sb = new StringBuilder();
                    sb.AppendLine("RegionIndex,Wavelength,Mean,StdDev");

                    if (ws.IntensityDataMap != null)
                    {
                        foreach (var kvp in ws.IntensityDataMap.OrderBy(k => k.Key))
                        {
                            int regionIdx = kvp.Key;
                            foreach (var data in kvp.Value)
                            {
                                sb.AppendLine($"{regionIdx},{data.wavelength},{data.mean},{data.stddev}");
                            }
                        }
                    }

                    // 5. 파일 저장 (UI 스레드 필요 - FolderPicker)
                    await UiInvokeAsync(async () =>
                    {
                        await _storageService.SaveExperimentResultAsync(
                            SaveFolderName,
                            fullBmp,
                            rgbBmp,
                            cropBitmaps,
                            sb.ToString(),
                            ct);

                        // 저장 완료 후 폴더명 자동 갱신
                        SaveFolderName = $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}";
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Save Failed: {ex.Message}");
                }
                finally
                {
                    // 리소스 정리
                    foreach (var d in disposables) d.Dispose();
                    IsSaving = false;
                }
            });
        }

        public override async ValueTask DisposeAsync()
        {
            _vimbaCameraService.StreamingStateChanged -= OnStreamingStateChanged;
            await base.DisposeAsync();
        }
    }
}
