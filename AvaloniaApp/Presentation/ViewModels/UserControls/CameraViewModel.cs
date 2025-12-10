// AvaloniaApp.Presentation/ViewModels/UserControls/CameraViewModel.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Converters;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.Views.UserControls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExCSS;
using LiveChartsCore.Measure;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;
using Rect = Avalonia.Rect;
using Size = Avalonia.Size;

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
        private int selectedCropIndex = 7;

        [ObservableProperty]
        private string selectedCropIndexText = "7";

        // 프리뷰 normalize on/off
        [ObservableProperty]
        private bool normalizePreviewEnabled = false;

        // normalize 타겟 밝기 (0~255)
        [ObservableProperty]
        private byte targetIntensity = 128;

        // 거리 선택 (0 = translation 없음)
        [ObservableProperty]
        private int selectedDistance = 0;

        // Stop 상태
        [ObservableProperty]
        private bool isStop = false;

        // DrawRect 모드 (Stop 이후 ROI 그려서 스펙트럼 분석)
        [ObservableProperty]
        private bool isDrawRectMode;

        // Translation 모드 (Stop 이후 DrawRect 를 템플릿으로 사용해서 offset 설정)
        [ObservableProperty]
        private bool isTranslationMode;

        public ObservableCollection<int> CropIndexOptions { get; } = new();

        /// <summary>
        /// 실제 드로잉 가능 여부 = Stop 상태 + (DrawRect 또는 Translation 모드) + ROI 개수 제한 미도달
        /// </summary>
        public bool CanDrawRegions => IsStop && !_analysis.IsLimitReached && (IsDrawRectMode || IsTranslationMode);

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
                RowSize: 504,
                ColSize: 508,
                RowGap: 1004,
                ColGap: 1056,
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
            SelectedCropIndex = 7;

            // ROI 개수 변동 시 그릴 수 있는지 여부 업데이트
            _analysis.Changed += (_, __) =>
            {
                OnPropertyChanged(nameof(NextRegionBrush));
                OnPropertyChanged(nameof(CanDrawRegions));
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

        partial void OnIsDrawRectModeChanged(bool value)
        {
            if (value)
            {
                // DrawRect 모드를 켜면 Translation 모드는 끈다 (서로 배타적)
                if (IsTranslationMode)
                    IsTranslationMode = false;
            }

            OnPropertyChanged(nameof(CanDrawRegions));
        }

        partial void OnIsTranslationModeChanged(bool value)
        {
            if (value)
            {
                // Translation 모드를 켜면 DrawRect 모드는 끈다 (서로 배타적)
                if (IsDrawRectMode)
                    IsDrawRectMode = false;
            }

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
                        // 카메라 콜백은 백그라운드 스레드일 수 있으므로 UI 스레드에서 프로퍼티 갱신
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Image = bmp;
                            IsStop = false;
                        });

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
                    //    정규화+translation 타일을 미리 한 번 캐시에 빌드
                    var src = Image;
                    if (src is not null)
                    {
                        try
                        {
                            _ = GetOrBuildNormalizedTiles(src);
                        }
                        catch
                        {
                            // 실패해도 CommitSelectionRect 에서 다시 시도하므로 무시
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

            var src = Image; // Stop 상태의 현재 프레임 스냅샷

            try
            {
                // 1) CPU-bound 작업(타일 생성 + 스티칭)은 백그라운드 스레드에서 수행
                var (originalBitmaps, processedBitmaps, stitched) = await Task.Run(() =>
                {
                    // Grid 보장
                    EnsureGridConfigured(src);

                    // 현재 Image + SelectedDistance + TargetIntensity 기준으로
                    // 정규화+translation된 타일들을 캐시에서 얻거나 새로 계산
                    var processedTiles = GetOrBuildNormalizedTiles(src);   // IReadOnlyList<WriteableBitmap>

                    // WriteableBitmap -> Bitmap 업캐스트용 리스트 (캐시 참조, 여기서 Dispose 하지 않음)
                    var processedList = new List<Bitmap>(processedTiles.Count);
                    foreach (var t in processedTiles)
                    {
                        processedList.Add(t);
                    }

                    // 원본 crop 타일 (translation/normalize 없이 grid 기준으로만 crop)
                    var originalList = new List<Bitmap>(_imageProcessService.TileCount);
                    for (int i = 0; i < _imageProcessService.TileCount; i++)
                    {
                        var tile = _imageProcessService.CropTile(src, i); // 새 WriteableBitmap 생성
                        originalList.Add(tile);
                    }

                    // 처리된 타일들로 전체 stitched 이미지 생성
                    var stitchedLocal = _imageProcessService.StitchTiles(processedList);

                    return (originalList, processedList, stitchedLocal);
                });

                try
                {
                    // 2) 파일 저장/폴더 선택 등은 UI 컨텍스트에서 비동기로 실행
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
                    stitched.Dispose();

                    foreach (var tile in originalBitmaps)
                    {
                        tile?.Dispose();
                    }

                    // processedBitmaps 는 Normalize 캐시의 인스턴스를 가리키므로
                    // 여기서 Dispose 하지 않는다.
                }
            }
            catch
            {
                // 필요시 로깅 또는 에러 알림 처리
            }
        }

        [RelayCommand]
        public async Task GetTranslationOffsetsAsync()
        {
            // 1) Stop 상태 + 유효한 프레임인지 확인
            if (!IsStop)
                return;

            if (Image is null)
                return;

            var src = Image; // 현재 프레임 스냅샷

            try
            {
                // 2) CPU 작업은 백그라운드 스레드에서 수행
                var offsets = await Task.Run<IReadOnlyList<TileTransform>>(() =>
                {
                    // Grid 보장
                    EnsureGridConfigured(src);

                    // (a) translation 없이 정규화 타일 생성
                    var tiles = _imageProcessService.BuildNormalizedTiles(
                        src,
                        _ => new TileTransform(0, 0),   // translation 없음
                        TargetIntensity);

                    try
                    {
                        // (b) 가운데 타일(인덱스 7)을 기준으로 평행 이동 계산
                        int referenceIndex = 7;
                        if (referenceIndex < 0 || referenceIndex >= tiles.Count)
                            referenceIndex = 0;

                        return _imageProcessService.ComputePhaseCorrelationOffsets(
                            tiles,
                            referenceIndex: referenceIndex);
                    }
                    finally
                    {
                        foreach (var tile in tiles)
                            tile.Dispose();
                    }
                });

                // 3) SelectedDistance에 대한 runtime translation 테이블 저장
                TranslationOffsets.SetRuntimeOffsets(SelectedDistance, offsets);

                // 4) 캐시/프리뷰 갱신
                InvalidateNormalizedTilesCache();
                _imageProcessService.RebuildTranslationPreviewTable();

                UpdatePreviewImages();
                OnPropertyChanged(nameof(DisplayImage));
            }
            catch
            {
                // 필요시 로그
            }
        }


        // ===== 내부 헬퍼 =====

        private int AllocateColorIndex()
        {
            var used = _analysis.Regions
                .Select(r => r.ColorIndex)
                .Distinct()
                .ToHashSet();

            for (int i = 0; i < 7; i++)
            {
                if (!used.Contains(i))
                    return i;
            }

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

                // dropdown용 인덱스 리스트 갱신 (0..TileCount-1, UI 인덱스)
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
        /// TranslationOffsets 의 정적 Table + 런타임 테이블을 모두 고려한다.
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
        /// - DrawRect 모드: ROI + 스펙트럼 분석만 수행 (translation X)
        /// - Translation 모드: 같은 rect를 ref 템플릿으로 사용해
        ///   모든 타일(0..N-1)에 대해 템플릿 매칭으로 translation offset 계산.
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

            // 현재 모드가 아무 것도 아니면 의미 없음
            if (!IsDrawRectMode && !IsTranslationMode)
                return;

            // UI 인덱스 → grid 인덱스로 변환
            int baseIndex = _imageProcessService.FlipVerticalIndex(SelectedCropIndex);

            // 2) 정규화 타일 캐시 가져오기 (없으면 생성)
            IReadOnlyList<WriteableBitmap> tiles;
            try
            {
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

            // 기준 타일에서의 정규화 좌표 [0..1]
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

            // ----- DrawRect 모드: ROI 추가 + 스펙트럼 분석 -----
            if (IsDrawRectMode)
            {
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

                // ROI 전체 요약값 (타일 mean/std 평균)
                double regionMean = allMeans.Count > 0 ? allMeans.Average() : 0.0;
                double regionStd = allSds.Count > 0 ? allSds.Average() : 0.0;

                SelectedRegionMean = regionMean;
                SelectedRegionStdDev = regionStd;

                // 내부용 ID(Index) 부여 (1,2,3,...)
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

                bool added = _analysis.TryAddRegion(region, tileStatsList);
                if (!added)
                    return;
            }

            // ----- Translation 모드: 템플릿 매칭으로 translation offset 계산 -----
            if (IsTranslationMode)
            {
                // heavy 작업은 백그라운드 스레드에서 실행
                var tilesSnapshot = tiles.ToArray();
                var referenceTile = baseTile;
                var referenceRect = baseRectInTile;
                int referenceIndex = baseIndex;
                int distanceKey = SelectedDistance;

                _ = Task.Run(() =>
                {
                    var offsets = new TileTransform[tilesSnapshot.Length];

                    for (int i = 0; i < tilesSnapshot.Length; i++)
                    {
                        var targetTile = tilesSnapshot[i];
                        if (targetTile is null)
                        {
                            offsets[i] = new TileTransform(0, 0);
                            continue;
                        }

                        if (i == referenceIndex)
                        {
                            // 기준 타일은 자기 자신이므로 (0,0)
                            offsets[i] = new TileTransform(0, 0);
                            continue;
                        }

                        var offset = _imageProcessService.ComputeTemplateMatchOffset(
                            referenceTile: referenceTile,
                            referenceRectInReferenceTile: referenceRect,
                            targetTile: targetTile);

                        offsets[i] = offset;
                    }

                    return offsets;
                }).ContinueWith(t =>
                {
                    if (t.Status != TaskStatus.RanToCompletion)
                        return;

                    var offsets = t.Result;

                    // UI 스레드에서 translation 테이블 갱신 + 프리뷰 갱신
                    Dispatcher.UIThread.Post(() =>
                    {
                        TranslationOffsets.SetRuntimeOffsets(distanceKey, offsets);

                        InvalidateNormalizedTilesCache();
                        _imageProcessService.RebuildTranslationPreviewTable();

                        UpdatePreviewImages();
                        OnPropertyChanged(nameof(DisplayImage));
                    });
                });
            }
        }
    }
}