using Communication.Drivers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HalconDotNet;
using Nodify;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using UI.Models;
using UI.Services;
using UI.ViewModels.Graph;
using UI.ViewModels.RunAndDisplay;
using Vision.Core;
using Vision.Execution;
using Vision.Graph;
using Vision.Runtime;
using Vision.Services;

namespace UI.ViewModels.RunAndDisplay
{
    /// <summary>
    /// Chạy thử đồ thị ngay trên giao diện và hiển thị kết quả:
    ///  - màu/trạng thái từng node, bảng "Kết quả", log "Output";
    ///  - "Ảnh gốc" (ảnh của node nguồn) và "Ảnh xử lý" (kết quả của node đang chọn).
    ///
    /// SỞ HỮU: giữ GraphRunResult của lần chạy gần nhất cho đến khi chạy lần sau (hoặc đóng) để còn xem ảnh/vùng
    /// của bất kỳ node nào. Trước khi giải phóng, các khung xem được đặt về Empty.
    /// Khi đang chạy, đồ thị bị khóa (không thêm/xóa node, không nối dây).
    /// </summary>
    public partial class RunViewModel : ViewModelBase, IDisposable
    {
        private const int MaxLogChars = 60_000;

        private readonly GraphEditorViewModel _editor;
        private readonly IDialogService _dialogs;
        private readonly OffsetDispatcher _dispatcher;
        private readonly DeviceManager _devices;
        private readonly SynchronizationContext _ui;
        private readonly StringBuilder _log = new();

        private CancellationTokenSource? _cts;
        private GraphRunResult? _lastRun;

        public ObservableCollection<NodeResultRowViewModel> Results { get; } = new();
        public bool HasResults => Results.Count > 0;

        [ObservableProperty] private ImageDisplay _original = ImageDisplay.Empty;
        [ObservableProperty] private ImageDisplay _processed = ImageDisplay.Empty;
        [ObservableProperty] private string _statusText = "Sẵn sàng";
        [ObservableProperty] private string _outputText = "";

        /// <summary>Đang chạy tự động theo tín hiệu PLC.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AutoButtonText))]
        [NotifyCanExecuteChangedFor(nameof(RunOnceCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunFromCameraCommand))]
        [NotifyCanExecuteChangedFor(nameof(TeachFromCameraCommand))]
        [NotifyCanExecuteChangedFor(nameof(TeachCommand))]
        private bool _isAuto;

        public string AutoButtonText => IsAuto ? "\u25A0 Dừng tự động" : "\u25B6 Tự động (PLC)";

        private PlcAutoRunner? _auto;

        /// <summary>Lưu ảnh và kết quả của mỗi lần "Chạy từ camera" vào ổ đĩa.</summary>
        [ObservableProperty] private bool _saveResults = true;
        [ObservableProperty] private string _saveFolder = @"D:\VisionData";

        private long _cycleId;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunOnceCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunFromCameraCommand))]
        [NotifyCanExecuteChangedFor(nameof(TeachFromCameraCommand))]
        [NotifyCanExecuteChangedFor(nameof(TeachCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        private bool _isRunning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDryRun))]
        [NotifyPropertyChangedFor(nameof(IsLive))]
        [NotifyPropertyChangedFor(nameof(ModeText))]
        private RunMode _mode = RunMode.DryRun;

        public string ModeText => Mode == RunMode.Live ? "Chế độ: CHẠY THẬT" : "Chế độ: Chạy thử";

        // Hai thuộc tính này gắn hai chiều với nút bật/tắt ở thanh công cụ
        public bool IsDryRun
        {
            get => Mode == RunMode.DryRun;
            set
            {
                if (value) Mode = RunMode.DryRun;
                else OnPropertyChanged(nameof(IsDryRun));       // bấm lại nút đang bật: giữ nguyên
            }
        }

        public bool IsLive
        {
            get => Mode == RunMode.Live;
            set
            {
                if (value)
                {
                    // Chạy thật có thể điều khiển robot/ghi file: luôn hỏi lại
                    if (_dialogs.Confirm("Chạy THẬT sẽ tác động ra thiết bị bên ngoài (robot, ghi file...).\nBạn chắc chắn muốn bật?"))
                        Mode = RunMode.Live;
                    else
                    {
                        OnPropertyChanged(nameof(IsLive));
                        OnPropertyChanged(nameof(IsDryRun));
                    }
                }
                else Mode = RunMode.DryRun;
            }
        }

        public RunViewModel(GraphEditorViewModel editor, IDialogService dialogs, OffsetDispatcher dispatcher, DeviceManager devices)
        {
            _devices = devices;
            _editor = editor;
            _dialogs = dialogs;
            _dispatcher = dispatcher;
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();

            Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

            // Chọn node khác thì khung "Ảnh xử lý" đổi theo
            _editor.SelectedNodeChanged += (_, _) => UpdateViewers();
        }

        /// <summary>Bỏ ảnh và kết quả của lần chạy trước (khi đổi sang graph khác).</summary>
        public void Reset()
        {
            ClearDisplay();
            Results.Clear();
            StatusText = "Sẵn sàng";
        }

        // ================= LỆNH =================

        private bool CanRun() => !IsRunning && !IsAuto;
        private bool CanStop() => IsRunning;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private Task RunOnceAsync()
            => UsesCamera() ? RunCoreAsync(Mode, GrabFromCamerasAsync) : RunCoreAsync(Mode);

        /// <summary>Đồ thị có node "Camera Image" thì mọi lệnh chạy đều tự chụp ảnh từ camera trước (không cần nhớ bấm nút riêng).</summary>
        private bool UsesCamera() => _editor.Model.Nodes.Any(n => n.ToolName == "CameraImage");

        /// <summary>
        /// Học mẫu: chạy một lần ở chế độ Teach. Tool học (vd FindShape) tạo và ghi file mẫu; tool Action không tác động ra ngoài.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private Task TeachAsync()
        {
            if (!_dialogs.Confirm("Học mẫu sẽ GHI ĐÈ file mẫu hiện có của các tool học (vd FindShape).\nĐặt vật đúng vị trí mẫu trước khi tiếp tục. Tiếp tục?"))
                return Task.CompletedTask;
            return UsesCamera() ? RunCoreAsync(RunMode.Teach, GrabFromCamerasAsync) : RunCoreAsync(RunMode.Teach);
        }

        /// <summary>Chụp ảnh từ các camera đang kết nối rồi chạy đồ thị (node "Camera Image" nhận ảnh); xong thì lưu kết quả.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private Task RunFromCameraAsync() => RunCoreAsync(Mode, GrabFromCamerasAsync);

        /// <summary>Học mẫu bằng ảnh vừa chụp từ camera (không lưu kết quả ra đĩa).</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private Task TeachFromCameraAsync()
        {
            if (!_dialogs.Confirm("Học mẫu sẽ GHI ĐÈ file mẫu hiện có của các tool học (vd FindShape).\nĐặt vật đúng vị trí mẫu trước khi tiếp tục. Tiếp tục?"))
                return Task.CompletedTask;
            return RunCoreAsync(RunMode.Teach, GrabFromCamerasAsync);
        }

        /// <summary>Chụp một khung mới từ MỖI camera đang kết nối. Khóa là tên thiết bị; người gọi sở hữu các ảnh.</summary>
        private async Task<Dictionary<string, HImage>> GrabFromCamerasAsync(CancellationToken ct)
        {
            var cameras = _devices.ConnectedCameras();
            if (cameras.Count == 0)
                throw new InvalidOperationException(
                    "Chưa có camera nào được kết nối. Mở Cấu hình kết nối, tab Camera, tick Bật rồi bấm Kết nối.");

            var images = new Dictionary<string, HImage>();
            // Chụp song song tất cả camera (mỗi camera tối đa 3 giây)
            var grabs = cameras.Select(c => (Name: c.Config.Name, Task: c.Camera.GrabNextAsync(3000, ct))).ToList();
            try
            {
                foreach (var (name, task) in grabs)
                {
                    try { images[name] = await task; }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        throw new InvalidOperationException($"Không lấy được ảnh từ {name}: {ex.Message}", ex);
                    }
                }
                return images;
            }
            catch
            {
                foreach (var img in images.Values) img.Dispose();
                // Camera còn đang chụp dở: ảnh về sau thì giải phóng luôn, không rò rỉ
                foreach (var (name, task) in grabs)
                    if (!images.ContainsKey(name))
                        _ = task.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); });
                throw;
            }
        }

        /// <summary>
        /// Chạy một lượt. Trả về kết luận (Ok / Ng / Error); null nếu không chạy hoặc bị dừng.
        /// interactive=false (chạy tự động): không hiện hộp thoại báo lỗi, chỉ ghi log và trạng thái.
        /// </summary>
        private async Task<RunVerdict?> RunCoreAsync(
            RunMode mode, Func<CancellationToken, Task<Dictionary<string, HImage>>>? imageSource = null, bool interactive = true)
        {
            var graph = _editor.Model;
            if (graph.Nodes.Count == 0)
            {
                StatusText = "Đồ thị trống";
                return null;
            }

            IsRunning = true;
            _editor.IsLocked = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            ClearDisplay();                       // bỏ ảnh đang hiển thị rồi mới giải phóng kết quả cũ
            Results.Clear();
            _editor.ResetRunState();
            StatusText = "Đang chạy...";

            var options = new RunOptions { Mode = mode, NeedDisplay = true };
            Dictionary<string, HImage>? sources = null;       // ảnh camera của lượt này (ta sở hữu, Dispose ở cuối)
            var startedAt = DateTime.Now;
            var logger = new UiLogger(PostLog);
            var executor = new GraphExecutor(logger);
            executor.NodeFinished += OnNodeFinished;

            AppendLog($"--- {(mode == RunMode.Teach ? "Học mẫu" : "Chạy")} {DateTime.Now:HH:mm:ss} ({ModeName(mode)}) ---");
            var sw = Stopwatch.StartNew();
            RunVerdict? verdict = null;

            try
            {
                if (imageSource != null)
                {
                    StatusText = "Đang chụp ảnh từ camera...";
                    sources = await imageSource(ct);
                    bool first = true;
                    foreach (var (name, image) in sources)
                    {
                        options.SetSeed("Image@" + name, image);          // node Camera Image chọn theo tên camera
                        if (first) options.SetSeed("Image", image);       // camera đầu tiên cũng là "Image"
                        first = false;
                    }
                    StatusText = "Đang chạy...";
                }

                var run = await Task.Run(() =>
                {
                    InitializeTools(graph, options, logger, ct);
                    return executor.Run(graph, options, ct);
                }, ct);

                _lastRun = run;
                sw.Stop();

                int bad = run.Nodes.Values.Count(n => n.Status != NodeStatus.Ok);
                StatusText = bad == 0
                    ? $"Xong: {run.Nodes.Count} node, {sw.ElapsedMilliseconds} ms"
                    : $"Xong: {bad}/{run.Nodes.Count} node không thành công ({sw.ElapsedMilliseconds} ms)";
                AppendLog(StatusText);

                UpdateViewers();
                verdict = CycleRecordBuilder.Evaluate(graph, run).Verdict;

                // Chạy thật: gửi độ bù (hoặc NG) cho robot. Chạy thử / học mẫu thì bộ gửi tự bỏ qua.
                var sent = await _dispatcher.DispatchAsync(graph, run, mode, ct);
                if (mode == RunMode.Live || sent.Attempted)
                {
                    AppendLog((sent.Success ? "[robot] " : "[robot - LỖI] ") + sent.Message);
                    StatusText += sent.Success ? " | Robot: OK" : " | Robot: LỖI";
                    if (!sent.Success)
                    {
                        verdict = RunVerdict.Error;                 // robot không nhận độ bù: PLC phải biết là có sự cố
                        if (interactive) _dialogs.ShowError(sent.Message);
                    }
                }

                // Lưu ảnh và kết quả (chỉ khi chạy từ camera, không lưu lúc học mẫu)
                if (sources != null && mode != RunMode.Teach && SaveResults)
                    await SaveCycleAsync(graph, run, sources, mode, startedAt, sw.ElapsedMilliseconds,
                                         sent.Attempted ? sent.Message : null, interactive);
            }
            catch (OperationCanceledException)
            {
                verdict = null;
                StatusText = "Đã dừng";
                AppendLog("Đã dừng theo yêu cầu.");
            }
            catch (Exception ex)
            {
                verdict = RunVerdict.Error;
                StatusText = "Lỗi khi chạy";
                AppendLog($"[lỗi] {ex.Message}");
                if (interactive) _dialogs.ShowError(ex.Message);
            }
            finally
            {
                if (sources != null)
                    foreach (var image in sources.Values) image.Dispose();     // GraphRunResult đã có bản copy riêng
                executor.NodeFinished -= OnNodeFinished;
                _cts?.Dispose();
                _cts = null;
                _editor.IsLocked = IsAuto;          // đang chạy tự động thì đồ thị vẫn bị khóa giữa các lượt
                IsRunning = false;
            }
            return verdict;
        }

        private async Task SaveCycleAsync(VisionGraph graph, GraphRunResult run, Dictionary<string, HImage> sources,
                                          RunMode mode, DateTime startedAt, long durationMs, string? robotMessage, bool interactive)
        {
            try
            {
                var record = CycleRecordBuilder.Build(graph, run, ++_cycleId, startedAt, durationMs, mode,
                                                      extraMessage: robotMessage);
                var archiver = new ResultArchiver(SaveFolder);
                string folder = await Task.Run(() => archiver.Save(record, graph, run, sources));
                AppendLog($"[lưu] {record.Verdict}: {folder}");
                StatusText += $" | Đã lưu ({record.Verdict})";
            }
            catch (Exception ex)
            {
                // Lỗi lưu (ví dụ không có ổ D:) không làm hỏng kết quả chạy; báo để người dùng biết dữ liệu chưa được lưu
                AppendLog($"[lưu - LỖI] {ex.Message}");
                StatusText += " | LỖI lưu";
                if (interactive) _dialogs.ShowError($"Không lưu được ảnh và kết quả vào '{SaveFolder}':\n{ex.Message}");
            }
        }

        // ================= CHẠY TỰ ĐỘNG THEO TÍN HIỆU PLC =================

        [RelayCommand]
        private void ToggleAuto()
        {
            if (IsAuto)
            {
                StopAuto("Đã tắt chế độ tự động.");
                return;
            }
            if (IsRunning) return;

            if (_editor.Model.Nodes.Count == 0)
            {
                _dialogs.ShowError("Đồ thị trống, chưa có gì để chạy.");
                return;
            }
            if (_devices.ConnectedCameras().Count == 0)
            {
                _dialogs.ShowError("Chưa có camera nào được kết nối. Mở Cấu hình kết nối, tab Camera, tick Bật rồi bấm Kết nối.");
                return;
            }
            if (Mode == RunMode.Live &&
                !_dialogs.Confirm("Chạy tự động ở chế độ CHẠY THẬT: mỗi khi PLC yêu cầu, chương trình sẽ gửi độ bù cho ROBOT.\nTiếp tục?"))
                return;

            if (_auto == null)
            {
                _auto = new PlcAutoRunner(_devices, RunAutoCycleAsync, PostLog);
                _auto.Stopped += reason => _ui.Post(_ =>
                {
                    if (!IsAuto) return;
                    StopAuto("Tự động DỪNG: " + reason);
                    _dialogs.ShowError("Chế độ tự động đã dừng:\n" + reason);
                }, null);
            }

            string? error = _auto.Start();
            if (error != null)
            {
                _dialogs.ShowError(error);
                return;
            }

            IsAuto = true;
            _editor.IsLocked = true;
            StatusText = $"Tự động ({ModeName(Mode)}): đang chờ tín hiệu PLC";
            AppendLog($"--- Bật chạy tự động {DateTime.Now:HH:mm:ss} ({ModeName(Mode)}) ---");
        }

        private void StopAuto(string message)
        {
            _auto?.Stop();
            _cts?.Cancel();                       // đang giữa một lượt thì dừng lượt đó
            IsAuto = false;
            if (!IsRunning) _editor.IsLocked = false;
            StatusText = message;
            AppendLog(message);
        }

        /// <summary>Thread nền gọi khi PLC yêu cầu; chạy lượt ở UI thread (lượt chạm vào giao diện) và trả về kết luận.</summary>
        private async Task<RunVerdict> RunAutoCycleAsync(CancellationToken ct)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var verdict = await await dispatcher.InvokeAsync(() => RunCoreAsync(Mode, GrabFromCamerasAsync, interactive: false));
            return verdict ?? RunVerdict.Error;     // không chạy được hoặc bị dừng giữa chừng: báo lỗi cho PLC
        }

        private static string ModeName(RunMode mode) => mode switch
        {
            RunMode.Live => "chạy thật",
            RunMode.Teach => "học mẫu",
            _ => "chạy thử"
        };

        /// <summary>Dừng giữa các node (một node đang chạy dở thì chạy nốt rồi mới dừng).</summary>
        [RelayCommand(CanExecute = nameof(CanStop))]
        private void Stop() => _cts?.Cancel();

        // ================= CHẠY =================

        /// <summary>Nạp tài nguyên cho các tool cần (shape model...). Chạy trên thread nền.</summary>
        private static void InitializeTools(VisionGraph graph, RunOptions options, IVisionLogger logger, CancellationToken ct)
        {
            var context = new ToolContext(options.Mode, ct, logger, options.NeedDisplay);
            foreach (var node in graph.Nodes)
            {
                ct.ThrowIfCancellationRequested();
                try { node.Tool.Initialize(context); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Node '{node.ToolName}' không khởi tạo được: {ex.Message}", ex);
                }
            }
        }

        /// <summary>Có thể đến từ thread nền. Chuẩn bị chữ ở đây, rồi chuyển phần chạm giao diện về UI thread.</summary>
        private void OnNodeFinished(VisionNode node, NodeRun run)
        {
            var row = new NodeResultRowViewModel(node, run);
            _ui.Post(_ =>
            {
                _editor.UpdateNodeRun(node, run);
                Results.Add(row);
            }, null);
        }

        // ================= HIỂN THỊ =================

        private void UpdateViewers()
        {
            var run = _lastRun;
            if (run == null)
            {
                Original = ImageDisplay.Empty;
                Processed = ImageDisplay.Empty;
                return;
            }

            var order = _editor.Model.GetTopologicalOrder();

            // Ảnh gốc: ảnh của node đầu tiên (theo thứ tự chạy) chạy thành công và có ảnh
            HImage? original = null;
            string originalCaption = "";
            foreach (var n in order)
            {
                if (run.Nodes.TryGetValue(n.Id, out var nr) && nr.Result is { Success: true } r &&
                    FindImage(r) is { } img)
                {
                    original = img;
                    originalCaption = n.ToolName;
                    break;
                }
            }
            Original = original == null ? ImageDisplay.Empty
                                        : new ImageDisplay(original, Array.Empty<HObject>(), originalCaption);

            // Ảnh xử lý: node đang chọn; chưa chọn thì lấy node thành công cuối cùng
            var selected = _editor.SelectedNode?.Model
                           ?? order.LastOrDefault(n => run.Nodes.TryGetValue(n.Id, out var x) && x.Status == NodeStatus.Ok);
            if (selected == null || !run.Nodes.TryGetValue(selected.Id, out var selRun))
            {
                Processed = ImageDisplay.Empty;
                return;
            }

            // Ảnh nền: ảnh do chính node trả ra; không có (vd Threshold chỉ trả vùng) thì dùng ảnh gốc
            HImage? background = (selRun.Result != null ? FindImage(selRun.Result) : null) ?? original;

            // Vẽ chồng: các đối tượng hiển thị của node (loại ảnh, vì ảnh đã là nền)
            IReadOnlyList<HObject> overlays = selRun.Result == null
                ? Array.Empty<HObject>()
                : selRun.Result.DisplayObjects.Where(o => o is not HImage).ToList();

            string caption = $"{selected.ToolName} - {(selRun.Status == NodeStatus.Ok ? "OK" : selRun.Message)}";
            Processed = new ImageDisplay(background, overlays, caption);
        }

        private static HImage? FindImage(VisionToolResult result)
            => result.All.Values.OfType<HImage>().FirstOrDefault();

        /// <summary>Đặt các khung xem về trống RỒI mới giải phóng kết quả cũ (khung không được giữ đối tượng đã bị Dispose).</summary>
        private void ClearDisplay()
        {
            Original = ImageDisplay.Empty;
            Processed = ImageDisplay.Empty;
            _lastRun?.Dispose();
            _lastRun = null;
        }

        // ================= LOG =================

        /// <summary>Gọi từ thread bất kỳ.</summary>
        private void PostLog(string message) => _ui.Post(_ => AppendLog(message), null);

        /// <summary>Chỉ gọi trên UI thread.</summary>
        private void AppendLog(string message)
        {
            _log.AppendLine(message);
            if (_log.Length > MaxLogChars)
                _log.Remove(0, _log.Length - MaxLogChars / 2);      // giữ nửa cuối
            OutputText = _log.ToString();
        }

        public void Dispose()
        {
            _auto?.Dispose();
            _cts?.Cancel();
            ClearDisplay();
        }
    }
}
