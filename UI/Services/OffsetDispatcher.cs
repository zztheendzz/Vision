using Communication.Configuration;
using Communication.Devices;
using Communication.Drivers;
using Communication.Mitsubishi;
using Communication.Plc;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Execution;
using Vision.Geometry;
using Vision.Graph;
using Vision.Tools.Locate;

namespace UI.Services
{
    /// <summary>Kết quả của một lần gửi độ bù cho robot, để hiện trong Output và thanh trạng thái.</summary>
    public sealed record DispatchResult(bool Attempted, bool Success, string Message);

    /// <summary>
    /// Cầu nối giữa kết quả xử lý ảnh và robot: sau một lần CHẠY THẬT, gửi độ bù hoặc báo NG cho robot đang nối.
    ///
    /// Luật (an toàn là trên hết):
    ///  - Chạy thử / học mẫu: KHÔNG bao giờ gửi.
    ///  - Có node lỗi cấu hình hoặc lỗi hệ thống: KHÔNG gửi gì (robot không nhận lệnh sai); người vận hành phải xem.
    ///  - Không thấy vật (node chạy tốt nhưng không có kết quả) hoặc node PlaceCompensation không ra độ bù: gửi NG.
    ///  - Còn lại: gửi độ bù (dX, dY, dRz) của PlaceCompensation.
    ///  - Robot chưa nối: không gửi, báo rõ để không tưởng đã gửi.
    /// Hai cách gửi (chọn ở cấu hình robot, ô "Cách gửi cho robot"):
    ///  - Độ lệch: ghi (dX, dY, dRz) vào biến điểm trung gian, chương trình robot tự cộng vào điểm.
    ///  - Ghi đè điểm: đọc điểm đã chọn, tự cộng độ bù theo quy ước của node, ghi đè lại đúng điểm đó.
    /// Giới hạn độ lệch tối đa do driver của hãng kiểm tra thêm một lớp nữa trước khi ra mạng.
    /// </summary>
    public sealed class OffsetDispatcher
    {
        public const string PlaceToolId = "PlaceCompensation";
        public const string PickToolId = "PickCompensation";

        private readonly DeviceManager _devices;
        private readonly Func<PlcMap?>? _plcMap;
        private long _cycleId;

        /// <param name="plcMap">Lấy bản đồ tín hiệu PLC hiện hành (cần khi robot nhận độ bù qua PLC).</param>
        public OffsetDispatcher(DeviceManager devices, Func<PlcMap?>? plcMap = null)
        {
            _devices = devices;
            _plcMap = plcMap;
        }

        public async Task<DispatchResult> DispatchAsync(
            VisionGraph graph, GraphRunResult run, RunMode mode, CancellationToken cancellationToken = default)
        {
            if (mode != RunMode.Live)
                return new DispatchResult(false, true, "Không phải chạy thật: không gửi cho robot.");

            // Lỗi ở bất kỳ node nào: không gửi (kể cả NG), để người vận hành kiểm tra
            foreach (var node in graph.Nodes)
            {
                if (!run.Nodes.TryGetValue(node.Id, out var nr)) continue;
                var status = nr.Result?.Status;
                if (status is ResultStatus.ConfigError or ResultStatus.SystemError)
                    return new DispatchResult(false, false, $"{node.ToolName} bị lỗi ({nr.Message}): KHÔNG gửi gì cho robot.");
            }

            // Robot nhận độ bù qua PLC: ghi thanh ghi Calib, không cần nối trực tiếp với robot
            var plcRobot = _devices.RobotConfigs.FirstOrDefault(
                c => c.Enabled && MitsubishiPointSettings.Read(c).Mode == RobotSendMode.ViaPlc);
            if (plcRobot != null)
                return await DispatchViaPlcAsync(plcRobot, graph, run, cancellationToken);

            var placeNodes = graph.Nodes.Where(n => n.ToolName == PlaceToolId).ToList();
            if (placeNodes.Count == 0)
                return new DispatchResult(false, true, "Đồ thị không có node PlaceCompensation: không có độ bù để gửi.");
            if (placeNodes.Count > 1)
                return new DispatchResult(false, false, "Có nhiều node PlaceCompensation: chưa biết gửi cái nào. Giữ lại một node.");

            var robots = _devices.ConnectedRobots();
            if (robots.Count == 0)
                return new DispatchResult(false, false, "Robot chưa kết nối: KHÔNG gửi được độ bù.");

            var (robotCfg, robot) = robots[0];
            string note = robots.Count > 1 ? $" (có {robots.Count} robot đang nối, dùng '{robotCfg.Name}')" : "";
            long id = Interlocked.Increment(ref _cycleId);

            bool anyNoResult = graph.Nodes.Any(n => run.Nodes.TryGetValue(n.Id, out var r) && r.Result?.Status == ResultStatus.NoResult);
            var placeNode = placeNodes[0];
            run.Nodes.TryGetValue(placeNode.Id, out var placeRun);

            if (!anyNoResult && placeRun?.Result is { Status: ResultStatus.Ok } placeResult &&
                placeResult.TryGet<RobotOffset>("Correction", out var offset))
            {
                var settings = robot.PointSettings;
                var convention = ReadConvention(placeNode);

                // Chế độ 2: tính ra điểm mới rồi ghi đè lên điểm đã chọn
                if (settings.Mode == RobotSendMode.OverwritePoint)
                    return await OverwriteAsync(robot, robotCfg.Name, settings, offset, convention, note, cancellationToken);

                // Chế độ 1 (điểm trung gian) và chế độ gói tin: gửi độ lệch, robot tự cộng
                var reply = await robot.SendOffsetAsync(offset.Dx, offset.Dy, offset.Drz, (int)id, cancellationToken);
                string convNote = convention == PlaceOffsetConvention.Additive
                    ? ""
                    : $" Lưu ý: node đang dùng quy ước {convention}, chương trình robot phải tự áp độ bù theo quy ước đó.";
                string where = settings.Mode == RobotSendMode.Packet
                    ? "bằng gói tin"
                    : $"vào điểm trung gian {settings.OffsetPoint}";
                return reply.Accepted
                    ? new DispatchResult(true, true,
                        $"Đã gửi độ bù {offset} {where} cho {robotCfg.Name} (#{id}): {reply.Raw}{note}.{convNote}")
                    : new DispatchResult(true, false, $"Robot {robotCfg.Name} KHÔNG nhận độ bù {offset}: {reply.Error}{note}");
            }

            var reject = await robot.SendRejectAsync((int)id, cancellationToken);
            return reject.Accepted
                ? new DispatchResult(true, true, $"Không có độ bù (không thấy vật): đã báo NG cho {robotCfg.Name} (#{id}){note}")
                : new DispatchResult(true, false, $"Báo NG cho {robotCfg.Name} thất bại: {reject.Error}{note}");
        }

        /// <summary>
        /// Chế độ "Qua PLC": ghi (dX, dY, 0, dRz) vào thanh ghi Calib X, Y, Z, R của PLC; PLC chuyển cho robot.
        /// Nguồn độ bù chọn ở cấu hình robot (ô "Gửi độ bù của tool nào"): không gửi / PlaceCompensation / PickCompensation / cả hai.
        /// Mỗi loại ghi vào nhóm thanh ghi riêng (đặt: Pos.Calib.*, gắp: Pos.CalibPick.*).
        /// Không thấy vật hoặc thiếu kết quả của một nguồn đã chọn thì KHÔNG ghi gì (bit NG do PLC báo như thường lệ).
        /// </summary>
        private async Task<DispatchResult> DispatchViaPlcAsync(
            DeviceConfig robotCfg, VisionGraph graph, GraphRunResult run, CancellationToken ct)
        {
            string source = robotCfg.GetString("PlcSource").Trim();
            if (source.Length == 0) source = DeviceSchemas.PlcSourcePlace;

            bool wantPlace = source == DeviceSchemas.PlcSourcePlace || source == DeviceSchemas.PlcSourceBoth;
            bool wantPick = source == DeviceSchemas.PlcSourcePick || source == DeviceSchemas.PlcSourceBoth;
            if (!wantPlace && !wantPick)
                return new DispatchResult(false, true, $"{robotCfg.Name}: đang chọn '{DeviceSchemas.PlcSourceNone}', không ghi độ bù nào xuống PLC.");

            // Mỗi nguồn đã chọn cần đúng một node trong đồ thị
            VisionNode? placeNode = null, pickNode = null;
            if (wantPlace && !TryFindSingle(graph, PlaceToolId, out placeNode, out string? e1)) return new DispatchResult(false, false, e1!);
            if (wantPick && !TryFindSingle(graph, PickToolId, out pickNode, out string? e2)) return new DispatchResult(false, false, e2!);

            bool anyNoResult = graph.Nodes.Any(n => run.Nodes.TryGetValue(n.Id, out var r) && r.Result?.Status == ResultStatus.NoResult);

            RobotOffset placeOffset = default, pickOffset = default;
            bool havePlace = !wantPlace || (TryGetOffset(run, placeNode!, "Correction", out placeOffset));
            bool havePick = !wantPick || (TryGetOffset(run, pickNode!, "Offset", out pickOffset));
            if (anyNoResult || !havePlace || !havePick)
                return new DispatchResult(false, true,
                    "Không có độ bù (không thấy vật hoặc thiếu kết quả của tool đã chọn): KHÔNG ghi thanh ghi Calib nào. NG do PLC báo bằng bit NG.");

            var map = _plcMap?.Invoke();
            if (map == null)
                return new DispatchResult(true, false, "Chưa có bản đồ tín hiệu PLC: KHÔNG ghi được độ bù.");

            var plc = _devices.PlcFor(robotCfg);
            if (plc == null)
                return new DispatchResult(true, false, "PLC chưa kết nối: KHÔNG ghi được độ bù cho robot.");

            double maxMm = robotCfg.GetDouble("MaxOffsetMm", 50), maxDeg = robotCfg.GetDouble("MaxOffsetDeg", 30);

            var jobs = new List<(string Name, string Prefix, RobotOffset Offset, string Note)>();
            if (wantPlace)
                jobs.Add(("đặt", PlcOffsetWriter.PlacePrefix, placeOffset, ConventionNote("PlaceCompensation", ReadEnum(placeNode!, PlaceOffsetConvention.Additive))));
            if (wantPick)
                jobs.Add(("gắp", PlcOffsetWriter.PickPrefix, pickOffset, ConventionNote("PickCompensation", ReadEnum(pickNode!, OffsetConvention.AboutRobotOrigin), OffsetConvention.Additive)));

            // Kiểm tra hết mọi nhóm trước, rồi mới ghi nhóm đầu tiên: lỗi ở nhóm sau thì chưa có gì bị ghi
            if (jobs.Count > 1)
            {
                string? overlap = PlcOffsetWriter.FindOverlapBetween(map, jobs.Select(j => j.Prefix).ToArray());
                if (overlap != null)
                    return new DispatchResult(true, false, $"Nhóm thanh ghi đặt và gắp trùng nhau: {overlap} KHÔNG ghi gì.");
            }
            foreach (var job in jobs)
            {
                var check = await PlcOffsetWriter.WriteAsync(plc, map, job.Offset.Dx, job.Offset.Dy, 0, job.Offset.Drz, maxMm, maxDeg, ct, job.Prefix, dryRun: true);
                if (!check.Success)
                    return new DispatchResult(true, false, $"Độ bù {job.Name} {job.Offset} không ghi được: {check.Message}. KHÔNG ghi gì.");
            }

            var done = new List<string>();
            foreach (var job in jobs)
            {
                var result = await PlcOffsetWriter.WriteAsync(plc, map, job.Offset.Dx, job.Offset.Dy, 0, job.Offset.Drz, maxMm, maxDeg, ct, job.Prefix);
                if (!result.Success)
                {
                    string already = done.Count == 0 ? "" : $" Đã ghi trước đó: {string.Join(" | ", done)}. PLC đang giữ độ bù {string.Join(", ", done.Select(d => d.Split(':')[0]))} của lần này.";
                    return new DispatchResult(true, false, $"Ghi độ bù {job.Name} {job.Offset} vào PLC thất bại: {result.Message}.{already}");
                }
                done.Add($"{job.Name}: {result.Message}{job.Note}");
            }

            return new DispatchResult(true, true, $"Đã ghi độ bù vào PLC. {string.Join(" | ", done)}");
        }

        private static bool TryFindSingle(VisionGraph graph, string toolId, out VisionNode? node, out string? error)
        {
            var nodes = graph.Nodes.Where(n => n.ToolName == toolId).ToList();
            node = nodes.Count == 1 ? nodes[0] : null;
            error = nodes.Count switch
            {
                0 => $"Đồ thị không có node {toolId}, nhưng cấu hình robot đang chọn gửi độ bù của tool này.",
                1 => null,
                _ => $"Có nhiều node {toolId}: chưa biết gửi cái nào. Giữ lại một node."
            };
            return nodes.Count == 1;
        }

        private static bool TryGetOffset(GraphRunResult run, VisionNode node, string port, out RobotOffset offset)
        {
            offset = default;
            return run.Nodes.TryGetValue(node.Id, out var nr)
                   && nr.Result is { Status: ResultStatus.Ok } result
                   && result.TryGet<RobotOffset>(port, out offset);
        }

        private static T ReadEnum<T>(VisionNode node, T fallback) where T : struct, Enum
        {
            if (!node.Parameters.TryGetValue("Convention", out var value)) return fallback;
            if (value is T t) return t;
            return Enum.TryParse(value?.ToString(), true, out T parsed) ? parsed : fallback;
        }

        private static string ConventionNote<T>(string tool, T convention, T? expected = null) where T : struct, Enum
        {
            T want = expected ?? (T)Enum.Parse(typeof(T), "Additive");
            return EqualityComparer<T>.Default.Equals(convention, want)
                ? ""
                : $" (Lưu ý: {tool} đang dùng quy ước {convention}, PLC/robot phải áp độ bù theo quy ước đó, không phải cộng thẳng)";
        }

        private static PlaceOffsetConvention ReadConvention(VisionNode node)
        {
            if (!node.Parameters.TryGetValue("Convention", out var value)) return PlaceOffsetConvention.Additive;
            if (value is PlaceOffsetConvention c) return c;
            return Enum.TryParse(value?.ToString(), true, out PlaceOffsetConvention parsed) ? parsed : PlaceOffsetConvention.Additive;
        }

        /// <summary>Đọc điểm đã chọn từ robot, áp độ bù, kiểm tra giới hạn rồi ghi đè lại đúng điểm đó.</summary>
        private static async Task<DispatchResult> OverwriteAsync(
            IRobotDriver robot, string robotName, RobotPointSettings settings, RobotOffset offset,
            PlaceOffsetConvention convention, string note, CancellationToken ct)
        {
            string point = settings.TargetPoint;
            if (!RobotPoint.IsValidName(point))
                return new DispatchResult(false, false, $"Chưa chọn điểm cần bù cho {robotName} (ô 'Điểm cần bù'): KHÔNG ghi gì.");

            string? invalid = robot.ValidateOffset(offset.Dx, offset.Dy, offset.Drz);
            if (invalid != null)
                return new DispatchResult(false, false, $"{invalid} (KHÔNG ghi điểm {point})");

            var read = await robot.ReadPointAsync(point, ct);
            if (!read.Ok)
                return new DispatchResult(false, false, $"Không đọc được điểm {point} từ {robotName}: {read.Error}. KHÔNG ghi gì.");

            var original = read.Point;
            var moved = Apply(original, offset, convention, settings.RzAxis);

            var reply = await robot.WritePointAsync(point, moved, ct);
            return reply.Accepted
                ? new DispatchResult(true, true,
                    $"Đã ghi đè {point} của {robotName}: {original.ToMelfa()} → {moved.ToMelfa()} (bù {offset}){note}")
                : new DispatchResult(true, false, $"Ghi đè {point} của {robotName} thất bại: {reply.Error}{note}");
        }

        /// <summary>Điểm mới = điểm cũ áp độ bù theo quy ước của node PlaceCompensation; Z, trục xoay còn lại và cờ giữ nguyên.</summary>
        public static RobotPoint Apply(RobotPoint p, RobotOffset offset, PlaceOffsetConvention convention, char rzAxis)
        {
            var taught = new Rigid2D(p.X, p.Y, p.GetAxis(rzAxis));
            var delta = new Rigid2D(offset.Dx, offset.Dy, offset.Drz);

            Rigid2D result = convention switch
            {
                PlaceOffsetConvention.AboutRobotOrigin => delta.Compose(taught),   // P' = R(Drz)·P + (Dx, Dy)
                PlaceOffsetConvention.ToolFrame => taught.Compose(delta),          // dịch trong hệ của tay gắp
                _ => new Rigid2D(taught.X + offset.Dx, taught.Y + offset.Dy, taught.ThetaDeg + offset.Drz)   // cộng thẳng
            };

            return (p with { X = result.X, Y = result.Y }).WithAxis(rzAxis, Pose2D.NormalizeAngle(result.ThetaDeg));
        }
    }
}
