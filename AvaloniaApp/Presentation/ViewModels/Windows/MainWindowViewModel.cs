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

        // [사용자가 결정하는 폴더 이름]
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
            ProcessViewModel processViewModel) : base(service)
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
            // 스트리밍 중이 아니고, 이미 저장 중이 아니며, 폴더명이 있어야 함
            return !_vimbaCameraService.IsStreaming &&
                   !IsSaving &&
                   !string.IsNullOrWhiteSpace(SaveFolderName);
        }

        [RelayCommand(CanExecute = nameof(CanSaveExperiment))]
        private async Task SaveExperimentAsync()
        {
            var ws = _workspaceService.Current;
            if (ws?.EntireFrameData is null) return;

            // 1. UI 상태 변경 (저장 중 표시)
            await UiInvokeAsync(() => IsSaving = true);

            await RunOperationAsync("SaveExperiment", async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("데이터 변환 및 저장 중...");

                int currentWd = ws.WorkingDistance;

                // Dispose 관리를 위한 리스트
                var disposables = new List<IDisposable>();

                // 저장할 비트맵들
                Bitmap? fullBmp = null;
                Bitmap? colorBmp = null;
                var cropBitmaps = new Dictionary<string, Bitmap>();

                try
                {
                    // ---------------------------------------------------------
                    // A. 데이터 가공 (Heavy Logic)
                    // ---------------------------------------------------------

                    // 1) Crop Frames 생성 (WD 기반 분할)
                    var cropFrames = _imageProcessService.GetCropFrameDatas(ws.EntireFrameData, currentWd);
                    foreach (var f in cropFrames) disposables.Add(f);

                    // 2) Crop Bitmaps 생성 (파장별)
                    var wavelengths = Options.GetWavelengthList();
                    var waveToIndex = Options.GetWavelengthIndexMap();

                    foreach (var wl in wavelengths)
                    {
                        if (!waveToIndex.TryGetValue(wl, out int idx)) continue;
                        if (idx >= cropFrames.Count) continue;

                        var cropBmp = _imageProcessService.CreateBitmapFromFrame(cropFrames[idx]);
                        cropBitmaps[$"{wl}nm"] = cropBmp; // 키: "450nm" 등
                    }

                    // 3) Color Image 생성 (RGB 합성)
                    try
                    {
                        var rgbFrame = _imageProcessService.GetRgbFrameDataFromCropFrames(cropFrames);
                        disposables.Add(rgbFrame);
                        colorBmp = _imageProcessService.CreateBitmapFromFrame(rgbFrame, PixelFormats.Bgr24);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"RGB 생성 실패: {ex.Message}");
                        // Color Image 생성이 실패하더라도 저장은 계속 진행
                    }

                    // 4) Full Image 생성 (Stitch)
                    //    원본 EntireFrameData를 바로 써도 되지만, 
                    //    '잘린 영역들의 합'을 보여주기 위해 Stitch를 사용
                    var stitchedFrame = _imageProcessService.GetStitchFrameData(ws.EntireFrameData, cropFrames, currentWd);
                    if (stitchedFrame != null)
                    {
                        disposables.Add(stitchedFrame);
                        fullBmp = _imageProcessService.CreateBitmapFromFrame(stitchedFrame);
                    }
                    else
                    {
                        // Fallback: 원본 사용
                        fullBmp = _imageProcessService.CreateBitmapFromFrame(ws.EntireFrameData);
                    }

                    // 5) CSV Content 생성
                    var sb = new StringBuilder();
                    sb.AppendLine("RegionIndex,Wavelength,Mean,StdDev");

                    if (ws.IntensityDataMap != null)
                    {
                        // Region 순서대로 정렬
                        foreach (var kvp in ws.IntensityDataMap.OrderBy(k => k.Key))
                        {
                            int regionIdx = kvp.Key;
                            foreach (var data in kvp.Value)
                            {
                                sb.AppendLine($"{regionIdx},{data.wavelength},{data.mean},{data.stddev}");
                            }
                        }
                    }

                    // ---------------------------------------------------------
                    // B. 저장 실행 (UI Thread Interaction 포함)
                    // ---------------------------------------------------------

                    await UiInvokeAsync(async () =>
                    {
                        // Folder Picker를 띄우고 실제 저장을 수행
                        await _storageService.SaveExperimentResultAsync(
                            SaveFolderName,
                            fullBmp!,
                            colorBmp,
                            cropBitmaps,
                            sb.ToString(),
                            ct);

                        // 성공 후 폴더명 자동 갱신 (다음 저장을 위해)
                        SaveFolderName = $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}";
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Save Failed: {ex}");
                    // 필요 시 User에게 에러 알림 다이얼로그 추가
                }
                finally
                {
                    // ---------------------------------------------------------
                    // C. 리소스 정리
                    // ---------------------------------------------------------

                    // 생성된 Bitmap들 해제
                    fullBmp?.Dispose();
                    colorBmp?.Dispose();
                    foreach (var bmp in cropBitmaps.Values) bmp.Dispose();

                    // 중간 생성된 FrameData들 해제 (Native Memory 반환)
                    foreach (var d in disposables) d.Dispose();

                    // UI 상태 복구
                    _service.Ui.Post(() => IsSaving = false);
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