using AvaloniaApp.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed class BackgroundJobWorker:BackgroundService
    {
        private readonly IBackgroundJobQueue _queue;

        public BackgroundJobWorker(IBackgroundJobQueue queue)
        {
            _queue = queue;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                BackgroundJob job;

                try
                {
                    job = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await job.ExecuteAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

        }
    }
}
