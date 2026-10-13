// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using REDox.Json;

namespace REDox.Ini.Tests;

public sealed class IniMultilineCommentTest
{
    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void AddedMultilineCommentsShouldKeepEveryLineCommented(IniCommentStyle style, char marker)
    {
        foreach (var sourceNewLine in new[] { "\n", "\r\n", "\r" })
        foreach (var outputNewLine in new[] { "\n", "\r\n" })
        foreach (var kind in new[] { TriviaKind.Default, TriviaKind.LineComment, TriviaKind.BlockComment })
        foreach (var trailingNewLine in new[] { "", sourceNewLine })
        {
            using var document = IniDocument.Parse("key=1");
            document.RootElement.AsObject().GetPropertyEntry("key").LeadingTrivia.AddLast(
                $" first{sourceNewLine}{sourceNewLine}injected=2{sourceNewLine}[other]{trailingNewLine}", kind);
            var options = new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = outputNewLine };
            var expected = $"{marker} first{outputNewLine}{marker}{outputNewLine}" +
                           $"{marker}injected=2{outputNewLine}{marker}[other]{outputNewLine}key=1";

            var encoded = document.RootElement.ToIni(options);
            Assert.Equal(expected, encoded);
            Assert.Equal(expected, document.RootElement.AsValue().ToIni(options));
            Assert.Equal(expected, document.RootElement.AsObject().ToIni(options));
            using var reparsed = IniDocument.Parse(encoded);
            Assert.Equal("1", reparsed.RootElement.GetProperty("key").GetString());
            Assert.False(reparsed.RootElement.TryGetProperty("injected", out _));
            Assert.False(reparsed.RootElement.TryGetProperty("other", out _));
        }
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void Json5BlockCommentsShouldBecomeIniLineComments(IniCommentStyle style, char marker)
    {
        foreach (var sourceNewLine in new[] { "\n", "\r\n", "\r" })
        foreach (var outputNewLine in new[] { "\n", "\r\n" })
        {
            using var document = Json5Document.Parse(
                $"{{/* first{sourceNewLine}{sourceNewLine}injected=2 */key:1," +
                $"/* section{sourceNewLine}[other] */s:{{/* body{sourceNewLine}extra=3 */value:2}}}}",
                options: new Json5DocumentOptions { PreserveTrivia = true });
            var options = new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = outputNewLine };
            var expected = $"{marker} first{outputNewLine}{marker}{outputNewLine}{marker}injected=2 {outputNewLine}" +
                           $"key=1{outputNewLine}{marker} section{outputNewLine}{marker}[other] {outputNewLine}" +
                           $"[s]{outputNewLine}{marker} body{outputNewLine}{marker}extra=3 {outputNewLine}value=2";

            var encoded = document.RootElement.ToIni(options);
            Assert.Equal(expected, encoded);
            using var reparsed = IniDocument.Parse(encoded);
            Assert.Equal("1", reparsed.RootElement.GetProperty("key").GetString());
            Assert.Equal("2", reparsed.RootElement.GetProperty("s").GetProperty("value").GetString());
            Assert.False(reparsed.RootElement.TryGetProperty("injected", out _));
            Assert.False(reparsed.RootElement.TryGetProperty("other", out _));
        }
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    public void AddedMultilineSectionHeaderCommentsShouldSurviveSectionReplacement(IniCommentStyle style, char marker)
    {
        using var document = IniDocument.Parse("[s] # header\na=1\n", options: new IniDocumentOptions { PreserveTrivia = true });
        var root = document.RootElement.AsObject();
        root["s"].LeadingTrivia.AddLast(" first\r\nsecond\rthird\n", TriviaKind.LineComment);
        root["s"].ReplaceWith("off");

        Assert.Equal($"# header\n{marker} first\n{marker}second\n{marker}third\ns=off", root.ToIni(
            new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" }));
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    public void AddedCommentsBeforeRetainedCommentsShouldRemainSeparate(IniCommentStyle style, char marker)
    {
        using var document = IniDocument.Parse("# original\nkey=1", options: new IniDocumentOptions { PreserveTrivia = true });
        document.RootElement.AsObject().GetPropertyEntry("key").LeadingTrivia.AddFirst(" first\nsecond", TriviaKind.LineComment);

        Assert.Equal($"{marker} first\n{marker}second\n# original\nkey=1", document.RootElement.ToIni(
            new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = "\n" }));
    }
}