using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Base
{
    public abstract partial class ViewModelBase : ObservableObject,IAsyncDisposable
    {
        [ObservableProperty] private string? lastError;

        protected readonly AppService _service;

        private readonly Dictionary<string, OperationState> _states = new();
        protected ViewModelBase()
        {

        }
        protected ViewModelBase(AppService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }
        public OperationState Op(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Operation key is required.", nameof(key));

            if (!_states.TryGetValue(key, out var s))
            {
                s = new OperationState();
                _states[key] = s;
            }

            return s;
        }

        protected Task UiInvokeAsync(Action action) => _service.Ui.InvokeAsync(action);

        protected async Task RunOperationAsync(
            string key,
            Func<CancellationToken, OperationContext, Task> backgroundWork,
            Action<OperationOptions>? configure = null,
            CancellationToken token = default)
        {
            if (backgroundWork is null) throw new ArgumentNullException(nameof(backgroundWork));

            var state = Op(key);
            if (state.IsRunning) return;

            var options = new OperationOptions();
            configure?.Invoke(options);

            await _service.OperationRunner.RunAsync(
                state,
                (ctx, ct) => backgroundWork(ct, ctx),
                options,
                token).ConfigureAwait(false);

            await UiInvokeAsync(() => LastError = state.Error).ConfigureAwait(false);
        }
        public virtual ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
