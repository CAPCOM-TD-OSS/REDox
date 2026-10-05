// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization;
using static REDox.Helper;

namespace REDox.Html;

public sealed class HtmlDocument : Document
{
    private static Tag[] _tags = Array.Empty<Tag>();
    private static long[] _tagsLut = Array.Empty<long>();
    private static NamedChar[] _namedChars = Array.Empty<NamedChar>();
    private static long[] _namedCharsLut = Array.Empty<long>();

    private static readonly byte[] HtmlDoctype = Encoding.UTF8.GetBytes("doctype");
    private static readonly byte[] HtmlCdata = Encoding.UTF8.GetBytes("cdata");

    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;

    static HtmlDocument()
    {
        CreateTagTable();
        CreateNamedCharTable();
    }

    private HtmlDocument(SerializerSettings settings) : base(settings)
    {
    }

    public ReadOnlyMemory<byte> Source => _source;

    public HtmlDocument Duplicate()
    {
        var doc = CreateSnapshot<HtmlDocument>();

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

    public static HtmlDocument Parse(Stream stream, SerializerSettings? settings = null,
        HtmlDocumentOptions options = default)
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

    public static HtmlDocument Parse(string html, SerializerSettings? settings = null,
        HtmlDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(html.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(html, buffer.AsSpan());

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
    ///     The returned document takes ownership of <paramref name="utf8Html" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static HtmlDocument Parse(byte[] utf8Html, SerializerSettings? settings = null,
        HtmlDocumentOptions options = default)
    {
        return Parse(utf8Html.AsMemory(), settings, options);
    }

    public static HtmlDocument Parse(ReadOnlySpan<byte> utf8Html, SerializerSettings? settings = null,
        HtmlDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Html.Length);

        try
        {
            utf8Html.CopyTo(buffer);

            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, utf8Html.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="utf8Html" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static HtmlDocument Parse(ReadOnlyMemory<byte> utf8Html, SerializerSettings? settings = null,
        HtmlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new HtmlDocument(settings);

        try
        {
            doc.EnsureCapacity(utf8Html.Length / 16);
            doc.Read(utf8Html, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static HtmlDocument ParseRented(ReadOnlyMemory<byte> utf8Html, byte[] rentedBuffer,
        SerializerSettings? settings = null, HtmlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new HtmlDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.EnsureCapacity(utf8Html.Length / 16);
            doc.Read(utf8Html, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }


    public static byte[] Encode(DElement element, HtmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings);
            Write(writer, reader, element.Id, options);

            return writer.Encode();
        }
    }


    public static string EncodeToString(DElement element, HtmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings);

            Write(writer, reader, reader.RootId, options);

            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream stream, HtmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            try
            {
                writer.Reset(stream, reader.Settings);
                Write(writer, reader, reader.RootId, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        HtmlWriteOptions options = default)
    {
        using (var cache = InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            try
            {
                writer.Reset(bufferWriter, reader.Settings);
                Write(writer, reader, reader.RootId, options);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    private void Read(ReadOnlyMemory<byte> bytes, HtmlDocumentOptions options)
    {
        _source = bytes;
        RootId = ParseHTML(_source.Span, options);
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, uint rootId, HtmlWriteOptions options)
    {
        Write(writer, reader, rootId, XmlTextType.Inner, ref options);
    }

    private uint ParseHTML(ReadOnlySpan<byte> chars, HtmlDocumentOptions options)
    {
        using var stack = new LocalStack<State>(stackalloc State[64], options.MaxDepth);

        uint parentId = 0;
        uint latestId = 0;

        var index = 0;
        var textIndex = 0;
        var textEscaped = false;
        var hasText = false;

        {
            var tokenId = AllocToken(DToken.MakeArray(0));

            if (!stack.Push(new State(parentId, latestId, Tag.General)))
            {
                throw new ParseException(this, ParseException.ErrorCode.MaxDepthExceeded, index);
            }

            parentId = tokenId;
            latestId = 0;
        }

        while (index < chars.Length)
        {
            var c = chars[index];

            switch (c)
            {
                default:
                    hasText = true;
                    if (c == '&')
                    {
                        textEscaped = true;
                    }

                    index++;
                    break;
                case (byte)'<':
                    if (hasText && textIndex > 0 && index - textIndex > 0)
                    {
                        var innerId = AllocToken(DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(
                                DToken.EncodeStringParamPayload(index - textIndex, textEscaped),
                                textIndex)));
                        IncToken(parentId);

                        if (latestId != 0)
                        {
                            LinkToken(latestId, innerId);
                        }

                        latestId = innerId;
                    }

                    var nc = chars[index + 1];
                    switch (nc)
                    {
                        default:
                            {
                                var empty = false;
                                c = chars[++index];

                                while (c <= 0x20)
                                {
                                    c = chars[++index];
                                }

                                var tokenIndex = index;

                                var tag = ParseTag(chars, ref index);

                                for (;;)
                                {
                                    var omit = false;
                                    var latestTag = stack.Peek().Tag;

                                    switch (latestTag)
                                    {
                                        case Tag.P:
                                            switch (tag)
                                            {
                                                case Tag.Address:
                                                case Tag.Article:
                                                case Tag.Aside:
                                                case Tag.BlockQuote:
                                                case Tag.Details:
                                                case Tag.Div:
                                                case Tag.Dl:
                                                case Tag.FieldSet:
                                                case Tag.FigCaption:
                                                case Tag.Figure:
                                                case Tag.Footer:
                                                case Tag.Form:
                                                case Tag.H1:
                                                case Tag.H2:
                                                case Tag.H3:
                                                case Tag.H4:
                                                case Tag.H5:
                                                case Tag.H6:
                                                case Tag.Head:
                                                case Tag.Hgroup:
                                                case Tag.Hr:
                                                case Tag.Main:
                                                case Tag.Menu:
                                                case Tag.Nav:
                                                case Tag.Ol:
                                                case Tag.P:
                                                case Tag.Pre:
                                                case Tag.Section:
                                                case Tag.Table:
                                                case Tag.Ul:
                                                    omit = true;
                                                    break;
                                            }

                                            break;
                                        case Tag.ColGroup:
                                            if (tag == Tag.ColGroup)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Col:
                                            if (tag == Tag.ColGroup)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Li:
                                            if (tag == Tag.Li)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Dt:
                                        case Tag.Dd:
                                            if (tag == Tag.Dt || tag == Tag.Dd)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Rt:
                                        case Tag.Rp:
                                            if (tag == Tag.Rt || tag == Tag.Rp)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Tr:
                                            if (tag == Tag.Tr)
                                            {
                                                omit = true;
                                            }

                                            if (tag == Tag.THead || tag == Tag.TFoot || tag == Tag.Tbody)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Td:
                                        case Tag.Th:
                                            if (tag == Tag.Td || tag == Tag.Th)
                                            {
                                                omit = true;
                                            }

                                            if (tag == Tag.Tr)
                                            {
                                                omit = true;
                                            }

                                            if (tag == Tag.THead || tag == Tag.TFoot || tag == Tag.Tbody)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.OptGroup:
                                            if (tag == Tag.OptGroup)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Option:
                                            if (tag == Tag.OptGroup)
                                            {
                                                omit = true;
                                            }

                                            if (tag == Tag.Option)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.THead:
                                            if (tag == Tag.Tbody || tag == Tag.TFoot)
                                            {
                                                omit = true;
                                            }

                                            break;
                                        case Tag.Tbody:
                                            if (tag == Tag.Tbody || tag == Tag.TFoot)
                                            {
                                                omit = true;
                                            }

                                            break;
                                    }

                                    if (!omit)
                                    {
                                        break;
                                    }

                                    var state = stack.Pop();
                                    parentId = state.ParentId;
                                    latestId = state.LatestId;
                                }

                                var elementLength = index - tokenIndex;

                                var elementId = AllocToken(DToken.MakeMap(0));

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
                                        c = chars[++index];
                                    }

                                    if (c == '>')
                                    {
                                        break;
                                    }

                                    if (c == '/' && chars[index + 1] == '>')
                                    {
                                        empty = true;
                                        index++;
                                        break;
                                    }

                                    var attrIndex = index;
                                    var escaped = false;
                                    while (c > 0x20 && c != '=' && c != '>')
                                    {
                                        if (c == '&')
                                        {
                                            escaped = true;
                                        }

                                        c = chars[++index];
                                    }

                                    AllocToken(DToken.Make(DTokenVariant.String,
                                        DToken.EncodeLengthOffsetPayload(
                                            DToken.EncodeStringParamPayload(index - attrIndex, escaped),
                                            attrIndex)));

                                    while (c <= 0x20)
                                    {
                                        c = chars[++index];
                                    }

                                    if (c == '=')
                                    {
                                        escaped = false;
                                        c = chars[++index];

                                        while (c <= 0x20)
                                        {
                                            c = chars[++index];
                                        }

                                        if (c == '"' || c == '\'')
                                        {
                                            var type = c;

                                            c = chars[++index];
                                            var valueIndex = index;
                                            while (c != type)
                                            {
                                                if (c == '&')
                                                {
                                                    escaped = true;
                                                }

                                                c = chars[++index];
                                            }

                                            AllocToken(DToken.Make(DTokenVariant.String,
                                                DToken.EncodeLengthOffsetPayload(
                                                    DToken.EncodeStringParamPayload(index - valueIndex, escaped),
                                                    valueIndex)));
                                            c = chars[++index];
                                        }
                                        else
                                        {
                                            var valueIndex = index;
                                            while (c > 0x20 && c != '>')
                                            {
                                                if (c == '&')
                                                {
                                                    escaped = true;
                                                }

                                                c = chars[++index];
                                            }

                                            if (index - valueIndex == 0)
                                            {
                                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat,
                                                    index);
                                            }

                                            AllocToken(DToken.Make(DTokenVariant.String,
                                                DToken.EncodeLengthOffsetPayload(
                                                    DToken.EncodeStringParamPayload(index - valueIndex, escaped),
                                                    valueIndex)));
                                        }

                                        IncToken(elementId);
                                    }
                                    else
                                    {
                                        AllocToken(DToken.Make(DTokenVariant.Null, 0));
                                        IncToken(elementId);
                                    }
                                }

                                AllocToken(
                                    DToken.Make(DTokenVariant.String,
                                        DToken.EncodeLengthOffsetPayload(elementLength, tokenIndex)));
                                var innerId = AllocToken(DToken.MakeArray(0));

                                if (IsVoidTag(tag))
                                {
                                    empty = true;
                                }

                                if (!empty)
                                {
                                    if (!stack.Push(new State(parentId, latestId, tag)))
                                    {
                                        throw new ParseException(this, ParseException.ErrorCode.MaxDepthExceeded,
                                            index);
                                    }

                                    parentId = innerId;
                                    latestId = 0;
                                }

                                IncToken(elementId);

                                if (index + 1 < chars.Length)
                                {
                                    c = chars[++index];
                                }
                                else
                                {
                                    c = (byte)'\0';
                                }

                                textIndex = index;
                                textEscaped = false;
                                hasText = false;

                                if (tag == Tag.Script || tag == Tag.Style)
                                {
                                    hasText = true;

                                    for (;;)
                                    {
                                        while (!(c == '<' && chars[index + 1] == '/'))
                                        {
                                            c = chars[++index];
                                        }

                                        var s = index + 2;
                                        while (chars[s] <= 0x20)
                                        {
                                            s++;
                                        }

                                        if (ParseTag(chars, ref s) == tag)
                                        {
                                            break;
                                        }

                                        index += 2;
                                        c = chars[index];
                                    }
                                }
                            }
                            break;
                        case (byte)'/':
                            {
                                index += 2;
                                c = chars[index];

                                while (c <= 0x20)
                                {
                                    c = chars[++index];
                                }

                                var tag = ParseTag(chars, ref index);

                                var pt = stack.Count;
                                while (pt > 0)
                                {
                                    if (stack.Buf[--pt].Tag == tag)
                                    {
                                        break;
                                    }
                                }

                                if (pt != 0)
                                {
                                    while (stack.Count > pt + 1)
                                    {
                                        stack.Pop();
                                    }

                                    var state = stack.Pop();
                                    parentId = state.ParentId;
                                    latestId = state.LatestId;
                                }

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
                                textEscaped = false;
                                hasText = false;
                            }
                            break;
                        case (byte)'!':
                            {
                                index += 2;
                                c = chars[index];

                                var innerId = AllocToken(DToken.MakeArray(1));

                                if (c == '-' && chars[index + 1] == '-')
                                {
                                    var commentIndex = index + 2;
                                    while (!(c == '-' && chars[index + 1] == '-' && chars[index + 2] == '>'))
                                    {
                                        c = chars[++index];
                                    }

                                    AllocToken(DToken.Make(DTokenVariant.String,
                                        DToken.EncodeLengthOffsetPayload(index - commentIndex, commentIndex)));
                                    index += 2;
                                }
                                else
                                {
                                    var tokenIndex = index;
                                    while (c > 0x20)
                                    {
                                        c = chars[++index];
                                    }

                                    AllocToken(DToken.Make(DTokenVariant.String,
                                        DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex)));

                                    while (c <= 0x20)
                                    {
                                        c = chars[++index];
                                    }

                                    tokenIndex = index;

                                    var scope = 1;
                                    do
                                    {
                                        if (c == '>')
                                        {
                                            scope--;
                                        }
                                        else
                                        {
                                            if (c == '<')
                                            {
                                                scope++;
                                            }
                                        }

                                        c = chars[++index];
                                    } while (scope > 0);

                                    AllocToken(DToken.Make(DTokenVariant.String,
                                        DToken.EncodeLengthOffsetPayload(index - tokenIndex - 1, tokenIndex)));
                                    IncToken(innerId);
                                }

                                IncToken(parentId);
                                if (latestId != 0)
                                {
                                    LinkToken(latestId, innerId);
                                }

                                latestId = innerId;

                                index++;
                                textIndex = index;
                                textEscaped = false;
                                hasText = false;
                            }
                            break;
                        case (byte)'?':
                            {
                                index += 2;
                                c = chars[index];

                                var innerId = AllocToken(DToken.MakeArray(1));

                                var tokenIndex = index;
                                while (c > 0x20)
                                {
                                    c = chars[++index];
                                }

                                AllocToken(DToken.Make(DTokenVariant.String,
                                    DToken.EncodeLengthOffsetPayload(index - tokenIndex, tokenIndex)));
                                c = chars[++index];

                                IncToken(innerId);

                                tokenIndex = index;
                                while (c != '>')
                                {
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

                                index++;
                                textIndex = index;
                                textEscaped = false;
                                hasText = false;
                            }
                            break;
                    }

                    break;
            }
        }

        return 1;
    }

    private static void CreateNamedCharTable()
    {
        var names = Enum.GetNames<NamedChar>();
        var values = Enum.GetValues<NamedChar>();

        var table = new ValueTuple<long, NamedChar>[names.Length];

        for (var i = 1; i < names.Length; i++)
        {
            long code = 0;

            foreach (var c in names[i])
            {
                code <<= 6;

                if (char.IsLower(c))
                {
                    code |= (long)c - 'a';
                }
                else
                {
                    if (char.IsUpper(c))
                    {
                        code |= (long)(c - 'A') + 26;
                    }
                    else
                    {
                        code |= (long)(c - '1') + 54;
                    }
                }
            }

            table[i] = new ValueTuple<long, NamedChar>(code, values[i]);
        }

        Array.Sort(table, (a, b) => Math.Sign(a.Item1 - b.Item1));

        _namedChars = new NamedChar[table.Length];
        _namedCharsLut = new long[table.Length];

        for (var i = 0; i < table.Length; i++)
        {
            _namedCharsLut[i] = table[i].Item1;
            _namedChars[i] = table[i].Item2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ParseNamedChar(ReadOnlySpan<byte> chars, ref int index)
    {
        long hash = 0;
        var startIndex = index;

        while (index < chars.Length)
        {
            var c = chars[index];

            if (c == ';')
            {
                index++;
                break;
            }

            hash <<= 6;

            if (c >= 'A')
            {
                if (c <= 'Z')
                {
                    hash |= (long)(c - 'A') + 26;
                }
                else
                {
                    if (c >= 'a' && c <= 'z')
                    {
                        hash |= (long)c - 'a';
                    }
                    else
                    {
                        index = startIndex;
                        return (char)0;
                    }
                }
            }
            else
            {
                if (c >= '1' && c <= '4')
                {
                    hash |= (long)(c - '1') + 54;
                }
                else
                {
                    index = startIndex;
                    return (char)0;
                }
            }

            if (index - startIndex >= 10)
            {
                index = startIndex;
                return (char)0;
            }

            index++;
        }

        var charindex = Array.BinarySearch(_namedCharsLut, hash);

        if (charindex < 0)
        {
            return (char)0;
        }

        return (char)_namedChars[charindex];
    }

    private static void CreateTagTable()
    {
        var names = Enum.GetNames<Tag>();
        var values = Enum.GetValues<Tag>();

        var table = new ValueTuple<long, Tag>[names.Length];

        for (var i = 1; i < names.Length; i++)
        {
            long code = 0;

            foreach (var c in names[i])
            {
                code <<= 5;

                if (char.IsDigit(c))
                {
                    code |= (long)c - '1';
                }
                else
                {
                    code |= (long)(char.ToLower(c) - 'a') + 6;
                }
            }

            table[i] = new ValueTuple<long, Tag>(code, values[i]);
        }

        Array.Sort(table, (a, b) => Math.Sign(a.Item1 - b.Item1));

        _tags = new Tag[table.Length];
        _tagsLut = new long[table.Length];

        for (var i = 0; i < table.Length; i++)
        {
            _tagsLut[i] = table[i].Item1;
            _tags[i] = table[i].Item2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsVoidTag(Tag tag)
    {
        return tag > Tag.General && tag <= Tag.Command;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Tag GetTag(ReadOnlySpan<byte> chars)
    {
        var index = 0;
        return ParseTag(chars, ref index);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Tag ParseTag(ReadOnlySpan<byte> chars, ref int index)
    {
        long hash = 0;

        while (index < chars.Length)
        {
            var c = chars[index];

            if (c <= 0x20 || c == '>' || c == '/')
            {
                break;
            }

            hash <<= 5;

            if (c >= 'A')
            {
                if (c <= 'Z')
                {
                    hash |= (long)(c - 'A') + 6;
                }
                else
                {
                    if (c >= 'a' && c <= 'z')
                    {
                        hash |= (long)(c - 'a') + 6;
                    }
                    else
                    {
                        hash = -1;
                    }
                }
            }
            else
            {
                if (c >= '1' && c <= '6')
                {
                    hash |= (long)c - '1';
                }
                else
                {
                    hash = -1;
                }
            }

            index++;
        }

        var tagindex = Array.BinarySearch(_tagsLut, hash);

        if (tagindex < 0)
        {
            return Tag.General;
        }

        return _tags[tagindex];
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, uint tokenId, XmlTextType texttype,
        ref HtmlWriteOptions options)
    {
        var value = reader.GetToken(tokenId);

        if (value.IsContainer)
        {
            if (value.Type == DTokenType.Array)
            {
                var values = reader.EnumerateArray(tokenId);

                foreach (var valueId in values)
                {
                    if (reader.GetToken(valueId).Type == DTokenType.Array)
                    {
                        var acount = reader.GetValueCount(valueId);

                        if (acount > 0)
                        {
                            var avalues = reader.EnumerateArray(valueId).GetEnumerator();
                            avalues.MoveNext();

                            var type = reader.GetToken(avalues.Current).Kind;

                            switch (type)
                            {
                                case DTokenKind.String:
                                case DTokenKind.Symbol:
                                    if (acount == 1)
                                    {
                                        //comment
                                        writer.WriteString("<!--"u8);
                                        writer.WriteString(reader.ReadUtf8String(avalues.Current));
                                        writer.WriteString("-->"u8);
                                    }
                                    else
                                    {
                                        if (acount == 2)
                                        {
                                            var etype = reader.ReadUtf8String(avalues.Current);
                                            avalues.MoveNext();
                                            var edata = reader.ReadUtf8String(avalues.Current);

                                            if (Utf8Helper.Equals(etype, HtmlDoctype, true))
                                            {
                                                writer.WriteString("<!"u8);
                                                writer.WriteString(etype);
                                                writer.WriteUtf8Byte((byte)' ');
                                                WriteXmlString(writer, edata, XmlTextType.Raw);
                                                writer.WriteUtf8Byte((byte)'>');
                                                writer.WriteNewLine();
                                            }

                                            if (Utf8Helper.Equals(etype, HtmlCdata, true))
                                            {
                                                writer.WriteString("<!["u8);
                                                writer.WriteString(etype);
                                                writer.WriteUtf8Byte((byte)'[');
                                                writer.WriteString(edata);
                                                writer.WriteString("]]>"u8);
                                                writer.WriteNewLine();
                                            }
                                        }
                                    }

                                    break;
                            }
                        }
                    }
                    else
                    {
                        Write(writer, reader, valueId, texttype, ref options);
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
                        var token = reader.GetToken(attr.Value);

                        if (token.IsContainer)
                        {
                            continue;
                        }

                        writer.WriteUtf8Byte((byte)' ');
                        writer.WriteString(reader.ReadUtf8String(attr.Key));

                        if (token.Kind != DTokenKind.Null)
                        {
                            writer.WriteUtf8Byte((byte)'=');
                            writer.WriteUtf8Byte((byte)'"');
                            Write(writer, reader, attr.Value, XmlTextType.QuotAttribute, ref options);
                            writer.WriteUtf8Byte((byte)'"');
                        }
                    }

                    var tag = GetTag(element);

                    if (IsVoidTag(tag))
                    {
                        writer.WriteUtf8Byte((byte)'>');
                    }
                    else
                    {
                        writer.WriteUtf8Byte((byte)'>');

                        if (tag == Tag.Script || tag == Tag.Style)
                        {
                            Write(writer, reader, pair.Value, XmlTextType.Raw, ref options);
                        }
                        else
                        {
                            Write(writer, reader, pair.Value, XmlTextType.Inner, ref options);
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
                    WriteXmlString(writer, reader.ReadUtf8String(tokenId), texttype);
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
                case DTokenKind.BigNumber:
                    writer.WriteString(reader.ReadBigNumber(tokenId));
                    break;
                case DTokenKind.ByteString:
                    writer.WriteByteString(reader.ReadByteString(tokenId));
                    break;
            }
        }
    }

    private ReadOnlySpan<byte> DecodeEscapedString(ReadOnlySpan<byte> src)
    {
        var dst = new byte[src.Length];
        var length = 0;
        var i = 0;

        while (i < src.Length)
        {
            var c = src[i++];

            if (c == '&')
            {
                var code = ParseNamedChar(src, ref i);

                if (code == '\0')
                {
                    dst[length++] = c;
                }
                else
                {
                    if (code <= 0x7f)
                    {
                        dst[length++] = (byte)code;
                    }
                    else
                    {
                        if (code <= 0x7ff)
                        {
                            dst[length++] = (byte)(((code >> 6) & 0x1f) | 0xc0);
                            dst[length++] = (byte)((code & 0x3f) | 0x80);
                        }
                        else
                        {
                            dst[length++] = (byte)(((code >> 12) & 0xf) | 0xe0);
                            dst[length++] = (byte)(((code >> 6) & 0x3f) | 0x80);
                            dst[length++] = (byte)((code & 0x3f) | 0x80);
                        }
                    }
                }
            }
            else
            {
                dst[length++] = c;
            }
        }

        return dst.AsSpan().Slice(0, length);
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var stringParam = DToken.DecodeStringParamPayload(param.length);

        if (!stringParam.escaped)
        {
            return _source.Span.Slice(param.offset, stringParam.encodedLength);
        }

        return DecodeEscapedString(_source.Span.Slice(param.offset, param.length));
    }

    private enum XmlTextType
    {
        Raw,
        Inner,
        AposAttribute,
        QuotAttribute
    }

    private enum Tag
    {
        General,

        //begin void element

        Area, // image-map hyperlink
        Base, // base URL
        Br, // line break
        Col, // table column
        Embed, // integration point for plugins NEW
        Hr, // thematic break CHANGED
        Img, // image
        Input, // input control CHANGED
        Link, // inter-document relationship metadata
        Meta, // metadata CHANGED
        Param, // initialization parameters for plugins
        Source, // media source NEW
        Track, // supplementary media track NEW
        Wbr, // line-break opportunity NEW

        KeyGen, // key-pair generator/input control NEW
        BaseFont,
        BgSound,
        Frame,
        IsIndex,

        Command, // command NEW

        //end void element

        A, // hyperlink CHANGED
        Abbr, // abbreviation
        Address, // contact information
        Article, // article NEW
        Aside, // tangential content NEW
        Audio, // audio stream NEW
        B, // offset text conventionally styled in bold CHANGED
        Bdi, // BiDi isolate NEW
        Bdo, // BiDi override
        BlockQuote, // block quotation
        Body, // document body
        Button, // button
        Canvas, // canvas for dynamic graphics NEW
        Caption, // table title
        Center,
        Cite, // cited title of a work CHANGED
        Code, // code fragment
        ColGroup, // table column group
        DataList, // predefined options for other controls NEW
        Dd, // description or value
        Del, // deleted text
        Details, // control for additional on-demand information NEW
        Dfn, // defining instance
        Div, // generic flow container
        Dialog, //
        Dl, // description list
        Dt, // term or name
        Em, // emphatic stress
        FieldSet, // set of related form controls
        FigCaption, // figure caption NEW
        Figure, // figure with optional caption NEW
        Footer, // footer NEW
        Form, // user-submittable form
        H1, // heading
        H2, // heading
        H3, // heading
        H4, // heading
        H5, // heading
        H6, // heading
        Head, // document metadata container
        Header, // header NEW
        Hgroup, // heading group NEW
        Html, // root element
        I, // offset text conventionally styled in italic CHANGED
        IFrame, // nested browsing context (inline frame)
        Ins, // inserted text
        Kbd, // user input
        Label, // caption for a form control
        Legend, // title or explanatory caption
        Li, // list item
        Map, // image-map definition
        Main,
        Mark, // marked (highlighted) text NEW
        Menu, // list of commands CHANGED
        Meter, // scalar gauge NEW
        Nav, // group of navigational links NEW
        NoScript, //fallback content for script
        Object, // generic external content
        Ol, // ordered list
        OptGroup, // group of options
        Option, // option
        Output, // result of a calculation in a form NEW
        P, // paragraph
        Pre, // preformatted text
        Progress, // progress indicator NEW
        Picture, //
        Q, // quoted text
        Rp, // ruby parenthesis NEW
        Rt, // ruby text NEW
        Ruby, // ruby annotation NEW
        S, // struck text CHANGED
        Samp, // (sample) output
        Script, // embedded script
        Section, // section NEW
        Select, // option-selection form control
        Small, // small print CHANGED
        Span, // generic span
        Strong, // strong importance
        Style, // style (presentation) information
        Sub, // subscript
        Summary, // summary, caption, or legend for a details control NEW
        Sup, // superscript
        SVG, // svg
        Table, // table
        Tbody, // table row group
        Td, // table cell
        TextArea, // text input area
        TFoot, // table footer row group
        Th, // table header cell
        THead, // table heading group
        Time, // date and/or time NEW
        Title, // document title
        Tr, // table row
        Ul, // unordered list
        Var, // variable or placeholder text
        Video, // video NEW
        U, //Underline
        Template, //Template
        Data, //Data
        Applet, //Applet
        MenuItem //Menu Item
    }

    private enum NamedChar
    {
        Tab = 9, // Tab
        NewLine = 10, // New Line
        quot = 34, // Quotation mark
        amp = 38, // Ampersand
        apos = 39, // apostrophe
        lt = 60, // Less-than
        gt = 62, // Greater than
        nbsp = 160, // Non-breaking space
        iexcl = 161, //	Inverted exclamation mark
        cent = 162, //	Cent
        pound = 163, //	Pound
        curren = 164, //	Currency
        yen = 165, //	Yen
        brvbar = 166, //	Broken vertical bar
        sect = 167, //	Section
        uml = 168, //	Spacing diaeresis
        copy = 169, //	Copyright
        ordf = 170, //	Feminine ordinal indicator
        laquo = 171, //	Opening/Left angle quotation mark
        not = 172, //	Negation
        shy = 173, //	Soft hyphen
        reg = 174, //	Registered trademark
        macr = 175, //	Spacing macron
        deg = 176, //	Degree
        plusmn = 177, //	Plus or minus
        sup2 = 178, //	Superscript 2
        sup3 = 179, //	Superscript 3
        acute = 180, //	Spacing acute
        micro = 181, //	Micro
        para = 182, //	Paragraph
        middot = 183, //	Dot
        cedil = 184, //	Spacing cedilla
        sup1 = 185, //	Superscript 1
        ordm = 186, //	Masculine ordinal indicator
        raquo = 187, //	Closing/Right angle quotation mark
        frac14 = 188, //	Fraction 1/4
        frac12 = 189, //	Fraction 1/2
        frac34 = 190, //	Fraction 3/4
        iquest = 191, //	Inverted question mark
        Agrave = 192, //	Capital a with grave accent
        Aacute = 193, //	Capital a with acute accent
        Acirc = 194, //	Capital a with circumflex accent
        Atilde = 195, //	Capital a with tilde
        Auml = 196, //	Capital a with umlaut
        Aring = 197, //	Capital a with ring
        AElig = 198, //	Capital ae
        Ccedil = 199, //	Capital c with cedilla
        Egrave = 200, //	Capital e with grave accent
        Eacute = 201, //	Capital e with acute accent
        Ecirc = 202, //	Capital e with circumflex accent
        Euml = 203, //	Capital e with umlaut
        Igrave = 204, //	Capital i with grave accent
        Iacute = 205, //	Capital i with accute accent
        Icirc = 206, //	Capital i with circumflex accent
        Iuml = 207, //	Capital i with umlaut
        ETH = 208, //	Capital eth (Icelandic)
        Ntilde = 209, //	Capital n with tilde
        Ograve = 210, //	Capital o with grave accent
        Oacute = 211, //	Capital o with accute accent
        Ocirc = 212, //	Capital o with circumflex accent
        Otilde = 213, //	Capital o with tilde
        Ouml = 214, //	Capital o with umlaut
        times = 215, //	Multiplication
        Oslash = 216, //	Capital o with slash
        Ugrave = 217, //	Capital u with grave accent
        Uacute = 218, //	Capital u with acute accent
        Ucirc = 219, //	Capital u with circumflex accent
        Uuml = 220, //	Capital u with umlaut
        Yacute = 221, //	Capital y with acute accent
        THORN = 222, //	Capital thorn (Icelandic)
        szlig = 223, //	Lowercase sharp s (German)
        agrave = 224, //	Lowercase a with grave accent
        aacute = 225, //	Lowercase a with acute accent
        acirc = 226, //	Lowercase a with circumflex accent
        atilde = 227, //	Lowercase a with tilde
        auml = 228, //	Lowercase a with umlaut
        aring = 229, //	Lowercase a with ring
        aelig = 230, //	Lowercase ae
        ccedil = 231, //	Lowercase c with cedilla
        egrave = 232, //	Lowercase e with grave accent
        eacute = 233, //	Lowercase e with acute accent
        ecirc = 234, //	Lowercase e with circumflex accent
        euml = 235, //	Lowercase e with umlaut
        igrave = 236, //	Lowercase i with grave accent
        iacute = 237, //	Lowercase i with acute accent
        icirc = 238, //	Lowercase i with circumflex accent
        iuml = 239, //	Lowercase i with umlaut
        eth = 240, //	Lowercase eth (Icelandic)
        ntilde = 241, //	Lowercase n with tilde
        ograve = 242, //	Lowercase o with grave accent
        oacute = 243, //	Lowercase o with acute accent
        ocirc = 244, //	Lowercase o with circumflex accent
        otilde = 245, //	Lowercase o with tilde
        ouml = 246, //	Lowercase o with umlaut
        divide = 247, //	Divide
        oslash = 248, //	Lowercase o with slash
        ugrave = 249, //	Lowercase u with grave accent
        uacute = 250, //	Lowercase u with acute accent
        ucirc = 251, //	Lowercase u with circumflex accent
        uuml = 252, //	Lowercase u with umlaut
        yacute = 253, //	Lowercase y with acute accent
        thorn = 254, //	Lowercase thorn (Icelandic)
        yuml = 255, //	Lowercase y with umlaut
        Amacr = 256, //	Latin capital letter a with macron
        amacr = 257, //	Latin small letter a with macron
        Abreve = 258, //	Latin capital letter a with breve
        abreve = 259, //	Latin small letter a with breve
        Aogon = 260, //	Latin capital letter a with ogonek
        aogon = 261, //	Latin small letter a with ogonek
        Cacute = 262, //	Latin capital letter c with acute
        cacute = 263, //	Latin small letter c with acute
        Ccirc = 264, //	Latin capital letter c with circumflex
        ccirc = 265, //	Latin small letter c with circumflex
        Cdot = 266, //	Latin capital letter c with dot above
        cdot = 267, //	Latin small letter c with dot above
        Ccaron = 268, //	Latin capital letter c with caron
        ccaron = 269, //	Latin small letter c with caron
        Dcaron = 270, //	Latin capital letter d with caron
        dcaron = 271, //	Latin small letter d with caron
        Dstrok = 272, //	Latin capital letter d with stroke
        dstrok = 273, //	Latin small letter d with stroke
        Emacr = 274, //	Latin capital letter e with macron
        emacr = 275, //	Latin small letter e with macron
        Ebreve = 276, //	Latin capital letter e with breve
        ebreve = 277, //	Latin small letter e with breve
        Edot = 278, //	Latin capital letter e with dot above
        edot = 279, //	Latin small letter e with dot above
        Eogon = 280, //	Latin capital letter e with ogonek
        eogon = 281, //	Latin small letter e with ogonek
        Ecaron = 282, //	Latin capital letter e with caron
        ecaron = 283, //	Latin small letter e with caron
        Gcirc = 284, //	Latin capital letter g with circumflex
        gcirc = 285, //	Latin small letter g with circumflex
        Gbreve = 286, //	Latin capital letter g with breve
        gbreve = 287, //	Latin small letter g with breve
        Gdot = 288, //	Latin capital letter g with dot above
        gdot = 289, //	Latin small letter g with dot above
        Gcedil = 290, //	Latin capital letter g with cedilla
        gcedil = 291, //	Latin small letter g with cedilla
        Hcirc = 292, //	Latin capital letter h with circumflex
        hcirc = 293, //	Latin small letter h with circumflex
        Hstrok = 294, //	Latin capital letter h with stroke
        hstrok = 295, //	Latin small letter h with stroke
        Itilde = 296, //	Latin capital letter I with tilde
        itilde = 297, //	Latin small letter I with tilde
        Imacr = 298, //	Latin capital letter I with macron
        imacr = 299, //	Latin small letter I with macron
        Ibreve = 300, //	Latin capital letter I with breve
        ibreve = 301, //	Latin small letter I with breve
        Iogon = 302, //	Latin capital letter I with ogonek
        iogon = 303, //	Latin small letter I with ogonek
        Idot = 304, //	Latin capital letter I with dot above
        imath, // &inodot = 305,//	Latin small letter dotless I
        IJlig = 306, //	Latin capital ligature ij
        ijlig = 307, //	Latin small ligature ij
        Jcirc = 308, //	Latin capital letter j with circumflex
        jcirc = 309, //	Latin small letter j with circumflex
        Kcedil = 310, //	Latin capital letter k with cedilla
        kcedil = 311, //	Latin small letter k with cedilla
        kgreen = 312, //	Latin small letter kra
        Lacute = 313, //	Latin capital letter l with acute
        lacute = 314, //	Latin small letter l with acute
        Lcedil = 315, //	Latin capital letter l with cedilla
        lcedil = 316, //	Latin small letter l with cedilla
        Lcaron = 317, //	Latin capital letter l with caron
        lcaron = 318, //	Latin small letter l with caron
        Lmidot = 319, //	Latin capital letter l with middle dot
        lmidot = 320, //	Latin small letter l with middle dot
        Lstrok = 321, //	Latin capital letter l with stroke
        lstrok = 322, //	Latin small letter l with stroke
        Nacute = 323, //	Latin capital letter n with acute
        nacute = 324, //	Latin small letter n with acute
        Ncedil = 325, //	Latin capital letter n with cedilla
        ncedil = 326, //	Latin small letter n with cedilla
        Ncaron = 327, //	Latin capital letter n with caron
        ncaron = 328, //	Latin small letter n with caron
        napos = 329, //	Latin small letter n preceded by apostrophe
        ENG = 330, //	Latin capital letter eng
        eng = 331, //	Latin small letter eng
        Omacr = 332, //	Latin capital letter o with macron
        omacr = 333, //	Latin small letter o with macron
        Obreve = 334, //	Latin capital letter o with breve
        obreve = 335, //	Latin small letter o with breve
        Odblac = 336, //	Latin capital letter o with double acute
        odblac = 337, //	Latin small letter o with double acute
        OElig = 338, //	Uppercase ligature OE
        oelig = 339, //	Lowercase ligature OE
        Racute = 340, //	Latin capital letter r with acute
        racute = 341, //	Latin small letter r with acute
        Rcedil = 342, //	Latin capital letter r with cedilla
        rcedil = 343, //	Latin small letter r with cedilla
        Rcaron = 344, //	Latin capital letter r with caron
        rcaron = 345, //	Latin small letter r with caron
        Sacute = 346, //	Latin capital letter s with acute
        sacute = 347, //	Latin small letter s with acute
        Scirc = 348, //	Latin capital letter s with circumflex
        scirc = 349, //	Latin small letter s with circumflex
        Scedil = 350, //	Latin capital letter s with cedilla
        scedil = 351, //	Latin small letter s with cedilla
        Scaron = 352, //	Uppercase S with caron
        scaron = 353, //	Lowercase S with caron
        Tcedil = 354, //	Latin capital letter t with cedilla
        tcedil = 355, //	Latin small letter t with cedilla
        Tcaron = 356, //	Latin capital letter t with caron
        tcaron = 357, //	Latin small letter t with caron
        Tstrok = 358, //	Latin capital letter t with stroke
        tstrok = 359, //	Latin small letter t with stroke
        Utilde = 360, //	Latin capital letter u with tilde
        utilde = 361, //	Latin small letter u with tilde
        Umacr = 362, //	Latin capital letter u with macron
        umacr = 363, //	Latin small letter u with macron
        Ubreve = 364, //	Latin capital letter u with breve
        ubreve = 365, //	Latin small letter u with breve
        Uring = 366, //	Latin capital letter u with ring above
        uring = 367, //	Latin small letter u with ring above
        Udblac = 368, //	Latin capital letter u with double acute
        udblac = 369, //	Latin small letter u with double acute
        Uogon = 370, //	Latin capital letter u with ogonek
        uogon = 371, //	Latin small letter u with ogonek
        Wcirc = 372, //	Latin capital letter w with circumflex
        wcirc = 373, //	Latin small letter w with circumflex
        Ycirc = 374, //	Latin capital letter y with circumflex
        ycirc = 375, //	Latin small letter y with circumflex
        Yuml = 376, //	Capital Y with diaeres
        fnof = 402, //	Lowercase with hook
        circ = 710, //	Circumflex accent
        tilde = 732, //	Tilde
        Alpha = 913, //	Alpha
        Beta = 914, //	Beta
        Gamma = 915, //	Gamma
        Delta = 916, //	Delta
        Epsilon = 917, //	Epsilon
        Zeta = 918, //	Zeta
        Eta = 919, //	Eta
        Theta = 920, //	Theta
        Iota = 921, //	Iota
        Kappa = 922, //	Kappa
        Lambda = 923, //	Lambda
        Mu = 924, //	Mu
        Nu = 925, //	Nu
        Xi = 926, //	Xi
        Omicron = 927, //	Omicron
        Pi = 928, //	Pi
        Rho = 929, //	Rho
        Sigma = 931, //	Sigma
        Tau = 932, //	Tau
        Upsilon = 933, //	Upsilon
        Phi = 934, //	Phi
        Chi = 935, //	Chi
        Psi = 936, //	Psi
        Omega = 937, //	Omega
        alpha = 945, //	alpha
        beta = 946, //	beta
        gamma = 947, //	gamma
        delta = 948, //	delta
        epsilon = 949, //	epsilon
        zeta = 950, //	zeta
        eta = 951, //	eta
        theta = 952, //	theta
        iota = 953, //	iota
        kappa = 954, //	kappa
        lambda = 955, //	lambda
        mu = 956, //	mu
        nu = 957, //	nu
        xi = 958, //	xi
        omicron = 959, //	omicron
        pi = 960, //	pi
        rho = 961, //	rho
        sigmaf = 962, //	sigmaf
        sigma = 963, //	sigma
        tau = 964, //	tau
        upsilon = 965, //	upsilon
        phi = 966, //	phi
        chi = 967, //	chi
        psi = 968, //	psi
        omega = 969, //	omega
        thetasym = 977, //	Theta symbol
        upsih = 978, //	Upsilon symbol
        piv = 982, //	Pi symbol
        ensp = 8194, //	En space
        emsp = 8195, //	Em space
        thinsp = 8201, //	Thin space
        zwnj = 8204, //	Zero width non-joiner
        zwj = 8205, //	Zero width joiner
        lrm = 8206, //	Left-to-right mark
        rlm = 8207, //	Right-to-left mark
        ndash = 8211, //	En dash
        mdash = 8212, //	Em dash
        lsquo = 8216, //	Left single quotation mark
        rsquo = 8217, //	Right single quotation mark
        sbquo = 8218, //	Single low-9 quotation mark
        ldquo = 8220, //	Left double quotation mark
        rdquo = 8221, //	Right double quotation mark
        bdquo = 8222, //	Double low-9 quotation mark
        dagger = 8224, //	Dagger
        Dagger = 8225, //	Double dagger
        bull = 8226, //	Bullet
        hellip = 8230, //	Horizontal ellipsis
        permil = 8240, //	Per mille
        prime = 8242, //	Minutes (Degrees)
        Prime = 8243, //	Seconds (Degrees)
        lsaquo = 8249, //	Single left angle quotation
        rsaquo = 8250, //	Single right angle quotation
        oline = 8254, //	Overline
        euro = 8364, //	Euro
        trade = 8482, //	Trademark
        larr = 8592, //	Left arrow
        uarr = 8593, //	Up arrow
        rarr = 8594, //	Right arrow
        darr = 8595, //	Down arrow
        harr = 8596, //	Left right arrow
        crarr = 8629, //	Carriage return arrow
        forall = 8704, //	For all
        part = 8706, //	Part
        exist = 8707, //	Exist
        empty = 8709, //	Empty
        nabla = 8711, //	Nabla
        isin = 8712, //	Is in
        notin = 8713, //	Not in
        ni = 8715, //	Ni
        prod = 8719, //	Product
        sum = 8721, //	Sum
        minus = 8722, //	Minus
        lowast = 8727, //	Asterisk (Lowast)
        radic = 8730, //	Square root
        prop = 8733, //	Proportional to
        infin = 8734, //	Infinity
        ang = 8736, //	Angle
        and = 8743, //	And
        or = 8744, //	Or
        cap = 8745, //	Cap
        cup = 8746, //	Cup
        _int = 8747, //	Integral
        there4 = 8756, //	Therefore
        sim = 8764, //	Similar to
        cong = 8773, //	Congurent to
        asymp = 8776, //	Almost equal
        ne = 8800, //	Not equal
        equiv = 8801, //	Equivalent
        le = 8804, //	Less or equal
        ge = 8805, //	Greater or equal
        sub = 8834, //	Subset of
        sup = 8835, //	Superset of
        nsub = 8836, //	Not subset of
        sube = 8838, //	Subset or equal
        supe = 8839, //	Superset or equal
        oplus = 8853, //	Circled plus
        otimes = 8855, //	Circled times
        perp = 8869, //	Perpendicular
        sdot = 8901, //	Dot operator
        lceil = 8968, //	Left ceiling
        rceil = 8969, //	Right ceiling
        lfloor = 8970, //	Left floor
        rfloor = 8971, //	Right floor
        loz = 9674, //	Lozenge
        spades = 9824, //	Spade
        clubs = 9827, //	Club
        hearts = 9829, //	Heart
        diams = 9830 //	Diamond
    }

    private struct State
    {
        public State(uint parentId, uint latestId, Tag tag)
        {
            ParentId = parentId;
            LatestId = latestId;
            Tag = tag;
        }

        public readonly uint ParentId;
        public readonly uint LatestId;
        public readonly Tag Tag;
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat,
            MaxDepthExceeded
        }

        private ErrorCode _errorCode;

        public ParseException(HtmlDocument doc, ErrorCode errorCode, int offset) : base(nameof(HtmlDocument), offset)
        {
            _errorCode = errorCode;
        }
    }
}