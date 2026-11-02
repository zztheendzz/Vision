using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Connection
{
    enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Error
    }
}
