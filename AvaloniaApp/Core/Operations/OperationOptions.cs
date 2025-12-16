using AvaloniaApp.Presentation.Operations;
using System;

namespace AvaloniaApp.Core.Operations
{
    public sealed class OperationOptions
    {
        public string? JobName { get; set; }
        public string? StartMessage { get; set; }
        public TimeSpan? Timeout { get; set; }

        public string CanceledMessage { get; set; } = "작업이 취소되었습니다.";
        public string TimeoutMessage { get; set; } = "시간이 초과되었습니다.";

        public bool Rethrow { get; set; } = false;

        public Action<OperationState>? OnStart { get; set; }
        public Action<OperationState>? OnSuccess { get; set; }
        public Action<OperationState, Exception>? OnError { get; set; }
    }
}