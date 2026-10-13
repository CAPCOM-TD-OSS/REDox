// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace REDox.Ini.Tests;

public sealed class IniScanTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(255)]
    public void LongNamesValuesAndCommentsShouldPreserveValuesAndTrivia(int length)
    {
        var padding = new string('a', length);
        var section = "section-" + padding + "日本語";
        var key = "key-" + padding;
        var value = "value-" + padding + ";embedded#fragment-日本語";

        foreach (var newLine in new[] { "\n", "\r\n", "\r" })
        foreach (var inlineComments in new[] { false, true })
        {
            var input = $"; {padding}{newLine}[ {section} ] ; {padding}{newLine}" +
                        $"{key} = {value}\t ; comment{newLine}tail=end";
            using var document = IniDocument.Parse(input, options: new IniDocumentOptions
            {
                AllowInlineComments = inlineComments,
                PreserveTrivia = true
            });
            var properties = document.RootElement.GetProperty(section);
            Assert.Equal(inlineComments ? value : value + "\t ; comment",
                properties.GetProperty(key).GetString());
            Assert.Equal("end", properties.GetProperty("tail").GetString());
            Assert.Equal(input, IniDocument.EncodeToString(document.RootElement,
                new IniWriteOptions { PreserveTrivia = true, NewLine = newLine == "\r" ? "\n" : newLine }));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingWhitespaceAndInlineCommentsShouldPreserveValues(bool inlineComments)
    {
        var whitespace = new string(' ', 128) + "\t\v\f";
        foreach (var newLine in new[] { "\n", "\r\n", "\r" })
        foreach (var preserveTrivia in new[] { false, true })
        {
            var input = $"global=value{whitespace}{newLine}" +
                        $"[section]{newLine}key=first{whitespace}; comment{newLine}" +
                        $"empty={whitespace}{newLine}last=final{whitespace}";
            using var document = IniDocument.Parse(input, options: new IniDocumentOptions
            {
                AllowInlineComments = inlineComments,
                PreserveTrivia = preserveTrivia
            });
            Assert.Equal("value", document.RootElement.GetProperty("global").GetString());
            var section = document.RootElement.GetProperty("section");
            Assert.Equal(inlineComments ? "first" : "first" + whitespace + "; comment",
                section.GetProperty("key").GetString());
            Assert.Equal("", section.GetProperty("empty").GetString());
            Assert.Equal("final", section.GetProperty("last").GetString());
            if (preserveTrivia)
            {
                Assert.Equal(input, IniDocument.EncodeToString(document.RootElement,
                    new IniWriteOptions { PreserveTrivia = true, NewLine = newLine == "\r" ? "\n" : newLine }));
            }
        }
    }

    [Theory]
    [InlineData(15)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(255)]
    public void DelimitersOnLaterLinesShouldNotCompleteMalformedNames(int length)
    {
        var padding = new string('a', length);
        const string prefix = "; 日本語\n";
        var offset = Encoding.UTF8.GetByteCount(prefix);

        foreach (var newLine in new[] { "\n", "\r", "\r\n" })
        {
            AssertError(prefix + "[" + padding + newLine + "]" + newLine + "key=value",
                "UnterminatedSection", offset);
            AssertError(prefix + padding + newLine + "key=value", "MissingEquals", offset);
        }

        AssertError(prefix + "[" + padding, "UnterminatedSection", offset);
        AssertError(prefix + padding, "MissingEquals", offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(255)]
    public void NullCharactersShouldReportTheirExactBytePosition(int length)
    {
        const string prefix = "; 日本語\nkey=";
        var input = prefix + new string('a', length) + "\0tail";
        AssertError(input, "InvalidValue", Encoding.UTF8.GetByteCount(prefix) + length);
    }

    private static void AssertError(string input, string error, int offset)
    {
        var exception = Assert.ThrowsAny<DocumentParseException>(() => IniDocument.Parse(input));
        Assert.Contains(error, exception.Message);
        Assert.Equal(offset, exception.BytePosition);
        Assert.False(IniDocument.TryParse(Encoding.UTF8.GetBytes(input), out var document));
        Assert.Null(document);
    }
}