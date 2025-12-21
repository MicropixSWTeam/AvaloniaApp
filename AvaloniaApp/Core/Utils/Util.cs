using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AvaloniaApp.Core.Models; // Offset

namespace AvaloniaApp.Core.Utils
{
    public static partial class Util
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ClampInt(int v, int min, int max)
            => v < min ? min : (v > max ? max : v);
        /// <summary>
        /// image 중심을 기준으로, ROI 중심점이 (centerPitchX, centerPitchY) 간격으로
        /// columns x rows 배치되며, 각 중심점을 기준으로 cropWidth/cropHeight 크기의 Rect 생성.
        /// Rect는 이미지 밖으로 나가지 않게 Top-Left를 클램프(크기는 유지).
        /// </summary>
        public static IReadOnlyList<Rect> CreateCoordinates(
            int imageWidth, int imageHeight,
            int cropWidth, int cropHeight,
            int centerPitchX, int centerPitchY,
            int columns, int rows)
        {
            if (imageWidth <= 0 || imageHeight <= 0) return Array.Empty<Rect>();
            if (cropWidth <= 0 || cropHeight <= 0) return Array.Empty<Rect>();
            if (columns <= 0 || rows <= 0) return Array.Empty<Rect>();
            if (cropWidth > imageWidth || cropHeight > imageHeight) return Array.Empty<Rect>();
            if (centerPitchX <= 0 || centerPitchY <= 0) return Array.Empty<Rect>();

            int imgCx = imageWidth / 2;
            int imgCy = imageHeight / 2;

            // 첫 중심점(좌상단 쪽) = 이미지 중심 - (전체 피치 폭/높이의 절반)
            int firstCenterX = imgCx - ((columns - 1) * centerPitchX) / 2;
            int firstCenterY = imgCy - ((rows - 1) * centerPitchY) / 2;

            int halfW = cropWidth / 2;
            int halfH = cropHeight / 2;

            int maxX = imageWidth - cropWidth;
            int maxY = imageHeight - cropHeight;

            var result = new Rect[columns * rows];
            int idx = 0;

            for (int r = 0; r < rows; r++)
            {
                int cy = firstCenterY + r * centerPitchY;
                int y = cy - halfH;
                if (y < 0) y = 0;
                else if (y > maxY) y = maxY;

                for (int c = 0; c < columns; c++)
                {
                    int cx = firstCenterX + c * centerPitchX;
                    int x = cx - halfW;
                    if (x < 0) x = 0;
                    else if (x > maxX) x = maxX;

                    result[idx++] = new Rect(x, y, cropWidth, cropHeight);
                }
            }

            return result;
        }
        /// <summary>
        /// base rects(Over) + offsets(WD별) => 이동된 rects 반환
        /// - rect.X/Y에 offset.X/Y를 더함
        /// - 이미지 밖으로 나가면 Top-Left를 클램프(크기는 유지)
        /// </summary>
        public static IReadOnlyList<OpenCvSharp.Rect> CalculateOffsetCropRects(
            IReadOnlyList<Rect> rects,
            IReadOnlyList<Offset> offsets,
            int imageWidth,
            int imageHeight)
        {
            if (rects is null) throw new ArgumentNullException(nameof(rects));
            if (offsets is null) throw new ArgumentNullException(nameof(offsets));

            int n = rects.Count;
            var result = new OpenCvSharp.Rect[n];

            for (int i = 0; i < n; i++)
            {
                var r = rects[i];

                int ox = (i < offsets.Count) ? offsets[i].X : 0;
                int oy = (i < offsets.Count) ? offsets[i].Y : 0;

                int w = r.Width;
                int h = r.Height;

                // 이동
                int x = r.X + ox;
                int y = r.Y + oy;

                // 클램프: 크기는 유지하고 Top-Left만 이미지 안으로
                int maxX = imageWidth - w;
                int maxY = imageHeight - h;

                // crop이 이미지보다 크면 의미 없으니 0크기(또는 예외로 바꿔도 됨)
                if (maxX < 0 || maxY < 0)
                {
                    result[i] = new OpenCvSharp.Rect(0, 0, 0, 0);
                    continue;
                }

                x = ClampInt(x, 0, maxX);
                y = ClampInt(y, 0, maxY);

                result[i] = new OpenCvSharp.Rect(x, y, w, h);
            }

            return result;
        }

        // 사용자가 요청한 시그니처(이미지 크기 없이)는 "클램프 불가"라서 제공 비추.
        // 꼭 원하면 아래처럼 오버로드로 두고, 호출부에서 imageWidth/imageHeight 넘기는 쪽을 사용하세요.
        public static IReadOnlyList<OpenCvSharp.Rect> CalculateOffsetCropRects(IReadOnlyList<Rect> rects,IReadOnlyList<Offset> offsets)
            => throw new NotSupportedException("imageWidth/imageHeight가 없으면 이미지 경계 클램프를 할 수 없습니다. 오버로드(이미지 크기 포함)를 사용하세요.");
        
    }
}
