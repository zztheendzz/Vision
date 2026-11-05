using System.Globalization;
using System.Text;

namespace Communication.Configuration
{
    /// <summary>
    /// Chữ có mã thoát: người dùng gõ \r \n \t \0 \\ hoặc \xHH (ví dụ \x03) và nhận ký tự thật.
    /// Dùng cho ký tự kết thúc gói tin.
    /// </summary>
    public static class Escapes
    {
        /// <summary>Đổi các mã thoát thành ký tự thật. Mã không nhận ra được giữ nguyên.</summary>
        public static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i == s.Length - 1)
                {
                    sb.Append(c);
                    continue;
                }

                char next = s[++i];
                switch (next)
                {
                    case 'r':  sb.Append('\r'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 't':  sb.Append('\t'); break;
                    case '0':  sb.Append('\0'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'x':
                        if (i + 2 <= s.Length - 1 &&
                            int.TryParse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber,
                                         CultureInfo.InvariantCulture, out int value))
                        {
                            sb.Append((char)value);
                            i += 2;
                        }
                        else
                        {
                            sb.Append("\\x");
                        }
                        break;
                    default:
                        sb.Append('\\').Append(next);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Các byte của chuỗi sau khi đổi mã thoát (mỗi ký tự đúng 1 byte, 0–255).</summary>
        public static byte[] ToBytes(string escaped) => Encoding.Latin1.GetBytes(Unescape(escaped));

        /// <summary>Hiển thị byte dạng "0D 0A"; rỗng thì "(không có)".</summary>
        public static string ToHex(string escaped)
        {
            byte[] bytes = ToBytes(escaped);
            return bytes.Length == 0 ? "(không có)" : BitConverter.ToString(bytes).Replace("-", " ");
        }
    }
}
