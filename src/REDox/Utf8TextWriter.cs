// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace REDox;

public sealed class Utf8TextWriter : IDisposable, IAsyncDisposable
{
    private readonly DocumentWriter _writer;
    private int _indent;

    private byte _indentByte;
    private byte _indentSize;
    private bool _indented;
    private byte[] _newLine = [];

    internal Utf8TextWriter()
    {
        _writer = new DocumentWriter();
        Settings = SerializerSettings.Default;
    }

    public Utf8TextWriter(SerializerSettings? settings = null, Utf8TextWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        _writer = new DocumentWriter();
        Settings = settings;
        Reset(settings.DefaultBufferSize, null, null, options);
    }

    public Utf8TextWriter(Stream stream, SerializerSettings? settings = null,
        Utf8TextWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;
        _writer = new DocumentWriter();
        Settings = settings;
        Reset(settings.DefaultBufferSize, stream, null, options);
    }

    public Utf8TextWriter(IBufferWriter<byte> bufferWriter, SerializerSettings? settings = null,
        Utf8TextWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        settings ??= SerializerSettings.Default;
        _writer = new DocumentWriter();
        Settings = settings;
        Reset(settings.DefaultBufferSize, null, bufferWriter, options);
    }

    public long BytesWritten => _writer.BytesWritten;

    public SerializerSettings Settings { get; private set; }

    public ValueTask DisposeAsync()
    {
        return _writer.DisposeAsync();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    public void Reset(SerializerSettings settings, Utf8TextWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Reset(settings.DefaultBufferSize, null, null, options);
    }

    public void Reset(Stream stream, SerializerSettings settings, Utf8TextWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Reset(settings.DefaultBufferSize, stream, null, options);
    }


    public void Reset(IBufferWriter<byte> bufferWriter, SerializerSettings settings,
        Utf8TextWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Reset(settings.DefaultBufferSize, null, bufferWriter, options);
    }

    private void Reset(int defaultBufferSize, Stream? stream, IBufferWriter<byte>? writer,
        Utf8TextWriteOptions options)
    {
        _writer.ResetInternal(defaultBufferSize, stream, writer);
        _indentByte = (byte)options.IndentCharacter;
        _newLine = options.Utf8NewLine;
        _indented = options.WriteIndented;
        _indentSize = (byte)options.IndentSize;
        _indent = 0;

        if (options.WriteBom)
        {
            Utf8Helper.WriteBom(_writer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNewLine()
    {
        _writer.WriteBytes(_newLine);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSpaceIfIndented()
    {
        if (_indented)
        {
            _writer.WriteByte((byte)' ');
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushIndent()
    {
        if (_indented)
        {
            _indent++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PopIndent()
    {
        if (_indented)
        {
            if (_indent <= 0)
            {
                throw new InvalidOperationException("Indentation stack is empty.");
            }

            _indent--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteIndentNewLine()
    {
        if (_indented)
        {
            WriteNewLine();
            WriteIndent();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteIndent()
    {
        if (_indented)
        {
            var count = _indentSize * _indent;
            if (count > 0)
            {
                _writer.Allocate(count).Fill(_indentByte);
            }
        }
    }

    public string EncodeToString()
    {
        if (_writer.IsStreaming)
        {
            throw new NotSupportedException();
        }

        return Utf8Helper.EncodeToString(_writer.EnumerateWrittenBuffers());
    }

    public byte[] Encode()
    {
        if (_writer.IsStreaming)
        {
            throw new NotSupportedException();
        }

        return _writer.ToArray();
    }

    public void Flush()
    {
        _writer.Flush();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return _writer.FlushAsync(cancellationToken);
    }

    public void WriteBom()
    {
        Utf8Helper.WriteBom(_writer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtf8Byte(byte value)
    {
        _writer.WriteByte(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNull()
    {
        WriteString("null"u8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool value)
    {
        WriteString(value ? "true"u8 : "false"u8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(ReadOnlySpan<char> value)
    {
        var maxByteCount = value.Length < 1024
            ? Encoding.UTF8.GetMaxByteCount(value.Length)
            : Encoding.UTF8.GetByteCount(value);

        var buf = _writer.BeginWrite(maxByteCount);
        var size = Encoding.UTF8.GetBytes(value, buf);
        _writer.EndWrite(size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var maxByteCount = value.Length < 1024
            ? Encoding.UTF8.GetMaxByteCount(value.Length)
            : Encoding.UTF8.GetByteCount(value);

        var buf = _writer.BeginWrite(maxByteCount);
        var size = Encoding.UTF8.GetBytes(value, buf);
        _writer.EndWrite(size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(ReadOnlySpan<byte> utf8Bytes)
    {
        _writer.WriteBytes(utf8Bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDecimal(decimal value)
    {
        var w = _writer;
        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(40), value, Settings.FloatFormatHandling));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value)
    {
        var w = _writer;
        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value, Settings.FloatFormatHandling));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingle(float value)
    {
        var w = _writer;
        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value, Settings.FloatFormatHandling));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHalf(Half value)
    {
        var w = _writer;
        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value, Settings.FloatFormatHandling));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        var w = _writer;
        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        var w = _writer;
        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value));
    }

    /// <summary>Writes a date and time using the serializer's date format, culture, and zone settings.</summary>
    public void WriteDateTime(DateTime value)
    {
        Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        var success = Utf8Helper.TryFormatTimestamp(value, Settings, buffer, out var bytesWritten);
        Debug.Assert(success);
        WriteString(buffer.Slice(0, bytesWritten));
    }

    /// <summary>Writes a date and time using the serializer's date format and culture, preserving its offset.</summary>
    public void WriteDateTimeOffset(DateTimeOffset value)
    {
        Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        var success = Utf8Helper.TryFormatTimestamp(value, Settings, buffer, out var bytesWritten);
        Debug.Assert(success);
        WriteString(buffer.Slice(0, bytesWritten));
    }

    /// <summary>Writes an ISO timestamp of the specified kind, independently of serializer date settings.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTimestamp(DateTime value, TimestampKind kind = TimestampKind.Default)
    {
        Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        var success = Utf8Helper.TryFormatTimestamp(value, kind, buffer, out var bytesWritten);
        Debug.Assert(success);
        WriteString(buffer.Slice(0, bytesWritten));
    }

    /// <summary>Writes an ISO timestamp preserving its offset, independently of serializer date settings.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTimestamp(DateTimeOffset value)
    {
        Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        var success = Utf8Helper.TryFormatTimestamp(value, buffer, out var bytesWritten);
        Debug.Assert(success);
        WriteString(buffer.Slice(0, bytesWritten));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid value)
    {
        var w = _writer;
        var bytes = w.BeginWrite(40);
        var success = Utf8Formatter.TryFormat(value, bytes, out var written);
        Debug.Assert(success);
        w.EndWrite(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> BeginWriteUtf8Bytes(int maxLength)
    {
        Debug.Assert(maxLength >= 0);
        return _writer.BeginWrite(maxLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndWriteUtf8Bytes(int length)
    {
        Debug.Assert(length >= 0);
        _writer.EndWrite(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AllocateUtf8Bytes(int length)
    {
        Debug.Assert(length >= 0);
        return _writer.Allocate(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByteString(ReadOnlySpan<byte> bytes, ByteStringKind kind = ByteStringKind.Default)
    {
        switch (kind)
        {
            case ByteStringKind.Guid:
                WriteGuid(new Guid(bytes, true));
                break;
            case ByteStringKind.Raw:
                WriteString(bytes);
                break;
            case ByteStringKind.Default:
            case ByteStringKind.Base64:
            case ByteStringKind.Base64Url:
            case ByteStringKind.Base16:
                var buf = _writer.BeginWrite(Utf8Helper.GetEncodedByteStringLength(bytes.Length, kind));
                var bytesWritten = Utf8Helper.EncodeByteString(bytes, buf, kind);
                _writer.EndWrite(bytesWritten);
                break;
            default:
                throw new ArgumentException($"Unsupported binary kind: {kind}", nameof(kind));
        }
    }
}