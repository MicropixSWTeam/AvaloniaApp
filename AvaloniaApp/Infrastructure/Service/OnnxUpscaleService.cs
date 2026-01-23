using AvaloniaApp.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AvaloniaApp.Infrastructure.Service
{
    public class OnnxUpscaleService : IDisposable
    {
        private InferenceSession? _session;
        private bool _disposed;
        private readonly IConfiguration _config;

        public bool IsEnabled { get; private set; }
        public bool IsModelLoaded => _session != null;
        public string? ExecutionProvider { get; private set; }
        public string? InputName { get; private set; }
        public string? OutputName { get; private set; }

        private const string ModelPath = "OnnxData/espcn_x2.onnx";

        public OnnxUpscaleService(IConfiguration config)
        {
            _config = config;
            IsEnabled = config.GetValue<bool>("Upscale:Enabled");
            if (IsEnabled)
            {
                try
                {
                    var preferGpu = config.GetValue<bool>("Upscale:PreferGpu", true);
                    var fullPath = Path.Combine(AppContext.BaseDirectory, ModelPath);
                    if (File.Exists(fullPath))
                    {
                        LoadModel(fullPath, !preferGpu);
                    }
                    else
                    {
                        Console.WriteLine($"[OnnxUpscaleService] Model not found at: {fullPath}");
                        IsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OnnxUpscaleService] Failed to initialize: {ex.Message}");
                    IsEnabled = false;
                    _session = null;
                }
            }
        }

        public void LoadModel(string path, bool forceCpu = false)
        {
            _session?.Dispose();
            _session = null;
            InputName = null;
            OutputName = null;
            ExecutionProvider = null;

            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            if (!forceCpu)
            {
                try
                {
                    sessionOptions.AppendExecutionProvider_CUDA(0);
                    _session = new InferenceSession(path, sessionOptions);
                    ExecutionProvider = "CUDA (GPU)";
                    InputName = _session.InputMetadata.Keys.First();
                    OutputName = _session.OutputMetadata.Keys.First();
                    Console.WriteLine($"[OnnxUpscaleService] Loaded model with {ExecutionProvider}");
                    return;
                }
                catch
                {
                    // Fall through to CPU
                }
            }

            // CPU fallback
            sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            _session = new InferenceSession(path, sessionOptions);
            ExecutionProvider = "CPU";
            InputName = _session.InputMetadata.Keys.First();
            OutputName = _session.OutputMetadata.Keys.First();
            Console.WriteLine($"[OnnxUpscaleService] Loaded model with {ExecutionProvider}");
        }

        public FrameData? Upscale(FrameData input)
        {
            if (_session == null || InputName == null || OutputName == null)
                return null;

            int height = input.Height;
            int width = input.Width;

            // ESPCN expects grayscale input [1, 1, H, W]
            var inputTensor = new DenseTensor<float>(new[] { 1, 1, height, width });
            var inputSpan = inputTensor.Buffer.Span;

            // Convert 8-bit grayscale [0,255] to float [0,1]
            var srcBytes = input.Bytes;
            int srcStride = input.Stride;
            for (int y = 0; y < height; y++)
            {
                int srcRowOffset = y * srcStride;
                int dstRowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    inputSpan[dstRowOffset + x] = srcBytes[srcRowOffset + x] / 255.0f;
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
            int outLength = outHeight * outWidth;

            // Allocate output buffer from pool
            var outBuffer = ArrayPool<byte>.Shared.Rent(outLength);
            try
            {
                // Convert float [0,1] to 8-bit grayscale [0,255]
                var outputSpan = ((DenseTensor<float>)outputTensor).Buffer.Span;
                for (int i = 0; i < outLength; i++)
                {
                    outBuffer[i] = (byte)Math.Clamp(outputSpan[i] * 255.0f, 0, 255);
                }

                return FrameData.Wrap(outBuffer, outWidth, outHeight, outWidth, outLength);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(outBuffer);
                throw;
            }
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
}
