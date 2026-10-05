using System.Globalization;
using System.Numerics;
using Newtonsoft.Json;

namespace REDox.Serialization.NewtonsoftJson.Tests;

public sealed class BigIntegerCultureCompatibilityTest
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    [InlineData("")]
    public void BigIntegerConversionMatchesFrameworkAcrossCultures(string cultureName)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        // An arbitrary sign makes any accidental use of the ambient number format observable.
        culture.NumberFormat.NegativeSign = "~";
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            var settings = new NewtonsoftJsonSerializerSettings(new JsonSerializerSettings());
            var large = BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture);
            foreach (var value in new[] { -large, large, BigInteger.MinusOne, BigInteger.Zero })
            {
                var json = JsonConvert.SerializeObject(value);
                Assert.Equal(json, Json.JsonSerializer.Serialize(value, settings));
                Assert.Equal(JsonConvert.DeserializeObject<BigInteger>(json),
                    Json.JsonSerializer.Deserialize<BigInteger>(json, settings));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}