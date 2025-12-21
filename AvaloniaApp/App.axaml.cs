using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Operations;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaApp.Presentation.ViewModels.Windows;
using AvaloniaApp.Presentation.Views.UserControls;
using AvaloniaApp.Presentation.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AvaloniaApp
{
    public static class ServiceCollectionExtensions
    {
        public static void AddAppServices(this IServiceCollection services)
        {
            services.AddSingleton<WorkspaceService>();

            services.AddSingleton(new BackgroundJobQueue(capacity: 128));
            services.AddHostedService<BackgroundJobWorker>();
            
            services.AddSingleton<OperationRunner>();

            services.AddSingleton<AppService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<UiService>();
            services.AddSingleton<VimbaCameraService>();
            services.AddSingleton<ImageProcessService>();
            services.AddSingleton<PopupService>();
            services.AddSingleton<StorageService>();
            services.AddSingleton<ImageHelperService>();

            services.AddSingleton<CameraViewModel>();
            services.AddSingleton<CameraConnectViewModel>();
            services.AddSingleton<CameraSettingViewModel>();
            services.AddSingleton<ChartViewModel>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<PopupHostWindowViewModel>();    

            // View
            services.AddSingleton<CameraView>();
            services.AddSingleton<CameraConnectView>();
            services.AddSingleton<CameraSettingView>();
            services.AddSingleton<ChartView>();
            services.AddSingleton<MainWindow>();
            services.AddTransient<PopupHostWindow>();
            services.AddTransient<Func<PopupHostWindow>>(sp =>
            {
                return () => sp.GetRequiredService<PopupHostWindow>();
            });
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
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.AddDebug();

            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            builder.Services.AddAppServices(); 

            _host = builder.Build();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var vm = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = vm;


            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = mainWindow;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _host.StartAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {

                    }
                });

                desktop.Exit += (_, __) =>
                {
                    try
                    {
                        _host!.StopAsync().GetAwaiter().GetResult();
                        if (_host is IAsyncDisposable ad)
                            ad.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        else
                            _host.Dispose();
                    }
                    catch { }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}