using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.RunAndDisplay
{
    /// <summary>Một dòng nhật ký gửi/nhận của khuôn test. Kind: GỬI, NHẬN, PLC hoặc LỖI.</summary>
    public sealed record LogLine(string Time, string Source, string Kind, string Text);
}
