using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using NetGui.Models;

namespace NetGui.Services;

public class FsService
{
    private readonly SC64Device _device;
    private SC64Stream? _stream;
    private FatFileSystem? _fs;

    public FsService(SC64Device device) => _device = device;

    public bool Mount(Action<string>? log = null)
    {
        try
        {
            log?.Invoke("Enabling ROM write access...");
            _device.ExecuteCmd('C', 1, 1); // ROM_WRITE_ENABLE
            
            log?.Invoke("Power cycling SD interface...");
            _device.SdDeinit();
            Thread.Sleep(100);

            log?.Invoke("Initializing SD Card...");
            bool success = false;
            uint resCode = 0;
            
            for (int i = 0; i < 3; i++) // 3 retries for slow cards
            {
                (success, resCode) = _device.SdInit();
                if (success) break;
                Thread.Sleep(200);
            }

            if (!success)
            {
                string statusDesc = (resCode == 0) ? "No Card / Timeout" : $"Status Bits: {resCode:X8}";
                log?.Invoke($"SD Init failed (Final Attempt). {statusDesc}");
                return false;
            }

            // Create stream (using 128GB as dummy length for modern cards)
            _stream = new SC64Stream(_device, 128L * 1024 * 1024 * 1024);
            
            log?.Invoke("Detecting partitions...");
            var partitionTable = new BiosPartitionTable(_stream);
            var fatPartition = partitionTable.Partitions.FirstOrDefault(p => 
                p.BiosType == 0x0C || p.BiosType == 0x0B || p.BiosType == 0x0E || p.BiosType == 0x07);

            if (fatPartition == null)
            {
                log?.Invoke("Error: No FAT32 partition found.");
                return false;
            }

            log?.Invoke($"Found partition at sector {fatPartition.FirstSector}");
            
            // Open the partition sub-stream via DiscUtils
            var partStream = fatPartition.Open();
            _fs = new FatFileSystem(partStream);
            
            log?.Invoke("SD Card mounted successfully.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Mount error: {ex.Message}");
            return false;
        }
    }

    public List<FileItem> ListDir(string path = "/")
    {
        if (_fs == null) return new List<FileItem>();
        
        var results = new List<FileItem>();
        var hiddenItems = new HashSet<string> { "sc64menu.n64", "System Volume Information", ".Trash-1000", "menu" };

        if (path != "/" && !string.IsNullOrEmpty(path))
        {
            results.Add(new FileItem { Name = "..", IsDirectory = true, SizeDisplay = "<PARENT>" });
        }

        try
        {
            foreach (var entry in _fs.GetFileSystemEntries(path))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) name = entry;
                if (hiddenItems.Contains(name)) continue;

                var isDir = _fs.DirectoryExists(entry);
                long size = 0;
                try { if (!isDir) size = _fs.GetFileInfo(entry).Length; } catch { }

                results.Add(new FileItem
                {
                    Name = name,
                    IsDirectory = isDir,
                    Size = size,
                    SizeDisplay = isDir ? "<DIR>" : FormatSize(size)
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ListDir error: {ex.Message}");
        }

        return results
            .OrderByDescending(x => x.Name == "..")
            .ThenByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name.ToLower())
            .ToList();
    }

    public bool Delete(string path)
    {
        if (_fs == null) return false;
        try
        {
            if (_fs.DirectoryExists(path))
            {
                _fs.DeleteDirectory(path, true);
            }
            else
            {
                _fs.DeleteFile(path);
            }
            return true;
        }
        catch { return false; }
    }

    public Stream OpenFile(string path, FileMode mode)
    {
        if (_fs == null) throw new InvalidOperationException("Not mounted");
        return _fs.OpenFile(path, mode, FileAccess.ReadWrite);
    }

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double s = size;
        int i = 0;
        while (s >= 1024 && i < units.Length - 1)
        {
            s /= 1024;
            i++;
        }
        return $"{s:F1} {units[i]}";
    }

    public void Disconnect()
    {
        _fs?.Dispose();
        _fs = null;
        _stream?.Dispose();
        _stream = null;
    }
}
