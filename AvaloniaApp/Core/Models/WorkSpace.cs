using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Models
{
    public class WorkSpace
    {
        public FrameData? EntireFrameData {  get; set; }
        public List<FrameData> CropFrameDatas { get; set; } = new();

    }
}
