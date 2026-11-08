using ActUtlTypeLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;


//   OPEN <số trạm logic>   -> OK | ERR <thông báo>
//   GET <địa chỉ>          -> OK <giá trị> | ERR ...
//   SET <địa chỉ> <giá trị>-> OK | ERR ...
//   CLOSE                  -> OK
//   QUIT                   -> (thoát)
// stdin đóng (chương trình chính tắt hoặc sập) thì tự thoát.
internal static class MxBridgeProgram
{
    private static object _com;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);

        string line;
        while ((line = Console.ReadLine()) != null)
        {
            string reply;
            try
            {
                var p = line.Trim().Split(' ');
                switch (p[0].ToUpperInvariant())
                {
                    case "OPEN": reply = Open(int.Parse(p[1], CultureInfo.InvariantCulture)); break;
                    case "GET": reply = Get(p[1]); break;
                    case "SET": reply = Set(p[1], int.Parse(p[2], CultureInfo.InvariantCulture)); break;
                    case "CLOSE": CloseCom(); reply = "OK"; break;
                    case "QUIT": CloseCom(); return 0;
                    default: reply = "ERR lệnh không hợp lệ: " + p[0]; break;
                }
            }
            catch (Exception ex)
            {
                reply = "ERR " + ex.GetType().Name + ": " + ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            }
            Console.WriteLine(reply);
            Console.Out.Flush();
        }
        CloseCom();
        return 0;
    }

    // Gọi COM bằng reflection (InvokeMember), không dùng dynamic: khỏi cần tham chiếu Microsoft.CSharp
    private static object Call(string method, params object[] args)
        => _com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, _com, args);

    private static string Open(int station)
    {
        CloseCom();
        Type type = Type.GetTypeFromProgID("ActUtlType.ActUtlType") ?? Type.GetTypeFromProgID("ActUtlTypeLib.ActUtlType");
        if (type == null) return "ERR Không tìm thấy MX Component (ActUtlType). Cài MX Component 32-bit.";

        object com = Activator.CreateInstance(type);
        _com = com;
        try
        {
            type.InvokeMember("ActLogicalStationNumber", BindingFlags.SetProperty, null, com, new object[] { station });
            int rc = (int)Call("Open");
            if (rc != 0)
            {
                CloseCom();
                return "ERR Open lỗi 0x" + rc.ToString("X8") + ". Kiểm tra số trạm logic " + station + " và GX Simulator3 đang RUN.";
            }
        }
        catch
        {
            CloseCom();
            throw;
        }
        return "OK";
    }

    private static string Get(string address)
    {
        if (_com == null) return "ERR Chưa mở";

        // GetDevice(string szDevice, out int lplData): tham số thứ 2 là tham số ra
        object[] args = { address, 0 };
        var modifiers = new ParameterModifier(2);
        modifiers[1] = true;
        int rc = (int)_com.GetType().InvokeMember("GetDevice", BindingFlags.InvokeMethod, null, _com, args,
                                                  new[] { modifiers }, null, null);
        return rc == 0 ? "OK " + Convert.ToInt32(args[1], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                       : "ERR đọc " + address + " lỗi 0x" + rc.ToString("X8");
    }

    private static string Set(string address, int value)
    {
        if (_com == null) return "ERR Chưa mở";
        int rc = (int)Call("SetDevice", address, value);
        return rc == 0 ? "OK" : "ERR ghi " + address + " lỗi 0x" + rc.ToString("X8");
    }

    private static void CloseCom()
    {
        if (_com == null) return;
        try { Call("Close"); } catch { }
        try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_com); } catch { }
        _com = null;
    }
}
