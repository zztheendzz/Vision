using Communication.Configuration;
using Communication.Devices;
using Communication.Drivers;
using Melfa.Robot;   // Melfa.Robot 0.1.22.6 (CHƯA kiểm chứng namespace / tên lớp: chỉnh trong phần "GỌI THƯ VIỆN" bên dưới)
using System.Globalization;
using System.IO;

namespace Communication.Mitsubishi
{
    /// <summary>
    /// Robot Mitsubishi MELFA qua Melfa.Robot (R-protocol; cổng Ethernet của bộ điều khiển đặt chế độ Server).
    /// Mọi lời gọi tới thư viện nằm gọn trong 4 hàm ở phần "GỌI THƯ VIỆN" để dễ sửa khi API thật khác.
    /// "Accepted" chỉ có nghĩa là lệnh đã gửi mà không lỗi, KHÔNG có nghĩa robot đã thực hiện xong.
    /// Độ bù được ghi vào một biến điểm trung gian; điểm đã bù có thể ghi đè lên điểm đã chọn. Các lệnh ghi điểm (P1=(...)(...)) và cách đọc điểm CHƯA kiểm chứng trên robot thật; lệnh NG mặc định để trống (báo lỗi, không gửi bừa).
    /// </summary>
    public sealed class MitsubishiRobot : IRobotDriver, IProbeable
    {
        public sealed record Settings(
            string Ip, int Port, int RobotNo, string ClientName, bool EnableOnConnect,
            string RejectTemplate, double MaxOffsetMm, double MaxOffsetDeg,
            RobotPointSettings Points, int PointCount = 20, string PointProgram = "", bool TakeControlOnConnect = false,
            double MaxMoveMm = 300);

        private readonly Settings _s;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private object? _robot;
        private bool _connected;
        private volatile bool _hasControl;
        private Communication.Transport.TcpTransport? _packet;      // đường gửi gói tin độ lệch (chế độ Packet)

        public MitsubishiRobot(Settings settings) => _s = settings;

        public static MitsubishiRobot FromConfig(DeviceConfig d) => new(new Settings(
            d.GetString(DeviceSchemas.KeyIp),
            d.GetInt(DeviceSchemas.KeyPort, 10009),
            d.GetInt("RobotNo", 1),
            d.GetString("ClientName"),
            d.GetBool("EnableOnConnect"),
            d.GetString("RejectCommand"),
            d.GetDouble("MaxOffsetMm", 50),
            d.GetDouble("MaxOffsetDeg", 30),
            MitsubishiPointSettings.Read(d),
            d.GetInt("PointCount", 20),
            d.GetString("PointProgram").Trim(),
            d.GetBool("TakeControl"),
            d.GetDouble("MaxMoveMm", 300)));

        public RobotPointSettings PointSettings => _s.Points;
        public bool HasControl => _connected && _hasControl;

        // ---------- Quyền điều khiển ----------

        public Task<RobotReply> TakeControlAsync(bool stopAndReset, CancellationToken ct = default)
            => ControlAsync("Đã chiếm quyền điều khiển" + (stopAndReset ? " (đã dừng + reset chương trình)" : ""), () =>
            {
                var r = Lib;
                r.OperationEnabled = true;
                if (stopAndReset) StopAndResetCore(r);
                _hasControl = true;
            }, needControl: false, ct);

        public Task<RobotReply> ReleaseControlAsync(CancellationToken ct = default)
            => ControlAsync("Đã trả quyền điều khiển", () =>
            {
                Lib.OperationEnabled = false;
                _hasControl = false;
            }, needControl: false, ct);

        public Task<RobotReply> StopAndResetAsync(CancellationToken ct = default)
            => ControlAsync("Đã dừng chương trình và reset", () => StopAndResetCore(Lib), needControl: true, ct);

        public Task<RobotReply> SetServoAsync(bool on, CancellationToken ct = default)
            => ControlAsync(on ? "Đã bật servo" : "Đã tắt servo", () => Lib.ServoOn = on, needControl: true, ct);

        public Task<RobotReply> MoveRelativeAsync(double dxMm, double dyMm, double dzMm, double drzDeg, CancellationToken ct = default)
        {
            string? invalid = Validate(dxMm, dyMm, drzDeg);
            if (invalid == null && (!double.IsFinite(dzMm) || Math.Abs(dzMm) > _s.MaxOffsetMm))
                invalid = $"Bước Z {dzMm:0.###} mm vượt giới hạn ±{_s.MaxOffsetMm} mm, không di chuyển.";
            if (invalid != null) return Task.FromResult(RobotReply.Fail(invalid));

            var d = new RobotPoint(dxMm, dyMm, dzMm, 0, 0, 0, 0, 0).WithAxis(_s.Points.RzAxis, drzDeg);
            string delta = string.Create(CultureInfo.InvariantCulture,
                $"({d.X:0.###},{d.Y:0.###},{d.Z:0.###},{d.A:0.###},{d.B:0.###},{d.C:0.###})");
            // MVS: đường thẳng; P_CURR: vị trí hiện tại. CHƯA kiểm chứng trên robot thật
            string command = "MVS P_CURR+" + delta;
            return ControlAsync($"Đã gửi: {command}", () => Lib.Execute(command), needControl: true, ct);
        }

        public async Task<RobotPointReply> GetCurrentPointAsync(CancellationToken ct = default)
        {
            if (!_connected) return RobotPointReply.Fail("Robot chưa kết nối.");
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try { return RobotPointReply.Success(await Task.Run(LibReadCurrent, ct).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return RobotPointReply.Fail(ex.Message); }
            finally { _lock.Release(); }
        }

        /// <summary>
        /// Di chuyển tới tọa độ tuyệt đối (X,Y,Z,A,B,C theo hệ gốc robot). Cần quyền điều khiển + servo ON.
        /// An toàn: phải đọc được vị trí hiện tại và khoảng cách XYZ tới đích không quá "Giới hạn di chuyển" (mặc định 300 mm).
        /// Cờ cấu hình lấy theo đích (thường lấy từ vị trí hiện tại) để tay robot không đổi tư thế.
        /// </summary>
        public async Task<RobotReply> MoveToAsync(RobotPoint target, bool linear, bool enforceDistanceLimit = true, CancellationToken ct = default)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối.");
            if (!_hasControl) return RobotReply.Fail("Chưa có quyền điều khiển. Bấm 'Chiếm quyền' trước.");
            if (!new[] { target.X, target.Y, target.Z, target.A, target.B, target.C }.All(double.IsFinite))
                return RobotReply.Fail("Tọa độ đích không hợp lệ.");

            var now = await GetCurrentPointAsync(ct).ConfigureAwait(false);
            string distanceText = "";
            if (now.Ok)
            {
                double dx = target.X - now.Point.X, dy = target.Y - now.Point.Y, dz = target.Z - now.Point.Z;
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                distanceText = $" (cách {distance:0.#} mm)";
                if (enforceDistanceLimit && distance > _s.MaxMoveMm)
                    return RobotReply.Fail($"Đích cách vị trí hiện tại {distance:0.#} mm, vượt giới hạn {_s.MaxMoveMm:0.#} mm, không di chuyển.");
            }
            else if (enforceDistanceLimit)
            {
                return RobotReply.Fail("Không đọc được vị trí hiện tại nên không kiểm tra được khoảng cách, không di chuyển: " + now.Error);
            }

            // Gán điểm vào biến tạm trước (chờ xong), rồi mới di chuyển tới biến đó. Gửi thẳng tọa độ trong MOV/MVS bị lỗi 2810
            // trên robot này, còn cách gán biến rồi "MVS biến" đã chạy được.
            const string TempPoint = "P99";   // dạng P+số như P4 bạn đã thử; tên chữ (PVIS) cần khai báo DEF POS nên có thể bị từ chối
            string assign = $"{TempPoint}={target.ToMelfa()}";
            string move = (linear ? "MVS " : "MOV ") + TempPoint;
            string command = $"{assign}; {move}";
            var reply = await ControlAsync($"Đã gửi: {command}{distanceText}", () =>
            {
                Lib.ExecuteBlocking(assign);
                Lib.Execute(move);
            }, needControl: true, ct)
                .ConfigureAwait(false);
            if (reply.Accepted) return reply;

            // Lỗi: kèm lệnh đã gửi và vị trí (có cờ) robot đọc được lúc gửi, để so cờ điểm đầu / điểm cuối khi gặp lỗi 2810
            string current = now.Ok ? now.Point.ToMelfa() : "không đọc được";
            // Góc các khớp J1..J6 (JPOSF) để biết J5 có gần 0° (ranh giới cờ) không
            string joints;
            try { joints = await Task.Run(() => Lib.CurrentPositionJ.ToString(), ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { joints = "không đọc được: " + ex.Message; }
            return RobotReply.Fail($"{reply.Error} [lệnh gửi: {command}; vị trí robot lúc gửi: {current}; các khớp: {joints}]");
        }

        public Task<RobotReply> SetSpeedAsync(int percent, CancellationToken ct = default)
        {
            byte value = (byte)Math.Clamp(percent, 1, 100);
            return ControlAsync($"Đã đặt tốc độ {value}%", () => Lib.Override = value, needControl: true, ct);
        }

        public async Task<string> GetPositionTextAsync(CancellationToken ct = default)
        {
            if (!_connected) return "Robot chưa kết nối";
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try { return await Task.Run(() => Lib.CurrentPositionP.ToString() ?? "", ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return "Không đọc được vị trí: " + ex.Message; }
            finally { _lock.Release(); }
        }

        // Chương trình không chạy thì STOP có thể báo lỗi: bỏ qua lỗi của hai lệnh này
        private static void StopAndResetCore(MelfaRobot r)
        {
            try { r.StopProgram(); } catch { }
            try { r.ResetProgram(); } catch { }
        }

        private async Task<RobotReply> ControlAsync(string okText, Action action, bool needControl, CancellationToken ct)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối.");
            if (needControl && !_hasControl) return RobotReply.Fail("Chưa có quyền điều khiển. Bấm 'Chiếm quyền' trước.");
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(action, ct).ConfigureAwait(false);
                return RobotReply.Ok(okText);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string hint = ex.Message.Contains("5600", StringComparison.Ordinal)
                    ? " → Bộ điều khiển robot đang ở trạng thái LỖI (alarm) nên không nhận lệnh. Xem mã lỗi trên teach pendant / RT ToolBox, " +
                      "xử lý nguyên nhân (va chạm, vượt giới hạn, dừng khẩn, servo...), reset lỗi, bật servo rồi thử lại."
                    : "";
                return RobotReply.Fail($"Lệnh điều khiển robot lỗi: {ex.Message}{hint}");
            }
            finally { _lock.Release(); }
        }

        private void RequireControl()
        {
            if (!_hasControl)
                throw new InvalidOperationException(
                    "Chưa có quyền điều khiển robot. Bấm 'Chiếm quyền' (hoặc tick 'Chiếm quyền điều khiển khi kết nối') trước khi đọc/ghi điểm.");
        }

        public bool IsConnected => _connected;

        /// <summary>
        /// Kiểm tra robot còn trên mạng không bằng ping, không được thì thử mở ổ cắm tới cổng robot. KHÔNG gọi thư viện Melfa.Robot
        /// (thư viện đang bị rút dây giữa chừng có thể ném lỗi ở luồng riêng của nó và làm sập chương trình).
        /// </summary>
        public async Task<string?> ProbeAsync(CancellationToken ct = default)
        {
            if (!_connected) return null;
            string ip = _s.Ip;
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ip, 1500).ConfigureAwait(false);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success) return null;
            }
            catch (Exception) { /* thử bằng ổ cắm bên dưới */ }

            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(1500);
                await client.ConnectAsync(ip, _s.Port, cts.Token).ConfigureAwait(false);
                return null;
            }
            catch (Exception) { return $"robot {ip} không trả lời (ping và cổng {_s.Port})"; }
        }

        public string Description => $"Mitsubishi MELFA {_s.Ip}:{_s.Port}";

        // ---------- Logic của mình (không phụ thuộc thư viện) ----------

        /// <summary>Thay {dx} {dy} {drz} {id} bằng số, luôn dùng dấu chấm thập phân.</summary>
        public static string FillTemplate(string template, double dx, double dy, double drz, int id)
        {
            var inv = CultureInfo.InvariantCulture;
            return template
                .Replace("{dx}", dx.ToString("0.###", inv))
                .Replace("{dy}", dy.ToString("0.###", inv))
                .Replace("{drz}", drz.ToString("0.###", inv))
                .Replace("{id}", id.ToString(inv));
        }

        /// <summary>Kiểm tra độ bù trước khi gửi. Trả về lỗi hoặc null.</summary>
        public string? Validate(double dx, double dy, double drz)
        {
            if (double.IsNaN(dx) || double.IsNaN(dy) || double.IsNaN(drz) ||
                double.IsInfinity(dx) || double.IsInfinity(dy) || double.IsInfinity(drz))
                return "Độ bù không phải số hợp lệ.";
            if (Math.Abs(dx) > _s.MaxOffsetMm || Math.Abs(dy) > _s.MaxOffsetMm)
                return $"Độ bù ({dx:0.###}, {dy:0.###}) mm vượt giới hạn ±{_s.MaxOffsetMm} mm, không gửi.";
            if (Math.Abs(drz) > _s.MaxOffsetDeg)
                return $"Góc bù {drz:0.###}° vượt giới hạn ±{_s.MaxOffsetDeg}°, không gửi.";
            return null;
        }

        public string? ValidateOffset(double dx, double dy, double drz) => Validate(dx, dy, drz);

        /// <summary>Chế độ "độ lệch": ghi (dX, dY, 0, Rz ở trục đã chọn) vào biến điểm trung gian.</summary>
        public async Task<RobotReply> SendOffsetAsync(double dxMm, double dyMm, double drzDeg, int cycleId, CancellationToken ct = default)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối, không gửi độ bù.");
            if (_s.Points.Mode == RobotSendMode.ViaPlc) return RobotReply.Fail("Chế độ 'Qua PLC': độ bù được ghi vào thanh ghi Calib của PLC, không gửi trực tiếp cho robot.");
            if (_s.Points.Mode == RobotSendMode.Packet)
            {
                string? bad = Validate(dxMm, dyMm, drzDeg);
                if (bad != null) return RobotReply.Fail(bad);
                return await SendPacketAsync(p => OffsetPacket.Build(p.OffsetTemplate, dxMm, dyMm, 0, drzDeg, cycleId, p.Decimals, p.Scale), ct)
                    .ConfigureAwait(false);
            }
            string name = _s.Points.OffsetPoint;
            if (!RobotPoint.IsValidName(name)) return RobotReply.Fail($"Tên biến điểm trung gian '{name}' không hợp lệ.");
            string? invalid = Validate(dxMm, dyMm, drzDeg);
            if (invalid != null) return RobotReply.Fail(invalid);

            var point = new RobotPoint(dxMm, dyMm, 0, 0, 0, 0, 0, 0).WithAxis(_s.Points.RzAxis, drzDeg);
            return await SendAsync($"{name}={point.ToMelfa()}", ct).ConfigureAwait(false);
        }

        // ---------- Điểm: liệt kê, đọc, ghi ----------

        // Đọc/ghi điểm dùng chương trình mở ở "slot soạn thảo" của bộ điều khiển (LOAD, LISTP, VAL=, SAVE):
        // thư viện Melfa.Robot hiện chưa có lệnh đọc biến toàn cục (VAL/GVAL còn TODO).
        // Vì vậy điểm nằm TRONG MỘT CHƯƠNG TRÌNH (ô "Chương trình chứa điểm"); chương trình đó không được đang chạy.

        public async Task<IReadOnlyList<string>> ListPointsAsync(CancellationToken ct = default)
        {
            if (!_connected) throw new InvalidOperationException("Robot chưa kết nối.");
            RequireProgram();
            RequireControl();
            int count = Math.Clamp(_s.PointCount, 1, 200);
            var names = Enumerable.Range(1, count).Select(i => "P" + i.ToString(CultureInfo.InvariantCulture)).ToList();

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    var found = new List<string>();
                    string? lastError = null;
                    EditSession(() =>
                    {
                        foreach (string name in names)
                        {
                            try { LibReadPoint(name); found.Add(name); }
                            catch (Exception ex) { lastError = ex.Message; }
                        }
                    }, save: false);
                    if (found.Count == 0) throw new IOException(lastError ?? "Không đọc được điểm nào từ chương trình " + _s.PointProgram);
                    return (IReadOnlyList<string>)found;
                }, ct).ConfigureAwait(false);
            }
            finally { _lock.Release(); }
        }

        public async Task<RobotPointReply> ReadPointAsync(string name, CancellationToken ct = default)
        {
            if (!_connected) return RobotPointReply.Fail("Robot chưa kết nối.");
            if (!RobotPoint.IsValidName(name)) return RobotPointReply.Fail($"Tên điểm '{name}' không hợp lệ.");
            if (string.IsNullOrWhiteSpace(_s.PointProgram))
                return RobotPointReply.Fail("Chưa nhập 'Chương trình chứa điểm' trong cấu hình robot.");

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    RobotPoint point = default;
                    EditSession(() => point = LibReadPoint(name), save: false);
                    return RobotPointReply.Success(point);
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RobotPointReply.Fail($"Đọc điểm {name} lỗi: {ex.Message}");
            }
            finally { _lock.Release(); }
        }

        public async Task<RobotReply> WritePointAsync(string name, RobotPoint point, CancellationToken ct = default)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối, không ghi điểm.");
            if (!RobotPoint.IsValidName(name)) return RobotReply.Fail($"Tên điểm '{name}' không hợp lệ.");
            if (string.IsNullOrWhiteSpace(_s.PointProgram))
                return RobotReply.Fail("Chưa nhập 'Chương trình chứa điểm' trong cấu hình robot, không ghi điểm.");

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string value = point.ToMelfa();
                string note = "";
                await Task.Run(() =>
                {
                    EditSession(() => Lib.ProgramSetVariable(name, value), save: true);

                    // Đọc lại từ bộ điều khiển để chắc giá trị đã đổi (không tin vào việc lệnh không báo lỗi)
                    RobotPoint back = default;
                    EditSession(() => back = LibReadPoint(name), save: false);
                    if (!SamePoint(back, point))
                        throw new InvalidOperationException(
                            $"Đã gửi lệnh ghi nhưng đọc lại {name} vẫn là {back.ToMelfa()} (cần {value}). Bộ điều khiển chưa nhận giá trị mới.");

                    // Chương trình đang nạp ở slot chạy vẫn là bản cũ: nạp lại để robot dùng giá trị mới
                    try { Lib.LoadProgram(_s.PointProgram); note = "; đã nạp lại chương trình vào slot 1"; }
                    catch (Exception ex) { note = $"; CHƯA nạp lại được chương trình vào slot chạy ({ex.Message})"; }
                }, ct).ConfigureAwait(false);
                return RobotReply.Ok($"{_s.PointProgram}: {name}={value}, đọc lại khớp{note}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RobotReply.Fail($"Ghi điểm {name} lỗi: {ex.Message}");
            }
            finally { _lock.Release(); }
        }

        private static bool SamePoint(RobotPoint a, RobotPoint b)
            => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(a.Z - b.Z) < 0.01
               && Math.Abs(a.A - b.A) < 0.01 && Math.Abs(a.B - b.B) < 0.01 && Math.Abs(a.C - b.C) < 0.01
               && a.Flag1 == b.Flag1;

        private void RequireProgram()
        {
            if (string.IsNullOrWhiteSpace(_s.PointProgram))
                throw new InvalidOperationException("Chưa nhập 'Chương trình chứa điểm' trong cấu hình robot (tên chương trình có các điểm P1, P2...).");
        }

        /// <summary>Mở chương trình ở slot soạn thảo, chạy việc, rồi lưu (save=true) hoặc đóng bỏ thay đổi. Luôn đóng chương trình.</summary>
        private void EditSession(Action work, bool save)
        {
            RequireControl();
            var robot = Lib;
            robot.ProgramOpen(_s.PointProgram);
            bool done = false;
            try
            {
                work();
                if (save) { robot.ProgramSave(); done = true; }
            }
            finally
            {
                if (!done) { try { robot.ProgramNew(); } catch { } }   // đóng, bỏ thay đổi
            }
        }

        private static string Shorten(string? text)
        {
            text ??= "";
            return text.Length <= 120 ? text : text[..120] + "…";
        }

        public async Task<RobotReply> SendRejectAsync(int cycleId, CancellationToken ct = default)
        {
            if (!_connected) return RobotReply.Fail("Robot chưa kết nối, không báo NG.");
            if (_s.Points.Mode == RobotSendMode.ViaPlc) return RobotReply.Fail("Chế độ 'Qua PLC': báo NG bằng bit NG của PLC, không gửi cho robot.");
            if (_s.Points.Mode == RobotSendMode.Packet)
            {
                var ps = _s.Points.Packet;
                if (ps == null || string.IsNullOrEmpty(ps.RejectTemplate))
                    return RobotReply.Fail("Chưa cấu hình mẫu gói tin báo NG, không gửi gì.");
                return await SendPacketAsync(p => OffsetPacket.Build(p.RejectTemplate, 0, 0, 0, 0, cycleId, p.Decimals, p.Scale), ct)
                    .ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(_s.RejectTemplate))
                return RobotReply.Fail("Chưa cấu hình lệnh báo NG cho robot, không gửi gì.");

            return await SendAsync(FillTemplate(_s.RejectTemplate, 0, 0, 0, cycleId), ct).ConfigureAwait(false);
        }

        /// <summary>Dựng gói tin theo mẫu rồi gửi qua TCP tới chương trình robot (không chờ trả lời).</summary>
        private async Task<RobotReply> SendPacketAsync(Func<RobotPacketSettings, string> build, CancellationToken ct)
        {
            var ps = _s.Points.Packet;
            if (ps == null) return RobotReply.Fail("Chưa có cấu hình gói tin.");
            var tcp = _packet;
            if (tcp == null || !tcp.IsConnected) return RobotReply.Fail($"Chưa nối tới nơi nhận gói tin {ps.Host}:{ps.Port}.");

            string packet;
            try { packet = build(ps); }
            catch (ArgumentException ex) { return RobotReply.Fail("Mẫu gói tin không hợp lệ: " + ex.Message); }

            try
            {
                await tcp.SendAsync(packet, ct).ConfigureAwait(false);
                return RobotReply.Ok($"Đã gửi gói tin tới {ps.Host}:{ps.Port}: {OffsetPacket.Visible(packet)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RobotReply.Fail($"Gửi gói tin lỗi: {ex.Message}");
            }
        }

        private async Task<RobotReply> SendAsync(string command, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() => LibExecute(command), ct).ConfigureAwait(false);
                return RobotReply.Ok(command);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RobotReply.Fail($"Gửi lệnh robot lỗi: {ex.Message}");
            }
            finally { _lock.Release(); }
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            Disconnect();
            await Task.Run(LibConnect, ct).ConfigureAwait(false);

            // Chế độ gói tin: nối thêm một đường TCP tới chương trình robot nhận gói tin
            if (_s.Points.Mode == RobotSendMode.Packet)
            {
                var ps = _s.Points.Packet ?? throw new InvalidOperationException("Thiếu cấu hình gói tin.");
                string? bad = OffsetPacket.Validate(ps.OffsetTemplate);
                if (bad != null) { try { LibClose(); } catch { } _robot = null; throw new InvalidOperationException("Mẫu gói tin: " + bad); }

                var tcp = new Communication.Transport.TcpTransport(new Communication.Transport.TcpOptions(
                    ps.Host, ps.Port, ps.ConnectTimeoutMs, Array.Empty<byte>(), Array.Empty<byte>(), System.Text.Encoding.Latin1));
                try { await tcp.ConnectAsync(ct).ConfigureAwait(false); }
                catch
                {
                    tcp.Dispose();
                    try { LibClose(); } catch { }
                    _robot = null;
                    throw;
                }
                _packet = tcp;
            }
            _connected = true;
        }

        public void Disconnect()
        {
            _connected = false;
            _hasControl = false;
            try { _packet?.Dispose(); } catch { }
            _packet = null;
            try { LibClose(); } catch { }
            _robot = null;
        }

        public void Dispose() { Disconnect(); _lock.Dispose(); }

        // ---------- GỌI THƯ VIỆN Melfa.Robot (chưa kiểm chứng; sửa ở đây nếu API khác) ----------

        private MelfaRobot Lib => (MelfaRobot)(_robot ?? throw new InvalidOperationException("Chưa kết nối robot."));

        private void LibConnect()
        {
            // Hàm dựng: (host, port, số robot). Open(name): name là tên phiên tùy ý (chữ + số), KHÔNG phải tên robot.
            var robot = new MelfaRobot(_s.Ip, _s.Port, _s.RobotNo);
            robot.Connect();
            robot.Open(string.IsNullOrWhiteSpace(_s.ClientName) ? "VisionApp" : _s.ClientName.Trim());
            if (_s.TakeControlOnConnect || _s.EnableOnConnect)
            {
                robot.OperationEnabled = true;                   // chiếm quyền điều khiển
                _hasControl = true;
                if (_s.TakeControlOnConnect) StopAndResetCore(robot);   // thoát trạng thái RUN/Pause
            }
            if (_s.EnableOnConnect) robot.ServoOn = true;
            _robot = robot;
        }

        private void LibExecute(string command) => Lib.Execute(command);

        /// <summary>
        /// Đọc một điểm của chương trình đang mở (LISTP). Lấy chữ từ IPosition.ToString() nếu có dạng (…)(f1,f2);
        /// không thì lấy các thuộc tính X,Y,Z,A,B,C và Flag1/Flag2 bằng reflection. Không xác định được cờ cấu hình
        /// thì BÁO LỖI (ghi sai cờ có thể làm tay robot đổi tư thế).
        /// </summary>
        private RobotPoint LibReadPoint(string name)
        {
            object? position = null;
            string? libError = null;
            try { position = Lib.ProgramGetPosition(name); }
            catch (Exception ex) { libError = ex.Message; }   // ví dụ không phân tích được điểm có trục thêm L1, L2

            if (position != null)
            {
                if (TryPoint(position, out var point, out string detail)) return point;
                libError = detail;
            }

            // Dự phòng: gọi lệnh LISTP thô (hàm private của thư viện) và tự phân tích chữ trả về
            string raw = LibRawCommand("LISTP" + name);
            if (RobotPoint.TryParse(raw, out var fromRaw)) return fromRaw;
            throw new InvalidOperationException(
                $"Không đọc được điểm {name}. Thư viện: {libError}. Chữ robot trả về cho LISTP: \"{Shorten(raw)}\"");
        }

        /// <summary>Vị trí hiện tại (PPOSF) thành RobotPoint, cùng cách đọc như điểm trong chương trình.</summary>
        private RobotPoint LibReadCurrent()
        {
            string? libError = null;
            try
            {
                object position = Lib.CurrentPositionP;
                if (TryPoint(position, out var point, out string detail)) return point;
                libError = detail;
            }
            catch (Exception ex) { libError = ex.Message; }

            string raw = LibRawCommand("PPOSF");
            if (RobotPoint.TryParse(raw, out var fromRaw)) return fromRaw;
            throw new InvalidOperationException($"Không đọc được vị trí hiện tại. Thư viện: {libError}. Chữ robot trả về cho PPOSF: \"{Shorten(raw)}\"");
        }

        /// <summary>Đổi đối tượng vị trí của thư viện thành RobotPoint: ưu tiên chữ (…)(f1,f2), không thì thuộc tính X..C + cờ.</summary>
        private static bool TryPoint(object position, out RobotPoint point, out string detail)
        {
            point = default;
            string text = position.ToString() ?? "";
            // Thư viện in có dấu cách giữa hai nhóm: "(x, y, ... , NaN, NaN) (7, 0)"; chấp nhận cả ")(" lẫn ") ("
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\)\s*\(") && RobotPoint.TryParse(text, out point))
            {
                detail = "";
                return true;
            }

            var type = position.GetType();
            double? Get(string prop)
            {
                var pi = type.GetProperty(prop);
                if (pi?.GetValue(position) is { } v)
                {
                    try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { }
                }
                return null;
            }

            double?[] axes = { Get("X"), Get("Y"), Get("Z"), Get("A"), Get("B"), Get("C") };
            double? f1 = Get("Flag1") ?? Get("Structure"), f2 = Get("Flag2") ?? Get("Multiturn");
            if (axes.All(a => a.HasValue) && f1.HasValue)
            {
                point = new RobotPoint(axes[0]!.Value, axes[1]!.Value, axes[2]!.Value, axes[3]!.Value, axes[4]!.Value, axes[5]!.Value,
                                       (int)f1.Value, (int)(f2 ?? 0));
                detail = "";
                return true;
            }

            detail = $"không đọc đủ X..C và cờ từ {type.Name} (\"{Shorten(text)}\")";
            return false;
        }

        /// <summary>Gọi hàm private DoCommandInternal(string) của MelfaRobot để lấy chữ trả về nguyên bản (chỉ dùng khi hàm công khai không phân tích được).</summary>
        private string LibRawCommand(string command)
        {
            var method = typeof(MelfaRobot).GetMethod("DoCommandInternal",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(string) }, null);
            if (method == null) return "(không gọi được lệnh thô)";
            try { return method.Invoke(Lib, new object[] { command })?.ToString() ?? ""; }
            catch (System.Reflection.TargetInvocationException ex) { return "(lỗi) " + (ex.InnerException?.Message ?? ex.Message); }
        }

        private void LibClose()
        {
            if (_robot is not MelfaRobot r) return;
            try { r.Close(); } catch { }
            try { r.Disconnect(); } catch { }
            try { (r as IDisposable)?.Dispose(); } catch { }
        }
    }
}
