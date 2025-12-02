using AvaloniaApp.Core.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Interfaces
{
    public interface IPopup
    {
        public string Title { get; set; }   
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
