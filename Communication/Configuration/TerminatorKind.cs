using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Configuration
{
   public  enum TerminatorKind
    {
        None,     // không thêm gì
        CR,       // \r  (byte 0D)
        LF,       // \n  (byte 0A)
        CRLF,     // \r\n (byte 0D 0A)
        Custom    // người dùng tự gõ
    }
}
