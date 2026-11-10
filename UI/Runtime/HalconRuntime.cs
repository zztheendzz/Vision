using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace UI.Runtime
{
    /// <summary>
    /// Trỏ HALCON vào thư mục runtime đi kèm app (halcon-runtime cạnh file exe), để máy không cài HALCON vẫn chạy được.
    ///
    /// ModuleInitializer chạy ngay khi assembly này được nạp, TRƯỚC mọi lệnh HALCON đầu tiên
    /// (HALCON đọc HALCONROOT và nạp DLL native ở lần gọi đầu).
    ///
    /// Không có thư mục halcon-runtime cạnh exe thì KHÔNG đổi gì: app dùng HALCON đã cài sẵn trong máy (như hiện nay).
    /// </summary>
    internal static class HalconRuntime
    {
        [ModuleInitializer]
        public static void EnsureHalconRuntime()
        {
            // Thư mục thật chứa exe (đúng cả khi publish single-file)
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

            // Thư mục halcon-runtime nằm cạnh exe
            string halconRoot = Path.Combine(exeDir, "halcon-runtime");
            string binDir = Path.Combine(halconRoot, "bin", "x64-win64");
            string genicamDir = Path.Combine(halconRoot, "genicam", "bin", "Win64_x64");

            // Chưa đóng gói runtime: dùng HALCON cài trong máy, đừng ghi đè biến môi trường đang đúng
            if (!Directory.Exists(binDir)) return;

            Environment.SetEnvironmentVariable("HALCONROOT", halconRoot);
            Environment.SetEnvironmentVariable("HALCONARCH", "x64-win64");

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (path.Contains(binDir, StringComparison.OrdinalIgnoreCase)) return;

            string extra = binDir + Path.PathSeparator;
            if (Directory.Exists(genicamDir)) extra += genicamDir + Path.PathSeparator;
            Environment.SetEnvironmentVariable("PATH", extra + path);
        }
    }
}
