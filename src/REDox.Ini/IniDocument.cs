// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using REDox.Serialization;

namespace REDox.Ini;

public sealed class IniDocument : Document
{
    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;
    private uint _tailId;

    private IniDocument(SerializerSettings settings) : base(settings)
    {
    }

    public IniDocument Duplicate()
    {
        var doc = CreateSnapshot<IniDocument>();

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

    public static IniDocument Parse(Stream stream, SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = Helper.ReadStream(stream, out var len, (settings ?? SerializerSettings.Default).DefaultBufferSize);

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

    public static IniDocument Parse(string ini, SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(ini.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(ini, buffer.AsSpan());

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
    ///     The returned document takes ownership of <paramref name="ini" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static IniDocument Parse(byte[] ini, SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        return Parse(ini.AsMemory(), settings, options);
    }

    public static IniDocument Parse(ReadOnlySpan<byte> ini, SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ini.Length);

        try
        {
            ini.CopyTo(buffer);

            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, ini.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="ini" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static IniDocument Parse(ReadOnlyMemory<byte> ini, SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new IniDocument(settings);

        try
        {
            doc.Read(ini, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static IniDocument ParseRented(ReadOnlyMemory<byte> ini, byte[] rentedBuffer,
        SerializerSettings? settings = null, IniDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new IniDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(ini, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> ini, [NotNullWhen(true)] out IniDocument? document,
        SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        var buffer = ArrayPool<byte>.Shared.Rent(ini.Length);

        try
        {
            ini.CopyTo(buffer);

            ReadOnlyMemory<byte> source = buffer.AsMemory(0, ini.Length);
            document = new IniDocument(settings)
            {
                _rentedBuffer = buffer
            };
            buffer = null;

            Utf8Helper.SkipUtf8Bom(ref source);

            document._source = source;
            document.EnsureCapacity(source.Length / 16);

            var parseException = document.ParseIni(source.Span, options);

            if (parseException != null)
            {
                document.Dispose();
                document = null;
                return false;
            }

            document.RootId = 1;
            document._tailId = document.AllocToken(default);
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

    public static string EncodeToString(DElement element,
        IniWriteOptions options = default)
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

    public static void EncodeTo(DElement element, Stream stream,
        IniWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            try
            {
                writer.Reset(stream, reader.Settings, options.TextWriteOptions);
                Write(writer, reader, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        IniWriteOptions options = default)
    {
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

    public static byte[] Encode(DElement element, IniWriteOptions options = default)
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

    private static void Write(Utf8TextWriter writer, in DataReader reader, IniWriteOptions options)
    {
        var iniDocument = reader.Document as IniDocument;
        var state = new SeparatorState();

        WriteIni(writer, in reader, iniDocument, reader.RootId, ref options, ref state);

        if (options.PreserveTrivia && iniDocument != null && iniDocument.RootElement.Equals(reader.RootElement))
        {
            WriteTriviaList(writer, in reader, iniDocument, iniDocument._tailId, ref state);
        }
    }

    private void Read(ReadOnlyMemory<byte> bytes, IniDocumentOptions options)
    {
        Utf8Helper.SkipUtf8Bom(ref bytes);

        _source = bytes;

        EnsureCapacity(bytes.Length / 16);

        var parseException = ParseIni(bytes.Span, options);

        if (parseException != null)
        {
            throw parseException;
        }

        RootId = 1;
        _tailId = AllocToken(default);
    }

    private static int TrimEnd(ReadOnlySpan<byte> bytes, int start, int end)
    {
        while (end > start && bytes[end - 1] <= 0x20)
        {
            end--;
        }

        return end;
    }

    private static int TrimStart(ReadOnlySpan<byte> bytes, int start, int end)
    {
        while (start < end && bytes[start] <= 0x20)
        {
            start++;
        }

        return start;
    }

    private static bool IsLineEnding(byte value)
    {
        return value is (byte)'\r' or (byte)'\n';
    }

    private static int FindLineEnd(ReadOnlySpan<byte> bytes, int index)
    {
        while (index < bytes.Length && !IsLineEnding(bytes[index]))
        {
            index++;
        }

        return index;
    }

    private static int FindValueEnd(ReadOnlySpan<byte> bytes, int start, bool allowInlineComments)
    {
        var index = start;
        var possibleCommentOffset = -1;

        while (index < bytes.Length && !IsLineEnding(bytes[index]))
        {
            var c = bytes[index];

            if (allowInlineComments && c <= 0x20)
            {
                possibleCommentOffset = index;

                do
                {
                    index++;
                } while (index < bytes.Length && !IsLineEnding(bytes[index]) && bytes[index] <= 0x20);

                if (index < bytes.Length && bytes[index] is (byte)';' or (byte)'#')
                {
                    return possibleCommentOffset;
                }

                continue;
            }

            if (allowInlineComments && index == start && c is (byte)';' or (byte)'#')
            {
                return index;
            }

            index++;
        }

        return index;
    }

    private static int IndexOfNull(ReadOnlySpan<byte> bytes, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (bytes[i] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private void ConsumeTrivia(ReadOnlySpan<byte> bytes, ref int index, bool preserveTrivia)
    {
        while (index < bytes.Length)
        {
            var triviaOffset = index;
            var c = bytes[index];

            if (c <= 0x20)
            {
                do
                {
                    index++;
                } while (index < bytes.Length && bytes[index] <= 0x20);

                if (preserveTrivia)
                {
                    AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                        DToken.EncodeLengthOffsetPayload(index - triviaOffset, triviaOffset)));
                }

                continue;
            }

            if (c is (byte)';' or (byte)'#')
            {
                index = FindLineEnd(bytes, index);

                if (preserveTrivia)
                {
                    AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                        DToken.EncodeLengthOffsetPayload(index - triviaOffset, triviaOffset)));
                }

                continue;
            }

            break;
        }
    }

    private ParseException? ParseIni(ReadOnlySpan<byte> bytes, IniDocumentOptions options)
    {
        var errorCode = ParseException.ErrorCode.None;

        var rootId = AllocToken(DToken.MakeMap(0));
        var parentId = rootId;
        var sectionId = 0U;
        var preserveTrivia = options.PreserveTrivia;
        var globalKeys = options.AllowDuplicateKeys ? null : new HashSet<string>();
        var sectionKeys = options.AllowDuplicateKeys ? null : new HashSet<string>();

        var index = 0;
        var nullIndex = IndexOfNull(bytes, 0, bytes.Length);

        if (nullIndex >= 0)
        {
            errorCode = ParseException.ErrorCode.InvalidValue;
            index = nullIndex;
            goto parseError;
        }

        while (index < bytes.Length)
        {
            ConsumeTrivia(bytes, ref index, preserveTrivia);

            if (index >= bytes.Length)
            {
                break;
            }

            var c = bytes[index];

            switch ((char)c)
            {
                case '[': // section
                    {
                        var sectionOffset = index;
                        var offset = ++index;

                        while (index < bytes.Length && bytes[index] != ']' && bytes[index] != '\r' &&
                               bytes[index] != '\n')
                        {
                            index++;
                        }

                        if (index >= bytes.Length || bytes[index] != ']')
                        {
                            errorCode = ParseException.ErrorCode.UnterminatedSection;
                            index = offset - 1;
                            goto parseError;
                        }

                        var sectionNameOffset = TrimStart(bytes, offset, index);
                        var sectionNameEnd = TrimEnd(bytes, sectionNameOffset, index);

                        if (sectionNameEnd == sectionNameOffset)
                        {
                            errorCode = ParseException.ErrorCode.EmptyKey;
                            index = offset - 1;
                            goto parseError;
                        }

                        if (globalKeys != null)
                        {
                            var sectionName = Encoding.UTF8.GetString(
                                bytes.Slice(sectionNameOffset, sectionNameEnd - sectionNameOffset));

                            if (!globalKeys.Add(sectionName))
                            {
                                errorCode = ParseException.ErrorCode.DuplicateKey;
                                index = sectionNameOffset;
                                goto parseError;
                            }
                        }

                        if (preserveTrivia && sectionOffset != sectionNameOffset)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(sectionNameOffset - sectionOffset, sectionOffset)));
                        }

                        var tokenId = AllocToken(DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(sectionNameEnd - sectionNameOffset, sectionNameOffset)));

                        index++;

                        if (preserveTrivia && sectionNameEnd != index)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(index - sectionNameEnd, sectionNameEnd)));
                        }

                        var triviaOffset = index;

                        while (index < bytes.Length && !IsLineEnding(bytes[index]) &&
                               bytes[index] != ';' && bytes[index] != '#')
                        {
                            if (bytes[index] > 0x20)
                            {
                                errorCode = ParseException.ErrorCode.InvalidSectionHeader;
                                goto parseError;
                            }

                            index++;
                        }

                        if (preserveTrivia && triviaOffset != index)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                                DToken.EncodeLengthOffsetPayload(index - triviaOffset, triviaOffset)));
                        }

                        ConsumeTrivia(bytes, ref index, preserveTrivia);

                        if (sectionId != 0)
                        {
                            LinkToken(sectionId, tokenId);
                        }

                        IncToken(rootId);

                        parentId = AllocToken(DToken.MakeMap(0));
                        sectionId = parentId;
                        sectionKeys?.Clear();
                    }
                    break;
                default:
                    if (c <= 0x20)
                    {
                        ConsumeTrivia(bytes, ref index, preserveTrivia);
                    }
                    else
                    {
                        var kOffset = index;

                        while (index < bytes.Length && bytes[index] != '=' && !IsLineEnding(bytes[index]))
                        {
                            index++;
                        }

                        if (index >= bytes.Length || bytes[index] != '=')
                        {
                            errorCode = ParseException.ErrorCode.MissingEquals;
                            index = kOffset;
                            goto parseError;
                        }

                        var kEnd = TrimEnd(bytes, kOffset, index);

                        if (kEnd == kOffset)
                        {
                            errorCode = ParseException.ErrorCode.EmptyKey;
                            index = kOffset;
                            goto parseError;
                        }

                        var keySet = parentId == rootId ? globalKeys : sectionKeys;

                        if (keySet != null)
                        {
                            var key = Encoding.UTF8.GetString(bytes.Slice(kOffset, kEnd - kOffset));

                            if (!keySet.Add(key))
                            {
                                errorCode = ParseException.ErrorCode.DuplicateKey;
                                index = kOffset;
                                goto parseError;
                            }
                        }

                        AllocToken(DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(kEnd - kOffset, kOffset)));

                        var separatorOffset = kEnd;
                        index++;

                        while (index < bytes.Length && !IsLineEnding(bytes[index]) && bytes[index] <= 0x20)
                        {
                            index++;
                        }

                        var vOffset = index;

                        index = FindValueEnd(bytes, index, options.AllowInlineComments);

                        var vEnd = TrimEnd(bytes, vOffset, index);

                        if (preserveTrivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(vOffset - separatorOffset, separatorOffset)));
                        }

                        AllocToken(DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(vEnd - vOffset, vOffset)));

                        IncToken(parentId);

                        index = vEnd;
                    }

                    break;
            }
        }

        return null;

        parseError:
        if (index >= bytes.Length)
        {
            index = bytes.Length - 1;
        }

        if (index < 0)
        {
            index = 0;
        }

        return new ParseException(this, errorCode, index);
    }

    private static void WriteIniKeyValue(Utf8TextWriter writer, in DataReader reader, IniDocument? document,
        KeyValuePair<uint, uint> kv,
        ref IniWriteOptions options, ref SeparatorState state)
    {
        var value = reader.GetToken(kv.Value);

        if (value.IsContainer)
        {
            return;
        }

        if (options.PreserveTrivia)
        {
            state.AllowWriteComment = true;
            WriteTriviaList(writer, reader, document, kv.Key, ref state);
            state.AllowWriteComment = false;
        }

        if (state.NeedNewLine)
        {
            state.NeedNewLine = false;
            writer.WriteNewLine();
        }

        writer.WriteString(reader.ReadUtf8String(kv.Key));

        state.NeedKeySeparator = true;
        if (options.PreserveTrivia)
        {
            WriteTriviaList(writer, reader, document, kv.Value, ref state);
        }

        if (state.NeedKeySeparator)
        {
            state.NeedKeySeparator = false;

            if (options.WriteSpaces)
            {
                writer.WriteString(" = "u8);
            }
            else
            {
                writer.WriteUtf8Byte((byte)'=');
            }
        }

        switch (value.Kind)
        {
            case DTokenKind.Integer:
                if (value.Variant == DTokenVariant.IntegerUnsigned)
                {
                    writer.WriteUInt64(reader.ReadUInt64(kv.Value));
                }
                else
                {
                    writer.WriteInt64(reader.ReadInt64(kv.Value));
                }

                break;
            case DTokenKind.InlineFloat:
                writer.WriteDouble(reader.ReadDouble(kv.Value));
                break;
            case DTokenKind.Float:
                switch (value.Variant)
                {
                    case DTokenVariant.FloatDecimal:
                        writer.WriteDecimal(reader.ReadDecimal(kv.Value));
                        break;
                    case DTokenVariant.FloatSingle:
                        writer.WriteSingle(reader.ReadSingle(kv.Value));
                        break;
                    case DTokenVariant.FloatHalf:
                        writer.WriteHalf(reader.ReadHalf(kv.Value));
                        break;
                    default:
                        writer.WriteDouble(reader.ReadDouble(kv.Value));
                        break;
                }

                break;
            case DTokenKind.Timestamp:
                {
                    Span<byte> buffer = stackalloc byte[Utf8Helper.TimestampBufferSize];
                    var success = Utf8Helper.TryFormatTimestamp(reader, kv.Value, buffer, out var bytesWritten);
                    Debug.Assert(success);
                    writer.WriteString(buffer.Slice(0, bytesWritten));
                }
                break;
            case DTokenKind.Boolean:
                writer.WriteBoolean(reader.ReadBoolean(kv.Value));
                break;
            case DTokenKind.String:
            case DTokenKind.Symbol:
                writer.WriteString(reader.ReadUtf8String(kv.Value));
                break;
            case DTokenKind.ByteString:
                writer.WriteByteString(reader.ReadByteString(kv.Value), value.ByteStringKind);
                break;
            case DTokenKind.BigNumber:
                writer.WriteString(reader.ReadBigNumber(kv.Value));
                break;
        }
    }

    private static void WriteTriviaList(Utf8TextWriter writer, in DataReader reader, IniDocument? document,
        uint tokenId,
        ref SeparatorState state)
    {
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var triviaKind = reader.GetToken(triviaId).TriviaKind;
            var trivia = reader.ReadTrivia(triviaId);

            if (document != null && !reader.GetToken(triviaId).IsExtended)
            {
                switch (triviaKind)
                {
                    case TriviaKind.Separator:
                        if (trivia.IndexOf((byte)'=') >= 0)
                        {
                            state.NeedKeySeparator = false;
                        }

                        if (trivia.IndexOf((byte)'[') >= 0)
                        {
                            state.NeedStartSectionSeparator = false;
                        }

                        if (trivia.IndexOf((byte)']') >= 0)
                        {
                            state.NeedEndSectionSeparator = false;
                        }

                        break;
                    case TriviaKind.Whitespace:
                        if (trivia.IndexOf((byte)'\n') >= 0)
                        {
                            state.NeedNewLine = false;
                        }

                        break;
                }

                if (triviaKind == TriviaKind.LineComment)
                {
                    writer.WriteUtf8Byte(document.GetCommentChar(reader.GetToken(triviaId)));
                }

                writer.WriteString(trivia);
            }
            else
            {
                if (!state.AllowWriteComment)
                {
                    continue;
                }

                if (triviaKind == TriviaKind.LineComment || triviaKind == TriviaKind.Default)
                {
                    if (state.NeedNewLine)
                    {
                        writer.WriteNewLine();
                    }

                    writer.WriteUtf8Byte((byte)';');
                    writer.WriteString(trivia);
                    state.NeedNewLine = true;
                }
            }
        }
    }


    private static void WriteIni(Utf8TextWriter writer, in DataReader reader, IniDocument? document, uint tokenId,
        ref IniWriteOptions options, ref SeparatorState state)
    {
        var token = reader.GetToken(tokenId);

        if (token.Type != DTokenType.Map)
        {
            return;
        }

        foreach (var kv in reader.EnumerateMap(tokenId))
        {
            var value = reader.GetToken(kv.Value);

            if (value.IsContainer)
            {
                continue;
            }

            WriteIniKeyValue(writer, reader, document, kv, ref options, ref state);
            state.NeedNewLine = true;
        }

        foreach (var kv in reader.EnumerateMap(tokenId))
        {
            var value = reader.GetToken(kv.Value);

            if (value.Type != DTokenType.Map)
            {
                continue;
            }

            state.NeedStartSectionSeparator = true;

            if (options.PreserveTrivia)
            {
                state.AllowWriteComment = true;
                WriteTriviaList(writer, reader, document, kv.Key, ref state);
                state.AllowWriteComment = false;
            }

            if (state.NeedNewLine)
            {
                state.NeedNewLine = false;
                writer.WriteNewLine();
            }

            if (state.NeedStartSectionSeparator)
            {
                state.NeedStartSectionSeparator = false;
                writer.WriteUtf8Byte((byte)'[');
            }

            writer.WriteString(reader.ReadUtf8String(kv.Key));

            state.NeedNewLine = true;
            state.NeedEndSectionSeparator = true;

            if (options.PreserveTrivia)
            {
                WriteTriviaList(writer, reader, document, kv.Value, ref state);
            }

            if (state.NeedEndSectionSeparator)
            {
                state.NeedEndSectionSeparator = false;
                writer.WriteUtf8Byte((byte)']');
            }

            foreach (var kv2 in reader.EnumerateMap(kv.Value))
            {
                WriteIniKeyValue(writer, reader, document, kv2, ref options, ref state);
                state.NeedNewLine = true;
            }
        }
    }

    private byte GetCommentChar(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, 1)[0];
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        if (token.TriviaKind == TriviaKind.LineComment)
        {
            return _source.Span.Slice(param.offset + 1, param.length - 1);
        }

        return _source.Span.Slice(param.offset, param.length);
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, param.length);
    }

    private ref struct SeparatorState
    {
        public bool NeedNewLine;
        public bool NeedKeySeparator;
        public bool NeedStartSectionSeparator;
        public bool NeedEndSectionSeparator;
        public bool AllowWriteComment;
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            UnterminatedSection,
            InvalidSectionHeader,
            EmptyKey,
            InvalidValue,
            MissingEquals,
            DuplicateKey
        }

        public ParseException(IniDocument doc, ErrorCode error, int offset, int length = -1)
            : this(doc, error, offset, length, GetLinePosition(doc._source.Span, offset))
        {
        }

        private ParseException(IniDocument doc, ErrorCode error, int offset, int length,
            (long LineNumber, long BytePositionInLine) position)
            : base(nameof(IniDocument), offset, CreateMessage(doc, error, offset, length, position), position.LineNumber,
                position.BytePositionInLine)
        {
        }

        private static string CreateMessage(IniDocument doc, ErrorCode error, int offset, int length,
            (long LineNumber, long BytePositionInLine) position)
        {
            var sb = new StringBuilder();

            sb.Append($"{nameof(IniDocument)} Error: {error}");
            AppendErrorValue(sb, doc._source.Span, offset, length);
            sb.Append($", LineNumber: {position.LineNumber}, BytePositionInLine: {position.BytePositionInLine}");

            return sb.ToString();
        }
    }
}