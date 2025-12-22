using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public class ProcessViewModel : ViewModelBase
    {
        public CameraViewModel CameraVM { get; }
        public ProcessViewModel(AppService service, CameraViewModel cameraViewModel) : base(service)
        {
            CameraVM = cameraViewModel;
        }
    }
}
