// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Csv;

public sealed class CsvDocument : Document
{
    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    private CsvDocument(SerializerSettings settings) : base(settings)
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

    public static bool TryParse(ReadOnlySpan<byte> utf8Csv, [NotNullWhen(true)] out CsvDocument? document,
        SerializerSettings? settings = null,
        CsvDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        document = new CsvDocument(settings);
        document.EnsureCapacity(utf8Csv.Length / 16);

        var parseException = document.ParseCsv(utf8Csv, options);

        if (parseException != null)
        {
            document.Dispose();
            document = null;
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(utf8Csv.Length);
        utf8Csv.CopyTo(buffer);
        document._source = buffer.AsMemory(0, utf8Csv.Length);
        document._rentedBuffer = buffer;

        return true;
    }

    public static CsvDocument Parse(string csv, SerializerSettings? settings = null,
        CsvDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(csv.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(csv, buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory(0, bytes), rentedBuffer, settings, options);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public static CsvDocument Parse(ReadOnlySpan<byte> utf8Csv, SerializerSettings? settings = null,
        CsvDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Csv.Length);

        try
        {
            utf8Csv.CopyTo(buffer);
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory(0, utf8Csv.Length), rentedBuffer, settings, options);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public static CsvDocument Parse(Stream stream, SerializerSettings? settings = null,
        CsvDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = Helper.ReadStream(stream, out var length,
            (settings ?? SerializerSettings.Default).DefaultBufferSize);

        try
        {
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory(0, length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="utf8Csv" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static CsvDocument Parse(byte[] utf8Csv, SerializerSettings? settings = null,
        CsvDocumentOptions options = default)
    {
        return Parse(utf8Csv.AsMemory(), settings, options);
    }

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="utf8Csv" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static CsvDocument Parse(ReadOnlyMemory<byte> utf8Csv, SerializerSettings? settings = null,
        CsvDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var doc = new CsvDocument(settings);

        try
        {
            doc.Read(utf8Csv, options);
        }
        catch
        {
            doc.Dispose();
            throw;
        }

        return doc;
    }

    private static CsvDocument ParseRented(ReadOnlyMemory<byte> utf8Csv, byte[] rentedBuffer,
        SerializerSettings? settings = null, CsvDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var doc = new CsvDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(utf8Csv, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }


    public static byte[] Encode(DElement element, CsvWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            writer.Reset(element.Document.Settings, options.TextWriteOptions with { WriteIndented = true });
            Write(writer, new DataReader(element), options);
            return writer.Encode();
        }
    }

    public static string EncodeToString(DElement element, CsvWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.Reset(reader.Settings, options.TextWriteOptions with { WriteIndented = true });
            Write(writer, reader, options);
            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream stream, CsvWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.Reset(stream, reader.Settings, options.TextWriteOptions with { WriteIndented = true });
                Write(writer, reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }


    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter, CsvWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.Reset(bufferWriter, reader.Settings, options.TextWriteOptions with { WriteIndented = true });
                Write(writer, reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public CsvDocument Duplicate()
    {
        var doc = CreateSnapshot<CsvDocument>();

        if (_rentedBuffer != null)
        {
            doc._source = _source.ToArray();
            doc._rentedBuffer = null;
        }

        return doc;
    }

    private void Read(ReadOnlyMemory<byte> bytes, CsvDocumentOptions options)
    {
        _source = bytes;
        EnsureCapacity(_source.Length / 16);

        var parseException = ParseCsv(bytes.Span, options);
        if (parseException != null)
        {
            throw parseException;
        }
    }


    private DocumentParseException? ParseCsv(ReadOnlySpan<byte> bytes, CsvDocumentOptions options)
    {
        using var headerTbl = new Helper.LocalList<DToken>(stackalloc DToken[32]);
        var column = 0;

        var arrayId = AllocToken(DToken.MakeArray(0));
        RootId = arrayId;

        var index = 0;

        var tokenIndex = index;
        var token = new DToken();

        var parentId = 0U;
        var latestId = 0U;

        var hasHeader = options.HasHeaderRecord;

        var separatorChar = options.SeparatorChar;
        if (separatorChar == 0)
        {
            separatorChar = ',';
        }

        if (!hasHeader)
        {
            parentId = AllocToken(DToken.MakeArray(0));
        }

        for (;;)
        {
            var c = index < bytes.Length ? bytes[index] : (byte)'\0';

            if (c == separatorChar || c == '\r' || c == '\n' || index >= bytes.Length)
            {
                if (token == default)
                {
                    token = DToken.Make(DTokenVariant.String,
                        DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex));
                }

                if (parentId != 0)
                {
                    if (hasHeader)
                    {
                        // Reject extra fields before allocating a missing header token;
                        // at the end of a record, reject missing fields as well.
                        if (column >= headerTbl.Count ||
                            (c != separatorChar && column + 1 != headerTbl.Count))
                        {
                            return new ParseException(this, ParseException.ErrorCode.ColumnCountMismatch, index);
                        }

                        AllocToken(headerTbl[column]);
                    }

                    AllocToken(token);
                    IncToken(parentId);
                }
                else
                {
                    if (hasHeader)
                    {
                        headerTbl.EnsureCount(column + 1);
                        headerTbl[column] = DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex));
                    }
                }

                token = default;
                column++;

                if (c == separatorChar)
                {
                    tokenIndex = index + 1;
                }
                else
                {
                    if (c == '\r')
                    {
                        if (index + 1 < bytes.Length && bytes[index + 1] == '\n')
                        {
                            index++;
                        }
                    }

                    if (parentId == 0)
                    {
                        if (index + 1 >= bytes.Length)
                        {
                            break;
                        }

                        if (hasHeader)
                        {
                            parentId = AllocToken(DToken.MakeMap(0));
                        }
                        else
                        {
                            parentId = AllocToken(DToken.MakeArray(0));
                        }
                    }
                    else
                    {
                        if (latestId != 0)
                        {
                            LinkToken(latestId, parentId);
                        }

                        latestId = parentId;

                        if (hasHeader)
                        {
                            parentId = AllocToken(DToken.MakeMap(0));
                        }
                        else
                        {
                            parentId = AllocToken(DToken.MakeArray(0));
                        }

                        IncToken(arrayId);

                        if (index + 1 >= bytes.Length)
                        {
                            break;
                        }
                    }

                    tokenIndex = index + 1;
                    column = 0;
                }
            }
            else
            {
                if (c == '"')
                {
                    var length = 0;
                    var escaped = false;
                    var quotedFieldOffset = index;

                    index++;
                    tokenIndex = index;

                    while (index < bytes.Length)
                    {
                        if (bytes[index] == '"')
                        {
                            if (index + 1 < bytes.Length && bytes[index + 1] == '"')
                            {
                                //Escape
                                escaped = true;
                                index++;
                            }
                            else
                            {
                                token = DToken.Make(DTokenVariant.String,
                                    DToken.EncodeLengthOffsetPayload(DToken.EncodeStringParamPayload(length, escaped),
                                        tokenIndex));
                                break;
                            }
                        }

                        index++;
                        length++;
                    }

                    if (token == default)
                    {
                        return new ParseException(this, ParseException.ErrorCode.UnterminatedQuotedField,
                            quotedFieldOffset);
                    }
                }
            }

            index++;
        }

        if (hasHeader && parentId == 0 && column > 0)
        {
            AllocToken(DToken.MakeArray(column));

            for (var i = 0; i < column; i++)
            {
                AllocToken(headerTbl[i]);
            }

            IncToken(arrayId);
        }

        return null;
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, CsvWriteOptions options)
    {
        var rootId = reader.RootId;

        var tokenId = rootId;

        if (reader.GetToken(tokenId).Type != DTokenType.Array)
        {
            return;
        }

        if (options.WriteBom)
        {
            writer.WriteBom();
        }

        var hasHeader = options.IncludeHeaderInFirstRow;
        var separatorChar = options.SeparatorChar;
        if (separatorChar == 0)
        {
            separatorChar = ',';
        }

        Span<byte> timestampBuffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
        foreach (var valueId in reader.EnumerateArray(tokenId))
        {
            var valueToken = reader.GetToken(valueId);

            if (hasHeader)
            {
                hasHeader = false;

                if (valueToken.Type == DTokenType.Map)
                {
                    var separator = false;
                    foreach (var kv in reader.EnumerateMap(valueId))
                    {
                        if (separator)
                        {
                            writer.WriteUtf8Byte((byte)separatorChar);
                        }

                        WriteCsvString(writer, reader.ReadUtf8String(kv.Key), (byte)separatorChar);
                        separator = true;
                    }
                }

                writer.WriteNewLine();
            }

            {
                var separator = false;
                foreach (var v in reader.EnumerateArray(valueId))
                {
                    if (separator)
                    {
                        writer.WriteUtf8Byte((byte)separatorChar);
                    }

                    var token = reader.GetToken(v);

                    if (token.IsContainer)
                    {
                        writer.WriteUtf8Byte((byte)'"');
                        writer.WriteString(JsonDocument.Encode(reader.ReadElement(v)));
                        writer.WriteUtf8Byte((byte)'"');
                    }
                    else
                    {
                        switch (token.Kind)
                        {
                            case DTokenKind.String:
                            case DTokenKind.Symbol:
                                WriteCsvString(writer, reader.ReadUtf8String(v), (byte)separatorChar);
                                break;
                            case DTokenKind.Boolean:
                                writer.WriteBoolean(reader.ReadBoolean(v));
                                break;
                            case DTokenKind.InlineFloat:
                                writer.WriteDouble(reader.ReadDouble(v));
                                break;
                            case DTokenKind.Float:
                                if (token.Variant == DTokenVariant.FloatDecimal)
                                {
                                    writer.WriteDecimal(reader.ReadDecimal(v));
                                }
                                else
                                {
                                    writer.WriteDouble(reader.ReadDouble(v));
                                }

                                break;
                            case DTokenKind.Integer:
                                if (token.Variant == DTokenVariant.IntegerUnsigned)
                                {
                                    writer.WriteUInt64(reader.ReadUInt64(v));
                                }
                                else
                                {
                                    writer.WriteInt64(reader.ReadInt64(v));
                                }

                                break;
                            case DTokenKind.BigNumber:
                                writer.WriteString(reader.ReadBigNumber(v));
                                break;
                            case DTokenKind.Timestamp:
                                {
                                    var success = Utf8Helper.TryFormatTimestamp(reader, v, timestampBuffer, out var bytesWritten);
                                    Debug.Assert(success);
                                    // Custom date formats can contain delimiters, quotes, or line breaks.
                                    WriteCsvString(writer, timestampBuffer.Slice(0, bytesWritten), (byte)separatorChar);
                                }
                                break;
                            case DTokenKind.Null:
                                writer.WriteNull();
                                break;
                            default:
                                writer.WriteUtf8Byte((byte)' ');
                                break;
                        }
                    }

                    separator = true;
                }
            }
            writer.WriteNewLine();
        }
    }

    private static void WriteCsvString(Utf8TextWriter writer, ReadOnlySpan<byte> utf8Bytes, byte separatorChar)
    {
        var escape = false;
        var length = 0;

        foreach (var c in utf8Bytes)
        {
            switch (c)
            {
                case (byte)'\r':
                case (byte)'\n':
                    escape = true;
                    break;
                case (byte)'"':
                    escape = true;
                    length++;
                    break;
            }

            if (c == separatorChar)
            {
                escape = true;
            }

            length++;
        }

        if (escape)
        {
            length += 2;
        }

        if (length == utf8Bytes.Length)
        {
            writer.WriteString(utf8Bytes);
            return;
        }

        var buf = writer.AllocateUtf8Bytes(length);
        var pt = 0;
        var len = utf8Bytes.Length;

        if (escape)
        {
            buf[pt++] = (byte)'"';
        }

        for (var i = 0; i < len; i++)
        {
            var c = utf8Bytes[i];

            if (c == '"')
            {
                buf[pt++] = c;
            }

            buf[pt++] = c;
        }

        if (escape)
        {
            buf[pt++] = (byte)'"';
        }

        if (pt != length)
        {
            throw new InvalidOperationException();
        }
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var stringParam = DToken.DecodeStringParamPayload(param.length);

        if (!stringParam.escaped)
        {
            return _source.Span.Slice(param.offset, stringParam.encodedLength);
        }

        var dst = new byte[stringParam.encodedLength];
        var src = _source.Span.Slice(param.offset);

        var srcIndex = 0;
        var dstIndex = 0;

        while (dstIndex < stringParam.encodedLength)
        {
            var c = src[srcIndex++];

            if (c == '"')
            {
                srcIndex++;
            }

            dst[dstIndex++] = c;
        }

        return dst;
    }

    private sealed class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            UnterminatedQuotedField,
            ColumnCountMismatch
        }

        private readonly ErrorCode _errorCode;

        public ParseException(CsvDocument document, ErrorCode errorCode, int bytePosition) : base(nameof(CsvDocument),
            bytePosition)
        {
            _errorCode = errorCode;
        }

        public override string Message =>
            $"{nameof(CsvDocument)} ParseError: {_errorCode}, BytePosition: {BytePosition}";
    }
}