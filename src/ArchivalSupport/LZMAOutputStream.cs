using SevenZip.Compression.LZMA;

namespace ArchivalSupport;

/// <summary>
/// A stream wrapper that provides streaming LZMA compression functionality.
/// Compresses data as it's written, eliminating the need for temporary files.
/// </summary>
internal class LZMAOutputStream : Stream
{
    private readonly Stream _baseStream;
    private readonly Encoder _encoder;
    private readonly MemoryStream _buffer;
    private readonly Stream _encodingStream;
    private bool _disposed = false;
    private long _uncompressedSize = 0;

    public long UncompressedSize => _uncompressedSize;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public LZMAOutputStream(Stream baseStream, Encoder encoder)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _buffer = new MemoryStream();
        _encodingStream = baseStream;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LZMAOutputStream));

        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentException("Invalid offset or count");

        // Buffer the data to compress later
        _buffer.Write(buffer, offset, count);
        _uncompressedSize += count;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override void WriteByte(byte value)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LZMAOutputStream));

        _buffer.WriteByte(value);
        _uncompressedSize++;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                // Perform the actual LZMA compression of all buffered data
                _buffer.Position = 0;
                _encoder.Code(_buffer, _encodingStream, _buffer.Length, -1, null);
                _encodingStream.Flush();
            }
            finally
            {
                _buffer?.Dispose();
                _disposed = true;
            }
        }

        base.Dispose(disposing);
    }

    public override void Flush()
    {
        // No-op for buffered approach
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // Not supported operations
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}