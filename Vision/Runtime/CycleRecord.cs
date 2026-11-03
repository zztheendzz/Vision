using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Execution;
using Vision.Tools;

namespace Vision.Runtime
{
    /// <summary>
    /// Bản ghi một chu kỳ chạy: dùng để hiển thị bảng kết quả, thống kê, ghi log và truy vết khi có sự cố.
    /// Chỉ chứa chữ và số (không giữ ảnh hay object Halcon) nên giữ lại bao lâu cũng không rò rỉ bộ nhớ.
    /// </summary>
    public sealed record CycleRecord(
        long Id,                                      // số thứ tự chu kỳ
        DateTime StartedAt,                           // giờ bắt đầu
        long DurationMs,                              // tổng thời gian chạy (ms)
        RunVerdict Verdict,                           // kết luận: Ok / Ng / Error
        string Message,                               // lý do ngắn gọn
        string? ProductCode,                          // mã sản phẩm (nếu có, ví dụ từ scanner)
        RunMode Mode,                                 // chạy thử hay chạy thật
        IReadOnlyDictionary<string, string> Values,   // giá trị các node ghi ra, dạng "Blob.Count" = "3"
        IReadOnlyList<string> NodeLines);             // mỗi node một dòng: "Threshold: Ok (12 ms)"
}
