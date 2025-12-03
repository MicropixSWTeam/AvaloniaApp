using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraConnectViewModel:ViewModelBase
    {
        [ObservableProperty]
        private IReadOnlyList<CameraInfo>? cameras;

        [ObservableProperty]
        private CameraInfo? selectedCamera;

        private CameraPipeline _cameraPipeline;
        private PopupService _popupService;
        public CameraConnectViewModel(CameraPipeline cameraPipeline,PopupService popupService) :base()
        {
            _cameraPipeline = cameraPipeline;
            _popupService = popupService;   
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
                });
            });
        }
        [RelayCommand]
        public async Task ConnectCameraAsync()
        {
            if (SelectedCamera is null)
                return;

            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueConnectAsync(
                    ct,
                    SelectedCamera.Id,
                    async () =>
                    {
                        _popupService.ClosePopup(this);
                        await Task.CompletedTask;
                    });
            });
        }
        [RelayCommand]
        public async Task DisconnectCameraAsync()
        {
            await RunSafeAsync(async ct =>
            {
                await _cameraPipeline.EnqueueDisconnectAsync(
                    ct,
                    async () =>
                    {
                        SelectedCamera = null;
                        await Task.CompletedTask;
                    });
            });
        }

    }
}
