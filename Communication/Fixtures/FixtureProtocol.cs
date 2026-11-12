using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Communication.Fixtures
{
    /// <summary>Trạng thái một slot trong khuôn test.</summary>
    public enum SlotState { Unknown, Busy, Empty, Pass, Repair, Fail, Nan, Error }

    /// <summary>Lệnh điều khiển khuôn test.</summary>
    public enum FixtureCommand { Open, Close, OpenAll, CloseAll, CheckSensor, State }

    /// <summary>
    /// Kết quả đối chiếu một dòng trả lời với hai mẫu của lệnh.
    /// Matched = false: dòng không khớp mẫu nào. Positive = true khớp mẫu 1 (OK / YES / OPENED), false khớp mẫu 2 (ERROR / NO / CLOSED).
    /// Slots: phần chữ thay cho {SLOTS} (danh sách slot lỗi) nếu mẫu có.
    /// </summary>
    public readonly record struct FixtureReply(bool Matched, bool Positive, string Slots);

    /// <summary>
    /// Trả lời CHECKSENSOR. Matched: khớp mẫu YES / NO. Slots: có bản hay không của từng slot (null nếu trả lời sai định dạng, xem Warning).
    /// </summary>
    public readonly record struct SensorReply(bool Matched, bool Positive, bool[]? Slots, string? Warning);

    /// <summary>
    /// Dựng lệnh gửi khuôn test và đọc trả lời / kết quả slot theo các mẫu trong cấu hình (bảng lệnh và bảng kết quả).
    /// {SS} = số khuôn, {NN} = số slot, {SLOTS} = danh sách slot lỗi. Khi so khớp, số được so theo giá trị (01 = 1) và không phân biệt hoa thường.
    /// </summary>
    public sealed class FixtureProtocol
    {
        private static readonly Regex Placeholder = new(@"\{(SS|NN|SLOTS)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Func<string, string> _get;

        /// <param name="getGroupValue">Đọc cài đặt chung của khuôn test theo khóa (catalog.GetGroupValue).</param>
        public FixtureProtocol(Func<string, string> getGroupValue) => _get = getGroupValue;

        public static string KeyOf(FixtureCommand c) => c.ToString();

        /// <summary>Chữ lệnh sẽ gửi, ví dụ OPEN_01. Số khuôn viết hai chữ số.</summary>
        public string BuildCommand(FixtureCommand command, int ss)
            => Render(_get($"Cmd.{KeyOf(command)}.Send"), ss, null, null);

        public static string Render(string template, int? ss, int? nn, string? slots)
        {
            return Placeholder.Replace(template, m => m.Groups[1].Value.ToUpperInvariant() switch
            {
                "SS" => (ss ?? 0).ToString("00", CultureInfo.InvariantCulture),
                "NN" => (nn ?? 0).ToString("00", CultureInfo.InvariantCulture),
                _ => slots ?? ""
            });
        }

        /// <summary>Đối chiếu trả lời của một lệnh với mẫu 1 và mẫu 2 (chỉ chấp nhận đúng số khuôn ss).</summary>
        public FixtureReply Interpret(FixtureCommand command, string reply, int ss)
        {
            string line = (reply ?? "").Trim();
            if (TryMatch(_get($"Cmd.{KeyOf(command)}.Ok"), line, out int? s1, out _, out string slots1) && (s1 == null || s1 == ss))
                return new FixtureReply(true, true, slots1);
            if (TryMatch(_get($"Cmd.{KeyOf(command)}.Err"), line, out int? s2, out _, out string slots2) && (s2 == null || s2 == ss))
                return new FixtureReply(true, false, slots2);
            return new FixtureReply(false, false, "");
        }

        /// <summary>
        /// Đọc trả lời CHECKSENSOR. Sau mẫu YES_{SS} / NO_{SS} có thể gắn trạng thái từng slot, mỗi slot 0 hoặc 1,
        /// ví dụ YES_1_1 (khuôn 1, slot 1 có bản), YES_1_1_0_1_1 hoặc YES_1_1011 (khuôn 1, 4 slot). Khuôn chỉ có một slot thì bỏ phần sau được (YES_1).
        /// Không có phần sau: YES = mọi slot có bản, NO = mọi slot trống.
        /// </summary>
        public SensorReply InterpretSensor(string reply, int ss, int slots)
        {
            string line = (reply ?? "").Trim();
            foreach (bool positive in new[] { true, false })
            {
                string template = _get(positive ? "Cmd.CheckSensor.Ok" : "Cmd.CheckSensor.Err");
                if (!TryMatch(template + "{SLOTS}", line, out int? s, out _, out string rest)) continue;
                if (s != null && s != ss) continue;

                var bits = new List<bool>();
                foreach (string token in Regex.Split(rest, @"[^0-9]+").Where(t => t.Length > 0))
                {
                    foreach (char c in token)
                    {
                        if (c is not ('0' or '1'))
                            return new SensorReply(true, positive, null, $"Trạng thái slot chỉ được là 0 hoặc 1 (nhận '{rest.Trim('_', ',', ' ')}')");
                        bits.Add(c == '1');
                    }
                }

                if (bits.Count == 0) return new SensorReply(true, positive, Enumerable.Repeat(positive, slots).ToArray(), null);
                if (bits.Count != slots)
                    return new SensorReply(true, positive, null, $"Trả lời có {bits.Count} trạng thái slot nhưng khuôn có {slots} slot");
                return new SensorReply(true, positive, bits.ToArray(), null);
            }
            return new SensorReply(false, false, null, null);
        }

        private static readonly (SlotState State, string Key)[] ResultKeys =
        {
            (SlotState.Busy, "Busy"), (SlotState.Pass, "Pass"), (SlotState.Repair, "Repair"),
            (SlotState.Fail, "Fail"), (SlotState.Nan, "Nan"), (SlotState.Error, "Error"),
        };

        /// <summary>Đọc một dòng kết quả slot (B_01_02, P_01_02...). Dòng không khớp mẫu nào thì trả về false.</summary>
        public bool TryParseSlotResult(string line, out int ss, out int nn, out SlotState state)
        {
            ss = nn = 0; state = SlotState.Unknown;
            line = (line ?? "").Trim();
            foreach (var (st, key) in ResultKeys)
            {
                string pattern = _get($"Res.{key}.Pattern");
                if (pattern.Length == 0) continue;
                if (TryMatch(pattern, line, out int? s, out int? n, out _) && s != null && n != null)
                {
                    ss = s.Value; nn = n.Value; state = st;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Mã gửi PLC của một trạng thái (theo bảng kết quả); null nếu trạng thái đó không gửi PLC.</summary>
        public int? PlcCode(SlotState state)
        {
            string? key = state switch
            {
                SlotState.Busy => "Busy",
                SlotState.Empty => "Empty",
                SlotState.Pass => "Pass",
                SlotState.Repair => "Repair",
                SlotState.Fail => "Fail",
                SlotState.Nan => "Nan",
                _ => null
            };
            if (key == null) return null;
            return int.TryParse(_get($"Res.{key}.Plc"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
        }

        /// <summary>So một dòng với mẫu: {SS}, {NN} là số, {SLOTS} là chữ bất kỳ; phần còn lại phải đúng từng ký tự.</summary>
        public static bool TryMatch(string template, string line, out int? ss, out int? nn, out string slots)
        {
            ss = nn = null; slots = "";
            if (string.IsNullOrWhiteSpace(template)) return false;

            var sb = new StringBuilder("^");
            int last = 0;
            foreach (Match m in Placeholder.Matches(template))
            {
                sb.Append(Regex.Escape(template.Substring(last, m.Index - last)));
                sb.Append(m.Groups[1].Value.ToUpperInvariant() switch
                {
                    "SS" => @"(?<SS>\d+)",
                    "NN" => @"(?<NN>\d+)",
                    _ => @"(?<SLOTS>.*)"
                });
                last = m.Index + m.Length;
            }
            sb.Append(Regex.Escape(template.Substring(last))).Append('$');

            Match r;
            try { r = Regex.Match(line, sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { return false; }
            if (!r.Success) return false;

            if (r.Groups["SS"].Success) ss = int.Parse(r.Groups["SS"].Value, CultureInfo.InvariantCulture);
            if (r.Groups["NN"].Success) nn = int.Parse(r.Groups["NN"].Value, CultureInfo.InvariantCulture);
            if (r.Groups["SLOTS"].Success) slots = r.Groups["SLOTS"].Value.Trim();
            return true;
        }
    }
}
