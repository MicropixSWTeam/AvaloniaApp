using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OnnxUpscaler.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OnnxUpscaler.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageService _imageService;
    private readonly OnnxService _onnxService;
    private Mat? _currentMat;

    [ObservableProperty]
    private Bitmap? _displayImage;

    [ObservableProperty]
    private string? _currentImagePath;

    [ObservableProperty]
    private string? _modelPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpscaleCommand))]
    private bool _isModelLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadModelCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpscaleCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private string? _statusText;

    // File picker callback set by view
    public Func<Task<string?>>? ModelFilePickerCallback { get; set; }

    public MainViewModel(ImageService imageService, OnnxService onnxService)
    {
        _imageService = imageService;
        _onnxService = onnxService;
    }

    public void LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            // Dispose previous Mat if exists
            _currentMat?.Dispose();

            // Load image using OpenCV and keep the Mat
            _currentMat = Cv2.ImRead(path, ImreadModes.Color);
            if (_currentMat.Empty())
            {
                _currentMat?.Dispose();
                _currentMat = null;
                return;
            }

            var bitmap = _imageService.MatToBitmap(_currentMat);
            if (bitmap != null)
            {
                DisplayImage?.Dispose();
                DisplayImage = bitmap;
                CurrentImagePath = path;
                StatusText = $"Loaded: {Path.GetFileName(path)} ({_currentMat.Width}x{_currentMat.Height})";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading image: {ex.Message}";
        }
    }

    private bool CanLoadModel() => !IsProcessing;

    [RelayCommand(CanExecute = nameof(CanLoadModel))]
    private async Task LoadModelAsync()
    {
        if (ModelFilePickerCallback == null)
            return;

        var path = await ModelFilePickerCallback();
        if (string.IsNullOrEmpty(path))
            return;

        IsProcessing = true;
        StatusText = "Loading model...";

        try
        {
            await Task.Run(() => _onnxService.LoadModel(path));
            ModelPath = Path.GetFileName(path);
            IsModelLoaded = true;
            StatusText = $"Model loaded: {ModelPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading model: {ex.Message}";
            IsModelLoaded = false;
            ModelPath = null;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanUpscale() => IsModelLoaded && !IsProcessing && _currentMat != null;

    [RelayCommand(CanExecute = nameof(CanUpscale))]
    private async Task UpscaleAsync()
    {
        if (_currentMat == null || !_onnxService.IsModelLoaded)
            return;

        IsProcessing = true;
        StatusText = "Upscaling...";

        try
        {
            var inputMat = _currentMat;
            Mat? outputMat = null;

            await Task.Run(() =>
            {
                outputMat = _onnxService.Upscale(inputMat);
            });

            if (outputMat != null)
            {
                // Dispose old Mat and update with new one
                _currentMat?.Dispose();
                _currentMat = outputMat;

                // Update display
                var bitmap = _imageService.MatToBitmap(_currentMat);
                if (bitmap != null)
                {
                    DisplayImage?.Dispose();
                    DisplayImage = bitmap;
                    StatusText = $"Upscaled: {_currentMat.Width}x{_currentMat.Height}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error upscaling: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
