using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Core
{
    /// <summary>
    /// Khai báo tĩnh một cổng (tên, kiểu dữ liệu, bắt buộc hay không). 
    /// Tool khai báo, node tạo port thật từ đây.
    /// </summary>
    public record PortDef(
        string Name,
        Type Type,
        bool Required = true,
        string? Description = null)
    {
        /// <summary>
        /// Kiểm tra xem giá trị truyền vào có khớp kiểu dữ liệu của cổng hay không.
        /// </summary>
        public bool IsValidValue(object? value)
        {
            if (value is null)
                return !Required;

            return Type.IsInstanceOfType(value);
        }

    }
}