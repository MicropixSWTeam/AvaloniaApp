// AvaloniaApp.Presentation/ViewModels/UserControls/CameraViewModel.cs
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel : ViewModelBase, IPopup
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

        // normalize 타겟 밝기 (0~255)
        [ObservableProperty]
        private byte targetIntensity = 128;

        // 거리 선택 (0 = translation 없음)
        [ObservableProperty]
        private int selectedDistance = 40;
        public ObservableCollection<int> CropIndexOptions { get; } = new();

        // Stop 상태
        [ObservableProperty]
        private bool isStop = false;

        /// <summary>
        /// 실제 드로잉 가능 여부 = Stop 상태 + Draw 모드
        /// </summary>
        public bool CanDrawRegions => IsStop && !_analysis.IsLimitReached;
        // 최근 드래그 중인 선택 영역(컨트롤 좌표)
        private Rect _selectionRectInControl;
        public Rect SelectionRectInControl
        {
            get => _selectionRectInControl;
            set => SetProperty(ref _selectionRectInControl, value);
        }

        // 컨트롤 실제 렌더 크기 (SelectionCanvas 크기)
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

        public string Title { get; set; } = "Camera View";
        public int Width { get; set; } = 900;
        public int Height { get; set; } = 600;

        // UI에서 바인딩할 거리 목록
        public IReadOnlyList<int> AvailableDistances { get; } =
            new[] { 0, 10, 20, 30, 40 };

        private readonly CameraPipeline _cameraPipeline;
        private readonly ImageProcessService _imageProcessService;
        private readonly DrawRectService _drawRectService;
        private readonly StorageService _storageService;
        private readonly RegionAnalysisWorkspace _analysis;

        /// <summary>
        /// XAML에서 ROI 오버레이를 위해 바인딩할 컬렉션.
        /// (_analysis.Regions 그대로 노출)
        /// </summary>
        public ObservableCollection<SelectionRegion> Regions => _analysis.Regions;
        public IBrush NextRegionBrush
            => new SolidColorBrush(RegionColorPalette.GetAvaloniaColor(_analysis.NextColorIndex));
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

        // ==== 정규화된 타일 캐시 (성능 개선용) ====
        // 같은 프레임 + 같은 거리 + 같은 TargetIntensity 에 대해서만 재사용
        private IReadOnlyList<WriteableBitmap>? _normalizedTilesCache;
        private Bitmap? _normalizedTilesSource;
        private int _normalizedTilesDistance;
        private byte _normalizedTilesTarget;

        public CameraViewModel(
            CameraPipeline cameraPipeline,
            ImageProcessService imageProcessService,
            DrawRectService drawRectService,
            StorageService storageService,
            RegionAnalysisWorkspace analysis) : base()
        {
            _cameraPipeline = cameraPipeline;
            _imageProcessService = imageProcessService;
            _drawRectService = drawRectService;
            _storageService = storageService;
            _analysis = analysis;

            _analysis.MaxRegions = 7;

            // ROI 개수 변동 시 그릴 수 있는지 여부 업데이트
            _analysis.Changed += (_, __) =>
            {
                OnPropertyChanged(nameof(NextRegionBrush));
            };
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
            // 새 프레임이 들어오므로 타일 캐시 무효화
            InvalidateNormalizedTilesCache();
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

        partial void OnTargetIntensityChanged(byte value)
        {
            // intensity 값 변경 → 타일 캐시 무효화
            InvalidateNormalizedTilesCache();

            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        partial void OnSelectedDistanceChanged(int value)
        {
            // 거리 변경 → 타일 캐시 무효화
            InvalidateNormalizedTilesCache();

            UpdatePreviewImages();
            OnPropertyChanged(nameof(DisplayImage));
        }

        partial void OnIsStopChanged(bool value)
        {
            OnPropertyChanged(nameof(CanDrawRegions));
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
                await _cameraPipeline.EnqueueStopPreviewAsync(ct, async () =>
                {
                    // 1) 스트리밍 중지 플래그
                    IsStop = true;

                    // 2) Stop 시점의 현재 프레임 기준으로
                    //    정규화+translation 타일을 미리 한 번 캐시에 빌드해서,
                    //    이후 첫 ROI 확정(CommitSelectionRect) 때는
                    //    무거운 작업을 다시 안 돌도록 함.
                    var src = Image;
                    if (src is not null)
                    {
                        try
                        {
                            _ = GetOrBuildNormalizedTiles(src);
                        }
                        catch
                        {
                            // 실패해도 CommitSelectionRect 쪽에서
                            // 필요 시 다시 시도하므로 여기서는 무시
                        }
                    }

                    await Task.CompletedTask;
                });
            });
        }
        [RelayCommand]
        public async Task SaveImageSetAsync()
        {
            // 선택: 저장은 Stop 상태에서만 허용
            if (!IsStop)
                return;

            if (Image is null)
                return;

            var src = Image;

            // Grid 보장
            EnsureGridConfigured(src);

            // 1) 현재 Image + SelectedDistance + TargetIntensity 기준으로
            //    정규화+translation된 타일들을 캐시에서 얻거나 새로 계산
            var processedTiles = GetOrBuildNormalizedTiles(src);   // IReadOnlyList<WriteableBitmap>

            // WriteableBitmap -> Bitmap 업캐스트용 리스트 (캐시 참조, 여기서 Dispose 하지 않음)
            var processedBitmaps = new List<Bitmap>(processedTiles.Count);
            foreach (var t in processedTiles)
            {
                processedBitmaps.Add(t);
            }

            // 2) 원본 crop 타일 (translation/normalize 없이 grid 기준으로만 crop)
            var originalBitmaps = new List<Bitmap>(_imageProcessService.TileCount);
            for (int i = 0; i < _imageProcessService.TileCount; i++)
            {
                var tile = _imageProcessService.CropTile(src, i); // 새 WriteableBitmap 생성
                originalBitmaps.Add(tile);
            }

            // 3) 처리된 타일들로 전체 stitched 이미지 생성
            //    (translation은 BuildNormalizedTiles 안에서 이미 적용된 상태라고 가정)
            var stitched = _imageProcessService.StitchTiles(processedBitmaps);

            try
            {
                // sessionName: null이면 StorageService 내부에서 Timestamp로 폴더 이름 생성
                await _storageService.SaveFullImageSetWithFolderDialogAsync(
                    fullImage: src,
                    originalTiles: originalBitmaps,
                    processedTiles: processedBitmaps,
                    stitched: stitched,
                    sessionName: null,
                    ct: CancellationToken.None);
            }
            finally
            {
                // stitched 는 여기서만 쓰는 임시 비트맵이므로 해제
                stitched.Dispose();

                // originalBitmaps 도 여기서만 쓰는 임시 비트맵이므로 해제
                foreach (var tile in originalBitmaps)
                {
                    tile?.Dispose();
                }

                // processedBitmaps 는 Normalize 캐시의 인스턴스를 가리키므로
                // 여기서 Dispose 하면 안 된다.
            }
        }

        [RelayCommand]
        public async Task GetTranslationOffsetsAsync()
        {
            await RunSafeAsync(async ct =>
            {
                // 1) Stop 상태 + 유효한 프레임인지 확인
                if (!IsStop)
                    return;

                if (Image is null)
                    return;

                // distance 0 은 "translation 없음" 이므로 보정할 필요가 없다.
                //if (SelectedDistance <= 0)
                //    return;

                var src = Image;

                // 2) 위상 상관 계산은 CPU 작업이므로 백그라운드 스레드에서 수행
                var offsets = await Task.Run<IReadOnlyList<TileTransform>>(() =>
                {
                    // Grid 보장
                    EnsureGridConfigured(src);

                    // (a) translation 이 전혀 적용되지 않은 정규화 타일 생성
                    var tiles = _imageProcessService.BuildNormalizedTiles(
                        src,
                        _ => new TileTransform(0, 0),   // distance=0 과 동일, translation 없음
                        TargetIntensity);

                    try
                    {
                        // (b) 가운데 타일(3x5 기준 인덱스 7)을 reference 로 사용해서
                        //     나머지 타일들의 평행 이동 오프셋 계산
                        int referenceIndex = 7;
                        if (referenceIndex < 0 || referenceIndex >= tiles.Count)
                            referenceIndex = 0;

                        return _imageProcessService.ComputePhaseCorrelationOffsets(
                            tiles,
                            referenceIndex: referenceIndex);
                    }
                    finally
                    {
                        // FFT 계산이 끝났으면 임시 타일은 즉시 해제
                        foreach (var tile in tiles)
                        {
                            tile.Dispose();
                        }
                    }
                }, ct);

                // 3) 현재 SelectedDistance 에 대한 runtime translation 테이블에 저장
                //    (정적 Table 은 건드리지 않는다)
                TranslationOffsets.SetRuntimeOffsets(SelectedDistance, offsets);

                // 4) 프리뷰용 translation ROI 캐시 재구축 + 정규화 타일 캐시 무효화
                InvalidateNormalizedTilesCache();
                _imageProcessService.RebuildTranslationPreviewTable();

                // 5) 현재 선택된 타일 프리뷰/표시 이미지 갱신
                UpdatePreviewImages();
                OnPropertyChanged(nameof(DisplayImage));

                await Task.CompletedTask;
            });
        }

        // ===== 내부 헬퍼 =====
        private int AllocateColorIndex()
        {
            // 현재 사용 중인 색 인덱스 수집
            var used = _analysis.Regions
                .Select(r => r.ColorIndex)
                .Distinct()
                .ToHashSet();

            for (int i = 0; i < 7; i++)
            {
                if (!used.Contains(i))
                    return i;   // 비어 있는 색 슬롯
            }

            // MaxRegions <= RoiColorCount 조건이면 여기 올 일이 없음. 방어적 fallback.
            return 0;
        }
        private void EnsureGridConfigured(Bitmap frame)
        {
            var size = frame.PixelSize;

            if (!_gridConfigured || size != _gridFrameSize)
            {
                _imageProcessService.ConfigureGrid(size, _gridConfig);
                _gridConfigured = true;
                _gridFrameSize = size;

                // ▼ dropdown용 인덱스 리스트 갱신 (0..TileCount-1, UI 인덱스)
                CropIndexOptions.Clear();
                for (int i = 0; i < _imageProcessService.TileCount; i++)
                {
                    CropIndexOptions.Add(i);
                }
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


            // UI 인덱스(아래→위)를 grid 인덱스(위→아래)로 변환
            int gridIndex = _imageProcessService.FlipVerticalIndex(SelectedCropIndex);


            if (gridIndex < 0 || gridIndex >= _imageProcessService.TileCount)
                return;


            // 원본 타일 (translation preview 사용)
            var rawTile = _imageProcessService.GetTranslationCropImage(Image, SelectedDistance, gridIndex);
            CroppedPreviewImage = rawTile;


            // normalize 프리뷰
            if (NormalizePreviewEnabled)
            {
                var normTile = _imageProcessService.NormalizeTile(rawTile, TargetIntensity);
                NormalizedPreviewImage = normTile;
            }
        }

        /// <summary>
        /// 거리 + 타일 인덱스에 대응하는 translation 값.
        /// - 0 이하면 (0,0)
        /// - TranslationOffsets 의 정적 Table + 런타임 테이블을 모두 고려한다.
        /// </summary>
        private static TileTransform GetTileTransform(int distance, int tileIndex)
        {
            return TranslationOffsets.GetTransformOrDefault(distance, tileIndex);
        }


        /// <summary>
        /// 정규화 타일 캐시 초기화(Dispose 포함).
        /// </summary>
        private void InvalidateNormalizedTilesCache()
        {
            if (_normalizedTilesCache is { Count: > 0 })
            {
                foreach (var tile in _normalizedTilesCache)
                    tile.Dispose();
            }

            _normalizedTilesCache = null;
            _normalizedTilesSource = null;
        }

        /// <summary>
        /// 현재 Image + SelectedDistance + TargetIntensity 조합에 대해
        /// 정규화+translation된 타일 배열을 한 번만 생성해서 캐시.
        /// </summary>
        private IReadOnlyList<WriteableBitmap> GetOrBuildNormalizedTiles(Bitmap source)
        {
            byte ti = TargetIntensity;

            if (_normalizedTilesCache is { Count: > 0 } &&
                ReferenceEquals(_normalizedTilesSource, source) &&
                _normalizedTilesDistance == SelectedDistance &&
                _normalizedTilesTarget == ti)
            {
                return _normalizedTilesCache;
            }

            // 기존 캐시 정리
            InvalidateNormalizedTilesCache();

            // Grid 설정 보장
            EnsureGridConfigured(source);

            // 거리별 translation을 적용한 정규화 타일 생성
            var tiles = _imageProcessService.BuildNormalizedTiles(
                source,
                idx => GetTileTransform(SelectedDistance, idx),
                ti);

            _normalizedTilesCache = tiles;
            _normalizedTilesSource = source;
            _normalizedTilesDistance = SelectedDistance;
            _normalizedTilesTarget = ti;

            return tiles;
        }

        /// <summary>
        /// ROI 선택 확정 시 호출.
        /// - Stop 상태일 때만 동작.
        /// - 미리 만들어둔(또는 캐시에서 가져온) 정규화 타일에서
        ///   동일 비율(u1..u2, v1..v2)의 영역을 잘라 Y mean/std 계산.
        /// </summary>
        public void CommitSelectionRect()
        {
            // 1) 기본 체크
            if (Image is null)
                return;

            if (!CanDrawRegions)
                return;

            if (SelectionRectInControl.Width <= 0 ||
                SelectionRectInControl.Height <= 0)
                return;

            // 반드시 "타일 모드"에서만 ROI 정의
            if (SelectedCropIndex < 0)
                return;

            if (_imageProcessService.TileCount <= 0)
                return;

            // UI 인덱스 → grid 인덱스로 변환
            int baseIndex = _imageProcessService.FlipVerticalIndex(SelectedCropIndex);

            // 2) 정규화 타일 캐시 가져오기 (없으면 생성)
            IReadOnlyList<WriteableBitmap> tiles;
            try
            {
                // 여기서 Image는 위에서 null 체크 했으므로 ! 사용해도 안전
                tiles = GetOrBuildNormalizedTiles(Image!);
            }
            catch
            {
                return;
            }

            if (baseIndex < 0 || baseIndex >= tiles.Count)
                return;

            var baseTile = tiles[baseIndex];
            if (baseTile is null)
                return;

            // SelectionCanvas 크기 기준 → 기준 타일 좌표로 변환
            var baseRectInTile = _drawRectService.ControlRectToImageRect(
                SelectionRectInControl,
                ImageControlSize,
                baseTile);

            if (baseRectInTile.Width <= 0 || baseRectInTile.Height <= 0)
                return;

            var baseSize = baseTile.PixelSize;
            if (baseSize.Width <= 0 || baseSize.Height <= 0)
                return;

            // 3) 기준 타일에서의 정규화 좌표 [0..1]
            double u1 = baseRectInTile.X / baseSize.Width;
            double v1 = baseRectInTile.Y / baseSize.Height;
            double u2 = (baseRectInTile.X + baseRectInTile.Width) / baseSize.Width;
            double v2 = (baseRectInTile.Y + baseRectInTile.Height) / baseSize.Height;

            u1 = Math.Clamp(u1, 0.0, 1.0);
            v1 = Math.Clamp(v1, 0.0, 1.0);
            u2 = Math.Clamp(u2, 0.0, 1.0);
            v2 = Math.Clamp(v2, 0.0, 1.0);

            if (u2 <= u1 || v2 <= v1)
                return;

            // 4) 모든 타일에 대해 동일 비율(u1..u2, v1..v2)의 영역에서 Y mean/std 계산
            var tileStatsList = new List<TileStats>(tiles.Count);
            var allMeans = new List<double>(tiles.Count);
            var allSds = new List<double>(tiles.Count);

            for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
            {
                var tileBmp = tiles[tileIndex];
                if (tileBmp is null)
                {
                    var zero = new TileStats(0, 0);
                    tileStatsList.Add(zero);
                    allMeans.Add(0);
                    allSds.Add(0);
                    continue;
                }

                var ps = tileBmp.PixelSize;
                if (ps.Width <= 0 || ps.Height <= 0)
                {
                    var zero = new TileStats(0, 0);
                    tileStatsList.Add(zero);
                    allMeans.Add(0);
                    allSds.Add(0);
                    continue;
                }

                int tx1 = (int)Math.Floor(u1 * ps.Width);
                int ty1 = (int)Math.Floor(v1 * ps.Height);
                int tx2 = (int)Math.Ceiling(u2 * ps.Width);
                int ty2 = (int)Math.Ceiling(v2 * ps.Height);

                var tileRectPx = new Rect(tx1, ty1, tx2 - tx1, ty2 - ty1);

                var stats = _drawRectService.GetYStatsFromGrayTile(tileBmp, tileRectPx);

                if (stats is { } s)
                {
                    var ts = new TileStats(s.mean, s.stdDev);
                    tileStatsList.Add(ts);
                    allMeans.Add(s.mean);
                    allSds.Add(s.stdDev);
                }
                else
                {
                    var zero = new TileStats(0, 0);
                    tileStatsList.Add(zero);
                    allMeans.Add(0);
                    allSds.Add(0);
                }
            }

            // 5) ROI 전체 요약값 (타일 mean/std 평균)
            double regionMean = allMeans.Count > 0 ? allMeans.Average() : 0.0;
            double regionStd = allSds.Count > 0 ? allSds.Average() : 0.0;

            SelectedRegionMean = regionMean;
            SelectedRegionStdDev = regionStd;

            // 6) 내부용 ID(Index) 부여 (1,2,3,...)
            int newIndex = _analysis.Regions.Count == 0
                ? 1
                : _analysis.Regions.Max(r => r.Index) + 1;

            // 색 인덱스 할당
            int colorIndex = AllocateColorIndex();

            var region = new SelectionRegion(
                newIndex,
                colorIndex,
                SelectionRectInControl, // CameraView 캔버스 좌표
                baseRectInTile,         // 기준 타일 좌표
                regionMean,
                regionStd);

            // 7) Workspace에 등록
            bool added = _analysis.TryAddRegion(region, tileStatsList);
            if (!added)
                return;
        }
    }
}
