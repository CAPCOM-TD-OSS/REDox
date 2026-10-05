// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public class Utf8TimestampFormattingTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(19)]
    public void InsufficientBufferReturnsFalse(int size)
    {
        Span<byte> buffer = stackalloc byte[64];
        var date = new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc);

        Assert.False(Utf8Helper.TryFormatTimestamp(date, SerializerSettings.Default, buffer.Slice(0, size), out var written));
        Assert.Equal(0, written);
    }

    [Fact]
    public void ExactlySizedBufferSucceeds()
    {
        Span<byte> buffer = stackalloc byte[20];
        var date = new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc);

        Assert.True(Utf8Helper.TryFormatTimestamp(date, SerializerSettings.Default, buffer, out var written));
        Assert.Equal("2026-10-04T12:34:56Z", Encoding.UTF8.GetString(buffer.Slice(0, written)));
    }

    [Fact]
    public void ExactlySizedMicrosoftTextBufferSucceeds()
    {
        Span<byte> buffer = stackalloc byte[9];
        var settings = new DoxSerializerSettings { DateFormatHandling = DateFormatHandling.MicrosoftDateFormat };

        Assert.False(Utf8Helper.TryFormatTimestamp(DateTime.UnixEpoch, settings, buffer.Slice(0, 8), out var written));
        Assert.Equal(0, written);
        Assert.True(Utf8Helper.TryFormatTimestamp(DateTime.UnixEpoch, settings, buffer, out written));
        Assert.Equal("/Date(0)/", Encoding.UTF8.GetString(buffer.Slice(0, written)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dd MMMM yyyy HH:mm:ss.FFFFFFF")]
    public void FormattingDoesNotAllocateIntermediateStringsOrArrays(string? format)
    {
        var settings = new DoxSerializerSettings
        {
            Culture = CultureInfo.GetCultureInfo("fr-FR"),
            DateFormatString = format
        };
        var date = new DateTime(2026, 2, 4, 12, 34, 56).AddTicks(1234567);
        var offsetDate = new DateTimeOffset(date, TimeSpan.FromMinutes(330));
        Span<byte> buffer = stackalloc byte[64];

        // Warm culture data and formatting paths before measuring managed allocations.
        Assert.True(Utf8Helper.TryFormatTimestamp(date, settings, buffer, out _));
        Assert.True(Utf8Helper.TryFormatTimestamp(offsetDate, settings, buffer, out _));
        Assert.True(Utf8Helper.TryFormatTimestamp(date, TimestampKind.LocalDateTime, buffer, out _));
        Assert.True(Utf8Helper.TryFormatTimestamp(offsetDate, buffer, out _));

        var success = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            success &= Utf8Helper.TryFormatTimestamp(date, settings, buffer, out _);
            success &= Utf8Helper.TryFormatTimestamp(offsetDate, settings, buffer, out _);
            success &= Utf8Helper.TryFormatTimestamp(date, TimestampKind.LocalDateTime, buffer, out _);
            success &= Utf8Helper.TryFormatTimestamp(offsetDate, buffer, out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(success);
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void LongCustomUtf8FormatFallsBackToIso()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var format = "dd MMMM yyyy '" + new string('あ', 80) + "'";
        var settings = new DoxSerializerSettings { Culture = culture, DateFormatString = format };
        var date = new DateTime(2026, 2, 4);
        Span<byte> buffer = stackalloc byte[64];

        Assert.True(Utf8Helper.TryFormatTimestamp(date, settings, buffer, out var written));
        Assert.Equal("2026-02-04T00:00:00", Encoding.UTF8.GetString(buffer.Slice(0, written)));
    }

    [Fact]
    public void LargeCultureExpansionFallsBackToIso()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var format = string.Join(" ", Enumerable.Repeat("dddd", 12));
        var settings = new DoxSerializerSettings { Culture = culture, DateFormatString = format };
        var date = new DateTime(2026, 10, 4);
        var expected = date.ToString(format, culture);
        Assert.True(Encoding.UTF8.GetByteCount(expected) > Utf8Helper.TimestampBufferSize);
        using var writer = new Utf8TextWriter(settings);

        writer.WriteDateTime(date);

        Assert.Equal("2026-10-04T00:00:00", writer.EncodeToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonWriterFallsBackToIsoForLongCustomUtf8Formats(bool withOffset)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var format = "dd MMMM yyyy HH:mm:ss.FFFFFFF zzz '" + new string('あ', 80) + "'";
        var settings = new DoxSerializerSettings { Culture = culture, DateFormatString = format };
        var date = new DateTimeOffset(2026, 2, 4, 12, 34, 56, TimeSpan.FromMinutes(330)).AddTicks(1);
        var value = withOffset ? DValue.From(date, settings) : DValue.From(date.UtcDateTime, settings);

        using var reparsed = JsonDocument.Parse(JsonDocument.EncodeToString(value));

        Assert.Equal(withOffset ? "2026-02-04T12:34:56.0000001+05:30" : "2026-02-04T07:04:56.0000001Z",
            reparsed.RootElement.GetString());
    }
}