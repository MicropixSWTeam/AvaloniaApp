using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
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
        private bool normalizePreviewEnabled = true;

        // normalize 타겟 밝기
        [ObservableProperty]
        private double targetIntensity = 128.0;

        // 거리 선택 (0 = translation 없음)
        [ObservableProperty]
        private int selectedDistance = 0;

        [ObservableProperty]
        private double selectedRegionMean;

        [ObservableProperty]
        private double selectedRegionStdDev;
        public string Title { get; set; } = "Camera Setting";
        public int Width { get; set; } = 900;
        public int Height { get; set; } = 600;
        // UI에서 바인딩할 거리 목록
        public IReadOnlyList<int> AvailableDistances { get; } =
            new[] { 0, 10, 20, 30, 40 };

        private readonly CameraPipeline _cameraPipeline;
        private readonly ImageProcessService _imageProcessService;

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
            ImageProcessService imageProcessService) : base()
        {
            _cameraPipeline = cameraPipeline;
            _imageProcessService = imageProcessService;
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

    }
}
