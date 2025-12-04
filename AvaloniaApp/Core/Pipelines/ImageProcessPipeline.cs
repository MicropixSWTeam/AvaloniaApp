// AvaloniaApp.Core/Pipelines/ImageProcessPipeline.cs
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Pipelines
{
    /// <summary>
    /// 무거운 이미지 처리(Normalize + Stitch)를 백그라운드에서 실행하는 파이프라인.
    /// - frame은 이 메서드가 소유(dispose)한다고 가정.
    /// - 결과 Bitmap 들의 소유권은 호출자(ViewModel)에게 넘어간다.
    /// </summary>
    public class ImageProcessPipeline
    {
        private readonly BackgroundJobQueue _backgroundJobQueue;
        private readonly ImageProcessService _imageProcessService;
        private readonly UiDispatcher _uiDispatcher;

        public ImageProcessPipeline(
            BackgroundJobQueue backgroundJobQueue,
            ImageProcessService imageProcessService,
            UiDispatcher uiDispatcher)
        {
            _backgroundJobQueue = backgroundJobQueue;
            _imageProcessService = imageProcessService;
            _uiDispatcher = uiDispatcher;
        }

        public Task EnqueueNormalizeAndStitchAsync(
            CancellationToken ct,
            Bitmap frame,
            CropGridConfig gridConfig,
            byte targetIntensity,
            IReadOnlyList<TileTransform>? transforms,
            Func<IReadOnlyList<Bitmap>, Bitmap, Task> onCompleted)
        {
            if (frame is null) throw new ArgumentNullException(nameof(frame));
            if (onCompleted is null) throw new ArgumentNullException(nameof(onCompleted));

            var job = new BackgroundJob(
                "NormalizeAndStitch",
                async token =>
                {
                    using (frame)
                    {
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                        var linked = linkedCts.Token;
                        linked.ThrowIfCancellationRequested();

                        var size = frame.PixelSize;

                        // grid 설정 (해상도 바뀌면 자동으로 재설정)
                        _imageProcessService.ConfigureGrid(size, gridConfig);

                        IReadOnlyList<Bitmap> normalizedTiles;

                        if (transforms is not null && transforms.Count > 0)
                        {
                            // 1) 타일별 translation + normalize 한 번만 수행
                            var tiles = _imageProcessService.BuildNormalizedTiles(
                                frame,
                                idx => transforms[idx],
                                targetIntensity); // IReadOnlyList<WriteableBitmap>

                            normalizedTiles = tiles.Cast<Bitmap>().ToArray();
                        }
                        else
                        {
                            // translation 없이 normalize만
                            normalizedTiles = _imageProcessService.NormalizeTiles(frame, targetIntensity);
                        }

                        // 2) stitching (translation 추가 적용 여부)
                        Bitmap stitched;
                        if (transforms is not null &&
                            transforms.Count == normalizedTiles.Count)
                        {
                            stitched = _imageProcessService.StitchTiles(normalizedTiles, transforms);
                        }
                        else
                        {
                            stitched = _imageProcessService.StitchTiles(normalizedTiles);
                        }

                        // 3) UI 스레드에서 ViewModel 업데이트
                        await _uiDispatcher.InvokeAsync(
                            () => onCompleted(normalizedTiles, stitched));
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
    }
}
