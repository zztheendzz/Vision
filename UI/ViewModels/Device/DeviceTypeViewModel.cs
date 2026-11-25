using Communication.Configuration;
using Communication.Fixtures;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UI.Services;
using UI.ViewModels.RunAndDisplay;

namespace UI.ViewModels.Device
{
    /// <summary>Một tab của một loại thiết bị: cài đặt chung + danh sách thiết bị + thêm / xóa.</summary>
    public partial class DeviceTypeViewModel : ViewModelBase
    {
        private readonly DeviceCatalog _catalog;
        private readonly DeviceConfigViewModel _owner;
        private readonly IDialogService _dialogs;

        public DeviceTypeSchema Schema { get; }

        public string Title => Schema.DisplayName;
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();
        public IReadOnlyList<FieldViewModel> GroupFields { get; }
        public bool HasGroupFields => GroupFields.Count > 0;
        public bool IsFixture => Schema.Type == DeviceType.Fixture;

        /// <summary>Tình trạng cổng nhận (đang nghe hay lý do chưa mở).</summary>
        public string ListenerStatus => _owner.ListenerStatus;

        /// <summary>Nhật ký gửi/nhận khuôn test (dùng chung cho mọi khuôn).</summary>
        public ObservableCollection<LogLine> Log => _owner.FixtureLog;
        public IRelayCommand ClearLogCommand => _owner.ClearFixtureLogCommand;
        public IRelayCommand CopyLogCommand => _owner.CopyFixtureLogCommand;

        /// <summary>DEBUG: nhật ký byte thô nhận / gửi (xóa sau khi chỉnh xong mẫu).</summary>
        public ObservableCollection<LogLine> RawLog => _owner.RawLog;
        public IRelayCommand ClearRawLogCommand => _owner.ClearRawLogCommand;
        public IRelayCommand CopyRawLogCommand => _owner.CopyRawLogCommand;

        /// <summary>Cài đặt chung, không kể các ô lệnh / kết quả của khuôn test (hai ô đó có bảng riêng).</summary>
        public IReadOnlyList<FieldViewModel> CommonFields { get; }
        /// <summary>Khuôn test: bảng lệnh OPEN / CLOSE / OPEN ALL / CLOSE ALL / CHECKSENSOR / STATE.</summary>
        public IReadOnlyList<FixtureCommandRow> CommandRows { get; } = Array.Empty<FixtureCommandRow>();
        /// <summary>Khuôn test: bảng kết quả slot BUSY / EMPTY / PASS / REPAIR / FAIL / NAN / ERROR.</summary>
        public IReadOnlyList<FixtureResultRow> ResultRows { get; } = Array.Empty<FixtureResultRow>();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveDeviceCommand))]
        private DeviceViewModel? _selectedDevice;

        public DeviceTypeViewModel(DeviceTypeSchema schema, DeviceCatalog catalog,
                                   DeviceConfigViewModel owner, IDialogService dialogs)
        {
            Schema = schema;
            _catalog = catalog;
            _owner = owner;
            _dialogs = dialogs;

            GroupFields = schema.GroupFields
                .Select(def => new FieldViewModel(
                    def,
                    () => catalog.GetGroupValue(schema.Type, def.Key),
                    v => catalog.SetGroupValue(schema.Type, def.Key, v),
                    () =>
                    {
                        foreach (var f in GroupFields!) f.RefreshVisibility();
                        _owner.MarkDirty();
                    },
                    k => catalog.GetGroupValue(schema.Type, k)))
                .ToList();

            _owner.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceConfigViewModel.ListenerStatus)) OnPropertyChanged(nameof(ListenerStatus));
            };

            CommonFields = GroupFields.Where(f => !f.Def.Key.StartsWith("Cmd.") && !f.Def.Key.StartsWith("Res.")).ToList();

            if (schema.Type == DeviceType.Fixture)
            {
                FieldViewModel? Find(string key) => GroupFields.FirstOrDefault(f => f.Def.Key == key);

                CommandRows = DeviceSchemas.FixtureCommands
                    .Select(c => new FixtureCommandRow(c.Title, Find($"Cmd.{c.Key}.Send")!, Find($"Cmd.{c.Key}.Ok")!, Find($"Cmd.{c.Key}.Err")!))
                    .ToList();
                ResultRows = DeviceSchemas.FixtureResults
                    .Select(r => new FixtureResultRow(r.Title, Find($"Res.{r.Key}.Pattern"), Find($"Res.{r.Key}.Plc")))
                    .ToList();
            }
        }

        // ---------- Quét khuôn: gửi lệnh (mặc định ASK) tới tất cả để lấy IP, số khuôn, trạng thái slot ----------

        public ObservableCollection<ScanRow> ScanResults { get; } = new();

        [ObservableProperty] private string _scanStatus = "Chưa quét";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanFixturesCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopScanCommand))]
        private bool _isScanning;

        private CancellationTokenSource? _scanCts;

        [RelayCommand(CanExecute = nameof(CanScan))]
        private async Task ScanFixturesAsync()
        {
            T Group<T>(string key, Func<string, T> parse, T fallback)
            {
                try { return parse(_catalog.GetGroupValueOrDefault(DeviceType.Fixture, key)); }
                catch { return fallback; }
            }

            string command = _catalog.GetGroupValueOrDefault(DeviceType.Fixture, "Scan.Command").Trim();
            var options = new FixtureScanOptions(
                command,
                Group("Scan.Port", v => int.Parse(v), 9999),
                _catalog.GetGroupValue(DeviceType.Fixture, "Scan.Method") == DeviceSchemas.ScanUdp ? ScanMethod.UdpBroadcast : ScanMethod.TcpSweep,
                _catalog.GetGroupValue(DeviceType.Fixture, "Scan.Range"),
                Group("Scan.TimeoutMs", v => int.Parse(v), 800),
                _catalog.GetGroupValueOrDefault(DeviceType.Fixture, "Scan.Pattern"),
                Escapes.ToBytes(_catalog.GetGroupValue(DeviceType.Fixture, DeviceSchemas.KeyTx)),
                Escapes.ToBytes(_catalog.GetGroupValue(DeviceType.Fixture, DeviceSchemas.KeyRx)));

            _scanCts = new CancellationTokenSource();
            IsScanning = true;
            // Quét lại chỉ thay các dòng do quét tạo ra; dòng khuôn tự nối vào (Tự nhận) giữ nguyên
            foreach (var old in ScanResults.Where(r => r.Source == "Quét").ToList()) ScanResults.Remove(old);
            ScanStatus = $"Đang quét (gửi '{options.Command}')...";
            var progress = new Progress<string>(m => ScanStatus = m);
            try
            {
                var found = await FixtureScanner.ScanAsync(options, progress, _scanCts.Token);
                foreach (var fx in found)
                    UpsertRow(fx, "Quét");
                ScanStatus = found.Count == 0
                    ? $"Không có khuôn nào trả lời lệnh '{options.Command}'. Kiểm tra dải IP, cổng {options.Port}, lệnh quét và mẫu trả lời."
                    : $"Tìm thấy {found.Count} khuôn. Chọn các dòng cần rồi bấm 'Thêm / cập nhật'.";
            }
            catch (OperationCanceledException) { ScanStatus = "Đã dừng quét"; }
            catch (Exception ex) { ScanStatus = "Lỗi quét: " + ex.Message; }
            finally
            {
                IsScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        private string ExistingFixtureName(ScannedFixture fx)
            => _owner.Devices.FirstOrDefault(d => d.Config.Type == DeviceType.Fixture &&
                   ((d.Config.GetString(DeviceSchemas.KeyIp) == fx.Ip && d.Config.GetInt(DeviceSchemas.KeyPort) == fx.Port) ||
                    d.Config.GetInt("FixtureNo") == fx.FixtureNo))?.Name ?? "";

        /// <summary>Thêm hoặc cập nhật dòng theo (IP, số khuôn): cùng khuôn xuất hiện lại thì gộp slot, giữ ô chọn.</summary>
        private void UpsertRow(ScannedFixture fx, string source)
        {
            var same = ScanResults.FirstOrDefault(r => r.Ip == fx.Ip && r.Fixture.FixtureNo == fx.FixtureNo);
            bool selected = same?.Selected ?? true;
            if (same != null)
            {
                var merged = new Dictionary<int, Communication.Fixtures.SlotState>(same.Fixture.Slots);
                foreach (var kv in fx.Slots) merged[kv.Key] = kv.Value;
                fx = fx with { Slots = merged };
                ScanResults.Remove(same);
            }
            ScanResults.Add(new ScanRow(fx, ExistingFixtureName(fx), same?.Source ?? source) { Selected = selected });
        }

        /// <summary>
        /// Khuôn tự nối vào cổng nhận và gửi một dòng (ví dụ P_01_2): lấy IP đầu kia, số khuôn, slot, trạng thái.
        /// Chỉ đưa vào bảng khi khuôn đó CHƯA có trong danh sách với đúng IP và số khuôn này (khuôn đã cấu hình xong thì không cần nhắc).
        /// Cổng điều khiển lấy theo "Quét khuôn – cổng" vì cổng nguồn của kết nối đến là cổng ngẫu nhiên.
        /// </summary>
        public void NoteInbound(string remote, string line)
        {
            if (!IsFixture) return;
            try
            {
                string ip = remote;
                int colon = remote.LastIndexOf(':');
                if (remote.StartsWith('[')) ip = remote.Substring(1, remote.IndexOf(']') - 1);   // IPv6 [::1]:1234
                else if (colon > 0) ip = remote.Substring(0, colon);
                if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase)) ip = ip.Substring(7);

                var m = FixtureScanner.BuildRegex(_catalog.GetGroupValueOrDefault(DeviceType.Fixture, "Scan.Pattern")).Match(line.Trim());
                if (!m.Success) return;

                int ss = int.Parse(m.Groups["SS"].Value);
                var slots = new Dictionary<int, Communication.Fixtures.SlotState>();
                if (m.Groups["NN"].Success)
                    slots[int.Parse(m.Groups["NN"].Value)] = FixtureScanner.StateOf(m.Groups["ST"].Success ? m.Groups["ST"].Value : "");

                int port = int.TryParse(_catalog.GetGroupValueOrDefault(DeviceType.Fixture, "Scan.Port"), out int p) ? p : 9999;
                bool known = _owner.Devices.Any(d => d.Config.Type == DeviceType.Fixture &&
                                                     d.Config.GetString(DeviceSchemas.KeyIp) == ip && d.Config.GetInt("FixtureNo") == ss);
                if (known) return;

                UpsertRow(new ScannedFixture(ip, port, ss, slots, line), "Tự nhận");
                ScanStatus = $"Khuôn {ss:00} ({ip}) tự nối vào cổng nhận, chưa có trong danh sách. Chọn dòng rồi bấm 'Thêm / cập nhật'.";
            }
            catch { /* dòng không phải mẫu quét: bỏ qua */ }
        }

        private bool CanScan() => IsFixture && !IsScanning;

        [RelayCommand(CanExecute = nameof(IsScanning))]
        private void StopScan() => _scanCts?.Cancel();

        /// <summary>Thêm khuôn mới (hoặc cập nhật khuôn đã có) từ các dòng đã chọn: IP, cổng, số khuôn, số slot.</summary>
        [RelayCommand]
        private void AddScanned()
        {
            int added = 0, updated = 0;
            foreach (var row in ScanResults.Where(r => r.Selected).ToList())
            {
                var fx = row.Fixture;
                var device = _owner.Devices.FirstOrDefault(d => d.Name == row.ExistingName && d.Config.Type == DeviceType.Fixture);
                if (device == null)
                {
                    device = _owner.CreateDevice(DeviceType.Fixture);
                    added++;
                }
                else updated++;

                void Put(string key, string value)
                {
                    var field = device.Fields.FirstOrDefault(x => x.Def.Key == key);
                    if (field != null) field.Text = value;
                    else device.Config.Set(key, value);
                }
                Put(DeviceSchemas.KeyIp, fx.Ip);
                Put(DeviceSchemas.KeyPort, fx.Port.ToString());
                Put("FixtureNo", fx.FixtureNo.ToString());
                if (fx.Slots.Count > 0) Put("Slots", Math.Max(1, fx.SlotCount).ToString());
            }

            _owner.MarkDirty();
            ScanStatus = $"Đã thêm {added}, cập nhật {updated} khuôn.";
        }

        [RelayCommand]
        private void SelectAllScanned(bool? select)
        {
            foreach (var r in ScanResults) r.Selected = select ?? true;
        }

        [RelayCommand]
        private void AddDevice()
        {
            var device = _owner.CreateDevice(Schema.Type);
            SelectedDevice = device;
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void RemoveDevice()
        {
            var device = SelectedDevice;
            if (device == null) return;

            var users = device.IsPlc ? _catalog.UsersOfPlc(device.Config) : new List<DeviceConfig>();
            string message = users.Count == 0
                ? $"Xóa '{device.Name}'?"
                : $"Xóa '{device.Name}'? {users.Count} thiết bị đang dùng PLC này ({string.Join(", ", users.Select(u => u.Name))}) sẽ bị bỏ chọn PLC nguồn.";
            if (!_dialogs.Confirm(message)) return;

            int index = Devices.IndexOf(device);
            _owner.DeleteDevice(device);
            SelectedDevice = Devices.Count == 0 ? null : Devices[Math.Min(index, Devices.Count - 1)];
        }

        private bool HasSelection() => SelectedDevice != null;

        [RelayCommand] private Task OpenAllAsync() => RunAllAsync(Communication.Fixtures.FixtureCommand.OpenAll);
        [RelayCommand] private Task CloseAllAsync() => RunAllAsync(Communication.Fixtures.FixtureCommand.CloseAll);

        // OPEN ALL / CLOSE ALL không có số khuôn: gửi qua khuôn đang chọn (nếu đã nối), không thì khuôn đã nối đầu tiên
        private Task RunAllAsync(Communication.Fixtures.FixtureCommand command)
        {
            var target = SelectedDevice is { IsConnected: true } ? SelectedDevice : Devices.FirstOrDefault(d => d.IsConnected);
            if (target == null)
            {
                _owner.StatusMessage = "Chưa có khuôn nào kết nối để gửi lệnh";
                return Task.CompletedTask;
            }
            return target.RunFixtureCommandAsync(command);
        }

        /// <summary>Hiện địa chỉ IPv4 của máy này (khuôn test cần gửi về đúng địa chỉ và "Cổng nhận").</summary>
        [RelayCommand]
        private void ScanIp()
        {
            var addresses = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        addresses.Add($"{ua.Address}  ({nic.Name})");
            }

            _dialogs.ShowInfo(addresses.Count == 0
                ? "Không tìm thấy địa chỉ IPv4 nào đang hoạt động."
                : "Địa chỉ IP của máy này:\n" + string.Join("\n", addresses));
        }
    }
}
