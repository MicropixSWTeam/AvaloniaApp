using AvaloniaApp.Presentation.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure.Service
{
    public class LayoutService
    {
        // 나중에 이 녀석이 '창 떼어내기/붙이기'를 관리할 겁니다.
        // 지금은 컴파일 에러 방지용 껍데기만 둡니다.

        public void Detach(ViewModelBase viewModel)
        {
            // TODO: PopupService를 이용해 이 viewModel을 새 창에 띄우는 로직 구현 예정
        }
        public void Attach(ViewModelBase viewModel)
        {
            // TODO: MainView의 원래 자리에 다시 붙이는 로직 구현 예정
        }
    }
}
