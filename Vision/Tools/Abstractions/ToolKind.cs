using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Tools.Abstractions
{
    /*
    Source	    Validate: không được có input port bắt buộc
    Pure	    Về sau (chạy lại một phần) có thể cache kết quả an toàn vì cùng input thì cùng output
    Stateful	Executor bắt buộc gọi Initialize trước khi chạy
    Action	    Chỉ thực thi khi RunMode.Live, không bao giờ được cache, UI tô màu cảnh báo
     */
    public enum ToolKind
    {
        /// <summary>Chưa gán. ToolDescriptor từ chối đăng ký.</summary>
        Unknown = 0,

        /// <summary>Không có input bắt buộc, đưa dữ liệu từ ngoài vào (đọc ảnh, camera).</summary>
        Source,

        /// <summary>Cùng input cho cùng output, không giữ trạng thái, không tác dụng phụ.</summary>
        Pure,

        /// <summary>Giữ tài nguyên nạp một lần (shape model, hiệu chuẩn), không tác động ra ngoài.</summary>
        Stateful,

        /// <summary>Có tác dụng phụ lên thế giới bên ngoài (gửi robot, ghi file). Chỉ chạy thật khi Live.</summary>
        Action
    }
}
