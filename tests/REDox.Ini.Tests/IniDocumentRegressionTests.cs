// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Ini.Tests;

public sealed class IniDocumentRegressionTests
{
    [Theory]
    [InlineData("\n", "")]
    [InlineData("\r\n", "")]
    [InlineData("\n", "  ")]
    [InlineData("\r\n", "  ")]
    [InlineData("\n", "; header{0}")]
    [InlineData("\r\n", "; header{0}")]
    [InlineData("\n", "  # header{0}\t")]
    [InlineData("\r\n", "  # header{0}\t")]
    [InlineData("\n", "{0}; header{0}")]
    [InlineData("\r\n", "{0}; header{0}")]
    public void AddingGlobalKeyBeforeFirstSectionShouldProduceValidIni(string newLine, string prefixTemplate)
    {
        var prefix = prefixTemplate.Replace("{0}", newLine);
        var input = $"{prefix}[s]{newLine}a=1{newLine}";
        using var document = IniDocument.Parse(input,
            options: new IniDocumentOptions { PreserveTrivia = true });

        document.RootElement.AsObject().Add("global", "2");

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = newLine });

        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("2", reparsed.RootElement.GetProperty("global").GetString());
        Assert.Equal("1", reparsed.RootElement.GetProperty("s").GetProperty("a").GetString());
        var separator = prefix.StartsWith(newLine) ? "" : newLine;
        Assert.Equal($"global=2{separator}{input}", encoded);
    }

    [Theory]
    [InlineData("\n", "none")]
    [InlineData("\r\n", "none")]
    [InlineData("\n", "object")]
    [InlineData("\r\n", "object")]
    [InlineData("\n", "map")]
    [InlineData("\r\n", "map")]
    public void ReplacingSectionWithScalarShouldProduceGlobalKey(string newLine, string view)
    {
        using var document = IniDocument.Parse($"[s]{newLine}a=1{newLine}",
            options: new IniDocumentOptions { PreserveTrivia = true });

        var section = document.RootElement.GetProperty("s");
        if (view == "object")
        {
            _ = section.AsObject();
        }
        else if (view == "map")
        {
            _ = section.AsMap();
        }

        document.RootElement.AsObject()["s"].ReplaceWith("off");

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = newLine });

        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("off", reparsed.RootElement.GetProperty("s").GetString());
        Assert.Single(reparsed.RootElement.EnumerateObject());
        Assert.Equal($"s=off{newLine}", encoded);
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(true, "true")]
    [InlineData(1.5, "1.5")]
    [InlineData(null, "")]
    public void ReplacingSectionWithNonStringScalarShouldProduceGlobalKey(object? value, string expected)
    {
        using var document = IniDocument.Parse("[s]\na=1\n",
            options: new IniDocumentOptions { PreserveTrivia = true });

        document.RootElement.AsObject()["s"].ReplaceWith(value);

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" });

        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal(expected, reparsed.RootElement.GetProperty("s").GetString());
        Assert.Equal($"s={expected}\n", encoded);
    }

    [Theory]
    [InlineData("\n", ';')]
    [InlineData("\r\n", ';')]
    [InlineData("\n", '#')]
    [InlineData("\r\n", '#')]
    public void ReplacingSectionWithScalarShouldRetainCommentsBeforeGlobalKey(string newLine, char marker)
    {
        var input = $"{marker} before{newLine}[ s ] {marker} header{newLine}" +
                    $"{marker} section comment{newLine}a=1{newLine}";
        using var document = IniDocument.Parse(input,
            options: new IniDocumentOptions { PreserveTrivia = true });

        document.RootElement.AsObject()["s"].ReplaceWith("off");

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, WriteSpaces = true, NewLine = newLine });

        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("off", reparsed.RootElement.GetProperty("s").GetString());
        Assert.Single(reparsed.RootElement.EnumerateObject());
        Assert.Equal($"{marker} before{newLine}{marker} header{newLine}" +
                     $"{marker} section comment{newLine}s = off{newLine}", encoded);
    }

    [Fact]
    public void ReplacingSectionWithScalarShouldPreserveFollowingSection()
    {
        using var document = IniDocument.Parse("global=1\n[replaced]\nold=2\n[remaining]\nkey=3\n",
            options: new IniDocumentOptions { PreserveTrivia = true });

        document.RootElement.AsObject()["replaced"].ReplaceWith("off");

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" });

        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("1", reparsed.RootElement.GetProperty("global").GetString());
        Assert.Equal("off", reparsed.RootElement.GetProperty("replaced").GetString());
        Assert.Equal("3", reparsed.RootElement.GetProperty("remaining").GetProperty("key").GetString());
        Assert.Equal("global=1\nreplaced=off\n[remaining]\nkey=3\n", encoded);
    }

    [Theory]
    [InlineData("a=1\rb=2\r")]
    [InlineData("a=1\r[s]\rb=2\r")]
    [InlineData("[s]\rb=2\r")]
    [InlineData("\r[s]\rb=2\r")]
    [InlineData("; header\r[ s ] # section\r\r a = 1\r; next\r b=2\r; tail")]
    [InlineData("[s]\r[empty]\r[t]\rc=3")]
    public void CrOnlyLineEndingsShouldRoundTripWithoutGeneratedLineBreaks(string input)
    {
        using var document = IniDocument.Parse(input,
            options: new IniDocumentOptions { PreserveTrivia = true });

        foreach (var newLine in new[] { "\n", "\r\n" })
        {
            var encoded = IniDocument.EncodeToString(document.RootElement,
                new IniWriteOptions { PreserveTrivia = true, NewLine = newLine });

            Assert.Equal(input, encoded);
            using var reparsed = IniDocument.Parse(encoded);
        }
    }

    [Fact]
    public void MixedLineEndingsShouldRoundTripWithoutGeneratedLineBreaks()
    {
        const string input = "a=1\r[s]\r\nb=2\n[t]\rc=3\r";
        using var document = IniDocument.Parse(input,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\r\n" });

        Assert.Equal(input, encoded);
        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("1", reparsed.RootElement.GetProperty("a").GetString());
        Assert.Equal("2", reparsed.RootElement.GetProperty("s").GetProperty("b").GetString());
        Assert.Equal("3", reparsed.RootElement.GetProperty("t").GetProperty("c").GetString());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AddingGlobalKeyShouldReuseLeadingCrBeforeFirstSection(string newLine)
    {
        const string input = "\r[s]\ra=1\r";
        using var document = IniDocument.Parse(input,
            options: new IniDocumentOptions { PreserveTrivia = true });

        document.RootElement.AsObject().Add("global", "2");

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = newLine });

        Assert.Equal("global=2" + input, encoded);
        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("2", reparsed.RootElement.GetProperty("global").GetString());
        Assert.Equal("1", reparsed.RootElement.GetProperty("s").GetProperty("a").GetString());
    }

    [Theory]
    [InlineData(';', false)]
    [InlineData('#', false)]
    [InlineData(';', true)]
    [InlineData('#', true)]
    public void ViewingSectionShouldPreserveHeaderWhitespaceAndComments(char commentMarker, bool asMap)
    {
        var input = $"[ s ] {commentMarker}comment\n{commentMarker}explanation\na=1\n";
        using var document = IniDocument.Parse(input,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var section = document.RootElement.GetProperty("s");
        if (asMap)
        {
            _ = section.AsMap();
        }
        else
        {
            _ = section.AsObject();
        }

        var encoded = IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" });

        Assert.Equal(input, encoded);
    }
}