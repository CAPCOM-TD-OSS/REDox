// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class ContainerTriviaRegressionTests
{
    [Theory]
    [InlineData("array", "[]")]
    [InlineData("array", "[1]")]
    [InlineData("object", "{}")]
    [InlineData("object", "{value:1}")]
    [InlineData("map", "{}")]
    [InlineData("map", "{value:1}")]
    public void ViewingParsedContainerShouldPreserveLeadingTrivia(string view, string value)
    {
        var input = "{node: /*before*/ " + value + ", tail:2}";
        using var document = Json5Document.Parse(input,
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var element = document.RootElement.GetProperty("node");
        var expected = ReadTrivia(element);
        Assert.Contains(expected, trivia => trivia.Kind == TriviaKind.BlockComment);

        var container = View(element, view);

        Assert.Equal(expected, ReadTrivia(container.AsElement()));
        Assert.Same(container, View(element, view));
        Assert.Equal(input, Json5Document.EncodeToString(document.RootElement,
            new Json5WriteOptions { PreserveTrivia = true }));
    }

    [Theory]
    [InlineData("object")]
    [InlineData("map")]
    public void SwitchingObjectAndMapViewsShouldPreserveParsedAndAddedTrivia(string firstView)
    {
        using var document = Json5Document.Parse("{node: /*before*/ {value:1}, tail:2}",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var element = document.RootElement.GetProperty("node");
        var container = View(element, firstView);
        container.LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
        var expected = ReadTrivia(element);

        var otherView = firstView == "object" ? "map" : "object";
        var converted = View(element, otherView);

        Assert.Equal(expected, ReadTrivia(converted.AsElement()));
        Assert.False(container.IsValid);
        Assert.Equal(1, converted.Count);

        var restored = View(element, firstView);
        Assert.Equal(expected, ReadTrivia(restored.AsElement()));
        Assert.False(converted.IsValid);
    }

    [Theory]
    [InlineData("array", "[1]")]
    [InlineData("object", "{value:1}")]
    [InlineData("map", "{value:1}")]
    public void AddingTriviaBeforeViewingContainerShouldRetainExistingTrivia(string view, string value)
    {
        using var document = Json5Document.Parse("{node: /*before*/ " + value + ", tail:2}",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var element = document.RootElement.GetProperty("node");
        var expected = ReadTrivia(element);

        element.AsValue().LeadingTrivia.AddLast("added", TriviaKind.BlockComment);

        var container = View(element, view);
        Assert.Equal(expected.Append((TriviaKind.BlockComment, "added")), ReadTrivia(container.AsElement()));
    }

    [Theory]
    [InlineData("array", "[1]")]
    [InlineData("object", "{value:1}")]
    [InlineData("map", "{value:1}")]
    public void SnapshotOfViewedContainerShouldRetainIndependentTrivia(string view, string value)
    {
        using var document = Json5Document.Parse("{node: /*before*/ " + value + ", tail:2}",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var element = document.RootElement.GetProperty("node");
        var container = View(element, view);
        container.LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
        var expected = ReadTrivia(element);

        using var snapshot = document.Duplicate();
        container.LeadingTrivia.Clear();
        document.Dispose();

        var snapshotElement = snapshot.RootElement.GetProperty("node");
        Assert.Equal(expected, ReadTrivia(snapshotElement));
        Assert.Equal(expected, ReadTrivia(View(snapshotElement, view).AsElement()));
    }

    private static DContainer View(DElement element, string view)
    {
        return view switch
        {
            "array" => element.AsArray(),
            "object" => element.AsObject(),
            "map" => element.AsMap(),
            _ => throw new ArgumentOutOfRangeException(nameof(view))
        };
    }

    private static (TriviaKind Kind, string Text)[] ReadTrivia(DElement element)
    {
        return element.AsValue().LeadingTrivia.GetEnumerator()
            .Select(trivia => (trivia.Kind, trivia.GetString())).ToArray();
    }
}