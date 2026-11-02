using Communication.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace Communication.Connection
{
    /// <summary>
    /// Kết nối TCP tới một thiết bị. Bản đầu tiên chỉ: kết nối, ngắt, kiểm tra còn sống không, gửi chữ.
    /// (Nhận dữ liệu từ thiết bị sẽ thêm sau, khi cần cho scanner/robot.)
    /// </summary>
    public class DeviceConnection : IDisposable
    {
        private TcpClient? _client;

        public DeviceConfig Config { get; }

        public DeviceConnection(DeviceConfig config)
        {
            Config = config;
        }

        /// <summary>True nếu đang nối và đầu bên kia chưa đóng.</summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    var socket = _client?.Client;
                    if (socket == null || !socket.Connected) return false;

                    // Đầu bên kia đã đóng: Poll báo "đọc được" nhưng không có dữ liệu nào
                    bool closedByPeer = socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0;
                    return !closedByPeer;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task ConnectAsync()
        {
            Disconnect();

            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(Config.IpAddress, Config.Port);
                var finished = await Task.WhenAny(connectTask, Task.Delay(Config.TimeoutMs));

                if (finished != connectTask)
                    throw new TimeoutException($"Quá thời gian chờ ({Config.TimeoutMs} ms)");

                await connectTask;          // nếu kết nối thất bại thì dòng này ném lỗi
                _client = client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>Gửi một gói tin: nội dung + ký tự kết thúc đã cấu hình.</summary>
        public async Task SendAsync(string text)
        {
            if (_client == null || !IsConnected)
                throw new InvalidOperationException("Chưa kết nối");

            byte[] body = Config.GetEncoding().GetBytes(text);
            byte[] end = Config.GetTerminatorBytes();

            byte[] packet = new byte[body.Length + end.Length];
            body.CopyTo(packet, 0);
            end.CopyTo(packet, body.Length);

            await _client.GetStream().WriteAsync(packet, 0, packet.Length);
        }

        public void Disconnect()
        {
            _client?.Dispose();
            _client = null;
        }

        public void Dispose() => Disconnect();
    }
}
