using AvaloniaApp.Presentation.Views.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.Services
{
    public class PopupService
    {
        private readonly Func<PopupHostWindow> _hostFactory;
        private readonly MainWindow _owner;
        public PopupService(
            Func<PopupHostWindow> hostFactory,
            MainWindow owner
        )
        {
            _hostFactory = hostFactory;
            _owner = owner;
        }
        public Task ShowAsync(object vm)
        {
            var host = _hostFactory();
            host.DataContext = vm; // View는 DataTemplate이 resolve
            host.Show(_owner);
            return Task.CompletedTask;
        }
    }
}
