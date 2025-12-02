using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel : ViewModelBase
    {
        [ObservableProperty]
        private IReadOnlyList<CameraInfo>? cameras;

        [ObservableProperty]
        private CameraInfo? selectedCamera;

        [ObservableProperty]
        private IReadOnlyList<PixelFormatInfo>? pixelFormats;

        [ObservableProperty]
        private PixelFormatInfo? selectedPixelFormat;

        CameraPipeline _cameraPipeline;

        public CameraViewModel(CameraPipeline cameraPipeline, DialogService? dialogService, UiDispatcher uiDispatcher, BackgroundJobQueue backgroundJobQueue
        ) : base(dialogService, uiDispatcher, backgroundJobQueue)
        {
            _cameraPipeline = cameraPipeline;
        }

        [RelayCommand]
        public async Task LoadCamerasAsync()
        {
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueGetCameraListAsync(ct, async list =>
                {
                    Cameras = list;
                    SelectedCamera = null;
                    PixelFormats = null;
                    SelectedPixelFormat = null;
                });
            });
        }
        [RelayCommand]
        public async Task LoadPixelFormatsAsync()
        {
            if (SelectedCamera is null)
            {
                return;
            }
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueGetPixelFormatListAsync(ct, SelectedCamera.Id, async list =>
                {
                    PixelFormats = list;
                    SelectedPixelFormat = PixelFormats.FirstOrDefault(p => p.IsAvailable) ?? PixelFormats.FirstOrDefault();
                });
            });
        }
        partial void OnSelectedCameraChanged(CameraInfo? value)
        {
            if (value is null)
            {
                PixelFormats = null;
                SelectedPixelFormat = null;
                return;
            }

            // 비동기로 픽셀 포맷 로드
            _ = LoadPixelFormatsAsync();
        }
    }
}
