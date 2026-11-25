using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Communication.Fixtures
{
    /// <summary>Cách quét khuôn.</summary>
    public enum ScanMethod
    {
        /// <summary>Nối TCP lần lượt tới mọi IP trong dải (song song), gửi lệnh quét, đọc trả lời. Khớp với khuôn đang là TCP server ở cổng 9999.</summary>
        TcpSweep,
        /// <summary>Gửi một gói UDP broadcast tới cổng quét; khuôn nào nghe được thì trả lời, IP lấy từ địa chỉ gửi.</summary>
        UdpBroadcast
    }

    /// <summary>
    /// Thông số quét. Pattern: mẫu một dòng trả lời, {STATE} = chữ trạng thái (P/F/R...), {SS} = số khuôn, {NN} = số slot.
    /// Range: "10.153.66.1-254", "10.153.66.1-10.153.66.200" hoặc "10.153.66.0/24"; rỗng = dải /24 của card mạng đang dùng.
    /// </summary>
    public sealed record FixtureScanOptions(
        string Command, int Port, ScanMethod Method, string Range, int TimeoutMs, string Pattern,
        byte[] TxTerminator, byte[] RxTerminator);

    /// <summary>Một khuôn tìm thấy: IP, số khuôn, trạng thái từng slot.</summary>
    public sealed record ScannedFixture(string Ip, int Port, int FixtureNo, IReadOnlyDictionary<int, SlotState> Slots, string Raw)
    {
        public int SlotCount => Slots.Count == 0 ? 1 : Slots.Keys.Max();

        /// <summary>Ví dụ "S1: PASS, S2: FAIL".</summary>
        public string Summary => Slots.Count == 0 ? "(chỉ trả số khuôn)"
            : string.Join(", ", Slots.OrderBy(k => k.Key).Select(k => $"S{k.Key}: {Name(k.Value)}"));

        public static string Name(SlotState s) => s switch
        {
            SlotState.Pass => "PASS",
            SlotState.Fail => "FAIL",
            SlotState.Repair => "REPAIR",
            SlotState.Busy => "BUSY",
            SlotState.Nan => "NAN",
            SlotState.Error => "ERROR",
            SlotState.Empty => "EMPTY",
            _ => "?"
        };
    }

    /// <summary>
    /// Quét tìm khuôn test: gửi lệnh quét (mặc định ASK) tới tất cả địa chỉ, khuôn trả về các dòng dạng P_01_1 / F_02_3 / R_01_2
    /// (trạng thái _ số khuôn _ slot). Từ đó lấy được IP, số khuôn, số slot và trạng thái từng slot.
    /// </summary>
    public static class FixtureScanner
    {
        private const int MaxAddresses = 1024;
        private const int Parallel = 64;

        public static async Task<List<ScannedFixture>> ScanAsync(
            FixtureScanOptions o, IProgress<string>? progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(o.Command)) throw new ArgumentException("Lệnh quét đang để trống");
            var regex = BuildRegex(o.Pattern);

            var replies = new Dictionary<string, List<string>>();       // IP → các dòng trả lời
            var lock_ = new object();

            if (o.Method == ScanMethod.UdpBroadcast)
                await UdpAsync(o, progress, ct, (ip, line) => { lock (lock_) { Get(replies, ip).Add(line); } });
            else
                await TcpAsync(o, progress, ct, (ip, line) => { lock (lock_) { Get(replies, ip).Add(line); } });

            var found = new List<ScannedFixture>();
            foreach (var (ip, lines) in replies.OrderBy(kv => IpKey(kv.Key)))
            {
                // Mỗi khuôn một số: nhóm theo số khuôn (một IP thường chỉ có một khuôn, nhưng không loại trừ)
                var bySs = new Dictionary<int, Dictionary<int, SlotState>>();
                var unparsed = new List<string>();
                foreach (string line in lines)
                {
                    var m = regex.Match(line);
                    if (!m.Success) { unparsed.Add(line); continue; }
                    int ss = int.Parse(m.Groups["SS"].Value);
                    if (!bySs.TryGetValue(ss, out var slots)) bySs[ss] = slots = new Dictionary<int, SlotState>();
                    if (m.Groups["NN"].Success)
                        slots[int.Parse(m.Groups["NN"].Value)] = StateOf(m.Groups["ST"].Success ? m.Groups["ST"].Value : "");
                }

                if (bySs.Count == 0)
                {
                    progress?.Report($"{ip}: có trả lời nhưng không khớp mẫu '{o.Pattern}': {string.Join(" | ", unparsed)}");
                    continue;
                }
                foreach (var (ss, slots) in bySs.OrderBy(kv => kv.Key))
                    found.Add(new ScannedFixture(ip, o.Port, ss, slots, string.Join(" | ", lines)));
            }
            return found;
        }

        private static List<string> Get(Dictionary<string, List<string>> d, string ip)
        {
            if (!d.TryGetValue(ip, out var l)) d[ip] = l = new List<string>();
            return l;
        }

        // ---------- TCP: quét dải IP ----------

        private static async Task TcpAsync(FixtureScanOptions o, IProgress<string>? progress, CancellationToken ct, Action<string, string> onLine)
        {
            var addresses = ExpandRange(o.Range);
            progress?.Report($"Quét {addresses.Count} địa chỉ (cổng {o.Port}), gửi '{o.Command}'...");

            int done = 0, hits = 0;
            using var gate = new SemaphoreSlim(Parallel);
            var tasks = addresses.Select(async ip =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var lines = await AskOneAsync(ip, o, ct).ConfigureAwait(false);
                    foreach (string l in lines) onLine(ip.ToString(), l);
                    if (lines.Count > 0) Interlocked.Increment(ref hits);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* IP không có khuôn: bình thường */ }
                finally
                {
                    gate.Release();
                    int n = Interlocked.Increment(ref done);
                    if (n % 64 == 0 || n == addresses.Count) progress?.Report($"Đã quét {n}/{addresses.Count} địa chỉ, {hits} nơi có trả lời");
                }
            }).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<List<string>> AskOneAsync(IPAddress ip, FixtureScanOptions o, CancellationToken ct)
        {
            var lines = new List<string>();
            int connectMs = Math.Clamp(o.TimeoutMs / 2, 150, 1500);

            using var client = new TcpClient();
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(connectMs);
                try { await client.ConnectAsync(ip, o.Port, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return lines; }   // quá hạn nối: không có khuôn
            }

            var stream = client.GetStream();
            byte[] send = Encoding.ASCII.GetBytes(o.Command).Concat(o.TxTerminator).ToArray();
            await stream.WriteAsync(send, ct).ConfigureAwait(false);
            Communication.Transport.RawTrace.Emit("Quét " + ip, "TX", send);

            // Đọc tới khi hết dữ liệu trong 250 ms (hoặc hết TimeoutMs)
            var buffer = new List<byte>();
            var chunk = new byte[1024];
            var deadline = DateTime.UtcNow.AddMilliseconds(o.TimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                int wait = lines.Count > 0 || buffer.Count > 0 ? 250 : (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(wait);
                int n;
                try { n = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
                if (n == 0) break;
                Communication.Transport.RawTrace.Emit("Quét " + ip, "RX", chunk.AsSpan(0, n));
                buffer.AddRange(chunk.AsSpan(0, n).ToArray());
                lines.AddRange(SplitLines(buffer, o.RxTerminator, flushRest: false));
            }
            lines.AddRange(SplitLines(buffer, o.RxTerminator, flushRest: true));      // dòng cuối không có ký tự kết thúc
            return lines;
        }

        // ---------- UDP broadcast ----------

        private static async Task UdpAsync(FixtureScanOptions o, IProgress<string>? progress, CancellationToken ct, Action<string, string> onLine)
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            byte[] send = Encoding.ASCII.GetBytes(o.Command).Concat(o.TxTerminator).ToArray();

            var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
            foreach (var b in DirectedBroadcasts()) targets.Add(b);
            foreach (var t in targets)
            {
                try { await udp.SendAsync(send, send.Length, new IPEndPoint(t, o.Port)).ConfigureAwait(false); }
                catch (SocketException) { }
            }
            Communication.Transport.RawTrace.Emit("Quét UDP broadcast", "TX", send);
            progress?.Report($"Đã gửi broadcast '{o.Command}' tới cổng {o.Port} ({targets.Count} địa chỉ), chờ trả lời {o.TimeoutMs} ms...");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(o.TimeoutMs);
            try
            {
                while (true)
                {
                    var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    Communication.Transport.RawTrace.Emit("Quét UDP " + r.RemoteEndPoint, "RX", r.Buffer);
                    var buf = r.Buffer.ToList();
                    foreach (string l in SplitLines(buf, o.RxTerminator, flushRest: true))
                        onLine(r.RemoteEndPoint.Address.ToString(), l);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }       // hết thời gian chờ
        }

        // ---------- Tiện ích ----------

        /// <summary>Trạng thái theo chữ cái đầu: P=PASS, F=FAIL, R=REPAIR, B=BUSY, I=NAN, E=ERROR.</summary>
        public static SlotState StateOf(string s) => (s ?? "").Trim().ToUpperInvariant() switch
        {
            "P" => SlotState.Pass,
            "F" => SlotState.Fail,
            "R" => SlotState.Repair,
            "B" => SlotState.Busy,
            "I" => SlotState.Nan,
            "E" => SlotState.Error,
            _ => SlotState.Unknown
        };

        private static readonly Regex Token = new(@"\{(STATE|SS|NN)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Dựng regex từ mẫu: {STATE} = một chữ cái, {SS}/{NN} = số; phần còn lại khớp đúng từng ký tự, không phân biệt hoa thường.</summary>
        public static Regex BuildRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "{STATE}_{SS}_{NN}";
            var sb = new StringBuilder("^\\s*");
            int last = 0;
            bool hasSs = false;
            foreach (Match m in Token.Matches(pattern))
            {
                sb.Append(Regex.Escape(pattern.Substring(last, m.Index - last)));
                switch (m.Groups[1].Value.ToUpperInvariant())
                {
                    case "STATE": sb.Append("(?<ST>[A-Za-z])"); break;
                    case "SS": sb.Append(@"(?<SS>\d+)"); hasSs = true; break;
                    default: sb.Append(@"(?<NN>\d+)"); break;
                }
                last = m.Index + m.Length;
            }
            if (!hasSs) throw new ArgumentException("Mẫu trả lời phải có {SS} (số khuôn)");
            sb.Append(Regex.Escape(pattern.Substring(last))).Append("\\s*$");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static List<string> SplitLines(List<byte> buffer, byte[] term, bool flushRest)
        {
            var lines = new List<string>();
            if (term.Length > 0)
            {
                while (true)
                {
                    int idx = IndexOf(buffer, term);
                    if (idx < 0) break;
                    string s = Encoding.ASCII.GetString(buffer.GetRange(0, idx).ToArray()).Trim();
                    buffer.RemoveRange(0, idx + term.Length);
                    if (s.Length > 0) lines.Add(s);
                }
            }
            if (flushRest && buffer.Count > 0)
            {
                // Không có ký tự kết thúc: tách theo xuống dòng bất kỳ
                foreach (string s in Encoding.ASCII.GetString(buffer.ToArray()).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    if (s.Trim().Length > 0) lines.Add(s.Trim());
                buffer.Clear();
            }
            return lines;
        }

        private static int IndexOf(List<byte> buffer, byte[] term)
        {
            for (int i = 0; i + term.Length <= buffer.Count; i++)
            {
                bool ok = true;
                for (int j = 0; j < term.Length && ok; j++) ok = buffer[i + j] == term[j];
                if (ok) return i;
            }
            return -1;
        }

        /// <summary>Các địa chỉ của dải: "a.b.c.d-e", "a.b.c.d-a.b.c.e", "a.b.c.0/24" hoặc rỗng (= dải /24 của card mạng đang chạy).</summary>
        public static List<IPAddress> ExpandRange(string range)
        {
            range = (range ?? "").Trim();
            uint from, to;

            if (range.Length == 0)
            {
                var local = LocalAddresses().FirstOrDefault()
                            ?? throw new InvalidOperationException("Không tìm thấy card mạng IPv4 nào đang chạy để suy ra dải quét. Nhập dải IP vào ô 'Dải IP quét'.");
                uint a = ToUInt(local);
                from = (a & 0xFFFFFF00u) + 1;
                to = (a & 0xFFFFFF00u) + 254;
            }
            else if (range.Contains('/'))
            {
                string[] p = range.Split('/');
                if (p.Length != 2 || !IPAddress.TryParse(p[0], out var baseIp) || !int.TryParse(p[1], out int bits) || bits is < 16 or > 30)
                    throw new FormatException("Dải CIDR không hợp lệ (ví dụ 10.153.66.0/24, độ dài mạng 16 đến 30)");
                uint mask = uint.MaxValue << (32 - bits);
                from = (ToUInt(baseIp) & mask) + 1;
                to = (ToUInt(baseIp) | ~mask) - 1;
            }
            else if (range.Contains('-'))
            {
                string[] p = range.Split('-', 2);
                if (!p[0].Trim().Contains('.') || !IPAddress.TryParse(p[0].Trim(), out var start))
                    throw new FormatException("IP đầu của dải không hợp lệ");
                from = ToUInt(start);
                string tail = p[1].Trim();
                if (tail.Contains('.') && IPAddress.TryParse(tail, out var end)) to = ToUInt(end);      // IP đầy đủ ("250" một mình cũng bị .NET hiểu là 0.0.0.250 nên phải có dấu chấm)
                else if (int.TryParse(tail, out int last) && last is >= 0 and <= 255) to = (from & 0xFFFFFF00u) + (uint)last;
                else throw new FormatException("IP cuối của dải không hợp lệ (ví dụ 10.153.66.1-254)");
            }
            else
            {
                if (!IPAddress.TryParse(range, out var one)) throw new FormatException("Dải IP không hợp lệ");
                from = to = ToUInt(one);
            }

            if (to < from) throw new FormatException("IP cuối nhỏ hơn IP đầu");
            if (to - from + 1 > MaxAddresses)
                throw new FormatException($"Dải quá lớn ({to - from + 1} địa chỉ), tối đa {MaxAddresses}");
            var list = new List<IPAddress>();
            for (uint u = from; u <= to; u++) list.Add(FromUInt(u));
            return list;
        }

        private static IEnumerable<IPAddress> LocalAddresses()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !ua.Address.ToString().StartsWith("169.254"))
                        yield return ua.Address;
            }
        }

        private static IEnumerable<IPAddress> DirectedBroadcasts()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    uint a = ToUInt(ua.Address), m = ToUInt(ua.IPv4Mask);
                    yield return FromUInt(a | ~m);
                }
            }
        }

        private static uint ToUInt(IPAddress ip) { var b = ip.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
        private static IPAddress FromUInt(uint u) => new(new[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u });
        private static uint IpKey(string ip) => IPAddress.TryParse(ip, out var a) ? ToUInt(a) : 0;
    }
}
