using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? lastError;

    // 추가: 로딩 메시지(선택)
    [ObservableProperty]
    private string? busyMessage;

    protected readonly DialogService? _dialogService;
    protected readonly BackgroundJobQueue? _backgroundJobQueue;
    protected readonly UiDispatcher? _uiDispatcher;

    public ViewModelBase()
    {
    }

    public ViewModelBase(DialogService? dialogService, UiDispatcher uiDispatcher, BackgroundJobQueue backgroundJobQueue)
    {
        _dialogService = dialogService;
        _uiDispatcher = uiDispatcher;
        _backgroundJobQueue = backgroundJobQueue;
    }

    protected async Task RunSafeAsync(
        Func<CancellationToken, Task> operation,
        bool showErrorDialog = true,
        string? busyMessage = null,
        CancellationToken token = default)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        BusyMessage = busyMessage;
        LastError = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = cts.Token;

        try
        {
            await operation(ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;

            if (showErrorDialog && _dialogService is not null)
            {
                await _dialogService.ShowMessageAsync("오류", ex.Message);
            }
        }
        finally
        {
            BusyMessage = null;
            IsBusy = false;
        }
    }

    protected async Task<T?> RunSafeAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool showErrorDialog = true,
        string? busyMessage = null,
        CancellationToken token = default)
    {
        if (IsBusy)
            return default;

        IsBusy = true;
        BusyMessage = busyMessage;
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
                await _dialogService.ShowMessageAsync("오류", ex.Message);
            }

            return default;
        }
        finally
        {
            BusyMessage = null;
            IsBusy = false;
        }
    }
}
