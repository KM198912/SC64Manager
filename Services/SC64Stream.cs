using System.IO;

namespace NetGui.Services;

public class SC64Stream : Stream
{
    private readonly SC64Device _device;
    private long _position;
    private long _length;
    private readonly uint _bufferAddr;
    private const int SectorSize = 512;
    private const int MaxChunkSize = 124 * 1024; // 124KB buffer for safe SDRAM alignment
    
    private byte[]? _cachedData;
    private uint _cachedSectorStart;
    private uint _cachedSectorCount;

    public SC64Stream(SC64Device device, long length = 0, uint bufferAddr = 0x03F00000)
    {
        _device = device;
        _length = length;
        _bufferAddr = bufferAddr;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public void SetLengthValue(long length) => _length = length;

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => _position
        };
        return _position;
    }

    public override void SetLength(long value) => _length = value;

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = 0;
        int maxSectors = MaxChunkSize / SectorSize;

        while (bytesRead < count)
        {
            uint sector = (uint)(_position / SectorSize);
            int offsetInSector = (int)(_position % SectorSize);

            // 1. Check if the requested data is already in our burst cache
            if (_cachedData != null && sector >= _cachedSectorStart && sector < _cachedSectorStart + _cachedSectorCount)
            {
                int cacheOffset = (int)((sector - _cachedSectorStart) * SectorSize + offsetInSector);
                int availableInCache = _cachedData.Length - cacheOffset;
                int toCopy = Math.Min(count - bytesRead, availableInCache);
                
                if (toCopy > 0)
                {
                    Array.Copy(_cachedData, cacheOffset, buffer, offset + bytesRead, toCopy);
                    _position += toCopy;
                    bytesRead += toCopy;
                    continue;
                }
            }

            // 2. Cache miss: Fetch a new burst from the SD card
            int remaining = count - bytesRead;
            // Aim for at least 128 sectors (64KB) or whatever fits the buffer, but no more than 124KB
            uint secCountToFetch = (uint)Math.Clamp(remaining / SectorSize + 1, 1, maxSectors);
            
            var data = _device.SdReadSectors(sector, secCountToFetch, _bufferAddr);
            if (data == null) break;

            _cachedData = data;
            _cachedSectorStart = sector;
            _cachedSectorCount = secCountToFetch;
        }
        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _cachedData = null; 

        int bytesWritten = 0;
        int maxSectors = MaxChunkSize / SectorSize;

        while (bytesWritten < count)
        {
            uint sector = (uint)(_position / SectorSize);
            int offsetInSector = (int)(_position % SectorSize);
            int remainingInSector = SectorSize - offsetInSector;

            if (offsetInSector == 0 && count - bytesWritten >= SectorSize)
            {
                // Full sectors: Pack as many as possible into a single burst
                int sectorsToPack = Math.Min((count - bytesWritten) / SectorSize, maxSectors);
                int sizeToPack = sectorsToPack * SectorSize;
                
                byte[] chunkData = new byte[sizeToPack];
                Array.Copy(buffer, offset + bytesWritten, chunkData, 0, sizeToPack);
                
                if (_device.MemoryWrite(_bufferAddr, chunkData))
                {
                    _device.SdWriteSectors(sector, (uint)sectorsToPack, _bufferAddr);
                }

                _position += sizeToPack;
                bytesWritten += sizeToPack;
            }
            else
            {
                // Partial sector: Read-Modify-Write
                int toWrite = Math.Min(count - bytesWritten, remainingInSector);
                byte[] sectorData = _device.SdReadSectors(sector, 1, _bufferAddr) ?? new byte[SectorSize];
                Array.Copy(buffer, offset + bytesWritten, sectorData, offsetInSector, toWrite);
                
                if (_device.MemoryWrite(_bufferAddr, sectorData))
                {
                    _device.SdWriteSectors(sector, 1, _bufferAddr);
                }

                _position += toWrite;
                bytesWritten += toWrite;
            }
        }
    }
}
