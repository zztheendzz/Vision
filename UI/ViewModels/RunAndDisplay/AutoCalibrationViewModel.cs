using Communication.Devices;
using Communication.Drivers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Text;
using UI.Services;
using Vision.Calibration;

namespace UI.ViewModels.RunAndDisplay
{
    /// <summary>
    /// Hai chức năng điều khiển robot kết hợp camera:
    ///  1. Hiệu chuẩn tự động (robot cầm mốc/vật đi qua lưới điểm, tự ghi calibration.json).
    ///  2. Chụp tại vị trí cố định (robot tới điểm chụp, đo vật, ghi tọa độ vào một điểm của robot).
    /// Chỉ làm phần giao diện: kiểm tra đầu vào, hỏi xác nhận, gọi AutoCalibrator / FixedShotPicker, hiện kết quả.
    /// </summary>
    public partial class AutoCalibrationViewModel : ViewModelBase
    {
        private const int MaxLogChars = 40_000;

        private readonly DeviceManager _devices;
        private readonly RunViewModel _run;
        private readonly IDialogService _dialogs;
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
        /// <summary>Điểm chụp. Để trống = robot đang ở sẵn vị trí chụp (không di chuyển).</summary>
        [ObservableProperty] private string _shotPoint = "";
        [ObservableProperty] private string _targetPoint = "P_PICK";
        [ObservableProperty] private double _rzOffsetDeg;
        [ObservableProperty] private bool _invertRz;
        [ObservableProperty] private int _shotSpeedPercent = 20;
        [ObservableProperty] private int _shotSettleMs = 500;
        /// <summary>Tắt = chỉ đo và báo kết quả, không ghi gì vào robot (để thử).</summary>
        [ObservableProperty] private bool _sendToRobot = true;

        public AutoCalibrationViewModel(DeviceManager devices, RunViewModel run, IDialogService dialogs)
        {
            _devices = devices;
            _run = run;
            _dialogs = dialogs;
            _progress = new Progress<string>(AppendLog);       // tạo trên UI thread: báo tiến độ tự về UI thread
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

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task ShootAndSendAsync()
        {
            var robot = FirstRobot();
            if (robot == null) return;

            var options = new FixedShotOptions(
                ShotPoint.Trim(), TargetPoint.Trim(), CalibrationPath, RzOffsetDeg, InvertRz,
                robot.PointSettings.RzAxis, ShotSettleMs, ShotSpeedPercent, SendToRobot);

            string moveText = string.IsNullOrWhiteSpace(options.ShotPoint)
                ? "Robot KHÔNG di chuyển (coi như đang ở vị trí chụp)."
                : $"Robot sẽ TỰ DI CHUYỂN tới điểm chụp {options.ShotPoint} (tốc độ {ShotSpeedPercent} %).";
            string sendText = SendToRobot
                ? $"Sau khi đo sẽ GHI ĐÈ điểm {options.TargetPoint} trong robot."
                : "Chỉ đo và báo kết quả, KHÔNG ghi vào robot.";
            if (!_dialogs.Confirm($"{moveText}\n{sendText}\n\nĐảm bảo quanh robot không có vật cản. Tiếp tục?")) return;

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                StatusText = "Đang chụp tại vị trí cố định...";
                AppendLog($"=== Chụp tại vị trí cố định {DateTime.Now:HH:mm:ss} ===");

                var result = await FixedShotPicker.RunAsync(robot, _run.MeasurePoseAsync, options, _progress, ct);

                AppendLog((result.Success ? "[xong] " : "[không ghi] ") + result.Message);
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

        // ================= HỖ TRỢ =================

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
