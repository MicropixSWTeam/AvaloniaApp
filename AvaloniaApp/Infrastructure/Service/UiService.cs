using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure.Service
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

            // 기존에 이게 컴파일 되고 있었다면 그대로 두세요.
            return Dispatcher.UIThread.InvokeAsync(action, priority).GetTask();
        }

        // ✅ 핵심: async 작업을 "진짜로" 기다리는 UI Invoke
        public Task InvokeAsync(Func<Task> func, DispatcherPriority priority = default)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return func();

            var tcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await func(); // UI 스레드에서 실행, 완료까지 대기
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, priority);

            return tcs.Task;
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> func, DispatcherPriority priority = default)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return func();

            var tcs = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var result = await func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, priority);

            return tcs.Task;
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