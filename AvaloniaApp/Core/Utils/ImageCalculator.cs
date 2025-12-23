using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenCvSharp;
using AvaloniaApp.Core.Models;

namespace AvaloniaApp.Core.Utils
{
    public static class ImageCalculator
    {
        private static readonly Dictionary<string, int> _precedence = new()
        {
            { "(", 0 }, { "+", 1 }, { "-", 1 }, { "|", 1 },
            { "*", 2 }, { "/", 2 }, { "&", 2 }
        };

        public static FrameData? Evaluate(string expression, Func<int, FrameData> frameProvider)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;

            // 스택을 try 블록 밖에서 선언하여 finally에서 접근 가능하게 함
            var stack = new Stack<MatInfo>();

            try
            {
                var rpnQueue = ParseToRPN(expression);
                if (rpnQueue.Count == 0) return null;

                foreach (var token in rpnQueue)
                {
                    if (int.TryParse(token, out int wavelength))
                    {
                        // 1. FrameData 가져오기
                        using var frame = frameProvider(wavelength);
                        if (frame == null) throw new ArgumentException($"Wavelength {wavelength} not found");

                        // 2. 32F Mat로 변환 (정밀도 유지)
                        var matF = new Mat();
                        try
                        {
                            using var mat8 = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
                            mat8.ConvertTo(matF, MatType.CV_32FC1);

                            // 성공적으로 생성된 경우에만 스택에 푸시
                            stack.Push(new MatInfo(matF, isTemporary: true));
                        }
                        catch
                        {
                            matF.Dispose(); // 변환 실패 시 즉시 해제
                            throw;
                        }
                    }
                    else
                    {
                        if (stack.Count < 2) throw new ArgumentException("Invalid expression: Not enough operands");

                        // 스택에서 꺼내기 (Pop된 순간 소유권은 이 지역변수로 넘어옴)
                        var right = stack.Pop();
                        var left = stack.Pop();

                        Mat resultMat = null;
                        try
                        {
                            resultMat = PerformOperation(left.mat, right.mat, token);
                        }
                        finally
                        {
                            // 연산에 사용된 임시 Mat 해제
                            if (left.isTemporary) left.mat.Dispose();
                            if (right.isTemporary) right.mat.Dispose();
                        }

                        // 결과 스택에 푸시
                        stack.Push(new MatInfo(resultMat, isTemporary: true));
                    }
                }

                if (stack.Count != 1) throw new ArgumentException("Calculation failed: Stack not empty/balanced");

                var final = stack.Pop();
                using var finalMatF = final.mat; // 여기서 소유권 가져오고 자동 해제 예약

                // 3. 결과 포장 (32F -> 8U) 및 Saturation(0~255 자르기) 자동 적용
                int len = finalMatF.Width * finalMatF.Height;
                var buffer = ArrayPool<byte>.Shared.Rent(len);

                // MatType.CV_8UC1으로 생성만 함 (데이터 복사는 ConvertTo에서)
                using var finalMat8 = new Mat(finalMatF.Height, finalMatF.Width, MatType.CV_8UC1);

                // 32F -> 8U 변환 (자동으로 소수점 반올림 및 0~255 클램핑 됨)
                finalMatF.ConvertTo(finalMat8, MatType.CV_8UC1);

                // OpenCvSharp Mat 데이터를 배열로 복사
                System.Runtime.InteropServices.Marshal.Copy(finalMat8.Data, buffer, 0, len);

                // FrameData.Wrap이 버퍼 소유권을 가져간다고 가정 (아니라면 로직 확인 필요)
                return FrameData.Wrap(buffer, finalMat8.Width, finalMat8.Height, finalMat8.Width, len);
            }
            catch (Exception ex)
            {
                // 로그 처리 (실제 앱에서는 ILogger 등을 사용)
                System.Diagnostics.Debug.WriteLine($"Expression Error: {ex.Message}");
                return null;
            }
            finally
            {
                // [중요] 예외 발생 시 스택에 남아있는 모든 Mat 해제
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    if (item.isTemporary)
                    {
                        item.mat.Dispose();
                    }
                }
            }
        }

        private static Mat PerformOperation(Mat a, Mat b, string op)
        {
            if (a.Size() != b.Size()) throw new InvalidOperationException($"Size mismatch: {a.Size()} vs {b.Size()}");

            var dst = new Mat();
            try
            {
                switch (op)
                {
                    case "+": Cv2.Add(a, b, dst); break;
                    case "-": Cv2.Subtract(a, b, dst); break;
                    case "|": Cv2.Absdiff(a, b, dst); break;
                    case "*": Cv2.Multiply(a, b, dst); break;
                    case "/": Cv2.Divide(a, b, dst); break; // 0으로 나누기 방어는 OpenCV가 0으로 처리함
                    case "&": Cv2.AddWeighted(a, 0.5, b, 0.5, 0, dst); break;
                    default: throw new ArgumentException($"Unknown operator: {op}");
                }
                return dst;
            }
            catch
            {
                dst.Dispose(); // 연산 실패 시 생성한 dst 해제
                throw;
            }
        }

        // ParseToRPN 및 MatInfo는 기존과 동일하게 유지
        private static Queue<string> ParseToRPN(string expression)
        {
            var output = new Queue<string>();
            var stack = new Stack<string>();
            // 정수 뿐만 아니라 소수점도 고려하고 싶다면 pattern 수정 필요 (현재는 정수만)
            var matches = Regex.Matches(expression, @"(\d+)|([\+\-\*\/\|\&\(\)])");

            foreach (Match m in matches)
            {
                string token = m.Value;
                if (int.TryParse(token, out _)) output.Enqueue(token);
                else if (token == "(") stack.Push(token);
                else if (token == ")")
                {
                    while (stack.Count > 0 && stack.Peek() != "(") output.Enqueue(stack.Pop());
                    if (stack.Count > 0) stack.Pop();
                }
                else
                {
                    while (stack.Count > 0 && _precedence.ContainsKey(stack.Peek()) &&
                           _precedence[stack.Peek()] >= _precedence[token])
                        output.Enqueue(stack.Pop());
                    stack.Push(token);
                }
            }
            while (stack.Count > 0) output.Enqueue(stack.Pop());
            return output;
        }

        private record MatInfo(Mat mat, bool isTemporary);
    }
}