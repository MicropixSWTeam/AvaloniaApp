using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading;

namespace AvaloniaApp.Presentation.Operations
{
    public partial class OperationState : ObservableObject
    {
        [ObservableProperty] private bool isRunning;
        [ObservableProperty] private double progress;
        [ObservableProperty] private bool isIndeterminate;
        [ObservableProperty] private string? message;
        [ObservableProperty] private string? error;

        private CancellationTokenSource? _cts;

        public bool CanCancel => _cts is { IsCancellationRequested: false } && IsRunning;

        internal void SetCancellation(CancellationTokenSource? cts)
        {
            _cts = cts;
            OnPropertyChanged(nameof(CanCancel));
        }

        public void Cancel()
        {
            if (_cts is { IsCancellationRequested: false })
            {
                _cts.Cancel();
                OnPropertyChanged(nameof(CanCancel));
            }
        }

        public void Reset(string? startMessage = null)
        {
            Error = null;
            Message = startMessage;
            Progress = 0;
            IsIndeterminate = true;
        }

        partial void OnIsRunningChanged(bool value)
            => OnPropertyChanged(nameof(CanCancel));
    }
}