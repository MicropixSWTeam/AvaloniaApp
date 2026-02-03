using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Interfaces
{
    public interface ICameraService : IAsyncDisposable
    {
        ChannelReader<FrameData> Frames { get; }
        CameraData? ConnectedCameraInfo { get; }
        bool IsStreaming { get; }

        event Action<bool>? StreamingStateChanged;
        event Action<CameraConnectionState>? ConnectionStateChanged;
        event Action<string>? ErrorOccurred;

        Task<IReadOnlyList<CameraData>> GetCameraListAsync(CancellationToken ct);
        Task StartPreviewAsync(CancellationToken ct, string cameraId);
        Task StopPreviewAndDisconnectAsync(CancellationToken ct);

        Task<double> GetExposureTimeAsync(CancellationToken ct);
        Task<double> SetExposureTimeAsync(double val, CancellationToken ct);
        Task<double> GetGainAsync(CancellationToken ct);
        Task<double> SetGainAsync(double val, CancellationToken ct);
        Task<double> GetGammaAsync(CancellationToken ct);
        Task<double> SetGammaAsync(double val, CancellationToken ct);
    }

    public interface IPopup
    {
        public string Title { get; set; }   
        public int Width { get; set; }
        public int Height { get; set; }
    }
    // 팝업이 닫힐 때 결과값을 전달하는 이벤트 인자
    public class DialogResultEventArgs : EventArgs
    {
        public object? Result { get; }
        public DialogResultEventArgs(object? result) => Result = result;
    }

    // 닫기 요청 기능 (결과 반환 포함)
    public interface IDialogRequestClose
    {
        event EventHandler<DialogResultEventArgs>? CloseRequested;
    }
}
