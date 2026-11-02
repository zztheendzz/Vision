using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Communication.Configuration
{
    public class DeviceConfigValidator
    {
        public ObservableCollection<DeviceConfig> Devices { get; } = new();
        private string? Validate()
        {
            foreach (var d in Devices)
            {
                if (string.IsNullOrWhiteSpace(d.Name))
                    return "Có thiết bị chưa đặt tên.";
                if (string.IsNullOrWhiteSpace(d.IpAddress))
                    return $"Thiết bị '{d.Name}' chưa có địa chỉ IP.";
                if (d.Port < 1 || d.Port > 65535)
                    return $"Thiết bị '{d.Name}': cổng phải từ 1 đến 65535.";
                if (d.TimeoutMs < 100)
                    return $"Thiết bị '{d.Name}': thời gian chờ tối thiểu 100 ms.";
            }

            var duplicate = Devices
                .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                return $"Có hai thiết bị trùng tên '{duplicate.Key}'.";

            return null;
        }
    }
}
