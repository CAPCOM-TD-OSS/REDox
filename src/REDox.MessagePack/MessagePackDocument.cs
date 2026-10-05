// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using REDox.Serialization;

namespace REDox.MessagePack;

public sealed class MessagePackDocument : Document
{
    private const int MaxLength = 0x1000000;
    private const int MinLength = 256;
    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    internal MessagePackDocument(SerializerSettings settings) : base(settings)
    {
    }

    public ReadOnlyMemory<byte> Source => _source;

    public MessagePackDocument Duplicate()
    {
        var doc = CreateSnapshot<MessagePackDocument>();

        if (_rentedBuffer != null)
        {
            doc._source = _source.ToArray();
            doc._rentedBuffer = null;
        }

        return doc;
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

    public static bool TryParse(ReadOnlySpan<byte> messagePack, [NotNullWhen(true)] out MessagePackDocument? document,
        SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        document = new MessagePackDocument(settings);
        document.EnsureCapacity(messagePack.Length / 16);

        var error = document.ParseMessagePack(messagePack, out var bytesConsumed, options);

        if (error != null)
        {
            document.Dispose();
            document = null;
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(bytesConsumed);

        messagePack.Slice(0, bytesConsumed).CopyTo(buffer);
        document._source = buffer.AsMemory(0, bytesConsumed);
        document._rentedBuffer = buffer;
        document.RootId = 1;

        return true;
    }


    public static MessagePackDocument Parse(Stream stream, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        var buffer = Helper.ReadStream(stream, out var len, settings.DefaultBufferSize);

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

    public static MessagePackDocument Parse(ReadOnlySpan<byte> messagePack, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var buffer = ArrayPool<byte>.Shared.Rent(messagePack.Length);

        try
        {
            messagePack.CopyTo(buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, messagePack.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="messagePack" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static MessagePackDocument Parse(byte[] messagePack, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        return Parse(messagePack.AsMemory(), settings, options);
    }

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="messagePack" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static MessagePackDocument Parse(ReadOnlyMemory<byte> messagePack, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new MessagePackDocument(settings);

        try
        {
            doc.Read(messagePack, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static MessagePackDocument ParseRented(ReadOnlyMemory<byte> messagePack, byte[] rentedBuffer,
        SerializerSettings? settings = null, MessagePackDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new MessagePackDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(messagePack, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static byte[] Encode(DElement element,
        MessagePackWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options);
            var rootId = reader.RootId;

            WriteValue(writer, reader, reader.GetToken(rootId), rootId);

            return writer.Encode();
        }
    }

    public static void EncodeTo(DElement element, Stream writeStream,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            try
            {
                writer.Reset(writeStream, reader.Settings, options);
                var rootId = reader.RootId;
                WriteValue(writer, reader, reader.GetToken(rootId), rootId);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            try
            {
                writer.Reset(bufferWriter, reader.Settings, options);
                var rootId = reader.RootId;
                WriteValue(writer, reader, reader.GetToken(rootId), rootId);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    internal void ParseInternal(ReadOnlyMemory<byte> bytes, SerializerSettings settings,
        MessagePackDocumentOptions options)
    {
        Reset(settings);
        Read(bytes, options);
    }

    private void Read(ReadOnlyMemory<byte> bytes, MessagePackDocumentOptions options)
    {
        var index = 0;

        EnsureCapacity(bytes.Length / 8);

        _source = bytes;

        var error = ParseMessagePack(_source.Span, out index, options);

        if (error != null)
        {
            throw error;
        }

        _source = _source.Slice(0, index);

        RootId = 1;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DToken MakeInt32Literal(int value)
    {
        return DToken.Make(DTokenVariant.Integer, 0x0080000000000000L | (uint)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DToken MakeUInt32Literal(uint value)
    {
        return DToken.Make(DTokenVariant.IntegerUnsigned, 0x0080000000000000L | value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DToken MakeSingleLiteral(float value)
    {
        return DToken.Make(DTokenVariant.FloatSingle, 0x0080000000000000L | Unsafe.BitCast<float, uint>(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint AllocExtensionToken(byte typeCode, int index, int length, bool preserveExtension)
    {
        if (preserveExtension)
        {
            AllocToken(DToken.Make(DTokenVariant.TriviaTag, 0));
        }

        var tokenId = AllocToken(DToken.MakeArray(2));
        AllocToken(MakeInt32Literal(typeCode));
        AllocToken(DToken.Make(DTokenVariant.ByteString, DToken.EncodeLengthOffsetPayload(length, index)));
        return tokenId;
    }

    private ParseException? ParseMessagePack(ReadOnlySpan<byte> bytes, out int bytesConsumed,
        MessagePackDocumentOptions options)
    {
        var index = 0;
        var count = 0;
        var parentId = 0U;
        var latestId = 0U;
        var tokenId = 0U;
        var errorCode = ParseException.ErrorCode.None;
        var strKind = options.OldSpec ? DTokenVariant.ByteStringRaw : DTokenVariant.String;
        var maxLength = options.MaxLength > 0 ? Math.Max(MinLength, options.MaxLength) : MaxLength;

        using var stack =
            new Helper.LocalStack<(uint pid, uint lid, int count)>(stackalloc (uint pid, uint lid, int count)[16],
                options.MaxDepth);

        while (index < bytes.Length)
        {
            var c = bytes[index++];
            var token = new DToken();

            if (c < 0xc0)
            {
                if (c >= (int)MessagePackCode.FixStr)
                {
                    var byteCount = c & 0x1f;
                    token = DToken.Make(strKind,
                        DToken.EncodeLengthOffsetPayload(byteCount, index));
                    index += byteCount;
                }
                else
                {
                    if (c < 0x80)
                    {
                        token = MakeInt32Literal(c);
                    }
                    else
                    {
                        if (!stack.Push((parentId, latestId, count)))
                        {
                            errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                            goto parseError;
                        }

                        if (c < (int)MessagePackCode.FixArray)
                        {
                            //fixmap
                            count = c & 0xf;
                            tokenId = AllocToken(DToken.MakeMap(count));
                            count *= 2;
                        }
                        else
                        {
                            //fixarray
                            count = c & 0xf;
                            tokenId = AllocToken(DToken.MakeArray(count));
                        }

                        parentId = tokenId;
                        latestId = 0;
                        goto nextToken;
                    }
                }
            }
            else
            {
                if ((MessagePackCode)c == MessagePackCode.Float64)
                {
                    token = DToken.Make(DTokenKind.Float, index);
                    index += 8;
                    goto assignToken;
                }

                if ((MessagePackCode)c == MessagePackCode.Float32)
                {
                    if (index + 4 > bytes.Length)
                    {
                        errorCode = ParseException.ErrorCode.InvalidFloatFormat;
                        goto parseError;
                    }

                    token = MakeSingleLiteral(BinaryPrimitives.ReadSingleBigEndian(bytes.Slice(index)));
                    index += 4;
                    goto assignToken;
                }

                if ((MessagePackCode)c == MessagePackCode.False)
                {
                    token = DToken.Make(DTokenVariant.BooleanFalse, 0);
                    goto assignToken;
                }

                if ((MessagePackCode)c == MessagePackCode.True)
                {
                    token = DToken.Make(DTokenVariant.BooleanTrue, 1);
                    goto assignToken;
                }

                if ((MessagePackCode)c == MessagePackCode.Nil)
                {
                    token = DToken.Make(DTokenVariant.Null, 0);
                    goto assignToken;
                }

                if (c >= 0xe0)
                {
                    token = MakeInt32Literal((sbyte)c);
                }
                else
                {
                    switch ((MessagePackCode)c)
                    {
                        case MessagePackCode.Uint8: //uint8
                            if (index >= bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = MakeUInt32Literal(bytes[index]);
                            index++;
                            break;
                        case MessagePackCode.Uint16: //uint16
                            if (index + 2 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = MakeUInt32Literal(BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index)));
                            index += 2;
                            break;
                        case MessagePackCode.Uint32: //uint32
                            if (index + 4 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = MakeUInt32Literal(BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index)));
                            index += 4;
                            break;
                        case MessagePackCode.Int8: //int8
                            if (index >= bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = MakeInt32Literal((sbyte)bytes[index]);
                            index++;
                            break;
                        case MessagePackCode.Int16: //int16
                            if (index + 2 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = MakeInt32Literal(BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(index)));
                            index += 2;
                            break;
                        case MessagePackCode.Int32: //int32
                            if (index + 4 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = MakeInt32Literal(BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(index)));
                            index += 4;
                            break;
                        case MessagePackCode.Str8: //str 8
                            {
                                if (index >= bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidStringFormat;
                                    goto parseError;
                                }

                                var byteCount = bytes[index];
                                token = DToken.Make(strKind,
                                    DToken.EncodeLengthOffsetPayload(byteCount, index + 1));
                                index += byteCount + 1;
                            }
                            break;
                        case MessagePackCode.Bin8: //bin8
                            {
                                if (index >= bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidBinaryFormat;
                                    goto parseError;
                                }

                                var byteCount = bytes[index];
                                token = DToken.Make(DTokenVariant.ByteString,
                                    DToken.EncodeLengthOffsetPayload(byteCount, index + 1));
                                index += byteCount + 1;
                            }
                            break;
                        case MessagePackCode.Bin16: //bin16
                            {
                                if (index + 2 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidBinaryFormat;
                                    goto parseError;
                                }

                                var byteCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index));

                                if (byteCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeBinary;
                                    goto parseError;
                                }

                                token = DToken.Make(DTokenVariant.ByteString,
                                    DToken.EncodeLengthOffsetPayload(byteCount, index + 2));
                                index += byteCount + 2;
                            }
                            break;
                        case MessagePackCode.Bin32: //bin32
                            {
                                if (index + 4 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidBinaryFormat;
                                    goto parseError;
                                }

                                var byteCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index));

                                if (byteCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeBinary;
                                    goto parseError;
                                }

                                token = DToken.Make(DTokenVariant.ByteString,
                                    DToken.EncodeLengthOffsetPayload(Math.Min((int)byteCount, MaxLength), index + 4));
                                index += (int)byteCount + 4;
                            }
                            break;
                        case MessagePackCode.Ext8: //ext 8
                            {
                                if (index + 2 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                    goto parseError;
                                }

                                var byteCount = bytes[index] + 1;
                                var typeCode = bytes[index + 1];

                                if (typeCode == 0xff)
                                {
                                    token = DToken.Make(DTokenVariant.Timestamp,
                                        DToken.EncodeLengthOffsetPayload(byteCount, index + 1));
                                    index += byteCount + 1;
                                }
                                else
                                {
                                    tokenId = AllocExtensionToken(typeCode, index + 2, byteCount - 1,
                                        options.PreserveExtension);
                                    index += byteCount + 1;
                                    goto linkToken;
                                }
                            }
                            break;
                        case MessagePackCode.Ext16: //ext 16
                            {
                                if (index + 3 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                    goto parseError;
                                }

                                var byteCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index)) + 1;

                                if (byteCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeExtFormat;
                                    goto parseError;
                                }

                                tokenId = AllocExtensionToken(bytes[index + 2], index + 3, byteCount - 1,
                                    options.PreserveExtension);
                                index += byteCount + 2;
                            }
                            goto linkToken;
                        case MessagePackCode.Ext32: //ext 32
                            {
                                if (index + 5 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                    goto parseError;
                                }

                                var byteCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index));

                                if (byteCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeExtFormat;
                                    goto parseError;
                                }

                                tokenId = AllocExtensionToken(bytes[index + 4], index + 5, (int)byteCount,
                                    options.PreserveExtension);
                                index += (int)byteCount + 5;
                            }
                            goto linkToken;
                        case MessagePackCode.Uint64: //uint64
                            if (index + 8 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = DToken.Make(DTokenVariant.IntegerUnsigned, index);
                            index += 8;
                            break;

                        case MessagePackCode.Int64: //int64
                            if (index + 8 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidIntegerFormat;
                                goto parseError;
                            }

                            token = DToken.Make(DTokenVariant.Integer, index);
                            index += 8;
                            break;
                        case MessagePackCode.FixExt1: //fixext 1
                            if (index + 2 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                goto parseError;
                            }

                            tokenId = AllocExtensionToken(bytes[index], index + 1, 1, options.PreserveExtension);
                            index += 2;
                            goto linkToken;
                        case MessagePackCode.FixExt2: //fixext 2
                            if (index + 3 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                goto parseError;
                            }

                            tokenId = AllocExtensionToken(bytes[index], index + 1, 2, options.PreserveExtension);
                            index += 3;
                            goto linkToken;
                        case MessagePackCode.FixExt4: //fixext 4
                            if (index + 5 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                goto parseError;
                            }

                            if (bytes[index] == 0xff)
                            {
                                token = DToken.Make(DTokenVariant.Timestamp,
                                    DToken.EncodeLengthOffsetPayload(5, index));
                                index += 5;
                            }
                            else
                            {
                                tokenId = AllocExtensionToken(bytes[index], index + 1, 4, options.PreserveExtension);
                                index += 5;
                                goto linkToken;
                            }

                            break;
                        case MessagePackCode.FixExt8: //fixext 8
                            if (index + 9 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                goto parseError;
                            }

                            if (bytes[index] == 0xff)
                            {
                                token = DToken.Make(DTokenVariant.Timestamp,
                                    DToken.EncodeLengthOffsetPayload(9, index));
                                index += 9;
                            }
                            else
                            {
                                tokenId = AllocExtensionToken(bytes[index], index + 1, 8, options.PreserveExtension);
                                index += 9;
                                goto linkToken;
                            }

                            break;
                        case MessagePackCode.FixExt16: //fixext 16
                            if (index + 17 > bytes.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidExtFormat;
                                goto parseError;
                            }

                            tokenId = AllocExtensionToken(bytes[index], index + 1, 16, options.PreserveExtension);
                            index += 17;
                            goto linkToken;
                        case MessagePackCode.Str16: //str 16
                            {
                                if (index + 2 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidStringFormat;
                                    goto parseError;
                                }

                                var byteCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index));

                                if (byteCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeString;
                                    goto parseError;
                                }

                                token = DToken.Make(strKind,
                                    DToken.EncodeLengthOffsetPayload(byteCount, index + 2));
                                index += byteCount + 2;
                            }
                            break;
                        case MessagePackCode.Str32: //str 32
                            {
                                if (index + 4 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidStringFormat;
                                    goto parseError;
                                }

                                var byteCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index));

                                if (byteCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeString;
                                    goto parseError;
                                }

                                token = DToken.Make(strKind,
                                    DToken.EncodeLengthOffsetPayload(Math.Min((int)byteCount, MaxLength), index + 4));
                                index += (int)byteCount + 4;
                            }
                            break;
                        case MessagePackCode.Array16: //array 16
                            {
                                if (index + 2 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidArrayFormat;
                                    goto parseError;
                                }

                                var valueCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index));

                                if (valueCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeArray;
                                    goto parseError;
                                }

                                token = DToken.MakeArray(valueCount);
                                index += 2;
                                tokenId = AllocToken(token);
                                if (!stack.Push((parentId, latestId, count)))
                                {
                                    errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                                    goto parseError;
                                }

                                count = valueCount;
                                parentId = tokenId;
                                latestId = 0;
                                goto nextToken;
                            }
                        case MessagePackCode.Array32: //array 32
                            {
                                if (index + 4 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidArrayFormat;
                                    goto parseError;
                                }

                                var valueCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index));

                                if (valueCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeArray;
                                    goto parseError;
                                }

                                token = DToken.MakeArray((int)valueCount);
                                index += 4;
                                tokenId = AllocToken(token);
                                if (!stack.Push((parentId, latestId, count)))
                                {
                                    errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                                    goto parseError;
                                }

                                count = (int)valueCount;
                                parentId = tokenId;
                                latestId = 0;
                                goto nextToken;
                            }
                        case MessagePackCode.Map16: //map 16
                            {
                                if (index + 2 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidMapFormat;
                                    goto parseError;
                                }

                                var valueCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index));

                                if (valueCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeMap;
                                    goto parseError;
                                }

                                token = DToken.MakeMap(valueCount);
                                index += 2;
                                tokenId = AllocToken(token);
                                if (!stack.Push((parentId, latestId, count)))
                                {
                                    errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                                    goto parseError;
                                }

                                count = valueCount * 2;
                                parentId = tokenId;
                                latestId = 0;
                                goto nextToken;
                            }
                        case MessagePackCode.Map32: //map 32
                            {
                                if (index + 4 > bytes.Length)
                                {
                                    errorCode = ParseException.ErrorCode.InvalidMapFormat;
                                    goto parseError;
                                }

                                var valueCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(index));

                                if (valueCount > maxLength)
                                {
                                    errorCode = ParseException.ErrorCode.TooLargeMap;
                                    goto parseError;
                                }

                                token = DToken.MakeMap((int)valueCount);
                                index += 4;

                                tokenId = AllocToken(token);
                                if (!stack.Push((parentId, latestId, count)))
                                {
                                    errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                                    goto parseError;
                                }

                                count = (int)valueCount * 2;
                                parentId = tokenId;
                                latestId = 0;
                                goto nextToken;
                            }
                        case MessagePackCode.NeverUsed: //never used
                            errorCode = ParseException.ErrorCode.InvalidFormat;
                            goto parseError;
                    }
                }
            }

            assignToken:

            tokenId = AllocToken(token);

            linkToken:

            if (parentId == 0)
            {
                if (index > bytes.Length)
                {
                    errorCode = ParseException.ErrorCode.OutOfBounds;
                    goto parseError;
                }

                bytesConsumed = index;
                return null;
            }

            if (latestId != 0)
            {
                LinkToken(latestId, tokenId);
            }

            latestId = tokenId;
            count--;

            nextToken:
            while (count == 0)
            {
                tokenId = parentId;

                (parentId, latestId, count) = stack.Pop();

                if (parentId == 0)
                {
                    if (index > bytes.Length)
                    {
                        errorCode = ParseException.ErrorCode.OutOfBounds;
                        goto parseError;
                    }

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

        errorCode = ParseException.ErrorCode.OutOfBounds;

        parseError:
        bytesConsumed = 0;
        return new ParseException(this, errorCode, index);
    }


    private static void WriteValue(MessagePackWriter writer, in DataReader reader, DToken token, uint tokenId)
    {
        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                if (writer.PreserveExtension && reader.GetValueCount(tokenId) == 2)
                {
                    var extension = false;

                    foreach (var triviaId in reader.EnumerateTrivia(tokenId))
                    {
                        var trivia = reader.GetToken(triviaId);

                        if (!trivia.IsExtended && trivia.Variant == DTokenVariant.TriviaTag)
                        {
                            extension = true;
                            break;
                        }
                    }

                    if (extension)
                    {
                        var enumerator = reader.EnumerateArray(tokenId);
                        enumerator.MoveNext();
                        var type = reader.ReadByte(enumerator.Current);
                        enumerator.MoveNext();
                        var data = reader.ReadByteString(enumerator.Current);

                        writer.WriteExtension(type, data);
                        return;
                    }
                }

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
                    switch (token.FloatKind)
                    {
                        case FloatKind.Half:
                            writer.WriteHalf(reader.ReadHalf(tokenId));
                            break;
                        case FloatKind.Single:
                        case FloatKind.Inherit:
                            writer.WriteSingle(reader.ReadSingle(tokenId));
                            break;
                        case FloatKind.Decimal:
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
                    writer.WriteNull();
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

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        if (token.Variant == DTokenVariant.TriviaTag)
        {
            return "Ext"u8;
        }

        return base.DecodeTrivia(token);
    }

    protected override long DecodeInteger(DToken token)
    {
        var param = (int)(long)token;

        if (((long)token & 0x0080000000000000L) != 0)
        {
            if (token.Variant == DTokenVariant.IntegerUnsigned)
            {
                return (uint)param;
            }

            return param;
        }

        return BinaryPrimitives.ReadInt64BigEndian(_source.Span.Slice(param));
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        if (param.length == MaxLength)
        {
            param.length = BinaryPrimitives.ReadInt32BigEndian(_source.Span.Slice(param.offset - 4));
        }

        return _source.Span.Slice(param.offset, param.length);
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = (int)(long)token;

        if (token.Variant == DTokenVariant.FloatSingle)
        {
            return Unsafe.BitCast<int, float>(param);
        }

        return BinaryPrimitives.ReadDoubleBigEndian(_source.Span.Slice(param));
    }

    protected override ReadOnlySpan<byte> DecodeByteString(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        if (param.length == MaxLength)
        {
            param.length = BinaryPrimitives.ReadInt32BigEndian(_source.Span.Slice(param.offset - 4));
        }

        return _source.Span.Slice(param.offset, param.length);
    }

    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        return DecodeDateTime(token);
    }

    protected override DateTime DecodeDateTime(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        switch (param.length)
        {
            case 5: // seconds in 32-bit unsigned int 
                {
                    var sec = BinaryPrimitives.ReadUInt32BigEndian(_source.Span.Slice(param.offset + 1, 4));

                    return new DateTime(sec * 10000000L + 621355968000000000L, DateTimeKind.Utc);
                }
            case 9: //  nanosec. in 30-bit unsigned int |   seconds in 34-bit unsigned int 
                {
                    var data64 = BinaryPrimitives.ReadUInt64BigEndian(_source.Span.Slice(param.offset + 1, 8));

                    var nsec = data64 >> 34;
                    var sec = data64 & 0x00000003ffffffffL;

                    return new DateTime((long)sec * 10000000L + (long)nsec / 100L + 621355968000000000L,
                        DateTimeKind.Utc);
                }
            case 13: // nanoseconds in 32-bit unsigned int | seconds in 64 - bit signed int
                {
                    var nsec = BinaryPrimitives.ReadUInt32BigEndian(_source.Span.Slice(param.offset + 1, 4));
                    var sec = BinaryPrimitives.ReadUInt64BigEndian(_source.Span.Slice(param.offset + 5, 8));
                    if (sec < 18446744011573954816UL)
                    {
                        throw new OverflowException();
                    }

                    sec -= 18446744011573954816UL;

                    return new DateTime((long)sec * 10000000L + nsec / 100L, DateTimeKind.Utc);
                }
        }

        throw new InvalidOperationException();
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat,
            InvalidIntegerFormat,
            InvalidMapFormat,
            InvalidArrayFormat,
            InvalidStringFormat,
            InvalidExtFormat,
            InvalidFloatFormat,
            InvalidBinaryFormat,
            OutOfBounds,
            TooLargeBinary,
            TooLargeString,
            TooLargeArray,
            TooLargeMap,
            TooLargeExtFormat,
            MaxDepthExceeded
        }

        private readonly ErrorCode _errorCode;

        public ParseException(MessagePackDocument doc, ErrorCode error, int offset) : base(nameof(MessagePackDocument),
            offset)
        {
            _errorCode = error;
        }

        public override string Message => $"{_errorCode}, BytePosition: {BytePosition}";
    }
}