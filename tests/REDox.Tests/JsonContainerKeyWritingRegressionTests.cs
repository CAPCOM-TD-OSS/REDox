// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class JsonContainerKeyWritingRegressionTests
{
    public static TheoryData<string, bool, Json5QuoteStyle, bool> Json5KeyCases()
    {
        var cases = new TheoryData<string, bool, Json5QuoteStyle, bool>();
        foreach (var shape in new[] { "map", "array", "nestedMap" })
        {
            foreach (var extended in new[] { false, true })
            {
                foreach (var style in Enum.GetValues<Json5QuoteStyle>())
                {
                    cases.Add(shape, extended, style, false);
                    cases.Add(shape, extended, style, true);
                }
            }
        }

        return cases;
    }

    [Theory]
    [InlineData("map", false)]
    [InlineData("map", true)]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("nestedMap", false)]
    [InlineData("nestedMap", true)]
    public void JsonDocumentShouldWriteContainerKeysAsJsonStrings(string shape, bool extended)
    {
        var map = CreateMap(shape, out var expectedKey);
        using var document = JsonDocument.Parse("0");
        document.RootElement.AsValue().ReplaceWith(map.AsElement());
        SetKeyExtensionState(document, extended);

        var output = JsonDocument.EncodeToString(document.RootElement);

        using var reparsed = JsonDocument.Parse(output);
        var pair = reparsed.RootElement.EnumerateMap().Single();
        Assert.Equal(expectedKey, pair.Key.GetString());
        Assert.Equal(9, pair.Value.GetInt32());
    }

    [Theory]
    [MemberData(nameof(Json5KeyCases))]
    public void Json5DocumentShouldWriteContainerKeysAsQuotedJsonStrings(
        string shape, bool extended, Json5QuoteStyle style, bool preserveTrivia)
    {
        var map = CreateMap(shape, out var expectedKey);
        using var document = Json5Document.Parse("0",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        document.RootElement.AsValue().ReplaceWith(map.AsElement());
        SetKeyExtensionState(document, extended);
        document.RootElement.AsValue().LeadingTrivia.AddFirst("head", TriviaKind.BlockComment);
        document.TrailingTrivia.AddLast("tail", TriviaKind.BlockComment);

        var output = Json5Document.EncodeToString(document.RootElement, new Json5WriteOptions
        {
            PropertyNameStyle = style,
            PreserveTrivia = preserveTrivia
        });

        using var reparsed = Json5Document.Parse(output,
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var pair = reparsed.RootElement.EnumerateMap().Single();
        Assert.Equal(expectedKey, pair.Key.GetString());
        Assert.Equal(9, pair.Value.GetInt32());
        Assert.Equal(((int)style & 1) == 0
                ? DTokenVariant.StringMultilineDoubleQuote
                : DTokenVariant.StringMultilineSingleQuote,
            pair.Key.Token.Variant);
        Assert.Equal(preserveTrivia, output.Contains("/*head*/"));
        Assert.Equal(preserveTrivia, output.Contains("/*tail*/"));
    }

    [Theory]
    [InlineData("map")]
    [InlineData("array")]
    [InlineData("nestedMap")]
    public void Json5ShouldWriteContainerKeysFromDoxNodeDocuments(string shape)
    {
        var map = CreateMap(shape, out var expectedKey);

        var output = Json5Document.EncodeToString(map.AsElement());

        using var reparsed = Json5Document.Parse(output);
        var pair = reparsed.RootElement.EnumerateMap().Single();
        Assert.Equal(expectedKey, pair.Key.GetString());
        Assert.Equal(9, pair.Value.GetInt32());
    }

    private static void SetKeyExtensionState(Document document, bool extended)
    {
        var key = document.RootElement.EnumerateMap().Single().Key;
        Assert.False(key.Token.IsExtended);
        if (extended)
        {
            key.AsContainer();
        }

        Assert.Equal(extended, key.Token.IsExtended);
    }

    private static DMap CreateMap(string shape, out string expectedKey)
    {
        var inner = new DMap();
        inner.Add("text", "quote\" apostrophe' slash\\ newline\n");
        inner.Add("value", 1);
        var key = inner.AsElement();
        if (shape == "array")
        {
            key = new DArray(new DValue[] { inner, 2 }).AsElement();
        }
        else if (shape == "nestedMap")
        {
            var outer = new DMap();
            outer.Add(new DArray(inner), 3);
            key = outer.AsElement();
        }

        expectedKey = JsonDocument.EncodeToString(key);
        var map = new DMap();
        map.Add(key, 9);
        return map;
    }
}