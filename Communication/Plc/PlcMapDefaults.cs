using System;
using System.Collections.Generic;
using System.Text;

namespace Communication.Plc
{
    /// <summary>
    /// Bản đồ tín hiệu MẶC ĐỊNH, lấy đúng theo bảng địa chỉ của máy mẫu. Máy khác chỉ cần sửa địa chỉ trên giao diện rồi lưu.
    /// Cột Kiểu / Cách kích / Hệ số là GIẢ ĐỊNH (bảng gốc không ghi), xem cột Ghi chú và sửa theo máy thật.
    /// </summary>
    public static class PlcMapDefaults
    {
        public const string GroupControl = "Điều khiển máy";
        public const string GroupMotion = "Vị trí và Jog";
        public const string GroupCamera = "Camera";
        public const string GroupFov = "FOV kiểm tra";
        public const string GroupScan = "Scan tay hút";
        public const string GroupError = "Lỗi";

        /// <summary>
        /// Thêm nhóm thanh ghi "Calib gắp" (độ bù của PickCompensation) nếu bản đồ chưa có. Địa chỉ để TRỐNG:
        /// máy nào dùng thì điền, không điền thì không gửi được độ bù gắp (có báo lỗi rõ). Gọi cả khi mở file cũ.
        /// </summary>
        public static void EnsurePickOffset(PlcMap map)
        {
            string[] axes = { "X", "Y", "Z", "R" };
            const string note = "C# ghi độ bù GẮP (PickCompensation) vào đây khi chọn gửi độ bù gắp. Địa chỉ để trống: điền địa chỉ thanh ghi PLC. " +
                                "Giả định: số 32 bit có dấu, đơn vị PLC = 0.001 mm (R: 0.001 độ). SỬA Kiểu và Hệ số theo PLC.";
            foreach (string axis in axes)
            {
                string key = $"Pos.CalibPick.{axis}";
                if (map.FindItem(key) != null) continue;
                map.Items.Add(new SignalItem
                {
                    Group = GroupMotion,
                    Key = key,
                    Title = $"Độ bù GẮP gửi robot {axis} (Calib gắp)",
                    Address = "",
                    Kind = SignalKind.DWord,
                    Signed = true,
                    Scale = 0.001,
                    Note = note
                });
            }
        }

        public static PlcMap Create()
        {
            var map = new PlcMap { Machine = "Máy mẫu" };

            void Bit(string group, string key, string title, string address, TriggerMode mode, string note = "")
                => map.Items.Add(new SignalItem
                {
                    Group = group,
                    Key = key,
                    Title = title,
                    Address = address,
                    Kind = SignalKind.Bit,
                    Mode = mode,
                    Note = note
                });

            void Reg(string group, string key, string title, string address, SignalKind kind, bool signed,
                     string note = "", double scale = 1)
                => map.Items.Add(new SignalItem
                {
                    Group = group,
                    Key = key,
                    Title = title,
                    Address = address,
                    Kind = kind,
                    Signed = signed,
                    Note = note,
                    Scale = scale
                });

            // ---------- Điều khiển máy ----------
            Bit(GroupControl, "Cmd.Reset", "Reset", "M164", TriggerMode.Pulse);
            Bit(GroupControl, "Cmd.Start", "Start", "M303", TriggerMode.Pulse);
            Bit(GroupControl, "Cmd.Stop", "Stop", "M168", TriggerMode.Pulse);
            Bit(GroupControl, "Cmd.Manual", "Manual", "M162", TriggerMode.Level);
            Bit(GroupControl, "Cmd.ResetError", "Reset lỗi", "L500", TriggerMode.Pulse);
            Bit(GroupControl, "Io.EnableRobot", "IOENA RB", "M305", TriggerMode.Level);
            Bit(GroupControl, "Status.Run", "Run", "L1000", TriggerMode.Level);

            // ---------- Vị trí ----------
            const string posNote = "Giả định: số 32 bit (2 thanh ghi), đơn vị thô của PLC. Sửa Kiểu và Hệ số theo máy.";
            string[] axes = { "X", "Y", "Z", "R" };
            // Calib X, Y, Z, R: nơi C# GHI độ bù, PLC lấy chuyển cho robot (chế độ gửi "Qua PLC")
            const string calibNote = "C# ghi độ bù vào đây, PLC chuyển cho robot. Giả định: số 32 bit có dấu, đơn vị PLC = 0.001 mm (R: 0.001 độ). SỬA Kiểu và Hệ số theo PLC.";
            for (int a = 0; a < 4; a++)
                Reg(GroupMotion, $"Pos.Calib.{axes[a]}", $"Độ bù ĐẶT gửi robot {axes[a]} (Calib)", $"D{80 + 2 * a}",
                    SignalKind.DWord, true, calibNote, scale: 0.001);
            EnsurePickOffset(map);
            for (int a = 0; a < 4; a++)
                Reg(GroupMotion, $"Pos.Cur.{axes[a]}", $"Tọa độ hiện tại {axes[a]}", $"D{70 + 2 * a}", SignalKind.DWord, true, posNote);

            // ---------- Jog (giữ nút thì chạy, giả định) ----------
            for (int a = 0; a < 4; a++) Bit(GroupMotion, $"Jog.{axes[a]}+", $"Jog {axes[a]}+", $"M{251 + a}", TriggerMode.Hold);
            for (int a = 0; a < 4; a++) Bit(GroupMotion, $"JogFine.{axes[a]}+", $"Jog tinh {axes[a]}+ (X1)", $"M{255 + a}", TriggerMode.Hold);
            for (int a = 0; a < 4; a++) Bit(GroupMotion, $"Jog.{axes[a]}-", $"Jog {axes[a]}-", $"M{261 + a}", TriggerMode.Hold);
            for (int a = 0; a < 4; a++) Bit(GroupMotion, $"JogFine.{axes[a]}-", $"Jog tinh {axes[a]}- (X1)", $"M{265 + a}", TriggerMode.Hold);

            // ---------- Camera: đi tới vị trí chụp và tốc độ theo trục ----------
            var cam = new SignalTable
            {
                Key = "Cam",
                Title = "Trục camera 1 đến 8",
                Group = GroupCamera,
                Note = "Theo bảng gốc, tốc độ của cả 8 trục cùng trỏ về D486. Nếu mỗi trục có thanh ghi riêng, sửa trong bảng này.",
                Columns =
                {
                    new SignalColumn { Key = "Move", Title = "Đi tới vị trí chụp", Kind = SignalKind.Bit, Mode = TriggerMode.Pulse },
                    new SignalColumn { Key = "Speed", Title = "Tốc độ", Kind = SignalKind.Word }
                }
            };
            for (int i = 0; i < 8; i++)
                cam.Rows.Add(new SignalRow { Label = $"Camera {i + 1}", Addresses = { $"S{i}", "D486" } });
            map.Tables.Add(cam);

            // ---------- FOV kiểm tra ----------
            Bit(GroupFov, "Fov.Check", "Tín hiệu kiểm tra (Check signal)", "M601", TriggerMode.Level);

            var fov = new SignalTable
            {
                Key = "Fov",
                Title = "Kết quả theo FOV",
                Group = GroupFov,
                IndexAddress = "D600",
                Note = "D600 là chỉ số FOV đang kiểm tra (0 đến 8). Dx, Dy, Dr giả định là thanh ghi 16 bit có dấu.",
                Columns =
                {
                    new SignalColumn { Key = "Pass", Title = "Pass", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "Fail", Title = "Fail", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "NA", Title = "N/A", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "Dx", Title = "Dx", Kind = SignalKind.Word, Signed = true },
                    new SignalColumn { Key = "Dy", Title = "Dy", Kind = SignalKind.Word, Signed = true },
                    new SignalColumn { Key = "Dr", Title = "Dr", Kind = SignalKind.Word, Signed = true }
                }
            };
            for (int i = 0; i < 9; i++)
                fov.Rows.Add(new SignalRow
                {
                    Label = $"FOV {i}",
                    Addresses = { $"M{611 + i}", $"M{621 + i}", $"M{631 + i}", $"D{61 + i}", $"D{71 + i}", $"D{81 + i}" }
                });
            map.Tables.Add(fov);

            // ---------- Scan tay hút ----------
            Bit(GroupScan, "Scan.MoldClose", "Gửi tín hiệu đóng khuôn", "M739", TriggerMode.Pulse);
            Reg(GroupScan, "Scan.SN", "SN", "D640", SignalKind.DWord, false,
                "Giả định: số 32 bit (D640 và D641). Nếu SN là chuỗi ký tự thì báo để bổ sung kiểu chuỗi.");
            Reg(GroupScan, "Scan.Fixture", "Fixture", "D642", SignalKind.DWord, false,
                "Giả định: số 32 bit (D642 và D643).");

            // Cột N/A lấy ĐÚNG theo bảng gốc: M672 xuất hiện hai lần và các hàng sau lệch một so với quy luật M671 + chỉ số.
            string[] scanNa = { "M671", "M672", "M672", "M673", "M674", "M675", "M676", "M677", "M678" };
            var scan = new SignalTable
            {
                Key = "Scan",
                Title = "Tín hiệu scan theo tay hút",
                Group = GroupScan,
                IndexAddress = "D601",
                Note = "D601 là chỉ số tay hút (0 đến 8). Cột N/A giữ nguyên theo bảng gốc và đang bị trùng M672 ở hàng 1 và 2: kiểm tra lại với PLC.",
                Columns =
                {
                    new SignalColumn { Key = "Signal", Title = "Scan signal", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "Pass", Title = "Pass", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "Fail", Title = "Fail", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "NA", Title = "N/A", Kind = SignalKind.Bit, Mode = TriggerMode.Level }
                }
            };
            for (int i = 0; i < 9; i++)
                scan.Rows.Add(new SignalRow
                {
                    Label = $"Tay hút {i}",
                    Addresses = { $"M{641 + i}", $"M{651 + i}", $"M{661 + i}", scanNa[i] }
                });
            map.Tables.Add(scan);

            var test = new SignalTable
            {
                Key = "ScanTest",
                Title = "Kết quả test (K1 đến K8)",
                Group = GroupScan,
                Columns = { new SignalColumn { Key = "Result", Title = "Kết quả test", Kind = SignalKind.Word } }
            };
            for (int i = 0; i < 8; i++)
                test.Rows.Add(new SignalRow { Label = $"K{i + 1}", Addresses = { $"D{1001 + i}" } });
            map.Tables.Add(test);

            // ---------- Lỗi ----------
            Bit(GroupError, "Err.RunCam", "RUN CAM Error", "L850", TriggerMode.Level);

            var err = new SignalTable
            {
                Key = "Err",
                Title = "Lỗi theo thiết bị 1 đến 8",
                Group = GroupError,
                Columns =
                {
                    new SignalColumn { Key = "Cam", Title = "Camera lỗi", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "Scanner", Title = "Scanner lỗi", Kind = SignalKind.Bit, Mode = TriggerMode.Level },
                    new SignalColumn { Key = "IT", Title = "IT lỗi", Kind = SignalKind.Bit, Mode = TriggerMode.Level }
                }
            };
            for (int i = 0; i < 8; i++)
                err.Rows.Add(new SignalRow { Label = $"Số {i + 1}", Addresses = { $"L{851 + i}", $"L{861 + i}", $"L{871 + i}" } });
            map.Tables.Add(err);

            map.Normalize();
            return map;
        }
    }
}
