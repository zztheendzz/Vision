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
        /// <summary>
        /// PLC Mitsubishi qua MX Component (COM), dùng tạm để thử với GX Simulator3.
        /// ĐANG TẮT: code vẫn còn nhưng chỉ được biên dịch và hiện trong giao diện khi định nghĩa MX_COMPONENT
        /// (Project Properties → Build → Conditional compilation symbols) và có project MxBridge.
        /// </summary>
        public const string MxComponent = "MX Component";

        /// <summary>Robot: gửi độ lệch (dX, dY, dRz) vào biến điểm trung gian; chương trình robot tự cộng vào điểm.</summary>
        public const string RobotSendOffset = "Gửi độ lệch (điểm trung gian)";
        /// <summary>Đã bỏ chế độ ghi đè điểm. Giữ hằng số để file cấu hình cũ còn giá trị này được chuyển về RobotSendOffset (DeviceCatalog.Repair).</summary>
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

        public const string ScanTcp = "Quét dải IP (TCP)";
        public const string ScanUdp = "Broadcast (UDP)";

        private static FieldDef Txt(string key, string label, string def = "", string section = "", string hint = "")
            => new(key, label, FieldKind.Text, def, section, Hint: hint);
        /// <summary>Ô chung cho mọi loại thiết bị: sau bao lâu tự nối lại khi mất kết nối (0 = không tự nối).</summary>
        public const string KeyReconnectSec = "ReconnectSec";
        private static FieldDef ReconnectField(int seconds = 5)
            => Int(KeyReconnectSec, "Tự nối lại sau (giây, 0 = tắt)", seconds, 0, 3600, "Chung",
                   "Thiết bị đang nối mà mất kết nối thì sau chừng này giây tự nối lại, lặp lại tới khi nối được. Thiết bị tự bấm Ngắt kết nối thì không tự nối.");

        private static FieldDef Int(string key, string label, int def, double min, double max, string section = "", string hint = "")
            => new(key, label, FieldKind.Integer, def.ToString(), section, Min: min, Max: max, Hint: hint);
        /// <summary>Các lệnh gửi khuôn test: khóa, tên hiển thị, mẫu gửi, mẫu trả lời 1 (OK/YES/OPENED), mẫu trả lời 2 (ERROR/NO/CLOSED).</summary>
        public const string OpenModeStacked = "Khuôn chồng: mở trên HOẶC dưới";
        public const string OpenModeSides = "2 bên A/B: mỗi bên chỉ 1 khuôn mở";
        public const string OpenModeSingle = "Toàn hệ thống chỉ 1 khuôn mở";

        public static readonly (string Key, string Title, string Send, string Ok, string Err)[] FixtureCommands =
        {
            ("Open",        "OPEN – mở khuôn",           "OPEN_{SS}",        "OPEN_{SS}_OK",  "OPEN_{SS}_ERROR"),
            ("Close",       "CLOSE – đóng khuôn",        "CLOSE_{SS}",       "CLOSE_{SS}_OK", "CLOSE_{SS}_ERROR"),
            ("OpenAll",     "OPEN ALL – mở tất cả",      "OPEN_ALL",         "OPEN_ALL_OK",   "OPEN_ALL_ERROR_{SLOTS}"),
            ("CloseAll",    "CLOSE ALL – đóng tất cả",   "CLOSE_ALL",        "CLOSE_ALL_OK",  "CLOSE_ALL_ERROR_{SLOTS}"),
            ("CheckSensor", "CHECKSENSOR – kiểm cảm biến", "CHECKSENSOR_{SS}", "YES_{SS}",      "NO_{SS}"),
            ("State",       "STATE – trạng thái slot",   "STATE_{SS}",       "",              ""),   // khuôn trả dòng kết quả slot (B_/P_/R_...), không phải OPENED/CLOSED
        };

        /// <summary>Kết quả slot: khóa, tên, mẫu nhận về (null = không có), mã gửi PLC (null = không gửi).</summary>
        public static readonly (string Key, string Title, string? Pattern, int? Plc)[] FixtureResults =
        {
            ("Busy",   "BUSY",   "B_{NN}_{SS}", 0),
            ("Empty",  "EMPTY",  null,          1),
            ("Pass",   "PASS",   "P_{NN}_{SS}", 2),
            ("Repair", "REPAIR", "R_{NN}_{SS}", 3),
            ("Fail",   "FAIL",   "F_{NN}_{SS}", 4),
            ("Nan",    "NAN",    "I_{NN}_{SS}", 5),
            ("Error",  "ERROR",  "E_{NN}_{SS}", null),
        };

        /// <summary>
        /// Cài đặt chung của khuôn test về lệnh và kết quả. {SS} = số khuôn, {NN} = số slot trong khuôn, {SLOTS} = danh sách slot lỗi.
        /// Khóa dạng "Cmd.Open.Send" và "Res.Pass.Pattern"; giao diện gom các khóa này thành hai bảng riêng.
        /// </summary>
        private static IEnumerable<FieldDef> FixtureProtocolFields()
        {
            foreach (var c in FixtureCommands)
            {
                yield return Txt($"Cmd.{c.Key}.Send", $"{c.Title} – lệnh gửi", c.Send, "Lệnh");
                yield return Txt($"Cmd.{c.Key}.Ok", $"{c.Title} – trả lời 1", c.Ok, "Lệnh");
                yield return Txt($"Cmd.{c.Key}.Err", $"{c.Title} – trả lời 2", c.Err, "Lệnh");
            }
            foreach (var r in FixtureResults)
            {
                if (r.Pattern != null) yield return Txt($"Res.{r.Key}.Pattern", $"{r.Title} – mẫu nhận về", r.Pattern, "Kết quả");
                if (r.Plc != null) yield return Int($"Res.{r.Key}.Plc", $"{r.Title} – mã gửi PLC", r.Plc.Value, 0, 255, "Kết quả");
            }
        }

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
                GroupFields = new[] { ReconnectField() },
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
                    // Thông số camera chỉnh bằng thanh trượt ở màn xem trực tiếp; để trống = không đặt khi nối
                    Txt("CamGamma", "Gamma camera (thanh trượt)", "", "Tinh chỉnh", "Số gốc của camera; trống = không đặt"),
                    Txt("CamContrast", "Tương phản camera (thanh trượt)", "", "Tinh chỉnh", "Số gốc của camera; trống = không đặt"),
                    Txt("CamFrameRate", "Tốc độ khung hình (fps)", "", "Tinh chỉnh", "Trống = không đặt"),
                }
            },
            new()
            {
                Type = DeviceType.Scanner, DisplayName = "Máy quét", NamePrefix = "Scanner",
                HasPlcSource = true, DefaultEnabled = false,
                GroupFields = new[]
                {
                    ReconnectField(),
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
                GroupFields = new[] { ReconnectField() },
                DeviceFields = new[]
                {
#if MX_COMPONENT
                    Choice(KeyMode, "Chế độ", Mitsubishi, new[] { Mitsubishi, MxComponent, Simulated }, "Kết nối"),
                    new FieldDef("StationNo", "Số trạm logic (MX Component)", FieldKind.Integer, "1", "Kết nối", Min: 0, Max: 1024,
                                 Hint: "Tạo trong Communication Setting Utility (GX Simulator3 hoặc PLC thật)")
                        { ShowWhenKey = KeyMode, ShowWhenValue = MxComponent },
#else
                    Choice(KeyMode, "Chế độ", Mitsubishi, new[] { Mitsubishi, Simulated }, "Kết nối"),
#endif
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
                    Addr("CameraTrigger", "Bit PLC yêu cầu chụp (app đọc thấy 1 thì tự về 0)", "M601"),
                    Addr("DoneBit", "Bit vision xong (báo PLC)", ""),
                    Addr("OkBit", "Bit kết quả OK (báo PLC)", ""),
                    Addr("NgBit", "Bit kết quả NG (báo PLC)", ""),
                    Addr("VisionErrBit", "Bit lỗi vision (báo PLC)", ""),
                    Addr("FovReg", "Thanh ghi FOV", "D600"),
                    Addr("RunBit", "Bit chạy", "L1000"),
                    Addr("RobotLostBit", "Bit báo mất kết nối robot (1 = mất)", ""),
                    Addr("CameraLostBit", "Bit báo mất kết nối camera (1 = mất)", ""),
                    Addr("ScannerLostBit", "Bit báo mất kết nối scanner (1 = mất)", ""),
                    Addr("FixtureLostBit", "Bit báo mất kết nối khuôn test (1 = mất)", ""),
                }
            },
            new()
            {
                // Robot Mitsubishi MELFA qua Melfa.Robot (R-protocol)
                Type = DeviceType.Robot, DisplayName = "Robot", NamePrefix = "Robot",
                HasPlcSource = true, DefaultEnabled = true,
                GroupFields = new[] { ReconnectField() },
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
                    Choice("SendMode", "Cách gửi cho robot", RobotSendOffset, new[] { RobotSendOffset, RobotSendPacket, RobotSendViaPlc }, "Độ bù"),
                    WhenSend(RobotSendViaPlc, Choice("PlcSource", "Gửi độ bù của tool nào", PlcSourcePlace,
                        new[] { PlcSourceNone, PlcSourcePlace, PlcSourcePick, PlcSourceBoth }, "Độ bù")),
                    Txt("TargetPoint", "Điểm cần bù", "P1", "Độ bù",
                        "Điểm đang chọn khi đọc / dạy điểm trong màn hình điều khiển robot (đọc danh sách ở nút 'Đọc danh sách điểm')"),
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
                    Addr("IoEnaBit", "Bit quyền điều khiển robot (1 = PLC, 0 = App)", "M305"),
                    Txt("Model3d", "Model 3D", "", "Hiển thị", "Đường dẫn file model"),
                }
            },
            new()
            {
                Type = DeviceType.Fixture, DisplayName = "Khuôn test", NamePrefix = "Fixture",
                HasPlcSource = true, DefaultEnabled = true,
                GroupFields = new[]
                {
                    ReconnectField(),
                    Choice(KeyMode, "Chế độ", "TCP Split", new[] { Simulated, "TCP", "TCP Split" }, "Chung"),
                    Choice("OpenKind", "Kiểu mở khuôn test", "Mở ngay", new[] { "Mở ngay", "Mở sau khi có kết quả" }, "Chung"),
                    Bool("OnlyFail", "Only Fail", false, "Chung"),
                    Bool("AskMode", "ASK Mode", true, "Chung"),
                    Term(KeyTx, "Ký tự kết thúc TX"),
                    Term(KeyRx, "Ký tự kết thúc RX"),
                    Int("ListenPort", "Cổng nhận", 5002, 1, 65535, "Chung"),
                    Int("ReplyTimeoutS", "Chờ trả lời từ khuôn (giây)", 20, 1, 600, "Chung",
                        "Một ô chung cho mọi lệnh gửi khuôn có chờ trả lời: mở, đóng, STATE, CHECKSENSOR, START. Hết giờ mà khuôn không trả lời thì báo lỗi. (Quét khuôn có thời gian chờ riêng bên dưới)"),
                    Txt("Scan.Command", "Quét khuôn – lệnh gửi", "ASK", "Quét khuôn",
                        "Lệnh gửi tới tất cả địa chỉ để hỏi khuôn. Mặc định ASK, đổi được theo khuôn của bạn."),
                    Int("Scan.Port", "Quét khuôn – cổng", 9999, 1, 65535, "Quét khuôn", "Cổng của khuôn để hỏi. Mặc định 9999."),
                    Choice("Scan.Method", "Quét khuôn – cách gửi", ScanTcp, new[] { ScanTcp, ScanUdp }, "Quét khuôn"),
                    Txt("Scan.Range", "Quét khuôn – dải IP", "", "Quét khuôn",
                        "Ví dụ 10.153.66.1-254 hoặc 10.153.66.0/24. Để trống = dải /24 của card mạng đang chạy. (Chỉ dùng khi quét theo TCP)"),
                    Int("Scan.TimeoutMs", "Quét khuôn – chờ trả lời (ms)", 800, 100, 10000, "Quét khuôn"),
                    Txt("Scan.Pattern", "Quét khuôn – mẫu trả lời", "{STATE}_{SS}_{NN}", "Quét khuôn",
                        "Một dòng trả lời: {STATE} = P/F/R (PASS/FAIL/REPAIR; còn B, I, E), {SS} = số khuôn (01, 02...), {NN} = số slot. Ví dụ P_01_2 = khuôn 1, slot 2 PASS."),
                    Bool("Interlock", "Khóa an toàn khi chạy tự động", true, "Chung",
                         "Chỉ áp dụng khi chạy tự động, bấm tay không bị chặn. Đóng chỉ khi có hàng; mở chỉ khi FAIL/REPAIR; khuôn nằm trên không mở khi khuôn dưới đang mở. Tắt khi chỉ mô phỏng."),
                    Choice("OpenMode", "Chế độ đóng/mở khuôn", OpenModeStacked,
                           new[] { OpenModeStacked, OpenModeSides, OpenModeSingle }, "Chế độ đóng/mở"),
                    Addr("Hs.PickedBit", "Bit robot gắp xong (→ mở khuôn)", "M739", "Bắt tay robot"),
                    Addr("Hs.PlacedBit", "Bit robot đặt xong (→ đóng khuôn)", "M740", "Bắt tay robot"),
                    Addr("Hs.FixtureReg", "Thanh ghi số khuôn đích (SS)", "D642", "Bắt tay robot"),
                    Int("Hs.OpenDelayMs", "Trễ trước khi MỞ (ms)", 0, 0, 600000, "Bắt tay robot",
                        "Từ lúc thấy bit 'robot gắp xong' = 1 đến lúc gửi lệnh mở."),
                    Int("Hs.CloseDelayMs", "Trễ trước khi ĐÓNG (ms)", 0, 0, 600000, "Bắt tay robot",
                        "Từ lúc thấy bit 'robot đặt xong' = 1 đến lúc gửi lệnh đóng."),
                    Int("Hs.PollMs", "Chu kỳ đọc bit (ms)", 50, 10, 1000, "Bắt tay robot"),
                }.Concat(FixtureProtocolFields()).ToArray(),
                DeviceFields = new[]
                {
                    Ip(KeyIp, "IP", "10.153.66.83"),
                    Int(KeyPort, "Cổng", 9999, 1, 65535, "Kết nối"),
                    Int("FixtureNo", "Số khuôn (SS)", 1, 1, 99, "Kết nối", "Số khuôn dùng trong lệnh: khuôn 1 → OPEN_01"),
                    new FieldDef("ResultReg", "Thanh ghi kết quả (mỗi slot một)", FieldKind.Address, "D1001", "Bit / thanh ghi PLC",
                        Hint: "Ghi mã kết quả của slot. Một địa chỉ: các slot lấy liên tiếp (D1001, D1002...). Hoặc liệt kê từng slot, cách dấu phẩy."),
                    Int("Slots", "Số slot", 1, 1, 64, "Kết nối"),
                    Int("BelowNo", "Khuôn nằm ngay dưới (SS)", 0, 0, 99, "Kết nối",
                        "Số khuôn nằm ngay bên dưới khuôn này, 0 = không có. Khuôn dưới đang mở thì khuôn này bị cấm mở (tránh va chạm robot)."),
                    Choice("Side", "Bên (A / B)", "A", new[] { "A", "B" }, "Kết nối"),
                    Bool("HasSensor", "Có cảm biến hàng", true, "Kết nối",
                         "Có: gửi CHECKSENSOR để biết có hàng. Không: dựa vào bit 'robot đặt xong' (mặc định M740)."),
                    Int("TestTimeoutS", "Timeout test (giây)", 100, 1, 36000, "Thời gian"),
                    Int("CloseToOpenMs", "Nghỉ Đóng→Mở (ms)", 2000, 0, 600000, "Thời gian"),
                    new FieldDef("HasSampleBit", "Bit có bản (mỗi slot một)", FieldKind.Address, "M1001", "Bit / thanh ghi PLC",
                        Hint: "Cờ có bản của slot (1 = có bản). Một địa chỉ: các slot lấy liên tiếp; hoặc liệt kê từng slot, cách dấu phẩy."),
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
