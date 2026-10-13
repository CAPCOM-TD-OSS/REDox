// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using REDox.Serialization;

namespace REDox.MessagePack;

public sealed class MessagePackWriter : DataWriter, IDisposable, IAsyncDisposable
{
    private readonly DocumentWriter _writer;
    private int _maxDepth;

    public MessagePackWriter(SerializerSettings? settings = null, MessagePackWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        _writer = new DocumentWriter();
        Reset(settings, options);
    }

    public MessagePackWriter(Stream writeStream, SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);
        settings ??= SerializerSettings.Default;

        _writer = new DocumentWriter();
        Reset(writeStream, settings, options);
    }

    public MessagePackWriter(IBufferWriter<byte> bufferWriter, SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        settings ??= SerializerSettings.Default;

        _writer = new DocumentWriter();
        Reset(bufferWriter, settings, options);
    }

    public long BytesWritten => _writer.BytesWritten;

    public int CurrentDepth => _writer.ContextDepth;

    public bool PreserveExtension { get; private set; }

    public bool OldSpec { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    public void Reset(SerializerSettings settings, MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Reset(null, null, settings, options);
    }

    public void Reset(Stream writeStream, SerializerSettings settings, MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);
        ArgumentNullException.ThrowIfNull(settings);

        Reset(writeStream, null, settings, options);
    }

    public void Reset(IBufferWriter<byte> bufferWriter, SerializerSettings settings,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        ArgumentNullException.ThrowIfNull(settings);

        Reset(null, bufferWriter, settings, options);
    }

    private void Reset(Stream? stream, IBufferWriter<byte>? writer, SerializerSettings settings,
        MessagePackWriteOptions options)
    {
        base.Reset(settings);

        _writer.ResetInternal(settings.DefaultBufferSize, stream, writer);
        _maxDepth = options.MaxDepth == 0 ? SerializerSettings.DefaultMaxDepth : options.MaxDepth;
        PreserveExtension = options.PreserveExtension;
        OldSpec = options.OldSpec;
    }

    public void Flush()
    {
        _writer.Flush();
    }

    public byte[] Encode()
    {
        return _writer.ToArray();
    }

    public void WriteExtension(byte type, ReadOnlySpan<byte> data)
    {
        NextElement();
        WriteExtensionValue(_writer, type, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextElement()
    {
        if (_writer.ContextDepth > 0)
        {
            ref var state = ref _writer.PeekContext();

            if ((int)state == 0)
            {
                throw new InvalidOperationException("The current MessagePack container has no remaining elements.");
            }

            state--;
        }
    }

    public override void WriteNull()
    {
        NextElement();
        WriteNullValue(_writer);
    }

    public override void WriteChar(char value)
    {
        NextElement();
        WritePlusIntegerValue(_writer, value);
    }

    public override void WriteGuid(Guid value)
    {
        NextElement();
        WriteGuidValue(_writer, value, OldSpec);
    }

    public override void WriteDateTime(DateTime value)
    {
        if (OldSpec)
        {
            WriteInt64(value.ToBinary());
            return;
        }

        NextElement();
        WriteDateTimeValue(_writer, value);
    }

    public override void WriteDateTimeOffset(DateTimeOffset value)
    {
        NextElement();
        WriteDateTimeOffsetValue(_writer, value);
    }

    public override void WriteByteString(ReadOnlySpan<byte> value, ByteStringKind kind = ByteStringKind.Default)
    {
        NextElement();
        if (OldSpec)
        {
            WriteStringValue(_writer, value, true);
        }
        else
        {
            WriteByteStringValue(_writer, value, kind);
        }
    }

    public override void WriteStartArray(int? definiteLength)
    {
        var w = _writer;

        if (w.ContextDepth >= _maxDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum depth of {_maxDepth}.");
        }

        NextElement();

        if (definiteLength == null)
        {
            w.PushContext(long.MaxValue);
            w.PushPlaceholder(5);
        }
        else
        {
            w.PushContext(definiteLength.Value);
            WriteArray(w, definiteLength.Value);
        }
    }

    public override void WriteEndArray()
    {
        var context = _writer.PopContext();

        if (context > int.MaxValue)
        {
            var count = (int)(long.MaxValue - context);
            Span<byte> buf = stackalloc byte[5];

            if (count <= 0xf)
            {
                buf[0] = (byte)((int)MessagePackCode.FixArray | (count & 0xf));
                buf = buf.Slice(0, 1);
            }
            else
            {
                if (count <= 0xffff)
                {
                    buf[0] = (byte)MessagePackCode.Array16;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)count);
                    buf = buf.Slice(0, 3);
                }
                else
                {
                    buf[0] = (byte)MessagePackCode.Array32;
                    BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)count);
                    buf = buf.Slice(0, 5);
                }
            }

            _writer.PopAndPatch(buf);
        }
        else
        {
            if ((int)context != 0)
            {
                throw new InvalidOperationException((int)context > 0
                    ? $"Cannot end the array because it still has {(int)context} unwritten element(s)."
                    : $"Cannot end the array because it has {-(int)context} extra written element(s).");
            }
        }
    }

    public override void WriteStartMap(int? definiteLength)
    {
        var w = _writer;

        if (w.ContextDepth >= _maxDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum depth of {_maxDepth}.");
        }

        NextElement();

        if (definiteLength == null)
        {
            w.PushContext(long.MaxValue);
            w.PushPlaceholder(5);
        }
        else
        {
            w.PushContext(definiteLength.Value * 2);
            WriteObject(w, definiteLength.Value);
        }
    }

    public override void WriteEndMap()
    {
        var context = _writer.PopContext();

        if (context > int.MaxValue)
        {
            var count = (int)(long.MaxValue - context);

            if ((count & 1) != 0)
            {
                throw new InvalidOperationException(
                    "MessagePack map incomplete; each key must be followed by a corresponding value.");
            }

            count /= 2;

            Span<byte> buf = stackalloc byte[5];

            if (count <= 0xf)
            {
                buf[0] = (byte)((int)MessagePackCode.FixMap | (count & 0xf));
                buf = buf.Slice(0, 1);
            }
            else
            {
                if (count <= 0xffff)
                {
                    buf[0] = (byte)MessagePackCode.Map16;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)count);
                    buf = buf.Slice(0, 3);
                }
                else
                {
                    buf[0] = (byte)MessagePackCode.Map32;
                    BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)count);
                    buf = buf.Slice(0, 5);
                }
            }

            _writer.PopAndPatch(buf);
        }
        else
        {
            if ((int)context != 0)
            {
                throw new InvalidOperationException((int)context > 0
                    ? $"Cannot end the map because it still has {(int)context} unwritten item(s)."
                    : $"Cannot end the map because it has {-(int)context} extra written item(s).");
            }
        }
    }

    public override void WriteDecimal(decimal value)
    {
        NextElement();
        WriteDecimalValue(_writer, value, OldSpec);
    }

    public override void WriteBigNumber(ReadOnlySpan<byte> value, BigNumberKind kind = BigNumberKind.Default)
    {
        NextElement();
        WriteBigNumberValue(_writer, value, kind, OldSpec);
    }

    public override void WriteInt64(long value)
    {
        NextElement();

        if (value < 0)
        {
            WriteMinusIntegerValue(_writer, value);
        }
        else
        {
            WritePlusIntegerValue(_writer, (ulong)value);
        }
    }

    public override void WriteUInt64(ulong value)
    {
        NextElement();
        WritePlusIntegerValue(_writer, value);
    }

    public override void WriteHalf(Half value)
    {
        NextElement();
        WriteSingleValue(_writer, (float)value);
    }

    public override void WriteSingle(float value)
    {
        NextElement();
        WriteSingleValue(_writer, value);
    }

    public override void WriteDouble(double value)
    {
        NextElement();
        WriteDoubleValue(_writer, value);
    }

    public override void WriteBoolean(bool value)
    {
        NextElement();
        WriteBooleanValue(_writer, value);
    }

    public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
    {
        NextElement();
        WriteStringValue(_writer, utf8Bytes, OldSpec);
    }

    public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
    {
        NextElement();
        WriteStringValue(_writer, value, OldSpec);
    }

    protected internal override void WriteDoubleValues(ReadOnlySpan<double> values)
    {
        var w = _writer;

        ref var state = ref w.PeekContext();
        state -= values.Length;

        var buf = w.Allocate(values.Length * 9);

        var i = 0;
        foreach (var value in values)
        {
            buf[i] = (byte)MessagePackCode.Float64;
            BinaryPrimitives.WriteDoubleBigEndian(buf.Slice(i + 1), value);
            i += 9;
        }
    }

    protected internal override void WritePropertyString(Utf8Symbol propertyName, string? value)
    {
        var w = _writer;

        if (w.ContextDepth > 0)
        {
            ref var state = ref w.PeekContext();
            state -= 2;
        }

        WriteStringValue(w, propertyName, OldSpec);
        if (value != null)
        {
            WriteStringValue(w, value, OldSpec);
        }
        else
        {
            WriteNullValue(w);
        }
    }

    protected internal override void WritePropertyBoolean(Utf8Symbol propertyName, bool value)
    {
        var w = _writer;

        if (w.ContextDepth > 0)
        {
            ref var state = ref w.PeekContext();
            state -= 2;
        }

        WriteStringValue(w, propertyName, OldSpec);
        WriteBooleanValue(w, value);
    }

    protected internal override void WritePropertyInt32(Utf8Symbol propertyName, int value)
    {
        var w = _writer;

        if (w.ContextDepth > 0)
        {
            ref var state = ref w.PeekContext();
            state -= 2;
        }

        WriteStringValue(w, propertyName, OldSpec);

        if (value < 0)
        {
            WriteMinusIntegerValue(w, value);
        }
        else
        {
            WritePlusIntegerValue(w, (ulong)value);
        }
    }

    protected internal override void WritePropertyInt64(Utf8Symbol propertyName, long value)
    {
        var w = _writer;

        if (w.ContextDepth > 0)
        {
            ref var state = ref w.PeekContext();
            state -= 2;
        }

        WriteStringValue(w, propertyName, OldSpec);

        if (value < 0)
        {
            WriteMinusIntegerValue(w, value);
        }
        else
        {
            WritePlusIntegerValue(w, (ulong)value);
        }
    }

    public override void WriteString(string value)
    {
        NextElement();
        WriteStringValue(_writer, value, OldSpec);
    }

    public override void WriteString(ReadOnlySpan<char> value)
    {
        NextElement();
        WriteStringValue(_writer, value, OldSpec);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteObject(DocumentWriter writer, int count)
    {
        if (count <= 0xf)
        {
            writer.WriteByte((byte)((int)MessagePackCode.FixMap | (count & 0xf)));
        }
        else
        {
            if (count <= 0xffff)
            {
                var buf = writer.Allocate(3);
                buf[0] = (byte)MessagePackCode.Map16;
                BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)count);
            }
            else
            {
                var buf = writer.Allocate(5);
                buf[0] = (byte)MessagePackCode.Map32;
                BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)count);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteArray(DocumentWriter writer, int count)
    {
        if (count <= 0xf)
        {
            writer.WriteByte((byte)((int)MessagePackCode.FixArray | (count & 0xf)));
        }
        else
        {
            if (count <= 0xffff)
            {
                var buf = writer.Allocate(3);
                buf[0] = (byte)MessagePackCode.Array16;
                BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)count);
            }
            else
            {
                var buf = writer.Allocate(5);
                buf[0] = (byte)MessagePackCode.Array32;
                BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)count);
            }
        }
    }

    private static void WriteStringValue(DocumentWriter writer, ReadOnlySpan<char> str, bool oldSpec)
    {
        var length = Encoding.UTF8.GetByteCount(str);

        var buf = writer.BeginWrite(length + 5);
        var headerSize = 0;

        if (length <= 0x1f)
        {
            buf[0] = (byte)((int)MessagePackCode.FixStr | (length & 0x1f));
            headerSize = 1;
        }
        else
        {
            if (length <= 0xff && !oldSpec)
            {
                buf[0] = (byte)MessagePackCode.Str8;
                buf[1] = (byte)length;
                headerSize = 2;
            }
            else
            {
                if (length <= 0xffff)
                {
                    buf[0] = (byte)MessagePackCode.Str16;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)length);
                    headerSize = 3;
                }
                else
                {
                    buf[0] = (byte)MessagePackCode.Str32;
                    BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)length);
                    headerSize = 5;
                }
            }
        }

        Encoding.UTF8.GetBytes(str, buf.Slice(headerSize));

        writer.EndWrite(length + headerSize);
    }

    private static void WriteStringValue(DocumentWriter writer, ReadOnlySpan<byte> str, bool oldSpec)
    {
        if (str.Length <= 0x1f)
        {
            var buf = writer.Allocate(str.Length + 1);
            buf[0] = (byte)((int)MessagePackCode.FixStr | (str.Length & 0x1f));
            str.CopyTo(buf.Slice(1));
        }
        else
        {
            if (str.Length <= 0xff && !oldSpec)
            {
                var buf = writer.Allocate(str.Length + 2);
                buf[0] = (byte)MessagePackCode.Str8;
                buf[1] = (byte)str.Length;
                str.CopyTo(buf.Slice(2));
            }
            else
            {
                if (str.Length <= 0xffff)
                {
                    var buf = writer.Allocate(str.Length + 3);
                    buf[0] = (byte)MessagePackCode.Str16;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)str.Length);
                    str.CopyTo(buf.Slice(3));
                }
                else
                {
                    var buf = writer.Allocate(str.Length + 5);
                    buf[0] = (byte)MessagePackCode.Str32;
                    BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)str.Length);
                    str.CopyTo(buf.Slice(5));
                }
            }
        }
    }

    private static void WriteExtensionValue(DocumentWriter writer, byte type, ReadOnlySpan<byte> data)
    {
        if (data.Length == 1)
        {
            var buf = writer.Allocate(3);
            buf[0] = (byte)MessagePackCode.FixExt1;
            buf[1] = type;
            data.CopyTo(buf.Slice(2));
        }
        else if (data.Length == 2)
        {
            var buf = writer.Allocate(4);
            buf[0] = (byte)MessagePackCode.FixExt2;
            buf[1] = type;
            data.CopyTo(buf.Slice(2));
        }
        else if (data.Length == 4)
        {
            var buf = writer.Allocate(6);
            buf[0] = (byte)MessagePackCode.FixExt4;
            buf[1] = type;
            data.CopyTo(buf.Slice(2));
        }
        else if (data.Length == 8)
        {
            var buf = writer.Allocate(10);
            buf[0] = (byte)MessagePackCode.FixExt8;
            buf[1] = type;
            data.CopyTo(buf.Slice(2));
        }
        else if (data.Length == 16)
        {
            var buf = writer.Allocate(18);
            buf[0] = (byte)MessagePackCode.FixExt16;
            buf[1] = type;
            data.CopyTo(buf.Slice(2));
        }
        else if (data.Length <= 0xff)
        {
            var buf = writer.Allocate(data.Length + 3);
            buf[0] = (byte)MessagePackCode.Ext8;
            buf[1] = (byte)data.Length;
            buf[2] = type;
            data.CopyTo(buf.Slice(3));
        }
        else
        {
            if (data.Length <= 0xffff)
            {
                var buf = writer.Allocate(data.Length + 4);
                buf[0] = (byte)MessagePackCode.Ext16;
                BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)data.Length);
                buf[3] = type;
                data.CopyTo(buf.Slice(4));
            }
            else
            {
                var buf = writer.Allocate(data.Length + 6);
                buf[0] = (byte)MessagePackCode.Ext32;
                BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)data.Length);
                buf[5] = type;
                data.CopyTo(buf.Slice(6));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePlusIntegerValue(DocumentWriter writer, ulong value)
    {
        if (value <= 127)
        {
            writer.WriteByte((byte)value);
        }
        else
        {
            if (value <= byte.MaxValue)
            {
                var buf = writer.Allocate(2);
                buf[0] = (byte)MessagePackCode.Uint8;
                buf[1] = (byte)value;
            }
            else
            {
                if (value <= ushort.MaxValue)
                {
                    var buf = writer.Allocate(3);
                    buf[0] = (byte)MessagePackCode.Uint16;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)value);
                }
                else
                {
                    if (value <= uint.MaxValue)
                    {
                        var buf = writer.Allocate(5);
                        buf[0] = (byte)MessagePackCode.Uint32;
                        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)value);
                    }
                    else
                    {
                        var buf = writer.Allocate(9);
                        buf[0] = (byte)MessagePackCode.Uint64;
                        BinaryPrimitives.WriteUInt64BigEndian(buf.Slice(1), value);
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBigNumberValue(DocumentWriter writer, ReadOnlySpan<byte> value, BigNumberKind kind,
        bool oldSpec)
    {
        WriteStringValue(writer, value, oldSpec);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteDecimalValue(DocumentWriter writer, decimal value, bool oldSpec)
    {
        Span<byte> buf = stackalloc byte[64];

        var result = Utf8Formatter.TryFormat(value, buf, out var bytesWritten);
        Debug.Assert(result);
        WriteStringValue(writer, buf.Slice(0, bytesWritten), oldSpec);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteMinusIntegerValue(DocumentWriter writer, long value)
    {
        if (value >= -32)
        {
            writer.WriteByte((byte)value);
        }
        else
        {
            if (value >= sbyte.MinValue)
            {
                var buf = writer.Allocate(2);
                buf[0] = (byte)MessagePackCode.Int8;
                buf[1] = (byte)value;
            }
            else
            {
                if (value >= short.MinValue)
                {
                    var buf = writer.Allocate(3);
                    buf[0] = (byte)MessagePackCode.Int16;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)value);
                }
                else
                {
                    if (value >= int.MinValue)
                    {
                        var buf = writer.Allocate(5);
                        buf[0] = (byte)MessagePackCode.Int32;
                        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)value);
                    }
                    else
                    {
                        var buf = writer.Allocate(9);
                        buf[0] = (byte)MessagePackCode.Int64;
                        BinaryPrimitives.WriteUInt64BigEndian(buf.Slice(1), (ulong)value);
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSingleValue(DocumentWriter writer, float value)
    {
        var buf = writer.Allocate(5);
        buf[0] = (byte)MessagePackCode.Float32;
        BinaryPrimitives.WriteSingleBigEndian(buf.Slice(1), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteDoubleValue(DocumentWriter writer, double value)
    {
        var buf = writer.Allocate(9);
        buf[0] = (byte)MessagePackCode.Float64;
        BinaryPrimitives.WriteDoubleBigEndian(buf.Slice(1), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteGuidValue(DocumentWriter writer, Guid guid, bool oldSpec)
    {
        var buf = writer.BeginWrite(40);
        int headerSize;

        if (oldSpec)
        {
            buf[0] = (byte)MessagePackCode.Str16;
            headerSize = 3;
        }
        else
        {
            buf[0] = (byte)MessagePackCode.Str8;
            headerSize = 2;
        }

        Utf8Formatter.TryFormat(guid, buf.Slice(headerSize), out var bytesWritten);

        if (oldSpec)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)bytesWritten);
        }
        else
        {
            buf[1] = (byte)bytesWritten;
        }

        writer.EndWrite(headerSize + bytesWritten);
    }

    private static void WriteDateTimeOffsetValue(DocumentWriter writer, DateTimeOffset value)
    {
        WriteArray(writer, 2);
        WriteDateTimeValue(writer, value.DateTime.ToLocalTime());

        var offset = value.Offset.TotalMinutes;
        if (offset >= 0)
        {
            WritePlusIntegerValue(writer, (ulong)offset);
        }
        else
        {
            WriteMinusIntegerValue(writer, (long)offset);
        }
    }

    private static void WriteDateTimeValue(DocumentWriter writer, DateTime value)
    {
        var tick = value.ToUniversalTime().Ticks;
        // Split absolute ticks first so dates before the epoch also have a
        // nonnegative nanosecond component and seconds rounded down.
        var sec = tick / TimeSpan.TicksPerSecond - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
        var nanoSec = (uint)(tick % TimeSpan.TicksPerSecond * 100L);

        if (sec >= 0 && sec <= uint.MaxValue && nanoSec == 0)
        {
            var buf = writer.Allocate(6);
            buf[0] = (byte)MessagePackCode.FixExt4;
            buf[1] = 0xff;
            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(2), (uint)sec);
        }
        else if (sec >= 0 && sec < 1L << 34)
        {
            var data64 = ((ulong)nanoSec << 34) | (ulong)sec;

            var buf = writer.Allocate(10);
            buf[0] = (byte)MessagePackCode.FixExt8;
            buf[1] = 0xff;
            BinaryPrimitives.WriteUInt64BigEndian(buf.Slice(2), data64);
        }
        else
        {
            var buf = writer.Allocate(15);
            buf[0] = (byte)MessagePackCode.Ext8;
            buf[1] = 12;
            buf[2] = 0xff;
            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(3), nanoSec);
            BinaryPrimitives.WriteInt64BigEndian(buf.Slice(7), sec);
        }
    }

    private static void WriteByteStringValue(DocumentWriter writer, ReadOnlySpan<byte> value, ByteStringKind kind)
    {
        if (kind == ByteStringKind.Guid && value.Length == 16)
        {
            WriteGuidValue(writer, new Guid(value, true), false);
            return;
        }

        if (value.Length <= 0xff)
        {
            var buf = writer.Allocate(2);
            buf[0] = (byte)MessagePackCode.Bin8;
            buf[1] = (byte)value.Length;
        }
        else
        {
            if (value.Length <= 0xffff)
            {
                var buf = writer.Allocate(3);
                buf[0] = (byte)MessagePackCode.Bin16;
                BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)value.Length);
            }
            else
            {
                var buf = writer.Allocate(5);
                buf[0] = (byte)MessagePackCode.Bin32;
                BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)value.Length);
            }
        }

        writer.WriteBytes(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBooleanValue(DocumentWriter writer, bool value)
    {
        writer.WriteByte(value ? (byte)MessagePackCode.True : (byte)MessagePackCode.False);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNullValue(DocumentWriter writer)
    {
        writer.WriteByte((byte)MessagePackCode.Nil);
    }
}