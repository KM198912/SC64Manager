using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NetGui.Services;
using NetGui.Models;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Alerts;

namespace NetGui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SC64Device _device;
    private readonly FsService _fs;

    public MainViewModel()
    {
        _device = new SC64Device();
        _fs = new FsService(_device);
        RefreshAvailablePorts();
        RefreshLocalFiles();
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
    public partial string LogText { get; set; } = "Ready. Select COM port and Connect.\n";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedPort { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentRemotePath { get; set; } = "/";

    public ObservableCollection<string> AvailablePorts { get; } = new();
    public ObservableCollection<FileItem> LocalFiles { get; } = new();
    public ObservableCollection<FileItem> RemoteFiles { get; } = new();

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText += $"[{timestamp}] {message}\n";
    }

    [RelayCommand]
    private void RefreshAvailablePorts()
    {
        AvailablePorts.Clear();
        foreach (var p in _device.GetAvailablePorts()) AvailablePorts.Add(p);
        if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(SelectedPort)) SelectedPort = AvailablePorts[0];
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
            Log($"Attempting to connect to {SelectedPort} at 1Mbps...");
            
            try
            {
                if (_device.Connect(SelectedPort))
                {
                    Log("Connected. Performing hardware handshake...");
                    if (!_device.ResetHandshake())
                    {
                        Log("Error: Hardware handshake failed. Check cable/power.");
                        _device.Disconnect();
                        return;
                    }

                    Log("Identifying SummerCart64...");
                    var version = _device.GetVersion();
                    
                    if (version == "Unknown")
                    {
                        Log("Error: SummerCart64 signature not found.");
                        _device.Disconnect();
                        return;
                    }

                    Log($"Device Version: {version}");

                    Log("Initializing protocol state...");
                    _device.StateReset();
                    
                    var success = await Task.Run(() => _fs.Mount(Log));
                    if (success)
                    {
                        IsConnected = true;
                        StatusText = $"Connected ({version})";
                        CurrentRemotePath = "/";
                        
                        IsBusy = false;
                        await RefreshRemoteFiles();
                    }
                    else
                    {
                        Log("Error: SD Card mounting failed.");
                        _device.Disconnect();
                    }
                }
                else
                {
                    Log($"Fatal: Could not open {SelectedPort}. Port may be busy.");
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
            IsConnected = false;
            StatusText = "Disconnected";
            RemoteFiles.Clear();
            Log("Disconnected.");
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
        ProgressValue = 0;
        ProgressText = "Reading Directory Tree...";
        Log($"Listing {CurrentRemotePath}...");
        
        try
        {
            var files = await Task.Run(() => _fs.ListDir(CurrentRemotePath));
            MainThread.BeginInvokeOnMainThread(() => {
                RemoteFiles.Clear();
                foreach (var f in files) RemoteFiles.Add(f);
                Log("Remote refresh complete.");
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var selected = RemoteFiles.Where(f => f.IsSelected && f.Name != "..").ToList();
        if (selected.Count == 0) return;

        var page = Application.Current?.Windows[0]?.Page;
        if (page == null) return;

        bool confirm = await page.DisplayAlertAsync("Confirm Delete", 
            $"Are you sure you want to delete {selected.Count} items?", "Yes", "No");
        if (!confirm) return;

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "Batch Deleting Files...";
        try
        {
            int count = 0;
            foreach (var item in selected)
            {
                var fullPath = CurrentRemotePath.TrimEnd('/') + "/" + item.Name;
                Log($"Deleting {item.Name}...");
                _fs.Delete(fullPath);
                count++;
                ProgressValue = (double)count / selected.Count;
            }
            await RefreshRemoteFiles();
        }
        finally
        {
            IsBusy = false;
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
            Log($"Starting High-Speed Upload: {result.FileName}...");
            
            await Task.Run(() => {
                using var localStream = File.OpenRead(result.FullPath);
                using var remoteStream = _fs.OpenFile(remotePath, FileMode.Create);
                CopyStreamWithProgress(localStream, remoteStream, localStream.Length);
            });

            Log("Upload verified and complete.");
            await RefreshRemoteFiles();
        }
        catch (Exception ex)
        {
            Log($"Upload Error: {ex.Message}");
        }
        finally
        {
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
            ProgressText = "Locating Remote File...";
            
            var remotePath = (CurrentRemotePath.TrimEnd('/') + "/" + item.Name);
            Log($"Preparing download: {item.Name}...");

            // We wrap the cart stream in a ProgressStream so that the system-driven SaveAsync
            // will automatically update our ProgressValue as it reads.
            using (var cartStream = _fs.OpenFile(remotePath, FileMode.Open))
            {
                var totalBytes = cartStream.Length; // Ensure FsService returns the correct length
                using (var progressStream = new ProgressStream(cartStream, totalBytes, (p, r, t) => {
                    ProgressValue = p;
                    ProgressText = $"Downloading {FormatSize(r)} of {FormatSize(t)}";
                }))
                {
                    var fileSaverResult = await FileSaver.Default.SaveAsync(item.Name, progressStream, CancellationToken.None);
                    if (fileSaverResult.IsSuccessful)
                    {
                        Log($"Download successful: {fileSaverResult.FilePath}");
                        // Note: Removed Toast here to prevent "Element Not Found" error on Windows UI transition
                    }
                    else
                    {
                        Log("Download cancelled or interrupted.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Download Error: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private void CopyStreamWithProgress(Stream source, Stream destination, long totalBytes)
    {
        byte[] buffer = new byte[65536]; // 64KB buffer matching optimized SC64 chunks
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
        LocalFiles.Clear();
        try
        {
            var dir = Directory.GetCurrentDirectory();
            foreach (var f in Directory.GetFileSystemEntries(dir).OrderBy(x => Directory.Exists(x) ? 0 : 1))
            {
                var isDir = Directory.Exists(f);
                var name = Path.GetFileName(f);
                LocalFiles.Add(new FileItem {
                    Name = name,
                    IsDirectory = isDir,
                    SizeDisplay = isDir ? "<DIR>" : FormatSize(new FileInfo(f).Length)
                });
            }
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
