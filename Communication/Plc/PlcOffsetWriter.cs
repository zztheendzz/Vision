using Communication.Drivers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Plc
{
    public sealed record PlcOffsetResult(bool Success, string Message);

    /// <summary>
    /// Gửi độ bù cho robot QUA PLC: C# ghi (dX, dY, dZ, dR) vào các thanh ghi "Calib X, Y, Z, R" trong bản đồ tín hiệu,
    /// PLC lấy các giá trị đó chuyển cho robot.
    ///
    ///  - Giá trị PLC (số nguyên) = độ bù / Hệ số của tín hiệu. Hệ số sai làm độ bù sai (ví dụ hệ số 1 làm tròn 0.35 mm
    ///    thành 0), vì vậy luôn kiểm tra hệ số ở bản đồ và xem giá trị thô trong thông báo kết quả.
    ///  - Số 32 bit: thanh ghi thấp ở địa chỉ N, thanh ghi cao ở N+1 (thứ tự word của Mitsubishi).
    ///  - An toàn: độ bù vượt giới hạn, thiếu địa chỉ, thanh ghi chồng nhau, hoặc số ngoài khoảng của kiểu: KHÔNG ghi gì.
    ///    Ghi xong đọc lại từng thanh ghi để kiểm chứng.
    ///  - Z: máy không dùng Z (địa chỉ trống) thì chỉ bỏ qua khi dZ = 0.
    /// </summary>
    public static class PlcOffsetWriter
    {
        /// <summary>Tiền tố khóa nhóm thanh ghi độ bù ĐẶT (PlaceCompensation).</summary>
        public const string PlacePrefix = "Pos.Calib";
        /// <summary>Tiền tố khóa nhóm thanh ghi độ bù GẮP (PickCompensation).</summary>
        public const string PickPrefix = "Pos.CalibPick";

        public static readonly string[] Keys = KeysFor(PlacePrefix);

        public static string[] KeysFor(string prefix) => new[] { $"{prefix}.X", $"{prefix}.Y", $"{prefix}.Z", $"{prefix}.R" };

        private sealed record Plan(string Key, SignalItem Item, PlcAddress Address, long Raw, double Value, int Words);

        public static async Task<PlcOffsetResult> WriteAsync(
            IPlcDriver plc, PlcMap map, double dx, double dy, double dz, double drz,
            double maxOffsetMm, double maxOffsetDeg, CancellationToken ct = default, string prefix = PlacePrefix, bool dryRun = false)
        {
            if (!plc.IsConnected) return Fail("PLC chưa kết nối.");

            double[] values = { dx, dy, dz, drz };
            if (values.Any(v => !double.IsFinite(v))) return Fail("Độ bù không phải số hợp lệ.");
            if (Math.Abs(dx) > maxOffsetMm || Math.Abs(dy) > maxOffsetMm || Math.Abs(dz) > maxOffsetMm)
                return Fail($"Độ bù ({dx:0.###}, {dy:0.###}, {dz:0.###}) mm vượt giới hạn ±{maxOffsetMm:0.###} mm.");
            if (Math.Abs(drz) > maxOffsetDeg)
                return Fail($"Góc bù {drz:0.###}° vượt giới hạn ±{maxOffsetDeg:0.###}°.");

            // ---------- Lập kế hoạch: kiểm tra hết trước khi ghi ----------
            var plans = new List<Plan>();
            var keys = KeysFor(prefix);
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                var item = map.FindItem(key);
                if (item == null) return Fail($"Bản đồ tín hiệu chưa có '{key}'.");

                if (string.IsNullOrWhiteSpace(item.Address))
                {
                    if (key.EndsWith(".Z", StringComparison.Ordinal) && values[i] == 0) continue;     // máy không dùng Z
                    return Fail($"'{item.Title}' ({key}) chưa có địa chỉ trong bản đồ tín hiệu.");
                }
                if (!PlcAddress.TryParse(item.Address, out var address, out string? error))
                    return Fail($"{key}: địa chỉ '{item.Address}': {error}");
                if (address.Class != DeviceClass.Word || item.Kind == SignalKind.Bit)
                    return Fail($"{key}: phải là thanh ghi (D, W, R...) kiểu 16 hoặc 32 bit.");
                if (!double.IsFinite(item.Scale) || item.Scale == 0)
                    return Fail($"{key}: hệ số phải khác 0.");

                double scaled = values[i] / item.Scale;
                if (!double.IsFinite(scaled) || Math.Abs(scaled) > 1e12)
                    return Fail($"{key}: giá trị quá lớn so với hệ số {item.Scale}.");
                long raw = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);

                var (min, max) = Range(item.Kind, item.Signed);
                if (raw < min || raw > max)
                    return Fail($"{key}: giá trị PLC {raw} ngoài khoảng {min}..{max} của kiểu {item.Kind}" +
                                (item.Signed ? "" : " không dấu (số âm cần bật 'Có dấu')") + ".");

                plans.Add(new Plan(key, item, address, raw, values[i], item.Kind == SignalKind.DWord ? 2 : 1));
            }

            // Hai tín hiệu không được chồng lên nhau, nếu không giá trị này ghi đè giá trị kia
            for (int i = 0; i < plans.Count; i++)
                for (int j = i + 1; j < plans.Count; j++)
                    if (plans[i].Address.Device == plans[j].Address.Device
                        && plans[i].Address.Number < plans[j].Address.Number + plans[j].Words
                        && plans[j].Address.Number < plans[i].Address.Number + plans[i].Words)
                        return Fail($"{plans[i].Key} ({plans[i].Address}) và {plans[j].Key} ({plans[j].Address}) chồng lên nhau trong PLC.");

            // Chỉ kiểm tra, chưa ghi (dùng khi phải ghi nhiều nhóm: kiểm hết rồi mới ghi nhóm đầu tiên)
            if (dryRun) return new PlcOffsetResult(true, "Hợp lệ (chưa ghi).");

            // ---------- Danh sách thanh ghi cần ghi ----------
            var words = new List<(string Address, short Value)>();
            foreach (var p in plans)
            {
                uint bits = unchecked((uint)p.Raw);
                words.Add((p.Address.ToString(), unchecked((short)(ushort)(bits & 0xFFFF))));
                if (p.Words == 2)
                {
                    if (!p.Address.TryOffset(1, out var next)) return Fail($"{p.Key}: không có thanh ghi kế tiếp.");
                    words.Add((next.ToString(), unchecked((short)(ushort)(bits >> 16))));
                }
            }

            // ---------- Ghi, rồi đọc lại ----------
            int written = 0;
            try
            {
                foreach (var (address, value) in words)
                {
                    ct.ThrowIfCancellationRequested();
                    await plc.WriteWordAsync(address, value, ct).ConfigureAwait(false);
                    written++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail($"Ghi thanh ghi lỗi sau {written}/{words.Count} thanh ghi ({ex.Message}). " +
                            "PLC có thể đang giữ bộ giá trị lẫn cũ và mới: ghi lại hoặc reset trước khi chạy tiếp.");
            }

            foreach (var (address, value) in words)
            {
                short back;
                try { back = await plc.ReadWordAsync(address, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Fail($"Đã ghi nhưng không đọc lại được {address} để kiểm chứng ({ex.Message}).");
                }
                if (back != value)
                    return Fail($"Ghi {address} = {value} nhưng đọc lại được {back}: PLC không nhận giá trị (kiểm tra địa chỉ / chế độ chạy của PLC).");
            }

            string detail = string.Join("; ", plans.Select(p =>
                $"{p.Key[(p.Key.LastIndexOf('.') + 1)..]} {p.Address}{(p.Words == 2 ? "+1" : "")} ← {p.Raw} ({p.Value:0.###})"));
            return new PlcOffsetResult(true, detail);
        }

        /// <summary>
        /// Các nhóm thanh ghi (ví dụ độ bù đặt và độ bù gắp) không được trùng nhau, nếu không nhóm ghi sau đè nhóm ghi trước.
        /// Trả về thông báo lỗi, hoặc null nếu ổn. Chỉ xét các tín hiệu đã có địa chỉ hợp lệ.
        /// </summary>
        public static string? FindOverlapBetween(PlcMap map, params string[] prefixes)
        {
            var used = new List<(string Key, PlcAddress Address, int Words)>();
            foreach (string prefix in prefixes)
                foreach (string key in KeysFor(prefix))
                {
                    var item = map.FindItem(key);
                    if (item == null || string.IsNullOrWhiteSpace(item.Address)) continue;
                    if (!PlcAddress.TryParse(item.Address, out var address, out _)) continue;
                    used.Add((key, address, item.Kind == SignalKind.DWord ? 2 : 1));
                }

            for (int i = 0; i < used.Count; i++)
                for (int j = i + 1; j < used.Count; j++)
                    if (used[i].Address.Device == used[j].Address.Device
                        && used[i].Address.Number < used[j].Address.Number + used[j].Words
                        && used[j].Address.Number < used[i].Address.Number + used[i].Words)
                        return $"{used[i].Key} ({used[i].Address}) và {used[j].Key} ({used[j].Address}) chồng lên nhau trong PLC.";
            return null;
        }

        private static (long Min, long Max) Range(SignalKind kind, bool signed) => (kind, signed) switch
        {
            (SignalKind.Word, true) => (short.MinValue, short.MaxValue),
            (SignalKind.Word, false) => (0, ushort.MaxValue),
            (SignalKind.DWord, true) => (int.MinValue, int.MaxValue),
            _ => (0, uint.MaxValue)
        };

        private static PlcOffsetResult Fail(string message) => new(false, message);
    }
}
