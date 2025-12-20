using AvaloniaApp.Core.Models;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Infrastructure
{
    public class WorkspaceService : IDisposable
    {
        private readonly object _gate = new();
        private Workspace? _current;

        public Workspace? Current
        {
            get { lock (_gate) { return _current; } }
        }
        public event Action<Workspace?>? Changed;
        public event Action? Updated;
        public void Replace(Workspace? replaceWorkspace)
        {
            Workspace? old;
            lock (_gate)
            {
                old= _current;
                _current= replaceWorkspace;
            }
            old?.Dispose();
            Changed?.Invoke(replaceWorkspace);   
        }
        public void Update(Action<Workspace> updateWorkspace)
        {
            Workspace? ws;
            lock (_gate)
            {
                ws = _current;
            }
            if (ws is null) return;
            updateWorkspace(ws);
            Updated?.Invoke();

        }
        public void SetEntireFrame(FrameData? frame) => Update(ws => ws.SetEntireFrameData(frame));
        public void SetCropFrameDatas(IEnumerable<FrameData> frames) => Update(ws => ws.SetCropFrameDatas(frames));
        public void SetStitchFrame(FrameData? frame) => Update(ws => ws.SetStitchFrameData(frame)); 
        public void RemoveRegionData(RegionData region) => Update(ws => ws.RemoveRegionData(region));
        public void Clear() => Replace(null);
        public void Dispose() => Clear();
    }
}
