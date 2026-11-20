using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Text.Json;
namespace Vision.Core
{
    /// <summary>
    /// Khai báo tham số của tool: tên, kiểu, mặc định, khoảng hợp lệ.
    /// UI dùng để sinh panel, node dùng để kiểm tra/ép kiểu giá trị truyền vào.
    /// </summary>
    public record ParamDef(
        string Name,
        Type Type,
        object Default,
        double? Min = null,
        double? Max = null,
        string? Description = null,
        string? ChoicesKey = null)
    {
        /// <summary>
        /// Ép giá trị về đúng kiểu và kiểm tra khoảng. Nhận cả JsonElement (khi đọc file).
        /// Ném ArgumentException nếu không hợp lệ.
        /// </summary>
        public object Coerce(object? value)
        {
            if (value is null)
                throw new ArgumentException($"Tham số '{Name}' không được null");

            try
            {
                if (value is JsonElement je) value = FromJson(je);

                if (!Type.IsInstanceOfType(value))
                {
                    if (Type.IsEnum)
                        value = value is string s ? Enum.Parse(Type, s, ignoreCase: true)
                                                  : Enum.ToObject(Type, value);
                    else
                        value = Convert.ChangeType(value, Type, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException(
                    $"Tham số '{Name}': không ép được '{value}' về {Type.Name} ({ex.Message})");
            }

            if ((Min.HasValue || Max.HasValue) && value is IConvertible)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (Min.HasValue && d < Min.Value)
                    throw new ArgumentException($"Tham số '{Name}' = {d} nhỏ hơn Min {Min}");
                if (Max.HasValue && d > Max.Value)
                    throw new ArgumentException($"Tham số '{Name}' = {d} lớn hơn Max {Max}");
            }
            return value;
        }

        private object FromJson(JsonElement je)
        {
            if (Type == typeof(double)) return je.GetDouble();
            if (Type == typeof(int)) return je.GetInt32();
            if (Type == typeof(long)) return je.GetInt64();
            if (Type == typeof(bool)) return je.GetBoolean();
            if (Type == typeof(string)) return je.GetString() ?? "";
            if (Type.IsEnum) return je.GetString() ?? throw new ArgumentException("enum null");
            throw new NotSupportedException($"Chưa hỗ trợ đọc JSON cho kiểu {Type.Name}");
        }
    }

}
