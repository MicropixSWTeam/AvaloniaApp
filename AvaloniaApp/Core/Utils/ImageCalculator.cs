using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCvSharp;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Configuration; // Options 사용

namespace AvaloniaApp.Core.Utils
{
    public class CompiledExpression
    {
        public Queue<string> RpnQueue { get; }
        public CompiledExpression(Queue<string> rpnQueue) => RpnQueue = rpnQueue;
    }

    public static class ImageCalculator
    {
        private static readonly Dictionary<string, int> _precedence = new()
        {
            { "(", 0 }, { "+", 1 }, { "-", 1 }, { "|", 1 },
            { "*", 2 }, { "/", 2 }, { "&", 2 }
        };

        public static FrameData? Evaluate(string expression, Func<int, FrameData?> frameProvider)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;
            if (!TryParse(expression, out var compiled, out _)) return null;
            return Evaluate(compiled!, frameProvider);
        }

        public static FrameData? Evaluate(CompiledExpression compiled, Func<int, FrameData?> frameProvider)
        {
            if (compiled == null || compiled.RpnQueue.Count == 0) return null;

            var tokens = compiled.RpnQueue.ToArray();
            var stack = new Stack<MatInfo>();

            // [핵심] 파장(예: 450) -> 인덱스(예: 2) 매핑 테이블 로드
            var waveMap = Options.GetWavelengthIndexMap();

            try
            {
                foreach (var token in tokens)
                {
                    if (double.TryParse(token, out double val))
                    {
                        bool isImageLoaded = false;

                        // 1. 숫자가 정수이고, 파장 목록에 존재하는 값인지 확인 (예: 450)
                        if (val == (int)val && waveMap.TryGetValue((int)val, out int index))
                        {
                            try
                            {
                                // 2. Provider에게 해당 인덱스의 프레임 요청 (Streaming or Workspace)
                                var frame = frameProvider(index);
                                if (frame != null)
                                {
                                    var matF = new Mat();
                                    using var mat8 = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
                                    mat8.ConvertTo(matF, MatType.CV_32FC1);

                                    stack.Push(new MatInfo(matF, true));
                                    frame.Dispose(); // Mat 변환 완료 후 FrameData 해제
                                    isImageLoaded = true;
                                }
                            }
                            catch { /* 로드 실패 시 상수로 처리 */ }
                        }

                        // 3. 파장이 아니거나 이미지를 로드하지 못했으면 상수(Scalar)로 처리 (예: "/ 2")
                        if (!isImageLoaded)
                        {
                            var scalarMat = new Mat(1, 1, MatType.CV_32FC1, new Scalar(val));
                            stack.Push(new MatInfo(scalarMat, true));
                        }
                    }
                    else // 연산자 처리
                    {
                        if (stack.Count < 2) throw new ArgumentException("Not enough operands");
                        var right = stack.Pop();
                        var left = stack.Pop();

                        try
                        {
                            var resultMat = PerformOperation(left.mat, right.mat, token);
                            stack.Push(new MatInfo(resultMat, true));
                        }
                        finally
                        {
                            if (left.isTemporary) left.mat.Dispose();
                            if (right.isTemporary) right.mat.Dispose();
                        }
                    }
                }

                if (stack.Count != 1) return null;
                var final = stack.Pop();
                using var finalMatF = final.mat;
                if (finalMatF.Width <= 1 && finalMatF.Height <= 1) return null; // 결과가 상수인 경우 무시

                int len = finalMatF.Width * finalMatF.Height;
                var buffer = ArrayPool<byte>.Shared.Rent(len);
                using var finalMat8 = new Mat(finalMatF.Height, finalMatF.Width, MatType.CV_8UC1);

                finalMatF.ConvertTo(finalMat8, MatType.CV_8UC1);
                System.Runtime.InteropServices.Marshal.Copy(finalMat8.Data, buffer, 0, len);

                return FrameData.Wrap(buffer, finalMat8.Width, finalMat8.Height, finalMat8.Width, len);
            }
            catch { return null; }
            finally
            {
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    if (item.isTemporary) item.mat.Dispose();
                }
            }
        }

        private static Mat PerformOperation(Mat a, Mat b, string op)
        {
            bool aIsS = a.Rows == 1 && a.Cols == 1;
            bool bIsS = b.Rows == 1 && b.Cols == 1;
            var dst = new Mat();

            // 1. Scalar 연산 지원 (이미지 + 10 등)
            if (aIsS && !bIsS)
            {
                double s = a.At<float>(0, 0);
                switch (op)
                {
                    case "+": Cv2.Add(new Scalar(s), b, dst); break;
                    case "-": Cv2.Subtract(new Scalar(s), b, dst); break;
                    case "*": Cv2.Multiply(new Scalar(s), b, dst); break;
                    case "/": Cv2.Divide(new Scalar(s), b, dst); break;
                    default: Cv2.Absdiff(new Scalar(s), b, dst); break;
                }
            }
            else if (!aIsS && bIsS)
            {
                double s = b.At<float>(0, 0);
                switch (op)
                {
                    case "+": Cv2.Add(a, new Scalar(s), dst); break;
                    case "-": Cv2.Subtract(a, new Scalar(s), dst); break;
                    case "*": Cv2.Multiply(a, new Scalar(s), dst); break;
                    case "/": Cv2.Divide(a, new Scalar(s), dst); break;
                    default: Cv2.Absdiff(a, new Scalar(s), dst); break;
                }
            }
            else // 2. 이미지 간 연산
            {
                switch (op)
                {
                    case "+": Cv2.Add(a, b, dst); break;
                    case "-": Cv2.Subtract(a, b, dst); break;
                    case "*": Cv2.Multiply(a, b, dst); break;
                    case "/": Cv2.Divide(a, b, dst); break;
                    case "&": Cv2.AddWeighted(a, 0.5, b, 0.5, 0, dst); break;
                    default: Cv2.Absdiff(a, b, dst); break;
                }
            }
            return dst;
        }

        public static bool TryParse(string expression, out CompiledExpression? result, out string? errorMessage)
        {
            result = null; errorMessage = null;
            try { var q = ParseToRPN(expression); result = new CompiledExpression(q); return true; }
            catch (Exception ex) { errorMessage = ex.Message; return false; }
        }

        private static Queue<string> ParseToRPN(string expression)
        {
            var output = new Queue<string>(); var stack = new Stack<string>();
            var matches = Regex.Matches(expression, @"(\d+(\.\d+)?)|([\+\-\*\/\|\&\(\)])");
            foreach (Match m in matches)
            {
                string t = m.Value;
                if (double.TryParse(t, out _)) output.Enqueue(t);
                else if (t == "(") stack.Push(t);
                else if (t == ")") { while (stack.Count > 0 && stack.Peek() != "(") output.Enqueue(stack.Pop()); stack.Pop(); }
                else { while (stack.Count > 0 && _precedence.ContainsKey(stack.Peek()) && _precedence[stack.Peek()] >= _precedence[t]) output.Enqueue(stack.Pop()); stack.Push(t); }
            }
            while (stack.Count > 0) output.Enqueue(stack.Pop());
            return output;
        }

        private record MatInfo(Mat mat, bool isTemporary);
    }
}