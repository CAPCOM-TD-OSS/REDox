// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class ClassTypeConverter : DataConverterFactory
{
    // Applications must preserve contract members, callbacks, and constructors
    // for runtime-supplied types, including property types and derived types.
    internal const DynamicallyAccessedMemberTypes ReflectionTypeMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicMethods;

    private const string ContractMetadata =
        "The application must preserve reflection metadata for contract types and their property and derived types.";

    public override bool CanConvert(Type type)
    {
        if (type.ContainsGenericParameters)
        {
            return false;
        }

        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = ContractMetadata)]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = ContractMetadata)]
    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || !settings.AllowDynamicGenericConverters)
        {
            return new DefaultConverter(type, settings);
        }

        return ConverterHelper.CreateConverter(typeof(ClassConverter<>).MakeGenericType(type), settings);
    }

    private class ClassConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        T> : DataConverter<T>
        where T : class
    {
        private readonly bool _anonymousType;
        private readonly DataContract _contract;
        private readonly bool _readFastPass;
        private readonly bool _writeFastPass;
        private ConverterHelper.ConstructDelegate<T>? _constructor;
        private DataConverter[] _constructorConverters = Array.Empty<DataConverter>();
        private ConverterHelper.DefaultConstructDelegate<T>? _defaultConstructor;
        private int? _definiteLength;
        private ConverterHelper.ExtensionDataHandler<T>? _extensionData;
        private bool _isReference;
        private Property[] _lookupTable = Array.Empty<Property>();
        private int _maskSize;
        private ConverterHelper.StreamingEventDelegate<T>? _onDeserialized;
        private ConverterHelper.StreamingEventDelegate<T>? _onDeserializing;
        private ConverterHelper.StreamingEventDelegate<T>? _onSerialized;
        private ConverterHelper.StreamingEventDelegate<T>? _onSerializing;
        private ulong[]? _populateMask;
        private DataProperty? _property;

        private ulong[]? _requiredMask;

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        public ClassConverter(SerializerSettings settings)
        {
            _anonymousType = typeof(T).IsGenericType &&
                             typeof(T).GetGenericTypeDefinition().Name.Contains("__AnonymousType");
            var abstractType = typeof(T).IsAbstract || typeof(T).IsInterface;

            var contract = settings.GetContract(typeof(T));
            if (contract.Converter == null)
            {
                contract.FixConverter(this);
            }

            _isReference = contract.IsReference;

            if ((settings.PreserveReferencesHandling & PreserveReferencesHandling.Objects) != 0)
            {
                _isReference = true;
            }

            InitProperties(settings, contract);

            InitCallBackMethod(contract);

            if (!abstractType)
            {
                InitConstructor(settings, contract);
            }

            _contract = contract;
            _writeFastPass = settings.Error == null &&
                             settings.PreserveReferencesHandling == PreserveReferencesHandling.None && !_isReference;

            _readFastPass = settings.Error == null && _constructor == null &&
                            !_isReference &&
                            _contract.UnmappedMemberHandling == UnmappedMemberHandling.Skip &&
                            !_anonymousType && _maskSize == 0 && !_contract.IsPolymorphic &&
                            _extensionData == null;
        }


        private Property[] Properties { get; set; } = Array.Empty<Property>();

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


        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (ClassConverter<T>)MemberwiseClone();
                converter._isReference = false;
                converter._property = property;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }


        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return null;
            }

            if (existingValue != null)
            {
                var type = existingValue.GetType();
                if (type != typeof(T))
                {
                    return (T?)reader.ReadObject(tokenId, type, existingValue);
                }
            }

            if (_readFastPass)
            {
                return ReadFast(reader, tokenId, existingValue);
            }

            var ignoreCase = reader.Settings.PropertyNameCaseInsensitive;
            var typeHandling = _contract.IsPolymorphic;
            var refId = 0U;
            var maskSize = _maskSize;

            Span<ulong> readMask = stackalloc ulong[maskSize];

            var instance = default(T);

            if (_constructor != null && (_anonymousType || existingValue == null))
            {
                var tokens = new uint[_constructorConverters.Length];
                using var list = new Helper.LocalList<(int propId, uint valueId)>(stackalloc (int, uint)[64]);
                using var extensionList = new Helper.LocalList<(uint keyId, uint valueId)>(stackalloc (uint, uint)[16]);

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    var prop = FindProperty(name, ignoreCase);

                    if (prop == null)
                    {
                        if (typeHandling && name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                        {
                            if (reader.ReadTypedObject(_contract, tokenId, kv.Value, ref existingValue))
                            {
                                return existingValue;
                            }
                        }
                        else
                        {
                            if (name.SequenceEqual(Utf8Helper.RefTag))
                            {
                                return (T?)reader.ReadReference(kv.Value);
                            }

                            if (name.SequenceEqual(Utf8Helper.IdTag))
                            {
                                refId = kv.Value;
                            }
                            else
                            {
                                switch (_contract.UnmappedMemberHandling)
                                {
                                    case UnmappedMemberHandling.Skip:
                                        if (_extensionData != null)
                                        {
                                            extensionList.Add((kv.Key, kv.Value));
                                        }

                                        break;
                                    case UnmappedMemberHandling.Disallow:
                                        reader.HandleException(SerializationError.UnmappedMember, null, instance,
                                            _contract, Utf8Helper.GetUtf16String(name),
                                            kv.Value);
                                        break;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (prop.ConstructParamIndex >= 0)
                        {
                            tokens[prop.ConstructParamIndex] = kv.Value;

                            if (maskSize > 0)
                            {
                                readMask[prop.Id / 64] |= 1UL << (prop.Id & 63);
                            }
                        }
                        else
                        {
                            list.Add((prop.Id, kv.Value));
                        }
                    }
                }

                instance = existingValue == null || _anonymousType
                    ? _constructor(reader, _constructorConverters, tokens)
                    : existingValue;

                if (_onDeserializing != null)
                {
                    _onDeserializing(instance, reader.Settings.Context);
                }

                if (refId > 0)
                {
                    reader.AddReference(refId, instance);
                }

                var targets = list.AsSpan();

                for (var i = 0; i < targets.Length; i++)
                {
                    var target = targets[i];
                    var prop = Properties[target.propId];

                    if (maskSize > 0)
                    {
                        readMask[prop.Id / 64] |= 1UL << (prop.Id & 63);
                    }

                    try
                    {
                        if (prop.Reader != null)
                        {
                            prop.Reader(reader, prop.Converter, instance, target.valueId);
                        }
                        else
                        {
                            if (prop.InitOnlyField)
                            {
                                ReadInitOnlyField(reader, prop.Info, prop.Converter, target.valueId, instance);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        reader.HandleException(SerializationError.FailedToRead, e, instance, _contract, prop.Info,
                            target.valueId);
                    }
                }

                var extensionTargets = extensionList.AsSpan();

                for (var i = 0; i < extensionTargets.Length; i++)
                {
                    ReadExtensionData(reader, instance, extensionTargets[i].keyId, extensionTargets[i].valueId);
                }
            }
            else
            {
                var first = true;

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    var prop = FindProperty(name, ignoreCase);

                    if (prop == null)
                    {
                        if (typeHandling && name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                        {
                            if (reader.ReadTypedObject(_contract, tokenId, kv.Value, ref existingValue))
                            {
                                return existingValue;
                            }

                            continue;
                        }

                        if (name.SequenceEqual(Utf8Helper.RefTag))
                        {
                            return (T?)reader.ReadReference(kv.Value);
                        }

                        if (name.SequenceEqual(Utf8Helper.IdTag))
                        {
                            refId = kv.Value;
                            continue;
                        }

                        switch (_contract.UnmappedMemberHandling)
                        {
                            case UnmappedMemberHandling.Disallow:
                                reader.HandleException(SerializationError.UnmappedMember, null, instance, _contract,
                                    Utf8Helper.GetUtf16String(name), kv.Value);
                                break;
                        }

                        if (_extensionData == null ||
                            _contract.UnmappedMemberHandling != UnmappedMemberHandling.Skip)
                        {
                            continue;
                        }
                    }

                    if (first)
                    {
                        first = false;
                        instance = existingValue ?? CreateInstance();

                        if (instance == null)
                        {
                            return null;
                        }

                        if (_onDeserializing != null)
                        {
                            _onDeserializing(instance, reader.Settings.Context);
                        }

                        if (refId > 0)
                        {
                            reader.AddReference(refId, instance);
                        }
                    }

                    if (prop == null)
                    {
                        ReadExtensionData(reader, instance!, kv.Key, kv.Value);
                        continue;
                    }

                    if (maskSize > 0 && prop.Id >= 0)
                    {
                        readMask[prop.Id / 64] |= 1UL << (prop.Id & 63);
                    }

                    try
                    {
                        if (prop.Reader != null)
                        {
                            prop.Reader(reader, prop.Converter, instance!, kv.Value);
                        }
                        else
                        {
                            if (prop.InitOnlyField)
                            {
                                ReadInitOnlyField(reader, prop.Info, prop.Converter, kv.Value, instance);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        reader.HandleException(SerializationError.FailedToRead, e, instance, _contract, prop.Info,
                            kv.Value);
                    }
                }

                if (first)
                {
                    instance = existingValue ?? CreateInstance();

                    if (instance == null)
                    {
                        return null;
                    }

                    if (_onDeserializing != null)
                    {
                        _onDeserializing(instance, reader.Settings.Context);
                    }

                    if (refId > 0)
                    {
                        reader.AddReference(refId, instance);
                    }
                }
            }

            if (_populateMask != null)
            {
                for (var i = 0; i < _populateMask.Length; i++)
                {
                    var mask = ~readMask[i] & _populateMask[i];

                    while (mask != 0)
                    {
                        var index = BitOperations.TrailingZeroCount(mask);
                        mask &= ~(1UL << index);

                        var prop = Properties[i * 64 + index];
                        if (prop.Reader != null)
                        {
                            prop.Reader(reader, prop.Converter, instance!, 0);
                        }
                    }
                }
            }

            if (_requiredMask != null)
            {
                for (var i = 0; i < _requiredMask.Length; i++)
                {
                    var mask = ~readMask[i] & _requiredMask[i];

                    while (mask != 0)
                    {
                        var index = BitOperations.TrailingZeroCount(mask);
                        mask &= ~(1UL << index);

                        var prop = Properties[i * 64 + index];

                        reader.HandleException(SerializationError.MissingRequiredMember, null, instance, _contract,
                            prop.Info,
                            tokenId);
                    }
                }
            }

            if (_onDeserialized != null)
            {
                _onDeserialized(instance!, reader.Settings.Context);
            }

            return instance;
        }

        private void ReadExtensionData(in DataReader reader, T instance, uint keyId, uint valueId)
        {
            try
            {
                _extensionData!.Read(reader, instance, keyId, valueId);
            }
            catch (Exception e)
            {
                reader.HandleException(SerializationError.FailedToRead, e, instance, _contract,
                    _extensionData!.Info, valueId);
            }
        }

        private T? ReadFast(in DataReader reader, uint tokenId, T? existingValue)
        {
            var ignoreCase = reader.Settings.PropertyNameCaseInsensitive;

            var instance = default(T);
            var first = true;

            foreach (var kv in reader.EnumerateMap(tokenId))
            {
                var name = reader.ReadUtf8String(kv.Key);
                var prop = FindProperty(name, ignoreCase);

                if (prop == null)
                {
                    continue;
                }

                if (first)
                {
                    first = false;
                    instance = existingValue ?? CreateInstance();

                    if (instance == null)
                    {
                        return null;
                    }

                    if (_onDeserializing != null)
                    {
                        _onDeserializing(instance, reader.Settings.Context);
                    }
                }

                if (prop.Reader != null)
                {
                    prop.Reader(reader, prop.Converter, instance!, kv.Value);
                }
                else
                {
                    if (prop.InitOnlyField)
                    {
                        ReadInitOnlyField(reader, prop.Info, prop.Converter, kv.Value, instance);
                    }
                }
            }

            if (first)
            {
                instance = existingValue ?? CreateInstance();

                if (instance == null)
                {
                    return null;
                }

                if (_onDeserializing != null)
                {
                    _onDeserializing(instance, reader.Settings.Context);
                }
            }

            if (_onDeserialized != null)
            {
                _onDeserialized(instance!, reader.Settings.Context);
            }

            return instance;
        }


        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                if (_writeFastPass)
                {
                    WriteFast(writer, value, false);
                }
                else
                {
                    WriteData(writer, value, false);
                }
            }
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
            if (!typeNeeded && (writer.Settings.IncludeDerivedProperties || _contract.IsPolymorphic))
            {
                var type = value.GetType();
                if (type != typeof(T) && writer.WritePolymorphic(_contract, _property, type, value))
                {
                    return;
                }
            }

            if (_isReference && writer.TryWriteReference(value))
            {
                return;
            }

            if (_contract.RequiresCycleCheck)
            {
                writer.PushCycleReference(value);
            }

            var typed = _contract.AlwaysWriteTypeDiscriminator || (typeNeeded && _contract.IsPolymorphic);
            var definiteLength = _definiteLength;

            if (_onSerializing != null)
            {
                _onSerializing(value, writer.Settings.Context);
            }

            if (typed)
            {
                if (_isReference)
                {
                    writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 2 : null);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 1 : null);
                }

                writer.WriteTypeDiscriminator(_contract);
            }
            else
            {
                if (_isReference)
                {
                    writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 1 : null);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(definiteLength);
                }
            }

            foreach (var prop in Properties)
            {
                if (prop.Writer != null)
                {
                    try
                    {
                        prop.Writer(writer, prop.Converter, value);
                    }
                    catch (Exception e) when (!(e is SerializationException))
                    {
                        writer.HandleException(e, value, _contract, prop.Info);
                    }
                }
            }

            if (_extensionData != null)
            {
                try
                {
                    _extensionData.Write(writer, value);
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, _contract, _extensionData.Info);
                }
            }

            writer.WriteEndMap();

            if (_onSerialized != null)
            {
                _onSerialized(value, writer.Settings.Context);
            }

            if (_contract.RequiresCycleCheck)
            {
                writer.PopCycleReference();
            }
        }


        private void WriteFast(DataWriter writer, T value, bool typeNeeded)
        {
            if (!typeNeeded && (writer.Settings.IncludeDerivedProperties || _contract.IsPolymorphic))
            {
                var type = value.GetType();
                if (type != typeof(T) && writer.WritePolymorphic(_contract, _property, type, value))
                {
                    return;
                }
            }

            var typed = _contract.AlwaysWriteTypeDiscriminator || (typeNeeded && _contract.IsPolymorphic);
            var definiteLength = _definiteLength;

            if (_onSerializing != null)
            {
                _onSerializing(value, writer.Settings.Context);
            }

            if (typed)
            {
                writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 1 : null);
                writer.WriteTypeDiscriminator(_contract);
            }
            else
            {
                writer.WriteStartMap(definiteLength);
            }

            foreach (var prop in Properties)
            {
                if (prop.Writer != null)
                {
                    prop.Writer(writer, prop.Converter, value);
                }
            }

            _extensionData?.Write(writer, value);

            writer.WriteEndMap();

            if (_onSerialized != null)
            {
                _onSerialized(value, writer.Settings.Context);
            }
        }

        private void ReadInitOnlyField(in DataReader reader, DataProperty info, DataConverter converter, uint valueId,
            T? instance)
        {
            if (info.IsStatic)
            {
                instance = null;
            }

            info.SetValue(instance,
                converter.ReadObject(in reader, info.PropertyType, valueId, info.GetValue(instance)));
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
        public void InitCallBackMethod(DataContract contract)
        {
            foreach (var method in contract.OnSerializingCallbacks)
            {
                _onSerializing += ConverterHelper.CreateStreamingEventDelegate<T>(method);
            }

            foreach (var method in contract.OnSerializedCallbacks)
            {
                _onSerialized += ConverterHelper.CreateStreamingEventDelegate<T>(method);
            }

            foreach (var method in contract.OnDeserializingCallbacks)
            {
                _onDeserializing += ConverterHelper.CreateStreamingEventDelegate<T>(method);
            }

            foreach (var method in contract.OnDeserializedCallbacks)
            {
                _onDeserialized += ConverterHelper.CreateStreamingEventDelegate<T>(method);
            }
        }

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private void InitConstructor(SerializerSettings settings, DataContract contract)
        {
            var ctor = contract.Constructor;

            if (ctor == null)
            {
                _defaultConstructor = () => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                return;
            }

            var parameters = ctor.GetParameters();
            var ignoreCase = true;

            if (parameters.Length == 0)
            {
                _defaultConstructor = ConverterHelper.CreateDefaultConstructFunction<T>();
            }
            else
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

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private Property InitProperty(DataProperty member, SerializerSettings settings)
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

            if (member.ObjectCreationHandling != ObjectCreationHandling.Replace)
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
                        (member.ObjectCreationHandling & ObjectCreationHandling.WhenReadOnly) != 0)
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
                    sp.Reader = ConverterHelper
                        .CreateReadDelegate<ConverterHelper.ReadDelegate<T>, T>(member, converter, true);
                }
                else
                {
                    if (member.Writable)
                    {
                        sp.Reader =
                            ConverterHelper.CreateReadDelegate<ConverterHelper.ReadDelegate<T>, T>(member, converter,
                                false);
                    }
                }
            }

            if (member.Readable)
            {
                sp.Writer =
                    ConverterHelper.CreateWriteDelegate<ConverterHelper.WriteDelegate<T>, T>(member, converter,
                        sp.Symbol);
            }

            return sp;
        }

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private void InitProperties(SerializerSettings settings, DataContract contract)
        {
            var ignoreCase = settings.PropertyNameCaseInsensitive;

            var props = new List<Property>();
            ulong[]? requiredMask = null;
            ulong[]? populateMask = null;
            var writePropertyCount = 0;
            var ignoreCheck = false;
            var maskSize = (contract.Properties.Count + 63) / 64;

            foreach (var member in contract.Properties)
            {
                var prop = InitProperty(member, settings);

                prop.Id = props.Count;

                if (member.IsRequired)
                {
                    if (requiredMask == null)
                    {
                        requiredMask = new ulong[maskSize];
                    }

                    requiredMask[prop.Id / 64] |= 1UL << (prop.Id & 63);
                }

                if ((member.DefaultValueHandling & DefaultValueHandling.Populate) != 0)
                {
                    if (populateMask == null)
                    {
                        populateMask = new ulong[maskSize];
                    }

                    populateMask[prop.Id / 64] |= 1UL << (prop.Id & 63);
                }

                if (prop.Writer != null)
                {
                    writePropertyCount++;
                }

                if ((member.DefaultValueHandling & DefaultValueHandling.Ignore) != 0)
                {
                    ignoreCheck = true;
                }

                if ((member.NullValueHandling & NullValueHandling.IgnoreWrite) != 0)
                {
                    ignoreCheck = true;
                }

                props.Add(prop);
            }

            Properties = props.ToArray();

            _extensionData = ConverterHelper.ExtensionDataHandler<T>.Create(contract.ExtensionData, settings);

            if (_extensionData != null)
            {
                ignoreCheck = true;
            }

            _definiteLength = ignoreCheck ? null : writePropertyCount;

            _lookupTable = new Property[Properties.Length];
            Array.Copy(Properties, _lookupTable, Properties.Length);
            Array.Sort(_lookupTable, (a, b) => Utf8Helper.Compare(a.Name, b.Name, ignoreCase));

            _requiredMask = requiredMask;
            _populateMask = populateMask;

            if (requiredMask != null || populateMask != null)
            {
                _maskSize = maskSize;
            }
        }

        private T? CreateInstance()
        {
            if (_defaultConstructor != null)
            {
                return _defaultConstructor();
            }

            return null;
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
            public ConverterHelper.ReadDelegate<T>? Reader;
            public ConverterHelper.WriteDelegate<T>? Writer;

            public Property(DataProperty info, DataConverter converter, string name)
            {
                Info = info;
                Converter = converter;
                Symbol = new Utf8Symbol(name);
                Name = Symbol.Utf8Bytes;
            }

            public override string ToString()
            {
                return Symbol.ToString();
            }
        }
    }

    internal class DefaultConverter : DataConverter
    {
        private readonly bool _abstractType;
        private readonly bool _anonymousType;
        private readonly DataContract _contract;
        private readonly int? _definiteLength;
        private readonly DataProperty? _extensionData;
        private readonly DataConverter? _extensionDataConverter;
        private readonly PropertyInfo? _extensionDataKeyProperty;
        private readonly MethodInfo? _extensionDataSetItemMethod;
        private readonly PropertyInfo? _extensionDataValueProperty;
        private readonly Type? _extensionDataValueType;
        private readonly Property[] _lookupTable;
        private readonly ulong[]? _requiredMask;

        [DynamicallyAccessedMembers(ReflectionTypeMembers)]
        private readonly Type _type;

        private readonly bool _typedReference;
        private readonly bool _valueType;
        private ConstructorInfo? _constructor;
        private DataConverter[] _constructorConverters = Array.Empty<DataConverter>();
        private bool _isReference;
        private DataProperty? _property;

        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = ContractMetadata)]
        [UnconditionalSuppressMessage("Trimming", "IL2065",
            Justification = "The entry type inherited by IDictionary<string, TValue> is KeyValuePair<string, TValue>; its public properties are explicitly preserved.")]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(IDictionary<,>))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(KeyValuePair<,>))]
        public DefaultConverter(
            [DynamicallyAccessedMembers(ReflectionTypeMembers)]
            Type type, SerializerSettings settings)
        {
            _type = type;
            _valueType = type.IsValueType;
            _anonymousType = type.IsGenericType && type.GetGenericTypeDefinition().Name.Contains("__AnonymousType");
            _abstractType = type.IsAbstract || type.IsInterface;

            var contract = settings.GetContract(type);
            if (contract.Converter == null)
            {
                contract.FixConverter(this);
            }

            _contract = contract;
            _isReference = contract.IsReference;

            if ((settings.PreserveReferencesHandling &
                 (_valueType ? PreserveReferencesHandling.Structs : PreserveReferencesHandling.Objects)) != 0)
            {
                _isReference = true;
            }

            if (_valueType && (settings.PreserveReferencesHandling & PreserveReferencesHandling.Objects) != 0)
            {
                _typedReference = true;
            }

            Properties = InitProperties(settings, contract, out _lookupTable, out _requiredMask, out _definiteLength);
            InitConstructor(settings, contract);

            var extensionData = contract.ExtensionData;
            if (extensionData != null && !extensionData.PropertyType.IsValueType)
            {
                var dictType = extensionData.PropertyType;
                var interfaces = dictType.GetInterfaces();
                if (dictType.IsInterface)
                {
                    interfaces = [dictType, .. interfaces];
                }

                foreach (var iface in interfaces)
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
                        iface.GetGenericArguments()[0] == typeof(string))
                    {
                        var valueType = iface.GetGenericArguments()[1];
                        // IDictionary<string, TValue> already exposes its closed
                        // KeyValuePair type through IEnumerable<KeyValuePair<string, TValue>>.
                        var pairType = Array.Find(iface.GetInterfaces(), static candidate =>
                                candidate.IsGenericType &&
                                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))!
                            .GetGenericArguments()[0];

                        _extensionData = extensionData;
                        _extensionDataValueType = valueType;
                        _extensionDataConverter = settings.GetConverter(valueType);
                        _extensionDataSetItemMethod = iface.GetProperty("Item")!.SetMethod!;
                        _extensionDataKeyProperty = pairType.GetProperty(nameof(KeyValuePair<string, object>.Key))!;
                        _extensionDataValueProperty =
                            pairType.GetProperty(nameof(KeyValuePair<string, object>.Value))!;
                        _definiteLength = null;
                        break;
                    }
                }
            }
        }

        protected internal override Type TargetType => _type;

        private Property[] Properties { get; }

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = reader.ReadString(tokenId);

            if (value == null)
            {
                throw new InvalidCastException();
            }

            //TODO:need to optimize
            var parseMethod = _type.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public,
                new[] { typeof(string) });

            var result = parseMethod?.Invoke(null, new object[] { value });

            if (result == null)
            {
                throw new InvalidCastException();
            }

            return result;
        }

        public override void WriteObjectAsPropertyName(DataWriter writer, object value)
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

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (DefaultConverter)MemberwiseClone();
                converter._isReference = false;
                converter._property = property;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            return ReadData(reader, tokenId, existingValue);
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteData(writer, value, objectType != _type);
        }

        private object? ReadData(in DataReader reader, uint tokenId, object? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return _valueType ? Activator.CreateInstance(_type) : null;
            }

            if (existingValue != null)
            {
                var type = existingValue.GetType();
                if (type != _type)
                {
                    return reader.ReadObject(tokenId, type, existingValue);
                }
            }

            var ignoreCase = reader.Settings.PropertyNameCaseInsensitive;
            var typeHandling = _contract.IsPolymorphic;
            var refId = 0U;
            var requiredMask = _requiredMask == null ? null : (ulong[])_requiredMask.Clone();
            object? instance = null;
            List<(uint keyId, uint valueId)>? extensionList = null;

            if (_constructor != null && _constructor.GetParameters().Length != 0)
            {
                var tokens = new uint[_constructorConverters.Length];
                var list = new List<(int propId, uint valueId)>();

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    var prop = FindProperty(name, ignoreCase);

                    if (prop == null)
                    {
                        if (typeHandling && name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                        {
                            if (reader.ReadTypedObject(_contract, _type, tokenId, kv.Value, ref existingValue))
                            {
                                return existingValue;
                            }
                        }
                        else
                        {
                            if (name.SequenceEqual(Utf8Helper.RefTag))
                            {
                                return reader.ReadReference(kv.Value);
                            }

                            if (name.SequenceEqual(Utf8Helper.IdTag))
                            {
                                refId = kv.Value;
                            }
                            else if (reader.Settings.UnmappedMemberHandling == UnmappedMemberHandling.Disallow)
                            {
                                reader.HandleException(SerializationError.UnmappedMember, null, instance,
                                    _contract, _property, kv.Value);
                            }
                            else if (_extensionData != null)
                            {
                                (extensionList ??= new List<(uint keyId, uint valueId)>()).Add((kv.Key, kv.Value));
                            }
                        }
                    }
                    else
                    {
                        if (prop.ConstructParamIndex >= 0)
                        {
                            tokens[prop.ConstructParamIndex] = kv.Value;
                            ClearRequired(requiredMask, prop.Id);
                        }
                        else
                        {
                            list.Add((prop.Id, kv.Value));
                        }
                    }
                }

                instance = existingValue == null || _anonymousType || _valueType
                    ? CreateParameterizedInstance(reader, tokens)
                    : existingValue;

                InvokeCallbacks(_contract.OnDeserializingCallbacks, instance, reader.Settings.Context);

                if (refId > 0)
                {
                    reader.AddReference(refId, instance);
                }

                foreach (var target in list)
                {
                    var prop = Properties[target.propId];
                    ClearRequired(requiredMask, prop.Id);
                    ReadPropertyWithErrorHandling(reader, prop, instance, target.valueId);
                }
            }
            else
            {
                var first = true;

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    var prop = FindProperty(name, ignoreCase);

                    if (prop == null)
                    {
                        if (typeHandling && name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                        {
                            if (reader.ReadTypedObject(_contract, _type, tokenId, kv.Value, ref existingValue))
                            {
                                return existingValue;
                            }

                            continue;
                        }

                        if (name.SequenceEqual(Utf8Helper.RefTag))
                        {
                            return reader.ReadReference(kv.Value);
                        }

                        if (name.SequenceEqual(Utf8Helper.IdTag))
                        {
                            refId = kv.Value;
                            continue;
                        }

                        switch (reader.Settings.UnmappedMemberHandling)
                        {
                            case UnmappedMemberHandling.Disallow:
                                reader.HandleException(SerializationError.UnmappedMember, null, instance,
                                    _contract, Utf8Helper.GetUtf16String(name), kv.Value);
                                break;
                            default:
                                if (_extensionData != null)
                                {
                                    (extensionList ??= new List<(uint keyId, uint valueId)>()).Add((kv.Key,
                                        kv.Value));
                                }

                                break;
                        }

                        if (prop == null)
                        {
                            continue;
                        }
                    }

                    if (first)
                    {
                        first = false;
                        instance = existingValue ?? CreateInstance();

                        if (instance == null)
                        {
                            return null;
                        }

                        InvokeCallbacks(_contract.OnDeserializingCallbacks, instance, reader.Settings.Context);

                        if (refId > 0)
                        {
                            reader.AddReference(refId, instance);
                        }
                    }

                    ClearRequired(requiredMask, prop.Id);
                    ReadPropertyWithErrorHandling(reader, prop, instance!, kv.Value);
                }

                if (first)
                {
                    instance = existingValue ?? CreateInstance();

                    if (instance == null)
                    {
                        return null;
                    }

                    InvokeCallbacks(_contract.OnDeserializingCallbacks, instance, reader.Settings.Context);

                    if (refId > 0)
                    {
                        reader.AddReference(refId, instance);
                    }
                }
            }

            if (extensionList != null && instance != null)
            {
                foreach (var target in extensionList)
                {
                    try
                    {
                        ReadExtensionData(reader, instance, target.keyId, target.valueId);
                    }
                    catch (Exception e)
                    {
                        reader.HandleException(SerializationError.FailedToRead, e, instance, _contract,
                            _extensionData, target.valueId);
                    }
                }
            }

            ReadMissingRequiredProperties(reader, requiredMask, instance);
            InvokeCallbacks(_contract.OnDeserializedCallbacks, instance!, reader.Settings.Context);

            return instance;
        }

        private void WriteData(DataWriter writer, object value, bool typeNeeded)
        {
            if (writer.Settings.IncludeDerivedProperties || _contract.IsPolymorphic)
            {
                var type = value.GetType();
                if (type != _type)
                {
                    writer.Settings.GetConverter(type, _property).WriteObject(writer, _type, value);
                    return;
                }
            }

            var isReference = _isReference || (_typedReference && typeNeeded);

            if (isReference && writer.TryWriteReference(value))
            {
                return;
            }

            if (_contract.RequiresCycleCheck)
            {
                writer.PushCycleReference(value);
            }

            var typed = _contract.AlwaysWriteTypeDiscriminator || (typeNeeded && _contract.IsPolymorphic);
            var definiteLength = _definiteLength;

            InvokeCallbacks(_contract.OnSerializingCallbacks, value, writer.Settings.Context);

            if (typed)
            {
                if (isReference)
                {
                    writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 2 : null);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 1 : null);
                }

                writer.WriteTypeDiscriminator(_contract);
            }
            else
            {
                if (isReference)
                {
                    writer.WriteStartMap(definiteLength != null ? definiteLength.Value + 1 : null);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(definiteLength);
                }
            }

            var preserved =
                (writer.Settings.PreserveReferencesHandling &
                 (PreserveReferencesHandling.Objects | PreserveReferencesHandling.IgnoreReadOnly)) ==
                (PreserveReferencesHandling.Objects | PreserveReferencesHandling.IgnoreReadOnly);

            foreach (var prop in Properties)
            {
                if (!prop.Writer)
                {
                    continue;
                }

                try
                {
                    WriteProperty(writer, prop, value);
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, _contract, prop.Info);
                }
            }

            if (_extensionData != null)
            {
                try
                {
                    WriteExtensionData(writer, value);
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, _contract, _extensionData);
                }
            }

            writer.WriteEndMap();

            InvokeCallbacks(_contract.OnSerializedCallbacks, value, writer.Settings.Context);

            if (_contract.RequiresCycleCheck)
            {
                writer.PopCycleReference();
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ContractMetadata)]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "The application must root Dictionary<string, TValue> for extension data properties that require a default implementation in Native AOT.")]
        private void ReadExtensionData(in DataReader reader, object instance, uint keyId, uint valueId)
        {
            var info = _extensionData!;
            var dict = info.Readable ? info.GetValue(instance) : null;

            if (dict == null)
            {
                if (!info.Writable)
                {
                    return;
                }

                dict = info.PropertyType.IsInterface || info.PropertyType.IsAbstract
                    ? Activator.CreateInstance(
                        typeof(Dictionary<,>).MakeGenericType(typeof(string), _extensionDataValueType!))!
                    : Activator.CreateInstance(info.PropertyType)!;

                info.SetValue(instance, dict);
            }

            var key = reader.ReadString(keyId);
            var value = _extensionDataConverter!.ReadObject(reader, _extensionDataValueType!, valueId, null);
            _extensionDataSetItemMethod!.Invoke(dict, new[] { key, value });
        }

        private void WriteExtensionData(DataWriter writer, object instance)
        {
            var info = _extensionData!;

            if (!info.Readable || info.GetValue(instance) is not IEnumerable dict)
            {
                return;
            }

            foreach (var kv in dict)
            {
                writer.WriteSymbol((string)_extensionDataKeyProperty!.GetValue(kv)!, SymbolKind.Identifier);
                _extensionDataConverter!.WriteObject(writer, _extensionDataValueType!,
                    _extensionDataValueProperty!.GetValue(kv));
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

        private void InitConstructor(SerializerSettings settings, DataContract contract)
        {
            if (_abstractType)
            {
                return;
            }

            var ctor = contract.Constructor;

            if (ctor == null)
            {
                return;
            }

            var parameters = ctor.GetParameters();
            if (parameters.Length == 0)
            {
                _constructor = ctor;
                return;
            }

            _constructor = ctor;
            _constructorConverters = new DataConverter[parameters.Length];
            var ignoreCase = true;

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

        private static Property[] InitProperties(SerializerSettings settings, DataContract contract,
            out Property[] lookupTable, out ulong[]? requiredMask, out int? definiteLength)
        {
            var props = new List<Property>();
            var mask = new ulong[(contract.Properties.Count + 63) / 64];
            var hasMask = false;
            var writePropertyCount = 0;
            var ignoreCheck = false;

            foreach (var member in contract.Properties)
            {
                var prop = InitProperty(member, settings, member.ObjectCreationHandling);
                prop.Id = props.Count;

                if ((member.DefaultValueHandling & DefaultValueHandling.Populate) != 0 || member.IsRequired)
                {
                    mask[prop.Id / 64] |= 1UL << (prop.Id & 63);
                    hasMask = true;
                }

                if (prop.Writer)
                {
                    writePropertyCount++;
                }

                if ((member.DefaultValueHandling & DefaultValueHandling.Ignore) != 0)
                {
                    ignoreCheck = true;
                }

                if ((member.NullValueHandling & NullValueHandling.IgnoreWrite) != 0)
                {
                    ignoreCheck = true;
                }

                props.Add(prop);
            }

            var properties = props.ToArray();
            definiteLength = ignoreCheck ? null : writePropertyCount;

            lookupTable = new Property[properties.Length];
            Array.Copy(properties, lookupTable, properties.Length);
            Array.Sort(lookupTable, (a, b) => Utf8Helper.Compare(a.Name, b.Name, settings.PropertyNameCaseInsensitive));

            requiredMask = hasMask ? mask : null;

            return properties;
        }

        private static Property InitProperty(DataProperty member, SerializerSettings settings,
            ObjectCreationHandling objectCreationHandling)
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

            var prop = new Property(member, converter)
            {
                Name = Utf8Helper.GetUtf8String(member.Name)
            };

            var existing = false;

            if (objectCreationHandling != ObjectCreationHandling.Replace && member.Readable)
            {
                if (member.PropertyType.IsArray)
                {
                    existing = (objectCreationHandling & ObjectCreationHandling.ReuseArray) != 0;
                }
                else
                {
                    existing = (objectCreationHandling & ObjectCreationHandling.ReuseObject) != 0;
                }

                if (member.Writable && (objectCreationHandling & ObjectCreationHandling.WhenReadOnly) != 0)
                {
                    existing = false;
                }
            }

            if (member.IsReadOnly && member.Info is FieldInfo fieldInfo)
            {
                prop.InitOnlyField = fieldInfo.IsInitOnly;
            }
            else
            {
                if (existing)
                {
                    prop.Reader = true;
                    prop.UseExistingValue = true;
                }
                else if (member.Writable)
                {
                    prop.Reader = true;
                }
            }

            prop.Writer = member.Readable;

            return prop;
        }

        private object? CreateInstance()
        {
            if (_abstractType)
            {
                return null;
            }

            if (_constructor == null)
            {
                return RuntimeHelpers.GetUninitializedObject(_type);
            }

            if (_constructor.GetParameters().Length == 0)
            {
                return _constructor.Invoke(null);
            }

            return null;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ContractMetadata)]
        private object CreateParameterizedInstance(in DataReader reader, uint[] tokens)
        {
            var parameters = _constructor!.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                args[i] = _constructorConverters[i].ReadObject(in reader, parameterType, tokens[i],
                    GetDefaultValue(parameterType));
            }

            return _constructor.Invoke(args);
        }

        private void ReadPropertyWithErrorHandling(in DataReader reader, Property prop, object? instance,
            uint tokenId)
        {
            try
            {
                if (prop.Reader)
                {
                    ReadProperty(reader, prop, instance, tokenId);
                }
                else if (prop.InitOnlyField)
                {
                    ReadInitOnlyField(reader, prop.Info!, prop.Converter, tokenId, instance);
                }
            }
            catch (Exception e)
            {
                reader.HandleException(SerializationError.FailedToRead, e, instance, _contract, prop.Info,
                    tokenId);
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ContractMetadata)]
        private static void ReadProperty(in DataReader reader, Property prop, object? instance, uint tokenId)
        {
            var info = prop.Info!;

            if (info.Writable && (info.DefaultValueHandling & DefaultValueHandling.Populate) != 0 && tokenId == 0)
            {
                info.SetValue(GetTarget(info, instance), GetDefaultValue(info));
                return;
            }

            if ((info.NullValueHandling & NullValueHandling.IgnoreRead) != 0 && reader.IsNullToken(tokenId))
            {
                return;
            }

            var target = GetTarget(info, instance);
            var existingValue = prop.UseExistingValue
                ? info.GetValue(target)
                : GetDefaultValue(prop.Converter.TargetType);
            var value = prop.Converter.ReadObject(in reader, info.PropertyType, tokenId, existingValue);

            if (info.Writable)
            {
                if ((info.DefaultValueHandling & DefaultValueHandling.IgnoreRead) != 0 &&
                    Equals(value, GetDefaultValue(info)))
                {
                    return;
                }

                info.SetValue(target, value);
            }
        }

        private static void ReadInitOnlyField(in DataReader reader, DataProperty info, DataConverter converter,
            uint valueId, object? instance)
        {
            var target = GetTarget(info, instance);
            info.SetValue(target, converter.ReadObject(in reader, info.PropertyType, valueId, info.GetValue(target)));
        }

        private void ReadMissingRequiredProperties(in DataReader reader, ulong[]? requiredMask, object? instance)
        {
            if (requiredMask == null)
            {
                return;
            }

            for (var i = 0; i < requiredMask.Length; i++)
            {
                var mask = requiredMask[i];

                while (mask != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(mask);
                    mask &= ~(1UL << bit);

                    var prop = Properties[i * 64 + bit];
                    if (prop.Reader)
                    {
                        ReadProperty(reader, prop, instance, 0);
                    }
                }
            }
        }

        private void WriteProperty(DataWriter writer, Property prop, object instance)
        {
            if (!ShouldWriteProperty(prop, instance))
            {
                return;
            }

            var value = prop.Info!.GetValue(GetTarget(prop.Info, instance));

            if (TryWriteReferenceLoopProperty(writer, prop, value))
            {
                return;
            }

            writer.WriteString(prop.Name);
            WritePropertyValue(writer, prop, value);
        }

        private void WritePropertyValue(DataWriter writer, Property prop, object? value)
        {
            var declaredType = prop.Info!.PropertyType;

            if (value != null && !declaredType.IsValueType &&
                (_contract.IsPolymorphic ||
                 writer.Settings.IncludeDerivedProperties) &&
                value.GetType() != declaredType)
            {
                var converter = writer.Settings.GetConverter(value.GetType(), prop.Info);

                converter.WriteObject(writer, declaredType, value);
                return;
            }

            prop.Converter.WriteObject(writer, declaredType, value);
        }

        private static bool TryWriteReferenceLoopProperty(DataWriter writer, Property prop, object? value)
        {
            var info = prop.Info!;

            if (value == null ||
                info.PropertyType.IsValueType ||
                info.ReferenceLoopHandling == ReferenceLoopHandling.Serialize ||
                !writer.IsCycleReference(value))
            {
                return false;
            }

            switch (info.ReferenceLoopHandling)
            {
                case ReferenceLoopHandling.Ignore:
                    return true;
                case ReferenceLoopHandling.Null:
                    writer.WriteString(prop.Name);
                    writer.WriteNull();
                    return true;
                case ReferenceLoopHandling.Error:
                    throw new SerializationException(SerializationError.ReferenceLoopDetected)
                    {
                        Member = info
                    };
                default:
                    return false;
            }
        }

        private static bool ShouldWriteProperty(Property prop, object instance)
        {
            var info = prop.Info!;
            var target = GetTarget(info, instance);
            var value = info.GetValue(target);
            var nullableType = Nullable.GetUnderlyingType(info.PropertyType);

            if ((info.DefaultValueHandling & DefaultValueHandling.Ignore) != 0)
            {
                if (Equals(value, GetDefaultValue(info)))
                {
                    return false;
                }
            }

            if ((info.NullValueHandling & NullValueHandling.IgnoreWrite) != 0 &&
                (!info.PropertyType.IsValueType || nullableType != null) &&
                value == null)
            {
                return false;
            }

            if (info.ConditionalMethod != null &&
                info.ConditionalMethod.Invoke(instance, Array.Empty<object>()) is false)
            {
                return false;
            }

            return true;
        }

        private static object? GetTarget(DataProperty info, object? instance)
        {
            return info.IsStatic ? null : instance;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ContractMetadata)]
        private static object? GetDefaultValue(DataProperty info)
        {
            if (info.DefaultValue != null)
            {
                return info.DefaultValue;
            }

            return GetDefaultValue(info.PropertyType);
        }

        private static object? GetDefaultValue(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
            Type type)
        {
            return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;
        }

        private static void ClearRequired(ulong[]? requiredMask, int propId)
        {
            if (requiredMask == null || propId < 0)
            {
                return;
            }

            requiredMask[propId / 64] &= ~(1UL << (propId & 63));
        }

        private static void InvokeCallbacks(IReadOnlyList<MethodInfo> methods, object instance,
            StreamingContext context)
        {
            foreach (var method in methods)
            {
                method.Invoke(instance, new object[] { context });
            }
        }

        private class Property
        {
            public readonly DataConverter Converter;
            public readonly DataProperty? Info;
            public int ConstructParamIndex = -1;
            public int Id;
            public bool InitOnlyField;
            public byte[] Name = Array.Empty<byte>();
            public bool Reader;
            public bool UseExistingValue;
            public bool Writer;

            public Property(DataProperty info, DataConverter converter)
            {
                Info = info;
                Converter = converter;
            }

            public override string ToString()
            {
                return Utf8Helper.GetUtf16String(Name);
            }
        }
    }
}