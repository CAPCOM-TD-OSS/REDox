using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using REDox.Json;

namespace REDox.Xml.Tests;

public sealed class XmlDocumentRegressionTests
{
    private const string ProbeInputVariable = "REDOX_XML_REGRESSION_INPUT";
    private const string ProbeExpectErrorVariable = "REDOX_XML_REGRESSION_EXPECT_ERROR";

    [Theory]
    [InlineData("<?xml version=\"1.0\"><root/>")]
    [InlineData("<?xml version=1.0?><root/>")]
    [InlineData("<?xml")]
    [InlineData("<?xml version=\"1.0\"")]
    [InlineData("<?xml version=\"1.0")]
    [InlineData("<?pi x>")]
    public Task ParseMalformedDeclarationOrInstructionShouldThrowDocumentParseException(string xml)
    {
        return RunParseInChildProcess(xml, true);
    }

    [Fact]
    public Task ParseProcessingInstructionInsideElementShouldComplete()
    {
        return RunParseInChildProcess("<root><?pi x?></root>", false);
    }

    [Theory]
    [InlineData("<r a==\"1\"/>")]
    [InlineData("<r a=/>")]
    [InlineData("<r a=>t</r>")]
    [InlineData("<r a=1/>")]
    [InlineData("<r a=")]
    [InlineData("<r a=\"1")]
    public void ParseMalformedAttributeShouldThrowDocumentParseException(string xml)
    {
        // Reject the input before a corrupt document can hang Parent traversal
        // or exceed the JSON writer's maximum depth.
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = XmlDocument.Parse(xml);
        });
    }

    [Fact]
    public Task ParseValidXmlDeclarationProducesUsableDocument()
    {
        return RunParseInChildProcess("<?xml version=\"1.0\"?><root/>", false);
    }

    [Theory]
    [InlineData("<root><?pi x?></root>")]
    [InlineData("<?pi?><root/>")]
    [InlineData("<?pi x=1.0 > text?><root/>")]
    [InlineData("<?xml version=\"1.0\"?><root><?pi x?><child/><?pi y?></root><?end done?>")]
    [InlineData("<root>before<?pi x?>after</root>")]
    [InlineData("<root><?pi x='unterminated?></root>")]
    public Task ParseProcessingInstructionsPreservesDataAndOrder(string xml)
    {
        return RunParseInChildProcess(xml, false);
    }

    [Theory]
    [InlineData("<r a=\"1\"/>", """[{"a":"1","r":[]}]""")]
    [InlineData("<r a = '' b=\"&amp;\"/>", """[{"a":"","b":"&","r":[]}]""")]
    public void ParseQuotedAttributesProducesUsableDocument(string xml, string expectedJson)
    {
        using var doc = XmlDocument.Parse(xml);

        Assert.Equal(expectedJson, doc.RootElement.ToJsonString());
        Assert.Null(doc.RootElement.AsValue().Parent);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(1000)]
    public void ParseNestingBeyondMaxDepthShouldThrowDocumentParseException(int depth)
    {
        var xml = string.Concat(Enumerable.Repeat("<a>", depth));

        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = XmlDocument.Parse(xml);
        });
    }

    [Theory]
    [InlineData(65)]
    [InlineData(1000)]
    public void ParseBalancedNestingBeyondMaxDepthShouldThrowDocumentParseException(int depth)
    {
        var xml = string.Concat(Enumerable.Repeat("<a>", depth)) + string.Concat(Enumerable.Repeat("</a>", depth));

        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = XmlDocument.Parse(xml);
        });
    }

    [Fact]
    public void ParseNestingWithinMaxDepthShouldSucceed()
    {
        var xml = string.Concat(Enumerable.Repeat("<a>", 32)) + "x" + string.Concat(Enumerable.Repeat("</a>", 32));

        using var doc = XmlDocument.Parse(xml);
        Assert.Equal(xml, XmlDocument.EncodeToString(doc.RootElement));
    }

    // This worker is selected explicitly by RunParseInChildProcess.
    [Fact(Explicit = true)]
    public void IsolatedParseProbe()
    {
        var xml = Environment.GetEnvironmentVariable(ProbeInputVariable);
        Assert.NotNull(xml);

        if (Environment.GetEnvironmentVariable(ProbeExpectErrorVariable) == "true")
        {
            Assert.ThrowsAny<DocumentParseException>(() =>
            {
                using var doc = XmlDocument.Parse(xml);
            });
        }
        else
        {
            AssertUsableDocument(xml);
        }
    }

    private static void AssertUsableDocument(string xml)
    {
        using var doc = XmlDocument.Parse(xml);
        Assert.Equal(xml, Encoding.UTF8.GetString(doc.Source.Span));
        Assert.Equal(xml, XmlDocument.EncodeToString(doc.RootElement));
        Assert.Equal(DTokenType.Array, doc.RootElement.Token.Type);
        Assert.NotEmpty(doc.RootElement.EnumerateArray());

        // Serialize before querying Parent: the writer has a depth limit,
        // whereas Parent traversal can run indefinitely on cyclic tokens.
        _ = doc.RootElement.ToJsonString();
        Assert.Null(doc.RootElement.AsValue().Parent);
    }

    private static async Task RunParseInChildProcess(string xml, bool expectParseError)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(typeof(XmlDocumentRegressionTests).Assembly.Location);
        startInfo.ArgumentList.Add("-noColor");
        startInfo.ArgumentList.Add("-noLogo");
        startInfo.ArgumentList.Add("-explicit");
        startInfo.ArgumentList.Add("only");
        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add($"{typeof(XmlDocumentRegressionTests).FullName}.{nameof(IsolatedParseProbe)}");
        startInfo.Environment[ProbeInputVariable] = xml;
        startInfo.Environment[ProbeExpectErrorVariable] = expectParseError ? "true" : "false";

        // Runtime heap limits are hexadecimal: 0x10000000 is 256 MiB.
        // An unbounded parser must not exhaust the main test runner's memory.
        startInfo.Environment["DOTNET_GCHeapHardLimit"] = "10000000";

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                Assert.Fail($"XmlDocument.Parse did not complete within five seconds for: {xml}");
            }

            var output = await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode == 0,
                $"Isolated XML parse failed for: {xml}{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}