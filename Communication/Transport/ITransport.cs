using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Transport
{
    public interface ITransport
    {
        void Connect();
        void Disconnect();
        void Send(byte[] data);
        void Send(string data);
        void Receive(Action<byte[]> callback);
        bool IsConnected { get; }
    }
}
