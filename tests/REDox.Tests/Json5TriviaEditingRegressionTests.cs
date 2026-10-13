// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class Json5TriviaEditingRegressionTests
{
    private static readonly Json5DocumentOptions s_parseOptions = new()
    {
        PreserveTrivia = true,
        EnableValueValidation = true
    };

    private static readonly Json5WriteOptions s_writeOptions = new()
    {
        PreserveTrivia = true,
        NewLine = "\n",
        PropertyNameStyle = Json5QuoteStyle.AlwaysDouble
    };

    public static TheoryData<string, TriviaKind, string, bool> ReplacedStructuralSeparatorCases()
    {
        var data = new TheoryData<string, TriviaKind, string, bool>();
        foreach (var source in new[]
                 {
                     "[1,2]", "[[1],2]", "[[],2]", "[{},2]", "[{a:[1]},2]",
                     "[[],{},3]", "{a:[],b:2}", "{a:{k:[1,],},b:2}", "[/*keep*/[1],/*next*/2]/*tail*/",
                     "[[],]", "[[1],]", "[{},]", "{a:[],}", "{a:{b:1},}", "[[],/*close*/]/*tail*/"
                 })
        foreach (var kind in new[]
                 {
                     TriviaKind.Default, TriviaKind.LineComment, TriviaKind.BlockComment,
                     TriviaKind.Whitespace, TriviaKind.Separator, TriviaKind.Tag, TriviaKind.Style
                 })
        foreach (var newLine in new[] { "\n", "\r\n" })
        foreach (var writeIndented in new[] { false, true })
        {
            data.Add(source, kind, newLine, writeIndented);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReplacedStructuralSeparatorCases))]
    public void ReplacingStructuralSeparatorShouldPreserveValuesAndComments(
        string source, TriviaKind kind, string newLine, bool writeIndented)
    {
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var plainOptions = s_writeOptions with { PreserveTrivia = false };
        var expected = Json5Document.EncodeToString(document.RootElement, plainOptions);
        var trailing = source is "[[],]" or "[[1],]" or "[{},]" or "{a:[],}" or "{a:{b:1},}" or "[[],/*close*/]/*tail*/";
        var collection = trailing
            ? document.TrailingTrivia
            : document.RootElement.Token.Type == DTokenType.Array
                ? document.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia
                : document.RootElement.EnumerateObject().Last().LeadingTrivia;
        var separator = collection.GetEnumerator().Last(item => item.Kind == TriviaKind.Separator &&
                                                                (trailing ? item.GetString() is "]" or "}" : item.GetString() == ","));
        separator.ReplaceWith(kind == TriviaKind.Whitespace ? " " : "edited", kind);
        var expectedComments = ReadComments(collection).Select(item => item.Text).ToArray();

        var encoded = Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { NewLine = newLine, WriteIndented = writeIndented });
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(expected, Json5Document.EncodeToString(reparsed.RootElement, plainOptions));
        var reparsedCollection = trailing
            ? reparsed.TrailingTrivia
            : reparsed.RootElement.Token.Type == DTokenType.Array
                ? reparsed.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia
                : reparsed.RootElement.EnumerateObject().Last().LeadingTrivia;
        Assert.Equal(expectedComments, ReadComments(reparsedCollection).Select(item => item.Text));
        Assert.Equal(expected, Json5Document.EncodeToString(document.RootElement, plainOptions));
    }

    public static TheoryData<string, string, bool> PropertyValueOutputCases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var value in new[] { "1", "'text'", "true", "null", "[]", "{}", "[/*inside*/1]", "{k:[1,2]}" })
        foreach (var comment in new[] { "", "/*keep*/", "//keep\n" })
        foreach (var newLine in new[] { "\n", "\r\n" })
        foreach (var writeIndented in new[] { false, true })
        {
            data.Add("{before:[],a:" + comment + value + ",after:2}", newLine, writeIndented);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PropertyValueOutputCases))]
    public void WritingPropertyValueShouldOmitParentColon(string source, string newLine, bool writeIndented)
    {
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var value = document.RootElement.GetProperty("a");
        var expected = Json5Document.EncodeToString(value, s_writeOptions with { PreserveTrivia = false });
        var comments = ReadComments(value.AsValue().LeadingTrivia);

        var encoded = Json5Document.EncodeToString(value,
            s_writeOptions with { NewLine = newLine, WriteIndented = writeIndented });
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(expected, Json5Document.EncodeToString(reparsed.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
        Assert.Equal(comments, ReadComments(reparsed.RootElement.AsValue().LeadingTrivia));
    }

    [Theory]
    [InlineData(" \t/*leading*/{ /*key*/\"a\" : [ 1 , 2 , ], /*last*/\"b\" : 3 } /*tail*/\r\n")]
    [InlineData("[ 1 , //next\r\n 2 ] //tail\n")]
    public void UnextendedTriviaShouldPreserveWhitespaceAndSeparators(string source)
    {
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        Assert.Equal(source, Json5Document.EncodeToString(document.RootElement, s_writeOptions));

        // Materializing the lists keeps the parsed trivia tokens unextended.
        _ = document.RootElement.AsValue().LeadingTrivia;
        _ = document.TrailingTrivia;
        Assert.Equal(source, Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    public static TheoryData<bool, bool, TriviaKind, bool> EditedTriviaKindCases()
    {
        var data = new TheoryData<bool, bool, TriviaKind, bool>();
        foreach (var parsedDocument in new[] { false, true })
        foreach (var replace in new[] { false, true })
        foreach (var kind in new[]
                 {
                     TriviaKind.Default, TriviaKind.LineComment, TriviaKind.BlockComment,
                     TriviaKind.Whitespace, TriviaKind.Separator, TriviaKind.Tag, TriviaKind.Style
                 })
        foreach (var writeIndented in new[] { false, true })
        {
            data.Add(parsedDocument, replace, kind, writeIndented);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EditedTriviaKindCases))]
    public void EditedTriviaShouldOnlyAcceptCommentKinds(
        bool parsedDocument, bool replace, TriviaKind kind, bool writeIndented)
    {
        using var document = Json5Document.Parse("/*old*/[1,2]", options: s_parseOptions);
        var element = parsedDocument ? document.RootElement : new DArray(1, 2).AsElement();
        var trivia = element.AsValue().LeadingTrivia;
        if (replace)
        {
            if (!parsedDocument)
            {
                trivia.AddLast("old", TriviaKind.BlockComment);
            }

            trivia.GetEnumerator().Single().ReplaceWith("edited", kind);
        }
        else
        {
            trivia.AddLast("edited", kind);
        }

        var options = s_writeOptions with { WriteIndented = writeIndented };
        var encoded = Json5Document.EncodeToString(element, options);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal("[1,2]", Json5Document.EncodeToString(reparsed.RootElement));
        if (kind is TriviaKind.Default or TriviaKind.LineComment or TriviaKind.BlockComment)
        {
            Assert.Equal(replace || !parsedDocument ? new[] { "edited" } : new[] { "old", "edited" },
                ReadComments(reparsed.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
        }
        else
        {
            Assert.DoesNotContain("edited", encoded);
            Assert.Equal(replace || !parsedDocument ? Array.Empty<string>() : new[] { "old" },
                ReadComments(reparsed.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
        }

        Assert.Equal("[1,2]", Json5Document.EncodeToString(element,
            s_writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData("[{}]/*tail*/", "{v:[]}", "root")]
    [InlineData("[[{v:[3]}], /*sibling*/5]", "{v:[{w:4}]}", "array")]
    [InlineData("{a:[{v:[3]}], /*sibling*/b:5}", "{v:[{w:4}]}", "object")]
    [InlineData("{a:[{v:[3]}]}", "{v:[{w:4}]}", "object")]
    [InlineData("[{v:[3]}] /*tail*/", "{v:[{w:4}]}", "root")]
    [InlineData("[{v:[{w:4}]}, /*sibling*/5]", "[{v:[3]}]", "array")]
    [InlineData("{a:{v:[{w:4}]}, /*sibling*/b:5}", "[{v:[3]}]", "object")]
    [InlineData("[{v:[{w:4}]}]", "[{v:[3]}]", "array")]
    [InlineData("{v:[{w:4}]} /*tail*/", "[{v:[3]}]", "root")]
    public void ReplacingMixedContainersShouldPreserveClosingBracketOrder(
        string input, string replacement, string targetKind)
    {
        using var document = Json5Document.Parse(input, options: s_parseOptions);
        using var replacementDocument = Json5Document.Parse(replacement, options: s_parseOptions);
        var target = targetKind switch
        {
            "root" => document.RootElement.AsValue(),
            "array" => document.RootElement.AsArray()[0],
            "object" => document.RootElement.GetProperty("a").AsValue(),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind))
        };

        target.ReplaceWith(replacementDocument.RootElement);

        AssertValuesRoundTrip(document);
    }

    [Theory]
    [InlineData("[[{}],{v:[]},0]/*tail*/")]
    [InlineData("[/*a*/[{v:[3]}], /*b*/{v:[{w:4}]}, /*c*/6,] /*tail*/")]
    [InlineData("[/*a*/{v:[{w:4}]}, /*b*/[{v:[3]}], /*c*/6,] /*tail*/")]
    public void RemovingMiddleArrayElementShouldPreserveClosingBracketOrder(string input)
    {
        using var document = Json5Document.Parse(input, options: s_parseOptions);

        document.RootElement.AsArray().RemoveAt(1);

        AssertValuesRoundTrip(document);
    }

    [Theory]
    [InlineData("{/*a*/a:[{v:[3]}], /*b*/b:{v:[{w:4}]}, /*c*/c:6,} /*tail*/")]
    [InlineData("{/*a*/a:{v:[{w:4}]}, /*b*/b:[{v:[3]}], /*c*/c:6,} /*tail*/")]
    public void RemovingMiddlePropertyShouldPreserveClosingBracketOrder(string input)
    {
        using var document = Json5Document.Parse(input, options: s_parseOptions);

        Assert.True(document.RootElement.AsObject().Remove("b"));

        AssertValuesRoundTrip(document);
    }

    public static TheoryData<string, int> MixedContainerInsertionCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var value in new[] { "0", "null", "[]", "{}", "[1]", "{v:2}", "[{v:[3]}]", "{v:[{w:4}]}" })
        {
            data.Add(value, 1);
            data.Add(value, 2);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MixedContainerInsertionCases))]
    public void InsertingBetweenMixedContainersShouldPreserveClosingBracketOrder(string otherValue, int index)
    {
        const string nested = "{v:[{w:4}]}";
        var first = index == 1 ? nested : otherValue;
        var second = index == 1 ? otherValue : nested;
        using var document = Json5Document.Parse($"[/*a*/{first}, /*b*/{second}, /*c*/6,] /*tail*/",
            options: s_parseOptions);

        document.RootElement.AsArray().Insert(index, new DArray(new DObject { ["n"] = 7 }));

        AssertValuesRoundTrip(document);
    }

    [Theory]
    [InlineData("/*inner*/")]
    [InlineData("//inner\n")]
    [InlineData("//inner\r\n")]
    public void ReplacingMixedContainersShouldKeepCommentsBetweenClosers(string innerComment)
    {
        using var document = Json5Document.Parse("[{v:1" + innerComment + "}/*outer*/]/*tail*/", options: s_parseOptions);
        using var replacement = Json5Document.Parse("{v:[0]}", options: s_parseOptions);

        document.RootElement.AsValue().ReplaceWith(replacement.RootElement);

        Assert.Equal("{\"v\":[0" + innerComment + "]/*outer*/}/*tail*/",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        AssertValuesRoundTrip(document);
    }

    [Theory]
    [InlineData(int.MaxValue - 63, false)]
    [InlineData(int.MaxValue - 63, true)]
    [InlineData(int.MaxValue - 62, false)]
    [InlineData(int.MaxValue - 62, true)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MaxValue, true)]
    public void WritingScalarWithLargeMaxDepthShouldSucceed(int maxDepth, bool preserveTrivia)
    {
        using var document = Json5Document.Parse("1", options: s_parseOptions);

        Assert.Equal("1", Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { MaxDepth = maxDepth, PreserveTrivia = preserveTrivia }));
    }

    [Theory]
    [InlineData(int.MaxValue - 63)]
    [InlineData(int.MaxValue - 62)]
    [InlineData(int.MaxValue)]
    public void WritingDeepContainersWithLargeMaxDepthShouldGrowClosingTokenBuffer(int maxDepth)
    {
        const int depth = 513;
        var source = "1";
        for (var i = 0; i < depth; i++)
        {
            source = i % 2 == 0 ? "[" + source + "]" : "{\"v\":" + source + "}";
        }

        var parseOptions = s_parseOptions with { MaxDepth = depth * 2 + 1 };
        using var document = Json5Document.Parse(source + "/*tail*/", options: parseOptions);

        var encoded = Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { MaxDepth = maxDepth });
        Assert.Equal(source + "/*tail*/", encoded);
        using var reparsed = Json5Document.Parse(encoded, options: parseOptions);
        Assert.Equal(source, Json5Document.EncodeToString(reparsed.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(130)]
    [InlineData(513)]
    public void ReplacingDeepMixedContainersShouldPreserveClosingBracketOrder(int depth)
    {
        var source = "0";
        var replacementSource = "0";
        for (var i = 0; i < depth; i++)
        {
            source = i % 2 == 0 ? "[" + source + "]" : "{v:" + source + "}";
            replacementSource = i % 2 == 0 ? "{v:" + replacementSource + "}" : "[" + replacementSource + "]";
        }

        // The parser also pushes a state for each object's property value.
        var parseOptions = s_parseOptions with { MaxDepth = depth * 2 + 1 };
        var writeOptions = s_writeOptions with { MaxDepth = depth + 1 };
        using var document = Json5Document.Parse(source + "/*tail*/", options: parseOptions);
        using var replacement = Json5Document.Parse(replacementSource, options: parseOptions);
        var expected = Json5Document.EncodeToString(replacement.RootElement, writeOptions with { PreserveTrivia = false });

        document.RootElement.AsValue().ReplaceWith(replacement.RootElement);

        var encoded = Json5Document.EncodeToString(document.RootElement, writeOptions);
        Assert.Equal(expected + "/*tail*/", encoded);
        using var reparsed = Json5Document.Parse(encoded, options: parseOptions);
        Assert.Equal(expected, Json5Document.EncodeToString(reparsed.RootElement, writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingWhitespaceAfterLineCommentShouldBeIgnored(bool prependComment)
    {
        using var document = Json5Document.Parse("[1,// two\n2]", options: s_parseOptions);
        var trivia = document.RootElement.AsArray()[1].LeadingTrivia;
        if (prependComment)
        {
            trivia.AddFirst(" first", TriviaKind.BlockComment);
        }

        foreach (var item in trivia)
        {
            if (item.Kind == TriviaKind.Whitespace)
            {
                item.ReplaceWith("\r\n");
            }
        }

        Assert.Equal(prependComment ? "[1/* first*/\n,// two\n2]" : "[1,// two\n2]",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        AssertValuesRoundTrip(document);
        Assert.Equal("[1,2]", Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    [Fact]
    public void AddedWhitespaceShouldBeIgnored()
    {
        using var document = Json5Document.Parse("{a:1}", options: s_parseOptions);

        document.RootElement.GetProperty("a").AsValue().LeadingTrivia.AddLast("\n  ", TriviaKind.Whitespace);

        Assert.Equal("{\"a\":1}", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal("{\"a\":1}", Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    public static TheoryData<string, string> AddedWhitespaceCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var location in new[] { "leading", "trailing", "property", "array", "node" })
        {
            foreach (var whitespace in new[] { " ", "\t", "\n", "\r\n \t", "\u00a0\u3000" })
            {
                data.Add(location, whitespace);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AddedWhitespaceCases))]
    public void AddedWhitespaceShouldBeIgnoredAtEveryLocation(string location, string whitespace)
    {
        if (location == "node")
        {
            var array = new DArray(1, 2);
            array[1].LeadingTrivia.AddLast(whitespace, TriviaKind.Whitespace);
            Assert.Equal("[1,2]", Json5Document.EncodeToString(array.AsElement(), s_writeOptions));
            Assert.Equal("[1,2]", Json5Document.EncodeToString(array.AsElement(),
                s_writeOptions with { PreserveTrivia = false }));
            return;
        }

        var input = location switch { "property" => "{a:1}", "array" => "[1,2]", _ => "{}" };
        using var document = Json5Document.Parse(input, options: s_parseOptions);
        var trivia = location switch
        {
            "trailing" => document.TrailingTrivia,
            "property" => document.RootElement.GetProperty("a").AsValue().LeadingTrivia,
            "array" => document.RootElement.AsArray()[1].LeadingTrivia,
            _ => document.RootElement.AsValue().LeadingTrivia
        };
        trivia.AddLast(whitespace, TriviaKind.Whitespace);

        var expected = location switch { "property" => "{\"a\":1}", "array" => "[1,2]", _ => "{}" };
        Assert.Equal(expected, Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal(expected, Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    [InlineData("\r", false)]
    [InlineData("\r", true)]
    public void ReplacedLineCommentTerminatorShouldBeIgnored(string newLine, bool trailing)
    {
        using var document = Json5Document.Parse(trailing ? "1//comment\n" : "[1,//comment\n2]", options: s_parseOptions);
        var trivia = trailing ? document.TrailingTrivia : document.RootElement.AsArray()[1].LeadingTrivia;
        foreach (var item in trivia)
        {
            if (item.Kind == TriviaKind.Whitespace && item.GetString() == "\n")
            {
                item.ReplaceWith(newLine);
            }
        }

        Assert.Equal(trailing ? "1//comment" : "[1,//comment\n2]",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        AssertValuesRoundTrip(document);
        Assert.Equal(trailing ? "1" : "[1,2]", Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingParsedLeadingCommentsShouldKeepThemInOutput(bool propertyValue)
    {
        var input = propertyValue ? "{a:/*first*/ /*last*/1}" : "/*first*/ /*last*/1";
        using var document = Json5Document.Parse(input, options: s_parseOptions);
        var value = propertyValue ? document.RootElement.GetProperty("a") : document.RootElement;
        foreach (var trivia in value.AsValue().LeadingTrivia)
        {
            if (trivia.Kind == TriviaKind.BlockComment)
            {
                trivia.ReplaceWith("changed");
            }
        }

        using var reparsed = Json5Document.Parse(
            Json5Document.EncodeToString(document.RootElement, s_writeOptions), options: s_parseOptions);
        var reparsedValue = propertyValue ? reparsed.RootElement.GetProperty("a") : reparsed.RootElement;
        Assert.Equal(new[] { "changed", "changed" },
            ReadComments(reparsedValue.AsValue().LeadingTrivia).Select(trivia => trivia.Text));
        Assert.Equal(1, reparsedValue.GetInt32());
    }

    public static TheoryData<string, string, bool> DirectTriviaReplacementCases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var value in new[] { "1", "18446744073709551615", "1.25", "true", "null", "'text'", "[]", "{a:[1]}" })
        {
            foreach (var context in new[] { "root", "property", "array" })
            {
                data.Add(value, context, false);
                data.Add(value, context, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DirectTriviaReplacementCases))]
    public void DirectTriviaReplacementShouldPreserveCommentsAndValues(string valueSource, string context, bool reverse)
    {
        var source = "/*first*/ \t/*last*/\n" + valueSource;
        source = context switch { "property" => "{value:" + source + "}", "array" => "[" + source + "]", _ => source };
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var plainOptions = s_writeOptions with { PreserveTrivia = false };
        var expectedValue = Json5Document.EncodeToString(document.RootElement, plainOptions);
        var value = TriviaReplacementTarget(document, context);
        var trivia = value.EnumerateTrivia().ToArray();
        foreach (var item in reverse ? trivia.Reverse() : trivia)
        {
            if (item.Kind == TriviaKind.BlockComment)
            {
                item.ReplaceWith(item.GetString() + " edited");
            }
        }

        Assert.True(value.Token.IsExtended);
        Assert.Equal(new[] { "first edited", "last edited" }, ReadComments(value.AsValue().LeadingTrivia).Select(item => item.Text));
        foreach (var item in value.EnumerateTrivia())
        {
            if (item.Kind == TriviaKind.BlockComment)
            {
                item.ReplaceWith(item.GetString() + " again");
            }
        }

        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(new[] { "first edited again", "last edited again" },
            ReadComments(TriviaReplacementTarget(reparsed, context).AsValue().LeadingTrivia).Select(item => item.Text));
        Assert.Equal(expectedValue, Json5Document.EncodeToString(document.RootElement, plainOptions));
        Assert.Equal(expectedValue, Json5Document.EncodeToString(reparsed.RootElement, plainOptions));
    }

    [Fact]
    public void DirectCommentReplacementShouldExtendTriviaListBeforeFurtherEdits()
    {
        using var document = Json5Document.Parse("/*first*/ /*last*/1", options: s_parseOptions);
        var trivia = document.RootElement.EnumerateTrivia().First();

        trivia.ReplaceWith("changed");

        Assert.True(document.RootElement.Token.IsExtended);
        Assert.Equal(new[] { "changed", "last" },
            ReadComments(document.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
        document.RootElement.AsValue().LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
        document.RootElement.AsValue().ReplaceWith(2);

        using var reparsed = Json5Document.Parse(
            Json5Document.EncodeToString(document.RootElement, s_writeOptions), options: s_parseOptions);
        Assert.Equal(2, reparsed.RootElement.GetInt32());
        Assert.Equal(new[] { "changed", "last", "added" },
            ReadComments(reparsed.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
    }

    [Theory]
    [InlineData("+1", 1UL)]
    [InlineData("9223372036854775807", 9223372036854775807UL)]
    [InlineData("9223372036854775808", 9223372036854775808UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("+18446744073709551615", ulong.MaxValue)]
    public void DirectTriviaReplacementShouldPreserveUnsignedInteger(string source, ulong expected)
    {
        using var document = Json5Document.Parse("/*comment*/" + source, options: s_parseOptions);
        Assert.Equal(expected, document.RootElement.GetUInt64());

        document.RootElement.EnumerateTrivia().Single().ReplaceWith("changed");

        Assert.True(document.RootElement.Token.IsExtended);
        Assert.Equal(expected, document.RootElement.GetUInt64());
        using var reparsed = Json5Document.Parse(
            Json5Document.EncodeToString(document.RootElement, s_writeOptions), options: s_parseOptions);
        Assert.Equal(expected, reparsed.RootElement.GetUInt64());
        Assert.Equal(new[] { "changed" },
            ReadComments(reparsed.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
    }

    [Fact]
    public void DirectWhitespaceReplacementShouldBeIgnored()
    {
        using var document = Json5Document.Parse("[1,//comment\n2]", options: s_parseOptions);
        foreach (var item in document.RootElement.EnumerateArray().Last().EnumerateTrivia())
        {
            if (item.Kind == TriviaKind.Whitespace)
            {
                item.ReplaceWith("\r\n");
            }
        }

        Assert.Equal("[1,//comment\n2]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        AssertValuesRoundTrip(document);
        Assert.Equal("[1,2]", Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    public static TheoryData<string, string, string, bool> LineCommentTerminatorReplacementCases()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var location in new[] { "root", "array", "property", "key", "closingArray", "closingObject", "closingNested", "siblingAfterContainer" })
        {
            foreach (var whitespace in new[] { "", " ", "\t", " \t", "\n", "\r", "\r\n" })
            {
                foreach (var newLine in new[] { "\n", "\r\n" })
                {
                    data.Add(location, whitespace, newLine, false);
                    data.Add(location, whitespace, newLine, true);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LineCommentTerminatorReplacementCases))]
    public void ReplacingLineCommentTerminatorShouldBeIgnoredAtEveryLocation(
        string location, string whitespace, string newLine, bool writeIndented)
    {
        var source = location switch
        {
            "root" => "//comment\n1",
            "array" => "[1,//comment\n2]",
            "property" => "{a://comment\n1}",
            "key" => "{a:1,//comment\nb:2}",
            "closingArray" => "[1//comment\n]",
            "closingObject" => "{a:1//comment\n}",
            "closingNested" => "{a:[1//comment\n]}",
            "siblingAfterContainer" => "[[1//comment\n],2]",
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var options = s_writeOptions with { NewLine = newLine, WriteIndented = writeIndented };
        var expectedValue = Json5Document.EncodeToString(document.RootElement, s_writeOptions with { PreserveTrivia = false });
        var trivia = LineCommentTerminatorTrivia(document, location);
        var expectedComments = ReadComments(trivia);
        var terminator = trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.Whitespace && item.GetString() == "\n");

        terminator.ReplaceWith(whitespace);

        var encoded = Json5Document.EncodeToString(document.RootElement, options);
        Assert.Contains("//comment" + newLine, encoded);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(expectedValue, Json5Document.EncodeToString(reparsed.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
        Assert.Equal(expectedComments, ReadComments(LineCommentTerminatorTrivia(reparsed, location)));
        Assert.Equal(expectedValue, Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
        Assert.Equal(expectedComments, ReadComments(trivia));
        Assert.Equal(whitespace, terminator.GetString());
    }

    private static DTriviaCollection LineCommentTerminatorTrivia(Json5Document document, string location)
    {
        return location switch
        {
            "root" => document.RootElement.AsValue().LeadingTrivia,
            "array" or "siblingAfterContainer" => document.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia,
            "property" => document.RootElement.GetProperty("a").AsValue().LeadingTrivia,
            "key" => document.RootElement.EnumerateObject().Last().LeadingTrivia,
            _ => document.TrailingTrivia
        };
    }

    [Fact]
    public void AddedCommentShouldNotBecomeLeadingTriviaOfLaterInsertedContainer()
    {
        using var document = Json5Document.Parse("[1]", options: s_parseOptions);
        var array = document.RootElement.AsArray();
        array[0].LeadingTrivia.AddLast("first", TriviaKind.BlockComment);
        array[0].LeadingTrivia.GetEnumerator().Single().ReplaceWith("first only");

        array.Add(new DArray(2));

        Assert.Empty(ReadComments(array[1].LeadingTrivia));
        Assert.Equal("[/*first only*/\n1,[2]]", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        AssertValuesRoundTrip(document);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{a:1}")]
    public void DirectlyEditedCommentsShouldSurviveSnapshotAndValueReplacement(string valueSource)
    {
        using var document = Json5Document.Parse("/*first*/ /*last*/" + valueSource, options: s_parseOptions);
        foreach (var item in document.RootElement.EnumerateTrivia())
        {
            if (item.Kind == TriviaKind.BlockComment)
            {
                item.ReplaceWith("changed");
            }
        }

        using var snapshot = document.Duplicate();
        document.RootElement.AsValue().ReplaceWith(2);
        document.Dispose();

        Assert.Equal(new[] { "changed", "changed" }, ReadComments(snapshot.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
        AssertValuesRoundTrip(snapshot);
        snapshot.RootElement.AsValue().ReplaceWith(3);
        using var reparsed = Json5Document.Parse(Json5Document.EncodeToString(snapshot.RootElement, s_writeOptions), options: s_parseOptions);
        Assert.Equal(3, reparsed.RootElement.GetInt32());
        Assert.Equal(new[] { "changed", "changed" }, ReadComments(reparsed.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
    }

    private static DElement TriviaReplacementTarget(Json5Document document, string context)
    {
        return context switch
        {
            "property" => document.RootElement.GetProperty("value"),
            "array" => document.RootElement.EnumerateArray().First(),
            _ => document.RootElement
        };
    }

    public static TheoryData<TriviaKind, string, bool> CommentAppendAtEofCases()
    {
        var data = new TheoryData<TriviaKind, string, bool>();
        foreach (var kind in new[] { TriviaKind.LineComment, TriviaKind.BlockComment, TriviaKind.Default })
        {
            foreach (var newLine in new[] { "\n", "\r\n" })
            {
                data.Add(kind, newLine, false);
                data.Add(kind, newLine, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CommentAppendAtEofCases))]
    public void AppendingCommentAfterLineCommentAtEofShouldKeepCommentsSeparate(
        TriviaKind kind, string newLine, bool writeIndented)
    {
        using var document = Json5Document.Parse("1// old", options: s_parseOptions);

        document.TrailingTrivia.AddLast(" new", kind);

        var encoded = Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { NewLine = newLine, WriteIndented = writeIndented });
        var addedComment = kind == TriviaKind.BlockComment ? "/* new*/" : "// new";
        Assert.Equal("1// old" + newLine + addedComment + newLine, encoded);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        var expectedKind = kind == TriviaKind.Default ? TriviaKind.LineComment : kind;
        Assert.Equal(new[] { (TriviaKind.LineComment, " old"), (expectedKind, " new") },
            ReadComments(reparsed.TrailingTrivia));
        Assert.Equal(1, reparsed.RootElement.GetInt32());
    }

    [Theory]
    [InlineData("1//", "")]
    [InlineData("[]// old", " old")]
    [InlineData("{a:[{}]}// old", " old")]
    public void AppendingMultipleCommentsAfterLineCommentAtEofShouldPreserveCommentsAndValue(
        string source, string oldComment)
    {
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var plainOptions = s_writeOptions with { PreserveTrivia = false };
        var expectedValue = Json5Document.EncodeToString(document.RootElement, plainOptions);
        document.TrailingTrivia.AddLast(" block", TriviaKind.BlockComment);
        document.TrailingTrivia.AddLast(" line", TriviaKind.LineComment);

        using var reparsed = Json5Document.Parse(
            Json5Document.EncodeToString(document.RootElement, s_writeOptions), options: s_parseOptions);
        Assert.Equal(new[]
        {
            (TriviaKind.LineComment, oldComment),
            (TriviaKind.BlockComment, " block"),
            (TriviaKind.LineComment, " line")
        }, ReadComments(reparsed.TrailingTrivia));
        Assert.Equal(expectedValue, Json5Document.EncodeToString(reparsed.RootElement, plainOptions));
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\r", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\r", true)]
    public void AppendingCommentAfterTerminatedLineCommentShouldNotInsertExtraNewLine(
        string terminator, bool addWhitespace)
    {
        using var document = Json5Document.Parse("1// old" + (addWhitespace ? "" : terminator), options: s_parseOptions);
        if (addWhitespace)
        {
            document.TrailingTrivia.AddLast(terminator, TriviaKind.Whitespace);
        }

        document.TrailingTrivia.AddLast(" new", TriviaKind.BlockComment);

        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        Assert.Equal("1// old" + (addWhitespace ? "\n" : terminator) + "/* new*/\n", encoded);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(new[] { (TriviaKind.LineComment, " old"), (TriviaKind.BlockComment, " new") },
            ReadComments(reparsed.TrailingTrivia));
    }

    [Theory]
    [InlineData("1//")]
    [InlineData("1// old")]
    [InlineData("[{}]// old")]
    [InlineData("1// old\r\n")]
    public void UneditedTrailingLineCommentShouldKeepOriginalTermination(string source)
    {
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        Assert.Equal(source, Json5Document.EncodeToString(document.RootElement, s_writeOptions));

        _ = document.TrailingTrivia;

        Assert.Equal(source, Json5Document.EncodeToString(document.RootElement, s_writeOptions));
    }

    [Fact]
    public void AppendingCommentAfterLineCommentAtEofShouldRespectPreserveTriviaOption()
    {
        using var document = Json5Document.Parse("1// old", options: s_parseOptions);
        document.TrailingTrivia.AddLast(" new", TriviaKind.BlockComment);

        Assert.Equal("1", Json5Document.EncodeToString(document.RootElement,
            s_writeOptions with { PreserveTrivia = false }));
    }

    public static TheoryData<bool, string, TriviaKind, bool, bool> MultilineLineCommentCases()
    {
        var data = new TheoryData<bool, string, TriviaKind, bool, bool>();
        foreach (var trailing in new[] { false, true })
        {
            foreach (var newLine in new[] { "\n", "\r\n", "\r", "\u2028", "\u2029" })
            {
                foreach (var kind in new[] { TriviaKind.LineComment, TriviaKind.Default })
                {
                    foreach (var replace in new[] { false, true })
                    {
                        data.Add(trailing, newLine, kind, replace, false);
                        data.Add(trailing, newLine, kind, replace, true);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MultilineLineCommentCases))]
    public void MultilineLineCommentShouldCommentEveryLine(
        bool trailing, string newLine, TriviaKind kind, bool replace, bool writeIndented)
    {
        var source = replace ? trailing ? "{}/*old*/" : "/*old*/{}" : "{}";
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var trivia = trailing ? document.TrailingTrivia : document.RootElement.AsValue().LeadingTrivia;
        var text = "one" + newLine + "two";
        if (replace)
        {
            trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment).ReplaceWith(text, kind);
        }
        else
        {
            trivia.AddLast(text, kind);
        }

        var outputNewLine = newLine == "\r\n" ? "\r\n" : "\n";
        var options = s_writeOptions with { NewLine = outputNewLine, WriteIndented = writeIndented };
        var encoded = Json5Document.EncodeToString(document.RootElement, options);
        var expectedComment = "//one" + outputNewLine + "//two" + outputNewLine;
        Assert.Equal(trailing ? "{}" + expectedComment : expectedComment + "{}", encoded);

        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        var comments = trailing ? reparsed.TrailingTrivia : reparsed.RootElement.AsObject().LeadingTrivia;
        Assert.Equal(new[] { "one", "two" }, ReadComments(comments).Select(item => item.Text));
        Assert.Equal(0, reparsed.RootElement.AsObject().Count);
    }

    [Theory]
    [InlineData("", "//\n")]
    [InlineData("\n", "//\n//\n")]
    [InlineData("\r\n", "//\n//\n")]
    [InlineData("\n\n", "//\n//\n//\n")]
    [InlineData("\r\r", "//\n//\n//\n")]
    [InlineData("one\n", "//one\n//\n")]
    [InlineData("\n[1,2]\n*/\n/*", "//\n//[1,2]\n//*/\n///*\n")]
    [InlineData("日本語\r\n次\u2028末\u2029", "//日本語\n//次\n//末\n//\n")]
    public void LineCommentShouldPreserveEmptyLinesAndCommentAllText(string text, string expectedComment)
    {
        using var document = Json5Document.Parse("{}", options: s_parseOptions);
        document.RootElement.AsValue().LeadingTrivia.AddLast(text, TriviaKind.LineComment);

        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        Assert.Equal(expectedComment + "{}", encoded);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        var expectedLines = expectedComment.Split('\n').SkipLast(1).Select(line => line.Substring(2));
        Assert.Equal(expectedLines, ReadComments(reparsed.RootElement.AsValue().LeadingTrivia).Select(item => item.Text));
        Assert.Equal(0, reparsed.RootElement.AsObject().Count);
    }

    [Theory]
    [InlineData("property", TriviaKind.LineComment)]
    [InlineData("property", TriviaKind.Default)]
    [InlineData("array", TriviaKind.LineComment)]
    [InlineData("array", TriviaKind.Default)]
    [InlineData("node", TriviaKind.LineComment)]
    [InlineData("node", TriviaKind.Default)]
    public void MultilineCommentShouldKeepFollowingValuesOutsideComment(string context, TriviaKind kind)
    {
        const string text = "first\r\nsecond\u2028third\n";
        if (context == "node")
        {
            var array = new DArray(1, 2);
            array[1].LeadingTrivia.AddLast(text, kind);
            using var reparsedNode = Json5Document.Parse(Json5Document.EncodeToString(array.AsElement(), s_writeOptions),
                options: s_parseOptions);
            Assert.Equal("[1,2]", Json5Document.EncodeToString(reparsedNode.RootElement));
            Assert.Equal(new[] { "first", "second", "third", "" },
                ReadComments(reparsedNode.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia).Select(item => item.Text));
            return;
        }

        using var document = Json5Document.Parse(context == "property" ? "{a:1}" : "[1,2]", options: s_parseOptions);
        var value = context == "property" ? document.RootElement.GetProperty("a") : document.RootElement.EnumerateArray().Last();
        value.AsValue().LeadingTrivia.AddLast(text, kind);

        using var reparsed = Json5Document.Parse(Json5Document.EncodeToString(document.RootElement, s_writeOptions),
            options: s_parseOptions);
        var reparsedValue = context == "property" ? reparsed.RootElement.GetProperty("a") : reparsed.RootElement.EnumerateArray().Last();
        Assert.Equal(context == "property" ? 1 : 2, reparsedValue.GetInt32());
        Assert.Equal(new[] { "first", "second", "third", "" },
            ReadComments(reparsedValue.AsValue().LeadingTrivia).Select(item => item.Text));
        AssertValuesRoundTrip(document);
    }

    public static TheoryData<string, string, bool> BlockCommentTerminatorCases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var text in new[] { "one*/two", "*/", "*/one*/two*/", "one\n*/\ntwo" })
        {
            foreach (var context in new[] { "leading", "trailing", "property", "array" })
            {
                data.Add(text, context, false);
                data.Add(text, context, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BlockCommentTerminatorCases))]
    public void BlockCommentTerminatorShouldNotProduceInvalidJson5(string text, string context, bool replace)
    {
        var comment = replace ? "/*old*/" : "";
        var source = context switch
        {
            "trailing" => "1" + comment,
            "property" => "{a:" + comment + "1}",
            "array" => "[1," + comment + "2]",
            _ => comment + "1"
        };
        using var document = Json5Document.Parse(source, options: s_parseOptions);
        var plainOptions = s_writeOptions with { PreserveTrivia = false };
        var expectedValue = Json5Document.EncodeToString(document.RootElement, plainOptions);
        var trivia = context switch
        {
            "trailing" => document.TrailingTrivia,
            "property" => document.RootElement.GetProperty("a").AsValue().LeadingTrivia,
            "array" => document.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia,
            _ => document.RootElement.AsValue().LeadingTrivia
        };
        if (replace)
        {
            trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment).ReplaceWith(text);
        }
        else
        {
            trivia.AddLast(text, TriviaKind.BlockComment);
        }

        Assert.Throws<ArgumentException>(() => Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Equal(text, trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment).GetString());
        var encodedWithoutTrivia = Json5Document.EncodeToString(document.RootElement, plainOptions);
        Assert.Equal(expectedValue, encodedWithoutTrivia);
        using var withoutTrivia = Json5Document.Parse(encodedWithoutTrivia, options: s_parseOptions);
        Assert.Equal(expectedValue, Json5Document.EncodeToString(withoutTrivia.RootElement, plainOptions));

        trivia.GetEnumerator().Single(item => item.Kind == TriviaKind.BlockComment).ReplaceWith("safe");
        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        var reparsedTrivia = context switch
        {
            "trailing" => reparsed.TrailingTrivia,
            "property" => reparsed.RootElement.GetProperty("a").AsValue().LeadingTrivia,
            "array" => reparsed.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia,
            _ => reparsed.RootElement.AsValue().LeadingTrivia
        };
        Assert.Equal(new[] { (TriviaKind.BlockComment, "safe") }, ReadComments(reparsedTrivia));
        AssertValuesRoundTrip(document);
    }

    [Fact]
    public void BlockCommentTerminatorOnNewNodeShouldBeRejectedWhenWritingTrivia()
    {
        var array = new DArray(1, 2);
        array[1].LeadingTrivia.AddLast("one*/two", TriviaKind.BlockComment);

        Assert.Throws<ArgumentException>(() => Json5Document.EncodeToString(array.AsElement(), s_writeOptions));
        Assert.Equal("[1,2]", Json5Document.EncodeToString(array.AsElement(),
            s_writeOptions with { PreserveTrivia = false }));
    }

    [Theory]
    [InlineData("", "/* */")]
    [InlineData("one\ntwo", "/* one\ntwo*/")]
    [InlineData("one\r\ntwo", "/* one\r\ntwo*/")]
    [InlineData("one\u2028two\u2029", "/* one\u2028two\u2029*/")]
    [InlineData("/* nested-looking text * /", "/* /* nested-looking text * /*/")]
    public void ValidBlockCommentTextShouldBePreserved(string text, string expectedComment)
    {
        using var document = Json5Document.Parse("1", options: s_parseOptions);
        document.TrailingTrivia.AddLast(" " + text, TriviaKind.BlockComment);

        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        Assert.Equal("1" + expectedComment + "\n", encoded);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(new[] { (TriviaKind.BlockComment, " " + text) }, ReadComments(reparsed.TrailingTrivia));
        Assert.Equal(1, reparsed.RootElement.GetInt32());
    }

    [Fact]
    public void ReplacingContainerWithNumberShouldNotAssert()
    {
        using var document = Json5Document.Parse("[[/*a*/1],2]", options: s_parseOptions);
        // xUnit reports Debug.Assert as a test failure instead of terminating the runner.
        document.RootElement.AsArray()[0].ReplaceWith(0);

        AssertValuesRoundTrip(document);
        Assert.Equal("[0,2]", Json5Document.EncodeToString(document.RootElement));
    }

    [Theory]
    [InlineData("{a:[],b:2}", false)]
    [InlineData("{a:[],b:2}", true)]
    [InlineData("{a:{},b:2}", false)]
    [InlineData("{a:{},b:2}", true)]
    [InlineData("{a:[/*a*/],b:2}", false)]
    [InlineData("{a:[/*a*/],b:2}", true)]
    [InlineData("{a:[ ],b:2}", false)]
    [InlineData("{a:[ ],b:2}", true)]
    public void ReplacingEmptyContainerShouldPreserveFollowingPropertyTrivia(string input, bool replaceWithString)
    {
        using var document = Json5Document.Parse(input, options: s_parseOptions);
        // Looking up "a" in DObject caches key strings and their leading trivia,
        // including the empty container's tokens in the following key's trivia list.
        var target = document.RootElement.AsObject()["a"];
        var replacement = replaceWithString ? (DValue)"x" : 0;
        target.ReplaceWith(replacement);

        AssertValuesRoundTrip(document);
        Assert.Equal(replaceWithString ? "{\"a\":\"x\",\"b\":2}" : "{\"a\":0,\"b\":2}",
            Json5Document.EncodeToString(document.RootElement, s_writeOptions with { PreserveTrivia = false }));
    }

    public static TheoryData<string, int, bool> ContainerScalarReplacementCases()
    {
        var data = new TheoryData<string, int, bool>();
        foreach (var container in new[] { "[]", "[/*inner*/]", "[1]", "[/*inner*/1]" })
        {
            for (var kind = 0; kind < 3; kind++)
            {
                data.Add(container, kind, false);
                data.Add(container, kind, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContainerScalarReplacementCases))]
    public void ReplacingContainerShouldPreserveTraversalAndTokenReuse(
        string container, int replacementKind, bool cacheFollowingTrivia)
    {
        using var document = Json5Document.Parse("[" + container + ",/*keep*/2]", options: s_parseOptions);
        if (cacheFollowingTrivia)
        {
            document.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
        }

        DValue replacement = replacementKind switch
        {
            0 => 0,
            1 => true,
            _ => "x"
        };
        document.RootElement.EnumerateArray().First().AsValue().ReplaceWith(replacement);

        // Keep the parent parsed until traversal has followed any necessary jump.
        AssertValuesRoundTrip(document);
        Assert.Equal(replacementKind switch { 0 => "[0,2]", 1 => "[true,2]", _ => "[\"x\",2]" },
            Json5Document.EncodeToString(document.RootElement, s_writeOptions with { PreserveTrivia = false }));
        var comments = ReadComments(document.RootElement.EnumerateArray().Last().AsValue().LeadingTrivia);
        Assert.Contains((TriviaKind.BlockComment, "keep"), comments);
        if (cacheFollowingTrivia)
        {
            Assert.Contains((TriviaKind.BlockComment, "added"), comments);
        }

        var array = document.RootElement.AsArray();
        for (var i = 0; i < 8; i++)
        {
            array.Insert(1, "reused");
            AssertValuesRoundTrip(document);
            Assert.Equal("reused", (string?)array[1]);
            Assert.Equal(2, (int)array[2]);
            array.RemoveAt(1);
            AssertValuesRoundTrip(document);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ClearingTriviaShouldReuseAllExtendedTokens(bool trailing, bool replaceParsed)
    {
        using var document = Json5Document.Parse("/*leading*/1/*trailing*/", options: s_parseOptions);
        var trivia = trailing ? document.TrailingTrivia : document.RootElement.AsValue().LeadingTrivia;
        var parsedTrivia = trivia.GetEnumerator().Single();
        if (replaceParsed)
        {
            parsedTrivia.ReplaceWith("replaced", TriviaKind.BlockComment);
        }

        trivia.AddFirst("first", TriviaKind.BlockComment);
        trivia.AddLast("last", TriviaKind.BlockComment);
        var extendedIds = trivia.GetEnumerator()
            .Where(item => document.GetToken(item.Id).IsExtended)
            .Select(item => item.Id).OrderBy(id => id).ToArray();
        var extendIds = extendedIds.Select(id => document.GetToken(id).ExtendId).ToArray();
        var reusableIds = extendedIds.Where(id => id != parsedTrivia.Id).ToArray();

        trivia.Clear();
        trivia.Clear();

        Assert.Empty(trivia.GetEnumerator());
        foreach (var extendId in extendIds)
        {
            Assert.Null(document.GetExtends()[(int)extendId]);
        }

        Assert.Equal(default, document.GetToken(parsedTrivia.Id));

        for (var i = 0; i < extendIds.Length; i++)
        {
            trivia.AddLast("reused", TriviaKind.BlockComment);
        }

        var reusedIds = trivia.GetEnumerator().Select(item => item.Id).ToArray();
        Assert.Equal(extendIds.Length, reusedIds.Distinct().Count());
        Assert.All(reusableIds, id => Assert.Contains(id, reusedIds));
        Assert.Equal(extendIds.OrderBy(id => id),
            reusedIds.Select(id => document.GetToken(id).ExtendId).OrderBy(id => id));
        Assert.DoesNotContain(parsedTrivia.Id, reusedIds);
        Assert.Equal(default, document.GetToken(parsedTrivia.Id));

        Assert.All(trivia.GetEnumerator(), item => Assert.Equal("reused", item.GetString()));
        AssertValuesRoundTrip(document);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("property")]
    [InlineData("value")]
    [InlineData("array")]
    [InlineData("trailing")]
    public void RepeatedParsedTriviaReplacementShouldKeepItsIdAndPreventReuse(string location)
    {
        using var document = Json5Document.Parse(
            "/*head*/{/*key*/a:/*value*/[/*item*/1,2]}/*tail*/", options: s_parseOptions);
        var trivia = location switch
        {
            "property" => document.RootElement.EnumerateObject().Single().LeadingTrivia,
            "value" => document.RootElement.GetProperty("a").AsValue().LeadingTrivia,
            "array" => document.RootElement.GetProperty("a").EnumerateArray().First().AsValue().LeadingTrivia,
            "trailing" => document.TrailingTrivia,
            _ => document.RootElement.AsValue().LeadingTrivia
        };
        var item = trivia.GetEnumerator().Single(value => value.Kind == TriviaKind.BlockComment);
        var parsedId = item.Id;

        item.ReplaceWith("first edit");
        var extendedToken = document.GetToken(parsedId);
        Assert.True(extendedToken.IsTriviaReuseProhibited);

        item.ReplaceWith("second edit", TriviaKind.LineComment);
        Assert.Equal(parsedId, trivia.GetEnumerator().Single(value => value.Kind == TriviaKind.LineComment).Id);
        Assert.Equal("second edit", item.GetString());
        Assert.Equal(TriviaKind.LineComment, item.Kind);
        Assert.Equal(extendedToken.ExtendId, document.GetToken(parsedId).ExtendId);
        AssertValuesRoundTrip(document);

        item.ReplaceWith("final edit", TriviaKind.BlockComment);
        Assert.True(document.GetToken(parsedId).IsTriviaReuseProhibited);
        using var snapshot = document.Duplicate();
        trivia.Clear();
        trivia.Clear();
        Assert.Empty(trivia.GetEnumerator());
        Assert.Equal(default, document.GetToken(parsedId));
        Assert.Null(document.GetExtends()[(int)extendedToken.ExtendId]);

        trivia.AddLast("new comment", TriviaKind.BlockComment);
        var added = trivia.GetEnumerator().Single();
        Assert.NotEqual(parsedId, added.Id);
        Assert.Equal(extendedToken.ExtendId, document.GetToken(added.Id).ExtendId);
        Assert.False(document.GetToken(added.Id).IsTriviaReuseProhibited);
        Assert.Equal(default, document.GetToken(parsedId));
        Assert.Contains("new comment", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.DoesNotContain("final edit", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.Contains("final edit", Json5Document.EncodeToString(snapshot.RootElement, s_writeOptions));
        AssertValuesRoundTrip(document);
        AssertValuesRoundTrip(snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedCommentAdditionAndClearingShouldKeepStorageBounded(bool trailing)
    {
        using var document = Json5Document.Parse("{}", options: s_parseOptions);
        var trivia = trailing ? document.TrailingTrivia : document.RootElement.AsObject().LeadingTrivia;
        for (var i = 0; i < 8; i++)
        {
            trivia.AddLast("temporary");
            trivia.Clear();
        }

        var baselineTokens = document.GetTokens().Length;
        var baselineExtends = document.GetExtends().Length;
        for (var i = 0; i < 10000; i++)
        {
            trivia.AddLast("temporary");
            trivia.Clear();
        }

        Assert.Equal("{}", Json5Document.EncodeToString(document.RootElement, s_writeOptions));
        Assert.True(document.GetTokens().Length <= baselineTokens * 2,
            $"Token storage grew from {baselineTokens} to {document.GetTokens().Length}.");
        Assert.True(document.GetExtends().Length <= baselineExtends * 2,
            $"Extend storage grew from {baselineExtends} to {document.GetExtends().Length}.");
    }

    private static void AssertValuesRoundTrip(Json5Document document)
    {
        var plainOptions = s_writeOptions with { PreserveTrivia = false };
        var expected = Json5Document.EncodeToString(document.RootElement, plainOptions);
        var encoded = Json5Document.EncodeToString(document.RootElement, s_writeOptions);
        using var reparsed = Json5Document.Parse(encoded, options: s_parseOptions);
        Assert.Equal(expected, Json5Document.EncodeToString(reparsed.RootElement, plainOptions));
    }

    private static (TriviaKind Kind, string Text)[] ReadComments(DTriviaCollection trivia)
    {
        return trivia.GetEnumerator()
            .Where(item => item.Kind == TriviaKind.Default || item.Kind == TriviaKind.LineComment ||
                           item.Kind == TriviaKind.BlockComment)
            .Select(item => (item.Kind, item.GetString())).ToArray();
    }
}