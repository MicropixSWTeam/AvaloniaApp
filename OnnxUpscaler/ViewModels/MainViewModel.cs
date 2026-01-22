using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OnnxUpscaler.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace OnnxUpscaler.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageService _imageService;
    private readonly OnnxService _onnxService;
    private Mat? _currentMat;
    private Mat? _originalMat;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndViewCommand))]
    private bool _hasUpscaledImage;

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

            // Dispose previous Mats if exists
            _currentMat?.Dispose();
            _originalMat?.Dispose();

            // Load as grayscale for ESPCN model
            _currentMat = Cv2.ImRead(path, ImreadModes.Grayscale);
            _originalMat = _currentMat.Clone();
            if (_currentMat.Empty())
            {
                _currentMat?.Dispose();
                _currentMat = null;
                return;
            }

            // Notify UpscaleCommand that CanExecute may have changed
            UpscaleCommand.NotifyCanExecuteChanged();
            HasUpscaledImage = false;

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
            StatusText = $"Model loaded: {ModelPath} [{_onnxService.ExecutionProvider}]";
        }
        catch (Exception ex)
        {
            // Print full error to console for debugging
            Console.Error.WriteLine($"[ERROR] Failed to load model: {ex}");
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
        const int iterations = 5;

        try
        {
            var inputMat = _currentMat;
            Mat? outputMat = null;
            var stopwatch = Stopwatch.StartNew();

            await Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    outputMat?.Dispose();
                    outputMat = _onnxService.Upscale(inputMat);
                }
            });

            stopwatch.Stop();
            var totalMs = stopwatch.ElapsedMilliseconds;
            var avgMs = totalMs / iterations;

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
                    StatusText = $"Upscaled: {_currentMat.Width}x{_currentMat.Height} | {iterations}x in {totalMs}ms (avg: {avgMs}ms)";
                    HasUpscaledImage = true;
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

    private bool CanSaveAndView() => HasUpscaledImage && _currentMat != null;

    [RelayCommand(CanExecute = nameof(CanSaveAndView))]
    private void SaveAndView()
    {
        if (_currentMat == null || _originalMat == null)
            return;

        try
        {
            // Get Output folder in application directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var outputDir = Path.Combine(appDir, "Output");
            Directory.CreateDirectory(outputDir);

            // Generate timestamp for filenames
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var originalPath = Path.Combine(outputDir, $"original_{timestamp}.png");
            var upscaledPath = Path.Combine(outputDir, $"upscaled_{timestamp}.png");

            // Save both images
            Cv2.ImWrite(originalPath, _originalMat);
            Cv2.ImWrite(upscaledPath, _currentMat);

            // Open the output folder
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDir,
                UseShellExecute = true
            });

            StatusText = $"Saved to: {outputDir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving image: {ex.Message}";
        }
    }
}
