using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Presentation.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel : ViewModelBase
    {
        [ObservableProperty]
        private Bitmap? image;

        private readonly CameraPipeline _cameraPipeline;

        // 프리뷰용 CTS (Start/Stop 에서 사용)
        private CancellationTokenSource? _previewCts;

        public CameraViewModel(CameraPipeline cameraPipeline) : base()
        {
            _cameraPipeline = cameraPipeline;
        }

        /// <summary>
        /// Image 속성이 바뀔 때 이전 Bitmap 해제 (메모리 누수 방지)
        /// </summary>
        partial void OnImageChanging(Bitmap? value)
        {
            image?.Dispose();
        }

        [RelayCommand]
        public async Task CaptureAsync()
        {
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueCaptureAsync(ct, async bmp =>
                {
                    Image = bmp;
                    await Task.CompletedTask;
                });
            });
        }

        [RelayCommand]
        public async Task StartPreviewAsync()
        {
            // 이미 프리뷰 중이면 무시
            if (_previewCts is not null)
                return;

            _previewCts = new CancellationTokenSource();

            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueStartPreviewAsync(
                    _previewCts.Token,
                    async bmp =>
                    {
                        // 여기서는 Bitmap 소유권을 ViewModel 이 가져감
                        Image = bmp;
                        await Task.CompletedTask;
                    });
            });
        }

        [RelayCommand]
        public Task StopPreviewAsync()
        {
            if (_previewCts is null)
                return Task.CompletedTask;

            _previewCts.Cancel();
            _previewCts.Dispose();
            _previewCts = null;

            return Task.CompletedTask;
        }
    }
}
