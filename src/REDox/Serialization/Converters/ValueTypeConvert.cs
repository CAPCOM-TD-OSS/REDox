// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class ValueTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        return type.IsValueType;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "The application must preserve reflection metadata for value types and their property types used by the non-generic converter.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "NullableTypeConverter<> is instantiated only with the non-nullable underlying value type; its public constructor is explicitly preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2071",
        Justification = "The generic converters are only instantiated for non-nullable value types, which do not require an explicit parameterless constructor to satisfy the struct constraint.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(NullableTypeConverter<>))]
    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || !settings.AllowDynamicGenericConverters)
        {
            return new DefaultConverter(type, settings);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return ConverterHelper.CreateConverter(
                typeof(NullableTypeConverter<>).MakeGenericType(type.GetGenericArguments()[0]), settings);
        }

        return ConverterHelper.CreateConverter(typeof(StructConverter<>).MakeGenericType(type), settings);
    }

    private class NullableTypeConverter<T> : DataConverter<T?> where T : struct
    {
        private readonly DataConverter<T> _converter;

        public NullableTypeConverter(SerializerSettings settings)
        {
            _converter = (DataConverter<T>)settings.GetConverter(typeof(T));
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return null;
            }

            return _converter.Read(reader, tokenId, existingValue.HasValue ? existingValue.Value : default);
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                _converter.Write(writer, value.Value);
            }
        }
    }

    private class StructConverter<T> : DataConverter<T> where T : struct
    {
        private readonly ConstructorHandling _constructorHandling;
        private readonly DataContract _contract;
        private readonly Property[] _lookupTable = Array.Empty<Property>();
        private readonly ObjectCreationHandling _objectCreationHandling;

        private readonly ulong[]? _requiredMask;
        private ConverterHelper.ConstructDelegate<T>? _constructor;
        private DataConverter[] _constructorConverters = Array.Empty<DataConverter>();
        private ConverterHelper.StreamingEventRefDelegate<T>? _onDeserialized;
        private ConverterHelper.StreamingEventRefDelegate<T>? _onDeserializing;
        private ConverterHelper.StreamingEventRefDelegate<T>? _onSerialized;
        private ConverterHelper.StreamingEventRefDelegate<T>? _onSerializing;

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        public StructConverter(SerializerSettings settings)
        {
            var typeInfo = typeof(T);
            var props = new List<Property>();
            var ignoreCase = settings.PropertyNameCaseInsensitive;

            var contract = settings.GetContract(typeInfo);

            var mask = new ulong[(contract.Properties.Count + 63) / 64];
            var hasMask = false;

            foreach (var member in contract.Properties)
            {
                var converter = member.Converter;

                if (converter == null)
                {
                    converter = settings.GetConverter(member.PropertyType, member);
                }
                else
                {
                    converter = converter.ResolvePropertyConverter(member, settings);
                }

                var sp = new Property(member, converter, member.Name);

                var existing = false;

                if (settings.ObjectCreationHandling != ObjectCreationHandling.Replace)
                {
                    if (member.Readable)
                    {
                        if (member.PropertyType.IsArray)
                        {
                            existing = (member.ObjectCreationHandling & ObjectCreationHandling.ReuseArray) != 0;
                        }
                        else
                        {
                            if (member.PropertyType.IsValueType)
                            {
                                existing = (member.ObjectCreationHandling & ObjectCreationHandling.ReuseStruct) != 0;
                            }
                            else
                            {
                                existing = (member.ObjectCreationHandling & ObjectCreationHandling.ReuseObject) != 0;
                            }
                        }

                        if (member.Writable &&
                            (settings.ObjectCreationHandling & ObjectCreationHandling.WhenReadOnly) != 0)
                        {
                            existing = false;
                        }
                    }
                }

                if (member.IsReadOnly && member.Info is FieldInfo fieldInfo)
                {
                    sp.InitOnlyField = fieldInfo.IsInitOnly;
                }
                else
                {
                    if (existing)
                    {
                        sp.Reader =
                            ConverterHelper.CreateReadDelegate<ConverterHelper.ReadRefDelegate<T>, T>(member, converter,
                                true);
                    }
                    else
                    {
                        if (member.Writable)
                        {
                            sp.Reader =
                                ConverterHelper
                                    .CreateReadDelegate<ConverterHelper.ReadRefDelegate<T>,
                                        T>(member, converter, false);
                        }
                    }
                }

                sp.Id = props.Count;

                if (member.Readable)
                {
                    sp.Writer =
                        ConverterHelper
                            .CreateWriteDelegate<ConverterHelper.WriteRefDelegate<T>, T>(member, converter, sp.Symbol);
                }

                if ((member.DefaultValueHandling & DefaultValueHandling.Populate) != 0 || member.IsRequired)
                {
                    mask[sp.Id / 64] |= 1UL << (sp.Id & 63);
                    hasMask = true;
                }

                props.Add(sp);
            }

            Properties = props.ToArray();

            _lookupTable = new Property[Properties.Length];
            Array.Copy(Properties, _lookupTable, Properties.Length);
            Array.Sort(_lookupTable, (a, b) => Utf8Helper.Compare(a.Name, b.Name, ignoreCase));

            InitCallBackMethod(contract);
            InitConstructor(settings, contract);

            if (hasMask)
            {
                _requiredMask = mask;
            }

            _contract = contract;
            _objectCreationHandling = settings.ObjectCreationHandling;
            _constructorHandling = settings.ConstructorHandling;
        }

        private Property[] Properties { get; } = Array.Empty<Property>();

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            var name = value.ToString();

            if (name == null)
            {
                throw new InvalidCastException();
            }

            if (writer.Settings.DictionaryKeyPolicy != null)
            {
                name = writer.Settings.DictionaryKeyPolicy.ConvertName(name);
            }

            writer.WriteString(name);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var ignoreCase = reader.Settings.PropertyNameCaseInsensitive;

            var requiredMask = _requiredMask != null ? stackalloc ulong[_requiredMask.Length] : default;

            if (_requiredMask != null)
            {
                _requiredMask.AsSpan().CopyTo(requiredMask);
            }

            T instance = default;

            if (_constructor != null)
            {
                var tokens = new uint[_constructorConverters.Length];
                using var list = new Helper.LocalList<(int propId, uint valueId)>(stackalloc (int, uint)[64]);

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    var prop = FindProperty(name, ignoreCase);

                    if (prop == null)
                    {
                        if (reader.Settings.UnmappedMemberHandling == UnmappedMemberHandling.Disallow)
                        {
                            reader.HandleException(SerializationError.UnmappedMember, null, instance, _contract,
                                Utf8Helper.GetUtf16String(name), kv.Value);
                        }
                    }
                    else
                    {
                        if (prop.ConstructParamIndex >= 0)
                        {
                            tokens[prop.ConstructParamIndex] = kv.Value;

                            if (_requiredMask != null)
                            {
                                requiredMask[prop.Id / 64] &= ~(1UL << (prop.Id & 63));
                            }
                        }
                        else
                        {
                            list.Add((prop.Id, kv.Value));
                        }
                    }
                }

                if ((_objectCreationHandling & ObjectCreationHandling.ReuseStruct) != 0)
                {
                    instance = existingValue;
                }
                else
                {
                    instance = _constructor(reader, _constructorConverters, tokens);
                }

                if (_onDeserializing != null)
                {
                    _onDeserializing(ref instance, reader.Settings.Context);
                }

                var targets = list.AsSpan();

                for (var i = 0; i < targets.Length; i++)
                {
                    var target = targets[i];
                    var prop = Properties[target.propId];

                    if (_requiredMask != null)
                    {
                        requiredMask[prop.Id / 64] &= ~(1UL << (prop.Id & 63));
                    }

                    try
                    {
                        if (prop.Reader != null)
                        {
                            prop.Reader(reader, prop.Converter, ref instance, target.valueId);
                        }
                        else
                        {
                            if (prop.InitOnlyField)
                            {
                                ReadInitOnlyField(reader, prop.Info, prop.Converter, target.valueId,
                                    ref instance);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        reader.HandleException(SerializationError.FailedToRead, e, instance, _contract, prop.Info,
                            target.valueId);
                    }
                }
            }
            else
            {
                if ((_objectCreationHandling & ObjectCreationHandling.ReuseStruct) != 0)
                {
                    instance = existingValue;
                }
                else
                {
                    if ((_constructorHandling & ConstructorHandling.IgnoreStructDefaultConstructor) == 0)
                    {
                        instance = new T();
                    }
                }

                if (_onDeserializing != null)
                {
                    _onDeserializing(ref instance, reader.Settings.Context);
                }

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    var prop = FindProperty(name, ignoreCase);

                    if (prop == null)
                    {
                        if (reader.Settings.UnmappedMemberHandling == UnmappedMemberHandling.Disallow)
                        {
                            reader.HandleException(SerializationError.UnmappedMember, null, instance, _contract,
                                Utf8Helper.GetUtf16String(name), kv.Value);
                        }
                    }
                    else
                    {
                        if (_requiredMask != null)
                        {
                            requiredMask[prop.Id / 64] &= ~(1UL << (prop.Id & 63));
                        }

                        try
                        {
                            if (prop.Reader != null)
                            {
                                prop.Reader(reader, prop.Converter, ref instance, kv.Value);
                            }
                            else
                            {
                                if (prop.InitOnlyField)
                                {
                                    ReadInitOnlyField(reader, prop.Info, prop.Converter, kv.Value, ref instance);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            reader.HandleException(SerializationError.FailedToRead, e, instance, _contract, prop.Info,
                                kv.Value);
                        }
                    }
                }
            }

            if (_requiredMask != null)
            {
                for (var i = 0; i < requiredMask.Length; i++)
                {
                    var mask = requiredMask[i];

                    while (mask != 0)
                    {
                        var index = BitOperations.TrailingZeroCount(mask);
                        mask &= ~(1UL << index);

                        var prop = Properties[index];
                        if (prop.Reader != null)
                        {
                            prop.Reader(reader, prop.Converter, ref instance, 0);
                        }
                    }
                }
            }

            if (_onDeserialized != null)
            {
                _onDeserialized(ref instance, reader.Settings.Context);
            }

            return instance;
        }

        private void ReadInitOnlyField(in DataReader reader, DataProperty info, DataConverter converter, uint valueId,
            ref T instance)
        {
            if (info.IsStatic)
            {
                info.SetValue(null, converter.ReadObject(in reader, info.PropertyType, valueId, info.GetValue(null)));
            }
            else
            {
                var value = converter.ReadObject(in reader, info.PropertyType, valueId,
                    info.GetValue(__makeref(instance)));
                if (value != null)
                {
                    info.SetValue(__makeref(instance), value);
                }
            }
        }

        public override void Write(DataWriter writer, T value)
        {
            WriteData(writer, value, false);
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                WriteData(writer, (T)value, objectType != typeof(T));
            }
        }

        private void WriteData(DataWriter writer, T value, bool typeNeeded)
        {
            var isReference = (writer.Settings.PreserveReferencesHandling & PreserveReferencesHandling.Structs) != 0;

            if (isReference && writer.TryWriteReference(value))
            {
                return;
            }

            var typed = _contract.AlwaysWriteTypeDiscriminator || (typeNeeded && _contract.IsPolymorphic);

            if (!isReference &&
                (writer.Settings.PreserveReferencesHandling & PreserveReferencesHandling.Objects) != 0 &&
                typeNeeded)
            {
                isReference = true;
                if (writer.TryWriteReference(value))
                {
                    return;
                }
            }

            if (_onSerializing != null)
            {
                _onSerializing(ref value, writer.Settings.Context);
            }

            var count = Properties.Length;

            if (typed)
            {
                if (isReference)
                {
                    writer.WriteStartMap(count + 2);
                    writer.WriteReferenceId();
                    writer.WriteTypeDiscriminator(_contract);
                }
                else
                {
                    writer.WriteStartMap(count + 1);
                    writer.WriteTypeDiscriminator(_contract);
                }
            }
            else
            {
                if (isReference)
                {
                    writer.WriteStartMap(count + 1);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(count);
                }
            }

            var preserved =
                (writer.Settings.PreserveReferencesHandling &
                 (PreserveReferencesHandling.Structs | PreserveReferencesHandling.IgnoreReadOnly)) ==
                (PreserveReferencesHandling.Structs | PreserveReferencesHandling.IgnoreReadOnly);

            foreach (var prop in Properties)
            {
                try
                {
                    if (prop.Writer != null)
                    {
                        prop.Writer(writer, prop.Converter, ref value);
                    }
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, _contract, prop.Info);
                }
            }

            writer.WriteEndMap();

            if (_onSerialized != null)
            {
                _onSerialized(ref value, writer.Settings.Context);
            }
        }


        private Property? FindProperty(ReadOnlySpan<byte> name, bool ignoreCase)
        {
            var lut = _lookupTable;
            var start = 0;
            var end = lut.Length - 1;

            while (start <= end)
            {
                var pos = (start + end) >> 1;
                var sign = Utf8Helper.Compare(lut[pos].Name, name, ignoreCase);

                if (sign == 0)
                {
                    return lut[pos];
                }

                if (sign < 0)
                {
                    start = pos + 1;
                }
                else
                {
                    end = pos - 1;
                }
            }

            return null;
        }

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private void InitCallBackMethod(DataContract contract)
        {
            foreach (var method in contract.OnSerializingCallbacks)
            {
                _onSerializing += ConverterHelper.CreateStreamingEventRefDelegate<T>(method);
            }

            foreach (var method in contract.OnSerializedCallbacks)
            {
                _onSerialized += ConverterHelper.CreateStreamingEventRefDelegate<T>(method);
            }

            foreach (var method in contract.OnDeserializingCallbacks)
            {
                _onDeserializing += ConverterHelper.CreateStreamingEventRefDelegate<T>(method);
            }

            foreach (var method in contract.OnDeserializedCallbacks)
            {
                _onDeserialized += ConverterHelper.CreateStreamingEventRefDelegate<T>(method);
            }
        }

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private void InitConstructor(SerializerSettings settings, DataContract contract)
        {
            var ctor = contract.Constructor;

            if (ctor == null)
            {
                return;
            }

            var parameters = ctor.GetParameters();
            var ignoreCase = true;

            if (parameters.Length != 0)
            {
                _constructor = ConverterHelper.CreateConstructFunction<T>(ctor);
                _constructorConverters = new DataConverter[parameters.Length];

                for (var i = 0; i < parameters.Length; i++)
                {
                    Property? prop = null;
                    Type? propType = null;
                    for (var j = 0; j < contract.Properties.Count; j++)
                    {
                        if (string.Compare(contract.Properties[j].Info.Name, parameters[i].Name, ignoreCase) == 0 &&
                            parameters[i].ParameterType.IsAssignableFrom(contract.Properties[j].PropertyType))
                        {
                            prop = Properties[j];
                            propType = contract.Properties[j].PropertyType;
                            break;
                        }
                    }

                    if (prop == null || prop.ConstructParamIndex >= 0)
                    {
                        _constructorConverters[i] = settings.GetConverter(parameters[i].ParameterType);
                        continue;
                    }

                    prop.ConstructParamIndex = i;
                    _constructorConverters[i] = propType == parameters[i].ParameterType
                        ? prop.Converter
                        : settings.GetConverter(parameters[i].ParameterType);
                }
            }
        }

        private class Property
        {
            public readonly DataConverter Converter;
            public readonly DataProperty Info;

            public readonly byte[] Name;
            public readonly Utf8Symbol Symbol;
            public int ConstructParamIndex = -1;
            public int Id;
            public bool InitOnlyField;
            public ConverterHelper.ReadRefDelegate<T>? Reader;
            public ConverterHelper.WriteRefDelegate<T>? Writer;

            public Property(DataProperty info, DataConverter converter, string name)
            {
                Info = info;
                Converter = converter;
                Symbol = new Utf8Symbol(name);
                Name = Symbol.Utf8Bytes;
            }
        }
    }

    private class DefaultConverter : DataConverter
    {
        private readonly DataContract _contract;
        private readonly DataConverter _converter;
        private readonly Type? _nullableType;

        public DefaultConverter(
            [DynamicallyAccessedMembers(ClassTypeConverter.ReflectionTypeMembers)]
            Type type,
            SerializerSettings settings)
        {
            _contract = settings.GetContract(type);
            _nullableType = Nullable.GetUnderlyingType(type);

            _converter = _nullableType != null
                ? settings.GetConverter(_nullableType)
                : new ClassTypeConverter.DefaultConverter(type, settings);
        }

        protected internal override Type TargetType => _contract.Type;

        public override void WriteObjectAsPropertyName(DataWriter writer, object value)
        {
            _converter.WriteObjectAsPropertyName(writer, value);
        }

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            return _converter.ReadObjectAsPropertyName(reader, tokenId);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            if (_nullableType == null)
            {
                return _converter.ReadObject(in reader, objectType, tokenId, existingValue);
            }

            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return null;
            }

            return _converter.ReadObject(in reader, _nullableType, tokenId, existingValue);
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (_nullableType == null)
            {
                _converter.WriteObject(writer, objectType, value);
                return;
            }

            _converter.WriteObject(writer, _nullableType, value);
        }
    }
}