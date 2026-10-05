// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using Newtonsoft.Json;

namespace REDox.Csv.Tests;

public class CsvTimestampWriteTest
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
        using var doc = CsvDocument.Parse("date,tail\n", new DoxSerializerSettings { DateTimeZoneHandling = zone });
        var date = new DateTime(2026, 10, 4, 12, 34, 56, kind).AddTicks(1);
        doc.RootElement.AsArray()[0].AsArray()[0] = date;
        var json = JsonConvert.SerializeObject(date, new JsonSerializerSettings
        {
            DateTimeZoneHandling = (DateTimeZoneHandling)zone
        });

        Assert.Equal(JsonConvert.DeserializeObject<string>(json) + ",tail\n",
            CsvDocument.EncodeToString(doc.RootElement, new CsvWriteOptions { NewLine = "\n" }));
    }

    [Theory]
    [InlineData("dd MMMM yyyy, HH:mm:ss", ',')]
    [InlineData("dd MMMM yyyy; HH:mm:ss", ';')]
    [InlineData("yyyy-MM-dd '\"quoted\"' HH:mm:ss", ',')]
    [InlineData("yyyy-MM-dd\nHH:mm:ss", ',')]
    public void CustomTimestampFormatIsEscapedAsOneField(string format, char separator)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        using var doc = CsvDocument.Parse($"date{separator}tail\ndate{separator}next\n", new DoxSerializerSettings
        {
            Culture = culture,
            DateFormatString = format,
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        }, new CsvDocumentOptions { SeparatorChar = separator });
        var date = new DateTime(2026, 10, 4, 12, 34, 56);
        doc.RootElement.AsArray()[0].AsArray()[0] = date;
        doc.RootElement.AsArray()[1].AsArray()[0] = date.AddDays(1);

        var csv = CsvDocument.EncodeToString(doc.RootElement,
            new CsvWriteOptions { SeparatorChar = separator, NewLine = "\n" });

        using var reparsed = CsvDocument.Parse(csv, options: new CsvDocumentOptions { SeparatorChar = separator });
        Assert.Equal(2, reparsed.RootElement.GetArrayLength());
        var firstRow = reparsed.RootElement.AsArray()[0].AsArray();
        var secondRow = reparsed.RootElement.AsArray()[1].AsArray();
        Assert.Equal(2, firstRow.Count);
        Assert.Equal(2, secondRow.Count);
        Assert.Equal(date.ToString(format, culture), (string?)firstRow[0]);
        Assert.Equal("tail", (string?)firstRow[1]);
        Assert.Equal(date.AddDays(1).ToString(format, culture), (string?)secondRow[0]);
        Assert.Equal("next", (string?)secondRow[1]);
    }

    [Fact]
    public void DefaultTimestampUsesMicrosoftFormat()
    {
        using var doc = CsvDocument.Parse("date\n", new DoxSerializerSettings
        {
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        });
        doc.RootElement.AsArray()[0].AsArray()[0] = DateTime.UnixEpoch;

        Assert.Equal("/Date(0)/\n",
            CsvDocument.EncodeToString(doc.RootElement, new CsvWriteOptions { NewLine = "\n" }));
    }

    [Theory]
    [InlineData(TimestampKind.LocalDate, "2026-10-04")]
    [InlineData(TimestampKind.LocalTime, "12:34:56.0000001")]
    [InlineData(TimestampKind.LocalDateTime, "2026-10-04T12:34:56.0000001")]
    [InlineData(TimestampKind.OffsetDateTime, "2026-10-04T12:34:56.0000001+00:00")]
    public void ExplicitTimestampKindUsesIso(TimestampKind kind, string expected)
    {
        using var doc = CsvDocument.Parse("date\n", new DoxSerializerSettings
        {
            DateFormatString = "yyyy",
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Local
        });
        doc.RootElement.AsArray()[0].AsArray()[0] =
            DValue.Create(new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc).AddTicks(1), kind);

        Assert.Equal(expected + "\n",
            CsvDocument.EncodeToString(doc.RootElement, new CsvWriteOptions { NewLine = "\n" }));
    }

    [Fact]
    public void OffsetTimestampPreservesOriginalOffset()
    {
        using var doc = CsvDocument.Parse("date\n", new DoxSerializerSettings
        {
            DateFormatString = "yyyy",
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Utc
        });
        doc.RootElement.AsArray()[0].AsArray()[0] =
            new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromMinutes(330)).AddTicks(1234567);

        Assert.Equal("2026-10-04T12:34:56.1234567+05:30\n",
            CsvDocument.EncodeToString(doc.RootElement, new CsvWriteOptions { NewLine = "\n" }));
    }

    [Fact]
    public void LongCustomUtf8FormatFallsBackToIsoAcrossRows()
    {
        var format = "yyyy-MM-dd, '" + new string('あ', 80) + "'";
        using var doc = CsvDocument.Parse("date,tail\ndate,next\n", new DoxSerializerSettings { DateFormatString = format });
        var date = new DateTime(2026, 10, 4);
        doc.RootElement.AsArray()[0].AsArray()[0] = date;
        doc.RootElement.AsArray()[1].AsArray()[0] = date.AddDays(1);

        var csv = CsvDocument.EncodeToString(doc.RootElement, new CsvWriteOptions { NewLine = "\n" });

        Assert.Equal("2026-10-04T00:00:00,tail\n2026-10-05T00:00:00,next\n", csv);
        using var reparsed = CsvDocument.Parse(csv);
        Assert.Equal(2, reparsed.RootElement.GetArrayLength());
        var firstRow = reparsed.RootElement.AsArray()[0].AsArray();
        var secondRow = reparsed.RootElement.AsArray()[1].AsArray();
        Assert.Equal(2, firstRow.Count);
        Assert.Equal(2, secondRow.Count);
        Assert.Equal("2026-10-04T00:00:00", (string?)firstRow[0]);
        Assert.Equal("2026-10-05T00:00:00", (string?)secondRow[0]);
    }
}