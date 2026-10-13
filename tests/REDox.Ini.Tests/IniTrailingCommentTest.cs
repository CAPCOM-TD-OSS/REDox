// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace REDox.Ini.Tests;

public sealed class IniTrailingCommentTest
{
    [Fact]
    public void TrailingCommentsShouldBeEditableWithoutChangingLeadingComments()
    {
        using var document = IniDocument.Parse("; header\nkey=1\n# tail\n; last",
            options: new IniDocumentOptions { PreserveTrivia = true });
        var comments = new List<DTrivia>();
        foreach (var trivia in document.TrailingTrivia)
        {
            if (trivia.Kind == TriviaKind.LineComment)
            {
                comments.Add(trivia);
            }
        }

        Assert.Equal(new[] { " tail", " last" }, comments.Select(trivia => trivia.GetString()));
        comments[0].ReplaceWith(" changed");
        document.TrailingTrivia.AddLast(" added");

        Assert.Equal("; header\nkey=1\n; changed\n; last\n; added", document.RootElement.ToIni(
            new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }));

        document.TrailingTrivia.Clear();
        Assert.False(document.TrailingTrivia.GetEnumerator().MoveNext());
        Assert.Equal("; header\nkey=1", document.RootElement.ToIni(new IniWriteOptions { PreserveTrivia = true }));
    }

    [Theory]
    [InlineData(IniCommentStyle.PreserveOrSemicolon, ';')]
    [InlineData(IniCommentStyle.PreserveOrHash, '#')]
    [InlineData(IniCommentStyle.AlwaysSemicolon, ';')]
    [InlineData(IniCommentStyle.AlwaysHash, '#')]
    public void AddedTrailingCommentsShouldUseStyleAndCommentEveryLine(IniCommentStyle style, char marker)
    {
        foreach (var input in new[] { "", "key=1", "[s]\nkey=1" })
        foreach (var newLine in new[] { "\n", "\r\n" })
        {
            using var document = IniDocument.Parse(input);
            document.TrailingTrivia.AddLast(" first\r\nsecond\rthird\n", TriviaKind.LineComment);
            document.TrailingTrivia.AddLast(" last", TriviaKind.BlockComment);
            var prefix = input.Replace("\n", newLine) + (input.Length == 0 ? "" : newLine);
            Assert.Equal($"{prefix}{marker} first{newLine}{marker}second{newLine}{marker}third{newLine}{marker} last",
                document.RootElement.ToIni(new IniWriteOptions
                {
                    PreserveTrivia = true, CommentStyle = style, NewLine = newLine
                }));
            Assert.Equal(input.Replace("\n", newLine), document.RootElement.ToIni(new IniWriteOptions { NewLine = newLine }));
        }
    }

    [Theory]
    [InlineData("key=1\n# tail", "key=1\n; first\n# tail")]
    [InlineData("# tail", "; first\n# tail")]
    public void CommentsCanBePrependedToTrailingTrivia(string input, string expected)
    {
        using var document = IniDocument.Parse(input, options: new IniDocumentOptions { PreserveTrivia = true });
        document.TrailingTrivia.AddFirst(" first", TriviaKind.LineComment);
        Assert.Equal(expected, document.RootElement.ToIni(new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }));
    }

    [Fact]
    public void TryParsedDocumentsShouldSupportTrailingCommentEdits()
    {
        Assert.True(IniDocument.TryParse(Encoding.UTF8.GetBytes("key=1\n# tail"), out var document,
            options: new IniDocumentOptions { PreserveTrivia = true }));
        Assert.NotNull(document);
        using (document)
        {
            document.TrailingTrivia.AddLast(" added");
            Assert.Equal("key=1\n# tail\n; added", document.RootElement.ToIni(
                new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }));
        }
    }

    [Fact]
    public void DuplicateShouldKeepTrailingCommentsIndependent()
    {
        using var original = IniDocument.Parse("key=1\n# tail", options: new IniDocumentOptions { PreserveTrivia = true });
        original.TrailingTrivia.AddLast(" original");
        using var duplicate = original.Duplicate();
        duplicate.TrailingTrivia.Clear();
        duplicate.TrailingTrivia.AddLast(" duplicate");
        var options = new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" };

        Assert.Equal("key=1\n# tail\n; original", original.RootElement.ToIni(options));
        original.Dispose();
        Assert.Equal("key=1\n; duplicate", duplicate.RootElement.ToIni(options));
    }

    [Fact]
    public void TrailingCommentsShouldBecomeInvalidAfterDisposal()
    {
        var document = IniDocument.Parse("key=1");
        var trailing = document.TrailingTrivia;
        trailing.AddLast(" added");
        document.Dispose();

        Assert.False(trailing.IsValid);
        Assert.Throws<ObjectDisposedException>(() => trailing.AddLast(" invalid"));
        Assert.Throws<ObjectDisposedException>(() => document.TrailingTrivia);
    }

    [Fact]
    public void EncodingASectionShouldExcludeDocumentTrailingComments()
    {
        using var document = IniDocument.Parse("[s]\nkey=1");
        document.TrailingTrivia.AddLast(" tail");

        Assert.Equal("key=1", document.RootElement.GetProperty("s").ToIni(new IniWriteOptions { PreserveTrivia = true }));
    }
}