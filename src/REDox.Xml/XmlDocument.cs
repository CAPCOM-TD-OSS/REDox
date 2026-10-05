// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.IO;
using System.Text;
using REDox.Serialization;
using static REDox.Helper;

namespace REDox.Xml;

public sealed class XmlDocument : Document
{
    private static readonly byte[] XmlQuot = Encoding.UTF8.GetBytes("&quot;");
    private static readonly byte[] XmlApos = Encoding.UTF8.GetBytes("&apos;");
    private static readonly byte[] XmlLt = Encoding.UTF8.GetBytes("&lt;");
    private static readonly byte[] XmlGt = Encoding.UTF8.GetBytes("&gt;");
    private static readonly byte[] XmlAmp = Encoding.UTF8.GetBytes("&amp;");

    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    private XmlDocument(SerializerSettings settings) : base(settings)
    {
    }

    public ReadOnlyMemory<byte> Source => _source;

    public XmlDocument Duplicate()
    {
        if (_rentedBuffer != null)
        {
            _source = _source.ToArray();

            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }

        return CreateSnapshot<XmlDocument>();
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

    public static XmlDocument Parse(string xml, SerializerSettings? settings = null, XmlDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(xml.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(xml, buffer.AsSpan());

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

    public static XmlDocument Parse(Stream stream, SerializerSettings? settings = null,
        XmlDocumentOptions options = default)
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

    /// <remarks>
    ///     The returned document takes ownership of <paramref name="utf8Xml" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static XmlDocument Parse(byte[] utf8Xml, SerializerSettings? settings = null,
        XmlDocumentOptions options = default)
    {
        return Parse(utf8Xml.AsMemory(), settings, options);
    }

    public static XmlDocument Parse(ReadOnlySpan<byte> utf8Xml, SerializerSettings? settings = null,
        XmlDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Xml.Length);

        try
        {
            utf8Xml.CopyTo(buffer);

            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, utf8Xml.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="utf8Xml" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static XmlDocument Parse(ReadOnlyMemory<byte> utf8Xml, SerializerSettings? settings = null,
        XmlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new XmlDocument(settings);

        try
        {
            doc.EnsureCapacity(utf8Xml.Length / 16);
            doc.Read(utf8Xml, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static XmlDocument ParseRented(ReadOnlyMemory<byte> utf8Xml, byte[] rentedBuffer,
        SerializerSettings? settings = null, XmlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new XmlDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.EnsureCapacity(utf8Xml.Length / 16);
            doc.Read(utf8Xml, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static byte[] Encode(DElement element, XmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            return writer.Encode();
        }
    }

    public static string EncodeToString(DElement element, XmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream stream, XmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(stream, reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            writer.Dispose();
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter, XmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(bufferWriter, reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            writer.Dispose();
        }
    }

    private void Read(ReadOnlyMemory<byte> bytes, XmlDocumentOptions options)
    {
        _source = bytes;
        ParseXml(_source.Span, options);
        RootId = 1;
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, uint rootId, XmlWriteOptions options)
    {
        WriteXml(writer, reader, reader.Document as XmlDocument, rootId, XmlTextType.Raw, ref options);
    }

    private void ParseXml(ReadOnlySpan<byte> chars, XmlDocumentOptions options)
    {
        using var stack = new LocalStack<(uint pid, uint lid)>(stackalloc (uint pid, uint lid)[64], options.MaxDepth);

        var index = 0;
        var textIndex = 0;
        var textEscaped = 0;
        var hasText = false;
        var keepComments = options.PreserveTrivia;

        var parentId = AllocToken(DToken.MakeArray(0));
        var latestId = 0U;

        while (index < chars.Length)
        {
            var c = chars[index];

            if (c != '<')
            {
                if (c > 0x20)
                {
                    if (c == '&')
                    {
                        textEscaped |= 1;
                    }
                    else if (c == ';')
                    {
                        textEscaped |= 2;
                    }

                    hasText = true;
                }

                index++;
                continue;
            }

            if (hasText && textIndex > 0)
            {
                var length = index - textIndex;
                var innerId = AllocToken(DToken.Make(DTokenVariant.String,
                    DToken.EncodeLengthOffsetPayload(DToken.EncodeStringParamPayload(length, textEscaped == 3),
                        textIndex)));
                IncToken(parentId);

                if (latestId != 0)
                {
                    LinkToken(latestId, innerId);
                }

                latestId = innerId;
            }

            c = ReadXmlByte(chars, ++index);

            switch ((char)c)
            {
                default:
                    {
                        while (c <= 0x20)
                        {
                            c = chars[++index];
                        }

                        var tokenIndex = index;

                        while (!(c <= 0x20 || c == '>' || c == '/'))
                        {
                            c = chars[++index];
                        }

                        var elementId = AllocToken(DToken.MakeMap(0));
                        var elementToken = DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex));

                        IncToken(parentId);
                        if (latestId != 0)
                        {
                            LinkToken(latestId, elementId);
                        }

                        latestId = elementId;

                        //parse attributes
                        c = chars[index];
                        for (;;)
                        {
                            while (c <= 0x20)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (c == '>' || (c == '/' && index + 1 < chars.Length && chars[index + 1] == '>'))
                            {
                                break;
                            }

                            var attrIndex = index;
                            while (c > 0x20 && c != '=' && c != '>' && c != '/' && c != '<')
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (index == attrIndex)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            AllocToken(DToken.Make(DTokenVariant.String,
                                DToken.EncodeLengthOffsetPayload(index - attrIndex, attrIndex)));

                            while (c <= 0x20)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (c != '=')
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            c = ReadXmlByte(chars, ++index);
                            while (c <= 0x20)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            // Every attribute needs a quoted value before its map entry is counted.
                            if (c != '"' && c != '\'')
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            var type = c;
                            var escaped = 0;

                            c = ReadXmlByte(chars, ++index);
                            var valueIndex = index;
                            while (c != type)
                            {
                                if (c == '&')
                                {
                                    escaped |= 1;
                                }
                                else if (c == ';')
                                {
                                    escaped |= 2;
                                }

                                c = ReadXmlByte(chars, ++index);
                            }

                            var kind = type == '"' ? DTokenVariant.String : DTokenVariant.StringSingleQuote;
                            AllocToken(DToken.Make(kind,
                                DToken.EncodeLengthOffsetPayload(
                                    DToken.EncodeStringParamPayload(index - valueIndex, escaped == 3),
                                    valueIndex)));
                            c = ReadXmlByte(chars, ++index);
                            IncToken(elementId);
                        }

                        AllocToken(elementToken);
                        var innerId = AllocToken(DToken.MakeArray(0));

                        if (c != '/')
                        {
                            stack.Push((parentId, latestId));
                            parentId = innerId;
                            latestId = 0;
                        }
                        else
                        {
                            c = chars[++index];
                        }

                        IncToken(elementId);

                        index++;
                        textIndex = index;
                        textEscaped = 0;
                        hasText = false;
                    }
                    break;
                case '/':
                    {
                        c = chars[++index];

                        while (c <= 0x20)
                        {
                            c = chars[++index];
                        }

                        while (!(c <= 0x20 || c == '>' || c == '/'))
                        {
                            c = chars[++index];
                        }

                        (parentId, latestId) = stack.Pop();

                        c = chars[index];
                        while (c <= 0x20)
                        {
                            c = chars[++index];
                        }

                        if (c == '>')
                        {
                            index++;
                        }

                        textIndex = index;
                        textEscaped = 0;
                        hasText = false;
                    }
                    break;
                case '!':
                    {
                        c = chars[++index];

                        if (c == '-' && chars[index + 1] == '-')
                        {
                            //comment
                            var commentIndex = index + 2;
                            while (!(c == '-' && chars[index + 1] == '-' && chars[index + 2] == '>'))
                            {
                                c = chars[++index];
                            }

                            if (keepComments)
                            {
                                AllocToken(DToken.Make(DTokenVariant.TriviaBlockComment,
                                    DToken.EncodeLengthOffsetPayload(index - commentIndex, commentIndex)));
                            }

                            index += 2;
                        }
                        else
                        {
                            var innerId = AllocToken(DToken.MakeArray(1));

                            var tokenIndex = index;
                            var scope = 0;
                            while (!(c == '>' && scope == 0))
                            {
                                if (c == '<')
                                {
                                    scope++;
                                }

                                if (c == '>')
                                {
                                    scope--;
                                }

                                c = chars[++index];
                            }

                            AllocToken(DToken.Make(DTokenVariant.String,
                                DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex)));

                            IncToken(parentId);
                            if (latestId != 0)
                            {
                                LinkToken(latestId, innerId);
                            }

                            latestId = innerId;
                        }

                        index++;
                        textIndex = index;
                        textEscaped = 0;
                        hasText = false;
                    }
                    break;
                case '?':
                    {
                        c = ReadXmlByte(chars, ++index);
                        var tokenIndex = index;
                        while (c > 0x20 && c != '?' && c != '>' && c != '<')
                        {
                            c = ReadXmlByte(chars, ++index);
                        }

                        if (index == tokenIndex)
                        {
                            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                        }

                        var isDeclaration = chars.Slice(tokenIndex, index - tokenIndex).SequenceEqual("xml"u8);

                        // Each instruction belongs to its current parent and position in the child list.
                        var instructionParentId = AllocToken(DToken.MakeArray(1));
                        IncToken(parentId);
                        if (latestId != 0)
                        {
                            LinkToken(latestId, instructionParentId);
                        }

                        latestId = instructionParentId;
                        AllocToken(DToken.MakeMap(1));
                        AllocToken(DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex)));
                        var elementId = AllocToken(DToken.MakeMap(0));

                        if (!isDeclaration)
                        {
                            // Processing-instruction data is opaque text, not XML attributes.
                            var terminator = chars.Slice(index).IndexOf("?>"u8);
                            if (terminator < 0 || (c > 0x20 && terminator != 0))
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            var endIndex = index + terminator;
                            while (index < endIndex && chars[index] <= 0x20)
                            {
                                index++;
                            }

                            if (index < endIndex)
                            {
                                AllocToken(DToken.Make(DTokenVariant.String,
                                    DToken.EncodeLengthOffsetPayload(endIndex - index, index)));
                                AllocToken(DToken.Make(DTokenVariant.Null, 0));
                                IncToken(elementId);
                            }

                            index = endIndex + 2;
                            textIndex = index;
                            textEscaped = 0;
                            hasText = false;
                            break;
                        }

                        // XML declaration values require quotes and a complete ?> terminator.
                        for (;;)
                        {
                            while (c <= 0x20)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (c == '?' && index + 1 < chars.Length && chars[index + 1] == '>')
                            {
                                break;
                            }

                            var attrIndex = index;
                            while (c > 0x20 && c != '=' && c != '>' && c != '?' && c != '<')
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (index == attrIndex)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            AllocToken(DToken.Make(DTokenVariant.String,
                                DToken.EncodeLengthOffsetPayload(index - attrIndex, attrIndex)));

                            while (c <= 0x20)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (c != '=')
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            c = ReadXmlByte(chars, ++index);
                            while (c <= 0x20)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            if (c != '"' && c != '\'')
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            var type = c;
                            c = ReadXmlByte(chars, ++index);
                            var valueIndex = index;
                            while (c != type)
                            {
                                c = ReadXmlByte(chars, ++index);
                            }

                            AllocToken(DToken.Make(
                                type == '"' ? DTokenVariant.String : DTokenVariant.StringSingleQuote,
                                DToken.EncodeLengthOffsetPayload(index - valueIndex, valueIndex)));
                            c = ReadXmlByte(chars, ++index);
                            IncToken(elementId);
                        }

                        index += 2;
                        textIndex = index;
                        textEscaped = 0;
                        hasText = false;
                    }
                    break;
            }
        }
    }

    private byte ReadXmlByte(ReadOnlySpan<byte> chars, int index)
    {
        if (index >= chars.Length)
        {
            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, chars.Length);
        }

        return chars[index];
    }

    private static void WriteComments(Utf8TextWriter writer, in DataReader reader, uint tokenId,
        ref XmlWriteOptions options)
    {
        if (!options.PreserveTrivia)
        {
            return;
        }

        foreach (var commentId in reader.EnumerateTrivia(tokenId))
        {
            if (reader.GetToken(commentId).Variant != DTokenVariant.TriviaWhitespace)
            {
                WriteXmlComment(writer, reader.ReadTrivia(commentId));
                writer.WriteIndentNewLine();
            }
        }
    }

    private static void WriteXml(Utf8TextWriter writer, in DataReader reader, XmlDocument? doc, uint tokenId,
        XmlTextType textType, ref XmlWriteOptions options)
    {
        var value = reader.GetToken(tokenId);

        WriteComments(writer, reader, tokenId, ref options);

        if (value.IsContainer)
        {
            if (value.Type == DTokenType.Array)
            {
                var first = true;
                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    if (reader.GetToken(valueId).Type == DTokenType.Array)
                    {
                        foreach (var elementId in reader.EnumerateArray(valueId))
                        {
                            if (reader.GetToken(elementId).Type != DTokenType.Map)
                            {
                                writer.PushIndent();
                                writer.WriteIndentNewLine();
                                writer.WriteUtf8Byte((byte)'<');
                                writer.WriteUtf8Byte((byte)'!');
                                WriteXml(writer, reader, doc, elementId, XmlTextType.Raw, ref options);
                                writer.WriteUtf8Byte((byte)'>');
                                writer.PopIndent();
                                writer.WriteIndentNewLine();
                            }
                            else
                            {
                                foreach (var kv in reader.EnumerateMap(elementId))
                                {
                                    writer.WriteUtf8Byte((byte)'<');
                                    writer.WriteUtf8Byte((byte)'?');

                                    writer.WriteString(reader.ReadUtf8String(kv.Key));

                                    foreach (var attr in reader.EnumerateMap(kv.Value))
                                    {
                                        writer.WriteUtf8Byte((byte)' ');
                                        writer.WriteString(reader.ReadUtf8String(attr.Key));

                                        var attrValue = reader.GetToken(attr.Value);

                                        if (attrValue.Kind != DTokenKind.Null)
                                        {
                                            var quot = attrValue.Variant == DTokenVariant.StringSingleQuote
                                                ? '\''
                                                : '"';
                                            writer.WriteUtf8Byte((byte)'=');
                                            writer.WriteUtf8Byte((byte)quot);
                                            WriteXml(writer, reader, doc, attr.Value, XmlTextType.Raw, ref options);
                                            writer.WriteUtf8Byte((byte)quot);
                                        }
                                    }

                                    writer.WriteUtf8Byte((byte)'?');
                                    writer.WriteUtf8Byte((byte)'>');
                                    writer.WriteIndentNewLine();
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!first)
                        {
                            writer.WriteIndentNewLine();
                        }

                        first = false;

                        WriteXml(writer, reader, doc, valueId, textType, ref options);
                    }
                }
            }
            else
            {
                var pairs = reader.EnumerateMap(tokenId);

                foreach (var pair in pairs)
                {
                    if (reader.GetToken(pair.Value).Type != DTokenType.Array)
                    {
                        continue;
                    }

                    var element = reader.ReadUtf8String(pair.Key);

                    writer.WriteUtf8Byte((byte)'<');
                    writer.WriteString(element);

                    foreach (var attr in pairs)
                    {
                        var attrValue = reader.GetToken(attr.Value);
                        var type = attrValue.Kind;

                        if (attrValue.IsContainer)
                        {
                            continue;
                        }

                        writer.WriteUtf8Byte((byte)' ');
                        writer.WriteString(reader.ReadUtf8String(attr.Key));

                        if (type != DTokenKind.Null)
                        {
                            var quot = attrValue.Variant == DTokenVariant.StringSingleQuote ? (byte)'\'' : (byte)'"';
                            writer.WriteUtf8Byte((byte)'=');
                            writer.WriteUtf8Byte(quot);
                            WriteXml(writer, reader, doc, attr.Value,
                                quot == '"' ? XmlTextType.QuotAttribute : XmlTextType.AposAttribute, ref options);
                            writer.WriteUtf8Byte(quot);
                        }
                    }

                    var count = reader.GetValueCount(pair.Value);

                    if (count == 0)
                    {
                        writer.WriteUtf8Byte((byte)'/');
                        writer.WriteUtf8Byte((byte)'>');
                    }
                    else
                    {
                        writer.WriteUtf8Byte((byte)'>');

                        var indented = true;

                        if (count == 1)
                        {
                            var array = reader.EnumerateArray(pair.Value);
                            array.MoveNext();

                            var top = reader.GetToken(array.Current);
                            if (top.Type != DTokenType.Map)
                            {
                                indented = false;
                            }
                        }

                        if (indented)
                        {
                            writer.PushIndent();
                            writer.WriteIndentNewLine();
                        }

                        WriteXml(writer, reader, doc, pair.Value, XmlTextType.Inner, ref options);
                        if (indented)
                        {
                            writer.PopIndent();
                            writer.WriteIndentNewLine();
                        }

                        writer.WriteString("</"u8);
                        writer.WriteString(element);
                        writer.WriteUtf8Byte((byte)'>');
                    }

                    break;
                }
            }
        }
        else
        {
            switch (value.Kind)
            {
                case DTokenKind.Null:
                    writer.WriteNull();
                    break;
                case DTokenKind.Boolean:
                    writer.WriteBoolean(reader.ReadBoolean(tokenId));
                    break;
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    WriteXmlString(writer, reader.ReadUtf8String(tokenId), textType);
                    break;
                case DTokenKind.Integer:
                    if (value.Variant == DTokenVariant.IntegerUnsigned)
                    {
                        writer.WriteUInt64(reader.ReadUInt64(tokenId));
                    }
                    else
                    {
                        writer.WriteInt64(reader.ReadInt64(tokenId));
                    }

                    break;
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
                case DTokenKind.Float:
                    if (value.Variant == DTokenVariant.FloatDecimal)
                    {
                        writer.WriteDecimal(reader.ReadDecimal(tokenId));
                    }
                    else
                    {
                        writer.WriteDouble(reader.ReadDouble(tokenId));
                    }

                    break;
                case DTokenKind.ByteString:
                    writer.WriteByteString(reader.ReadByteString(tokenId));
                    break;
            }
        }
    }

    private static void WriteXmlComment(Utf8TextWriter writer, ReadOnlySpan<byte> comment)
    {
        var buf = writer.BeginWriteUtf8Bytes(comment.Length * 6);
        var pt = 0;

        buf[pt++] = (byte)'<';
        buf[pt++] = (byte)'-';
        buf[pt++] = (byte)'-';

        comment.CopyTo(buf.Slice(pt));
        pt += comment.Length;

        buf[pt++] = (byte)'-';
        buf[pt++] = (byte)'-';
        buf[pt++] = (byte)'>';

        writer.EndWriteUtf8Bytes(pt);
    }


    private static void WriteXmlString(Utf8TextWriter writer, ReadOnlySpan<byte> value, XmlTextType type)
    {
        if (type == XmlTextType.Raw)
        {
            writer.WriteString(value);
            return;
        }

        var buf = writer.BeginWriteUtf8Bytes(value.Length * 4);
        var pt = 0;

        foreach (var c in value)
        {
            switch (c)
            {
                case (byte)'<':
                    buf[pt++] = (byte)'&';
                    buf[pt++] = (byte)'l';
                    buf[pt++] = (byte)'t';
                    buf[pt++] = (byte)';';
                    break;
                case (byte)'>':
                    buf[pt++] = (byte)'&';
                    buf[pt++] = (byte)'g';
                    buf[pt++] = (byte)'t';
                    buf[pt++] = (byte)';';
                    break;
                case (byte)'&':
                    buf[pt++] = (byte)'&';
                    buf[pt++] = (byte)'a';
                    buf[pt++] = (byte)'m';
                    buf[pt++] = (byte)'p';
                    buf[pt++] = (byte)';';
                    break;
                case (byte)'"':
                    if (type != XmlTextType.QuotAttribute)
                    {
                        buf[pt++] = c;
                    }
                    else
                    {
                        buf[pt++] = (byte)'&';
                        buf[pt++] = (byte)'q';
                        buf[pt++] = (byte)'u';
                        buf[pt++] = (byte)'o';
                        buf[pt++] = (byte)'t';
                        buf[pt++] = (byte)';';
                    }

                    break;
                case (byte)'\'':
                    if (type != XmlTextType.AposAttribute)
                    {
                        buf[pt++] = c;
                    }
                    else
                    {
                        buf[pt++] = (byte)'&';
                        buf[pt++] = (byte)'a';
                        buf[pt++] = (byte)'p';
                        buf[pt++] = (byte)'o';
                        buf[pt++] = (byte)'s';
                        buf[pt++] = (byte)';';
                    }

                    break;
                default:
                    buf[pt++] = c;
                    break;
            }
        }

        writer.EndWriteUtf8Bytes(pt);
    }

    private static void WriteXmlInnerString(DocumentWriter dw, ReadOnlySpan<byte> value)
    {
        var buf = dw.BeginWrite(value.Length * 4);
        var pt = 0;

        foreach (var c in value)
        {
            switch (c)
            {
                case (byte)'<':
                    buf[pt++] = (byte)'&';
                    buf[pt++] = (byte)'l';
                    buf[pt++] = (byte)'t';
                    buf[pt++] = (byte)';';
                    break;
                case (byte)'>':
                    buf[pt++] = (byte)'&';
                    buf[pt++] = (byte)'g';
                    buf[pt++] = (byte)'t';
                    buf[pt++] = (byte)';';
                    break;
                case (byte)'&':
                    buf[pt++] = (byte)'&';
                    buf[pt++] = (byte)'a';
                    buf[pt++] = (byte)'m';
                    buf[pt++] = (byte)'p';
                    buf[pt++] = (byte)';';
                    break;
                default:
                    buf[pt++] = c;
                    break;
            }
        }

        dw.EndWrite(pt);
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        return _source.Span.Slice(param.offset, param.length);
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var stringParam = DToken.DecodeStringParamPayload(param.length);

        var src = _source.Slice(param.offset, stringParam.encodedLength);

        if (!stringParam.escaped)
        {
            return src.Span;
        }

        return DecodeEscapedXmlString(src.Span);
    }

    public static ReadOnlySpan<byte> DecodeEscapedXmlString(ReadOnlySpan<byte> src)
    {
        var dst = new byte[src.Length];

        var i = 0;
        var length = 0;
        while (i < src.Length)
        {
            var c = src[i++];

            if (c == '&')
            {
                switch ((char)src[i])
                {
                    case 'a':
                        if (src.Slice(i - 1, XmlAmp.Length).SequenceEqual(XmlAmp))
                        {
                            dst[length++] = (byte)'&';
                            i += XmlAmp.Length - 1;
                        }
                        else
                        {
                            if (src.Slice(i - 1, XmlApos.Length).SequenceEqual(XmlApos))
                            {
                                dst[length++] = (byte)'\'';
                                i += XmlApos.Length - 1;
                            }
                        }

                        break;
                    case 'l':
                        if (src.Slice(i - 1, XmlLt.Length).SequenceEqual(XmlLt))
                        {
                            dst[length++] = (byte)'<';
                            i += XmlLt.Length - 1;
                        }

                        break;
                    case 'g':
                        if (src.Slice(i - 1, XmlGt.Length).SequenceEqual(XmlGt))
                        {
                            dst[length++] = (byte)'>';
                            i += XmlGt.Length - 1;
                        }

                        break;
                    case 'q':
                        if (src.Slice(i - 1, XmlQuot.Length).SequenceEqual(XmlQuot))
                        {
                            dst[length++] = (byte)'"';
                            i += XmlQuot.Length - 1;
                        }

                        break;
                }
            }
            else
            {
                dst[length++] = c;
            }
        }

        return dst.AsSpan().Slice(0, length);
    }

    private enum XmlTextType
    {
        Raw,
        Inner,
        AposAttribute,
        QuotAttribute
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat
        }

        public ErrorCode _errorCode;

        public ParseException(XmlDocument doc, ErrorCode error, int offset) : base(nameof(XmlDocument), offset)
        {
            _errorCode = error;
        }
    }
}