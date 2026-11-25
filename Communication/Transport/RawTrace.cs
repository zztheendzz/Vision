using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Transport
{
    /// <summary>
    /// Vết byte thô gửi / nhận của khuôn test, để DEBUG xem khuôn thực sự gửi những ký tự gì (kể cả ký tự kết thúc).
    /// Ai quan tâm thì đăng ký Frame; không có ai đăng ký thì gần như không tốn gì. Sau khi debug xong có thể xóa cả lớp này.
    /// Sự kiện có thể bắn từ thread nền.
    /// </summary>
    public static class RawTrace
    {
        /// <summary>(nguồn, hướng "TX"/"RX", byte thô).</summary>
        public static event Action<string, string, byte[]>? Frame;

        public static void Emit(string source, string direction, ReadOnlySpan<byte> data)
        {
            var handler = Frame;
            if (handler == null || data.Length == 0) return;
            handler(source, direction, data.ToArray());
        }

        /// <summary>Chữ thấy được: ký tự in được giữ nguyên, CR/LF/NUL/ký tự điều khiển hiện thành &lt;CR&gt; &lt;LF&gt; &lt;NUL&gt; &lt;0xHH&gt;.</summary>
        public static string Visible(ReadOnlySpan<byte> data)
        {
            var sb = new StringBuilder();
            foreach (byte b in data)
            {
                if (b == 13) sb.Append("<CR>");
                else if (b == 10) sb.Append("<LF>");
                else if (b == 0) sb.Append("<NUL>");
                else if (b == 9) sb.Append("<TAB>");
                else if (b >= 32 && b < 127) sb.Append((char)b);
                else sb.Append("<0x").Append(b.ToString("X2")).Append('>');
            }
            return sb.ToString();
        }

        public static string Hex(ReadOnlySpan<byte> data)
        {
            var sb = new StringBuilder();
            foreach (byte b in data) sb.Append(b.ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }

        /// <summary>Dạng một dòng nhật ký: chữ thấy được, rồi mã hex trong ngoặc vuông.</summary>
        public static string Describe(ReadOnlySpan<byte> data) => $"{Visible(data)}   [{Hex(data)}]";
    }
}
