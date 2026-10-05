// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization;

namespace REDox.Cbor;

public sealed class CborDocument : Document
{
    private const int MaxLength = 0x1000000;
    private const int MinLength = 256;
    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    internal CborDocument(SerializerSettings settings) : base(settings)
    {
    }

    public ReadOnlyMemory<byte> Source => _source;

    protected override void Dispose(bool disposing)
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }

        base.Dispose(disposing);
    }

    public CborDocument Duplicate()
    {
        var doc = CreateSnapshot<CborDocument>();

        if (_rentedBuffer != null)
        {
            doc._source = _source.ToArray();
            doc._rentedBuffer = null;
        }

        return doc;
    }

    public static CborDocument Parse(Stream stream, SerializerSettings? settings = null,
        CborDocumentOptions options = default)
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

    public static CborDocument Parse(ReadOnlySpan<byte> cbor, SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(cbor.Length);

        try
        {
            cbor.CopyTo(buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, cbor.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="cbor" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static CborDocument Parse(byte[] cbor, SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        return Parse(cbor.AsMemory(), settings, options);
    }

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="cbor" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static CborDocument Parse(ReadOnlyMemory<byte> cbor, SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new CborDocument(settings);

        try
        {
            doc.Read(cbor, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static CborDocument ParseRented(ReadOnlyMemory<byte> cbor, byte[] rentedBuffer,
        SerializerSettings? settings = null, CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new CborDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(cbor, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static bool TryParse(ReadOnlyMemory<byte> cbor, [NotNullWhen(true)] out CborDocument? document,
        SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        document = new CborDocument(settings);
        document.EnsureCapacity(cbor.Length / 8);

        ParseException? error;
        int bytesConsumed;
        try
        {
            error = document.ParseCbor(cbor.Span, out bytesConsumed, options);
        }
        catch (ParseException ex)
        {
            error = ex;
            bytesConsumed = 0;
        }

        if (error != null)
        {
            document.Dispose();
            document = null;
            return false;
        }

        document._source = cbor.Slice(0, bytesConsumed);
        document.RootId = document.GetFirstValueId();

        return true;
    }

    public static byte[] Encode(DElement element,
        CborWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.Reset(reader.Settings, options);
            Write(writer, reader, options);
            return writer.Encode();
        }
    }

    public static void EncodeTo(DElement element, Stream writeStream,
        CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.Reset(writeStream, reader.Settings, options);
            Write(writer, reader, options);
            writer.Dispose();
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        CborWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.Reset(bufferWriter, reader.Settings, options);
            Write(writer, reader, options);
            writer.Dispose();
        }
    }

    internal void ParseInternal(ReadOnlyMemory<byte> bytes, SerializerSettings settings, CborDocumentOptions options)
    {
        Reset(settings);
        Read(bytes, options);
    }

    private void Read(ReadOnlyMemory<byte> bytes, CborDocumentOptions options)
    {
        EnsureCapacity(bytes.Length / 8);

        var bytesConsumed = 0;

        var error = ParseCbor(bytes.Span, out bytesConsumed, options);

        if (error != null)
        {
            throw error;
        }

        _source = bytes.Slice(0, bytesConsumed);
        RootId = GetFirstValueId();
    }

    private uint GetFirstValueId()
    {
        var tokenId = 1U;

        while (GetToken(tokenId).IsLeadingTrivia)
        {
            tokenId++;
        }

        return tokenId;
    }

    private static void Write(CborWriter writer, in DataReader reader, CborWriteOptions options)
    {
        var token = reader.GetToken(reader.RootId);

        if (options.UseSequenceFormat)
        {
            if (token.Type != DTokenType.Array)
            {
                throw new InvalidOperationException("The root of the CBOR sequence format must be an array");
            }

            foreach (var valueId in reader.EnumerateArray(reader.RootId))
            {
                var value = reader.GetToken(valueId);

                WriteValue(writer, reader, value, valueId);
            }
        }
        else
        {
            WriteValue(writer, reader, token, reader.RootId);
        }
    }

    private static void WriteValue(CborWriter writer, in DataReader reader, DToken token, uint tokenId)
    {
        if (writer.PreserveTag)
        {
            WriteTags(writer, reader, tokenId);
        }

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                writer.WriteStartArray(reader.GetValueCount(tokenId));

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    var value = reader.GetToken(valueId);

                    WriteValue(writer, reader, value, valueId);
                }

                writer.WriteEndArray();
            }
            else
            {
                writer.WriteStartMap(reader.GetValueCount(tokenId));

                foreach (var pair in reader.EnumerateMap(tokenId))
                {
                    var key = reader.GetToken(pair.Key);

                    if (key.Type == DTokenType.Text)
                    {
                        if (writer.PreserveTag)
                        {
                            WriteTags(writer, reader, pair.Key);
                        }

                        writer.WriteString(reader.ReadUtf8String(pair.Key));
                    }
                    else
                    {
                        WriteValue(writer, reader, key, pair.Key);
                    }

                    var value = reader.GetToken(pair.Value);

                    WriteValue(writer, reader, value, pair.Value);
                }

                writer.WriteEndMap();
            }
        }
        else
        {
            switch (token.Kind)
            {
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    writer.WriteString(reader.ReadUtf8String(tokenId));
                    break;
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
                case DTokenKind.Float:
                    switch (token.Variant)
                    {
                        case DTokenVariant.FloatHalf:
                            writer.WriteHalf(reader.ReadHalf(tokenId));
                            break;
                        case DTokenVariant.FloatSingle:
                            writer.WriteSingle(reader.ReadSingle(tokenId));
                            break;
                        case DTokenVariant.FloatDecimal:
                            writer.WriteDecimal(reader.ReadDecimal(tokenId));
                            break;
                        default:
                            writer.WriteDouble(reader.ReadDouble(tokenId));
                            break;
                    }

                    break;
                case DTokenKind.Integer:
                    if (token.Variant == DTokenVariant.IntegerUnsigned)
                    {
                        writer.WriteUInt64(reader.ReadUInt64(tokenId));
                    }
                    else
                    {
                        writer.WriteInt64(reader.ReadInt64(tokenId));
                    }

                    break;
                case DTokenKind.Null:
                    if (token.Variant == DTokenVariant.NullUndefined)
                    {
                        writer.WriteUndefined();
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    break;
                case DTokenKind.Boolean:
                    writer.WriteBoolean(reader.ReadBoolean(tokenId));
                    break;
                case DTokenKind.BigNumber:
                    writer.WriteBigNumber(reader.ReadBigNumber(tokenId), token.BigNumberKind);
                    break;
                case DTokenKind.ByteString:
                    writer.WriteByteString(reader.ReadByteString(tokenId), token.ByteStringKind);
                    break;
                case DTokenKind.Timestamp:
                    if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                    {
                        writer.WriteDateTimeOffset(reader.ReadDateTimeOffset(tokenId));
                    }
                    else
                    {
                        writer.WriteDateTime(reader.ReadDateTime(tokenId));
                    }

                    break;
            }
        }
    }


    private static bool IsKnownTag(CborTag tag)
    {
        switch (tag)
        {
            case CborTag.DateTimeString:
            case CborTag.UnixTimeSeconds:
            case CborTag.UnsignedBigNum:
            case CborTag.NegativeBigNum:
            case CborTag.DecimalFraction:
            case CborTag.Base64UrlLaterEncoding:
            case CborTag.Base64StringLaterEncoding:
            case CborTag.Base16StringLaterEncoding:
            case CborTag.Uuid:
                return true;
            default:
                return false;
        }
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        if (token.Variant == DTokenVariant.TriviaTag)
        {
            var tag = DecodeTag(token);

            if (tag <= int.MaxValue && Enum.IsDefined((CborTag)tag))
            {
                return Encoding.UTF8.GetBytes(((CborTag)tag).ToString());
            }

            var buffer = new byte[20];
            Utf8Formatter.TryFormat(tag, buffer, out var bytesWritten);

            return buffer.AsSpan(0, bytesWritten);
        }

        return base.DecodeTrivia(token);
    }

    private ulong DecodeTag(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return (ulong)DecodeNumber(_source.Span, param.offset, (MiscParam)param.length);
    }

    private static void WriteTags(CborWriter writer, in DataReader reader, uint tokenId)
    {
        var cborDocument = reader.Document as CborDocument;

        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var trivia = reader.GetToken(triviaId);

            if (trivia.TriviaKind != TriviaKind.Tag)
            {
                continue;
            }

            if (cborDocument != null && !trivia.IsExtended)
            {
                writer.WriteTag(cborDocument.DecodeTag(trivia));
                continue;
            }

            var text = reader.ReadTrivia(triviaId);

            if (Utf8Parser.TryParse(text, out ulong tag, out var bytesConsumed) && bytesConsumed == text.Length)
            {
                writer.WriteTag(tag);
            }
            else if (Enum.TryParse(Encoding.UTF8.GetString(text), out CborTag knownTag) && knownTag != CborTag.Invalid)
            {
                writer.WriteTag((ulong)knownTag);
            }
        }
    }

    private int GetInfinityLength(ReadOnlySpan<byte> bytes)
    {
        var index = 0;
        var length = 0;
        while (bytes[index] != 0xff)
        {
            var result = ParseCborToken(bytes, ref index);

            if (!(result.type == CborMajorType.Binary || result.type == CborMajorType.String))
            {
                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, 0);
            }

            index += (int)result.val;
            length += (int)result.val;
        }

        return length;
    }

    private ParseException? ParseCbor(ReadOnlySpan<byte> cbor, out int bytesConsumed, CborDocumentOptions options)
    {
        bytesConsumed = 0;

        var tag = CborTag.Invalid;
        var index = 0;
        var count = 0;
        var parentId = 0U;
        var latestId = 0U;
        var tokenId = 0U;
        var maxLength = options.MaxLength > 0 ? Math.Max(MinLength, options.MaxLength) : MaxLength;

        using var stack =
            new Helper.LocalStack<(uint pid, uint lid, int count)>(stackalloc (uint pid, uint lid, int count)[64],
                options.MaxDepth);

        if (options.UseSequenceFormat)
        {
            if (!stack.Push((parentId, latestId, count)))
            {
                return new ParseException(this, ParseException.ErrorCode.MaxDepthExceeded, index);
            }

            parentId = AllocToken(DToken.MakeArray(0));
            latestId = 0;
            count = -1;
        }

        while (index < cbor.Length)
        {
            var c = cbor[index++];

            var param = (MiscParam)(c & 0x1f);

            switch (c & 0x1f)
            {
                case 24:
                    if (cbor.Length - index < 1)
                    {
                        return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                    }

                    param = (MiscParam)cbor[index];
                    index++;
                    break;
                case 25:
                    if (cbor.Length - index < 2)
                    {
                        return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                    }

                    param = (MiscParam)((cbor[index] << 8) | cbor[index + 1]);
                    index += 2;
                    break;
                case 26:
                    if (cbor.Length - index < 4)
                    {
                        return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                    }

                    param = MiscParam.Int32;
                    index += 4;
                    break;
                case 27:
                    if (cbor.Length - index < 8)
                    {
                        return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                    }

                    param = MiscParam.Int64;
                    index += 8;
                    break;
                case 28:
                case 29:
                case 30:
                    return new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                case 31:
                    if ((CborMajorType)(c >> 5) is CborMajorType.PlusInteger or CborMajorType.MinusInteger or CborMajorType.Tag)
                    {
                        return new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                    }

                    param = MiscParam.Infinity;
                    break;
            }

            var kind = DTokenVariant.Undefined;
            var offset = index;

            switch ((CborMajorType)(c >> 5))
            {
                case CborMajorType.PlusInteger:
                    if (tag == CborTag.UnixTimeSeconds)
                    {
                        kind = DTokenVariant.Timestamp;
                    }
                    else
                    {
                        kind = DTokenVariant.IntegerUnsigned;
                    }

                    break;
                case CborMajorType.MinusInteger:
                    kind = DTokenVariant.Integer;
                    break;
                case CborMajorType.Binary:
                    switch (param & MiscParam.LengthMask)
                    {
                        default:
                            if ((ushort)param > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeByteStringLength, index);
                            }

                            if ((ushort)param > cbor.Length - index)
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index += (ushort)param;
                            break;
                        case MiscParam.Infinity:
                            while (index < cbor.Length && cbor[index] != 0xff)
                            {
                                var chunk = cbor[index];
                                var result = ParseCborToken(cbor, ref index);
                                if (result.type != CborMajorType.Binary || (chunk & 0x1f) == 31)
                                {
                                    return new ParseException(this,
                                        ParseException.ErrorCode.InvalidIndefiniteLengthByteString, index);
                                }

                                if ((ulong)result.val > (uint)(cbor.Length - index))
                                {
                                    return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                                }

                                index += (int)result.val;
                            }

                            if (index == cbor.Length)
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index++;
                            break;
                        case MiscParam.Int32:
                            var length = BinaryPrimitives.ReadUInt32BigEndian(cbor.Slice(offset - 4));
                            if (length > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeByteStringLength, index);
                            }

                            if (length > (uint)(cbor.Length - index))
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index += (int)length;
                            break;
                        case MiscParam.Int64:
                            return new ParseException(this, ParseException.ErrorCode.TooLargeByteStringLength,
                                index);
                    }

                    switch (tag)
                    {
                        case CborTag.Uuid:
                            kind = DTokenVariant.ByteStringGuid;
                            break;
                        case CborTag.Base64UrlLaterEncoding:
                            kind = DTokenVariant.ByteStringBase64Url;
                            break;
                        case CborTag.Base64StringLaterEncoding:
                            kind = DTokenVariant.ByteStringBase64;
                            break;
                        case CborTag.Base16StringLaterEncoding:
                            kind = DTokenVariant.ByteStringBase16;
                            break;
                        case CborTag.UnsignedBigNum:
                            kind = DTokenVariant.BigNumberInteger;
                            param |= MiscParam.Unsigned;
                            break;
                        case CborTag.NegativeBigNum:
                            kind = DTokenVariant.BigNumberInteger;
                            break;
                        default:
                            kind = DTokenVariant.ByteString;
                            break;
                    }

                    break;
                case CborMajorType.String:
                    if (tag == CborTag.DateTimeString)
                    {
                        kind = DTokenVariant.TimestampOffsetDateTime;
                    }
                    else
                    {
                        kind = DTokenVariant.String;
                    }

                    switch (param & MiscParam.LengthMask)
                    {
                        default:
                            if ((ushort)param > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeTextStringLength, index);
                            }

                            if ((ushort)param > cbor.Length - index)
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index += (ushort)param;
                            break;
                        case MiscParam.Infinity:
                            while (index < cbor.Length && cbor[index] != 0xff)
                            {
                                var chunk = cbor[index];
                                var result = ParseCborToken(cbor, ref index);
                                if (result.type != CborMajorType.String || (chunk & 0x1f) == 31)
                                {
                                    return new ParseException(this,
                                        ParseException.ErrorCode.InvalidIndefiniteLengthTextString, index);
                                }

                                if ((ulong)result.val > (uint)(cbor.Length - index))
                                {
                                    return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                                }

                                index += (int)result.val;
                            }

                            if (index == cbor.Length)
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index++;
                            break;
                        case MiscParam.Int32:
                            var length = BinaryPrimitives.ReadUInt32BigEndian(cbor.Slice(offset - 4));
                            if (length > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeTextStringLength, index);
                            }

                            if (length > (uint)(cbor.Length - index))
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index += (int)length;
                            break;
                        case MiscParam.Int64:
                            return new ParseException(this, ParseException.ErrorCode.TooLargeTextStringLength,
                                index);
                    }

                    break;
                case CborMajorType.Array:
                    int arrayLength;
                    switch (param & MiscParam.LengthMask)
                    {
                        default:
                            arrayLength = (ushort)param;
                            if (arrayLength > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeArrayLength, index);
                            }

                            break;
                        case MiscParam.Infinity:
                            arrayLength = -1;
                            break;
                        case MiscParam.Int32:
                            var length = BinaryPrimitives.ReadUInt32BigEndian(cbor.Slice(offset - 4));
                            if (length > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeArrayLength, index);
                            }

                            if (length > (uint)(cbor.Length - index))
                            {
                                return new ParseException(this, ParseException.ErrorCode.MismatchArrayLength, index);
                            }

                            arrayLength = (int)length;
                            break;
                        case MiscParam.Int64:
                            return new ParseException(this, ParseException.ErrorCode.TooLargeArrayLength, index);
                    }

                    if (tag == CborTag.DecimalFraction)
                    {
                        kind = DTokenVariant.FloatDecimal;

                        var result1 = ParseCborToken(cbor, ref index);
                        var result2 = ParseCborToken(cbor, ref index);

                        if (result2.type == CborMajorType.Tag)
                        {
                            var result3 = ParseCborToken(cbor, ref index);
                            if ((ulong)result3.val > (uint)maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeByteStringLength, index);
                            }

                            if ((ulong)result3.val > (uint)(cbor.Length - index))
                            {
                                return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                            }

                            index += (int)result3.val;
                        }
                    }
                    else
                    {
                        kind = DTokenType.Array.ToVariant();
                        if (!stack.Push((parentId, latestId, count)))
                        {
                            return new ParseException(this, ParseException.ErrorCode.MaxDepthExceeded, index);
                        }

                        count = arrayLength;
                        tokenId = AllocToken(DToken.MakeArray(Math.Max(count, 0)));
                        parentId = tokenId;
                        latestId = 0;
                    }

                    break;
                case CborMajorType.Map:
                    kind = DTokenType.Map.ToVariant();
                    if (!stack.Push((parentId, latestId, count)))
                    {
                        return new ParseException(this, ParseException.ErrorCode.MaxDepthExceeded, index);
                    }

                    switch (param & MiscParam.LengthMask)
                    {
                        default:
                            count = (ushort)param;
                            if (count > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeMapLength, index);
                            }

                            tokenId = AllocToken(DToken.MakeMap(count));
                            count *= 2;
                            break;
                        case MiscParam.Infinity:
                            tokenId = AllocToken(DToken.MakeMap(0));
                            count = -1;
                            break;
                        case MiscParam.Int32:
                            var length = BinaryPrimitives.ReadUInt32BigEndian(cbor.Slice(offset - 4));
                            if (length > maxLength)
                            {
                                return new ParseException(this, ParseException.ErrorCode.TooLargeMapLength, index);
                            }

                            if (length > (uint)(cbor.Length - index) / 2)
                            {
                                return new ParseException(this, ParseException.ErrorCode.MismatchMapLength, index);
                            }

                            count = (int)length;
                            tokenId = AllocToken(DToken.MakeMap(count));
                            count *= 2;
                            break;
                        case MiscParam.Int64:
                            return new ParseException(this, ParseException.ErrorCode.TooLargeMapLength, index);
                    }

                    parentId = tokenId;
                    latestId = 0;
                    break;
                case CborMajorType.Tag:
                    if (index == cbor.Length)
                    {
                        return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
                    }

                    tag = (CborTag)DecodeNumber(cbor, offset, param);
                    if (options.PreserveTag && !IsKnownTag(tag))
                    {
                        AllocToken(DToken.Make(DTokenVariant.TriviaTag,
                            DToken.EncodeLengthOffsetPayload((int)param, offset)));
                    }

                    continue;
                case CborMajorType.Other:
                    switch ((CborOtherType)(c & 0x1f))
                    {
                        case CborOtherType.False: //false
                            kind = DTokenVariant.BooleanFalse;
                            break;
                        case CborOtherType.True: //true
                            kind = DTokenVariant.BooleanTrue;
                            break;
                        case CborOtherType.Null: //null
                            kind = DTokenVariant.Null;
                            break;
                        case CborOtherType.Undefined: //undefined
                            kind = DTokenVariant.NullUndefined;
                            break;
                        case CborOtherType.Half: //half float
                            kind = DTokenVariant.FloatHalf;
                            break;
                        case CborOtherType.Single: //single float
                            kind = DTokenVariant.FloatSingle;
                            break;
                        case CborOtherType.Double: //double float
                            if (tag == CborTag.UnixTimeSeconds)
                            {
                                param |= MiscParam.Float;
                                kind = DTokenVariant.Timestamp;
                            }
                            else
                            {
                                kind = DTokenVariant.Float;
                            }

                            break;
                        default:
                            if (count < 0 && param == MiscParam.Infinity &&
                                !(options.UseSequenceFormat && stack.Count == 1))
                            {
                                if (GetToken(parentId).Type == DTokenType.Map && (count & 1) == 0)
                                {
                                    return new ParseException(this, ParseException.ErrorCode.MismatchMapLength, index);
                                }

                                tokenId = parentId;
                                (parentId, latestId, count) = stack.Pop();
                                kind = DTokenVariant.Undefined;
                            }
                            else
                            {
                                return new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            break;
                    }

                    break;
            }

            if (kind.IsContainer)
            {
                while (count == 0)
                {
                    tokenId = parentId;

                    (parentId, latestId, count) = stack.Pop();

                    if (parentId == 0)
                    {
                        bytesConsumed = index;
                        return null;
                    }

                    if (latestId != 0)
                    {
                        LinkToken(latestId, tokenId);
                    }

                    latestId = tokenId;
                    count--;
                }
            }
            else
            {
                if (kind != DTokenVariant.Undefined)
                {
                    tokenId = AllocToken(DToken.Make(kind, DToken.EncodeLengthOffsetPayload((int)param, offset)));
                }

                if (parentId == 0)
                {
                    bytesConsumed = index;
                    return null;
                }

                if (latestId != 0)
                {
                    LinkToken(latestId, tokenId);
                }

                latestId = tokenId;

                for (;;)
                {
                    if (count < 0)
                    {
                        if (GetToken(parentId).Type == DTokenType.Map)
                        {
                            if ((--count & 1) == 0)
                            {
                                IncToken(parentId);
                            }
                        }
                        else
                        {
                            IncToken(parentId);
                        }

                        break;
                    }

                    if (--count == 0)
                    {
                        tokenId = parentId;

                        (parentId, latestId, count) = stack.Pop();

                        if (parentId == 0)
                        {
                            bytesConsumed = index;
                            return null;
                        }

                        if (latestId != 0)
                        {
                            LinkToken(latestId, tokenId);
                        }

                        latestId = tokenId;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            tag = CborTag.Invalid;
        }

        if (!options.UseSequenceFormat || stack.Count != 1)
        {
            return new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
        }

        bytesConsumed = index;
        return null;
    }

    private (long val, CborMajorType type) ParseCborToken(ReadOnlySpan<byte> src, ref int index)
    {
        if ((uint)index >= (uint)src.Length)
        {
            throw new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
        }

        var spt = index;
        var c = src[spt++];

        var mt = (CborMajorType)(c >> 5);
        var st = c & 0x1f;
        long val = st;

        if (st is >= 24 and <= 27 && src.Length - spt < 1 << (st - 24))
        {
            throw new ParseException(this, ParseException.ErrorCode.OutOfBounds, index);
        }

        switch (val)
        {
            case 24:
                val = src[spt];
                spt += 1;
                break;
            case 25:
                val = BinaryPrimitives.ReadUInt16BigEndian(src.Slice(spt));
                spt += 2;
                break;
            case 26:
                val = BinaryPrimitives.ReadUInt32BigEndian(src.Slice(spt));
                spt += 4;
                break;
            case 27:
                val = (long)BinaryPrimitives.ReadUInt64BigEndian(src.Slice(spt));
                spt += 8;
                break;
            case 28:
            case 29:
            case 30:
                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
        }

        index = spt;

        return (val, mt);
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var misc = (MiscParam)param.length;

        var length = DecodeNumber(_source.Span, param.offset, misc);

        if ((misc & MiscParam.LengthMask) == MiscParam.Infinity)
        {
            var dst = new byte[length];
            var src = _source.Span.Slice(param.offset);

            var dpt = 0;
            var spt = 0;
            while (src[spt] != 0xff)
            {
                var result = ParseCborToken(src, ref spt);
                var val = result.val;

                src.Slice(spt, (int)val).CopyTo(dst.AsSpan().Slice(dpt));
                dpt += (int)val;
                spt += (int)val;
            }

            return dst;
        }

        return _source.Span.Slice(param.offset, (int)length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long DecodeNumber(ReadOnlySpan<byte> bytes, int offset, MiscParam param)
    {
        switch (param & MiscParam.LengthMask)
        {
            default:
                return (ushort)param;
            case MiscParam.Int32:
                return BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset - 4));
            case MiscParam.Int64:
                return BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(offset - 8));
            case MiscParam.Infinity:
                return GetInfinityLength(bytes.Slice(offset));
        }
    }


    protected override DateTime DecodeDateTime(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        if (((MiscParam)param.length & MiscParam.Float) != 0)
        {
            var unixTime = BinaryPrimitives.ReadDoubleBigEndian(_source.Span.Slice(param.offset - 8));

            var ticks = (long)(unixTime * TimeSpan.TicksPerSecond + DateTimeOffset.UnixEpoch.Ticks);

            return new DateTime(ticks, DateTimeKind.Utc);
        }
        else
        {
            var unixTime = DecodeNumber(_source.Span, param.offset, (MiscParam)param.length);

            var ticks = unixTime * TimeSpan.TicksPerSecond + DateTimeOffset.UnixEpoch.Ticks;

            return new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        var span = _source.Span.Slice(param.offset, param.length);

        if (Utf8Helper.TryParseTimestamp(span, out var dateTime, out var offset, null, true))
        {
            if (offset.HasValue)
            {
                return new DateTimeOffset(
                    new DateTime(dateTime.ToUniversalTime().Ticks + offset.Value.Ticks, DateTimeKind.Unspecified),
                    offset.Value);
            }

            return dateTime;
        }

        return default;
    }

    protected override long DecodeInteger(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        var val = DecodeNumber(_source.Span, param.offset, (MiscParam)param.length);

        return token.Variant == DTokenVariant.IntegerUnsigned ? val : ~val;
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        switch (token.Variant)
        {
            case DTokenVariant.FloatHalf:
                return (double)BinaryPrimitives.ReadHalfBigEndian(_source.Span.Slice(param.offset - 2, 2));
            case DTokenVariant.FloatSingle:
                return BinaryPrimitives.ReadSingleBigEndian(_source.Span.Slice(param.offset - 4, 4));
            default:
                return BinaryPrimitives.ReadDoubleBigEndian(_source.Span.Slice(param.offset - 8, 8));
        }
    }

    protected override ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        var param = (MiscParam)DToken.DecodeLengthOffsetPayload(token).length;

        var bytes = DecodeByteString(token).ToArray();

        if ((param & MiscParam.Unsigned) == 0)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= 0xff;
            }

            return Utf8Helper.GetUtf8String(new BigInteger(bytes, false, true).ToString());
        }

        return Utf8Helper.GetUtf8String(new BigInteger(bytes, true, true).ToString());
    }

    protected override ReadOnlySpan<byte> DecodeByteString(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var misc = (MiscParam)param.length;
        var length = DecodeNumber(_source.Span, param.offset, misc);

        if ((misc & MiscParam.LengthMask) == MiscParam.Infinity)
        {
            var src = _source.Span.Slice(param.offset);
            var dst = new byte[length];
            var dpt = 0;
            var spt = 0;

            while (src[spt] != 0xff)
            {
                var result = ParseCborToken(src, ref spt);
                var val = (int)result.val;

                src.Slice(spt, val).CopyTo(dst.AsSpan().Slice(dpt));
                dpt += val;
                spt += val;
            }

            return dst;
        }

        return _source.Span.Slice(param.offset, (int)length);
    }

    protected override bool TryDecodeDecimal(DToken token, out decimal result)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        var index = param.offset;

        var scale = ParseCborToken(_source.Span, ref index);
        var value = ParseCborToken(_source.Span, ref index);

        var dScale = scale.type == CborMajorType.MinusInteger ? (byte)Math.Abs(~scale.val) : (byte)scale.val;

        result = default;

        if (dScale > 28)
        {
            return false;
        }

        if (value.type == CborMajorType.Tag)
        {
            var bin = ParseCborToken(_source.Span, ref index);
            var tbl = _source.Span.Slice(index, (int)bin.val);

            if (tbl.Length > 12)
            {
                return false;
            }

            Span<int> dVal = stackalloc int[3];
            for (var i = 0; i < tbl.Length; i++)
            {
                dVal[i / 4] |= tbl[tbl.Length - i - 1] << ((i & 3) * 8);
            }

            if ((CborTag)value.val == CborTag.NegativeBigNum)
            {
                result = new decimal(dVal[0] + 1, dVal[1], dVal[2], true, dScale);
                return true;
            }

            result = new decimal(dVal[0], dVal[1], dVal[2], false, dScale);
            return true;
        }

        if (value.type == CborMajorType.MinusInteger)
        {
            var v = Math.Abs(~value.val);
            result = new decimal((int)(v & 0xffffffff), (int)(v >> 32), 0, true, dScale);
            return true;
        }
        else
        {
            var v = value.val;
            result = new decimal((int)(v & 0xffffffff), (int)(v >> 32), 0, false, dScale);
            return true;
        }
    }


    [Flags]
    private enum MiscParam
    {
        Immediate = 0x000000,
        Int32 = 0x010000,
        Int64 = 0x020000,
        Infinity = 0x030000,
        Unsigned = 0x040000,
        Float = 0x080000,
        LengthMask = 0x030000
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat,
            OutOfBounds,
            InvalidIndefiniteLengthByteString,
            InvalidIndefiniteLengthTextString,
            MismatchMapLength,
            MismatchArrayLength,
            TooLargeTextStringLength,
            TooLargeByteStringLength,
            TooLargeArrayLength,
            TooLargeMapLength,
            MaxDepthExceeded
        }

        private readonly ErrorCode _errorCode;

        public ParseException(CborDocument doc, ErrorCode error, int offset) : base(nameof(CborDocument), offset)
        {
            _errorCode = error;
        }

        public override string Message =>
            $"{nameof(CborDocument)} Error: {_errorCode}, BytePosition: {BytePosition}";
    }
}