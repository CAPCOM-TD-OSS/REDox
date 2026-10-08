using System.IO;
using System.Text;
using REDox.Json;

namespace REDox.Tests;

public sealed class PooledBufferLifetimeTest
{
    private static Stream Body(string s)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(s));
    }

    [Fact]
    public void DeserializedObjectMemberMustNotObserveReturnedPoolBuffer()
    {
        const string alice = """{"User":"alice","Meta":{"card":"4111-1111-1111-1111"}}""";
        const string bob = """{"User":"bob  ","Meta":{"card":"5500-0000-0000-0004"}}""";

        var req = JsonSerializer.Deserialize<Request>(Body(alice))!;
        var before = req.Meta?.ToString();
        Assert.Contains("4111-1111-1111-1111", before);

        using (var doc = JsonDocument.Parse(Body(bob)))
        {
        }

        var after = req.Meta?.ToString();
        Assert.Equal(before, after);
        Assert.DoesNotContain("5500-0000-0000-0004", after);
    }

    public sealed class Request
    {
        public string? User { get; set; }
        public object? Meta { get; set; }
    }
}