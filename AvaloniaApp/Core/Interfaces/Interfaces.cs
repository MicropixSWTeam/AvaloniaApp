using AvaloniaApp.Core.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Interfaces
{
    public interface IPopup
    {
        public string Title { get; set; }   
        public int Width { get; set; }
        public int Height { get; set; }
    }
    // 팝업이 닫힐 때 결과값을 전달하는 이벤트 인자
    public class DialogResultEventArgs : EventArgs
    {
        public object? Result { get; }
        public DialogResultEventArgs(object? result) => Result = result;
    }

    // 닫기 요청 기능 (결과 반환 포함)
    public interface IDialogRequestClose
    {
        event EventHandler<DialogResultEventArgs>? CloseRequested;
    }
}
