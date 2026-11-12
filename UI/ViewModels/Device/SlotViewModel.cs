using Communication.Fixtures;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Text;

namespace UI.ViewModels.Device
{
    /// <summary>Một slot (ô test hàng) trong khuôn: trạng thái, thanh ghi PLC của slot, có bản hay không.</summary>
    public partial class SlotViewModel : ViewModelBase
    {
        /// <summary>Các kết quả chọn được để gửi PLC.</summary>
        public static IReadOnlyList<SlotState> Choices { get; } = new[]
        {
            SlotState.Pass, SlotState.Fail, SlotState.Repair, SlotState.Nan, SlotState.Error, SlotState.Busy, SlotState.Empty
        };

        public SlotViewModel(int number) => Number = number;

        public int Number { get; }
        public string Title => $"Slot {Number}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        private SlotState _state = SlotState.Unknown;

        public string StateText => State == SlotState.Unknown ? "—" : State.ToString().ToUpperInvariant();

        /// <summary>Slot có bản không theo CHECKSENSOR: "Có bản", "Trống", hoặc rỗng nếu chưa kiểm.</summary>
        [ObservableProperty] private string _sampleText = "";

        private string _register = "";

        /// <summary>Thanh ghi PLC của slot (sửa được trong ô). Đặt từ giao diện thì báo ra ngoài để ghi vào cấu hình.</summary>
        public string Register
        {
            get => _register;
            set
            {
                value = (value ?? "").Trim();
                if (value == _register) return;
                _register = value;
                OnPropertyChanged();
                RegisterEdited?.Invoke(this);
            }
        }

        /// <summary>Cập nhật thanh ghi từ cấu hình, không báo ngược lại.</summary>
        public void SetRegisterQuiet(string value)
        {
            _register = value;
            OnPropertyChanged(nameof(Register));
        }

        public event Action<SlotViewModel>? RegisterEdited;
        public event Action<SlotViewModel>? SendRequested;

        /// <summary>Gửi kết quả đang chọn của slot này vào thanh ghi PLC của nó.</summary>
        [RelayCommand]
        private void Send() => SendRequested?.Invoke(this);
    }
}
