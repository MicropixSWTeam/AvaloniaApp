using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    /// <summary>
    /// Singleton DI 주입을 위한 매니저
    /// </summary>
    public sealed class AppService
    {
        public UiService Ui { get;}
        public OperationRunner OperationRunner{ get;}
        public VimbaCameraService Camera{ get;}
        public ImageProcessServiceTest ImageProcess{ get;}
        public Drawservice Draw{ get; }
        public StorageService Storage{ get; }
        public PopupService Popup{ get; }
        public WorkspaceService WorkSpace { get;}
        public Options Options { get; }
        public AppService(
            UiService ui,
            OperationRunner operationrunner,
            VimbaCameraService camera,
            ImageProcessServiceTest imageprocess,
            Drawservice draw,
            StorageService storage,
            PopupService popup,

            WorkspaceService workspace,
            Options options) 
        { 
            Ui = ui ?? throw new ArgumentNullException(nameof(ui));
            OperationRunner = operationrunner ?? throw new ArgumentNullException( nameof(operationrunner));
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            ImageProcess = imageprocess ?? throw new ArgumentNullException(nameof(imageprocess));
            Draw = draw?? throw new ArgumentNullException(nameof(draw));    
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Popup = popup ?? throw new ArgumentNullException(nameof(popup));
            WorkSpace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            Options = options ?? throw new ArgumentNullException(nameof(options));  
        }
    }
}
