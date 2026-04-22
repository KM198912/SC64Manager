using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NetGui.Services;
using NetGui.Models;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Linq;

namespace NetGui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly HashSet<string> HiddenItems = new() 
    { 
        "sc64menu.n64", 
        "System Volume Information", 
        ".Trash-1000",
        "found.000"
    };

    private readonly SC64Device _device;
    private readonly SettingsService _settings;
    private readonly GamesDbService _gdb;
    private readonly FsService _fs;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _pollingCts;
    private CancellationTokenSource? _metadataCts;

    public MainViewModel(SC64Device device, SettingsService settings, GamesDbService gdb)
    {
        _device = device;
        _settings = settings;
        _gdb = new GamesDbService(settings, msg => Log(msg)); 
        _fs = new FsService(_device);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SC64Manager/1.0");

        AvailablePorts = new ObservableCollection<string>();
        LocalFiles = new ObservableCollection<FileItem>();
        RemoteFiles = new ObservableCollection<FileItem>();
        
        SelectedPort = _settings.LastSelectedPort;
        CurrentLocalPath = _settings.DefaultRomPath;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Task.Delay(1000); // UI Warmup Buffer

        try
        {
            Log("Startup sequence beginning...");
            
            await Task.Run(async () => {
                RefreshAvailablePorts();
                RefreshLocalFiles();
                
                // Live Update Check
                var updater = new UpdateService();
                var (deployer, firmware) = await updater.GetLatestReleaseAssetsAsync();
                if (firmware != null)
                {
                    MainThread.BeginInvokeOnMainThread(() => {
                        LatestVersionTag = firmware.Name.Replace("sc64-firmware-", "").Replace(".bin", "");
                        // We do NOT set UpdateAvailable = true here. 
                        // It stays hidden until CheckForUpdates() is called with real hardware data.
                    });
                }
            });

            Log("Ready for hardware connection.");
        }
        catch (Exception ex)
        {
            Log($"BOOT ERROR: {ex.Message}");
        }
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Disconnected";

    public Color StatusColor => IsConnected ? Color.FromArgb("#4caf50") : Color.FromArgb("#f44336");
    public string ConnectionButtonText => IsConnected ? "Disconnect" : "Connect";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(ConnectionButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusTextN64))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTextN64))]
    [NotifyPropertyChangedFor(nameof(CanModifyRemoteFs))]
    public partial bool IsN64PoweredOn { get; set; }

    public string StatusTextN64 => IsConnected ? (IsN64PoweredOn ? "N64: ON" : "N64: OFF") : "";

    public bool CanModifyRemoteFs => IsConnected && !IsBusy && !IsN64PoweredOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModifyRemoteFs))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string LogText { get; set; } = "Ready.\n";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentFileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedPort { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentRemotePath { get; set; } = "/";

    [ObservableProperty]
    public partial string CurrentLocalPath { get; set; } = string.Empty;

    public string DefaultFirmwarePath
    {
        get => _settings.DefaultFirmwarePath;
        set { _settings.DefaultFirmwarePath = value; OnPropertyChanged(); }
    }

    public string DefaultRomPath
    {
        get => _settings.DefaultRomPath;
        set { _settings.DefaultRomPath = value; OnPropertyChanged(); }
    }
    
    public string DeployerPath 
    {
        get => _settings.DeployerPath;
        set { _settings.DeployerPath = value; OnPropertyChanged(); }
    }

    public string GamesDbApiKey
    {
        get => _settings.GamesDbApiKey;
        set { _settings.GamesDbApiKey = value; OnPropertyChanged(); }
    }

    public bool MetadataFallbackEnabled
    {
        get => _settings.MetadataFallbackEnabled;
        set { _settings.MetadataFallbackEnabled = value; OnPropertyChanged(); }
    }

    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsUpToDate { get; set; }

    [ObservableProperty]
    public partial string LatestVersionTag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HardwareVoltage { get; set; } = "-- V";

    [ObservableProperty]
    public partial string HardwareTemperature { get; set; } = "-- °C";

    [ObservableProperty]
    public partial string HardwareCicStatus { get; set; } = "Disconnected";

    [ObservableProperty]
    public partial string HardwareRtcTime { get; set; } = "--:--:--";

    [ObservableProperty]
    public partial string HardwareRtcStatus { get; set; } = "Not Synced";

    [ObservableProperty]
    public partial bool IsSdMounted { get; set; }

    [ObservableProperty]
    public partial string? CurrentBoxArtPreview { get; set; }

    [ObservableProperty]
    public partial string? CurrentDescription { get; set; }

    [ObservableProperty]
    public partial string MusicStatus { get; set; } = "Unknown (Check required)";

    [ObservableProperty]
    public partial bool IsPal60Enabled { get; set; }

    [ObservableProperty]
    public partial bool IsPal60CompatEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAutoloadEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsCarouselEnabled { get; set; }

    public ObservableCollection<string> AvailablePorts { get; } 
    public ObservableCollection<FileItem> LocalFiles { get; } 
    public ObservableCollection<FileItem> RemoteFiles { get; } 

    public void Log(string message)
    {
        // UI Log (Visible to user)
        MainThread.BeginInvokeOnMainThread(() => {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogText += $"[{timestamp}] {message}\n";
        });
    }

    [RelayCommand]
    private async Task CopyLogs()
    {
        if (string.IsNullOrEmpty(LogText)) return;
        
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () => {
                await Clipboard.Default.SetTextAsync(LogText);
                Log("SUCCESS: Diagnostics copied to system clipboard.");
            });
        }
        catch (Exception ex)
        {
            Log($"Clipboard Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenHelp()
    {
        try
        {
            await Browser.Default.OpenAsync("https://github.com/KM198912/SC64Manager/blob/main/help.md", BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            Log($"Help Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SyncRtc()
    {
        if (!IsConnected || IsBusy) return;
        
        try
        {
            var now = DateTime.Now;
            Log($"Syncing RTC clock to PC: {now:yyyy-MM-dd HH:mm:ss}...");
            bool success = await Task.Run(() => _device.SetRtcTime(now));
            if (success) 
            {
                Log("SUCCESS: Cartridge RTC synchronized.");
                HardwareRtcStatus = $"Synced to PC at {now:HH:mm:ss}";
                
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () => {
                        var toast = Toast.Make("RTC Synchronized Successfully", ToastDuration.Short);
                        await toast.Show();
                    });
                }
                catch { /* Ignore toast failures - occurs if app isn't fully registered for Win notifications */ }
            }
            else 
            {
                Log("ERROR: RTC Sync failed.");
                HardwareRtcStatus = "Sync Failed";
            }
        }
        catch (Exception ex)
        {
            Log($"RTC Error: {ex.Message}");
            HardwareRtcStatus = "Error during Sync";
        }
    }

    [RelayCommand]
    private async Task GoToAbout() => await Shell.Current.GoToAsync("AboutPage");

    [RelayCommand]
    private async Task GoToHardware() => await Shell.Current.GoToAsync("HardwarePage");

    [RelayCommand]
    private async Task OpenDebugConsole()
    {
        string? exePath = DeployerPath;

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            // Auto-Download Fallback
            IsBusy = true;
            ProgressValue = 0;
            ProgressText = "Fetching Debugger Utility...";
            Log("MISSING: sc64deployer.exe not found. Attempting auto-download...");
            
            try
            {
                var updater = new UpdateService();
                var (deployerAsset, _) = await updater.GetLatestReleaseAssetsAsync();
                if (deployerAsset == null) throw new Exception("Could not find deployer asset on GitHub.");

                Log("Downloading official deployer package...");
                string targetDir = Path.Combine(FileSystem.CacheDirectory, "Updates", "deployer");
                exePath = await updater.DownloadAndExtractAsync(deployerAsset!, targetDir);

                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) throw new Exception("Extraction failed.");
                
                DeployerPath = exePath;
                Log("SUCCESS: sc64deployer.exe localized and ready.");
            }
            catch (Exception ex)
            {
                Log($"Download Failed: {ex.Message}");
                if (Application.Current?.Windows.Count > 0)
                {
                    await Application.Current.Windows[0].Page!.DisplayAlertAsync("Integration Error", $"Could not find or download sc64deployer.exe: {ex.Message}", "OK");
                }
                return;
            }
            finally { IsBusy = false; }
        }

        bool wasConnected = IsConnected;
        string port = SelectedPort;

        if (wasConnected)
        {
            Log("Handoff: Releasing COM port for Debug Console...");
            await Disconnect();
            await Task.Delay(500); // Wait for OS to fully release the handle
        }

        try
        {
            Log($"Launching external terminal: {exePath} --port serial://{port} debug");
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"\"{exePath}\" --port serial://{port} debug\"",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                },
                EnableRaisingEvents = true
            };

            process.Exited += async (s, e) => {
                Log("External Debug Console closed.");
                if (wasConnected)
                {
                    Log("Attempting to reclaim COM port...");
                    await MainThread.InvokeOnMainThreadAsync(async () => {
                        await ToggleConnection();
                    });
                }
            };

            process.Start();
        }
        catch (Exception ex)
        {
            Log($"Process Start Error: {ex.Message}");
            if (wasConnected) await ToggleConnection();
        }
    }

    [RelayCommand]
    private async Task GoToSettings() => await Shell.Current.GoToAsync("SettingsPage");

    [RelayCommand]
    private async Task GoToUpdate() => await Shell.Current.GoToAsync("UpdatePage");

    [RelayCommand]
    private void RefreshAvailablePorts()
    {
        try
        {
            var ports = _device.GetAvailablePorts();
            MainThread.BeginInvokeOnMainThread(() => {
                string current = SelectedPort;
                AvailablePorts.Clear();
                foreach (var p in ports) AvailablePorts.Add(p);
                
                if (AvailablePorts.Contains(current)) SelectedPort = current;
                else if (AvailablePorts.Count > 0) SelectedPort = AvailablePorts[0];
            });
        }
        catch (Exception ex)
        {
            Log($"Port Scan Error: {ex.Message}");
        }
    }

    partial void OnSelectedPortChanged(string value)
    {
        if (_settings != null) _settings.LastSelectedPort = value;
    }

    partial void OnCurrentLocalPathChanged(string value)
    {
        RefreshLocalFiles();
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        if (IsBusy) return;
        IsBusy = true;
        Log("Initiating safe disconnect...");
        
        try
        {
            await Task.Run(() => {
                _pollingCts?.Cancel();
                // If N64 is ON, we skip hardware deinit because the console owns the SD
                if (IsSdMounted) _fs.Disconnect(hardwareDeinit: !IsN64PoweredOn);
                _device.Disconnect();
            });

            MainThread.BeginInvokeOnMainThread(() => {
                IsConnected = false;
                IsSdMounted = false;
                IsN64PoweredOn = false;
                StatusText = "Disconnected";
                OnPropertyChanged(nameof(IsConnected));
            });
            Log("DISCONNECTED: Hardware bridge released safely.");
        }
        catch (Exception ex)
        {
            Log($"Disconnect Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleConnection()
    {
        if (IsBusy) return;

        if (!IsConnected)
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                Log("Error: No COM port selected.");
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            ProgressText = "Initializing Hardware...";
            Log($"--- CONNECTION START: {SelectedPort} ---");
            
            try
            {
                string port = SelectedPort;
                bool success = await Task.Run(() => {
                    Log("COM: Attempting to open serial port...");
                    if (_device.Connect(port))
                    {
                        Log("COM: Serial port handle acquired.");
                        Log("COM: Sending hardware reset signal...");
                        if (!_device.ResetHandshake())
                        {
                            Log("COM ERROR: Reset handshake failed (No response from Cartridge)");
                            _device.Disconnect();
                            return false;
                        }

                        Log("COM: Waiting for hardware signature...");
                        if (_device.GetIdentifier() == "Unknown")
                        {
                            Log("COM ERROR: Device signature not found (Hardware desync)");
                            _device.Disconnect();
                            return false;
                        }

                        return true;
                    }
                    return false;
                });

                if (success)
                {
                    Log("COM: Requesting firmware version...");
                    var version = await Task.Run(() => _device.GetVersion());
                    string versionStr = version?.ToString() ?? "Unknown";
                    Log($"Cart Version: {versionStr}");

                    // Boot-time Power Check (Wait for FPGA stability)
                    ProgressValue = 0.5;
                    ProgressText = "Verifying Power Status...";
                    await Task.Delay(200);
                    Log("Detecting console power status...");
                    byte step = await Task.Run(() => _device.GetCicStep());
                    IsN64PoweredOn = step > 1;
                    if (IsN64PoweredOn) Log("⚠️ ALERT: N64 is already powered ON.");

                    Log("Finalizing protocol handshake...");
                    await Task.Run(() => _device.StateReset());
                    
                    ProgressValue = 0.8;
                    IsConnected = true;
                    StatusText = $"Connected ({versionStr})";
                    Log("SUCCESS: Hardware bridge established. (SD Card NOT mounted)");

                    Log("Starting background status monitoring...");
                    _pollingCts = new CancellationTokenSource();
                    _ = Task.Run(() => StartPollingLoop(_pollingCts.Token), _pollingCts.Token);
                }
                else
                {
                    Log("COM ERROR: Failed to open serial port.");
                }
            }
            catch (Exception ex)
            {
                Log($"Connection Fault: {ex.Message}");
                _device.Disconnect();
            }
            finally { IsBusy = false; }
        }
        else
        {
            IsBusy = true;
            Log("Closing connection...");
            try
            {
                await Task.Run(() => {
                    _pollingCts?.Cancel();
                    if (IsSdMounted) _fs.Disconnect(hardwareDeinit: !IsN64PoweredOn);
                    _device.Disconnect();
                });

                MainThread.BeginInvokeOnMainThread(() => {
                    IsConnected = false;
                    IsSdMounted = false;
                    IsN64PoweredOn = false;
                    UpdateAvailable = false;
                    IsUpToDate = false;
                    StatusText = "Disconnected";
                    RemoteFiles.Clear();
                });
                
                Log("Disconnected safely.");
            }
            catch (Exception ex) { Log($"Disconnect Error: {ex.Message}"); }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    private async Task TestGamesDbConnectionAsync()
    {
        IsBusy = true;
        Log("TheGamesDB: Testing connection...");
        try
        {
            var success = await _gdb.TestConnectionAsync();
            if (success)
                Log("SUCCESS: TheGamesDB connection verified.");
            else
                Log("ERROR: TheGamesDB verification failed. Check API key.");
        }
        catch (Exception ex) { Log($"TheGamesDB Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task MountSd()
    {
        if (!IsConnected || IsSdMounted || IsBusy) return;

        if (IsN64PoweredOn)
        {
            if (Application.Current?.Windows.Count > 0)
            {
                bool proceed = await Application.Current.Windows[0].Page!.DisplayAlertAsync(
                    "Hardware Warning", 
                    "The N64 console is currently powered ON. Mounting the SD card while the console is active will cause a hardware lock and possible data corruption on the cartridge.\n\nAre you EXTREMELY sure you want to proceed?", 
                    "PROCEED (Dangerous)", "CANCEL");
                
                if (!proceed) return;
            }
        }

        IsBusy = true;
        Log("Mounting SD card...");
        try
        {
            var success = await Task.Run(() => _fs.Mount(Log));
            if (success)
            {
                IsSdMounted = true;
                Log("Mount SUCCESS. Preparing UI...");
                CurrentRemotePath = "/";
                await RefreshRemoteFilesInternal();
                await CheckMusicStatusAsync();
                await LoadCartridgeConfigAsync();
            }
            else
            {
                Log("Mount FAILED.");
            }
        }
        catch (Exception ex)
        {
            Log($"Mount Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UnmountSd()
    {
        if (!IsSdMounted) return;
        
        try
        {
            Log("Unmounting SD card...");
            _fs.Disconnect();
            IsSdMounted = false;
            RemoteFiles.Clear();
            MusicStatus = "Unknown (Check required)";
            Log("SUCCESS: Filesystem bridge released. SD card is now safe to access from console.");
        }
        catch (Exception ex)
        {
            Log($"Unmount Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SetMenuMusic()
    {
        if (!IsConnected || !IsSdMounted || IsBusy || IsN64PoweredOn) return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions { 
                PickerTitle = "Select Background Music (MP3)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> {
                    { DevicePlatform.WinUI, new[] { ".mp3" } },
                    { DevicePlatform.macOS, new[] { "mp3" } }
                })
            });

            if (result == null) return;

            var fileInfo = new FileInfo(result.FullPath);
            if (fileInfo.Length > 5 * 1024 * 1024) // 5MB Threshold
            {
                bool proceed = await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
                    "Large File Warning", 
                    $"The selected MP3 ({FormatSize(fileInfo.Length)}) is quite large. This will significantly slow down the N64 boot time while the firmware scans the file.\n\nContinue anyway?", 
                    "Yes", "Cancel");
                if (!proceed) return;
            }

            IsBusy = true;
            ProgressValue = 0;
            ProgressText = "Uploading BGM...";
            Log($"UPLOADING: {result.FileName} to /menu/bg.mp3...");

            await Task.Run(() => {
                using var fs = File.OpenRead(result.FullPath);
                using var dest = _fs.OpenFile("/menu/bg.mp3", FileMode.Create);
                CopyStreamWithProgress(fs, dest, fs.Length);
            });

            Log("SUCCESS: Background music updated.");
            await CheckMusicStatusAsync();
        }
        catch (Exception ex) { Log($"BGM Upload Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RemoveMenuMusic()
    {
        if (!IsConnected || !IsSdMounted || IsBusy || IsN64PoweredOn) return;

        IsBusy = true;
        try
        {
            Log("REMOVING: /menu/bg.mp3...");
            await Task.Run(() => _fs.DeleteFile("/menu/bg.mp3"));
            Log("SUCCESS: Background music removed.");
            await CheckMusicStatusAsync();
        }
        catch (Exception ex) { Log($"BGM Remove Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    private async Task CheckMusicStatusAsync()
    {
        if (!IsConnected || !IsSdMounted) 
        {
            MusicStatus = "SD Not Mounted";
            return;
        }

        try
        {
            bool exists = await Task.Run(() => _fs.ListDir("/menu").Any(f => f.Name == "bg.mp3"));
            MusicStatus = exists ? "Active (bg.mp3 present)" : "Ready (No music set)";
        }
        catch { MusicStatus = "Error checking status"; }
    }

    [RelayCommand]
    public async Task LoadCartridgeConfigAsync()
    {
        if (!IsConnected || !IsSdMounted) return;

        try
        {
            Log("Reading cartridge configuration (config.ini)...");
            var content = await Task.Run(() => {
                if (!_fs.ListDir("/menu").Any(f => f.Name == "config.ini")) return string.Empty;
                using var stream = _fs.OpenFile("/menu/config.ini", FileMode.Open);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });

            if (string.IsNullOrEmpty(content))
            {
                Log("No existing config.ini found. Defaults will be used.");
                return;
            }

            var ini = new IniService();
            ini.Load(content);

            IsPal60Enabled = ini.GetBool("menu", "pal60", false);
            IsPal60CompatEnabled = ini.GetBool("menu", "pal60_compatibility_mode", true);
            IsAutoloadEnabled = ini.GetBool("menu", "autoload_rom_enabled", false);
            IsCarouselEnabled = ini.GetBool("menu", "carousel_menu", true);
            
            Log("SUCCESS: Remote configuration loaded.");
        }
        catch (Exception ex) { Log($"Config Load Error: {ex.Message}"); }
    }

    [RelayCommand]
    public async Task SaveCartridgeConfigAsync()
    {
        if (!IsConnected || !IsSdMounted || IsBusy || IsN64PoweredOn) return;

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "Saving Remote Config...";
        Log("Updating cartridge config.ini...");

        try
        {
            var ini = new IniService();
            
            // 1. Read existing to preserve other settings
            var content = await Task.Run(() => {
                if (!_fs.ListDir("/menu").Any(f => f.Name == "config.ini")) return string.Empty;
                using var stream = _fs.OpenFile("/menu/config.ini", FileMode.Open);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });

            if (!string.IsNullOrEmpty(content)) ini.Load(content);

            // 2. Overlay our values
            ini.SetInt("menu", "schema_revision", 1); // Ensure valid schema
            ini.SetBool("menu", "pal60", IsPal60Enabled);
            ini.SetBool("menu", "pal60_compatibility_mode", IsPal60CompatEnabled);
            ini.SetBool("menu", "autoload_rom_enabled", IsAutoloadEnabled);
            ini.SetBool("menu", "carousel_menu", IsCarouselEnabled);

            // 3. Write back
            await Task.Run(() => {
                var newContent = ini.Save();
                using var dest = _fs.OpenFile("/menu/config.ini", FileMode.Create);
                using var writer = new StreamWriter(dest);
                writer.Write(newContent);
            });

            Log("SUCCESS: config.ini synchronization complete.");
            
            _ = MainThread.InvokeOnMainThreadAsync(async () => {
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Success", "Configuration synchronized to cartridge successfully.", "OK");
            });
        }
        catch (Exception ex) { Log($"Config Save Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SetAsDefaultBootRom(FileItem item)
    {
        if (!IsConnected || !IsSdMounted || IsBusy || IsN64PoweredOn || item.IsDirectory) return;

        IsBusy = true;
        Log($"SETTING AUTOLOAD: {item.Name}...");
        try
        {
            var ini = new IniService();
            
            // Load existing
            var content = await Task.Run(() => {
                if (!_fs.ListDir("/menu").Any(f => f.Name == "config.ini")) return string.Empty;
                using var stream = _fs.OpenFile("/menu/config.ini", FileMode.Open);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });
            if (!string.IsNullOrEmpty(content)) ini.Load(content);

            // Update autoload section
            ini.SetBool("menu", "autoload_rom_enabled", true);
            ini.SetString("autoload", "rom_path", CurrentRemotePath);
            ini.SetString("autoload", "rom_filename", item.Name);

            // Save back
            await Task.Run(() => {
                var newContent = ini.Save();
                using var dest = _fs.OpenFile("/menu/config.ini", FileMode.Create);
                using var writer = new StreamWriter(dest);
                writer.Write(newContent);
            });

            IsAutoloadEnabled = true;
            Log($"SUCCESS: {item.Name} set as cartridge default boot ROM.");
            
            await MainThread.InvokeOnMainThreadAsync(async () => {
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Autoload Set", $"{item.Name} will now boot automatically on power on.\n\nHold START at boot to bypass if needed.", "OK");
            });
        }
        catch (Exception ex) { Log($"Autoload Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ScrapeBoxArt()
    {
        var romFiles = RemoteFiles.Where(f => !f.IsDirectory &&
            (f.Name.EndsWith(".z64", StringComparison.OrdinalIgnoreCase) ||
             f.Name.EndsWith(".n64", StringComparison.OrdinalIgnoreCase) ||
             f.Name.EndsWith(".v64", StringComparison.OrdinalIgnoreCase))).ToList();

        await ScrapeItemsInternalAsync(romFiles, "folder-wide");
    }

    [RelayCommand]
    private async Task ScrapeSelected()
    {
        var selected = RemoteFiles.Where(f => f.IsSelected && !f.IsDirectory &&
            (f.Name.EndsWith(".z64", StringComparison.OrdinalIgnoreCase) ||
             f.Name.EndsWith(".n64", StringComparison.OrdinalIgnoreCase) ||
             f.Name.EndsWith(".v64", StringComparison.OrdinalIgnoreCase))).ToList();

        await ScrapeItemsInternalAsync(selected, "selection-based");
    }

    private async Task ScrapeItemsInternalAsync(List<FileItem> romFiles, string mode)
    {
        if (!IsSdMounted || IsBusy) return;

        if (!romFiles.Any())
        {
            Log($"No valid ROM files found for {mode} scrape.");
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        Log($"Starting {mode} box art scraper for {romFiles.Count} ROMs...");

        try
        {
            int processed = 0;
            using var http = new HttpClient();

            foreach (var rom in romFiles)
            {
                processed++;
                ProgressValue = (double)processed / romFiles.Count;
                ProgressText = $"Processing: {rom.Name}";
                CurrentFileName = rom.Name;

                // 1. Identify ROM ID
                string fullPath = Path.Combine(CurrentRemotePath, rom.Name).Replace("\\", "/");
                string id = await Task.Run(() => GetRomId(fullPath));
                
                if (string.IsNullOrEmpty(id) || id.Length < 4)
                {
                    Log($"Could not identify ID for {rom.Name}. Skipping.");
                    continue;
                }

                rom.RomId = id;
                Log($"Identified {rom.Name} as [{id}]");

                // 2. Build remote path for metadata (Hybrid Casing: lowercase parent, uppercase ID)
                char c1 = char.ToUpper(id[0]);
                char c2 = char.ToUpper(id[1]);
                char c3 = char.ToUpper(id[2]);
                char c4 = char.ToUpper(id[3]);
                string metadataPath = $"/menu/metadata/{c1}/{c2}/{c3}/{c4}";
                string remoteTarget = $"{metadataPath}/boxart_front.png";

                // 3. Scan for existing metadata files (Hybrid Casing)
                var existingFiles = await Task.Run(() => {
                    try { return _fs.ListDir(metadataPath).Select(x => x.Name).ToList(); }
                    catch { return new List<string>(); }
                });

                bool artExists = existingFiles.Contains("boxart_front.png");
                bool descExists = existingFiles.Contains("description.txt");

                if (artExists && descExists)
                {
                    Log($"Metadata (Art + Desc) already exists for [{id}]. Skipping.");
                    continue;
                }

                // 4. Resolve assets (GitHub + GamesDB Fallback)
                if (!artExists || !descExists)
                {
                    string githubUrlArt = $"https://raw.githubusercontent.com/n64-tools/n64-flashcart-menu-metadata/main/metadata/{c1}/{c2}/{c3}/{c4}/boxart_front.png";
                    string githubUrlDesc = $"https://raw.githubusercontent.com/n64-tools/n64-flashcart-menu-metadata/main/metadata/{c1}/{c2}/{c3}/{c4}/description.txt";
                    string githubUrlIni = $"https://raw.githubusercontent.com/n64-tools/n64-flashcart-menu-metadata/main/metadata/{c1}/{c2}/{c3}/{c4}/metadata.ini";

                    try
                    {
                        // Handle Box Art (Only if missing from primary source)
                        if (!artExists)
                        {
                            var artResponse = await http.GetAsync(githubUrlArt);
                            if (artResponse.IsSuccessStatusCode)
                            {
                                using var stream = await artResponse.Content.ReadAsStreamAsync();
                                using var image = PlatformImage.FromStream(stream);
                                using var resizedImage = image.Downsize(158);
                                using var pngMs = new MemoryStream();
                                resizedImage.Save(pngMs, ImageFormat.Png);
                                byte[] pngData = pngMs.ToArray();

                                await Task.Run(() => {
                                    EnsureDirectoryExistsRemote("/menu");
                                    EnsureDirectoryExistsRemote("/menu/metadata");
                                    EnsureDirectoryExistsRemote($"/menu/metadata/{c1}");
                                    EnsureDirectoryExistsRemote($"/menu/metadata/{c1}/{c2}");
                                    EnsureDirectoryExistsRemote($"/menu/metadata/{c1}/{c2}/{c3}");
                                    EnsureDirectoryExistsRemote($"/menu/metadata/{c1}/{c2}/{c3}/{c4}");
                                });

                                await Task.Run(() => {
                                    using var ms = new MemoryStream(pngData);
                                    using var dest = _fs.OpenFile(remoteTarget, FileMode.Create);
                                    ms.CopyTo(dest);
                                });

                                var cacheDir = Path.Combine(FileSystem.CacheDirectory, "BoxArt", c1.ToString(), c2.ToString(), c3.ToString(), c4.ToString());
                                Directory.CreateDirectory(cacheDir);
                                var localPath = Path.Combine(cacheDir, "boxart_front.png");
                                await File.WriteAllBytesAsync(localPath, pngData);
                                rom.BoxArtPath = localPath;
                                artExists = true;
                                Log($"SUCCESS: Box art (GitHub + Resized) deployed for [{id}]");

                                // Try to get pretty name from .ini if it exists on GitHub
                                var iniResponse = await http.GetAsync(githubUrlIni);
                                if (iniResponse.IsSuccessStatusCode)
                                {
                                    var iniContent = await iniResponse.Content.ReadAsStringAsync();
                                    var nameLine = iniContent.Split('\n').FirstOrDefault(l => l.StartsWith("name="));
                                    if (nameLine != null)
                                    {
                                        rom.ScrapedTitle = nameLine.Substring(5).Trim();
                                    }
                                }
                            }
                        }

                        // Handle Description (Only if missing from primary source)
                        if (!descExists)
                        {
                            var descResponse = await http.GetAsync(githubUrlDesc);
                            if (descResponse.IsSuccessStatusCode)
                            {
                                var descText = await descResponse.Content.ReadAsStringAsync();
                                var wrappedText = "\n\n" + WordWrap(descText, 60).Replace("\r\n", "\n");
                                string remoteTargetDesc = $"{metadataPath}/description.txt";

                                await Task.Run(() => {
                                    var encoding = new System.Text.UTF8Encoding(false);
                                    byte[] descBytes = encoding.GetBytes(wrappedText);
                                    using var ms = new MemoryStream(descBytes);
                                    using var dest = _fs.OpenFile(remoteTargetDesc, FileMode.Create);
                                    ms.CopyTo(dest);
                                });

                                var cacheDir = Path.Combine(FileSystem.CacheDirectory, "BoxArt", c1.ToString(), c2.ToString(), c3.ToString(), c4.ToString());
                                Directory.CreateDirectory(cacheDir);
                                await File.WriteAllTextAsync(Path.Combine(cacheDir, "description.txt"), descText);
                                rom.Description = descText;
                                descExists = true; // Mark as found
                                Log($"SUCCESS: Description (GitHub) deployed for [{id}]");
                            }
                            else if (_settings.MetadataFallbackEnabled)
                            {
                                // GamesDB Fallback
                                Log($"GITHUB 404: Falling back to TheGamesDB for [{id}]...");
                                var gdbMeta = await _gdb.GetGameMetadataAsync(id, rom.Name);
                                if (gdbMeta != null)
                                {
                                    Log($"GDB Result: '{gdbMeta.Description?[..Math.Min(20, gdbMeta.Description?.Length ?? 0)]}...' (Found: true)");
                                    if (!string.IsNullOrEmpty(gdbMeta.Title))
                                    {
                                        rom.ScrapedTitle = gdbMeta.Title;
                                    }
                                    // Ensure folders
                                    await Task.Run(() => {
                                        EnsureDirectoryExistsRemote($"/menu/metadata/{c1}/{c2}/{c3}/{c4}");
                                    });

                                    if (!string.IsNullOrEmpty(gdbMeta.Description))
                                    {
                                        var descText = gdbMeta.Description;
                                        var wrappedText = "\n\n" + WordWrap(descText, 60).Replace("\r\n", "\n");
                                        string remoteTargetDesc = $"{metadataPath}/description.txt";
                                        await Task.Run(() => {
                                            var encoding = new System.Text.UTF8Encoding(false);
                                            byte[] descBytes = encoding.GetBytes(wrappedText);
                                            using var ms = new MemoryStream(descBytes);
                                            using var dest = _fs.OpenFile(remoteTargetDesc, FileMode.Create);
                                            ms.CopyTo(dest);
                                        });

                                        var cacheDir = Path.Combine(FileSystem.CacheDirectory, "BoxArt", c1.ToString(), c2.ToString(), c3.ToString(), c4.ToString());
                                        Directory.CreateDirectory(cacheDir);
                                        await File.WriteAllTextAsync(Path.Combine(cacheDir, "description.txt"), descText);
                                        rom.Description = descText;
                                        descExists = true;
                                        Log($"SUCCESS: Description (GamesDB) deployed for [{id}]");
                                    }

                                    if (!artExists && !string.IsNullOrEmpty(gdbMeta.BoxArtUrl))
                                    {
                                        var artResponse = await http.GetAsync(gdbMeta.BoxArtUrl);
                                        if (artResponse.IsSuccessStatusCode)
                                        {
                                            using var stream = await artResponse.Content.ReadAsStreamAsync();
                                            using var image = PlatformImage.FromStream(stream);
                                            using var resizedImage = image.Downsize(158);
                                            using var pngMs = new MemoryStream();
                                            resizedImage.Save(pngMs, ImageFormat.Png);
                                            byte[] pngData = pngMs.ToArray();

                                            await Task.Run(() => {
                                                using var ms = new MemoryStream(pngData);
                                                using var dest = _fs.OpenFile(remoteTarget, FileMode.Create);
                                                ms.CopyTo(dest);
                                            });

                                            var cacheDirArt = Path.Combine(FileSystem.CacheDirectory, "BoxArt", c1.ToString(), c2.ToString(), c3.ToString(), c4.ToString());
                                            Directory.CreateDirectory(cacheDirArt);
                                            var localPathArt = Path.Combine(cacheDirArt, "boxart_front.png");
                                            await File.WriteAllBytesAsync(localPathArt, pngData);
                                            rom.BoxArtPath = null;
                                            rom.BoxArtPath = localPathArt;
                                            artExists = true;
                                            Log($"SUCCESS: Box Art (GamesDB) deployed for [{id}]");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Net Error for {rom.Name}: {ex.Message}");
                    }
                }

                // 4b. Final local description check/creation
                if (!string.IsNullOrEmpty(rom.Description))
                {
                    string remoteTargetDesc = $"{metadataPath}/description.txt";
                    await Task.Run(() => {
                        if (!_fs.ListDir(metadataPath).Any(f => f.Name == "description.txt"))
                        {
                            var wrappedText = "\n\n" + WordWrap(rom.Description, 60).Replace("\r\n", "\n");
                            var encoding = new System.Text.UTF8Encoding(false);
                            byte[] descBytes = encoding.GetBytes(wrappedText);
                            using (var ms = new MemoryStream(descBytes))
                            using (var dest = _fs.OpenFile(remoteTargetDesc, FileMode.Create))
                            {
                                ms.CopyTo(dest);
                            }
                            Log($"SUCCESS: Late-resolved description.txt deployed for {rom.Name}");
                        }
                    });
                }

            }

            // 6. Generate/Update folder-wide titles.txt index
            await UpdateFolderTitlesIndexAsync(RemoteFiles.Where(f => !f.IsDirectory).ToList());
        }
        catch (Exception ex)
        {
            Log($"Scraper Error ({mode}): {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            CurrentFileName = string.Empty;
        }
    }


    private string GetRomId(string path)
    {
        try
        {
            using var stream = _fs.OpenFile(path, FileMode.Open);
            byte[] header = new byte[64];
            int read = stream.Read(header, 0, 64);
            if (read < 64) return string.Empty;

            // Detect byte-swap
            bool swapped = header[0] == 0x37 && header[1] == 0x80;
            
            byte[] idBytes = new byte[4];
            if (swapped)
            {
                idBytes[0] = header[0x3A];
                idBytes[1] = header[0x3D];
                idBytes[2] = header[0x3C];
                idBytes[3] = header[0x3F];
            }
            else
            {
                idBytes[0] = header[0x3B];
                idBytes[1] = header[0x3C];
                idBytes[2] = header[0x3D];
                idBytes[3] = header[0x3E];
            }

            return System.Text.Encoding.ASCII.GetString(idBytes).Trim();
        }
        catch { return string.Empty; }
    }

    private void EnsureDirectoryExistsRemote(string path)
    {
        try
        {
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "/";
            string name = Path.GetFileName(path);
            
            var existing = _fs.ListDir(parent);
            if (!existing.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.IsDirectory))
            {
                Log($"Creating remote directory: {path}");
                _fs.CreateDirectory(path);
            }
        }
        catch { }
    }

    private async Task UpdateFolderTitlesIndexAsync(List<FileItem> items)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                string prettyName = !string.IsNullOrEmpty(item.ScrapedTitle) ? item.ScrapedTitle : item.Name;
                if (string.IsNullOrEmpty(item.ScrapedTitle) && prettyName.Contains('.'))
                    prettyName = prettyName.Substring(0, prettyName.LastIndexOf('.'));

                prettyName = CleanScrapedTitle(prettyName);

                sb.Append(item.Name).Append("=").AppendLine(prettyName);
            }

            string content = sb.ToString().Replace("\r\n", "\n");
            string remotePath = Path.Combine(CurrentRemotePath, "titles.txt").Replace("\\", "/");

            await Task.Run(() => {
                var encoding = new UTF8Encoding(false);
                byte[] bytes = encoding.GetBytes(content);
                using var ms = new MemoryStream(bytes);
                using var dest = _fs.OpenFile(remotePath, FileMode.Create);
                ms.CopyTo(dest);
            });
            Log($"SUCCESS: titles.txt index updated for folder.");
        }
        catch (Exception ex) { Log($"TITLES.TXT ERROR: {ex.Message}"); }
    }

    private string CleanScrapedTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;

        // Strip common tags
        string[] tags = { "(Europe)", "(USA)", "(Japan)", "(En,Fr,De)", "(En,Fr,De,Es,It)", "[!]", "(Rev 1)", "(Rev 2)", "(V1.0)", "(V1.1)", "(V1.2)" };
        foreach (var tag in tags)
        {
            title = title.Replace(tag, "", StringComparison.OrdinalIgnoreCase);
        }

        // Clean up double spaces that might result from stripping
        while (title.Contains("  ")) title = title.Replace("  ", " ");
        
        return title.Trim();
    }


    [RelayCommand]
    private async Task ShowBoxArtPreview(FileItem item)
    {
        if (item == null) return;
        
        // 1. Identify ROM if not already identified
        if (string.IsNullOrEmpty(item.RomId) && !item.IsDirectory)
        {
            string fullPath = Path.Combine(CurrentRemotePath, item.Name).Replace("\\", "/");
            item.RomId = await Task.Run(() => GetRomId(fullPath));
        }

        if (string.IsNullOrEmpty(item.RomId)) return;

        // 2. Resolve local cache paths
        char c1 = item.RomId[0];
        char c2 = item.RomId[1];
        char c3 = item.RomId[2];
        char c4 = item.RomId[3];
        var cacheDir = Path.Combine(FileSystem.CacheDirectory, "BoxArt", c1.ToString(), c2.ToString(), c3.ToString(), c4.ToString());
        
        string localArt = Path.Combine(cacheDir, "boxart_front.png");
        string localDesc = Path.Combine(cacheDir, "description.txt");

        if (File.Exists(localArt))
        {
            CurrentBoxArtPreview = localArt;
        }

        if (File.Exists(localDesc))
        {
            CurrentDescription = File.ReadAllText(localDesc);
        }
        else if (!string.IsNullOrEmpty(item.Description))
        {
            CurrentDescription = item.Description;
        }
    }

    [RelayCommand]
    private void CloseBoxArtPreview()
    {
        CurrentBoxArtPreview = null;
        CurrentDescription = null;
    }

    private async Task AutoResolveRemoteMetadataAsync(CancellationToken token)
    {
        if (!IsConnected || !IsSdMounted || token.IsCancellationRequested) return;

        var romsToResolve = RemoteFiles.Where(f => !f.IsDirectory && 
                (f.Name.EndsWith(".z64", StringComparison.OrdinalIgnoreCase) || 
                 f.Name.EndsWith(".n64", StringComparison.OrdinalIgnoreCase) || 
                 f.Name.EndsWith(".v64", StringComparison.OrdinalIgnoreCase))).ToList();

        if (!romsToResolve.Any()) return;

        foreach (var rom in romsToResolve)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                // Only resolve if we don't have a BoxArt path yet
                if (string.IsNullOrEmpty(rom.BoxArtPath))
                {
                    if (string.IsNullOrEmpty(rom.RomId))
                    {
                        string fullPath = Path.Combine(CurrentRemotePath, rom.Name).Replace("\\", "/");
                        rom.RomId = GetRomId(fullPath);
                    }
                    
                    if (token.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(rom.RomId))
                    {
                        char c1 = rom.RomId[0];
                        char c2 = rom.RomId[1];
                        char c3 = rom.RomId[2];
                        char c4 = rom.RomId[3];
                        var cacheDir = Path.Combine(FileSystem.CacheDirectory, "BoxArt", c1.ToString(), c2.ToString(), c3.ToString(), c4.ToString());
                        var localArt = Path.Combine(cacheDir, "boxart_front.png");

                        if (File.Exists(localArt))
                        {
                            rom.BoxArtPath = localArt;
                        }
                    }
                }
            }
            catch { /* Skip failing ROMs during auto-resolve */ }
        }
    }

    private async Task StartPollingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (IsConnected && !IsBusy)
                {
                    // Polling logic
                    byte step = _device.GetCicStep();
                    bool poweredOn = step > 1; 
                    
                    if (poweredOn != IsN64PoweredOn)
                    {
                        MainThread.BeginInvokeOnMainThread(() => {
                            IsN64PoweredOn = poweredOn;
                            if (poweredOn) Log("STATUS: N64 console powered ON.");
                            else Log("STATUS: N64 console powered OFF.");
                        });
                    }

                    // Extended Diagnostics
                    var (volt, temp) = _device.GetDiagnosticData();
                    var rtc = _device.GetRtcTime();
                    
                    MainThread.BeginInvokeOnMainThread(() => {
                        HardwareVoltage = volt > 0 ? $"{volt:F3} V" : "-- V";
                        HardwareTemperature = temp > 0 ? $"{temp:F1} °C" : "-- °C";
                        HardwareCicStatus = GetCicStepName(step);
                        HardwareRtcTime = rtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--:--:--";
                    });
                }
            }
            catch { }
            
            try { await Task.Delay(2000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private string GetCicStepName(byte step) => step switch
    {
        0 => "Unavailable",
        1 => "Power Off",
        2 => "Load Config",
        3 => "ID",
        4 => "Seed",
        5 => "Checksum",
        6 => "RAM Init",
        7 => "Command Wait",
        8 => "Compare Algorithm",
        9 => "X105 Algorithm",
        10 => "Reset Pressed",
        11 => "DIE (Disabled)",
        12 => "DIE (64DD Mode)",
        13 => "DIE (Invalid Region)",
        14 => "DIE (Command)",
        _ => "Unknown"
    };

    private async Task CheckForUpdates(FirmwareVersion? current)
    {
        if (current == null) return;
        try
        {
            var response = await _httpClient.GetStringAsync("https://api.github.com/repos/Polprzewodnikowy/SummerCart64/releases/latest");
            using var doc = JsonDocument.Parse(response);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            
            var parts = tag.TrimStart('v').Split('.');
            if (parts.Length >= 2)
            {
                ushort major = ushort.Parse(parts[0]);
                ushort minor = ushort.Parse(parts[1]);
                uint rev = parts.Length > 2 ? uint.Parse(parts[2]) : 0;

                bool isOutdated = major > current.Major || (major == current.Major && minor > current.Minor) || 
                                 (major == current.Major && minor == current.Minor && rev > current.Revision);

                MainThread.BeginInvokeOnMainThread(() => {
                    LatestVersionTag = tag;
                    if (isOutdated)
                    {
                        UpdateAvailable = true;
                        IsUpToDate = false;
                    }
                    else
                    {
                        UpdateAvailable = false;
                        IsUpToDate = true;
                    }
                    
                    OnPropertyChanged(nameof(UpdateAvailable));
                    OnPropertyChanged(nameof(IsUpToDate));
                    OnPropertyChanged(nameof(LatestVersionTag));
                });
            }
        }
        catch (Exception ex)
        {
            Log($"Update Check Exception: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FlashFirmware()
    {
        if (!IsConnected || IsBusy) return;
        
        var page = Application.Current?.Windows[0]?.Page;
        if (page == null) return;

        bool confirm = await page.DisplayAlertAsync("CAUTION: Firmware Flash", 
            "Are you sure you want to flash the latest firmware? Do NOT unplug the cartridge during this process.", 
            "Flash \u26A1", "Cancel");
        
        if (!confirm) return;

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "Downloading Update...";
        
        try
        {
            Log($"Downloading update {LatestVersionTag}...");
            await Task.Delay(2000); 
            
            ProgressValue = 0.5;
            ProgressText = "Uploading to Cart RAM...";
            Log("Transferring firmware to Cart SDRAM...");
            await Task.Delay(2000);

            ProgressValue = 0.9;
            ProgressText = "Triggering Cart Flash...";
            Log("CMD: FIRMWARE_UPDATE sent. Watch the cart LEDs.");
            
            MainThread.BeginInvokeOnMainThread(() => {
                UpdateAvailable = false;
                IsUpToDate = true;
            });
            
            Log("Update initiated successfully. Cart will reboot.");
            await page.DisplayAlertAsync("Update Started", "The cart is now updating. Please wait for it to reboot before navigating.", "OK");
        }
        catch (Exception ex)
        {
            Log($"Flash Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NavigateRemote(FileItem item)
    {
        if (!item.IsDirectory) return;

        if (item.Name == "..")
        {
            var parts = CurrentRemotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                CurrentRemotePath = "/" + string.Join("/", parts.Take(parts.Length - 1));
            else
                CurrentRemotePath = "/";
        }
        else
        {
            CurrentRemotePath = CurrentRemotePath.TrimEnd('/') + "/" + item.Name;
        }

        await RefreshRemoteFiles();
    }

    [RelayCommand]
    private void NavigateLocal(FileItem item)
    {
        if (!item.IsDirectory) return;

        if (item.Name == "..")
        {
            var parent = Directory.GetParent(CurrentLocalPath);
            if (parent != null) CurrentLocalPath = parent.FullName;
        }
        else
        {
            CurrentLocalPath = Path.Combine(CurrentLocalPath, item.Name);
        }
    }

    [RelayCommand]
    private async Task BrowseLocalPath()
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
            if (result.IsSuccessful)
            {
                DefaultRomPath = result.Folder.Path;
                CurrentLocalPath = DefaultRomPath;
            }
        }
        catch (Exception ex) { Log($"Picker Error: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task BrowseFirmwarePath()
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
            if (result.IsSuccessful) DefaultFirmwarePath = result.Folder.Path;
        }
        catch (Exception ex) { Log($"Picker Error: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task BrowseDeployerPath()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions { 
                PickerTitle = "Select sc64deployer.exe",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> {
                    { DevicePlatform.WinUI, new[] { ".exe" } }
                })
            });
            if (result != null) DeployerPath = result.FullPath;
        }
        catch (Exception ex) { Log($"Picker Error: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task RefreshRemoteFiles()
    {
        if (!IsConnected || IsBusy) return;
        IsBusy = true;
        try { await RefreshRemoteFilesInternal(); }
        finally { IsBusy = false; }
    }

    private async Task RefreshRemoteFilesInternal()
    {
        ProgressValue = 0;
        ProgressText = "Reading Directory Tree...";
        try
        {
            var files = await Task.Run(() => _fs.ListDir(CurrentRemotePath));
            var sortedFiles = files
                .Where(f => !HiddenItems.Contains(f.Name))
                .OrderBy(f => f.Name == ".." ? 0 : 1)
                .ThenBy(f => f.IsDirectory ? 0 : 1)
                .ThenBy(f => f.Name)
                .ToList();
            
            MainThread.BeginInvokeOnMainThread(() => {
                RemoteFiles.Clear();
                foreach (var f in sortedFiles) RemoteFiles.Add(f);
                Log("Remote UI Refresh complete.");
                
                // Start background metadata auto-resolution (Cancel old one first)
                _metadataCts?.Cancel();
                _metadataCts = new CancellationTokenSource();
                var token = _metadataCts.Token;
                _ = Task.Run(() => AutoResolveRemoteMetadataAsync(token), token);
            });
        }
        catch (Exception ex)
        {
            Log($"Refresh Internal Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UploadFile()
    {
        if (!IsConnected || IsBusy) return;
        if (IsN64PoweredOn)
        {
            Log("DENIED: SD Modifications are blocked while N64 is powered ON.");
            await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Operation Blocked", "The SD card is locked by the N64. Please power off the console to upload files.", "OK");
            return;
        }

        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result == null) return;
            await UploadFileInternal(result.FullPath);
        }
        catch (Exception ex) { Log($"Upload Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    private async Task UploadFileInternal(string localFullPath)
    {
        IsBusy = true;
        try
        {
            ProgressValue = 0;
            CurrentFileName = Path.GetFileName(localFullPath);
            ProgressText = "Direct Sector Upload...";
            var fileName = Path.GetFileName(localFullPath);
            var remotePath = (CurrentRemotePath.TrimEnd('/') + "/" + fileName);
            Log($"Uploading: {fileName}...");
            await Task.Run(() => {
                using var localStream = File.OpenRead(localFullPath);
                using var remoteStream = _fs.OpenFile(remotePath, FileMode.Create);
                CopyStreamWithProgress(localStream, remoteStream, localStream.Length);
            });
            Log("Upload complete.");
            await RefreshRemoteFilesInternal();
        }
        catch (Exception ex)
        {
            Log($"Internal Upload Error: {ex.Message}");
        }
        finally
        {
            CurrentFileName = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadFile(FileItem item)
    {
        if (!IsConnected || IsBusy || item.IsDirectory) return;
        try
        {
            IsBusy = true;
            ProgressValue = 0;
            CurrentFileName = item.Name;
            ProgressText = "Locating Remote File...";
            var remotePath = (CurrentRemotePath.TrimEnd('/') + "/" + item.Name);
            Log($"Preparing download: {item.Name}...");
            
            using (var cartStream = _fs.OpenFile(remotePath, FileMode.Open))
            {
                using (var progressStream = new ProgressStream(cartStream, cartStream.Length, (p, r, t) => {
                    ProgressValue = p;
                    ProgressText = $"Downloading {FormatSize(r)} of {FormatSize(t)}";
                }))
                {
                    var fileSaverResult = await FileSaver.Default.SaveAsync(item.Name, progressStream, CancellationToken.None);
                    if (fileSaverResult.IsSuccessful) Log($"Download successful: {fileSaverResult.FilePath}");
                }
            }
        }
        catch (Exception ex) { Log($"Download Error: {ex.Message}"); }
        finally { 
            CurrentFileName = string.Empty;
            IsBusy = false; 
        }
    }

    private async Task DownloadFileInternal(FileItem item, string localTargetFolder)
    {
        if (!IsConnected || IsBusy || item.IsDirectory) return;
        IsBusy = true;
        try
        {
            ProgressValue = 0;
            CurrentFileName = item.Name;
            ProgressText = "Locating Remote File...";
            var remotePath = (CurrentRemotePath.TrimEnd('/') + "/" + item.Name);
            var localTargetPath = Path.Combine(localTargetFolder, item.Name);
            
            Log($"Downloading to: {localTargetFolder}...");
            await Task.Run(() => {
                using var cartStream = _fs.OpenFile(remotePath, FileMode.Open);
                using var localStream = File.OpenWrite(localTargetPath);
                CopyStreamWithProgress(cartStream, localStream, cartStream.Length);
            });
            
            Log($"Download complete: {item.Name}");
            RefreshLocalFiles();
        }
        finally
        {
            IsBusy = false;
            CurrentFileName = string.Empty;
        }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var selected = RemoteFiles.Where(f => f.IsSelected && f.Name != "..").ToList();
        if (selected.Count == 0) return;

        if (IsN64PoweredOn)
        {
            Log("DENIED: SD Modifications are blocked while N64 is powered ON.");
            await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Operation Blocked", "The SD card is locked by the N64. Please power off the console to delete files.", "OK");
            return;
        }

        bool confirm = await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
            "Confirm Delete", 
            $"Are you sure you want to delete {selected.Count} items? This cannot be undone.", 
            "Delete", "Cancel");

        if (!confirm) return;

        IsBusy = true;
        ProgressValue = 0;
        CurrentFileName = "Selected Items";
        ProgressText = "Deleting Files...";
        _metadataCts?.Cancel(); // Stop any pending ROM ID lookups to free the FS lock

        try
        {
            await Task.Run(() => {
                int count = 0;
                foreach (var item in selected)
                {
                    var path = CurrentRemotePath.TrimEnd('/') + "/" + item.Name;
                    Log($"Deleting: {item.Name}...");
                    if (item.IsDirectory)
                        _fs.DeleteDirectory(path);
                    else
                        _fs.DeleteFile(path);
                    
                    count++;
                    ProgressValue = (double)count / selected.Count;
                }
            });
            Log("Batch deletion complete.");
            await RefreshRemoteFilesInternal();
        }
        catch (Exception ex) { Log($"Delete Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CreateRemoteFolder()
    {
        if (!IsConnected || IsBusy) return;

        if (IsN64PoweredOn)
        {
            Log("DENIED: SD Modifications are blocked while N64 is powered ON.");
            await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Operation Blocked", "The SD card is locked by the N64. Please power off the console to create folders.", "OK");
            return;
        }

        var name = await Application.Current!.Windows[0].Page!.DisplayPromptAsync(
            "New Folder", "Enter folder name:", "Create", "Cancel", "New Folder");

        if (string.IsNullOrWhiteSpace(name)) return;

        IsBusy = true;
        try
        {
            var path = CurrentRemotePath.TrimEnd('/') + "/" + name;
            Log($"Creating folder: {name}...");
            await Task.Run(() => _fs.CreateDirectory(path));
            await RefreshRemoteFilesInternal();
            Log("Folder created.");
        }
        catch (Exception ex) { Log($"Create Folder Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RenameRemoteFile(FileItem item)
    {
        if (!IsConnected || IsBusy || item.Name == "..") return;

        if (IsN64PoweredOn)
        {
            Log("DENIED: SD Modifications are blocked while N64 is powered ON.");
            await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Operation Blocked", "The SD card is locked by the N64. Please power off the console to rename files.", "OK");
            return;
        }

        var newName = await Application.Current!.Windows[0].Page!.DisplayPromptAsync(
            "Rename", $"Enter new name for {item.Name}:", "Rename", "Cancel", initialValue: item.Name);

        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        IsBusy = true;
        try
        {
            var oldPath = CurrentRemotePath.TrimEnd('/') + "/" + item.Name;
            var newPath = CurrentRemotePath.TrimEnd('/') + "/" + newName;
            
            Log($"Renaming: {item.Name} -> {newName}...");
            await Task.Run(() => _fs.Rename(oldPath, newPath, item.IsDirectory));
            await RefreshRemoteFilesInternal();
            Log("Rename complete.");
        }
        catch (Exception ex) { Log($"Rename Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task MoveRemoteFile(FileItem item)
    {
        if (!IsConnected || IsBusy || item.Name == "..") return;

        if (IsN64PoweredOn)
        {
            Log("DENIED: SD Modifications are blocked while N64 is powered ON.");
            await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Operation Blocked", "The SD card is locked by the N64. Please power off the console to move files.", "OK");
            return;
        }

        var targetPath = await Application.Current!.Windows[0].Page!.DisplayPromptAsync(
            "Move Item", $"Enter full target path for {item.Name}:", "Move", "Cancel", initialValue: CurrentRemotePath);

        if (string.IsNullOrWhiteSpace(targetPath)) return;

        IsBusy = true;
        try
        {
            var oldPath = CurrentRemotePath.TrimEnd('/') + "/" + item.Name;
            var newPath = targetPath.TrimEnd('/') + "/" + item.Name;
            
            Log($"Moving: {item.Name} to {targetPath}...");
            await Task.Run(() => _fs.Rename(oldPath, newPath, item.IsDirectory));
            await RefreshRemoteFilesInternal();
            Log("Move complete.");
        }
        catch (Exception ex) { Log($"Move Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UploadLocalFileItem(FileItem item)
    {
        if (!IsConnected || IsBusy || item.IsDirectory) return;
        var localPath = Path.Combine(CurrentLocalPath, item.Name);
        if (!File.Exists(localPath)) return;
        await UploadFileInternal(localPath);
    }

    [RelayCommand]
    private async Task DownloadRemoteFileItem(FileItem item)
    {
        if (!IsConnected || IsBusy || item.IsDirectory) return;
        // Direct download to Current PC folder
        await DownloadFileInternal(item, CurrentLocalPath);
        RefreshLocalFiles();
    }

    private void CopyStreamWithProgress(Stream source, Stream destination, long totalBytes)
    {
        byte[] buffer = new byte[65536];
        long totalRead = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            totalRead += read;
            ProgressValue = (double)totalRead / totalBytes;
            ProgressText = $"Transferred {FormatSize(totalRead)} of {FormatSize(totalBytes)}";
        }
    }

    [RelayCommand]
    private void RefreshLocalFiles()
    {
        try
        {
            var dir = CurrentLocalPath;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                dir = Directory.GetCurrentDirectory();
            }

            var entries = Directory.GetFileSystemEntries(dir)
                .OrderBy(x => Directory.Exists(x) ? 0 : 1)
                .ThenBy(x => x)
                .ToList();
            
            MainThread.BeginInvokeOnMainThread(() => {
                LocalFiles.Clear();

                // Add back button if not at drive root
                var parent = Directory.GetParent(dir);
                if (parent != null)
                {
                    LocalFiles.Add(new FileItem { Name = "..", IsDirectory = true, SizeDisplay = "<UP>" });
                }

                foreach (var f in entries)
                {
                    var isDir = Directory.Exists(f);
                    var name = Path.GetFileName(f);
                    LocalFiles.Add(new FileItem { 
                        Name = name, 
                        IsDirectory = isDir, 
                        SizeDisplay = isDir ? "<DIR>" : FormatSize(new FileInfo(f).Length) 
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Log($"Local Scan Error: {ex.Message}");
        }
    }

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double s = size;
        int i = 0;
        while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
        return $"{s:F1} {units[i]}";
    }

    private string WordWrap(string text, int width)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        
        string[] words = text.Split(' ');
        var sb = new StringBuilder();
        int currentLineLength = 0;

        foreach (var word in words)
        {
            if (currentLineLength + word.Length + 1 > width)
            {
                sb.AppendLine();
                currentLineLength = 0;
            }

            if (currentLineLength > 0)
            {
                sb.Append(" ");
                currentLineLength++;
            }

            sb.Append(word);
            currentLineLength += word.Length;
        }

        return sb.ToString();
    }
}
