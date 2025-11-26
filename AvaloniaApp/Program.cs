using Avalonia;
using AvaloniaApp.Services;
using AvaloniaApp.ViewModels;
using AvaloniaApp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace AvaloniaApp
{
    public static class Program
    {
        public static IHost? Host { get; private set; }
        public static IServiceProvider Services => Host!.Services;
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        public static IHostBuilder CreateHostBuilder(string[] args) =>
        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 여기서 DI 등록
                ConfigureServices(services);
            });

        private static void ConfigureServices(IServiceCollection services)
        {
            // Services
            services.AddSingleton<CameraService>();
            // ViewModels
            services.AddSingleton<MainWindowViewModel>();

            // Views

            // Windows
            services.AddTransient<MainWindow>();
        }
        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
