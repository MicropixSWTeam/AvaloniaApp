using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AvaloniaApp.Infrastructure
{
    /// <summary>
    /// Avalonia UI 스레드(Dispatcher.UIThread)에서 작업을 실행하기 위한 래퍼입니다.
    /// ViewModel/Service 코드에서 UI 변경이 필요할 때 직접 Dispatcher를 사용하지 않고
    /// 이 클래스를 주입받아 호출합니다.
    /// </summary>
    public sealed class UiDispatcher
    {
        /// <summary>
        /// 지정된 <paramref name="action"/>을 UI 스레드에서 실행합니다.
        /// 현재 스레드가 UI 스레드라면 즉시 실행하고, 아니면 Dispatcher에 Post 합니다.
        /// </summary>
        /// <param name="action">UI 스레드에서 실행할 동기 작업.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        /// <exception cref="ArgumentNullException">action이 null인 경우.</exception>
        public Task InvokeAsync(Action action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// 지정된 <paramref name="func"/>을 UI 스레드에서 실행하고 결과를 반환합니다.
        /// 현재 스레드가 UI 스레드라면 즉시 실행하고, 아니면 Dispatcher에 Post 합니다.
        /// </summary>
        /// <typeparam name="T">반환 타입.</typeparam>
        /// <param name="func">UI 스레드에서 실행할 함수.</param>
        /// <returns>함수 실행 결과를 포함한 Task.</returns>
        /// <exception cref="ArgumentNullException">func가 null인 경우.</exception>
        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (func is null) throw new ArgumentNullException(nameof(func));

            if (Dispatcher.UIThread.CheckAccess())
            {
                return Task.FromResult(func());
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// 비동기 <paramref name="func"/>을 UI 스레드에서 실행합니다.
        /// 현재 스레드가 UI 스레드라면 바로 호출하고, 아니면 Dispatcher에 Post 합니다.
        /// </summary>
        /// <param name="func">UI 스레드에서 실행할 비동기 함수.</param>
        /// <returns>함수 완료를 나타내는 Task.</returns>
        /// <exception cref="ArgumentNullException">func가 null인 경우.</exception>
        public Task InvokeAsync(Func<Task> func)
        {
            if (func is null) throw new ArgumentNullException(nameof(func));

            if (Dispatcher.UIThread.CheckAccess())
            {
                return func();
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await func().ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }
    }
}
