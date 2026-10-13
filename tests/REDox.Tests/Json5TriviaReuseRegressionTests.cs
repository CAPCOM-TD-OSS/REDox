// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using REDox.Json;

namespace REDox.Tests;

public sealed class Json5TriviaReuseRegressionTests
{
    private static readonly Json5DocumentOptions s_parseOptions = new() { PreserveTrivia = true };

    private static readonly Json5WriteOptions s_writeOptions = new()
    {
        PreserveTrivia = true,
        NewLine = "\n",
        PropertyNameStyle = Json5QuoteStyle.AlwaysDouble
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusingClearedParsedTriviaForValueShouldPreserveParsedParent(bool materializeParent)
    {
        using var document = Json5Document.Parse("[/*old*/1,2]", options: s_parseOptions);
        if (materializeParent)
        {
            document.RootElement.AsArray();
        }

        var first = document.RootElement.EnumerateArray().First().AsValue();
        var second = document.RootElement.EnumerateArray().Last().AsValue();
        first.LeadingTrivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment)
            .ReplaceWith("edited");
        first.LeadingTrivia.Clear();
        second.ReplaceWith(new DArray(3));

        Assert.Equal("[1,[3]]", Json5Document.EncodeToString(document.RootElement, s_writeOptions with { PreserveTrivia = false }));
        AssertRoundTrip(document, "[1,[3]]");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingContainerAfterClearingChildTriviaShouldPreserveFollowingValue(bool materializeParent)
    {
        using var document = Json5Document.Parse("[[/*old*/1],/*keep*/2]", options: s_parseOptions);
        if (materializeParent)
        {
            document.RootElement.AsArray();
        }

        var container = document.RootElement.EnumerateArray().First().AsValue();
        var child = container.AsElement().EnumerateArray().Single().AsValue();
        child.LeadingTrivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment)
            .ReplaceWith("edited");
        child.LeadingTrivia.Clear();
        container.ReplaceWith(0);

        AssertRoundTrip(document, "[0,2]");
        Assert.Contains("keep", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingMaterializedChildShouldPreserveParentTraversal(bool materializeParent)
    {
        using var document = Json5Document.Parse("[[1,2],/*keep*/3]", options: s_parseOptions);
        if (materializeParent)
        {
            document.RootElement.AsArray();
        }

        var child = document.RootElement.EnumerateArray().First();
        child.AsArray();
        Assert.True(child.Token.IsExtended);

        child.AsValue().ReplaceWith(0);

        Assert.Equal(materializeParent, document.RootElement.Token.IsExtended);
        AssertRoundTrip(document, "[0,3]");
        Assert.Contains("keep", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    public static TheoryData<string, string, string, string> ContainerReplacementTriviaCases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var value in new[] { "1", "18446744073709551615", "1.25", "true", "null", "'text'", "[]", "[1]", "{}", "{x:[2]}" })
        {
            foreach (var context in new[] { "root", "property", "array" })
            {
                foreach (var replacement in new[] { "[]", "[3]", "{b:4}" })
                {
                    foreach (var triviaState in new[] { "parsed", "materialized", "edited", "added" })
                    {
                        data.Add(value, context, replacement, triviaState);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContainerReplacementTriviaCases))]
    public void ReplacingWithContainerShouldPreserveLeadingTrivia(
        string valueSource, string context, string replacementSource, string triviaState)
    {
        var source = "/*old*/ \t" + valueSource;
        source = context switch
        {
            "property" => "{a:" + source + ",b:9}",
            "array" => "[" + source + ",9]",
            _ => source
        };
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        using var replacement = Json5Document.Parse(replacementSource, options: s_parseOptions);
        var target = context switch
        {
            "property" => document.RootElement.GetProperty("a"),
            "array" => document.RootElement.EnumerateArray().First(),
            _ => document.RootElement
        };
        var trivia = target.AsValue().LeadingTrivia;
        switch (triviaState)
        {
            case "materialized":
                document.EnsureElementTriviaList(target.Id);
                break;
            case "edited":
                trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment).ReplaceWith("edited");
                break;
            case "added":
                trivia.AddLast("added", TriviaKind.BlockComment);
                break;
        }

        var expectedTrivia = trivia.GetEnumerator().Select(item => (item.Id, item.Kind, Text: item.GetString())).ToArray();
        target.AsValue().ReplaceWith(replacement.RootElement);

        Assert.Equal(expectedTrivia, target.EnumerateTrivia().Select(item => (item.Id, item.Kind, Text: item.GetString())).ToArray());
        var plainReplacement = Json5Document.EncodeToString(replacement.RootElement, s_writeOptions with { PreserveTrivia = false });
        var expected = context switch
        {
            "property" => "{\"a\":" + plainReplacement + ",\"b\":9}",
            "array" => "[" + plainReplacement + ",9]",
            _ => plainReplacement
        };
        AssertRoundTrip(document, expected);
        Assert.Contains(triviaState == "edited" ? "/*edited*/" : "/*old*/", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        if (triviaState == "added")
        {
            Assert.Contains("/*added*/", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        }
    }

    [Theory]
    [InlineData("root", false)]
    [InlineData("root", true)]
    [InlineData("property", false)]
    [InlineData("property", true)]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("trailing", false)]
    [InlineData("trailing", true)]
    public void ClearingParsedTriviaShouldInvalidateHandlesWithoutInvalidatingElements(string context, bool edit)
    {
        var source = context switch
        {
            "property" => "{a:/*old*/1,b:2}",
            "array" => "[/*old*/1,2]",
            "trailing" => "1/*old*/",
            _ => "/*old*/1"
        };
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var element = document.RootElement;
        var trivia = GetTrivia(document, context);
        var old = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        if (edit)
        {
            old.ReplaceWith("edited");
            Assert.True(old.IsValid);
        }

        var documentVersion = document.Version;
        trivia.Clear();
        trivia.Clear();

        Assert.Equal(documentVersion, document.Version);
        Assert.True(element.IsValid);
        Assert.True(trivia.IsValid);
        Assert.False(old.IsValid);
        Assert.Equal(default, document.GetToken(old.Id));
        Assert.Throws<ObjectDisposedException>(() => old.GetString());
        Assert.Throws<ObjectDisposedException>(() => old.Kind);
        Assert.Throws<ObjectDisposedException>(() => old.ReplaceWith("stale"));
        trivia.AddLast("new", TriviaKind.BlockComment);
        Assert.False(old.IsValid);
        AssertRoundTrip(document, Json5Document.EncodeToString(element, s_writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingExtendedParsedTriviaShouldReuseExtendIdsWithoutReusingParsedSlots(bool freeTokenAvailable)
    {
        using var document = Json5Document.Parse("/*one*/ /*two*/[1,2]", options: s_parseOptions);
        var array = document.RootElement.AsArray();
        uint freedId = 0;
        if (freeTokenAvailable)
        {
            freedId = array[1].AsElement().Id;
            array.RemoveAt(1);
        }

        var trivia = document.RootElement.AsValue().LeadingTrivia;
        var parsed = trivia.GetEnumerator().Where(item => item.Kind == TriviaKind.BlockComment).ToArray();
        foreach (var item in parsed)
        {
            item.ReplaceWith("edited", TriviaKind.BlockComment);
        }

        var extendIds = parsed.Select(item => document.GetToken(item.Id).ExtendId).OrderBy(id => id).ToArray();
        var version = document.Version;
        using var snapshot = document.Duplicate();
        var snapshotTrivia = snapshot.RootElement.AsArray().LeadingTrivia.GetEnumerator()
            .Where(item => item.Kind == TriviaKind.BlockComment).ToArray();

        trivia.Clear();
        trivia.Clear();

        Assert.Equal(version, document.Version);
        Assert.All(parsed, item => Assert.False(item.IsValid));
        Assert.All(parsed, item => Assert.Equal(default, document.GetToken(item.Id)));
        Assert.All(extendIds, id => Assert.Null(document.GetExtends()[(int)id]));

        for (var i = 0; i < parsed.Length; i++)
        {
            trivia.AddLast("new", TriviaKind.BlockComment);
        }

        var added = trivia.GetEnumerator().ToArray();
        Assert.Equal(extendIds, added.Select(item => document.GetToken(item.Id).ExtendId).OrderBy(id => id).ToArray());
        Assert.All(parsed, item => Assert.DoesNotContain(item.Id, added.Select(value => value.Id)));
        Assert.All(parsed, item => Assert.Equal(default, document.GetToken(item.Id)));
        if (freeTokenAvailable)
        {
            Assert.Contains(freedId, added.Select(item => item.Id));
        }

        Assert.All(snapshotTrivia, item => Assert.True(item.IsValid));
        Assert.All(snapshotTrivia, item => Assert.Equal("edited", item.GetString()));
        var expected = freeTokenAvailable ? "[1]" : "[1,2]";
        AssertRoundTrip(document, expected);
        AssertRoundTrip(snapshot, expected);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("property")]
    [InlineData("array")]
    [InlineData("trailing")]
    public void ReusingTriviaInSameCollectionShouldInvalidateOldHandles(string context)
    {
        using var document = Json5Document.Parse(context == "array" ? "[1,2]" : "{a:1,b:2}", options: s_parseOptions);
        var trivia = GetTrivia(document, context);
        var documentVersion = document.Version;
        for (var i = 0; i < 8; i++)
        {
            trivia.AddLast("old", TriviaKind.BlockComment);
            var old = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
            trivia.AddFirst("head", TriviaKind.BlockComment);
            trivia.AddLast("tail", TriviaKind.LineComment);
            Assert.True(old.IsValid);
            old.ReplaceWith("edited");
            old.ReplaceWith("edited again");
            Assert.Equal("edited again", old.GetString());

            trivia.Clear();
            Assert.False(old.IsValid);
            // The free list is LIFO; consume the last trivia before reusing the old slot.
            trivia.AddLast("new tail", TriviaKind.LineComment);
            trivia.AddFirst("new", TriviaKind.BlockComment);
            var current = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
            Assert.Equal(old.Id, current.Id);
            Assert.True(current.IsValid);
            Assert.False(old.IsValid);
            Assert.NotEqual(old, current);
            Assert.Throws<ObjectDisposedException>(() => old.ReplaceWith("stale"));
            Assert.Equal("new", current.GetString());
            trivia.Clear();
        }

        Assert.Equal(documentVersion, document.Version);
        AssertRoundTrip(document, context == "array" ? "[1,2]" : "{\"a\":1,\"b\":2}");
    }

    [Fact]
    public void ReusingTriviaInSiblingCollectionShouldInvalidateOnlyClearedHandles()
    {
        using var document = Json5Document.Parse("[1,2]", options: s_parseOptions);
        var array = document.RootElement.AsArray();
        var first = array[0].LeadingTrivia;
        var second = array[1].LeadingTrivia;
        second.AddLast("keep", TriviaKind.BlockComment);
        var keep = second.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        first.AddLast("old", TriviaKind.BlockComment);
        var old = first.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        first.Clear();
        second.AddLast("new", TriviaKind.BlockComment);
        var current = second.GetEnumerator().Single(item => item.GetString() == "new");

        Assert.Equal(old.Id, current.Id);
        Assert.False(old.IsValid);
        Assert.True(keep.IsValid);
        Assert.NotEqual(old, current);
        Assert.Throws<ObjectDisposedException>(() => old.ReplaceWith("stale"));
        keep.ReplaceWith("still valid");
        Assert.Equal("new", current.GetString());
        AssertRoundTrip(document, "[1,2]");
    }

    [Theory]
    [InlineData("root")]
    [InlineData("property")]
    [InlineData("array")]
    [InlineData("trailing")]
    public void DuplicateShouldKeepTriviaVersionsAndStorageIndependent(string context)
    {
        using var document = Json5Document.Parse(context == "array" ? "[1,2]" : "{a:1,b:2}", options: s_parseOptions);
        var trivia = GetTrivia(document, context);
        for (var i = 0; i < 2; i++)
        {
            trivia.AddLast("temporary", TriviaKind.BlockComment);
            trivia.Clear();
        }

        trivia.AddLast("keep", TriviaKind.BlockComment);
        var original = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        using var snapshot = document.Duplicate();
        var snapshotTrivia = GetTrivia(snapshot, context);
        var copied = snapshotTrivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);
        Assert.True(copied.IsValid);
        trivia.Clear();
        trivia.AddLast("original only", TriviaKind.BlockComment);
        var added = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment);

        Assert.False(original.IsValid);
        Assert.True(copied.IsValid);
        Assert.Equal("keep", copied.GetString());
        copied.ReplaceWith("snapshot only");
        Assert.Equal("original only", added.GetString());
        snapshotTrivia.Clear();
        Assert.False(copied.IsValid);
        Assert.True(added.IsValid);
        Assert.Equal("original only", added.GetString());
        var expected = context == "array" ? "[1,2]" : "{\"a\":1,\"b\":2}";
        AssertRoundTrip(document, expected);
        AssertRoundTrip(snapshot, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingTriviaShouldInvalidateExistingEnumerators(bool parsed)
    {
        using var document = Json5Document.Parse(parsed ? "/*old*/1" : "1", options: s_parseOptions);
        var trivia = document.RootElement.AsValue().LeadingTrivia;
        if (!parsed)
        {
            trivia.AddLast("old", TriviaKind.BlockComment);
        }

        var enumerator = trivia.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        trivia.Clear();
        trivia.AddLast("new", TriviaKind.BlockComment);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
    }

    private static DTriviaCollection GetTrivia(Json5Document document, string context)
    {
        return context switch
        {
            "property" => document.RootElement.GetProperty("a").AsValue().LeadingTrivia,
            "array" => document.RootElement.EnumerateArray().First().AsValue().LeadingTrivia,
            "trailing" => document.TrailingTrivia,
            _ => document.RootElement.AsValue().LeadingTrivia
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    [InlineData(98765)]
    public void MixedTriviaAndStructureEditsShouldMatchIndependentModel(int seed)
    {
        var random = new Random(seed);
        using var document = Json5Document.Parse(
            "/*head*/[/*a*/{k:[/*b*/1,/*c*/2],/*n*/n:3},/*d*/[/*e*/4],/*f*/5]/*tail*/",
            options: s_parseOptions);
        var expected = JsonNode.Parse(Json5Document.EncodeToString(document.RootElement, s_writeOptions with { PreserveTrivia = false }))!;
        string[] replacements = ["0", "\"x\"", "true", "[]", "{}", "[/*new*/1,{k:[2]}]", "{k:[/*new*/3],n:4}"];
        for (var step = 0; step < 300; step++)
        {
            var targets = GetTargets(document.RootElement, expected);
            var target = targets[random.Next(targets.Count)];
            var operation = random.Next(9);
            var text = $"s{seed}_{step}";
            try
            {
                switch (operation)
                {
                    case 0:
                        target.Element.AsValue().LeadingTrivia.AddFirst(text, TriviaKind.BlockComment);
                        target.Element.AsValue().LeadingTrivia.AddLast(text, TriviaKind.LineComment);
                        break;
                    case 1:
                        foreach (var trivia in target.Element.EnumerateTrivia().ToArray())
                        {
                            if (trivia.Kind == TriviaKind.BlockComment || trivia.Kind == TriviaKind.LineComment)
                            {
                                trivia.ReplaceWith(text, TriviaKind.BlockComment);
                            }
                        }

                        break;
                    case 2:
                        target.Element.AsValue().LeadingTrivia.Clear();
                        break;
                    case 3:
                        using (var replacement = Json5Document.Parse(replacements[random.Next(replacements.Length)],
                                   options: s_parseOptions))
                        {
                            var model = JsonNode.Parse(Json5Document.EncodeToString(replacement.RootElement, s_writeOptions with { PreserveTrivia = false }))!;
                            var parent = target.Model.Parent;
                            var index = parent is JsonArray array ? array.IndexOf(target.Model) : -1;
                            var name = parent is JsonObject obj
                                ? obj.Single(pair => ReferenceEquals(pair.Value, target.Model)).Key
                                : null;
                            target.Element.AsValue().ReplaceWith(replacement.RootElement);
                            if (parent is JsonArray modelArray)
                            {
                                modelArray[index] = model;
                            }
                            else if (parent is JsonObject modelObject)
                            {
                                modelObject[name!] = model;
                            }
                            else
                            {
                                expected = model;
                            }
                        }

                        break;
                    case 4 when target.Model is JsonArray modelArray:
                        var arrayView = target.Element.AsArray();
                        var insertIndex = random.Next(arrayView.Count + 1);
                        arrayView.Insert(insertIndex, new DObject { ["v"] = step });
                        modelArray.Insert(insertIndex, new JsonObject { ["v"] = step });
                        if (arrayView.Count > 3)
                        {
                            var removeIndex = random.Next(arrayView.Count);
                            arrayView.RemoveAt(removeIndex);
                            modelArray.RemoveAt(removeIndex);
                        }

                        break;
                    case 5 when target.Model is JsonObject modelObject:
                        var objectView = target.Element.AsObject();
                        objectView["v"] = step;
                        modelObject["v"] = step;
                        if (modelObject.ContainsKey("n"))
                        {
                            objectView.Remove("n");
                            modelObject.Remove("n");
                        }

                        break;
                    case 6:
                        var originalOutput = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
                        using (var snapshot = document.Duplicate())
                        {
                            snapshot.RootElement.AsValue().LeadingTrivia.Clear();
                            snapshot.TrailingTrivia.Clear();
                            snapshot.RootElement.AsValue().LeadingTrivia.AddLast(text, TriviaKind.BlockComment);
                            AssertRoundTrip(snapshot, expected.ToJsonString());
                        }

                        Assert.Equal(originalOutput, Json5Document.EncodeToString(document.RootElement, s_writeOptions));
                        break;
                    case 7:
                        document.TrailingTrivia.AddFirst(text, TriviaKind.BlockComment);
                        document.TrailingTrivia.Clear();
                        document.TrailingTrivia.AddLast(text, TriviaKind.LineComment);
                        break;
                    case 8 when target.Model is JsonObject:
                        var properties = target.Element.EnumerateObject().ToArray();
                        if (properties.Length > 0)
                        {
                            var property = properties[random.Next(properties.Length)];
                            foreach (var trivia in property.LeadingTrivia.GetEnumerator().ToArray())
                            {
                                if (trivia.Kind == TriviaKind.BlockComment)
                                {
                                    trivia.ReplaceWith(text);
                                }
                            }

                            property.LeadingTrivia.Clear();
                            property.LeadingTrivia.AddLast(text, TriviaKind.BlockComment);
                        }

                        break;
                }

                AssertRoundTrip(document, expected.ToJsonString());
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Seed {seed}, step {step}, operation {operation}, path {target.Model.GetPath()}", error);
            }
        }
    }

    private static List<(DElement Element, JsonNode Model)> GetTargets(DElement element, JsonNode model)
    {
        var targets = new List<(DElement, JsonNode)> { (element, model) };
        if (model is JsonArray array)
        {
            var elements = element.EnumerateArray().ToArray();
            Assert.Equal(array.Count, elements.Length);
            for (var i = 0; i < elements.Length; i++)
            {
                targets.AddRange(GetTargets(elements[i], array[i]!));
            }
        }
        else if (model is JsonObject obj)
        {
            foreach (var property in element.EnumerateObject())
            {
                targets.AddRange(GetTargets(property.Value, obj[property.Name]!));
            }
        }

        return targets;
    }

    private static void AssertRoundTrip(Json5Document document, string expected)
    {
        Assert.Equal(expected, Json5Document.EncodeToString(document.RootElement, s_writeOptions with { PreserveTrivia = false }));
        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(expected, Json5Document.EncodeToString(reparsed.RootElement, s_writeOptions with { PreserveTrivia = false }));
        AssertComments(document.TrailingTrivia, reparsed.TrailingTrivia);
        AssertElementComments(document.RootElement, reparsed.RootElement);
    }

    private static void AssertElementComments(DElement original, DElement reparsed)
    {
        AssertComments(original.AsValue().LeadingTrivia, reparsed.AsValue().LeadingTrivia);
        if (original.Token.Type == DTokenType.Array)
        {
            var originalValues = original.EnumerateArray().ToArray();
            var reparsedValues = reparsed.EnumerateArray().ToArray();
            for (var i = 0; i < originalValues.Length; i++)
            {
                AssertElementComments(originalValues[i], reparsedValues[i]);
            }
        }
        else if (original.Token.Type == DTokenType.Map)
        {
            var originalProperties = original.EnumerateObject().ToArray();
            var reparsedProperties = reparsed.EnumerateObject().ToArray();
            for (var i = 0; i < originalProperties.Length; i++)
            {
                AssertComments(originalProperties[i].LeadingTrivia, reparsedProperties[i].LeadingTrivia);
                AssertElementComments(originalProperties[i].Value, reparsedProperties[i].Value);
            }
        }
    }

    private static void AssertComments(DTriviaCollection original, DTriviaCollection reparsed)
    {
        static (TriviaKind Kind, string Text)[] ReadComments(DTriviaCollection trivia)
        {
            return trivia.GetEnumerator()
                .Where(item => item.Kind is TriviaKind.Default or TriviaKind.LineComment or TriviaKind.BlockComment)
                .Select(item => (item.Kind == TriviaKind.Default ? TriviaKind.LineComment : item.Kind, item.GetString()))
                .ToArray();
        }

        Assert.Equal(ReadComments(original), ReadComments(reparsed));
    }
}