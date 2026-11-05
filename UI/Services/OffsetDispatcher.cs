using Communication.Drivers;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Execution;
using Vision.Geometry;
using Vision.Graph;

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
    /// Giới hạn độ lệch tối đa do driver của hãng kiểm tra thêm một lớp nữa trước khi ra mạng.
    /// </summary>
    public sealed class OffsetDispatcher
    {
        public const string PlaceToolId = "PlaceCompensation";

        private readonly DeviceManager _devices;
        private long _cycleId;

        public OffsetDispatcher(DeviceManager devices)
        {
            _devices = devices;
        }

        public async Task<DispatchResult> DispatchAsync(
            VisionGraph graph, GraphRunResult run, RunMode mode, CancellationToken cancellationToken = default)
        {
            if (mode != RunMode.Live)
                return new DispatchResult(false, true, "Không phải chạy thật: không gửi cho robot.");

            var placeNodes = graph.Nodes.Where(n => n.ToolName == PlaceToolId).ToList();
            if (placeNodes.Count == 0)
                return new DispatchResult(false, true, "Đồ thị không có node PlaceCompensation: không có độ bù để gửi.");
            if (placeNodes.Count > 1)
                return new DispatchResult(false, false, "Có nhiều node PlaceCompensation: chưa biết gửi cái nào. Giữ lại một node.");

            // Lỗi ở bất kỳ node nào: không gửi (kể cả NG), để người vận hành kiểm tra
            foreach (var node in graph.Nodes)
            {
                if (!run.Nodes.TryGetValue(node.Id, out var nr)) continue;
                var status = nr.Result?.Status;
                if (status is ResultStatus.ConfigError or ResultStatus.SystemError)
                    return new DispatchResult(false, false, $"{node.ToolName} bị lỗi ({nr.Message}): KHÔNG gửi gì cho robot.");
            }

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
                var reply = await robot.SendOffsetAsync(offset.Dx, offset.Dy, offset.Drz, (int)id, cancellationToken);
                return reply.Accepted
                    ? new DispatchResult(true, true, $"Đã gửi độ bù cho {robotCfg.Name}: {offset}, robot nhận (#{id}){note}")
                    : new DispatchResult(true, false, $"Robot {robotCfg.Name} KHÔNG nhận độ bù {offset}: {reply.Error}{note}");
            }

            var reject = await robot.SendRejectAsync((int)id, cancellationToken);
            return reject.Accepted
                ? new DispatchResult(true, true, $"Không có độ bù (không thấy vật): đã báo NG cho {robotCfg.Name} (#{id}){note}")
                : new DispatchResult(true, false, $"Báo NG cho {robotCfg.Name} thất bại: {reject.Error}{note}");
        }
    }
}
