using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApp.Core.Enums;   // OperationKey
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AvaloniaApp.Presentation.ViewModels.Operations
{
    /// <summary>
    /// 개별 작업(영역)의 상태를 표현하는 ViewModel용 클래스입니다.
    /// View에서는 IsRunning / Progress / Message / Error / CanCancel 를 바인딩해서 사용합니다.
    /// </summary>
    public partial class OperationState : ObservableObject
    {
        /// <summary>
        /// 현재 작업이 실행 중인지 여부입니다.
        /// true이면 UI에서 해당 Scope를 비활성화하는 데 사용할 수 있습니다.
        /// </summary>
        [ObservableProperty] private bool isRunning;

        /// <summary>
        /// 작업 진행률(0~100 등 임의의 스케일)을 나타냅니다.
        /// </summary>
        [ObservableProperty] private double progress;

        /// <summary>
        /// 현재 작업 상태에 대한 메시지(예: "진행 중...", "완료" 등)입니다.
        /// </summary>
        [ObservableProperty] private string? message;

        /// <summary>
        /// 작업 실패 시 에러 메시지를 저장합니다.
        /// </summary>
        [ObservableProperty] private string? error;

        /// <summary>
        /// 현재 작업을 취소하기 위한 내부 CancellationTokenSource입니다.
        /// </summary>
        private CancellationTokenSource? _cts;

        /// <summary>
        /// 현재 작업이 취소 가능하면 true입니다.
        /// (내부 CTS가 존재하고 아직 Cancel되지 않았으며, 작업이 실행 중인 경우)
        /// </summary>
        public bool CanCancel => _cts is { IsCancellationRequested: false } && IsRunning;

        /// <summary>
        /// 내부적으로 취소용 CTS를 설정합니다.
        /// RunOperationAsync에서 사용합니다.
        /// </summary>
        /// <param name="cts">새 CancellationTokenSource 또는 null.</param>
        internal void SetCancellation(CancellationTokenSource? cts)
        {
            _cts = cts;
            OnPropertyChanged(nameof(CanCancel));
        }

        /// <summary>
        /// 현재 작업에 취소를 요청합니다.
        /// 내부 CTS가 존재하고 아직 취소되지 않았다면 Cancel()을 호출합니다.
        /// </summary>
        public void Cancel()
        {
            if (_cts is { IsCancellationRequested: false })
            {
                _cts.Cancel();
                OnPropertyChanged(nameof(CanCancel));
            }
        }

        /// <summary>
        /// IsRunning 변경 시 CanCancel도 함께 변경되도록 갱신합니다.
        /// </summary>
        /// <param name="value">새 IsRunning 값.</param>
        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    /// <summary>
    /// 백그라운드 작업에서 Progress/Message를 업데이트하기 위한 컨텍스트입니다.
    /// </summary>
    public sealed class OperationContext
    {
        private readonly OperationState _state;
        private readonly UiDispatcher? _dispatcher;

        /// <summary>
        /// 새로운 OperationContext를 생성합니다.
        /// </summary>
        /// <param name="key">이 컨텍스트가 속한 OperationKey.</param>
        /// <param name="state">연결된 OperationState.</param>
        /// <param name="dispatcher">UI 업데이트에 사용할 UiDispatcher (null 가능).</param>
        internal OperationContext(OperationKey key, OperationState state, UiDispatcher? dispatcher)
        {
            Key = key;
            _state = state;
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// 현재 작업의 키입니다.
        /// </summary>
        public OperationKey Key { get; }

        /// <summary>
        /// 진행률과 상태 메시지를 업데이트합니다.
        /// UI 스레드에서 안전하게 업데이트되도록 필요시 UiDispatcher를 사용합니다.
        /// </summary>
        /// <param name="value">진행률 값.</param>
        /// <param name="message">표시할 메시지 (null이면 메시지를 변경하지 않음).</param>
        public void Report(double value, string? message = null)
        {
            void Apply()
            {
                _state.Progress = value;
                if (message is not null)
                    _state.Message = message;
            }

            if (_dispatcher is not null)
                _dispatcher.InvokeAsync(Apply);
            else
                Apply();
        }
    }

    /// <summary>
    /// RunOperationAsync에 전달할 UI 콜백 및 Job 설정 옵션입니다.
    /// </summary>
    public sealed class OperationOptions
    {
        /// <summary>
        /// BackgroundJobQueue에 등록할 Job 이름입니다.
        /// null이면 OperationKey.ToString()을 사용합니다.
        /// </summary>
        public string? JobName { get; set; }

        /// <summary>
        /// UI 스레드에서 작업 시작 시 호출할 콜백입니다.
        /// </summary>
        public Action<OperationState>? OnUiStart { get; set; }

        /// <summary>
        /// UI 스레드에서 작업 성공 시 호출할 콜백입니다.
        /// </summary>
        public Action<OperationState>? OnUiSuccess { get; set; }

        /// <summary>
        /// UI 스레드에서 작업 실패 시 호출할 콜백입니다.
        /// </summary>
        public Action<OperationState, Exception>? OnUiError { get; set; }
    }

    /// <summary>
    /// OperationState/RunOperationAsync를 제공하는 ViewModel용 베이스 클래스입니다.
    /// 전역 Busy 없이 OperationKey/OperationState 단위로 UI Scope를 제어합니다.
    /// </summary>
    public abstract partial class OperationViewModelBase : ObservableObject
    {
        /// <summary>
        /// 마지막 에러 메시지(디버그/로그용)입니다.
        /// </summary>
        [ObservableProperty] private string? lastError;

        /// <summary>
        /// 백그라운드 Job 실행에 사용할 큐입니다.
        /// null이면 Task.Run으로 대신 실행합니다.
        /// </summary>
        protected readonly BackgroundJobQueue? _backgroundJobQueue;

        /// <summary>
        /// UI 스레드 호출을 위한 Dispatcher 래퍼입니다.
        /// </summary>
        protected readonly UiDispatcher? _uiDispatcher;

        /// <summary>
        /// OperationKey별 OperationState 저장소입니다.
        /// </summary>
        private readonly Dictionary<OperationKey, OperationState> _operations = new();

        /// <summary>
        /// DI 없이 사용하는 파라미터 없는 기본 생성자입니다.
        /// 테스트용 등 특별한 경우에만 사용합니다.
        /// </summary>
        protected OperationViewModelBase()
        {
        }

        /// <summary>
        /// DI를 사용하여 필요한 인프라 서비스를 주입받는 생성자입니다.
        /// </summary>
        /// <param name="dialogService">에러 표시 등에 사용할 DialogService.</param>
        /// <param name="uiDispatcher">UI 스레드 호출용 UiDispatcher.</param>
        /// <param name="backgroundJobQueue">백그라운드 Job 큐.</param>
        /// <param name="logger">로그 출력용 ILogger.</param>
        protected OperationViewModelBase(
            UiDispatcher? uiDispatcher,
            BackgroundJobQueue? backgroundJobQueue)
        {
            _uiDispatcher = uiDispatcher;
            _backgroundJobQueue = backgroundJobQueue;
        }

        /// <summary>
        /// 지정된 OperationKey에 해당하는 OperationState를 가져옵니다.
        /// 존재하지 않으면 새로 생성합니다.
        /// </summary>
        /// <param name="key">조회할 OperationKey.</param>
        /// <returns>해당 키의 OperationState 인스턴스.</returns>
        protected OperationState GetOperationState(OperationKey key)
        {
            if (!_operations.TryGetValue(key, out var state))
            {
                state = new OperationState();
                _operations[key] = state;
            }

            return state;
        }

        /// <summary>
        /// UI 스레드에서 실행되어야 하는 Action을 호출합니다.
        /// UiDispatcher가 존재하면 이를 사용하고, 없으면 현재 스레드에서 바로 실행합니다.
        /// </summary>
        /// <param name="action">UI 스레드에서 실행할 작업.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        protected Task InvokeUiAsync(Action? action)
        {
            if (action is null)
                return Task.CompletedTask;

            if (_uiDispatcher is not null)
                return _uiDispatcher.InvokeAsync(action);

            action();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 지정된 OperationKey로 백그라운드 작업을 실행합니다.
        /// - 같은 키의 작업이 이미 실행 중이면 중복 실행을 스킵합니다.
        /// - OperationState의 IsRunning / Progress / Message / Error를 자동 관리합니다.
        /// - Cancel을 지원합니다(OperationState.Cancel()).
        /// </summary>
        /// <param name="key">작업을 식별할 OperationKey.</param>
        /// <param name="backgroundWork">
        /// 실제 백그라운드에서 실행할 함수입니다.
        /// CancellationToken과 OperationContext를 인자로 받습니다.
        /// </param>
        /// <param name="configure">
        /// UI 콜백 및 Job 이름 등을 설정하기 위한 옵션 구성 액션입니다.
        /// null이면 기본값(콜백 없음)으로 실행됩니다.
        /// </param>
        /// <param name="token">외부 취소를 위한 CancellationToken.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        protected async Task RunOperationAsync(
            OperationKey key,
            Func<CancellationToken, OperationContext, Task> backgroundWork,
            Action<OperationOptions>? configure = null,
            CancellationToken token = default)
        {
            if (backgroundWork is null)
                throw new ArgumentNullException(nameof(backgroundWork));

            var state = GetOperationState(key);

            // 같은 key 작업이 이미 실행 중이면 중복 실행 방지
            if (state.IsRunning)
                return;

            var options = new OperationOptions();
            configure?.Invoke(options);

            // 상태 초기화
            LastError = null;
            state.Progress = 0;
            state.Message = null;
            state.Error = null;

            state.IsRunning = true;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            state.SetCancellation(cts);

            var ct = cts.Token;
            var ctx = new OperationContext(key, state, _uiDispatcher);

            try
            {
                if (options.OnUiStart is not null)
                {
                    await InvokeUiAsync(() => options.OnUiStart(state)).ConfigureAwait(false);
                }

                if (_backgroundJobQueue is not null)
                {
                    var jobName = options.JobName ?? key.ToString();
                    var job = new BackgroundJob(
                        jobName,
                        innerCt => backgroundWork(innerCt, ctx),
                        skipIfExists: true); // Queue 레벨에서도 동일 key 중복 방지

                    await _backgroundJobQueue.EnqueueAsync(job, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Run(() => backgroundWork(ct, ctx), ct)
                        .ConfigureAwait(false);
                }

                if (options.OnUiSuccess is not null)
                {
                    await InvokeUiAsync(() => options.OnUiSuccess(state)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 취소는 조용히 처리
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
                LastError = ex.Message;

                if (options.OnUiError is not null)
                {
                    await InvokeUiAsync(() => options.OnUiError(state, ex)).ConfigureAwait(false);
                }
            }
            finally
            {
                state.SetCancellation(null);
                state.IsRunning = false;
            }
        }
    }
}
