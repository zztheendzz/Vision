using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Configuration
{
    public class SerialSettings
    {
        public string COMPort { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public int StopBits { get; set; }
        public SerialSettings()
        {
            COMPort = "COM1";
            BaudRate = 9600;
            DataBits = 8;
            StopBits = 1;
        }
        public SerialSettings(string comPort, int baudRate, int dataBits, int stopBits)
        {
            COMPort = comPort;
            BaudRate = baudRate;
            DataBits = dataBits;
            StopBits = stopBits;
        }
    }
}
