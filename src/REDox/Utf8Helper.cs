// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using REDox.Serialization;

namespace REDox;

static class Utf8Helper
{
    public const int TimestampBufferSize = 64;
    public static readonly Utf8Symbol ValueTag = new("$value");
    public static readonly Utf8Symbol ValuesTag = new("$values");
    public static readonly Utf8Symbol RefTag = new("$ref");
    public static readonly Utf8Symbol IdTag = new("$id");
    public static readonly Utf8Symbol TypeTag = new("$type");

    public static readonly Utf8Symbol KeyLiteral = new("Key");
    public static readonly Utf8Symbol ValueLiteral = new("Value");
    public static readonly Utf8Symbol TrueLiteral = new("True");
    public static readonly Utf8Symbol FalseLiteral = new("False");

    private static readonly byte[] MicrosoftDateBegin = "/Date("u8.ToArray();
    private static readonly byte[] MicrosoftDateEnd = ")/"u8.ToArray();

    private static readonly byte[] Utf8Bom = new byte[] { 0xef, 0xbb, 0xbf };

    private static readonly int[] PowersOfTen =
    {
        1000000, 100000, 10000, 1000, 100, 10, 1, -10, -100, -1000, -10000, -100000, -1000000, -10000000, -100000000,
        -1000000000
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBom(DocumentWriter writer)
    {
        writer.WriteBytes(Utf8Bom);
    }

    public static int EncodeBase64(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (Base64.EncodeToUtf8(src, dst, out var bytesConsumed, out var bytesWritten) != OperationStatus.Done)
        {
            throw new InvalidOperationException();
        }

        return bytesWritten;
    }

    public static int EncodeBase64Url(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (Base64Url.EncodeToUtf8(src, dst, out var bytesConsumed, out var bytesWritten) != OperationStatus.Done)
        {
            throw new InvalidOperationException();
        }

        return bytesWritten;
    }

    public static int EncodeBase16(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (!Convert.TryToHexString(src, dst, out var bytesWritten))
        {
            throw new InvalidOperationException();
        }

        return bytesWritten;
    }

    public static int GetEncodedByteStringLength(int byteLength, ByteStringKind kind)
    {
        switch (kind)
        {
            case ByteStringKind.Default:
            case ByteStringKind.Base64:
            case ByteStringKind.Inherit:
                {
                    var length = Base64.GetMaxEncodedToUtf8Length(byteLength);
                    if (length < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(byteLength));
                    }

                    return length;
                }
            case ByteStringKind.Base64Url:
                {
                    var length = Base64Url.GetEncodedLength(byteLength);
                    if (length < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(byteLength));
                    }

                    return length;
                }
            case ByteStringKind.Base16:
                if (byteLength > int.MaxValue / 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(byteLength));
                }

                return byteLength * 2;
            case ByteStringKind.Guid:
                if (byteLength != 16)
                {
                    throw new ArgumentException("GUID binary value must be exactly 16 bytes.", nameof(byteLength));
                }

                return 36;
            case ByteStringKind.Raw:
                return byteLength;
            default:
                throw new ArgumentException($"Unsupported binary kind: {kind}", nameof(kind));
        }
    }

    public static int EncodeByteString(ReadOnlySpan<byte> src, Span<byte> dst, ByteStringKind kind)
    {
        switch (kind)
        {
            case ByteStringKind.Default:
            case ByteStringKind.Base64:
            case ByteStringKind.Inherit:
                return EncodeBase64(src, dst);
            case ByteStringKind.Base64Url:
                return EncodeBase64Url(src, dst);
            case ByteStringKind.Base16:
                return EncodeBase16(src, dst);
            case ByteStringKind.Guid:
                if (src.Length != 16)
                {
                    throw new ArgumentException("GUID binary value must be exactly 16 bytes.", nameof(src));
                }

                if (!Utf8Formatter.TryFormat(new Guid(src, true), dst, out var bytesWritten))
                {
                    throw new InvalidOperationException();
                }

                return bytesWritten;
            case ByteStringKind.Raw:
                src.CopyTo(dst);
                return src.Length;
            default:
                throw new ArgumentException($"Unsupported binary kind: {kind}", nameof(kind));
        }
    }

    public static byte[] EncodeByteString(ReadOnlySpan<byte> src, ByteStringKind kind)
    {
        var result = new byte[GetEncodedByteStringLength(src.Length, kind)];
        var bytesWritten = EncodeByteString(src, result, kind);

        if (bytesWritten == result.Length)
        {
            return result;
        }

        return result.AsSpan(0, bytesWritten).ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, double value, FloatFormatHandling format)
    {
        var pt = 0;
        if (Utf8Formatter.TryFormat(value, buf, out var bytes))
        {
            FormatNumber(buf, ref bytes, format);
            pt += bytes;
        }

        return pt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, int value)
    {
        if (Utf8Formatter.TryFormat(value, buf, out var bytes))
        {
            return bytes;
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, long value)
    {
        if (Utf8Formatter.TryFormat(value, buf, out var bytes))
        {
            return bytes;
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, ulong value)
    {
        if (Utf8Formatter.TryFormat(value, buf, out var bytes))
        {
            return bytes;
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, float value, FloatFormatHandling format)
    {
        var pt = 0;
        if (Utf8Formatter.TryFormat(value, buf, out var bytes))
        {
            FormatNumber(buf, ref bytes, format);
            pt += bytes;
        }

        return pt;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, Half value, FloatFormatHandling format)
    {
        Span<char> chars = stackalloc char[32];

        if (value.TryFormat(
                chars,
                out var charsWritten,
                default,
                CultureInfo.InvariantCulture))
        {
            var bytes = Encoding.UTF8.GetBytes(
                chars[..charsWritten],
                buf);

            FormatNumber(buf, ref bytes, format);
            return bytes;
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodeNumber(Span<byte> buf, decimal value, FloatFormatHandling format)
    {
        if (Utf8Formatter.TryFormat(value, buf, out var bytes))
        {
            FormatNumber(buf, ref bytes, format);
            return bytes;
        }

        return 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteString(DocumentWriter writer, ReadOnlySpan<char> chars)
    {
        var buf = writer.BeginWrite(chars.Length * 4);

        var count = Encoding.UTF8.GetBytes(chars, buf);

        writer.EndWrite(count);
    }

    private static void FormatDecimalPart(Span<byte> buf, ref int bytes)
    {
        for (var i = 0; i < bytes; i++)
        {
            var c = (char)buf[i];
            if (c == 'e' || c == 'E' || c == '.')
            {
                return;
            }
        }

        buf[bytes++] = (byte)'.';
        buf[bytes++] = (byte)'0';
    }

    private static void FormatSpecialFloatNumber(Span<byte> buf, char c, ref int bytes, FloatFormatHandling format)
    {
        switch (format & FloatFormatHandling.SpecialFloatMask)
        {
            case FloatFormatHandling.SpecialFloatAsXmlSymbol:
                if (c == 'y')
                {
                    if (buf[0] == '-')
                    {
                        buf[2] = (byte)'N';
                        buf[3] = (byte)'F';
                        bytes = 4;
                    }
                    else
                    {
                        buf[1] = (byte)'N';
                        buf[2] = (byte)'F';
                        bytes = 3;
                    }
                }

                break;
            case FloatFormatHandling.SpecialFloatAsString:
                for (var i = bytes; i > 0; i--)
                {
                    buf[i] = buf[i - 1];
                }

                buf[0] = (byte)'"';
                buf[bytes + 1] = (byte)'"';
                bytes += 2;
                break;
            case FloatFormatHandling.SpecialFloatAsSymbol:
                break;
            case FloatFormatHandling.SpecialFloatAsDefaultValue:
                buf[0] = (byte)'0';
                buf[1] = (byte)'.';
                buf[2] = (byte)'0';
                bytes = 3;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FormatNumber(Span<byte> buf, ref int bytes, FloatFormatHandling format)
    {
        var c = (char)buf[bytes - 1];

        if (char.IsDigit(c))
        {
            if ((format & FloatFormatHandling.AlwaysIncludeDecimal) != 0)
            {
                FormatDecimalPart(buf, ref bytes);
            }
        }
        else
        {
            FormatSpecialFloatNumber(buf, c, ref bytes, format);
        }
    }

    public static bool TryFormatTimestamp(DateTime value, SerializerSettings settings,
        Span<byte> bytes, out int bytesWritten)
    {
        // DOM timestamp values can bypass the type converter's zone handling.
        value = settings.DateTimeZoneHandling switch
        {
            DateTimeZoneHandling.Local => value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Local)
                : value.ToLocalTime(),
            DateTimeZoneHandling.Utc => value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime(),
            DateTimeZoneHandling.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            _ => value
        };

        return TryFormatTextTimestamp(value, null, settings, bytes, out bytesWritten);
    }

    public static bool TryFormatTimestamp(DateTimeOffset value, SerializerSettings settings,
        Span<byte> bytes, out int bytesWritten)
    {
        return TryFormatTextTimestamp(value.DateTime, value.Offset, settings, bytes, out bytesWritten);
    }

    private static bool TryFormatTextTimestamp(DateTime value, TimeSpan? offset, SerializerSettings settings,
        Span<byte> bytes, out int bytesWritten)
    {
        if (string.IsNullOrEmpty(settings.DateFormatString) &&
            settings.DateFormatHandling == DateFormatHandling.MicrosoftDateFormat &&
            bytes.Length < TimestampBufferSize)
        {
            // A text buffer can fit the value while being too small for JSON's two escape bytes.
            Span<byte> buffer = stackalloc byte[TimestampBufferSize];
            if (!TryFormatTimestamp(value, offset, null, settings.Culture,
                    settings.DateFormatHandling, buffer, out var length) || bytes.Length < length - 2)
            {
                bytesWritten = 0;
                return false;
            }

            buffer.Slice(1, length - 3).CopyTo(bytes);
            bytesWritten = length - 2;
            bytes[bytesWritten - 1] = (byte)'/';
            return true;
        }

        if (!TryFormatTimestamp(value, offset, settings.DateFormatString, settings.Culture,
                settings.DateFormatHandling, bytes, out bytesWritten))
        {
            return false;
        }

        if (string.IsNullOrEmpty(settings.DateFormatString) &&
            settings.DateFormatHandling == DateFormatHandling.MicrosoftDateFormat)
        {
            // The JSON formatter escapes slashes; text consumers need the literal UTF-8 value.
            bytes.Slice(1, bytesWritten - 2).CopyTo(bytes);
            bytesWritten -= 2;
            bytes[bytesWritten - 1] = (byte)'/';
        }

        return true;
    }

    public static bool TryFormatTimestamp(DateTime value, TimestampKind kind, Span<byte> bytes, out int bytesWritten)
    {
        var format = kind switch
        {
            TimestampKind.OffsetDateTime => value.Kind == DateTimeKind.Utc
                ? "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
                : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            TimestampKind.LocalDate => "yyyy-MM-dd",
            TimestampKind.LocalTime => "HH:mm:ss.FFFFFFF",
            _ => "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
        };

        return value.TryFormat(bytes, out bytesWritten, format, CultureInfo.InvariantCulture);
    }

    public static bool TryFormatTimestamp(DateTimeOffset value, Span<byte> bytes, out int bytesWritten)
    {
        return value.TryFormat(bytes, out bytesWritten, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
    }

    public static bool TryFormatTimestamp(in DataReader reader, uint tokenId, Span<byte> bytes, out int bytesWritten)
    {
        // Explicit kinds keep their ISO representation; default timestamps use serializer settings.
        var kind = reader.GetToken(tokenId).TimestampKind;
        return kind switch
        {
            TimestampKind.OffsetDateTime => TryFormatTimestamp(reader.ReadDateTimeOffset(tokenId), bytes, out bytesWritten),
            TimestampKind.Default or TimestampKind.Inherit =>
                TryFormatTimestamp(reader.ReadDateTime(tokenId), reader.Settings, bytes, out bytesWritten),
            _ => TryFormatTimestamp(reader.ReadDateTime(tokenId), kind, bytes, out bytesWritten)
        };
    }

    public static bool TryFormatHexadecimal(long value, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        var negative = value < 0;

        var magnitude = negative
            ? (ulong)-(value + 1) + 1
            : (ulong)value;

        var digitCount = 1;

        for (var v = magnitude; v >= 16; v >>= 4)
        {
            digitCount++;
        }

        var requiredLength =
            (negative ? 1 : 0) +
            2 + // "0x"
            digitCount;

        if (destination.Length < requiredLength)
        {
            return false;
        }

        var pt = 0;

        if (negative)
        {
            destination[pt++] = (byte)'-';
        }

        destination[pt++] = (byte)'0';
        destination[pt++] = (byte)'x';

        var digitEnd = pt + digitCount;
        var index = digitEnd;

        do
        {
            var v = magnitude & 0xf;

            destination[--index] = v < 10
                ? (byte)('0' + v)
                : (byte)('a' + (v - 10));

            magnitude >>= 4;
        } while (index > pt);

        bytesWritten = digitEnd;
        return true;
    }

    public static bool TryFormatOctalString(
        long value,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;

        var negative = value < 0;

        var magnitude = negative
            ? (ulong)-(value + 1) + 1
            : (ulong)value;

        var digitCount = 1;

        for (var v = magnitude; v >= 8; v >>= 3)
        {
            digitCount++;
        }

        var requiredLength =
            (negative ? 1 : 0) +
            2 + // "0o"
            digitCount;

        if (destination.Length < requiredLength)
        {
            return false;
        }

        var pt = 0;

        if (negative)
        {
            destination[pt++] = (byte)'-';
        }

        destination[pt++] = (byte)'0';
        destination[pt++] = (byte)'o';

        var digitEnd = pt + digitCount;
        var index = digitEnd;

        do
        {
            destination[--index] =
                (byte)('0' + (magnitude & 0x7));

            magnitude >>= 3;
        } while (index > pt);

        bytesWritten = digitEnd;
        return true;
    }

    public static bool TryFormatBinaryString(long value, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        var negative = value < 0;

        var magnitude = negative
            ? (ulong)-(value + 1) + 1
            : (ulong)value;

        var digitCount = 1;

        for (var v = magnitude; v >= 2; v >>= 1)
        {
            digitCount++;
        }

        var requiredLength =
            (negative ? 1 : 0) +
            2 + // "0b"
            digitCount;

        if (destination.Length < requiredLength)
        {
            return false;
        }

        var pt = 0;

        if (negative)
        {
            destination[pt++] = (byte)'-';
        }

        destination[pt++] = (byte)'0';
        destination[pt++] = (byte)'b';

        var digitEnd = pt + digitCount;
        var index = digitEnd;

        do
        {
            destination[--index] =
                (byte)('0' + (magnitude & 0x1));

            magnitude >>= 1;
        } while (index > pt);

        bytesWritten = digitEnd;
        return true;
    }

    public static bool TryParseBase64(ReadOnlySpan<byte> utf8Bytes, out ReadOnlySpan<byte> value, out int bytesConsumed)
    {
        var maxLength = Base64.GetMaxDecodedFromUtf8Length(utf8Bytes.Length);
        var buffer = new byte[maxLength];

        var status = Base64.DecodeFromUtf8(utf8Bytes, buffer, out bytesConsumed, out var bytesWritten);

        if (status != OperationStatus.Done)
        {
            value = default;
            return false;
        }

        value = buffer.AsSpan(0, bytesWritten);
        return true;
    }

    public static bool TryParseBase64Url(ReadOnlySpan<byte> utf8Bytes, out ReadOnlySpan<byte> value,
        out int bytesConsumed)
    {
        var maxLength = Base64Url.GetMaxDecodedLength(utf8Bytes.Length);
        var buffer = new byte[maxLength];

        var status = Base64Url.DecodeFromUtf8(utf8Bytes, buffer, out bytesConsumed, out var bytesWritten);

        if (status != OperationStatus.Done)
        {
            value = default;
            return false;
        }

        value = buffer.AsSpan(0, bytesWritten);
        return true;
    }

    public static bool TryParseBase16(ReadOnlySpan<byte> utf8Bytes, out ReadOnlySpan<byte> value, out int bytesConsumed)
    {
        var maxLength = (utf8Bytes.Length + 1) / 2;
        var buffer = new byte[maxLength];

        var status = Convert.FromHexString(utf8Bytes, buffer, out bytesConsumed, out var bytesWritten);

        if (status != OperationStatus.Done)
        {
            value = default;
            return false;
        }

        value = buffer.AsSpan(0, bytesWritten);
        return true;
    }

    public static bool TryParseHexadecimal(ReadOnlySpan<byte> span, out long value)
    {
        value = 0;

        if (span.IsEmpty)
        {
            return false;
        }

        var i = 0;
        var negative = false;

        if (span[i] == '-' || span[i] == '+')
        {
            negative = span[i] == '-';
            i++;

            if (i >= span.Length)
            {
                return false;
            }
        }

        // "0x" + at least one digit
        if (i + 2 >= span.Length ||
            span[i] != '0' ||
            (span[i + 1] != 'x' && span[i + 1] != 'X'))
        {
            return false;
        }

        i += 2;

        // Positive:  0x7FFF_FFFF_FFFF_FFFF
        // Negative: -0x8000_0000_0000_0000
        var limit = negative
            ? 0x8000_0000_0000_0000UL
            : 0x7FFF_FFFF_FFFF_FFFFUL;

        ulong magnitude = 0;

        while (i < span.Length)
        {
            var c = span[i++];
            uint digit;

            if (c >= '0' && c <= '9')
            {
                digit = (uint)(c - '0');
            }
            else if (c >= 'a' && c <= 'f')
            {
                digit = (uint)(c - 'a' + 10);
            }
            else if (c >= 'A' && c <= 'F')
            {
                digit = (uint)(c - 'A' + 10);
            }
            else
            {
                return false;
            }

            // magnitude * 16 + digit > limit
            if (magnitude > (limit - digit) / 16)
            {
                return false;
            }

            magnitude = magnitude * 16 + digit;
        }

        if (negative)
        {
            if (magnitude == 0x8000_0000_0000_0000UL)
            {
                value = long.MinValue;
            }
            else
            {
                value = -(long)magnitude;
            }
        }
        else
        {
            value = (long)magnitude;
        }

        return true;
    }

    public static bool TryFormatTimestamp(DateTime dateTime, TimeSpan? offset, string? dateFormatString,
        CultureInfo? culture, DateFormatHandling format, Span<byte> destination, out int bytesWritten)
    {
        if (dateTime.Kind == DateTimeKind.Local)
        {
            if (dateTime.Ticks >= 621355968000000000L)
            {
                offset = new DateTimeOffset(dateTime).Offset;
            }
            else
            {
                offset = TimeZoneInfo.Local.GetUtcOffset(dateTime);
            }
        }

        if (!string.IsNullOrEmpty(dateFormatString))
        {
            var success = offset == null
                ? dateTime.TryFormat(destination, out bytesWritten, dateFormatString, culture)
                : new DateTimeOffset(dateTime, offset.Value).TryFormat(destination, out bytesWritten, dateFormatString, culture);
            if (success)
            {
                return true;
            }

            // ISO timestamps always fit in the standard 64-byte buffer, even when the custom format does not.
            return TryFormatTimestamp(dateTime, offset, null, CultureInfo.InvariantCulture,
                DateFormatHandling.IsoDateFormat, destination, out bytesWritten);
        }

        if (destination.Length < TimestampBufferSize)
        {
            Span<byte> buffer = stackalloc byte[TimestampBufferSize];
            if (!TryFormatTimestamp(dateTime, offset, null, culture, format, buffer, out var length) ||
                !buffer.Slice(0, length).TryCopyTo(destination))
            {
                bytesWritten = 0;
                return false;
            }

            bytesWritten = length;
            return true;
        }

        if (format == DateFormatHandling.IsoDateFormat)
        {
            var year = dateTime.Year;
            var month = dateTime.Month;
            var day = dateTime.Day;
            var hour = dateTime.Hour;
            var minute = dateTime.Minute;
            var second = dateTime.Second;
            var subtick = dateTime.Ticks % 10000000;
            var pt = 0;

            destination[pt++] = (byte)('0' + year / 1000);
            destination[pt++] = (byte)('0' + year / 100 % 10);
            destination[pt++] = (byte)('0' + year / 10 % 10);
            destination[pt++] = (byte)('0' + year % 10);
            destination[pt++] = (byte)'-';
            destination[pt++] = (byte)('0' + month / 10);
            destination[pt++] = (byte)('0' + month % 10);
            destination[pt++] = (byte)'-';
            destination[pt++] = (byte)('0' + day / 10);
            destination[pt++] = (byte)('0' + day % 10);
            destination[pt++] = (byte)'T';
            destination[pt++] = (byte)('0' + hour / 10);
            destination[pt++] = (byte)('0' + hour % 10);
            destination[pt++] = (byte)':';
            destination[pt++] = (byte)('0' + minute / 10);
            destination[pt++] = (byte)('0' + minute % 10);
            destination[pt++] = (byte)':';
            destination[pt++] = (byte)('0' + second / 10);
            destination[pt++] = (byte)('0' + second % 10);

            if (subtick > 0)
            {
                destination[pt++] = (byte)'.';

                var d = 1000000;
                while (d > 0)
                {
                    destination[pt++] = (byte)('0' + subtick / d % 10);

                    if (subtick % d == 0)
                    {
                        break;
                    }

                    d /= 10;
                }
            }

            if (offset != null)
            {
                hour = Math.Abs(offset.Value.Hours);
                minute = Math.Abs(offset.Value.Minutes);

                destination[pt++] = offset.Value.Ticks < 0 ? (byte)'-' : (byte)'+';
                destination[pt++] = (byte)('0' + hour / 10);
                destination[pt++] = (byte)('0' + hour % 10);
                destination[pt++] = (byte)':';
                destination[pt++] = (byte)('0' + minute / 10);
                destination[pt++] = (byte)('0' + minute % 10);
            }
            else
            {
                if (dateTime.Kind == DateTimeKind.Utc)
                {
                    destination[pt++] = (byte)'Z';
                }
            }

            bytesWritten = pt;
            return true;
        }
        else
        {
            var timeSpan = offset != null ? offset.Value : new TimeSpan();
            var specialTicks = dateTime.Ticks == DateTime.MinValue.Ticks || dateTime.Ticks == DateTime.MaxValue.Ticks;

            if (offset == null && dateTime.Kind == DateTimeKind.Unspecified && !specialTicks)
            {
                if (dateTime.Ticks >= 621355968000000000L)
                {
                    var dt = new DateTimeOffset(dateTime);
                    timeSpan = dt.Offset;
                    dateTime = dt.DateTime;
                }
                else
                {
                    timeSpan = TimeZoneInfo.Local.GetUtcOffset(dateTime);
                }
            }

            var time = (Math.Max(0, dateTime.Ticks - (specialTicks ? 0 : timeSpan.Ticks)) - 621355968000000000L) /
                       10000;
            var pt = 0;

            destination[pt++] = (byte)'\\';
            MicrosoftDateBegin.CopyTo(destination.Slice(pt));
            pt += MicrosoftDateBegin.Length;

            Utf8Formatter.TryFormat(time, destination.Slice(pt), out var bytes);
            pt += bytes;

            if (offset != null || timeSpan.Ticks != 0)
            {
                var hour = Math.Abs(timeSpan.Hours);
                var minute = Math.Abs(timeSpan.Minutes);

                destination[pt++] = timeSpan.Ticks < 0 ? (byte)'-' : (byte)'+';
                destination[pt++] = (byte)('0' + hour / 10);
                destination[pt++] = (byte)('0' + hour % 10);
                destination[pt++] = (byte)('0' + minute / 10);
                destination[pt++] = (byte)('0' + minute % 10);
            }

            MicrosoftDateEnd.CopyTo(destination.Slice(pt));
            pt += MicrosoftDateEnd.Length;
            destination[pt - 1] = (byte)'\\';
            destination[pt++] = (byte)'/';

            bytesWritten = pt;

            return true;
        }
    }

    private static bool TryParseTimeOffset(ReadOnlySpan<byte> source, out long value)
    {
        value = default;

        if (source.IsEmpty)
        {
            return false;
        }

        if (source[0] == '+' || source[0] == '-')
        {
            if (source.Length != 6 || source[3] != ':')
            {
                return false;
            }

            var h0 = source[1] - '0';
            var h1 = source[2] - '0';
            var m0 = source[4] - '0';
            var m1 = source[5] - '0';

            if (h0 < 0 || h0 > 2 || h1 < 0 || h1 > 9 || m0 < 0 || m0 > 5 || m1 < 0 || m1 > 9)
            {
                return false;
            }

            var offsetHour = h0 * 10 + h1;
            var offsetMinute = m0 * 10 + m1;

            const long ticksPerMinute = 600_000_000;
            const long ticksPerHour = 36_000_000_000;
            const long maxOffsetTicks = 504_000_000_000; // 14:00

            var offsetTicks = offsetHour * ticksPerHour + offsetMinute * ticksPerMinute;

            if (offsetTicks > maxOffsetTicks)
            {
                return false;
            }

            if (source[0] == '-')
            {
                offsetTicks = -offsetTicks;
            }

            value = offsetTicks;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParseDate(ReadOnlySpan<byte> source, out int year, out int month, out int day)
    {
        year = default;
        month = default;
        day = default;

        if (source.Length >= 10 && source[4] == '-' && source[7] == '-')
        {
            var y0 = source[0] - '0';
            var y1 = source[1] - '0';
            var y2 = source[2] - '0';
            var y3 = source[3] - '0';
            var m0 = source[5] - '0';
            var m1 = source[6] - '0';
            var d0 = source[8] - '0';
            var d1 = source[9] - '0';

            if (y0 < 0 || y0 > 9 || y1 < 0 || y1 > 9 || y2 < 0 || y2 > 9 || y3 < 0 || y3 > 9 ||
                m0 < 0 || m0 > 1 || m1 < 0 || m1 > 9 ||
                d0 < 0 || d0 > 3 || d1 < 0 || d1 > 9)
            {
                return false;
            }

            year = y0 * 1000 + y1 * 100 + y2 * 10 + y3;
            month = m0 * 10 + m1;
            day = d0 * 10 + d1;

            if (year < 1 || year > 9999 || month < 1 || month > 12)
            {
                return false;
            }

            var maxDay = DateTime.DaysInMonth(year, month);

            if (day < 1 || day > maxDay)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    public static bool TryParseDateTimeOffsetIso8601(ReadOnlySpan<byte> source, out DateTime value,
        out TimeSpan? offset)
    {
        value = default;
        offset = null;

        if (!TryParseDate(source, out var year, out var month, out var day))
        {
            return false;
        }

        if (source.Length >= 19 && source[10] == 'T' && source[13] == ':' && source[16] == ':')
        {
            var h0 = source[11] - '0';
            var h1 = source[12] - '0';
            var m0 = source[14] - '0';
            var m1 = source[15] - '0';
            var s0 = source[17] - '0';
            var s1 = source[18] - '0';

            if (
                h0 < 0 || h0 > 2 || h1 < 0 || h1 > 9 ||
                m0 < 0 || m0 > 5 || m1 < 0 || m1 > 9 ||
                s0 < 0 || s0 > 5 || s1 < 0 || s1 > 9)
            {
                return false;
            }

            var hour = h0 * 10 + h1;
            var minute = m0 * 10 + m1;
            var second = s0 * 10 + s1;

            if (hour > 23)
            {
                return false;
            }

            value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);

            if (source.Length == 19)
            {
                return true;
            }

            source = source.Slice(19);

            if (source.Length == 1)
            {
                if (source[0] == 'Z')
                {
                    value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                    return true;
                }

                return false;
            }

            if (source[0] == '.')
            {
                source = source.Slice(1);

                long ticks;
                if (source[0] >= '0' && source[0] <= '9' &&
                    Utf8Parser.TryParse(source, out ticks, out var bytesConsumed) && bytesConsumed <= 16)
                {
                    var pow = PowersOfTen[bytesConsumed - 1];
                    if (pow > 0)
                    {
                        ticks *= pow;
                    }
                    else
                    {
                        ticks /= -pow;
                    }

                    value = new DateTime(value.Ticks + ticks, DateTimeKind.Unspecified);

                    source = source.Slice(bytesConsumed);
                }
            }
        }
        else
        {
            if (source.Length == 10)
            {
                value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
                return true;
            }

            if (source.Length < 16 || source[10] != 'T' || source[13] != ':')
            {
                return false;
            }

            var h0 = source[11] - '0';
            var h1 = source[12] - '0';
            var m0 = source[14] - '0';
            var m1 = source[15] - '0';

            if (h0 < 0 || h0 > 2 || h1 < 0 || h1 > 9 ||
                m0 < 0 || m0 > 5 || m1 < 0 || m1 > 9)
            {
                return false;
            }

            var hour = h0 * 10 + h1;
            var minute = m0 * 10 + m1;

            if (hour > 23)
            {
                return false;
            }

            source = source.Slice(16);

            value = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        }

        if (source.Length == 0)
        {
            return true;
        }

        if (source.Length == 1)
        {
            if (source[0] == 'Z')
            {
                value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                return true;
            }

            return false;
        }

        if (TryParseTimeOffset(source, out var offsetTicks))
        {
            offset = new TimeSpan(offsetTicks);
            return true;
        }

        return false;
    }

    public static bool TryParseTimestamp(ReadOnlySpan<byte> source, out DateTime value, out TimeSpan? offset,
        string? formatString, bool dateTimeOffset)
    {
        return TryParseTimestamp(source, out value, out offset, formatString, dateTimeOffset,
            CultureInfo.InvariantCulture);
    }

    public static bool TryParseTimestamp(ReadOnlySpan<byte> source, out DateTime value, out TimeSpan? offset,
        string? formatString, bool dateTimeOffset, CultureInfo? culture)
    {
        if (source.IsEmpty)
        {
            value = default;
            offset = null;
            return false;
        }

        if (TryParseDateTimeOffsetIso8601(source, out value, out offset))
        {
            if (offset != null)
            {
                value = new DateTime(value.Ticks - offset.Value.Ticks, DateTimeKind.Utc).ToLocalTime();
            }

            return true;
        }

        if (source.StartsWith(MicrosoftDateBegin))
        {
            var pt = MicrosoftDateBegin.Length;

            if (Utf8Parser.TryParse(source.Slice(pt), out long ticks, out var bytes))
            {
                pt += bytes;

                if (pt + 5 < source.Length)
                {
                    var separator = source[pt];

                    if (separator == '+' || separator == '-')
                    {
                        var hour = (source[pt + 1] - '0') * 10 + source[pt + 2] - '0';
                        var minute = (source[pt + 3] - '0') * 10 + source[pt + 4] - '0';

                        if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                        {
                            offset = new TimeSpan(hour, minute, 0);
                            if (separator == '-')
                            {
                                offset = -offset;
                            }

                            pt += 5;
                        }
                    }
                }

                if (source.Slice(pt).StartsWith(MicrosoftDateEnd))
                {
                    if (dateTimeOffset)
                    {
                        if (offset == null || offset.Value.Ticks == 0)
                        {
                            value = new DateTime(ticks * 10000L + 621355968000000000L, DateTimeKind.Utc);
                        }
                        else
                        {
                            value =
                                new DateTime(ticks * 10000L + 621355968000000000L, DateTimeKind.Utc).ToLocalTime();
                        }
                    }
                    else
                    {
                        // The milliseconds identify a UTC instant, regardless of the encoded offset.
                        var utc = new DateTime(ticks * TimeSpan.TicksPerMillisecond + DateTime.UnixEpoch.Ticks,
                            DateTimeKind.Utc);
                        value = offset.HasValue ? utc.ToLocalTime() : utc;
                    }

                    return true;
                }
            }
        }
        else
        {
            if (source[source.Length - 1] == 'z' &&
                TryParseDateTimeOffsetIso8601(source.Slice(0, source.Length - 1), out value, out offset))
            {
                value = DateTime.SpecifyKind(value, DateTimeKind.Utc);

                if (offset != null)
                {
                    value = new DateTime(value.Ticks - offset.Value.Ticks, DateTimeKind.Utc).ToLocalTime();
                }

                return true;
            }

            var charLength = Encoding.UTF8.GetCharCount(source);

            if (charLength <= 0 || charLength > 256)
            {
                return false;
            }

            Span<char> chars = stackalloc char[charLength];

            var len = Encoding.UTF8.GetChars(source, chars);

            chars = chars.Slice(0, len);

            if (!string.IsNullOrEmpty(formatString))
            {
                if (DateTime.TryParseExact(chars, formatString, culture, DateTimeStyles.RoundtripKind, out value))
                {
                    return true;
                }
            }

            if (dateTimeOffset && DateTimeOffset.TryParse(chars, culture, DateTimeStyles.None, out var parsedOffset))
            {
                // Preserve the parsed offset and its date when the input contains only a time.
                value = parsedOffset.UtcDateTime;
                offset = parsedOffset.Offset;
                return true;
            }

            if (chars.Length >= 6)
            {
                var offsetSpan = chars.Slice(chars.Length - 6);

                if ((offsetSpan[0] == '+' || offsetSpan[0] == '-') && offsetSpan[3] == ':')
                {
                    var h0 = offsetSpan[1] - '0';
                    var h1 = offsetSpan[2] - '0';
                    var m0 = offsetSpan[4] - '0';
                    var m1 = offsetSpan[5] - '0';

                    var offsetHour = h0 * 10 + h1;
                    var offsetMinute = m0 * 10 + m1;

                    const long ticksPerMinute = 600_000_000;
                    const long ticksPerHour = 36_000_000_000;

                    var offsetTicks = offsetHour * ticksPerHour + offsetMinute * ticksPerMinute;

                    if (offsetSpan[0] == '-')
                    {
                        offsetTicks = -offsetTicks;
                    }

                    offset = new TimeSpan(offsetTicks);

                    chars = chars.Slice(0, chars.Length - 6);

                    if (chars.Length > 0 && chars[chars.Length - 1] == 'Z')
                    {
                        return false;
                    }

                    if (chars.Length > 6 && (chars[chars.Length - 6] == '+' || chars[chars.Length - 6] == '-'))
                    {
                        return false;
                    }
                }
            }

            if (DateTime.TryParse(chars, culture, DateTimeStyles.None, out value))
            {
                if (offset != null)
                {
                    value = new DateTime(value.Ticks - offset.Value.Ticks, DateTimeKind.Utc).ToLocalTime();
                }

                return true;
            }
        }

        value = default;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ToUpper(long c)
    {
        var m = c & 0x7f7f7f7f7f7f7f7f;
        var a = m + 0x0505050505050505;
        var b = m + 0x1f1f1f1f1f1f1f1f;
        return c - (long)(((ulong)((a ^ b) & ~c) & 0x8080808080808080) >> 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, bool ignoreCase)
    {
        if (a.Length == b.Length)
        {
            if (ignoreCase)
            {
                var len = a.Length / 8;

                if (len > 0)
                {
                    var al = MemoryMarshal.Cast<byte, long>(a);
                    var bl = MemoryMarshal.Cast<byte, long>(b);

                    for (var i = 0; i < len; i++)
                    {
                        var aa = ToUpper(al[i]);
                        var bb = ToUpper(bl[i]);

                        if (aa != bb)
                        {
                            return aa.CompareTo(bb);
                        }
                    }
                }

                {
                    var ofs = len * 8;
                    long aa = 0;
                    long bb = 0;

                    for (var i = b.Length - 1; i >= ofs; i--)
                    {
                        aa <<= 8;
                        bb <<= 8;

                        aa |= a[i];
                        bb |= b[i];
                    }

                    aa = ToUpper(aa);
                    bb = ToUpper(bb);

                    if (aa != bb)
                    {
                        return aa.CompareTo(bb);
                    }
                }

                return 0;
            }
            else
            {
                var len = a.Length / 8;

                if (len > 0)
                {
                    var al = MemoryMarshal.Cast<byte, long>(a);
                    var bl = MemoryMarshal.Cast<byte, long>(b);

                    for (var i = 0; i < len; i++)
                    {
                        var aa = al[i];
                        var bb = bl[i];

                        if (aa != bb)
                        {
                            return aa.CompareTo(bb);
                        }
                    }
                }

                {
                    var ofs = len * 8;
                    long aa = 0;
                    long bb = 0;

                    for (var i = b.Length - 1; i >= ofs; i--)
                    {
                        aa <<= 8;
                        bb <<= 8;

                        aa |= a[i];
                        bb |= b[i];
                    }

                    if (aa != bb)
                    {
                        return aa.CompareTo(bb);
                    }
                }

                return 0;
            }
        }

        return a.Length - b.Length;
    }

    public static bool Equals(ReadOnlySpan<byte> str1, ReadOnlySpan<char> str2)
    {
        var i1 = 0;
        var i2 = 0;

        while (i1 < str1.Length && i2 < str2.Length)
        {
            int s = str2[i2++];
            int d;

            var c = str1[i1++];

            if (c <= 0x7f)
            {
                d = c & 0x7f;
            }
            else
            {
                if (0xc0 <= c && c <= 0xdf)
                {
                    if (i1 >= str1.Length)
                    {
                        return false;
                    }

                    var c2 = str1[i1++];
                    d = ((c & 0x1f) << 6) | (c2 & 0x3f);
                }
                else
                {
                    if (0xe0 <= c && c <= 0xef)
                    {
                        if (i1 + 2 > str1.Length)
                        {
                            return false;
                        }

                        var c2 = str1[i1++];
                        var c3 = str1[i1++];

                        d = ((c & 0x0f) << 12) | ((c2 & 0x3f) << 6) | (c3 & 0x3f);
                    }
                    else
                    {
                        if (i1 + 3 > str1.Length)
                        {
                            return false;
                        }

                        var c2 = str1[i1++];
                        var c3 = str1[i1++];
                        var c4 = str1[i1++];

                        d = ((c & 0x7) << 18) | ((c2 & 0x3f) << 12) | ((c3 & 0x3f) << 6) | (c4 & 0x3f);
                    }
                }
            }

            if (char.IsSurrogate((char)s))
            {
                if (!char.IsHighSurrogate((char)s) || i2 >= str2.Length || !char.IsLowSurrogate(str2[i2]))
                {
                    return false;
                }

                var ch = 0x10000 + ((char)s - 0xD800) * 0x400 + (str2[i2++] - 0xDC00);

                s = ch;
            }

            if (s != d)
            {
                return false;
            }
        }

        return i1 == str1.Length && i2 == str2.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, bool ignoreCase)
    {
        if (ignoreCase)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                var ac = a[i];
                var bc = b[i];

                if (ac != bc)
                {
                    if (ac >= 'a' && ac <= 'z')
                    {
                        ac -= 'a' - 'A';
                    }

                    if (bc >= 'a' && bc <= 'z')
                    {
                        bc -= 'a' - 'A';
                    }

                    if (ac != bc)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        return a.SequenceEqual(b);
    }

    public static DTokenVariant GetIntegerTokenKind(ReadOnlySpan<byte> number)
    {
        if (number[0] == '-')
        {
            if (Utf8Parser.TryParse(number, out long _, out var bytes) && bytes == number.Length)
            {
                return DTokenVariant.Integer;
            }
        }
        else
        {
            if (Utf8Parser.TryParse(number, out ulong _, out var bytes) && bytes == number.Length)
            {
                return DTokenVariant.IntegerUnsigned;
            }
        }

        {
            if (Utf8Parser.TryParse(number, out decimal _, out var bytes) && bytes == number.Length)
            {
                return DTokenVariant.FloatDecimal;
            }
        }

        return DTokenVariant.BigNumber;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SkipUtf8Bom(ref ReadOnlyMemory<byte> source)
    {
        var span = source.Span;

        if (span.Length >= 3 &&
            span[0] == 0xEF &&
            span[1] == 0xBB &&
            span[2] == 0xBF)
        {
            source = source.Slice(3);
        }
    }

    public static string EncodeToString(DocumentWriter.BufferEnumerator buffers)
    {
        var enumerator = buffers.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            return string.Empty;
        }

        var first = enumerator.Current;

        if (!enumerator.MoveNext())
        {
            return Encoding.UTF8.GetString(first.Span);
        }

        var charCount = CountUtf16CharsFromValidUtf8(ref buffers);

        return string.Create(charCount, buffers, static (chars, buffers) =>
        {
            var decoder = Encoding.UTF8.GetDecoder();
            var charPt = 0;
            var enumerator = buffers.GetEnumerator();

            var flush = !enumerator.MoveNext();

            var current = enumerator.Current;

            while (!flush)
            {
                flush = !enumerator.MoveNext();

                var written = decoder.GetChars(
                    current.Span,
                    chars.Slice(charPt),
                    flush);

                charPt += written;
                current = enumerator.Current;
            }
        });
    }

    private static int CountUtf16CharsFromValidUtf8(ref DocumentWriter.BufferEnumerator buffers)
    {
        var count = 0;

        foreach (var buf in buffers)
        {
            count += CountUtf16CharsFromValidUtf8(
                buf.Span);
        }

        return count;
    }

    private static int CountUtf16CharsFromValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var count = 0;

        foreach (var b in bytes)
        {
            // ASCII: 0xxxxxxx
            if (b < 0x80)
            {
                count++;
            }
            // continuation byte: 10xxxxxx
            else if ((b & 0xC0) == 0x80)
            {
                // count 0
            }
            // 2-byte or 3-byte sequence lead
            else if (b < 0xF0)
            {
                count++;
            }
            // 4-byte sequence lead => surrogate pair
            else
            {
                count += 2;
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GetUtf8String(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetUtf16String(ReadOnlySpan<byte> utf8Bytes)
    {
        return Encoding.UTF8.GetString(utf8Bytes);
    }

    public static byte[] ConvertHexToDecimal(ReadOnlySpan<byte> hexDigits, bool negative)
    {
        const uint limbBase = 1000000000;

        if (hexDigits.IsEmpty)
        {
            throw new FormatException();
        }

        // little-endian limbs in base 10^9
        var limbs = new uint[hexDigits.Length * 4 / 29 + 2];
        var limbCount = 1;

        foreach (var c in hexDigits)
        {
            int digit;
            if (c is >= (byte)'0' and <= (byte)'9')
            {
                digit = c - '0';
            }
            else if (c is >= (byte)'a' and <= (byte)'f')
            {
                digit = c - 'a' + 10;
            }
            else if (c is >= (byte)'A' and <= (byte)'F')
            {
                digit = c - 'A' + 10;
            }
            else
            {
                throw new FormatException();
            }

            ulong carry = (uint)digit;
            for (var i = 0; i < limbCount; i++)
            {
                var v = (ulong)limbs[i] * 16 + carry;
                limbs[i] = (uint)(v % limbBase);
                carry = v / limbBase;
            }

            if (carry != 0)
            {
                limbs[limbCount++] = (uint)carry;
            }
        }

        var isZero = limbCount == 1 && limbs[0] == 0;
        var sign = negative && !isZero ? 1 : 0;
        var result = new byte[sign + CountDigits(limbs[limbCount - 1]) + (limbCount - 1) * 9];
        var pos = result.Length;

        for (var i = 0; i < limbCount - 1; i++)
        {
            var v = limbs[i];
            for (var j = 0; j < 9; j++)
            {
                result[--pos] = (byte)('0' + v % 10);
                v /= 10;
            }
        }

        var top = limbs[limbCount - 1];
        do
        {
            result[--pos] = (byte)('0' + top % 10);
            top /= 10;
        } while (top != 0);

        if (sign != 0)
        {
            result[0] = (byte)'-';
        }

        return result;

        static int CountDigits(uint v)
        {
            var n = 1;
            while (v >= 10)
            {
                v /= 10;
                n++;
            }

            return n;
        }
    }
}