// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization;

namespace REDox.Json;

sealed class JsonTextEncoder : ITextEncoder<JsonTextEncoder>
{
    private readonly ulong[]? _escapeBmpMap;
    private readonly bool _escapeNonAscii;
    private readonly byte[] _escapeTable;
    private readonly SearchValues<char>? _escapeValues;
    private readonly bool _upperEscape;

    private JsonTextEncoder(TextEncoderPolicy policy)
    {
        var escape = policy.EscapeMask |
                     TextEscapeMask.DoubleQuote |
                     TextEscapeMask.Backslash |
                     TextEscapeMask.C0Controls;

        _escapeNonAscii = (escape & TextEscapeMask.AllNonAscii) != 0;
        _upperEscape = policy.UpperCaseHexEscapes;

        var tbl = new byte[128];

        for (var j = 0; j < tbl.Length; j++)
        {
            var mask = TextEncoderPolicy.GetEscapeMask((char)j);

            if ((escape & mask) == 0)
            {
                continue;
            }

            switch ((char)j)
            {
                case '"':
                    tbl[j] = (byte)'"';
                    break;
                case '\\':
                    tbl[j] = (byte)'\\';
                    break;
                case '\b':
                    tbl[j] = (byte)'b';
                    break;
                case '\f':
                    tbl[j] = (byte)'f';
                    break;
                case '\n':
                    tbl[j] = (byte)'n';
                    break;
                case '\r':
                    tbl[j] = (byte)'r';
                    break;
                case '\t':
                    tbl[j] = (byte)'t';
                    break;
                case '/':
                    tbl[j] = (byte)'/';
                    break;
                default:
                    tbl[j] = (byte)'u';
                    break;
            }

            if ((policy.UnicodeEscapes & mask) != 0)
            {
                tbl[j] = (byte)'u';
            }
        }

        _escapeTable = tbl;

        if ((escape & TextEscapeMask.AllNonAscii) == 0)
        {
            Span<char> buf = stackalloc char[128];

            var i = 0;
            for (var c = 0; c < buf.Length; c++)
            {
                if ((TextEncoderPolicy.GetEscapeMask((char)c) & escape) != 0)
                {
                    buf[i++] = (char)c;
                }
            }

            _escapeValues = SearchValues.Create(buf.Slice(0, i));
        }

        var escapeRanges = policy.EscapeRanges.Span;

        if (escapeRanges.Length > 0 && (escape & TextEscapeMask.AllNonAsciiBmp) != 0)
        {
            var escapeBmpMap = new ulong[1024];

            foreach (var escapeRange in escapeRanges)
            {
                for (var i = 0; i < escapeRange.Length; i++)
                {
                    var c = (ushort)(escapeRange.FirstCodePoint + i);

                    escapeBmpMap[c / 64] |= 1UL << (c & 63);
                }
            }

            _escapeBmpMap = escapeBmpMap;
        }

        EscapeMask = escape;
    }

    public static JsonTextEncoder Create(
        TextEncoderPolicy policy)
    {
        return new JsonTextEncoder(policy);
    }

    public TextEscapeMask EscapeMask { get; }

    private static int WriteEscapedChar(Span<byte> dest, char c, bool upper)
    {
        dest[0] = (byte)'\\';
        dest[1] = (byte)'u';

        var v0 = (c >> (3 * 4)) & 0xf;
        var v1 = (c >> (2 * 4)) & 0xf;
        var v2 = (c >> (1 * 4)) & 0xf;
        var v3 = (c >> (0 * 4)) & 0xf;

        var uc = upper ? 'A' : 'a';

        dest[2] = (byte)(v0 < 10 ? v0 + '0' : v0 - 10 + uc);
        dest[3] = (byte)(v1 < 10 ? v1 + '0' : v1 - 10 + uc);
        dest[4] = (byte)(v2 < 10 ? v2 + '0' : v2 - 10 + uc);
        dest[5] = (byte)(v3 < 10 ? v3 + '0' : v3 - 10 + uc);

        return 6;
    }

    public int Encode(Span<byte> dest, ReadOnlySpan<byte> utf8Bytes)
    {
        var pt = 0;
        var escapeTable = _escapeTable;
        var len = utf8Bytes.Length;

        dest[pt++] = (byte)'"';

        for (var i = 0; i < len; i++)
        {
            var c = utf8Bytes[i];

            if (c < 0x80)
            {
                var e = escapeTable[c];

                if (e == 0)
                {
                    dest[pt++] = c;
                }
                else
                {
                    pt += WriteEscapedAscii(dest.Slice(pt), c, e);
                }
            }
            else
            {
                pt += WriteNonAscii(dest.Slice(pt), utf8Bytes, ref i);
            }
        }

        dest[pt++] = (byte)'"';

        return pt;
    }

    public int Encode(Span<byte> dest, ReadOnlySpan<byte> utf8Bytes, char quoteChar)
    {
        var pt = 0;
        var escapeTable = _escapeTable;
        var len = utf8Bytes.Length;

        if (quoteChar > 0)
        {
            dest[pt++] = (byte)quoteChar;
        }

        for (var i = 0; i < len; i++)
        {
            var c = utf8Bytes[i];

            if (c < 0x80)
            {
                var e = escapeTable[c];

                if (c == '\'' && quoteChar == '\'')
                {
                    dest[pt] = (byte)'\\';
                    dest[pt + 1] = c;
                    pt += 2;
                }
                else if (e == 0)
                {
                    dest[pt++] = c;
                }
                else
                {
                    pt += WriteEscapedAscii(dest.Slice(pt), c, e);
                }
            }
            else
            {
                pt += WriteNonAscii(dest.Slice(pt), utf8Bytes, ref i);
            }
        }

        if (quoteChar > 0)
        {
            dest[pt++] = (byte)quoteChar;
        }

        return pt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WriteEscapedAscii(Span<byte> dest, byte c, byte e)
    {
        if (e == 'u')
        {
            return WriteEscapedChar(dest, (char)c, _upperEscape);
        }

        dest[0] = (byte)'\\';
        dest[1] = e;
        return 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WriteNonAscii(Span<byte> dest, ReadOnlySpan<byte> utf8Bytes, ref int i)
    {
        if (!_escapeNonAscii)
        {
            dest[0] = utf8Bytes[i];
            return 1;
        }

        var pt = 0;
        var status = Rune.DecodeFromUtf8(utf8Bytes.Slice(i), out var rune, out var bytes);

        if (status != OperationStatus.Done)
        {
            i += bytes - 1;
            return WriteEscapedChar(dest, '\ufffd', _upperEscape);
        }

        var utf32 = rune.Value;

        if (IsEscapedCodePoint(utf32))
        {
            if (utf32 > 0xffff)
            {
                pt += WriteEscapedChar(dest, (char)((utf32 - 0x10000) / 0x400 + 0xd800), _upperEscape);
                pt += WriteEscapedChar(dest.Slice(pt), (char)((utf32 - 0x10000) % 0x400 + 0xdc00),
                    _upperEscape);
            }
            else
            {
                pt += WriteEscapedChar(dest, (char)utf32, _upperEscape);
            }
        }
        else
        {
            utf8Bytes.Slice(i, bytes).CopyTo(dest);
            pt += bytes;
        }

        i += bytes - 1;
        return pt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEscapedCodePoint(int codePoint)
    {
        if (codePoint > 0xffff || char.IsSurrogate((char)codePoint))
        {
            return (EscapeMask & TextEscapeMask.NonBmp) != 0;
        }

        if ((EscapeMask & TextEscapeMask.AllNonAsciiBmp) == 0)
        {
            return false;
        }

        if (_escapeBmpMap != null)
        {
            return (_escapeBmpMap[codePoint / 64] & (1UL << (codePoint & 63))) != 0;
        }

        return true;
    }

    public int Encode(Span<byte> dest, ReadOnlySpan<char> chars)
    {
        var pt = 0;

        dest[pt++] = (byte)'"';

        var escapeValues = _escapeValues;
        var escapeTable = _escapeTable;

        if (escapeValues != null)
        {
            var span = chars;

            for (;;)
            {
                var index = span.IndexOfAny(escapeValues);

                if (index < 0)
                {
                    pt += Encoding.UTF8.GetBytes(span, dest.Slice(pt));
                    break;
                }

                if (index > 0)
                {
                    pt += Encoding.UTF8.GetBytes(span.Slice(0, index), dest.Slice(pt));
                }

                var c = span[index];
                var e = escapeTable[c];
                if (e == 'u')
                {
                    pt += WriteEscapedChar(dest.Slice(pt), c, _upperEscape);
                }
                else
                {
                    dest[pt] = (byte)'\\';
                    dest[pt + 1] = e;
                    pt += 2;
                }

                span = span.Slice(index + 1);
            }
        }
        else
        {
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];

                if (c < 0x80)
                {
                    var e = escapeTable[c];
                    if (e == 0)
                    {
                        dest[pt++] = (byte)c;
                    }
                    else
                    {
                        if (e == 'u')
                        {
                            pt += WriteEscapedChar(dest.Slice(pt), c, _upperEscape);
                        }
                        else
                        {
                            dest[pt] = (byte)'\\';
                            dest[pt + 1] = e;
                            pt += 2;
                        }
                    }
                }
                else
                {
                    if (IsEscapedCodePoint(c))
                    {
                        pt += WriteEscapedChar(dest.Slice(pt), c, _upperEscape);
                    }
                    else
                    {
                        if (c <= 0x7ff)
                        {
                            dest[pt] = (byte)(((c >> 6) & 0x1f) | 0xc0);
                            dest[pt + 1] = (byte)((c & 0x3f) | 0x80);
                            pt += 2;
                        }
                        else
                        {
                            if (char.IsSurrogate(c))
                            {
                                if (!char.IsHighSurrogate(c) || i + 1 >= chars.Length ||
                                    !char.IsLowSurrogate(chars[i + 1]))
                                {
                                    pt += WriteEscapedChar(dest.Slice(pt), '\ufffd', _upperEscape);
                                    continue;
                                }

                                var ch = 0x10000 + (c - 0xD800) * 0x400 + (chars[++i] - 0xDC00);

                                dest[pt] = (byte)((ch >> 18) | 0xf0);
                                dest[pt + 1] = (byte)(((ch >> 12) & 0x3f) | 0x80);
                                dest[pt + 2] = (byte)(((ch >> 6) & 0x3f) | 0x80);
                                dest[pt + 3] = (byte)((ch & 0x3f) | 0x80);
                                pt += 4;
                            }
                            else
                            {
                                dest[pt] = (byte)(((c >> 12) & 0xf) | 0xe0);
                                dest[pt + 1] = (byte)(((c >> 6) & 0x3f) | 0x80);
                                dest[pt + 2] = (byte)((c & 0x3f) | 0x80);
                                pt += 3;
                            }
                        }
                    }
                }
            }
        }

        dest[pt++] = (byte)'"';

        return pt;
    }
}