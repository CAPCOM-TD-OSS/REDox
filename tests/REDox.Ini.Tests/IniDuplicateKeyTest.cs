using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace REDox.Ini.Tests;

public class IniDuplicateKeyTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateKeyAfterGrowthShouldReportItsPosition(bool inSection)
    {
        var builder = new StringBuilder(inSection ? "[設定]\n" : string.Empty);
        for (var index = 0; index < 512; index++)
        {
            builder.Append("キー").Append(index).Append("=value\n");
        }

        var offset = Encoding.UTF8.GetByteCount(builder.ToString()) + 2;
        builder.Append("  キー0  =duplicate\n");
        AssertDuplicate(Encoding.UTF8.GetBytes(builder.ToString()), offset);
    }

    [Fact]
    public void DuplicateSectionAfterGrowthShouldBeRejected()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < 512; index++)
        {
            builder.Append("[設定").Append(index).Append("]\nkey=value\n");
        }

        var offset = Encoding.UTF8.GetByteCount(builder.ToString()) + 3;
        builder.Append("[  設定0  ]\n");
        AssertDuplicate(Encoding.UTF8.GetBytes(builder.ToString()), offset);
    }

    [Fact]
    public void GlobalKeyAndSectionShouldShareTheSameNamespaceAfterGrowth()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < 512; index++)
        {
            builder.Append("key").Append(index).Append("=value\n");
        }

        var offset = Encoding.UTF8.GetByteCount(builder.ToString()) + 1;
        builder.Append("[key0]\n");
        AssertDuplicate(Encoding.UTF8.GetBytes(builder.ToString()), offset);
    }

    [Fact]
    public void SectionKeysShouldBeClearedAndPooledStorageShouldBeReusable()
    {
        var builder = new StringBuilder();
        for (var section = 0; section < 100; section++)
        {
            builder.Append("[section").Append(section).Append("]\n");
            for (var key = 0; key < 128; key++)
            {
                builder.Append("key").Append(key).Append('=').Append(section).Append('\n');
            }
        }

        var input = Encoding.UTF8.GetBytes(builder.ToString());
        for (var repeat = 0; repeat < 3; repeat++)
        {
            using var document = IniDocument.Parse(input);
            for (var section = 0; section < 100; section++)
            {
                Assert.Equal(section.ToString(CultureInfo.InvariantCulture),
                    document.RootElement.GetProperty("section" + section).GetProperty("key127").GetString());
            }
        }
    }

    [Fact]
    public void GrowthAfterSectionResetsShouldDiscardEarlierKeys()
    {
        var builder = new StringBuilder("[first]\n");
        for (var key = 0; key < 20; key++)
        {
            builder.Append("old").Append(key).Append("=first\n");
        }

        builder.Append("[empty1]\n[empty2]\n[second]\n");
        for (var key = 0; key < 128; key++)
        {
            builder.Append("new").Append(key).Append("=second\n");
        }

        builder.Append("old0=reused\n[third]\nnew0=third\nold0=third\n");

        var input = Encoding.UTF8.GetBytes(builder.ToString());
        for (var repeat = 0; repeat < 3; repeat++)
        {
            using var document = IniDocument.Parse(input);
            Assert.Equal("first", document.RootElement.GetProperty("first").GetProperty("old0").GetString());
            Assert.Equal("second", document.RootElement.GetProperty("second").GetProperty("new127").GetString());
            Assert.Equal("reused", document.RootElement.GetProperty("second").GetProperty("old0").GetString());
            Assert.Equal("third", document.RootElement.GetProperty("third").GetProperty("new0").GetString());
        }

        var offset = input.Length;
        builder.Append("old0=duplicate\n");
        AssertDuplicate(Encoding.UTF8.GetBytes(builder.ToString()), offset);
    }

    [Fact]
    public void KeysInTheSameBucketShouldRemainDistinctAfterGrowth()
    {
        var keys = new List<string>();
        for (var candidate = 0; keys.Count < 40; candidate++)
        {
            var key = "key" + candidate.ToString(CultureInfo.InvariantCulture);
            var hash = new HashCode();
            hash.AddBytes(Encoding.UTF8.GetBytes(key));
            if ((hash.ToHashCode() & 31) == 0)
            {
                keys.Add(key);
            }
        }

        var builder = new StringBuilder();
        foreach (var key in keys)
        {
            builder.Append(key).Append('=').Append(key).Append('\n');
        }

        using (var document = IniDocument.Parse(builder.ToString()))
        {
            foreach (var key in keys)
            {
                Assert.Equal(key, document.RootElement.GetProperty(key).GetString());
            }
        }

        var offset = Encoding.UTF8.GetByteCount(builder.ToString());
        builder.Append(keys[0]).Append("=duplicate\n");
        AssertDuplicate(Encoding.UTF8.GetBytes(builder.ToString()), offset);
    }

    [Theory]
    [InlineData("FF", "FE")]
    [InlineData("EFBFBD", "FF")]
    [InlineData("FF", "EFBFBD")]
    public void DifferentUtf8BytesShouldRemainDistinctEvenIfTheyDecodeToTheSameString(string firstHex, string secondHex)
    {
        var prefix = new StringBuilder();
        for (var index = 0; index < 128; index++)
        {
            prefix.Append("key").Append(index).Append("=value\n");
        }

        var bytes = new List<byte>(Encoding.UTF8.GetBytes(prefix.ToString()));
        bytes.AddRange(Convert.FromHexString(firstHex));
        bytes.AddRange("=first\n"u8.ToArray());
        bytes.AddRange(Convert.FromHexString(secondHex));
        bytes.AddRange("=second\n"u8.ToArray());
        using var document = IniDocument.Parse(bytes.ToArray());
        var values = new List<string?>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name == "\ufffd")
            {
                values.Add(property.Value.GetString());
            }
        }

        Assert.Equal(new[] { "first", "second" }, values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalInvalidUtf8BytesShouldBeRejectedAsDuplicates(bool inSection)
    {
        var prefix = Encoding.UTF8.GetBytes(inSection ? "[section]\n" : string.Empty);
        byte[] input = [.. prefix, 0xff, .. "=first\n"u8, 0xff, .. "=second\n"u8];
        AssertDuplicate(input, prefix.Length + 8);
    }

    /*
    [Fact]
    public void InvalidUtf8KeysShouldBeReusableInDifferentSections()
    {
        byte[] input = [.. "[first]\n"u8, 0xff, .. "a=one\n"u8, 0xfe, .. "b=two\n[second]\n"u8,
            0xff, .. "a=three\n"u8];
        using var document = IniDocument.Parse(input);
        Assert.Equal("one", document.RootElement.GetProperty("first").GetProperty("\ufffda").GetString());
        Assert.Equal("two", document.RootElement.GetProperty("first").GetProperty("\ufffdb").GetString());
        Assert.Equal("three", document.RootElement.GetProperty("second").GetProperty("\ufffda").GetString());
    }
    */

    [Fact]
    public void NamesShouldRemainCaseSensitiveAndShouldNotBeNormalized()
    {
        using var document = IniDocument.Parse("Key=one\nkey=two\né=three\ne\u0301=four\n[Section]\n[section]\n");
        Assert.Equal("one", document.RootElement.GetProperty("Key").GetString());
        Assert.Equal("two", document.RootElement.GetProperty("key").GetString());
        Assert.Equal("three", document.RootElement.GetProperty("é").GetString());
        Assert.Equal("four", document.RootElement.GetProperty("e\u0301").GetString());
    }

    [Theory]
    [InlineData("Key=one\nkey=two\n")]
    [InlineData("[s]\nKey=one\nKEY=two\n")]
    [InlineData("[Section]\n[SECTION]\n")]
    [InlineData("name=one\n[NAME]\n")]
    public void CaseInsensitiveSettingsShouldRejectCaseVariantDuplicates(string ini)
    {
        var settings = new DoxSerializerSettings { PropertyNameCaseInsensitive = true };

        var exception = Assert.ThrowsAny<DocumentParseException>(() => IniDocument.Parse(ini, settings));
        Assert.Contains("DuplicateKey", exception.Message);

        using var sensitive = IniDocument.Parse(ini);
        Assert.NotNull(sensitive);

        using var allowed = IniDocument.Parse(ini, settings, new IniDocumentOptions { AllowDuplicateKeys = true });
        Assert.NotNull(allowed);
    }

    [Fact]
    public void CaseInsensitiveSettingsShouldNotFoldNonAsciiNames()
    {
        var settings = new DoxSerializerSettings { PropertyNameCaseInsensitive = true };
        using var document = IniDocument.Parse("\u00e9=one\n\u00c9=two\n", settings);
        Assert.NotNull(document);
    }

    private static void AssertDuplicate(byte[] input, int expectedOffset)
    {
        var exception = Assert.ThrowsAny<DocumentParseException>(() => IniDocument.Parse(input));
        Assert.Contains("DuplicateKey", exception.Message);
        Assert.Equal(expectedOffset, exception.BytePosition);

        Assert.False(IniDocument.TryParse(input.AsSpan(), out var rejected));
        Assert.Null(rejected);

        Assert.True(IniDocument.TryParse(input.AsSpan(), out var allowed,
            options: new IniDocumentOptions { AllowDuplicateKeys = true }));
        using (allowed)
        {
            Assert.NotNull(allowed);
        }
    }
}