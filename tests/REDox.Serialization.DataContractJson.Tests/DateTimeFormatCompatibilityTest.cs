using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using REDox.Json;
using FrameworkSettings = System.Runtime.Serialization.Json.DataContractJsonSerializerSettings;

namespace REDox.Serialization.DataContractJson.Tests;

public class DateTimeFormatCompatibilityTest
{
    private static string SerializeFramework(object value, Type type, FrameworkSettings settings)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(type, settings).WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static object? DeserializeFramework(string json, Type type, FrameworkSettings settings)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new DataContractJsonSerializer(type, settings).ReadObject(stream);
    }

    public static IEnumerable<object[]> Formats()
    {
        var japaneseCalendar = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        japaneseCalendar.DateTimeFormat.Calendar = new JapaneseCalendar();
        foreach (var format in new[]
                 {
                     new DateTimeFormat("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                     new DateTimeFormat("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                     new DateTimeFormat("O", CultureInfo.InvariantCulture),
                     new DateTimeFormat("D", CultureInfo.GetCultureInfo("ja-JP")),
                     new DateTimeFormat("dd MMMM yyyy HH:mm:ss", CultureInfo.GetCultureInfo("fr-FR")),
                     new DateTimeFormat("gg y年M月d日 HH:mm:ss", japaneseCalendar),
                     new DateTimeFormat("'" + new string('x', 320) + "'yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                 })
        {
            yield return new object[] { format };
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void ConfiguredFormatsRoundTripLikeFramework(DateTimeFormat format)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var settings = new FrameworkSettings { DateTimeFormat = format };
            var adapter = new DataContractJsonSerializerSettings(settings);
            var value = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567);
            var expectedJson = SerializeFramework(value, typeof(DateTime), settings);
            Assert.Equal(expectedJson, JsonSerializer.Serialize(value, adapter));

            var expected = (DateTime)DeserializeFramework(expectedJson, typeof(DateTime), settings)!;
            var actual = JsonSerializer.Deserialize<DateTime>(expectedJson, adapter);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Kind, actual.Kind);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(DateTimeStyles.None)]
    [InlineData(DateTimeStyles.RoundtripKind)]
    [InlineData(DateTimeStyles.AssumeLocal)]
    [InlineData(DateTimeStyles.AssumeUniversal)]
    [InlineData(DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)]
    public void ParsingStylesAndOffsetsMatchFramework(DateTimeStyles styles)
    {
        var settings = new FrameworkSettings
        {
            DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ss.fffffffK", CultureInfo.InvariantCulture)
            {
                DateTimeStyles = styles
            }
        };
        var adapter = new DataContractJsonSerializerSettings(settings);
        foreach (var suffix in new[] { "", "Z", "+09:00", "-05:00" })
        {
            var json = $"\"2024-01-02T03:04:05.1234567{suffix}\"";
            var expected = (DateTime)DeserializeFramework(json, typeof(DateTime), settings)!;
            var actual = JsonSerializer.Deserialize<DateTime>(json, adapter);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Kind, actual.Kind);
        }
    }

    [Theory]
    [InlineData("2024-01-02T03:04:05.1234567Z")]
    [InlineData("2024-01-02T03:04:05.1234567+09:00")]
    [InlineData("2024-01-02T03:04:05.1")]
    [InlineData("2024-01-02T03:04:05")]
    [InlineData("2024-02-30T03:04:05.1234567")]
    [InlineData("2024/01/02T03:04:05.1234567")]
    public void ExactTimestampFormatRejectsNonMatchingInput(string text)
    {
        var settings = new FrameworkSettings
        {
            DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)
        };
        var json = $"\"{text}\"";
        Assert.ThrowsAny<Exception>(() => DeserializeFramework(json, typeof(DateTime), settings));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<DateTime>(json,
            new DataContractJsonSerializerSettings(settings)));
    }

    [Fact]
    public void DateTimeOffsetDateComponentUsesConfiguredFormat()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var settings = new FrameworkSettings
            {
                DateTimeFormat = new DateTimeFormat("dd MMMM yyyy HH:mm:ss", CultureInfo.GetCultureInfo("fr-FR"))
                {
                    DateTimeStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
                }
            };
            var value = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));
            var expectedJson = SerializeFramework(value, typeof(DateTimeOffset), settings);
            var adapter = new DataContractJsonSerializerSettings(settings);
            Assert.Equal(expectedJson, JsonSerializer.Serialize(value, adapter));
            var expected = (DateTimeOffset)DeserializeFramework(expectedJson, typeof(DateTimeOffset), settings)!;
            var actual = JsonSerializer.Deserialize<DateTimeOffset>(expectedJson, adapter);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Offset, actual.Offset);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}