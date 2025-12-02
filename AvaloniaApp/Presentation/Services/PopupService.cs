using Avalonia.Controls;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Presentation.Views.UserControls; // 실제 뷰들이 있는 네임스페이스
using AvaloniaApp.Presentation.Views.Windows;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.Services
{
    public class PopupService
    {
        private readonly Func<PopupHostWindow> _hostFactory;
        private readonly MainWindow _owner;
        private readonly Dictionary<object, PopupHostWindow> _popupDic = new();


        public PopupService(Func<PopupHostWindow> hostFactory, MainWindow owner)
        {
            _hostFactory = hostFactory;
            _owner = owner;
        }

        public Task ShowModelessAsync(object vm)
        {
            if (vm is null)
                throw new ArgumentNullException(nameof(vm));

            // 동일 VM instance에 대해 하나만 띄우고 싶다면 key를 viewModel로 사용
            var key = vm;

            if (_popupDic.TryGetValue((vm.GetType(), vm), out var existing))
            {
                if (existing.IsVisible)
                {
                    existing.Activate();
                    existing.Focus();
                    return Task.CompletedTask;
                }

                _popupDic.Remove((vm.GetType(), vm));
            }

            var host = _hostFactory();

            if(vm is IPopup data)
            {

            }

            host.DataContext = vm;    // 여기만 세팅하면 DataTemplate이 알아서 View를 생성

            _popupDic[(vm.GetType(), vm)] = host;

            host.Closed += (_, __) =>
            {
                _popupDic.Remove((vm.GetType(), vm));
            };

            host.Show(_owner);
            return Task.CompletedTask;
        }

        public Task ShowModalAsync(ViewType viewType, object vm)
        {
            var host = _hostFactory();
            host.Title = viewType.ToString();
            host.Content = CreateView(viewType, vm);

            host.ShowDialog(_owner);
            return Task.CompletedTask;
        }

        /// <summary>
        /// ViewType + ViewModel → 실제 UserControl(View) 생성하는 곳
        /// 이게 "view 할당"을 하는 핵심 위치다.
        /// </summary>
        private Control CreateView(ViewType viewType, object vm)
        {
            return viewType switch
            {
                ViewType.Camera => new CameraView { DataContext = vm },
                ViewType.CameraConnect => new CameraConnectView { DataContext = vm },
                ViewType.SpectrumChart => new ChartView { DataContext = vm },

                _ => throw new ArgumentOutOfRangeException(nameof(viewType), viewType, "지원하지 않는 ViewType")
            };
        }
    }
}
