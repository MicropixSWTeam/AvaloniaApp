using AvaloniaApp.Infrastructure.Service;
using AvaloniaApp.Presentation.ViewModels.Base;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public sealed class RgbImageViewModel : ViewModelBase
    {
        public CameraViewModel CameraVM { get; }
        public RgbImageViewModel(AppService service, CameraViewModel cameraViewModel) : base(service)
        {
            CameraVM = cameraViewModel;
        }
    }
}
