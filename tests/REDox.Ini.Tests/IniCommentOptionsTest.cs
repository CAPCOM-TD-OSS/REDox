// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.IO;
using System.Text;
using REDox.Json;

namespace REDox.Ini.Tests;

public sealed class IniCommentOptionsTest
{
    [Theory]
    [InlineData(IniCommentSyntax.Both, ';', true)]
    [InlineData(IniCommentSyntax.Both, '#', true)]
    [InlineData(IniCommentSyntax.Semicolon, ';', true)]
    [InlineData(IniCommentSyntax.Semicolon, '#', false)]
    [InlineData(IniCommentSyntax.Hash, ';', false)]
    [InlineData(IniCommentSyntax.Hash, '#', true)]
    public void SelectedDelimitersShouldApplyToFullLinesAndInlineComments(
        IniCommentSyntax syntax, char marker, bool recognized)
    {
        var headerMarker = syntax == IniCommentSyntax.Hash ? '#' : ';';
        var input = $"{marker}name=literal\n[s] {headerMarker} header\n" +
                    $"{marker}sectionKey=sectionValue\nvalue=start {marker}comment\n" +
                    $"first={marker}literal\nembedded=left{marker}right\n";
        foreach (var preserveTrivia in new[] { false, true })
        {
            var options = new IniDocumentOptions
            {
                CommentSyntax = syntax,
                AllowInlineComments = true,
                PreserveTrivia = preserveTrivia
            };
            using var document = IniDocument.Parse(input, options: options);
            Assert.Equal(!recognized, document.RootElement.TryGetProperty(marker + "name", out _));
            var section = document.RootElement.GetProperty("s");
            Assert.Equal(!recognized, section.TryGetProperty(marker + "sectionKey", out _));
            Assert.Equal(recognized ? "start" : $"start {marker}comment", section.GetProperty("value").GetString());
            Assert.Equal(recognized ? "" : $"{marker}literal", section.GetProperty("first").GetString());
            Assert.Equal($"left{marker}right", section.GetProperty("embedded").GetString());
            if (!recognized)
            {
                Assert.Equal("literal", document.RootElement.GetProperty(marker + "name").GetString());
                Assert.Equal("sectionValue", section.GetProperty(marker + "sectionKey").GetString());
            }

            if (preserveTrivia)
            {
                Assert.Equal(input, IniDocument.EncodeToString(document.RootElement,
                    new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }));
            }

            Assert.True(IniDocument.TryParse(Encoding.UTF8.GetBytes(input), out var parsed, options: options));
            using (parsed)
            {
                Assert.Equal(section.GetProperty("value").GetString(),
                    parsed.RootElement.GetProperty("s").GetProperty("value").GetString());
            }
        }
    }

    [Theory]
    [InlineData(IniCommentSyntax.Semicolon, "# not a comment\nkey=value")]
    [InlineData(IniCommentSyntax.Hash, "; not a comment\nkey=value")]
    [InlineData(IniCommentSyntax.Semicolon, "[s] # not a comment\nkey=value")]
    [InlineData(IniCommentSyntax.Hash, "[s] ; not a comment\nkey=value")]
    public void UnselectedDelimitersShouldNotHideMalformedLines(IniCommentSyntax syntax, string input)
    {
        var options = new IniDocumentOptions { CommentSyntax = syntax };
        Assert.ThrowsAny<DocumentParseException>(() => IniDocument.Parse(input, options: options));
        Assert.False(IniDocument.TryParse(Encoding.UTF8.GetBytes(input), out var document, options: options));
        Assert.Null(document);
    }

    [Theory]
    [InlineData(IniCommentSyntax.Both)]
    [InlineData(IniCommentSyntax.Semicolon)]
    [InlineData(IniCommentSyntax.Hash)]
    public void DisabledInlineCommentsShouldRetainBothDelimiters(IniCommentSyntax syntax)
    {
        using var document = IniDocument.Parse("value=first ; second # third\n",
            options: new IniDocumentOptions { CommentSyntax = syntax });
        Assert.Equal("first ; second # third", document.RootElement.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(IniCommentSyntax.Both)]
    [InlineData(IniCommentSyntax.Semicolon)]
    [InlineData(IniCommentSyntax.Hash)]
    public void StreamAndByteInputsShouldHonorCommentSyntaxWithBom(IniCommentSyntax syntax)
    {
        var marker = syntax == IniCommentSyntax.Hash ? '#' : ';';
        var bytes = Encoding.UTF8.GetBytes($"\uFEFF{marker} header\rkey=value {marker} inline\r");
        var options = new IniDocumentOptions { CommentSyntax = syntax, AllowInlineComments = true };
        using var stream = new MemoryStream(bytes);
        using var fromStream = IniDocument.Parse(stream, options: options);
        using var fromArray = IniDocument.Parse(bytes, options: options);
        using var fromMemory = IniDocument.Parse(bytes.AsMemory(), options: options);
        using var fromSpan = IniDocument.Parse(bytes.AsSpan(), options: options);
        foreach (var document in new[] { fromStream, fromArray, fromMemory, fromSpan })
        {
            Assert.Equal("value", document.RootElement.GetProperty("key").GetString());
        }
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon)]
    [InlineData(IniCommentStyle.PreserveOrHash)]
    [InlineData(IniCommentStyle.AlwaysSemicolon)]
    [InlineData(IniCommentStyle.AlwaysHash)]
    public void OutputStyleShouldChangeOnlyDelimitersAndPreserveFormatting(IniCommentStyle style)
    {
        foreach (var newLine in new[] { "\n", "\r\n", "\r" })
        {
            var input = $"; header #body{newLine}[ s ] # section ;body{newLine}" +
                        $"key=left;right#fragment ; inline #body{newLine}# tail ;body";
            using var document = IniDocument.Parse(input,
                options: new IniDocumentOptions { PreserveTrivia = true, AllowInlineComments = true });

            var encoded = IniDocument.EncodeToString(document.RootElement,
                new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" });

            var marker = style == IniCommentStyle.AlwaysHash ? '#' : ';';
            var expected = style is IniCommentStyle.PreserveOrSemicolon or IniCommentStyle.PreserveOrHash
                ? input
                : $"{marker} header #body{newLine}[ s ] {marker} section ;body{newLine}" +
                  $"key=left;right#fragment {marker} inline #body{newLine}{marker} tail ;body";
            Assert.Equal(expected, encoded);
            using var reparsed = IniDocument.Parse(encoded, options: new IniDocumentOptions
            {
                AllowInlineComments = true,
                CommentSyntax = style switch
                {
                    IniCommentStyle.AlwaysHash => IniCommentSyntax.Hash,
                    IniCommentStyle.AlwaysSemicolon => IniCommentSyntax.Semicolon,
                    _ => IniCommentSyntax.Both
                }
            });
            Assert.Equal("left;right#fragment", reparsed.RootElement.GetProperty("s").GetProperty("key").GetString());
        }
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void AddedCommentsShouldUseOutputStyle(IniCommentStyle style, char marker)
    {
        using var document = IniDocument.Parse("key=1");
        var root = document.RootElement.AsObject();
        var trivia = root.GetPropertyEntry("key").LeadingTrivia;
        trivia.AddLast(" added", TriviaKind.LineComment);
        trivia.AddLast(" default");

        var encoded = IniDocument.EncodeToString(root,
            new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" });

        Assert.Equal($"{marker} added\n{marker} default\nkey=1", encoded);
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void ModifiedCommentsShouldUseOutputStyle(IniCommentStyle style, char marker)
    {
        using var document = IniDocument.Parse("# original\nkey=1",
            options: new IniDocumentOptions { PreserveTrivia = true });
        foreach (var trivia in document.RootElement.AsObject().GetPropertyEntry("key").LeadingTrivia)
        {
            if (trivia.Kind == TriviaKind.LineComment)
            {
                trivia.ReplaceWith(" changed");
            }
        }

        Assert.Equal($"{marker} changed\nkey=1", IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" }));
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    public void PreserveStylesShouldUseFallbackOnlyForAddedComments(IniCommentStyle style, char marker)
    {
        using var document = IniDocument.Parse("; prefix\n[s] # header\nkey=1 ; inline\n# tail",
            options: new IniDocumentOptions { PreserveTrivia = true, AllowInlineComments = true });
        var root = document.RootElement.AsObject();
        root.GetPropertyEntry("s").LeadingTrivia.AddFirst(" added section", TriviaKind.LineComment);
        root["s"].AsObject().GetPropertyEntry("key").LeadingTrivia.AddFirst(" added key");

        Assert.Equal($"{marker} added section\n; prefix\n[s] # header\n{marker} added key\nkey=1 ; inline\n# tail",
            IniDocument.EncodeToString(root,
                new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" }));
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void CommentsFromOtherFormatsShouldUseOutputStyle(IniCommentStyle style, char marker)
    {
        using var document = Json5Document.Parse("{\n// added\nkey:1\n}",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        Assert.Equal($"{marker} added\nkey=1", IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" }));
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon)]
    [InlineData(IniCommentStyle.PreserveOrHash)]
    [InlineData(IniCommentStyle.AlwaysSemicolon)]
    [InlineData(IniCommentStyle.AlwaysHash)]
    public void ConvertedSectionCommentsShouldUseOutputStyle(IniCommentStyle style)
    {
        using var document = IniDocument.Parse("; prefix\n[s] # header\n# body\na=1\n",
            options: new IniDocumentOptions { PreserveTrivia = true });
        document.RootElement.AsObject()["s"].ReplaceWith("off");
        var expected = style switch
        {
            IniCommentStyle.AlwaysHash => "# prefix\n# header\n# body\ns=off\n",
            IniCommentStyle.AlwaysSemicolon => "; prefix\n; header\n; body\ns=off\n",
            _ => "; prefix\n# header\n# body\ns=off\n"
        };
        Assert.Equal(expected, IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" }));
    }

    [Fact]
    public void AllOutputApisShouldHonorCommentStyle()
    {
        using var document = IniDocument.Parse("; header\nkey=1\n; tail",
            options: new IniDocumentOptions { PreserveTrivia = true });
        var options = new IniWriteOptions { PreserveTrivia = true, CommentStyle = IniCommentStyle.AlwaysHash, NewLine = "\n" };
        const string expected = "# header\nkey=1\n# tail";
        Assert.Equal(expected, IniDocument.EncodeToString(document.RootElement, options));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), IniDocument.Encode(document.RootElement, options));
        using var stream = new MemoryStream();
        IniDocument.EncodeTo(document.RootElement, stream, options);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), stream.ToArray());
        var buffer = new ArrayBufferWriter<byte>();
        IniDocument.EncodeTo(document.RootElement, buffer, options);
        Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon)]
    [InlineData(IniCommentStyle.PreserveOrHash)]
    [InlineData(IniCommentStyle.AlwaysSemicolon)]
    [InlineData(IniCommentStyle.AlwaysHash)]
    public void CommentStyleShouldNotEnableTriviaOutput(IniCommentStyle style)
    {
        using var document = IniDocument.Parse("; header\nkey=1\n# tail",
            options: new IniDocumentOptions { PreserveTrivia = true });
        Assert.Equal("key=1", IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { CommentStyle = style }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UndefinedCommentOptionsShouldBeRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IniDocumentOptions { CommentSyntax = (IniCommentSyntax)value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new IniWriteOptions { CommentStyle = (IniCommentStyle)value });
    }

    [Fact]
    public void UndefinedCommentSyntaxShouldBeRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IniDocumentOptions { CommentSyntax = (IniCommentSyntax)3 });
    }

    [Fact]
    public void DefaultStyleShouldPreserveSourceDelimitersAndUseSemicolonsForAddedComments()
    {
        Assert.Equal(IniCommentStyle.PreserveOrSemicolon, default(IniWriteOptions).CommentStyle);
        Assert.Equal(IniCommentStyle.PreserveOrSemicolon, new IniWriteOptions().CommentStyle);
        using var document = IniDocument.Parse("# header\nkey=1\n; tail",
            options: new IniDocumentOptions { PreserveTrivia = true });
        document.RootElement.AsObject().GetPropertyEntry("key").LeadingTrivia.AddLast(" added", TriviaKind.LineComment);

        Assert.Equal("# header\n; added\nkey=1\n; tail", IniDocument.EncodeToString(document.RootElement,
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }));
    }
}