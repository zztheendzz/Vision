using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UI.Services;

namespace UI.Models
{
    /// <summary>Một hàng của bảng chụp: số liệu đã định dạng sẵn để hiện, "-" khi không đo được.</summary>
    public sealed class CaptureRow
    {
        public int No { get; }
        public string Phase { get; }
        public string RobotX { get; }
        public string RobotY { get; }
        public string RobotRz { get; }
        public string PixelRow { get; }
        public string PixelColumn { get; }
        public string PixelAngle { get; }
        public string Note { get; }
        public bool Failed { get; }

        public CaptureRow(int no, CaptureSample s)
        {
            No = no;
            Phase = s.Index > 0 ? $"{s.Phase} {s.Index}" : s.Phase;
            RobotX = Fmt(s.RobotX, 3);
            RobotY = Fmt(s.RobotY, 3);
            RobotRz = Fmt(s.RobotRz, 2);
            PixelRow = Fmt(s.PixelRow, 2);
            PixelColumn = Fmt(s.PixelColumn, 2);
            PixelAngle = Fmt(s.PixelAngleDeg, 2);
            Note = s.Note;
            Failed = s.PixelRow == null;
        }

        private static string Fmt(double? v, int digits)
            => v is { } d && double.IsFinite(d) ? d.ToString("F" + digits, CultureInfo.InvariantCulture) : "-";
    }
}
