// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class DocumentParentTableRegressionTests
{
    [Fact]
    public void ReplacingContainersWithInlineScalarsShouldNotAllocateParentTable()
    {
        using var source = Json5Document.Parse("[" + string.Join(",", Enumerable.Repeat("[1,2]", 128)) + "]");
        // Warm the replacement path, then measure on a snapshot whose buffers
        // are not pooled, so a parent table allocation cannot be hidden by reuse.
        source.RootElement.EnumerateArray().First().AsValue().ReplaceWith(0);
        using var document = source.Duplicate();
        var targets = document.RootElement.EnumerateArray();

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var target in targets)
        {
            target.AsValue().ReplaceWith(0);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        var values = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(128, values.Length);
        Assert.All(values, value => Assert.Equal(0, value.GetInt32()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ParentTableShouldIncludeEveryScalarAndContainer(bool snapshot, bool materialize)
    {
        var json = "{" + string.Join(",", Enumerable.Range(0, 128)
            .Select(i => $"k{i}:[{i},{{leaf:{i}}}]")) + "}";
        using var source = snapshot ? Json5Document.Parse(json) : null;
        using var document = source?.Duplicate() ?? Json5Document.Parse(json);
        var root = document.RootElement;
        if (materialize)
        {
            root.AsObject();
        }

        Assert.Equal(0U, document.GetParentId(root.Id));
        foreach (var pair in root.EnumerateMap())
        {
            Assert.Equal(root.Id, document.GetParentId(pair.Key.Id));
            Assert.Equal(root.Id, document.GetParentId(pair.Value.Id));
            var items = pair.Value.EnumerateArray().ToArray();
            Assert.All(items, item => Assert.Equal(pair.Value.Id, document.GetParentId(item.Id)));
            var leaf = items[1].EnumerateMap().Single();
            Assert.Equal(items[1].Id, document.GetParentId(leaf.Key.Id));
            Assert.Equal(items[1].Id, document.GetParentId(leaf.Value.Id));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentTableShouldTraverseContainerKeysAndValues(bool materialize)
    {
        var map = new DMap();
        using var source = map.Document;
        for (var i = 0; i < 128; i++)
        {
            var key = new DArray(i);
            var value = new DArray(i + 1);
            using var keyDocument = key.Document;
            using var valueDocument = value.Document;
            map.Add(key, value);
        }

        var root = map.AsElement().Clone();
        using var document = root.Document;
        if (materialize)
        {
            root.AsMap();
        }

        foreach (var pair in root.EnumerateMap())
        {
            Assert.Equal(root.Id, document.GetParentId(pair.Key.Id));
            Assert.Equal(root.Id, document.GetParentId(pair.Value.Id));
            Assert.Equal(pair.Key.Id, document.GetParentId(pair.Key.EnumerateArray().Single().Id));
            Assert.Equal(pair.Value.Id, document.GetParentId(pair.Value.EnumerateArray().Single().Id));
        }
    }

    [Fact]
    public void ParentTableShouldSkipReusedTokensBehindJump()
    {
        using var document = Json5Document.Parse("[[1,2],{leaf:3}]");
        var root = document.RootElement;
        var removed = root.EnumerateArray().First();
        var orphanId = removed.EnumerateArray().Last().Id;
        removed.AsValue().ReplaceWith(0);
        Assert.Equal(orphanId, document.JoinAny(0, 99));

        var remaining = root.EnumerateArray().Last();
        Assert.Equal(root.Id, document.GetParentId(remaining.Id));
        Assert.Equal(remaining.Id, document.GetParentId(remaining.GetProperty("leaf").Id));
        Assert.Equal(0U, document.GetParentId(orphanId));
    }
}