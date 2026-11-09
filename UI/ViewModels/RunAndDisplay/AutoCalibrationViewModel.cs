using Communication.Devices;
using Communication.Drivers;
using Communication.Mitsubishi;
using Communication.Plc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using UI.Services;
using Vision.Calibration;
using Vision.Geometry;

namespace UI.ViewModels.RunAndDisplay
{
    /// <summary>
    /// Hai chức năng điều khiển robot kết hợp camera:
    ///  1. Hiệu chuẩn tự động (robot cầm mốc/vật đi qua lưới điểm, tự ghi calibration.json).
    ///  2. Chụp tại vị trí cố định (tới điểm chụp, đo vật, tính độ bù so với vị trí chuẩn, ghi vào thanh ghi PLC;
    ///     không ghi gì vào robot).
    /// Chỉ làm phần giao diện: kiểm tra đầu vào, hỏi xác nhận, gọi AutoCalibrator / FixedShotPicker, hiện kết quả.
    /// </summary>
    public partial class AutoCalibrationViewModel : ViewModelBase
    {
        private const int MaxLogChars = 40_000;

        private readonly DeviceManager _devices;
        private readonly RunViewModel _run;
        private readonly IDialogService _dialogs;
        private readonly Func<PlcMap?>? _plcMap;
        private readonly IProgress<string> _progress;
        private readonly StringBuilder _log = new();
        private CancellationTokenSource? _cts;

        // ---------- Dùng chung ----------
        [ObservableProperty] private string _calibrationPath = "calibration.json";
        [ObservableProperty] private string _logText = "";
        [ObservableProperty] private string _statusText = "Sẵn sàng";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCalibrationCommand))]
        [NotifyCanExecuteChangedFor(nameof(ShootAndSendCommand))]
        [NotifyCanExecuteChangedFor(nameof(TeachReferenceCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        private bool _isBusy;

        // ---------- 1. Hiệu chuẩn tự động ----------
        /// <summary>Tên điểm tâm trong robot. Để trống = dùng đúng vị trí hiện tại của robot làm tâm.</summary>
        [ObservableProperty] private string _centerPoint = "";
        [ObservableProperty] private int _countX = 3;
        [ObservableProperty] private int _countY = 3;
        [ObservableProperty] private double _stepMm = 5;
        /// <summary>Các góc xoay kiểm tra, cách nhau bằng dấu phẩy, dùng dấu chấm thập phân. Để trống = bỏ qua.</summary>
        [ObservableProperty] private string _rotationsText = "-10, 10";
        [ObservableProperty] private int _calSpeedPercent = 10;
        [ObservableProperty] private int _calSettleMs = 800;
        [ObservableProperty] private double _maxRmsMm = 0.3;
        [ObservableProperty] private int _minValidPoints = 6;

        // ---------- 2. Chụp tại vị trí cố định ----------
        /// <summary>Điểm chụp. Để trống = máy đang ở sẵn vị trí chụp (không cần robot kết nối, không di chuyển).</summary>
        [ObservableProperty] private string _shotPoint = "";
        [ObservableProperty] private bool _invertRz;
        [ObservableProperty] private int _shotSpeedPercent = 20;
        [ObservableProperty] private int _shotSettleMs = 500;
        /// <summary>Tắt = chỉ đo và báo kết quả, không ghi gì vào PLC (để thử).</summary>
        [ObservableProperty] private bool _sendToPlc = true;
        /// <summary>Bật = ghi vào nhóm thanh ghi Calib ĐẶT (Pos.Calib.*); tắt = nhóm Calib GẮP (Pos.CalibPick.*).</summary>
        [ObservableProperty] private bool _useCalibPlaceGroup;

        // Tư thế chuẩn: vị trí vật (hệ robot) lúc dạy điểm gắp / đặt. Độ bù = tư thế đo được trừ tư thế chuẩn.
        [ObservableProperty] private bool _referenceTaught;
        [ObservableProperty] private double _refX;
        [ObservableProperty] private double _refY;
        [ObservableProperty] private double _refRz;

        private bool _loadingReference;

        private sealed record ReferenceFile(bool Taught, double X, double Y, double Rz);

        private static string ReferencePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VisionGraph", "fixed_shot_reference.json");

        partial void OnReferenceTaughtChanged(bool value) => SaveReference();
        partial void OnRefXChanged(double value) => SaveReference();
        partial void OnRefYChanged(double value) => SaveReference();
        partial void OnRefRzChanged(double value) => SaveReference();

        private void LoadReference()
        {
            try
            {
                if (!File.Exists(ReferencePath)) return;
                var file = JsonSerializer.Deserialize<ReferenceFile>(File.ReadAllText(ReferencePath));
                if (file == null || !double.IsFinite(file.X) || !double.IsFinite(file.Y) || !double.IsFinite(file.Rz)) return;
                _loadingReference = true;
                RefX = file.X; RefY = file.Y; RefRz = file.Rz; ReferenceTaught = file.Taught;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Không đọc được: coi như chưa dạy, buộc người dùng dạy lại (an toàn hơn dùng số cũ không chắc đúng)
            }
            finally { _loadingReference = false; }
        }

        private void SaveReference()
        {
            if (_loadingReference) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReferencePath)!);
                File.WriteAllText(ReferencePath, JsonSerializer.Serialize(new ReferenceFile(ReferenceTaught, RefX, RefY, RefRz)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppendLog("[cảnh báo] Không lưu được vị trí chuẩn: " + ex.Message);
            }
        }

        /// <param name="plcMap">Lấy bản đồ tín hiệu PLC hiện hành (địa chỉ thanh ghi Calib để ghi độ bù).</param>
        public AutoCalibrationViewModel(DeviceManager devices, RunViewModel run, IDialogService dialogs, Func<PlcMap?>? plcMap = null)
        {
            _devices = devices;
            _run = run;
            _dialogs = dialogs;
            _plcMap = plcMap;
            _progress = new Progress<string>(AppendLog);       // tạo trên UI thread: báo tiến độ tự về UI thread
            LoadReference();
        }

        // ================= LỆNH =================

        private bool CanStart() => !IsBusy;
        private bool CanStop() => IsBusy;

        [RelayCommand(CanExecute = nameof(CanStop))]
        private void Stop() => _cts?.Cancel();

        /// <summary>Đang chạy thì cửa sổ không được đóng.</summary>
        public bool CanClose => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartCalibrationAsync()
        {
            var robot = FirstRobot();
            if (robot == null) return;

            if (!TryParseRotations(RotationsText, out var rotations, out string? rotError))
            {
                _dialogs.ShowError(rotError!);
                return;
            }

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                // Tâm: điểm có tên hoặc vị trí hiện tại
                RobotPoint center;
                if (string.IsNullOrWhiteSpace(CenterPoint))
                {
                    var cur = await robot.GetCurrentPointAsync(ct);
                    if (!cur.Ok) { _dialogs.ShowError("Không đọc được vị trí hiện tại của robot: " + cur.Error); return; }
                    center = cur.Point;
                }
                else
                {
                    if (!RobotPoint.IsValidName(CenterPoint)) { _dialogs.ShowError("Tên 'Điểm tâm' không hợp lệ."); return; }
                    var read = await robot.ReadPointAsync(CenterPoint.Trim(), ct);
                    if (!read.Ok) { _dialogs.ShowError($"Không đọc được điểm {CenterPoint}: {read.Error}"); return; }
                    center = read.Point;
                }

                var plan = new AutoCalibrationPlan(center, CountX, CountY, StepMm, rotations,
                    CalSpeedPercent, CalSettleMs, MaxRmsMm, MinValidPoints, robot.PointSettings.RzAxis);

                string? invalid = AutoCalibrator.Validate(plan);
                if (invalid != null) { _dialogs.ShowError(invalid); return; }

                var grid = AutoCalibrator.BuildGrid(plan);
                double halfX = (CountX - 1) * StepMm / 2, halfY = (CountY - 1) * StepMm / 2;
                if (!_dialogs.Confirm(
                        $"Robot sẽ TỰ DI CHUYỂN qua {grid.Count} điểm quanh tâm X {center.X:F1}, Y {center.Y:F1}\n" +
                        $"(±{halfX:F1} mm theo X, ±{halfY:F1} mm theo Y), tốc độ {CalSpeedPercent} %" +
                        (rotations.Count > 0 ? $", rồi xoay {robot.PointSettings.RzAxis} để kiểm tra dấu góc." : ".") +
                        "\n\nĐảm bảo: mốc đang được cầm chắc, quanh robot không có vật cản, tay đặt gần nút dừng khẩn.\nTiếp tục?"))
                    return;

                StatusText = "Đang hiệu chuẩn tự động...";
                AppendLog($"=== Hiệu chuẩn tự động {DateTime.Now:HH:mm:ss} ===");

                var result = await AutoCalibrator.RunAsync(robot, _run.MeasurePoseAsync, plan, _progress, ct);

                foreach (string w in result.Warnings) AppendLog("[cảnh báo] " + w);
                AppendLog((result.Success ? "[xong] " : "[không lưu] ") + result.Message);

                if (!result.Success || result.Data == null)
                {
                    StatusText = "Hiệu chuẩn không thành công";
                    _dialogs.ShowError(result.Message);
                    return;
                }

                SaveCalibration(result.Data);
                StatusText = $"Đã lưu hiệu chuẩn: RMS {result.Data.RmsMm:F3} mm";
                _dialogs.ShowInfo(
                    $"{result.Message}\nĐã lưu: {Path.GetFullPath(CalibrationPath)}" +
                    (result.Warnings.Count > 0 ? "\n\nCẢNH BÁO:\n- " + string.Join("\n- ", result.Warnings) : ""));
            }
            catch (OperationCanceledException)
            {
                StatusText = "Đã dừng";
                AppendLog("Đã dừng theo yêu cầu.");
            }
            catch (Exception ex)
            {
                StatusText = "Lỗi";
                AppendLog("[lỗi] " + ex.Message);
                _dialogs.ShowError(ex.Message);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsBusy = false;
            }
        }

        private FixedShotOptions BuildShotOptions(IRobotDriver? robot, bool useReference)
            => new(ShotPoint.Trim(), CalibrationPath, InvertRz, robot?.PointSettings.RzAxis ?? 'C',
                   ShotSettleMs, ShotSpeedPercent,
                   useReference && ReferenceTaught ? new RobotPose(RefX, RefY, RefRz) : null);

        private string ShotMoveText(FixedShotOptions options)
            => string.IsNullOrWhiteSpace(options.ShotPoint)
                ? "Robot KHÔNG di chuyển (coi như máy đang ở vị trí chụp)."
                : $"Robot sẽ TỰ DI CHUYỂN tới điểm chụp {options.ShotPoint} (tốc độ {ShotSpeedPercent} %).";

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task ShootAndSendAsync()
        {
            var robot = TryGetRobot();
            var options = BuildShotOptions(robot, useReference: true);

            if (SendToPlc && !ReferenceTaught)
            {
                _dialogs.ShowError("Chưa dạy vị trí chuẩn của vật. Đặt vật đúng vị trí dạy điểm gắp / đặt, rồi bấm " +
                                   "'Lấy vị trí vật hiện tại làm chuẩn'. Nếu không, độ bù sẽ sai.");
                return;
            }

            string group = UseCalibPlaceGroup ? "Calib ĐẶT (Pos.Calib)" : "Calib GẮP (Pos.CalibPick)";
            string sendText = SendToPlc
                ? $"Sau khi đo sẽ GHI độ bù vào thanh ghi PLC, nhóm {group}. Không ghi gì vào robot."
                : "Chỉ đo và báo kết quả, KHÔNG ghi vào PLC.";
            if (!_dialogs.Confirm($"{ShotMoveText(options)}\n{sendText}\n\nĐảm bảo quanh robot không có vật cản. Tiếp tục?")) return;

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                StatusText = "Đang chụp tại vị trí cố định...";
                AppendLog($"=== Chụp tại vị trí cố định {DateTime.Now:HH:mm:ss} ===");

                OffsetSender? send = null;
                if (SendToPlc) send = SendOffsetToPlcAsync;
                var result = await FixedShotPicker.RunAsync(robot, _run.MeasurePoseAsync, options, send, _progress, ct);

                AppendLog((result.Success ? "[xong] " : "[không gửi] ") + result.Message);
                StatusText = result.Success ? "Xong" : "Không thành công";
                if (!result.Success) _dialogs.ShowError(result.Message);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Đã dừng";
                AppendLog("Đã dừng theo yêu cầu.");
            }
            catch (Exception ex)
            {
                StatusText = "Lỗi";
                AppendLog("[lỗi] " + ex.Message);
                _dialogs.ShowError(ex.Message);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsBusy = false;
            }
        }

        /// <summary>Đo vật ở vị trí hiện tại và lưu làm tư thế chuẩn (vật đang đúng vị trí dạy điểm gắp / đặt).</summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task TeachReferenceAsync()
        {
            var robot = TryGetRobot();
            var options = BuildShotOptions(robot, useReference: false);

            if (!_dialogs.Confirm(
                    "Dạy vị trí chuẩn: vật PHẢI đang nằm đúng vị trí mà điểm gắp / đặt trong robot (PLC) đã được dạy.\n\n" +
                    ShotMoveText(options) + "\nTư thế đo được sẽ thay vị trí chuẩn cũ. Tiếp tục?"))
                return;

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                StatusText = "Đang dạy vị trí chuẩn...";
                AppendLog($"=== Dạy vị trí chuẩn {DateTime.Now:HH:mm:ss} ===");

                var result = await FixedShotPicker.MeasureAsync(robot, _run.MeasurePoseAsync, options, _progress, ct);
                if (!result.Success || result.Pose is not { } pose)
                {
                    AppendLog("[không lưu] " + result.Message);
                    StatusText = "Không thành công";
                    _dialogs.ShowError(result.Message);
                    return;
                }

                RefX = pose.X; RefY = pose.Y; RefRz = pose.Rz;
                ReferenceTaught = true;
                AppendLog($"[xong] Đã lưu vị trí chuẩn: {pose}");
                StatusText = "Đã dạy vị trí chuẩn";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Đã dừng";
                AppendLog("Đã dừng theo yêu cầu.");
            }
            catch (Exception ex)
            {
                StatusText = "Lỗi";
                AppendLog("[lỗi] " + ex.Message);
                _dialogs.ShowError(ex.Message);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsBusy = false;
            }
        }

        /// <summary>Ghi độ bù vào thanh ghi PLC (nhóm đặt hoặc gắp), giới hạn lấy từ cấu hình robot nếu có.</summary>
        private async Task<(bool Ok, string Message)> SendOffsetToPlcAsync(RobotOffset offset, CancellationToken ct)
        {
            var map = _plcMap?.Invoke();
            if (map == null) return (false, "Chưa có bản đồ tín hiệu PLC.");

            var robotCfg = _devices.RobotConfigs.FirstOrDefault(c => c.Enabled && MitsubishiPointSettings.Read(c).Mode == RobotSendMode.ViaPlc)
                           ?? _devices.RobotConfigs.FirstOrDefault(c => c.Enabled);

            IPlcDriver? plc = null;
            if (robotCfg != null) plc = _devices.PlcFor(robotCfg);
            if (plc == null)
            {
                var plcs = _devices.ConnectedPlcs();
                if (plcs.Count > 0) { var (_, first) = plcs[0]; plc = first; }
            }
            if (plc == null) return (false, "PLC chưa kết nối.");

            double maxMm = robotCfg?.GetDouble("MaxOffsetMm", 50) ?? 50;
            double maxDeg = robotCfg?.GetDouble("MaxOffsetDeg", 30) ?? 30;
            string prefix = UseCalibPlaceGroup ? PlcOffsetWriter.PlacePrefix : PlcOffsetWriter.PickPrefix;

            var result = await PlcOffsetWriter.WriteAsync(plc, map, offset.Dx, offset.Dy, 0, offset.Drz, maxMm, maxDeg, ct, prefix);
            return (result.Success, result.Message);
        }

        // ================= HỖ TRỢ =================

        /// <summary>Robot đang nối (nếu có), không báo lỗi: tab 2 chỉ cần robot khi có điểm chụp.</summary>
        private IRobotDriver? TryGetRobot()
        {
            var robots = _devices.ConnectedRobots();
            return robots.Count == 0 ? null : robots[0].Driver;
        }

        private IRobotDriver? FirstRobot()
        {
            var robots = _devices.ConnectedRobots();
            if (robots.Count == 0)
            {
                _dialogs.ShowError("Robot chưa kết nối. Mở Cấu hình thiết bị, tab Robot, bấm Kết nối.");
                return null;
            }
            if (robots.Count > 1) AppendLog($"Có {robots.Count} robot đang nối, dùng '{robots[0].Config.Name}'.");
            return robots[0].Driver;
        }

        /// <summary>Lưu calibration.json; file cũ (nếu có) được sao thành .bak trước khi thay.</summary>
        private void SaveCalibration(CalibrationData data)
        {
            if (string.IsNullOrWhiteSpace(CalibrationPath))
                throw new InvalidOperationException("Chưa nhập đường dẫn file hiệu chuẩn.");

            if (File.Exists(CalibrationPath))
                File.Copy(CalibrationPath, CalibrationPath + ".bak", overwrite: true);
            CalibrationStore.Save(data, CalibrationPath);
        }

        private static bool TryParseRotations(string text, out List<double> angles, out string? error)
        {
            angles = new List<double>();
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;

            foreach (string part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
                {
                    error = $"Góc xoay kiểm tra '{part}' không phải số. Dùng dấu chấm thập phân, phân cách bằng dấu phẩy.";
                    return false;
                }
                angles.Add(v);
            }
            return true;
        }

        private void AppendLog(string message)
        {
            _log.AppendLine(message);
            if (_log.Length > MaxLogChars) _log.Remove(0, _log.Length - MaxLogChars / 2);
            LogText = _log.ToString();
        }
    }
}
