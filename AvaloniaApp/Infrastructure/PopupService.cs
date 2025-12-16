using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Presentation.ViewModels.Windows;
using AvaloniaApp.Presentation.Views.Windows;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public class PopupService
    {
        // 윈도우 생성 팩토리 (DI 컨테이너가 제공)
        private readonly Func<PopupHostWindow> _hostFactory;

        // 현재 열려있는 팝업들 관리 (Key: ViewModel, Value: Window)
        private readonly Dictionary<object, PopupHostWindow> _popupDic = new();

        public PopupService(Func<PopupHostWindow> hostFactory)
        {
            _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
        }

        // 현재 메인 윈도우를 안전하게 가져오는 헬퍼
        private Window? GetOwnerWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        public Task ShowModelessAsync(object vm)
        {
            if (vm is null) throw new ArgumentNullException(nameof(vm));

            var key = vm;

            // 이미 열려있으면 활성화
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

            // IPopup 인터페이스를 통해 창 크기/제목 설정
            if (vm is IPopup popup)
            {
                host.Title = popup.Title;
                host.Width = popup.Width;
                host.Height = popup.Height;
            }

            host.DataContext = vm;

            // 닫힐 때 딕셔너리에서 제거
            host.Closed += (_, __) => _popupDic.Remove(key);

            _popupDic[key] = host;

            var owner = GetOwnerWindow();
            if (owner != null)
                host.Show(owner); // Owner 지정 (선택사항)
            else
                host.Show();

            return Task.CompletedTask;
        }

        public Task ShowModalAsync(object vm)
        {
            if (vm is null) throw new ArgumentNullException(nameof(vm));

            var host = _hostFactory();

            if (vm is IPopup popup)
            {
                host.Title = popup.Title;
                host.Width = popup.Width;
                host.Height = popup.Height;
            }

            host.DataContext = vm;

            var owner = GetOwnerWindow();
            if (owner != null)
                return host.ShowDialog(owner);

            // Owner가 없으면 모달을 띄울 수 없거나 그냥 Show를 해야 함
            host.Show();
            return Task.CompletedTask;
        }

        public void ClosePopup(object vm)
        {
            if (vm is null) return;

            if (_popupDic.TryGetValue(vm, out var popup))
            {
                popup.Close(); // 이벤트 핸들러에서 Remove됨
            }
        }

        public void CloseAllPopups()
        {
            // 복사본으로 순회 (Close 시 컬렉션 변경되므로)
            foreach (var popup in new List<PopupHostWindow>(_popupDic.Values))
            {
                popup.Close();
            }
            _popupDic.Clear();
        }
    }
}