using Communication.Devices;
using Communication.Drivers;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Calibration;
using Vision.Geometry;

namespace UI.Services
{
    /// <summary>
    /// Thông số một lần hiệu chuẩn tự động.
    /// Robot cầm mốc (hoặc vật) và đi qua lưới điểm quanh Center; mỗi điểm dừng hẳn, chụp, đo pixel, đọc vị trí robot.
    /// </summary>
    public sealed record AutoCalibrationPlan(
        RobotPoint Center,                       // điểm tâm: mốc nằm giữa vùng nhìn của camera, nét tốt
        int CountX,                              // số điểm theo X robot (2..7)
        int CountY,                              // số điểm theo Y robot (2..7)
        double StepMm,                           // bước giữa hai điểm lưới (mm)
        IReadOnlyList<double> RotationsDeg,      // các góc xoay thêm tại tâm để kiểm tra dấu góc (vd -10, 10). Rỗng = bỏ qua
        int SpeedPercent,                        // tốc độ robot khi hiệu chuẩn (1..100)
        int SettleMs,                            // chờ sau khi tới nơi cho hết rung
        double MaxRmsMm,                         // sai số RMS lớn nhất chấp nhận được
        int MinValidPoints,                      // số điểm hợp lệ tối thiểu
        char RzAxis,                             // trục xoay của robot ứng với góc Rz (thường 'C')
        double MaxErrorMm = 0,                   // sai số lớn nhất của một điểm lưới chấp nhận được (0 = không kiểm tra)
        double MaxRotationRmsMm = 0.3,           // sai số RMS của calib xoay (tìm mốc cách trục xoay); vượt thì không dùng kết quả xoay
        double MaxAngleErrorDeg = 1.0,           // lệch góc tối đa giữa góc robot xoay và góc đo từ ảnh; vượt thì cảnh báo
        int Averages = 1);                       // số lần chụp + đo tại mỗi điểm lưới, lấy trung bình (giảm nhiễu đo). 1 = một lần

    /// <summary>Kết quả calib xoay: vector từ TCP tới mốc (mm, hệ robot), sai số RMS của phép khớp và số mẫu dùng.</summary>
    public sealed record RotationFit(double OffsetX, double OffsetY, double RmsMm, int Samples);

    /// <summary>Kết quả hiệu chuẩn tự động. Data khác null khi tính được ma trận (kể cả khi bị từ chối vì sai số lớn).</summary>
    public sealed record AutoCalibrationResult(
        bool Success, string Message, IReadOnlyList<CalibrationPoint> Points,
        CalibrationData? Data, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Hiệu chuẩn camera - robot TỰ ĐỘNG (dùng cho cả hai trường hợp: robot cầm vật đưa tới camera cố định,
    /// hoặc robot cầm mốc quét qua vùng nhìn của camera đặt trên bàn).
    ///
    /// Lớp này KHÔNG biết camera hay giao diện: việc "chụp và đo" được truyền vào qua hàm measure
    /// (trả Pose2D của mốc trong ảnh, hoặc null nếu không thấy).
    ///
    /// An toàn: kiểm tra trước khi đi (robot có quyền điều khiển, vùng quét nhỏ), đi chậm, mỗi bước hủy được,
    /// cuối cùng về lại tâm. Không ghi file; người gọi tự lưu Data nếu Success.
    /// </summary>
    public static class AutoCalibrator
    {
        public const double MaxHalfSpanMm = 60;          // nửa bề rộng lưới tối đa tính từ tâm
        public const double MaxRotationDeg = 30;

        private const double ArriveToleranceMm = 0.05;
        private const double ArriveToleranceDeg = 0.05;
        private static readonly TimeSpan ArriveTimeout = TimeSpan.FromSeconds(20);

        /// <summary>Kiểm tra thông số. Trả về lỗi hoặc null nếu hợp lệ.</summary>
        public static string? Validate(AutoCalibrationPlan plan)
        {
            if (plan.CountX < 2 || plan.CountX > 7 || plan.CountY < 2 || plan.CountY > 7)
                return "Số điểm mỗi hướng phải từ 2 đến 7.";
            if (plan.StepMm < 0.5 || plan.StepMm > 50)
                return "Bước lưới phải từ 0.5 đến 50 mm.";
            double halfX = (plan.CountX - 1) * plan.StepMm / 2;
            double halfY = (plan.CountY - 1) * plan.StepMm / 2;
            if (halfX > MaxHalfSpanMm || halfY > MaxHalfSpanMm)
                return $"Vùng quét quá rộng (nửa bề rộng tối đa {MaxHalfSpanMm} mm). Giảm số điểm hoặc bước.";
            foreach (double a in plan.RotationsDeg)
                if (!double.IsFinite(a) || Math.Abs(a) > MaxRotationDeg)
                    return $"Góc xoay kiểm tra phải trong ±{MaxRotationDeg}°.";
            if (plan.SpeedPercent < 1 || plan.SpeedPercent > 100) return "Tốc độ phải từ 1 đến 100 %.";
            if (plan.SettleMs < 0 || plan.SettleMs > 10_000) return "Thời gian chờ ổn định phải từ 0 đến 10000 ms.";
            if (plan.MinValidPoints < 3) return "Số điểm hợp lệ tối thiểu phải từ 3 trở lên.";
            if (plan.MinValidPoints > plan.CountX * plan.CountY)
                return "Số điểm hợp lệ tối thiểu lớn hơn tổng số điểm lưới.";
            if (!double.IsFinite(plan.MaxRmsMm) || plan.MaxRmsMm <= 0) return "Sai số RMS tối đa phải lớn hơn 0.";
            if (plan.Averages is < 1 or > 20) return "Số lần chụp trung bình mỗi điểm phải từ 1 đến 20.";
            if (!double.IsFinite(plan.MaxErrorMm) || plan.MaxErrorMm < 0) return "Sai số lớn nhất của một điểm không được âm (0 = không kiểm tra).";
            if (!double.IsFinite(plan.MaxRotationRmsMm) || plan.MaxRotationRmsMm <= 0) return "Sai số tối đa của calib xoay phải lớn hơn 0.";
            if (!double.IsFinite(plan.MaxAngleErrorDeg) || plan.MaxAngleErrorDeg <= 0) return "Lệch góc tối đa phải lớn hơn 0.";
            return null;
        }

        /// <summary>Các điểm lưới, đi theo hình rắn (zigzag) để robot không phải chạy ngược về đầu hàng.</summary>
        public static IReadOnlyList<RobotPoint> BuildGrid(AutoCalibrationPlan plan)
        {
            var list = new List<RobotPoint>();
            for (int j = 0; j < plan.CountY; j++)
            {
                double dy = (j - (plan.CountY - 1) / 2.0) * plan.StepMm;
                for (int k = 0; k < plan.CountX; k++)
                {
                    int i = j % 2 == 0 ? k : plan.CountX - 1 - k;
                    double dx = (i - (plan.CountX - 1) / 2.0) * plan.StepMm;
                    list.Add(plan.Center with { X = plan.Center.X + dx, Y = plan.Center.Y + dy });
                }
            }
            return list;
        }

        public static async Task<AutoCalibrationResult> RunAsync(
            IRobotDriver robot,
            Func<CancellationToken, Task<Pose2D?>> measure,
            AutoCalibrationPlan plan,
            IProgress<string> progress,
            CancellationToken ct,
            Action<CaptureSample>? sample = null)
        {
            var points = new List<CalibrationPoint>();
            var warnings = new List<string>();

            string? invalid = Validate(plan);
            if (invalid != null) return Fail(invalid, points, warnings);
            if (!robot.IsConnected) return Fail("Robot chưa kết nối.", points, warnings);
            if (!robot.HasControl)
                return Fail("Chưa chiếm quyền điều khiển robot (tab Điều khiển của robot → Chiếm quyền).", points, warnings);

            var grid = BuildGrid(plan);
            progress.Report($"Hiệu chuẩn tự động: {grid.Count} điểm lưới, bước {plan.StepMm} mm, tốc độ {plan.SpeedPercent} %.");

            var speed = await robot.SetSpeedAsync(plan.SpeedPercent, ct);
            if (!speed.Accepted) return Fail($"Không đặt được tốc độ: {speed.Error}", points, warnings);

            try
            {
                // ---------- Pha 1: lưới tịnh tiến ----------
                int skipped = 0;
                for (int n = 0; n < grid.Count; n++)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = grid[n];
                    progress.Report($"[{n + 1}/{grid.Count}] Đi tới X {target.X:F2}, Y {target.Y:F2}...");

                    var arrived = await GoAndSettleAsync(robot, target, plan, ct);
                    if (arrived.Error != null) return Fail(arrived.Error, points, warnings);

                    var (pose, spreadPx) = await MeasureAveragedAsync(measure, plan.Averages, ct);
                    if (pose is not { } p || !p.IsFinite)
                    {
                        sample?.Invoke(new CaptureSample("Lưới", n + 1, arrived.Actual.X, arrived.Actual.Y, arrived.Actual.GetAxis(plan.RzAxis),
                                                         null, null, null, "Không thấy mốc"));
                        skipped++;
                        progress.Report($"    Không thấy mốc ở điểm này: bỏ qua.");
                        continue;
                    }

                    // Dùng vị trí robot ĐỌC LẠI sau khi dừng, không dùng vị trí lệnh
                    var actual = arrived.Actual;
                    points.Add(new CalibrationPoint(p.Column, p.Row, actual.X, actual.Y));
                    sample?.Invoke(new CaptureSample("Lưới", n + 1, actual.X, actual.Y, actual.GetAxis(plan.RzAxis),
                                                     p.Row, p.Column, p.AngleDeg,
                                                     plan.Averages > 1 ? $"TB {plan.Averages} lần, lệch chuẩn ảnh {spreadPx:F3} px" : ""));
                    progress.Report($"    pixel ({p.Column:F2}, {p.Row:F2}) ↔ robot ({actual.X:F3}, {actual.Y:F3})");
                }

                if (skipped > 0) warnings.Add($"{skipped} điểm không đo được và đã bị bỏ qua.");

                if (points.Count < plan.MinValidPoints)
                    return Fail($"Chỉ đo được {points.Count} điểm (cần ít nhất {plan.MinValidPoints}). " +
                                "Kiểm tra ánh sáng, nét, và vùng quét có nằm trong vùng nhìn của camera không.", points, warnings);

                // ---------- Tính ma trận ----------
                CalibrationData data;
                Affine2D affine;
                try
                {
                    data = CalibrationStore.Create(points);
                    affine = data.ToAffine();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    return Fail("Không tính được ma trận: " + ex.Message, points, warnings);
                }

                progress.Report($"Ma trận: {points.Count} điểm, RMS {data.RmsMm:F3} mm, lớn nhất {data.MaxErrorMm:F3} mm, " +
                                $"{affine.MillimetersPerPixel:F4} mm/pixel.");

                ReportResiduals(points, affine, progress, sample, warnings);

                // ---------- Pha 2: kiểm tra dấu góc ----------
                if (plan.RotationsDeg.Count > 0)
                {
                    var (rotWarn, fit) = await CheckRotationAsync(robot, measure, plan, affine, progress, ct, sample);
                    warnings.AddRange(rotWarn);

                    // ---------- Calib xoay: bù độ lệch giữa mốc và trục xoay robot vào ma trận ----------
                    if (fit != null)
                    {
                        double limit = plan.MaxRotationRmsMm;
                        double length = Math.Sqrt(fit.OffsetX * fit.OffsetX + fit.OffsetY * fit.OffsetY);
                        if (fit.RmsMm > limit)
                            warnings.Add($"Calib xoay: sai số {fit.RmsMm:F3} mm vượt {limit:F2} mm, KHÔNG dùng kết quả xoay (mốc đo không ổn định, hoặc robot xoay không quanh TCP). Ma trận vẫn lưu như chưa calib xoay.");
                        else
                        {
                            data.Matrix[2] += fit.OffsetX;
                            data.Matrix[5] += fit.OffsetY;
                            data.MarkOffsetXMm = fit.OffsetX;
                            data.MarkOffsetYMm = fit.OffsetY;
                            data.RotationRmsMm = fit.RmsMm;
                            progress.Report($"Calib xoay: mốc cách trục xoay robot {length:F3} mm (X {fit.OffsetX:F3}, Y {fit.OffsetY:F3}), " +
                                            $"sai số {fit.RmsMm:F3} mm ({fit.Samples} góc). Đã cộng vào ma trận.");
                        }
                    }
                }

                if (plan.MaxErrorMm > 0 && data.MaxErrorMm > plan.MaxErrorMm)
                    return new AutoCalibrationResult(false,
                        $"Có điểm sai {data.MaxErrorMm:F3} mm, vượt giới hạn một điểm {plan.MaxErrorMm:F3} mm: KHÔNG lưu. " +
                        "Xem điểm nào lệch nhiều trong nhật ký (thường do mốc đo sai ở điểm đó hoặc robot chưa dừng hẳn).",
                        points, data, warnings);

                if (data.RmsMm > plan.MaxRmsMm)
                    return new AutoCalibrationResult(false,
                        $"Sai số RMS {data.RmsMm:F3} mm vượt giới hạn {plan.MaxRmsMm:F3} mm: KHÔNG lưu. " +
                        "Thường do robot chưa dừng hẳn lúc chụp, mốc đo không ổn định, hoặc TCP/độ cao Z sai.",
                        points, data, warnings);

                return new AutoCalibrationResult(true,
                    $"Xong: {points.Count} điểm, RMS {data.RmsMm:F3} mm, lớn nhất {data.MaxErrorMm:F3} mm.",
                    points, data, warnings);
            }
            catch (OperationCanceledException)
            {
                return Fail("Đã dừng theo yêu cầu. Robot dừng ở vị trí hiện tại, kiểm tra trước khi chạy tiếp.", points, warnings);
            }
            finally
            {
                // Về lại tâm (cố gắng, không ném lỗi). Không dùng token đã hủy để vẫn về được tâm.
                try
                {
                    using var cts = new CancellationTokenSource(ArriveTimeout);
                    var back = await GoAndSettleAsync(robot, plan.Center, plan with { SettleMs = 0 }, cts.Token);
                    progress.Report(back.Error == null ? "Đã về lại điểm tâm." : "Chưa về được điểm tâm: " + back.Error);
                }
                catch (Exception ex)
                {
                    progress.Report("Chưa về được điểm tâm: " + ex.Message);
                }
            }
        }

        // ============================================================
        // Kiểm tra dấu góc: xoay C quanh tâm, so góc đo từ ảnh với góc robot đã xoay
        // ============================================================

        /// <summary>
        /// Chụp + đo nhiều lần tại cùng một điểm rồi lấy trung bình (góc lấy trung bình vòng). Cần đo được ít nhất một nửa số lần,
        /// không thì coi như không thấy. spreadPx: độ lệch chuẩn vị trí giữa các lần (pixel), cho biết độ ồn của phép đo.
        /// </summary>
        private static async Task<(Pose2D? Pose, double SpreadPx)> MeasureAveragedAsync(
            Func<CancellationToken, Task<Pose2D?>> measure, int count, CancellationToken ct)
        {
            if (count <= 1) return (await measure(ct), 0);

            var ok = new List<Pose2D>();
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (await measure(ct) is { } p && p.IsFinite) ok.Add(p);
            }
            if (ok.Count == 0 || ok.Count * 2 < count) return (null, 0);

            double row = ok.Average(p => p.Row), col = ok.Average(p => p.Column);
            double sin = ok.Sum(p => Math.Sin(p.AngleDeg * Math.PI / 180.0));
            double cos = ok.Sum(p => Math.Cos(p.AngleDeg * Math.PI / 180.0));
            double angle = Pose2D.NormalizeAngle(Math.Atan2(sin, cos) * 180.0 / Math.PI);
            double spread = Math.Sqrt(ok.Average(p => (p.Row - row) * (p.Row - row) + (p.Column - col) * (p.Column - col)));
            return (new Pose2D(row, col, angle), spread);
        }

        /// <summary>
        /// Báo sai số còn lại của từng điểm sau khi khớp (dx, dy theo mm), để biết lỗi nằm ở đâu:
        ///  - sai số ngẫu nhiên đều khắp lưới: nhiễu đo (tăng số lần trung bình, ánh sáng, nét, mốc);
        ///  - sai số tăng dần ra phía rìa ảnh: méo thấu kính, ma trận affine không đủ (cần calib camera bù méo trước);
        ///  - sai số theo hướng robot đi (dương ở một nửa lưới, âm ở nửa kia): robot dừng chưa ổn định hoặc có độ rơ.
        /// </summary>
        private static void ReportResiduals(
            IReadOnlyList<CalibrationPoint> points, Affine2D affine, IProgress<string> progress,
            Action<CaptureSample>? sample, List<string> warnings)
        {
            double cx = points.Average(p => p.PixelX), cy = points.Average(p => p.PixelY);
            var rows = new List<(double Radius, double Err)>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var (x, y) = affine.Apply(p.PixelX, p.PixelY);
                double dx = x - p.RobotX, dy = y - p.RobotY;
                double err = Math.Sqrt(dx * dx + dy * dy);
                rows.Add((Math.Sqrt((p.PixelX - cx) * (p.PixelX - cx) + (p.PixelY - cy) * (p.PixelY - cy)), err));
                sample?.Invoke(new CaptureSample("Sai số", i + 1, dx, dy, null, p.PixelY, p.PixelX, null,
                                                 $"dx, dy = sai số còn lại (mm); |e| = {err:F3} mm"));
            }

            double mmPerPx = affine.MillimetersPerPixel;
            double rms = Math.Sqrt(rows.Average(r => r.Err * r.Err));
            progress.Report($"Sai số RMS {rms:F3} mm = {(mmPerPx > 0 ? rms / mmPerPx : 0):F2} pixel (1 pixel = {mmPerPx:F4} mm).");

            if (rows.Count >= 6)
            {
                var sorted = rows.OrderBy(r => r.Radius).ToList();
                int half = sorted.Count / 2;
                double inner = Math.Sqrt(sorted.Take(half).Average(r => r.Err * r.Err));
                double outer = Math.Sqrt(sorted.Skip(half).Average(r => r.Err * r.Err));
                progress.Report($"Sai số trong ({half} điểm gần tâm ảnh): {inner:F3} mm; ngoài (rìa): {outer:F3} mm.");
                if (outer > 0.15 && outer > inner * 1.8)
                    warnings.Add($"Sai số tăng ra phía rìa ảnh (trong {inner:F3} mm, ngoài {outer:F3} mm): dấu hiệu méo thấu kính. " +
                                 "Ma trận affine không bù được méo; cần calib camera bù méo, hoặc thu nhỏ vùng quét.");
            }
        }

        private static async Task<(List<string> Warnings, RotationFit? Fit)> CheckRotationAsync(
            IRobotDriver robot, Func<CancellationToken, Task<Pose2D?>> measure, AutoCalibrationPlan plan,
            Affine2D affine, IProgress<string> progress, CancellationToken ct, Action<CaptureSample>? sample = null)
        {
            var warnings = new List<string>();

            var centerArrived = await GoAndSettleAsync(robot, plan.Center, plan, ct);
            if (centerArrived.Error != null) { warnings.Add("Kiểm tra góc: " + centerArrived.Error); return (warnings, null); }
            var basePose = await measure(ct);
            if (basePose is not { } b || !b.IsFinite)
            {
                warnings.Add("Kiểm tra góc: không đo được mốc ở góc 0.");
                return (warnings, null);
            }
            sample?.Invoke(new CaptureSample("Xoay", 0, centerArrived.Actual.X, centerArrived.Actual.Y,
                                             centerArrived.Actual.GetAxis(plan.RzAxis), b.Row, b.Column, b.AngleDeg, "Góc 0"));
            double baseRz = affine.TransformAngle(b.AngleDeg);

            double worst = 0;
            int rotIndex = 0;
            var samples = new List<(double Deg, double Qx, double Qy, double Tx, double Ty)>();
            foreach (double a in plan.RotationsDeg.Where(x => Math.Abs(x) > 1e-6))
            {
                ct.ThrowIfCancellationRequested();
                var target = plan.Center.WithAxis(plan.RzAxis, plan.Center.GetAxis(plan.RzAxis) + a);
                progress.Report($"Kiểm tra góc: xoay {a:+0.##;-0.##}°...");

                var arrived = await GoAndSettleAsync(robot, target, plan, ct);
                if (arrived.Error != null) { warnings.Add("Kiểm tra góc: " + arrived.Error); break; }

                var pose = await measure(ct);
                rotIndex++;
                if (pose is not { } p || !p.IsFinite)
                {
                    sample?.Invoke(new CaptureSample("Xoay", rotIndex, arrived.Actual.X, arrived.Actual.Y,
                                                     arrived.Actual.GetAxis(plan.RzAxis), null, null, null, "Không thấy mốc"));
                    warnings.Add($"Kiểm tra góc: không đo được mốc ở {a:+0.##;-0.##}°.");
                    continue;
                }

                double measured = Pose2D.NormalizeAngle(affine.TransformAngle(p.AngleDeg) - baseRz);
                double expected = Pose2D.NormalizeAngle(arrived.Actual.GetAxis(plan.RzAxis) - centerArrived.Actual.GetAxis(plan.RzAxis));
                double err = Math.Abs(Pose2D.NormalizeAngle(measured - expected));
                worst = Math.Max(worst, err);
                var (qx, qy) = affine.Apply(p.Column, p.Row);
                samples.Add((measured, qx, qy, arrived.Actual.X, arrived.Actual.Y));
                sample?.Invoke(new CaptureSample("Xoay", rotIndex, arrived.Actual.X, arrived.Actual.Y,
                                                 arrived.Actual.GetAxis(plan.RzAxis), p.Row, p.Column, p.AngleDeg,
                                                 $"Robot xoay {expected:+0.##;-0.##}°, ảnh {measured:+0.##;-0.##}°, lệch {err:F2}°"));
                progress.Report($"    robot xoay {expected:+0.##;-0.##}°, ảnh đo {measured:+0.##;-0.##}°, lệch {err:F2}°");

                if (err > plan.MaxAngleErrorDeg && Math.Abs(Pose2D.NormalizeAngle(measured + expected)) < plan.MaxAngleErrorDeg * 2)
                    warnings.Add($"Dấu góc có vẻ NGƯỢC: robot xoay {expected:+0.##;-0.##}° nhưng ảnh đo {measured:+0.##;-0.##}°. " +
                                 "Dùng 'Đảo chiều góc' khi gửi tọa độ, hoặc kiểm tra quy ước trục xoay.");
            }

            if (worst > plan.MaxAngleErrorDeg && !warnings.Any(w => w.Contains("NGƯỢC")))
                warnings.Add($"Góc đo từ ảnh lệch tới {worst:F2}° so với góc robot xoay (ngưỡng {plan.MaxAngleErrorDeg}°). " +
                             "Kiểm tra mốc có đối xứng không (mốc tròn không có hướng sẽ đo góc không ổn định).");
            return (warnings, FitMarkOffset(samples));
        }

        /// <summary>
        /// Tìm vector d từ TCP tới mốc. Ma trận thô cho q = vị trí mốc thật − d, nên khi robot xoay góc m quanh TCP (đứng yên ở T):
        /// q − T = (R(m) − I)·d. Giải bình phương tối thiểu cho d (2 ẩn), dùng góc m đo được từ ảnh nên đúng dù dấu robot ngược.
        /// Cần ít nhất 1 góc khác 0 (từ 2 góc trở lên mới có sai số để đánh giá).
        /// </summary>
        public static RotationFit? FitMarkOffset(IReadOnlyList<(double Deg, double Qx, double Qy, double Tx, double Ty)> samples)
        {
            var use = samples.Where(s => Math.Abs(s.Deg) >= 1.0).ToList();
            if (use.Count < 1) return null;

            double nx = 0, ny = 0, den = 0;
            foreach (var s in use)
            {
                double r = s.Deg * Math.PI / 180.0, c = Math.Cos(r), sn = Math.Sin(r);
                double bx = s.Qx - s.Tx, by = s.Qy - s.Ty;
                nx += (c - 1) * bx + sn * by;
                ny += -sn * bx + (c - 1) * by;
                den += 2 - 2 * c;
            }
            if (den < 1e-9) return null;
            double dx = nx / den, dy = ny / den;

            double sum = 0;
            foreach (var s in use)
            {
                double r = s.Deg * Math.PI / 180.0, c = Math.Cos(r), sn = Math.Sin(r);
                double ex = (c - 1) * dx - sn * dy - (s.Qx - s.Tx);
                double ey = sn * dx + (c - 1) * dy - (s.Qy - s.Ty);
                sum += ex * ex + ey * ey;
            }
            return new RotationFit(dx, dy, Math.Sqrt(sum / use.Count), use.Count);
        }

        // ============================================================
        // Di chuyển và chờ tới nơi
        // ============================================================

        private sealed record Arrival(RobotPoint Actual, string? Error);

        /// <summary>Đi thẳng tới đích, chờ vị trí ổn định tại đích, chờ thêm SettleMs, rồi đọc vị trí thật.</summary>
        private static async Task<Arrival> GoAndSettleAsync(
            IRobotDriver robot, RobotPoint target, AutoCalibrationPlan plan, CancellationToken ct)
        {
            // MVS (đường thẳng) bắt buộc cờ tư thế điểm đầu và điểm cuối GIỐNG NHAU, nếu không robot báo lỗi 2810
            // "Posture flag is disagree" và treo lỗi. Kiểm tra trước để báo rõ, không gửi lệnh sai.
            var before = await robot.GetCurrentPointAsync(ct);
            if (before.Ok && (before.Point.Flag1 != target.Flag1 || before.Point.Flag2 != target.Flag2))
                return new Arrival(default,
                    $"Cờ tư thế của robot hiện tại ({before.Point.Flag1},{before.Point.Flag2}) khác cờ của điểm đích ({target.Flag1},{target.Flag2}). " +
                    "Lệnh đi thẳng cần hai cờ giống nhau (lỗi 2810). Đưa robot về đúng tư thế của điểm tâm rồi chạy lại, " +
                    "hoặc để trống ô 'Điểm tâm' để dùng vị trí hiện tại làm tâm. KHÔNG gửi lệnh.");

            var move = await robot.MoveToAsync(target, linear: true, enforceDistanceLimit: true, ct);
            if (!move.Accepted) return new Arrival(default, $"Robot không nhận lệnh di chuyển: {move.Error}");

            var deadline = DateTime.UtcNow + ArriveTimeout;
            int stable = 0;
            RobotPoint last = default;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    return new Arrival(default, "Quá thời gian chờ robot tới nơi (kiểm tra servo, lỗi, tốc độ).");

                var now = await robot.GetCurrentPointAsync(ct);
                if (!now.Ok) return new Arrival(default, $"Không đọc được vị trí robot: {now.Error}");
                last = now.Point;

                bool near = Math.Abs(last.X - target.X) <= ArriveToleranceMm
                         && Math.Abs(last.Y - target.Y) <= ArriveToleranceMm
                         && Math.Abs(Pose2D.NormalizeAngle(last.GetAxis(plan.RzAxis) - target.GetAxis(plan.RzAxis))) <= ArriveToleranceDeg;

                stable = near ? stable + 1 : 0;
                if (stable >= 2) break;                  // hai lần đọc liên tiếp đều ở đích
                await Task.Delay(150, ct);
            }

            if (plan.SettleMs > 0) await Task.Delay(plan.SettleMs, ct);

            var final = await robot.GetCurrentPointAsync(ct);
            return final.Ok ? new Arrival(final.Point, null) : new Arrival(last, null);
        }

        private static AutoCalibrationResult Fail(string message, IReadOnlyList<CalibrationPoint> points, IReadOnlyList<string> warnings)
            => new(false, message, points, null, warnings);
    }
}
