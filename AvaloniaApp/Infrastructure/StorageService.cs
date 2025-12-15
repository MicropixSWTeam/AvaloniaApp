// ==============================
// AvaloniaApp.Infrastructure/StorageService.cs
// - 원본 전체 이미지 + 원본 타일 + 처리된 타일 + stitched 전체 이미지 저장 지원
// ==============================
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using AvaloniaApp.Infrastructure;
using CommunityToolkit.Mvvm.Input;
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

            var file = await provider.SaveFilePickerAsync(options);
            if (file is null)
                return; // 사용자 취소

            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream); // PNG 저장
        }

        /// <summary>
        /// (기존) stitched + tiles 묶음을 한 번에 저장.
        /// 그대로 두고, 필요 시 다른 곳에서 사용 가능.
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

            var parents = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "이미지 세트를 저장할 폴더를 선택하세요"
            });

            if (parents is null || parents.Count == 0)
                return;

            var parent = parents[0];

            var folderName = string.IsNullOrWhiteSpace(sessionName)
                ? $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}"
                : sessionName.Trim();

            var subFolder = await parent.CreateFolderAsync(folderName);
            if (subFolder is null)
                return;

            await SaveBitmapToFolderAsync(subFolder, "stitched.png", stitched, ct);

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
        /// <summary>
        /// 원본 전체 이미지 + 원본 crop 타일 + 처리된(translation+normalize) 타일 + stitched 전체 이미지를 한 번에 저장.
        /// </summary>
        public async Task SaveFullImageSetWithFolderDialogAsync(
            Bitmap fullImage,
            IReadOnlyList<Bitmap> originalTiles,
            IReadOnlyList<Bitmap> processedTiles,
            Bitmap stitched,
            string? sessionName,
            CancellationToken ct = default)
        {
            if (fullImage is null) throw new ArgumentNullException(nameof(fullImage));
            if (originalTiles is null) throw new ArgumentNullException(nameof(originalTiles));
            if (processedTiles is null) throw new ArgumentNullException(nameof(processedTiles));
            if (stitched is null) throw new ArgumentNullException(nameof(stitched));

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

            // 4) 원본 전체 이미지 저장
            await SaveBitmapToFolderAsync(subFolder, "full_original.png", fullImage, ct);

            // 5) stitched 전체 이미지 저장
            await SaveBitmapToFolderAsync(subFolder, "stitched.png", stitched, ct);

            // 6) 원본 crop 타일 저장
            for (int i = 0; i < originalTiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var tile = originalTiles[i];
                if (tile is null)
                    continue;

                var fileName = $"orig_tile_{i:D2}.png";
                await SaveBitmapToFolderAsync(subFolder, fileName, tile, ct);
            }

            // 7) 처리된(translation+normalize) 타일 저장
            for (int i = 0; i < processedTiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var tile = processedTiles[i];
                if (tile is null)
                    continue;

                var fileName = $"proc_tile_{i:D2}.png";
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
