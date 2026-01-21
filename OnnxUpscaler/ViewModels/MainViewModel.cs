using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OnnxUpscaler.Services;

namespace OnnxUpscaler.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageService _imageService;

    [ObservableProperty]
    private Bitmap? _displayImage;

    [ObservableProperty]
    private string? _currentImagePath;

    public MainViewModel(ImageService imageService)
    {
        _imageService = imageService;
    }

    public void LoadImage(string path)
    {
        var bitmap = _imageService.LoadImage(path);
        if (bitmap != null)
        {
            // Dispose previous image if exists
            DisplayImage?.Dispose();
            DisplayImage = bitmap;
            CurrentImagePath = path;
        }
    }
}
