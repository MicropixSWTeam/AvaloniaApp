using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Operations;
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
    public partial class CameraConnectViewModel:ViewModelBase,IPopup
    {
        [ObservableProperty]
        private IReadOnlyList<CameraInfo>? cameras;

        [ObservableProperty]
        private CameraInfo? selectedCamera;

        private CameraPipeline _cameraPipeline;
        private PopupService _popupService;

        public string Title { get; set; } = "Camera Connect";
        public int Width { get; set; } = 500;
        public int Height { get; set; } = 250;

        public CameraConnectViewModel(
           UiDispatcher uiDispatcher,
           OperationRunner runner,
           CameraPipeline cameraPipeline,
           PopupService popupService)
           : base(uiDispatcher, runner)
        {
            _cameraPipeline = cameraPipeline;
            _popupService = popupService;
        }
        [RelayCommand]
        public Task LoadCamerasAsync()
        {
            return RunOperationAsync(
                key: "Camera.LoadList",
                backgroundWork: async (ct, _) =>
                {
                    await _cameraPipeline.EnqueueGetCameraListAsync(
                        ct,
                        list =>
                        {
                            Cameras = list;
                            SelectedCamera ??= list.FirstOrDefault();
                            return Task.CompletedTask;
                        });
                });
        }

        [RelayCommand]
        public Task ConnectCameraAsync()
        {
            var cam = SelectedCamera;
            if (cam is null)
                return Task.CompletedTask;

            return RunOperationAsync(
                key: "Camera.Connect",
                backgroundWork: async (ct, _) =>
                {
                    await _cameraPipeline.EnqueueConnectAsync(
                        ct,
                        cam.Id,
                        onConnect: () =>
                        {
                            // Popup 닫기 로직은 PopupService API에 맞춰 여기서 호출
                            // (메서드명이 확실치 않아서 컴파일 깨지는 호출은 넣지 않음)
                            return Task.CompletedTask;
                        });
                });
        }

        [RelayCommand]
        public Task DisconnectCameraAsync()
        {
            return RunOperationAsync(
                key: "Camera.Disconnect",
                backgroundWork: async (ct, _) =>
                {
                    await _cameraPipeline.EnqueueDisconnectAsync(
                        ct,
                        onDisconnect: () =>
                        {
                            // 필요 시 UI 상태 갱신
                            return Task.CompletedTask;
                        });
                });
        }
    }
}
