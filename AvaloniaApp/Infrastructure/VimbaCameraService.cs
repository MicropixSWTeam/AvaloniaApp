using AvaloniaApp.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Infrastructure
{
    public class VimbaCameraService : ICameraService
    {
        public Task GetCameraList(CancellationToken ct)
        {
            using IVmbSystem vmbSystem = IVmbSystem.Startup();
            var cameras = vmbSystem.GetCameras().ToList();
            return Task.CompletedTask;
        }
        public Task ConnectAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task DisconnectAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task StartAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task StopAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task StartStreamAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }   

        public Task StopStreamAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task CaptureAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

    }
}
