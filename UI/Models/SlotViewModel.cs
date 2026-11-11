using Communication.Fixtures;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using UI.ViewModels;

namespace UI.Models
{
    /// <summary>Một slot (ô test hàng) trong khuôn: số thứ tự và trạng thái hiện tại.</summary>
    public partial class SlotViewModel : ViewModelBase
    {
        public SlotViewModel(int number) => Number = number;

        public int Number { get; }
        public string Title => $"Slot {Number}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        private SlotState _state = SlotState.Unknown;

        public string StateText => State == SlotState.Unknown ? "—" : State.ToString().ToUpperInvariant();
    }
}
