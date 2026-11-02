using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Communication.Transport
{
    internal class TcpServerTransport : ITransport
    {
        private TcpListener _listener;
        private TcpClient _client;
        private readonly int _port;

        public TcpServerTransport(int port)
        {
            _port = port;
        }

        public bool IsOpen => _client != null && _client.Connected;

        public bool IsConnected => throw new NotImplementedException();

        public void Open()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            // Chờ thiết bị kết nối vào
            _client = _listener.AcceptTcpClient();
        }

        public void Close()
        {
            _client?.Close();
            _client?.Dispose();
            _listener?.Stop();
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

        public void Connect()
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void Send(byte[] data)
        {
            throw new NotImplementedException();
        }

        public void Send(string data)
        {
            throw new NotImplementedException();
        }

        public void Receive(Action<byte[]> callback)
        {
            throw new NotImplementedException();
        }
    }
}
