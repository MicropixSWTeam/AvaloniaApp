using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure.Service
{
    public class DialogService
    {
        public async Task<ButtonResult> ShowMessageAsync(string title, string message, ButtonEnum buttons = ButtonEnum.Ok)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, buttons);

            return await box.ShowAsync();
        }
    }
}

