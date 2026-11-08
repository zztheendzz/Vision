using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Core
{
    public enum ResultStatus
    {
        /// <summary>Có kết quả hợp lệ.</summary>
        Ok = 1,

        /// <summary>Chạy tốt nhưng không có gì để trả (không thấy vật, điểm số thấp). Là tình huống bình thường.</summary>
        NoResult = 2,

        /// <summary>Cấu hình sai: thiếu input, tham số sai, thiếu file.</summary>
        ConfigError = 3,

        /// <summary>Lỗi hệ thống: exception, lỗi Halcon, mất kết nối.</summary>
        SystemError = 4
    }
}
