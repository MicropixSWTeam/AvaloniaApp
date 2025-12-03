using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.ViewModels.UserControls;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class CameraSettingView : UserControl
    {
        public CameraSettingView()
        {
            InitializeComponent();
        }
        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is CameraSettingViewModel vm)
            {
                await vm.LoadAsync();
            }
        }
    }
}