using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using REDox.Serialization;

namespace REDox.Tests;

public class Utf8HelperTests
{
    // ============================================================
    // Base64
    // ============================================================

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("hello")]
    [InlineData("Hello, World!")]
    [InlineData("日本語")]
    public void Base64_RoundTrip(string text)
    {
        var source = Encoding.UTF8.GetBytes(text);

        Span<byte> encoded = stackalloc byte[256];

        var encodedLength = Utf8Helper.EncodeBase64(
            source,
            encoded);

        var result = Utf8Helper.TryParseBase64(
            encoded[..encodedLength],
            out var decoded,
            out var bytesConsumed);

        Assert.True(result);
        Assert.Equal(encodedLength, bytesConsumed);
        Assert.Equal(source, decoded.ToArray());
    }

    [Theory]
    [InlineData("!")]
    [InlineData("@@@@")]
    [InlineData("****")]
    public void TryParseBase64_Invalid_ReturnsFalse(string text)
    {
        var result = Utf8Helper.TryParseBase64(
            Encoding.UTF8.GetBytes(text),
            out _,
            out _);

        Assert.False(result);
    }

    // ============================================================
    // Base64Url
    // ============================================================

    [Fact]
    public void Base64Url_RoundTrip()
    {
        byte[] source =
        [
            0x00,
            0x01,
            0x7F,
            0x80,
            0xFB,
            0xEF,
            0xFF
        ];

        Span<byte> encoded = stackalloc byte[64];

        var encodedLength = Utf8Helper.EncodeBase64Url(
            source,
            encoded);

        var result = Utf8Helper.TryParseBase64Url(
            encoded[..encodedLength],
            out var decoded,
            out var bytesConsumed);

        Assert.True(result);
        Assert.Equal(encodedLength, bytesConsumed);
        Assert.Equal(source, decoded.ToArray());
    }

    // ============================================================
    // Base16
    // ============================================================

    [Theory]
    [InlineData("", "")]
    [InlineData("00", "\x00")]
    [InlineData("414243", "ABC")]
    [InlineData("48656C6C6F", "Hello")]
    public void TryParseBase16_Valid_ReturnsExpected(
        string hex,
        string expected)
    {
        var result = Utf8Helper.TryParseBase16(
            Encoding.ASCII.GetBytes(hex),
            out var decoded,
            out var consumed);

        Assert.True(result);
        Assert.Equal(hex.Length, consumed);
        Assert.Equal(
            Encoding.UTF8.GetBytes(expected),
            decoded.ToArray());
    }

    [Theory]
    [InlineData("G0")]
    [InlineData("0G")]
    [InlineData("ZZ")]
    [InlineData("123")]
    public void TryParseBase16_Invalid_ReturnsFalse(string text)
    {
        var result = Utf8Helper.TryParseBase16(
            Encoding.ASCII.GetBytes(text),
            out _,
            out _);

        Assert.False(result);
    }

    // ============================================================
    // EncodeNumber - integer
    // ============================================================

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-1, "-1")]
    [InlineData(int.MaxValue, "2147483647")]
    [InlineData(int.MinValue, "-2147483648")]
    public void EncodeNumber_Int_ReturnsExpected(
        int value,
        string expected)
    {
        Span<byte> buffer = stackalloc byte[64];

        var written = Utf8Helper.EncodeNumber(buffer, value);

        Assert.Equal(
            expected,
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1L, "1")]
    [InlineData(-1L, "-1")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public void EncodeNumber_Long_ReturnsExpected(
        long value,
        string expected)
    {
        Span<byte> buffer = stackalloc byte[64];

        var written = Utf8Helper.EncodeNumber(buffer, value);

        Assert.Equal(
            expected,
            Encoding.UTF8.GetString(buffer[..written]));
    }

    // ============================================================
    // EncodeNumber - floating point
    // ============================================================

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(1.5)]
    [InlineData(-123.456)]
    public void EncodeNumber_Double_RoundTrips(double value)
    {
        Span<byte> buffer = stackalloc byte[128];

        var written = Utf8Helper.EncodeNumber(
            buffer,
            value,
            0);

        var text = Encoding.UTF8.GetString(buffer[..written]);

        Assert.True(
            double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed));

        Assert.Equal(value, parsed);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(1.5)]
    public void EncodeNumber_AlwaysIncludeDecimal_IncludesDecimal(
        double value)
    {
        Span<byte> buffer = stackalloc byte[128];

        var written = Utf8Helper.EncodeNumber(
            buffer,
            value,
            FloatFormatHandling.AlwaysIncludeDecimal);

        var text = Encoding.UTF8.GetString(buffer[..written]);

        Assert.Contains(".", text);
    }

    [Fact]
    public void EncodeNumber_Half_IsCultureIndependent()
    {
        var oldCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture =
                CultureInfo.GetCultureInfo("fr-FR");

            Span<byte> buffer = stackalloc byte[64];

            var written = Utf8Helper.EncodeNumber(
                buffer,
                (Half)1.5f,
                0);

            var text =
                Encoding.UTF8.GetString(buffer[..written]);

            Assert.Equal("1.5", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }

    [Fact]
    public void EncodeNumber_Half_Integer_AlwaysIncludeDecimal()
    {
        Span<byte> buffer = stackalloc byte[64];

        var written = Utf8Helper.EncodeNumber(
            buffer,
            (Half)2,
            FloatFormatHandling.AlwaysIncludeDecimal);

        Assert.Equal(
            "2.0",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void EncodeNumber_NaN_AsString()
    {
        Span<byte> buffer = stackalloc byte[64];

        var written = Utf8Helper.EncodeNumber(
            buffer,
            double.NaN,
            FloatFormatHandling.SpecialFloatAsString);

        Assert.Equal(
            "\"NaN\"",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void EncodeNumber_SpecialFloatAsDefault_ReturnsZero(
        double value)
    {
        Span<byte> buffer = stackalloc byte[64];

        var written = Utf8Helper.EncodeNumber(
            buffer,
            value,
            FloatFormatHandling.SpecialFloatAsDefaultValue);

        Assert.Equal(
            "0.0",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    // ============================================================
    // TryParseHexadecimal
    // ============================================================

    [Theory]
    [InlineData("0x0", 0L)]
    [InlineData("0X0", 0L)]
    [InlineData("0x1", 1L)]
    [InlineData("0x9", 9L)]
    [InlineData("0xa", 10L)]
    [InlineData("0xA", 10L)]
    [InlineData("0xf", 15L)]
    [InlineData("0xF", 15L)]
    [InlineData("0x10", 16L)]
    [InlineData("0xff", 255L)]
    [InlineData("0x123456", 0x123456L)]
    [InlineData("+0x1", 1L)]
    [InlineData("-0x1", -1L)]
    [InlineData("-0xff", -255L)]
    public void TryParseHexadecimal_Valid(
        string text,
        long expected)
    {
        var result = Utf8Helper.TryParseHexadecimal(
            Encoding.ASCII.GetBytes(text),
            out var actual);

        Assert.True(result);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(
        "0x7fffffffffffffff",
        long.MaxValue)]
    [InlineData(
        "0X7FFFFFFFFFFFFFFF",
        long.MaxValue)]
    [InlineData(
        "-0x8000000000000000",
        long.MinValue)]
    [InlineData(
        "-0X8000000000000000",
        long.MinValue)]
    public void TryParseHexadecimal_Boundary(
        string text,
        long expected)
    {
        Assert.True(
            Utf8Helper.TryParseHexadecimal(
                Encoding.ASCII.GetBytes(text),
                out var actual));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("0")]
    [InlineData("x")]
    [InlineData("0x")]
    [InlineData("+0x")]
    [InlineData("-0x")]
    [InlineData("123")]
    [InlineData("ff")]
    [InlineData(" 0x1")]
    [InlineData("0x1 ")]
    [InlineData("0xg")]
    [InlineData("0x1g")]
    [InlineData("0x1_0")]
    [InlineData("0x1.0")]
    [InlineData("0x+1")]
    [InlineData("0x-1")]
    public void TryParseHexadecimal_Invalid_ReturnsFalse(
        string text)
    {
        Assert.False(
            Utf8Helper.TryParseHexadecimal(
                Encoding.ASCII.GetBytes(text),
                out _));
    }

    [Theory]
    [InlineData("0x8000000000000000")]
    [InlineData("+0x8000000000000000")]
    [InlineData("0xffffffffffffffff")]
    [InlineData("-0x8000000000000001")]
    [InlineData("-0xffffffffffffffff")]
    [InlineData("0x10000000000000000")]
    [InlineData("-0x10000000000000000")]
    public void TryParseHexadecimal_Overflow_ReturnsFalse(
        string text)
    {
        Assert.False(
            Utf8Helper.TryParseHexadecimal(
                Encoding.ASCII.GetBytes(text),
                out _));
    }

    // ============================================================
    // TryFormatHexadecimal
    // ============================================================

    [Theory]
    [InlineData(0L, "0x0")]
    [InlineData(1L, "0x1")]
    [InlineData(15L, "0xf")]
    [InlineData(16L, "0x10")]
    [InlineData(255L, "0xff")]
    [InlineData(-1L, "-0x1")]
    [InlineData(-255L, "-0xff")]
    [InlineData(long.MaxValue, "0x7fffffffffffffff")]
    public void TryFormatHexadecimal_Valid(
        long value,
        string expected)
    {
        Span<byte> buffer = stackalloc byte[32];

        Assert.True(
            Utf8Helper.TryFormatHexadecimal(
                value,
                buffer,
                out var written));

        Assert.Equal(
            expected,
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void TryFormatHexadecimal_LongMinValue()
    {
        Span<byte> buffer = stackalloc byte[32];

        Assert.True(
            Utf8Helper.TryFormatHexadecimal(
                long.MinValue,
                buffer,
                out var written));

        Assert.Equal(
            "-0x8000000000000000",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    // ============================================================
    // TryFormatOctalString
    // ============================================================

    [Theory]
    [InlineData(0L, "0o0")]
    [InlineData(1L, "0o1")]
    [InlineData(7L, "0o7")]
    [InlineData(8L, "0o10")]
    [InlineData(9L, "0o11")]
    [InlineData(63L, "0o77")]
    [InlineData(64L, "0o100")]
    [InlineData(255L, "0o377")]
    [InlineData(512L, "0o1000")]
    [InlineData(-1L, "-0o1")]
    [InlineData(-8L, "-0o10")]
    [InlineData(-255L, "-0o377")]
    public void TryFormatOctalString_Valid(
        long value,
        string expected)
    {
        Span<byte> buffer = stackalloc byte[32];

        Assert.True(
            Utf8Helper.TryFormatOctalString(
                value,
                buffer,
                out var written));

        Assert.Equal(
            expected,
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Theory]
    [InlineData(
        long.MaxValue,
        "0o777777777777777777777")]
    [InlineData(
        long.MinValue,
        "-0o1000000000000000000000")]
    public void TryFormatOctalString_Boundary(
        long value,
        string expected)
    {
        Span<byte> buffer = stackalloc byte[32];

        Assert.True(
            Utf8Helper.TryFormatOctalString(
                value,
                buffer,
                out var written));

        Assert.Equal(
            expected,
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Theory]
    [InlineData(0L, 2)]
    [InlineData(1L, 2)]
    [InlineData(8L, 3)]
    [InlineData(-1L, 3)]
    [InlineData(-8L, 4)]
    public void TryFormatOctalString_BufferTooSmall(
        long value,
        int length)
    {
        Span<byte> buffer = stackalloc byte[length];

        var result =
            Utf8Helper.TryFormatOctalString(
                value,
                buffer,
                out var written);

        Assert.False(result);
        Assert.Equal(0, written);
    }

    // ============================================================
    // TryFormatBinaryString
    // ============================================================

    [Theory]
    [InlineData(0L, "0b0")]
    [InlineData(1L, "0b1")]
    [InlineData(2L, "0b10")]
    [InlineData(3L, "0b11")]
    [InlineData(8L, "0b1000")]
    [InlineData(-1L, "-0b1")]
    [InlineData(-8L, "-0b1000")]
    public void TryFormatBinaryString_Valid(
        long value,
        string expected)
    {
        Span<byte> buffer = stackalloc byte[128];

        Assert.True(
            Utf8Helper.TryFormatBinaryString(
                value,
                buffer,
                out var written));

        Assert.Equal(
            expected,
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void TryFormatBinaryString_LongMinValue()
    {
        Span<byte> buffer = stackalloc byte[128];

        Assert.True(
            Utf8Helper.TryFormatBinaryString(
                long.MinValue,
                buffer,
                out var written));

        Assert.Equal(
            "-0b1" + new string('0', 63),
            Encoding.UTF8.GetString(buffer[..written]));
    }

    // ============================================================
    // TryParseDate
    // ============================================================

    [Theory]
    [InlineData("0001-01-01", 1, 1, 1)]
    [InlineData("2024-01-01", 2024, 1, 1)]
    [InlineData("2024-02-29", 2024, 2, 29)]
    [InlineData("2023-02-28", 2023, 2, 28)]
    [InlineData("9999-12-31", 9999, 12, 31)]
    public void TryParseDate_Valid(
        string text,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var result = Utf8Helper.TryParseDate(
            Encoding.ASCII.GetBytes(text),
            out var year,
            out var month,
            out var day);

        Assert.True(result);
        Assert.Equal(expectedYear, year);
        Assert.Equal(expectedMonth, month);
        Assert.Equal(expectedDay, day);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2024")]
    [InlineData("2024-01")]
    [InlineData("2024/01/01")]
    [InlineData("0000-01-01")]
    [InlineData("2024-00-01")]
    [InlineData("2024-13-01")]
    [InlineData("2024-01-00")]
    [InlineData("2024-01-32")]
    [InlineData("2023-02-29")]
    [InlineData("2100-02-29")]
    [InlineData("20a4-01-01")]
    public void TryParseDate_Invalid_ReturnsFalse(
        string text)
    {
        Assert.False(
            Utf8Helper.TryParseDate(
                Encoding.ASCII.GetBytes(text),
                out _,
                out _,
                out _));
    }

    // ============================================================
    // TryParseDateTimeOffsetISO8601
    // ============================================================

    [Theory]
    [InlineData("2024-05-06")]
    [InlineData("2024-05-06T07:08")]
    [InlineData("2024-05-06T07:08:09")]
    [InlineData("2024-05-06T07:08:09.1")]
    [InlineData("2024-05-06T07:08:09.123")]
    [InlineData("2024-05-06T07:08:09.1234567")]
    public void TryParseDateTimeOffsetISO8601_ValidWithoutOffset(
        string text)
    {
        Assert.True(
            Utf8Helper.TryParseDateTimeOffsetIso8601(
                Encoding.ASCII.GetBytes(text),
                out _,
                out var offset));

        Assert.Null(offset);
    }

    [Fact]
    public void TryParseDateTimeOffsetISO8601_Z_ReturnsUtc()
    {
        Assert.True(
            Utf8Helper.TryParseDateTimeOffsetIso8601(
                "2024-05-06T07:08:09Z"u8,
                out var value,
                out var offset));

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Null(offset);
    }

    [Theory]
    [InlineData(
        "2024-05-06T07:08:09+00:00",
        0,
        0)]
    [InlineData(
        "2024-05-06T07:08:09+09:00",
        9,
        0)]
    [InlineData(
        "2024-05-06T07:08:09+09:30",
        9,
        30)]
    [InlineData(
        "2024-05-06T07:08:09-05:30",
        -5,
        -30)]
    [InlineData(
        "2024-05-06T07:08:09+14:00",
        14,
        0)]
    public void TryParseDateTimeOffsetISO8601_ValidOffset(
        string text,
        int hours,
        int minutes)
    {
        Assert.True(
            Utf8Helper.TryParseDateTimeOffsetIso8601(
                Encoding.ASCII.GetBytes(text),
                out _,
                out var offset));

        Assert.Equal(
            new TimeSpan(hours, minutes, 0),
            offset);
    }

    [Theory]
    [InlineData("2024-05-06T24:00:00")]
    [InlineData("2024-05-06T23:60:00")]
    [InlineData("2024-05-06T23:59:60")]
    [InlineData("2024-05-06T07:08:09+14:01")]
    [InlineData("2024-05-06T07:08:09+15:00")]
    [InlineData("2024-05-06T07:08:09+99:00")]
    [InlineData("2024-05-06T07:08:09+09")]
    [InlineData("2024-05-06T07:08:09+09:")]
    [InlineData("2024-05-06T07:08:09+09:000")]
    public void TryParseDateTimeOffsetISO8601_Invalid(
        string text)
    {
        Assert.False(
            Utf8Helper.TryParseDateTimeOffsetIso8601(
                Encoding.ASCII.GetBytes(text),
                out _,
                out _));
    }

    // regression:
    // fractional separator must have at least one digit.
    [Theory]
    [InlineData("2024-05-06T07:08:09.")]
//    [InlineData("2024-05-06T07:08:09.Z")] System.Text.Json Compatibility
//    [InlineData("2024-05-06T07:08:09.+09:00")] System.Text.Json Compatibility
    public void TryParseDateTimeOffsetISO8601_EmptyFraction_ReturnsFalse(
        string text)
    {
        var exception = Record.Exception(() =>
        {
            var result =
                Utf8Helper.TryParseDateTimeOffsetIso8601(
                    Encoding.ASCII.GetBytes(text),
                    out _,
                    out _);

            Assert.False(result);
        });

        Assert.Null(exception);
    }

    // ============================================================
    // TryFormatTimestamp
    // ============================================================

    [Fact]
    public void TryFormatTimestamp_IsoUtc()
    {
        var date =
            new DateTime(
                2024,
                5,
                6,
                7,
                8,
                9,
                DateTimeKind.Utc);

        Span<byte> buffer = stackalloc byte[128];

        Assert.True(
            Utf8Helper.TryFormatTimestamp(
                date,
                null,
                null,
                CultureInfo.InvariantCulture,
                DateFormatHandling.IsoDateFormat,
                buffer,
                out var written));

        Assert.Equal(
            "2024-05-06T07:08:09Z",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void TryFormatTimestamp_IsoFraction()
    {
        var date =
            new DateTime(
                    2024,
                    5,
                    6,
                    7,
                    8,
                    9,
                    DateTimeKind.Utc)
                .AddTicks(1_234_567);

        Span<byte> buffer = stackalloc byte[128];

        Assert.True(
            Utf8Helper.TryFormatTimestamp(
                date,
                null,
                null,
                CultureInfo.InvariantCulture,
                DateFormatHandling.IsoDateFormat,
                buffer,
                out var written));

        Assert.Equal(
            "2024-05-06T07:08:09.1234567Z",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void TryFormatTimestamp_IsoWithOffset()
    {
        var date =
            new DateTime(
                2024,
                5,
                6,
                7,
                8,
                9,
                DateTimeKind.Unspecified);

        Span<byte> buffer = stackalloc byte[128];

        Assert.True(
            Utf8Helper.TryFormatTimestamp(
                date,
                TimeSpan.FromHours(9),
                null,
                CultureInfo.InvariantCulture,
                DateFormatHandling.IsoDateFormat,
                buffer,
                out var written));

        Assert.Equal(
            "2024-05-06T07:08:09+09:00",
            Encoding.UTF8.GetString(buffer[..written]));
    }

    // ============================================================
    // TryParseTimestamp
    // ============================================================

    [Fact]
    public void TryParseTimestamp_Empty_ReturnsFalse()
    {
        var result =
            Utf8Helper.TryParseTimestamp(
                ReadOnlySpan<byte>.Empty,
                out _,
                out _,
                null,
                false);

        Assert.False(result);
    }

    [Fact]
    public void TryParseTimestamp_IsoUtc()
    {
        Assert.True(
            Utf8Helper.TryParseTimestamp(
                "2024-05-06T07:08:09Z"u8,
                out var value,
                out var offset,
                null,
                false));

        Assert.Equal(
            new DateTime(
                2024,
                5,
                6,
                7,
                8,
                9,
                DateTimeKind.Utc),
            value);

        Assert.Null(offset);
    }

    [Fact]
    public void TryParseTimestamp_LowercaseZ()
    {
        Assert.True(
            Utf8Helper.TryParseTimestamp(
                "2024-05-06T07:08:09z"u8,
                out var value,
                out _,
                null,
                false));

        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public void TryParseTimestamp_MicrosoftEpoch()
    {
        Assert.True(
            Utf8Helper.TryParseTimestamp(
                "/Date(0)/"u8,
                out var value,
                out _,
                null,
                false));

        Assert.Equal(DateTime.UnixEpoch, value);
    }

    [Theory]
    [InlineData("+0900", 540)]
    [InlineData("-0500", -300)]
    [InlineData("+0000", 0)]
    public void TryParseTimestamp_MicrosoftDateWithOffset_UsesMachineLocalTime(
        string suffix, int offsetMinutes)
    {
        const long milliseconds = 1705285810000;
        var source = Encoding.UTF8.GetBytes($"/Date({milliseconds}{suffix})/");
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;

        Assert.True(Utf8Helper.TryParseTimestamp(source, out var actual, out var offset, null, false));
        Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), offset);
        Assert.Equal(DateTimeKind.Local, actual.Kind);
        // The milliseconds encode a UTC instant; the suffix must not replace the machine's local offset.
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParseTimestamp_CustomFormat()
    {
        Assert.True(
            Utf8Helper.TryParseTimestamp(
                "2024/05/06 07:08:09"u8,
                out var value,
                out _,
                "yyyy/MM/dd HH:mm:ss",
                false));

        Assert.Equal(
            new DateTime(
                2024,
                5,
                6,
                7,
                8,
                9),
            value);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2024-99-99")]
    [InlineData("2024-05-06T99:99:99")]
    public void TryParseTimestamp_Invalid_ReturnsFalse(
        string text)
    {
        Assert.False(
            Utf8Helper.TryParseTimestamp(
                Encoding.UTF8.GetBytes(text),
                out _,
                out _,
                null,
                false));
    }

    // ============================================================
    // Utf8CharsToUtf32Char
    // ============================================================

    [Theory]
    [InlineData("A", 0x41, 1)]
    [InlineData("é", 0xE9, 2)]
    [InlineData("あ", 0x3042, 3)]
    [InlineData("😀", 0x1F600, 4)]
    public void Utf8CharsToUtf32Char_Valid(
        string text,
        int expectedCodePoint,
        int expectedConsumed)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        var consumed =
            Utf8Helper.Utf8CharsToUtf32Char(
                bytes,
                out var codePoint);

        Assert.Equal(expectedConsumed, consumed);
        Assert.Equal(expectedCodePoint, codePoint);
    }

    // ============================================================
    // Compare
    // ============================================================

    [Theory]
    [InlineData("", "", false)]
    [InlineData("a", "a", false)]
    [InlineData("abcdefg", "abcdefg", false)]
    [InlineData("abcdefgh", "abcdefgh", false)]
    [InlineData("abcdefghijklmnop", "abcdefghijklmnop", false)]
    [InlineData("ABC", "abc", true)]
    [InlineData("AbCdEfGh", "aBcDeFgH", true)]
    public void Compare_Equal_ReturnsZero(
        string a,
        string b,
        bool ignoreCase)
    {
        Assert.Equal(
            0,
            Utf8Helper.Compare(
                Encoding.UTF8.GetBytes(a),
                Encoding.UTF8.GetBytes(b),
                ignoreCase));
    }

    [Fact]
    public void Compare_DifferentLengths_UsesLength()
    {
        Assert.True(
            Utf8Helper.Compare(
                "a"u8,
                "aa"u8,
                false) < 0);

        Assert.True(
            Utf8Helper.Compare(
                "aa"u8,
                "a"u8,
                false) > 0);
    }

    [Fact]
    public void Compare_IsAntisymmetric()
    {
        var a = BitConverter.GetBytes(long.MinValue);
        var b = BitConverter.GetBytes(0L);

        var ab = Utf8Helper.Compare(a, b, false);
        var ba = Utf8Helper.Compare(b, a, false);

        Assert.NotEqual(0, ab);
        Assert.Equal(-Math.Sign(ab), Math.Sign(ba));
    }

    [Fact]
    public void Compare_SortAndBinarySearch_AreConsistent()
    {
        byte[][] values =
        [
            "foo"u8.ToArray(),
            "bar"u8.ToArray(),
            "abcd"u8.ToArray(),
            "abcdefgh"u8.ToArray(),
            "abcdefghi"u8.ToArray(),
            "xxxxxxxx"u8.ToArray(),
            "日本語"u8.ToArray()
        ];

        var comparer =
            Comparer<byte[]>.Create((a, b) =>
                Utf8Helper.Compare(
                    a,
                    b,
                    false));

        Array.Sort(values, comparer);

        foreach (var value in values)
        {
            var index =
                Array.BinarySearch(
                    values,
                    value,
                    comparer);

            Assert.True(index >= 0);
        }
    }

    // ============================================================
    // Equals UTF8 / UTF16
    // ============================================================

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("日本語")]
    [InlineData("éàö")]
    [InlineData("😀")]
    [InlineData("A😀B日本語C")]
    public void Equals_Utf8AndUtf16_SameString_ReturnsTrue(
        string text)
    {
        Assert.True(
            Utf8Helper.Equals(
                Encoding.UTF8.GetBytes(text),
                text.AsSpan()));
    }

    [Theory]
    [InlineData("abc", "abd")]
    [InlineData("abc", "ab")]
    [InlineData("日本", "日本語")]
    [InlineData("😀", "😁")]
    public void Equals_Utf8AndUtf16_Different_ReturnsFalse(
        string left,
        string right)
    {
        Assert.False(
            Utf8Helper.Equals(
                Encoding.UTF8.GetBytes(left),
                right.AsSpan()));
    }

    // Optional robustness tests:
    // enable these if malformed UTF-8 is allowed to reach this helper.
    [Fact]
    public void Equals_TruncatedUtf8_DoesNotThrow()
    {
        byte[] invalidUtf8 =
        [
            0xE3,
            0x81
        ];

        var exception = Record.Exception(() =>
        {
            var result =
                Utf8Helper.Equals(
                    invalidUtf8,
                    "あ".AsSpan());

            Assert.False(result);
        });

        Assert.Null(exception);
    }

    // ============================================================
    // Equals UTF8 / UTF8
    // ============================================================

    [Theory]
    [InlineData("abc", "abc", false, true)]
    [InlineData("abc", "ABC", false, false)]
    [InlineData("abc", "ABC", true, true)]
    [InlineData("AbCd", "aBcD", true, true)]
    [InlineData("abc", "abd", true, false)]
    [InlineData("", "", false, true)]
    public void Equals_Utf8Utf8_ReturnsExpected(
        string left,
        string right,
        bool ignoreCase,
        bool expected)
    {
        Assert.Equal(
            expected,
            Utf8Helper.Equals(
                Encoding.UTF8.GetBytes(left),
                Encoding.UTF8.GetBytes(right),
                ignoreCase));
    }

    [Fact]
    public void Equals_IgnoreCase_IsAsciiOnly()
    {
        Assert.False(
            Utf8Helper.Equals(
                Encoding.UTF8.GetBytes("ä"),
                Encoding.UTF8.GetBytes("Ä"),
                true));
    }

    // ============================================================
    // GetIntegerTokenKind
    // ============================================================

    [Theory]
    [InlineData("-1")]
    [InlineData("-9223372036854775808")]
    public void GetIntegerTokenKind_SignedInteger(
        string text)
    {
        Assert.Equal(
            DTokenVariant.Integer,
            Utf8Helper.GetIntegerTokenKind(
                Encoding.ASCII.GetBytes(text)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("9223372036854775808")]
    [InlineData("18446744073709551615")]
    public void GetIntegerTokenKind_UnsignedInteger(
        string text)
    {
        Assert.Equal(
            DTokenVariant.IntegerUnsigned,
            Utf8Helper.GetIntegerTokenKind(
                Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void GetIntegerTokenKind_LargerThanUInt64_ButDecimal()
    {
        Assert.Equal(
            DTokenVariant.FloatDecimal,
            Utf8Helper.GetIntegerTokenKind(
                "18446744073709551616"u8));
    }

    [Fact]
    public void GetIntegerTokenKind_LargerThanDecimal_ReturnsBigNumber()
    {
        Assert.Equal(
            DTokenVariant.BigNumber,
            Utf8Helper.GetIntegerTokenKind(
                "79228162514264337593543950336"u8));
    }

    [Theory]
    [InlineData("123abc")]
    [InlineData("-123abc")]
    public void GetIntegerTokenKind_PartialParse_IsNotInteger(
        string text)
    {
        Assert.Equal(
            DTokenVariant.BigNumber,
            Utf8Helper.GetIntegerTokenKind(
                Encoding.ASCII.GetBytes(text)));
    }

    // ============================================================
    // BOM
    // ============================================================

    [Fact]
    public void SkipUtf8Bom_WithBom_RemovesBom()
    {
        ReadOnlyMemory<byte> source =
            new byte[]
            {
                0xEF,
                0xBB,
                0xBF,
                (byte)'a',
                (byte)'b',
                (byte)'c'
            };

        Utf8Helper.SkipUtf8Bom(ref source);

        Assert.Equal(
            "abc",
            Encoding.UTF8.GetString(source.Span));
    }

    [Fact]
    public void SkipUtf8Bom_WithoutBom_DoesNothing()
    {
        ReadOnlyMemory<byte> source =
            Encoding.UTF8.GetBytes("abc");

        Utf8Helper.SkipUtf8Bom(ref source);

        Assert.Equal(
            "abc",
            Encoding.UTF8.GetString(source.Span));
    }

    [Fact]
    public void SkipUtf8Bom_PartialBom_DoesNothing()
    {
        ReadOnlyMemory<byte> source =
            new byte[]
            {
                0xEF,
                0xBB
            };

        Utf8Helper.SkipUtf8Bom(ref source);

        Assert.Equal(2, source.Length);
    }


    // ============================================================
    // UTF8 / UTF16 helpers
    // ============================================================

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("日本語")]
    [InlineData("é")]
    [InlineData("😀")]
    [InlineData("Hello 日本語 😀")]
    public void Utf8Utf16_RoundTrip(string text)
    {
        var utf8 =
            Utf8Helper.GetUtf8String(text);

        var utf16 =
            Utf8Helper.GetUtf16String(utf8);

        Assert.Equal(text, utf16);
    }
}