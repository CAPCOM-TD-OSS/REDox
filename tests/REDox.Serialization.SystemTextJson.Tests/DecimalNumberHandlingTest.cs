using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

public class DecimalNumberHandlingTest
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
                     "\"nan\"", "\" 1.2\"", "\"1.2 \"", "\"invalid\"", "\"1e2\"",
                     "\"1\\u002e2\"", "\"+1.2\"", "\"1.2suffix\""
                 })
        {
            AssertMatches<decimal>(json, options, settings);
        }
    }

    [Theory]
    [InlineData("\"79228162514264337593543950335\"")]
    [InlineData("\"-79228162514264337593543950335\"")]
    [InlineData("\"79228162514264337593543950336\"")]
    [InlineData("\"1234567890.1234567890123456789\"")]
    public void DecimalStringPreservesPrecisionAndChecksRange(string json)
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        AssertMatches<decimal>(json, options, new SystemTextJsonSerializerSettings(options));
    }

    private static void AssertMatches<T>(string json, JsonSerializerOptions options, SerializerSettings settings)
    {
        T expected;
        try
        {
            expected = JsonSerializer.Deserialize<T>(json, options)!;
        }
        catch (JsonException)
        {
            var error = Record.Exception(() => Json.JsonSerializer.Deserialize<T>(json, settings));
            Assert.True(error != null, $"{typeof(T).Name}, {options.NumberHandling}, {json}: expected rejection");
            return;
        }

        T actual = default!;
        var readError = Record.Exception(() => actual = Json.JsonSerializer.Deserialize<T>(json, settings)!);
        Assert.True(readError == null, $"{typeof(T).Name}, {options.NumberHandling}, {json}: {readError}");
        Assert.Equal(expected, actual);
    }
}