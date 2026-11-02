using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace Communication.Transport
{
    public class TcpClientTransport
    {
        private TcpClient _client;
        private readonly string _ip;
        private readonly int _port;
        private readonly int _timeout;

        public TcpClientTransport(string ip, int port, int timeout)
        {
            _ip = ip;
            _port = port;
            _timeout = timeout;
        }

        public bool IsOpen => _client != null && _client.Connected;

        public void Open()
        {
            _client = new TcpClient();
            _client.ReceiveTimeout = _timeout;
            _client.SendTimeout = _timeout;
            _client.Connect(_ip, _port);
        }

        public void Close()
        {
            _client?.Close();
            _client?.Dispose();
            _client = null;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_client == null || !_client.Connected) return 0;
            return _client.GetStream().Read(buffer, offset, count);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (_client == null || !_client.Connected) return;
            _client.GetStream().Write(buffer, offset, count);
        }
    }
}
