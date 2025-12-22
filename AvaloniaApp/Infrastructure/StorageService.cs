using Avalonia;
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
            ct.ThrowIfCancellationRequested();

            var provider = GetStorageProvider();

            // 1. 저장할 상위 위치 선택 (Folder Picker)
            var options = new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "데이터를 저장할 위치(상위 폴더)를 선택하세요"
            };

            var parents = await provider.OpenFolderPickerAsync(options);
            if (parents is null || parents.Count == 0) return;

            ct.ThrowIfCancellationRequested();

            var parentFolder = parents[0];

            // 2. 상위 폴더 아래에 고유 이름 폴더 생성 (동일 이름 존재 시 _001, _002...)
            var baseName = SanitizeFileName(folderName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}";

            var (targetFolder, usedName) = await CreateUniqueFolderAsync(parentFolder, baseName, ct);
            if (targetFolder is null) return;

            // 3. 폴더 안에 파일 저장
            // 3-1. Full Image
            await SaveBitmapToFolderAsync(targetFolder, "FullImage.png", fullImage, ct);

            // 3-2. Color Image (Optional)
            if (colorImage != null)
                await SaveBitmapToFolderAsync(targetFolder, "ColorImage.png", colorImage, ct);

            // 3-3. Crop Images
            foreach (var kvp in cropImages)
            {
                // kvp.Key 예: "450nm", "530nm" 등
                await SaveBitmapToFolderAsync(targetFolder, $"{kvp.Key}.png", kvp.Value, ct);
            }

            // 3-4. CSV Data
            await SaveTextToFolderAsync(targetFolder, "IntensityData.csv", csvContent, ct);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim().TrimEnd('.');
        }

        private static async Task<(IStorageFolder? folder, string usedName)> CreateUniqueFolderAsync(
            IStorageFolder parent,
            string baseName,
            CancellationToken ct)
        {
            // baseName -> baseName_001 -> baseName_002 순으로 시도
            for (int i = 0; i < 1000; i++)
            {
                ct.ThrowIfCancellationRequested();

                var name = i == 0 ? baseName : $"{baseName}_{i:000}";

                try
                {
                    // CreateFolderAsync는 폴더가 이미 있으면 null을 반환하거나 예외를 던질 수 있음(구현 의존)
                    // 여기서는 충돌 시 예외/null 처리를 통해 다음 번호 시도
                    var folder = await parent.CreateFolderAsync(name);
                    if (folder != null)
                        return (folder, name);
                }
                catch
                {
                    // 이름 충돌 또는 권한 문제: 다음 suffix 시도
                }
            }
            return (null, baseName);
        }

        private static async Task SaveBitmapToFolderAsync(IStorageFolder folder, string fileName, Bitmap bitmap, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var file = await folder.CreateFileAsync(fileName);
                if (file is null) return;

                using var stream = await file.OpenWriteAsync();
                bitmap.Save(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bitmap Save Error ({fileName}): {ex}");
            }
        }

        private static async Task SaveTextToFolderAsync(IStorageFolder folder, string fileName, string content, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var file = await folder.CreateFileAsync(fileName);
                if (file is null) return;

                using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CSV Save Error ({fileName}): {ex}");
            }
        }
    }
}