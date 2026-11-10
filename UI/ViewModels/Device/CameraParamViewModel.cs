using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.Device
{
    /// <summary>
    /// Một thông số camera chỉnh bằng thanh trượt khi xem trực tiếp (phơi sáng, gain, gamma...).
    /// Kéo thanh trượt thì sau một khoảng ngắn (200 ms không kéo nữa) mới gửi xuống camera, để không gửi hàng trăm lệnh.
    /// Khoảng giá trị và giá trị ban đầu do camera báo (Load); camera không hỗ trợ thì ô bị khóa.
    /// </summary>
    public partial class CameraParamViewModel : ViewModelBase
    {
        private readonly Func<double, bool> _apply;
        private CancellationTokenSource? _cts;
        private bool _loading;

        public CameraParamViewModel(string halconName, string label, string unit, Func<double, bool> apply)
        {
            HalconName = halconName;
            Label = label;
            Unit = unit;
            _apply = apply;
        }

        /// <summary>Tên tham số theo camera / HALCON (ExposureTime, Gain...).</summary>
        public string HalconName { get; }
        public string Label { get; }
        public string Unit { get; }

        [ObservableProperty] private bool _isSupported;
        [ObservableProperty] private double _min;
        [ObservableProperty] private double _max = 1;
        [ObservableProperty] private double _value;
        [ObservableProperty] private string _status = "";

        /// <summary>Nạp khoảng giá trị và giá trị hiện tại đọc từ camera. Việc nạp KHÔNG gửi lệnh xuống camera.</summary>
        public void Load(bool supported, double current, double min, double max)
        {
            _cts?.Cancel();
            _loading = true;
            try
            {
                IsSupported = supported;
                Status = supported ? "" : "camera không hỗ trợ";
                if (!supported) return;

                Max = max;
                Min = min;
                Max = max;                                   // đặt lại Max sau Min: thanh trượt không ép sai khi khoảng mới nằm ngoài khoảng cũ
                Value = Math.Clamp(current, min, max);
            }
            finally { _loading = false; }
        }

        partial void OnValueChanged(double value)
        {
            if (_loading || !IsSupported) return;

            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            _ = ApplyAsync(value, cts.Token);
        }

        private async Task ApplyAsync(double value, CancellationToken ct)
        {
            try
            {
                await Task.Delay(200, ct);                                    // chờ người dùng ngừng kéo
                bool ok = await Task.Run(() => _apply(value), ct);            // không chặn giao diện
                if (!ct.IsCancellationRequested) Status = ok ? "" : "camera từ chối giá trị này";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Status = ex.Message; }
        }
    }
}
