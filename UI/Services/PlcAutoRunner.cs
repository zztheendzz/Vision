using Communication.Drivers;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Runtime;

namespace UI.Services
{
    /// <summary>
    /// Chạy tự động theo tín hiệu PLC (bắt tay bằng bit):
    ///   1. PLC bật "Bit PLC yêu cầu chụp" = 1 (mặc định M601). Chương trình đọc liên tục; thấy 1 thì HẠ NGAY bit này về 0 (đã nhận lệnh),
    ///      rồi xóa các bit kết quả cũ, chụp, chạy đồ thị, gửi độ bù cho robot (chế độ Chạy thật), lưu kết quả.
    ///   2. Chương trình ghi kết quả về PLC: Bit OK / Bit NG / Bit lỗi (một trong ba), rồi CUỐI CÙNG mới đặt "Bit vision xong" = 1.
    ///   3. PLC thấy Bit xong = 1 thì đọc kết quả. Các bit kết quả được xóa ở lần yêu cầu chụp kế tiếp (hoặc khi dừng chạy tự động).
    /// PLC chỉ cần bật bit yêu cầu một lần và chờ Bit xong; không cần tự hạ bit yêu cầu. Bit nào để trống thì bỏ qua, nhưng phải có ít nhất một bit báo về.
    /// Chỉ dùng PLC đầu tiên đang kết nối. Vòng đọc chạy ở thread nền; hàm runCycle phải tự chuyển về UI thread nếu cần.
    /// </summary>
    public sealed class PlcAutoRunner : IDisposable
    {
        private const int MaxReadFailures = 5;

        private sealed record Bits(string Done, string Ok, string Ng, string Err);

        private readonly DeviceManager _devices;
        private readonly Func<CancellationToken, Task<RunVerdict>> _runCycle;
        private readonly Action<string> _log;
        private CancellationTokenSource? _cts;

        /// <summary>Báo khi vòng tự dừng vì lỗi (mất liên lạc PLC, không ghi được kết quả...). Không báo khi người dùng tự dừng.</summary>
        public event Action<string>? Stopped;

        public bool IsRunning { get; private set; }

        public PlcAutoRunner(DeviceManager devices, Func<CancellationToken, Task<RunVerdict>> runCycle, Action<string> log)
        {
            _devices = devices;
            _runCycle = runCycle;
            _log = log;
        }

        /// <summary>Bắt đầu. Trả về lỗi (chữ) nếu chưa đủ điều kiện; null nếu đã chạy.</summary>
        public string? Start()
        {
            if (IsRunning) return null;

            var plcs = _devices.ConnectedPlcs();
            if (plcs.Count == 0) return "Chưa có PLC nào kết nối.";

            var (cfg, plc) = plcs[0];
            string trigger = cfg.GetString("CameraTrigger").Trim();
            if (trigger.Length == 0) return $"Chưa đặt 'Bit PLC yêu cầu chụp' cho {cfg.Name}.";

            var bits = new Bits(cfg.GetString("DoneBit").Trim(), cfg.GetString("OkBit").Trim(),
                                cfg.GetString("NgBit").Trim(), cfg.GetString("VisionErrBit").Trim());
            if (bits.Done.Length + bits.Ok.Length + bits.Ng.Length + bits.Err.Length == 0)
                return $"Chưa đặt bit nào để báo kết quả về PLC cho {cfg.Name} (Bit vision xong / OK / NG / lỗi).";

            int pollMs = Math.Clamp(cfg.GetInt("PollMs", 20), 10, 1000);

            _cts = new CancellationTokenSource();
            IsRunning = true;
            var ct = _cts.Token;
            _ = Task.Run(() => LoopAsync(plc, cfg.Name, trigger, bits, pollMs, ct));
            _log($"[tự động] Bật. Chờ bit {trigger} của {cfg.Name} lên 1.");
            return null;
        }

        public void Stop() => _cts?.Cancel();

        private async Task LoopAsync(IPlcDriver plc, string plcName, string trigger, Bits bits, int pollMs, CancellationToken ct)
        {
            string? stopReason = null;
            try
            {
                await ClearAsync(plc, bits, ct);
                int failures = 0;

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(pollMs, ct);

                    bool now;
                    try
                    {
                        now = await plc.ReadBitAsync(trigger, ct);
                        failures = 0;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (++failures >= MaxReadFailures)
                        {
                            stopReason = $"Mất liên lạc với {plcName}: {ex.Message}";
                            break;
                        }
                        continue;
                    }

                    if (!now) continue;

                    // Nhận lệnh: hạ bit yêu cầu về 0 ngay để PLC biết đã nhận, rồi xóa kết quả cũ
                    try
                    {
                        await plc.WriteBitAsync(trigger, false, ct);
                        await ClearAsync(plc, bits, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        stopReason = $"Không hạ được bit yêu cầu chụp {trigger} của {plcName}: {ex.Message}";
                        break;
                    }

                    _log($"[tự động] {plcName} yêu cầu chụp ({trigger}=1, đã hạ về 0)");
                    RunVerdict verdict;
                    try { verdict = await _runCycle(ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _log($"[tự động - LỖI] {ex.Message}");
                        verdict = RunVerdict.Error;
                    }

                    try { await WriteResultAsync(plc, bits, verdict, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        stopReason = $"Không ghi được kết quả về {plcName}: {ex.Message}";
                        break;
                    }
                    _log($"[tự động] Đã báo {plcName}: {verdict}");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { stopReason = ex.Message; }
            finally
            {
                // Dừng thì hạ các bit báo về PLC, để PLC không đọc nhầm kết quả cũ
                try { await ClearAsync(plc, bits, CancellationToken.None); } catch { }
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
                if (stopReason != null) Stopped?.Invoke(stopReason);
            }
        }

        // Kết quả trước, "xong" sau cùng: PLC thấy xong = 1 thì kết quả đã sẵn sàng
        private static async Task WriteResultAsync(IPlcDriver plc, Bits bits, RunVerdict verdict, CancellationToken ct)
        {
            await WriteAsync(plc, bits.Ok, verdict == RunVerdict.Ok, ct);
            await WriteAsync(plc, bits.Ng, verdict == RunVerdict.Ng, ct);
            await WriteAsync(plc, bits.Err, verdict == RunVerdict.Error, ct);
            await WriteAsync(plc, bits.Done, true, ct);
        }

        // Xóa "xong" trước, rồi mới xóa kết quả
        private static async Task ClearAsync(IPlcDriver plc, Bits bits, CancellationToken ct)
        {
            await WriteAsync(plc, bits.Done, false, ct);
            await WriteAsync(plc, bits.Ok, false, ct);
            await WriteAsync(plc, bits.Ng, false, ct);
            await WriteAsync(plc, bits.Err, false, ct);
        }

        private static Task WriteAsync(IPlcDriver plc, string address, bool value, CancellationToken ct)
            => address.Length == 0 ? Task.CompletedTask : plc.WriteBitAsync(address, value, ct);

        public void Dispose() => Stop();
    }
}
