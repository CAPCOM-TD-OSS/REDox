// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class DocumentTokenControlRegressionTests
{
    private static readonly Json5WriteOptions s_writeOptions = new()
    {
        PreserveTrivia = false,
        PropertyNameStyle = Json5QuoteStyle.AlwaysDouble
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingContainerWithItselfShouldPreserveContents(bool materializeRoot)
    {
        using var document = Json5Document.Parse("[1,{a:[2,3]}]");
        if (materializeRoot)
        {
            document.RootElement.AsArray();
        }

        document.RootElement.AsValue().ReplaceWith(document.RootElement);

        Assert.Equal("[1,{\"a\":[2,3]}]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingObjectWithItselfShouldPreserveContents(bool materializeRoot)
    {
        using var document = Json5Document.Parse("{a:[1,2],b:{c:3}}");
        if (materializeRoot)
        {
            document.RootElement.AsObject();
        }

        document.RootElement.AsValue().ReplaceWith(document.RootElement);

        Assert.Equal("{\"a\":[1,2],\"b\":{\"c\":3}}",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingContainerWithItselfShouldPreserveTriviaHandles(bool materializeRoot)
    {
        using var document = Json5Document.Parse("/*head*/[/*child*/1]/*tail*/",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        if (materializeRoot)
        {
            document.RootElement.AsArray();
        }

        var root = document.RootElement;
        var head = root.EnumerateTrivia().Single(item => item.Kind == TriviaKind.BlockComment);
        var child = root.EnumerateArray().Single().EnumerateTrivia()
            .Single(item => item.Kind == TriviaKind.BlockComment);
        var tail = document.TrailingTrivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        var version = document.Version;
        var options = s_writeOptions with { PreserveTrivia = true };
        var expected = Json5Document.EncodeToString(root, options);

        root.AsValue().ReplaceWith(root);

        Assert.Equal(version, document.Version);
        Assert.True(head.IsValid);
        Assert.True(child.IsValid);
        Assert.True(tail.IsValid);
        Assert.Equal("head", head.GetString());
        Assert.Equal("child", child.GetString());
        Assert.Equal("tail", tail.GetString());
        Assert.Equal(expected, Json5Document.EncodeToString(document.RootElement, options));
    }

    [Theory]
    [InlineData("descendant", false)]
    [InlineData("descendant", true)]
    [InlineData("ancestor", false)]
    [InlineData("ancestor", true)]
    [InlineData("sibling", false)]
    [InlineData("sibling", true)]
    public void ReplacingContainerFromSameDocumentShouldPreserveSourceContents(string relationship, bool materializeRoot)
    {
        using var document = Json5Document.Parse("[[1,{a:[2,3]}],[4]]");
        if (materializeRoot)
        {
            document.RootElement.AsArray();
        }

        var root = document.RootElement;
        var first = root.EnumerateArray().First();
        var second = root.EnumerateArray().Last();
        string expected;
        switch (relationship)
        {
            case "descendant":
                root.AsValue().ReplaceWith(first);
                expected = "[1,{\"a\":[2,3]}]";
                break;
            case "ancestor":
                first.AsValue().ReplaceWith(root);
                expected = "[[[1,{\"a\":[2,3]}],[4]],[4]]";
                break;
            default:
                second.AsValue().ReplaceWith(first);
                expected = "[[1,{\"a\":[2,3]}],[1,{\"a\":[2,3]}]]";
                break;
        }

        Assert.Equal(expected, Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloningAndCopyingParsedContainerShouldFollowJumpPastReusedTokens(bool map)
    {
        using var document = Json5Document.Parse(map ? "{a:[1,2],b:3}" : "[[1,2],3]",
            options: new Json5DocumentOptions { PreserveTrivia = false });
        var container = map ? document.RootElement.GetProperty("a") : document.RootElement.EnumerateArray().First();
        var freedId = container.EnumerateArray().Last().Id;
        container.AsValue().ReplaceWith(0);

        // Reuse a slot inside the old container without attaching it to the document's tree.
        Assert.Equal(freedId, document.JoinAny(0, 99));
        var root = document.RootElement;
        Assert.False(root.Token.IsExtended);
        var expected = map ? "{\"a\":0,\"b\":3}" : "[0,3]";
        Assert.Equal(expected, Json5Document.EncodeToString(root, s_writeOptions));

        var clone = root.Clone();
        Assert.Equal(expected, Json5Document.EncodeToString(clone, s_writeOptions));

        var destination = map ? root.GetProperty("b") : root.EnumerateArray().Last();
        destination.AsValue().ReplaceWith(root);
        Assert.Equal(map ? "{\"a\":0,\"b\":{\"a\":0,\"b\":3}}" : "[0,[0,3]]",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Fact]
    public void CloningParsedMapShouldFollowJumpAfterReplacedContainerKey()
    {
        var key = new DArray(1, 2);
        using var keyDocument = key.Document;
        var map = new DMap();
        using var mapDocument = map.Document;
        map.Add(key, 3);
        var source = map.AsElement().Clone();
        var sourceDocument = source.Document;
        var parsedKey = source.EnumerateMap().Single().Key;
        var freedId = parsedKey.EnumerateArray().Last().Id;
        parsedKey.AsValue().ReplaceWith("key");
        Assert.Equal(freedId, sourceDocument.JoinAny(0, 99));
        var root = sourceDocument.RootElement;
        Assert.False(root.Token.IsExtended);
        Assert.Equal("{\"key\":3}", Json5Document.EncodeToString(root, s_writeOptions));

        var clone = root.Clone();
        Assert.Equal("{\"key\":3}", Json5Document.EncodeToString(clone, s_writeOptions));
    }

    [Fact]
    public void AddingContainerToItselfShouldAppendIndependentCopy()
    {
        using var document = Json5Document.Parse("[1,{a:[2,3]}]");
        var array = document.RootElement.AsArray();

        array.Add(document.RootElement);
        array[0] = 9;

        Assert.Equal("[9,{\"a\":[2,3]},[1,{\"a\":[2,3]}]]",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingContainerWithScalarShouldReclaimExtendIdFromReservedSlot(bool materializeParent)
    {
        using var document = Json5Document.Parse("[[1,2],3]",
            options: new Json5DocumentOptions { PreserveTrivia = false });
        if (materializeParent)
        {
            document.RootElement.AsArray();
        }

        var container = document.RootElement.EnumerateArray().First();
        var linkId = container.Token.LinkId;
        var first = container.EnumerateArray().First();
        Assert.Equal(container.Id + 1, first.Id);
        first.AsValue().ReplaceWith("extended child");
        var extendId = document.GetToken(first.Id).ExtendId;

        container.AsValue().ReplaceWith(0);

        Assert.Equal("[0,3]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        var jump = document.GetToken(first.Id);
        Assert.Equal(DTokenKind.Control, jump.Kind);
        Assert.False(jump.IsExtended);
        Assert.Equal(linkId, jump.JumpId);

        Assert.Null(document.GetExtends()[(int)extendId]);
        var replacement = document.AddExtendArray(0);
        Assert.NotEqual(first.Id, replacement.Id);
        Assert.Equal(extendId, document.GetToken(replacement.Id).ExtendId);
        Assert.Equal("[0,3]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Fact]
    public void ReplacingContainerWithScalarShouldReclaimNestedReservedExtendIds()
    {
        using var document = Json5Document.Parse("[[[1],2],3]",
            options: new Json5DocumentOptions { PreserveTrivia = false });
        var container = document.RootElement.EnumerateArray().First();
        var linkId = container.Token.LinkId;
        var child = container.EnumerateArray().First();
        child.AsArray();
        var grandchild = child.EnumerateArray().Single();
        grandchild.AsValue().ReplaceWith("extended grandchild");
        var extendIds = new[] { document.GetToken(child.Id).ExtendId, document.GetToken(grandchild.Id).ExtendId };
        using var snapshot = document.Duplicate();

        container.AsValue().ReplaceWith(0);

        Assert.Equal("[0,3]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal(linkId, document.GetToken(child.Id).JumpId);
        Assert.Equal(default, document.GetToken(grandchild.Id));
        Assert.All(extendIds, id => Assert.Null(document.GetExtends()[(int)id]));

        var replacements = new[] { document.AddExtendArray(0), document.AddExtendArray(0) };
        Assert.Equal(extendIds.OrderBy(id => id),
            replacements.Select(item => document.GetToken(item.Id).ExtendId).OrderBy(id => id));
        Assert.All(replacements, item => Assert.NotEqual(child.Id, item.Id));
        Assert.All(replacements, item => Assert.NotEqual(grandchild.Id, item.Id));
        Assert.Equal("[0,3]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal("[[[\"extended grandchild\"],2],3]",
            Json5Document.EncodeToString(snapshot.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingElementShouldReleaseItsTriviaAndTriviaList(bool replaceParsedTrivia)
    {
        using var document = Json5Document.Parse(replaceParsedTrivia ? "[1,/*old*/2]" : "[1,2]",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var array = document.RootElement.AsArray();
        var removedId = array[1].AsElement().Id;
        var trivia = array[1].LeadingTrivia;
        if (replaceParsedTrivia)
        {
            trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment)
                .ReplaceWith("edited", TriviaKind.BlockComment);
        }
        else
        {
            trivia.AddLast("added", TriviaKind.BlockComment);
        }

        var comment = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        var commentExtendId = document.GetToken(comment.Id).ExtendId;
        var triviaListId = document.GetToken(removedId).TriviaId;
        var extendCount = document.GetExtends().Length;

        array.RemoveAt(1);

        Assert.Equal("[1]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.False(comment.IsValid);
        Assert.Null(document.GetExtends()[(int)commentExtendId]);
        Assert.Null(document.GetExtends()[(int)triviaListId]);
        if (replaceParsedTrivia)
        {
            Assert.Equal(default, document.GetToken(comment.Id));
        }
        else
        {
            Assert.True(document.GetToken(comment.Id).IsEmptyExtend);
        }

        // Recreating a value's trivia needs a list, a scalar payload, and a comment.
        // All three ExtendIds should be available from the removed element.
        array[0].LeadingTrivia.AddLast("replacement", TriviaKind.BlockComment);
        Assert.Equal(extendCount, document.GetExtends().Length);
        Assert.Equal("replacement", array[0].LeadingTrivia.GetEnumerator()
            .Single(item => item.Kind == TriviaKind.BlockComment).GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingContainerShouldReleaseDescendantTrivia(bool materializeContainer)
    {
        using var document = Json5Document.Parse("[/*container*/[/*child*/1],/*keep*/2]",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var array = document.RootElement.AsArray();
        var container = array[0].AsElement();
        var containerComment = container.EnumerateTrivia().Single(item => item.Kind == TriviaKind.BlockComment);
        var containerTriviaListId = materializeContainer ? container.AsArray().TriviaId : 0;
        var child = container.EnumerateArray().Single();
        var childComment = child.EnumerateTrivia().Single(item => item.Kind == TriviaKind.BlockComment);
        childComment.ReplaceWith("edited child", TriviaKind.BlockComment);
        var childTriviaListId = document.GetToken(child.Id).TriviaId;
        var childCommentExtendId = document.GetToken(childComment.Id).ExtendId;
        using var snapshot = document.Duplicate();

        array.RemoveAt(0);

        Assert.Equal("[2]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal(default, document.GetToken(containerComment.Id));
        Assert.Equal(default, document.GetToken(childComment.Id));
        Assert.Null(document.GetExtends()[(int)childTriviaListId]);
        Assert.Null(document.GetExtends()[(int)childCommentExtendId]);
        if (materializeContainer)
        {
            Assert.Null(document.GetExtends()[(int)containerTriviaListId]);
        }

        Assert.Equal("keep", array[0].LeadingTrivia.GetEnumerator()
            .Single(item => item.Kind == TriviaKind.BlockComment).GetString());
        Assert.Equal("edited child", snapshot.RootElement.EnumerateArray().First()
            .EnumerateArray().Single().EnumerateTrivia()
            .Single(item => item.Kind == TriviaKind.BlockComment).GetString());
    }

    [Fact]
    public void RemovingElementShouldReleasePreviouslyClearedTriviaList()
    {
        using var document = Json5Document.Parse("[1,2]",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var array = document.RootElement.AsArray();
        var trivia = array[1].LeadingTrivia;
        trivia.AddLast("temporary", TriviaKind.BlockComment);
        trivia.Clear();
        var triviaListId = document.GetToken(array[1].AsElement().Id).TriviaId;
        var extendCount = document.GetExtends().Length;

        array.RemoveAt(1);

        Assert.Null(document.GetExtends()[(int)triviaListId]);
        array[0].LeadingTrivia.AddLast("replacement", TriviaKind.BlockComment);
        Assert.Equal(extendCount, document.GetExtends().Length);
        Assert.Equal("replacement", array[0].LeadingTrivia.GetEnumerator()
            .Single(item => item.Kind == TriviaKind.BlockComment).GetString());
    }

    [Fact]
    public void ReplacingExtendedScalarWithInlineLiteralShouldReleaseOldPayload()
    {
        using var document = Json5Document.Parse("0");
        document.RootElement.AsValue().ReplaceWith("old extended value");
        var extendId = document.RootElement.Token.ExtendId;

        document.RootElement.AsValue().ReplaceWith(true);

        Assert.Equal("true", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Null(document.GetExtends()[(int)extendId]);
        var replacement = document.AddExtendArray(0);
        Assert.Equal(extendId, document.GetToken(replacement.Id).ExtendId);
    }
}