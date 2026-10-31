using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Core
{
    public enum RunMode
    {
        DryRun = 0,

        /// <summary>Chạy thật (sản xuất).</summary>
        Live = 1,

        /// <summary>Đang dạy mẫu: không tác động ra ngoài.</summary>
        Teach = 2
    }
}
