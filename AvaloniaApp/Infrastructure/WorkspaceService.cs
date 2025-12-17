using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public void Replace(Workspace? next)
        {
            Workspace? old;
            lock (_gate)
            {
                old= _current;
                _current= next;
            }
            old?.Dispose();
            Changed?.Invoke(next);   
        }
        public void Clear() => Replace(null);
        public void Dispose() => Clear();
    }
}
