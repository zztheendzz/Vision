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

            RobotSendMode sendMode = mode == DeviceSchemas.RobotSendPacket ? RobotSendMode.Packet
                                   : mode == DeviceSchemas.RobotSendViaPlc ? RobotSendMode.ViaPlc
                                   : RobotSendMode.OffsetViaPoint;

            string host = d.GetString("PacketHost").Trim();
            var packet = new RobotPacketSettings(
                host.Length == 0 ? d.GetString(DeviceSchemas.KeyIp).Trim() : host,     // trống = dùng IP robot
                d.GetInt("PacketPort", 10002),
                d.GetString("PacketTemplate"),
                d.GetString("PacketReject"),
                d.GetInt("PacketDecimals", 3),
                d.GetDouble("PacketScale", 1));

            return new RobotPointSettings(
                sendMode,
                target,
                offsetPoint.Length == 0 ? "P_VOFS" : offsetPoint,
                axis is "A" or "B" ? axis[0] : 'C',
                packet);
        }
    }
}
