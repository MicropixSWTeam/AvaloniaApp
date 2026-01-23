using AvaloniaApp.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Serilog;
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
            var preferGpu = config.GetValue<bool>("Upscale:PreferGpu", true);
            Log.Information("[OnnxUpscaleService] Enabled={Enabled}, PreferGpu={PreferGpu}", IsEnabled, preferGpu);

            if (IsEnabled)
            {
                try
                {
                    var fullPath = Path.Combine(AppContext.BaseDirectory, ModelPath);
                    Log.Information("[OnnxUpscaleService] Model path: {Path}", fullPath);

                    if (File.Exists(fullPath))
                    {
                        LoadModel(fullPath, !preferGpu);
                    }
                    else
                    {
                        Log.Warning("[OnnxUpscaleService] Model not found at: {Path}", fullPath);
                        IsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[OnnxUpscaleService] Failed to initialize");
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
                    Log.Information("[OnnxUpscaleService] Loaded model with {Provider}", ExecutionProvider);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[OnnxUpscaleService] CUDA initialization failed, falling back to CPU");
                }
            }

            // CPU fallback
            sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            _session = new InferenceSession(path, sessionOptions);
            ExecutionProvider = "CPU";
            InputName = _session.InputMetadata.Keys.First();
            OutputName = _session.OutputMetadata.Keys.First();
            Log.Information("[OnnxUpscaleService] Loaded model with {Provider}", ExecutionProvider);
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
