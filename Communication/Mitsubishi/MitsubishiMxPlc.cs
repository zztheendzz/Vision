#if MX_COMPONENT
using Communication.Configuration;
using Communication.Drivers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Communication.Mitsubishi
{

    /// <summary>
    /// PLC Mitsubishi qua MX Component, DÙNG TẠM để thử với GX Simulator3.
    /// MX Component bản 32-bit không nạp được vào chương trình AnyCPU / 64-bit, nên ta không gọi COM trực tiếp:
    /// chạy chương trình phụ MxBridge.exe (x86, project MxBridge) và nói chuyện với nó qua stdin/stdout.
    /// Đặt MxBridge.exe cạnh file chạy chính, hoặc trong thư mục con MxBridge\ .
    /// Chưa chạy thử với MX Component thật.
    /// </summary>
    public sealed class MitsubishiMxPlc : IPlcDriver, IProbeable
    {
        private const int OpenTimeoutMs = 15000;
        private const int CommandTimeoutMs = 5000;

        private readonly int _station;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Process? _bridge;
        private bool _connected;

        public MitsubishiMxPlc(int logicalStation) => _station = logicalStation;

        public static MitsubishiMxPlc FromConfig(DeviceConfig d) => new(d.GetInt("StationNo", 1));

        public bool IsConnected => _connected && _bridge is { HasExited: false };

        public async Task<string?> ProbeAsync(CancellationToken ct = default)
        {
            if (!_connected) return null;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(CommandTimeoutMs + 1000);
                await ReadBitAsync("M0", cts.Token).ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "PLC không trả lời"; }
            catch (Exception ex) when (ex is not OperationCanceledException) { return ex.Message; }
        }
        public string Description => $"MX Component trạm {_station} (qua MxBridge x86)";

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            Disconnect();

            string exe = FindBridge()
                ?? throw new IOException(
                    "Không thấy MxBridge.exe. Build project MxBridge (x86) rồi chép MxBridge.exe cạnh file chạy chính " +
                    "(hoặc vào thư mục con MxBridge\\).");

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var process = Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = new UTF8Encoding(false)
                }) ?? throw new IOException("Không chạy được MxBridge.exe.");
                _bridge = process;

                try
                {
                    await SendAsync($"OPEN {_station.ToString(CultureInfo.InvariantCulture)}", OpenTimeoutMs, ct).ConfigureAwait(false);
                    _connected = true;
                }
                catch
                {
                    KillBridge();
                    throw;
                }
            }
            finally { _lock.Release(); }
        }

        public void Disconnect()
        {
            _connected = false;
            var process = _bridge;
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("QUIT");
                    process.StandardInput.Flush();
                    process.WaitForExit(500);
                }
            }
            catch { }
            KillBridge();
        }

        private void KillBridge()
        {
            _connected = false;
            var process = _bridge;
            _bridge = null;
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.Dispose(); } catch { }
        }

        private static string? FindBridge()
        {
            string baseDir = AppContext.BaseDirectory;
            foreach (string path in new[]
                     {
                         Path.Combine(baseDir, "MxBridge.exe"),
                         Path.Combine(baseDir, "MxBridge", "MxBridge.exe")
                     })
                if (File.Exists(path)) return path;
            return null;
        }

        // Gửi một lệnh, nhận một dòng trả lời. Phần sau "OK " được trả về; "ERR ..." thành IOException.
        // Gọi trong _lock. Quá hạn thì coi như chương trình phụ treo: đóng nó và báo mất kết nối.
        private async Task<string> SendAsync(string command, int timeoutMs, CancellationToken ct)
        {
            var process = _bridge ?? throw new InvalidOperationException("Chưa kết nối PLC (MX Component).");
            if (process.HasExited) { _connected = false; throw new IOException("MxBridge.exe đã thoát."); }

            await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            var read = process.StandardOutput.ReadLineAsync();
            var finished = await Task.WhenAny(read, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (finished != read)
            {
                ct.ThrowIfCancellationRequested();
                KillBridge();
                throw new TimeoutException($"MxBridge không trả lời trong {timeoutMs} ms, đã đóng kết nối.");
            }

            string? line = await read.ConfigureAwait(false);
            if (line == null) { KillBridge(); throw new IOException("MxBridge.exe đã đóng."); }

            if (line.StartsWith("OK", StringComparison.Ordinal)) return line.Length > 3 ? line[3..] : "";
            throw new IOException("PLC (MX): " + (line.StartsWith("ERR ", StringComparison.Ordinal) ? line[4..] : line));
        }

        private async Task<string> CommandAsync(string command, CancellationToken ct)
        {
            if (!_connected) throw new InvalidOperationException("Chưa kết nối PLC (MX Component).");
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try { return await SendAsync(command, CommandTimeoutMs, ct).ConfigureAwait(false); }
            finally { _lock.Release(); }
        }

        private static int ParseValue(string text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v
                : throw new IOException("PLC (MX): trả lời không phải số: " + text);

        public async Task<bool> ReadBitAsync(string address, CancellationToken ct = default)
            => ParseValue(await CommandAsync($"GET {address}", ct).ConfigureAwait(false)) != 0;

        public Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
            => CommandAsync($"SET {address} {(value ? 1 : 0)}", ct);

        public async Task<short> ReadWordAsync(string address, CancellationToken ct = default)
            => unchecked((short)ParseValue(await CommandAsync($"GET {address}", ct).ConfigureAwait(false)));

        public Task WriteWordAsync(string address, int value, CancellationToken ct = default)
        {
            if (value < short.MinValue || value > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), $"Giá trị {value} ngoài khoảng 16 bit.");
            return CommandAsync($"SET {address} {value.ToString(CultureInfo.InvariantCulture)}", ct);
        }

        public void Dispose() { Disconnect(); _lock.Dispose(); }
    }
}
#endif