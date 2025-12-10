using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Operations;
using Microsoft.Extensions.Logging;

namespace AvaloniaApp.Presentation.ViewModels.Base
{
    /// <summary>
    /// 공통 OperationHostBase를 상속하는 ViewModel 베이스 클래스입니다.
    /// RunOperationAsync / OperationState 기능을 그대로 사용할 수 있습니다.
    /// </summary>
    public abstract partial class ViewModelBase : OperationViewModelBase
    {
        /// <summary>
        /// DI 없이 사용하는 기본 생성자입니다.
        /// 테스트/디자인 타임 등의 특별한 경우에만 사용합니다.
        /// </summary>
        protected ViewModelBase()
        {
        }

        /// <summary>
        /// DI를 통해 필요한 서비스를 주입받는 생성자입니다.
        /// </summary>
        /// <param name="dialogService">에러 표시 등에 사용할 DialogService.</param>
        /// <param name="uiDispatcher">UI 스레드 호출용 UiDispatcher.</param>
        /// <param name="backgroundJobQueue">백그라운드 Job 큐.</param>
        /// <param name="logger">로그 출력용 ILogger.</param>
        protected ViewModelBase(
            UiDispatcher uiDispatcher,
            BackgroundJobQueue backgroundJobQueue
        )
            : base( uiDispatcher, backgroundJobQueue)
        {
        }
    }
}
