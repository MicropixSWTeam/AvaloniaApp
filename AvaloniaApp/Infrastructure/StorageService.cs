using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Bitmap? fullImage,
            Bitmap? colorImage,
            Dictionary<string, Bitmap> cropImages,
            string csvContent,
            CancellationToken ct = default)
        {
            var provider = GetStorageProvider();

            // 1. 저장할 상위 폴더 선택
            var options = new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "실험 데이터를 저장할 위치(상위 폴더)를 선택하세요"
            };

            var parents = await provider.OpenFolderPickerAsync(options);
            if (parents is null || parents.Count == 0) return;

            var parentFolder = parents[0];

            // 2. 타겟 폴더 생성 (폴더명 정제)
            var safeFolderName = SanitizeFileName(folderName);
            var targetFolder = await parentFolder.CreateFolderAsync(safeFolderName);

            if (targetFolder is null) return;

            // 3. 전체 이미지 저장 (Null 체크)
            if (fullImage != null)
            {
                await SaveBitmapToFolderAsync(targetFolder, "FullImage.png", fullImage);
            }

            // 4. 컬러 이미지 저장 (Null 체크 - 여기서 터졌던 것임)
            if (colorImage != null)
            {
                await SaveBitmapToFolderAsync(targetFolder, "ColorImage.png", colorImage);
            }

            // 5. Crop 이미지들 저장
            foreach (var kvp in cropImages)
            {
                if (kvp.Value is null) continue; // 비트맵 없으면 스킵

                var safeFileName = $"{SanitizeFileName(kvp.Key)}.png";
                await SaveBitmapToFolderAsync(targetFolder, safeFileName, kvp.Value);
            }

            // 6. CSV 데이터 저장
            await SaveTextToFolderAsync(targetFolder, "IntensityData.csv", csvContent);
        }

        /// <summary>
        /// 파일명에 사용할 수 없는 문자를 언더스코어(_)로 변경
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private async Task SaveBitmapToFolderAsync(IStorageFolder folder, string fileName, Bitmap? bitmap)
        {
            // [핵심 수정] 비트맵이 Null이면 저장 시도조차 하지 않고 리턴
            if (bitmap is null) return;

            try
            {
                var file = await folder.CreateFileAsync(fileName);
                if (file is null) return;

                // await using 사용
                await using var stream = await file.OpenWriteAsync();

                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }

                // 여기서 bitmap이 null이면 터지는데, 위에서 막았으므로 안전함
                bitmap.Save(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save Error ({fileName}): {ex.Message}");
            }
        }

        private async Task SaveTextToFolderAsync(IStorageFolder folder, string fileName, string content)
        {
            try
            {
                var file = await folder.CreateFileAsync(fileName);
                if (file is null) return;

                await using var stream = await file.OpenWriteAsync();

                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }

                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save CSV Error: {ex.Message}");
            }
        }
    }
}