using System;
using System.Collections.Generic;
using System.Text;
using UI.ViewModels.RunAndDisplay;

namespace UI.ViewModels.Device
{
    /// <summary>Một dòng bảng lệnh khuôn test: lệnh gửi và hai mẫu trả lời (sửa trực tiếp trong bảng).</summary>
    public sealed class FixtureCommandRow
    {
        public FixtureCommandRow(string title, FieldViewModel send, FieldViewModel ok, FieldViewModel err)
        {
            Title = title; Send = send; Ok = ok; Err = err;
        }

        public string Title { get; }
        public FieldViewModel Send { get; }
        public FieldViewModel Ok { get; }
        public FieldViewModel Err { get; }
    }

    /// <summary>Một dòng bảng kết quả slot: mẫu nhận về và mã gửi PLC. Ô null = dòng đó không có ô này (hiện dấu —).</summary>
    public sealed class FixtureResultRow
    {
        public FixtureResultRow(string title, FieldViewModel? pattern, FieldViewModel? plc)
        {
            Title = title; Pattern = pattern; Plc = plc;
        }

        public string Title { get; }
        public FieldViewModel? Pattern { get; }
        public FieldViewModel? Plc { get; }
        public bool HasPattern => Pattern != null;
        public bool HasPlc => Plc != null;
        public bool NoPattern => Pattern == null;
        public bool NoPlc => Plc == null;
    }


}

