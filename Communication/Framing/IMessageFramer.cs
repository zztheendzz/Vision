using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Framing
{
    public interface IMessageFramer
    {
        byte[] Frame(byte[] message);
        List<byte[]> Push(byte[] data);

    }
}
