// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using REDox.Serialization;

namespace REDox.Toml;

public sealed class TomlDocument : Document
{
    public TomlDocument Duplicate()
    {
        if (_rentedBuffer != null)
        {
            _source = _source.ToArray();

            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }

        return CreateSnapshot<TomlDocument>();
    }

    private TomlDocument(SerializerSettings settings) : base(settings)
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

    public void ClearCollapsed()
    {
        if (_collapsed != null)
        {
            _collapsed.Clear();
            _collapsed = null;
        }
    }

    public void SetCollapsed(string key, bool? collapsed)
    {
        if (_collapsed == null)
        {
            _collapsed = new Dictionary<ulong, bool>();
        }

        var hash = MakeHash(0, Encoding.UTF8.GetBytes(key).AsSpan());

        if (collapsed != null)
        {
            _collapsed.Add(hash, collapsed.Value);
        }
        else
        {
            _collapsed.Remove(hash);
        }
    }

    public bool? GetCollapsed(string key)
    {
        if (_collapsed == null)
        {
            return null;
        }

        var hash = MakeHash(0, Encoding.UTF8.GetBytes(key).AsSpan());

        if (_collapsed.TryGetValue(hash, out var collapsed))
        {
            return collapsed;
        }

        return null;
    }

    public static bool TryParse(ReadOnlySpan<byte> toml, [NotNullWhen(true)] out TomlDocument? document,
        SerializerSettings? settings = null,
        TomlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var buffer = ArrayPool<byte>.Shared.Rent(toml.Length);

        toml.CopyTo(buffer);

        document = new TomlDocument(settings);
        document._rentedBuffer = buffer;
        document.EnsureCapacity(toml.Length / 16);

        var source = buffer.AsMemory(0, toml.Length);

        var error = document.ReadCore(source, options);

        if (error != null)
        {
            document.Dispose();
            document = null;
            return false;
        }

        return true;
    }

    public static TomlDocument Parse(Stream stream, SerializerSettings? settings = null,
        TomlDocumentOptions options = default)
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

    public static TomlDocument Parse(string toml, SerializerSettings? settings = null,
        TomlDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(toml.Length));

        try
        {
            var bytes = Encoding.UTF8.GetBytes(toml, buffer.AsSpan());

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
    ///     The returned document takes ownership of <paramref name="toml" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static TomlDocument Parse(byte[] toml, SerializerSettings? settings = null,
        TomlDocumentOptions options = default)
    {
        return Parse(toml.AsMemory(), settings, options);
    }

    public static TomlDocument Parse(ReadOnlySpan<byte> toml, SerializerSettings? settings = null,
        TomlDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(toml.Length);

        try
        {
            toml.CopyTo(buffer);

            var rentedBuffer = buffer;
            buffer = null;

            return ParseRented(rentedBuffer.AsMemory().Slice(0, toml.Length), rentedBuffer, settings, options);
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
    ///     The returned document takes ownership of <paramref name="toml" /> and references it without copying.
    ///     The caller must not modify the buffer while the document is in use.
    /// </remarks>
    public static TomlDocument Parse(ReadOnlyMemory<byte> toml, SerializerSettings? settings = null,
        TomlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new TomlDocument(settings);

        try
        {
            doc.Read(toml, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static TomlDocument ParseRented(ReadOnlyMemory<byte> toml, byte[] rentedBuffer,
        SerializerSettings? settings = null, TomlDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        var doc = new TomlDocument(settings)
        {
            _rentedBuffer = rentedBuffer
        };

        try
        {
            doc.Read(toml, options);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    public static byte[] Encode(DElement element, TomlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            return writer.Encode();
        }
    }

    public static string EncodeToString(DElement element, TomlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream stream, TomlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(stream, reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            writer.Dispose();
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        TomlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(bufferWriter, reader.Settings, options.TextWriteOptions);
            Write(writer, reader, reader.RootId, options);
            writer.Dispose();
        }
    }

    private void Read(ReadOnlyMemory<byte> bytes, TomlDocumentOptions options)
    {
        Utf8Helper.SkipUtf8Bom(ref bytes);

        EnsureCapacity(bytes.Length / 16);

        var error = ReadCore(bytes, options);

        if (error != null)
        {
            throw error;
        }
    }

    private DocumentParseException? ReadCore(ReadOnlyMemory<byte> bytes, TomlDocumentOptions options)
    {
        _source = bytes;

        try
        {
            ParseToml(bytes.Span, options);
        }
        catch (DocumentParseException e)
        {
            return e;
        }

        RootId = 1;

        if (options.EnableValueValidation)
        {
            foreach (var token in GetTokens())
            {
                var error = ValidateToken(token);

                if (error != null)
                {
                    return error;
                }
            }
        }

        return null;
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, uint rootId, TomlWriteOptions options)
    {
        var stack = new Helper.LocalStack<uint>(stackalloc uint[64], options.MaxDepth);
        var stackPt = 0;

        WriteTable(writer, reader, reader.Document as TomlDocument, rootId, 0, ref stack, stackPt, false, ref options);

        stack.Dispose();
    }

    private (uint parentId, uint latestId, uint valueId) SelectTable(ReadOnlySpan<byte> bytes, uint tableId,
        DToken key,
        ref ulong tableHash)
    {
        var param = DToken.DecodeLengthOffsetPayload(key);
        var name = DecodeUtf8Bytes(key);

        tableHash = MakeHash(tableHash, name);

        var parentId = 0U;
        var latestId = 0U;

        if (GetToken(tableId).Type == DTokenType.Array)
        {
            var count = GetValueCount(tableId);
            var valueId = tableId + 1;

            var i = 0;
            while (i < count)
            {
                if (!GetToken(valueId).IsIgnore)
                {
                    tableId = valueId;
                    i++;
                }

                valueId = NextToken(valueId);
            }
        }

        if (GetToken(tableId).Type == DTokenType.Map)
        {
            var count = GetValueCount(tableId);
            var valueId = tableId + 1;

            var i = 0;
            while (i < count)
            {
                if (!GetToken(valueId).IsIgnore)
                {
                    var keyName = DecodeUtf8Bytes(GetToken(valueId));
                    latestId = valueId;
                    valueId++;

                    if (name.SequenceEqual(keyName))
                    {
                        parentId = valueId;
                        break;
                    }

                    i++;
                }

                valueId = NextToken(valueId);
            }
        }
        else
        {
            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, param.offset);
        }

        if (parentId == 0)
        {
            var count = GetValueCount(tableId);
            var valueId = AllocToken(key);
            parentId = AllocToken(DToken.MakeMap(0));
            AllocToken(default);

            IncToken(tableId);

            if (latestId != 0)
            {
                var latestType = GetToken(latestId + 1);

                if (latestType.IsContainer)
                {
                    LinkToken(latestId + 1, valueId);
                }
                else
                {
                    var nextId = latestId + 2;
                    while (GetToken(nextId).IsIgnore)
                    {
                        nextId = NextToken(nextId);
                    }

                    if (nextId != valueId)
                    {
                        if (GetToken(tableId + 1) != default)
                        {
                            latestId = NextToken(tableId + 1);
                        }

                        LinkToken(parentId, latestId);
                        LinkToken(tableId + 1, valueId);
                    }
                }
            }
            else
            {
                if (count == 0 && tableId + 1 != valueId)
                {
                    LinkToken(tableId + 1, valueId);
                }
            }

            latestId = 0;

            return (parentId, latestId, valueId);
        }
        else
        {
            var parentType = GetToken(parentId);

            if (!parentType.IsContainer)
            {
                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, param.offset);
            }

            latestId = 0;
            var count = GetValueCount(parentId);
            var valueId = parentId + 1;
            var i = 0;

            while (i < count)
            {
                if (!GetToken(valueId).IsIgnore)
                {
                    latestId = valueId;

                    if (parentType.Type == DTokenType.Map)
                    {
                        valueId++;
                    }

                    i++;
                }

                valueId = NextToken(valueId);
            }

            return (parentId, latestId, valueId);
        }
    }

    private enum Lexer
    {
        Comment = '#',
        Tab = '\t',
        Space = ' ',
        CR = '\r',
        LF = '\n',
        BeginInlineTable = '{',
        EndInlineTable = '}',
        BeginTable = '[',
        EndTable = ']',
        String = '"',
        LiteralString = '\'',
        ValueSeparator = ',',
        Dot = '.',
        Eq = '=',
        Number = 0x100,
        Boolean = 0x101,
        KeyAssign = 0x102,
        Key = 0x103,
        StringKey = 0x104,
        LiteralStringKey = 0x105,
        Assign = 0x106,
        DateTime = 0x107,
        HexNumber = 0x108,
        OctNumber = 0x109,
        BinNumber = 0x10a
    }

    private static ulong MakeHash(ulong hash, ReadOnlySpan<byte> key)
    {
        if (hash != 0)
        {
            hash <<= 7;
            hash ^= (byte)'.';
        }

        //TODO 
        foreach (var v in key)
        {
            hash <<= 7;
            hash ^= v;
        }

        return hash;
    }

    private void ParseToml(ReadOnlySpan<byte> bytes, TomlDocumentOptions options)
    {
        using var stack =
            new Helper.LocalStack<(uint pid, uint lid)>(stackalloc (uint pid, uint lid)[32], options.MaxDepth);

        var rootId = AllocToken(DToken.MakeMap(0));
        var tableId = 0U;
        var isArray = false;
        var parentId = rootId;
        var latestId = 0U;
        var currentId = parentId;
        var currentHash = 0UL;
        var tokenId = 0U;
        var key = new DToken();
        var tableHash = 0UL;
        var index = 0;

        var preserveTrivia = options.PreserveTrivia;

        if (preserveTrivia)
        {
            if (_collapsed == null)
            {
                _collapsed = new Dictionary<ulong, bool>();
            }
        }

        var linestart = index;

        while (index < bytes.Length)
        {
            var c = bytes[index];

            switch ((Lexer)c)
            {
                case Lexer.Comment:
                    {
                        var lineend = linestart != index;

                        c = bytes[++index];
                        var offset = index;
                        while (c != '\n')
                        {
                            if (++index >= bytes.Length)
                            {
                                break;
                            }

                            c = bytes[index];
                        }

                        if (preserveTrivia)
                        {
                            if (lineend)
                            {
                                AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                                    DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                            }
                            else
                            {
                                AllocToken(DToken.Make(DTokenVariant.TriviaLineComment,
                                    DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                            }
                        }
                    }
                    break;
                case Lexer.Tab:
                case Lexer.Space:
                case Lexer.CR:
                    {
                        index++;
                        linestart++;
                    }
                    break;
                case Lexer.LF:
                    {
                        if (preserveTrivia && linestart == index)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaWhitespace,
                                DToken.EncodeLengthOffsetPayload(0, index)));
                        }

                        index++;

                        if (GetToken(parentId).Type == DTokenType.Map)
                        {
                            while (stack.Count > 0 && parentId != currentId)
                            {
                                var stackParent = GetToken(stack.Buf[stack.Count - 1].pid);
                                if (stackParent.IsStringEncoded || stackParent.Type == DTokenType.Array)
                                {
                                    break;
                                }

                                (parentId, latestId) = stack.Pop();
                            }
                        }

                        if (stack.Count == 0)
                        {
                            tableHash = currentHash;
                        }

                        linestart = index;
                    }
                    break;
                case Lexer.BeginInlineTable:
                    {
                        index++;
                        if (stack.Count > 0)
                        {
                            if (preserveTrivia && GetToken(parentId).Type == DTokenType.Text)
                            {
                                _collapsed![MakeHash(tableHash, DecodeUtf8Bytes(GetToken(parentId)))] = false;
                            }

                            tokenId = AllocToken(DToken.MakeMap(0));

                            stack.Push((parentId, latestId));
                            parentId = tokenId;
                            latestId = 0;
                        }
                        else
                        {
                            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                        }
                    }
                    break;
                case Lexer.EndInlineTable:
                    {
                        index++;
                        if (stack.Count > 0)
                        {
                            while (!GetToken(stack.Buf[stack.Count - 1].pid).IsStringEncoded &&
                                   GetToken(stack.Buf[stack.Count - 1].pid).Type != DTokenType.Array)
                            {
                                (parentId, latestId) = stack.Pop();
                            }

                            tokenId = parentId;

                            if (tokenId == 0)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            var tokenType = GetToken(tokenId);

                            if (tokenType.Type != DTokenType.Map)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            {
                                (parentId, latestId) = stack.Pop();
                            }

                            goto case Lexer.Assign;
                        }

                        throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                    }
                case Lexer.BeginTable:
                    {
                        index++;
                        if (stack.Count > 0)
                        {
                            if (GetToken(parentId).Type == DTokenType.Text)
                            {
                                if (preserveTrivia)
                                {
                                    var indented = true;
                                    var i = index;
                                    while (i < bytes.Length && bytes[i] != '\n')
                                    {
                                        if (bytes[i] > 0x20)
                                        {
                                            indented = false;
                                            break;
                                        }

                                        i++;
                                    }

                                    if (indented)
                                    {
                                        _collapsed![MakeHash(tableHash, DecodeUtf8Bytes(GetToken(parentId)))] = false;
                                    }
                                }
                            }

                            //begin array
                            tokenId = AllocToken(DToken.MakeArray(0));

                            stack.Push((parentId, latestId));
                            parentId = tokenId;
                            latestId = 0;
                        }
                        else
                        {
                            //begin table
                            tableId = rootId;
                            tableHash = 0;

                            if (bytes[index] == '[')
                            {
                                //table array
                                isArray = true;
                                index++;
                            }
                        }
                    }
                    break;
                case Lexer.EndTable:
                    {
                        index++;
                        if (stack.Count > 0)
                        {
                            //end array
                            tokenId = parentId;

                            if (tokenId == 0)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            var tokenType = GetToken(tokenId);

                            if (tokenType.Type != DTokenType.Array)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            (parentId, latestId) = stack.Pop();

                            goto case Lexer.Assign;
                        }

                        //end table
                        if (tableId == 0)
                        {
                            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                        }

                        if (isArray)
                        {
                            if (bytes[index] != ']')
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            index++;
                            isArray = false;

                            var parentToken = GetToken(parentId);
                            if (parentToken.IsContainer)
                            {
                                if (parentToken.Type == DTokenType.Map)
                                {
                                    if (GetValueCount(parentId) != 0)
                                    {
                                        throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                                    }

                                    SetToken(parentId, DToken.MakeArray(1));
                                    parentId++;
                                    SetToken(parentId, DToken.MakeMap(0));
                                }
                                else
                                {
                                    IncToken(parentId);
                                    parentId = AllocToken(DToken.MakeMap(0));
                                    AllocToken(default);

                                    if (latestId != 0)
                                    {
                                        LinkToken(latestId, parentId);
                                    }

                                    latestId = 0;
                                }
                            }
                            else
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }
                        }

                        if (tableHash != 0 && preserveTrivia)
                        {
                            _collapsed![tableHash] = true;
                        }

                        currentId = parentId;
                        currentHash = tableHash;
                        tableId = 0;
                    }
                    break;
                case Lexer.String:
                    {
                        if (stack.Count == 0 || GetToken(parentId).Type == DTokenType.Map)
                        {
                            goto case Lexer.StringKey;
                        }

                        c = bytes[++index];

                        var offset = index;
                        var multiline = false;

                        if (c == '"' && index + 1 < bytes.Length && bytes[index + 1] == '"')
                        {
                            if (stack.Count == 0)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            multiline = true;
                        }

                        if (multiline)
                        {
                            index += 2;
                            c = bytes[index];
                            offset = index;

                            var first = true;

                            while (!(c == '"' && bytes[index + 1] == '"' && bytes[index + 2] == '"' &&
                                     (index + 3 >= bytes.Length || bytes[index + 3] != '"')))
                            {
                                if (first)
                                {
                                    if (c > 0x20)
                                    {
                                        first = false;
                                    }

                                    if (c == '\n')
                                    {
                                        offset = index + 1;
                                        first = false;
                                    }
                                }

                                if (c == '\\')
                                {
                                    //escape sequence
                                    index++;
                                }

                                c = bytes[++index];
                            }

                            tokenId = AllocToken(DToken.Make(DTokenVariant.StringMultilineDoubleQuote,
                                DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                            index += 3;
                        }
                        else
                        {
                            var escaped = false;

                            while (c != '"')
                            {
                                if (c == '\\')
                                {
                                    //escape sequence
                                    escaped = true;
                                    index++;
                                }

                                c = bytes[++index];
                            }

                            tokenId = AllocToken(DToken.Make(DTokenVariant.String,
                                DToken.EncodeLengthOffsetPayload(DToken.EncodeStringParamPayload(index - offset, escaped),
                                    offset)));
                            index++;
                        }

                        goto case Lexer.Assign;
                    }
                case Lexer.LiteralString:
                    {
                        if (stack.Count == 0 || GetToken(parentId).Type == DTokenType.Map)
                        {
                            goto case Lexer.LiteralStringKey;
                        }

                        c = bytes[++index];

                        var offset = index;
                        var multiline = false;

                        if (c == '\'' && index + 1 < bytes.Length && bytes[index + 1] == '\'')
                        {
                            if (stack.Count == 0)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            multiline = true;
                        }

                        if (multiline)
                        {
                            index += 2;
                            c = bytes[index];
                            offset = index;
                            var first = true;

                            while (!(c == '\'' && bytes[index + 1] == '\'' && bytes[index + 2] == '\'' &&
                                     (index + 3 >= bytes.Length || bytes[index + 3] != '\'')))
                            {
                                if (first)
                                {
                                    if (c > 0x20)
                                    {
                                        first = false;
                                    }

                                    if (c == '\n')
                                    {
                                        offset = index + 1;
                                        first = false;
                                    }
                                }

                                c = bytes[++index];
                            }

                            tokenId = AllocToken(DToken.Make(DTokenVariant.StringMultilineSingleQuote,
                                DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                            index += 3;
                        }
                        else
                        {
                            while (c != '\'')
                            {
                                c = bytes[++index];
                            }

                            tokenId = AllocToken(DToken.Make(DTokenVariant.StringSingleQuote,
                                DToken.EncodeLengthOffsetPayload(index - offset, offset)));
                            index++;
                        }

                        goto case Lexer.Assign;
                    }
                case Lexer.ValueSeparator:
                    {
                        if (stack.Count > 0 && GetToken(parentId).Type == DTokenType.Map)
                        {
                            while (GetToken(stack.Buf[stack.Count - 1].pid).Type != DTokenType.Text &&
                                   GetToken(stack.Buf[stack.Count - 1].pid).Type != DTokenType.Array)
                            {
                                (parentId, latestId) = stack.Pop();
                            }
                        }

                        index++;
                    }
                    break;
                case Lexer.Dot:
                    {
                        index++;
                        if (tableId != 0)
                        {
                            tableId = parentId;
                        }
                        else
                        {
                            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                        }
                    }
                    break;
                case Lexer.Eq:
                    {
                        index++;
                    }
                    break;
                case Lexer.HexNumber:
                    {
                        var offset = index;
                        index += 2;

                        while (index < bytes.Length)
                        {
                            c = bytes[index];
                            if (!(char.IsDigit((char)c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') ||
                                  c == '_'))
                            {
                                break;
                            }

                            index++;
                        }

                        tokenId = AllocToken(DToken.Make(DTokenVariant.IntegerHexadecimal,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));

                        goto case Lexer.Assign;
                    }
                case Lexer.OctNumber:
                    {
                        var offset = index;
                        index += 2;

                        while (index < bytes.Length)
                        {
                            c = bytes[index];
                            if (!((c >= '0' && c <= '7') || c == '_'))
                            {
                                break;
                            }

                            index++;
                        }

                        tokenId = AllocToken(DToken.Make(DTokenVariant.IntegerOctal,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));

                        goto case Lexer.Assign;
                    }
                case Lexer.BinNumber:
                    {
                        var offset = index;
                        index += 2;

                        while (index < bytes.Length)
                        {
                            c = bytes[index];
                            if (!((c >= '0' && c <= '1') || c == '_'))
                            {
                                break;
                            }

                            index++;
                        }

                        tokenId = AllocToken(DToken.Make(DTokenVariant.IntegerBinary,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));

                        goto case Lexer.Assign;
                    }
                case Lexer.Number:
                    {
                        var offset = index;
                        var tokenType = DTokenVariant.Integer;

                        if (c == '-' || c == '+')
                        {
                            c = bytes[++index];
                        }

                        if (c == 'n' || c == 'i')
                        {
                            tokenType = DTokenVariant.Float;
                            index += 3;
                        }
                        else
                        {
                            for (;;)
                            {
                                if ((c < '0' || c > '9') && c != '_')
                                {
                                    if (c == 'e' || c == 'E' || c == '.' || c == '+' || c == '-')
                                    {
                                        tokenType = DTokenVariant.Float;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }

                                if (++index >= bytes.Length)
                                {
                                    break;
                                }

                                c = bytes[index];
                            }
                        }

                        tokenId = AllocToken(DToken.Make(tokenType,
                            DToken.EncodeLengthOffsetPayload(index - offset, offset)));

                        goto case Lexer.Assign;
                    }
                case Lexer.DateTime:
                    {
                        var offset = index;
                        var type = DTokenVariant.TimestampLocalDate;
                        var hasDate = index + 4 < bytes.Length && bytes[index + 4] == '-';
                        var hasTime = !hasDate || (index + 10 < bytes.Length &&
                                                   (bytes[index + 10] == 'T' || bytes[index + 10] == 't' ||
                                                    bytes[index + 10] == ' '));
                        var hasOffset = false;

                        if (!hasDate)
                        {
                            type = DTokenVariant.TimestampLocalTime;
                        }

                        while (index < bytes.Length)
                        {
                            c = bytes[index];
                            if (c == ',' || c == ']' || c == '}' || c == '#' || c == '\r' || c == '\n')
                            {
                                break;
                            }

                            if (hasDate && hasTime)
                            {
                                if (c == 'z' || c == 'Z')
                                {
                                    hasOffset = true;
                                }
                                else if ((c == '-' || c == '+') && index > offset + 10)
                                {
                                    hasOffset = true;
                                }
                            }

                            index++;
                        }

                        while (index > offset && (bytes[index - 1] == ' ' || bytes[index - 1] == '\t'))
                        {
                            index--;
                        }

                        if (hasDate && hasTime)
                        {
                            type = hasOffset
                                ? DTokenVariant.TimestampOffsetDateTime
                                : DTokenVariant.TimestampLocalDateTime;
                        }

                        tokenId = AllocToken(DToken.Make(type, DToken.EncodeLengthOffsetPayload(index - offset, offset)));

                        goto case Lexer.Assign;
                    }
                case Lexer.Boolean:
                    {
                        if (c == 't')
                        {
                            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanTrue,
                                DToken.EncodeLengthOffsetPayload(0, index)));
                            index += 4;
                        }
                        else
                        {
                            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanFalse,
                                DToken.EncodeLengthOffsetPayload(0, index)));
                            index += 5;
                        }

                        goto case Lexer.Assign;
                    }
                case Lexer.Key:
                    {
                        var offset = index;
                        var length = 1;

                        c = bytes[++index];
                        while (char.IsLetterOrDigit((char)c) || c == '_' || c == '-')
                        {
                            if (++index >= bytes.Length)
                            {
                                break;
                            }

                            c = bytes[index];
                            length++;
                        }

                        key = DToken.Make(DTokenVariant.String, DToken.EncodeLengthOffsetPayload(length, offset));

                        goto case Lexer.KeyAssign;
                    }
                case Lexer.StringKey:
                    {
                        c = bytes[++index];

                        var offset = index;

                        while (c != '"')
                        {
                            if (c == '\\')
                            {
                                //escape sequence
                                index++;
                            }

                            c = bytes[++index];
                        }

                        key = DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(DToken.EncodeStringParamPayload(index - offset, true),
                                offset));

                        c = ++index < bytes.Length ? bytes[index] : (byte)'\n';

                        goto case Lexer.KeyAssign;
                    }
                case Lexer.LiteralStringKey:
                    {
                        c = bytes[++index];

                        var offset = index;
                        var length = 0;

                        while (c != '\'')
                        {
                            c = bytes[++index];
                            length++;
                        }

                        c = ++index < bytes.Length ? bytes[index] : (byte)'\n';

                        key = DToken.Make(DTokenVariant.StringSingleQuote,
                            DToken.EncodeLengthOffsetPayload(length, offset));

                        goto case Lexer.KeyAssign;
                    }
                case Lexer.Assign:
                    {
                        var parentType = GetToken(parentId);

                        if (parentType.IsContainer)
                        {
                            if (parentType.Type == DTokenType.Map)
                            {
                                IncToken(parentId);

                                if (latestId != 0)
                                {
                                    var latestType = GetToken(latestId + 1);

                                    if (latestType.IsContainer)
                                    {
                                        LinkToken(latestId + 1, tokenId);
                                    }
                                    else
                                    {
                                        var nextId = latestId + 2;
                                        while (GetToken(nextId).IsIgnore)
                                        {
                                            nextId = NextToken(nextId);
                                        }

                                        if (nextId != tokenId)
                                        {
                                            if (GetToken(parentId + 1) == default)
                                            {
                                                nextId = NextToken(parentId + 1);
                                                if (nextId == 0)
                                                {
                                                    nextId = parentId + 2;
                                                }

                                                LinkToken(parentId + 1, tokenId);
                                                LinkToken(AllocToken(default), nextId);
                                            }
                                            else
                                            {
                                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat,
                                                    index);
                                            }
                                        }
                                    }
                                }

                                latestId = tokenId;

                                stack.Push((parentId, latestId));
                                parentId = tokenId;
                                latestId = 0;
                            }
                            else
                            {
                                IncToken(parentId);

                                if (latestId != 0)
                                {
                                    LinkToken(latestId, tokenId);
                                }

                                latestId = tokenId;
                            }
                        }
                        else
                        {
                            switch (parentType.Kind)
                            {
                                case DTokenKind.String:
                                case DTokenKind.Symbol:
                                    {
                                        (parentId, latestId) = stack.Pop();
                                    }
                                    break;
                                default:
                                    throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }
                        }
                    }
                    break;
                case Lexer.KeyAssign:
                    {
                        if (tableId != 0)
                        {
                            var result = SelectTable(bytes, tableId, key, ref tableHash);
                            latestId = result.latestId;
                            parentId = result.parentId;
                        }
                        else
                        {
                            while (c <= 0x20 && index + 1 < bytes.Length)
                            {
                                c = bytes[++index];
                            }

                            if (c == '.')
                            {
                                index++;
                                var result = SelectTable(bytes, parentId, key, ref tableHash);

                                stack.Push((parentId, result.valueId));

                                latestId = result.latestId;
                                parentId = result.parentId;
                                break;
                            }

                            tokenId = AllocToken(key);

                            goto case Lexer.Assign;
                        }
                    }
                    break;
                default:
                    {
                        if (stack.Count > 0)
                        {
                            if (GetToken(parentId).Type == DTokenType.Map)
                            {
                                //inline table key
                                if (char.IsLetterOrDigit((char)c) || c == '_' || c == '-')
                                {
                                    goto case Lexer.Key;
                                }

                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            //value
                            if (c == 't' || c == 'f')
                            {
                                goto case Lexer.Boolean;
                            }

                            if (c == '-' || c == '+' || c == 'n' || c == 'i')
                            {
                                goto case Lexer.Number;
                            }

                            if (c == '0' && index + 2 < bytes.Length)
                            {
                                var t = bytes[index + 1];
                                if (t == 'x')
                                {
                                    goto case Lexer.HexNumber;
                                }

                                if (t == 'o')
                                {
                                    goto case Lexer.OctNumber;
                                }

                                if (t == 'b')
                                {
                                    goto case Lexer.BinNumber;
                                }
                            }

                            if (char.IsDigit((char)c))
                            {
                                if ((index + 4 < bytes.Length && bytes[index + 2] == ':') ||
                                    (index + 7 < bytes.Length && bytes[index + 4] == '-' && bytes[index + 7] == '-'))
                                {
                                    goto case Lexer.DateTime;
                                }

                                goto case Lexer.Number;
                            }

                            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                        }

                        //key
                        if (char.IsLetterOrDigit((char)c) || c == '_' || c == '-')
                        {
                            goto case Lexer.Key;
                        }

                        throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                    }
            }
        }

        if (GetToken(parentId).Type == DTokenType.Map)
        {
            while (stack.Count > 0 && parentId != currentId)
            {
                (parentId, latestId) = stack.Pop();
            }
        }

        if (stack.Count > 0)
        {
            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
        }
    }

    private static uint WriteValue(Utf8TextWriter writer, in DataReader reader, TomlDocument? doc, uint valueId,
        bool indented,
        ref Helper.LocalStack<uint> stack, ref TomlWriteOptions options)
    {
        var token = reader.GetToken(valueId);
        var valueType = token.Kind;
        var endcommentId = 0U;

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                var count = reader.GetValueCount(valueId);
                var i = 0;

                writer.WriteUtf8Byte((byte)'[');

                if (indented)
                {
                    writer.PushIndent();
                    writer.WriteNewLine();
                }
                else
                {
                    writer.WriteSpaceIfIndented();
                }

                foreach (var elementId in reader.EnumerateArray(valueId))
                {
                    if (reader.GetToken(elementId).Kind == DTokenKind.Null)
                    {
                        i++;
                        continue;
                    }

                    writer.WriteIndent();

                    {
                        var commentId = WriteValue(writer, reader, doc, elementId, false, ref stack, ref options);
                        if (commentId != 0)
                        {
                            endcommentId = commentId;
                        }
                    }

                    if (i + 1 < count)
                    {
                        writer.WriteUtf8Byte((byte)',');
                    }
                    else
                    {
                        if (indented)
                        {
                            writer.PopIndent();
                            writer.WriteNewLine();
                        }
                        else
                        {
                            writer.WriteSpaceIfIndented();
                        }
                    }

                    i++;
                }

                writer.WriteUtf8Byte((byte)']');
            }
            else
            {
                writer.WriteUtf8Byte((byte)'{');
                writer.WriteSpaceIfIndented();
                WriteTable(writer, reader, doc, valueId, 0, ref stack, 0, true, ref options);
                writer.WriteSpaceIfIndented();
                writer.WriteUtf8Byte((byte)'}');
            }
        }
        else
        {
            switch (valueType)
            {
                case DTokenKind.Integer:
                    if (doc != null && doc.GetRawData(valueId, out var intdata))
                    {
                        writer.WriteString(intdata);
                    }
                    else
                    {
                        var value = reader.ReadInt64(valueId);

                        switch (reader.GetToken(valueId).Variant)
                        {
                            case DTokenVariant.IntegerHexadecimal:
                                {
                                    var buf = writer.BeginWriteUtf8Bytes(64);
                                    Utf8Helper.TryFormatHexadecimal(value, buf, out var bytesWritten);
                                    writer.EndWriteUtf8Bytes(bytesWritten);
                                }
                                break;
                            case DTokenVariant.IntegerOctal:
                                {
                                    var buf = writer.BeginWriteUtf8Bytes(64);
                                    Utf8Helper.TryFormatOctalString(value, buf, out var bytesWritten);
                                    writer.EndWriteUtf8Bytes(bytesWritten);
                                }
                                break;
                            case DTokenVariant.IntegerBinary:
                                {
                                    var buf = writer.BeginWriteUtf8Bytes(80);
                                    Utf8Helper.TryFormatBinaryString(value, buf, out var bytesWritten);
                                    writer.EndWriteUtf8Bytes(bytesWritten);
                                }
                                break;
                            default:
                                writer.WriteInt64(value);
                                break;
                        }
                    }

                    break;
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(valueId));
                    break;
                case DTokenKind.Float:
                    if (doc != null && doc.GetRawData(valueId, out var floatdata))
                    {
                        writer.WriteString(floatdata);
                    }
                    else
                    {
                        if (token.Variant == DTokenVariant.FloatDecimal)
                        {
                            writer.WriteDecimal(reader.ReadDecimal(valueId));
                        }
                        else
                        {
                            writer.WriteDouble(reader.ReadDouble(valueId));
                        }
                    }

                    break;
                case DTokenKind.Boolean:
                    writer.WriteBoolean(reader.ReadBoolean(valueId));
                    break;
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    if (doc != null && doc.GetRawData(valueId, out var strdata))
                    {
                        switch (reader.GetToken(valueId).Variant)
                        {
                            case DTokenVariant.StringMultilineDoubleQuote:
                                writer.WriteString("\"\"\""u8);
                                writer.WriteString(strdata);
                                writer.WriteString("\"\"\""u8);
                                break;
                            case DTokenVariant.StringSingleQuote:
                                writer.WriteUtf8Byte((byte)'\'');
                                writer.WriteString(strdata);
                                writer.WriteUtf8Byte((byte)'\'');
                                break;
                            case DTokenVariant.StringMultilineSingleQuote:
                                writer.WriteString("\"\"\""u8);
                                writer.WriteString(strdata);
                                writer.WriteString("\"\"\""u8);
                                break;
                            default:
                                writer.WriteUtf8Byte((byte)'"');
                                writer.WriteString(strdata);
                                writer.WriteUtf8Byte((byte)'"');
                                break;
                        }
                    }
                    else
                    {
                        WriteTomlString(writer, reader.GetToken(valueId).StringKind,
                            reader.ReadUtf8String(valueId));
                    }

                    break;
                case DTokenKind.Timestamp:
                    if (doc != null && doc.GetRawData(valueId, out var datedata))
                    {
                        writer.WriteString(datedata);
                    }
                    else
                    {
                        writer.WriteString(reader.ReadDateTime(valueId).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
                            CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return endcommentId;
    }

    private static void WriteTable(Utf8TextWriter writer, in DataReader reader, TomlDocument? doc, uint parentId,
        ulong baseHash,
        ref Helper.LocalStack<uint> stack, int collapsePt, bool inline, ref TomlWriteOptions options)
    {
        var separator = false;

        foreach (var kv in reader.EnumerateMap(parentId))
        {
            if (reader.GetToken(kv.Value).Kind == DTokenKind.Null)
            {
                continue;
            }

            var keyName = reader.ReadUtf8String(kv.Key);
            var valueId = kv.Value;

            var valueType = reader.GetToken(valueId);
            var indented = false;

            if (valueType.IsContainer && !inline)
            {
                var hash = MakeHash(baseHash, keyName);
                var collapsed = false;

                if (doc?._collapsed == null || !doc._collapsed.TryGetValue(hash, out collapsed))
                {
                    var collapseLevel = options.CollapseLevel;
                    var keepcollapsed = options.PreserveTrivia;

                    var isTableArray = valueType.Type == DTokenType.Array &&
                                       IsArrayOfTables(reader, valueId);

                    if (keepcollapsed || stack.Count >= collapseLevel)
                    {
                        var expandLevel = options.InlineTableLevel;

                        if ((keepcollapsed || stack.Count != expandLevel) && valueType.Type == DTokenType.Map)
                        {
                            stack.Push(kv.Key);
                            WriteTable(writer, reader, doc, valueId, hash, ref stack, collapsePt, inline, ref options);
                            stack.Pop();
                            continue;
                        }

                        if (isTableArray)
                        {
                            collapsed = true;
                        }
                    }
                    else
                    {
                        if (valueType.Type == DTokenType.Map || isTableArray)
                        {
                            collapsed = true;
                        }
                    }
                }
                else
                {
                    if (valueType.Type == DTokenType.Array && !collapsed)
                    {
                        indented = true;
                    }
                }

                if (collapsed)
                {
                    if (valueType.Type == DTokenType.Map)
                    {
                        writer.WriteIndent();
                        writer.WriteUtf8Byte((byte)'[');
                        for (var j = 0; j < stack.Count; j++)
                        {
                            WriteTomlPropertyName(writer, reader.GetToken(stack.Buf[j]).StringKind,
                                reader.ReadUtf8String(stack.Buf[j]));
                            writer.WriteUtf8Byte((byte)'.');
                        }

                        WriteTomlPropertyName(writer, reader.GetToken(kv.Key).StringKind, keyName);
                        writer.WriteUtf8Byte((byte)']');

                        writer.WriteNewLine();

                        stack.Push(kv.Key);
                        WriteTable(writer, reader, doc, valueId, hash, ref stack, stack.Count, inline, ref options);
                        stack.Pop();
                    }
                    else
                    {
                        foreach (var elementId in reader.EnumerateArray(valueId))
                        {
                            if (reader.GetToken(elementId).Type != DTokenType.Map)
                            {
                                continue;
                            }

                            writer.WriteIndent();
                            writer.WriteString("[["u8);
                            for (var j = 0; j < stack.Count; j++)
                            {
                                WriteTomlPropertyName(writer, reader.GetToken(stack.Buf[j]).StringKind,
                                    reader.ReadUtf8String(stack.Buf[j]));
                                writer.WriteUtf8Byte((byte)'.');
                            }

                            WriteTomlPropertyName(writer, reader.GetToken(kv.Key).StringKind, keyName);
                            writer.WriteString("]]"u8);
                            writer.WriteNewLine();

                            writer.PushIndent();
                            stack.Push(kv.Key);
                            WriteTable(writer, reader, doc, elementId, hash, ref stack, stack.Count, inline,
                                ref options);
                            stack.Pop();
                            writer.PopIndent();
                        }
                    }

                    continue;
                }
            }

            if (inline)
            {
                if (separator)
                {
                    writer.WriteUtf8Byte((byte)',');
                    writer.WriteSpaceIfIndented();
                }

                separator = true;
            }
            else
            {
                writer.WriteIndent();
                for (var i = collapsePt; i < stack.Count; i++)
                {
                    WriteTomlPropertyName(writer, reader.GetToken(stack.Buf[i]).StringKind,
                        reader.ReadUtf8String(stack.Buf[i]));
                    writer.WriteUtf8Byte((byte)'.');
                }
            }

            WriteTomlPropertyName(writer, reader.GetToken(kv.Key).StringKind,
                reader.ReadUtf8String(kv.Key));

            writer.WriteSpaceIfIndented();
            writer.WriteUtf8Byte((byte)'=');
            writer.WriteSpaceIfIndented();

            var endcommentId = WriteValue(writer, reader, doc, valueId, indented, ref stack, ref options);

            if (!inline)
            {
                if (endcommentId != 0)
                {
                    var comment = reader.ReadTrivia(endcommentId);

                    if (comment.Length > 0)
                    {
                        writer.WriteUtf8Byte((byte)' ');
                        writer.WriteUtf8Byte((byte)'#');
                        writer.WriteString(comment);
                    }
                }

                writer.WriteNewLine();
            }
        }
    }

    private static void WriteTomlPropertyName(Utf8TextWriter writer, StringKind type, ReadOnlySpan<byte> utf8Bytes)
    {
        if (IsBareKey(utf8Bytes))
        {
            writer.WriteString(utf8Bytes);
            return;
        }

        var buf = writer.BeginWriteUtf8Bytes(utf8Bytes.Length * 2 + 2);
        var pt = 0;

        var c = type == StringKind.SingleQuote ? (byte)'\'' : (byte)'"';

        buf[pt++] = c;
        utf8Bytes.CopyTo(buf.Slice(pt));
        pt += utf8Bytes.Length;
        buf[pt++] = c;

        writer.EndWriteUtf8Bytes(pt);
    }

    private static bool IsBareKey(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.IsEmpty)
        {
            return false;
        }

        foreach (var c in utf8Bytes)
        {
            var isBare = (c >= (byte)'A' && c <= (byte)'Z') ||
                         (c >= (byte)'a' && c <= (byte)'z') ||
                         (c >= (byte)'0' && c <= (byte)'9') ||
                         c == (byte)'_' || c == (byte)'-';

            if (!isBare)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsArrayOfTables(in DataReader reader, uint arrayId)
    {
        var any = false;

        foreach (var elementId in reader.EnumerateArray(arrayId))
        {
            var token = reader.GetToken(elementId);

            if (token.Kind == DTokenKind.Null)
            {
                continue;
            }

            if (token.Type != DTokenType.Map)
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    private static void WriteTomlString(Utf8TextWriter writer, StringKind type, ReadOnlySpan<byte> utf8Bytes)
    {
        var buf = writer.BeginWriteUtf8Bytes(utf8Bytes.Length * 2 + 6);
        var pt = 0;

        switch (type)
        {
            case StringKind.SingleQuote:
                buf[pt++] = (byte)'\'';
                break;
            case StringKind.MultilineSingleQuote:
                buf[pt++] = (byte)'\'';
                buf[pt++] = (byte)'\'';
                buf[pt++] = (byte)'\'';
                break;
            case StringKind.MultilineDoubleQuote:
                buf[pt++] = (byte)'"';
                buf[pt++] = (byte)'"';
                buf[pt++] = (byte)'"';
                break;
            default:
                buf[pt++] = (byte)'"';
                break;
        }

        if (type == StringKind.MultilineDoubleQuote || type == StringKind.MultilineSingleQuote)
        {
            foreach (var c in utf8Bytes)
            {
                if (c == '\n')
                {
                    buf[pt++] = (byte)'\r';
                    buf[pt++] = (byte)'\n';
                    break;
                }
            }
        }

        if (type == StringKind.Default || type == StringKind.MultilineDoubleQuote)
        {
            foreach (var c in utf8Bytes)
            {
                switch (c)
                {
                    case 0x22:
                        buf[pt++] = (byte)'\\';
                        buf[pt++] = (byte)'"';
                        break;
                    case 0x5c:
                        buf[pt++] = (byte)'\\';
                        buf[pt++] = (byte)'\\';
                        break;
                    case 0x08:
                        buf[pt++] = (byte)'\\';
                        buf[pt++] = (byte)'b';
                        break;
                    case 0x0a:
                        if (type == StringKind.MultilineDoubleQuote)
                        {
                            buf[pt++] = c;
                        }
                        else
                        {
                            buf[pt++] = (byte)'\\';
                            buf[pt++] = (byte)'n';
                        }

                        break;
                    case 0x0c:
                        buf[pt++] = (byte)'\\';
                        buf[pt++] = (byte)'f';
                        break;
                    case 0x0d:
                        if (type == StringKind.MultilineDoubleQuote)
                        {
                            buf[pt++] = c;
                        }
                        else
                        {
                            buf[pt++] = (byte)'\\';
                            buf[pt++] = (byte)'r';
                        }

                        break;
                    case 0x00:
                    case 0x01:
                    case 0x02:
                    case 0x03:
                    case 0x04:
                    case 0x05:
                    case 0x06:
                    case 0x07:
                    case 0x0b:
                    case 0x0e:
                    case 0x0f:
                    case 0x10:
                    case 0x11:
                    case 0x12:
                    case 0x13:
                    case 0x14:
                    case 0x15:
                    case 0x16:
                    case 0x17:
                    case 0x18:
                    case 0x1a:
                    case 0x1b:
                    case 0x1c:
                    case 0x1d:
                    case 0x1e:
                    case 0x1f:
                    case 0x7f:
                        buf[pt++] = (byte)'\\';
                        buf[pt++] = (byte)'u';
                        buf[pt++] = (byte)'0';
                        buf[pt++] = (byte)'0';
                        if (c < 0x10)
                        {
                            buf[pt++] = (byte)'0';
                        }
                        else
                        {
                            if (c == 0x7f)
                            {
                                buf[pt++] = (byte)'7';
                            }
                            else
                            {
                                buf[pt++] = (byte)'1';
                            }
                        }

                        if ((c & 0xf) < 10)
                        {
                            buf[pt++] = (byte)((c & 0xf) + '0');
                        }
                        else
                        {
                            buf[pt++] = (byte)((c & 0xf) - 10 + 'A');
                        }

                        break;
                    default:
                        buf[pt++] = c;
                        break;
                }
            }
        }
        else
        {
            foreach (var c in utf8Bytes)
            {
                buf[pt++] = c;
            }
        }

        switch (type)
        {
            case StringKind.SingleQuote:
                buf[pt++] = (byte)'\'';
                break;
            case StringKind.MultilineSingleQuote:
                buf[pt++] = (byte)'\'';
                buf[pt++] = (byte)'\'';
                buf[pt++] = (byte)'\'';
                break;
            case StringKind.MultilineDoubleQuote:
                buf[pt++] = (byte)'"';
                buf[pt++] = (byte)'"';
                buf[pt++] = (byte)'"';
                break;
            default:
                buf[pt++] = (byte)'"';
                break;
        }

        writer.EndWriteUtf8Bytes(pt);
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        if (span[0] == 'n' || ((span[0] == '-' || span[0] == '+') && span[1] == 'n'))
        {
            return double.NaN;
        }

        if (span[0] == 'i' || (span[0] == '+' && span[1] == 'i'))
        {
            return double.PositiveInfinity;
        }

        if (span[0] == '-' && span[1] == 'i')
        {
            return double.NegativeInfinity;
        }

        return double.Parse(Encoding.UTF8.GetString(span).Replace("_", ""), NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    protected override long DecodeInteger(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        var type = token.Variant;
        var span = _source.Span.Slice(param.offset, param.length);
        var index = 0;
        var minus = false;
        var v = 0L;

        if (span[index] == '-')
        {
            minus = true;
            index++;
        }
        else
        {
            if (span[index] == '+')
            {
                index++;
            }
        }

        switch (type)
        {
            case DTokenVariant.IntegerHexadecimal:
                index += 2;
                while (index < span.Length)
                {
                    var c = span[index++];

                    if (c == '_')
                    {
                        continue;
                    }

                    v *= 16;
                    if (c >= 'a' && c <= 'f')
                    {
                        v += c - 'a' + 10;
                    }
                    else
                    {
                        if (c >= 'A' && c <= 'F')
                        {
                            v += c - 'A' + 10;
                        }
                        else
                        {
                            v += c - '0';
                        }
                    }
                }

                break;
            case DTokenVariant.IntegerOctal:
                index += 2;
                while (index < span.Length)
                {
                    var c = span[index++];

                    if (c == '_')
                    {
                        continue;
                    }

                    v *= 8;
                    v += c - '0';
                }

                break;
            case DTokenVariant.IntegerBinary:
                index += 2;
                while (index < span.Length)
                {
                    var c = span[index++];

                    if (c == '_')
                    {
                        continue;
                    }

                    v *= 2;
                    v += c - '0';
                }

                break;
            default:
                while (index < span.Length)
                {
                    var c = span[index++];

                    if (c == '_')
                    {
                        continue;
                    }

                    v *= 10;
                    v += c - '0';
                }

                break;
        }

        return minus ? -v : v;
    }

    private int EncodeUtf8(Span<byte> dst, int src)
    {
        if (src <= 0x7f)
        {
            dst[0] = (byte)src;
            return 1;
        }

        if (src <= 0x7ff)
        {
            dst[0] = (byte)(((src >> 6) & 0x1f) | 0xc0);
            dst[1] = (byte)((src & 0x3f) | 0x80);
            return 2;
        }

        if (src <= 0xffff)
        {
            dst[0] = (byte)(((src >> 12) & 0xf) | 0xe0);
            dst[1] = (byte)(((src >> 6) & 0x3f) | 0x80);
            dst[2] = (byte)((src & 0x3f) | 0x80);
            return 3;
        }

        dst[0] = (byte)(((src >> 18) & 0x7) | 0xf0);
        dst[1] = (byte)(((src >> 12) & 0x3f) | 0x80);
        dst[2] = (byte)(((src >> 6) & 0x3f) | 0x80);
        dst[3] = (byte)((src & 0x3f) | 0x80);

        return 4;
    }

    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        return DecodeDateTime(token);
    }

    protected override DateTime DecodeDateTime(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        return DateTime.Parse(Encoding.UTF8.GetString(span), CultureInfo.InvariantCulture);
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        return _source.Span.Slice(param.offset, param.length);
    }

    private ReadOnlySpan<byte> DecodeEscapedString(ReadOnlySpan<byte> src, bool trim)
    {
        var dst = new byte[src.Length];

        var i = 0;
        var length = 0;
        while (i < src.Length)
        {
            var c = src[i++];

            if (c == '\\')
            {
                c = src[i++];

                switch (c)
                {
                    case (byte)'u':
                        {
                            var uc = 0;
                            for (var j = 0; j < 4; j++)
                            {
                                uc *= 16;

                                c = src[i++];
                                if (char.IsDigit((char)c))
                                {
                                    uc += c - '0';
                                }
                                else
                                {
                                    if (char.IsUpper((char)c))
                                    {
                                        uc += c - 'A' + 10;
                                    }
                                    else
                                    {
                                        uc += c - 'a' + 10;
                                    }
                                }
                            }

                            length += EncodeUtf8(dst.AsSpan().Slice(length), uc);
                        }
                        break;
                    case (byte)'U':
                        {
                            var uc = 0;
                            for (var j = 0; j < 8; j++)
                            {
                                uc *= 16;

                                c = src[i++];
                                if (char.IsDigit((char)c))
                                {
                                    uc += c - '0';
                                }
                                else
                                {
                                    if (char.IsUpper((char)c))
                                    {
                                        uc += c - 'A' + 10;
                                    }
                                    else
                                    {
                                        uc += c - 'a' + 10;
                                    }
                                }
                            }

                            length += EncodeUtf8(dst.AsSpan().Slice(length), uc);
                        }
                        break;
                    case (byte)'x':
                        {
                            byte value = 0;
                            for (var j = 0; j < 2; j++)
                            {
                                value *= 16;

                                c = src[i++];
                                if (char.IsDigit((char)c))
                                {
                                    value += (byte)(c - '0');
                                }
                                else
                                {
                                    if (char.IsUpper((char)c))
                                    {
                                        value += (byte)(c - 'A' + 10);
                                    }
                                    else
                                    {
                                        value += (byte)(c - 'a' + 10);
                                    }
                                }
                            }

                            dst[length++] = value;
                        }
                        break;
                    case (byte)'"':
                        dst[length++] = (byte)'"';
                        break;
                    case (byte)'\\':
                        dst[length++] = (byte)'\\';
                        break;
                    case (byte)'b':
                        dst[length++] = (byte)'\b';
                        break;
                    case (byte)'f':
                        dst[length++] = (byte)'\f';
                        break;
                    case (byte)'n':
                        dst[length++] = (byte)'\n';
                        break;
                    case (byte)'r':
                        dst[length++] = (byte)'\r';
                        break;
                    case (byte)'t':
                        dst[length++] = (byte)'\t';
                        break;
                    case (byte)'e':
                        dst[length++] = 0x1b;
                        break;
                    default:
                        if (trim)
                        {
                            while (i < src.Length && src[i] <= 0x20)
                            {
                                i++;
                            }
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

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var stringParam = DToken.DecodeStringParamPayload(param.length);
        var type = token.Variant;

        if (type == DTokenVariant.StringMultilineDoubleQuote || stringParam.escaped)
        {
            return DecodeEscapedString(_source.Span.Slice(param.offset, stringParam.encodedLength),
                type == DTokenVariant.StringMultilineDoubleQuote);
        }

        return _source.Span.Slice(param.offset, stringParam.encodedLength);
    }

    private DocumentParseException? ValidateToken(DToken token)
    {
        if (token.Kind != DTokenKind.String && token.Kind != DTokenKind.Symbol)
        {
            return null;
        }

        var type = token.Variant;

        if (type != DTokenVariant.String && type != DTokenVariant.StringMultilineDoubleQuote)
        {
            // Literal strings are stored verbatim and require no escape validation.
            return null;
        }

        var param = DToken.DecodeLengthOffsetPayload(token);
        var stringParam = DToken.DecodeStringParamPayload(param.length);
        var bytes = _source.Span.Slice(param.offset, stringParam.encodedLength);
        var multiline = type == DTokenVariant.StringMultilineDoubleQuote;

        var i = 0;

        while (i < bytes.Length)
        {
            var c = bytes[i++];

            if (c < 0x20 && c != (byte)'\t')
            {
                if (multiline && (c == (byte)'\n' || c == (byte)'\r'))
                {
                    continue;
                }

                return new ParseException(this, ParseException.ErrorCode.UnescapedStringValue,
                    param.offset + i - 1);
            }

            if (c == (byte)'\\')
            {
                if (i >= bytes.Length)
                {
                    return new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence,
                        param.offset + i);
                }

                c = bytes[i++];

                switch (c)
                {
                    case (byte)'"':
                    case (byte)'\\':
                    case (byte)'b':
                    case (byte)'f':
                    case (byte)'n':
                    case (byte)'r':
                    case (byte)'t':
                    case (byte)'e':
                        break;
                    case (byte)'u':
                    case (byte)'U':
                    case (byte)'x':
                        {
                            var digits = c == (byte)'x' ? 2 : c == (byte)'u' ? 4 : 8;

                            if (i + digits > bytes.Length)
                            {
                                return new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence,
                                    param.offset + i);
                            }

                            for (var j = 0; j < digits; j++)
                            {
                                var h = bytes[i++];

                                if (!Uri.IsHexDigit((char)h))
                                {
                                    return new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence,
                                        param.offset + i - 1);
                                }
                            }
                        }
                        break;
                    default:
                        if (multiline && (c == (byte)' ' || c == (byte)'\t' || c == (byte)'\n' || c == (byte)'\r'))
                        {
                            // Line-ending backslash (trim trailing whitespace) is valid for multiline strings.
                            break;
                        }

                        return new ParseException(this, ParseException.ErrorCode.InvalidEscapeSequence,
                            param.offset + i - 1);
                }
            }
        }

        return null;
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

#if false
		void Validate()
        {
			for(uint i = 1; i < GetTokens().Length; i++)
            {
                switch (GetToken(i).Kind)
                {
					case TokenKind.String:
                        {
							var token = GetToken(i);

							var param = Token.UnpackLengthOffset(token);
							var offset = param.offset;
							var length = param.length;
							var type = token.ExKind;
							var span = _Bytes.Slice(offset, (int)length).Span;
							var quot = 0;

							for(var j = 0; j < span.Length; j++)
                            {
								if (span[j] == 0x7f)
                                {
									throw new ParseException();
                                }
								if (span[j] < 0x20)
								{
									if (!(span[j] == '\r' || span[j] == '\n' || span[j] == '\t'))
									{
										throw new ParseException();
									}
								}

								if (type == ExTokenKind.StringEscaped || type == ExTokenKind.String)
                                {
									if (span[j] == '\r' || span[j] == '\n')
									{
										throw new ParseException();
									}
								}
								if (type == ExTokenKind.StringMultiline)
                                {
									if (span[j] == '"')
									{
										if (++quot >= 3)
										{
											throw new ParseException();
										}
									}
									else
                                    {
										quot = 0;
                                    }
                                }
								if (type == ExTokenKind.StringMultilineLiteral)
								{
									if (span[j] == '\'')
									{
										if (++quot >= 3)
                                        {
											throw new ParseException();
                                        }
									}
									else
									{
										quot = 0;
									}
								}
								if (type == ExTokenKind.StringEscaped || type == ExTokenKind.StringMultiline)
								{
									if (span[j] == '\\' && j + 1 < span.Length)
									{
										switch ((char)span[j + 1])
										{
											case 't':
											case 'n':
											case 'f':
											case 'r':
											case 'e':
											case '"':
											case '\\':
												j++;
												break;
											case 'u':
												j++;
												for(var k = 0; k < 4; k++)
                                                {
													var c = (char)span[++j];
													if (!(char.IsDigit(c) || (c >= 'a' && c<= 'f') || (c >= 'A' && c <= 'F')))
                                                    {
														throw new ParseException();
                                                    }
												}
												break;
											case 'U':
												j++;
												for (var k = 0; k < 8; k++)
												{
													var c = (char)span[++j];
													if (!(char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
													{
														throw new ParseException();
													}
												}
												break;
											case '\r':
											case '\n':
											case ' ':
											case '\t':
												if (type != ExTokenKind.StringMultiline)
												{
													throw new ParseException();
												}
												break;
											default:
												throw new ParseException();
										}
									}
								}
							}
                        }
						break;
                }
            }
        }
#endif

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat,
            UnescapedStringValue,
            InvalidEscapeSequence
        }

        private ErrorCode _errorCode;

        public ParseException(TomlDocument doc, ErrorCode error, int offset) : base(nameof(TomlDocument), offset)
        {
            _errorCode = error;
        }
    }


    private ReadOnlyMemory<byte> _source;
    private byte[]? _rentedBuffer;
    private Dictionary<ulong, bool>? _collapsed;
}