using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraConnectViewModel:ViewModelBase,IPopup
    {
        [ObservableProperty]
        private IReadOnlyList<CameraInfo>? cameras;

        [ObservableProperty]
        private CameraInfo? selectedCamera;

        private PopupService _popupService;

        public CameraConnectViewModel(AppService service) : base(service)
        {
        }

        public string Title { get; set; } = "Camera Connect";
        public int Width { get; set; } = 500;
        public int Height { get; set; } = 250;
    }
}
