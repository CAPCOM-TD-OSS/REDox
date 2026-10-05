using System;
using REDox.Json;

namespace REDox.Tests;

public class MicrosoftDateTimeConversionTest
{
    [Theory]
    [InlineData("+0900")]
    [InlineData("-0500")]
    [InlineData("+0000")]
    public void MicrosoftDateWithOffset_ConvertsUtcInstantToMachineLocalTime(string suffix)
    {
        const long milliseconds = 1705285810000;
        using var document = JsonDocument.Parse($"\"\\/Date({milliseconds}{suffix})\\/\"");
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;

        var actual = (DateTime)document.RootElement.AsValue();

        Assert.Equal(DateTimeKind.Local, actual.Kind);
        Assert.Equal(expected, actual);
    }
}