using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    /// <summary>
    /// 큐에서 잡을 하나씩 꺼내서 실행하는 워커.
    /// - 잡마다 개별 타임아웃 (기본 4초)
    /// - 잡에서 예외/취소 발생해도 워커는 계속 동작
    /// - 호스트 종료(stoppingToken 취소) 때만 루프 종료
    /// </summary>
    public sealed class BackgroundJobWorker : BackgroundService
    {
        private readonly BackgroundJobQueue _queue;
        private readonly ILogger<BackgroundJobWorker>? _logger;
        private readonly TimeSpan _defaultJobTimeout;

        public BackgroundJobWorker(
            BackgroundJobQueue queue,
            ILogger<BackgroundJobWorker>? logger = null,
            TimeSpan? defaultJobTimeout = null)
        {
            _queue = queue;
            _logger = logger;
            _defaultJobTimeout = defaultJobTimeout ?? TimeSpan.FromSeconds(4);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                BackgroundJob job;

                // 1) 큐에서 잡 꺼내기
                try
                {
                    job = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 호스트가 내려가는 중
                    break;
                }

                var timeout = job.Timeout ?? _defaultJobTimeout;

                // 2) 잡 실행 (개별 타임아웃 + 예외 방어)
                try
                {
                    using var cts =
                        CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                    if (timeout > TimeSpan.Zero &&
                        timeout != Timeout.InfiniteTimeSpan)
                    {
                        cts.CancelAfter(timeout);
                    }

                    await job.ExecuteAsync(cts.Token);
                }
                catch (OperationCanceledException oce)
                {
                    // 호스트 종료이면 루프 종료
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // 그 외(타임아웃/잡 내부 취소)는 경고만 찍고 다음 잡 계속
                    _logger?.LogWarning(
                        oce,
                        "Background job {JobName} was cancelled or timed out after {Timeout} seconds.",
                        job.Name,
                        timeout.TotalSeconds);
                }
                catch (Exception ex)
                {
                    // 잡 하나 실패해도 워커는 계속 돌아간다
                    _logger?.LogError(
                        ex,
                        "Background job {JobName} failed with exception.",
                        job.Name);
                }
            }
        }
    }
}
