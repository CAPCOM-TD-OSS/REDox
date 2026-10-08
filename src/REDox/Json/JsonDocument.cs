// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;
using REDox.Serialization;
using static REDox.Helper;

namespace REDox.Json;

public sealed class JsonDocument : Document
{
    private static readonly byte[] s_unescapeTable;

    private static readonly SearchValues<byte> s_stringValidationSearchValues = SearchValues.Create(
        "\0\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000b\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\\"u8);

    private static readonly SearchValues<byte> s_hexDigitSearchValues =
        SearchValues.Create("0123456789abcdefABCDEF"u8);

    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    static JsonDocument()
    {
        s_unescapeTable = new byte[128];
        s_unescapeTable['"'] = (byte)'"';
        s_unescapeTable['\\'] = (byte)'\\';
        s_unescapeTable['/'] = (byte)'/';
        s_unescapeTable['b'] = (byte)'\b';
        s_unescapeTable['f'] = (byte)'\f';
        s_unescapeTable['n'] = (byte)'\n';
        s_unescapeTable['r'] = (byte)'\r';
        s_unescapeTable['t'] = (byte)'\t';
    }

    internal JsonDocument(SerializerSettings settings) : base(settings)
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

    public static bool TryParse(ReadOnlySpan<byte> utf8Json, [NotNullWhen(true)] out JsonDocument? document,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        document = new JsonDocument(settings);
        document.EnsureCapacity(utf8Json.Length / 16);

        var error = options.UseNewlineDelimitedFormat
            ? document.ParseNDJson(utf8Json, options)
            : document.ParseJson(utf8Json, options);

        if (error != null)
        {
            document.Dispose();
            document = null;
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(utf8Json.Length);

        utf8Json.CopyTo(buffer);
        document._source = buffer.AsMemory(0, utf8Json.Length);
        document._rentedBuffer = buffer;

        if (options.EnableValueValidation)
        {
            foreach (var token in document.GetTokens())
            {
                if (document.ValidateToken(token) != null)
                {
                    document.Dispose();
                    document = null;
                    return false;
                }
            }
        }

        document.RootId = 1;

        return true;
    }

    public static JsonDocument Parse(string json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(json.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json, buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, bytes), rentedBuffer, settings, options);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public static JsonDocument Parse(Stream stream, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = ReadStream(stream, out var len, (settings ?? SerializerSettings.Default).DefaultBufferSize);

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

    public static JsonDocument Parse(ReadOnlySpan<byte> utf8Json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Json.Length);

        try
        {
            utf8Json.CopyTo(buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, utf8Json.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="utf8Json" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static JsonDocument Parse(byte[] utf8Json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        return Parse(utf8Json.AsMemory(), settings, options);
    }

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="utf8Json" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static JsonDocument Parse(ReadOnlyMemory<byte> utf8Json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var doc = new JsonDocument(settings);

        try
        {
            doc.Read(utf8Json, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static JsonDocument ParseRented(ReadOnlyMemory<byte> utf8Json, byte[] rentedBuffer,
        SerializerSettings? settings = null, JsonDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var doc = new JsonDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(utf8Json, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public JsonDocument Duplicate()
    {
        var doc = CreateSnapshot<JsonDocument>();

        if (_rentedBuffer != null)
        {
            doc._source = _source.ToArray();
            doc._rentedBuffer = null;
        }

        return doc;
    }

    private static JsonWriteOptions NormalizeWriteOptions(JsonWriteOptions options)
    {
        // A newline-delimited record must be written on a single line.
        return options.UseNewlineDelimitedFormat && options.WriteIndented
            ? options with { WriteIndented = false }
            : options;
    }

    public static string EncodeToString(DElement element,
        JsonWriteOptions options = default)
    {
        options = NormalizeWriteOptions(options);

        using (var cache = InstanceCache<JsonWriter>.Get(() => new JsonWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options);

            if (options.UseNewlineDelimitedFormat)
            {
                WriteNDJson(writer, reader);
            }
            else
            {
                WriteJson(writer, reader);
            }

            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream writeStream,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);

        options = NormalizeWriteOptions(options);

        using (var cache = InstanceCache<JsonWriter>.Get(() => new JsonWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            try
            {
                writer.Reset(writeStream, reader.Settings, options);

                if (options.UseNewlineDelimitedFormat)
                {
                    WriteNDJson(writer, reader);
                }
                else
                {
                    WriteJson(writer, reader);
                }
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);

        options = NormalizeWriteOptions(options);

        using (var cache = InstanceCache<JsonWriter>.Get(() => new JsonWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            try
            {
                writer.Reset(bufferWriter, reader.Settings, options);

                if (options.UseNewlineDelimitedFormat)
                {
                    WriteNDJson(writer, reader);
                }
                else
                {
                    WriteJson(writer, reader);
                }
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static byte[] Encode(DElement element,
        JsonWriteOptions options = default)
    {
        options = NormalizeWriteOptions(options);

        using (var cache = InstanceCache<JsonWriter>.Get(() => new JsonWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options);

            if (options.UseNewlineDelimitedFormat)
            {
                WriteNDJson(writer, reader);
            }
            else
            {
                WriteJson(writer, reader);
            }

            return writer.Encode();
        }
    }


    private static bool TryValidateFloat(ReadOnlySpan<byte> bytes, out ParseException.ErrorCode errorCode)
    {
        errorCode = ParseException.ErrorCode.InvalidNumberValue;

        if (bytes.IsEmpty)
        {
            return false;
        }

        var i = 0;
        var first = bytes[0];

        if (first == '+' || first == '.' || (first == '-' && bytes.Length > 1 && bytes[1] == '.'))
        {
            errorCode = ParseException.ErrorCode.InvalidStartOfNumber;
            return false;
        }

        if (first == '-')
        {
            i++;
        }

        var start = i;
        while (i < bytes.Length && (uint)(bytes[i] - '0') <= 9)
        {
            i++;
        }

        if (i == start)
        {
            if (i < bytes.Length && char.IsAsciiLetter((char)bytes[i]))
            {
                errorCode = ParseException.ErrorCode.NamedFloatingPointLiteral;
            }

            return false;
        }

        if (i < bytes.Length && bytes[i] == '.')
        {
            i++;

            if (i == bytes.Length)
            {
                errorCode = ParseException.ErrorCode.InvalidEndOfNumber;
                return false;
            }

            start = i;
            while (i < bytes.Length && (uint)(bytes[i] - '0') <= 9)
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }
        }

        if (i < bytes.Length && (bytes[i] | 0x20) == 'e')
        {
            i++;

            if (i < bytes.Length && (bytes[i] == '+' || bytes[i] == '-'))
            {
                i++;
            }

            start = i;
            while (i < bytes.Length && (uint)(bytes[i] - '0') <= 9)
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }
        }

        return i == bytes.Length;
    }

    private ParseException? ValidateToken(DToken token)
    {
        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    var param = DToken.DecodeLengthOffsetPayload(token);
                    ReadOnlySpan<byte> bytes;

                    if (token.Variant == DTokenVariant.StringMultilineDoubleQuote)
                    {
                        if ((param.length & 0xffffff) << 8 > 0x3fffffff)
                        {
                            return new ParseException(this, ParseException.ErrorCode.TooLargeValue, param.offset);
                        }

                        bytes = GetStringBlob(token, out _);
                    }
                    else
                    {
                        bytes = _source.Span.Slice(param.offset, param.length);
                    }

                    for (var i = 0; i < bytes.Length; i++)
                    {
                        var next = bytes.Slice(i).IndexOfAny(s_stringValidationSearchValues);
                        if (next < 0)
                        {
                            break;
                        }

                        i += next;

                        if (bytes[i] < 0x20)
                        {
                            return new ParseException(this, ParseException.ErrorCode.UnescapedStringValue,
                                param.offset + i);
                        }

                        if (bytes[i] == '\\')
                        {
                            switch (bytes[i + 1])
                            {
                                case (byte)'u':
                                    if (i + 5 >= bytes.Length)
                                    {
                                        return new ParseException(this,
                                            ParseException.ErrorCode.InvalidEscapeSequence, param.offset + i,
                                            bytes.Length - i);
                                    }

                                    if (bytes.Slice(i + 2, 4).ContainsAnyExcept(s_hexDigitSearchValues))
                                    {
                                        return new ParseException(this,
                                            ParseException.ErrorCode.InvalidEscapeSequence,
                                            param.offset + i, 6);
                                    }

                                    i += 5;
                                    break;
                                case (byte)'"':
                                case (byte)'\\':
                                case (byte)'b':
                                case (byte)'f':
                                case (byte)'n':
                                case (byte)'r':
                                case (byte)'t':
                                case (byte)'/':
                                    i++;
                                    break;
                                default:
                                    return new ParseException(this,
                                        ParseException.ErrorCode.InvalidEscapeSequence, param.offset + i, 2);
                            }
                        }
                    }
                }
                break;
            case DTokenKind.Float:
                {
                    var param = DToken.DecodeLengthOffsetPayload(token);
                    var bytes = _source.Span.Slice(param.offset, param.length);

                    if (!TryValidateFloat(bytes, out var errorCode))
                    {
                        return new ParseException(this, errorCode, param.offset, param.length);
                    }
                }
                break;
            case DTokenKind.Integer:
                {
                    var param = DToken.DecodeLengthOffsetPayload(token);
                    var bytes = _source.Span.Slice(param.offset, param.length);

                    if (bytes[0] == '+' || (bytes.Length == 1 && bytes[0] == '-') ||
                        (bytes.Length > 1 && bytes[0] == '0') ||
                        (bytes.Length > 2 && bytes[0] == '-' && bytes[1] == '0'))
                    {
                        return new ParseException(this, ParseException.ErrorCode.InvalidStartOfNumber,
                            param.offset, param.length);
                    }
                }
                break;
        }

        return null;
    }

    public IReadOnlyList<DocumentParseException> GetValueValidationErrors()
    {
        List<DocumentParseException>? errors = null;

        var tokens = GetTokens();

        foreach (var token in tokens)
        {
            if (token.IsExtended)
            {
                continue;
            }

            var error = ValidateToken(token);
            if (error != null)
            {
                if (errors == null)
                {
                    errors = new List<DocumentParseException>();
                }

                errors.Add(error);
            }
        }

        if (errors != null)
        {
            return errors;
        }

        return Array.Empty<DocumentParseException>();
    }

    internal void ParseInternal(ReadOnlyMemory<byte> bytes, SerializerSettings settings, JsonDocumentOptions options)
    {
        Reset(settings);
        Read(bytes, options);
    }

    private void Read(ReadOnlyMemory<byte> source, JsonDocumentOptions options)
    {
        Utf8Helper.SkipUtf8Bom(ref source);

        _source = source;
        EnsureCapacity(_source.Length / 16);

        {
            var error = options.UseNewlineDelimitedFormat
                ? ParseNDJson(_source.Span, options)
                : ParseJson(_source.Span, options);

            if (error != null)
            {
                throw error;
            }
        }

        if (options.EnableValueValidation)
        {
            foreach (var error in GetValueValidationErrors())
            {
                throw error;
            }
        }

        RootId = 1;
    }

    private ParseException? ParseNDJson(ReadOnlySpan<byte> json, JsonDocumentOptions options)
    {
        const long whiteSpace = (1L << 0x09) | (1L << 0x0a) | (1L << 0x0d) | (1L << 0x20);

        var rootId = AllocToken(DToken.MakeArray(0));
        var latestId = 0U;
        var start = 0;

        while (start < json.Length)
        {
            var newline = json.Slice(start).IndexOf((byte)'\n');
            var end = newline < 0 ? json.Length : start + newline + 1;

            var blank = true;

            for (var i = start; i < end; i++)
            {
                var c = json[i];

                if (c > 0x20 || ((whiteSpace >> c) & 1) == 0)
                {
                    blank = false;
                    break;
                }
            }

            if (blank)
            {
                start = end;
                continue;
            }

            var tokenId = (uint)GetTokens().Length;
            var error = ParseJson(json.Slice(0, end), options, start);

            if (error != null)
            {
                return error;
            }

            IncToken(rootId);

            if (latestId != 0)
            {
                LinkToken(latestId, tokenId);
            }

            latestId = tokenId;
            start = end;
        }

        return null;
    }

    private ParseException? ParseJson(ReadOnlySpan<byte> json, JsonDocumentOptions options, int index = 0)
    {
        using var stack = new LocalStack<(uint pid, uint lid)>(stackalloc (uint pid, uint lid)[16], options.MaxDepth);

        var errorCode = ParseException.ErrorCode.None;
        var allowTrailingCommas = options.AllowTrailingCommas;

        if (index >= json.Length)
        {
            errorCode = ParseException.ErrorCode.NoJsonTokenExists;
            goto parseError;
        }

        const char assignValue = (char)256;
        const char parseNumber = (char)257;
        const long whiteSpace = (1L << 0x09) | (1L << 0x0a) | (1L << 0x0d) | (1L << 0x20);

        var parentId = 0U;
        var latestId = 0U;
        var separateId = 0U;
        var tokenId = 0U;

        for (;;)
        {
            var c = json[index];

            //skip whitespace
            while (c <= 0x20)
            {
                if (((whiteSpace >> c) & 1) == 0)
                {
                    errorCode = ParseException.ErrorCode.InvalidStartOfValue;
                    goto parseError;
                }

                if (++index >= json.Length)
                {
                    errorCode = ParseException.ErrorCode.NoJsonTokenExists;
                    goto parseError;
                }

                c = json[index];
            }

            switch ((char)c)
            {
                case '"': //string or key
                    {
                        var offset = index + 1;
                        var kind = DTokenVariant.String;

                        for (;;)
                        {
                            var start = index + 1;
                            index = json.Slice(start).IndexOfAny((byte)'"', (byte)'\\');
                            if (index < 0)
                            {
                                errorCode = ParseException.ErrorCode.UnterminatedString;
                                goto parseError;
                            }

                            index += start;
                            if (json[index] == '\\')
                            {
                                if (++index >= json.Length)
                                {
                                    errorCode = ParseException.ErrorCode.UnterminatedString;
                                    goto parseError;
                                }

                                kind = DTokenVariant.StringDoubleQuote;
                            }
                            else
                            {
                                break;
                            }
                        }

                        var length = index - offset;

                        if (length > 0x1ffffff)
                        {
                            length >>= 8;

                            if (kind == DTokenVariant.StringDoubleQuote)
                            {
                                length |= 0x1000000;
                            }

                            kind = DTokenVariant.StringMultilineDoubleQuote;
                        }

                        tokenId = AllocToken(DToken.Make(kind,
                            DToken.EncodeLengthOffsetPayload(length, offset)));
                        index++;

                        if (GetToken(parentId).Type == DTokenType.Map)
                        {
                            if (latestId != 0 && latestId != separateId)
                            {
                                errorCode = ParseException.ErrorCode.InvalidAfterValue;
                                goto parseError;
                            }

                            if (index >= json.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidAfterPropertyName;
                                goto parseError;
                            }

                            IncToken(parentId);

                            if (latestId != 0)
                            {
                                LinkToken(latestId + 1, tokenId);
                            }

                            latestId = tokenId;

                            if (!stack.Push((parentId, latestId)))
                            {
                                errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                                goto parseError;
                            }

                            parentId = tokenId;
                            latestId = 0;
                        }
                        else
                        {
                            goto case assignValue;
                        }
                    }
                    break;
                case ':': //name separator
                    {
                        if (latestId != 0 || GetToken(parentId).Type != DTokenType.Text)
                        {
                            errorCode = ParseException.ErrorCode.InvalidAfterPropertyName;
                            goto parseError;
                        }

                        latestId = parentId;

                        if (++index >= json.Length)
                        {
                            errorCode = ParseException.ErrorCode.InvalidAfterPropertyName;
                            goto parseError;
                        }
                    }
                    break;
                case ',': //value separator
                    {
                        if (latestId == separateId || !GetToken(parentId).IsContainer)
                        {
                            errorCode = ParseException.ErrorCode.InvalidStartOfValue;
                            goto parseError;
                        }

                        separateId = latestId;

                        if (++index >= json.Length)
                        {
                            errorCode = ParseException.ErrorCode.InvalidEndOfValue;
                            goto parseError;
                        }
                    }
                    break;
                case '[': //begin array
                case '{': //begin map
                    {
                        tokenId = AllocToken(c == '[' ? DToken.MakeArray(0) : DToken.MakeMap(0));

                        if (!stack.Push((parentId, latestId)))
                        {
                            errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                            goto parseError;
                        }

                        parentId = tokenId;
                        latestId = 0;
                        separateId = 0;

                        if (++index >= json.Length)
                        {
                            errorCode = c == '['
                                ? ParseException.ErrorCode.UnterminatedArray
                                : ParseException.ErrorCode.UnterminatedObject;
                            goto parseError;
                        }
                    }
                    break;
                case ']': //end array
                    {
                        tokenId = parentId;

                        if (latestId != 0 && latestId == separateId && !allowTrailingCommas)
                        {
                            errorCode = ParseException.ErrorCode.InvalidTrailingCommas;
                            goto parseError;
                        }

                        if (GetToken(tokenId).Type != DTokenType.Array)
                        {
                            errorCode = ParseException.ErrorCode.InvalidEndOfArray;
                            goto parseError;
                        }

                        (parentId, latestId) = stack.Pop();
                        index++;

                        separateId = latestId;

                        goto case assignValue;
                    }
                case '}': //end map
                    {
                        tokenId = parentId;

                        if (latestId != 0 && latestId == separateId && !allowTrailingCommas)
                        {
                            errorCode = ParseException.ErrorCode.InvalidTrailingCommas;
                            goto parseError;
                        }

                        if (GetToken(tokenId).Type != DTokenType.Map)
                        {
                            errorCode = GetToken(tokenId).Type == DTokenType.Text
                                ? ParseException.ErrorCode.InvalidAfterPropertyName
                                : ParseException.ErrorCode.InvalidEndOfObject;
                            goto parseError;
                        }

                        (parentId, latestId) = stack.Pop();
                        index++;

                        separateId = latestId;

                        goto case assignValue;
                    }
                case 't': //true
                    if (json.Slice(index + 1).StartsWith("rue"u8))
                    {
                        tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanTrue, 0));
                        index += 4;
                        goto case assignValue;
                    }

                    errorCode = ParseException.ErrorCode.InvalidBooleanValue;
                    goto parseError;
                case 'f': //false
                    if (json.Slice(index + 1).StartsWith("alse"u8))
                    {
                        tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanFalse, 0));
                        index += 5;
                        goto case assignValue;
                    }

                    errorCode = ParseException.ErrorCode.InvalidBooleanValue;
                    goto parseError;
                case 'n': //null
                    if (json.Slice(index + 1).StartsWith("ull"u8))
                    {
                        tokenId = AllocToken(DToken.Make(DTokenVariant.Null, 0));
                        index += 4;
                        goto case assignValue;
                    }

                    goto case parseNumber;
                case assignValue:
                    if (parentId == 0)
                    {
                        if (index > json.Length)
                        {
                            errorCode = ParseException.ErrorCode.InvalidEndOfValue;
                            goto parseError;
                        }

                        while (index < json.Length)
                        {
                            c = json[index++];

                            if (c > 0x20)
                            {
                                errorCode = ParseException.ErrorCode.AdditionalTextEncountered;
                                goto parseError;
                            }

                            if (((whiteSpace >> c) & 1) == 0)
                            {
                                errorCode = ParseException.ErrorCode.InvalidEndOfValue;
                                goto parseError;
                            }
                        }

                        return null;
                    }

                    if (index >= json.Length)
                    {
                        errorCode = ParseException.ErrorCode.InvalidEndOfValue;
                        goto parseError;
                    }

                    var parentType = GetToken(parentId).Type;

                    if (parentType == DTokenType.Array)
                    {
                        if (latestId != 0 && latestId != separateId)
                        {
                            errorCode = ParseException.ErrorCode.InvalidAfterValue;
                            goto parseError;
                        }

                        IncToken(parentId);

                        if (latestId != 0)
                        {
                            LinkToken(latestId, tokenId);
                        }

                        latestId = tokenId;
                    }
                    else
                    {
                        if (parentType == DTokenType.Text)
                        {
                            if (latestId != parentId)
                            {
                                errorCode = ParseException.ErrorCode.InvalidAfterPropertyName;
                                goto parseError;
                            }

                            (parentId, latestId) = stack.Pop();
                            separateId = 0;
                        }
                        else
                        {
                            errorCode = ParseException.ErrorCode.InvalidAfterValue;
                            goto parseError;
                        }
                    }

                    break;
                case parseNumber:
                    {
                        var tokenType = DTokenVariant.Integer;
                        var offset = index;
                        var minus = false;

                        if (c == '-')
                        {
                            if (++index >= json.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidStartOfNumber;
                                goto parseError;
                            }

                            c = json[index];
                            minus = true;
                        }
                        else
                        {
                            tokenType = DTokenVariant.IntegerUnsigned;
                        }

                        for (;;)
                        {
                            if (c < '0' || c > '9')
                            {
                                if (c <= 0x20 || c == ']' || c == ',' || c == '}')
                                {
                                    break;
                                }

                                tokenType = DTokenVariant.Float;
                            }

                            if (++index >= json.Length)
                            {
                                break;
                            }

                            c = json[index];
                        }

                        var length = index - offset;
                        if (length > 19)
                        {
                            if (tokenType != DTokenVariant.Float)
                            {
                                tokenType = Utf8Helper.GetIntegerTokenKind(json.Slice(offset, length));
                            }
                        }

                        if (minus && length == 2)
                        {
                            if (json[offset + 1] == '0')
                            {
                                tokenType = DTokenVariant.Float; //-0
                            }
                        }

                        tokenId = AllocToken(DToken.Make(tokenType, DToken.EncodeLengthOffsetPayload(length, offset)));
                        goto case assignValue;
                    }
                default:
                    goto case parseNumber;
            }
        }

        parseError:

        if (index >= json.Length)
        {
            index = json.Length - 1;
        }

        if (index < 0)
        {
            index = 0;
        }

        return new ParseException(this, errorCode, index);
    }


    internal static void WriteJson(JsonWriter writer, in DataReader reader)
    {
        if (reader.Document is JsonDocument doc)
        {
            WriteValue(writer, reader, doc, reader.RootId);
        }
        else
        {
            WriteValue(writer, reader, reader.RootId);
        }
    }

    private static void WriteNDJson(JsonWriter writer, in DataReader reader)
    {
        if (reader.GetToken(reader.RootId).Type != DTokenType.Array)
        {
            WriteJson(writer, reader);
            return;
        }

        var doc = reader.Document as JsonDocument;
        var first = true;

        foreach (var valueId in reader.EnumerateArray(reader.RootId))
        {
            if (!first)
            {
                writer.WriteNewLine();
            }

            first = false;

            if (doc != null)
            {
                WriteValue(writer, reader, doc, valueId);
            }
            else
            {
                WriteValue(writer, reader, valueId);
            }
        }
    }

    private static void WriteValue(JsonWriter writer, in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                writer.WriteStartArray();
                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    WriteValue(writer, reader, valueId);
                }

                writer.WriteEndArray();
            }
            else
            {
                writer.WriteStartMap();
                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var keyToken = reader.GetToken(kv.Key);

                    if (keyToken.IsStringEncoded)
                    {
                        writer.WriteString(reader.ReadUtf8String(kv.Key));
                    }
                    else
                    {
                        writer.WriteString(
                            Encode(reader.ReadElement(kv.Key)));
                    }

                    var valueId = kv.Value;

                    WriteValue(writer, reader, valueId);
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
                case DTokenKind.Float:
                    switch (token.FloatKind)
                    {
                        case FloatKind.Single:
                            writer.WriteSingle(reader.ReadSingle(tokenId));
                            break;
                        case FloatKind.Half:
                            writer.WriteHalf(reader.ReadHalf(tokenId));
                            break;
                        case FloatKind.Decimal:
                            writer.WriteDecimal(reader.ReadDecimal(tokenId));
                            break;
                        default:
                            writer.WriteDouble(reader.ReadDouble(tokenId));
                            break;
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
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
            }
        }
    }


    private static void WriteValue(JsonWriter writer, in DataReader reader, JsonDocument doc, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                writer.WriteStartArray();
                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    WriteValue(writer, reader, doc, valueId);
                }

                writer.WriteEndArray();
            }
            else
            {
                writer.WriteStartMap();
                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    if (doc.GetRawData(kv.Key, out var kSpan))
                    {
                        writer.WriteRawString(kSpan);
                    }
                    else
                    {
                        var keyToken = reader.GetToken(kv.Key);

                        if (keyToken.IsStringEncoded)
                        {
                            writer.WriteString(reader.ReadUtf8String(kv.Key));
                        }
                        else
                        {
                            var element = reader.ReadElement(kv.Key);

                            writer.WriteString(Encode(element));
                        }
                    }

                    var valueId = kv.Value;

                    WriteValue(writer, reader, doc, valueId);
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
                    if (doc.GetRawData(tokenId, out var sSpan))
                    {
                        writer.WriteRawString(sSpan);
                    }
                    else
                    {
                        writer.WriteString(reader.ReadUtf8String(tokenId));
                    }

                    break;
                case DTokenKind.Integer:
                    if (doc.GetRawData(tokenId, out var iSpan))
                    {
                        writer.WriteBigNumber(iSpan);
                    }
                    else
                    {
                        if (token.Variant == DTokenVariant.IntegerUnsigned)
                        {
                            writer.WriteUInt64(reader.ReadUInt64(tokenId));
                        }
                        else
                        {
                            writer.WriteInt64(reader.ReadInt64(tokenId));
                        }
                    }

                    break;
                case DTokenKind.Float:
                    if (doc.GetRawData(tokenId, out var fSpan))
                    {
                        writer.WriteBigNumber(fSpan);
                    }
                    else
                    {
                        switch (token.FloatKind)
                        {
                            case FloatKind.Single:
                                writer.WriteSingle(reader.ReadSingle(tokenId));
                                break;
                            case FloatKind.Half:
                                writer.WriteHalf(reader.ReadHalf(tokenId));
                                break;
                            case FloatKind.Decimal:
                                writer.WriteDecimal(reader.ReadDecimal(tokenId));
                                break;
                            default:
                                writer.WriteDouble(reader.ReadDouble(tokenId));
                                break;
                        }
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
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
            }
        }
    }

    protected override long DecodeInteger(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        if (token.Variant == DTokenVariant.IntegerUnsigned)
        {
            if (Utf8Parser.TryParse(span, out ulong value, out var bytesConsumed) && bytesConsumed == span.Length)
            {
                return (long)value;
            }
        }
        else
        {
            if (Utf8Parser.TryParse(span, out long value, out var bytesConsumed) && bytesConsumed == span.Length)
            {
                return value;
            }
        }

        throw new ParseException(this, ParseException.ErrorCode.InvalidNumberValue, param.offset, param.length);
    }

    protected override ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        return span;
    }

    protected override bool TryDecodeDecimal(DToken token, out decimal result)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        if (Utf8Parser.TryParse(span, out result, out var bytes) && bytes == span.Length)
        {
            return true;
        }

        result = default;
        return false;
    }

    private ReadOnlySpan<byte> GetStringBlob(DToken token, out bool escaped)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        var blob = _source.Slice(param.offset).Span;

        var length = param.length;

        if ((length & 0x1000000) != 0)
        {
            length &= 0xffffff;
            escaped = true;
        }
        else
        {
            escaped = false;
        }

        length <<= 8;

        while (true)
        {
            var quoteOffset = blob.Slice(length).IndexOf((byte)'"');
            if (quoteOffset < 0)
            {
                break;
            }

            var quoteIndex = length + quoteOffset;
            var backslashCount = 0;
            for (var i = quoteIndex - 1; i >= 0 && blob[i] == '\\'; i--)
            {
                backslashCount++;
            }

            if ((backslashCount & 1) == 0)
            {
                length = quoteIndex;
                break;
            }

            length = quoteIndex + 1;
        }

        return blob.Slice(0, length);
    }

    protected override string DecodeString(DToken token)
    {
        var kind = token.Variant;

        if (kind != DTokenVariant.StringMultilineDoubleQuote)
        {
            var param = DToken.DecodeLengthOffsetPayload(token);
            var src = _source.Slice(param.offset, param.length);

            if (kind == DTokenVariant.String)
            {
                return Utf8Helper.GetUtf16String(src.Span);
            }

            if (TryDecodeEscapedJsonStringUtf16(src.Span, out var value))
            {
                return value;
            }

            throw new ParseException(this, ParseException.ErrorCode.UnescapedStringValue, param.offset, param.length);
        }
        else
        {
            var src = GetStringBlob(token, out var escaped);

            if (!escaped)
            {
                return Utf8Helper.GetUtf16String(src);
            }

            if (TryDecodeEscapedJsonStringUtf16(src, out var value))
            {
                return value;
            }

            throw new ParseException(this, ParseException.ErrorCode.UnescapedStringValue,
                DToken.DecodeLengthOffsetPayload(token).offset);
        }
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var kind = token.Variant;

        if (kind != DTokenVariant.StringMultilineDoubleQuote)
        {
            var param = DToken.DecodeLengthOffsetPayload(token);
            var src = _source.Slice(param.offset, param.length);

            if (kind == DTokenVariant.String)
            {
                return src.Span;
            }

            if (TryDecodeEscapedJsonString(src.Span, out var value))
            {
                return value;
            }

            throw new ParseException(this, ParseException.ErrorCode.UnescapedStringValue, param.offset, param.length);
        }
        else
        {
            var src = GetStringBlob(token, out var escaped);

            if (!escaped)
            {
                return src;
            }

            if (TryDecodeEscapedJsonString(src, out var value))
            {
                return value;
            }

            throw new ParseException(this, ParseException.ErrorCode.UnescapedStringValue,
                DToken.DecodeLengthOffsetPayload(token).offset);
        }
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var src = _source.Span.Slice(param.offset, param.length);

        if (Utf8Parser.TryParse(src, out double value, out var bytes) && bytes == src.Length)
        {
            return value;
        }

        if ((Settings.FloatFormatHandling & FloatFormatHandling.SpecialFloatMask) ==
            FloatFormatHandling.SpecialFloatAsXmlSymbol)
        {
            if (src.SequenceEqual("INF"u8))
            {
                return double.PositiveInfinity;
            }

            if (src.SequenceEqual("-INF"u8))
            {
                return double.NegativeInfinity;
            }
        }

        throw new ParseException(this, ParseException.ErrorCode.InvalidNumberValue, param.offset, param.length);
    }

    protected override ReadOnlySpan<byte> DecodeByteString(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, param.length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetRawData(uint tokenId, out ReadOnlySpan<byte> data)
    {
        var token = GetToken(tokenId);

        if (token.IsExtended)
        {
            data = ReadOnlySpan<byte>.Empty;
            return false;
        }

        if (token.Variant == DTokenVariant.StringMultilineDoubleQuote)
        {
            data = GetStringBlob(token, out _);
        }
        else
        {
            var param = DToken.DecodeLengthOffsetPayload(token);

            data = _source.Slice(param.offset, param.length).Span;
        }

        return true;
    }


    private static bool TryDecodeEscapedJsonStringUtf16(ReadOnlySpan<byte> src, out string value)
    {
        var buffer = src.Length >= 256 ? ArrayPool<char>.Shared.Rent(src.Length) : null;

        try
        {
            var dest = buffer != null ? buffer.AsSpan() : stackalloc char[src.Length];
            var status = OperationStatus.Done;

            var d = 0;

            for (;;)
            {
                var index = src.IndexOf((byte)'\\');

                if (index < 0)
                {
                    status = Utf8.ToUtf16(src, dest.Slice(d), out var bytesRead, out var charsWritten);
                    d += charsWritten;
                    break;
                }

                if (index > 0)
                {
                    status = Utf8.ToUtf16(src.Slice(0, index), dest.Slice(d), out var bytesRead,
                        out var charsWritten);
                    d += charsWritten;
                }

                if (index + 2 > src.Length)
                {
                    status = OperationStatus.InvalidData;
                    break;
                }

                index++;
                var c = src[index++];

                if (c == 'u')
                {
                    if (index + 4 > src.Length ||
                        !(Utf8Parser.TryParse(src.Slice(index, 4), out ushort uc, out var bytesConsumed, 'X') &&
                          bytesConsumed == 4))
                    {
                        status = OperationStatus.InvalidData;
                        break;
                    }

                    dest[d++] = (char)uc;
                    index += 4;
                }
                else
                {
                    if (c < s_unescapeTable.Length && s_unescapeTable[c] != 0)
                    {
                        dest[d++] = (char)s_unescapeTable[c];
                    }
                    else
                    {
                        status = OperationStatus.InvalidData;
                        break;
                    }
                }

                src = src.Slice(index);
            }

            if (status != OperationStatus.Done)
            {
                value = string.Empty;
                return false;
            }
            else
            {
                value = new string(dest.Slice(0, d));
                return true;
            }
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
    }

    private static bool TryDecodeEscapedJsonString(ReadOnlySpan<byte> src, out ReadOnlySpan<byte> value)
    {
        var buffer = new byte[src.Length];

        var dest = buffer.AsSpan();
        var d = 0;
        var status = OperationStatus.Done;
        var ls = 0;

        for (;;)
        {
            var index = src.IndexOf((byte)'\\');

            if (index < 0)
            {
                src.CopyTo(dest.Slice(d));
                d += src.Length;
                break;
            }

            if (index > 0)
            {
                src.Slice(0, index).CopyTo(dest.Slice(d));
                d += index;
            }

            if (index + 2 > src.Length)
            {
                status = OperationStatus.InvalidData;
                break;
            }

            index++;
            var c = src[index++];

            if (c == 'u')
            {
                if (index + 4 > src.Length ||
                    !(Utf8Parser.TryParse(src.Slice(index, 4), out ushort uc, out var bytesConsumed, 'X') &&
                      bytesConsumed == 4))
                {
                    status = OperationStatus.InvalidData;
                    break;
                }


                if (uc >= 0xD800 && uc <= 0xDBFF)
                {
                    ls = uc;
                }
                else
                {
                    if (uc >= 0xDC00 && uc <= 0xDFFF)
                    {
                        if (ls == 0)
                        {
                            status = Utf8.FromUtf16([(char)0xFFFD], dest.Slice(d), out var charRead,
                                out var bytesWritten);
                            d += bytesWritten;
                        }
                        else
                        {
                            status = Utf8.FromUtf16([(char)ls, (char)uc], dest.Slice(d), out var charRead,
                                out var bytesWritten);
                            d += bytesWritten;
                            ls = 0;
                        }
                    }
                    else
                    {
                        if (ls != 0)
                        {
                            status = Utf8.FromUtf16([(char)0xFFFD], dest.Slice(d), out var charRead,
                                out var bytesWritten);
                            d += bytesWritten;
                            ls = 0;
                        }

                        {
                            status = Utf8.FromUtf16([(char)uc], dest.Slice(d), out var charRead,
                                out var bytesWritten);
                            d += bytesWritten;
                        }
                    }
                }

                index += 4;
            }
            else
            {
                if (c < s_unescapeTable.Length && s_unescapeTable[c] != 0)
                {
                    dest[d++] = s_unescapeTable[c];
                }
                else
                {
                    status = OperationStatus.InvalidData;
                    break;
                }
            }

            src = src.Slice(index);
        }

        if (ls != 0)
        {
            status = Utf8.FromUtf16([(char)0xFFFD], dest.Slice(d), out var charRead, out var bytesWritten);
            d += bytesWritten;
        }

        if (status != OperationStatus.Done)
        {
            value = ReadOnlySpan<byte>.Empty;
            return false;
        }

        value = dest.Slice(0, d);
        return true;
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            NoJsonTokenExists,
            InvalidTrailingCommas,
            InvalidNullValue,
            InvalidBooleanValue,
            InvalidNumberValue,
            UnescapedStringValue,
            InvalidEscapeSequence,
            InvalidStartOfNumber,
            InvalidStartOfValue,
            InvalidEndOfNumber,
            InvalidEndOfObject,
            InvalidEndOfArray,
            InvalidEndOfValue,
            UnterminatedObject,
            UnterminatedArray,
            UnterminatedString,
            InvalidAfterPropertyName,
            InvalidAfterValue,
            NamedFloatingPointLiteral,
            AdditionalTextEncountered,
            MaxDepthExceeded,
            TooLargeValue
        }

        public ParseException(JsonDocument doc, ErrorCode error, int offset, int length = -1)
            : this(doc, error, offset, length, GetLinePosition(doc._source.Span, offset))
        {
        }

        private ParseException(JsonDocument doc, ErrorCode error, int offset, int length,
            (long LineNumber, long BytePositionInLine) position)
            : base(nameof(JsonDocument), offset, CreateMessage(doc, error, offset, length, position), position.LineNumber,
                position.BytePositionInLine)
        {
        }

        private static string CreateMessage(JsonDocument doc, ErrorCode error, int offset, int length,
            (long LineNumber, long BytePositionInLine) position)
        {
            var sb = new StringBuilder();

            sb.Append($"{nameof(JsonDocument)} Error: {error}");
            AppendErrorValue(sb, doc._source.Span, offset, length);
            sb.Append($", LineNumber: {position.LineNumber}, BytePositionInLine: {position.BytePositionInLine}");

            return sb.ToString();
        }
    }
}