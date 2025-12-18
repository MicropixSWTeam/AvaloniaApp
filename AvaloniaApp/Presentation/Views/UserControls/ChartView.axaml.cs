using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class ChartView : UserControl
    {
        public ChartView()
        {
            InitializeComponent();
            DemoChart.Series = ScreenshotChartDemo.CreateSeriesLikeScreenshot();
        }
    }
}