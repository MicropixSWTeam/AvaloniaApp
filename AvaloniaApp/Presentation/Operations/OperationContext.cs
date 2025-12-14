using AvaloniaApp.Infrastructure;
using System;
using System.Threading;

namespace AvaloniaApp.Presentation.Operations
{
    public sealed class OperationContext
    {
        private readonly OperationState _state;
        private readonly UiDispatcher? _ui;
        private readonly CancellationToken _lifetime;

        private readonly object _gate = new();
        private bool _pendingIndeterminate;
        private double _pendingProgress;
        private string? _pendingMessage;

        private int _scheduled;

        internal OperationContext(OperationState state, UiDispatcher? ui, CancellationToken lifetime)
        {
            _state = state;
            _ui = ui;
            _lifetime = lifetime;
        }

        public void ReportProgress(double value, string? message = null)
        {
            if (_lifetime.IsCancellationRequested) return;

            lock (_gate)
            {
                _pendingIndeterminate = false;
                _pendingProgress = value;
                if (message is not null)
                    _pendingMessage = message;
            }

            ScheduleApply();
        }

        public void ReportIndeterminate(string message)
        {
            if (_lifetime.IsCancellationRequested) return;

            lock (_gate)
            {
                _pendingIndeterminate = true;
                _pendingMessage = message;
            }

            ScheduleApply();
        }

        public void ReportMessage(string message)
        {
            if (_lifetime.IsCancellationRequested) return;

            lock (_gate)
            {
                _pendingMessage = message;
            }

            ScheduleApply();
        }

        private void ScheduleApply()
        {
            if (_lifetime.IsCancellationRequested) return;

            if (_ui is null)
            {
                Apply();
                return;
            }

            if (Interlocked.Exchange(ref _scheduled, 1) == 1)
                return;

            _ui.Post(() =>
            {
                Interlocked.Exchange(ref _scheduled, 0);
                Apply();
            });
        }

        private void Apply()
        {
            if (_lifetime.IsCancellationRequested) return;

            bool ind;
            double prog;
            string? msg;

            lock (_gate)
            {
                ind = _pendingIndeterminate;
                prog = _pendingProgress;
                msg = _pendingMessage;
            }

            _state.IsIndeterminate = ind;
            if (!ind)
                _state.Progress = prog;

            if (msg is not null)
                _state.Message = msg;
        }
    }
}