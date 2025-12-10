using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Jobs
{
    /// <summary>
    /// BackgroundJobQueue에 등록할 단일 백그라운드 작업을 나타냅니다.
    /// </summary>
    public sealed class BackgroundJob
    {
        /// <summary>
        /// 작업의 논리적 이름입니다.
        /// 중복 실행 방지(SkipIfExists) 시 키로 사용됩니다.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 실제로 실행될 비동기 작업입니다.
        /// </summary>
        public Func<CancellationToken, Task> Work { get; }

        /// <summary>
        /// 동일한 Name을 가진 Job이 큐에 있거나 실행 중일 때
        /// 새 Job을 스킵할지 여부입니다.
        /// </summary>
        public bool SkipIfExists { get; }

        /// <summary>
        /// 새로운 BackgroundJob을 생성합니다.
        /// </summary>
        /// <param name="name">작업 이름 (중복 방지 키).</param>
        /// <param name="work">실제 실행할 비동기 작업.</param>
        /// <param name="skipIfExists">
        /// true이면 동일 Name Job이 이미 active일 경우 새 Job을 스킵합니다.
        /// </param>
        /// <exception cref="ArgumentNullException">name 또는 work가 null인 경우.</exception>
        public BackgroundJob(string name, Func<CancellationToken, Task> work, bool skipIfExists = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Work = work ?? throw new ArgumentNullException(nameof(work));
            SkipIfExists = skipIfExists;
        }
    }
}
