using System.Text;
using REDox.Json;

namespace REDox.Toml.Tests;

public sealed class TomlDocumentRegressionTests
{
    private const string CommentInsideArrayTableHeader =
        "[[servers#]]\nname = \"a\"\n[[servers]]\nname = \"b\"\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseCommentInsideArrayTableHeaderShouldThrowDocumentParseException(bool enableValueValidation)
    {
        // Reject the malformed header before returning a corrupt document.
        // Parent can loop indefinitely and ToJsonString can overflow the stack.
        var ex = Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = TomlDocument.Parse(
                CommentInsideArrayTableHeader,
                options: new TomlDocumentOptions { EnableValueValidation = enableValueValidation });
        });

        Assert.Equal(9, ex.BytePosition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryParseCommentInsideArrayTableHeaderShouldReturnFalse(bool enableValueValidation)
    {
        var parsed = TomlDocument.TryParse(
            Encoding.UTF8.GetBytes(CommentInsideArrayTableHeader),
            out var doc,
            options: new TomlDocumentOptions { EnableValueValidation = enableValueValidation });

        using (doc)
        {
            Assert.False(parsed);
            Assert.Null(doc);
        }
    }

    [Theory]
    [InlineData("[servers#]\nname = \"a\"\n")]
    [InlineData("[[#]]\nname = \"a\"\n")]
    [InlineData("[[servers.#]]\nname = \"a\"\n")]
    [InlineData("[[servers # comment\n]]\nname = \"a\"\n")]
    public void ParseCommentInsideTableHeaderWithTriviaShouldThrowAtComment(string toml)
    {
        var ex = Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = TomlDocument.Parse(toml,
                options: new TomlDocumentOptions { PreserveTrivia = true });
        });

        Assert.Equal(toml.IndexOf('#'), ex.BytePosition);
    }

    [Theory]
    [InlineData("[[\"servers#\"]]", false)]
    [InlineData("[[\"servers#\"]]", true)]
    [InlineData("[['servers#']]", false)]
    [InlineData("[['servers#']]", true)]
    public void ParseQuotedHashInArrayTableHeaderShouldPreserveTableName(string header, bool preserveTrivia)
    {
        using var doc = TomlDocument.Parse(header + "\nname = \"a\"\n[[servers]]\nname = \"b\"\n",
            options: new TomlDocumentOptions { PreserveTrivia = preserveTrivia });

        Assert.Equal(
            """{"servers#":[{"name":"a"}],"servers":[{"name":"b"}]}""",
            doc.RootElement.ToJsonString());
        Assert.Null(doc.RootElement.AsValue().Parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseCommentAfterArrayTableHeaderShouldKeepRowsSeparate(bool preserveTrivia)
    {
        using var doc = TomlDocument.Parse(
            "[[servers]] # first server\nname = \"a\"\n[[servers]] # second server\nname = \"b\"\n",
            options: new TomlDocumentOptions { PreserveTrivia = preserveTrivia });

        Assert.Equal(
            """{"servers":[{"name":"a"},{"name":"b"}]}""",
            doc.RootElement.ToJsonString());
        Assert.Null(doc.RootElement.AsValue().Parent);
    }

    [Fact]
    public void ParseCommentAfterTableHeaderShouldKeepTablesSeparate()
    {
        using var doc = TomlDocument.Parse(
            "[servers] # first table\nname = \"a\"\n[other] # second table\nname = \"b\"\n");

        Assert.Equal(
            """{"servers":{"name":"a"},"other":{"name":"b"}}""",
            doc.RootElement.ToJsonString());
        Assert.Null(doc.RootElement.AsValue().Parent);
    }
}