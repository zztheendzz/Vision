using System;
using System.Collections.Generic;
using System.Text;

namespace Vision.Tools.Registry
{
    /// <summary>
    /// Đánh dấu một lớp là tool để registry tự tìm thấy. Id là khóa LƯU VÀO FILE: đặt rồi không đổi.
    /// Muốn đổi tên hiển thị thì sửa DisplayName, không sửa Id.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class VisionToolAttribute : Attribute
    {
        public string Id { get; }
        public string DisplayName { get; }

        /// <summary>Nhóm hiển thị trong toolbox (vd "Tiền xử lý", "Định vị"). Rỗng thì dùng nhóm theo ToolKind.</summary>
        public string Group { get; init; } = "";
        public string Description { get; init; } = "";

        public VisionToolAttribute(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }
}
