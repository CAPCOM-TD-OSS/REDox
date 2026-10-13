// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization;

namespace REDox.Ini;

/// <summary>Represents an editable INI document backed by UTF-8 data.</summary>
/// <remarks>
///     The root contains global keys and section objects. Names are case-sensitive unless
///     <see cref="SerializerSettings.PropertyNameCaseInsensitive" /> is enabled, and parsed values are text.
///     Dispose the document when finished; its elements are valid only while the document is in use.
/// </remarks>
public sealed class IniDocument : Document
{
    private byte[]? _rentedBuffer;
    private ReadOnlyMemory<byte> _source;
    private uint _tailId;

    private IniDocument(SerializerSettings settings) : base(settings)
    {
    }

    /// <summary>Gets the editable trivia following the document's last key or section.</summary>
    /// <remarks>
    ///     Parsed trailing comments and whitespace are retained when <see cref="IniDocumentOptions.PreserveTrivia" />
    ///     is enabled. Use <see cref="IniWriteOptions.PreserveTrivia" /> to include trailing comments in output.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This document has been disposed.</exception>
    public DTriviaCollection TrailingTrivia
    {
        get
        {
            ObjectDisposedException.ThrowIf(!IsValid, this);
            // Keep the trivia IDs in a list before replacements change their token representation.
            EnsureElementTriviaList(_tailId);
            return new DValue(this, _tailId).LeadingTrivia;
        }
    }

    /// <summary>Creates a snapshot with independent token storage.</summary>
    /// <returns>A document that remains valid after this document is disposed.</returns>
    /// <remarks>
    ///     Rented input is copied. Caller-supplied byte arrays and memory may remain shared and must not be modified
    ///     while either document is in use.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This document has been disposed.</exception>
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

    /// <summary>Releases the document's token storage and any rented input buffer.</summary>
    /// <param name="disposing">Whether the method is called during explicit disposal.</param>
    protected override void Dispose(bool disposing)
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Reads UTF-8 INI data from a stream and parses it into a document.</summary>
    /// <param name="stream">The stream to read from its current position to the end. The stream remains open.</param>
    /// <param name="settings">
    ///     The serializer settings, or <see langword="null" /> to use
    ///     <see cref="SerializerSettings.Default" />.
    /// </param>
    /// <param name="options">The parsing options.</param>
    /// <returns>A document that must be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream" /> is <see langword="null" />.</exception>
    /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
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

    /// <summary>Encodes an INI string as UTF-8 and parses it into a document.</summary>
    /// <param name="ini">The INI text to parse.</param>
    /// <param name="settings">
    ///     The serializer settings, or <see langword="null" /> to use
    ///     <see cref="SerializerSettings.Default" />.
    /// </param>
    /// <param name="options">The parsing options.</param>
    /// <returns>A document that must be disposed when no longer needed.</returns>
    /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
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

    /// <summary>Parses a UTF-8 INI byte array without copying it.</summary>
    /// <param name="ini">The UTF-8 INI data to parse.</param>
    /// <param name="settings">
    ///     The serializer settings, or <see langword="null" /> to use
    ///     <see cref="SerializerSettings.Default" />.
    /// </param>
    /// <param name="options">The parsing options.</param>
    /// <returns>A document that must be disposed when no longer needed.</returns>
    /// <remarks>The caller must not modify the input while this document or a snapshot of it is in use.</remarks>
    /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
    public static IniDocument Parse(byte[] ini, SerializerSettings? settings = null,
        IniDocumentOptions options = default)
    {
        return Parse(ini.AsMemory(), settings, options);
    }

    /// <summary>Copies UTF-8 INI data into document-owned storage and parses it.</summary>
    /// <param name="ini">The UTF-8 INI data to parse.</param>
    /// <param name="settings">
    ///     The serializer settings, or <see langword="null" /> to use
    ///     <see cref="SerializerSettings.Default" />.
    /// </param>
    /// <param name="options">The parsing options.</param>
    /// <returns>A document that must be disposed when no longer needed.</returns>
    /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
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

    /// <summary>Parses UTF-8 INI memory without copying it.</summary>
    /// <param name="ini">The UTF-8 INI data to parse.</param>
    /// <param name="settings">
    ///     The serializer settings, or <see langword="null" /> to use
    ///     <see cref="SerializerSettings.Default" />.
    /// </param>
    /// <param name="options">The parsing options.</param>
    /// <returns>A document that must be disposed when no longer needed.</returns>
    /// <remarks>The caller must keep the input valid and unchanged while this document or a snapshot of it is in use.</remarks>
    /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
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

    /// <summary>Tries to parse UTF-8 INI data, copying it into document-owned storage.</summary>
    /// <param name="ini">The UTF-8 INI data to parse.</param>
    /// <param name="document">The parsed document on success, or <see langword="null" /> on a parsing failure.</param>
    /// <param name="settings">
    ///     The serializer settings, or <see langword="null" /> to use
    ///     <see cref="SerializerSettings.Default" />.
    /// </param>
    /// <param name="options">The parsing options.</param>
    /// <returns><see langword="true" /> if parsing succeeds; otherwise, <see langword="false" />.</returns>
    /// <remarks>The returned document must be disposed when no longer needed.</remarks>
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
            document._tailId = document.AllocToken(DToken.Make(DTokenVariant.Null, 0));
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

    /// <summary>Writes an element as an INI string.</summary>
    /// <param name="element">The root object containing global values and section objects.</param>
    /// <param name="options">The output formatting options.</param>
    /// <returns>The encoded INI text.</returns>
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

    /// <summary>Writes an element as UTF-8 INI data to a stream.</summary>
    /// <param name="element">The root object containing global values and section objects.</param>
    /// <param name="stream">The destination stream, which remains open.</param>
    /// <param name="options">The output formatting options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream" /> is <see langword="null" />.</exception>
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

    /// <summary>Writes an element as UTF-8 INI data to a buffer writer.</summary>
    /// <param name="element">The root object containing global values and section objects.</param>
    /// <param name="bufferWriter">The destination buffer writer, owned by the caller.</param>
    /// <param name="options">The output formatting options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bufferWriter" /> is <see langword="null" />.</exception>
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

    /// <summary>Writes an element as a UTF-8 INI byte array.</summary>
    /// <param name="element">The root object containing global values and section objects.</param>
    /// <param name="options">The output formatting options.</param>
    /// <returns>A new byte array containing the encoded INI data.</returns>
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
        var state = new SeparatorState { CommentStyle = options.CommentStyle };

        WriteIni(writer, in reader, iniDocument, reader.RootId, ref options, ref state);

        if (options.PreserveTrivia && iniDocument != null && iniDocument.RootElement.Equals(reader.RootElement))
        {
            state.AllowWriteComment = true;
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
        _tailId = AllocToken(DToken.Make(DTokenVariant.Null, 0));
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLineEnding(byte value)
    {
        return value is (byte)'\r' or (byte)'\n';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCommentMarker(byte value, IniCommentSyntax syntax)
    {
        return (value == (byte)';' && syntax != IniCommentSyntax.Hash) ||
               (value == (byte)'#' && syntax != IniCommentSyntax.Semicolon);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLineEnd(ReadOnlySpan<byte> bytes, int index)
    {
        var offset = bytes.Slice(index).IndexOfAny((byte)'\r', (byte)'\n');
        return offset < 0 ? bytes.Length : index + offset;
    }

    private static int FindValueEnd(ReadOnlySpan<byte> bytes, int start, bool allowInlineComments, IniCommentSyntax commentSyntax)
    {
        if (!allowInlineComments)
        {
            return FindLineEnd(bytes, start);
        }

        var index = start;
        while (index < bytes.Length)
        {
            var remaining = bytes.Slice(index);
            var offset = commentSyntax switch
            {
                IniCommentSyntax.Semicolon => remaining.IndexOfAny((byte)'\r', (byte)'\n', (byte)';'),
                IniCommentSyntax.Hash => remaining.IndexOfAny((byte)'\r', (byte)'\n', (byte)'#'),
                _ => remaining.IndexOfAny("\r\n;#"u8)
            };
            if (offset < 0)
            {
                return bytes.Length;
            }

            index += offset;
            if (IsLineEnding(bytes[index]) || index == start)
            {
                return index;
            }

            if (bytes[index - 1] <= 0x20)
            {
                return index;
            }

            index++;
        }

        return index;
    }

    private void ConsumeTrivia(ReadOnlySpan<byte> bytes, ref int index, bool preserveTrivia, IniCommentSyntax commentSyntax)
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

            if (IsCommentMarker(c, commentSyntax))
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
        var globalBuffer = options.AllowDuplicateKeys
            ? []
            : stackalloc Helper.LocalUtf8KeySet.Entry[32];
        var sectionBuffer = options.AllowDuplicateKeys
            ? []
            : stackalloc Helper.LocalUtf8KeySet.Entry[32];
        using var globalKeys = new Helper.LocalUtf8KeySet(this, globalBuffer);
        using var sectionKeys = new Helper.LocalUtf8KeySet(this, sectionBuffer);

        var index = 0;
        var nullIndex = bytes.IndexOf((byte)0);

        if (nullIndex >= 0)
        {
            errorCode = ParseException.ErrorCode.InvalidValue;
            index = nullIndex;
            goto parseError;
        }

        while (index < bytes.Length)
        {
            ConsumeTrivia(bytes, ref index, preserveTrivia, options.CommentSyntax);

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

                        var sectionEnd = bytes.Slice(index).IndexOfAny((byte)']', (byte)'\r', (byte)'\n');
                        index = sectionEnd < 0 ? bytes.Length : index + sectionEnd;

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

                        var sectionToken = DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(sectionNameEnd - sectionNameOffset, sectionNameOffset));

                        if (!options.AllowDuplicateKeys)
                        {
                            if (!globalKeys.Add(sectionToken))
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

                        var tokenId = AllocToken(sectionToken);

                        index++;

                        if (preserveTrivia && sectionNameEnd != index)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(index - sectionNameEnd, sectionNameEnd)));
                        }

                        var triviaOffset = index;

                        while (index < bytes.Length && !IsLineEnding(bytes[index]) &&
                               !IsCommentMarker(bytes[index], options.CommentSyntax))
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

                        ConsumeTrivia(bytes, ref index, preserveTrivia, options.CommentSyntax);

                        if (sectionId != 0)
                        {
                            LinkToken(sectionId, tokenId);
                        }

                        IncToken(rootId);

                        parentId = AllocToken(DToken.MakeMap(0));
                        sectionId = parentId;
                        sectionKeys.Clear();
                    }
                    break;
                default:
                    if (c <= 0x20)
                    {
                        ConsumeTrivia(bytes, ref index, preserveTrivia, options.CommentSyntax);
                    }
                    else
                    {
                        var kOffset = index;

                        var keyEnd = bytes.Slice(index).IndexOfAny((byte)'=', (byte)'\r', (byte)'\n');
                        index = keyEnd < 0 ? bytes.Length : index + keyEnd;

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

                        var keyToken = DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(kEnd - kOffset, kOffset));

                        if (!options.AllowDuplicateKeys)
                        {
                            var added = parentId == rootId
                                ? globalKeys.Add(keyToken)
                                : sectionKeys.Add(keyToken);
                            if (!added)
                            {
                                errorCode = ParseException.ErrorCode.DuplicateKey;
                                index = kOffset;
                                goto parseError;
                            }
                        }

                        AllocToken(keyToken);

                        var separatorOffset = kEnd;
                        index++;

                        while (index < bytes.Length && !IsLineEnding(bytes[index]) && bytes[index] <= 0x20)
                        {
                            index++;
                        }

                        var vOffset = index;

                        index = FindValueEnd(bytes, index, options.AllowInlineComments, options.CommentSyntax);

                        var vEnd = TrimEnd(bytes, vOffset, index);

                        if (preserveTrivia)
                        {
                            AllocToken(DToken.Make(DTokenVariant.TriviaSeparator,
                                DToken.EncodeLengthOffsetPayload(vOffset - separatorOffset, separatorOffset)));
                        }

                        AllocToken(DToken.Make(DTokenVariant.String,
                            DToken.EncodeLengthOffsetPayload(vEnd - vOffset, vOffset)));

                        IncToken(parentId);

                        if (preserveTrivia)
                        {
                            index = vEnd;
                        }
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

        var replacedSection = false;
        if (options.PreserveTrivia && document != null && value.IsExtended)
        {
            foreach (var triviaId in reader.EnumerateTrivia(kv.Value))
            {
                var triviaToken = reader.GetToken(triviaId);
                if (!triviaToken.IsExtended && triviaToken.TriviaKind == TriviaKind.Separator &&
                    reader.ReadTrivia(triviaId).IndexOf((byte)']') >= 0)
                {
                    replacedSection = true;
                    break;
                }
            }
        }

        if (options.PreserveTrivia)
        {
            state.AllowWriteComment = true;
            WriteTriviaList(writer, reader, document, kv.Key, ref state);
            state.AllowWriteComment = false;

            if (replacedSection)
            {
                WriteSectionHeaderComments(writer, reader, document!, kv.Value, ref state);
            }
        }

        if (state.NeedNewLine)
        {
            state.NeedNewLine = false;
            writer.WriteNewLine();
        }

        state.NeedCommentTerminator = false;
        writer.WriteString(reader.ReadUtf8String(kv.Key));

        state.NeedKeySeparator = true;
        if (options.PreserveTrivia && !replacedSection)
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

    private static void WriteSectionHeaderComments(Utf8TextWriter writer, in DataReader reader, IniDocument document,
        uint tokenId, ref SeparatorState state)
    {
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var triviaToken = reader.GetToken(triviaId);
            if (triviaToken.TriviaKind is not (TriviaKind.LineComment or TriviaKind.BlockComment or TriviaKind.Default))
            {
                continue;
            }

            WriteComment(writer, reader.ReadTrivia(triviaId),
                GetOutputCommentChar(document, triviaToken, state.CommentStyle), ref state);
            if (state.NeedNewLine)
            {
                writer.WriteNewLine();
            }

            state.NeedNewLine = false;
            state.NeedCommentTerminator = false;
        }
    }

    private static void WriteTriviaList(Utf8TextWriter writer, in DataReader reader, IniDocument? document,
        uint tokenId,
        ref SeparatorState state)
    {
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var triviaToken = reader.GetToken(triviaId);
            var triviaKind = triviaToken.TriviaKind;
            var trivia = reader.ReadTrivia(triviaId);

            if (document != null && !triviaToken.IsExtended)
            {
                // Generated comments must end before retained trivia resumes.
                if (state.NeedCommentTerminator)
                {
                    if (triviaKind != TriviaKind.Whitespace || trivia.IndexOfAny((byte)'\r', (byte)'\n') < 0)
                    {
                        writer.WriteNewLine();
                    }

                    state.NeedNewLine = false;
                    state.NeedCommentTerminator = false;
                }

                // A prefix originally at the start of the file now follows generated global keys.
                if (state.NeedStartSectionSeparator && state.NeedNewLine &&
                    DToken.DecodeLengthOffsetPayload(triviaToken).offset == 0 &&
                    (triviaKind != TriviaKind.Whitespace || trivia.IndexOfAny((byte)'\r', (byte)'\n') < 0))
                {
                    state.NeedNewLine = false;
                    writer.WriteNewLine();
                }

                switch (triviaKind)
                {
                    case TriviaKind.Separator:
                        if (trivia.IndexOf((byte)'=') >= 0)
                        {
                            state.NeedKeySeparator = false;
                        }

                        if (trivia.IndexOf((byte)'[') >= 0)
                        {
                            if (!state.NeedStartSectionSeparator)
                            {
                                continue;
                            }

                            if (state.NeedNewLine)
                            {
                                state.NeedNewLine = false;
                                writer.WriteNewLine();
                            }

                            state.NeedStartSectionSeparator = false;
                        }

                        if (trivia.IndexOf((byte)']') >= 0)
                        {
                            if (!state.NeedEndSectionSeparator)
                            {
                                continue;
                            }

                            state.NeedEndSectionSeparator = false;
                        }

                        break;
                    case TriviaKind.Whitespace:
                        if (trivia.IndexOfAny((byte)'\r', (byte)'\n') >= 0)
                        {
                            state.NeedNewLine = false;
                        }

                        break;
                }

                if (triviaKind == TriviaKind.LineComment)
                {
                    writer.WriteUtf8Byte(GetOutputCommentChar(document, triviaToken, state.CommentStyle));
                    state.NeedNewLine = true;
                }

                writer.WriteString(trivia);
            }
            else
            {
                if (!state.AllowWriteComment)
                {
                    continue;
                }

                if (triviaKind is TriviaKind.LineComment or TriviaKind.BlockComment or TriviaKind.Default)
                {
                    WriteComment(writer, trivia, GetOutputCommentChar(document, triviaToken, state.CommentStyle),
                        ref state);
                }
            }
        }
    }


    private static void WriteContainerComments(Utf8TextWriter writer, in DataReader reader, IniDocument? document,
        uint tokenId, ref SeparatorState state)
    {
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var triviaToken = reader.GetToken(triviaId);
            // Retained INI separators and comments are written with their source formatting.
            if (document != null && !triviaToken.IsExtended)
            {
                continue;
            }

            if (triviaToken.TriviaKind is TriviaKind.LineComment or TriviaKind.BlockComment or TriviaKind.Default)
            {
                WriteComment(writer, reader.ReadTrivia(triviaId),
                    GetOutputCommentChar(document, triviaToken, state.CommentStyle), ref state);
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

        if (options.PreserveTrivia)
        {
            WriteContainerComments(writer, reader, document, tokenId, ref state);
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

            state.NeedCommentTerminator = false;
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

            if (options.PreserveTrivia)
            {
                WriteContainerComments(writer, reader, document, kv.Value, ref state);
            }

            foreach (var kv2 in reader.EnumerateMap(kv.Value))
            {
                WriteIniKeyValue(writer, reader, document, kv2, ref options, ref state);
                state.NeedNewLine = true;
            }
        }
    }

    private static void WriteComment(Utf8TextWriter writer, ReadOnlySpan<byte> comment, byte marker,
        ref SeparatorState state)
    {
        if (state.NeedNewLine)
        {
            writer.WriteNewLine();
        }

        while (true)
        {
            writer.WriteUtf8Byte(marker);
            var lineEnd = comment.IndexOfAny((byte)'\r', (byte)'\n');
            if (lineEnd < 0)
            {
                writer.WriteString(comment);
                state.NeedNewLine = true;
                state.NeedCommentTerminator = true;
                return;
            }

            writer.WriteString(comment.Slice(0, lineEnd));
            writer.WriteNewLine();
            var newLineLength = comment[lineEnd] == (byte)'\r' && lineEnd + 1 < comment.Length &&
                                comment[lineEnd + 1] == (byte)'\n'
                ? 2
                : 1;
            comment = comment.Slice(lineEnd + newLineLength);
            if (comment.IsEmpty)
            {
                state.NeedNewLine = false;
                state.NeedCommentTerminator = false;
                return;
            }
        }
    }

    private static byte GetOutputCommentChar(IniDocument? document, DToken token, IniCommentStyle style)
    {
        return style switch
        {
            IniCommentStyle.AlwaysHash => (byte)'#',
            IniCommentStyle.AlwaysSemicolon => (byte)';',
            _ => document != null && !token.IsExtended && token.TriviaKind == TriviaKind.LineComment
                ? document.GetCommentChar(token)
                : style == IniCommentStyle.PreserveOrHash
                    ? (byte)'#'
                    : (byte)';'
        };
    }

    private byte GetCommentChar(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, 1)[0];
    }

    /// <summary>Retrieves the source bytes for a trivia token, excluding a line comment's delimiter.</summary>
    /// <param name="token">The trivia token to decode.</param>
    /// <returns>The corresponding source bytes.</returns>
    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        if (token.TriviaKind == TriviaKind.LineComment)
        {
            return _source.Span.Slice(param.offset + 1, param.length - 1);
        }

        return _source.Span.Slice(param.offset, param.length);
    }

    /// <summary>Retrieves the UTF-8 source bytes referenced by a token.</summary>
    /// <param name="token">The token to decode.</param>
    /// <returns>The corresponding source bytes.</returns>
    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, param.length);
    }

    private ref struct SeparatorState
    {
        public IniCommentStyle CommentStyle;
        public bool NeedNewLine;
        public bool NeedCommentTerminator;
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