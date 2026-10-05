using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

public class FloatingPointNumberHandlingTest
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
                     "\"1\\u002e2\"", "\"+1.2\"", "\"1.2suffix\""
                 })
        {
            AssertMatches<float>(json, options, settings);
            AssertMatches<double>(json, options, settings);
        }
    }

    [Theory]
    [InlineData(JsonNumberHandling.AllowReadingFromString)]
    [InlineData(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public void ReadingFromStringAllowsLowercaseNaN(JsonNumberHandling handling)
    {
        // REDox intentionally accepts the lowercase spelling supported by Utf8Parser.
        var settings = new SystemTextJsonSerializerSettings(new JsonSerializerOptions { NumberHandling = handling });

        Assert.True(float.IsNaN(Json.JsonSerializer.Deserialize<float>("\"nan\"", settings)));
        Assert.True(double.IsNaN(Json.JsonSerializer.Deserialize<double>("\"nan\"", settings)));
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