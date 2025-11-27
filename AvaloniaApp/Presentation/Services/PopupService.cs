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
        public Task ShowModelessAsync(object vm)
        {
            var host = _hostFactory();
            host.DataContext = vm; 
            host.Show(_owner);
            return Task.CompletedTask;
        }
        public Task ShowModalAsync(object vm)
        {
            var host = _hostFactory();
            host.DataContext = vm;
            host.ShowDialog(_owner);
            return Task.CompletedTask;
        }
    }
}
