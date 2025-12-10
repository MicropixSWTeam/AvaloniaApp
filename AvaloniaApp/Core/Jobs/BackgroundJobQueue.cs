using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    /// <summary>
    /// 단일 워커 기반 백그라운드 Job 큐입니다.
    /// Job을 순차 실행하고, Name을 기준으로 중복 실행을 제어합니다.
    /// </summary>
    public sealed class BackgroundJobQueue : IDisposable
    {
        private readonly Channel<QueuedJob> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _workerTask;
        private readonly ConcurrentDictionary<string, byte> _activeKeys = new();

        /// <summary>
        /// 새로운 BackgroundJobQueue 인스턴스를 생성하고
        /// 내부 워커 루프를 시작합니다.
        /// </summary>
        public BackgroundJobQueue()
        {
            _channel = Channel.CreateUnbounded<QueuedJob>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _workerTask = Task.Run(WorkerLoop);
        }

        /// <summary>
        /// Job을 큐에 등록하고, 해당 Job이 완료될 때까지 비동기로 대기합니다.
        /// SkipIfExists가 true이고 동일한 Name Job이 active인 경우는 바로 반환합니다.
        /// </summary>
        /// <param name="job">큐에 등록할 BackgroundJob.</param>
        /// <param name="cancellationToken">대기 취소용 토큰.</param>
        /// <returns>Job 완료를 나타내는 Task.</returns>
        /// <exception cref="ArgumentNullException">job이 null인 경우.</exception>
        /// <exception cref="OperationCanceledException">대기 중 취소된 경우.</exception>
        public async Task EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken = default)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));

            cancellationToken.ThrowIfCancellationRequested();

            var key = job.Name ?? string.Empty;

            if (job.SkipIfExists && key.Length != 0 && _activeKeys.ContainsKey(key))
            {
                // 이미 같은 Key Job 이 큐/실행 중
                return;
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (key.Length != 0)
            {
                _activeKeys.TryAdd(key, 0);
            }

            var queued = new QueuedJob(key, job.Work, linkedCts, tcs);

            await _channel.Writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);

            // Job 실행 완료까지 대기 (UI 스레드가 아니므로 블로킹 아님)
            await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// 내부 워커 루프입니다.
        /// 채널에 들어온 Job을 순차적으로 실행합니다.
        /// </summary>
        private async Task WorkerLoop()
        {
            var reader = _channel.Reader;

            try
            {
                while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        try
                        {
                            await item.Work(item.Cts.Token).ConfigureAwait(false);
                            item.Completion.TrySetResult(null);
                        }
                        catch (OperationCanceledException oce)
                        {
                            item.Completion.TrySetException(oce);
                        }
                        catch (Exception ex)
                        {
                            item.Completion.TrySetException(ex);
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(item.Key))
                            {
                                _activeKeys.TryRemove(item.Key, out _);
                            }

                            item.Cts.Dispose();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 큐 전체 종료 시
            }
        }

        /// <summary>
        /// 큐를 종료하고 내부 워커를 정리합니다.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _channel.Writer.TryComplete();

            try
            {
                _workerTask.Wait(2000);
            }
            catch
            {
                // 무시
            }

            _cts.Dispose();
        }

        /// <summary>
        /// 내부에서 사용하는 큐잉된 Job 표현입니다.
        /// </summary>
        private sealed class QueuedJob
        {
            /// <summary>
            /// Job 이름 (중복 방지 키).
            /// </summary>
            public string Key { get; }

            /// <summary>
            /// 실제 실행할 작업 함수.
            /// </summary>
            public Func<CancellationToken, Task> Work { get; }

            /// <summary>
            /// Job 전용 CancellationTokenSource.
            /// </summary>
            public CancellationTokenSource Cts { get; }

            /// <summary>
            /// 외부에 완료 여부를 알리기 위한 TaskCompletionSource.
            /// </summary>
            public TaskCompletionSource<object?> Completion { get; }

            /// <summary>
            /// 새 QueuedJob을 생성합니다.
            /// </summary>
            public QueuedJob(
                string key,
                Func<CancellationToken, Task> work,
                CancellationTokenSource cts,
                TaskCompletionSource<object?> completion)
            {
                Key = key;
                Work = work;
                Cts = cts;
                Completion = completion;
            }
        }
    }
}
