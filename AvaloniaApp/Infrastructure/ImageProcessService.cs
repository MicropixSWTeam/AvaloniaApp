using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Models;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public partial class ImageProcessService
    {
        /// <summary>
        /// 이미 구해둔 CropFrames(파장 타일들)에서 RGB(BGR24) FrameData를 생성합니다.
        /// 특정 파장(450, 530, 630)이 존재해야 합니다.
        /// </summary>
        public FrameData GetRgbFrameDataFromCropFrames(IReadOnlyList<FrameData> cropFrames)
        {
            // 설정된 파장 인덱스 맵핑 가져오기
            var map = Options.GetWavelengthIndexMap();

            // 필수 파장 체크
            if (!map.TryGetValue(450, out int idxBlue) ||
                !map.TryGetValue(530, out int idxGreen) ||
                !map.TryGetValue(630, out int idxRed))
            {
                throw new InvalidOperationException("RGB wavelengths mapping not found.");
            }

            if (idxBlue >= cropFrames.Count || idxGreen >= cropFrames.Count || idxRed >= cropFrames.Count)
                throw new InvalidOperationException("RGB wavelengths are not available in current crop frames.");

            var frameB = cropFrames[idxBlue];
            var frameG = cropFrames[idxGreen];
            var frameR = cropFrames[idxRed];

            int w = frameB.Width;
            int h = frameB.Height;

            // 사이즈 일치 여부 확인
            if (frameG.Width != w || frameG.Height != h || frameR.Width != w || frameR.Height != h)
                throw new InvalidOperationException("RGB crop frames sizes are not identical.");

            // BGR24 (3 channels)
            int len = w * h * 3;
            var rgbBuffer = ArrayPool<byte>.Shared.Rent(len);

            try
            {
                // OpenCV Mat 생성 (메모리 복사 없이 래핑)
                using var matB = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameB.Bytes, frameB.Stride);
                using var matG = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameG.Bytes, frameG.Stride);
                using var matR = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameR.Bytes, frameR.Stride);

                using var matDst = Mat.FromPixelData(h, w, MatType.CV_8UC3, rgbBuffer);

                // Merge: B, G, R -> Destination (Avalonia/Windows Bitmap은 BGR 순서)
                Cv2.Merge(new[] { matB, matG, matR }, matDst);

                // Stride = Width * 3 (Packed)
                return FrameData.Wrap(rgbBuffer, w, h, w * 3, len);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(rgbBuffer);
                throw;
            }
        }

        /// <summary>
        /// 저장용: FullFrame(EntireFrameData) 크기를 기준으로 개별 CropFrames를 원래 좌표에 스티칭합니다.
        /// </summary>
        public FrameData? GetStitchFrameData(FrameData fullFrame, IReadOnlyList<FrameData> cropFrames, int wd = 0)
        {
            if (cropFrames == null || cropFrames.Count == 0) return null;

            int entireW = fullFrame.Width;
            int entireH = fullFrame.Height;
            int dstLen = entireW * entireH; // 8bit Gray assumed

            // 좌표 정보 가져오기
            var coordinates = Options.GetCoordinates(wd);

            var stitchBuffer = ArrayPool<byte>.Shared.Rent(dstLen);
            Array.Clear(stitchBuffer, 0, dstLen); // 배경 0으로 초기화

            try
            {
                int count = Math.Min(cropFrames.Count, coordinates.Count);
                unsafe
                {
                    fixed (byte* pDstBase = stitchBuffer)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var crop = cropFrames[i];
                            var rect = coordinates[i];

                            // 좌표 범위 계산 (경계 체크)
                            int x = Math.Max(0, rect.X);
                            int y = Math.Max(0, rect.Y);

                            // 원본 프레임을 넘어가지 않도록 클리핑
                            int w = Math.Min(crop.Width, entireW - x);
                            int h = Math.Min(crop.Height, entireH - y);

                            if (w <= 0 || h <= 0) continue;

                            fixed (byte* pSrcBase = crop.Bytes)
                            {
                                for (int row = 0; row < h; row++)
                                {
                                    // 소스 행
                                    byte* pSrc = pSrcBase + (long)row * crop.Stride;
                                    // 타겟 행 (y + row)
                                    byte* pDst = pDstBase + (long)(y + row) * entireW + x;

                                    // 고속 복사
                                    Buffer.MemoryCopy(pSrc, pDst, w, w);
                                }
                            }
                        }
                    }
                }

                return FrameData.Wrap(stitchBuffer, entireW, entireH, entireW, dstLen);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(stitchBuffer);
                throw;
            }
        }
        public FrameData CloneFrameData(FrameData src) => FrameData.CloneFullFrame(src);

        // [신규] 전처리 로직 (OpenCV In-place 처리)
        public unsafe void ProcessFrame(FrameData frame)
        {
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
            // 필요 시 여기에 정규화, 노이즈 제거 등의 로직 추가
            // Cv2.Normalize(mat, mat, 0, 255, NormTypes.MinMax);
        }

        public FrameData GetCropFrameData(FrameData fullframe, int previewIndex, int wd = 0)
        {
            var coordinates = Options.GetCoordinates(wd);
            var rect = coordinates[previewIndex];
            return CropFrameData(fullframe, rect);
        }

        public List<FrameData> GetCropFrameDatas(FrameData fullframe, int wd = 0)
        {
            var coordinates = Options.GetCoordinates(wd);
            var results = new List<FrameData>(coordinates.Count);
            try
            {
                for (int i = 0; i < coordinates.Count; i++)
                {
                    results.Add(CropFrameData(fullframe, coordinates[i]));
                }
                return results;
            }
            catch
            {
                foreach (var r in results) r.Dispose();
                results.Clear();
                throw;
            }
        }

        public FrameData GetRgbFrameData(FrameData fullframe, int wd = 0)
        {
            int idxBlue = Options.GetWavelengthIndexMap()[450];
            int idxGreen = Options.GetWavelengthIndexMap()[530];
            int idxRed = Options.GetWavelengthIndexMap()[630];

            var coords = Options.GetCoordinates(wd);
            var rectBlue = coords[idxBlue];
            var rectGreen = coords[idxGreen];
            var rectRed = coords[idxRed];

            // 1. 각 채널 Crop (최적화된 CloneCropFrame 사용)
            using var frameB = CropFrameData(fullframe, rectBlue);
            using var frameG = CropFrameData(fullframe, rectGreen);
            using var frameR = CropFrameData(fullframe, rectRed);

            int w = frameB.Width;
            int h = frameB.Height;
            int len = w * h * 3; // 3채널

            // 2. OpenCV Merge
            // 각 채널을 Mat으로 변환 (데이터 복사 없이 포인터만 사용)
            using var matB = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameB.Bytes, frameB.Stride);
            using var matG = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameG.Bytes, frameG.Stride);
            using var matR = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameR.Bytes, frameR.Stride);

            // 결과 담을 버퍼
            var rgbBuffer = ArrayPool<byte>.Shared.Rent(len);

            try
            {
                // MatType.CV_8UC3 (Blue, Green, Red 순서 - OpenCV 기본)
                // 만약 Avalonia Bitmap이 BGRA 등을 요구한다면 Alpha 채널 추가 필요할 수 있음
                // 여기서는 일단 RGB(또는 BGR) 3채널 Raw Data를 만듦
                using var matDst = Mat.FromPixelData(h, w, MatType.CV_8UC3, rgbBuffer);

                // 채널 병합 (B, G, R 순서로 넣어야 BGR 포맷이 됨 -> Avalonia는 보통 BGRA나 RGB 지원)
                // 여기서는 PixelFormats.Rgb24 대응을 위해 R, G, B 순서로 Merge하거나
                // OpenCV 기본 BGR로 하고 PixelFormats.Bgr24를 쓸 수 있음. (아래 Convert에서 Bgr24 사용 예정)
                Cv2.Merge(new[] { matB, matG, matR }, matDst);

                // FrameData 생성 (Stride는 w * 3)
                return FrameData.Wrap(rgbBuffer, w, h, w * 3, len);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(rgbBuffer);
                throw;
            }
        }
        public FrameData? GetStitchFrameData(List<FrameData> frames, int wd = 0)
        {
            if (frames == null || frames.Count == 0) return null;

            int entireW = Options.EntireWidth;
            int entireH = Options.EntireHeight;
            int dstLen = entireW * entireH;
            var coordinates = Options.GetCoordinates(wd);
            var stitchBuffer = ArrayPool<byte>.Shared.Rent(dstLen);
            Array.Clear(stitchBuffer, 0, dstLen);

            try
            {
                int count = Math.Min(frames.Count, coordinates.Count);
                unsafe
                {
                    fixed (byte* pDstBase = stitchBuffer)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var crop = frames[i];
                            var rect = coordinates[i];
                            int x = Math.Max(0, (int)rect.X);
                            int y = Math.Max(0, (int)rect.Y);
                            int w = Math.Min(crop.Width, entireW - x);
                            int h = Math.Min(crop.Height, entireH - y);

                            if (w <= 0 || h <= 0) continue;

                            fixed (byte* pSrcBase = crop.Bytes)
                            {
                                for (int row = 0; row < h; row++)
                                {
                                    byte* pSrc = pSrcBase + (long)row * crop.Stride;
                                    byte* pDst = pDstBase + (long)(y + row) * entireW + x;
                                    Buffer.MemoryCopy(pSrc, pDst, w, w);
                                }
                            }
                        }
                    }
                }
                return FrameData.Wrap(stitchBuffer, entireW, entireH, entireW, dstLen);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(stitchBuffer);
                throw;
            }
        }

        public unsafe void ConvertFrameDataToWriteableBitmap(WriteableBitmap bitmap, FrameData frame)
        {
            using var fb = bitmap.Lock();

            int width = Math.Min(frame.Width, fb.Size.Width);
            int height = Math.Min(frame.Height, fb.Size.Height);

            int srcStride = frame.Stride;
            int dstStride = fb.RowBytes;
            int copyWidth = width;

            fixed (byte* src0 = frame.Bytes)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                if (dstStride == srcStride && width == frame.Width && dstStride == width)
                {
                    long totalBytes = (long)dstStride * height;
                    if (totalBytes <= frame.Length)
                    {
                        Buffer.MemoryCopy(src, dst, totalBytes, totalBytes);
                        return;
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * dstStride,
                        copyWidth,
                        copyWidth);
                }
            }
        }
        public unsafe void ConvertRgbFrameDataToWriteableBitmap(WriteableBitmap bitmap, FrameData frame)
        {
            using var fb = bitmap.Lock();

            int width = Math.Min(frame.Width, fb.Size.Width);
            int height = Math.Min(frame.Height, fb.Size.Height);

            int srcStride = frame.Stride;
            int dstStride = fb.RowBytes;
            // 3 bytes per pixel
            int copyWidthBytes = width * 3;

            fixed (byte* src0 = frame.Bytes)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * dstStride,
                        copyWidthBytes,
                        copyWidthBytes);
                }
            }
        }
        public WriteableBitmap CreateBitmapFromFrame(FrameData frame, PixelFormat? format = null)
        {
            var pxFormat = format ?? PixelFormats.Gray8; // 기본값 Mono8
            // RGB 데이터라면 호출자가 PixelFormats.Bgr24 등을 명시해야 함

            var bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                pxFormat,
                AlphaFormat.Opaque);

            // 기존 변환 메서드 재사용 (Lock & Copy)
            if (pxFormat == PixelFormats.Bgr24 || pxFormat == PixelFormats.Rgb24)
            {
                ConvertRgbFrameDataToWriteableBitmap(bitmap, frame);
            }
            else
            {
                ConvertFrameDataToWriteableBitmap(bitmap, frame);
            }

            return bitmap;
        }
        private unsafe FrameData CropFrameData(FrameData src, Rect roi)
        {
            return FrameData.CloneCropFrame(src, roi);
        }
        public FrameData CalculateTwoWavelengths(FrameData fullFrame, int waveLength1, int waveLength2, ImageOperationType op, int wd = 0)
        {
            var map = Options.GetWavelengthIndexMap();

            if (!map.TryGetValue(waveLength1, out int idx1) || !map.TryGetValue(waveLength2, out int idx2))
            {
                throw new ArgumentException("Invalid Wavelength provided.");
            }

            var coords = Options.GetCoordinates(wd);
            if (idx1 >= coords.Count || idx2 >= coords.Count)
            {
                throw new ArgumentException("Coordinates not found for the given wavelength.");
            }

            // 1. 두 영역을 Crop 합니다. (FrameData는 내부적으로 ArrayPool을 사용하므로 using 필수)
            // GetCropFrameData는 내부적으로 CloneCropFrame을 호출하여 새 버퍼를 할당합니다.
            using var frame1 = GetCropFrameData(fullFrame, idx1, wd);
            using var frame2 = GetCropFrameData(fullFrame, idx2, wd);

            int w = frame1.Width;
            int h = frame1.Height;

            // 크기가 다르면 연산 불가 (Grid 설정상 같아야 정상)
            if (w != frame2.Width || h != frame2.Height)
                throw new InvalidOperationException("Frame sizes do not match.");

            // 결과 담을 버퍼 할당 (ArrayPool)
            int len = w * h; // Mono8 기준
            var resultBuffer = ArrayPool<byte>.Shared.Rent(len);

            try
            {
                // 2. OpenCV Mat으로 래핑 (메모리 복사 없음)
                using var mat1 = Mat.FromPixelData(h, w, MatType.CV_8UC1, frame1.Bytes, frame1.Stride);
                using var mat2 = Mat.FromPixelData(h, w, MatType.CV_8UC1, frame2.Bytes, frame2.Stride);
                using var matDst = Mat.FromPixelData(h, w, MatType.CV_8UC1, resultBuffer); // Stride = w

                // 3. 정밀 연산을 위해 32F(Float)로 변환
                // 단순히 byte끼리 연산하면 255를 넘거나 0 밑으로 갈 때 정보가 바로 손실됨
                using var mat1f = new Mat();
                using var mat2f = new Mat();
                mat1.ConvertTo(mat1f, MatType.CV_32FC1);
                mat2.ConvertTo(mat2f, MatType.CV_32FC1);

                using var matResultf = new Mat();

                // 4. 연산 수행
                switch (op)
                {
                    case ImageOperationType.Add:
                        Cv2.Add(mat1f, mat2f, matResultf);
                        break;

                    case ImageOperationType.Subtract:
                        Cv2.Subtract(mat1f, mat2f, matResultf);
                        break;

                    case ImageOperationType.Difference:
                        Cv2.Absdiff(mat1f, mat2f, matResultf);
                        break;

                    case ImageOperationType.Multiply:
                        // 단순 곱셈은 값이 너무 커지므로 보통 정규화하거나 Scale을 둡니다.
                        // 여기서는 단순 곱셈 후 Saturate 합니다. (필요시 1/255.0 스케일링 추가)
                        Cv2.Multiply(mat1f, mat2f, matResultf);
                        break;

                    case ImageOperationType.Divide:
                        // 나눗셈 (0으로 나누기 방지 포함)
                        // 결과가 아주 작을 수 있으므로 (예: 100/200 = 0.5) 시각화를 위해 스케일링이 필요할 수 있습니다.
                        // 여기서는 순수 나눗셈을 수행합니다.
                        Cv2.Divide(mat1f, mat2f, matResultf);
                        // 시각화를 위해 255를 곱해주는 경우가 많습니다. (Ratio Map)
                        // matResultf *= 255.0; 
                        break;

                    case ImageOperationType.Average:
                        Cv2.AddWeighted(mat1f, 0.5, mat2f, 0.5, 0, matResultf);
                        break;
                }

                // 5. 결과를 다시 8bit로 변환 (Saturate: 0~255 범위로 클램핑)
                matResultf.ConvertTo(matDst, MatType.CV_8UC1);

                // 6. FrameData 포장하여 반환
                // Mat이 Dispose 되어도 resultBuffer는 ArrayPool 소유이므로 안전함 (FrameData가 Dispose될 때 반환)
                return FrameData.Wrap(resultBuffer, w, h, w, len);
            }
            catch
            {
                // 에러 발생 시 버퍼 반환
                ArrayPool<byte>.Shared.Return(resultBuffer);
                throw;
            }
        }
    }
}