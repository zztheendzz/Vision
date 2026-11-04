using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Configuration
{
    /// <summary>Loại thiết bị. Chỉ để phân loại và hiển thị, cách kết nối giống nhau (TCP).</summary>
    public enum DeviceType
    {
        Camera,
        Scanner,
        PLC,
        Robot,
        Fixture,
        PC,
        Other
    }



}
