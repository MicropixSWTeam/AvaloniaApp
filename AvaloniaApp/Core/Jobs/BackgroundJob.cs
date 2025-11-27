using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    public sealed record BackgroundJob(string Name,Func<CancellationToken,Task> ExecuteAsync);
}
