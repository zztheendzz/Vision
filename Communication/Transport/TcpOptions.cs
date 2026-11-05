using System.Text;

namespace Communication.Transport
{
    public sealed record TcpOptions(
        string Host,
        int Port,
        int ConnectTimeoutMs,
        byte[] TxTerminator,
        byte[] RxTerminator,
        Encoding Encoding)
    {
        public static readonly byte[] Crlf = { 13, 10 };
    }
}
