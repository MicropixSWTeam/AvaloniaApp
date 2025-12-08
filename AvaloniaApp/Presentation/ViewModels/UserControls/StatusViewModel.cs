using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class StatusViewModel : ViewModelBase,IPopup
    {
        [ObservableProperty]
        private string message = "처리 중입니다...";

        public string Title { get; set; } = "Processing";

        public int Width { get; set; } = 260;
        public int Height { get; set; } = 120;

        public StatusViewModel()
        {
        }

        public StatusViewModel(string message)
        {
            Message = message;
        }
    }
}
