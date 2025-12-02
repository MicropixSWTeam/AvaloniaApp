using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public class TabViewModel:ViewModelBase
    {
        private readonly PopupService _popupService;
        public TabViewModel(PopupService popupService)
        {
            _popupService = popupService;
        }
    }
}
