using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Base
{
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private bool isBusy;
        [ObservableProperty]
        private string? lastError;

        protected readonly DialogService? _dialogService;
        protected readonly BackgroundJobQueue? _backgroundJobQueue;
        protected readonly UiDispatcher? _uiDispatcher;
        protected readonly ILogger? _logger;

        public ViewModelBase()
        {
        }
        public ViewModelBase(DialogService? dialogService,UiDispatcher uiDispatcher,BackgroundJobQueue backgroundJobQueue)
        {
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
            _backgroundJobQueue = backgroundJobQueue;
        }
        /// <summary>
        /// Busy / 예외 / 취소를 한 번에 처리하는 공통 비동기 래퍼
        /// </summary>
        protected async Task RunSafeAsync(Func<CancellationToken, Task> operation,bool showErrorDialog = true,CancellationToken token = default)
        {
            // 이미 다른 작업이 돌고 있으면 무시
            if (IsBusy)
                return;

            IsBusy = true;
            LastError = null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = cts.Token;

            try
            {
                await operation(ct);
            }
            catch (OperationCanceledException)
            {
                // 취소는 보통 조용히 넘김 (필요하면 상태 메시지만 갱신)
            }
            catch (Exception ex)
            {
                LastError = ex.Message;

                if (showErrorDialog && _dialogService is not null)
                {
                    await _dialogService.ShowMessageAsync(
                        "오류",
                        ex.Message
                    );
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 결과값이 필요한 경우용 제네릭 버전
        /// </summary>
        protected async Task<T?> RunSafeAsync<T>(Func<CancellationToken, Task<T>> operation,bool showErrorDialog = true,CancellationToken token = default)
        {
            if (IsBusy)
                return default;

            IsBusy = true;
            LastError = null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = cts.Token;

            try
            {
                return await operation(ct);
            }
            catch (OperationCanceledException)
            {
                return default;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;

                if (showErrorDialog && _dialogService is not null)
                {
                    await _dialogService.ShowMessageAsync(
                        "오류",
                        ex.Message
                    );
                }

                return default;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
