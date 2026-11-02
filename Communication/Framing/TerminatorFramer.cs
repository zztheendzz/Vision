using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Framing
{
    public class TerminatorFramer:IMessageFramer
    {
        private readonly byte[] _terminator;
        private readonly int _maxBufferSize;
        private readonly List<byte> _buffer = new List<byte>();

        public TerminatorFramer(byte[] terminator, int maxBufferSize = 4096)
        {
            _terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
            _maxBufferSize = maxBufferSize;
        }

        public byte[] Frame(byte[] message)
        {
            var framed = new byte[message.Length + _terminator.Length];
            Buffer.BlockCopy(message, 0, framed, 0, message.Length);
            Buffer.BlockCopy(_terminator, 0, framed, message.Length, _terminator.Length);
            return framed;
        }

        public List<byte[]> Push(byte[] data)
        {
            var messages = new List<byte[]>();
            _buffer.AddRange(data);

            if (_buffer.Count > _maxBufferSize)
            {
                _buffer.Clear();
                throw new InvalidOperationException("Buffer overflow: Vượt quá kích thước bộ đệm cho phép.");
            }

            // Logic cắt gói dựa trên _terminator có thể được xử lý tại đây
            // Trả về danh sách các gói hoàn chỉnh và giữ lại phần dư trong _buffer.

            return messages;
        }
    }
}
