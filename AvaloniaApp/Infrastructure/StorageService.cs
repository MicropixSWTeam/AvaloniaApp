using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public class StorageService
    {
        // 현재 MainWindow 에서 IStorageProvider 가져오기
        private IStorageProvider GetStorageProvider()
        {
            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is { } window)
            {
                return window.StorageProvider;
            }

            throw new InvalidOperationException("StorageProvider를 가져올 수 없습니다.");
        }

        /// <summary>
        /// SaveFilePicker 를 띄워서 단일 Bitmap 저장 (PNG).
        /// </summary>
        public async Task SaveBitmapWithDialogAsync(
            Bitmap bitmap,
            string suggestedFileName,
            CancellationToken ct = default)
        {
            if (bitmap is null)
                throw new ArgumentNullException(nameof(bitmap));

            var storage = GetStorageProvider();

            var options = new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedFileName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG image")
                    {
                        Patterns = new[] { "*.png" }
                    }
                }
            };

            // Avalonia Storage API 자체는 CancellationToken 을 받지 않으므로
            // ct 는 호출 측에서만 관리 (여기서는 단순하게 무시)
            var file = await storage.SaveFilePickerAsync(options);
            if (file is null)
                return; // 사용자 취소

            await using var stream = await file.OpenWriteAsync();
            // Bitmap.Save(Stream, quality) – quality 는 null 로 기본값 사용
            bitmap.Save(stream, null);
        }

        /// <summary>
        /// 여러 Bitmap 을 한 폴더에 일괄 저장.
        /// 첫 번째 파일 이름을 사용자가 고르면,
        /// 같은 폴더에 name_01.png, name_02.png... 이런 식으로 저장.
        /// </summary>
        public async Task SaveBitmapsWithDialogAsync(
            IReadOnlyList<Bitmap> bitmaps,
            string baseFileName,
            CancellationToken ct = default)
        {
            if (bitmaps is null || bitmaps.Count == 0)
                return;

            var firstBitmap = bitmaps[0];
            if (firstBitmap is null)
                return;

            var storage = GetStorageProvider();

            var options = new FilePickerSaveOptions
            {
                SuggestedFileName = baseFileName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG image")
                    {
                        Patterns = new[] { "*.png" }
                    }
                }
            };

            var firstFile = await storage.SaveFilePickerAsync(options);
            if (firstFile is null)
                return;

            // 첫 파일 저장
            await using (var s = await firstFile.OpenWriteAsync())
            {
                firstBitmap.Save(s, null);
            }

            // 로컬 경로를 얻을 수 있는 플랫폼이면 나머지는 직접 파일 생성해서 저장
            var firstPath = firstFile.TryGetLocalPath();
            if (string.IsNullOrEmpty(firstPath))
            {
                // 모바일/웹 같은 플랫폼에서는 로컬 경로 개념이 없어서
                // 일단 첫 번째 파일만 저장하는 정도로 둔다.
                return;
            }

            var folder = Path.GetDirectoryName(firstPath)!;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(firstPath);
            var ext = Path.GetExtension(firstPath);
            if (string.IsNullOrEmpty(ext))
                ext = ".png";

            for (int i = 1; i < bitmaps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var bmp = bitmaps[i];
                if (bmp is null)
                    continue;

                var fileName = $"{nameWithoutExt}_{i:D2}{ext}";
                var path = Path.Combine(folder, fileName);

                await using var fs = File.Open(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read);

                bmp.Save(fs, null);
            }
        }
    }
}
