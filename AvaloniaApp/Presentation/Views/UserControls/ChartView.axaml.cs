using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaApp.Presentation.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class ChartView : UserControl
    {
        private ChartViewModel? ViewModel => DataContext as ChartViewModel;
        public ChartView()
        {
            InitializeComponent();
            if(ViewModel != null)
                Chart.Series = ViewModel.SeriesCollection;
        }
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is ChartViewModel vm)
            {
                Chart.Series = vm.SeriesCollection;
            }
        }
    }
}