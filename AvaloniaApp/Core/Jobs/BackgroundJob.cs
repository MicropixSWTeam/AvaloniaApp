using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    /// <summary>
    /// 채널에 들어가는 단일 작업 단위.
    /// Name: 디버그/로그용 이름
    /// Timeout: 이 잡에만 적용되는 타임아웃 (null이면 기본값 사용)
    /// </summary>
    public sealed class BackgroundJob
    {
        public string Name { get; }
        public TimeSpan? Timeout { get; }

        private readonly Func<CancellationToken, Task> _handler;

        public BackgroundJob(
            string name,
            Func<CancellationToken, Task> handler,
            TimeSpan? timeout = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Timeout = timeout;
        }

        public Task ExecuteAsync(CancellationToken token)
            => _handler(token);
    }
}
