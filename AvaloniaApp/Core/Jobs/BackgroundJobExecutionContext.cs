using System.Threading;

namespace AvaloniaApp.Core.Jobs
{
    internal static class BackgroundJobExecutionContext
    {
        public static readonly AsyncLocal<BackgroundJobQueue?> CurrentQueue = new();
        public static readonly AsyncLocal<int> Depth = new();

        public static bool IsRunningInside(BackgroundJobQueue queue)
            => Depth.Value > 0 && ReferenceEquals(CurrentQueue.Value, queue);
    }
}