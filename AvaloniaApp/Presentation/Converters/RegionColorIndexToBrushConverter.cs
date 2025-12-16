using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AvaloniaApp.Core.Models;
using System;
using System.Globalization;


namespace AvaloniaApp.Presentation.Converters
{
    /// <summary>
    /// SelectionRegion.Index 또는 int → SolidColorBrush 변환.
    /// CameraView/ChartView 양쪽에서 ROI 색상을 통일하는 데 사용.
    /// </summary>
    public sealed class RegionColorIndexToBrushConverter : IValueConverter
    {
        public static RegionColorIndexToBrushConverter Instance { get; } = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int idx)
            {
                var color = RegionColorPalette.GetAvaloniaColor(idx);
                return new SolidColorBrush(color);
            }

            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // 역변환은 사용 안 함
            return BindingOperations.DoNothing;
        }
    }
}