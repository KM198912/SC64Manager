using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;

namespace NetGui.Services;

public record FirmwareVersion(ushort Major, ushort Minor, uint Revision)
{
    public override string ToString() => $"v{Major}.{Minor}.{Revision}";
}

public class SC64Device : IDisposable
{
    private SerialPort? _serial;
    private const int TimeoutMs = 30000;
    private readonly SemaphoreSlim _serialLock = new(1, 1);

    public bool IsConnected => _serial?.IsOpen ?? false;

    public List<string> GetAvailablePorts()
    {
        return SerialPort.GetPortNames().ToList();
    }

    public bool Connect(string portName)
    {
        try
        {
            _serial = new SerialPort(portName, 1000000)
            {
                ReadTimeout = TimeoutMs,
                WriteTimeout = TimeoutMs,
                DtrEnable = true,
                RtsEnable = true
            };
            _serial.Open();
            _serial.DiscardInBuffer();
            _serial.DiscardOutBuffer();
            return true;
        }
        catch { return false; }
    }

    public void Disconnect()
    {
        if (_serial != null && _serial.IsOpen) _serial.Close();
    }

    public bool StateReset()
    {
        var (err, _) = ExecuteCmd('R');
        return !err;
    }

    public bool ResetHandshake()
    {
        if (_serial == null || !_serial.IsOpen) return false;
        try
        {
            _serial.DtrEnable = true;
            var timeout = DateTime.Now.AddSeconds(2);
            while (!_serial.DsrHolding && DateTime.Now < timeout) Thread.Sleep(5);
            _serial.DtrEnable = false;
            timeout = DateTime.Now.AddSeconds(2);
            while (_serial.DsrHolding && DateTime.Now < timeout) Thread.Sleep(5);
            Thread.Sleep(100);
            return true;
        }
        catch { return false; }
    }

    public (bool error, byte[]? data) ExecuteCmd(char id, uint arg0 = 0, uint arg1 = 0, byte[]? data = null)
    {
        if (_serial == null || !_serial.IsOpen) return (true, null);

        _serialLock.Wait();
        try
        {
            byte[] header = new byte[12];
            header[0] = (byte)'C';
            header[1] = (byte)'M';
            header[2] = (byte)'D';
            header[3] = (byte)id;
            WriteUInt32BE(header, 4, arg0);
            WriteUInt32BE(header, 8, arg1);

            _serial.Write(header, 0, 12);
            if (data != null) _serial.Write(data, 0, data.Length);

            int pktRetries = 0;
            const int MaxPktRetries = 100;

            while (pktRetries < MaxPktRetries)
            {
                byte[] respHeader = new byte[8];
                int read = 0;
                while (read < 8)
                {
                    int r = _serial.Read(respHeader, read, 8 - read);
                    if (r == 0) return (true, null);
                    read += r;
                }

                string ident = Encoding.ASCII.GetString(respHeader, 0, 3);
                uint dataLen = ReadUInt32BE(respHeader, 4);

                if (ident == "PKT")
                {
                    pktRetries++;
                    if (dataLen > 0)
                    {
                        byte[] junk = new byte[dataLen];
                        int jRead = 0;
                        while(jRead < (int)dataLen) jRead += _serial.Read(junk, jRead, (int)dataLen - jRead);
                    }
                    continue; 
                }

                bool isError = ident == "ERR";
                if (dataLen > 0)
                {
                    byte[] respData = new byte[dataLen];
                    int dataRead = 0;
                    while (dataRead < (int)dataLen)
                    {
                        dataRead += _serial.Read(respData, dataRead, (int)dataLen - dataRead);
                    }
                    return (isError, respData);
                }
                return (isError, Array.Empty<byte>());
            }
            return (true, null); // Hit retry limit
        }
        catch { return (true, null); }
        finally { _serialLock.Release(); }
    }

    public string GetIdentifier()
    {
        var (err, data) = ExecuteCmd('v');
        if (err || data == null) return "Unknown";
        return Encoding.ASCII.GetString(data).TrimEnd('\0');
    }

    public FirmwareVersion? GetVersion()
    {
        var (err, data) = ExecuteCmd('V');
        if (err || data == null || data.Length < 8) return null;
        
        ushort major = (ushort)((data[0] << 8) | data[1]);
        ushort minor = (ushort)((data[2] << 8) | data[3]);
        uint rev = ReadUInt32BE(data, 4);
        return new FirmwareVersion(major, minor, rev);
    }

    public byte[]? MemoryRead(uint addr, uint size)
    {
        var (err, data) = ExecuteCmd('m', addr, size);
        return err ? null : data;
    }

    public bool MemoryWrite(uint addr, byte[] data)
    {
        var (err, _) = ExecuteCmd('M', addr, (uint)data.Length, data);
        return !err;
    }

    public bool UpdateFirmware(uint addr, uint length)
    {
        var (err, _) = ExecuteCmd('F', addr, length);
        return !err;
    }

    public byte GetCicStep()
    {
        var (err, data) = ExecuteCmd('?');
        if (err || data == null || data.Length < 8) return 0; // 0 = Unavailable
        return (byte)((data[7] >> 4) & 0x0F);
    }

    public (bool success, uint status) SdInit()
    {
        var (err, data) = ExecuteCmd('i', 0, 1); // Op 1: Init
        uint status = (data != null && data.Length >= 8) ? ReadUInt32BE(data, 4) : 0;
        return (!err, status);
    }

    public bool SdDeinit()
    {
        var (err, _) = ExecuteCmd('i', 0, 0); // Op 0: Deinit
        return !err;
    }

    public byte[]? SdReadSectors(uint sector, uint count, uint bufferAddr)
    {
        // 1. Move from SD -> Cart RAM
        byte[] sBuf = new byte[4];
        WriteUInt32BE(sBuf, 0, sector);
        var (errRead, _) = ExecuteCmd('s', bufferAddr, count, sBuf);
        if (errRead) return null;

        // 2. Hardware Sync Delay (Matches official protocol)
        Thread.Sleep(10);

        // 3. Fetch from Cart RAM -> PC
        return MemoryRead(bufferAddr, count * 512);
    }

    public bool SdWriteSectors(uint sector, uint count, uint bufferAddr)
    {
        byte[] sBuf = new byte[4];
        WriteUInt32BE(sBuf, 0, sector);
        var (err, _) = ExecuteCmd('S', bufferAddr, count, sBuf);
        return !err;
    }

    private static void WriteUInt32BE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    public void Dispose()
    {
        _serial?.Dispose();
        _serialLock.Dispose();
    }
}
