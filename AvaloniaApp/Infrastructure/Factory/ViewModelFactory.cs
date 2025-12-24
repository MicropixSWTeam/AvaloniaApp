using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure.Factory
{
    public class ViewModelFactory
    {
        private readonly IServiceProvider _serviceProvider;
        public ViewModelFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        // Transient로 등록된 VM을 새로 생성해서 반환
        public T Create<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();
    }
}
