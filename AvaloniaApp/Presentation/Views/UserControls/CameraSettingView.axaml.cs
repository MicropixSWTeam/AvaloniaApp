using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class CameraSettingView : UserControl
    {
        public CameraSettingView()
        {
            InitializeComponent();
        }
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _ = TryLoadAsync();
        }


        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _ = TryLoadAsync();
        }


        private async System.Threading.Tasks.Task TryLoadAsync()
        {
            if (DataContext is CameraSettingViewModel vm)
                await vm.LoadAsync(); // 내부에서 IsStreaming 체크하도록 수정 (아래)
        }
    }
}