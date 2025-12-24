using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure.Service;
using AvaloniaApp.Presentation.Operations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Operations
{
    public sealed class OperationRunner
    {
        private readonly BackgroundJobQueue _queue;
        private readonly UiService _ui;

        public OperationRunner(BackgroundJobQueue queue, UiService ui)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        public async Task RunAsync(
            OperationState state,
            Func<OperationContext, CancellationToken, Task> body,
            OperationOptions? options = null,
            CancellationToken externalToken = default)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            if (body is null) throw new ArgumentNullException(nameof(body));

            options ??= new OperationOptions();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            using var lifetimeCts = new CancellationTokenSource();

            await _ui.InvokeAsync(() =>
            {
                state.Reset(options.StartMessage);
                state.IsRunning = true;
                state.SetCancellation(cts);
                options.OnStart?.Invoke(state);
            }).ConfigureAwait(false);

            var ctx = new OperationContext(state, _ui, lifetimeCts.Token);

            Func<CancellationToken, Task> work = ct => body(ctx, ct);

            var job = new BackgroundJob(
                options.JobName ?? "Operation",
                work,
                externalCancellationToken: cts.Token,
                timeout: options.Timeout);

            Exception? captured = null;

            try
            {
                await _queue.EnqueueAndWaitAsync(job, waitToken: cts.Token).ConfigureAwait(false);
                await _ui.InvokeAsync(() => options.OnSuccess?.Invoke(state)).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
            {
                captured = oce;
                await _ui.InvokeAsync(() => state.Message = options.CanceledMessage).ConfigureAwait(false);
            }
            catch (TimeoutException tex)
            {
                captured = tex;
                await _ui.InvokeAsync(() =>
                {
                    state.Error = tex.Message;
                    state.Message = options.TimeoutMessage;
                    options.OnError?.Invoke(state, tex);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                captured = ex;
                await _ui.InvokeAsync(() =>
                {
                    state.Error = ex.Message;
                    options.OnError?.Invoke(state, ex);
                }).ConfigureAwait(false);
            }
            finally
            {
                // ★ 종료 이후 UI 업데이트 차단
                lifetimeCts.Cancel();

                try
                {
                    await _ui.InvokeAsync(() =>
                    {
                        state.SetCancellation(null);
                        state.IsRunning = false;
                        state.IsIndeterminate = false;
                    }).ConfigureAwait(false);
                }
                catch { /* UI 스레드 종료 시 예외 무시 */ }
            }

            if (captured is not null && options.Rethrow)
                throw captured;
        }
    }
}