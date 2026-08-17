using DiscUtils;
using DiscUtils.ExFat;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using NetGui.Models;

namespace NetGui.Services;

public class FsService
{
    private readonly SC64Device _device;
    private DiscFileSystem? _fs;
    private SC64Stream? _stream;
    private readonly SemaphoreSlim _fsLock = new(1, 1);

    public FsService(SC64Device device)
    {
        _device = device;
    }

    public bool Mount(Action<string> log)
    {
        _fsLock.Wait();
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
            _stream = new SC64Stream(_device, 128L * 1024 * 1024 * 1024); // Use large bounds
            
            log("FS: Scanning for BIOS/MBR partitions...");
            var partitionTable = new BiosPartitionTable(_stream);
            Stream partitionStream;
            if (partitionTable.Partitions.Count == 0)
            {
                log("FS: No primary partitions found. Attempting direct mount...");
                partitionStream = _stream;
            }
            else
            {
                log($"FS: Found {partitionTable.Partitions.Count} partitions. Using first partition.");
                partitionStream = partitionTable.Partitions[0].Open();
            }

            if (ExFatFileSystem.Detect(partitionStream))
            {
                log("FS: Detected exFAT filesystem.");
                partitionStream.Seek(0, SeekOrigin.Begin);
                _fs = new ExFatFileSystem(partitionStream);
            }
            else
            {
                partitionStream.Seek(0, SeekOrigin.Begin);
                _fs = new FatFileSystem(partitionStream);
            }

            log($"FS: Mount successful. Label: {_fs.FriendlyName}");
            return true;
        }
        catch (Exception ex)
        {
            log($"FS CRITICAL: {ex.Message}");
            return false;
        }
        finally { _fsLock.Release(); }
    }

    public List<FileItem> ListDir(string path)
    {
        var items = new List<FileItem>();
        if (_fs == null) return items;

        _fsLock.Wait();
        try
        {
            if (path != "/")
            {
                items.Add(new FileItem { Name = "..", IsDirectory = true, SizeDisplay = "<UP>" });
            }

            foreach (var dir in _fs.GetDirectories(path).ToList())
            {
                items.Add(new FileItem
                {
                    Name = Path.GetFileName(dir),
                    IsDirectory = true,
                    SizeDisplay = "<DIR>"
                });
            }

            foreach (var file in _fs.GetFiles(path).ToList())
            {
                var info = _fs.GetFileInfo(file);
                items.Add(new FileItem
                {
                    Name = Path.GetFileName(file),
                    IsDirectory = false,
                    SizeDisplay = FormatSize(info.Length)
                });
            }
        }
        catch { }
        finally { _fsLock.Release(); }

        return items;
    }

    // Wrap remaining operations
    public Stream OpenFile(string path, FileMode mode)
    {
        _fsLock.Wait();
        try
        {
            if (_fs == null) throw new InvalidOperationException("Not mounted");
            return _fs.OpenFile(path, mode);
        }
        finally { _fsLock.Release(); }
    }

    /// <summary>
    /// Atomically writes all bytes to a remote filesystem file, holding the filesystem
    /// lock for the entire open/write/close sequence to prevent corruption.
    /// </summary>
    public void WriteAllBytes(string path, byte[] data)
    {
        _fsLock.Wait();
        try
        {
            if (_fs == null) throw new InvalidOperationException("Not mounted");
            using var dest = _fs.OpenFile(path, FileMode.Create);
            dest.Write(data, 0, data.Length);
        }
        finally { _fsLock.Release(); }
    }

    public void DeleteFile(string path)
    {
        _fsLock.Wait();
        try { _fs?.DeleteFile(path); } finally { _fsLock.Release(); }
    }

    public void DeleteDirectory(string path, bool recursive = true)
    {
        _fsLock.Wait();
        try { _fs?.DeleteDirectory(path, recursive); } finally { _fsLock.Release(); }
    }

    public void Rename(string oldPath, string newPath, bool isDirectory)
    {
        _fsLock.Wait();
        try
        {
            if (_fs == null) return;
            if (isDirectory) _fs.MoveDirectory(oldPath, newPath);
            else _fs.MoveFile(oldPath, newPath);
        }
        finally { _fsLock.Release(); }
    }

    public void CreateDirectory(string path)
    {
        _fsLock.Wait();
        try { _fs?.CreateDirectory(path); } finally { _fsLock.Release(); }
    }

    public void Disconnect(bool hardwareDeinit = true)
    {
        _fsLock.Wait();
        try
        {
            _fs?.Dispose();
            _stream?.Dispose();
            if (hardwareDeinit) _device.SdDeinit();
            _fs = null;
            _stream = null;
        }
        finally { _fsLock.Release(); }
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
