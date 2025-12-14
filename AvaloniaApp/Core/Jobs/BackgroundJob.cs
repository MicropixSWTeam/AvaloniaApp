using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed class BackgroundJob
    {
        private readonly TaskCompletionSource<object?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // (선택) 디버깅/추적용 메타데이터
        public Guid Id { get; } = Guid.NewGuid();
        public DateTimeOffset EnqueuedAtUtc { get; internal set; }
        public DateTimeOffset? StartedAtUtc { get; internal set; }
        public DateTimeOffset? CompletedAtUtc { get; internal set; }

        public string Name { get; }
        public Func<CancellationToken, Task> Work { get; }
        public TimeSpan? Timeout { get; init; }
        public CancellationToken ExternalCancellationToken { get; }

        public Task Completion => _tcs.Task;

        public BackgroundJob(
            string name,
            Func<CancellationToken, Task> work,
            CancellationToken externalCancellationToken = default,
            TimeSpan? timeout = null)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Job name is required.", nameof(name))
                : name;

            Work = work ?? throw new ArgumentNullException(nameof(work));
            ExternalCancellationToken = externalCancellationToken;
            Timeout = timeout;
        }

        internal Task ExecuteAsync(CancellationToken ct) => Work(ct);
        internal void TrySetCompleted() => _tcs.TrySetResult(null);
        internal void TrySetCanceled(CancellationToken token) => _tcs.TrySetCanceled(token);
        internal void TrySetFaulted(Exception ex) => _tcs.TrySetException(ex);
    }
}