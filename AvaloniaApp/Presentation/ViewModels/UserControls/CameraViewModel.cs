using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public sealed class RoiSelection
    {
        public int Index { get; }
        public Rect ControlRect { get; } // 컨트롤 좌표계의 사각형 (그대로 캔버스에 그림)
        public double Mean { get; }
        public double StdDev { get; }

        public string Label => $"R{Index + 1}";

        public RoiSelection(int index, Rect controlRect, double mean, double stdDev)
        {
            Index = index;
            ControlRect = controlRect;
            Mean = mean;
            StdDev = stdDev;
        }
    }
    public partial class CameraViewModel : ViewModelBase
    {
        // 카메라에서 들어오는 전체 프레임 (스트리밍)
        [ObservableProperty]
        private Bitmap? image;

        // 프리뷰용 원본 crop 타일 이미지 (translation 반영)
        [ObservableProperty]
        private Bitmap? croppedPreviewImage;

        // 프리뷰용 normalize된 타일 이미지 (translation + normalize)
        [ObservableProperty]
        private Bitmap? normalizedPreviewImage;

        // -1이면 전체, 0 이상이면 해당 타일 인덱스
        [ObservableProperty]
        private int selectedCropIndex = -1;

        [ObservableProperty]
        private string selectedCropIndexText = "-1";

        // 프리뷰 normalize on/off
        [ObservableProperty]
        private bool normalizePreviewEnabled = false;

        // normalize 타겟 밝기
        [ObservableProperty]
        private double targetIntensity = 128.0;

        [ObservableProperty]
        private int selectedDistance = 0;
        
        [ObservableProperty]
        private bool isStop = false;

        // 최근 선택 영역(컨트롤 좌표)
        private Rect _selectionRectInControl;
        public Rect SelectionRectInControl
        {
            get => _selectionRectInControl;
            set => SetProperty(ref _selectionRectInControl, value);
        }

        // 컨트롤 실제 렌더 크기 (CameraView 코드비하인드에서 업데이트)
        private Size _imageControlSize;
        public Size ImageControlSize
        {
            get => _imageControlSize;
            set => SetProperty(ref _imageControlSize, value);
        }

        [ObservableProperty]
        private double selectedRegionMean;

        [ObservableProperty]
        private double selectedRegionStdDev;

        // 여러 개 ROI
        public ObservableCollection<RoiSelection> RoiSelections { get; } = new();

        public string Title { get; set; } = "Camera Setting";
        public int Width { get; set; } = 900;
        public int Height { get; set; } = 600;

        // UI에서 바인딩할 거리 목록
        public IReadOnlyList<int> AvailableDistances { get; } =
            new[] { 0, 10, 20, 30, 40 };

        private readonly CameraPipeline _cameraPipeline;
        private readonly ImageProcessService _imageProcessService;
        private readonly ImageProcessPipeline _imageProcessPipeline;
        private readonly DrawRectService _drawRectService;
        private readonly StorageService _storageService;
        // grid 설정 (현재 카메라 해상도 기준 5x3, 1064x1012)
        private readonly CropGridConfig _gridConfig =
            new CropGridConfig(
                RowSize: 1012,
                ColSize: 1064,
                RowGap: 1012,
                ColGap: 1064,
                RowCount: 3,
                ColCount: 5);

        private bool _gridConfigured;
        private PixelSize _gridFrameSize;

      public CameraViewModel(
        CameraPipeline cameraPipeline,
        ImageProcessService imageProcessService,
        DrawRectService drawRectService,
        StorageService storageService,
        ImageProcessPipeline imageProcessPipeline) : base()
    {
        _cameraPipeline = cameraPipeline;
        _imageProcessService = imageProcessService;
        _drawRectService = drawRectService;
        _storageService = storageService;
        _imageProcessPipeline = imageProcessPipeline;
    }

        /// <summary>
        /// 실제 화면에 바인딩할 이미지
        /// - SelectedCropIndex >= 0: 타일 프리뷰
        ///   - NormalizePreviewEnabled && NormalizedPreviewImage != null → normalize된 타일
        ///   - 아니면 CroppedPreviewImage
        /// - SelectedCropIndex < 0: 전체 Image
        /// </summary>
        public Bitmap? DisplayImage
        {
            get
            {
                if (SelectedCropIndex >= 0)
                {
                    if (NormalizePreviewEnabled && NormalizedPreviewImage is not null)
                        return NormalizedPreviewImage;
                    if (CroppedPreviewImage is not null)
                        return CroppedPreviewImage;
                }

                return Image;
            }
        }

        // ===== ObservableProperty partials =====

        partial void OnImageChanging(Bitmap? value)
        {
            image?.Dispose();
        }

        partial void OnImageChanged(Bitmap? value)
        {
            if (Image is not null)
                EnsureGridConfigured(Image);

            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        partial void OnCroppedPreviewImageChanging(Bitmap? value)
        {
            croppedPreviewImage?.Dispose();
        }

        partial void OnNormalizedPreviewImageChanging(Bitmap? value)
        {
            normalizedPreviewImage?.Dispose();
        }

        partial void OnSelectedCropIndexChanged(int value)
        {
            SelectedCropIndexText = value.ToString();
            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        partial void OnSelectedCropIndexTextChanged(string value)
        {
            if (!int.TryParse(value, out var idx))
                return;

            if (idx < -1)
                idx = -1;

            if (idx != SelectedCropIndex)
                SelectedCropIndex = idx;
        }

        partial void OnNormalizePreviewEnabledChanged(bool value)
        {
            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        partial void OnTargetIntensityChanged(double value)
        {
            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        partial void OnSelectedDistanceChanged(int value)
        {
            // 거리 변경 시 translation이 달라지므로 프리뷰 갱신
            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        // ===== Commands =====

        [RelayCommand]
        public async Task StartPreviewAsync()
        {
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueStartPreviewAsync(
                    ct,
                    async bmp =>
                    {
                        Image = bmp;
                        IsStop = false;
                        await Task.CompletedTask;
                    });
            });
        }

        [RelayCommand]
        public async Task StopPreviewAsync()
        {
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueStopPreviewAsync(ct);
                IsStop = true;
            });
        }

        /// <summary>
        /// 다음 타일로 이동하는 버튼 (&gt;)
        /// -1 → 0 → 1 → ... → (마지막)
        /// </summary>
        [RelayCommand]
        private void NextIndex()
        {
            if (!_gridConfigured || _imageProcessService.TileCount == 0)
                return;

            int max = _imageProcessService.TileCount - 1;

            if (SelectedCropIndex < 0)
            {
                SelectedCropIndex = 0;
            }
            else if (SelectedCropIndex < max)
            {
                SelectedCropIndex++;
            }
        }

        /// <summary>
        /// 이전 타일로 이동하는 버튼 (&lt;)
        /// ... → 2 → 1 → 0 → -1
        /// </summary>
        [RelayCommand]
        private void PrevIndex()
        {
            if (!_gridConfigured || _imageProcessService.TileCount == 0)
                return;

            if (SelectedCropIndex > 0)
            {
                SelectedCropIndex--;
            }
            else if (SelectedCropIndex == 0)
            {
                SelectedCropIndex = -1; // 전체 이미지로 돌아가기
            }
        }
        [RelayCommand]
        private async Task SaveDisplayImageAsync()
        {
            // 화면에 표시 중인 이미지가 없으면 무시
            var bmp = DisplayImage as Bitmap;
            if (bmp is null)
                return;

            await RunSafeAsync(async ct =>
            {
                await _storageService.SaveBitmapWithDialogAsync(
                    bmp,
                    ".png",   // 기본 제안 파일명
                    ct);
            });
        }
        // ===== 내부 헬퍼 =====

        private void EnsureGridConfigured(Bitmap frame)
        {
            var size = frame.PixelSize;

            if (!_gridConfigured || size != _gridFrameSize)
            {
                _imageProcessService.ConfigureGrid(size, _gridConfig);
                _gridConfigured = true;
                _gridFrameSize = size;
            }
        }

        /// <summary>
        /// 현재 Image, SelectedCropIndex, SelectedDistance, TargetIntensity, NormalizePreviewEnabled
        /// 를 기반으로 프리뷰 타일 이미지들을 갱신.
        /// </summary>
        private void UpdatePreviewImages()
        {
            CroppedPreviewImage = null;
            NormalizedPreviewImage = null;

            if (Image is null)
                return;

            if (SelectedCropIndex < 0)
                return; // 전체 이미지 모드

            EnsureGridConfigured(Image);

            if (SelectedCropIndex >= _imageProcessService.TileCount)
                return;

            // 거리 + 인덱스 기반 translation 오프셋
            var transform = GetTileTransform(SelectedDistance, SelectedCropIndex);

            // 원본 타일 (translation 적용)
            var rawTile = _imageProcessService.CropTile(Image, SelectedCropIndex, transform);
            CroppedPreviewImage = rawTile;

            // normalize 프리뷰
            if (NormalizePreviewEnabled)
            {
                var ti = (byte)System.Math.Clamp(
                    (int)System.Math.Round(TargetIntensity),
                    0,
                    255);

                var normTile = _imageProcessService.NormalizeTile(Image, SelectedCropIndex, transform, ti);
                NormalizedPreviewImage = normTile;
            }
        }

        /// <summary>
        /// 거리 + 타일 인덱스에 대응하는 translation 값.
        /// - 거리 <= 0 or 테이블에 없음 → (0,0)
        /// - 해당 타일 인덱스 데이터 없음 (예: 가운데 7번) → (0,0)
        /// </summary>
        private static TileTransform GetTileTransform(int distance, int tileIndex)
        {
            if (distance <= 0)
                return new TileTransform(0, 0);

            if (!TranslationOffsets.Table.TryGetValue(distance, out var perTile))
                return new TileTransform(0, 0);

            if (!perTile.TryGetValue(tileIndex, out var t))
                return new TileTransform(0, 0);

            return t;
        }
        // CameraViewModel 클래스 내부, 아무 데나 메서드로 추가
        public void UpdateSelectionStats()
        {
            // 현재 화면에 보이는 비트맵 (전체 or 타일)
            var bmp = DisplayImage;
            if (bmp is null)
            {
                SelectedRegionMean = 0;
                SelectedRegionStdDev = 0;
                return;
            }

            var stats = _drawRectService.GetYStatsFromSelection(
                bmp,
                SelectionRectInControl,
                ImageControlSize);

            if (stats is { } s)
            {
                SelectedRegionMean = s.mean;
                SelectedRegionStdDev = s.stdDev;
            }
            else
            {
                SelectedRegionMean = 0;
                SelectedRegionStdDev = 0;
            }
        }
        /// <summary>
        /// 현재 SelectionRectInControl을 기준으로
        /// DisplayImage에서 Y 평균/표준편차를 구하고
        /// RoiSelections에 1개 ROI를 추가한다.
        /// </summary>
        public void CommitSelectionRect()
        {
            var bmp = DisplayImage;
            if (bmp is null)
            {
                SelectedRegionMean = 0;
                SelectedRegionStdDev = 0;
                return;
            }

            // 컨트롤 크기는 CameraView 코드비하인드에서 계속 갱신해준다.
            var stats = _drawRectService.GetYStatsFromSelection(
                bmp,
                SelectionRectInControl,
                ImageControlSize);

            if (stats is not { } s)
            {
                SelectedRegionMean = 0;
                SelectedRegionStdDev = 0;
                return;
            }

            SelectedRegionMean = s.mean;
            SelectedRegionStdDev = s.stdDev;

            // 새 ROI 추가 (ControlRect는 그대로 사용, ImageRect는 필요하면 나중에 추가)
            var index = RoiSelections.Count;
            var roi = new RoiSelection(index, SelectionRectInControl, s.mean, s.stdDev);
            RoiSelections.Add(roi);
        }
        [RelayCommand]
        private void ClearRois()
        {
            RoiSelections.Clear();
            SelectedRegionMean = 0;
            SelectedRegionStdDev = 0;
        }

        [RelayCommand]
        private void RemoveLastRoi()
        {
            if (RoiSelections.Count == 0) return;

            RoiSelections.RemoveAt(RoiSelections.Count - 1);

            if (RoiSelections.Count > 0)
            {
                var last = RoiSelections[^1];
                SelectedRegionMean = last.Mean;
                SelectedRegionStdDev = last.StdDev;
            }
            else
            {
                SelectedRegionMean = 0;
                SelectedRegionStdDev = 0;
            }
        }

    }
}
