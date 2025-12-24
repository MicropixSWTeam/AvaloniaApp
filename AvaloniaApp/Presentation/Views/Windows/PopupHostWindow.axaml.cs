using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Presentation.Views.Windows
{
    public partial class PopupHostWindow : Window
    {
        public PopupHostWindow()
        {
            InitializeComponent();

            this.Opened += (s, e) =>
            {
                this.SizeToContent = SizeToContent.Manual;
                this.SizeToContent = SizeToContent.WidthAndHeight;
            };
        }
    }
}