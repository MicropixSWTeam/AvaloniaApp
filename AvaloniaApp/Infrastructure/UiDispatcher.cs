using Avalonia.Threading;
using AvaloniaApp.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public sealed class UiDispatcher:IUiDispatcher
    {
        public void Invoke(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }
        public Task InvokeAsync(Func<Task> action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return action();

            var tcs = new TaskCompletionSource();

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }
    }
}
