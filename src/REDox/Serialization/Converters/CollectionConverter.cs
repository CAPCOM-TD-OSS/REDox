// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class CollectionConverter : DataConverterFactory
{
    // Applications must preserve interfaces, constructors, and contract metadata
    // for runtime-supplied collection types, including derived types.
    private const string CollectionMetadata =
        "The application must preserve collection interfaces, constructors, and contract metadata, including derived types.";

    private const DynamicallyAccessedMemberTypes CollectionConstructors =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors;

    private static readonly Dictionary<Type, int> s_convertTypes = new()
    {
        { typeof(BitArray), 0 },
        { typeof(Queue), 0 },
        { typeof(Stack), 0 },
        { typeof(IDictionary), 0 },
        { typeof(IList), 1 },
        { typeof(ICollection), 2 },
        { typeof(IEnumerable), 3 }
    };

    private static Type? GetConvertType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
        Type type)
    {
        if (s_convertTypes.ContainsKey(type))
        {
            return type;
        }

        Type? infType = null;
        var pri = int.MaxValue;

        foreach (var inf in type.GetInterfaces())
        {
            if (s_convertTypes.TryGetValue(inf, out var priority))
            {
                if (pri > priority)
                {
                    pri = priority;
                    infType = inf;
                }
            }
        }

        return infType;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = CollectionMetadata)]
    public override bool CanConvert(Type type)
    {
        return GetConvertType(type) != null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = CollectionMetadata)]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = CollectionMetadata)]
    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var convertType = GetConvertType(type);
        if (!RuntimeFeature.IsDynamicCodeSupported || !settings.AllowDynamicGenericConverters)
        {
            return CreateReflectionConverter(type, convertType, settings);
        }

        switch (convertType)
        {
            case var t when t == typeof(BitArray):
                return new BitArrayConverter(settings);
            case var t when t == typeof(Queue):
                return new QueueConverter(settings);
            case var t when t == typeof(Stack):
                return new StackConverter(settings);
            case var t when t == typeof(IList):
                return ConverterHelper.CreateConverter(typeof(ListConverter<>).MakeGenericType(type), settings);
            case var t when t == typeof(ICollection):
                return ConverterHelper.CreateConverter(typeof(CollectionInterfaceConverter<>).MakeGenericType(type),
                    settings);
            case var t when t == typeof(IEnumerable):
                return ConverterHelper.CreateConverter(typeof(EnumerableInterfaceConverter<>).MakeGenericType(type),
                    settings);
            case var t when t == typeof(IDictionary):
                return ConverterHelper.CreateConverter(typeof(DictionaryConverter<>).MakeGenericType(type),
                    settings);
        }

        throw new NotSupportedException();
    }

    private static DataConverter CreateReflectionConverter(
        [DynamicallyAccessedMembers(CollectionConstructors)]
        Type type,
        Type? convertType, SerializerSettings settings)
    {
        if (convertType == typeof(BitArray))
        {
            return new BitArrayConverter(settings);
        }

        if (convertType == typeof(Queue))
        {
            return new QueueConverter(settings);
        }

        if (convertType == typeof(Stack))
        {
            return new StackConverter(settings);
        }

        DataConverter converter;
        if (convertType == typeof(IList))
        {
            converter = new ListConverter<IList>(type, CreateReflectionFactory<IList>(type), settings);
        }
        else if (convertType == typeof(IDictionary))
        {
            converter = new DictionaryConverter<IDictionary>(type, CreateReflectionFactory<IDictionary>(type), settings);
        }
        else if (convertType == typeof(ICollection))
        {
            converter = new CollectionInterfaceConverter<ICollection>(type, settings);
        }
        else if (convertType == typeof(IEnumerable))
        {
            converter = new EnumerableInterfaceConverter<IEnumerable>(type, settings);
        }
        else
        {
            throw new NotSupportedException();
        }

        return new DefaultConverter(type, converter, convertType == typeof(IEnumerable));
    }

    private static Func<T>? CreateReflectionFactory<T>(
        [DynamicallyAccessedMembers(CollectionConstructors)]
        Type type)
    {
        if (type.IsInterface)
        {
            return null;
        }

        // Match Expression.New(Type), including non-public parameterless constructors.
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                              null, Type.EmptyTypes, null)
                          ?? throw new NotSupportedException($"Collection type '{type}' has no parameterless constructor.");
        return () => (T)constructor.Invoke(null);
    }

    private sealed class DefaultConverter : DataConverter
    {
        private readonly DataConverter _converter;
        private readonly bool _enumerable;

        public DefaultConverter(Type type, DataConverter converter, bool enumerable)
        {
            TargetType = type;
            _converter = converter;
            _enumerable = enumerable;
        }

        protected internal override Type TargetType { get; }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            var converter = _converter.ResolvePropertyConverter(property, settings);
            return converter == _converter ? this : new DefaultConverter(TargetType, converter, _enumerable);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            if (existingValue != null && !TargetType.IsInstanceOfType(existingValue))
            {
                throw new InvalidCastException($"Existing collection cannot be converted to '{TargetType}'.");
            }

            var value = _converter.ReadObject(in reader, objectType, tokenId, existingValue);
            if (value != null && !TargetType.IsInstanceOfType(value))
            {
                // Match the original enumerable converter, which returns null
                // when the default array does not implement the requested type.
                if (_enumerable)
                {
                    return null;
                }

                throw new InvalidCastException($"Collection value cannot be converted to '{TargetType}'.");
            }

            return value;
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value != null && !TargetType.IsInstanceOfType(value))
            {
                throw new InvalidCastException($"Collection value cannot be converted to '{TargetType}'.");
            }

            _converter.WriteObject(writer, objectType, value);
        }

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            throw new NotSupportedException();
        }

        public override void WriteObjectAsPropertyName(DataWriter writer, object? value)
        {
            throw new NotSupportedException();
        }
    }

    private abstract class EnumerateConverter<T> : DataConverter<T> where T : IEnumerable
    {
        private readonly bool _includeDerivedProperties;
        private bool _isReference;
        private DataProperty? _property;

        public EnumerateConverter(SerializerSettings settings) : this(typeof(T), settings)
        {
        }

        protected EnumerateConverter(Type type, SerializerSettings settings)
        {
            Contract = settings.GetContract(type);
            Converter = (DataConverter<object>)settings.GetConverter(typeof(object));
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Arrays) != 0;
            _includeDerivedProperties = settings.IncludeDerivedProperties;
        }

        protected DataContract Contract { get; }

        protected DataConverter<object> Converter { get; }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (EnumerateConverter<T>)MemberwiseClone();

                converter._isReference = false;
                converter._property = property;

                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (Contract.Type.IsInterface && _includeDerivedProperties)
            {
                var converter = writer.Settings.GetConverter(value.GetType(), _property);

                converter.WriteObject(writer, Contract.Type, value);
            }
            else
            {
                WriteData(writer, value, false);
            }
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteData(writer, (T)value, objectType != Contract.Type);
        }

        private void WriteData(DataWriter writer, T value, bool typeNeeded)
        {
            var count = 0;
            if (value is ICollection collection)
            {
                count = collection.Count;
            }
            else
            {
                foreach (var _ in value)
                {
                    count++;
                }
            }

            using var scope = writer.BeginWriteArray(count, Contract, typeNeeded, _isReference, value);

            if (scope.Skipped)
            {
                return;
            }

            var index = 0;
            foreach (var element in value)
            {
                try
                {
                    Converter.Write(writer, element);
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, Contract, index);
                }

                index++;
            }
        }
    }

    private class QueueConverter : EnumerateConverter<Queue>
    {
        public QueueConverter(SerializerSettings settings) : base(settings)
        {
        }

        public override Queue? Read(in DataReader reader, uint tokenId, Queue? existingValue)
        {
            Queue? value = null;
            if (reader.ReadTypedArray(Contract, tokenId, ref value))
            {
                return value;
            }

            var collection = new Queue(reader.GetValueCount(tokenId));
            var converter = Converter;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                collection.Enqueue(converter.Read(reader, valueId, null));
            }

            return collection;
        }
    }

    private class StackConverter : EnumerateConverter<Stack>
    {
        public StackConverter(SerializerSettings settings) : base(settings)
        {
        }

        public override Stack? Read(in DataReader reader, uint tokenId, Stack? existingValue)
        {
            Stack? value = null;
            if (reader.ReadTypedArray(Contract, tokenId, ref value))
            {
                return value;
            }

            var collection = new Stack(reader.GetValueCount(tokenId));
            var converter = Converter;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                collection.Push(converter.Read(reader, valueId, null));
            }

            return collection;
        }
    }

    private class BitArrayConverter : EnumerateConverter<BitArray>
    {
        public BitArrayConverter(SerializerSettings settings) : base(settings)
        {
        }

        public override BitArray? Read(in DataReader reader, uint tokenId, BitArray? existingValue)
        {
            var count = reader.GetValueCount(tokenId);
            var collection = new BitArray(count);
            var index = 0;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                collection[index++] = reader.ReadBoolean(valueId);
            }

            return collection;
        }
    }

    private class CollectionInterfaceConverter<T> : EnumerateConverter<T> where T : IEnumerable
    {
        public CollectionInterfaceConverter(SerializerSettings settings) : base(settings)
        {
        }

        public CollectionInterfaceConverter(Type type, SerializerSettings settings) : base(type, settings)
        {
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (existingValue != null)
            {
                return (T?)reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            object? value = null;
            if (reader.ReadTypedArray(Contract, Contract.Type, tokenId, ref value))
            {
                return (T?)value;
            }

            var list = new object?[reader.GetValueCount(tokenId)];
            var converter = Converter;
            var index = 0;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                list[index++] = converter.Read(reader, valueId, null);
            }

            return (T)(object)list;
        }
    }

    private class ListConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        T>
        : EnumerateConverter<T> where T : IList
    {
        private readonly Func<T>? _generator;

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        public ListConverter(SerializerSettings settings) : base(settings)
        {
            if (!typeof(T).IsInterface)
            {
                _generator = CreateFactory();
            }
        }

        public ListConverter(Type type, Func<T>? generator, SerializerSettings settings) : base(type, settings)
        {
            _generator = generator;
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            object? value = existingValue;
            if (reader.ReadTypedArray(Contract, Contract.Type, tokenId, ref value))
            {
                return (T?)value;
            }

            IList? collection = existingValue;

            if (collection == null)
            {
                var count = reader.GetValueCount(tokenId);

                if (_generator == null)
                {
                    collection = new ArrayList(count);
                }
                else
                {
                    collection = _generator();
                }
            }

            var converter = Converter;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                collection.Add(converter.Read(reader, valueId, null));
            }

            return (T)collection;
        }

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private static Func<T> CreateFactory()
        {
            return Expression.Lambda<Func<T>>(Expression.New(typeof(T))).Compile();
        }
    }

    private class DictionaryConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        T>
        : DataConverter<T> where T : IDictionary
    {
        private readonly DataContract _contract;
        private readonly DictionaryFormatHandling _dictionaryFormat;
        private readonly NamingPolicy? _dictionaryKeyPolicy;
        private readonly Func<T>? _generator;
        private readonly bool _includeDerivedProperties;
        private readonly UnknownObjectTypeHandling _unknownObjectType;
        private bool _isReference;
        private DataProperty? _property;

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        public DictionaryConverter(SerializerSettings settings)
            : this(typeof(T), typeof(T).IsInterface ? null : CreateFactory(), settings)
        {
        }

        public DictionaryConverter(Type type, Func<T>? generator, SerializerSettings settings)
        {
            _generator = generator;
            _contract = settings.GetContract(type);
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Arrays) != 0;
            _dictionaryFormat = settings.DictionaryFormatHandling;
            _unknownObjectType = settings.UnknownObjectTypeHandling;
            _includeDerivedProperties = settings.IncludeDerivedProperties;
            _dictionaryKeyPolicy = settings.DictionaryKeyPolicy;
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (DictionaryConverter<T>)MemberwiseClone();
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
                return default;
            }

            IDictionary? collection = existingValue;

            if (collection == null)
            {
                var count = reader.GetValueCount(tokenId);

                if (_generator == null)
                {
                    switch (_unknownObjectType)
                    {
                        case UnknownObjectTypeHandling.Element:
                            collection = new Dictionary<string, object>(count);
                            break;
                        default:
                            collection = new Hashtable(count);
                            break;
                    }
                }
                else
                {
                    collection = _generator();
                }
            }

            switch (_dictionaryFormat)
            {
                case DictionaryFormatHandling.Object:
                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        var key = reader.ReadUtf8String(kv.Key);

                        if (key.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                        {
                            continue;
                        }

                        if (Utf8Helper.RefTag.AsSpan().SequenceEqual(key) ||
                            Utf8Helper.IdTag.AsSpan().SequenceEqual(key))
                        {
                            continue;
                        }

                        var value = reader.ReadObject(kv.Value, typeof(object), null);

                        collection[Encoding.UTF8.GetString(key)] = value;
                    }

                    break;
                case DictionaryFormatHandling.Map:
                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        var key = reader.ReadObject(kv.Key, typeof(object), null);
                        var value = reader.ReadObject(kv.Value, typeof(object), null);

                        if (key != null)
                        {
                            collection[key] = value;
                        }
                    }

                    break;
                case DictionaryFormatHandling.KeyValuePair:
                    foreach (var v in reader.EnumerateArray(tokenId))
                    {
                        object? key = null;

                        foreach (var kv in reader.EnumerateMap(v))
                        {
                            var name = reader.ReadUtf8String(kv.Key);

                            if (name.SequenceEqual(Utf8Helper.KeyLiteral))
                            {
                                try
                                {
                                    key = reader.ReadObject(kv.Value, typeof(object), null);
                                }
                                catch (Exception e)
                                {
                                    reader.HandleException(SerializationError.FailedToRead, e, collection,
                                        _contract, Utf8Helper.KeyLiteral.ToString(), kv.Value);
                                }
                            }
                            else
                            {
                                if (name.SequenceEqual(Utf8Helper.ValueLiteral) && key != null)
                                {
                                    try
                                    {
                                        collection[key] = reader.ReadObject(kv.Value, typeof(object), null);
                                    }
                                    catch (Exception e)
                                    {
                                        reader.HandleException(SerializationError.FailedToRead, e, collection,
                                            _contract,
                                            Utf8Helper.ValueLiteral.ToString(), kv.Value);
                                    }
                                }
                                else
                                {
                                    throw new FormatException();
                                }
                            }
                        }
                    }

                    break;
            }

            return (T)collection;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (_contract.Type.IsInterface && _includeDerivedProperties)
            {
                var converter = writer.Settings.GetConverter(value.GetType(), _property);

                converter.WriteObject(writer, _contract.Type, value);
            }
            else
            {
                WriteData(writer, value, false);
            }
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var typeNeeded = objectType != _contract.Type;

            WriteData(writer, (T)value, typeNeeded);
        }

        private void WriteData(DataWriter writer, T value, bool typeNeeded)
        {
            var np = _dictionaryKeyPolicy;

            switch (_dictionaryFormat)
            {
                case DictionaryFormatHandling.Object:
                    {
                        using var scope =
                            writer.BeginWriteCollection(value.Count, _contract, typeNeeded, _isReference, value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (DictionaryEntry v in value)
                        {
                            var keyConverter = writer.Settings.GetConverter(v.Key.GetType());

                            keyConverter.WriteObjectAsPropertyName(writer, v.Key);

                            if (v.Value == null)
                            {
                                writer.WriteNull();
                            }
                            else
                            {
                                writer.WriteObject(v.Value, typeof(object));
                            }
                        }
                    }
                    break;
                case DictionaryFormatHandling.Map:
                    {
                        using var scope = writer.BeginWriteCollection(value.Count, _contract, typeNeeded, _isReference,
                            value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (DictionaryEntry v in value)
                        {
                            writer.WriteObject(v.Key, typeof(object));
                            writer.WriteObject(v.Value, typeof(object));
                        }
                    }
                    break;
                case DictionaryFormatHandling.KeyValuePair:
                    {
                        using var scope = writer.BeginWriteArray(value.Count, _contract, typeNeeded, _isReference, value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (DictionaryEntry v in value)
                        {
                            writer.WriteStartMap(2);
                            writer.WriteSymbol(Utf8Helper.KeyLiteral, SymbolKind.Metadata);

                            if (v.Key == null)
                            {
                                writer.WriteNull();
                            }
                            else
                            {
                                writer.WriteObject(v.Key, typeof(object));
                            }

                            writer.WriteSymbol(Utf8Helper.ValueLiteral, SymbolKind.Metadata);

                            if (v.Value == null)
                            {
                                writer.WriteNull();
                            }
                            else
                            {
                                writer.WriteObject(v.Value, typeof(object));
                            }

                            writer.WriteEndMap();
                        }
                    }

                    break;
            }
        }

        [RequiresDynamicCode(ConverterHelper.DynamicCode)]
        private static Func<T> CreateFactory()
        {
            return Expression.Lambda<Func<T>>(Expression.New(typeof(T))).Compile();
        }
    }

    private class EnumerableInterfaceConverter<T> : EnumerateConverter<T> where T : IEnumerable
    {
        public EnumerableInterfaceConverter(SerializerSettings settings) : base(settings)
        {
        }

        public EnumerableInterfaceConverter(Type type, SerializerSettings settings) : base(type, settings)
        {
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (existingValue != null && Contract.Type.IsInterface)
            {
                return (T?)reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            object? value = existingValue;
            if (reader.ReadTypedArray(Contract, Contract.Type, tokenId, ref value))
            {
                return (T?)value;
            }

            var list = new object?[reader.GetValueCount(tokenId)];
            var converter = Converter;
            var index = 0;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                list[index++] = converter.Read(reader, valueId, null);
            }

            if (list is T)
            {
                return (T)(object)list;
            }

            return default;
        }
    }
}