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

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunOnceCommand))]
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

        public RunViewModel(GraphEditorViewModel editor, IDialogService dialogs, OffsetDispatcher dispatcher)
        {
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

        private bool CanRun() => !IsRunning;
        private bool CanStop() => IsRunning;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private Task RunOnceAsync() => RunCoreAsync(Mode);

        /// <summary>
        /// Học mẫu: chạy một lần ở chế độ Teach. Tool học (vd FindShape) tạo và ghi file mẫu; tool Action không tác động ra ngoài.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private Task TeachAsync()
        {
            if (!_dialogs.Confirm("Học mẫu sẽ GHI ĐÈ file mẫu hiện có của các tool học (vd FindShape).\nĐặt vật đúng vị trí mẫu trước khi tiếp tục. Tiếp tục?"))
                return Task.CompletedTask;
            return RunCoreAsync(RunMode.Teach);
        }

        private async Task RunCoreAsync(RunMode mode)
        {
            var graph = _editor.Model;
            if (graph.Nodes.Count == 0)
            {
                StatusText = "Đồ thị trống";
                return;
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
            var logger = new UiLogger(PostLog);
            var executor = new GraphExecutor(logger);
            executor.NodeFinished += OnNodeFinished;

            AppendLog($"--- {(mode == RunMode.Teach ? "Học mẫu" : "Chạy")} {DateTime.Now:HH:mm:ss} ({ModeName(mode)}) ---");
            var sw = Stopwatch.StartNew();

            try
            {
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

                // Chạy thật: gửi độ bù (hoặc NG) cho robot. Chạy thử / học mẫu thì bộ gửi tự bỏ qua.
                var sent = await _dispatcher.DispatchAsync(graph, run, mode, ct);
                if (mode == RunMode.Live || sent.Attempted)
                {
                    AppendLog((sent.Success ? "[robot] " : "[robot - LỖI] ") + sent.Message);
                    StatusText += sent.Success ? " | Robot: OK" : " | Robot: LỖI";
                    if (!sent.Success) _dialogs.ShowError(sent.Message);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Đã dừng";
                AppendLog("Đã dừng theo yêu cầu.");
            }
            catch (Exception ex)
            {
                StatusText = "Lỗi khi chạy";
                AppendLog($"[lỗi] {ex.Message}");
                _dialogs.ShowError(ex.Message);
            }
            finally
            {
                executor.NodeFinished -= OnNodeFinished;
                _cts?.Dispose();
                _cts = null;
                _editor.IsLocked = false;
                IsRunning = false;
            }
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
            _cts?.Cancel();
            ClearDisplay();
        }
    }
}
