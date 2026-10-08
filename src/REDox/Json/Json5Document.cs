// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization;

namespace REDox.Json;

public sealed class Json5Document : Document
{
    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;
    private uint _tailId;

    private Json5Document(SerializerSettings settings) : base(settings)
    {
    }

    public Json5Document Duplicate()
    {
        var doc = CreateSnapshot<Json5Document>();

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

    public static bool TryParse(ReadOnlySpan<byte> json5, [NotNullWhen(true)] out Json5Document? document,
        SerializerSettings? settings = null,
        Json5DocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        document = new Json5Document(settings);
        document.EnsureCapacity(json5.Length / 16);

        var error = document.ParseJson5(json5, out var rootId, options);

        if (error != null)
        {
            document.Dispose();
            document = null;
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(json5.Length);

        json5.CopyTo(buffer);
        document._source = buffer.AsMemory(0, json5.Length);
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

        document.RootId = rootId;
        document._tailId = document.AllocToken(default);

        return true;
    }

    public static Json5Document Parse(string json5, SerializerSettings? settings = null,
        Json5DocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(json5.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json5, buffer.AsSpan());
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

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="json5" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static Json5Document Parse(byte[] json5, SerializerSettings? settings = null,
        Json5DocumentOptions options = default)
    {
        return Parse(json5.AsMemory(), settings, options);
    }


    public static Json5Document Parse(ReadOnlySpan<byte> json5, SerializerSettings? settings = null,
        Json5DocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(json5.Length);

        try
        {
            json5.CopyTo(buffer.AsSpan());
            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, json5.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="json5" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static Json5Document Parse(ReadOnlyMemory<byte> json5, SerializerSettings? settings = null,
        Json5DocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var doc = new Json5Document(settings);

        try
        {
            doc.Read(json5, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static Json5Document ParseRented(ReadOnlyMemory<byte> json5, byte[] rentedBuffer,
        SerializerSettings? settings = null, Json5DocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var doc = new Json5Document(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(json5, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static Json5Document Parse(Stream stream, SerializerSettings? settings = null,
        Json5DocumentOptions options = default)
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

    public static string EncodeToString(DElement element,
        Json5WriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.Reset(reader.Settings, options.TextWriteOptions);
            Write(writer, reader, options);
            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream writeStream,
        Json5WriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writeStream);

        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.Reset(writeStream, reader.Settings, options.TextWriteOptions);
                Write(writer, reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        Json5WriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);

        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.Reset(bufferWriter, reader.Settings, options.TextWriteOptions);
                Write(writer, reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static byte[] Encode(DElement element,
        Json5WriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions);
            Write(writer, in reader, options);
            return writer.Encode();
        }
    }

    private void Read(ReadOnlyMemory<byte> source, Json5DocumentOptions options)
    {
        Utf8Helper.SkipUtf8Bom(ref source);

        _source = source;

        EnsureCapacity(_source.Length / 16);

        {
            var error = ParseJson5(_source.Span, out var rootId, options);

            RootId = rootId;
            _tailId = AllocToken(default);

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
    }


    private ParseException? ValidateToken(DToken token)
    {
        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    var param = DToken.DecodeLengthOffsetPayload(token);
                    var bytes = _source.Span.Slice(param.offset, param.length);

                    if (token.Variant == DTokenVariant.String)
                    {
                        if (!IsEcmaScriptIdentity(bytes))
                        {
                            return new ParseException(this, ParseException.ErrorCode.IllegalUnquotedKey,
                                param.offset);
                        }
                    }

                    var i = 0;
                    var highSurrogate = 0;

                    while (i < bytes.Length)
                    {
                        var c = bytes[i++];

                        if (c < 0x20)
                        {
                            return new ParseException(this, ParseException.ErrorCode.UnescapedStringValue,
                                param.offset + i);
                        }

                        if (c == '\\')
                        {
                            if (i >= bytes.Length)
                            {
                                return new ParseException(this, ParseException.ErrorCode.UnescapedStringValue,
                                    param.offset + i);
                            }

                            c = bytes[i++];

                            if (IsLineTerminator(c, bytes, ref i))
                            {
                                continue;
                            }

                            switch (c)
                            {
                                case (byte)'x':
                                case (byte)'u':
                                    var len = c == (byte)'x' ? 2 : 4;

                                    i += len;

                                    if (i > bytes.Length)
                                    {
                                        return new ParseException(this,
                                            ParseException.ErrorCode.InvalidEscapeSequence,
                                            param.offset + i - 1 - len);
                                    }

                                    if (!TryParseHex(bytes.Slice(i - len, len), out var codePoint))
                                    {
                                        return new ParseException(this,
                                            ParseException.ErrorCode.InvalidEscapeSequence, param.offset + i - 1 - len,
                                            len + 1);
                                    }

                                    if (codePoint >= 0xd800 && codePoint <= 0xdbff)
                                    {
                                        highSurrogate = codePoint;
                                        continue;
                                    }

                                    if (codePoint >= 0xdc00 && codePoint <= 0xdfff)
                                    {
                                        if (highSurrogate != 0)
                                        {
                                            codePoint = 0x10000 + (highSurrogate - 0xd800) * 0x400 +
                                                        (codePoint - 0xdc00);
                                            highSurrogate = 0;
                                        }
                                        else
                                        {
                                            return new ParseException(this,
                                                ParseException.ErrorCode.InvalidSurrogatePair,
                                                param.offset + i - 1 - len,
                                                len + 1);
                                        }
                                    }

                                    if (!Rune.TryCreate(codePoint, out _))
                                    {
                                        return new ParseException(this, ParseException.ErrorCode.InvalidSurrogatePair,
                                            param.offset + i - 1 - len, len + 1);
                                    }

                                    break;
                                case (byte)'0':
                                    if (i < bytes.Length && bytes[i] >= '0' && bytes[i] <= '9')
                                    {
                                        return new ParseException(this,
                                            ParseException.ErrorCode.InvalidEscapeSequence, param.offset + i - 1, 2);
                                    }

                                    break;
                                case (byte)'1':
                                case (byte)'2':
                                case (byte)'3':
                                case (byte)'4':
                                case (byte)'5':
                                case (byte)'6':
                                case (byte)'7':
                                case (byte)'8':
                                case (byte)'9':
                                    return new ParseException(this,
                                        ParseException.ErrorCode.InvalidEscapeSequence, param.offset + i - 1, 1);
                            }
                        }
                    }

                    if (highSurrogate != 0)
                    {
                        return new ParseException(this, ParseException.ErrorCode.InvalidSurrogatePair, param.offset,
                            param.length);
                    }
                }
                break;
            case DTokenKind.Float:
                {
                    var param = DToken.DecodeLengthOffsetPayload(token);
                    var bytes = _source.Span.Slice(param.offset, param.length);

                    if (!Utf8Parser.TryParse(bytes, out double _, out var bytesConsumed) ||
                        param.length != bytesConsumed)
                    {
                        return new ParseException(this, ParseException.ErrorCode.InvalidNumberValue, param.offset,
                            param.length);
                    }
                }
                break;
            case DTokenKind.Integer:
                {
                    var param = DToken.DecodeLengthOffsetPayload(token);
                    var bytes = _source.Span.Slice(param.offset, param.length);

                    if (token.Variant == DTokenVariant.IntegerHexadecimal)
                    {
                        if (!Utf8Helper.TryParseHexadecimal(bytes, out _))
                        {
                            return new ParseException(this, ParseException.ErrorCode.InvalidNumberValue,
                                param.offset,
                                param.length);
                        }
                    }
                    else
                    {
                        if ((bytes.Length == 1 && (bytes[0] == '-' || bytes[0] == '+')) ||
                            (bytes.Length > 1 && bytes[0] == '0') ||
                            (bytes.Length > 2 && bytes[1] == '0' && (bytes[0] == '-' || bytes[0] == '+')))
                        {
                            return new ParseException(this, ParseException.ErrorCode.InvalidStartOfNumber,
                                param.offset, param.length);
                        }
                    }

                    break;
                }
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

    private ParseException? ParseJson5(ReadOnlySpan<byte> json, out uint rootId, Json5DocumentOptions options)
    {
        using var stack =
            new Helper.LocalStack<(uint pid, uint lid)>(stackalloc (uint pid, uint lid)[16], options.MaxDepth);

        rootId = 1;

        var errorCode = ParseException.ErrorCode.None;
        var index = 0;
        var trivia = options.PreserveTrivia;

        if (json.Length == 0)
        {
            errorCode = ParseException.ErrorCode.NoJsonTokenExists;
            goto parseError;
        }

        const char assignValue = (char)256;
        const char parseNumber = (char)257;
        const char parsePropertyName = (char)258;
        const char parseLineComment = (char)259;
        const char parseBlockComment = (char)260;

        const long whiteSpace =
            (1L << 0x09) | (1L << 0x0a) | (1L << 0x0b) | (1L << 0x0c) | (1L << 0x0d) | (1L << 0x20);

        var parentId = 0U;
        var latestId = 0U;
        var separateId = 0U;
        var tokenId = 0U;

        for (;;)
        {
            var c = json[index];

            //skip whitespace
            var ws = index;

            while (c <= 0x20)
            {
                if (((whiteSpace >> c) & 1) == 0)
                {
                    errorCode = ParseException.ErrorCode.InvalidStartOfValue;
                    goto parseError;
                }

                if (++index >= json.Length)
                {
                    if (trivia && ws != index)
                    {
                        AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                            DToken.EncodeLengthOffsetPayload(index - ws, ws)));
                    }

                    goto parseEnd;
                }

                c = json[index];
            }

            if (trivia && ws != index)
            {
                AllocToken(
                    DToken.Make(DTokenVariant.TriviaWhitespace, DToken.EncodeLengthOffsetPayload(index - ws, ws)));
            }

            switch ((char)c)
            {
                case '"': //string or key
                case '\'':
                    {
                        var offset = index + 1;
                        var escaped = false;
                        var doubleQuote = c == (byte)'"';

                        for (;;)
                        {
                            var start = index + 1;
                            if (doubleQuote)
                            {
                                index = json.Slice(start).IndexOfAny((byte)'"', (byte)'\\');
                            }
                            else
                            {
                                index = json.Slice(start).IndexOfAny((byte)'\'', (byte)'\\');
                            }

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

                                escaped = true;
                            }
                            else
                            {
                                break;
                            }
                        }

                        var kind = escaped
                            ? doubleQuote
                                ? DTokenVariant.StringMultilineDoubleQuote
                                : DTokenVariant.StringMultilineSingleQuote
                            : doubleQuote
                                ? DTokenVariant.StringDoubleQuote
                                : DTokenVariant.StringSingleQuote;

                        tokenId = AllocToken(DToken.Make(
                            kind,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));
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
                                do
                                {
                                    latestId++;
                                } while (GetToken(latestId).IsIgnore);

                                LinkToken(latestId, tokenId);
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

                        if (trivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(1, index)));
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

                        if (trivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(1, index)));
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

                        if (GetToken(tokenId).Type != DTokenType.Array)
                        {
                            errorCode = ParseException.ErrorCode.InvalidEndOfArray;
                            goto parseError;
                        }

                        if (trivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(1, index)));
                        }

                        (parentId, latestId) = stack.Pop();
                        index++;

                        separateId = latestId;

                        goto case assignValue;
                    }
                case '}': //end map
                    {
                        tokenId = parentId;

                        if (GetToken(tokenId).Type != DTokenType.Map)
                        {
                            errorCode = GetToken(tokenId).Type == DTokenType.Text
                                ? ParseException.ErrorCode.InvalidAfterPropertyName
                                : ParseException.ErrorCode.InvalidEndOfObject;
                            goto parseError;
                        }

                        if (trivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(1, index)));
                        }

                        (parentId, latestId) = stack.Pop();
                        index++;

                        separateId = latestId;

                        goto case assignValue;
                    }
                case '/':
                    {
                        if (++index < json.Length)
                        {
                            c = json[index];
                            if (c == '/')
                            {
                                goto case parseLineComment;
                            }

                            if (c == '*')
                            {
                                goto case parseBlockComment;
                            }
                        }

                        errorCode = ParseException.ErrorCode.InvalidComment;
                        goto parseError;
                    }
                case (char)0xc2:
                case (char)0xe1:
                case (char)0xe2:
                case (char)0xe3:
                case (char)0xef:
                    var len = GetUnicodeWhiteSpaceLength(json.Slice(index));

                    if (len > 0)
                    {
                        if (trivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                                DToken.EncodeLengthOffsetPayload(len, index)));
                        }

                        index += len;
                        if (index >= json.Length)
                        {
                            goto parseEnd;
                        }

                        break;
                    }

                    goto default;
                case assignValue:
                    if (parentId == 0)
                    {
                        if (index > json.Length)
                        {
                            errorCode = ParseException.ErrorCode.InvalidEndOfValue;
                            goto parseError;
                        }

                        if (index == json.Length)
                        {
                            goto parseEnd;
                        }

                        goto parseTrailingTrivia;
                    }

                    if (index >= json.Length)
                    {
                        errorCode = ParseException.ErrorCode.InvalidEndOfValue;
                        goto parseError;
                    }

                    var parentToken = GetToken(parentId);

                    if (parentToken.Type == DTokenType.Array)
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
                        if (parentToken.Type == DTokenType.Text)
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
                        var tokenType = DTokenVariant.IntegerUnsigned;
                        var offset = index;
                        var minus = false;

                        if (c == '-' || c == '+')
                        {
                            if (++index >= json.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidStartOfNumber;
                                goto parseError;
                            }

                            if (c == '-')
                            {
                                tokenType = DTokenVariant.Integer;
                                minus = true;
                            }

                            c = json[index];
                        }

                        if (c == 'n' || c == 'i' ||
                            (c == 'N' && !json.Slice(index).StartsWith("NaN"u8)) ||
                            (c == 'I' && !json.Slice(index).StartsWith("Infinity"u8))
                           )
                        {
                            errorCode = ParseException.ErrorCode.InvalidNumberValue;
                            goto parseError;
                        }

                        for (;;)
                        {
                            if (c < '0' || c > '9')
                            {
                                if (c <= 0x20 || c == ']' || c == ',' || c == '}' || c == '/' || (c & 0x80) != 0)
                                {
                                    break;
                                }

                                if (c == 'x' || c == 'X')
                                {
                                    tokenType = DTokenVariant.IntegerHexadecimal;
                                }
                                else
                                {
                                    if (tokenType != DTokenVariant.IntegerHexadecimal)
                                    {
                                        tokenType = DTokenVariant.Float;
                                    }
                                }
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
                                if (tokenType == DTokenVariant.IntegerHexadecimal)
                                {
                                    tokenType = DTokenVariant.BigNumberHexadecimal;
                                }
                                else
                                {
                                    tokenType = Utf8Helper.GetIntegerTokenKind(json.Slice(offset, length));
                                }
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
                case parsePropertyName:
                    {
                        var offset = index;
                        var kind = DTokenVariant.String;

                        while (!(c <= 0x20 || c == ':'))
                        {
                            if (c == '\\')
                            {
                                kind = DTokenVariant.StringLiteral;
                            }

                            index++;
                            if (index >= json.Length)
                            {
                                errorCode = ParseException.ErrorCode.InvalidAfterPropertyName;
                                goto parseError;
                            }

                            c = json[index];
                        }

                        if (latestId != 0 && latestId != separateId)
                        {
                            errorCode = ParseException.ErrorCode.InvalidAfterValue;
                            goto parseError;
                        }

                        tokenId = AllocToken(DToken.Make(kind,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));

                        IncToken(parentId);

                        if (latestId != 0)
                        {
                            do
                            {
                                latestId++;
                            } while (GetToken(latestId).IsIgnore);

                            LinkToken(latestId, tokenId);
                        }

                        latestId = tokenId;

                        if (!stack.Push((parentId, latestId)))
                        {
                            errorCode = ParseException.ErrorCode.MaxDepthExceeded;
                            goto parseError;
                        }

                        parentId = tokenId;
                        latestId = 0;
                        break;
                    }
                case parseLineComment:
                    {
                        if (index + 1 >= json.Length)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                                DToken.EncodeLengthOffsetPayload(0, json.Length)));

                            goto parseEnd;
                        }

                        c = json[++index];

                        var offset = index;

                        for (;;)
                        {
                            if (c == '\n' || c == '\r')
                            {
                                break;
                            }

                            if (index + 1 >= json.Length)
                            {
                                AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                                    DToken.EncodeLengthOffsetPayload(index + 1 - offset, offset)));

                                goto parseEnd;
                            }

                            c = json[++index];
                        }

                        AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                    }
                    break;
                case parseBlockComment:
                    {
                        if (index + 1 >= json.Length)
                        {
                            errorCode = ParseException.ErrorCode.UnterminatedBlockComment;
                            goto parseError;
                        }

                        c = json[++index];
                        var offset = index;

                        while (index + 1 < json.Length && !(c == '*' && json[index + 1] == '/'))
                        {
                            c = json[++index];
                        }

                        if (index + 1 >= json.Length || c != '*' || json[index + 1] != '/')
                        {
                            errorCode = ParseException.ErrorCode.UnterminatedBlockComment;
                            goto parseError;
                        }

                        AllocToken(DToken.Make(DTokenVariant.TriviaBlockComment,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                        index += 2;

                        if (index >= json.Length)
                        {
                            goto parseEnd;
                        }
                    }
                    break;
                default:
                    if (GetToken(parentId).Type == DTokenType.Map)
                    {
                        goto case parsePropertyName;
                    }

                    if (c == 't')
                    {
                        if (json.Slice(index + 1).StartsWith("rue"u8))
                        {
                            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanTrue,
                                DToken.EncodeLengthOffsetPayload(0, index)));
                            index += 4;
                            goto case assignValue;
                        }

                        errorCode = ParseException.ErrorCode.InvalidBooleanValue;
                        goto parseError;
                    }

                    if (c == 'f')
                    {
                        if (json.Slice(index + 1).StartsWith("alse"u8))
                        {
                            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanFalse,
                                DToken.EncodeLengthOffsetPayload(0, index)));
                            index += 5;
                            goto case assignValue;
                        }

                        errorCode = ParseException.ErrorCode.InvalidBooleanValue;
                        goto parseError;
                    }

                    if (c == 'n')
                    {
                        if (json.Slice(index + 1).StartsWith("ull"u8))
                        {
                            tokenId = AllocToken(
                                DToken.Make(DTokenVariant.Null, DToken.EncodeLengthOffsetPayload(0, index)));
                            index += 4;
                            goto case assignValue;
                        }

                        errorCode = ParseException.ErrorCode.InvalidNullValue;
                        goto parseError;
                    }

                    goto case parseNumber;
            }
        }

        parseTrailingTrivia:
        for (;;)
        {
            if (index >= json.Length)
            {
                goto parseEnd;
            }

            var c = json[index];
            var ws = index;

            while (c <= 0x20)
            {
                if (((whiteSpace >> c) & 1) == 0)
                {
                    errorCode = ParseException.ErrorCode.InvalidStartOfValue;
                    goto parseError;
                }

                if (++index >= json.Length)
                {
                    if (trivia && ws != index)
                    {
                        AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                            DToken.EncodeLengthOffsetPayload(index - ws, ws)));
                    }

                    goto parseEnd;
                }

                c = json[index];
            }

            if (trivia && ws != index)
            {
                AllocToken(
                    DToken.Make(DTokenVariant.TriviaWhitespace, DToken.EncodeLengthOffsetPayload(index - ws, ws)));
            }

            switch ((char)c)
            {
                case '/':
                    if (++index < json.Length)
                    {
                        c = json[index];
                        if (c == '/')
                        {
                            goto parseTrailingLineComment;
                        }

                        if (c == '*')
                        {
                            goto parseTrailingBlockComment;
                        }
                    }

                    errorCode = ParseException.ErrorCode.InvalidComment;
                    goto parseError;
                case (char)0xc2:
                case (char)0xe1:
                case (char)0xe2:
                case (char)0xe3:
                case (char)0xef:
                    var len = GetUnicodeWhiteSpaceLength(json.Slice(index));

                    if (len > 0)
                    {
                        if (trivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                                DToken.EncodeLengthOffsetPayload(len, index)));
                        }

                        index += len;
                        continue;
                    }

                    errorCode = ParseException.ErrorCode.AdditionalTextEncountered;
                    goto parseError;
                default:
                    errorCode = ParseException.ErrorCode.AdditionalTextEncountered;
                    goto parseError;
            }

            parseTrailingLineComment:
            if (index + 1 >= json.Length)
            {
                AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                    DToken.EncodeLengthOffsetPayload(0, json.Length)));

                goto parseEnd;
            }

            c = json[++index];

            var offset = index;

            for (;;)
            {
                if (c == '\n' || c == '\r')
                {
                    break;
                }

                if (index + 1 >= json.Length)
                {
                    AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                        DToken.EncodeLengthOffsetPayload(index + 1 - offset, offset)));

                    goto parseEnd;
                }

                c = json[++index];
            }

            AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                DToken.EncodeLengthOffsetPayload(index - offset, offset)));
            continue;

            parseTrailingBlockComment:
            if (index + 1 >= json.Length)
            {
                errorCode = ParseException.ErrorCode.UnterminatedBlockComment;
                goto parseError;
            }

            c = json[++index];
            offset = index;

            while (index + 1 < json.Length && !(c == '*' && json[index + 1] == '/'))
            {
                c = json[++index];
            }

            if (index + 1 >= json.Length || c != '*' || json[index + 1] != '/')
            {
                errorCode = ParseException.ErrorCode.UnterminatedBlockComment;
                goto parseError;
            }

            AllocToken(DToken.Make(DTokenVariant.TriviaBlockComment,
                DToken.EncodeLengthOffsetPayload(index - offset, offset)));
            index += 2;
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

        parseEnd:
        if (tokenId == 0)
        {
            errorCode = ParseException.ErrorCode.NoJsonTokenExists;
            goto parseError;
        }

        if (parentId != 0)
        {
            var parentToken = GetToken(parentId);
            errorCode = parentToken.Type switch
            {
                DTokenType.Array => ParseException.ErrorCode.UnterminatedArray,
                DTokenType.Map => ParseException.ErrorCode.UnterminatedObject,
                DTokenType.Text => ParseException.ErrorCode.InvalidAfterPropertyName,
                _ => ParseException.ErrorCode.InvalidEndOfValue
            };
            goto parseError;
        }

        rootId = tokenId;
        return null;
    }

    private static int GetUnicodeWhiteSpaceLength(ReadOnlySpan<byte> span)
    {
        if (span.Length < 2)
        {
            return 0;
        }

        var b0 = span[0];
        var b1 = span[1];

        switch (b0)
        {
            case 0xc2:
                // U+00A0 (NBSP): C2 A0
                return b1 == 0xA0 ? 2 : 0;

            case 0xe2:
                if (span.Length < 3)
                {
                    return 0;
                }

                var b2 = span[2];

                if (b1 == 0x80)
                {
                    // U+2000-200A (En Quad ~ Hair Space): E2 80 80~8A
                    if (b2 >= 0x80 && b2 <= 0x8a)
                    {
                        return 3;
                    }

                    // U+2028 (LS), U+2029 (PS), U+202F (NNBSP)
                    if (b2 == 0xa8 || b2 == 0xa9 || b2 == 0xaf)
                    {
                        return 3;
                    }
                }
                else if (b1 == 0x81)
                {
                    // U+205F (Medium Mathematical Space): E2 81 9F
                    if (b2 == 0x9f)
                    {
                        return 3;
                    }
                }

                return 0;

            case 0xe3:
                if (span.Length < 3)
                {
                    return 0;
                }

                // U+3000 : E3 80 80
                if (b1 == 0x80 && span[2] == 0x80)
                {
                    return 3;
                }

                return 0;

            case 0xef:
                if (span.Length < 3)
                {
                    return 0;
                }

                // U+FEFF (BOM): EF BB BF
                if (b1 == 0xbb && span[2] == 0xbf)
                {
                    return 3;
                }

                return 0;

            case 0xe1:
                if (span.Length < 3)
                {
                    return 0;
                }

                // U+1680 (Ogham Space Mark): E1 9A 80
                if (b1 == 0x9a && span[2] == 0x80)
                {
                    return 3;
                }

                return 0;

            default:
                return 0;
        }
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, Json5WriteOptions options)
    {
        var json5Document = reader.Document as Json5Document;

        var state = new SeparatorState(stackalloc ulong[8], options.MaxDepth);

        state.WriteIndented = options.WriteIndented;

        var encoder = writer.Settings.TextEncoderPolicy.GetEncoder<JsonTextEncoder>();

        WriteJson5(writer, reader, encoder, json5Document, reader.RootId, ref options, ref state);

        if (options.PreserveTrivia)
        {
            if (json5Document != null && json5Document.RootElement.Equals(reader.RootElement))
            {
                WriteTriviaList(writer, in reader, json5Document, json5Document._tailId, ref state);
            }
            else
            {
                WriteTriviaList(writer, in reader, null, 0, ref state);
            }
        }
    }

    private static bool TryParseHex(ReadOnlySpan<byte> bytes, out int result)
    {
        result = 0;
        foreach (var b in bytes)
        {
            var val = 0;
            if (b >= '0' && b <= '9')
            {
                val = b - '0';
            }
            else if (b >= 'a' && b <= 'f')
            {
                val = b - 'a' + 10;
            }
            else if (b >= 'A' && b <= 'F')
            {
                val = b - 'A' + 10;
            }
            else
            {
                return false;
            }

            result = (result << 4) | val;
        }

        return true;
    }

    private static bool IsEcmaScriptIdentity(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty)
        {
            return false;
        }

        var i = 0;
        var isFirstChar = true;

        while (i < name.Length)
        {
            if (Rune.DecodeFromUtf8(name.Slice(i), out var rune, out var bytesConsumed) != OperationStatus.Done)
            {
                return false;
            }

            if (rune.Value == '\\')
            {
                i += bytesConsumed;

                if (i >= name.Length || name[i] != 'u')
                {
                    return false;
                }

                i++;

                if (i + 4 > name.Length)
                {
                    return false;
                }

                if (!TryParseHex(name.Slice(i, 4), out var hexValue))
                {
                    return false;
                }

                if (!Rune.TryCreate(hexValue, out rune))
                {
                    return false;
                }

                i += 4;
            }
            else
            {
                i += bytesConsumed;
            }

            if (rune.Value == '$' || rune.Value == '_')
            {
                isFirstChar = false;
                continue;
            }

            if (rune.Value > 0xffff)
            {
                return false;
            }

            if (rune.Value == '\u200c' || rune.Value == '\u200d')
            {
                if (isFirstChar)
                {
                    return false;
                }

                continue;
            }

            var cat = Rune.GetUnicodeCategory(rune);

            switch (cat)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.LetterNumber:
                    isFirstChar = false;
                    continue;
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.ConnectorPunctuation:
                    if (isFirstChar)
                    {
                        return false;
                    }

                    continue;

                default:
                    return false;
            }
        }

        return true;
    }

    private static void WriteJson5String(Utf8TextWriter writer, ReadOnlySpan<byte> utf8Bytes,
        JsonTextEncoder encoder, char quatChar = (char)0)
    {
        var buf = writer.BeginWriteUtf8Bytes(utf8Bytes.Length * 6 + 2);
        var pt = 0;
        var len = utf8Bytes.Length;

        pt += encoder.Encode(buf.Slice(pt), utf8Bytes, quatChar);

        writer.EndWriteUtf8Bytes(pt);
    }

    private static void WriteTrivia(Utf8TextWriter writer, in DataReader reader, uint triviaId,
        ref SeparatorState state)
    {
        var utf8Bytes = reader.ReadTrivia(triviaId);

        var kind = reader.GetToken(triviaId).TriviaKind;

        if (kind == TriviaKind.LineComment || kind == TriviaKind.Default)
        {
            writer.WriteString("//"u8);
        }

        if (kind == TriviaKind.BlockComment)
        {
            writer.WriteString("/*"u8);
        }

        writer.WriteString(utf8Bytes);

        if (kind == TriviaKind.BlockComment)
        {
            writer.WriteString("*/"u8);
        }
    }

    private static char GetQuoteChar(DTokenVariant kind, Json5QuoteStyle style)
    {
        var qc = '"';

        switch (style)
        {
            case Json5QuoteStyle.AlwaysDouble:
            case Json5QuoteStyle.AutoOrDouble:
                qc = '"';
                break;
            case Json5QuoteStyle.AlwaysSingle:
            case Json5QuoteStyle.AutoOrSingle:
                qc = '\'';
                break;
            case Json5QuoteStyle.PreserveOrDouble:
                if (kind == DTokenVariant.StringSingleQuote ||
                    kind == DTokenVariant.StringMultilineSingleQuote)
                {
                    qc = '\'';
                }
                else
                {
                    qc = '"';
                }

                break;
            case Json5QuoteStyle.PreserveOrSingle:
                if (kind == DTokenVariant.StringDoubleQuote ||
                    kind == DTokenVariant.StringMultilineDoubleQuote)
                {
                    qc = '"';
                }
                else
                {
                    qc = '\'';
                }

                break;
        }

        return qc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringValue(Utf8TextWriter writer, ReadOnlySpan<byte> utf8Bytes, char quoteChar)
    {
        var bytes = writer.AllocateUtf8Bytes(utf8Bytes.Length + 2);
        bytes[0] = (byte)quoteChar;
        utf8Bytes.CopyTo(bytes.Slice(1));
        bytes[utf8Bytes.Length + 1] = (byte)quoteChar;
    }

    private static void WriteTriviaList(Utf8TextWriter writer, in DataReader reader, Json5Document? document,
        uint tokenId, ref SeparatorState state)
    {
        if (document == null)
        {
            foreach (var triviaId in reader.EnumerateTrivia(tokenId))
            {
                var kind = reader.GetToken(triviaId).TriviaKind;

                if (kind == TriviaKind.LineComment ||
                    kind == TriviaKind.BlockComment ||
                    kind == TriviaKind.Default)
                {
                    WriteTrivia(writer, in reader, triviaId, ref state);
                    if (state.WriteIndented)
                    {
                        writer.WriteIndentNewLine();
                    }
                    else
                    {
                        writer.WriteNewLine();
                    }
                }
            }
        }
        else
        {
            using var skipSeparators = new Helper.LocalList<ulong>(stackalloc ulong[1]);
            var separatorIndex = 0;
            var pendingCommaIndex = -1;
            var excessClosers = 0;

            if (state.HasEndToken)
            {
                var closerCount = 0;

                foreach (var triviaId in reader.EnumerateTrivia(tokenId))
                {
                    var trivia = reader.GetToken(triviaId);

                    if (!trivia.IsExtended && trivia.TriviaKind == TriviaKind.Separator)
                    {
                        var separator = reader.ReadTrivia(triviaId);

                        if (separator.SequenceEqual("}"u8) || separator.SequenceEqual("]"u8))
                        {
                            closerCount++;
                        }
                    }
                }

                if (state.EndTokenCount > closerCount)
                {
                    while (state.EndTokenCount > closerCount)
                    {
                        writer.WriteIndentNewLine();
                        writer.WriteUtf8Byte(state.DequeueEndToken());
                    }

                    state.HeadEndTokenEmpty = false;
                }

                excessClosers = closerCount - state.EndTokenCount;
            }

            var valueSeparatorAllowed = state.HasEndToken ? !state.HeadEndTokenEmpty : state.NeedValueSeparator;

            foreach (var triviaId in reader.EnumerateTrivia(tokenId))
            {
                var trivia = reader.GetToken(triviaId);

                if (trivia.IsExtended)
                {
                    continue;
                }

                var kind = reader.GetToken(triviaId).TriviaKind;

                if (kind == TriviaKind.Separator)
                {
                    var separator = reader.ReadTrivia(triviaId);
                    var skip = false;

                    if (separator.SequenceEqual("}"u8) || separator.SequenceEqual("]"u8))
                    {
                        if (excessClosers > 0)
                        {
                            excessClosers--;
                            skip = true;
                        }
                        else if (state.RemoveEndToken(separator[0]))
                        {
                            valueSeparatorAllowed = true;
                            pendingCommaIndex = -1;
                        }
                        else
                        {
                            skip = true;
                        }
                    }

                    if (separator.SequenceEqual(","u8))
                    {
                        if (valueSeparatorAllowed)
                        {
                            valueSeparatorAllowed = false;

                            if (!state.HasEndToken)
                            {
                                state.NeedValueSeparator = false;
                            }
                            else
                            {
                                pendingCommaIndex = separatorIndex;
                            }
                        }
                        else
                        {
                            skip = true;
                        }
                    }

                    if (skip)
                    {
                        skipSeparators.EnsureCount((separatorIndex >> 6) + 1);
                        skipSeparators[separatorIndex >> 6] |= 1UL << (separatorIndex & 63);
                    }

                    separatorIndex++;

                    if (separator.SequenceEqual(":"u8))
                    {
                        state.NeedKeySeparator = false;
                    }
                }
            }

            if (pendingCommaIndex >= 0 && state.HasEndToken)
            {
                if (state.NeedValueSeparator)
                {
                    state.NeedValueSeparator = false;
                }
                else
                {
                    skipSeparators.EnsureCount((pendingCommaIndex >> 6) + 1);
                    skipSeparators[pendingCommaIndex >> 6] |= 1UL << (pendingCommaIndex & 63);
                }
            }

            if (state.NeedKeySeparator)
            {
                state.NeedKeySeparator = false;

                writer.WriteUtf8Byte((byte)':');
                writer.WriteSpaceIfIndented();
            }

            while (state.HasEndToken)
            {
                writer.WriteIndentNewLine();
                writer.WriteUtf8Byte(state.DequeueEndToken());
            }

            if (state.NeedValueSeparator)
            {
                state.NeedValueSeparator = false;

                writer.WriteUtf8Byte((byte)',');
            }

            separatorIndex = 0;

            foreach (var triviaId in reader.EnumerateTrivia(tokenId))
            {
                var trivia = reader.GetToken(triviaId);

                if (trivia.IsExtended)
                {
                    var kind = trivia.TriviaKind;

                    if (kind == TriviaKind.LineComment ||
                        kind == TriviaKind.BlockComment ||
                        kind == TriviaKind.Default)
                    {
                        WriteTrivia(writer, reader, triviaId, ref state);
                        writer.WriteNewLine();
                    }
                }
                else
                {
                    if (trivia.TriviaKind == TriviaKind.Separator)
                    {
                        var skip = (separatorIndex >> 6) < skipSeparators.Count &&
                                   (skipSeparators[separatorIndex >> 6] & (1UL << (separatorIndex & 63))) != 0;

                        separatorIndex++;

                        if (skip)
                        {
                            continue;
                        }
                    }

                    WriteTrivia(writer, reader, triviaId, ref state);
                }
            }
        }
    }

    private static void WriteJson5(Utf8TextWriter writer, in DataReader reader, JsonTextEncoder encoder,
        Json5Document? document, uint tokenId,
        ref Json5WriteOptions options, ref SeparatorState state)
    {
        var latestId = tokenId;
        var token = reader.GetToken(tokenId);
        var preserveSyntax = options.PreserveTrivia && document != null;

        if (options.PreserveTrivia)
        {
            WriteTriviaList(writer, in reader, document, tokenId, ref state);
        }

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                var values = reader.EnumerateArray(tokenId);
                var separator = false;

                writer.WriteUtf8Byte((byte)'[');
                if (reader.GetValueCount(tokenId) > 0)
                {
                    writer.PushIndent();
                    writer.WriteIndentNewLine();
                    foreach (var valueId in values)
                    {
                        if (separator)
                        {
                            if (preserveSyntax)
                            {
                                state.NeedValueSeparator = true;
                            }
                            else
                            {
                                writer.WriteUtf8Byte((byte)',');
                                writer.WriteIndentNewLine();
                            }
                        }

                        WriteJson5(writer, reader, encoder, document, valueId, ref options, ref state);
                        separator = true;
                    }

                    writer.PopIndent();
                    writer.WriteIndentNewLine();
                }

                if (preserveSyntax)
                {
                    state.EnqueueEndToken((byte)']', reader.GetValueCount(tokenId) == 0);
                }
                else
                {
                    writer.WriteUtf8Byte((byte)']');
                }
            }
            else
            {
                writer.WriteUtf8Byte((byte)'{');

                if (reader.GetValueCount(tokenId) > 0)
                {
                    writer.PushIndent();
                    writer.WriteIndentNewLine();

                    var pairs = reader.EnumerateMap(tokenId);
                    var separator = false;

                    foreach (var pair in pairs)
                    {
                        if (separator)
                        {
                            if (preserveSyntax)
                            {
                                state.NeedValueSeparator = true;
                            }
                            else
                            {
                                writer.WriteUtf8Byte((byte)',');
                                writer.WriteIndentNewLine();
                            }
                        }

                        if (options.PreserveTrivia)
                        {
                            WriteTriviaList(writer, in reader, document, pair.Key, ref state);
                        }

                        var keyKind = reader.GetToken(pair.Key).Variant;

                        var qc = (char)0;

                        switch (options.PropertyNameStyle)
                        {
                            case Json5QuoteStyle.AlwaysDouble:
                                qc = '"';
                                break;
                            case Json5QuoteStyle.AlwaysSingle:
                                qc = '\'';
                                break;
                            case Json5QuoteStyle.PreserveOrDouble:
                            case Json5QuoteStyle.PreserveOrSingle:
                                if (keyKind == DTokenVariant.StringDoubleQuote ||
                                    keyKind == DTokenVariant.StringMultilineDoubleQuote)
                                {
                                    qc = '"';
                                }

                                if (keyKind == DTokenVariant.StringSingleQuote ||
                                    keyKind == DTokenVariant.StringMultilineSingleQuote)
                                {
                                    qc = '\'';
                                }

                                break;
                        }

                        if (document != null && document.GetRawData(pair.Key, out var rawData) &&
                            (options.PropertyNameStyle == Json5QuoteStyle.PreserveOrDouble ||
                             options.PropertyNameStyle == Json5QuoteStyle.PreserveOrSingle))
                        {
                            if (qc == 0)
                            {
                                writer.WriteString(rawData);
                            }
                            else
                            {
                                WriteStringValue(writer, rawData, qc);
                            }
                        }
                        else
                        {
                            var name = reader.ReadUtf8String(pair.Key);

                            if (qc == 0 && !IsEcmaScriptIdentity(name))
                            {
                                qc = ((int)options.PropertyNameStyle & 1) == 0 ? '"' : '\'';
                            }

                            WriteJson5String(writer, name, encoder, qc);
                        }

                        if (preserveSyntax)
                        {
                            state.NeedKeySeparator = true;
                        }
                        else
                        {
                            writer.WriteUtf8Byte((byte)':');
                            writer.WriteSpaceIfIndented();
                        }

                        WriteJson5(writer, reader, encoder, document, pair.Value, ref options, ref state);
                        separator = true;
                    }

                    writer.PopIndent();
                    writer.WriteIndentNewLine();
                }

                if (preserveSyntax)
                {
                    state.EnqueueEndToken((byte)'}', reader.GetValueCount(tokenId) == 0);
                }
                else
                {
                    writer.WriteUtf8Byte((byte)'}');
                }
            }
        }
        else
        {
            switch (token.Kind)
            {
                case DTokenKind.Null:
                    writer.WriteNull();
                    break;
                case DTokenKind.Boolean:
                    writer.WriteBoolean(reader.ReadBoolean(tokenId));
                    break;
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    {
                        var qc = GetQuoteChar(token.Variant, options.StringStyle);

                        if (document != null && document.GetRawData(tokenId, out var rawData) &&
                            (options.StringStyle == Json5QuoteStyle.PreserveOrDouble ||
                             options.StringStyle == Json5QuoteStyle.PreserveOrSingle))
                        {
                            WriteStringValue(writer, rawData, qc);
                        }
                        else
                        {
                            WriteJson5String(writer, reader.ReadUtf8String(tokenId),
                                encoder, qc);
                        }
                    }
                    break;
                case DTokenKind.Integer:
                    {
                        if (document != null && document.GetRawData(tokenId, out var rawData))
                        {
                            writer.WriteString(rawData);
                        }
                        else
                        {
                            var integerKind = token.IntegerKind;

                            if (integerKind == IntegerKind.Hexadecimal)
                            {
                                var buf = writer.BeginWriteUtf8Bytes(64);
                                Utf8Helper.TryFormatHexadecimal(reader.ReadInt64(tokenId), buf,
                                    out var bytesWritten);
                                writer.EndWriteUtf8Bytes(bytesWritten);
                            }
                            else
                            {
                                if (integerKind == IntegerKind.Unsigned)
                                {
                                    writer.WriteUInt64(reader.ReadUInt64(tokenId));
                                }
                                else
                                {
                                    writer.WriteInt64(reader.ReadInt64(tokenId));
                                }
                            }
                        }
                    }
                    break;
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
                case DTokenKind.Float:
                    {
                        if (document != null && document.GetRawData(tokenId, out var rawData))
                        {
                            writer.WriteString(rawData);
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
                    }
                    break;
                case DTokenKind.BigNumber:
                    {
                        if (document != null && document.GetRawData(tokenId, out var rawData))
                        {
                            writer.WriteString(rawData);
                        }
                        else
                        {
                            writer.WriteString(reader.ReadBigNumber(tokenId));
                        }
                    }
                    break;
                case DTokenKind.ByteString:
                    {
                        var qc = GetQuoteChar(token.Variant, options.StringStyle);

                        writer.WriteUtf8Byte((byte)qc);
                        writer.WriteByteString(reader.ReadByteString(tokenId), token.ByteStringKind);
                        writer.WriteUtf8Byte((byte)qc);
                    }
                    break;
                case DTokenKind.Timestamp:
                    {
                        var qc = GetQuoteChar(token.Variant, options.StringStyle);

                        if (document != null && document.GetRawData(tokenId, out var rawData))
                        {
                            WriteStringValue(writer, rawData, qc);
                        }
                        else
                        {
                            Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
                            var success = Utf8Helper.TryFormatTimestamp(reader, tokenId, buffer, out var bytesWritten);
                            Debug.Assert(success);
                            // Escape the formatted value using the selected JSON5 quote style.
                            WriteJson5String(writer, buffer.Slice(0, bytesWritten), encoder, qc);
                        }
                    }
                    break;
            }
        }
    }

    protected override long DecodeInteger(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        if (token.Variant == DTokenVariant.IntegerHexadecimal)
        {
            if (Utf8Helper.TryParseHexadecimal(span, out var value))
            {
                return value;
            }
        }
        else
        {
            if (Utf8Parser.TryParse(span, out long value, out var bytes) && bytes == param.length)
            {
                return value;
            }
        }

        throw new ParseException(this, ParseException.ErrorCode.InvalidNumberValue, param.offset, param.length);
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var src = _source.Slice(param.offset, param.length);
        var kind = token.Variant;

        if (kind == DTokenVariant.StringMultilineDoubleQuote ||
            kind == DTokenVariant.StringMultilineSingleQuote ||
            kind == DTokenVariant.StringLiteral)
        {
            return DecodeEscapedJson5String(src.Span);
        }

        return src.Span;
    }


    private ReadOnlySpan<byte> DecodeEscapedJson5String(ReadOnlySpan<byte> src)
    {
        var dst = new byte[src.Length];

        var i = 0;
        var written = 0;
        var highSurrogate = 0;

        while (i < src.Length)
        {
            var c = src[i++];

            if (c != '\\')
            {
                if (written >= dst.Length)
                {
                    throw new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence, i);
                }

                dst[written++] = c;
                continue;
            }

            if (i >= src.Length)
            {
                throw new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence, i);
            }

            c = src[i++];

            switch (c)
            {
                case (byte)'"': dst[written++] = (byte)'"'; break;
                case (byte)'\\': dst[written++] = (byte)'\\'; break;
                case (byte)'b': dst[written++] = (byte)'\b'; break;
                case (byte)'f': dst[written++] = (byte)'\f'; break;
                case (byte)'n': dst[written++] = (byte)'\n'; break;
                case (byte)'r': dst[written++] = (byte)'\r'; break;
                case (byte)'t': dst[written++] = (byte)'\t'; break;
                case (byte)'v': dst[written++] = (byte)'\v'; break;
                case (byte)'0':
                    dst[written++] = (byte)'\0';
                    break;

                case (byte)'u':
                case (byte)'x':
                    {
                        var len = c == 'u' ? 4 : 2;
                        if (!TryParseHex(src.Slice(i, len), out var codePoint))
                        {
                            throw new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence, i - 1, len + 1);
                        }

                        i += len;

                        if (codePoint >= 0xd800 && codePoint <= 0xdbff)
                        {
                            highSurrogate = codePoint;
                            continue;
                        }

                        if (codePoint >= 0xdc00 && codePoint <= 0xdfff)
                        {
                            if (highSurrogate != 0)
                            {
                                codePoint = 0x10000 + (highSurrogate - 0xd800) * 0x400 + (codePoint - 0xdc00);
                                highSurrogate = 0;
                            }
                            else
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence, i - 1,
                                    len + 1);
                            }
                        }

                        if (Rune.TryCreate(codePoint, out var rune))
                        {
                            written += rune.EncodeToUtf8(dst.AsSpan().Slice(written));
                        }
                        else
                        {
                            throw new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence, i - 1, len + 1);
                        }
                    }
                    break;
                default:
                    if (!IsLineTerminator(c, src, ref i))
                    {
                        dst[written++] = c;
                    }

                    break;
            }
        }

        return dst.AsSpan().Slice(0, written);
    }

    private static bool IsLineTerminator(byte c, ReadOnlySpan<byte> src, ref int i)
    {
        if (c == '\n')
        {
            return true;
        }

        if (c == '\r')
        {
            if (i < src.Length && src[i] == '\n')
            {
                i++;
            }

            return true;
        }

        if (c == 0xe2)
        {
            if (i + 1 < src.Length && src[i] == 0x80 && (src[i + 1] == 0xa8 || src[i + 1] == 0xa9))
            {
                i += 2;
                return true;
            }
        }

        return false;
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, param.length);
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        if (Utf8Parser.TryParse(_source.Span.Slice(param.offset, param.length), out double value, out var bytes) &&
            bytes == param.length)
        {
            return value;
        }

        throw new ParseException(this, ParseException.ErrorCode.InvalidNumberValue, param.offset, param.length);
    }

    protected override ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        if (token.Variant == DTokenVariant.BigNumberHexadecimal)
        {
            var offset = 0;
            var minus = false;
            if (span[0] == '-')
            {
                offset++;
                minus = true;
            }
            else if (span[0] == '+')
            {
                offset++;
            }

            return Utf8Helper.ConvertHexToDecimal(span.Slice(offset + 2), minus);
        }

        return span;
    }

    private bool GetRawData(uint tokenId, out ReadOnlySpan<byte> data)
    {
        var token = GetToken(tokenId);

        if (token.IsExtended)
        {
            data = null;
            return false;
        }

        var param = DToken.DecodeLengthOffsetPayload(token);

        data = _source.Slice(param.offset, param.length).Span;

        return true;
    }

    private ref struct SeparatorState
    {
        public SeparatorState(Span<ulong> initialBuffer, int maxDepth)
        {
            if (maxDepth == 0)
            {
                maxDepth = SerializerSettings.DefaultMaxDepth;
            }

            if (initialBuffer.IsEmpty)
            {
                throw new ArgumentException(
                    "Initial buffer must not be empty.",
                    nameof(initialBuffer));
            }

            var requiredWords = (maxDepth + 63) >> 6;
            var initialWords = Math.Min(initialBuffer.Length, requiredWords);

            _endTokenBits = initialBuffer.Slice(0, initialWords);
            _endTokenBits.Clear();

            _maxDepth = maxDepth;
        }

        public bool HasEndToken => _endTokenCount != 0;

        public readonly int EndTokenCount => _endTokenCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueEndToken(byte token, bool isEmpty)
        {
            if (_endTokenCount == 0)
            {
                HeadEndTokenEmpty = isEmpty;
            }

            if (_endTokenCount >= _maxDepth)
            {
                throw new InvalidOperationException(
                    "MaxDepth exceeded while writing JSON5.");
            }

            if (_endTokenCount == Capacity)
            {
                ResizeEndTokenBuffer();
            }

            var index = _endTokenHead + _endTokenCount;

            if (index >= Capacity)
            {
                index -= Capacity;
            }

            SetEndToken(index, token);
            _endTokenCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte DequeueEndToken()
        {
            var token = GetEndToken(_endTokenHead);

            _endTokenHead++;

            if (_endTokenHead == Capacity)
            {
                _endTokenHead = 0;
            }

            _endTokenCount--;

            if (_endTokenCount == 0)
            {
                _endTokenHead = 0;
            }

            return token;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RemoveEndToken(byte token)
        {
            if (_endTokenCount == 0)
            {
                return false;
            }

            if (GetEndToken(_endTokenHead) != token)
            {
                return false;
            }

            HeadEndTokenEmpty = false;

            _endTokenHead++;

            if (_endTokenHead == Capacity)
            {
                _endTokenHead = 0;
            }

            _endTokenCount--;

            if (_endTokenCount == 0)
            {
                _endTokenHead = 0;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly byte GetEndToken(int index)
        {
            var word = index >> 6;
            var bit = index & 63;

            return ((_endTokenBits[word] >> bit) & 1) != 0
                ? (byte)'}'
                : (byte)']';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetEndToken(int index, byte token)
        {
            var word = index >> 6;
            var bit = index & 63;
            var mask = 1UL << bit;

            switch (token)
            {
                case (byte)'}':
                    _endTokenBits[word] |= mask;
                    break;

                case (byte)']':
                    _endTokenBits[word] &= ~mask;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(token));
            }
        }

        private void ResizeEndTokenBuffer()
        {
            var maxWords = (_maxDepth + 63) >> 6;
            var newWordCount = Math.Min(
                _endTokenBits.Length * 2,
                maxWords);

            var buffer = new ulong[newWordCount];

            // Ring buffer -> linear bit sequence.
            for (var i = 0; i < _endTokenCount; i++)
            {
                var sourceIndex = _endTokenHead + i;

                if (sourceIndex >= Capacity)
                {
                    sourceIndex -= Capacity;
                }

                if (GetEndToken(sourceIndex) == (byte)'}')
                {
                    buffer[i >> 6] |= 1UL << (i & 63);
                }
            }

            _heapEndTokenBits = buffer;
            _endTokenBits = buffer;
            _endTokenHead = 0;
        }

        private readonly int Capacity => _endTokenBits.Length << 6;

        private Span<ulong> _endTokenBits;
        private ulong[]? _heapEndTokenBits;

        private int _endTokenHead;
        private int _endTokenCount;
        private readonly int _maxDepth;

        public bool NeedValueSeparator;
        public bool HeadEndTokenEmpty;
        public bool NeedKeySeparator;
        public bool WriteIndented;
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            NoJsonTokenExists,
            InvalidNullValue,
            InvalidBooleanValue,
            InvalidNumberValue,
            UnescapedStringValue,
            InvalidEscapeSequence,
            InvalidSurrogatePair,
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
            InvalidComment,
            UnterminatedBlockComment,
            IllegalUnquotedKey,
            MaxDepthExceeded
        }

        public ParseException(Json5Document doc, ErrorCode error, int offset, int length = -1)
            : this(doc, error, offset, length, GetLinePosition(doc._source.Span, offset))
        {
        }

        private ParseException(Json5Document doc, ErrorCode error, int offset, int length,
            (long LineNumber, long BytePositionInLine) position)
            : base(nameof(Json5Document), offset, CreateMessage(doc, error, offset, length, position), position.LineNumber,
                position.BytePositionInLine)
        {
        }

        private static string CreateMessage(Json5Document doc, ErrorCode error, int offset, int length,
            (long LineNumber, long BytePositionInLine) position)
        {
            var sb = new StringBuilder();

            sb.Append($"{nameof(Json5Document)} Error: {error}");
            AppendErrorValue(sb, doc._source.Span, offset, length);
            sb.Append($", LineNumber: {position.LineNumber}, BytePositionInLine: {position.BytePositionInLine}");

            return sb.ToString();
        }
    }
}