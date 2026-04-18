using System;
using System.IO;

namespace NetGui.Services;

public class ProgressStream : Stream
{
    private readonly Stream _innerStream;
    private readonly long _totalLength;
    private long _totalRead;
    private readonly Action<double, long, long> _onProgress;

    public ProgressStream(Stream innerStream, long totalLength, Action<double, long, long> onProgress)
    {
        _innerStream = innerStream;
        _totalLength = totalLength;
        _onProgress = onProgress;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _innerStream.Read(buffer, offset, count);
        if (read > 0)
        {
            _totalRead += read;
            double progress = _totalLength > 0 ? (double)_totalRead / _totalLength : 0;
            _onProgress?.Invoke(progress, _totalRead, _totalLength);
        }
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _innerStream.Dispose();
        base.Dispose(disposing);
    }
}
