// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace REDox.Ini.Tests;

public sealed class IniCreateTest
{
    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void EmptyDocumentShouldSupportLeadingAndTrailingTrivia(IniCommentStyle style, char marker)
    {
        foreach (var newLine in new[] { "\n", "\r\n" })
        {
            using var document = IniDocument.Parse("");
            var root = document.RootElement.AsObject();
            root.LeadingTrivia.AddFirst(" header\r\ncontinued", TriviaKind.LineComment);
            document.TrailingTrivia.AddLast(" tail\rcontinued", TriviaKind.BlockComment);
            var options = new IniWriteOptions { PreserveTrivia = true, CommentStyle = style, NewLine = newLine };
            Assert.Equal($"{marker} header{newLine}{marker}continued{newLine}{marker} tail{newLine}{marker}continued",
                root.ToIni(options));

            root.Add("global", "1");
            root.GetPropertyEntry("global").LeadingTrivia.AddLast(" global key");
            var section = root.AddObject("section");
            root.GetPropertyEntry("section").LeadingTrivia.AddLast(" section key");
            section.LeadingTrivia.AddLast(" section body\ncontinued", TriviaKind.LineComment);
            section.Add("value", "2");
            section.GetPropertyEntry("value").LeadingTrivia.AddLast(" value key");

            var expected = $"{marker} header{newLine}{marker}continued{newLine}" +
                           $"{marker} global key{newLine}global=1{newLine}" +
                           $"{marker} section key{newLine}[section]{newLine}" +
                           $"{marker} section body{newLine}{marker}continued{newLine}" +
                           $"{marker} value key{newLine}value=2{newLine}" +
                           $"{marker} tail{newLine}{marker}continued";
            var encoded = root.ToIni(options);
            Assert.Equal(expected, encoded);
            using var reparsed = IniDocument.Parse(encoded, options: new IniDocumentOptions { PreserveTrivia = true });
            Assert.Equal("1", reparsed.RootElement.GetProperty("global").GetString());
            Assert.Equal("2", reparsed.RootElement.GetProperty("section").GetProperty("value").GetString());
            Assert.Equal(encoded, reparsed.RootElement.ToIni(options));
            Assert.Equal(new[] { " tail", "continued" }, GetComments(reparsed.TrailingTrivia));

            Assert.Equal($"global=1{newLine}[section]{newLine}value=2",
                root.ToIni(new IniWriteOptions { NewLine = newLine }));

            foreach (var trivia in root.LeadingTrivia)
            {
                trivia.ReplaceWith(" changed header");
            }

            foreach (var trivia in document.TrailingTrivia)
            {
                trivia.ReplaceWith(" changed tail");
            }

            Assert.StartsWith($"{marker} changed header{newLine}", root.ToIni(options));
            Assert.EndsWith($"{marker} changed tail", root.ToIni(options));

            root.LeadingTrivia.Clear();
            root.GetPropertyEntry("global").LeadingTrivia.Clear();
            root.GetPropertyEntry("section").LeadingTrivia.Clear();
            section.LeadingTrivia.Clear();
            section.GetPropertyEntry("value").LeadingTrivia.Clear();
            document.TrailingTrivia.Clear();
            Assert.Equal($"global=1{newLine}[section]{newLine}value=2", root.ToIni(options));
        }
    }

    [Fact]
    public void CreatedDocumentShouldAllowMoreLeadingCommentsAfterReparsing()
    {
        using var created = IniDocument.Parse("");
        created.RootElement.AsObject().LeadingTrivia.AddLast(" first");
        created.RootElement.AsObject().Add("key", "1");
        created.TrailingTrivia.AddLast(" tail");
        var options = new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" };
        using var reparsed = IniDocument.Parse(created.RootElement.ToIni(options),
            options: new IniDocumentOptions { PreserveTrivia = true });
        reparsed.RootElement.AsObject().LeadingTrivia.AddLast(" added");

        Assert.Equal("; added\n; first\nkey=1\n; tail", reparsed.RootElement.ToIni(options));
    }

    [Fact]
    public void CreatedSectionShouldWriteContainerLeadingTriviaBeforeItsKeys()
    {
        using var document = IniDocument.Parse("");
        var section = document.RootElement.AsObject().AddObject("section");
        section.LeadingTrivia.AddLast(" body");
        section.Add("value", "1");
        document.TrailingTrivia.AddLast(" tail");

        Assert.Equal("[section]\n; body\nvalue=1\n; tail", document.RootElement.ToIni(
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }));
    }

    [Fact]
    public void SnapshotOfCreatedDocumentShouldKeepBothTriviaCollectionsIndependent()
    {
        using var original = IniDocument.Parse("");
        original.RootElement.AsObject().LeadingTrivia.AddLast(" header");
        original.TrailingTrivia.AddLast(" tail");
        original.RootElement.AsObject().Add("key", "1");
        using var duplicate = original.Duplicate();
        duplicate.RootElement.AsObject().LeadingTrivia.Clear();
        duplicate.TrailingTrivia.Clear();
        var options = new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" };

        Assert.Equal("; header\nkey=1\n; tail", original.RootElement.ToIni(options));
        Assert.Equal("key=1", duplicate.RootElement.ToIni(options));
        original.Dispose();
        duplicate.RootElement.AsObject().LeadingTrivia.AddLast(" duplicate");
        duplicate.TrailingTrivia.AddLast(" end");
        Assert.Equal("; duplicate\nkey=1\n; end", duplicate.RootElement.ToIni(options));
    }

    [Fact]
    public void LeadingAndTrailingTriviaOfCreatedDocumentShouldBecomeInvalidAfterDisposal()
    {
        var document = IniDocument.Parse("");
        var leading = document.RootElement.AsObject().LeadingTrivia;
        var trailing = document.TrailingTrivia;
        leading.AddLast(" header");
        trailing.AddLast(" tail");
        document.Dispose();

        Assert.False(leading.IsValid);
        Assert.False(trailing.IsValid);
        Assert.Throws<ObjectDisposedException>(() => leading.AddLast(" invalid"));
        Assert.Throws<ObjectDisposedException>(() => trailing.AddLast(" invalid"));
    }

    private static List<string> GetComments(DTriviaCollection triviaCollection)
    {
        var comments = new List<string>();
        foreach (var trivia in triviaCollection)
        {
            if (trivia.Kind == TriviaKind.LineComment)
            {
                comments.Add(trivia.GetString());
            }
        }

        return comments;
    }
}