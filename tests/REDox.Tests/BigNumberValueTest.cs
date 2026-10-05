using System.Text;
using REDox.Json;

namespace REDox.Tests;

public class BigNumberValueTest
{
    [Theory]
    [InlineData("-9223372036854775808", "-9223372036854775808")]
    [InlineData("18446744073709551615", "18446744073709551615")]
    [InlineData("1.25", "1.25")]
    [InlineData("1e2", "100")]
    public void NumericValuesCanBeReadWithoutRelaxedConversion(string json, string expected)
    {
        using var document = JsonDocument.Parse(json,
            new DoxSerializerSettings { AllowRelaxedScalarConversion = false });

        Assert.Equal(expected, Encoding.UTF8.GetString(document.RootElement.GetBigNumber()));
        Assert.True(document.RootElement.TryGetBigNumber(out var value));
        Assert.Equal(expected, Encoding.UTF8.GetString(value));
    }

    [Fact]
    public void DecimalValuePreservesPrecision()
    {
        var value = DValue.From(1234567890.1234567890123456789m,
            new DoxSerializerSettings { AllowRelaxedScalarConversion = false });

        Assert.Equal("1234567890.1234567890123456789",
            Encoding.UTF8.GetString(value.AsElement().GetBigNumber()));
    }
}