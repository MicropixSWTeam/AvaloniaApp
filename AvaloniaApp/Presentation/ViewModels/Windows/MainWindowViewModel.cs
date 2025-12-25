using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure.Service;
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
        private readonly LogoViewModel _logoViewModel;

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
            ProcessViewModel processViewModel,
            LogoViewModel logoViewModel
            ) : base(service)
        {
            _rawImageViewModel = rawImage;
            _rgbImageViewModel = rgbImage;
            _cameraViewModel = cameraViewModel;
            _chartViewModel = chartViewModel;
            _cameraSettingViewModel = cameraSettingViewModel;
            _processViewModel = processViewModel;
            _logoViewModel = logoViewModel;

            _topLeftContent = _cameraSettingViewModel;
            _topCenterContent = _rawImageViewModel;
            _topRightContent = _chartViewModel;
            _bottomLeftContent = _logoViewModel;
            _bottomCenterContent = _rgbImageViewModel;
            _bottomRightContent = _processViewModel;
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

        [RelayCommand]
        public async Task OnClickSaveButtonAsync()
        {

            // 1. 저장할 데이터가 있는지 확인
            var currentWs = _service.WorkSpace.Current;
            if (currentWs?.EntireFrameData == null)
            {
                await _service.Popup.ShowMessageAsync("알림", "저장할 데이터가 없습니다.", DialogType.Info);
                return;
            }

            // 2. 파일명 입력 팝업 띄우기
            string? fileName = await _service.Popup.ShowCustomAsync<InputDialogViewModel, string>(vm =>
            {
                // InputViewModel 초기화 (제목, 메시지, 기본값)
                vm.Init("데이터 저장", "저장할 파일(폴더) 이름을 입력하세요.", $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}");
            });

            // 취소했거나 빈 값이면 중단
            if (string.IsNullOrWhiteSpace(fileName)) return;

            // 3. 데이터 스냅샷 (Snapshot) 만들기
            // 카메라가 계속 돌고 있을 수 있으므로, 현재 시점의 데이터를 복제해둡니다.
            FrameData? safeSnapshot = null;
            lock (_service.WorkSpace)
            {
                if (currentWs.EntireFrameData != null)
                    safeSnapshot = _service.ImageProcess.CloneFrameData(currentWs.EntireFrameData);
            }

            if (safeSnapshot == null) return;

            // 4. 실제 저장 작업 실행 (PerformSaveAsync 호출)
            await PerformSaveAsync(fileName, safeSnapshot, currentWs.WorkingDistance, currentWs.IntensityDataMap);
        }
        [RelayCommand]
        public async Task OnClickLoadButtonAsync()
        {
            // 1. LoadViewModel 팝업 띄우기 (Factory 생성)
            string? folderName = await _service.Popup.ShowCustomAsync<LoadDialogViewModel, string>();

            if (string.IsNullOrEmpty(folderName)) return;

            await RunOperationAsync("LoadImage", async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("Loading...");

                // StorageService에 로드 요청
                var frame = await _service.Storage.LoadFullImageAsFrameDataAsync(folderName);

                if (frame != null)
                {
                    // Workspace에 데이터 설정 (화면 갱신됨)
                    _service.WorkSpace.SetEntireFrame(frame);

                    // 성공 알림
                    await _service.Ui.InvokeAsync(() =>
                        _service.Popup.ShowMessageAsync("완료", "불러오기 성공!", DialogType.Complete));
                }
                else
                {
                    // 실패 알림
                    await _service.Ui.InvokeAsync(() =>
                        _service.Popup.ShowMessageAsync("오류", "이미지 파일이 없습니다.", DialogType.Error));
                }
            });
        }
        /// <summary>
        /// 실제 저장 로직 (이미지 가공, CSV 생성, 파일 쓰기)
        /// </summary>
        private async Task PerformSaveAsync(string folderName, FrameData snapshot, int workingDistance, IReadOnlyDictionary<int, IntensityData[]>? intensityMap)
        {
            await RunOperationAsync("SaveExperiment", async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("Processing & Saving...");

                // 메모리 해제를 위한 리스트
                var disposables = new List<IDisposable> { snapshot };

                Bitmap? fullBmp = null;
                Bitmap? colorBmp = null;
                var cropBitmaps = new Dictionary<string, Bitmap>();

                try
                {
                    // A. Crop 이미지 생성 (파장별)
                    var cropFrames = _service.ImageProcess.GetCropFrameDatas(snapshot, workingDistance);
                    foreach (var f in cropFrames) disposables.Add(f);

                    var wavelengths = Options.GetWavelengthList();
                    var waveToIndex = Options.GetWavelengthIndexMap();

                    // UI 스레드에서 비트맵 생성 (Avalonia 제약)
                    await _service.Ui.InvokeAsync(() =>
                    {
                        foreach (var wl in wavelengths)
                        {
                            if (waveToIndex.TryGetValue(wl, out int idx) && idx < cropFrames.Count)
                            {
                                var bmp = _service.ImageProcess.CreateBitmapFromFrame(cropFrames[idx]);
                                if (bmp != null) cropBitmaps[$"{wl}nm"] = bmp;
                            }
                        }
                    });

                    // B. RGB 이미지 생성
                    try
                    {
                        var rgbFrame = _service.ImageProcess.GetRgbFrameDataFromCropFrames(cropFrames);
                        if (rgbFrame != null)
                        {
                            disposables.Add(rgbFrame);
                            await _service.Ui.InvokeAsync(() =>
                                colorBmp = _service.ImageProcess.CreateBitmapFromFrame(rgbFrame, Avalonia.Platform.PixelFormats.Bgr24));
                        }
                    }
                    catch { /* RGB 생성 실패 시 무시 */ }

                    // C. 전체(Stitched) 이미지 생성
                    var stitchedFrame = _service.ImageProcess.GetStitchFrameData(snapshot, cropFrames, workingDistance);
                    if (stitchedFrame != null) disposables.Add(stitchedFrame);

                    await _service.Ui.InvokeAsync(() =>
                    {
                        if (stitchedFrame != null)
                            fullBmp = _service.ImageProcess.CreateBitmapFromFrame(stitchedFrame);

                        // Stitch 실패 시 원본 저장
                        if (fullBmp == null)
                            fullBmp = _service.ImageProcess.CreateBitmapFromFrame(snapshot);
                    });

                    // D. CSV 데이터 생성
                    var sb = new StringBuilder();
                    sb.AppendLine("RegionIndex,Wavelength,Mean,StdDev");
                    if (intensityMap != null)
                    {
                        foreach (var kvp in intensityMap.OrderBy(k => k.Key))
                        {
                            foreach (var data in kvp.Value)
                                sb.AppendLine($"{kvp.Key},{data.wavelength},{data.mean},{data.stddev}");
                        }
                    }

                    // E. 파일 쓰기 (StorageService 호출)
                    if (fullBmp != null)
                    {
                        await _service.Storage.SaveExperimentResultAsync(
                            folderName, fullBmp, colorBmp, cropBitmaps, sb.ToString(), ct);

                        // 완료 알림
                        await _service.Ui.InvokeAsync(() =>
                            _service.Popup.ShowMessageAsync("완료", "저장이 완료되었습니다.", DialogType.Complete));
                    }
                }
                catch (Exception ex)
                {
                    // 에러 알림
                    await _service.Ui.InvokeAsync(() =>
                        _service.Popup.ShowMessageAsync("오류", $"저장 중 문제 발생: {ex.Message}", DialogType.Error));
                }
                finally
                {
                    // F. 리소스 정리 (필수!)
                    fullBmp?.Dispose();
                    colorBmp?.Dispose();
                    foreach (var b in cropBitmaps.Values) b.Dispose();
                    foreach (var d in disposables) d.Dispose();
                }
            });
        }
    }
}