using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public sealed class UiDispatcher
    {
        public void Post(Action action, DispatcherPriority priority = default)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.UIThread.Post(action, priority);
        }

        public Task InvokeAsync(Action action, DispatcherPriority priority = default)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            var op = Dispatcher.UIThread.InvokeAsync(action, priority);
            return op.GetTask();
        }

        public async Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority = default)
        {
            if (func is null) throw new ArgumentNullException(nameof(func));

            if (Dispatcher.UIThread.CheckAccess())
                return func();

            var op = Dispatcher.UIThread.InvokeAsync(func, priority);
            return await op;
        }
    }
}