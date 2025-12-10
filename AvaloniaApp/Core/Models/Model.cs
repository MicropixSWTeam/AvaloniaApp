using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Models
{
    public class Model : IAsyncDisposable
    {
        #region CropModel
        /// <summary>
        /// Crop시 행 개수
        /// </summary>
        int RowCount { get; set; } = 5;
        /// <summary>
        /// Crop시 열 개수
        /// </summary>
        int ColCount { get; set; } = 3;
        /// <summary>
        /// Crop시 WidthSize
        /// </summary>
        int WidthSize { get; set; } = 1064;
        /// <summary>
        /// Crop시 HeightSize
        /// </summary>
        int HeightSize { get; set; } = 1012;
        /// <summary>
        /// Crop시 WidthGap
        /// </summary>
        int WidthGap { get; set; } = 1064;
        /// <summary>
        /// Crop시 HeightGap
        /// </summary>
        int HeightGap { get; set; } = 1012;
        #endregion
        #region ImageModel
        /// <summary>
        /// 원본이미지
        /// </summary>
        public Mat? EntireImage { get; set; }
        /// <summary>
        /// 잘린 이미지들
        /// </summary>
        public List<Mat> CropImages { get; set; } = new();
        /// <summary>
        /// Calibration이 적용된 잘린 이미지들
        /// </summary>
        public List<Mat> CalibrationCropImages { get; set; } = new();
        /// <summary>
        /// Normalize와 Calibration이 적용된 잘린 이미지들
        /// </summary>
        public List<Mat> NormalizeCropImages { get; set; } = new();
        /// <summary>
        /// 최종적으로 Stitching된 이미지
        /// </summary>
        public Mat? StitchImage {  get; set; }
        #endregion
        #region CalibrationModel

        #endregion
        #region RectModel

        #endregion
        public void SetSourceImage(Mat Image)
        {

        }
        ValueTask IAsyncDisposable.DisposeAsync()
        {
            EntireImage?.Dispose();
            EntireImage = null;

            foreach (var m in CropImages) m.Dispose();
            CropImages.Clear();

            foreach (var m in CalibrationCropImages) m.Dispose();
            CalibrationCropImages.Clear();

            foreach (var m in NormalizeCropImages) m.Dispose();
            NormalizeCropImages.Clear();

            StitchImage?.Dispose();
            StitchImage = null;

            return ValueTask.CompletedTask;
        }
    }
}
