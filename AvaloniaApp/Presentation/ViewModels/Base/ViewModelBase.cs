using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.Base
{
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty] private string? lastError;

        protected readonly UiDispatcher _ui;
        protected readonly OperationRunner _runner;

        private readonly Dictionary<string, OperationState> _states = new();
        protected ViewModelBase()
        {

        }
        protected ViewModelBase(UiDispatcher uiDispatcher, OperationRunner runner)
        {
            _ui = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
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

        protected Task UiAsync(Action action)
            => _ui.InvokeAsync(action);

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

            await _runner.RunAsync(
                state,
                (ctx, ct) => backgroundWork(ct, ctx),
                options,
                token).ConfigureAwait(false);

            await UiAsync(() => LastError = state.Error).ConfigureAwait(false);
        }
    }
}
