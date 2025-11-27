using AvaloniaApp.Core.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Interfaces
{
    public interface ICameraService
    {
        Task ConnectAsync(CancellationToken ct);
        Task DisconnectAsync(CancellationToken ct); 
        Task CaptureAsync(CancellationToken ct);
    }
    public interface IBackgroundJobQueue
    {
        ValueTask EnqueueAsync(BackgroundJob job, CancellationToken ct = default);
        ValueTask<BackgroundJob> DequeueAsync(CancellationToken ct);
    }
    public interface IUiDispatcher
    {
        void Invoke(Action action);
        Task InvokeAsync(Func<Task> action);
    }   
    public interface IPipeline
    {

    }
}
