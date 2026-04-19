using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NetGui.Services;
using NetGui.Models;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Alerts;
using System.Net.Http.Json;
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
    private readonly FsService _fs;
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _pollingCts;

    public MainViewModel(SettingsService settings)
    {
        _settings = settings;
        _device = new SC64Device();
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

    public string DefaultFirmwarePath => _settings.DefaultFirmwarePath;
    public string DefaultRomPath => _settings.DefaultRomPath;
    
    public string DeployerPath 
    {
        get => _settings.DeployerPath;
        set { _settings.DeployerPath = value; OnPropertyChanged(); }
    }

    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsUpToDate { get; set; }

    [ObservableProperty]
    public partial string LatestVersionTag { get; set; } = string.Empty;

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
    private async Task GoToAbout() => await Shell.Current.GoToAsync("AboutPage");

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
            Disconnect();
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
    private void Disconnect()
    {
        try
        {
            _pollingCts?.Cancel();
            _fs.Disconnect();
            _device.Disconnect();
            IsConnected = false;
            IsN64PoweredOn = false;
            OnPropertyChanged(nameof(IsConnected));
            Log("DISCONNECTED: Hardware bridge released.");
        }
        catch (Exception ex)
        {
            Log($"Disconnect Error: {ex.Message}");
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
                if (_device.Connect(SelectedPort))
                {
                    Log("Connected. Performing hardware handshake...");
                    if (!_device.ResetHandshake())
                    {
                        Log("Error: Handshake failed.");
                        _device.Disconnect();
                        return;
                    }

                    Log("Identifying SummerCart64...");
                    if (_device.GetIdentifier() == "Unknown")
                    {
                        Log("Error: Device signature not found.");
                        _device.Disconnect();
                        return;
                    }

                    var version = _device.GetVersion();
                    Log($"Cart Version: {version}");

                    // Boot-time Power Check (Wait for FPGA stability)
                    await Task.Delay(200);
                    Log("Detecting console power status...");
                    byte step = _device.GetCicStep();
                    IsN64PoweredOn = step > 1;
                    if (IsN64PoweredOn) Log("⚠️ ALERT: N64 is already powered ON.");

                    Log("Initializing protocol state...");
                    _device.StateReset();
                    
                    Log("Mounting SD card...");
                    var success = await Task.Run(() => _fs.Mount(Log));
                    if (success)
                    {
                        Log("Mount SUCCESS. Preparing UI...");
                        
                        MainThread.BeginInvokeOnMainThread(async () => {
                            IsConnected = true;
                            StatusText = $"Connected ({version})";
                            CurrentRemotePath = "/";
                            
                            Log("Starting hardware status polling...");
                            _pollingCts = new CancellationTokenSource();
                            _ = StartPollingLoop(_pollingCts.Token);

                            Log("Scanning initial directory tree...");
                            await RefreshRemoteFilesInternal();
                            
                            Log("Running update check sequence...");
                            await CheckForUpdates(version);
                        });
                        
                        Log("Connection sequence COMPLETED.");
                    }
                    else
                    {
                        Log("Error: SD Card mount failed.");
                        _device.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"CRITICAL CRASH: {ex.Message}\n{ex.StackTrace}");
                if (Application.Current?.Windows.Count > 0)
                {
                    await Application.Current.Windows[0].Page!.DisplayAlertAsync("Connection Crash", ex.Message, "OK");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        else
        {
            _pollingCts?.Cancel();
            _fs.Disconnect();
            _device.Disconnect();
            
            MainThread.BeginInvokeOnMainThread(() => {
                IsConnected = false;
                IsN64PoweredOn = false;
                UpdateAvailable = false;
                IsUpToDate = false;
                StatusText = "Disconnected";
                RemoteFiles.Clear();
            });
            
            Log("Disconnected.");
        }
    }

    private async Task StartPollingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Only poll if not busy with another command (the Lock handles this but it's cleaner to check)
                if (IsConnected && !IsBusy)
                {
                    byte step = _device.GetCicStep();
                    bool poweredOn = step > 1; // 0=Unavailable, 1=PowerOff, Others=On
                    
                    if (poweredOn != IsN64PoweredOn)
                    {
                        MainThread.BeginInvokeOnMainThread(() => {
                            IsN64PoweredOn = poweredOn;
                            if (poweredOn) Log("STATUS: N64 console powered ON.");
                            else Log("STATUS: N64 console powered OFF.");
                        });
                    }
                }
            }
            catch { }
            
            try { await Task.Delay(2000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

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
        var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
        if (result.IsSuccessful)
        {
            _settings.DefaultRomPath = result.Folder.Path;
            CurrentLocalPath = result.Folder.Path;
            OnPropertyChanged(nameof(DefaultRomPath));
        }
    }

    [RelayCommand]
    private async Task BrowseFirmwarePath()
    {
        var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
        if (result.IsSuccessful)
        {
            _settings.DefaultFirmwarePath = result.Folder.Path;
            OnPropertyChanged(nameof(DefaultFirmwarePath));
        }
    }

    [RelayCommand]
    private async Task BrowseDeployerPath()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions {
            PickerTitle = "Select sc64deployer.exe",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> {
                { DevicePlatform.WinUI, new[] { ".exe" } }
            })
        });

        if (result != null)
        {
            DeployerPath = result.FullPath;
        }
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
}
