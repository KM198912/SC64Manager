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
                var timeout = DateTime.Now.AddMilliseconds(2000); 

                // 1. Sliding Window Sync: Find the start of a valid packet marker (C, E, or P)
                bool synced = false;
                while (!synced && DateTime.Now < timeout)
                {
                    if (_serial.BytesToRead > 0)
                    {
                        byte b = (byte)_serial.ReadByte();
                        if (b == (byte)'C' || b == (byte)'E' || b == (byte)'P')
                        {
                            respHeader[0] = b;
                            synced = true;
                        }
                    }
                    else { Thread.Sleep(1); }
                }

                if (!synced) return (true, null); // Final timeout

                // 2. Acquisition: Read the remaining 7 bytes of the header
                read = 1;
                while (read < 8 && DateTime.Now < timeout)
                {
                    if (_serial.BytesToRead > 0)
                    {
                        read += _serial.Read(respHeader, read, 8 - read);
                    }
                    else { Thread.Sleep(1); }
                }

                if (read < 8) return (true, null);

                string ident = Encoding.ASCII.GetString(respHeader, 0, 3);
                uint dataLen = ReadUInt32BE(respHeader, 4);

                // Protocol Sanity Check: Ident must be CMP, ERR or PKT
                if (ident != "CMP" && ident != "ERR" && ident != "PKT")
                {
                    // Still garbage: Try again
                    pktRetries++; 
                    continue; 
                }

                // Safety Limit: Prevent OutOfMemory by capping allocations at 4MB
                if (dataLen > 4 * 1024 * 1024)
                {
                    _serial.DiscardInBuffer();
                    return (true, null);
                }

                if (ident == "PKT")
                {
                    pktRetries++;
                    if (dataLen > 0)
                    {
                        byte[] junk = new byte[dataLen];
                        int jRead = 0;
                        while(jRead < (int)dataLen && DateTime.Now < timeout)
                        {
                            if (_serial.BytesToRead > 0) 
                                jRead += _serial.Read(junk, jRead, (int)dataLen - jRead);
                        }
                    }
                    continue; 
                }

                bool isError = ident == "ERR";
                if (dataLen > 0)
                {
                    byte[] respData = new byte[dataLen];
                    int dataRead = 0;
                    while (dataRead < (int)dataLen && DateTime.Now < timeout)
                    {
                        if (_serial.BytesToRead > 0)
                            dataRead += _serial.Read(respData, dataRead, (int)dataLen - dataRead);
                    }
                    if (dataRead < (int)dataLen) return (true, null); // Partial read timeout
                    return (isError, respData);
                }
                return (isError, Array.Empty<byte>());
            }
            return (true, null); // Hit retry limit
        }
        catch (Exception)
        {
            return (true, null);
        }
        finally
        {
            if (_serialLock.CurrentCount == 0) _serialLock.Release();
        }
    }

    public bool SdDeinit(bool force = false)
    {
        if (force) return true;
        var (err, _) = ExecuteCmd('i', 0, 0);
        return !err;
    }

    public string GetIdentifier()
    {
        var (err, data) = ExecuteCmd('v'); // Official IDENTIFIER_GET
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

    public (float voltage, float temperature) GetDiagnosticData()
    {
        var (err, data) = ExecuteCmd('%');
        if (err || data == null || data.Length < 16) return (0, 0);

        uint rawVersion = ReadUInt32BE(data, 0);
        bool isVersioned = (rawVersion & (1u << 31)) != 0;
        uint version = rawVersion & ~(1u << 31);

        if (isVersioned && version == 1)
        {
            // V2 Protocol (Versioned V1)
            float v = ReadUInt32BE(data, 4) / 1000.0f;
            float t = ReadUInt32BE(data, 8) / 10.0f;
            return (v, t);
        }

        return (0, 0);
    }

    public DateTime? GetRtcTime()
    {
        var (err, data) = ExecuteCmd('t');
        if (err || data == null || data.Length < 8) return null;

        try
        {
            int second = FromBcd(data[3]);
            int minute = FromBcd(data[2]);
            int hour = FromBcd(data[1]);
            int day = FromBcd(data[7]);
            int month = FromBcd(data[6]);
            int year = 1900 + (FromBcd(data[4]) * 100) + FromBcd(data[5]);

            return new DateTime(year, month, day, hour, minute, second);
        }
        catch { return null; }
    }

    public bool SetRtcTime(DateTime dt)
    {
        byte second = ToBcd((byte)dt.Second);
        byte minute = ToBcd((byte)dt.Minute);
        byte hour = ToBcd((byte)dt.Hour);
        byte day = ToBcd((byte)dt.Day);
        byte month = ToBcd((byte)dt.Month);
        byte year = ToBcd((byte)(dt.Year % 100));
        byte century = ToBcd((byte)((dt.Year - 1900) / 100));
        byte weekday = ToBcd((byte)((int)dt.DayOfWeek + 1));

        uint arg0 = (uint)((weekday << 24) | (hour << 16) | (minute << 8) | second);
        uint arg1 = (uint)((century << 24) | (year << 16) | (month << 8) | day);

        var (err, _) = ExecuteCmd('T', arg0, arg1);
        return !err;
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

    private static byte ToBcd(byte value) => (byte)((value / 10 << 4) | (value % 10));
    private static byte FromBcd(byte value) => (byte)((value >> 4) * 10 + (value & 0x0F));

    public void Dispose()
    {
        _serial?.Dispose();
        _serialLock.Dispose();
    }
}
