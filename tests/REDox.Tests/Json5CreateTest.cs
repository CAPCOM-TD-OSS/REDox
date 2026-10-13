// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Tests;

public sealed class Json5CreateTest
{
    [Theory]
    [InlineData("{}", TriviaKind.Default)]
    [InlineData("{}", TriviaKind.LineComment)]
    [InlineData("{}", TriviaKind.BlockComment)]
    [InlineData("[]", TriviaKind.LineComment)]
    [InlineData("[]", TriviaKind.BlockComment)]
    [InlineData("null", TriviaKind.LineComment)]
    [InlineData("null", TriviaKind.BlockComment)]
    public void EmptyDocumentShouldSupportLeadingAndTrailingComments(string input, TriviaKind kind)
    {
        foreach (var newLine in new[] { "\n", "\r\n" })
        {
            using var document = Json5Document.Parse(input);
            var leading = document.RootElement.AsValue().LeadingTrivia;
            leading.AddFirst(" header", kind);
            document.TrailingTrivia.AddLast(" tail", kind);
            var options = new Json5WriteOptions { PreserveTrivia = true, NewLine = newLine };
            var header = kind == TriviaKind.BlockComment ? "/* header*/" : "// header";
            var tail = kind == TriviaKind.BlockComment ? "/* tail*/" : "// tail";

            Assert.Equal($"{header}{newLine}{input}{tail}{newLine}",
                Json5Document.EncodeToString(document.RootElement, options));

            var expectedValue = input;
            if (input == "{}")
            {
                var root = document.RootElement.AsObject();
                root.Add("value", 1);
                root.Add("nested", new DArray(2, new DObject { ["value"] = 3 }));
                expectedValue = "{value:1,nested:[2,{value:3}]}";
            }
            else if (input == "[]")
            {
                document.RootElement.AsArray().Add(new DObject { ["value"] = 1 });
                expectedValue = "[{value:1}]";
            }
            else
            {
                document.RootElement.AsValue().ReplaceWith(1);
                expectedValue = "1";
            }

            var encoded = Json5Document.EncodeToString(document.RootElement, options);
            Assert.Equal($"{header}{newLine}{expectedValue}{tail}{newLine}", encoded);
            using var reparsed = Json5Document.Parse(encoded,
                options: new Json5DocumentOptions { PreserveTrivia = true, EnableValueValidation = true });
            Assert.Equal(new[] { " header" }, GetComments(reparsed.RootElement.AsValue().LeadingTrivia));
            Assert.Equal(new[] { " tail" }, GetComments(reparsed.TrailingTrivia));
            Assert.Equal(encoded, Json5Document.EncodeToString(reparsed.RootElement, options));
            Assert.Equal(expectedValue, Json5Document.EncodeToString(reparsed.RootElement));
            Assert.Equal(expectedValue, Json5Document.EncodeToString(document.RootElement));

            leading.Clear();
            document.TrailingTrivia.Clear();
            Assert.Equal(expectedValue, Json5Document.EncodeToString(document.RootElement, options));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParsedTrailingCommentsShouldSupportReplacementAndAddition(bool tryParse)
    {
        const string input = "/* header*/{value:[1]}/* tail*/// last";
        var parseOptions = new Json5DocumentOptions { PreserveTrivia = true };
        Json5Document document;
        if (tryParse)
        {
            Assert.True(Json5Document.TryParse(Encoding.UTF8.GetBytes(input), out var parsed, options: parseOptions));
            Assert.NotNull(parsed);
            document = parsed;
        }
        else
        {
            document = Json5Document.Parse(input, options: parseOptions);
        }

        using (document)
        {
            var trailing = document.TrailingTrivia;
            var options = new Json5WriteOptions { PreserveTrivia = true, NewLine = "\n" };
            Assert.Equal(input, Json5Document.EncodeToString(document.RootElement, options));
            var comments = trailing.GetEnumerator().Where(trivia =>
                trivia.Kind == TriviaKind.BlockComment || trivia.Kind == TriviaKind.LineComment).ToArray();
            Assert.Equal(new[] { " tail", " last" }, comments.Select(trivia => trivia.GetString()));

            comments[0].ReplaceWith(" changed");
            comments[1].ReplaceWith(" replaced", TriviaKind.BlockComment);
            trailing.AddFirst(" first", TriviaKind.BlockComment);
            trailing.AddLast(" added");
            Assert.Equal(new[] { " first", " changed", " replaced", " added" }, GetComments(trailing));
            var encoded = Json5Document.EncodeToString(document.RootElement, options);
            Assert.StartsWith("/* header*/", encoded);
            using var reparsed = Json5Document.Parse(encoded, options: parseOptions);
            Assert.Equal(1, reparsed.RootElement.GetProperty("value").AsArray()[0].AsElement().GetInt32());
            Assert.Equal(GetComments(trailing), GetComments(reparsed.TrailingTrivia));

            trailing.Clear();
            Assert.False(trailing.GetEnumerator().MoveNext());
            Assert.Equal("/* header*/{value:[1]}", Json5Document.EncodeToString(document.RootElement, options));
        }
    }

    [Fact]
    public void DuplicateShouldKeepLeadingAndTrailingCommentsIndependent()
    {
        using var original = Json5Document.Parse("{}");
        original.RootElement.AsObject().LeadingTrivia.AddLast(" header");
        original.TrailingTrivia.AddLast(" tail");
        original.RootElement.AsObject().Add("value", 1);
        using var duplicate = original.Duplicate();
        duplicate.RootElement.AsObject().LeadingTrivia.Clear();
        duplicate.TrailingTrivia.Clear();
        var options = new Json5WriteOptions { PreserveTrivia = true, NewLine = "\n" };

        Assert.Equal("// header\n{value:1}// tail\n", Json5Document.EncodeToString(original.RootElement, options));
        Assert.Equal("{value:1}", Json5Document.EncodeToString(duplicate.RootElement, options));
        original.Dispose();
        duplicate.RootElement.AsObject().LeadingTrivia.AddLast(" duplicate");
        duplicate.TrailingTrivia.AddLast(" end");
        Assert.Equal("// duplicate\n{value:1}// end\n", Json5Document.EncodeToString(duplicate.RootElement, options));
    }

    [Fact]
    public void LeadingAndTrailingTriviaShouldBecomeInvalidAfterDisposal()
    {
        var document = Json5Document.Parse("{}");
        var leading = document.RootElement.AsObject().LeadingTrivia;
        var trailing = document.TrailingTrivia;
        leading.AddLast(" header");
        trailing.AddLast(" tail");
        document.Dispose();

        Assert.False(leading.IsValid);
        Assert.False(trailing.IsValid);
        Assert.Throws<ObjectDisposedException>(() => leading.AddLast(" invalid"));
        Assert.Throws<ObjectDisposedException>(() => trailing.AddLast(" invalid"));
        Assert.Throws<ObjectDisposedException>(() => document.TrailingTrivia);
    }

    [Fact]
    public void EncodingChildShouldExcludeDocumentTrailingComments()
    {
        using var document = Json5Document.Parse("{}");
        document.RootElement.AsObject().Add("child", new DObject { ["value"] = 1 });
        document.TrailingTrivia.AddLast(" tail");

        Assert.Equal("{value:1}", Json5Document.EncodeToString(document.RootElement.GetProperty("child"),
            new Json5WriteOptions { PreserveTrivia = true }));
    }

    private static string[] GetComments(DTriviaCollection trivia)
    {
        return trivia.GetEnumerator().Where(value => value.Kind == TriviaKind.Default ||
                                                     value.Kind == TriviaKind.LineComment || value.Kind == TriviaKind.BlockComment)
            .Select(value => value.GetString()).ToArray();
    }
}