using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using REDox.Json;
using REDox.Tests;

namespace REDox.Serialization.DataContractJson.Tests;

public sealed class DataContractJsonCompatibility
{
    private static readonly string[] CultureNames = { "", "pt-BR", "sv-SE", "ja-JP", "th-TH" };
    private readonly ITestOutputHelper _output;

    public DataContractJsonCompatibility(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<System.Runtime.Serialization.Json.DataContractJsonSerializerSettings> Settings
    {
        get
        {
            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("yyyy/MM/dd")
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings();

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz")
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                EmitTypeInformation = EmitTypeInformation.Always
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                EmitTypeInformation = EmitTypeInformation.AsNeeded
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true,
                EmitTypeInformation = EmitTypeInformation.Always
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                SerializeReadOnlyTypes = true
            };

            yield return new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                IgnoreExtensionDataObject = true
            };
        }
    }

    public static IEnumerable<object[]> GetDeserializeData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetInstances())
            {
                yield return new[] { inst, settings };
            }
        }
    }

    public static IEnumerable<object[]> GetSerializeData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetSerializeInstances())
            {
                yield return new[] { inst, settings };
            }
        }
    }

    public static IEnumerable<object[]> GetTransitionData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetTransitionInstances())
            {
                yield return new[] { inst.src, inst.dest, settings };
            }
        }
    }

    public static IEnumerable<object[]> GetSerializeCultureData()
    {
        return AddCultureVariations(GetSerializeData());
    }

    public static IEnumerable<object[]> GetDeserializeCultureData()
    {
        return AddCultureVariations(GetDeserializeData());
    }

    public static IEnumerable<object[]> GetTransitionCultureData()
    {
        return AddCultureVariations(GetTransitionData());
    }

    private static IEnumerable<object[]> AddCultureVariations(IEnumerable<object[]> source)
    {
        foreach (var row in source)
        {
            foreach (var cultureName in CultureNames)
            {
                var variation = new object[row.Length + 1];
                Array.Copy(row, variation, row.Length);
                variation[row.Length] = cultureName;
                yield return variation;
            }
        }
    }

    private static void WithCurrentCulture(string cultureName, Action action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [MemberData(nameof(GetSerializeCultureData))]
    public void SerializeWithCurrentCulture(object inst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings, string cultureName)
    {
        WithCurrentCulture(cultureName, () => Serialize(inst, settings));
    }

    [Theory]
    [MemberData(nameof(GetDeserializeCultureData))]
    public void DeserializeWithCurrentCulture(object inst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings, string cultureName)
    {
        WithCurrentCulture(cultureName, () => Deserialize(inst, settings));
    }

    [Theory]
    [MemberData(nameof(GetTransitionCultureData))]
    public void TransitionWithCurrentCulture(object src, object dst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings, string cultureName)
    {
        WithCurrentCulture(cultureName, () => Transition(src, dst, settings));
    }

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void Serialize(object inst, System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var doxsettings = new DataContractJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, out var sjson, out var exception))
        {
            _output.WriteLine(exception!.ToString());
            return;
        }

        Assert.NotNull(sjson);

        var tjson = JsonSerializer.Serialize(inst, type, doxsettings);

        Assert.Equal(sjson, tjson);
    }

    [Theory]
    [MemberData(nameof(GetDeserializeData))]
    public void Deserialize(object inst, System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var doxsettings = new DataContractJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, out var json, out _))
        {
            return;
        }

        Assert.NotNull(json);

        if (!TryParseJson(type, json, settings, out var sinst))
        {
            return;
        }

        if (!TryCreateJson(type, sinst, settings, out var sjson, out _))
        {
            return;
        }

        var dinst = JsonSerializer.Deserialize(json, type, doxsettings);

        if (!TryCreateJson(type, dinst, settings, out var djson, out _))
        {
            return;
        }

        Assert.Equal(sjson, djson);
    }

    [Theory]
    [MemberData(nameof(GetTransitionData))]
    public void Transition(object src, object dst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var doxsettings = new DataContractJsonSerializerSettings(settings);

        var stype = src.GetType();
        var dtype = dst.GetType();

        if (!TryCreateJson(stype, src, settings, out var sjson, out _))
        {
            return;
        }

        Assert.NotNull(sjson);

        var djson = JsonSerializer.Serialize(src, stype, doxsettings);

        Assert.Equal(sjson, djson);

        if (!TryParseJson(dst.GetType(), sjson, settings, out var sinst))
        {
            return;
        }

        var doc = JsonDocument.Parse(djson, doxsettings);

        var dinst = doc.RootElement.ToObject(dtype);

        if (!TryCreateJson(dtype, sinst, settings, out var sjson2, out _))
        {
            return;
        }

        Assert.NotNull(sjson2);

        var djson2 = JsonSerializer.Serialize(dinst, dtype, doxsettings);

        Assert.Equal(sjson2, djson2);
        Assert.Equal(sinst?.GetType(), dinst?.GetType());
    }

    private bool TryCreateJson(Type type, object? inst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings, out string? json,
        out Exception? exception)
    {
        try
        {
            var sz = new DataContractJsonSerializer(type, settings);

            using (var ms = new MemoryStream())
            {
                sz.WriteObject(ms, inst);

                json = Encoding.UTF8.GetString(ms.ToArray());
                exception = null;
                return true;
            }
        }
        catch (Exception ex)
        {
            json = null;
            exception = ex;
            return false;
        }
    }

    private bool TryParseJson(Type type, string json,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings, out object? value)
    {
        try
        {
            var sz = new DataContractJsonSerializer(type, settings);

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                value = sz.ReadObject(ms);
                return true;
            }
        }
        catch (Exception)
        {
            value = null;
            return false;
        }
    }
}