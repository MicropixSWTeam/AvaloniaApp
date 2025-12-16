using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModelTest : ViewModelBase
    {
        // 핵심 서비스 (AppServices에서 분해하여 보관하거나 직접 참조)
        private readonly VimbaCameraService _cameraService;
        private readonly ImageProcessServiceTest _imageProcessService;
        private readonly UiThrottler _throttler;

        // 백그라운드 루프 제어용 토큰 및 태스크
        private CancellationTokenSource? _consumeCts;
        private Task? _consumeTask;

        // UI 렌더링용 비트맵 및 데이터
        private WriteableBitmap? _previewFullImage;
        private FrameData? _previewFullFrameData; // UI로 보낼 대기 중인 최신 프레임

        // 캡처 제어 플래그 (0: 중지, 1: 캡처 중)
        // 백그라운드 스레드와 공유되므로 int + Interlocked 사용
        private int _isCapturing;

        // FPS 계산용 변수
        private long _lastRenderTs;
        private double _fpsEma;

        // 뷰(Code-behind)에서 화면 갱신을 위해 구독하는 이벤트
        public event Action? PreviewInvalidated;

        // UI 바인딩 속성들
        [ObservableProperty] private ObservableCollection<CameraInfo> cameras = new();
        [ObservableProperty] private CameraInfo? selectedCamera;
        [ObservableProperty] private Bitmap? previewBitmap;
        [ObservableProperty] private bool isPreviewing;
        [ObservableProperty] private double previewFps;
        [ObservableProperty] private int capturedFrameCount;

        // 생성자: AppServices 하나만 받아서 처리 (Facade 패턴 적용)
        public CameraViewModelTest(AppService service) : base(service)
        {
            _cameraService = service.Camera ?? throw new ArgumentNullException("CameraService missing"); 

            _imageProcessService = service.ImageProcess; 

            _throttler = _service.Ui.CreateThrottler();
        }

        // [명령] 카메라 목록 새로고침
        [RelayCommand]
        private async Task RefreshCamerasAsync()
        {
            await RunOperationAsync(
                key: "RefreshCameras",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("카메라 목록을 찾는 중입니다...");
                    var list = await _cameraService.GetCameraListAsync(ct).ConfigureAwait(false);

                    await UiInvokeAsync(() =>
                    {
                        Cameras.Clear();
                        foreach (var c in list) Cameras.Add(c);
                        // 첫 번째 카메라 자동 선택
                        SelectedCamera ??= Cameras.FirstOrDefault();
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "GetCameraList";
                    opt.Timeout = TimeSpan.FromSeconds(3);
                });
        }

        // [명령] 프리뷰 시작
        [RelayCommand]
        private async Task StartPreviewAsync()
        {
            // 카메라가 없으면 새로고침 시도
            if (Cameras.Count == 0) await RefreshCamerasAsync();

            var cam = SelectedCamera;
            if (cam is null) return; // 선택된 카메라 없음

            await RunOperationAsync(
                key: "PreviewStart",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("카메라 연결 및 프리뷰 시작 중...");

                    // 1. 카메라 연결 (오래 걸릴 수 있음)
                    await _cameraService.StartPreviewAsync(ct, cam.Id).ConfigureAwait(false);

                    // 2. 프레임 소비 루프 시작 (백그라운드)
                    RestartConsumeLoop();

                    // 3. UI 상태 업데이트
                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = true;
                        PreviewFps = 0;
                        _fpsEma = 0;
                        _lastRenderTs = 0;
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "StartPreview";
                    opt.Timeout = TimeSpan.FromSeconds(10); // 넉넉하게
                });
        }

        // [명령] 프리뷰 정지
        [RelayCommand]
        private async Task StopPreviewAsync()
        {
            await RunOperationAsync(
                key: "PreviewStop",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("프리뷰 정지 및 연결 해제 중...");

                    // 1. 소비 루프 중단
                    CancelConsumeLoop();
                    if (_consumeTask != null) await _consumeTask.ConfigureAwait(false);

                    // 2. 카메라 연결 해제
                    await _cameraService.StopPreviewAndDisconnectAsync(ct).ConfigureAwait(false);

                    // 3. UI 상태 업데이트
                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = false;
                        PreviewFps = 0;
                        // 중요: PreviewBitmap을 null로 초기화하지 않음 -> 마지막 화면 유지
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "StopPreview";
                    opt.Timeout = TimeSpan.FromSeconds(5);
                });
        }

        // [명령] 캡처 시작
        [RelayCommand]
        private void StartCapture()
        {
            if (!IsPreviewing) return;

            // 캡처 세션 초기화 (필요 시)
            // _services.Spectral.StartNewSession();

            CapturedFrameCount = 0;
            // 플래그를 1로 설정하여 백그라운드 루프가 저장하도록 함
            Interlocked.Exchange(ref _isCapturing, 1);
        }

        // [명령] 캡처 중지
        [RelayCommand]
        private void StopCapture()
        {
            Interlocked.Exchange(ref _isCapturing, 0);
            // 필요 시 세션 저장 완료 알림
        }

        // 내부 로직: 소비 루프 재시작
        private void RestartConsumeLoop()
        {
            CancelConsumeLoop();
            _consumeCts = new CancellationTokenSource();
            _consumeTask = ConsumeFramesAsync(_consumeCts.Token);
        }

        // 내부 로직: 소비 루프 취소
        private void CancelConsumeLoop()
        {
            try { _consumeCts?.Cancel(); } catch { }
            _consumeCts?.Dispose();
            _consumeCts = null;
        }

        // [핵심] 백그라운드 프레임 소비 루프
        private async Task ConsumeFramesAsync(CancellationToken ct)
        {
            var reader = _cameraService.Frames;

            try
            {
                // 채널에서 데이터가 들어올 때까지 대기
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    // 들어온 데이터를 하나씩 꺼냄
                    while (reader.TryRead(out var frame))
                    {
                        // 1. 캡처 로직 (백그라운드에서 처리)
                        if (Volatile.Read(ref _isCapturing) == 1)
                        {
                            // 원본 프레임 안전하게 복제 (ArrayPool 사용)
                            var captured = FrameData.CloneFullFrame(frame);

                            // TODO: 여기서 영상 처리(Crop 등) 후 저장
                            // _services.ImageProcess.Save(captured); 또는
                            // _services.Workspace.Add(captured);

                            // 임시: 저장 로직이 없으므로 누수 방지를 위해 해제
                            captured.Dispose();

                            // UI에 캡처 카운트 갱신 (단순 작업이라 Post 사용)
                            _service.Ui.Post(() => CapturedFrameCount++);
                        }

                        // 2. UI 렌더링용 데이터 갱신 (Coalescing: 최신만 유지)
                        // 이전 대기 중이던 프레임은 버리고(Dispose) 새 것으로 교체
                        var old = Interlocked.Exchange(ref _previewFullFrameData, frame);
                        old?.Dispose();

                        // 3. UI 갱신 요청 (Throttling: 화면 갱신 속도 조절)
                        // UI 스레드가 바쁘면 이번 요청은 무시됨
                        _throttler.Run(RenderPendingOnUi);
                    }
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConsumeFramesAsync Error: {ex}");
            }
            finally
            {
                // 루프 종료 시 대기 중인 프레임 정리
                var pending = Interlocked.Exchange(ref _previewFullFrameData, null);
                pending?.Dispose();
                _throttler.Reset();
            }
        }

        // [UI 스레드] 실제 화면 그리기
        private void RenderPendingOnUi()
        {
            // 대기 중인 최신 프레임 가져오기 (소유권 이전)
            var packet = Interlocked.Exchange(ref _previewFullFrameData, null);
            if (packet is null) return;

            try
            {
                // 비트맵 준비 (크기가 다르면 새로 생성)
                EnsureSharedPreview(packet.Width, packet.Height);

                if (_previewFullImage is null) return;

                // WriteableBitmap에 픽셀 데이터 고속 복사
                using (var buffer = _previewFullImage.Lock())
                {
                    unsafe
                    {
                        Buffer.MemoryCopy(
                            (void*)System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(packet.Bytes, 0),
                            (void*)buffer.Address,
                            buffer.Size.Height * buffer.RowBytes,
                            packet.Length);
                    }
                }

                // Avalonia 11.0 이상인 경우, 변경 사항 알림이 필요할 수 있음 (대부분 자동 처리됨)
                // _previewFullImage.AddDirtyRect(new Rect(0, 0, packet.Width, packet.Height));

                UpdatePreviewFpsOnUi();

                // 뷰에게 갱신 알림 (필요 시)
                PreviewInvalidated?.Invoke();
            }
            finally
            {
                // 렌더링 끝났으니 반납 (ArrayPool로 돌아감)
                packet.Dispose();
            }
        }

        // 비트맵 초기화 및 리사이징 처리
        private void EnsureSharedPreview(int width, int height)
        {
            if (_previewFullImage is not null)
            {
                var ps = _previewFullImage.PixelSize;
                if (ps.Width == width && ps.Height == height)
                    return; // 크기 같으면 재사용

                _previewFullImage.Dispose();
                _previewFullImage = null;
            }

            _previewFullImage = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Gray8, // 흑백 카메라 기준
                AlphaFormat.Opaque);

            // 바인딩된 속성 업데이트 (화면 깜빡임 방지를 위해 교체 시에만)
            PreviewBitmap = _previewFullImage;
        }

        // FPS 계산 (EMA 필터 적용)
        private void UpdatePreviewFpsOnUi()
        {
            long now = Stopwatch.GetTimestamp();
            long last = _lastRenderTs;
            _lastRenderTs = now;

            if (last == 0) return;

            double dt = (double)(now - last) / Stopwatch.Frequency;
            if (dt <= 0) return;

            double inst = 1.0 / dt; // 순간 FPS

            // 지수 이동 평균 (Alpha = 0.2)
            const double alpha = 0.20;
            _fpsEma = (_fpsEma <= 0) ? inst : (_fpsEma + (inst - _fpsEma) * alpha);

            PreviewFps = _fpsEma;
        }

        // 뷰모델 종료 시 정리
        public override async ValueTask DisposeAsync()
        {
            // 루프 중단
            CancelConsumeLoop();
            if (_consumeTask != null) await _consumeTask.ConfigureAwait(false);

            // 카메라 연결 해제 (안전하게)
            try
            {
                await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

            // 비트맵 해제
            _previewFullImage?.Dispose();

            await base.DisposeAsync();
        }
    }
}