using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCvSharp;
using AvaloniaApp.Core.Models;

namespace AvaloniaApp.Core.Utils
{
    // [최적화] 매 프레임 파싱하지 않도록 미리 컴파일된 식을 저장
    public class CompiledExpression
    {
        public Queue<string> RpnQueue { get; }
        public CompiledExpression(Queue<string> rpnQueue)
        {
            RpnQueue = rpnQueue;
        }
    }

    public static class ImageCalculator
    {
        private static readonly Dictionary<string, int> _precedence = new()
        {
            { "(", 0 }, { "+", 1 }, { "-", 1 }, { "|", 1 },
            { "*", 2 }, { "/", 2 }, { "&", 2 } // &는 평균(Average)
        };

        // 문자열 파싱과 연산 분리 (편의용 오버로드)
        public static FrameData? Evaluate(string expression, Func<int, FrameData?> frameProvider)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;
            if (!TryParse(expression, out var compiled, out _)) return null;

            return Evaluate(compiled!, frameProvider);
        }

        // [핵심 로직] 컴파일된 식(RPN)을 받아 실제 연산 수행
        public static FrameData? Evaluate(CompiledExpression compiled, Func<int, FrameData?> frameProvider)
        {
            if (compiled == null || compiled.RpnQueue.Count == 0) return null;

            var tokens = compiled.RpnQueue.ToArray();
            var stack = new Stack<MatInfo>();

            try
            {
                foreach (var token in tokens)
                {
                    // 1. 숫자인 경우 (이미지 인덱스 또는 상수)
                    if (double.TryParse(token, out double val))
                    {
                        bool isImageLoaded = false;

                        // 정수라면 파장(Wavelength)으로 간주하고 이미지 로드 시도
                        if (val == (int)val)
                        {
                            try
                            {
                                // 이미지가 존재하면 로드 (FrameData 복사본 생성 방지 위해 using 처리 주의)
                                var frame = frameProvider((int)val);
                                if (frame != null)
                                {
                                    var matF = new Mat();
                                    // 8비트 이미지를 32비트 실수(Float)로 변환하여 정밀도 유지
                                    using var mat8 = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
                                    mat8.ConvertTo(matF, MatType.CV_32FC1);

                                    stack.Push(new MatInfo(matF, isTemporary: true));
                                    isImageLoaded = true;

                                    // frameProvider가 복사본을 준 경우라면 여기서 Dispose 해야 함 (상황에 따라 다름)
                                    // 보통 Provider가 "사용 후 버려도 되는" Frame을 준다면 Dispose 호출.
                                    frame.Dispose();
                                }
                            }
                            catch
                            {
                                // 로드 실패 시 무시하고 상수로 처리
                            }
                        }

                        // 이미지가 아니거나 로드에 실패했다면 -> 상수(Scalar)로 취급 (예: "/ 3")
                        if (!isImageLoaded)
                        {
                            // 1x1 크기의 Scalar Mat 생성
                            var scalarMat = new Mat(1, 1, MatType.CV_32FC1, new Scalar(val));
                            stack.Push(new MatInfo(scalarMat, isTemporary: true));
                        }
                    }
                    // 2. 연산자인 경우 (+, -, *, / ...)
                    else
                    {
                        if (stack.Count < 2) throw new ArgumentException("Not enough operands");

                        var right = stack.Pop();
                        var left = stack.Pop();

                        Mat resultMat = null;
                        try
                        {
                            resultMat = PerformOperation(left.mat, right.mat, token);
                        }
                        finally
                        {
                            // 연산에 사용된 임시 매트릭스 해제
                            if (left.isTemporary) left.mat.Dispose();
                            if (right.isTemporary) right.mat.Dispose();
                        }
                        stack.Push(new MatInfo(resultMat, isTemporary: true));
                    }
                }

                if (stack.Count != 1) throw new ArgumentException("Calculation failed: Stack not balanced");

                // 최종 결과 처리
                var final = stack.Pop();
                using var finalMatF = final.mat;

                // 결과가 1x1 Scalar인 경우 (예: "3 + 5") -> 예외 처리 혹은 단색 이미지 생성
                // 여기서는 전체 크기로 확장하거나, 일반적으론 이미지 크기가 됨
                if (finalMatF.Width == 1 && finalMatF.Height == 1)
                {
                    // 결과가 숫자 하나라면 처리가 애매하지만, 일단 null 리턴 혹은 무시
                    return null;
                }

                // 32F -> 8U 변환 (Saturate Cast 자동 적용: 0~255 범위로 잘림)
                int len = finalMatF.Width * finalMatF.Height;
                var buffer = ArrayPool<byte>.Shared.Rent(len);
                using var finalMat8 = new Mat(finalMatF.Height, finalMatF.Width, MatType.CV_8UC1);

                finalMatF.ConvertTo(finalMat8, MatType.CV_8UC1);

                // 버퍼 복사
                System.Runtime.InteropServices.Marshal.Copy(finalMat8.Data, buffer, 0, len);

                return FrameData.Wrap(buffer, finalMat8.Width, finalMat8.Height, finalMat8.Width, len);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Eval Error] {ex.Message}");
                return null;
            }
            finally
            {
                // 스택에 남은 잔여물 정리
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    if (item.isTemporary) item.mat.Dispose();
                }
            }
        }

        // [구조 개선] Mat vs Mat, Mat vs Scalar 모두 처리 가능하도록 개선
        private static Mat PerformOperation(Mat a, Mat b, string op)
        {
            // 1x1 Mat인지 확인 (상수 여부 판별)
            bool aIsScalar = (a.Rows == 1 && a.Cols == 1);
            bool bIsScalar = (b.Rows == 1 && b.Cols == 1);

            // 둘 다 이미지가 아니고, 크기가 다르면 연산 불가 (Scalar 브로드캐스팅 제외)
            if (!aIsScalar && !bIsScalar && a.Size() != b.Size())
                throw new InvalidOperationException($"Size mismatch: {a.Size()} vs {b.Size()}");

            var dst = new Mat();
            try
            {
                // OpenCV는 Mat과 Scalar 간 연산을 직접 지원하지 않는 메서드도 있으므로 분기 처리
                // 하지만 MatType이 CV_32F로 통일되어 있으므로, Scalar도 1x1 Mat으로 취급하여 연산 가능
                // 단, 크기가 다른 두 Mat의 연산은 OpenCV 기본 함수에서 에러가 발생할 수 있음 -> Scalar 변환 필요

                // Helper: 1x1 Mat에서 값 추출
                double GetScalar(Mat m) => m.At<float>(0, 0);

                if (aIsScalar && !bIsScalar) // Scalar op Image
                {
                    double s = GetScalar(a);
                    switch (op)
                    {
                        case "+": Cv2.Add(new Scalar(s), b, dst); break;
                        case "-": Cv2.Subtract(new Scalar(s), b, dst); break; // s - b
                        case "*": Cv2.Multiply(new Scalar(s), b, dst); break;
                        case "/": Cv2.Divide(new Scalar(s), b, dst); break;   // s / b
                        case "|": Cv2.Absdiff(new Scalar(s), b, dst); break;
                        default: throw new ArgumentException($"Unknown operator for Scalar-Image: {op}");
                    }
                }
                else if (!aIsScalar && bIsScalar) // Image op Scalar (가장 흔한 케이스, 예: Img / 3)
                {
                    double s = GetScalar(b);
                    switch (op)
                    {
                        case "+": Cv2.Add(a, new Scalar(s), dst); break;
                        case "-": Cv2.Subtract(a, new Scalar(s), dst); break;
                        case "*": Cv2.Multiply(a, new Scalar(s), dst); break;
                        case "/": Cv2.Divide(a, new Scalar(s), dst); break;
                        case "|": Cv2.Absdiff(a, new Scalar(s), dst); break;
                        case "&": Cv2.AddWeighted(a, 0.5, a, 0.5, 0, dst); break; // Image & Scalar? (의미 모호함, 무시하거나 a 리턴)
                        default: throw new ArgumentException($"Unknown operator for Image-Scalar: {op}");
                    }
                }
                else // Image op Image (또는 Scalar op Scalar)
                {
                    switch (op)
                    {
                        case "+": Cv2.Add(a, b, dst); break;
                        case "-": Cv2.Subtract(a, b, dst); break;
                        case "|": Cv2.Absdiff(a, b, dst); break;
                        case "*": Cv2.Multiply(a, b, dst); break;
                        case "/": Cv2.Divide(a, b, dst); break;
                        case "&": Cv2.AddWeighted(a, 0.5, b, 0.5, 0, dst); break; // 평균
                        default: throw new ArgumentException($"Unknown operator: {op}");
                    }
                }

                return dst;
            }
            catch
            {
                dst.Dispose();
                throw;
            }
        }

        // 검증 및 파싱 로직
        public static bool TryParse(string expression, out CompiledExpression? result, out string? errorMessage)
        {
            result = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(expression))
            {
                errorMessage = "Expression is empty";
                return false;
            }

            try
            {
                var rpnQueue = ParseToRPN(expression);
                if (rpnQueue.Count == 0)
                {
                    errorMessage = "No valid tokens found";
                    return false;
                }
                result = new CompiledExpression(rpnQueue);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        // 중위 표기법 -> 후위 표기법(RPN) 변환 (Shunting-yard algorithm)
        private static Queue<string> ParseToRPN(string expression)
        {
            var output = new Queue<string>();
            var stack = new Stack<string>();
            // 정수, 실수(소수점 포함), 연산자, 괄호 인식 정규식
            var matches = Regex.Matches(expression, @"(\d+(\.\d+)?)|([\+\-\*\/\|\&\(\)])");

            foreach (Match m in matches)
            {
                string token = m.Value;
                if (double.TryParse(token, out _)) // 숫자면 큐에 추가
                {
                    output.Enqueue(token);
                }
                else if (token == "(")
                {
                    stack.Push(token);
                }
                else if (token == ")")
                {
                    bool foundParen = false;
                    while (stack.Count > 0)
                    {
                        if (stack.Peek() == "(")
                        {
                            foundParen = true;
                            stack.Pop();
                            break;
                        }
                        output.Enqueue(stack.Pop());
                    }
                    if (!foundParen) throw new ArgumentException("Mismatched parentheses");
                }
                else // 연산자
                {
                    while (stack.Count > 0 && _precedence.ContainsKey(stack.Peek()) &&
                           _precedence[stack.Peek()] >= _precedence[token])
                    {
                        output.Enqueue(stack.Pop());
                    }
                    stack.Push(token);
                }
            }
            while (stack.Count > 0)
            {
                var top = stack.Pop();
                if (top == "(") throw new ArgumentException("Mismatched parentheses");
                output.Enqueue(top);
            }
            return output;
        }

        // Stack 관리를 위한 내부 레코드
        private record MatInfo(Mat mat, bool isTemporary);
    }
}