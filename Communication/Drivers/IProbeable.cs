using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Drivers
{
    /// <summary>
    /// Thiết bị tự kiểm tra còn sống không. "Đã nối" chỉ là cờ trong chương trình: rút dây mạng hay tắt nguồn thì cờ vẫn đúng,
    /// nên DeviceManager gọi ProbeAsync định kỳ (một thao tác đọc nhẹ) để biết thật.
    /// </summary>
    public interface IProbeable
    {
        /// <summary>Trả về null nếu còn sống, hoặc lý do nếu không trả lời. Đang bận việc khác thì coi là còn sống.</summary>
        Task<string?> ProbeAsync(CancellationToken ct = default);
    }
}
