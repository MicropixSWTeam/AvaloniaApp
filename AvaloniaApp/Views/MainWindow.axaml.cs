using Avalonia.Controls;
using AvaloniaApp.ViewModels;

namespace AvaloniaApp.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}