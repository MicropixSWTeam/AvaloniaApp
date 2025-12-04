using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed class BackgroundJobQueue
    {
        private readonly Channel<BackgroundJob> _channel;

        // capacity 파라미터는 더 이상 쓰지 않지만,
        // 기존 호출부 깨지지 않게 시그니처만 유지
        public BackgroundJobQueue(int capacity = 100)
        {
            var options = new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            };

            _channel = Channel.CreateUnbounded<BackgroundJob>(options);
        }

        public ValueTask EnqueueAsync(BackgroundJob job, CancellationToken ct = default)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            return _channel.Writer.WriteAsync(job, ct);
        }

        public ValueTask<BackgroundJob> DequeueAsync(CancellationToken ct)
            => _channel.Reader.ReadAsync(ct);
    }
}
