using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Runtime
{
    /// <summary>Kết luận cuối của một chu kỳ.</summary>
    public enum RunVerdict
    {
        /// <summary>Mọi node chạy xong và đều có kết quả.</summary>
        Ok,

        /// <summary>Không đạt: có node chạy tốt nhưng không có kết quả (ví dụ không tìm thấy vật). Đây là tình huống bình thường.</summary>
        Ng,

        /// <summary>Lỗi: lấy ảnh hỏng, node lỗi cấu hình hoặc lỗi hệ thống. Cần người kiểm tra.</summary>
        Error
    }
}
