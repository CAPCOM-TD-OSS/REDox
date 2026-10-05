using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

public class HalfNumberHandlingTest
{
    [Theory]
    [InlineData(JsonNumberHandling.Strict)]
    [InlineData(JsonNumberHandling.AllowReadingFromString)]
    [InlineData(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [InlineData(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public void ReadMatchesSystemTextJson(JsonNumberHandling handling)
    {
        var options = new JsonSerializerOptions { NumberHandling = handling };
        var settings = new SystemTextJsonSerializerSettings(options);
        foreach (var json in new[]
                 {
                     "1.2", "\"1.2\"", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"",
                     "\" 1.2\"", "\"1.2 \"", "\"invalid\"", "\"1e2\"",
                     "\"1\\u002e2\"", "\"1,234.5\""
                 })
        {
            Half expected;
            try
            {
                expected = JsonSerializer.Deserialize<Half>(json, options);
            }
            catch (JsonException)
            {
                Assert.ThrowsAny<Exception>(() => Json.JsonSerializer.Deserialize<Half>(json, settings));
                continue;
            }

            Half actual = default;
            var error = Record.Exception(() => actual = Json.JsonSerializer.Deserialize<Half>(json, settings));
            Assert.True(error == null, $"Input {json}: {error}");
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(JsonNumberHandling.AllowReadingFromString)]
    [InlineData(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public void ReadingFromStringAllowsLowercaseNaN(JsonNumberHandling handling)
    {
        // REDox intentionally accepts the lowercase spelling supported by Half.TryParse.
        var settings = new SystemTextJsonSerializerSettings(new JsonSerializerOptions { NumberHandling = handling });

        Assert.True(Half.IsNaN(Json.JsonSerializer.Deserialize<Half>("\"nan\"", settings)));
    }
}