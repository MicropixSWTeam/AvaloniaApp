using Avalonia.Controls;
using AvaloniaApp.Presentation.Views.Windows;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.Services
{
    public class PopupService
    {
        private readonly Func<PopupHostWindow> _hostFactory;
        private readonly MainWindow _owner;
        private readonly Dictionary<object, PopupHostWindow> _popupDic = new();

        public PopupService(Func<PopupHostWindow> hostFactory, MainWindow owner)
        {
            _hostFactory = hostFactory;
            _owner = owner;
        }

        public Task ShowModelessAsync(object vm)
        {
            if (vm is null)
                throw new ArgumentNullException(nameof(vm));

            // key = vm 인스턴스 하나
            var key = vm;

            if (_popupDic.TryGetValue(key, out var existing))
            {
                if (existing.IsVisible)
                {
                    existing.Activate();
                    existing.Focus();
                    return Task.CompletedTask;
                }

                _popupDic.Remove(key);
            }

            var host = _hostFactory();

            host.DataContext = vm;

            _popupDic[key] = host;

            host.Closed += (_, __) =>
            {
                _popupDic.Remove(key);
            };

            host.Show(_owner);
            return Task.CompletedTask;
        }

        public Task ShowModalAsync(object vm)
        {
            if (vm is null)
                throw new ArgumentNullException(nameof(vm));

            var host = _hostFactory();
            host.DataContext = vm;

            // 모달은 딕셔너리에 안 넣음 (ShowDialog가 반환될 때 이미 닫힌 상태)
            host.ShowDialog(_owner);
            return Task.CompletedTask;
        }

        public void ClosePopup(object vm)
        {
            if (vm is null)
                throw new ArgumentNullException(nameof(vm));

            var key = vm;

            if (_popupDic.TryGetValue(key, out var popup))
            {
                if (popup.IsVisible)
                {
                    popup.Close();
                }
                _popupDic.Remove(key);
            }
        }

        public void CloseAllPopups()
        {
            foreach (var popup in _popupDic.Values)
            {
                if (popup.IsVisible)
                {
                    popup.Close();
                }
            }
            _popupDic.Clear();
        }
    }
}
