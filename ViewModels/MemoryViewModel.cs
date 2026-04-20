using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetGui.Services;
using System.Collections.ObjectModel;
using System.Text;

namespace NetGui.ViewModels;

public partial class MemoryRow : ObservableObject
{
    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HexData { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AsciiData { get; set; } = string.Empty;
}

public partial class MemoryViewModel : ObservableObject
{
    private readonly SC64Device _device;
    private readonly IDispatcherTimer _timer;

    public MemoryViewModel(SC64Device device)
    {
        _device = device;
        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (s, e) => { if (IsAutoRefresh) Refresh(); };
    }

    [ObservableProperty]
    public partial string AddressInput { get; set; } = "00000000";

    [ObservableProperty]
    public partial uint BaseAddress { get; set; } = 0x00000000;

    [ObservableProperty]
    public partial bool IsAutoRefresh { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<MemoryRow> MemoryRows { get; } = new();

    public ObservableCollection<string> Presets { get; } = new()
    {
        "SDRAM (0x00000000)",
        "Flash (0x04000000)",
        "Data Buffer (0x05000000)",
        "EEPROM (0x05002000)",
        "ROM (0x10000000)",
        "Registers (0x1FFF0000)"
    };

    [ObservableProperty]
    public partial string? SelectedPreset { get; set; }

    partial void OnSelectedPresetChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        
        // Extract hex value from preset string like "SDRAM (0x00000000)"
        var match = System.Text.RegularExpressions.Regex.Match(value, @"0x([0-9A-Fa-f]+)");
        if (match.Success)
        {
            AddressInput = match.Groups[1].Value;
            JumpToAddress();
        }
    }

    [RelayCommand]
    private void JumpToAddress()
    {
        if (uint.TryParse(AddressInput, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
        {
            BaseAddress = addr & ~0xFu; // Align to 16 bytes
            Refresh();
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        if (!_device.IsConnected || IsBusy) return;

        Task.Run(() =>
        {
            IsBusy = true;
            try
            {
                const uint viewSize = 256;
                byte[]? data = _device.MemoryRead(BaseAddress, viewSize);
                
                if (data != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MemoryRows.Clear();
                        for (int i = 0; i < data.Length; i += 16)
                        {
                            var row = new MemoryRow
                            {
                                Address = $"{(BaseAddress + i):X8}"
                            };

                            var hexBuilder = new StringBuilder();
                            var asciiBuilder = new StringBuilder();

                            for (int j = 0; j < 16; j++)
                            {
                                if (i + j < data.Length)
                                {
                                    byte b = data[i + j];
                                    hexBuilder.Append($"{b:X2} ");
                                    asciiBuilder.Append((b >= 32 && b <= 126) ? (char)b : '.');
                                }
                            }

                            row.HexData = hexBuilder.ToString().Trim();
                            row.AsciiData = asciiBuilder.ToString();
                            MemoryRows.Add(row);
                        }
                    });
                }
            }
            finally
            {
                IsBusy = false;
            }
        });
    }

    [RelayCommand]
    private void PageUp()
    {
        if (BaseAddress >= 256) BaseAddress -= 256;
        else BaseAddress = 0;
        AddressInput = BaseAddress.ToString("X8");
        Refresh();
    }

    [RelayCommand]
    private void PageDown()
    {
        BaseAddress += 256;
        AddressInput = BaseAddress.ToString("X8");
        Refresh();
    }
}
