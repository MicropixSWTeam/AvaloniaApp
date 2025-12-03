using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Presentation.ViewModels;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel : ViewModelBase
    {
        [ObservableProperty]
        private Bitmap? image;
        [ObservableProperty]
        private List<Bitmap> cropImages = new();
        [ObservableProperty]
        private int selectedCropIndex = -1; 

        private readonly CameraPipeline _cameraPipeline;
        public CameraViewModel(CameraPipeline cameraPipeline) : base()
        {
            _cameraPipeline = cameraPipeline;
        }
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
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueStartPreviewAsync(
                    ct,
                    async bmp =>
                    {
                        // 여기서는 Bitmap 소유권을 ViewModel 이 가져감
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
    }
}
