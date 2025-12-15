using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
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

    }
}
