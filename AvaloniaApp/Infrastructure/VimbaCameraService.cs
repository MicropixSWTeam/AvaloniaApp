using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Infrastructure
{
    public class VimbaCameraService : IAsyncDisposable
    {
        private IVmbSystem _system;
        private SemaphoreSlim _gate = new(1, 1);

        private IReadOnlyList<ICamera>? _cameras;
        private IOpenCamera? _openCamera;
        private IAcquisition? _acquisition;

        private bool _disposed; 

        public CameraInfo? ConnectedCameraInfo { get; private set; }
        public Bitmap? LastCapturedImage { get; private set; }
        public VimbaCameraService()
        {
            //_system = IVmbSystem.Startup();
        }
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VimbaCameraService));
        }
        public Task<IReadOnlyList<PixelFormatInfo>> GetSupportPixelformatListAsync(
    CancellationToken ct,
    string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            // 카메라 열기 (편의상 OpenCameraByID 사용, 없으면 기존 코드처럼 GetCameraByID + Open 써도 됨)
            _openCamera = _system.OpenCameraByID(id);

            // 1) "PixelFormat"이라는 이름의 Feature를 인덱서로 직접 꺼낸다.
            //    (문서에서 말하는 "dictionary key" 방식)
            var pixelFormatFeatureDynamic = _openCamera.Features["PixelFormat"];

            if (pixelFormatFeatureDynamic is null)
                throw new InvalidOperationException("Camera does not expose 'PixelFormat' feature.");

            // 2) Enum 타입으로 캐스팅
            var pfFeature = pixelFormatFeatureDynamic as IEnumFeature;
            if (pfFeature is null)
                throw new InvalidOperationException("'PixelFormat' feature is not an enum feature.");

            // 3) Enum 엔트리 리스트 가져오기
            var entries = pfFeature.EnumEntriesByName; // IDictionary<string, IEnumEntry>

            var list = new List<PixelFormatInfo>(entries.Count);

            foreach (var kv in entries)
            {
                var entry = kv.Value;

                // 실제로 사용할 수 있는 값만 UI에 보여주고 싶으면 IsAvailable == true만 필터
                if (!entry.IsAvailable)
                    continue;

                list.Add(new PixelFormatInfo(
                    name: entry.Name,            // API에서 쓰는 이름 (예: "Mono8", "RGB8")
                    displayName: entry.DisplayName, // GUI에 보여줄 이름
                    isAvailable: entry.IsAvailable));
            }

            return Task.FromResult<IReadOnlyList<PixelFormatInfo>>(list);
        }
        public Task<IReadOnlyList<CameraInfo>> GetCameraListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var cameras = _system.GetCameras();  // VmbNET 공식 API
            _cameras = cameras;

            var result = cameras
                .Select(c => new CameraInfo(
                    id: c.Id,
                    name: c.Name,
                    serial: c.Serial,
                    modelName: c.ModelName))
                .ToArray();

            return Task.FromResult<IReadOnlyList<CameraInfo>>(result);
        }

        public async Task ConnectAsync(CancellationToken ct,string id)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

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
        ValueTask IAsyncDisposable.DisposeAsync()
        {
            _system.Shutdown();
            throw new NotImplementedException();
        }
    }
}
