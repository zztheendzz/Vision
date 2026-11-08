using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Configuration
{
    enum ConnectionKind
    {
        TcpClient,
        TcpServer, 
        Serial,
        MQTT
    }
}