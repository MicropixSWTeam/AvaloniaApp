using AvaloniaApp.Core.Enums; // DialogType이 정의된 곳 (없으면 아래 참고)
using AvaloniaApp.Core.Interfaces; // IDialogRequestClose, IPopup 위치
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Infrastructure.Service;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using Tmds.DBus.Protocol;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class DialogViewModel : ViewModelBase, IDialogRequestClose
    {
        public event EventHandler<DialogResultEventArgs>? CloseRequested;

        [ObservableProperty]
        private string _title = "";

        [ObservableProperty]
        private string _message = "";

        [ObservableProperty]
        private int _width = 400;

        [ObservableProperty]
        private int _height = 200;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HeaderColor))]   // Type이 바뀌면 색상도 다시 계산해라
        [NotifyPropertyChangedFor(nameof(IsCancelVisible))] // Type이 바뀌면 버튼 보임여부도 다시 계산해라
        private DialogType _type;

        // 4. 생성자: 의존성 주입(Service)만 받습니다. (Action, String 등 데이터 제거!)
        public DialogViewModel(AppService service) : base(service)
        {
        }

        // 5. 초기화 메서드 (PopupService가 생성 직후에 호출함)
        public void Init(DialogType type, string title, string message)
        {
            Type = type;
            Title = title;
            Message = message;
        }

        // 6. 파생 속성 (Type에 따라 UI 스타일 변경)
        public bool IsCancelVisible => Type == DialogType.Confirm;

        public string HeaderColor => Type switch
        {
            DialogType.Error => "#D32F2F",   // 빨강
            DialogType.Complete => "#388E3C",// 초록
            DialogType.Confirm => "#1976D2", // 파랑
            _ => "Gray"
        };

        // 7. 명령 (Commands)
        [RelayCommand]
        private void Confirm()
        {
            // 확인 버튼: true 반환
            CloseRequested?.Invoke(this, new DialogResultEventArgs(true));
        }

        [RelayCommand]
        private void Cancel()
        {
            // 취소 버튼: false 반환
            CloseRequested?.Invoke(this, new DialogResultEventArgs(false));
        }
    }
}