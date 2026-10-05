using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class CultureCompatibilityTest
{
    private static void WithCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void NumericValueToStringMatchesJsonNode(string culture)
    {
        WithCulture(culture, () =>
        {
            Assert.Equal(JsonValue.Create(1.23)!.ToString(), DValue.Create(1.23).ToString());
            Assert.Equal(JsonValue.Create(-1.23)!.ToString(), DValue.Create(-1.23).ToString());
            Assert.Equal(JsonValue.Create(1.2300m)!.ToString(), DValue.Create(1.2300m).ToString());
            Assert.Equal(JsonValue.Create(-42L)!.ToString(), DValue.Create(-42L).ToString());
            Assert.Equal(JsonValue.Create(ulong.MaxValue)!.ToString(), DValue.Create(ulong.MaxValue).ToString());
        });
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void NumericStringsDecodeTheSameInParsedAndCreatedValues(string culture)
    {
        WithCulture(culture, () =>
        {
            using var document = JsonDocument.Parse("\"42.75\"");
            var parsed = document.RootElement.AsValue();
            var created = DValue.Create("42.75");
            Assert.Equal(42.75, (double)parsed);
            Assert.Equal((double)parsed, (double)created);
            Assert.Equal(42.75m, (decimal)parsed);
            Assert.Equal((decimal)parsed, (decimal)created);
            var parsedReader = new DataReader(document.RootElement);
            var createdReader = new DataReader(created.AsElement());
            Assert.Equal(parsedReader.ReadDouble(parsedReader.RootId), createdReader.ReadDouble(createdReader.RootId));
            Assert.Equal(parsedReader.ReadDecimal(parsedReader.RootId), createdReader.ReadDecimal(createdReader.RootId));
            Assert.Equal(-42L, (long)DValue.Create("-42"));
            Assert.Equal(42UL, (ulong)DValue.Create("42"));
        });
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    public void RelaxedDatesUseInvariantCultureByDefault(string culture)
    {
        WithCulture(culture, () =>
        {
            const string json = "\"01/15/2024 10:30:10\"";
            using var document = JsonDocument.Parse(json);
            var expected = (DateTime)JToken.Parse(json);
            var actual = (DateTime)document.RootElement.AsValue();
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal((DateTimeOffset)JToken.Parse(json), (DateTimeOffset)document.RootElement.AsValue());
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dd MMMM yyyy HH:mm:ss")]
    public void RelaxedDatesHonorExplicitCulture(string? format)
    {
        WithCulture("pt-BR", () =>
        {
            var settings = new DoxSerializerSettings
            {
                Culture = CultureInfo.GetCultureInfo("fr-FR"),
                DateFormatString = format
            };
            using var document = JsonDocument.Parse("\"02 janvier 2024 03:04:05\"", settings);
            Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5), (DateTime)document.RootElement.AsValue());
            Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5),
                ((DateTimeOffset)document.RootElement.AsValue()).DateTime);
        });
    }

    [Fact]
    public void IntegerStringsIgnoreAmbientSignSymbols()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "~";
            culture.NumberFormat.PositiveSign = "!";
            CultureInfo.CurrentCulture = culture;
            var signed = new DataReader(DValue.Create("-42").AsElement());
            var unsigned = new DataReader(DValue.Create("+42").AsElement());
            Assert.Equal(-42L, signed.ReadInt64(signed.RootId));
            Assert.Equal(42UL, unsigned.ReadUInt64(unsigned.RootId));
            Assert.Equal("-42", DValue.Create(-42).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NullCultureExplicitlyUsesAmbientCulture()
    {
        WithCulture("fr-FR", () =>
        {
            using var document = JsonDocument.Parse("\"02 janvier 2024 03:04:05\"",
                new DoxSerializerSettings { Culture = null });
            Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5), (DateTime)document.RootElement.AsValue());
        });
    }
}