using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public class StorageService
    {
        private IStorageProvider GetStorageProvider()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is { } window)
            {
                return window.StorageProvider;
            }
            throw new InvalidOperationException("StorageProvider Unavailable");
        }

        public async Task SaveExperimentResultAsync(
            string folderName,
            Bitmap fullImage,
            Bitmap? colorImage,
            Dictionary<string, Bitmap> cropImages,
            string csvContent,
            CancellationToken ct = default)
        {
            var provider = GetStorageProvider();
            var options = new FolderPickerOpenOptions { AllowMultiple = false, Title = "실험 데이터를 저장할 상위 폴더를 선택하세요" };
            var parents = await provider.OpenFolderPickerAsync(options);
            if (parents is null || parents.Count == 0) return;

            var parentFolder = parents[0];
            // 사용자가 입력한 이름으로 폴더 생성
            var targetFolder = await parentFolder.CreateFolderAsync(folderName);
            if (targetFolder is null) return;

            await SaveBitmapToFolderAsync(targetFolder, "FullImage.png", fullImage);
            if (colorImage != null) await SaveBitmapToFolderAsync(targetFolder, "ColorImage.png", colorImage);

            foreach (var kvp in cropImages)
            {
                await SaveBitmapToFolderAsync(targetFolder, $"{kvp.Key}.png", kvp.Value);
            }

            await SaveTextToFolderAsync(targetFolder, "IntensityData.csv", csvContent);
        }

        private async Task SaveBitmapToFolderAsync(IStorageFolder folder, string fileName, Bitmap bitmap)
        {
            try
            {
                var file = await folder.CreateFileAsync(fileName);
                if (file is null) return;
                using var stream = await file.OpenWriteAsync();
                bitmap.Save(stream);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Save Error ({fileName}): {ex}"); }
        }

        private async Task SaveTextToFolderAsync(IStorageFolder folder, string fileName, string content)
        {
            try
            {
                var file = await folder.CreateFileAsync(fileName);
                if (file is null) return;
                using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(content);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Save CSV Error: {ex}"); }
        }
    }
}