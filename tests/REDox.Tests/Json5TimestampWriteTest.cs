// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using Newtonsoft.Json;
using REDox.Json;

namespace REDox.Tests;

public class Json5TimestampWriteTest
{
    [Theory]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Local)]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Unspecified)]
    public void DefaultTimestampHonorsZoneHandling(DateTimeKind kind, Serialization.DateTimeZoneHandling zone)
    {
        using var doc = Json5Document.Parse("{date:null}", new DoxSerializerSettings { DateTimeZoneHandling = zone });
        var date = new DateTime(2026, 10, 4, 12, 34, 56, kind).AddTicks(1);
        doc.RootElement.AsObject()["date"] = date;
        var json = JsonConvert.SerializeObject(date, new JsonSerializerSettings
        {
            DateTimeZoneHandling = (DateTimeZoneHandling)zone
        });

        var encoded = Json5Document.EncodeToString(doc.RootElement);

        using var reparsed = Json5Document.Parse(encoded);
        Assert.Equal(JsonConvert.DeserializeObject<string>(json),
            reparsed.RootElement.GetProperty("date").GetString());
    }

    [Theory]
    [InlineData(Json5QuoteStyle.AlwaysDouble)]
    [InlineData(Json5QuoteStyle.AlwaysSingle)]
    public void CustomTimestampFormatIsEscapedWithSelectedQuoteStyle(Json5QuoteStyle style)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var format = "dd MMMM yyyy '\"quoted\"' \"'single'\" '\\\\path'\nHH:mm:ss.FFFFFFF 'あ'";
        using var doc = Json5Document.Parse("{date:null}", new DoxSerializerSettings
        {
            Culture = culture,
            DateFormatString = format,
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        });
        var date = new DateTime(2026, 10, 4, 12, 34, 56).AddTicks(1234567);
        doc.RootElement.AsObject()["date"] = date;

        var encoded = Json5Document.EncodeToString(doc.RootElement.GetProperty("date"),
            new Json5WriteOptions { StringStyle = style });

        var quote = style == Json5QuoteStyle.AlwaysDouble ? '"' : '\'';
        Assert.Equal(quote, encoded[0]);
        Assert.Equal(quote, encoded[^1]);
        using var reparsed = Json5Document.Parse(encoded);
        Assert.Equal(date.ToString(format, culture), reparsed.RootElement.GetString());
    }

    [Fact]
    public void LongCustomFormatFallsBackToIso()
    {
        using var doc = Json5Document.Parse("{date:null}", new DoxSerializerSettings
        {
            DateFormatString = "yyyy '" + new string('あ', 80) + "'",
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Utc
        });
        doc.RootElement.AsObject()["date"] = new DateTime(2026, 10, 4, 12, 34, 56).AddTicks(1);

        using var reparsed = Json5Document.Parse(Json5Document.EncodeToString(doc.RootElement));

        Assert.Equal("2026-10-04T12:34:56.0000001Z", reparsed.RootElement.GetProperty("date").GetString());
    }

    [Fact]
    public void DefaultTimestampUsesMicrosoftFormat()
    {
        using var doc = Json5Document.Parse("{date:null}", new DoxSerializerSettings
        {
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        });
        doc.RootElement.AsObject()["date"] = DateTime.UnixEpoch;

        using var reparsed = Json5Document.Parse(Json5Document.EncodeToString(doc.RootElement));
        Assert.Equal("/Date(0)/", reparsed.RootElement.GetProperty("date").GetString());
    }

    [Theory]
    [InlineData(TimestampKind.LocalDate, "2026-10-04")]
    [InlineData(TimestampKind.LocalTime, "12:34:56.0000001")]
    [InlineData(TimestampKind.LocalDateTime, "2026-10-04T12:34:56.0000001")]
    [InlineData(TimestampKind.OffsetDateTime, "2026-10-04T12:34:56.0000001+00:00")]
    public void ExplicitTimestampKindUsesIso(TimestampKind kind, string expected)
    {
        using var doc = Json5Document.Parse("{date:null}", new DoxSerializerSettings
        {
            DateFormatString = "yyyy",
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Local
        });
        doc.RootElement.AsObject()["date"] =
            DValue.Create(new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc).AddTicks(1), kind);

        using var reparsed = Json5Document.Parse(Json5Document.EncodeToString(doc.RootElement));
        Assert.Equal(expected, reparsed.RootElement.GetProperty("date").GetString());
    }

    [Fact]
    public void OffsetTimestampPreservesOriginalOffset()
    {
        using var doc = Json5Document.Parse("{date:null}", new DoxSerializerSettings
        {
            DateFormatString = "yyyy",
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Utc
        });
        doc.RootElement.AsObject()["date"] =
            new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromMinutes(-330)).AddTicks(1234567);

        using var reparsed = Json5Document.Parse(Json5Document.EncodeToString(doc.RootElement));
        Assert.Equal("2026-10-04T12:34:56.1234567-05:30", reparsed.RootElement.GetProperty("date").GetString());
    }
}