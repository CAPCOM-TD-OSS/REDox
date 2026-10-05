// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using REDox.Serialization;

namespace REDox.Cbor;

public sealed class CborWriter : DataWriter, IDisposable, IAsyncDisposable
{
    private const byte NullToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.Null;
    private const byte UndefinedToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.Undefined;
    private const byte TrueToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.True;
    private const byte FalseToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.False;
    private const byte HalfToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.Half;
    private const byte SingleToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.Single;
    private const byte DoubleToken = ((int)CborMajorType.Other << 5) | (int)CborOtherType.Double;
    private const byte InfinityArrayToken = ((int)CborMajorType.Array << 5) | 31;
    private const byte InfinityMapToken = ((int)CborMajorType.Map << 5) | 31;
    private const byte EndIndefiniteLengthToken = 0xff;

    private readonly DocumentWriter _writer;
    private bool _convertIndefiniteLengthEncodings;
    private CborDateFormatHandling _dateFormatHandling;
    private int _maxDepth;

    public CborWriter(SerializerSettings? settings = null, CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        _writer = new DocumentWriter();
        Reset(settings, options);
    }

    public CborWriter(Stream writeStream, SerializerSettings? settings = null, CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);
        settings ??= SerializerSettings.Default;

        _writer = new DocumentWriter();
        Reset(writeStream, settings, options);
    }

    public CborWriter(IBufferWriter<byte> bufferWriter, SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        settings ??= SerializerSettings.Default;

        _writer = new DocumentWriter();
        Reset(bufferWriter, settings, options);
    }

    public long BytesWritten => _writer.BytesWritten;

    public int CurrentDepth => _writer.ContextDepth;

    public bool PreserveTag { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    public void Reset(Stream writeStream, SerializerSettings settings, CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);
        ArgumentNullException.ThrowIfNull(settings);

        Reset(writeStream, null, settings, options);
    }

    public void Reset(IBufferWriter<byte> bufferWriter, SerializerSettings settings, CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        ArgumentNullException.ThrowIfNull(settings);

        Reset(null, bufferWriter, settings, options);
    }

    public void Reset(SerializerSettings settings, CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Reset(null, null, settings, options);
    }

    private void Reset(Stream? stream, IBufferWriter<byte>? writer, SerializerSettings settings,
        CborWriteOptions options)
    {
        base.Reset(settings);
        _writer.ResetInternal(settings.DefaultBufferSize, stream, writer);
        _dateFormatHandling = options.DateFormatHandling;
        _maxDepth = options.MaxDepth == 0 ? SerializerSettings.DefaultMaxDepth : options.MaxDepth;
        _convertIndefiniteLengthEncodings = options.ConvertIndefiniteLengthEncodings;
        PreserveTag = options.PreserveTag;
    }

    public bool TryEncode(Span<byte> bytes, out int bytesWritten)
    {
        var byteSize = (int)_writer.BytesWritten;

        if (bytes.Length < byteSize)
        {
            bytesWritten = 0;
            return false;
        }

        _writer.CopyTo(bytes);

        bytesWritten = byteSize;
        return true;
    }

    public void Flush()
    {
        _writer.Flush();
    }

    public byte[] Encode()
    {
        return _writer.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextElement()
    {
        if (_writer.ContextDepth > 0)
        {
            ref var state = ref _writer.PeekContext();

            if (state == 0)
            {
                throw new IndexOutOfRangeException();
            }

            state--;
        }
    }

    public override void WriteNull()
    {
        NextElement();
        WriteNullValue(_writer);
    }

    public void WriteTag(ulong tag)
    {
        WriteCborToken(_writer, CborMajorType.Tag, tag);
    }

    public void WriteUndefined()
    {
        NextElement();
        _writer.WriteByte(UndefinedToken);
    }

    public override void WriteChar(char value)
    {
        NextElement();

        Span<byte> bytes = stackalloc byte[4];
        var len = Encoding.UTF8.GetBytes(new ReadOnlySpan<char>(ref value), bytes);

        WriteStringValue(_writer, bytes.Slice(0, len));
    }

    public override void WriteBigNumber(ReadOnlySpan<byte> value, BigNumberKind kind = BigNumberKind.Default)
    {
        NextElement();

        if (BigInteger.TryParse(Encoding.UTF8.GetString(value), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number))
        {
            WriteBigIntegerValue(_writer, number);
        }
        else
        {
            WriteStringValue(_writer, value);
        }
    }

    public override void WriteDateTime(DateTime value)
    {
        if (value.Ticks == 0)
        {
            WriteDateTimeOffset(default);
        }
        else
        {
            WriteDateTimeOffset(value);
        }
    }

    protected internal override void WriteDoubleValues(ReadOnlySpan<double> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteFloatValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteBooleanValues(ReadOnlySpan<bool> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteBooleanValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteSingleValues(ReadOnlySpan<float> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteFloatValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteInt32Values(ReadOnlySpan<int> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteIntegerValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteInt64Values(ReadOnlySpan<long> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteIntegerValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteUInt64Values(ReadOnlySpan<ulong> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteUnsignedIntegerValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteUInt32Values(ReadOnlySpan<uint> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteUnsignedIntegerValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteGuidValues(ReadOnlySpan<Guid> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteGuidValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }

    protected internal override void WriteStringValues(ReadOnlySpan<string?> values)
    {
        var w = _writer;
        foreach (var value in values)
        {
            WriteStringValue(w, value);
        }

        ref var state = ref w.PeekContext();
        state -= values.Length;
    }


    protected internal override void WritePropertyString(Utf8Symbol propertyName, string? value)
    {
        var w = _writer;

        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        if (value != null)
        {
            WriteStringValue(w, value);
        }
        else
        {
            WriteNullValue(w);
        }
    }

    protected internal override void WritePropertyBoolean(Utf8Symbol propertyName, bool value)
    {
        var w = _writer;

        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteBooleanValue(w, value);
    }

    protected internal override void WritePropertyInt32(Utf8Symbol propertyName, int value)
    {
        var w = _writer;

        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteIntegerValue(w, value);
    }

    protected internal override void WritePropertyInt64(Utf8Symbol propertyName, long value)
    {
        var w = _writer;
        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteIntegerValue(w, value);
    }

    protected internal override void WritePropertyUInt32(Utf8Symbol propertyName, uint value)
    {
        var w = _writer;
        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteUnsignedIntegerValue(w, value);
    }

    protected internal override void WritePropertyGuid(Utf8Symbol propertyName, Guid value)
    {
        var w = _writer;
        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteGuidValue(w, value);
    }

    protected internal override void WritePropertyUInt64(Utf8Symbol propertyName, ulong value)
    {
        var w = _writer;
        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteUnsignedIntegerValue(w, value);
    }

    protected internal override void WritePropertySingle(Utf8Symbol propertyName, float value)
    {
        var w = _writer;
        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteFloatValue(w, value);
    }

    protected internal override void WritePropertyDouble(Utf8Symbol propertyName, double value)
    {
        var w = _writer;
        ref var state = ref w.PeekContext();
        state -= 2;

        WriteStringValue(w, propertyName);
        WriteFloatValue(w, value);
    }

    public override void WriteDateTimeOffset(DateTimeOffset value)
    {
        NextElement();
        WriteDateTimeOffsetValue(_writer, value, _dateFormatHandling);
    }

    public override void WriteStartArray(int? definiteLength)
    {
        NextElement();

        var w = _writer;

        if (w.ContextDepth >= _maxDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum depth of {_maxDepth}.");
        }

        if (definiteLength == null)
        {
            if (_convertIndefiniteLengthEncodings)
            {
                w.PushContext(long.MaxValue);
                w.PushPlaceholder(9);
            }
            else
            {
                w.PushContext(-1);
                w.WriteByte(InfinityArrayToken);
            }
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

        if (context < 0)
        {
            _writer.WriteByte(EndIndefiniteLengthToken);
        }
        else
        {
            if (context > int.MaxValue)
            {
                var count = (int)(long.MaxValue - context);
                Span<byte> buf = stackalloc byte[9];
                var bytesWritten = EncodeCborToken(buf, CborMajorType.Array, (ulong)count);
                _writer.PopAndPatch(buf.Slice(0, bytesWritten));
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
    }

    public override void WriteStartMap(int? definiteLength)
    {
        NextElement();

        var w = _writer;

        if (w.ContextDepth >= _maxDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum depth of {_maxDepth}.");
        }

        if (definiteLength == null)
        {
            if (_convertIndefiniteLengthEncodings)
            {
                w.PushContext(long.MaxValue);
                w.PushPlaceholder(9);
            }
            else
            {
                w.PushContext(-1);
                w.WriteByte(InfinityMapToken);
            }
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

        if (context < 0)
        {
            if ((context & 1) == 0)
            {
                throw new InvalidOperationException(
                    "CBOR map incomplete; each key must be followed by a corresponding value.");
            }

            _writer.WriteByte(EndIndefiniteLengthToken);
        }
        else
        {
            if (context > int.MaxValue)
            {
                var count = (int)(long.MaxValue - context);

                if ((count & 1) != 0)
                {
                    throw new InvalidOperationException(
                        "CBOR map incomplete; each key must be followed by a corresponding value.");
                }

                count /= 2;

                Span<byte> buf = stackalloc byte[9];
                var bytesWritten = EncodeCborToken(buf, CborMajorType.Map, (ulong)count);
                _writer.PopAndPatch(buf.Slice(0, bytesWritten));
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
    }

    public override void WriteInt32(int value)
    {
        NextElement();
        WriteIntegerValue(_writer, value);
    }

    public override void WriteUInt32(uint value)
    {
        NextElement();
        WriteUnsignedIntegerValue(_writer, value);
    }

    public override void WriteGuid(Guid value)
    {
        NextElement();
        WriteGuidValue(_writer, value);
    }

    public override void WriteByteString(ReadOnlySpan<byte> value, ByteStringKind kind = ByteStringKind.Default)
    {
        NextElement();
        WriteByteStringValue(_writer, value, kind);
    }

    public override void WriteDecimal(decimal value)
    {
        NextElement();
        WriteDecimalValue(_writer, value);
    }

    public override void WriteInt64(long value)
    {
        NextElement();
        WriteIntegerValue(_writer, value);
    }

    public override void WriteUInt64(ulong value)
    {
        NextElement();
        WriteUnsignedIntegerValue(_writer, value);
    }

    public override void WriteHalf(Half value)
    {
        NextElement();
        WriteHalfValue(_writer, value);
    }

    public override void WriteSingle(float value)
    {
        NextElement();
        WriteFloatValue(_writer, value);
    }

    public override void WriteDouble(double value)
    {
        NextElement();
        WriteFloatValue(_writer, value);
    }

    public override void WriteBoolean(bool value)
    {
        NextElement();
        WriteBooleanValue(_writer, value);
    }

    public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
    {
        NextElement();
        WriteStringValue(_writer, utf8Bytes);
    }

    public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
    {
        NextElement();
        WriteStringValue(_writer, value);
    }

    public override void WriteString(string value)
    {
        NextElement();
        WriteStringValue(_writer, value);
    }

    public override void WriteString(ReadOnlySpan<char> value)
    {
        NextElement();
        WriteStringValue(_writer, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteGuidValue(DocumentWriter w, Guid value)
    {
        Span<byte> guidBytes = stackalloc byte[16];
        value.TryWriteBytes(guidBytes, true, out _);
        WriteByteStringValue(w, guidBytes, ByteStringKind.Guid);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNullValue(DocumentWriter dw)
    {
        dw.WriteByte(NullToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBooleanValue(DocumentWriter dw, bool value)
    {
        dw.WriteByte(value ? TrueToken : FalseToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteHalfValue(DocumentWriter dw, Half value)
    {
        var buf = dw.Allocate(3);
        buf[0] = HalfToken;
        BinaryPrimitives.WriteHalfBigEndian(buf.Slice(1), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSingleValue(DocumentWriter dw, float value)
    {
        var buf = dw.Allocate(5);
        buf[0] = SingleToken;
        BinaryPrimitives.WriteSingleBigEndian(buf.Slice(1), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteDoubleValue(DocumentWriter dw, double value)
    {
        var buf = dw.Allocate(9);
        buf[0] = DoubleToken;
        BinaryPrimitives.WriteDoubleBigEndian(buf.Slice(1), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFloatValue(DocumentWriter dw, double value)
    {
        var fValue = (float)value;

        if (value == fValue)
        {
            var hValue = (Half)fValue;

            if ((float)hValue == fValue)
            {
                WriteHalfValue(dw, hValue);
            }
            else
            {
                WriteSingleValue(dw, fValue);
            }
        }
        else
        {
            WriteDoubleValue(dw, value);
        }
    }

    private static void WriteBigIntegerValue(DocumentWriter dw, BigInteger value)
    {
        var bytes = value.ToByteArray(false, true);

        if (value.Sign < 0)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= 0xff;
            }

            WriteTagToken(dw, CborTag.NegativeBigNum);
        }
        else
        {
            WriteTagToken(dw, CborTag.UnsignedBigNum);
        }

        WriteByteStringValue(dw, bytes, ByteStringKind.Default);
    }

    private static void WriteDecimalValue(DocumentWriter dw, decimal value)
    {
        WriteTagToken(dw, CborTag.DecimalFraction);

        WriteArray(dw, 2);

        Span<byte> bytes = stackalloc byte[16];
        MemoryMarshal.Write(bytes, in value);

        var scale = bytes[2];

        var hi = MemoryMarshal.Read<int>(bytes.Slice(4, 4));
        var lo = MemoryMarshal.Read<ulong>(bytes.Slice(8, 8));

        if (scale > 0)
        {
            WriteCborToken(dw, CborMajorType.MinusInteger, (ulong)(scale - 1));
        }
        else
        {
            WriteCborToken(dw, CborMajorType.PlusInteger, scale);
        }

        var minus = bytes[3] != 0;

        if (minus)
        {
            bytes[8]--;
        }

        if (hi != 0)
        {
            var count = 4;
            while (count > 0)
            {
                if (bytes[4 + count - 1] != 0)
                {
                    break;
                }

                count--;
            }

            WriteTagToken(dw, minus ? CborTag.NegativeBigNum : CborTag.UnsignedBigNum);
            WriteCborToken(dw, CborMajorType.Binary, (ulong)count + 8);
            for (var i = 0; i < count; i++)
            {
                dw.WriteByte(bytes[4 + count - i - 1]);
            }

            for (var i = 0; i < 8; i++)
            {
                dw.WriteByte(bytes[15 - i]);
            }
        }
        else
        {
            if (minus)
            {
                var count = 8;
                while (count > 0)
                {
                    if (bytes[8 + count - 1] != 0)
                    {
                        break;
                    }

                    count--;
                }

                WriteTagToken(dw, CborTag.NegativeBigNum);
                WriteCborToken(dw, CborMajorType.Binary, (ulong)count);
                for (var i = 0; i < count; i++)
                {
                    dw.WriteByte(bytes[8 + count - i - 1]);
                }
            }
            else
            {
                WriteCborToken(dw, CborMajorType.PlusInteger, lo);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteIntegerValue(DocumentWriter dw, long value)
    {
        if (value < 0)
        {
            WriteCborToken(dw, CborMajorType.MinusInteger, (ulong)~value);
        }
        else
        {
            WriteCborToken(dw, CborMajorType.PlusInteger, (ulong)value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUnsignedIntegerValue(DocumentWriter dw, ulong value)
    {
        WriteCborToken(dw, CborMajorType.PlusInteger, value);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTagToken(DocumentWriter dw, CborTag type)
    {
        WriteCborToken(dw, CborMajorType.Tag, (ulong)type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteCborToken(DocumentWriter dw, CborMajorType type, ulong value)
    {
        if (value < 24)
        {
            dw.WriteByte((byte)(((int)type << 5) | (int)value));
        }
        else
        {
            if (value <= 0xff)
            {
                var buf = dw.Allocate(2);
                buf[0] = (byte)(((int)type << 5) | 24);
                buf[1] = (byte)value;
            }
            else
            {
                if (value <= 0xffff)
                {
                    var buf = dw.Allocate(3);
                    buf[0] = (byte)(((int)type << 5) | 25);
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)value);
                }
                else
                {
                    if (value <= 0xffffffff)
                    {
                        var buf = dw.Allocate(5);
                        buf[0] = (byte)(((int)type << 5) | 26);
                        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)value);
                    }
                    else
                    {
                        var buf = dw.Allocate(9);
                        buf[0] = (byte)(((int)type << 5) | 27);
                        BinaryPrimitives.WriteUInt64BigEndian(buf.Slice(1), value);
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeCborToken(Span<byte> buf, CborMajorType type, ulong value)
    {
        if (value < 24)
        {
            buf[0] = (byte)(((int)type << 5) | (int)value);
            return 1;
        }

        if (value <= 0xff)
        {
            buf[0] = (byte)(((int)type << 5) | 24);
            buf[1] = (byte)value;
            return 2;
        }

        if (value <= 0xffff)
        {
            buf[0] = (byte)(((int)type << 5) | 25);
            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)value);
            return 3;
        }

        if (value <= 0xffffffff)
        {
            buf[0] = (byte)(((int)type << 5) | 26);
            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)value);
            return 5;
        }

        buf[0] = (byte)(((int)type << 5) | 27);
        BinaryPrimitives.WriteUInt64BigEndian(buf.Slice(1), value);
        return 9;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteArray(DocumentWriter dw, long count)
    {
        WriteCborToken(dw, CborMajorType.Array, (ulong)count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteObject(DocumentWriter dw, long count)
    {
        WriteCborToken(dw, CborMajorType.Map, (ulong)count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringValue(DocumentWriter dw, ReadOnlySpan<char> str)
    {
        var capacity = str.Length * 3;

        var buf = dw.BeginWrite(capacity + 5);
        var pt = 0;

        pt += capacity < 24 ? 1 : capacity <= 0xff ? 2 : capacity <= 0xffff ? 3 : 5;

        var length = Encoding.UTF8.GetBytes(str, buf.Slice(pt));

        if (capacity < 24)
        {
            buf[0] = (byte)(((int)CborMajorType.String << 5) | length);
        }
        else
        {
            if (capacity <= 0xff)
            {
                buf[0] = ((int)CborMajorType.String << 5) | 24;
                buf[1] = (byte)length;
            }
            else
            {
                if (capacity <= 0xffff)
                {
                    buf[0] = ((int)CborMajorType.String << 5) | 25;
                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(1), (ushort)length);
                }
                else
                {
                    buf[0] = ((int)CborMajorType.String << 5) | 26;
                    BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1), (uint)length);
                }
            }
        }

        pt += length;

        dw.EndWrite(pt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringValue(DocumentWriter dw, ReadOnlySpan<byte> str)
    {
        WriteCborToken(dw, CborMajorType.String, (ulong)str.Length);
        dw.WriteBytes(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteByteStringValue(DocumentWriter dw, ReadOnlySpan<byte> bytes, ByteStringKind kind)
    {
        if (kind == ByteStringKind.Guid)
        {
            WriteTagToken(dw, CborTag.Uuid);
        }

        WriteCborToken(dw, CborMajorType.Binary, (ulong)bytes.Length);
        dw.WriteBytes(bytes);
    }

    private static void WriteDateTimeOffsetValue(DocumentWriter dw, DateTimeOffset value, CborDateFormatHandling format)
    {
        if (format == CborDateFormatHandling.DateTimeText)
        {
            Span<byte> bytes = stackalloc byte[64];

            if (Utf8Helper.TryFormatTimestamp(value.DateTime, value.Offset, null, null,
                    DateFormatHandling.IsoDateFormat,
                    bytes, out var bytesWritten))
            {
                WriteTagToken(dw, CborTag.DateTimeString);
                WriteCborToken(dw, CborMajorType.String, (ulong)bytesWritten);
                dw.WriteBytes(bytes.Slice(0, bytesWritten));
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
        else
        {
            WriteTagToken(dw, CborTag.UnixTimeSeconds);

            if (format == CborDateFormatHandling.UnixTimeSecondsInt64)
            {
                WriteIntegerValue(dw, value.ToUnixTimeSeconds());
            }
            else
            {
                WriteDoubleValue(dw,
                    (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) /
                    (double)TimeSpan.TicksPerSecond);
            }
        }
    }
}