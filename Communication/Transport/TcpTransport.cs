using System.IO;
using System.Net.Sockets;
using Communication.Drivers;

namespace Communication.Transport
{
    /// <summary>TCP client có tách khung theo ký tự kết thúc. Mỗi lúc chỉ một yêu cầu (SemaphoreSlim).</summary>
    public sealed class TcpTransport : ITransport, IProbeable
    {
        private readonly TcpOptions _opt;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly List<byte> _buffer = new();

        public TcpTransport(TcpOptions options) => _opt = options;

        public bool IsConnected => _client?.Connected == true;

        /// <summary>Ký tự kết thúc đang dùng khi gửi / nhận (để nhật ký hiện rõ).</summary>
        public byte[] TxTerminator => _opt.TxTerminator;
        public byte[] RxTerminator => _opt.RxTerminator;
        public string Description => $"TCP {_opt.Host}:{_opt.Port}";

        /// <summary>Phía bên kia đóng hoặc đường truyền đứt thì ổ cắm báo "đọc được" mà không có dữ liệu.</summary>
        public Task<string?> ProbeAsync(CancellationToken ct = default)
        {
            var client = _client;
            if (client == null) return Task.FromResult<string?>(null);
            try
            {
                var sock = client.Client;
                if (sock.Poll(0, System.Net.Sockets.SelectMode.SelectError)) return Task.FromResult<string?>("lỗi đường truyền");
                if (sock.Poll(0, System.Net.Sockets.SelectMode.SelectRead) && sock.Available == 0)
                    return Task.FromResult<string?>("phía bên kia đã đóng kết nối");
                return Task.FromResult<string?>(null);
            }
            catch (Exception ex) { return Task.FromResult<string?>(ex.Message); }
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            Disconnect();
            var client = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(_opt.ConnectTimeoutMs);
                await client.ConnectAsync(_opt.Host, _opt.Port, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException($"Không nối được {_opt.Host}:{_opt.Port} trong {_opt.ConnectTimeoutMs} ms.");
            }
            catch
            {
                client.Dispose();
                throw;
            }
            try
            {
                // Giữ nhịp (keepalive): rút dây mạng thì sau ~11 giây hệ điều hành đóng ổ cắm, ProbeAsync mới thấy
                var sock = client.Client;
                sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
                sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);
                sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
            catch { /* hệ điều hành không hỗ trợ: bỏ qua */ }
            _client = client;
            _stream = client.GetStream();
            _buffer.Clear();
        }

        public void Disconnect()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _stream = null;
            _client = null;
            _buffer.Clear();
        }

        public async Task SendAsync(string text, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try { await WriteAsync(text, ct).ConfigureAwait(false); }
            finally { _lock.Release(); }
        }

        public async Task<string> RequestAsync(string text, int timeoutMs, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                DiscardStale();
                await WriteAsync(text, ct).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                try { return await ReadFrameAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"Không có trả lời từ {Description} trong {timeoutMs} ms.");
                }
            }
            finally { _lock.Release(); }
        }

        private NetworkStream RequireStream()
            => _stream ?? throw new InvalidOperationException("Chưa kết nối " + Description);

        private async Task WriteAsync(string text, CancellationToken ct)
        {
            var stream = RequireStream();
            byte[] body = _opt.Encoding.GetBytes(text);
            byte[] frame = new byte[body.Length + _opt.TxTerminator.Length];
            body.CopyTo(frame, 0);
            _opt.TxTerminator.CopyTo(frame, body.Length);
            await stream.WriteAsync(frame, ct).ConfigureAwait(false);
            RawTrace.Emit(_opt.Host + ":" + _opt.Port, "TX", frame);
        }

        // Bỏ dữ liệu cũ còn sót để không đọc nhầm trả lời của lệnh trước
        private void DiscardStale()
        {
            _buffer.Clear();
            var stream = _stream;
            if (stream == null) return;
            var junk = new byte[1024];
            while (stream.DataAvailable)
            {
                int got = stream.Read(junk, 0, junk.Length);
                RawTrace.Emit(_opt.Host + ":" + _opt.Port, "RX (bỏ, dữ liệu cũ)", junk.AsSpan(0, got));
            }
        }

        private async Task<string> ReadFrameAsync(CancellationToken ct)
        {
            var stream = RequireStream();
            var term = _opt.RxTerminator;
            var chunk = new byte[512];
            while (true)
            {
                int idx = term.Length == 0 ? (_buffer.Count > 0 ? _buffer.Count : -1) : IndexOf(term);
                if (idx >= 0)
                {
                    string s = _opt.Encoding.GetString(_buffer.GetRange(0, idx).ToArray());
                    _buffer.RemoveRange(0, Math.Min(_buffer.Count, idx + term.Length));
                    return s;
                }
                int n = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
                if (n == 0) throw new IOException("Đầu kia đã đóng kết nối.");
                RawTrace.Emit(_opt.Host + ":" + _opt.Port, "RX", chunk.AsSpan(0, n));
                _buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            }
        }

        private int IndexOf(byte[] term)
        {
            for (int i = 0; i + term.Length <= _buffer.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < term.Length && match; j++) match = _buffer[i + j] == term[j];
                if (match) return i;
            }
            return -1;
        }

        public void Dispose() { Disconnect(); _lock.Dispose(); }
    }
}
