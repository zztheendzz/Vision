using Communication.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Connection
{
    interface  IDeviceConnection
    {
        DeviceConfig Config { get; }
        ConnectionState State { get; }

        event EventHandler<ConnectionState> StateChanged;
        event EventHandler<byte[]> MessageReceived;

        void Connect();
        void Disconnect();
        void Send(byte[] data);
        byte[] SendAndReceive(byte[] data, int timeout);
    }
}
