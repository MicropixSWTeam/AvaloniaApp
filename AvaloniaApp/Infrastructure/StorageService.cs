using Avalonia.Media.Imaging;
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
        // EXE 파일 실행 위치 기준 "SaveData" 폴더 경로
        private string SaveRootPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SaveData");

        public StorageService()
        {
            // 서비스 시작 시 폴더가 없으면 생성
            if (!Directory.Exists(SaveRootPath))
            {
                Directory.CreateDirectory(SaveRootPath);
            }
        }

        // 저장된 폴더 목록 가져오기 (로드 화면용)
        public List<string> GetSavedFolders()
        {
            if (!Directory.Exists(SaveRootPath)) return new List<string>();

            return new DirectoryInfo(SaveRootPath)
                .GetDirectories()
                .OrderByDescending(d => d.CreationTime) // 최신순 정렬
                .Select(d => d.Name)
                .ToList();
        }

        public async Task SaveExperimentResultAsync(
            string folderName,
            Bitmap? fullImage,
            Bitmap? colorImage,
            Dictionary<string, Bitmap> cropImages,
            string csvContent,
            CancellationToken ct = default)
        {
            // 폴더명 정제 (특수문자 제거)
            var safeFolderName = SanitizeFileName(folderName);
            var targetFolderPath = Path.Combine(SaveRootPath, safeFolderName);

            if (!Directory.Exists(targetFolderPath)) Directory.CreateDirectory(targetFolderPath);

            try
            {
                // 이미지 및 CSV 저장 (System.IO 사용)
                if (fullImage != null)
                    await SaveBitmapToPathAsync(Path.Combine(targetFolderPath, "FullImage.png"), fullImage);

                if (colorImage != null)
                    await SaveBitmapToPathAsync(Path.Combine(targetFolderPath, "ColorImage.png"), colorImage);

                foreach (var kvp in cropImages)
                {
                    if (kvp.Value == null) continue;
                    var fileName = $"{SanitizeFileName(kvp.Key)}.png";
                    await SaveBitmapToPathAsync(Path.Combine(targetFolderPath, fileName), kvp.Value);
                }

                await File.WriteAllTextAsync(Path.Combine(targetFolderPath, "IntensityData.csv"), csvContent, Encoding.UTF8, ct);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] Error: {ex.Message}");
                throw; // 에러를 상위(ViewModel)로 던져서 알림창을 띄우게 함
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private async Task SaveBitmapToPathAsync(string path, Bitmap bitmap)
        {
            await Task.Run(() =>
            {
                using var stream = File.Open(path, FileMode.Create);
                bitmap.Save(stream);
            });
        }
    }
}