using Communication.Fixtures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UI.ViewModels.Device;

namespace UI.Services
{
    /// <summary>Bước của một mã đang gắn với một khuôn.</summary>
    public enum JobStep
    {
        /// <summary>Đã gắn mã và khuôn đã đóng, đang gửi START (nếu app tắt giữa chừng thì không biết START đã đi chưa).</summary>
        Closed,
        /// <summary>Đã gửi START, đang chờ khuôn test xong.</summary>
        Started,
        /// <summary>Khuôn đã báo kết quả cuối (PASS / FAIL...), chờ robot gắp ra.</summary>
        Done,
        /// <summary>Không chắc START đã gửi hay khuôn không trả lời khi hỏi STATE: cần người kiểm tra.</summary>
        NeedsCheck
    }

    public sealed class QueuedCode
    {
        public string Code { get; set; } = "";
        public DateTime At { get; set; } = DateTime.Now;

        /// <summary>false: mã đọc từ file sau lần tắt app, chờ người dùng xác nhận mới được gắn vào khuôn.</summary>
        public bool Confirmed { get; set; } = true;
    }

    public sealed class FixtureJob
    {
        public int FixtureNo { get; set; }
        public string Code { get; set; } = "";
        public JobStep Step { get; set; }
        public SlotState Result { get; set; } = SlotState.Unknown;
        public DateTime At { get; set; } = DateTime.Now;
        public string Note { get; set; } = "";

        [JsonIgnore] public bool AwaitingState { get; set; }
    }

    internal sealed class JobFile
    {
        public List<QueuedCode> Queue { get; set; } = new();
        public List<FixtureJob> Jobs { get; set; } = new();
    }

    /// <summary>
    /// Mã QR do vision quét → khuôn test.
    ///  - Vision quét xong: mã vào HÀNG ĐỢI (theo thứ tự quét).
    ///  - Robot đặt hàng vào khuôn N (số khuôn đọc từ D642) và khuôn đóng xong: lấy mã ĐẦU hàng đợi gắn vào khuôn N,
    ///    gửi một lần "START:&lt;mã&gt;" tới khuôn N (không chờ trả lời, không gửi lại).
    ///  - Khuôn báo kết quả: BUSY = đang test; PASS / FAIL... = xong; REPAIR = coi như chưa test, mã trả về ĐẦU hàng đợi.
    ///  - Chưa có kết quả cuối thì không mở khuôn (khóa an toàn lúc chạy tự động).
    /// Mọi thay đổi ghi xuống file ngay (ghi tạm rồi đổi tên) để mất điện / app sập vẫn đọc lại được.
    /// Khi bật: hỏi STATE từng khuôn, kết quả quyết định bước tiếp; khuôn không trả lời thì giữ nguyên, đánh dấu cần kiểm tra.
    /// Hàng đợi đọc từ file chưa dùng được cho đến khi người dùng xác nhận.
    /// </summary>
    public sealed class TestJobCoordinator
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly DeviceConfigViewModel _root;
        private readonly object _lock = new();
        private readonly string _path;
        private List<QueuedCode> _queue = new();
        private List<FixtureJob> _jobs = new();

        /// <summary>Có thay đổi (hàng đợi hoặc trạng thái): cửa sổ hiển thị tự làm mới. Có thể bắn từ luồng nền.</summary>
        public event Action? Changed;

        public TestJobCoordinator(DeviceConfigViewModel root, string? path = null)
        {
            _root = root;
            _path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionGraph", "test_jobs.json");
            Load();
        }

        public string FilePath => _path;

        // ---------- Đọc ra ----------

        public IReadOnlyList<QueuedCode> QueueSnapshot()
        {
            lock (_lock) return _queue.Select(q => new QueuedCode { Code = q.Code, At = q.At, Confirmed = q.Confirmed }).ToList();
        }

        public IReadOnlyList<FixtureJob> JobsSnapshot()
        {
            lock (_lock)
                return _jobs.OrderBy(j => j.FixtureNo).Select(j => new FixtureJob
                {
                    FixtureNo = j.FixtureNo,
                    Code = j.Code,
                    Step = j.Step,
                    Result = j.Result,
                    At = j.At,
                    Note = j.Note
                }).ToList();
        }

        /// <summary>Có mã đọc từ file đang chờ xác nhận không.</summary>
        public bool HasUnconfirmed { get { lock (_lock) return _queue.Any(q => !q.Confirmed); } }

        // ---------- Mã từ vision ----------

        /// <summary>Vision vừa quét được một mã: xếp vào cuối hàng đợi. Mã đã có trong hàng đợi hoặc đang gắn khuôn thì bỏ qua.</summary>
        public void Enqueue(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0) return;

            bool added;
            lock (_lock)
            {
                added = !_queue.Any(q => q.Code == code) && !_jobs.Any(j => j.Code == code);
                if (added) { _queue.Add(new QueuedCode { Code = code }); Save(); }
            }
            Log(added ? $"Vision quét mã {code}: vào hàng đợi" : $"Mã {code} đã có trong hàng đợi / đang test, bỏ qua");
            if (added) Changed?.Invoke();
        }

        public void ConfirmAll()
        {
            lock (_lock) { foreach (var q in _queue) q.Confirmed = true; Save(); }
            Changed?.Invoke();
        }

        public void RemoveQueued(string code)
        {
            lock (_lock) { _queue.RemoveAll(q => q.Code == code); Save(); }
            Changed?.Invoke();
        }

        /// <summary>Xóa bản ghi của một khuôn (người dùng kiểm tra xong, hoặc khuôn đã được dọn tay).</summary>
        public void ClearJob(int fixtureNo)
        {
            lock (_lock) { _jobs.RemoveAll(j => j.FixtureNo == fixtureNo); Save(); }
            Changed?.Invoke();
        }

        /// <summary>Trả mã của khuôn về ĐẦU hàng đợi (người dùng báo hàng chưa test).</summary>
        public void ReturnToQueue(int fixtureNo)
        {
            lock (_lock)
            {
                var job = _jobs.FirstOrDefault(j => j.FixtureNo == fixtureNo);
                if (job == null) return;
                _queue.Insert(0, new QueuedCode { Code = job.Code });
                _jobs.Remove(job);
                Save();
            }
            Changed?.Invoke();
        }

        // ---------- Khuôn đóng: gắn mã, gửi START ----------

        /// <summary>
        /// Robot đã đặt hàng và khuôn đã đóng xong: gắn mã đầu hàng đợi vào khuôn rồi gửi START một lần.
        /// Trả về lý do nếu không gắn / không gửi được (chỉ để ghi nhật ký, không dừng chạy tự động); null nếu đã gửi.
        /// </summary>
        public async Task<string?> OnClosedAsync(int fixtureNo)
        {
            FixtureJob job;
            lock (_lock)
            {
                var old = _jobs.FirstOrDefault(j => j.FixtureNo == fixtureNo);
                if (old != null && old.Step != JobStep.Done)
                    return $"Khuôn {fixtureNo:00} còn mã {old.Code} chưa xong ({old.Step}), không gắn mã mới";
                if (old != null) _jobs.Remove(old);

                var next = _queue.FirstOrDefault(q => q.Confirmed);
                if (next == null)
                    return _queue.Count > 0
                        ? $"Khuôn {fixtureNo:00} đã đóng nhưng các mã trong hàng đợi chưa được xác nhận (mở cửa sổ 'Mã test')"
                        : $"Khuôn {fixtureNo:00} đã đóng nhưng không có mã quét nào đang chờ";
                _queue.Remove(next);

                job = new FixtureJob { FixtureNo = fixtureNo, Code = next.Code, Step = JobStep.Closed, At = DateTime.Now };
                _jobs.Add(job);
                Save();                                          // ghi trước khi gửi: crash giữa chừng thì còn dấu vết
            }
            Changed?.Invoke();
            Log($"Khuôn {fixtureNo:00} đóng: gắn mã {job.Code}, gửi START");

            string? error = null;
            try
            {
                var fixture = await OnUi(() => _root.Devices.FirstOrDefault(d => d.Config.Type == Communication.Configuration.DeviceType.Fixture
                                                                                   && d.Enabled && d.FixtureNo == fixtureNo));
                if (fixture == null) error = $"không có khuôn số {fixtureNo:00} đang bật";
                else error = await OnUiResult(() => fixture.SendStartAsync(job.Code));       // null = START_OK
            }
            catch (Exception ex) { error = ex.Message; }

            lock (_lock)
            {
                if (error == null) { job.Step = JobStep.Started; job.Note = ""; }
                else { job.Step = JobStep.NeedsCheck; job.Note = "Gửi START lỗi: " + error; }
                job.At = DateTime.Now;
                Save();
            }
            Changed?.Invoke();
            if (error != null) Log($"[LỖI] START khuôn {fixtureNo:00} mã {job.Code}: {error}", error: true);
            return error == null ? null : $"START khuôn {fixtureNo:00}: {error}";
        }

        // ---------- Kết quả từ khuôn ----------

        /// <summary>Khuôn báo trạng thái slot (B / P / R / F / I / E). Gọi từ DeviceViewModel.ApplyLine.</summary>
        public void OnSlotResult(int fixtureNo, SlotState state)
        {
            string? message = null;
            lock (_lock)
            {
                var job = _jobs.FirstOrDefault(j => j.FixtureNo == fixtureNo);
                if (job == null) return;
                job.AwaitingState = false;

                switch (state)
                {
                    case SlotState.Busy:
                        if (job.Step != JobStep.Done) { job.Step = JobStep.Started; job.Note = ""; }
                        break;

                    case SlotState.Repair:
                        // Coi như chưa test: mở khuôn cho robot gắp sang khuôn khác, mã trả về đầu hàng đợi
                        _queue.Insert(0, new QueuedCode { Code = job.Code });
                        _jobs.Remove(job);
                        message = $"Khuôn {fixtureNo:00} báo REPAIR: mã {job.Code} trả về đầu hàng đợi";
                        break;

                    case SlotState.Pass:
                    case SlotState.Fail:
                    case SlotState.Nan:
                    case SlotState.Error:
                        job.Step = JobStep.Done;
                        job.Result = state;
                        job.Note = "";
                        job.At = DateTime.Now;
                        message = $"Khuôn {fixtureNo:00} mã {job.Code}: {state.ToString().ToUpperInvariant()}";
                        break;

                    default:
                        return;
                }
                Save();
            }
            if (message != null) Log(message);
            Changed?.Invoke();
        }

        /// <summary>Khuôn đã mở (robot gắp hàng ra): bản ghi đã có kết quả thì xóa; chưa xong thì giữ lại và cảnh báo.</summary>
        public void OnOpened(int fixtureNo)
        {
            bool stillActive = false;
            lock (_lock)
            {
                var job = _jobs.FirstOrDefault(j => j.FixtureNo == fixtureNo);
                if (job == null) return;
                if (job.Step == JobStep.Done) { _jobs.Remove(job); Save(); }
                else stillActive = true;
            }
            if (stillActive) Log($"[LỖI] Khuôn {fixtureNo:00} đã mở khi mã chưa có kết quả, giữ bản ghi để kiểm tra", error: true);
            Changed?.Invoke();
        }

        /// <summary>Lý do chưa được mở khuôn (chưa có kết quả cuối); null nếu mở được.</summary>
        public string? OpenBlockReason(int fixtureNo)
        {
            lock (_lock)
            {
                var job = _jobs.FirstOrDefault(j => j.FixtureNo == fixtureNo);
                return job?.Step switch
                {
                    JobStep.Closed or JobStep.Started => $"mã {job.Code} chưa có kết quả test, chưa được mở",
                    JobStep.NeedsCheck => $"mã {job.Code} cần kiểm tra ({job.Note}), chưa được mở; xử lý trong cửa sổ 'Mã test'",
                    _ => null
                };
            }
        }

        // ---------- Khôi phục khi bật ----------

        /// <summary>
        /// Hỏi STATE một khuôn. Trả lời B/P/F/R cập nhật bản ghi (qua OnSlotResult). Không trả lời / không nối được thì
        /// giữ nguyên, ghi chú cần kiểm tra, KHÔNG gửi lại START. Gọi trên luồng giao diện.
        /// </summary>
        public async Task ReconcileAsync(DeviceViewModel fixture)
        {
            int no = fixture.FixtureNo;
            lock (_lock)
            {
                var job = _jobs.FirstOrDefault(j => j.FixtureNo == no);
                if (job != null) job.AwaitingState = true;
            }

            await fixture.RunFixtureCommandAsync(FixtureCommand.State);

            string code;
            lock (_lock)
            {
                var job = _jobs.FirstOrDefault(j => j.FixtureNo == no);
                if (job == null || !job.AwaitingState) return;
                job.AwaitingState = false;
                code = job.Code;
                if (job.Step is JobStep.Closed or JobStep.Started)
                {
                    job.Step = JobStep.NeedsCheck;
                    job.Note = "Khuôn không trả lời STATE khi bật lại";
                }
                Save();
            }
            Log($"Khuôn {no:00} không trả lời STATE: giữ mã {code}, chờ kiểm tra", error: true);
            Changed?.Invoke();
        }

        // ---------- File ----------

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var file = JsonSerializer.Deserialize<JobFile>(File.ReadAllText(_path), Options);
                if (file == null) return;
                _queue = file.Queue;
                foreach (var q in _queue) q.Confirmed = false;           // sau lần tắt app: chờ người dùng xác nhận
                _jobs = file.Jobs;

                // Bước Closed còn lại từ lần trước: không biết START đã đi chưa
                foreach (var j in _jobs.Where(j => j.Step == JobStep.Closed))
                {
                    j.Step = JobStep.NeedsCheck;
                    j.Note = "App tắt khi đang gửi START, không rõ START đã đi chưa";
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                try { File.Move(_path, _path + ".loi_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")); } catch { }
                _queue = new(); _jobs = new();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new JobFile { Queue = _queue, Jobs = _jobs }, Options));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                _root.AddFixtureLog("Mã test", "LỖI", "Không ghi được " + _path + ": " + ex.Message);
            }
        }

        private void Log(string text, bool error = false)
            => _root.AddFixtureLog("Mã test", error ? "LỖI" : "TEST", text);

        private static Task<T> OnUi<T>(Func<T> f)
            => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task;

        private static Task<T> OnUiResult<T>(Func<Task<T>> f)
            => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task.Unwrap();

        private static Task OnUiTask(Func<Task> f)
            => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task.Unwrap();
    }
}
