using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OnnxUpscaler.ViewModels;
using System.Linq;
using System.Threading.Tasks;

namespace OnnxUpscaler;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var dropZone = this.FindControl<Border>("DropZone");
        if (dropZone != null)
        {
            dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
            dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        // Wire up file picker callback when DataContext is set
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ModelFilePickerCallback = PickModelFileAsync;
            }
        };
    }

    private async Task<string?> PickModelFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select ONNX Model",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ONNX Model") { Patterns = new[] { "*.onnx" } },
                FilePickerFileTypes.All
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        e.DragEffects = e.Data.GetFiles() != null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
#pragma warning restore CS0618
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files?.FirstOrDefault() is IStorageFile file)
        {
            var path = file.TryGetLocalPath();
            if (path != null && DataContext is MainViewModel viewModel)
            {
                viewModel.LoadImage(path);
            }
        }
    }
}
