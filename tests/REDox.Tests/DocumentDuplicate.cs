using System;
using System.Text;
using REDox.Json;

namespace REDox.Tests;

public class DocumentDuplicate
{
    [Fact]
    public void JsonDuplicate()
    {
        var json = """
                   {"A":123,"B":"ABC","C":true,"D":[1,2,3],"E":{"F":1.23}}
                   """;

        var utf8Json = Encoding.UTF8.GetBytes(json);

        using var doc = JsonDocument.Parse(json);

        Assert.Equal("""{"A":123,"B":"ABC","C":true,"D":[1,2,3],"E":{"F":1.23}}""", doc.RootElement.ToString());

        if (doc.RootElement.TryGetProperty("A", out var a))
        {
            a.AsValue().ReplaceWith(999);
        }

        Assert.Equal("""{"A":999,"B":"ABC","C":true,"D":[1,2,3],"E":{"F":1.23}}""", doc.RootElement.ToString());

        if (doc.RootElement.TryGetProperty("D", out var d))
        {
            var arr = d.AsArray();
            arr.Add(4);
            arr.Add(5);
        }

        Assert.Equal("""{"A":999,"B":"ABC","C":true,"D":[1,2,3,4,5],"E":{"F":1.23}}""", doc.RootElement.ToString());

        if (doc.RootElement.TryGetProperty("E", out var e))
        {
            var obj = e.AsObject();
            obj.Add("G", true);
        }

        Assert.Equal("""{"A":999,"B":"ABC","C":true,"D":[1,2,3,4,5],"E":{"F":1.23,"G":true}}""",
            doc.RootElement.ToString());

        var doc2 = doc.Duplicate();

        Assert.Equal("""{"A":999,"B":"ABC","C":true,"D":[1,2,3,4,5],"E":{"F":1.23,"G":true}}""",
            doc2.RootElement.ToString());

        if (doc2.RootElement.TryGetProperty("D", out var d2))
        {
            d2.AsArray().RemoveAt(2);
        }

        Assert.Equal("""{"A":999,"B":"ABC","C":true,"D":[1,2,3,4,5],"E":{"F":1.23,"G":true}}""",
            doc.RootElement.ToString());
        Assert.Equal("""{"A":999,"B":"ABC","C":true,"D":[1,2,4,5],"E":{"F":1.23,"G":true}}""",
            doc2.RootElement.ToString());

        doc.Dispose();

        Assert.False(doc.RootElement.IsValid);
        Assert.False(doc.IsValid);

        var doc3 = doc2.Duplicate();
        var doc4 = doc2.Duplicate();

        TestContext.Current.TestOutputHelper?.WriteLine(doc2.RootElement.ToString()!);
    }

    [Fact]
    public void DuplicateKeepsRentedSourceOwnedByOriginal()
    {
        var json = """{"A":"original-value"}""";

        using var doc = JsonDocument.Parse(json);
        var source = doc.Source;

        using var dup = doc.Duplicate();

        Assert.False(source.Span.Overlaps(dup.Source.Span));
        Assert.True(source.Span.Overlaps(doc.Source.Span));

        for (var i = 0; i < 16; i++)
        {
            using var other = JsonDocument.Parse("""{"Z":"overwritten-xx"}""");
        }

        Assert.Equal(json, Encoding.UTF8.GetString(source.Span));
        Assert.Equal(json, doc.RootElement.ToString());
        Assert.Equal(json, dup.RootElement.ToString());

        doc.Dispose();

        Assert.Equal(json, dup.RootElement.ToString());
    }

    [Fact]
    public void DuplicateDoesNotShareParentTable()
    {
        using var doc = JsonDocument.Parse("""{"A":{"B":[1,2,{"C":3}]},"D":[4,5]}""");

        DValue OriginalC() => doc.RootElement.AsObject()["A"].AsObject()["B"].AsArray()[2].AsObject()["C"];

        Assert.Equal("$.A.B[2].C", OriginalC().GetPath());

        var dup = doc.Duplicate();

        DValue DupC() => dup.RootElement.AsObject()["A"].AsObject()["B"].AsArray()[2].AsObject()["C"];

        dup.RootElement.AsObject()["D"].AsArray().Add(6);
        dup.RootElement.AsObject().Add("E", "x");

        Assert.Equal("$.A.B[2].C", DupC().GetPath());
        Assert.Equal("$.D[2]", dup.RootElement.AsObject()["D"].AsArray()[2].GetPath());
        Assert.Equal("$.A.B[2].C", OriginalC().GetPath());
        Assert.Equal("$.A.B[2]", OriginalC().Parent!.Value.GetPath());

        doc.Dispose();

        for (var i = 0; i < 16; i++)
        {
            using var other = JsonDocument.Parse("""[[[[0]]],[[[1]]],[[[2]]]]""");
            _ = other.RootElement.AsArray()[2].AsArray()[0].GetPath();
        }

        Assert.Equal("$.A.B[2].C", DupC().GetPath());
        Assert.Equal("$.A.B[2]", DupC().Parent!.Value.GetPath());

        dup.Dispose();
    }
}