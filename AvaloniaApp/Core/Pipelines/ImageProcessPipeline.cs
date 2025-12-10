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

    }
}
