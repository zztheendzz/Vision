using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Communication.Fixtures
{
    /// <summary>
    /// Máy chủ TCP nghe ở "Cổng nhận": khuôn test nối vào đây để gửi kết quả slot (B_01_02, P_01_02...) chủ động.
    /// Mỗi kết nối được tách dòng theo ký tự kết thúc RX; mỗi dòng báo qua <see cref="LineReceived"/>.
    /// </summary>
    public sealed class FixtureListener : IDisposable
    {
        private readonly byte[] _terminator;
        private readonly CancellationTokenSource _cts = new();
        private TcpListener? _listener;

        public FixtureListener(int port, byte[] rxTerminator)
        {
            Port = port;
            _terminator = rxTerminator;
        }

        public int Port { get; }

        /// <summary>(địa chỉ đầu kia, nội dung dòng).</summary>
        public event Action<string, string>? LineReceived;

        /// <summary>Thông báo trạng thái: có khuôn nối vào, ngắt, lỗi đọc.</summary>
        public event Action<string>? Info;

        /// <summary>Bắt đầu nghe. Ném lỗi nếu cổng đang bị chiếm.</summary>
        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _ = AcceptLoopAsync(_cts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex) { Info?.Invoke("Lỗi nhận kết nối: " + ex.Message); return; }

                _ = ClientLoopAsync(client, ct);
            }
        }

        private async Task ClientLoopAsync(TcpClient client, CancellationToken ct)
        {
            string remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
            Info?.Invoke($"{remote} đã nối vào cổng {Port}");
            var buffer = new List<byte>();
            var chunk = new byte[1024];
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    while (!ct.IsCancellationRequested)
                    {
                        int n = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
                        if (n == 0) break;
                        Communication.Transport.RawTrace.Emit("Cổng nhận " + remote, "RX", chunk.AsSpan(0, n));
                        buffer.AddRange(chunk.AsSpan(0, n).ToArray());
                        foreach (string line in TakeLines(buffer))
                            if (line.Length > 0) LineReceived?.Invoke(remote, line);
                    }
                }
                Info?.Invoke($"{remote} đã ngắt");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Info?.Invoke($"{remote}: lỗi đọc: {ex.Message}"); }
        }

        /// <summary>Lấy các dòng đầy đủ ra khỏi bộ đệm. Không có ký tự kết thúc thì mỗi lần nhận được coi là một dòng.</summary>
        private List<string> TakeLines(List<byte> buffer)
        {
            var lines = new List<string>();
            if (_terminator.Length == 0)
            {
                lines.Add(Encoding.ASCII.GetString(buffer.ToArray()));
                buffer.Clear();
                return lines;
            }

            while (true)
            {
                int idx = IndexOf(buffer, _terminator);
                if (idx < 0) break;
                lines.Add(Encoding.ASCII.GetString(buffer.GetRange(0, idx).ToArray()).Trim());
                buffer.RemoveRange(0, idx + _terminator.Length);
            }
            return lines;
        }

        private static int IndexOf(List<byte> buffer, byte[] term)
        {
            for (int i = 0; i + term.Length <= buffer.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < term.Length && match; j++) match = buffer[i + j] == term[j];
                if (match) return i;
            }
            return -1;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
