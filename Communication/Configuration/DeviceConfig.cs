using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Communication.Configuration
{
    /// <summary>
    /// Cấu hình của MỘT thiết bị. Chỉ chứa dữ liệu (không có logic kết nối), để lưu ra file JSON.
    /// </summary>
    public class DeviceConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Thiết bị mới";
        public DeviceType Type { get; set; } = DeviceType.Other;

        public string IpAddress { get; set; } = "192.168.1.100";
        public int Port { get; set; } = 5000;

        /// <summary>Thời gian chờ khi kết nối, tính bằng mili giây.</summary>
        public int TimeoutMs { get; set; } = 3000;

        /// <summary>Tự kết nối khi mở phần mềm.</summary>
        public bool AutoConnect { get; set; }

        /// <summary>Mã hóa chữ khi gửi: "ASCII" hoặc "UTF-8".</summary>
        public string TextEncoding { get; set; } = "ASCII";

        public TerminatorKind Terminator { get; set; } = TerminatorKind.CRLF;
        public SerialSettings SerialConfig { get; set; }
        /// <summary>
        /// Chỉ dùng khi Terminator = Custom. Gõ ký tự thường, hoặc mã đặc biệt:
        /// \r \n \t \0 và \xHH (ví dụ \x03 là byte 03).
        /// </summary>
        public string CustomTerminator { get; set; } = "";

        public Encoding GetEncoding()
            => TextEncoding == "UTF-8" ? Encoding.UTF8 : Encoding.ASCII;

        /// <summary>Các byte của ký tự kết thúc gói tin (rỗng nếu None).</summary>
        public byte[] GetTerminatorBytes()
        {
            string text = Terminator switch
            {
                TerminatorKind.None => "",
                TerminatorKind.CR => "\r",
                TerminatorKind.LF => "\n",
                TerminatorKind.CRLF => "\r\n",
                _ => Unescape(CustomTerminator)
            };

            // Latin1: mỗi ký tự ứng với đúng 1 byte (0-255), nên \xHH ra đúng giá trị đã gõ
            return Encoding.Latin1.GetBytes(text);
        }

        /// <summary>Đổi các mã \r \n \t \0 \\ \xHH thành ký tự thật.</summary>
        private static string Unescape(string s)
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
                    case 'r': sb.Append('\r'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'x':
                        // cần đủ 2 ký tự hex phía sau
                        if (i + 2 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 2), NumberStyles.HexNumber,
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
    }
}
