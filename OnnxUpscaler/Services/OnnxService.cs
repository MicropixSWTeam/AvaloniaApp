using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnnxUpscaler.Services;

public class OnnxService : IDisposable
{
    private InferenceSession? _session;
    private bool _disposed;

    public bool IsModelLoaded => _session != null;
    public string? ModelPath { get; private set; }
    public string? InputName { get; private set; }
    public string? OutputName { get; private set; }

    public void LoadModel(string path)
    {
        // Dispose existing session if any
        _session?.Dispose();
        _session = null;
        ModelPath = null;
        InputName = null;
        OutputName = null;

        // Create session options for CPU execution
        var sessionOptions = new SessionOptions();
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        // Load the model
        _session = new InferenceSession(path, sessionOptions);
        ModelPath = path;

        // Get input/output names dynamically
        InputName = _session.InputMetadata.Keys.First();
        OutputName = _session.OutputMetadata.Keys.First();
    }

    public Mat Upscale(Mat input)
    {
        if (_session == null || InputName == null || OutputName == null)
            throw new InvalidOperationException("Model not loaded. Call LoadModel first.");

        // Convert BGR to RGB
        using var rgb = new Mat();
        Cv2.CvtColor(input, rgb, ColorConversionCodes.BGR2RGB);

        int height = rgb.Rows;
        int width = rgb.Cols;
        int channels = 3;

        // Create input tensor [1, 3, H, W]
        var inputTensor = new DenseTensor<float>(new[] { 1, channels, height, width });

        // Convert HWC uint8 [0,255] to CHW float32 [0,1]
        unsafe
        {
            byte* ptr = (byte*)rgb.Data;
            for (int h = 0; h < height; h++)
            {
                for (int w = 0; w < width; w++)
                {
                    int pixelIndex = (h * width + w) * channels;
                    inputTensor[0, 0, h, w] = ptr[pixelIndex + 0] / 255.0f; // R
                    inputTensor[0, 1, h, w] = ptr[pixelIndex + 1] / 255.0f; // G
                    inputTensor[0, 2, h, w] = ptr[pixelIndex + 2] / 255.0f; // B
                }
            }
        }

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputName, inputTensor)
        };

        using var results = _session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        // Get output dimensions
        var outputDims = outputTensor.Dimensions.ToArray();
        int outHeight = outputDims[2];
        int outWidth = outputDims[3];

        // Create output Mat (RGB)
        var outputRgb = new Mat(outHeight, outWidth, MatType.CV_8UC3);

        // Convert CHW float32 [0,1] to HWC uint8 [0,255]
        unsafe
        {
            byte* ptr = (byte*)outputRgb.Data;
            for (int h = 0; h < outHeight; h++)
            {
                for (int w = 0; w < outWidth; w++)
                {
                    int pixelIndex = (h * outWidth + w) * channels;
                    ptr[pixelIndex + 0] = (byte)Math.Clamp(outputTensor[0, 0, h, w] * 255.0f, 0, 255); // R
                    ptr[pixelIndex + 1] = (byte)Math.Clamp(outputTensor[0, 1, h, w] * 255.0f, 0, 255); // G
                    ptr[pixelIndex + 2] = (byte)Math.Clamp(outputTensor[0, 2, h, w] * 255.0f, 0, 255); // B
                }
            }
        }

        // Convert RGB back to BGR for OpenCV
        var outputBgr = new Mat();
        Cv2.CvtColor(outputRgb, outputBgr, ColorConversionCodes.RGB2BGR);
        outputRgb.Dispose();

        return outputBgr;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _session?.Dispose();
                _session = null;
            }
            _disposed = true;
        }
    }
}
