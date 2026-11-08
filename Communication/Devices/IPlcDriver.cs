namespace Communication.Drivers
{
    public interface IPlcDriver : IDeviceLink
    {
        Task<bool> ReadBitAsync(string address, CancellationToken ct = default);
        Task WriteBitAsync(string address, bool value, CancellationToken ct = default);
        Task<short> ReadWordAsync(string address, CancellationToken ct = default);
        Task WriteWordAsync(string address, int value, CancellationToken ct = default);
    }
}
