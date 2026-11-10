using Communication.Configuration;
using Communication.Drivers;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Communication.Camera
{
    /// <summary>
    /// Lớp quản lý Camera sử dụng giao thức GigE Vision thông qua HALCON .NET (`HalconDotNet`).
    /// Hỗ trợ kết nối, ngắt kết nối, chụp ảnh liên tục (Live view), cấu hình thông số và Trigger.
    /// </summary>
    public sealed class HalconCamera : IDeviceLink
    {
        // Handle kết nối framegrabber của HALCON (đại diện cho camera đang mở)
        private HTuple _hAcqHandle = null;

        // Dùng để hủy token của luồng chụp ảnh ngầm (Background Task)
        private CancellationTokenSource _cts = null;

        // Task chạy vòng lặp chụp ảnh liên tục
        private Task _grabTask = null;

        // Cờ đánh dấu trạng thái đang chạy chụp ảnh (volatile để đảm bảo tính đồng bộ giữa các luồng)
        private volatile bool _isGrabbing = false;

        // Cờ đánh dấu đối tượng đã được giải phóng tài nguyên (Dispose Pattern)
        private bool _disposed = false;

        // Khóa đồng bộ luồng (Thread Synchronization Lock) để bảo vệ truy xuất _hAcqHandle
        private readonly object _lockObj = new object();

        /// <summary>Tên / ID camera truyền cho OpenFramegrabber ("default" = camera đầu tiên).</summary>
        public string DeviceId { get; set; } = "default";

        // Sự kiện bắn ra khi chụp được một ảnh mới (truyền HObject đi, người nhận tự Dispose)
        public event EventHandler<HObject> OnImageGrabbed;

        // Sự kiện thông báo lỗi xảy ra trong quá trình hoạt động
        public event EventHandler<string> OnError;

        /// <summary>
        /// Kiểm tra xem camera đã mở kết nối hay chưa.
        /// </summary>
        public bool IsConnected
        {
            get { lock (_lockObj) { return _hAcqHandle != null; } }
        }

        /// <summary>Camera có đang chụp liên tục không.</summary>
        public bool IsGrabbing => _isGrabbing;

        public string Description => $"GigE Vision {DeviceId}" + (Warnings.Length > 0 ? " – " + Warnings : "");

        public HalconCamera() { }

        /// <summary>Tạo camera từ cấu hình thiết bị (Thiết bị, Phơi sáng, Gain, Kiểu trigger).</summary>
        public static HalconCamera FromConfig(DeviceConfig d) => new(d.GetString("DeviceId"))
        {
            ExposureUs = d.GetDouble("Exposure", 5000),
            Gain = d.GetDouble("Gain", 0),
            TriggerMode = d.GetString("Trigger")
        };

        public HalconCamera(string deviceId) { DeviceId = string.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId; }

        // ---- Phần nối với hệ thống cấu hình (IDeviceLink) ----

        /// <summary>Cấu hình áp dụng ngay sau khi nối (lấy từ ô nhập trong màn hình cấu hình).</summary>
        public double? ExposureUs { get; set; }
        public double? Gain { get; set; }
        /// <summary>"Liên tục" (TriggerMode Off, camera đã chạy ổn ở dự án cũ), "Software" hoặc "Hardware".</summary>
        public string TriggerMode { get; set; } = "Liên tục";
        public string HardwareLine { get; set; } = "Line0";

        /// <summary>Ghi chú sau khi nối: tham số nào không áp dụng được (nối vẫn thành công).</summary>
        public string Warnings { get; private set; } = string.Empty;

        public Task ConnectAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            LastError = string.Empty;
            string connectError = string.Empty;
            void Capture(object? s, string m) => connectError = m;
            OnError += Capture;
            bool ok;
            try { ok = Connect(DeviceId); }
            finally { OnError -= Capture; }
            if (!ok) throw new IOException(connectError.Length > 0 ? connectError : "Không mở được camera " + DeviceId);

            var warn = new List<string>();
            if (ExposureUs is double e && !SetExposureTime(e)) warn.Add("Exposure");
            if (Gain is double g && g >= 1 && !SetGain(g)) warn.Add("Gain");

            bool useTrigger = TriggerMode != "Liên tục";
            if (!ConfigureTrigger(useTrigger, TriggerMode != "Hardware", HardwareLine)) warn.Add("Trigger: " + LastError);
            Warnings = warn.Count == 0 ? "" : "Chưa áp dụng được: " + string.Join("; ", warn);
        }, ct);

        #region Scan / Connect / Disconnect

        /// <summary>
        /// Quét tất cả Camera GigE Vision đang kết nối trong mạng LAN.
        /// </summary>
        /// <returns>Danh sách tên/địa chỉ các camera tìm thấy.</returns>
        public static List<string> ScanCameras(out string error)
        {
            error = null;
            var cameraList = new List<string>();
            try
            {
                // Gọi hàm HALCON để lấy thông tin các thiết bị GigEVision2
                HOperatorSet.InfoFramegrabber("GigEVision2", "device", out HTuple information, out HTuple valueList);

                for (int i = 0; i < valueList.Length; i++)
                {
                    string deviceName = valueList[i].S;
                    if (!string.IsNullOrWhiteSpace(deviceName))
                        cameraList.Add(deviceName);
                }
            }
            catch (HalconException ex)
            {
                error = $"Lỗi quét Camera: {ex.Message}";
            }

            return cameraList;
        }

        /// <summary>
        /// Mở kết nối tới một camera cụ thể.
        /// </summary>
        /// <param name="cameraName">Tên hoặc ID của camera (mặc định là "default").</param>
        /// <returns>True nếu kết nối thành công, ngược lại False.</returns>
        public bool Connect(string cameraName = "default")
        {
            // Ngắt kết nối cũ NGOÀI khối lock (vì bên trong Disconnect -> StopGrabbing có gọi Task.Wait,
            // nếu giữ lock sẽ dẫn đến tình trạng GrabLoop bị kẹt không lấy được lock -> gây treo ứng dụng).
            if (IsConnected) Disconnect();

            lock (_lockObj)
            {
                try
                {
                    string deviceToOpen = string.IsNullOrEmpty(cameraName) ? "default" : cameraName;

                    // Mở kết nối Framegrabber với giao thức GigEVision2
                    HOperatorSet.OpenFramegrabber(
                        "GigEVision2",
                        0, 0, 0, 0, 0, 0,
                        "default",
                        -1,
                        "default",
                        -1,
                        "false",
                        "default",
                        deviceToOpen,
                        0,
                        -1,
                        out _hAcqHandle
                    );

                    return true;
                }
                catch (HalconException ex)
                {
                    _hAcqHandle = null;
                    OnError?.Invoke(this, $"Lỗi OpenFramegrabber (DeviceId đang dùng: '{(string.IsNullOrEmpty(cameraName) ? "default" : cameraName)}'): {ex.Message}. {DescribeFoundCameras()}");
                    return false;
                }
                catch (Exception ex)
                {
                    _hAcqHandle = null;
                    OnError?.Invoke(this, $"Lỗi không xác định khi kết nối Camera: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Khi mở camera thất bại: liệt kê những camera HALCON (GigEVision2) thấy, để phân biệt
        /// "không thấy camera" (IP / subnet / tường lửa) với "thấy nhưng không mở được" (đang bị chương trình khác giữ).
        /// </summary>
        private static string DescribeFoundCameras()
        {
            var list = ScanCameras(out string scanError);
            if (scanError != null) return scanError;
            return list.Count == 0
                ? "HALCON không thấy camera GigE nào (kiểm tra IP cùng subnet với card mạng, tường lửa cho chương trình này)."
                : "HALCON thấy: " + string.Join(" | ", list) +
                  ". Camera có trong danh sách nhưng không mở được: thường do chương trình khác (SDK / viewer của hãng) đang giữ camera, hoặc DeviceId sai.";
        }

        /// <summary>
        /// Ngắt kết nối camera, đồng thời dừng quá trình chụp ảnh và giải phóng handle.
        /// </summary>
        public void Disconnect()
        {
            // Dừng chụp ảnh trước khi đóng kết nối
            StopGrabbing();

            lock (_lockObj)
            {
                if (_hAcqHandle != null)
                {
                    try { HOperatorSet.CloseFramegrabber(_hAcqHandle); }
                    catch (HalconException) { }
                    finally { _hAcqHandle = null; }
                }
            }
        }

        #endregion

        #region Grabbing

        /// <summary>
        /// Bắt đầu quá trình chụp ảnh liên tục (Live acquisition) trên một luồng nền (Background Task).
        /// </summary>
        public void StartGrabbing()
        {
            lock (_lockObj)
            {
                // Nếu chưa kết nối hoặc đang chụp rồi thì bỏ qua
                if (_hAcqHandle == null || _isGrabbing) return;

                _cts = new CancellationTokenSource();
                _isGrabbing = true;

                var token = _cts.Token;

                // Chạy vòng lặp chụp ảnh bất đồng bộ
                _grabTask = Task.Run(() => GrabLoop(token));
            }
        }

        /// <summary>
        /// Dừng quá trình chụp ảnh liên tục và đợi luồng kết thúc an toàn.
        /// </summary>
        public void StopGrabbing()
        {
            CancellationTokenSource cts;
            Task task;
            lock (_lockObj)
            {
                cts = _cts;
                task = _grabTask;
                _cts = null;
                _grabTask = null;
                _isGrabbing = false;
            }

            if (cts == null) return;

            try { cts.Cancel(); } catch { }
            try { task?.Wait(2000); } catch { } // Đợi tối đa 2 giây để task kết thúc
            cts.Dispose();
        }

        /// <summary>
        /// Vòng lặp chính thực hiện việc lấy ảnh liên tục từ camera.
        /// </summary>
        /// <param name="token">Token dùng để hủy vòng lặp.</param>
        private void GrabLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HTuple localHandle;
                lock (_lockObj) { localHandle = _hAcqHandle; }
                if (localHandle == null) break;

                HObject image = null;
                try
                {
                    // Lấy ảnh bất đồng bộ với timeout 500ms (giúp vòng lặp có thể thoát được khi có yêu cầu Cancel)
                    HOperatorSet.GrabImageAsync(out image, localHandle, 500.0);

                    if (image != null && image.IsInitialized())
                    {
                        // Mỗi người nhận sự kiện được một bản Clone RIÊNG (người nhận tự Dispose bản của mình), để
                        // nhiều nơi cùng nghe (xem trước, chạy đồ thị...) mà không Dispose nhầm ảnh của nhau.
                        var handlers = OnImageGrabbed;
                        if (handlers != null)
                        {
                            foreach (EventHandler<HObject> h in handlers.GetInvocationList())
                            {
                                HObject copy = image.Clone();
                                try { h(this, copy); }
                                catch (Exception ex)
                                {
                                    copy.Dispose();
                                    OnError?.Invoke(this, $"Lỗi xử lý ảnh: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (HalconException ex)
                {
                    // Nếu đã có yêu cầu hủy thì bỏ qua lỗi timeout khi đang đóng
                    if (token.IsCancellationRequested) break;

                    // Nếu là lỗi Timeout (chưa có frame hoặc chưa kích hoạt trigger) thì bỏ qua, tiếp tục vòng lặp
                    if (ex.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    OnError?.Invoke(this, $"Lỗi trong quá trình Grab: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    // Đảm bảo lỗi từ hàm xử lý bên ngoài (Subscriber) không làm chết vòng lặp ngầm
                    OnError?.Invoke(this, $"Lỗi xử lý ảnh: {ex.Message}");
                }
                finally
                {
                    // Giải phóng bộ nhớ đệm ảnh tại chỗ của vòng lặp
                    image?.Dispose();
                }
            }

            if (!token.IsCancellationRequested)
                _isGrabbing = false;   // Đặt lại cờ nếu vòng lặp tự thoát do lỗi phần cứng/mất kết nối
        }

        #endregion

        #region Camera Parameters

        /// <summary>
        /// Hàm tiện ích dùng để giới hạn giá trị (Clamp) nằm trong khoảng [min, max].
        /// </summary>
        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        // Khóa riêng cho việc set tham số nhằm tránh việc nhiều lệnh Stop/Start grab chồng chéo khi kéo thanh trượt (Slider)
        private readonly object _paramGate = new object();

        /// <summary>
        /// Thiết lập tham số cho camera. Ưu tiên set trực tiếp không dừng live, 
        /// nếu camera từ chối khi đang acquire thì sẽ tự động dừng grab -> set -> chạy lại.
        /// </summary>
        private bool SetParam(string name, HTuple value, string label)
        {
            lock (_paramGate)
            {
                // 1. Thử áp dụng trực tiếp khi đang chạy (không bị khựng khung hình live)
                if (TryApply(name, value, label, out string error)) return true;

                // 2. Nếu camera từ chối khi đang acquisition -> dừng grab, thực hiện set lại, sau đó chạy lại
                if (_isGrabbing)
                {
                    StopGrabbing();   // Gọi NGOÀI _lockObj để tránh deadlock
                    try
                    {
                        if (TryApply(name, value, label, out error)) return true;
                    }
                    finally
                    {
                        StartGrabbing();
                    }
                }

                OnError?.Invoke(this, $"Không thể chỉnh {label}: {error}");
                return false;
            }
        }

        /// <summary>
        /// Hàm hỗ trợ gọi lệnh Set và Get lại tham số từ phần cứng HALCON để kiểm tra.
        /// </summary>
        private bool TryApply(string name, HTuple value, string label, out string error)
        {
            error = string.Empty;
            lock (_lockObj)
            {
                if (_hAcqHandle == null)
                {
                    error = "Camera chưa kết nối";
                    return false;
                }

                try
                {
                    HOperatorSet.SetFramegrabberParam(_hAcqHandle, name, value);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, name, out HTuple actual);
                    System.Diagnostics.Debug.WriteLine($"{label}: yêu cầu {value}, thực tế {actual}");
                    return true;
                }
                catch (HalconException ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        // --- Các phương thức cấu hình thông số cụ thể ---

        // Thời gian phơi sáng (ExposureTime): 0..300000 µs
        public bool SetExposureTime(double us)
            => SetParam("ExposureTime", new HTuple(Clamp(us, 0, 300000)), "Exposure");

        // Độ khuếch đại (Gain): 1..8
        public bool SetGain(double gain)
            => SetParam("Gain", new HTuple(Clamp(gain, 1.0, 8.0)), "Gain");

        // Độ tương phản Gamma: 0..255
        public bool SetGamma(int raw)
            => SetParam("Gamma", new HTuple((int)Clamp(raw, 0, 255)), "Gamma");

        // Độ tương phản Contrast: 0..200
        public bool SetContrast(int raw)
            => SetParam("Contrast", new HTuple((int)Clamp(raw, 0, 200)), "Contrast");

        /// <summary>
        /// Đọc giá trị hiện tại và khoảng cho phép (min, max) của một tham số số của camera ("ExposureTime", "Gain"...).
        /// Trả false nếu camera không có tham số này hoặc không báo khoảng giá trị.
        /// </summary>
        public bool TryReadNumericParam(string name, out double current, out double min, out double max)
        {
            current = min = max = 0;
            lock (_lockObj)
            {
                if (_hAcqHandle == null) return false;
                try
                {
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, name, out HTuple value);
                    double[] c = value.ToDArr();
                    if (c.Length == 0) return false;

                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, name + "_range", out HTuple range);
                    double[] r = range.ToDArr();
                    if (r.Length < 2) return false;

                    current = c[0];
                    min = r[0];
                    max = r[1];
                    return max > min;
                }
                catch (Exception)
                {
                    return false;      // không hỗ trợ, hoặc kiểu giá trị không phải số
                }
            }
        }

        /// <summary>
        /// Đặt một tham số số của camera theo giá trị gốc của camera (không cắt khoảng). Camera từ chối thì trả false và báo lỗi qua OnError.
        /// <paramref name="integer"/> = true với tham số nguyên (Gamma, Contrast trên nhiều camera): làm tròn rồi gửi số nguyên.
        /// </summary>
        public bool SetNumericParam(string name, double value, bool integer, string label)
            => SetParam(name, integer ? new HTuple((int)Math.Round(value)) : new HTuple(value), label);

        /// <summary>
        /// Thiết lập tốc độ khung hình (Frame Rate / FPS).
        /// </summary>
        public bool SetFrameRate(double fps)
        {
            bool wasGrabbing = _isGrabbing;
            if (wasGrabbing) StopGrabbing();

            try
            {
                lock (_lockObj)
                {
                    if (_hAcqHandle == null) return false;

                    // Kích hoạt cho phép chỉnh frame rate (bỏ qua ngoại lệ nếu camera không hỗ trợ tính năng này)
                    TrySet("AcquisitionFrameRateEnable", "true");

                    HOperatorSet.SetFramegrabberParam(_hAcqHandle, "AcquisitionFrameRate", fps);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "AcquisitionFrameRate", out HTuple actual);
                    System.Diagnostics.Debug.WriteLine($"FrameRate: yêu cầu {fps}, thực tế {actual}");
                }
                return true;
            }
            catch (HalconException ex)
            {
                OnError?.Invoke(this, $"Không thể chỉnh Frame Rate: {ex.Message}");
                return false;
            }
            finally
            {
                if (wasGrabbing) StartGrabbing();
            }
        }

        /// <summary>
        /// Chỉ gọi trong khối lock. Bỏ qua ngoại lệ nếu tham số không được camera hỗ trợ.
        /// </summary>
        private void TrySet(string name, HTuple value)
        {
            try { HOperatorSet.SetFramegrabberParam(_hAcqHandle, name, value); }
            catch (HalconException ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrySet bỏ qua '{name}'={value}: {ex.Message}");
            }
        }

        #endregion

        #region Trigger

        /// <summary>Thông báo lỗi gần nhất của thao tác Trigger (dùng để hiển thị lên ViewModel/UI).</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Đọc một tham số dạng chuỗi từ camera một cách an toàn.
        /// </summary>
        private string ReadParam(string name)
        {
            try
            {
                HOperatorSet.GetFramegrabberParam(_hAcqHandle, name, out HTuple v);
                return v.ToString();
            }
            catch (HalconException ex)
            {
                return $"(lỗi: {ex.Message})";
            }
        }

        /// <summary>
        /// Cấu hình chế độ chụp Trigger cho camera.
        /// </summary>
        /// <param name="useTrigger">True nếu bật Trigger, False nếu chạy liên tục (Free-run).</param>
        /// <param name="isSoftwareTrigger">True nếu dùng Software Trigger, False nếu dùng Hardware Trigger.</param>
        /// <param name="hardwareLine">Tên cổng tín hiệu cứng nếu dùng Hardware Trigger (mặc định "Line0").</param>
        public bool ConfigureTrigger(bool useTrigger, bool isSoftwareTrigger = true, string hardwareLine = "Line0")
        {
            bool wasGrabbing = _isGrabbing;
            StopGrabbing();   // Dừng vòng lặp chụp bên ngoài khối lock

            try
            {
                lock (_lockObj)
                {
                    if (_hAcqHandle == null)
                    {
                        LastError = "Camera chưa kết nối";
                        return false;
                    }

                    // Dừng tiến trình acquisition ở tầng HALCON trước khi đổi mode Trigger.
                    // (Vì GrabImageAsync ngầm bật acquisition, khiến camera khóa TriggerMode không cho chỉnh sửa).
                    if (useTrigger)
                        TrySet("do_abort_grab", 1);

                    string wanted = useTrigger ? "On" : "Off";
                    try
                    {
                        if (useTrigger)
                            TrySet("TriggerSource", isSoftwareTrigger ? "Software" : hardwareLine);

                        HOperatorSet.SetFramegrabberParam(_hAcqHandle, "TriggerMode", wanted);
                    }
                    catch (HalconException ex)
                    {
                        LastError = $"Set TriggerMode='{wanted}' lỗi: {ex.Message}. " +
                                    $"Giá trị hợp lệ: {ReadParam("TriggerMode_values")}";
                        OnError?.Invoke(this, LastError);
                        return false;
                    }

                    // Đọc lại giá trị thực tế từ phần cứng để xác thực
                    string actual = ReadParam("TriggerMode");
                    System.Diagnostics.Debug.WriteLine($"TriggerMode: yêu cầu {wanted}, thực tế {actual}");

                    if (!string.Equals(actual.Trim('"'), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        LastError = $"TriggerMode yêu cầu '{wanted}' nhưng camera trả về '{actual}'. " +
                                    $"Giá trị hợp lệ: {ReadParam("TriggerMode_values")}";
                        OnError?.Invoke(this, LastError);
                        return false;
                    }
                }

                LastError = string.Empty;
                return true;
            }
            finally
            {
                if (wasGrabbing) StartGrabbing();
            }
        }

        /// <summary>
        /// Lấy MỘT khung ảnh mới (chụp sau lúc gọi). Người gọi sở hữu HImage trả về và phải Dispose.
        /// Nếu vòng chụp chưa chạy thì tự bật rồi tắt lại sau khi lấy xong; đang chạy (xem trực tiếp) thì dùng luôn.
        /// Trigger Software: tự gửi lệnh chụp. Hardware: chờ tín hiệu phần cứng đến hết thời gian chờ.
        /// </summary>
        public async Task<HImage> GrabNextAsync(int timeoutMs = 3000, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Camera chưa kết nối.");

            var tcs = new TaskCompletionSource<HImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? sender, HObject frame)
            {
                HImage img = null;
                try { img = new HImage(frame); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { frame.Dispose(); }
                if (img != null && !tcs.TrySetResult(img)) img.Dispose();   // khung thứ hai trở đi: bỏ
            }

            bool startedHere = !_isGrabbing;
            OnImageGrabbed += Handler;
            try
            {
                if (startedHere) StartGrabbing();

                if (TriggerMode == "Software")
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);   // để vòng chụp kịp chờ khung
                    if (!TriggerSoftware()) throw new InvalidOperationException(LastError);
                }

                var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (finished != tcs.Task)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException($"Camera {DeviceId} không gửi ảnh trong {timeoutMs} ms.");
                }
                return await tcs.Task.ConfigureAwait(false);
            }
            catch
            {
                if (tcs.Task.IsCompletedSuccessfully) tcs.Task.Result.Dispose();   // ảnh về sát lúc lỗi: không rò rỉ
                throw;
            }
            finally
            {
                OnImageGrabbed -= Handler;
                if (startedHere) StopGrabbing();
            }
        }

        /// <summary>
        /// Gửi một lệnh Software Trigger để camera chụp một ảnh (cần đang ở TriggerMode=On, Source=Software
        /// và vòng chụp đang chạy: ảnh sẽ về qua sự kiện OnImageGrabbed).
        /// </summary>
        public bool TriggerSoftware()
        {
            lock (_lockObj)
            {
                if (_hAcqHandle == null)
                {
                    LastError = "Camera chưa kết nối";
                    return false;
                }
                try
                {
                    HOperatorSet.SetFramegrabberParam(_hAcqHandle, "TriggerSoftware", "execute");
                    return true;
                }
                catch (HalconException ex)
                {
                    LastError = $"Software trigger lỗi: {ex.Message}";
                    OnError?.Invoke(this, LastError);
                    return false;
                }
            }
        }

        /// <summary>
        /// Hiển thị hộp thoại chứa các thông số liên quan đến Trigger để phục vụ chẩn đoán lỗi (Debug).
        /// </summary>
        public string DebugTrigger()
        {
            lock (_lockObj)
            {
                if (_hAcqHandle == null)
                {
                    return "Camera chưa được kết nối!";
                }

                return $"TriggerMode={ReadParam("TriggerMode")}\n" +
                    $"TriggerMode_values={ReadParam("TriggerMode_values")}\n" +
                    $"TriggerActivation={ReadParam("TriggerActivation")}\n" +
                    $"TriggerActivation_values={ReadParam("TriggerActivation_values")}\n" +
                    $"TriggerSource={ReadParam("TriggerSource")}\n" +
                    $"TriggerSource_values={ReadParam("TriggerSource_values")}\n" +
                    $"AcquisitionMode={ReadParam("AcquisitionMode")}\n" +
                    $"AcquisitionMode_values={ReadParam("AcquisitionMode_values")}\n" +
                    $"TriggerSoftware={ReadParam("TriggerSoftware")}\n" +
                    $"TriggerDelay={ReadParam("TriggerDelay")}";
            }
        }

        #endregion

        #region Debug

        /// <summary>
        /// In ra danh sách toàn bộ các thông số được hỗ trợ bởi thiết bị GenICam (GenICam Device Parameters).
        /// </summary>
        public string PrintSupportedParameters()
        {
            lock (_lockObj)
            {
                if (_hAcqHandle == null)
                {
                    return "Camera chưa được kết nối!";
                }

                try
                {
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "available_param_names", out HTuple deviceParams);

                    // Danh sách trả về có thể chứa tên trùng lặp (ví dụ: StreamThreadPriority...), thực hiện lọc trùng
                    var names = new List<string>();
                    for (int i = 0; i < deviceParams.Length; i++)
                        names.Add(deviceParams[i].S);

                    return string.Join("\n", names.Distinct());
                }
                catch (HalconException ex)
                {
                    return $"Lỗi truy vấn: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// In ra thông tin chi tiết một số tham số phổ biến như Gamma, Exposure, Gain, PixelFormat kèm theo phạm vi giá trị hợp lệ của chúng.
        /// </summary>
        public string DebugGamma()
        {
            lock (_lockObj)
            {
                if (_hAcqHandle == null)
                {
                    return "Camera chưa được kết nối!";
                }

                try
                {
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "Gamma", out HTuple g0);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "Gamma_range", out HTuple range);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "ExposureAuto", out HTuple ea);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "PixelFormat", out HTuple pf);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "Gain_range", out HTuple gr);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "Contrast_range", out HTuple cr);
                    HOperatorSet.GetFramegrabberParam(_hAcqHandle, "ExposureTime_range", out HTuple er);

                    return ($"Gamma={g0}, range={range}\n" +
                        $"ExposureAuto={ea}, PixelFormat={pf}\n" +
                        $"Gain_range={gr}\n" +
                        $"Contrast_range={cr}\n" +
                        $"ExposureTime_range={er}");
                }
                catch (HalconException ex)
                {
                    return $"Lỗi truy vấn: {ex.Message}";
                }
            }
        }

        #endregion

        #region Dispose

        // Thực hiện giao diện IDisposable tiêu chuẩn để giải phóng tài nguyên an toàn
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Ngắt kết nối và giải phóng toàn bộ tài nguyên liên quan camera
                    Disconnect();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}