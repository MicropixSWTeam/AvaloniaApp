using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpenCvSharp;
using System;
using System.IO;

namespace OnnxUpscaler.Services;

public class ImageService
{
    /// <summary>
    /// Load an image from the specified path using OpenCV and convert to Avalonia Bitmap.
    /// </summary>
    /// <param name="path">Path to the image file</param>
    /// <returns>Avalonia Bitmap or null if loading fails</returns>
    public Bitmap? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            // Load image using OpenCV
            using var mat = Cv2.ImRead(path, ImreadModes.Color);
            if (mat.Empty())
                return null;

            return MatToBitmap(mat);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Convert an OpenCV Mat to Avalonia Bitmap.
    /// </summary>
    /// <param name="mat">OpenCV Mat (BGR format)</param>
    /// <returns>Avalonia Bitmap</returns>
    public Bitmap? MatToBitmap(Mat mat)
    {
        try
        {
            // Convert BGR to BGRA for Avalonia
            using var bgra = new Mat();
            if (mat.Channels() == 3)
            {
                Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
            }
            else if (mat.Channels() == 4)
            {
                mat.CopyTo(bgra);
            }
            else if (mat.Channels() == 1)
            {
                Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA);
            }
            else
            {
                return null;
            }

            // Encode to PNG in memory
            Cv2.ImEncode(".png", bgra, out var buffer);

            // Create Avalonia Bitmap from memory stream
            using var ms = new MemoryStream(buffer);
            return new Bitmap(ms);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
