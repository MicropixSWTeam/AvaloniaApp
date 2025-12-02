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
        public CameraViewModel(CameraPipeline cameraPipeline, DialogService? dialogService, UiDispatcher uiDispatcher, BackgroundJobQueue backgroundJobQueue) : base(dialogService, uiDispatcher, backgroundJobQueue)
        {
        }
    }
}
