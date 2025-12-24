using AvaloniaApp.Infrastructure.Service;
using AvaloniaApp.Presentation.ViewModels.Base;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public sealed class RawImageViewModel : ViewModelBase
    {
        public CameraViewModel CameraVM { get; }
        public RawImageViewModel(AppService service, CameraViewModel cameraViewModel) : base(service)
        {
            CameraVM = cameraViewModel; 
        }
    }
}
