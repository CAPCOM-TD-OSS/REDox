// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using REDox.Serialization;

namespace REDox.Json;

public sealed class JsonWriter : DataWriter, IDisposable, IAsyncDisposable
{
    private readonly DocumentWriter _writer;

    private JsonTextEncoder _encoder;
    private FloatFormatHandling _floatFormat;
    private byte _indentByte;
    private byte _indentSize;
    private bool _indented;
    private int _maxDepth;
    private byte[] _newLine = [];

    public JsonWriter(SerializerSettings? settings = null, JsonWriteOptions options = default)
    {
        RequiresStringKeys = true;
        settings ??= SerializerSettings.Default;
        _writer = new DocumentWriter();
        _encoder = settings.TextEncoderPolicy.GetEncoder<JsonTextEncoder>();
        _floatFormat = settings.FloatFormatHandling;
        Reset(null, null, settings, options);
    }

    public JsonWriter(Stream writeStream, SerializerSettings? settings = null, JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);

        RequiresStringKeys = true;
        settings ??= SerializerSettings.Default;
        _writer = new DocumentWriter();
        _encoder = settings.TextEncoderPolicy.GetEncoder<JsonTextEncoder>();
        _floatFormat = settings.FloatFormatHandling;
        Reset(writeStream, null, settings, options);
    }

    public JsonWriter(IBufferWriter<byte> bufferWriter, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);

        RequiresStringKeys = true;
        settings ??= SerializerSettings.Default;
        _writer = new DocumentWriter();
        _encoder = settings.TextEncoderPolicy.GetEncoder<JsonTextEncoder>();
        _floatFormat = settings.FloatFormatHandling;
        Reset(null, bufferWriter, settings, options);
    }


    public long BytesWritten => _writer.BytesWritten;

    public int CurrentDepth => _writer.ContextDepth;

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    public byte[] Encode()
    {
        return _writer.ToArray();
    }

    public void Flush()
    {
        _writer.Flush();
    }

    public async ValueTask FlushAsync()
    {
        await _writer.FlushAsync();
    }

    public string EncodeToString()
    {
        return Utf8Helper.EncodeToString(_writer.EnumerateWrittenBuffers());
    }

    public void Reset(SerializerSettings settings, JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Reset(null, null, settings, options);
    }

    public void Reset(Stream writeStream, SerializerSettings settings, JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);
        ArgumentNullException.ThrowIfNull(settings);

        Reset(writeStream, null, settings, options);
    }

    public void Reset(IBufferWriter<byte> bufferWriter, SerializerSettings settings, JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        ArgumentNullException.ThrowIfNull(settings);

        Reset(null, bufferWriter, settings, options);
    }

    private void Reset(Stream? stream, IBufferWriter<byte>? writer, SerializerSettings settings,
        JsonWriteOptions options)
    {
        base.Reset(settings);

        _writer.ResetInternal(settings.DefaultBufferSize, stream, writer);

        _indentByte = (byte)options.IndentCharacter;
        _newLine = options.Utf8NewLine;
        _indented = options.WriteIndented;
        _indentSize = (byte)options.IndentSize;
        _maxDepth = options.MaxDepth == 0 ? SerializerSettings.DefaultMaxDepth : options.MaxDepth;
        _encoder = settings.TextEncoderPolicy.GetEncoder<JsonTextEncoder>();
        _floatFormat = settings.FloatFormatHandling;

        if (options.WriteBom)
        {
            Utf8Helper.WriteBom(_writer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextIndentedElement()
    {
        var w = _writer;

        if (w.ContextDepth > 0)
        {
            ref var state = ref w.PeekContext();

            if (state < 0)
            {
                if (state == -1)
                {
                    WriteIndentNewLine();
                }

                if (--state < -2)
                {
                    if ((state & 1) == 0)
                    {
                        w.WriteByte((byte)',');
                        WriteIndentNewLine();
                    }
                    else
                    {
                        w.WriteByte((byte)':');
                        w.WriteByte((byte)' ');
                    }
                }
            }
            else
            {
                if (state++ > 0)
                {
                    w.WriteByte((byte)',');
                }

                WriteIndentNewLine();
            }
        }
    }

    public override void WriteNull()
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.WriteBytes("null"u8);
    }

    public override void WriteChar(char value)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(_encoder.Encode(w.BeginWrite(8), new ReadOnlySpan<char>(ref value)));
    }

    public override void WriteDecimal(decimal value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(40), value, _floatFormat));
    }

    public override void WriteBigNumber(ReadOnlySpan<byte> value, BigNumberKind kind = BigNumberKind.Default)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.WriteBytes(value);
    }

    public override void WriteDateTime(DateTime value)
    {
        Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        var success = Utf8Helper.TryFormatTimestamp(value, null, Settings.DateFormatString, Settings.Culture,
            Settings.DateFormatHandling, buffer, out var bytes);
        Debug.Assert(success);
        if (!string.IsNullOrEmpty(Settings.DateFormatString))
        {
            WriteString(buffer.Slice(0, bytes));
        }
        else
        {
            WriteRawString(buffer.Slice(0, bytes));
        }
    }

    public override void WriteDateTimeOffset(DateTimeOffset value)
    {
        Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        var success = Utf8Helper.TryFormatTimestamp(value.DateTime, value.Offset, Settings.DateFormatString, Settings.Culture,
            Settings.DateFormatHandling, buffer, out var bytes);
        Debug.Assert(success);
        if (!string.IsNullOrEmpty(Settings.DateFormatString))
        {
            WriteString(buffer.Slice(0, bytes));
        }
        else
        {
            WriteRawString(buffer.Slice(0, bytes));
        }
    }

    protected internal override void WriteBooleanValues(ReadOnlySpan<bool> values)
    {
        var w = _writer;

        if (_indented)
        {
            base.WriteBooleanValues(values);
            return;
        }

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(values.Length * 6);
        var pt = 0;
        var separator = state > 0;

        foreach (var value in values)
        {
            if (separator)
            {
                buf[pt++] = (byte)',';
            }

            separator = true;

            if (value)
            {
                "true"u8.CopyTo(buf.Slice(pt));
                pt += 4;
            }
            else
            {
                "false"u8.CopyTo(buf.Slice(pt));
                pt += 5;
            }
        }

        state += values.Length;

        w.EndWrite(pt);
    }

    protected internal override void WriteInt32Values(ReadOnlySpan<int> values)
    {
        var w = _writer;

        if (_indented)
        {
            base.WriteInt32Values(values);
            return;
        }

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(values.Length * 24);
        var pt = 0;
        var separator = state > 0;

        foreach (var value in values)
        {
            if (separator)
            {
                buf[pt++] = (byte)',';
            }

            separator = true;

            pt += Utf8Helper.EncodeNumber(buf.Slice(pt), value);
        }

        state += values.Length;

        w.EndWrite(pt);
    }

    protected internal override void WriteDoubleValues(ReadOnlySpan<double> values)
    {
        var w = _writer;

        if (_indented)
        {
            base.WriteDoubleValues(values);
            return;
        }

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(values.Length * 32);
        var pt = 0;
        var separator = state > 0;

        foreach (var value in values)
        {
            if (separator)
            {
                buf[pt++] = (byte)',';
            }

            separator = true;

            pt += Utf8Helper.EncodeNumber(buf.Slice(pt), value, _floatFormat);
        }

        state += values.Length;

        w.EndWrite(pt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodePropertyName(Span<byte> buf, Utf8Symbol propertyName, bool separate)
    {
        var pt = 0;

        if (separate)
        {
            buf[pt++] = (byte)',';
        }

        buf[pt++] = (byte)'"';
        propertyName.AsSpan().CopyTo(buf.Slice(pt));
        pt += propertyName.ByteLength;
        buf[pt++] = (byte)'"';
        buf[pt++] = (byte)':';

        return pt;
    }

    protected internal override void WritePropertyInt64(Utf8Symbol propertyName, long value)
    {
        if (_indented || (propertyName.EscapeMask & _encoder.EscapeMask) != 0)
        {
            base.WritePropertyInt64(propertyName, value);
            return;
        }

        var w = _writer;

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(propertyName.ByteLength + 32);
        var pt = EncodePropertyName(buf, propertyName, state < -2);
        pt += Utf8Helper.EncodeNumber(buf.Slice(pt), value);
        w.EndWrite(pt);

        state -= 2;
    }

    protected internal override void WritePropertyInt32(Utf8Symbol propertyName, int value)
    {
        var w = _writer;

        if (_indented || (propertyName.EscapeMask & _encoder.EscapeMask) != 0)
        {
            base.WritePropertyInt32(propertyName, value);
            return;
        }

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(propertyName.ByteLength + 64);
        var pt = EncodePropertyName(buf, propertyName, state < -2);
        pt += Utf8Helper.EncodeNumber(buf.Slice(pt), value);
        w.EndWrite(pt);

        state -= 2;
    }

    protected internal override void WritePropertyString(Utf8Symbol propertyName, string? value)
    {
        var w = _writer;

        if (_indented || value == null || (propertyName.EscapeMask & _encoder.EscapeMask) != 0)
        {
            base.WritePropertyString(propertyName, value);
            return;
        }

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(propertyName.ByteLength + value.Length * 6 + 8);
        var pt = EncodePropertyName(buf, propertyName, state < -2);
        pt += _encoder.Encode(buf.Slice(pt), value);
        w.EndWrite(pt);

        state -= 2;
    }

    protected internal override void WritePropertyBoolean(Utf8Symbol propertyName, bool value)
    {
        var w = _writer;

        if (_indented || (propertyName.EscapeMask & _encoder.EscapeMask) != 0)
        {
            base.WritePropertyBoolean(propertyName, value);
            return;
        }

        ref var state = ref w.PeekContext();

        var buf = w.BeginWrite(propertyName.ByteLength + 16);
        var pt = EncodePropertyName(buf, propertyName, state < -2);

        if (value)
        {
            "true"u8.CopyTo(buf.Slice(pt));
            pt += 4;
        }
        else
        {
            "false"u8.CopyTo(buf.Slice(pt));
            pt += 5;
        }

        w.EndWrite(pt);

        state -= 2;
    }

    public override void WriteStartArray(int? definiteLength = null)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        if (w.ContextDepth >= _maxDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum depth of {_maxDepth}.");
        }

        w.WriteByte((byte)'[');
        w.PushContext(0);
    }

    public override void WriteEndArray()
    {
        var w = _writer;

        var state = w.PopContext();

        if (state > 0)
        {
            if (_indented)
            {
                WriteIndentNewLine();
            }
        }

        w.WriteByte((byte)']');
    }

    public override void WriteStartMap(int? definiteLength = null)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        if (w.ContextDepth >= _maxDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum depth of {_maxDepth}.");
        }

        w.WriteByte((byte)'{');
        w.PushContext(-1);
    }

    public override void WriteEndMap()
    {
        var w = _writer;

        var state = w.PopContext();
        if (state < -1)
        {
            if (_indented)
            {
                WriteIndentNewLine();
            }
        }

        w.WriteByte((byte)'}');
    }

    public override void WriteGuid(Guid value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        WriteGuidString(w, value);
    }

    public override void WriteByteString(ReadOnlySpan<byte> value, ByteStringKind kind = ByteStringKind.Default)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        WriteByteString(w, value, kind);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void NextElement(DocumentWriter w)
    {
        if (w.ContextDepth > 0)
        {
            ref var state = ref w.PeekContext();

            if (state < 0)
            {
                if (--state < -2)
                {
                    if ((state & 1) == 0)
                    {
                        w.WriteByte((byte)',');
                    }
                    else
                    {
                        w.WriteByte((byte)':');
                    }
                }
            }
            else
            {
                if (state++ > 0)
                {
                    w.WriteByte((byte)',');
                }
            }
        }
    }

    public override void WriteInt32(int value)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value));
    }

    public override void WriteUInt32(uint value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value));
    }

    public override void WriteInt64(long value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value));
    }

    public override void WriteUInt64(ulong value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value));
    }

    public override void WriteHalf(Half value)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value, _floatFormat));
    }

    public override void WriteSingle(float value)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value, _floatFormat));
    }

    public override void WriteDouble(double value)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.EndWrite(Utf8Helper.EncodeNumber(w.BeginWrite(32), value, _floatFormat));
    }

    public override void WriteNumberString(ReadOnlySpan<byte> utf8Bytes)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        WriteStringValue(w, utf8Bytes);
    }

    public override void WriteBoolean(bool value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        w.WriteBytes(value ? "true"u8 : "false"u8);
    }

    public override void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        var buf = w.BeginWrite(value.Length * 6 + 2);
        w.EndWrite(_encoder.Encode(buf, value));
    }

    public override void WriteString(ReadOnlySpan<char> value)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        var buf = w.BeginWrite(value.Length * 6 + 2);
        w.EndWrite(_encoder.Encode(buf, value));
    }

    public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        var buf = w.BeginWrite(utf8Bytes.Length * 6 + 2);
        w.EndWrite(_encoder.Encode(buf, utf8Bytes));
    }

    internal void WriteRawString(ReadOnlySpan<byte> value)
    {
        var w = _writer;

        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        WriteStringValue(w, value);
    }

    public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
    {
        var w = _writer;
        if (_indented)
        {
            NextIndentedElement();
        }
        else
        {
            NextElement(w);
        }

        if ((value.EscapeMask & _encoder.EscapeMask) != 0)
        {
            var buf = w.BeginWrite(value.ByteLength * 6 + 2);
            w.EndWrite(_encoder.Encode(buf, value));
        }
        else
        {
            WriteStringValue(w, value);
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringValue(DocumentWriter writer, ReadOnlySpan<byte> utf8Bytes)
    {
        var byteCount = utf8Bytes.Length + 2;
        var bytes = writer.BeginWrite(byteCount);
        bytes[0] = (byte)'"';
        utf8Bytes.CopyTo(bytes.Slice(1));
        bytes[utf8Bytes.Length + 1] = (byte)'"';
        writer.EndWrite(byteCount);
    }

    private static void WriteGuidString(DocumentWriter writer, Guid value)
    {
        var bytes = writer.BeginWrite(40);
        bytes[0] = (byte)'"';
        var success = Utf8Formatter.TryFormat(value, bytes.Slice(1), out var written);
        Debug.Assert(success);
        bytes[written + 1] = (byte)'"';
        writer.EndWrite(written + 2);
    }

    private static void WriteByteString(DocumentWriter writer, ReadOnlySpan<byte> bytes, ByteStringKind kind)
    {
        switch (kind)
        {
            case ByteStringKind.Guid:
                WriteGuidString(writer, new Guid(bytes, true));
                break;
            default:
                {
                    var encodeKind = kind is ByteStringKind.Raw or ByteStringKind.Inherit
                        ? ByteStringKind.Default
                        : kind;
                    var buf = writer.BeginWrite(Utf8Helper.GetEncodedByteStringLength(bytes.Length, encodeKind) + 2);
                    buf[0] = (byte)'"';
                    var bytesWritten = Utf8Helper.EncodeByteString(bytes, buf.Slice(1), encodeKind);
                    buf[bytesWritten + 1] = (byte)'"';
                    writer.EndWrite(bytesWritten + 2);
                }
                break;
        }
    }

    public void WriteNewLine()
    {
        _writer.WriteBytes(_newLine);
    }

    private void WriteIndentNewLine()
    {
        WriteNewLine();
        WriteIndent();
    }

    private void WriteIndent()
    {
        var depth = CurrentDepth * _indentSize;

        if (depth > 0)
        {
            _writer.Allocate(depth).Fill(_indentByte);
        }
    }
}