using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Services
{
    public interface IVisionLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? ex = null);
    }

    public sealed class NullVisionLogger : IVisionLogger
    {
        public static readonly NullVisionLogger Instance = new();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }
}
