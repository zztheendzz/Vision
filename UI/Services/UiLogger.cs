using System;
using System.Collections.Generic;
using System.Text;
using Vision.Services;

namespace UI.Services
{
    public sealed class UiLogger : IVisionLogger
    {
        private readonly Action<string> _sink;
        public UiLogger(Action<string> sink) => _sink = sink;

        public void Info(string message) => _sink(message);
        public void Warn(string message) => _sink("[cảnh báo] " + message);
        public void Error(string message, Exception? ex = null)
            => _sink("[lỗi] " + message + (ex == null ? "" : $" ({ex.Message})"));
    }
}
