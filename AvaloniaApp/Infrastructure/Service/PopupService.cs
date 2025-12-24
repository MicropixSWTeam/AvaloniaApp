using Avalonia.Controls;
using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Infrastructure.Factory; // ViewModelFactory 경로 확인
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaApp.Presentation.Views.Windows;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public class PopupService
    {
        private readonly Func<PopupHostWindow> _hostFactory;
        private readonly ViewModelFactory _vmFactory;
        private readonly Dictionary<object, PopupHostWindow> _popupDic = new();

        public PopupService(Func<PopupHostWindow> hostFactory, ViewModelFactory vmFactory)
        {
            _hostFactory = hostFactory;
            _vmFactory = vmFactory;
        }

        // [커스텀 팝업] Load, Input 등
        public async Task<TResult?> ShowCustomAsync<TViewModel, TResult>(Action<TViewModel>? init = null)
            where TViewModel : IDialogRequestClose
        {
            var vm = _vmFactory.Create<TViewModel>();
            init?.Invoke(vm);
            return await ShowDialogInternalAsync<TResult>(vm);
        }

        // [공통 팝업] 메시지, 에러
        public async Task<bool> ShowMessageAsync(string title, string message, DialogType type)
        {
            var vm = _vmFactory.Create<DialogViewModel>();
            vm.Init(type, title, message);
            return await ShowDialogInternalAsync<bool>(vm);
        }

        private async Task<TResult?> ShowDialogInternalAsync<TResult>(IDialogRequestClose vm)
        {
            var tcs = new TaskCompletionSource<TResult?>();

            // 결과값 수신용 핸들러
            EventHandler<DialogResultEventArgs> handler = null!;
            handler = (s, e) =>
            {
                vm.CloseRequested -= handler;

                if (e.Result is TResult res) tcs.TrySetResult(res);
                else tcs.TrySetResult(default);

                // 여기서 ClosePopup을 호출해도 되지만, 
                // ShowModalAsync에서도 구독하고 있으므로 중복 호출은 Avalonia가 무시합니다.
                ClosePopup(vm);
            };

            vm.CloseRequested += handler;

            await ShowModalAsync(vm);

            return await tcs.Task;
        }

        public Task ShowModalAsync(object vm)
        {
            var host = _hostFactory();

            // [제목 설정] IPopup 인터페이스가 있으면 제목 적용
            if (vm is IPopup popup)
            {
                host.Title = popup.Title;
            }

            host.DataContext = vm;

            // =========================================================
            // [수정 핵심] ShowModalAsync를 직접 호출해도 닫히도록 이벤트 연결
            // =========================================================
            if (vm is IDialogRequestClose requestClose)
            {
                EventHandler<DialogResultEventArgs> closeHandler = null!;
                closeHandler = (s, e) =>
                {
                    requestClose.CloseRequested -= closeHandler; // 메모리 누수 방지
                    ClosePopup(vm); // 닫기 요청이 오면 창을 닫음
                };
                requestClose.CloseRequested += closeHandler;

                // 창이 X버튼 등으로 강제로 닫혔을 때도 이벤트 구독 해제
                host.Closed += (_, __) => requestClose.CloseRequested -= closeHandler;
            }
            // =========================================================

            _popupDic[vm] = host;
            host.Closed += (_, __) => _popupDic.Remove(vm);

            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (owner != null)
                return host.ShowDialog(owner);

            host.Show();
            return Task.CompletedTask;
        }

        public void ClosePopup(object vm)
        {
            if (_popupDic.TryGetValue(vm, out var window))
            {
                window.Close();
                _popupDic.Remove(vm);
            }
        }
    }
}