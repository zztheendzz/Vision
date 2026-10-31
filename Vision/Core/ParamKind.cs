using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Core
{
    public enum ParamKind
    {
        /// <summary>Kiểu số (int, double, float, v.v.)</summary>
        Number = 0,

        /// <summary>Kiểu chuỗi văn bản</summary>
        String = 1,

        /// <summary>Kiểu bật/tắt (Boolean)</summary>
        Boolean = 2,

        /// <summary>Kiểu lựa chọn danh sách (Enum/Choice)</summary>
        Choice = 3,

        /// <summary>Kiểu đường dẫn tệp tin (File Path)</summary>
        FilePath = 4,

        /// <summary>Kiểu vùng quan tâm trên ảnh (ROI)</summary>
        Roi = 5
    }
}
