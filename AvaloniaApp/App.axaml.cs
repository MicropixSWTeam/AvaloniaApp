using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.ViewModels;
using AvaloniaApp.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace AvaloniaApp
{
    public static class ServiceCollectionExtensions
    {
        public static void AddAppServices(this IServiceCollection services)
        {
            // 서비스/도메인
            services.AddSingleton<DialogService>();

            services.AddSingleton<IUiDispatcher, UiDispatcher>();
            services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();

            services.AddSingleton<CameraPipeline>();

            // 백그라운드 워커
            services.AddHostedService<BackgroundJobWorker>();

            // ViewModel
            services.AddSingleton<MainWindowViewModel>();

            // View
            services.AddTransient<MainWindow>();
            services.AddTransient<PopupHostWindow>();
        }
    }

    public partial class App : Application
    {
        private IHost? _host;

        public IServiceProvider Services =>
            _host?.Services ?? throw new InvalidOperationException("Host not initialized");

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Generic Host 생성
            var builder = Host.CreateApplicationBuilder();
            // 로깅, 설정 등
            builder.Logging.AddDebug();

            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            // 설정 바인딩
            builder.Services.Configure<CameraOptions>(builder.Configuration.GetSection("Camera"));
            // DI 등록
            builder.Services.AddAppServices(); // 직접 만든 확장 메서드

            _host = builder.Build();

            // BackgroundService 시작
            _host.StartAsync().GetAwaiter().GetResult();

            // 필요한 서비스 꺼내기 (VM, Window 등)
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var vm = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = vm;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = mainWindow;

                // 여기서 종료 이벤트에 연결해서 Host 정리
                desktop.Exit += (_, __) =>
                {
                    _host!.StopAsync().GetAwaiter().GetResult();
                    _host?.Dispose();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}