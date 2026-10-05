using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace REDox.Tests;

public class Utf8TextWriterTest
{
    private readonly ITestOutputHelper _output;

    public Utf8TextWriterTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EncodeToStringDecodesUtf8AcrossBufferSegments()
    {
        using var writer = new Utf8TextWriter(SerializerSettings.Default);

        var bufSize = writer.Settings.DefaultBufferSize;

        writer.WriteString(Encoding.ASCII.GetBytes(new string('a', bufSize - 1)));

        var utf8Bytes = Encoding.UTF8.GetBytes("あ");
        writer.WriteUtf8Byte(utf8Bytes[0]);
        writer.WriteString(new ReadOnlySpan<byte>(utf8Bytes, 1, utf8Bytes.Length - 1));

        var expected = new string('a', bufSize - 1) + "あ";

        _output.WriteLine(expected.Length.ToString());
        _output.WriteLine(writer.EncodeToString().Length.ToString());

        Assert.Equal(expected, writer.EncodeToString());
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.Local)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Local)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.Local)]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.Unspecified)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Unspecified)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.Unspecified)]
    public void WriteDateTimeMatchesNewtonsoftJson(DateTimeKind kind, Serialization.DateTimeZoneHandling zone)
    {
        var value = new DateTime(2026, 10, 4, 12, 34, 56, kind).AddTicks(1234567);

        foreach (var format in new[] { Serialization.DateFormatHandling.IsoDateFormat, Serialization.DateFormatHandling.MicrosoftDateFormat })
        {
            using var writer = new Utf8TextWriter(new DoxSerializerSettings
            {
                DateTimeZoneHandling = zone,
                DateFormatHandling = format
            });

            writer.WriteDateTime(value);

            var json = JsonConvert.SerializeObject(value, new JsonSerializerSettings
            {
                DateTimeZoneHandling = (DateTimeZoneHandling)zone,
                DateFormatHandling = (DateFormatHandling)format
            });
            // Text output contains the date value without JSON quotes or escapes.
            Assert.Equal(JsonConvert.DeserializeObject<string>(json), writer.EncodeToString());
        }
    }

    [Theory]
    [InlineData(-7, Serialization.DateFormatHandling.IsoDateFormat)]
    [InlineData(0, Serialization.DateFormatHandling.IsoDateFormat)]
    [InlineData(9, Serialization.DateFormatHandling.IsoDateFormat)]
    [InlineData(-7, Serialization.DateFormatHandling.MicrosoftDateFormat)]
    [InlineData(0, Serialization.DateFormatHandling.MicrosoftDateFormat)]
    [InlineData(9, Serialization.DateFormatHandling.MicrosoftDateFormat)]
    public void WriteDateTimeOffsetPreservesOffset(int offsetHours, Serialization.DateFormatHandling format)
    {
        var value = new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromHours(offsetHours)).AddTicks(1);
        using var writer = new Utf8TextWriter(new DoxSerializerSettings
        {
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Utc,
            DateFormatHandling = format
        });

        writer.WriteDateTimeOffset(value);

        var json = JsonConvert.SerializeObject(value, new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatHandling = (DateFormatHandling)format
        });
        Assert.Equal(JsonConvert.DeserializeObject<string>(json), writer.EncodeToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteDateTimeUsesCultureAndCustomFormat(bool withOffset)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var format = "dddd dd MMMM yyyy HH:mm:ss.FFFFFFF zzz";
        var value = new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromHours(9)).AddTicks(1234567);
        using var writer = new Utf8TextWriter(new DoxSerializerSettings
        {
            Culture = culture,
            DateFormatString = format,
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        });

        if (withOffset)
        {
            writer.WriteDateTimeOffset(value);
            Assert.Equal(value.ToString(format, culture), writer.EncodeToString());
        }
        else
        {
            writer.WriteDateTime(value.UtcDateTime);
            Assert.Equal(value.UtcDateTime.ToString(format, culture), writer.EncodeToString());
        }
    }

    [Theory]
    [InlineData(TimestampKind.LocalDate, "2026-10-04")]
    [InlineData(TimestampKind.LocalTime, "12:34:56.0000001")]
    [InlineData(TimestampKind.LocalDateTime, "2026-10-04T12:34:56.0000001")]
    [InlineData(TimestampKind.OffsetDateTime, "2026-10-04T12:34:56.0000001Z")]
    public void WriteTimestampUsesIsoDespiteSettings(TimestampKind kind, string expected)
    {
        using var writer = new Utf8TextWriter(new DoxSerializerSettings
        {
            Culture = CultureInfo.GetCultureInfo("fr-FR"),
            DateFormatString = "dddd",
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Local
        });

        writer.WriteTimestamp(new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc).AddTicks(1), kind);

        Assert.Equal(expected, writer.EncodeToString());
    }

    [Fact]
    public void WriteTimestampPreservesOffsetAndFractionalTicks()
    {
        using var writer = new Utf8TextWriter(new DoxSerializerSettings { DateFormatString = "yyyy" });

        writer.WriteTimestamp(new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromMinutes(-330)).AddTicks(1234567));

        Assert.Equal("2026-10-04T12:34:56.1234567-05:30", writer.EncodeToString());
    }

    [Theory]
    [InlineData(TimestampKind.LocalDate, "2026-10-04")]
    [InlineData(TimestampKind.LocalTime, "12:34:56")]
    [InlineData(TimestampKind.LocalDateTime, "2026-10-04T12:34:56")]
    [InlineData(TimestampKind.OffsetDateTime, "2026-10-04T12:34:56Z")]
    public void WriteTimestampOmitsEmptyFraction(TimestampKind kind, string expected)
    {
        using var writer = new Utf8TextWriter(SerializerSettings.Default);

        writer.WriteTimestamp(new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc), kind);

        Assert.Equal(expected, writer.EncodeToString());
    }

    [Fact]
    public void WriteOffsetTimestampOmitsEmptyFraction()
    {
        using var writer = new Utf8TextWriter(SerializerSettings.Default);

        writer.WriteTimestamp(new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromHours(9)));

        Assert.Equal("2026-10-04T12:34:56+09:00", writer.EncodeToString());
    }
}