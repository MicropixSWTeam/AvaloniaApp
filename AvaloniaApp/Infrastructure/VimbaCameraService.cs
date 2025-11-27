using AvaloniaApp.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public class VimbaCameraService : ICameraService
    {
        public Task CaptureAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task ConnectAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task DisconnectAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
