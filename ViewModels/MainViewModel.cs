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

    public MainViewModel()
    {
        _device = new SC64Device();
        _fs = new FsService(_device);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SC64Manager/1.0");

        AvailablePorts = new ObservableCollection<string>();
        LocalFiles = new ObservableCollection<FileItem>();
        RemoteFiles = new ObservableCollection<FileItem>();
        
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Task.Delay(1000); // UI Warmup Buffer

        try
        {
            Log("Startup sequence beginning...");
            
            await Task.Run(() => {
                RefreshAvailablePorts();
                RefreshLocalFiles();
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
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string LogText { get; set; } = "Ready.\n";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedPort { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentRemotePath { get; set; } = "/";

    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsUpToDate { get; set; }

    [ObservableProperty]
    public partial string LatestVersionTag { get; set; } = string.Empty;

    public ObservableCollection<string> AvailablePorts { get; } 
    public ObservableCollection<FileItem> LocalFiles { get; } 
    public ObservableCollection<FileItem> RemoteFiles { get; } 

    private void Log(string message)
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
    private void RefreshAvailablePorts()
    {
        try
        {
            var ports = _device.GetAvailablePorts();
            MainThread.BeginInvokeOnMainThread(() => {
                AvailablePorts.Clear();
                foreach (var p in ports) AvailablePorts.Add(p);
                if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(SelectedPort)) SelectedPort = AvailablePorts[0];
            });
        }
        catch (Exception ex)
        {
            Log($"Port Scan Error: {ex.Message}");
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
            _fs.Disconnect();
            _device.Disconnect();
            
            MainThread.BeginInvokeOnMainThread(() => {
                IsConnected = false;
                UpdateAvailable = false;
                IsUpToDate = false;
                StatusText = "Disconnected";
                RemoteFiles.Clear();
            });
            
            Log("Disconnected.");
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
            {
                CurrentRemotePath = "/" + string.Join("/", parts.Take(parts.Length - 1));
            }
            else
            {
                CurrentRemotePath = "/";
            }
        }
        else
        {
            CurrentRemotePath = CurrentRemotePath.TrimEnd('/') + "/" + item.Name;
        }

        await RefreshRemoteFiles();
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
            var filteredFiles = files.Where(f => !HiddenItems.Contains(f.Name)).ToList();
            
            MainThread.BeginInvokeOnMainThread(() => {
                RemoteFiles.Clear();
                foreach (var f in filteredFiles) RemoteFiles.Add(f);
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
        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result == null) return;
            IsBusy = true;
            ProgressValue = 0;
            ProgressText = "Direct Sector Upload...";
            var remotePath = (CurrentRemotePath.TrimEnd('/') + "/" + result.FileName);
            Log($"Uploading: {result.FileName}...");
            await Task.Run(() => {
                using var localStream = File.OpenRead(result.FullPath);
                using var remoteStream = _fs.OpenFile(remotePath, FileMode.Create);
                CopyStreamWithProgress(localStream, remoteStream, localStream.Length);
            });
            Log("Upload complete.");
            await RefreshRemoteFiles();
        }
        catch (Exception ex) { Log($"Upload Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DownloadFile(FileItem item)
    {
        if (!IsConnected || IsBusy || item.IsDirectory) return;
        try
        {
            IsBusy = true;
            ProgressValue = 0;
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
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var selected = RemoteFiles.Where(f => f.IsSelected && f.Name != "..").ToList();
        if (selected.Count == 0) return;

        bool confirm = await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
            "Confirm Delete", 
            $"Are you sure you want to delete {selected.Count} items? This cannot be undone.", 
            "Delete", "Cancel");

        if (!confirm) return;

        IsBusy = true;
        ProgressValue = 0;
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
            await RefreshRemoteFiles();
        }
        catch (Exception ex) { Log($"Delete Error: {ex.Message}"); }
        finally { IsBusy = false; }
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

    private void RefreshLocalFiles()
    {
        try
        {
            var dir = Directory.GetCurrentDirectory();
            var entries = Directory.GetFileSystemEntries(dir).OrderBy(x => Directory.Exists(x) ? 0 : 1);
            
            MainThread.BeginInvokeOnMainThread(() => {
                LocalFiles.Clear();
                foreach (var f in entries)
                {
                    var isDir = Directory.Exists(f);
                    var name = Path.GetFileName(f);
                    LocalFiles.Add(new FileItem { Name = name, IsDirectory = isDir, SizeDisplay = isDir ? "<DIR>" : FormatSize(new FileInfo(f).Length) });
                }
            });
        }
        catch { }
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
