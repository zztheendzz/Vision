namespace Communication.Configuration
{
    /// <summary>Bộ thuộc tính của một loại thiết bị.</summary>
    public sealed class DeviceTypeSchema
    {
        public DeviceType Type { get; init; }
        /// <summary>Tên hiển thị của loại, ví dụ "Máy quét".</summary>
        public string DisplayName { get; init; } = "";
        /// <summary>Tiền tố đặt tên thiết bị mới: "Camera" → "Camera 1", "Camera 2"...</summary>
        public string NamePrefix { get; init; } = "";
        /// <summary>Thiết bị loại này có chọn "PLC nguồn" không.</summary>
        public bool HasPlcSource { get; init; }
        /// <summary>Trạng thái bật/tắt khi vừa thêm.</summary>
        public bool DefaultEnabled { get; init; }
        /// <summary>Cài đặt chung cho cả loại (chế độ, ký tự kết thúc...).</summary>
        public IReadOnlyList<FieldDef> GroupFields { get; init; } = Array.Empty<FieldDef>();
        /// <summary>Thuộc tính riêng của từng thiết bị.</summary>
        public IReadOnlyList<FieldDef> DeviceFields { get; init; } = Array.Empty<FieldDef>();

        public FieldDef? FindGroup(string key) => GroupFields.FirstOrDefault(f => f.Key == key);
        public FieldDef? FindDevice(string key) => DeviceFields.FirstOrDefault(f => f.Key == key);
    }

    /// <summary>
    /// Thuộc tính của 6 loại thiết bị. Giá trị mặc định lấy từ màn hình mẫu; riêng camera và các danh sách lựa chọn
    /// là giá trị gợi ý, chỉnh lại ở đây nếu cần.
    /// PLC và Robot có ô "Chế độ": Mitsubishi (nối thật, mặc định) hoặc Simulated (chạy giả lập khi chưa có thiết bị).
    /// </summary>
    public static class DeviceSchemas
    {
        // Khóa dùng ở nhiều nơi (code kết nối đọc theo khóa này)
        public const string KeyMode = "Mode";
        public const string KeyIp = "Ip";
        public const string KeyPort = "Port";
        public const string KeyTimeoutMs = "TimeoutMs";
        public const string KeyTx = "TxTerminator";
        public const string KeyRx = "RxTerminator";

        public const string Simulated = "Simulated";
        public const string Mitsubishi = "Mitsubishi";
        public const string GigE = "GigE Vision";
        /// <summary>PLC Mitsubishi qua MX Component (COM); dùng tạm để thử với GX Simulator3.</summary>
        public const string MxComponent = "MX Component";

        /// <summary>Robot: gửi độ lệch (dX, dY, dRz) vào biến điểm trung gian; chương trình robot tự cộng vào điểm.</summary>
        public const string RobotSendOffset = "Gửi độ lệch (điểm trung gian)";
        /// <summary>Robot: app tính điểm mới rồi ghi đè lên điểm đã chọn.</summary>
        public const string RobotSendOverwrite = "Ghi đè điểm đã tính";
        /// <summary>Robot: gửi gói tin chữ (mẫu tùy chọn) chứa độ lệch qua TCP tới chương trình robot.</summary>
        public const string RobotSendPacket = "Gửi gói tin độ lệch (TCP)";
        /// <summary>Robot: ghi độ bù vào thanh ghi Calib X, Y, Z, R của PLC (địa chỉ ở Bản đồ tín hiệu PLC); PLC chuyển cho robot.</summary>
        public const string RobotSendViaPlc = "Qua PLC (ghi thanh ghi Calib X, Y, Z, R)";

        /// <summary>Chế độ Qua PLC: không gửi độ bù nào (chỉ chạy và hiển thị kết quả).</summary>
        public const string PlcSourceNone = "Không gửi";
        /// <summary>Chế độ Qua PLC: gửi độ bù của node PlaceCompensation vào nhóm thanh ghi Calib (đặt).</summary>
        public const string PlcSourcePlace = "Chỉ PlaceCompensation (độ bù đặt)";
        /// <summary>Chế độ Qua PLC: gửi độ bù của node PickCompensation vào nhóm thanh ghi Calib gắp.</summary>
        public const string PlcSourcePick = "Chỉ PickCompensation (độ bù gắp)";
        /// <summary>Chế độ Qua PLC: gửi cả hai, mỗi loại vào nhóm thanh ghi riêng.</summary>
        public const string PlcSourceBoth = "Cả hai (đặt và gắp)";

        private static FieldDef Txt(string key, string label, string def = "", string section = "", string hint = "")
            => new(key, label, FieldKind.Text, def, section, Hint: hint);
        private static FieldDef Int(string key, string label, int def, double min, double max, string section = "", string hint = "")
            => new(key, label, FieldKind.Integer, def.ToString(), section, Min: min, Max: max, Hint: hint);
        private static FieldDef Num(string key, string label, string def, double min, double max, string section = "", string hint = "")
            => new(key, label, FieldKind.Number, def, section, Min: min, Max: max, Hint: hint);
        private static FieldDef Bool(string key, string label, bool def, string section = "", string hint = "")
            => new(key, label, FieldKind.Bool, def ? "true" : "false", section, Hint: hint);
        private static FieldDef Choice(string key, string label, string def, string[] choices, string section = "")
            => new(key, label, FieldKind.Choice, def, section, choices);
        private static FieldDef Addr(string key, string label, string def, string section = "Bit / thanh ghi PLC")
            => new(key, label, FieldKind.Address, def, section);
        private static FieldDef Ip(string key, string label, string def, string section = "Kết nối")
            => new(key, label, FieldKind.IpAddress, def, section);
        private static FieldDef Term(string key, string label, string def = @"\r\n")
            => new(key, label, FieldKind.Terminator, def, "Ký tự kết thúc", Hint: @"Gõ \r \n \t \0 hoặc \xHH");

        /// <summary>Làm ô chỉ hiện khi Chế độ = Mitsubishi (các thông số nối thật).</summary>
        private static FieldDef WhenSend(string mode, FieldDef f) => f with { ShowWhenKey = "SendMode", ShowWhenValue = mode };
        private static FieldDef WhenMitsubishi(FieldDef f) => f with { ShowWhenKey = KeyMode, ShowWhenValue = Mitsubishi };

        public static readonly IReadOnlyList<DeviceTypeSchema> All = new List<DeviceTypeSchema>
        {
            new()
            {
                Type = DeviceType.Camera, DisplayName = "Camera", NamePrefix = "Camera",
                HasPlcSource = true, DefaultEnabled = false,
                DeviceFields = new[]
                {
                    Choice("Protocol", "Giao thức truyền thông", GigE, new[] { GigE, Simulated }, "Kết nối"),
                    Txt("DeviceId", "Thiết bị", "default", "Kết nối", "Tên camera lấy từ nút Quét; \"default\" = camera đầu tiên"),
                    Choice("ColorSpace", "Không gian màu", "Mono", new[] { "Mono", "RGB" }, "Ảnh"),
                    Choice("Trigger", "Kiểu trigger", "Liên tục", new[] { "Liên tục", "Software", "Hardware" }, "Ảnh"),
                    Choice("PixelFormat", "Định dạng pixel", "Mono8", new[] { "Mono8", "Mono12", "BayerRG8", "RGB8" }, "Ảnh"),
                    Int("HeartbeatMs", "Heartbeat (ms)", 3000, 100, 600000, "GigE"),
                    Int("PacketSize", "Packet size (B)", 1500, 576, 16000, "GigE"),
                    Addr("ErrorBit", "Bit Error", "L851"),
                    Num("Exposure", "Phơi sáng (µs)", "5000", 1, 10_000_000, "Tinh chỉnh"),
                    Num("DarkLevel", "Vùng tối", "0", 0, 4095, "Tinh chỉnh"),
                    Num("Gain", "Gain", "0", 0, 100, "Tinh chỉnh"),
                    Num("Gamma", "Gamma", "1", 0.1, 4, "Tinh chỉnh"),
                }
            },
            new()
            {
                Type = DeviceType.Scanner, DisplayName = "Máy quét", NamePrefix = "Scanner",
                HasPlcSource = true, DefaultEnabled = false,
                GroupFields = new[]
                {
                    Choice(KeyMode, "Chế độ", Simulated, new[] { Simulated, "TCP Client", "Serial" }, "Chung"),
                    Term(KeyTx, "Ký tự kết thúc TX"),
                    Term(KeyRx, "Ký tự kết thúc RX"),
                },
                DeviceFields = new[]
                {
                    Txt("StartChar", "Bắt đầu", "E", "Giao thức"),
                    Txt("EndChar", "Kết thúc", "D", "Giao thức"),
                    Txt("ScanCommand", "Lệnh quét", "TRIGGER", "Giao thức"),
                    Txt("NgString", "Chuỗi NG", "", "Giao thức"),
                    Txt("StopCommand", "Lệnh dừng quét", "Y", "Giao thức"),
                    Addr("TriggerBit", "Trigger Bit", "M641"),
                    Addr("PassBit", "Bit Pass", "M651"),
                    Addr("FailBit", "Bit Fail", "M661"),
                    Addr("ErrorBit", "Bit Error", "L861"),
                    Int("CodeLength", "Độ dài mã", 0, 0, 1000, "Giao thức", "0 = không kiểm tra độ dài"),
                }
            },
            new()
            {
                // PLC Mitsubishi qua HslCommunication (giao thức MC)
                Type = DeviceType.PLC, DisplayName = "PLC", NamePrefix = "PLC",
                HasPlcSource = false, DefaultEnabled = true,
                DeviceFields = new[]
                {
                    Choice(KeyMode, "Chế độ", Mitsubishi, new[] { Mitsubishi, MxComponent, Simulated }, "Kết nối"),
                    new FieldDef("StationNo", "Số trạm logic (MX Component)", FieldKind.Integer, "1", "Kết nối", Min: 0, Max: 1024,
                                 Hint: "Tạo trong Communication Setting Utility (GX Simulator3 hoặc PLC thật)")
                        { ShowWhenKey = KeyMode, ShowWhenValue = MxComponent },
                    WhenMitsubishi(Ip(KeyIp, "IP", "192.168.0.195")),
                    WhenMitsubishi(Int(KeyPort, "Cổng", 3006, 1, 65535, "Kết nối")),
                    WhenMitsubishi(Choice("Frame", "Khung dữ liệu", "Binary", new[] { "Binary", "Ascii" }, "Kết nối")),
                    WhenMitsubishi(Int(KeyTimeoutMs, "Timeout (ms)", 3000, 100, 60000, "Kết nối")),
                    Int("PollMs", "Poll (ms)", 20, 1, 60000, "Kết nối"),
                    Bool("UseAxis", "Dùng trục", false, "Kết nối"),
                    Addr("StartBit", "Bit Start", "M303"),
                    Addr("StopBit", "Bit Stop", "M162"),
                    Addr("ResetBit", "Bit Reset", "M164"),
                    Addr("ManualBit", "Bit Manual", "M162"),
                    Addr("ResetErrorBit", "Bit Reset Error", "I500"),
                    Addr("ErrorBit", "Bit Error", "I693"),
                    Addr("PickOnlyBit", "Bit pick-only", ""),
                    Addr("GDecodeReg", "G-Decode Reg", ""),
                    Addr("CameraTrigger", "Bit PLC yêu cầu chụp", ""),
                    Addr("DoneBit", "Bit vision xong (báo PLC)", ""),
                    Addr("OkBit", "Bit kết quả OK (báo PLC)", ""),
                    Addr("NgBit", "Bit kết quả NG (báo PLC)", ""),
                    Addr("VisionErrBit", "Bit lỗi vision (báo PLC)", ""),
                    Addr("FovReg", "Thanh ghi FOV", "D600"),
                    Addr("RunBit", "Bit chạy", "L1000"),
                }
            },
            new()
            {
                // Robot Mitsubishi MELFA qua Melfa.Robot (R-protocol)
                Type = DeviceType.Robot, DisplayName = "Robot", NamePrefix = "Robot",
                HasPlcSource = true, DefaultEnabled = true,
                DeviceFields = new[]
                {
                    Choice(KeyMode, "Chế độ", Mitsubishi, new[] { Mitsubishi, Simulated }, "Kết nối"),
                    WhenMitsubishi(Ip(KeyIp, "IP", "192.168.0.20")),
                    WhenMitsubishi(Int(KeyPort, "Cổng (đã đặt ở bộ điều khiển)", 10009, 1, 65535, "Kết nối")),
                    WhenMitsubishi(Int("RobotNo", "Số robot", 1, 1, 3, "Kết nối")),
                    WhenMitsubishi(Txt("ClientName", "Tên phiên làm việc", "VisionApp", "Kết nối")),
                    WhenMitsubishi(Bool("TakeControl", "Chiếm quyền điều khiển khi kết nối", false, "Điều khiển",
                        "Bật quyền điều khiển, dừng chương trình đang chạy và reset. Cần để đọc/ghi điểm của chương trình")),
                    WhenMitsubishi(Bool("EnableOnConnect", "Bật servo khi kết nối", false, "Điều khiển",
                        "Tắt là an toàn: chương trình không tự bật servo robot. Bật ô này cũng chiếm quyền điều khiển")),
                    Choice("SendMode", "Cách gửi cho robot", RobotSendOffset, new[] { RobotSendOffset, RobotSendOverwrite, RobotSendPacket, RobotSendViaPlc }, "Độ bù"),
                    WhenSend(RobotSendViaPlc, Choice("PlcSource", "Gửi độ bù của tool nào", PlcSourcePlace,
                        new[] { PlcSourceNone, PlcSourcePlace, PlcSourcePick, PlcSourceBoth }, "Độ bù")),
                    Txt("TargetPoint", "Điểm cần bù", "P1", "Độ bù",
                        "Chọn từ danh sách đọc ở nút 'Đọc danh sách điểm'. Dùng cho chế độ ghi đè điểm"),
                    Txt("PointProgram", "Chương trình chứa điểm", "", "Độ bù",
                        "Tên chương trình robot có các điểm P1, P2... (đọc/ghi điểm qua chương trình này; không được đang chạy)"),
                    WhenSend(RobotSendOffset, Txt("OffsetPoint", "Biến điểm trung gian (toàn cục)", "P_VOFS", "Độ bù",
                        "Chế độ độ lệch: ghi (dX,dY,0,0,0,dRz) vào biến toàn cục này bằng lệnh EXEC; chương trình robot cộng vào điểm")),
                    WhenSend(RobotSendPacket, Txt("PacketHost", "Địa chỉ nhận gói tin", "", "Gói tin",
                        "Trống = dùng IP của robot ở trên")),
                    WhenSend(RobotSendPacket, Int("PacketPort", "Cổng nhận gói tin", 10002, 1, 65535, "Gói tin",
                        "Cổng TCP mà chương trình robot đang nghe (khác cổng điều khiển)")),
                    WhenSend(RobotSendPacket, Txt("PacketTemplate", "Mẫu gói tin độ lệch", @"{dx},{dy},{drz}\r", "Gói tin",
                        @"Ô thay thế: {id} {dx} {dy} {dz} {drz} (góc xoay, cũng viết {dc}); định dạng riêng {dx:F2}; mã thoát \r \n \t \xHH. Ví dụ X{dx},Y{dy},R{drz}\r\n")),
                    WhenSend(RobotSendPacket, Txt("PacketReject", "Mẫu gói tin báo NG", @"NG\r", "Gói tin",
                        "Gửi khi không thấy vật. Để trống = không gửi gì và báo lỗi")),
                    WhenSend(RobotSendPacket, Int("PacketDecimals", "Số chữ số lẻ", 3, 0, 9, "Gói tin")),
                    WhenSend(RobotSendPacket, Num("PacketScale", "Hệ số nhân độ lệch", "1", 0.000001, 1000000, "Gói tin",
                        "Ví dụ 1000 để gửi micromet dạng số nguyên (kèm số lẻ = 0)")),
                    Choice("RzAxis", "Trục xoay Rz của điểm", "C", new[] { "A", "B", "C" }, "Độ bù"),
                    Int("PointCount", "Số điểm quét (P1..Pn)", 20, 1, 200, "Độ bù", "Khi đọc danh sách, thử lần lượt P1 đến Pn"),
                    WhenMitsubishi(Txt("RejectCommand", "Lệnh báo NG", "", "Độ bù",
                        "Để trống = chưa cấu hình, chương trình sẽ báo lỗi khi cần báo NG")),
                    Num("MaxOffsetMm", "Giới hạn gửi lệch (mm)", "50", 0.1, 1000, "An toàn"),
                    Num("MaxOffsetDeg", "Giới hạn gửi góc (độ)", "30", 0.1, 360, "An toàn"),
                    Num("MaxMoveMm", "Giới hạn di chuyển tới tọa độ (mm)", "300", 1, 5000, "An toàn",
                        "Từ chối di chuyển nếu đích cách vị trí hiện tại xa hơn số này"),
                    Addr("IoEnaBit", "IOENA", "M305"),
                    Txt("Model3d", "Model 3D", "", "Hiển thị", "Đường dẫn file model"),
                }
            },
            new()
            {
                Type = DeviceType.Fixture, DisplayName = "Khuôn test", NamePrefix = "Fixture",
                HasPlcSource = true, DefaultEnabled = true,
                GroupFields = new[]
                {
                    Choice(KeyMode, "Chế độ", "TCP Split", new[] { Simulated, "TCP", "TCP Split" }, "Chung"),
                    Choice("OpenKind", "Kiểu mở khuôn test", "Mở ngay", new[] { "Mở ngay", "Mở sau khi có kết quả" }, "Chung"),
                    Bool("OnlyFail", "Only Fail", false, "Chung"),
                    Bool("AskMode", "ASK Mode", true, "Chung"),
                    Term(KeyTx, "Ký tự kết thúc TX"),
                    Term(KeyRx, "Ký tự kết thúc RX"),
                    Int("ListenPort", "Cổng nhận", 5002, 1, 65535, "Chung"),
                },
                DeviceFields = new[]
                {
                    Ip(KeyIp, "IP", "10.153.66.83"),
                    Int(KeyPort, "Cổng", 9999, 1, 65535, "Kết nối"),
                    Addr("ResultReg", "Thanh ghi kết quả", "D1001"),
                    Int("Slots", "Số slot", 1, 1, 64, "Kết nối"),
                    Int("TestTimeoutS", "Timeout test (giây)", 100, 1, 36000, "Thời gian"),
                    Int("CloseToOpenMs", "Nghỉ Đóng→Mở (ms)", 2000, 0, 600000, "Thời gian"),
                    Addr("HasSampleBit", "Bit có bản", "M1001"),
                    Bool("CloseOnly", "Chỉ đóng/không gửi SN", false, "Kết nối"),
                }
            },
            new()
            {
                Type = DeviceType.PC, DisplayName = "PC", NamePrefix = "PC",
                HasPlcSource = false, DefaultEnabled = false,
                GroupFields = new[]
                {
                    Choice(KeyMode, "Chế độ", "TCP Client", new[] { Simulated, "TCP Client" }, "Chung"),
                    Term(KeyTx, "Ký tự kết thúc TX"),
                    Term(KeyRx, "Ký tự kết thúc RX"),
                },
                DeviceFields = new[]
                {
                    Ip(KeyIp, "IP", "192.168.0.1"),
                    Int(KeyPort, "Cổng", 5000, 1, 65535, "Kết nối"),
                    Int(KeyTimeoutMs, "Timeout (ms)", 2000, 100, 600000, "Kết nối"),
                    Addr("ErrorBit", "Bit Error", "L871"),
                }
            },
        };

        public static DeviceTypeSchema For(DeviceType type) => All.First(s => s.Type == type);
    }
}
