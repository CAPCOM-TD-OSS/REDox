// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using REDox.Serialization;

namespace REDox;

public sealed class DoxDocument : Document
{
    private const short DoxMajorVersion = 1;
    private const short DoxMinorVersion = 0;
    private const int DoxMagic = 'D' | ('O' << 8) | ('X' << 16);
    private const long InlineValueMask = 0x0080000000000000L;
    private const long InlineIntegerPayloadMask = 0x007fffffffffffffL;
    private const int ExtendedLengthMarker = 0x1ffffff;
    private const long MinInlineInteger = -(1L << 54);
    private const long MaxInlineInteger = (1L << 54) - 1;
    private const ulong MaxInlineUnsignedInteger = (1UL << 55) - 1;

    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    internal DoxDocument(SerializerSettings settings) : base(settings)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }

        base.Dispose(disposing);
    }

    public DoxDocument Duplicate()
    {
        var doc = CreateSnapshot<DoxDocument>();

        if (_rentedBuffer != null)
        {
            doc._source = _source.ToArray();
            doc._rentedBuffer = null;
        }

        return doc;
    }

    public static DoxDocument Parse(Stream stream, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = Helper.ReadStream(stream, out var len,
            (settings ?? SerializerSettings.Default).DefaultBufferSize);

        try
        {
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, len), rentedBuffer, settings, options);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public static DoxDocument Parse(ReadOnlySpan<byte> data, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);

        try
        {
            data.CopyTo(buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, data.Length), rentedBuffer, settings, options);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="data" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static DoxDocument Parse(byte[] data, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        return Parse(data.AsMemory(), settings, options);
    }

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="data" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static DoxDocument Parse(ReadOnlyMemory<byte> data, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new DoxDocument(settings);

        try
        {
            doc.Read(data);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static DoxDocument ParseRented(ReadOnlyMemory<byte> data, byte[] rentedBuffer,
        SerializerSettings? settings = null, DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new DoxDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(data);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out DoxDocument? document,
        SerializerSettings? settings = null, DoxDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);

        try
        {
            data.CopyTo(buffer.AsSpan());

            if (!TryParse(buffer.AsMemory(0, data.Length), out document, settings, options))
            {
                return false;
            }

            document._rentedBuffer = buffer;
            buffer = null;

            return true;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public static bool TryParse(ReadOnlyMemory<byte> data, [NotNullWhen(true)] out DoxDocument? document,
        SerializerSettings? settings = null, DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        document = new DoxDocument(settings);

        if (document.TryRead(data, out _, out _))
        {
            return true;
        }

        document.Dispose();
        document = null;
        return false;
    }

    public static byte[] Encode(DElement element, DoxWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<DocumentWriter>.Get(() => new DocumentWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.ResetInternal(reader.Settings.DefaultBufferSize, null, null);
            Write(writer, in reader, options);
            return writer.ToArray();
        }
    }

    public static void EncodeTo(DElement element, Stream stream,
        DoxWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using (var cache = Helper.InstanceCache<DocumentWriter>.Get(() => new DocumentWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.ResetInternal(reader.Settings.DefaultBufferSize, stream, null);
                Write(writer, in reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        DoxWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);

        using (var cache = Helper.InstanceCache<DocumentWriter>.Get(() => new DocumentWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.ResetInternal(reader.Settings.DefaultBufferSize, null, bufferWriter);
                Write(writer, in reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    protected override string DecodeString(DToken token)
    {
        if (((long)token & InlineValueMask) != 0)
        {
            return string.Empty;
        }

        var param = DToken.DecodeLengthOffsetPayload(token);

        if (param.length == 0)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(param.offset));

            return Encoding.UTF8.GetString(_source.Span.Slice(param.offset + 4, length));
        }
        else
        {
            var length = param.length & 0xfff;
            var utf8 = _source.Slice(param.offset, length);
            var utf16Length = (param.length >> 12) & 0xfff;

            return string.Create(utf16Length, utf8,
                (span, source) => { Encoding.UTF8.GetChars(source.Span, span); });
        }
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        if (((long)token & InlineValueMask) != 0)
        {
            return [];
        }

        var param = DToken.DecodeLengthOffsetPayload(token);

        if (param.length == 0)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(param.offset));

            return _source.Span.Slice(param.offset + 4, length);
        }
        else
        {
            var length = param.length & 0xfff;

            return _source.Span.Slice(param.offset, length);
        }
    }

    protected override long DecodeInteger(DToken token)
    {
        if (((long)token & InlineValueMask) != 0)
        {
            if (token.Variant == DTokenVariant.IntegerUnsigned)
            {
                return (long)token & InlineIntegerPayloadMask;
            }

            return ((long)token << 9) >> 9;
        }

        var param = DToken.DecodeLengthOffsetPayload(token);
        return BinaryPrimitives.ReadInt64LittleEndian(_source.Span.Slice(param.offset, param.length));
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = (int)(long)token;

        if (((long)token & InlineValueMask) != 0)
        {
            return Unsafe.BitCast<int, float>(param);
        }

        return BinaryPrimitives.ReadDoubleLittleEndian(_source.Span.Slice(param));
    }

    protected override DateTime DecodeDateTime(DToken token)
    {
        var buffer = _source.Span.Slice(DToken.DecodeLengthOffsetPayload(token).offset, 8);
        var dateTime = BinaryPrimitives.ReadInt64LittleEndian(buffer);

        return DateTime.FromBinary(dateTime);
    }

    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        var buffer = _source.Span.Slice(DToken.DecodeLengthOffsetPayload(token).offset, 16);
        var dateTime = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        var timeSpan = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(8));

        return new DateTimeOffset(DateTime.FromBinary(dateTime), new TimeSpan(timeSpan));
    }

    protected override ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        return DecodeBytes(param.length, param.offset);
    }

    protected override ReadOnlySpan<byte> DecodeByteString(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        return DecodeBytes(param.length, param.offset);
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        return DecodeBytes(param.length, param.offset);
    }

    protected override bool TryDecodeDecimal(DToken token, out decimal result)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        Span<int> bits = stackalloc int[4];

        bits[0] = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(param.offset));
        bits[1] = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(param.offset + 4));
        bits[2] = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(param.offset + 8));
        bits[3] = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(param.offset + 12));

        result = new decimal(bits);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteDateTime(DocumentWriter w, DateTime value, TimestampKind kind)
    {
        var offset = (int)w.BytesWritten;
        var buffer = w.Allocate(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value.ToBinary());
        return DToken.Make(kind.ToVariant(), DToken.EncodeLengthOffsetPayload(0, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteDateTimeOffset(DocumentWriter w, DateTimeOffset value)
    {
        var offset = (int)w.BytesWritten;
        var buffer = w.Allocate(16);
        var dateTime = value.DateTime;
        var timeSpan = value.Offset;

        BinaryPrimitives.WriteInt64LittleEndian(buffer, dateTime.ToBinary());
        BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(8), timeSpan.Ticks);

        return DToken.Make(DTokenVariant.TimestampOffsetDateTime, DToken.EncodeLengthOffsetPayload(0, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteBigNumber(DocumentWriter w, ReadOnlySpan<byte> value, BigNumberKind kind)
    {
        var tokenParam = WriteBytes(w, value);

        return DToken.Make(kind.ToVariant(), tokenParam);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteByteString(DocumentWriter w, ReadOnlySpan<byte> value, ByteStringKind kind)
    {
        var tokenParam = WriteBytes(w, value);

        return DToken.Make(kind.ToVariant(), tokenParam);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteTrivia(DocumentWriter w, ReadOnlySpan<byte> value, TriviaKind kind)
    {
        var tokenParam = WriteBytes(w, value);

        return DToken.Make(kind.ToVariant(), tokenParam);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long WriteBytes(DocumentWriter w, ReadOnlySpan<byte> value)
    {
        var offset = (int)w.BytesWritten;

        if (value.Length >= ExtendedLengthMarker)
        {
            var buffer = w.Allocate(4);
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value.Length);
            w.WriteBytes(value);

            return DToken.EncodeLengthOffsetPayload(ExtendedLengthMarker, offset);
        }

        w.WriteBytes(value);

        return DToken.EncodeLengthOffsetPayload(value.Length, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> DecodeBytes(int length, int offset)
    {
        if (length == ExtendedLengthMarker)
        {
            length = BinaryPrimitives.ReadInt32LittleEndian(_source.Span.Slice(offset));
            offset += 4;
        }

        return _source.Span.Slice(offset, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteString(DocumentWriter w, ReadOnlySpan<char> value, DTokenVariant variant)
    {
        if (value.Length == 0)
        {
            return DToken.Make(variant, InlineValueMask);
        }

        var offset = w.BytesWritten;
        var byteCount = Encoding.UTF8.GetByteCount(value);

        if (byteCount > 0xfff || value.Length > 0xfff)
        {
            var buffer = w.BeginWrite(byteCount + 4);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(0), byteCount);
            Encoding.UTF8.GetBytes(value, buffer.Slice(4));
            w.EndWrite(byteCount + 4);

            return DToken.Make(variant,
                DToken.EncodeLengthOffsetPayload(0, (int)offset));
        }
        else
        {
            var buffer = w.BeginWrite(byteCount);
            Encoding.UTF8.GetBytes(value, buffer);
            w.EndWrite(byteCount);

            return DToken.Make(variant,
                DToken.EncodeLengthOffsetPayload(byteCount | (value.Length << 12), (int)offset));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteString(DocumentWriter w, ReadOnlySpan<byte> value, DTokenVariant variant)
    {
        if (value.Length == 0)
        {
            return DToken.Make(variant, InlineValueMask);
        }

        var offset = w.BytesWritten;

        var charCount = Encoding.UTF8.GetCharCount(value);

        if (charCount > 0xfff || value.Length > 0xfff)
        {
            var buffer = w.Allocate(4);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(0), value.Length);
            w.WriteBytes(value);
            return DToken.Make(variant,
                DToken.EncodeLengthOffsetPayload(0, (int)offset));
        }

        w.WriteBytes(value);
        return DToken.Make(variant,
            DToken.EncodeLengthOffsetPayload(value.Length | (charCount << 12), (int)offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteDecimal(DocumentWriter w, decimal value)
    {
        Span<int> bytes = stackalloc int[4];
        decimal.TryGetBits(value, bytes, out var valuesWritten);
        Debug.Assert(valuesWritten == 4, "Decimal should always have 4 bits.");

        var offset = (int)w.BytesWritten;

        var buffer = w.Allocate(16);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, bytes[0]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), bytes[1]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), bytes[2]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12), bytes[3]);

        return DToken.Make(DTokenVariant.FloatDecimal, DToken.EncodeLengthOffsetPayload(0, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteHalf(DocumentWriter w, Half value)
    {
        var fValue = (float)value;
        var iValue = Unsafe.BitCast<float, int>(fValue);

        return new DToken((long)(((ulong)DTokenVariant.FloatHalf << 56) | InlineValueMask |
                                 (uint)iValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteSingle(DocumentWriter w, float value)
    {
        var iValue = Unsafe.BitCast<float, int>(value);

        return new DToken((long)(((ulong)DTokenVariant.FloatSingle << 56) | InlineValueMask |
                                 (uint)iValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteDouble(DocumentWriter w, double value)
    {
        var fValue = (float)value;

        if (fValue == value)
        {
            return WriteSingle(w, fValue);
        }

        var payload = DToken.EncodeInlineFloatPayload(value);
        if (payload >= 0)
        {
            return DToken.MakeInlineFloat(payload);
        }

        var offset = (int)w.BytesWritten;
        var bytes = w.Allocate(8);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        return DToken.Make(DTokenVariant.Float, DToken.EncodeLengthOffsetPayload(0, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteBoolean(DocumentWriter w, bool value)
    {
        return DToken.Make(value ? DTokenVariant.BooleanTrue : DTokenVariant.BooleanFalse, 0);
    }

    internal static void WriteHeader(DocumentWriter w)
    {
        var buffer = w.Allocate(8);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, DoxMagic);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), (DoxMajorVersion << 16) | DoxMinorVersion);
    }

    internal static void WriteFooter(DocumentWriter w, ReadOnlySpan<DToken> tokens)
    {
        var tokenOffset = checked((int)w.BytesWritten);

        var padding = (8 - (tokenOffset & 7)) & 7;

        for (var i = 0; i < padding; i++)
        {
            w.WriteByte(0);
        }

        tokenOffset += padding;

        foreach (var t in tokens)
        {
            var buffer = w.Allocate(8);
            BinaryPrimitives.WriteInt64LittleEndian(buffer, (long)t);
        }

        {
            var buffer = w.Allocate(8);
            BinaryPrimitives.WriteInt32LittleEndian(buffer, tokens.Length);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), tokenOffset);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteInt32(DocumentWriter w, int value)
    {
        return DToken.Make(DTokenVariant.Integer, InlineValueMask | (value & InlineIntegerPayloadMask));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteInt64(DocumentWriter w, long value)
    {
        if (value >= MinInlineInteger && value <= MaxInlineInteger)
        {
            return DToken.Make(DTokenVariant.Integer, InlineValueMask | (value & InlineIntegerPayloadMask));
        }

        var offset = (int)w.BytesWritten;
        var buffer = w.Allocate(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        return DToken.Make(DTokenVariant.Integer, DToken.EncodeLengthOffsetPayload(8, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken WriteUInt64(DocumentWriter w, ulong value)
    {
        if (value <= MaxInlineUnsignedInteger)
        {
            return DToken.Make(DTokenVariant.IntegerUnsigned, InlineValueMask | (long)value);
        }

        var offset = (int)w.BytesWritten;
        var buffer = w.Allocate(8);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        return DToken.Make(DTokenVariant.IntegerUnsigned, DToken.EncodeLengthOffsetPayload(8, offset));
    }

    internal void ParseInternal(ReadOnlyMemory<byte> bytes, SerializerSettings settings)
    {
        Reset(settings);
        Read(bytes);
    }

    private void Read(ReadOnlyMemory<byte> bytes)
    {
        if (!TryRead(bytes, out var errorCode, out var offset))
        {
            throw new ParseException(this, errorCode, offset);
        }
    }

    private bool TryRead(ReadOnlyMemory<byte> bytes, out ParseException.ErrorCode errorCode, out int offset)
    {
        errorCode = ParseException.ErrorCode.None;
        offset = 0;

        if (bytes.Length < 16)
        {
            errorCode = ParseException.ErrorCode.InvalidFormat;
            return false;
        }

        var span = bytes.Span;
        var magic = BinaryPrimitives.ReadInt32LittleEndian(span);

        if (magic != DoxMagic)
        {
            errorCode = ParseException.ErrorCode.InvalidFormat;
            return false;
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4));

        if (version >> 16 != DoxMajorVersion)
        {
            errorCode = ParseException.ErrorCode.InvalidVersion;
            offset = 4;
            return false;
        }

        var footerOffset = bytes.Length - 8;
        var footer = span.Slice(footerOffset, 8);
        var tokenLength = BinaryPrimitives.ReadInt32LittleEndian(footer);
        var tokenOffset = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(4));

        if (tokenLength < 2 || tokenOffset < 8 || tokenOffset > footerOffset)
        {
            errorCode = ParseException.ErrorCode.InvalidFormat;
            offset = footerOffset;
            return false;
        }

        var tokenByteLength = (long)tokenLength * Unsafe.SizeOf<DToken>();
        var maxTokenByteLength = footerOffset - tokenOffset;

        if (tokenByteLength > maxTokenByteLength)
        {
            errorCode = ParseException.ErrorCode.InvalidFormat;
            offset = tokenOffset;
            return false;
        }

        var tokens = MemoryMarshal.Cast<byte, DToken>(
            span.Slice(tokenOffset, (int)tokenByteLength));

        ReplaceTokens(tokens);

        _source = bytes;

        var rootId = 1;
        while (rootId < tokens.Length && tokens[rootId].IsIgnore)
        {
            rootId++;
        }

        if (rootId >= tokens.Length)
        {
            errorCode = ParseException.ErrorCode.MissingRootValue;
            return false;
        }

        RootId = (uint)rootId;

        return true;
    }

    private static uint WriteDox(DocumentWriter dw, in DataReader reader, uint tokenId, DToken[] tokens,
        ref int tokenCount, DoxWriteOptions options)
    {
        var token = reader.GetToken(tokenId);

        if (options.PreserveTrivia)
        {
            WriteTriviaList(dw, in reader, tokenId, tokens, ref tokenCount);
        }

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                var count = reader.GetValueCount(tokenId);
                var id = tokenCount++;
                tokens[id] = DToken.MakeArray(count);
                var latestId = 0U;

                foreach (var v in reader.EnumerateArray(tokenId))
                {
                    var valueId = WriteDox(dw, in reader, v, tokens, ref tokenCount, options);

                    if (latestId != 0)
                    {
                        if (tokens[latestId].IsContainer)
                        {
                            tokens[latestId].LinkId = valueId;
                        }
                    }

                    latestId = valueId;
                }

                return (uint)id;
            }
            else
            {
                var count = reader.GetValueCount(tokenId);
                var id = tokenCount++;
                tokens[id] = DToken.MakeMap(count);
                var latestId = 0U;

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var keyId = WriteDox(dw, in reader, kv.Key, tokens, ref tokenCount, options);
                    if (latestId != 0)
                    {
                        if (tokens[latestId].IsContainer)
                        {
                            tokens[latestId].LinkId = keyId;
                        }
                    }

                    latestId = keyId;

                    var valueId = WriteDox(dw, in reader, kv.Value, tokens, ref tokenCount, options);

                    if (latestId != 0)
                    {
                        if (tokens[latestId].IsContainer)
                        {
                            tokens[latestId].LinkId = valueId;
                        }
                    }

                    latestId = valueId;
                }

                return (uint)id;
            }
        }

        switch (token.Kind)
        {
            case DTokenKind.Null:
                {
                    var id = tokenCount++;
                    tokens[id] = DToken.Make(token.Variant, 0);
                    return (uint)id;
                }
            case DTokenKind.Boolean:
                {
                    var id = tokenCount++;
                    tokens[id] = DToken.Make(token.Variant, 0);
                    return (uint)id;
                }
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    var id = tokenCount++;
                    tokens[id] = WriteString(dw, reader.ReadUtf8String(tokenId), token.Variant);
                    return (uint)id;
                }
            case DTokenKind.Integer:
                {
                    var id = tokenCount++;
                    if (token.Variant == DTokenVariant.IntegerUnsigned)
                    {
                        var value = reader.ReadUInt64(tokenId);

                        tokens[id] = WriteUInt64(dw, value);
                    }
                    else
                    {
                        var value = reader.ReadInt64(tokenId);

                        tokens[id] = WriteInt64(dw, value);
                    }

                    return (uint)id;
                }
            case DTokenKind.InlineFloat:
                {
                    var id = tokenCount++;
                    tokens[id] = WriteDouble(dw, reader.ReadDouble(tokenId));
                    return (uint)id;
                }
            case DTokenKind.Float:
                {
                    var id = tokenCount++;
                    switch (token.FloatKind)
                    {
                        case FloatKind.Single:
                            tokens[id] = WriteSingle(dw, reader.ReadSingle(tokenId));
                            break;
                        case FloatKind.Decimal:
                            tokens[id] = WriteDecimal(dw, reader.ReadDecimal(tokenId));
                            break;
                        case FloatKind.Half:
                            tokens[id] = WriteHalf(dw, reader.ReadHalf(tokenId));
                            break;
                        default:
                            tokens[id] = WriteDouble(dw, reader.ReadDouble(tokenId));
                            break;
                    }

                    return (uint)id;
                }
            case DTokenKind.BigNumber:
                {
                    var id = tokenCount++;
                    tokens[id] = WriteBigNumber(dw, reader.ReadBigNumber(tokenId), token.BigNumberKind);
                    return (uint)id;
                }
            case DTokenKind.ByteString:
                {
                    var id = tokenCount++;
                    tokens[id] = WriteByteString(dw, reader.ReadByteString(tokenId), token.ByteStringKind);
                    return (uint)id;
                }
            case DTokenKind.Timestamp:
                {
                    var id = tokenCount++;
                    if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                    {
                        tokens[id] = WriteDateTimeOffset(dw, reader.ReadDateTimeOffset(tokenId));
                    }
                    else
                    {
                        tokens[id] = WriteDateTime(dw, reader.ReadDateTime(tokenId), token.TimestampKind);
                    }

                    return (uint)id;
                }
            default:
                return 0;
        }
    }

    private static void WriteTriviaList(DocumentWriter dw, in DataReader reader, uint tokenId, DToken[] tokens,
        ref int tokenCount)
    {
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var trivia = reader.GetToken(triviaId);

            tokens[tokenCount++] = WriteTrivia(dw, reader.ReadTrivia(triviaId), trivia.TriviaKind);
        }
    }

    private static void Write(DocumentWriter dw, in DataReader reader, DoxWriteOptions options)
    {
        var tokens = new DToken[reader.Document.GetTokens().Length];
        var tokenCount = 1;

        WriteHeader(dw);
        WriteDox(dw, in reader, reader.RootId, tokens, ref tokenCount, options);
        WriteFooter(dw, tokens.AsSpan().Slice(0, tokenCount));
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat,
            InvalidVersion,
            MissingRootValue
        }

        private readonly ErrorCode _errorCode;

        public ParseException(DoxDocument doc, ErrorCode errorCode, int offset) : base(nameof(DoxDocument), offset)
        {
            _errorCode = errorCode;
        }

        public override string Message =>
            $"{nameof(DoxDocument)} Error: {_errorCode}, BytePosition: {BytePosition}";
    }
}