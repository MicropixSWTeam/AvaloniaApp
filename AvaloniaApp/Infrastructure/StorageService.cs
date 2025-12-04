using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
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

            throw new InvalidOperationException(
                "StorageProvider를 가져올 수 없습니다. MainWindow가 아직 준비되지 않았을 수 있습니다.");
        }

        /// <summary>
        /// SaveFilePicker 를 통해 단일 Bitmap 저장 (PNG).
        /// suggestedFileName 은 "frame.png" 같은 이름.
        /// </summary>
        public async Task SaveBitmapWithDialogAsync(
            Bitmap bitmap,
            string suggestedFileName,
            CancellationToken ct = default)
        {
            if (bitmap is null)
                throw new ArgumentNullException(nameof(bitmap));

            var provider = GetStorageProvider();

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

            // Avalonia Storage API 는 CancellationToken 을 직접 받지 않으므로
            // ct 는 호출 측에서만 관리 (여기서는 단순히 무시)
            var file = await provider.SaveFilePickerAsync(options);
            if (file is null)
                return; // 사용자 취소

            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream); // 확장자에 맞춰 PNG로 저장
        }

        /// <summary>
        /// stitched + tiles 묶음을 한 번에 저장:
        /// 1) 부모 폴더 선택
        /// 2) 그 안에 sessionName(없으면 timestamp) 이름의 서브 폴더 생성
        /// 3) 서브 폴더 안에 stitched.png + tile_00.png... 저장
        /// </summary>
        public async Task SaveImageSetWithFolderDialogAsync(
            Bitmap stitched,
            IReadOnlyList<Bitmap> tiles,
            string? sessionName,
            CancellationToken ct = default)
        {
            if (stitched is null) throw new ArgumentNullException(nameof(stitched));
            if (tiles is null) throw new ArgumentNullException(nameof(tiles));

            var provider = GetStorageProvider();

            if (!provider.CanPickFolder)
                return;

            // 1) 부모 폴더 선택
            var parents = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "이미지 세트를 저장할 폴더를 선택하세요"
            });

            if (parents is null || parents.Count == 0)
                return;

            var parent = parents[0];

            // 2) 세션 폴더 이름
            var folderName = string.IsNullOrWhiteSpace(sessionName)
                ? $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}"
                : sessionName.Trim();

            // 3) 서브 폴더 생성
            var subFolder = await parent.CreateFolderAsync(folderName);
            if (subFolder is null)
                return;

            // 4) stitched 저장
            await SaveBitmapToFolderAsync(subFolder, "stitched.png", stitched, ct);

            // 5) 타일들 저장
            for (int i = 0; i < tiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var tile = tiles[i];
                if (tile is null)
                    continue;

                var fileName = $"tile_{i:D2}.png";
                await SaveBitmapToFolderAsync(subFolder, fileName, tile, ct);
            }
        }

        private static async Task SaveBitmapToFolderAsync(
            IStorageFolder folder,
            string fileName,
            Bitmap bitmap,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var file = await folder.CreateFileAsync(fileName);
            if (file is null)
                return;

            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream);
        }
    }
}
