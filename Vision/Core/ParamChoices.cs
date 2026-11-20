using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Core
{
    /// <summary>
    /// Danh sách lựa chọn động cho tham số kiểu chuỗi (ParamDef.ChoicesKey). Nơi chứa dữ liệu (cấu hình thiết bị...) đăng ký
    /// một hàm trả về danh sách; bảng thuộc tính đọc lại mỗi lần chọn node nên luôn thấy tên mới.
    /// </summary>
    public static class ParamChoices
    {
        public const string Cameras = "Cameras";

        private static readonly Dictionary<string, Func<IReadOnlyList<string>>> Providers = new();

        public static void Register(string key, Func<IReadOnlyList<string>> provider)
        {
            lock (Providers) Providers[key] = provider;
        }

        public static IReadOnlyList<string> Get(string key)
        {
            Func<IReadOnlyList<string>>? p;
            lock (Providers) Providers.TryGetValue(key, out p);
            try { return p?.Invoke() ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }
    }
}
