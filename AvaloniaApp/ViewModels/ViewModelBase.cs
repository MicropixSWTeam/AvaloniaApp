using AvaloniaApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.ViewModels
{
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private bool isBusy;
        [ObservableProperty]
        private string? lastError;

        /// <summary>
        /// Busy / 예외 / 취소를 공통 처리하는 래퍼
        /// </summary>
        protected async Task RunSafeAsync(
            Func<CancellationToken, Task> operation,
            bool showErrorDialog = true,
            CancellationToken token = default)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            LastError = null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            try
            {
                await operation(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 취소는 보통 조용히 무시하거나 로그만 남김
            }
            catch (Exception ex)
            {
                LastError = ex.Message;

                // TODO: ILogger 있다면 여기서 로그
                //if (showErrorDialog && DialogService is not null)
                //{
                //    await DialogService.ShowMessageAsync("오류", ex.Message);
                //}
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
