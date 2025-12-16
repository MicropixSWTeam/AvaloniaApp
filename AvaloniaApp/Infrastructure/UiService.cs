using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public sealed class UiThrottler
    {
        private readonly UiService _ui; // 변경됨
        private int _isScheduled;

        // internal로 막아서 오직 UiService.CreateThrottler()를 통해서만 만들게 강제할 수도 있음
        public UiThrottler(UiService ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        public void Run(Action action)
        {
            if (Interlocked.Exchange(ref _isScheduled, 1) == 0)
            {
                _ui.Post(() =>
                {
                    try { action(); }
                    finally { Interlocked.Exchange(ref _isScheduled, 0); }
                });
            }
        }

        public void Reset() => Interlocked.Exchange(ref _isScheduled, 0);
    }
    /// <summary>
    /// UI 스레드 접근 및 제어를 담당하는 통합 서비스입니다.
    /// 기존 UiDispatcher와 Factory 역할을 모두 수행합니다.
    /// </summary>
    public sealed class UiService
    {
        // =========================================================
        // 1. Dispatcher 기본 기능 (Global)
        // =========================================================

        public void Post(Action action, DispatcherPriority priority = default)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }
            Dispatcher.UIThread.Post(action, priority);
        }

        public Task InvokeAsync(Action action, DispatcherPriority priority = default)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Dispatcher.UIThread.InvokeAsync(action, priority).GetTask();
        }

        public async Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority = default)
        {
            if (Dispatcher.UIThread.CheckAccess()) return func();
            return await Dispatcher.UIThread.InvokeAsync(func, priority);
        }

        // =========================================================
        // 2. 팩토리 기능 (Factory Method)
        // =========================================================

        /// <summary>
        /// 현재 UiService를 기반으로 동작하는 새로운 Throttler를 생성합니다.
        /// ViewModel마다 별도의 상태 관리가 필요하므로 매번 new로 생성합니다.
        /// </summary>
        public UiThrottler CreateThrottler()
        {
            // 'this'를 넘겨주어 Throttler가 이 서비스를 사용하게 함
            return new UiThrottler(this);
        }
    }
}