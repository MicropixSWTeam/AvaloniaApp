using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Utils;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel : ViewModelBase
    {
        private readonly VimbaCameraService _cameraService;
        private readonly ImageProcessService _imageProcessService;
        private readonly WorkspaceService _workspaceService;
        private readonly StorageService _storageService;
        private readonly UiThrottler _uiThrottler;

        private CancellationTokenSource? _consumeCts;
        private Task? _consumeTask;
        private volatile bool _stopRequested;

        private int _isAnalyzing = 0;

        // Preview & RGB Bitmaps
        private WriteableBitmap? _rawpreviewBitmap;
        private FrameData? _previewFrameData;

        private WriteableBitmap? _rgbBitmap;
        private FrameData? _rgbFrameData;

        // Processed Result
        private FrameData? _processedFrameData;
        private WriteableBitmap? _processedBitmap;

        public event Action? RawPreviewInvalidated;
        public event Action? RgbPreviewInvalidated;
        public event Action? ProcessedPreviewInvalidated;

        public ReadOnlyObservableCollection<RegionData>? Regions => _workspaceService.Current?.RegionDatas;
        public int NextAvailableRegionColorIndex => _workspaceService.Current?.GetNextAvailableIndex() ?? -1;

        // ComboBox Items
        public ObservableCollection<ComboBoxData> WavelengthIndexs { get; }
            = new ObservableCollection<ComboBoxData>(Options.GetWavelengthIndexComboBoxData());
        public ObservableCollection<ComboBoxData> WorkingDistances { get; }
            = new ObservableCollection<ComboBoxData>(Options.GetWorkingDistanceComboBoxData());

        // Camera & Status Properties
        [ObservableProperty] private ObservableCollection<CameraData> cameras = new();
        [ObservableProperty] private CameraData? selectedCamera;
        [ObservableProperty] private Bitmap? rawBitmap;
        [ObservableProperty] private Bitmap? previewBitmap; // (참고: 로직상 주로 RawBitmap을 사용 중)
        [ObservableProperty] private Bitmap? rgbBitmap;
        [ObservableProperty] private bool isPreviewing;
        [ObservableProperty] private double previewFps;

        // Selections
        [ObservableProperty] private ComboBoxData? _selectedWavelengthIndex;
        [ObservableProperty] private ComboBoxData? _selectedWorkingDistance;

        // Process State
        [ObservableProperty] private bool _isProcessApply;
        [ObservableProperty] private Bitmap? processedBitmap;
        [ObservableProperty] private string expressionText = "(450 + 550)";

        // 저장 관련 속성
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private string _saveFolderName = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private bool _isSaving;

        // Computed Properties
        public int CurrentWavelengthIndex => SelectedWavelengthIndex?.NumericValue ?? Options.DefaultWavelengthIndex;
        public int CurrentWorkingDistance => SelectedWorkingDistance?.NumericValue ?? Options.DefaultWorkingDistance;
        public int CropWidth => Options.CropWidthSize;
        public int CropHeight => Options.CropHeightSize;

        public CameraViewModel(AppService service) : base(service)
        {
            _cameraService = service.Camera ?? throw new ArgumentNullException();
            _imageProcessService = service.ImageProcess;
            _workspaceService = service.WorkSpace;
            _storageService = service.Storage;
            _uiThrottler = _service.Ui.CreateThrottler();

            SelectedWavelengthIndex = WavelengthIndexs.FirstOrDefault();
            SelectedWorkingDistance = WorkingDistances.FirstOrDefault();

            _workspaceService.Update(ws => ws.SetWorkingDistance(CurrentWorkingDistance));
        }

        partial void OnSelectedWavelengthIndexChanged(ComboBoxData? oldValue, ComboBoxData? newValue) => UpdateDisplayIfStopped();
        partial void OnSelectedWorkingDistanceChanged(ComboBoxData? oldValue, ComboBoxData? newValue) => UpdateDisplayIfStopped();

        private void UpdateDisplayIfStopped()
        {
            if (!IsPreviewing)
                _service.Ui.InvokeAsync(() => DisplayWorkspaceImage(CurrentWavelengthIndex, CurrentWorkingDistance));
        }

        [RelayCommand]
        public async Task AddRegion(Rect rect)
        {
            _workspaceService.Update(ws => ws.AddRegionData(rect));
            if (!IsPreviewing) await CalculateIntensityDatasAsync();
        }

        [RelayCommand]
        public async Task RemoveRegion(RegionData region)
        {
            if (region == null) return;
            _workspaceService.Update(ws => ws.RemoveRegionData(region));
            if (!IsPreviewing) await CalculateIntensityDatasAsync();
        }

        [RelayCommand]
        private void ApplyImageProcess()
        {
            IsProcessApply = true;
        }

        [RelayCommand]
        private void StopImageProcess()
        {
            IsProcessApply = false;
        }

        [RelayCommand]
        private async Task RefreshCamerasAsync()
        {
            await RunOperationAsync("RefreshCameras", async (ct, ctx) =>
            {
                var list = await _cameraService.GetCameraListAsync(ct);
                await UiInvokeAsync(() =>
                {
                    Cameras.Clear();
                    foreach (var c in list) Cameras.Add(c);
                    SelectedCamera ??= Cameras.FirstOrDefault();
                });
            });
        }

        [RelayCommand]
        private async Task StartPreviewAsync()
        {
            if (IsPreviewing) return;
            if (Cameras.Count == 0 || SelectedCamera == null)
            {
                await RefreshCamerasAsync();
                if (SelectedCamera == null) return;
            }

            await RunOperationAsync("PreviewStart", async (ct, ctx) =>
            {
                bool isConnected = false;
                while (!ct.IsCancellationRequested && !isConnected)
                {
                    try
                    {
                        await _cameraService.StartPreviewAsync(ct, SelectedCamera.Id);
                        isConnected = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Start Failed: {ex.Message}. Resetting...");
                        try { await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None); } catch { }
                        try { await Task.Delay(1000, ct); } catch { break; }
                    }
                }
                if (isConnected)
                {
                    RestartConsumeLoop();
                    await UiInvokeAsync(() => IsPreviewing = true);
                }
            });
        }

        [RelayCommand]
        private async Task StopPreviewAsync()
        {
            if (!IsPreviewing) return;
            await RunOperationAsync("PreviewStop", async (ct, ctx) =>
            {
                try
                {
                    _stopRequested = true;
                    CancelConsumeLoop();
                    if (_consumeTask != null) await Task.WhenAny(_consumeTask, Task.Delay(2000));
                    await _cameraService.StopPreviewAndDisconnectAsync(ct);
                }
                catch (Exception ex) { Debug.WriteLine($"Stop failed: {ex.Message}"); }
                finally
                {
                    await UiInvokeAsync(() => {
                        IsPreviewing = false;
                        DisplayWorkspaceImage(CurrentWavelengthIndex, CurrentWorkingDistance);
                    });
                }
            });
        }

        private bool IsCanSave()
        {
            return !IsSaving && !string.IsNullOrWhiteSpace(SaveFolderName);
        }

        [RelayCommand(CanExecute = nameof(IsCanSave))]
        private async Task SaveAsync()
        {
            var currentWs = _workspaceService.Current;
            if (currentWs == null) return;

            FrameData? safeSnapshot = null;

            lock (_workspaceService)
            {
                var sourceRef = currentWs.EntireFrameData;
                if (sourceRef != null)
                {
                    safeSnapshot = _imageProcessService.CloneFrameData(sourceRef);
                }
                else
                {
                    Debug.WriteLine("[Save] 저장할 데이터가 없습니다.");
                }
            }

            if (safeSnapshot is null) return;

            await UiInvokeAsync(() => IsSaving = true);

            await RunOperationAsync("SaveExperiment", async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("Saving...");

                int currentWd = currentWs.WorkingDistance;
                var disposables = new List<IDisposable>();
                disposables.Add(safeSnapshot);

                Bitmap? fullBmp = null;
                Bitmap? colorBmp = null;
                var cropBitmaps = new Dictionary<string, Bitmap>();

                try
                {
                    // 1. Calculations (Background Thread)
                    var cropFrames = _imageProcessService.GetCropFrameDatas(safeSnapshot, currentWd);
                    foreach (var f in cropFrames) disposables.Add(f);

                    var wavelengths = Options.GetWavelengthList();
                    var waveToIndex = Options.GetWavelengthIndexMap();

                    // [핵심 수정] Bitmap 생성은 반드시 UI Thread에서 수행
                    await UiInvokeAsync(() =>
                    {
                        foreach (var wl in wavelengths)
                        {
                            if (!waveToIndex.TryGetValue(wl, out int idx)) continue;
                            if (idx >= cropFrames.Count) continue;

                            var cropBmp = _imageProcessService.CreateBitmapFromFrame(cropFrames[idx]);
                            if (cropBmp != null) cropBitmaps[$"{wl}nm"] = cropBmp;
                        }
                    });

                    // 2. RGB Calculation (Background)
                    FrameData? rgbFrame = null;
                    try
                    {
                        rgbFrame = _imageProcessService.GetRgbFrameDataFromCropFrames(cropFrames);
                        if (rgbFrame != null) disposables.Add(rgbFrame);
                    }
                    catch { }

                    // RGB Bitmap (UI Thread)
                    if (rgbFrame != null)
                    {
                        await UiInvokeAsync(() =>
                        {
                            colorBmp = _imageProcessService.CreateBitmapFromFrame(rgbFrame, PixelFormats.Bgr24);
                        });
                    }

                    // 3. Stitching (Background - Heavy)
                    var stitchedFrame = _imageProcessService.GetStitchFrameData(safeSnapshot, cropFrames, currentWd);
                    if (stitchedFrame != null) disposables.Add(stitchedFrame);

                    // Stitched Bitmap (UI Thread)
                    await UiInvokeAsync(() =>
                    {
                        if (stitchedFrame != null)
                        {
                            fullBmp = _imageProcessService.CreateBitmapFromFrame(stitchedFrame);
                        }

                        // Fallback
                        if (fullBmp == null)
                        {
                            Debug.WriteLine("[Save] Fallback to original frame.");
                            fullBmp = _imageProcessService.CreateBitmapFromFrame(safeSnapshot);
                        }
                    });

                    // 4. CSV (Background)
                    var sb = new StringBuilder();
                    sb.AppendLine("RegionIndex,Wavelength,Mean,StdDev");
                    if (currentWs.IntensityDataMap != null)
                    {
                        foreach (var kvp in currentWs.IntensityDataMap.OrderBy(k => k.Key))
                        {
                            foreach (var data in kvp.Value)
                                sb.AppendLine($"{kvp.Key},{data.wavelength},{data.mean},{data.stddev}");
                        }
                    }

                    // 5. Save to Disk (UI Thread)

                    if (fullBmp == null) return;

                    await _storageService.SaveExperimentResultAsync(SaveFolderName, fullBmp, colorBmp, cropBitmaps, sb.ToString(), ct).ConfigureAwait(false); ;

                    await UiInvokeAsync(() =>
                    {
                        SaveFolderName = $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}";
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Save Error] {ex}");
                }
                finally
                {
                    fullBmp?.Dispose();
                    colorBmp?.Dispose();
                    foreach (var bmp in cropBitmaps.Values) bmp.Dispose();
                    foreach (var d in disposables) d.Dispose();
                }
            });
        }

        private async Task CalculateIntensityDatasAsync()
        {
            await RunOperationAsync("CalculateIntensityDatas", async (ct, ctx) =>
            {
                var currentWorkspace = _workspaceService.Current;
                if (currentWorkspace?.EntireFrameData is null) return;

                var intensityMap = _imageProcessService.ComputeIntensityDataMap(
                    currentWorkspace.EntireFrameData,
                    currentWorkspace.RegionDatas.ToList(),
                    CurrentWorkingDistance);

                _workspaceService.Update(ws => ws.UpdateIntensityDataMap(intensityMap));
            });
        }

        private void RestartConsumeLoop()
        {
            CancelConsumeLoop();
            _stopRequested = false;
            _consumeCts = new CancellationTokenSource();
            _consumeTask = Task.Run(() => ConsumeFramesAsync(_consumeCts.Token));
        }

        private void CancelConsumeLoop() => _consumeCts?.Cancel();

        private async Task ConsumeFramesAsync(CancellationToken ct)
        {
            var reader = _cameraService.Frames;
            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var frame))
                    {
                        if (_stopRequested) { frame.Dispose(); continue; }

                        _imageProcessService.ProcessFrame(frame);
                        var fullFrameClone = _imageProcessService.CloneFrameData(frame);

                        lock (_workspaceService)
                        {
                            _workspaceService.SetEntireFrame(fullFrameClone);
                        }

                        var previewCrop = _imageProcessService.GetCropFrameData(frame, CurrentWavelengthIndex, CurrentWorkingDistance);
                        var oldPreview = Interlocked.Exchange(ref _previewFrameData, previewCrop);
                        oldPreview?.Dispose();

                        var rgbFrame = _imageProcessService.GetRgbFrameData(frame, CurrentWorkingDistance);
                        var oldRgb = Interlocked.Exchange(ref _rgbFrameData, rgbFrame);
                        oldRgb?.Dispose();


                        int wd = CurrentWorkingDistance;
                        var processedFrame = ImageCalculator.Evaluate(ExpressionText, (index) =>
                        {
                            return _imageProcessService.GetCropFrameData(frame, index, wd);
                        });

                        // 2. 결과 비트맵 업데이트 및 이벤트 발생
                        if (processedFrame != null)
                        {
                            var old = Interlocked.Exchange(ref _processedFrameData, processedFrame);
                            old?.Dispose();
                        }

                        _uiThrottler.Run(UpdateUI);

                        if (Interlocked.CompareExchange(ref _isAnalyzing, 1, 0) == 0)
                        {
                            var analysisFrame = _imageProcessService.CloneFrameData(frame);
                            _ = Task.Run(() => RunAnalysisAndUnlock(analysisFrame));
                        }
                        frame.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine(ex); }
            finally { CleanupPendingFrames(); }
        }

        private void RunAnalysisAndUnlock(FrameData frame)
        {
            try
            {
                var regions = _workspaceService.Current?.RegionDatas?.ToList();
                if (regions != null)
                {
                    var map = _imageProcessService.ComputeIntensityDataMap(frame, regions, CurrentWorkingDistance);
                    _workspaceService.Update(ws => ws.UpdateIntensityDataMap(map));
                }
            }
            catch { }
            finally
            {
                frame.Dispose();
                Interlocked.Exchange(ref _isAnalyzing, 0);
            }
        }

        private void CleanupPendingFrames()
        {
            var p = Interlocked.Exchange(ref _previewFrameData, null); p?.Dispose();
            var a = Interlocked.Exchange(ref _rgbFrameData, null); a?.Dispose();
            var proc = Interlocked.Exchange(ref _processedFrameData, null); proc?.Dispose();
            _uiThrottler.Reset();
            Interlocked.Exchange(ref _isAnalyzing, 0);
        }

        private void UpdateUI()
        {
            if (!IsPreviewing) return;
            UpdatePreviewUI();
            UpdateRGBUI();
            UpdateProcessedUI();
        }

        private void UpdateProcessedUI()
        {
            if (!IsProcessApply) return;
            var frame = Interlocked.Exchange(ref _processedFrameData, null);
            if (frame is null) return;
            try
            {
                EnsureProcessedBitmap(frame.Width, frame.Height);
                if (_processedBitmap != null)
                {
                    _imageProcessService.ConvertFrameDataToWriteableBitmap(_processedBitmap, frame);
                    ProcessedPreviewInvalidated?.Invoke();
                    OnPropertyChanged(nameof(ProcessedBitmap));
                }
            }
            finally { frame.Dispose(); }
        }

        private void UpdatePreviewUI()
        {
            if (!IsPreviewing) return;
            var frame = Interlocked.Exchange(ref _previewFrameData, null);
            if (frame is null) return;
            try
            {
                EnsureSharedPreview(frame.Width, frame.Height);
                if (_rawpreviewBitmap != null)
                {
                    _imageProcessService.ConvertFrameDataToWriteableBitmap(_rawpreviewBitmap, frame);
                    RawPreviewInvalidated?.Invoke();
                }
            }
            finally { frame.Dispose(); }
        }

        private void UpdateRGBUI()
        {
            if (!IsPreviewing) return;
            var frame = Interlocked.Exchange(ref _rgbFrameData, null);
            if (frame is null) return;
            try
            {
                EnsureRgbBitmap(frame.Width, frame.Height);
                if (_rgbBitmap != null)
                {
                    _imageProcessService.ConvertRgbFrameDataToWriteableBitmap(_rgbBitmap, frame);
                    RgbPreviewInvalidated?.Invoke();
                }
            }
            finally { frame.Dispose(); }
        }

        private void DisplayWorkspaceImage(int index, int wd)
        {
            lock (_workspaceService)
            {
                var ws = _workspaceService.Current;
                var frame = ws?.EntireFrameData;

                if (frame is null) return;

                var crop = _imageProcessService.GetCropFrameData(frame, index, wd);
                try
                {
                    EnsureSharedPreview(crop.Width, crop.Height);
                    if (_rawpreviewBitmap is not null)
                    {
                        _imageProcessService.ConvertFrameDataToWriteableBitmap(_rawpreviewBitmap, crop);
                        RawPreviewInvalidated?.Invoke();
                    }
                }
                finally { crop.Dispose(); }
            }
        }

        private void EnsureSharedPreview(int width, int height)
        {
            if (_rawpreviewBitmap is not null)
            {
                var ps = _rawpreviewBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height) return;
                _rawpreviewBitmap.Dispose();
                _rawpreviewBitmap = null;
            }
            _rawpreviewBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Gray8, AlphaFormat.Opaque);
            RawBitmap = _rawpreviewBitmap;
        }

        private void EnsureRgbBitmap(int width, int height)
        {
            if (_rgbBitmap is not null)
            {
                var ps = _rgbBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height) return;
                _rgbBitmap.Dispose();
                _rgbBitmap = null;
            }
            _rgbBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgr24, AlphaFormat.Opaque);
            RgbBitmap = _rgbBitmap;
        }

        private void EnsureProcessedBitmap(int width, int height)
        {
            if (_processedBitmap is not null)
            {
                var ps = _processedBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height) return;
                _processedBitmap.Dispose();
                _processedBitmap = null;
            }
            _processedBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Gray8, AlphaFormat.Opaque);
            ProcessedBitmap = _processedBitmap;
        }

        public override async ValueTask DisposeAsync()
        {
            CancelConsumeLoop();
            if (_consumeTask != null) await _consumeTask.ConfigureAwait(false);
            CleanupPendingFrames();
            try { await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None); } catch { }
            _rawpreviewBitmap?.Dispose();
            _rgbBitmap?.Dispose();
            _processedBitmap?.Dispose();
            await base.DisposeAsync();
        }
    }
}