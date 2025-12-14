using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed class BackgroundJobWorker : BackgroundService
    {
        private readonly BackgroundJobQueue _queue;
        private readonly TimeSpan _defaultJobTimeout;

        public BackgroundJobWorker(BackgroundJobQueue queue, TimeSpan? defaultJobTimeout = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _defaultJobTimeout = defaultJobTimeout ?? Timeout.InfiniteTimeSpan;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                BackgroundJob job;
                try
                {
                    job = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (ChannelClosedException) { break; }

                var timeout = job.Timeout ?? _defaultJobTimeout;

                if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or Infinite.");

                // 0도 타임아웃으로 인정(즉시 timeout)
                var hasTimeout = timeout != Timeout.InfiniteTimeSpan;

                using var timeoutCts = hasTimeout ? new CancellationTokenSource(timeout) : null;
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    job.ExternalCancellationToken,
                    timeoutCts?.Token ?? CancellationToken.None);

                var prevQueue = BackgroundJobExecutionContext.CurrentQueue.Value;
                var prevDepth = BackgroundJobExecutionContext.Depth.Value;
                BackgroundJobExecutionContext.CurrentQueue.Value = _queue;
                BackgroundJobExecutionContext.Depth.Value = prevDepth + 1;

                job.StartedAtUtc = DateTimeOffset.UtcNow;

                try
                {
                    // (1) 실행 전 이미 취소/타임아웃이면 실행 스킵
                    if (linkedCts.Token.IsCancellationRequested)
                    {
                        CompleteByToken(job, stoppingToken, timeoutCts, timeout);
                        continue;
                    }

                    await job.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);

                    // (2) 작업이 취소/타임아웃을 조용히 return 해도 결과를 정규화
                    CompleteByToken(job, stoppingToken, timeoutCts, timeout);
                }
                catch (OperationCanceledException)
                {
                    // (3) OCE도 동일 로직으로 정규화
                    CompleteByToken(job, stoppingToken, timeoutCts, timeout);
                }
                catch (Exception ex)
                {
                    job.TrySetFaulted(ex);
                }
                finally
                {
                    job.CompletedAtUtc = DateTimeOffset.UtcNow;

                    BackgroundJobExecutionContext.Depth.Value = prevDepth;
                    BackgroundJobExecutionContext.CurrentQueue.Value = prevQueue;
                }
            }
        }

        private static void CompleteByToken(
            BackgroundJob job,
            CancellationToken stoppingToken,
            CancellationTokenSource? timeoutCts,
            TimeSpan timeout)
        {
            // 우선순위: stopping > external > timeout
            if (stoppingToken.IsCancellationRequested)
            {
                job.TrySetCanceled(stoppingToken);
                return;
            }

            if (job.ExternalCancellationToken.IsCancellationRequested)
            {
                job.TrySetCanceled(job.ExternalCancellationToken);
                return;
            }

            if (timeoutCts?.IsCancellationRequested == true)
            {
                job.TrySetFaulted(new TimeoutException(
                    $"Job '{job.Name}' timed out after {timeout}."));
                return;
            }

            job.TrySetCompleted();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _queue.Complete();

            // 큐에 남은 작업은 즉시 취소 완료
            var canceledToken = new CancellationToken(canceled: true);
            while (_queue.TryDequeue(out var pending))
                pending.TrySetCanceled(canceledToken);

            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}