using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Enums
{
    public enum Distance
    {
        Ten,
        Twenty,
        Thirty,
        Forty,
        Over,
    }
    public enum ImageOperationType
    {
        Add,        // A + B (Saturate)
        Subtract,   // A - B (Saturate 0)
        Difference, // |A - B| (절대값 차이)
        Multiply,   // A * B (Scale 필요할 수 있음)
        Divide,     // A / B (0 처리 필요)
        Average     // (A + B) / 2
    }
}
