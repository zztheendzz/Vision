using Communication.Configuration;
using Communication.Drivers;
using Communication.Fixtures;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UI.ViewModels.Device;

namespace UI.Services
{
    /// <summary>
    /// Bắt tay robot ↔ khuôn test bằng 2 bit PLC (mặc định M739 / M740, sửa được trong cài đặt khuôn):
    ///   Bit "robot gắp xong" = 1  → chờ trễ mở → MỞ khuôn đích (số khuôn đọc từ thanh ghi, mặc định D642) → hạ bit về 0.
    ///   Bit "robot đặt xong" = 1  → chờ trễ đóng → ĐÓNG khuôn đích (có cảm biến: CHECKSENSOR trước khi đóng;
    ///                                không cảm biến: tin vào bit này) → hạ bit về 0.
    /// Lệnh đi qua khóa an toàn (chế độ khuôn chồng / 2 bên A,B / chỉ 1 khuôn mở). Bị chặn hoặc lỗi thì KHÔNG hạ bit,
    /// thử lại vài lần; vẫn không được thì dừng chạy tự động để robot không đặt hàng vào khuôn đóng.
    /// Chạy cùng chế độ tự động; vòng đọc ở thread nền, lệnh khuôn gửi trên UI thread.
    /// </summary>
    public sealed class FixtureHandshakeRunner : IDisposable
    {
        private const int Attempts = 3;
        private const int RetryDelayMs = 1500;
        private const int MaxReadFailures = 5;

        private readonly DeviceConfigViewModel _root;
        private readonly Action<string> _log;
        private CancellationTokenSource? _cts;

        public event Action<string>? Stopped;
        public bool IsRunning { get; private set; }

        public FixtureHandshakeRunner(DeviceConfigViewModel root, Action<string> log)
        {
            _root = root;
            _log = log;
        }

        private string Group(string key) => _root.Catalog.GetGroupValueOrDefault(DeviceType.Fixture, key).Trim();

        private int GroupInt(string key, int fallback, int min, int max)
            => int.TryParse(Group(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? Math.Clamp(v, min, max) : fallback;

        /// <summary>Bắt đầu. Trả về lỗi (chữ) nếu chưa đủ điều kiện; null nếu đã chạy. Chưa đặt bit nào thì bỏ qua (trả về null, không chạy).</summary>
        public string? Start()
        {
            if (IsRunning) return null;

            string picked = Group("Hs.PickedBit"), placed = Group("Hs.PlacedBit"), reg = Group("Hs.FixtureReg");
            if (picked.Length == 0 && placed.Length == 0) return null;
            if (reg.Length == 0) return "Chưa đặt thanh ghi số khuôn đích (Hs.FixtureReg) cho bắt tay robot–khuôn.";

            var plcs = _root.Manager.ConnectedPlcs();
            if (plcs.Count == 0) return "Chưa có PLC nào kết nối (cần cho bắt tay robot–khuôn).";
            var (cfg, plc) = plcs[0];

            int openDelay = GroupInt("Hs.OpenDelayMs", 0, 0, 600000);
            int closeDelay = GroupInt("Hs.CloseDelayMs", 0, 0, 600000);
            int pollMs = GroupInt("Hs.PollMs", 50, 10, 1000);

            _cts = new CancellationTokenSource();
            IsRunning = true;
            var ct = _cts.Token;
            _ = Task.Run(() => LoopAsync(plc, cfg.Name, picked, placed, reg, openDelay, closeDelay, pollMs, ct));
            _log($"[khuôn] Bắt tay robot: mở khi {(picked.Length > 0 ? picked : "—")}=1 (trễ {openDelay} ms), đóng khi {(placed.Length > 0 ? placed : "—")}=1 (trễ {closeDelay} ms); khuôn đích đọc từ {reg}.");
            return null;
        }

        public void Stop() => _cts?.Cancel();

        private async Task LoopAsync(IPlcDriver plc, string plcName, string picked, string placed, string reg,
                                     int openDelay, int closeDelay, int pollMs, CancellationToken ct)
        {
            string? stopReason = null;
            try
            {
                await RefreshStatesAsync(ct);

                int failures = 0;
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(pollMs, ct);

                    bool pick, place;
                    try
                    {
                        pick = picked.Length > 0 && await plc.ReadBitAsync(picked, ct);
                        place = placed.Length > 0 && await plc.ReadBitAsync(placed, ct);
                        failures = 0;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (++failures >= MaxReadFailures) { stopReason = $"Mất liên lạc với {plcName}: {ex.Message}"; break; }
                        continue;
                    }

                    if (pick)
                    {
                        stopReason = await HandleAsync(plc, picked, reg, open: true, openDelay, ct);
                        if (stopReason != null) break;
                    }
                    if (place)
                    {
                        stopReason = await HandleAsync(plc, placed, reg, open: false, closeDelay, ct);
                        if (stopReason != null) break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { stopReason = ex.Message; }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
                if (stopReason != null) Stopped?.Invoke(stopReason);
            }
        }

        /// <summary>Xử lý một lần bit lên 1. Trả về lý do dừng nếu không làm được; null nếu xong (đã hạ bit).</summary>
        private async Task<string?> HandleAsync(IPlcDriver plc, string bit, string reg, bool open, int delayMs, CancellationToken ct)
        {
            string what = open ? "MỞ" : "ĐÓNG";
            int no;
            try { no = await plc.ReadWordAsync(reg, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"Không đọc được thanh ghi số khuôn {reg}: {ex.Message}";
            }

            var fixture = await OnUiAsync(() => _root.Devices.FirstOrDefault(d => d.Config.Type == DeviceType.Fixture && d.Enabled && d.FixtureNo == no));
            if (fixture == null)
                return $"{bit}=1 nhưng không có khuôn số {no:00} (đọc từ {reg}) đang bật.";

            _log($"[khuôn] {bit}=1 → {what} khuôn {no:00} ({fixture.Name})" + (delayMs > 0 ? $" sau {delayMs} ms" : ""));
            if (delayMs > 0) await Task.Delay(delayMs, ct);

            string? last = null;
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                last = await OnUiTaskAsync(() => DoAsync(fixture, open));
                if (last == null) break;
                _log($"[khuôn - LỖI] {what} khuôn {no:00} lần {attempt}/{Attempts}: {last}");
                if (attempt < Attempts) await Task.Delay(RetryDelayMs, ct);
            }
            if (last != null) return $"Không {what.ToLowerInvariant()} được khuôn {no:00} ({fixture.Name}): {last}";

            try { await plc.WriteBitAsync(bit, false, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"Không hạ được bit {bit}: {ex.Message}";
            }
            _log($"[khuôn] Đã {what.ToLowerInvariant()} khuôn {no:00}, hạ {bit} về 0.");
            return null;
        }

        /// <summary>Chạy trên UI thread. Trả về null nếu thành công, hoặc lý do lỗi.</summary>
        private static async Task<string?> DoAsync(DeviceViewModel d, bool open)
        {
            if (open)
            {
                if (d.IsOpen == true) return null;                    // đã mở sẵn
                d.RobotPlaced = false;
            }
            else
            {
                d.RobotPlaced = true;                                 // khuôn không cảm biến: bit 'đặt xong' là bằng chứng có hàng
                if (d.IsOpen == false) return null;                   // đã đóng sẵn
            }

            for (int i = 0; i < 50 && d.FixtureBusy; i++) await Task.Delay(100);   // chờ lệnh khác đang chạy (tối đa 5 s)
            await d.RunFixtureCommandAsync(open ? FixtureCommand.Open : FixtureCommand.Close, automatic: true);

            bool ok = d.IsOpen == open;
            return ok ? null : (string.IsNullOrWhiteSpace(d.Message) ? "khuôn không đổi trạng thái" : d.Message);
        }

        /// <summary>Khi bật: hỏi trạng thái mở/đóng của từng khuôn đang nối, để khóa an toàn biết khuôn nào đang mở.</summary>
        private async Task RefreshStatesAsync(CancellationToken ct)
        {
            var list = await OnUiAsync(() => _root.Devices.Where(d => d.Config.Type == DeviceType.Fixture && d.Enabled).ToList());
            foreach (var d in list)
            {
                ct.ThrowIfCancellationRequested();
                try { await OnUiTaskAsync(() => d.RunFixtureCommandAsync(FixtureCommand.State)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log($"[khuôn - LỖI] Hỏi trạng thái {d.Name}: {ex.Message}"); }
            }
        }

        private static Task<T> OnUiTaskAsync<T>(Func<Task<T>> f)
            => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task.Unwrap();

        private static Task OnUiTaskAsync(Func<Task> f)
            => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task.Unwrap();

        private static Task<T> OnUiAsync<T>(Func<T> f)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            return dispatcher.InvokeAsync(f).Task;
        }

        public void Dispose() => Stop();
    }
}
