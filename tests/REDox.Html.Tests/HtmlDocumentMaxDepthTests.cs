using System.Linq;

namespace REDox.Html.Tests;

public sealed class HtmlDocumentMaxDepthTests
{
    private static string Nest(int depth)
    {
        return string.Concat(Enumerable.Repeat("<div>", depth)) + "x" +
               string.Concat(Enumerable.Repeat("</div>", depth));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void ParseNestingBeyondDefaultMaxDepthShouldThrowDocumentParseException(int depth)
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = HtmlDocument.Parse(Nest(depth));
        });
    }

    [Fact]
    public void ParseNestingWithinDefaultMaxDepthShouldSucceed()
    {
        var html = Nest(32);
        using var doc = HtmlDocument.Parse(html);
        Assert.Equal(html, HtmlDocument.EncodeToString(doc.RootElement));
    }

    [Fact]
    public void ParseNestingBeyondCustomMaxDepthShouldThrowDocumentParseException()
    {
        var options = new HtmlDocumentOptions { MaxDepth = 8 };

        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = HtmlDocument.Parse(Nest(16), options: options);
        });
    }

    [Fact]
    public void ParseDeepNestingWithLargeMaxDepthShouldSucceed()
    {
        var options = new HtmlDocumentOptions { MaxDepth = 512 };

        var html = Nest(200);
        using var doc = HtmlDocument.Parse(html, options: options);
        Assert.Equal(html.Length, doc.Source.Length);
    }
}