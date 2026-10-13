// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class DocumentGenericCopyRegressionTests
{
    private static readonly Json5WriteOptions s_writeOptions = new()
    {
        PreserveTrivia = false,
        PropertyNameStyle = Json5QuoteStyle.AlwaysDouble
    };

    public static TheoryData<string, bool> GenericNodeTypes()
    {
        var cases = new TheoryData<string, bool>();
        foreach (var inputType in new[] { "element", "value", "container", "interfaceValue", "interfaceContainer" })
        {
            cases.Add(inputType, false);
            cases.Add(inputType, true);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(GenericNodeTypes))]
    public void ReplacingContainerWithItselfThroughGenericTypeShouldPreserveContentsAndTrivia(
        string inputType, bool materializeRoot)
    {
        using var document = Json5Document.Parse("/*head*/[/*child*/1,{a:[2,3]}]/*tail*/",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        if (materializeRoot)
        {
            document.RootElement.AsArray();
        }

        var childElement = document.RootElement.EnumerateArray().First();
        childElement.AsValue().LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
        var root = document.RootElement;
        var head = root.EnumerateTrivia().Single(item => item.Kind == TriviaKind.BlockComment);
        var child = childElement.EnumerateTrivia().Last();
        var tail = document.TrailingTrivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        var version = document.Version;

        ReplaceWithGenericNode(root.AsValue(), root, inputType);

        Assert.Equal("[1,{\"a\":[2,3]}]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal(version, document.Version);
        Assert.True(head.IsValid);
        Assert.True(child.IsValid);
        Assert.True(tail.IsValid);
        Assert.Equal("head", head.GetString());
        Assert.Equal("added", child.GetString());
        Assert.Equal("tail", tail.GetString());
    }

    [Theory]
    [MemberData(nameof(GenericNodeTypes))]
    public void CopyingAncestorThroughGenericTypeShouldReadSourceBeforeReplacingDescendant(
        string inputType, bool materializeRoot)
    {
        using var document = Json5Document.Parse("[[1,{a:[2,3]}],[4]]");
        if (materializeRoot)
        {
            document.RootElement.AsArray();
        }

        var root = document.RootElement;
        var first = root.EnumerateArray().First().AsValue();

        ReplaceWithGenericNode(first, root, inputType);

        Assert.Equal("[[[1,{\"a\":[2,3]}],[4]],[4]]",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData("2", false)]
    [InlineData("2", true)]
    [InlineData("true", false)]
    [InlineData("true", true)]
    [InlineData("null", false)]
    [InlineData("null", true)]
    public void CopyingInvalidScalarShouldRejectReusedTokenWithoutChangingDestination(string scalar, bool boxed)
    {
        using var document = Json5Document.Parse("[1," + scalar + "]",
            options: new Json5DocumentOptions { PreserveTrivia = false });
        var array = document.RootElement.AsArray();
        var removed = array[1].AsElement();
        array.RemoveAt(1);
        array.Add(99);
        Assert.False(removed.IsValid);
        Assert.Equal(removed.Id, array[1].AsElement().Id);

        Assert.Throws<ObjectDisposedException>(() =>
        {
            if (boxed)
            {
                array[0].ReplaceWith<object>(removed);
            }
            else
            {
                array[0].ReplaceWith(removed);
            }
        });

        Assert.Equal("[1,99]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializingInvalidScalarShouldRejectReusedToken(bool boxed)
    {
        using var document = Json5Document.Parse("[1,2]",
            options: new Json5DocumentOptions { PreserveTrivia = false });
        var array = document.RootElement.AsArray();
        var removed = array[1].AsElement();
        array.RemoveAt(1);
        array.Add(99);

        Assert.Throws<ObjectDisposedException>(() =>
        {
            if (boxed)
            {
                JsonSerializer.Serialize<object>(removed);
            }
            else
            {
                JsonSerializer.Serialize(removed);
            }
        });
    }

    [Theory]
    [InlineData("typed", false)]
    [InlineData("typed", true)]
    [InlineData("object", false)]
    [InlineData("object", true)]
    [InlineData("interface", false)]
    [InlineData("interface", true)]
    public void CopyingAndSerializingInvalidValueShouldRejectReusedToken(string inputType, bool containerHandle)
    {
        using var document = Json5Document.Parse("[1,2]",
            options: new Json5DocumentOptions { PreserveTrivia = false });
        var array = document.RootElement.AsArray();
        var removed = containerHandle ? array[1] : array[1].AsElement().AsValue();
        array.RemoveAt(1);
        array.Add(99);
        // Inserting before the replacement also changes the old container index.
        array.Insert(0, 0);
        Assert.False(removed.IsValid);
        Assert.Equal((uint)removed.Payload, array[2].AsElement().Id);

        Assert.Throws<ObjectDisposedException>(() =>
        {
            switch (inputType)
            {
                case "typed":
                    array[1].ReplaceWith(removed);
                    break;
                case "object":
                    array[1].ReplaceWith<object>(removed);
                    break;
                case "interface":
                    array[1].ReplaceWith<IDoxNode>(removed);
                    break;
            }
        });

        Assert.Throws<ObjectDisposedException>(() =>
        {
            switch (inputType)
            {
                case "typed":
                    JsonSerializer.Serialize(removed);
                    break;
                case "object":
                    JsonSerializer.Serialize<object>(removed);
                    break;
                case "interface":
                    JsonSerializer.Serialize<IDoxNode>(removed);
                    break;
            }
        });
        Assert.Equal("[0,1,99]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    private static void ReplaceWithGenericNode(DValue target, DElement source, string inputType)
    {
        switch (inputType)
        {
            case "element":
                target.ReplaceWith<object>(source);
                break;
            case "value":
                target.ReplaceWith<object>(source.AsValue());
                break;
            case "container":
                target.ReplaceWith<object>(source.AsContainer());
                break;
            case "interfaceValue":
                target.ReplaceWith<IDoxNode>(source.AsValue());
                break;
            case "interfaceContainer":
                target.ReplaceWith<IDoxNode>(source.AsContainer());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(inputType));
        }
    }
}