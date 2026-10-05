using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace REDox.Xml.Tests;

public sealed class XmlSerializerRoundTripTests
{
    public enum SampleState
    {
        Pending,
        Ready
    }

    public static TheoryData<string, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool>();
            string[] names =
            [
                "scalars", "nested_objects", "collections", "null_values", "empty_values",
                "attributes_and_text", "unicode", "escaped_characters", "whitespace_text",
                "attribute_whitespace", "text_line_breaks", "namespaces", "polymorphism",
                "mixed_content", "null_root"
            ];

            foreach (var name in names)
            {
                var skipReason = name switch
                {
                    // TODO: Re-enable when ParseXml preserves whitespace-only text nodes.
                    "whitespace_text" => "Known issue: whitespace-only text nodes are discarded.",
                    // TODO: Re-enable when numeric references and attribute whitespace survive parsing and writing.
                    "attribute_whitespace" => "Known issue: attribute CR, LF and tab values do not round-trip.",
                    // TODO: Re-enable when carriage-return character references round-trip in element text.
                    "text_line_breaks" => "Known issue: carriage-return character references in text are corrupted.",
                    _ => null
                };

                foreach (var indented in new[] { false, true })
                foreach (var omitDeclaration in new[] { false, true })
                {
                    cases.Add(new TheoryDataRow<string, bool, bool>(name, indented, omitDeclaration)
                    {
                        Skip = skipReason
                    });
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void XmlSerializerOutputCanBeParsedWrittenAndDeserialized(
        string scenario, bool indented, bool omitDeclaration)
    {
        var (type, value) = CreateSample(scenario);
        var serializer = new XmlSerializer(type);
        var originalXml = Serialize(serializer, value, indented, omitDeclaration);

        using var doc = XmlDocument.Parse(originalXml);
        var restoredXml = XmlDocument.EncodeToString(doc.RootElement);

        AssertEquivalentXml(originalXml, restoredXml);

        // Compare against XmlSerializer's own round trip, including its defaults
        // for omitted members, nullable values and text-node normalization.
        var expected = Deserialize(serializer, originalXml);
        var actual = Deserialize(serializer, restoredXml);
        Assert.Equivalent(expected, actual, true);

        // A second parse/write cycle must also preserve the document's contents.
        using var reparsed = XmlDocument.Parse(restoredXml);
        AssertEquivalentXml(originalXml, XmlDocument.EncodeToString(reparsed.RootElement));
    }

    private static string Serialize(XmlSerializer serializer, object? value, bool indented, bool omitDeclaration)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(false),
                   Indent = indented,
                   OmitXmlDeclaration = omitDeclaration,
                   NewLineChars = "\n",
                   NewLineHandling = NewLineHandling.Entitize
               }))
        {
            serializer.Serialize(writer, value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static object? Deserialize(XmlSerializer serializer, string xml)
    {
        using var reader = new StringReader(xml);
        return serializer.Deserialize(reader);
    }

    private static void AssertEquivalentXml(string expectedXml, string actualXml)
    {
        var expected = XDocument.Parse(expectedXml, LoadOptions.PreserveWhitespace);
        var actual = XDocument.Parse(actualXml, LoadOptions.PreserveWhitespace);

        Assert.Equal(expected.Declaration?.Version, actual.Declaration?.Version);
        Assert.Equal(expected.Declaration?.Encoding, actual.Declaration?.Encoding);
        Assert.Equal(expected.Declaration?.Standalone, actual.Declaration?.Standalone);

        RemoveFormattingWhitespace(expected);
        RemoveFormattingWhitespace(actual);

        // Attribute order, escaping and empty-element notation may differ.
        // Expanded names, attribute values and child-node order must match.
        Assert.True(XNode.DeepEquals(expected.Root, actual.Root),
            $"XML contents changed.{Environment.NewLine}Expected: {expectedXml}{Environment.NewLine}Actual: {actualXml}");
    }

    private static void RemoveFormattingWhitespace(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (!element.Elements().Any() ||
                element.AncestorsAndSelf().Any(e => (string?)e.Attribute(XNamespace.Xml + "space") == "preserve"))
            {
                continue;
            }

            var textNodes = element.Nodes().OfType<XText>().ToArray();
            if (textNodes.All(t => t.Value.All(c => c is ' ' or '\t' or '\r' or '\n')))
            {
                foreach (var text in textNodes)
                {
                    text.Remove();
                }
            }
        }
    }

    private static (Type Type, object? Value) CreateSample(string scenario)
    {
        object value = scenario switch
        {
            "scalars" => new ScalarValues
            {
                Count = 42, LargeNumber = long.MaxValue, Enabled = true, Amount = 1234.56m,
                Ratio = 1.25, Timestamp = new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc),
                Identifier = Guid.Parse("a41ce0c0-e6b4-4e85-a6af-4e50d50b8722"),
                State = SampleState.Ready, OptionalCount = 7, Binary = [0, 1, 127, 128, 255]
            },
            "nested_objects" => new Order
            {
                Id = "order-42", Customer = new Customer { Name = "Leon", City = "Tokyo" },
                Lines = [new OrderLine { Product = "Book", Quantity = 2 }, new OrderLine { Product = "Pen", Quantity = 3 }]
            },
            "collections" => new Collections
            {
                Names = ["Leon", "Claire", "Ada"], Tags = ["first", "second", "third"],
                Customers = [new Customer { Name = "Leon", City = "Tokyo" }, new Customer { Name = "Claire", City = "Osaka" }]
            },
            "null_values" => new OptionalValues { Text = null, Number = null, Customer = null },
            "empty_values" => new Collections { Names = ["", null, "tail"], Tags = [], Customers = [] },
            "attributes_and_text" => new Label { Code = "A-42", Text = "Display name" },
            "unicode" => new Label { Code = "識別子🎮", Text = "日本語、café、Ελλάδα、😀" },
            "escaped_characters" => new Label { Code = "a<&\"'b", Text = "A < B & C > D; \"quotes\", 'apostrophes', literal &amp;" },
            "whitespace_text" => new Label { Code = "space", Text = " \t " },
            "attribute_whitespace" => new Label { Code = "line1\r\nline2\tline3", Text = "Value" },
            "text_line_breaks" => new Label { Code = "lines", Text = "line1\r\nline2\rline3\nend" },
            "namespaces" => new NamespacedDocument
            {
                Id = "record-1", Name = "Leon", Namespaces = CreateNamespaces()
            },
            "polymorphism" => new Zoo { Animals = [new Dog { Name = "Buddy", BarkVolume = 3 }, new Cat { Name = "Ada", Lives = 9 }] },
            "mixed_content" => new Paragraph { Content = ["Before ", new Emphasis { Text = "important & useful" }, " after."] },
            "null_root" => new OptionalValues(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown test scenario.")
        };

        return (value.GetType(), scenario == "null_root" ? null : value);
    }

    private static XmlSerializerNamespaces CreateNamespaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("r", "urn:roundtrip:record");
        namespaces.Add("m", "urn:roundtrip:metadata");
        namespaces.Add("p", "urn:roundtrip:person");
        return namespaces;
    }

    public sealed class ScalarValues
    {
        public int Count { get; set; }
        public long LargeNumber { get; set; }
        public bool Enabled { get; set; }
        public decimal Amount { get; set; }
        public double Ratio { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid Identifier { get; set; }
        public SampleState State { get; set; }
        public int? OptionalCount { get; set; }
        public byte[]? Binary { get; set; }
    }

    public sealed class Customer
    {
        public string? Name { get; set; }
        public string? City { get; set; }
    }

    public sealed class Order
    {
        [XmlAttribute("id")] public string? Id { get; set; }
        public Customer? Customer { get; set; }
        [XmlArrayItem("line")] public OrderLine[]? Lines { get; set; }
    }

    public sealed class OrderLine
    {
        [XmlAttribute("product")] public string? Product { get; set; }
        [XmlAttribute("quantity")] public int Quantity { get; set; }
    }

    public sealed class Collections
    {
        [XmlArrayItem("name", IsNullable = true)]
        public string?[]? Names { get; set; }

        [XmlElement("tag")] public string[]? Tags { get; set; }
        public List<Customer>? Customers { get; set; }
    }

    public sealed class OptionalValues
    {
        [XmlElement(IsNullable = true)] public string? Text { get; set; }
        public int? Number { get; set; }
        [XmlElement(IsNullable = true)] public Customer? Customer { get; set; }
    }

    [XmlRoot("label")]
    public sealed class Label
    {
        [XmlAttribute("code")] public string? Code { get; set; }
        [XmlText] public string? Text { get; set; }
    }

    [XmlRoot("document", Namespace = "urn:roundtrip:record")]
    public sealed class NamespacedDocument
    {
        [XmlAttribute("id", Namespace = "urn:roundtrip:metadata")]
        public string? Id { get; set; }

        [XmlElement("name", Namespace = "urn:roundtrip:person")]
        public string? Name { get; set; }

        [XmlNamespaceDeclarations] public XmlSerializerNamespaces? Namespaces { get; set; }
    }

    [XmlInclude(typeof(Dog))]
    [XmlInclude(typeof(Cat))]
    public abstract class Animal
    {
        public string? Name { get; set; }
    }

    public sealed class Dog : Animal
    {
        public int BarkVolume { get; set; }
    }

    public sealed class Cat : Animal
    {
        public int Lives { get; set; }
    }

    public sealed class Zoo
    {
        [XmlElement("animal")] public Animal[]? Animals { get; set; }
    }

    [XmlRoot("paragraph")]
    public sealed class Paragraph
    {
        [XmlText(typeof(string))]
        [XmlElement("em", typeof(Emphasis))]
        public object[]? Content { get; set; }
    }

    public sealed class Emphasis
    {
        [XmlText] public string? Text { get; set; }
    }
}