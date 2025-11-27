using AvaloniaApp.Core.Enums;
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

        private Dictionary<ViewType, PopupHostWindow> _popupDic = new();
        public PopupService(Func<PopupHostWindow> hostFactory,MainWindow owner)
        {
            _hostFactory = hostFactory;
            _owner = owner;
        }
        public Task ShowModelessAsync(ViewType viewType,object vm)
        {
            if(_popupDic.TryGetValue(viewType,out var existing))
            {
                if (existing.IsVisible)
                {
                    existing.Activate();
                    existing.Focus();
                    return Task.CompletedTask;
                }

                _popupDic.Remove(viewType);
            }

            var host = _hostFactory();
            host.DataContext = vm; 
            host.Title = viewType.ToString();

            _popupDic[viewType] = host;

            host.Closed += (_, __) =>
            {
                _popupDic.Remove(viewType);
            };

            host.Show(_owner);
            return Task.CompletedTask;
        }
        public Task ShowModalAsync(ViewType viewType, object vm)
        {
            var host = _hostFactory();
            host.DataContext = vm;
            host.ShowDialog(_owner);
            return Task.CompletedTask;
        }
    }
}
