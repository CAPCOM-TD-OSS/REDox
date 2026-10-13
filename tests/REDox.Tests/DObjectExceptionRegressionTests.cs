// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class DObjectExceptionRegressionTests
{
    [Theory]
    [InlineData("setObject", false)]
    [InlineData("setObject", true)]
    [InlineData("setValue", false)]
    [InlineData("setValue", true)]
    [InlineData("tryAdd", false)]
    [InlineData("tryAdd", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    public void FailedSerializationRemovesNewPropertyAndAllowsRetry(string operation, bool partialWrite)
    {
        var failure = new InvalidOperationException("Serialization failed.");
        var settings = new DoxSerializerSettings
        {
            PropertyNameCaseInsensitive = true,
            Converters = [new FailingValueConverter(failure, partialWrite)]
        };
        var obj = new DObject(settings);
        for (var i = 0; i < 20; i++)
        {
            obj.Add($"keep{i}", i);
        }

        var before = obj.ToJsonString();
        var value = new FailingValue();
        for (var i = 0; i < 3; i++)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                switch (operation)
                {
                    case "setObject": obj.SetProperty("failed", value); break;
                    case "setValue": obj["failed"] = DValue.Create(value); break;
                    case "tryAdd": obj.TryAdd("failed", value); break;
                    case "add": obj.Add("failed", value); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
            });

            Assert.Same(failure, exception);
            Assert.Equal(20, obj.Count);
            Assert.False(obj.ContainsKey("FAILED"));
            Assert.False(obj.TryGetPropertyValue("failed", out _));
            Assert.Equal(before, obj.ToJsonString());
            Assert.Equal(default, obj.Document.GetToken(0));
            Assert.Equal(19, (int)obj["keep19"]);
        }

        obj.SetProperty("FAILED", new[] { 1, 2 });
        Assert.Equal(21, obj.Count);
        Assert.Equal("[1,2]", obj["failed"].ToJsonString());
        Assert.True(obj.Remove("failed"));
        Assert.Equal(before, obj.ToJsonString());
    }

    [Fact]
    public void InvalidValueDoesNotRemoveExistingProperty()
    {
        var obj = new DObject { { "keep", 1 } };

        Assert.Throws<ObjectDisposedException>(() => obj.SetProperty("keep", default(DValue)));
        Assert.Throws<ObjectDisposedException>(() => obj["keep"] = default);
        Assert.False(obj.TryAdd("keep", default(DValue)));
        Assert.Equal("{\"keep\":1}", obj.ToJsonString());
    }

    private sealed class FailingValue;

    private sealed class FailingValueConverter(Exception failure, bool partialWrite) : DataConverter<FailingValue>
    {
        public override FailingValue? Read(in DataReader reader, uint tokenId, FailingValue? existingValue)
        {
            throw new NotSupportedException();
        }

        public override void Write(DataWriter writer, FailingValue? value)
        {
            if (partialWrite)
            {
                writer.WriteStartArray(null);
                writer.WriteInt32(42);
            }

            throw failure;
        }
    }
}