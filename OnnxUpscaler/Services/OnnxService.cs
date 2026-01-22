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
    public string? ExecutionProvider { get; private set; }

    public void LoadModel(string path) => LoadModel(path, forceCpu: false);

    public void LoadModel(string path, bool forceCpu)
    {
        // Dispose existing session if any
        _session?.Dispose();
        _session = null;
        ModelPath = null;
        InputName = null;
        OutputName = null;
        ExecutionProvider = null;

        var sessionOptions = new SessionOptions();
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        if (!forceCpu)
        {
            try
            {
                // Try to use CUDA (GPU)
                sessionOptions.AppendExecutionProvider_CUDA(0);
                _session = new InferenceSession(path, sessionOptions);
                ExecutionProvider = "CUDA (GPU)";
                ModelPath = path;
                InputName = _session.InputMetadata.Keys.First();
                OutputName = _session.OutputMetadata.Keys.First();
                return;
            }
            catch { /* Fall through to CPU */ }
        }

        // CPU fallback or forced CPU
        sessionOptions = new SessionOptions();
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        _session = new InferenceSession(path, sessionOptions);
        ExecutionProvider = "CPU";
        ModelPath = path;
        InputName = _session.InputMetadata.Keys.First();
        OutputName = _session.OutputMetadata.Keys.First();
    }

    public Mat Upscale(Mat input)
    {
        if (_session == null || InputName == null || OutputName == null)
            throw new InvalidOperationException("Model not loaded. Call LoadModel first.");

        // ESPCN expects grayscale input [1, 1, H, W]
        int height = input.Rows;
        int width = input.Cols;

        // Create input tensor [1, 1, H, W] for grayscale
        var inputTensor = new DenseTensor<float>(new[] { 1, 1, height, width });
        var inputSpan = inputTensor.Buffer.Span;

        // Convert HW uint8 [0,255] to CHW float32 [0,1]
        unsafe
        {
            byte* ptr = (byte*)input.Data;
            for (int i = 0; i < height * width; i++)
            {
                inputSpan[i] = ptr[i] / 255.0f;
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

        // Create output Mat (grayscale)
        var outputMat = new Mat(outHeight, outWidth, MatType.CV_8UC1);

        // Convert CHW float32 [0,1] to HW uint8 [0,255]
        var outputSpan = ((DenseTensor<float>)outputTensor).Buffer.Span;
        unsafe
        {
            byte* ptr = (byte*)outputMat.Data;
            for (int i = 0; i < outHeight * outWidth; i++)
            {
                ptr[i] = (byte)Math.Clamp(outputSpan[i] * 255.0f, 0, 255);
            }
        }

        return outputMat;
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
