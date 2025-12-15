using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed class BackgroundJobQueue
    {
        private readonly Channel<BackgroundJob> _channel;

        public BackgroundJobQueue(int? capacity = null)
        {
            if (capacity is > 0)
            {
                _channel = Channel.CreateBounded<BackgroundJob>(new BoundedChannelOptions(capacity.Value)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            }
            else
            {
                _channel = Channel.CreateUnbounded<BackgroundJob>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            }
        }

        public async ValueTask EnqueueAsync(BackgroundJob job, CancellationToken enqueueToken = default)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));

            // 이미 외부 취소면 큐에 넣지 않고 즉시 취소 완료
            if (job.ExternalCancellationToken.IsCancellationRequested)
            {
                job.TrySetCanceled(job.ExternalCancellationToken);
                return;
            }

            job.EnqueuedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await _channel.Writer.WriteAsync(job, enqueueToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // enqueue 대기 중 취소되면 completion도 정리
                if (enqueueToken.IsCancellationRequested)
                    job.TrySetCanceled(enqueueToken);
                else if (job.ExternalCancellationToken.IsCancellationRequested)
                    job.TrySetCanceled(job.ExternalCancellationToken);
                else
                    job.TrySetCanceled(new CancellationToken(canceled: true));

                throw;
            }
            catch (ChannelClosedException ex)
            {
                job.TrySetFaulted(new InvalidOperationException("BackgroundJobQueue is closed.", ex));
                throw;
            }
            catch (Exception ex)
            {
                job.TrySetFaulted(ex);
                throw;
            }
        }

        public async Task EnqueueAndWaitAsync(
            BackgroundJob job,
            CancellationToken enqueueToken = default,
            CancellationToken waitToken = default)
        {
            if (BackgroundJobExecutionContext.IsRunningInside(this))
            {
                throw new InvalidOperationException(
                "Do not EnqueueAndWait into the same BackgroundJobQueue from inside a running BackgroundJob. " +
                "Call the inner method directly, or use a separate queue.");
            }

            await EnqueueAsync(job, enqueueToken).ConfigureAwait(false);
            await job.Completion.WaitAsync(waitToken).ConfigureAwait(false);
        }

        public ValueTask<BackgroundJob> DequeueAsync(CancellationToken ct)
            => _channel.Reader.ReadAsync(ct);

        public bool TryDequeue(out BackgroundJob job)
            => _channel.Reader.TryRead(out job);

        public void Complete(Exception? error = null)
            => _channel.Writer.TryComplete(error);
    }
}