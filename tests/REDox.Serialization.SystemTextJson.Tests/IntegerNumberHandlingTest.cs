using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

public class IntegerNumberHandlingTest
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var typeCase in new (Type Type, string Min, string Max)[]
                 {
                     (typeof(sbyte), "-128", "127"), (typeof(byte), "0", "255"),
                     (typeof(short), "-32768", "32767"), (typeof(ushort), "0", "65535"),
                     (typeof(int), "-2147483648", "2147483647"), (typeof(uint), "0", "4294967295"),
                     (typeof(long), "-9223372036854775808", "9223372036854775807"),
                     (typeof(ulong), "0", "18446744073709551615"),
                     (typeof(Int128), "-170141183460469231731687303715884105728", "170141183460469231731687303715884105727"),
                     (typeof(UInt128), "0", "340282366920938463463374607431768211455")
                 })
        {
            foreach (var handling in new[] { JsonNumberHandling.Strict, JsonNumberHandling.AllowReadingFromString })
            {
                yield return new object[] { typeCase.Type, typeCase.Min, typeCase.Max, handling };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadMatchesSystemTextJson(Type type, string min, string max, JsonNumberHandling handling)
    {
        var options = new JsonSerializerOptions { NumberHandling = handling };
        var settings = new SystemTextJsonSerializerSettings(options);
        var overflow = (BigInteger.Parse(max, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        var underflow = (BigInteger.Parse(min, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture);
        foreach (var json in new[]
                 {
                     "0", "42", min, max, "\"42\"", $"\"{min}\"", $"\"{max}\"",
                     $"\"{overflow}\"", $"\"{underflow}\"", "\"1.2\"", "\"1e2\"",
                     "\"42suffix\"", "\"invalid\"", "\"\"", "\" 42\"", "\"42 \"",
                     "\"4\\u0032\""
                 })
        {
            object? expected;
            try
            {
                expected = JsonSerializer.Deserialize(json, type, options);
            }
            catch (JsonException)
            {
                var error = Record.Exception(() => Json.JsonSerializer.Deserialize(json, type, settings));
                Assert.True(error != null, $"{type.Name}, {handling}, {json}: expected rejection");
                continue;
            }

            object? actual = null;
            var readError = Record.Exception(() => actual = Json.JsonSerializer.Deserialize(json, type, settings));
            Assert.True(readError == null, $"{type.Name}, {handling}, {json}: {readError}");
            Assert.Equal(expected, actual);
        }
    }
}