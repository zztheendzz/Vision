using Communication.Drivers;

namespace Communication.Transport
{
    /// <summary>Đường truyền chữ có ký tự kết thúc (dùng cho Khuôn test và PC; PLC / Robot dùng thư viện Mitsubishi).</summary>
    public interface ITransport : IDeviceLink
    {
        Task SendAsync(string text, CancellationToken ct = default);

        /// <summary>Gửi một dòng rồi chờ một dòng trả lời (đến ký tự kết thúc RX). Quá hạn thì ném TimeoutException.</summary>
        Task<string> RequestAsync(string text, int timeoutMs, CancellationToken ct = default);
    }
}
