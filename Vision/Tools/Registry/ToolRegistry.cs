using System;
using System.Collections.Generic;
using System.Text;
using Vision.Tools.Abstractions;
using Vision.Tools.Source;
using Vision.Tools.Locate;
namespace Vision.Tools.Registry
{
    public static class ToolRegistry
    {
        private static readonly Dictionary<string, Func<IVisionTool>> _map = new();

        static ToolRegistry()
        {
            Register("LoadImage", () => new LoadImageTool());
            Register("Threshold", () => new ThresholdTool());
            Register("Blob", () => new BlobTool());
        }

        public static void Register(string name, Func<IVisionTool> factory) => _map[name] = factory;
        public static IEnumerable<string> Names => _map.Keys;

        public static IVisionTool Create(string name)
            => _map.TryGetValue(name, out var f)
                ? f()
                : throw new KeyNotFoundException($"Chưa đăng ký tool '{name}'");
    }
}
