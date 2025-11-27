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
        private readonly ILogger<BackgroundJobWorker> _logger;

        public BackgroundJobWorker(IBackgroundJobQueue queue,ILogger<BackgroundJobWorker> logger)
        {
            _queue = queue;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackgroundJobWorker started");

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
                    _logger.LogInformation("Job {Name} started", job.Name);
                    await job.ExecuteAsync(stoppingToken);
                    _logger.LogInformation("Job {Name} completed", job.Name);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Job {Name} canceled", job.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {Name} failed", job.Name);
                }
            }

            _logger.LogInformation("BackgroundJobWorker stopped");
        }
    }
}
