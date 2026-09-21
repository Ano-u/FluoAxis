namespace FluoAxis.Import;

using System.IO;

/// <summary>
/// Provides a seekable, re-openable view over an untrusted input stream
/// while respecting <see cref="ImportLimits"/>. The input stream is never
/// closed by this scope: a seekable input is wrapped so that disposal is
/// not propagated and the original position is restored; a non-seekable
/// input is copied once into a bounded in-memory buffer. Open XML
/// requires a seekable stream, and inspection/reading traverse the
/// content multiple times (shared strings, then each sheet), so this
/// scope is the single entry point through which streams reach the
/// adapters.
/// </summary>
internal sealed class SeekableStreamScope : IDisposable
{
    private readonly Stream? _original;
    private readonly long _originalPosition;
    private readonly MemoryStream? _copy;
    private readonly NonDisposingStream? _wrapper;

    private SeekableStreamScope(Stream? original, long originalPosition, MemoryStream? copy, NonDisposingStream wrapper)
    {
        _original = original;
        _originalPosition = originalPosition;
        _copy = copy;
        _wrapper = wrapper;
    }

    /// <summary>Seekable stream positioned at the start of the content. Disposing it does not close the underlying input.</summary>
    public Stream Stream => _wrapper!;

    public long Length => _copy?.Length ?? _original!.Length;

    /// <summary>
    /// Creates a seekable view over <paramref name="input"/>. When the input
    /// is not seekable, it is fully copied subject to the byte cap.
    /// </summary>
    public static SeekableStreamScope Create(Stream input, ImportLimits limits)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(limits);

        if (!input.CanRead)
        {
            throw new ArgumentException("The input stream must be readable.", nameof(input));
        }

        long position = input.CanSeek ? input.Position : 0;
        if (input.CanSeek && input.Length - position > limits.MaxBytes)
        {
            throw new ImportLimitException(
                ImportErrorCodes.ImportFileTooLarge,
                $"The input exceeds the import size limit of {limits.MaxBytes} bytes.");
        }

        var wrapper = new NonDisposingStream(input);
        if (input.CanSeek)
        {
            input.Position = position;
            return new SeekableStreamScope(input, position, copy: null, wrapper);
        }

        var copy = new MemoryStream((int)Math.Min(limits.MaxBytes, int.MaxValue));
        try
        {
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limits.MaxBytes)
                {
                    throw new ImportLimitException(
                        ImportErrorCodes.ImportFileTooLarge,
                        $"The input exceeds the import size limit of {limits.MaxBytes} bytes.");
                }

                copy.Write(buffer, 0, read);
            }

            copy.Position = 0;
            return new SeekableStreamScope(null, 0, copy, new NonDisposingStream(copy));
        }
        catch
        {
            wrapper.Dispose();
            copy.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _copy?.Dispose();
        if (_original is not null && _original.CanSeek)
        {
            _original.Position = _originalPosition;
        }

        _wrapper?.Dispose();
    }
}

/// <summary>Stream wrapper whose Dispose does not close the wrapped stream. For transient in-method use only.</summary>
internal sealed class NonDisposingStream(Stream inner) : Stream
{
    private bool _disposed;

    public override bool CanRead => !_disposed && inner.CanRead;

    public override bool CanSeek => !_disposed && inner.CanSeek;

    public override bool CanWrite => !_disposed && inner.CanWrite;

    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return inner.Length;
        }
    }

    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return inner.Position;
        }

        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            inner.Position = value;
        }
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return inner.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        inner.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        inner.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Thrown internally when an import limit is exceeded; the import layer
/// converts it into a structured blocking error at the API boundary.
/// </summary>
internal sealed class ImportLimitException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
