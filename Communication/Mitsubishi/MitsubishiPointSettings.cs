using Communication.Configuration;
using Communication.Devices;
using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Mitsubishi
{
    /// <summary>Đọc cấu hình điểm của robot từ DeviceConfig (dùng chung cho robot thật và giả lập).</summary>
    public static class MitsubishiPointSettings
    {
        public static RobotPointSettings Read(DeviceConfig d)
        {
            string mode = d.GetString("SendMode");
            string target = d.GetString("TargetPoint").Trim();
            string offsetPoint = d.GetString("OffsetPoint").Trim();
            string axis = d.GetString("RzAxis").Trim().ToUpperInvariant();

            return new RobotPointSettings(
                mode == DeviceSchemas.RobotSendOverwrite ? RobotSendMode.OverwritePoint : RobotSendMode.OffsetViaPoint,
                target,
                offsetPoint.Length == 0 ? "P_VOFS" : offsetPoint,
                axis is "A" or "B" ? axis[0] : 'C');
        }
    }
}
