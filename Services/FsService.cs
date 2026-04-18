using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using NetGui.Models;

namespace NetGui.Services;

public class FsService
{
    private readonly SC64Device _device;
    private FatFileSystem? _fatFs;
    private SC64Stream? _stream;

    public FsService(SC64Device device)
    {
        _device = device;
    }

    public bool Mount(Action<string> log)
    {
        try
        {
            log("FS: Power cycling SD interface...");
            _device.SdDeinit();
            Thread.Sleep(500);

            log("FS: Initializing SD Card...");
            var (initSuccess, status) = _device.SdInit();
            if (!initSuccess)
            {
                log($"FS ERROR: SdInit failed with status 0x{status:X8}");
                return false;
            }

            log("FS: Creating hardware stream (0x03F00000)...");
            _stream = new SC64Stream(_device, 128L * 1024 * 1024 * 1024); // Use large bounds, DiscUtils will handle real bounds
            
            log("FS: Scanning for BIOS/MBR partitions...");
            var partitionTable = new BiosPartitionTable(_stream);
            if (partitionTable.Partitions.Count == 0)
            {
                log("FS: No primary partitions found. Attempting direct FAT mount...");
                _fatFs = new FatFileSystem(_stream);
            }
            else
            {
                log($"FS: Found {partitionTable.Partitions.Count} partitions. Using first partition.");
                var partition = partitionTable.Partitions[0];
                _fatFs = new FatFileSystem(partition.Open());
            }

            log($"FS: Mount successful. Label: {_fatFs.FriendlyName}");
            return true;
        }
        catch (Exception ex)
        {
            log($"FS CRITICAL: {ex.Message}");
            return false;
        }
    }

    public List<FileItem> ListDir(string path)
    {
        var items = new List<FileItem>();
        if (_fatFs == null) return items;

        try
        {
            if (path != "/")
            {
                items.Add(new FileItem { Name = "..", IsDirectory = true, SizeDisplay = "<UP>" });
            }

            foreach (var dir in _fatFs.GetDirectories(path))
            {
                items.Add(new FileItem
                {
                    Name = Path.GetFileName(dir),
                    IsDirectory = true,
                    SizeDisplay = "<DIR>"
                });
            }

            foreach (var file in _fatFs.GetFiles(path))
            {
                var info = _fatFs.GetFileInfo(file);
                items.Add(new FileItem
                {
                    Name = Path.GetFileName(file),
                    IsDirectory = false,
                    SizeDisplay = FormatSize(info.Length)
                });
            }
        }
        catch { }

        return items;
    }

    public Stream OpenFile(string path, FileMode mode)
    {
        if (_fatFs == null) throw new InvalidOperationException("Not mounted");
        return _fatFs.OpenFile(path, mode);
    }

    public void DeleteFile(string path)
    {
        if (_fatFs == null) return;
        _fatFs.DeleteFile(path);
    }

    public void DeleteDirectory(string path, bool recursive = true)
    {
        if (_fatFs == null) return;
        _fatFs.DeleteDirectory(path, recursive);
    }

    public void Disconnect()
    {
        _fatFs?.Dispose();
        _stream?.Dispose();
        _device.SdDeinit();
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
