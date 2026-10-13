// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Runtime.Serialization;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class SerializerSettingsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttributeNamingPoliciesAreInstantiated(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = new NamingModel { Id = 7, Other = 9 };

        var json = JsonSerializer.Serialize(source, settings);
        var result = JsonSerializer.Deserialize<NamingModel>(json, settings);

        Assert.Contains("\"type_Id\":7", json);
        Assert.Contains("\"property_Other\":9", json);
        Assert.NotNull(result);
        Assert.Equal(source.Id, result.Id);
        Assert.Equal(source.Other, result.Other);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttributeConvertersUseDefaultAndParameterizedConstructors(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = new ConverterModel { Plain = 7, Offset = 9, Items = [1, 2] };

        var json = JsonSerializer.Serialize(source, settings);
        var result = JsonSerializer.Deserialize<ConverterModel>(json, settings);

        Assert.Contains("\"Plain\":8", json);
        Assert.Contains("\"Offset\":19", json);
        Assert.Contains("\"Items\":[21,22]", json);
        Assert.NotNull(result);
        Assert.Equal(source.Plain, result.Plain);
        Assert.Equal(source.Offset, result.Offset);
        Assert.Equal(source.Items, result.Items);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContractsIncludeBaseMembersPrivateConstructorsAndCallbacks(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = ContractModel.Create(7, 9);

        var result = DoxSerializer.DeepCopy(source, settings);

        Assert.Equal(7, result.Value);
        Assert.Equal(9, result.Secret);
        Assert.True(result.Deserialized);
        Assert.Contains("\"secret\":9", JsonSerializer.Serialize(source, settings));
        Assert.DoesNotContain("\"Value\"", JsonSerializer.Serialize(ContractModel.Create(0, 9), settings));
    }

    [DataObject(NamingPolicyType = typeof(TypePrefixPolicy))]
    public sealed class NamingModel
    {
        public int Id { get; set; }

        [DataProperty(NamingPolicyType = typeof(PropertyPrefixPolicy))]
        public int Other { get; set; }
    }

    public sealed class TypePrefixPolicy : NamingPolicy
    {
        public override string ConvertName(string name)
        {
            return "type_" + name;
        }
    }

    public sealed class PropertyPrefixPolicy : NamingPolicy
    {
        public override string ConvertName(string name)
        {
            return "property_" + name;
        }
    }

    public sealed class ConverterModel
    {
        [DataConverter(typeof(OffsetConverter))]
        public int Plain { get; set; }

        [DataConverter(typeof(OffsetConverter), 10)]
        public int Offset { get; set; }

        [DataProperty(ItemConverterType = typeof(OffsetConverter), ItemConverterParameters = new object[] { 20 })]
        public List<int> Items { get; set; } = [];
    }

    public sealed class OffsetConverter : DataConverter<int>
    {
        private readonly int _offset;

        public OffsetConverter() : this(1)
        {
        }

        public OffsetConverter(int offset)
        {
            _offset = offset;
        }

        public override int Read(in DataReader reader, uint tokenId, int existingValue)
        {
            return reader.ReadInt32(tokenId) - _offset;
        }

        public override void Write(DataWriter writer, int value)
        {
            writer.WriteInt32(value + _offset);
        }
    }

    [DataContract]
    public class ContractBase
    {
        [DataMember(Name = "secret")] private int _secret;

        public int Secret => _secret;

        public bool Deserialized { get; private set; }

        protected void SetSecret(int secret)
        {
            _secret = secret;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Deserialized = true;
        }
    }

    [DataContract]
    public sealed class ContractModel : ContractBase
    {
        [DataConstructor]
        private ContractModel(int value)
        {
            Value = value;
        }

        [DataMember] public int Value { get; }

        public static ContractModel Create(int value, int secret)
        {
            var instance = new ContractModel(value);
            instance.SetSecret(secret);
            return instance;
        }

        private bool ShouldSerializeValue()
        {
            return Value != 0;
        }
    }
}