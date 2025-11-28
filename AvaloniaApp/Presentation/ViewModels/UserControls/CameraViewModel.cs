using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel :ViewModelBase
    {
        CameraPipeline _cameraPipeline;

        public CameraViewModel(CameraPipeline cameraPipeline,DialogService? dialogService,IUiDispatcher uiDispatcher,IBackgroundJobQueue backgroundJobQueue
        ) : base(dialogService, uiDispatcher, backgroundJobQueue)
        {
            _cameraPipeline = cameraPipeline;
        }
    }
}
