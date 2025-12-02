using AvaloniaApp.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed class BackgroundJobQueue 
    {
        private readonly Channel<BackgroundJob> _channel;

        public BackgroundJobQueue(int capacity = 100)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                SingleWriter = false,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            };

            _channel = Channel.CreateBounded<BackgroundJob>(options);
        }

        public ValueTask EnqueueAsync(BackgroundJob job, CancellationToken ct = default)
            => _channel.Writer.WriteAsync(job, ct);

        public ValueTask<BackgroundJob> DequeueAsync(CancellationToken ct)
            => _channel.Reader.ReadAsync(ct);
    }
}
