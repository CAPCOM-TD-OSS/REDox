// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable 0162

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class GenericCollectionConverter : DataCollectionConverterFactory
{
    private static readonly Dictionary<Type, int> _convertTypes = new()
    {
        { typeof(List<>), 0 },
        { typeof(Dictionary<,>), 0 },
        { typeof(ObservableCollection<>), 0 },
        { typeof(Collection<>), 0 },
        { typeof(HashSet<>), 0 },
        { typeof(Stack<>), 0 },
        { typeof(Queue<>), 0 },
        { typeof(LinkedList<>), 0 },
        { typeof(SortedSet<>), 0 },
        { typeof(SortedDictionary<,>), 0 },
        { typeof(SortedList<,>), 0 },
        { typeof(ConcurrentDictionary<,>), 0 },
        { typeof(ConcurrentStack<>), 0 },
        { typeof(ConcurrentQueue<>), 0 },
        { typeof(ConcurrentBag<>), 0 },
        { typeof(ReadOnlyCollection<>), 0 },
        { typeof(KeyValuePair<,>), 0 },
        { typeof(IDictionary<,>), 1 },
        { typeof(IReadOnlyDictionary<,>), 2 },
        { typeof(ISet<>), 4 },
        { typeof(IList<>), 5 },
        { typeof(IReadOnlyList<>), 6 },
        { typeof(ICollection<>), 7 },
        { typeof(IReadOnlyCollection<>), 8 },
        { typeof(IEnumerable<>), 9 }
    };

    private static readonly Dictionary<Type, Type> _sequenceConverters = new()
    {
        { typeof(LinkedList<>), typeof(LinkListConverter<,>) },
        { typeof(List<>), typeof(ListConverter<,>) },
        { typeof(Collection<>), typeof(ListConverter<,>) },
        { typeof(ObservableCollection<>), typeof(ListConverter<,>) },
        { typeof(IList<>), typeof(ListConverter<,>) },
        { typeof(ICollection<>), typeof(ListConverter<,>) },
        { typeof(HashSet<>), typeof(SetConverter<,>) },
        { typeof(SortedSet<>), typeof(SetConverter<,>) },
        { typeof(ISet<>), typeof(SetConverter<,>) },
        { typeof(Stack<>), typeof(ReadOnlyCollectionConverter<,>) },
        { typeof(Queue<>), typeof(ReadOnlyCollectionConverter<,>) },
        { typeof(ConcurrentQueue<>), typeof(ProducerConsumerCollectionConverter<,>) },
        { typeof(ConcurrentStack<>), typeof(ProducerConsumerCollectionConverter<,>) },
        { typeof(ConcurrentBag<>), typeof(ProducerConsumerCollectionConverter<,>) },
        { typeof(ReadOnlyCollection<>), typeof(ReadOnlyCollectionConverter<,>) },
        { typeof(IReadOnlyCollection<>), typeof(ReadOnlyCollectionConverter<,>) },
        { typeof(IReadOnlyList<>), typeof(ReadOnlyCollectionConverter<,>) },
        { typeof(IReadOnlySet<>), typeof(ReadOnlyCollectionConverter<,>) },
        { typeof(IEnumerable<>), typeof(IEnumerableConverter<,>) }
    };

    private static readonly Dictionary<Type, Type> _dictionaryConverters = new()
    {
        { typeof(Dictionary<,>), typeof(DictionaryConverter<,,>) },
        { typeof(SortedDictionary<,>), typeof(DictionaryConverter<,,>) },
        { typeof(SortedList<,>), typeof(DictionaryConverter<,,>) },
        { typeof(ConcurrentDictionary<,>), typeof(DictionaryConverter<,,>) },
        { typeof(IDictionary<,>), typeof(DictionaryConverter<,,>) },
        { typeof(IReadOnlyDictionary<,>), typeof(ReadOnlyDictionaryConverter<,,>) }
    };

    private static readonly Type[][] _interfacePriorities =
    [
        [typeof(IList<>), typeof(ISet<>), typeof(IDictionary<,>)],
        [typeof(ICollection<>)],
        [typeof(IProducerConsumerCollection<>)],
        [typeof(IReadOnlyList<>)],
        [typeof(IReadOnlyCollection<>)],
        [typeof(IEnumerable<>)]
    ];

    public bool IgnoreProducerConsumerCollectionInterface { get; init; }

    private static Type? GetConvertType(Type type)
    {
        if (type.IsGenericType)
        {
            var genType = type.GetGenericTypeDefinition();
            if (_convertTypes.ContainsKey(genType))
            {
                return genType;
            }
        }

        Type? infType = null;
        var pri = int.MaxValue;

        foreach (var inf in type.GetInterfaces())
        {
            if (inf.IsGenericType)
            {
                var genType = inf.GetGenericTypeDefinition();
                if (_convertTypes.TryGetValue(genType, out var priority))
                {
                    if (pri > priority)
                    {
                        pri = priority;
                        infType = genType;
                    }
                }
            }
        }

        return infType;
    }

    public override bool CanConvert(Type type)
    {
        return GetConvertType(type) != null;
    }

    public override DataConverter CreateCollectionConverter(Type type, DataConverter? itemConverter,
        SerializerSettings settings)
    {
        if (type.IsGenericType)
        {
            var defType = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();

            if (defType == typeof(KeyValuePair<,>))
            {
                return ConverterHelper.CreateConverter(
                    typeof(KeyValuePairConverter<,>).MakeGenericType(args[0], args[1]), settings);
            }

            if (_sequenceConverters.TryGetValue(defType, out var sequenceConverter))
            {
                if (IgnoreProducerConsumerCollectionInterface &&
                    sequenceConverter == typeof(ProducerConsumerCollectionConverter<,>))
                {
                    sequenceConverter = typeof(ReadOnlyCollectionConverter<,>);
                }

                return CreateSequenceConverter(type, args[0], sequenceConverter, itemConverter, settings);
            }

            if (_dictionaryConverters.TryGetValue(defType, out var dictionaryConverter))
            {
                return CreateDictionaryConverter(type, args[0], args[1], dictionaryConverter, itemConverter, settings);
            }
        }

        foreach (var candidates in _interfacePriorities)
        {
            if (IgnoreProducerConsumerCollectionInterface &&
                candidates.IndexOf(typeof(IProducerConsumerCollection<>)) >= 0)
            {
                continue;
            }

            foreach (var inf in type.GetInterfaces())
            {
                if (!inf.IsGenericType)
                {
                    continue;
                }

                var defType = inf.GetGenericTypeDefinition();
                if (Array.IndexOf(candidates, defType) < 0)
                {
                    continue;
                }

                var args = inf.GetGenericArguments();
                if (_dictionaryConverters.TryGetValue(defType, out var dictionaryConverter))
                {
                    return CreateDictionaryConverter(type, args[0], args[1], dictionaryConverter, itemConverter,
                        settings);
                }

                return CreateSequenceConverter(type, args[0], _sequenceConverters[defType], itemConverter, settings);
            }
        }

        throw new NotSupportedException();
    }

    private static DataConverter CreateSequenceConverter(Type type, Type elementType, Type openConverterType,
        DataConverter? itemConverter, SerializerSettings settings)
    {
        if (!settings.AllowDynamicGenericConverters)
        {
            return new DefaultConverter(type, elementType, openConverterType, itemConverter, settings);
        }

        return ConverterHelper.CreateCollectionConverter(openConverterType.MakeGenericType(type, elementType),
            itemConverter, settings);
    }

    private static DataConverter CreateDictionaryConverter(Type type, Type keyType, Type valueType,
        Type openConverterType, DataConverter? itemConverter, SerializerSettings settings)
    {
        if (!settings.AllowDynamicGenericConverters)
        {
            return new DefaultDictionaryConverter(type, keyType, valueType, itemConverter, settings);
        }

        var converterType = openConverterType.MakeGenericType(type, keyType, valueType);

        return openConverterType == typeof(ReadOnlyDictionaryConverter<,,>)
            ? ConverterHelper.CreateConverter(converterType, settings)
            : ConverterHelper.CreateCollectionConverter(converterType, itemConverter, settings);
    }

    private abstract class EnumerateConverter<T, U> : DataConverter<T> where T : IEnumerable<U?>
    {
        private readonly bool _includeDerivedProperties;
        private bool _isReference;
        private bool _isReferenceExisting;
        private DataProperty? _property;

        public EnumerateConverter(DataConverter? itemConverter, SerializerSettings settings)
        {
            Converter = ConverterHelper.CreateItemConverter<U>(itemConverter, settings);
            Contract = settings.GetContract(typeof(T));
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Collections) != 0;
            _includeDerivedProperties = settings.IncludeDerivedProperties;

            if ((settings.PreserveReferencesHandling & PreserveReferencesHandling.Structs) == 0 &&
                typeof(T).IsValueType)
            {
                _isReference = false;
            }
        }

        protected DataContract Contract { get; }

        protected DataConverter<U> Converter { get; set; }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            var itemConverter = Converter.ResolvePropertyConverter(property, settings);

            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (EnumerateConverter<T, U>)MemberwiseClone();
                converter._isReference = false;
                converter._isReferenceExisting = true;
                converter._property = property;
                converter.Converter = (DataConverter<U>)itemConverter;
                return converter;
            }

            if (itemConverter != Converter)
            {
                var converter = (EnumerateConverter<T, U>)MemberwiseClone();
                converter.Converter = (DataConverter<U>)itemConverter;
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

            if (typeof(T).IsInterface && _includeDerivedProperties)
            {
                var converter = writer.Settings.GetConverter(value.GetType(), _property);

                converter.WriteObject(writer, typeof(T), value);
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

            WriteData(writer, (T)value, IsTypeNeeded(objectType));
        }

        protected virtual bool IsTypeNeeded(Type objectType)
        {
            return objectType != typeof(T);
        }

        private void WriteData(DataWriter writer, T value, bool typeNeeded)
        {
            var count = 0;
            if (value is ICollection<U> collection)
            {
                count = collection.Count;
            }
            else
            {
                foreach (var v in value)
                {
                    count++;
                }
            }

            if (_isReferenceExisting)
            {
                //TODO:
                if (writer.CheckReferenced(value))
                {
                    writer.TryWriteReference(value);
                    return;
                }
            }

            using var scope = writer.BeginWriteArray(count, Contract, typeNeeded, _isReference,
                value);

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

    private class DefaultDictionaryConverter : DataConverter
    {
        private readonly MethodInfo? _addMethod;
        private readonly DataContract _contract;
        private readonly DictionaryFormatHandling _dictionaryFormat;
        private readonly PropertyInfo? _indexer;
        private readonly Type _keyType;
        private readonly MethodInfo? _tryAddMethod;
        private readonly Type _valueType;
        private bool _isReference;
        private bool _isReferenceExisting;
        private DataConverter _keyConverter;
        private DataConverter _valueConverter;

        public DefaultDictionaryConverter(Type type, Type keyType, Type valueType, DataConverter? itemConverter,
            SerializerSettings settings)
        {
            _contract = settings.GetContract(type);
            _keyType = keyType;
            _valueType = valueType;
            _keyConverter = settings.GetConverter(_keyType);
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Collections) != 0;
            _dictionaryFormat = settings.DictionaryFormatHandling;

            if (itemConverter != null)
            {
                if (!itemConverter.CanConvert(_valueType))
                {
                    throw new NotSupportedException();
                }

                _valueConverter = itemConverter is DataConverterFactory factory
                    ? factory.CreateConverter(_valueType, settings)
                    : itemConverter;
            }
            else
            {
                _valueConverter = settings.GetConverter(_valueType);
            }

            _indexer = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, _valueType,
                new[] { _keyType }, null);
            _addMethod = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { _keyType, _valueType }, null);
            _tryAddMethod = type.GetMethod("TryAdd", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { _keyType, _valueType }, null);
        }

        protected internal override Type TargetType => _contract.Type;

        public override void WriteObjectAsPropertyName(DataWriter writer, object? value)
        {
            throw new NotSupportedException();
        }

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            throw new NotSupportedException();
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (DefaultDictionaryConverter)MemberwiseClone();
                converter._isReference = false;
                converter._isReferenceExisting = true;
                return converter;
            }

            var keyConverter = _keyConverter.ResolvePropertyConverter(property, settings);
            var valueConverter = _valueConverter.ResolvePropertyConverter(property, settings);

            if (_keyConverter != keyConverter || _valueConverter != valueConverter)
            {
                var converter = (DefaultDictionaryConverter)MemberwiseClone();
                converter._keyConverter = keyConverter;
                converter._valueConverter = valueConverter;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            var kind = reader.GetToken(tokenId).Kind;

            if (kind == DTokenKind.Null)
            {
                return existingValue;
            }

            var dict = existingValue;

            if (dict == null)
            {
                dict = CreateDictionary();
            }

            var format = reader.Settings.DictionaryFormatHandling;

            switch (format)
            {
                case DictionaryFormatHandling.Object:
                case DictionaryFormatHandling.Map:
                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        if (reader.GetToken(kv.Key).Type == DTokenType.Text)
                        {
                            var name = reader.ReadUtf8String(kv.Key);

                            if (name.Length > 0 && name[0] == '$')
                            {
                                if (_contract.TypeDiscriminatorPropertyName.AsSpan().SequenceEqual(name))
                                {
                                    var type = reader.Settings
                                        .GetConverter(_contract, reader.ReadUtf8String(kv.Value))
                                        .TargetType;

                                    if (dict!.GetType() != type)
                                    {
                                        dict = CreateDictionary(type);
                                    }

                                    continue;
                                }

                                if (Utf8Helper.RefTag.AsSpan().SequenceEqual(name) ||
                                    Utf8Helper.IdTag.AsSpan().SequenceEqual(name))
                                {
                                    continue;
                                }
                            }
                        }

                        var key = format == DictionaryFormatHandling.Object
                            ? _keyConverter.ReadObjectAsPropertyName(in reader, kv.Key)
                            : _keyConverter.ReadObject(in reader, _keyType, kv.Key, null);

                        if (key == null)
                        {
                            throw new ArgumentNullException(nameof(key),
                                $"A null key was read for dictionary type '{_contract.Type}'.");
                        }

                        try
                        {
                            SetValue(dict!, key, _valueConverter.ReadObject(in reader, _valueType, kv.Value, null));
                        }
                        catch (Exception e)
                        {
                            reader.HandleException(SerializationError.FailedToRead, e, dict, _contract, key,
                                kv.Value);
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

                            if (Utf8Helper.Compare(name, Utf8Helper.KeyLiteral, true) == 0)
                            {
                                key = _keyConverter.ReadObject(in reader, _keyType, kv.Value, null);
                            }
                            else
                            {
                                if (Utf8Helper.Compare(name, Utf8Helper.ValueLiteral, true) == 0 &&
                                    key != null)
                                {
                                    try
                                    {
                                        SetValue(dict!, key,
                                            _valueConverter.ReadObject(in reader, _valueType, kv.Value, null));
                                    }
                                    catch (Exception e)
                                    {
                                        reader.HandleException(SerializationError.FailedToRead, e, dict, _contract, key,
                                            kv.Value);
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

            return dict;
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var typeNeeded = objectType != TargetType;
            if (typeNeeded &&
                TargetType.IsGenericType &&
                objectType.IsGenericType &&
                TargetType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                objectType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                var targetArguments = TargetType.GetGenericArguments();
                var objectArguments = objectType.GetGenericArguments();
                typeNeeded = targetArguments[0] != objectArguments[0] || targetArguments[1] != objectArguments[1];
            }

            WriteData(writer, value, typeNeeded);
        }

        private static (Type Key, Type Value) GetDictionaryArguments(Type type)
        {
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(Dictionary<,>) ||
                    definition == typeof(SortedDictionary<,>) ||
                    definition == typeof(SortedList<,>) ||
                    definition == typeof(ConcurrentDictionary<,>) ||
                    definition == typeof(IDictionary<,>))
                {
                    var args = type.GetGenericArguments();
                    return (args[0], args[1]);
                }
            }

            foreach (var inf in type.GetInterfaces())
            {
                if (inf.IsGenericType && inf.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    var args = inf.GetGenericArguments();
                    return (args[0], args[1]);
                }
            }

            throw new NotSupportedException();
        }

        private object CreateDictionary()
        {
            return CreateDictionary(TargetType);
        }

        private object CreateDictionary(Type type)
        {
            if (type.IsInterface)
            {
                //TODO: MakeGenericType used
                var value = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(_keyType, _valueType));

                if (value != null)
                {
                    return value;
                }
            }

            try
            {
                var value = Activator.CreateInstance(type);
                if (value != null)
                {
                    return value;
                }
            }
            catch (MissingMethodException e)
            {
                throw new NotSupportedException(null, e);
            }

            throw new NotSupportedException();
        }

        private void SetValue(object dict, object key, object? value)
        {
            if (dict is IDictionary dictionary)
            {
                dictionary[key] = value;
                return;
            }

            if (_indexer != null)
            {
                _indexer.SetValue(dict, value, new[] { key });
                return;
            }

            if (_addMethod != null)
            {
                _addMethod.Invoke(dict, new[] { key, value });
                return;
            }

            if (_tryAddMethod != null)
            {
                _tryAddMethod.Invoke(dict, new[] { key, value });
                return;
            }

            throw new NotSupportedException();
        }

        private void WriteData(DataWriter writer, object value, bool typeNeeded)
        {
            if (_isReferenceExisting)
            {
                if (writer.CheckReferenced(value))
                {
                    writer.TryWriteReference(value);
                    return;
                }
            }

            var count = GetCount(value);

            switch (_dictionaryFormat)
            {
                case DictionaryFormatHandling.Object:
                    {
                        using var scope = writer.BeginWriteCollection(count, _contract, typeNeeded, _isReference,
                            value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (var v in (IEnumerable)value)
                        {
                            var key = GetKey(v);
                            if (key == null)
                            {
                                throw new ArgumentNullException(nameof(key),
                                    $"A null key was found in dictionary type '{_contract.Type}'.");
                            }

                            _keyConverter.WriteObjectAsPropertyName(writer, key);
                            _valueConverter.WriteObject(writer, _valueType, GetValue(v));
                        }
                    }
                    break;
                case DictionaryFormatHandling.Map:
                    {
                        using var scope = writer.BeginWriteCollection(count, _contract, typeNeeded, _isReference, value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (var v in (IEnumerable)value)
                        {
                            _keyConverter.WriteObject(writer, _keyType, GetKey(v));
                            _valueConverter.WriteObject(writer, _valueType, GetValue(v));
                        }
                    }
                    break;
                case DictionaryFormatHandling.KeyValuePair:
                    {
                        using var scope = writer.BeginWriteArray(count, _contract, typeNeeded, false,
                            value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (var v in (IEnumerable)value)
                        {
                            writer.WriteStartMap(2);
                            writer.WriteSymbol(Utf8Helper.KeyLiteral, SymbolKind.Metadata);
                            _keyConverter.WriteObject(writer, _keyType, GetKey(v));
                            writer.WriteSymbol(Utf8Helper.ValueLiteral, SymbolKind.Metadata);
                            _valueConverter.WriteObject(writer, _valueType, GetValue(v));
                            writer.WriteEndMap();
                        }
                    }
                    break;
            }
        }

        private static int GetCount(object value)
        {
            if (value is ICollection collection)
            {
                return collection.Count;
            }

            throw new NotSupportedException();
        }

        private object? GetKey(object entry)
        {
            return entry.GetType().GetProperty("Key")!.GetValue(entry);
        }

        private object? GetValue(object entry)
        {
            return entry.GetType().GetProperty("Value")!.GetValue(entry);
        }
    }

    private class DictionaryConverter<T, U, V> : DataConverter<T> where T : IDictionary<U, V?> where U : notnull
    {
        private readonly DataContract _contract;
        private readonly DictionaryFormatHandling _dictionaryFormat;
        private readonly Func<int, T>? _generator;
        private readonly bool _includeDerivedProperties;

        private bool _isReference;
        private bool _isReferenceExisting;
        private DataConverter<U> _keyConverter;
        private DataProperty? _property;
        private DataConverter<V> _valueConverter;

        public DictionaryConverter(DataConverter? itemConverter, SerializerSettings settings)
        {
            if (itemConverter != null)
            {
                if (!itemConverter.CanConvert(typeof(V)))
                {
                    throw new NotSupportedException();
                }

                if (itemConverter is DataConverterFactory factory)
                {
                    _valueConverter = (DataConverter<V>)factory.CreateConverter(typeof(V), settings);
                }
                else
                {
                    _valueConverter = (DataConverter<V>)itemConverter;
                }
            }
            else
            {
                _valueConverter = (DataConverter<V>)settings.GetConverter(typeof(V));
            }

            _keyConverter = (DataConverter<U>)settings.GetConverter(typeof(U));
            _contract = settings.GetContract(typeof(T));
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Collections) != 0;
            _includeDerivedProperties = settings.IncludeDerivedProperties;
            _dictionaryFormat = settings.DictionaryFormatHandling;

            if (!typeof(T).IsInterface)
            {
                _generator = CreateFactory();
            }
            else
            {
                _generator = capacity => (T)(object)new Dictionary<U, V>(capacity);
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            var keyConverter = (DataConverter<U>)_keyConverter.ResolvePropertyConverter(property, settings);
            var valueConverter = (DataConverter<V>)_valueConverter.ResolvePropertyConverter(property, settings);

            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (DictionaryConverter<T, U, V>)MemberwiseClone();
                converter._isReference = false;
                converter._isReferenceExisting = true;
                converter._property = property;
                converter._keyConverter = keyConverter;
                converter._valueConverter = valueConverter;
                return converter;
            }

            if (_keyConverter != keyConverter ||
                _valueConverter != valueConverter)
            {
                var converter = (DictionaryConverter<T, U, V>)MemberwiseClone();
                converter._property = property;
                converter._keyConverter = keyConverter;
                converter._valueConverter = valueConverter;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            var kind = reader.GetToken(tokenId).Kind;

            if (kind == DTokenKind.Null)
            {
                return existingValue;
            }

            var dict = existingValue;

            if (dict == null)
            {
                if (_generator == null)
                {
                    throw new NotSupportedException();
                }

                var count = reader.GetValueCount(tokenId);

                dict = _generator(count);
            }

            var format = reader.Settings.DictionaryFormatHandling;

            switch (format)
            {
                case DictionaryFormatHandling.Object:
                case DictionaryFormatHandling.Map:
                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        if (reader.GetToken(kv.Key).Type == DTokenType.Text)
                        {
                            var name = reader.ReadUtf8String(kv.Key);

                            if (name.Length > 0 && name[0] == '$')
                            {
                                if (_contract.TypeDiscriminatorPropertyName.AsSpan().SequenceEqual(name))
                                {
                                    var type = reader.Settings
                                        .GetConverter(_contract, reader.ReadUtf8String(kv.Value))
                                        .TargetType;

                                    if (dict!.GetType() != type)
                                    {
                                        dict = (T?)Activator.CreateInstance(type);
                                    }

                                    continue;
                                }

                                if (Utf8Helper.RefTag.AsSpan().SequenceEqual(name) ||
                                    Utf8Helper.IdTag.AsSpan().SequenceEqual(name))
                                {
                                    continue;
                                }
                            }
                        }

                        var key = format == DictionaryFormatHandling.Object
                            ? _keyConverter.ReadAsPropertyName(reader, kv.Key)
                            : _keyConverter.Read(reader, kv.Key, default);

                        if (key == null)
                        {
                            throw new ArgumentNullException(nameof(key),
                                $"A null key was read for dictionary type '{_contract.Type}'.");
                        }

                        try
                        {
                            dict![key] = _valueConverter.Read(reader, kv.Value, default);
                        }
                        catch (Exception e)
                        {
                            reader.HandleException(SerializationError.FailedToRead, e, dict, _contract, key,
                                kv.Value);
                        }
                    }

                    break;
                case DictionaryFormatHandling.KeyValuePair:
                    foreach (var v in reader.EnumerateArray(tokenId))
                    {
                        U? key = default;

                        foreach (var kv in reader.EnumerateMap(v))
                        {
                            var name = reader.ReadUtf8String(kv.Key);

                            if (Utf8Helper.Compare(name, Utf8Helper.KeyLiteral, true) == 0)
                            {
                                key = _keyConverter.Read(reader, kv.Value, default);
                            }
                            else
                            {
                                if (Utf8Helper.Compare(name, Utf8Helper.ValueLiteral, true) == 0 &&
                                    key != null)
                                {
                                    try
                                    {
                                        dict[key] = _valueConverter.Read(reader, kv.Value, default);
                                    }
                                    catch (Exception e)
                                    {
                                        reader.HandleException(SerializationError.FailedToRead, e, dict, _contract, key,
                                            kv.Value);
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

            return dict;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (typeof(T).IsInterface && _includeDerivedProperties)
            {
                var converter = writer.Settings.GetConverter(value.GetType(), _property);

                converter.WriteObject(writer, typeof(T), value);
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

            var typeNeeded = objectType != typeof(T);
            if (typeNeeded)
            {
                if (typeof(T) == typeof(Dictionary<U, V>) && objectType == typeof(IDictionary<U, V>))
                {
                    typeNeeded = false;
                }
            }

            WriteData(writer, (T)value, typeNeeded);
        }

        private void WriteData(DataWriter writer, T value, bool typeNeeded)
        {
            if (_isReferenceExisting)
            {
                //TODO:
                if (writer.CheckReferenced(value))
                {
                    writer.TryWriteReference(value);
                    return;
                }
            }

            switch (_dictionaryFormat)
            {
                case DictionaryFormatHandling.Object:
                    {
                        using var scope = writer.BeginWriteCollection(value.Count, _contract, typeNeeded, _isReference,
                            value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (var v in value)
                        {
                            _keyConverter.WriteAsPropertyName(writer, v.Key);
                            _valueConverter.Write(writer, v.Value);
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

                        foreach (var v in value)
                        {
                            _keyConverter.Write(writer, v.Key);
                            _valueConverter.Write(writer, v.Value);
                        }
                    }

                    break;
                case DictionaryFormatHandling.KeyValuePair:
                    {
                        using var scope = writer.BeginWriteArray(value.Count, _contract, typeNeeded, false,
                            value);

                        if (scope.Skipped)
                        {
                            return;
                        }

                        foreach (var v in value)
                        {
                            writer.WriteStartMap(2);
                            writer.WriteSymbol(Utf8Helper.KeyLiteral, SymbolKind.Metadata);
                            _keyConverter.Write(writer, v.Key);
                            writer.WriteSymbol(Utf8Helper.ValueLiteral, SymbolKind.Metadata);
                            _valueConverter.Write(writer, v.Value);
                            writer.WriteEndMap();
                        }
                    }
                    break;
            }
        }

        private static Func<int, T>? CreateFactory()
        {
            var arg = Expression.Parameter(typeof(int), "arg");

            var ctorInt = typeof(T).GetConstructor(new[] { typeof(int) });
            if (ctorInt != null)
            {
                return Expression.Lambda<Func<int, T>>(
                    Expression.New(ctorInt, arg),
                    arg
                ).Compile();
            }

            var ctorDefault = typeof(T).GetConstructor(Type.EmptyTypes);
            if (ctorDefault != null)
            {
                return Expression.Lambda<Func<int, T>>(
                    Expression.New(ctorDefault),
                    arg
                ).Compile();
            }

            return null;
        }
    }

    private class ReadOnlyDictionaryConverter<T, U, V> : DataConverter<T>
        where T : IReadOnlyDictionary<U, V> where U : notnull
    {
        private bool _isReference;
        private DataProperty? _property;

        public ReadOnlyDictionaryConverter(SerializerSettings settings)
        {
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.All) != 0;
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (_isReference &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 &&
                property.IsReadOnly)
            {
                _isReference = false;
                _property = property;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (existingValue != null)
            {
                return (T?)reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            return (T?)reader.ReadObject(tokenId, typeof(Dictionary<U, V>), existingValue);
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var converter = writer.Settings.GetConverter(value.GetType(), _property);

            converter.WriteObject(writer, typeof(T), value);
        }
    }

    private class KeyValuePairConverter<U, V> : DataConverter<KeyValuePair<U?, V?>>
    {
        private readonly DataConverter<U> _KeyConverter;
        private readonly Utf8Symbol _KeyId;
        private readonly DataConverter<V> _ValueConverter;
        private readonly Utf8Symbol _ValueId;
        private readonly bool _ignoreReadOnlyProperties;

        public KeyValuePairConverter(SerializerSettings settings)
        {
            _KeyConverter = (DataConverter<U>)settings.GetConverter(typeof(U));
            _ValueConverter = (DataConverter<V>)settings.GetConverter(typeof(V));

            _KeyId = Utf8Helper.KeyLiteral;
            _ValueId = Utf8Helper.ValueLiteral;
            _ignoreReadOnlyProperties = settings.IgnoreReadOnlyProperties;

            if (settings.PropertyNamingPolicy != null)
            {
                _KeyId = new Utf8Symbol(settings.PropertyNamingPolicy.ConvertName("Key"));
                _ValueId = new Utf8Symbol(settings.PropertyNamingPolicy.ConvertName("Value"));
            }
        }

        public override KeyValuePair<U?, V?> Read(in DataReader reader, uint tokenId,
            KeyValuePair<U?, V?> existingValue)
        {
            U? key = default;
            V? value = default;

            foreach (var kv in reader.EnumerateMap(tokenId))
            {
                var name = reader.ReadUtf8String(kv.Key);

                if (name.SequenceEqual(_KeyId))
                {
                    key = _KeyConverter.Read(reader, kv.Value, default);
                }
                else
                {
                    if (name.SequenceEqual(_ValueId))
                    {
                        value = _ValueConverter.Read(reader, kv.Value, default);
                    }
                }
            }

            return new KeyValuePair<U?, V?>(key, value);
        }

        public override void Write(DataWriter writer, KeyValuePair<U?, V?> value)
        {
            if (_ignoreReadOnlyProperties)
            {
                writer.WriteStartMap(0);
                writer.WriteEndMap();
            }
            else
            {
                writer.WriteStartMap(2);
                writer.WriteString(_KeyId);
                _KeyConverter.Write(writer, value.Key);
                writer.WriteString(_ValueId);
                _ValueConverter.Write(writer, value.Value);
                writer.WriteEndMap();
            }
        }
    }

    private abstract class CollectionReadConverter<T, U> : EnumerateConverter<T, U>
        where T : IEnumerable<U?>
    {
        protected CollectionReadConverter(DataConverter? itemConverter, SerializerSettings settings)
            : base(itemConverter, settings)
        {
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            var token = reader.GetToken(tokenId);

            if (token.Kind == DTokenKind.Null)
            {
                return default;
            }

            if (token.Type == DTokenType.Map)
            {
                uint refId = 0;
                uint typeId = 0;
                uint valuesId = 0;

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    if (name.SequenceEqual(Contract.TypeDiscriminatorPropertyName))
                    {
                        typeId = kv.Value;
                    }
                    else if (name.SequenceEqual(Utf8Helper.ValuesTag))
                    {
                        valuesId = kv.Value;
                    }
                    else if (name.SequenceEqual(Utf8Helper.IdTag))
                    {
                        refId = kv.Value;
                    }
                    else if (name.SequenceEqual(Utf8Helper.RefTag))
                    {
                        return (T?)reader.ReadReference(kv.Value);
                    }
                }

                if (valuesId == 0)
                {
                    throw new InvalidOperationException();
                }

                var list = existingValue;

                if (typeId != 0)
                {
                    var typeConverter = reader.Settings.GetConverter(Contract, reader.ReadUtf8String(typeId));

                    if (list != null && !list.GetType().IsAssignableTo(typeConverter.TargetType))
                    {
                        list = default;
                    }

                    list = (T?)typeConverter.ReadObject(in reader, typeof(T), valuesId, list);
                }
                else
                {
                    list = ReadItems(in reader, valuesId, list);
                }

                if (refId > 0 && list != null)
                {
                    reader.AddReference(refId, list);
                }

                return list;
            }

            return ReadItems(in reader, tokenId, existingValue);
        }

        protected abstract T? ReadItems(in DataReader reader, uint tokenId, T? existingValue);
    }

    private class ListConverter<T, U> : CollectionReadConverter<T, U> where T : ICollection<U?>
    {
        private readonly Func<int, T>? _generator;
        private readonly bool _szArray;

        public ListConverter(DataConverter? itemConverter, SerializerSettings settings) : base(itemConverter,
            settings)
        {
            if (typeof(T).IsInterface)
            {
                _generator = capacity => (T)(object)new List<U>(capacity);
                _szArray = settings.UnknownArrayTypeHandling == UnknownArrayTypeHandling.SzArray;
            }
            else
            {
                _generator = CreateFactory();
                _szArray = false;
            }
        }

        protected override T? ReadItems(in DataReader reader, uint tokenId, T? existingValue)
        {
            var list = existingValue;
            var converter = Converter;

            if (list == null)
            {
                if (_generator == null)
                {
                    throw new NotSupportedException();
                }

                list = _generator(reader.GetValueCount(tokenId));
            }

            if (list is U[])
            {
                return list;
            }

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list.Add(converter.Read(reader, valueId, default));
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, Contract, list.Count,
                        valueId);
                }
            }

            if (_szArray)
            {
                var array = new U?[list.Count];
                var index = 0;

                foreach (var value in list)
                {
                    array[index++] = value;
                }

                return (T)(array as IEnumerable<U>);
            }

            return list;
        }

        protected override bool IsTypeNeeded(Type objectType)
        {
            var typeNeeded = objectType != typeof(T);
            if (typeNeeded)
            {
                if (typeof(T) == typeof(List<U>) && (objectType == typeof(IList<U>) ||
                                                     objectType == typeof(ICollection<U>) ||
                                                     objectType == typeof(IEnumerable<U>)))
                {
                    typeNeeded = false;
                }
            }

            return typeNeeded;
        }

        private static Func<int, T>? CreateFactory()
        {
            var arg = Expression.Parameter(typeof(int), "arg");

            var ctorInt = typeof(T).GetConstructor(new[] { typeof(int) });
            if (ctorInt != null)
            {
                return Expression.Lambda<Func<int, T>>(
                    Expression.New(ctorInt, arg),
                    arg
                ).Compile();
            }

            var ctorDefault = typeof(T).GetConstructor(Type.EmptyTypes);
            if (ctorDefault != null)
            {
                return Expression.Lambda<Func<int, T>>(
                    Expression.New(ctorDefault),
                    arg
                ).Compile();
            }

            return null;
        }
    }

    private class SetConverter<T, U> : CollectionReadConverter<T, U> where T : ISet<U?>
    {
        private readonly Func<T>? _Generator;

        public SetConverter(DataConverter? itemConverter, SerializerSettings settings) : base(itemConverter,
            settings)
        {
            if (typeof(T).IsInterface)
            {
                _Generator = () => (T)(object)new HashSet<U>();
            }
            else
            {
                _Generator = CreateFactory();
            }
        }

        protected override T? ReadItems(in DataReader reader, uint tokenId, T? existingValue)
        {
            var list = existingValue;
            var converter = Converter;

            if (list == null)
            {
                if (_Generator == null)
                {
                    throw new NotSupportedException();
                }

                list = _Generator();
            }

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list.Add(converter.Read(reader, valueId, default));
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, Contract, list.Count,
                        valueId);
                }
            }

            return list;
        }

        protected override bool IsTypeNeeded(Type objectType)
        {
            var typeNeeded = objectType != typeof(T);
            if (typeNeeded)
            {
                if (typeof(T) == typeof(HashSet<U>) && objectType == typeof(ISet<U>))
                {
                    typeNeeded = false;
                }
            }

            return typeNeeded;
        }

        private static Func<T>? CreateFactory()
        {
            if (typeof(T).GetConstructor(Array.Empty<Type>()) == null)
            {
                return null;
            }

            return Expression.Lambda<Func<T>>(Expression.New(typeof(T))).Compile();
        }
    }

    private class LinkListConverter<T, U> : CollectionReadConverter<T, U> where T : LinkedList<U?>
    {
        private readonly Func<T> _Generator;

        public LinkListConverter(DataConverter? itemConverter, SerializerSettings settings) : base(itemConverter,
            settings)
        {
            _Generator = CreateFactory();
        }

        protected override T? ReadItems(in DataReader reader, uint tokenId, T? existingValue)
        {
            var list = existingValue;
            var converter = Converter;

            if (list == null)
            {
                list = _Generator();
            }

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list.AddLast(new LinkedListNode<U?>(converter.Read(reader, valueId, default)));
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, Contract, list.Count,
                        valueId);
                }
            }

            return list;
        }

        private static Func<T> CreateFactory()
        {
            return Expression.Lambda<Func<T>>(Expression.New(typeof(T))).Compile();
        }
    }

    private class ReadOnlyCollectionConverter<T, U> : CollectionReadConverter<T, U> where T : IReadOnlyCollection<U>
    {
        private readonly Func<U?[], T> _factory;
        private readonly bool _includeDerived;

        public ReadOnlyCollectionConverter(DataConverter itemConverter, SerializerSettings settings) : base(
            itemConverter, settings)
        {
            if (typeof(T).IsInterface)
            {
                if (typeof(T) == typeof(IReadOnlySet<U>))
                {
                    _factory = count => (T)(object)new HashSet<U?>(count);
                }
                else
                {
                    _factory = count => (T)(object)new List<U?>(count);
                }
            }
            else
            {
                _factory = CreateFactory();
            }

            _includeDerived = settings.IncludeDerivedProperties;
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (existingValue != null && typeof(T).IsInterface && _includeDerived)
            {
                return (T?)reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            return base.Read(in reader, tokenId, existingValue);
        }

        protected override T? ReadItems(in DataReader reader, uint tokenId, T? existingValue)
        {
            var converter = Converter;
            var count = reader.GetValueCount(tokenId);
            var list = new U?[count];

            var index = 0;
            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list[index++] = converter.Read(reader, valueId, default);
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, Contract, index,
                        valueId);
                }
            }

            return _factory(list);
        }

        private static Func<U?[], T> CreateFactory()
        {
            var target = Expression.Parameter(typeof(U[]), "list");
            var ctor = typeof(T).GetConstructor(new[] { typeof(U[]) });

            return Expression.Lambda<Func<U?[], T>>(
                Expression.New(ctor!, target), target
            ).Compile();
        }
    }

    private class ProducerConsumerCollectionConverter<T, U> : CollectionReadConverter<T, U>
        where T : IProducerConsumerCollection<U?>
    {
        private readonly Func<U?[], T> _factory;
        private readonly bool _includeDerived;

        public ProducerConsumerCollectionConverter(DataConverter itemConverter, SerializerSettings settings) : base(
            itemConverter, settings)
        {
            if (typeof(T).IsInterface)
            {
                _factory = count => (T)(object)new List<U?>(count);
            }
            else
            {
                _factory = CreateFactory();
            }

            _includeDerived = settings.IncludeDerivedProperties;
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (existingValue != null && typeof(T).IsInterface && _includeDerived)
            {
                return (T?)reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            return base.Read(in reader, tokenId, existingValue);
        }

        protected override T? ReadItems(in DataReader reader, uint tokenId, T? existingValue)
        {
            var converter = Converter;

            if (existingValue != null)
            {
                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    existingValue.TryAdd(converter.Read(reader, valueId, default));
                }

                return existingValue;
            }

            var count = reader.GetValueCount(tokenId);
            var list = new U?[count];

            var index = 0;
            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list[index++] = converter.Read(reader, valueId, default);
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, Contract, index,
                        valueId);
                }
            }

            return _factory(list);
        }

        private static Func<U?[], T> CreateFactory()
        {
            var target = Expression.Parameter(typeof(U[]), "list");
            var ctor = typeof(T).GetConstructor(new[] { typeof(U[]) });

            return Expression.Lambda<Func<U?[], T>>(
                Expression.New(ctor!, target), target
            ).Compile();
        }
    }

    private class IEnumerableConverter<T, U> : CollectionReadConverter<T, U> where T : IEnumerable<U>
    {
        private readonly bool _includeDerived;
        private readonly bool _szArray;

        public IEnumerableConverter(DataConverter itemConverter, SerializerSettings settings) : base(itemConverter,
            settings)
        {
            _szArray = settings.UnknownArrayTypeHandling == UnknownArrayTypeHandling.SzArray;
            _includeDerived = settings.IncludeDerivedProperties;
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (existingValue != null && typeof(T).IsInterface && _includeDerived)
            {
                return (T?)reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            return base.Read(in reader, tokenId, default);
        }

        protected override T? ReadItems(in DataReader reader, uint tokenId, T? existingValue)
        {
            var count = reader.GetValueCount(tokenId);
            var converter = Converter;

            var list = new List<U?>(count);

            foreach (var elementId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list.Add(converter.Read(reader, elementId, default));
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, Contract, list.Count,
                        elementId);
                }
            }

            var result = _szArray ? list.ToArray() as IEnumerable<U> : list as IEnumerable<U>;

            if (result is T)
            {
                return (T)result;
            }

            return default;
        }
    }

    private enum CollectionKind
    {
        Mutable,
        ReadOnly,
        Enumerable
    }

    private class DefaultConverter : DataConverter
    {
        private static readonly Dictionary<Type, Type> _interfaceDefaults = new()
        {
            { typeof(ISet<>), typeof(HashSet<>) },
            { typeof(IReadOnlySet<>), typeof(HashSet<>) },
            { typeof(IList<>), typeof(List<>) },
            { typeof(IReadOnlyList<>), typeof(List<>) },
            { typeof(ICollection<>), typeof(List<>) },
            { typeof(IReadOnlyCollection<>), typeof(List<>) },
            { typeof(IEnumerable<>), typeof(List<>) }
        };

        private readonly DataContract _contract;
        private readonly Type _elementType;
        private readonly bool _includeDerivedProperties;
        private readonly CollectionKind _kind;
        private readonly bool _szArray;
        private bool _isReference;
        private bool _isReferenceExisting;
        private DataConverter _itemConverter;
        private DataProperty? _property;

        public DefaultConverter(Type type, Type elementType, Type openConverterType,
            DataConverter? itemConverter, SerializerSettings settings)
        {
            _kind = openConverterType == typeof(ReadOnlyCollectionConverter<,>)
                ? CollectionKind.ReadOnly
                : openConverterType == typeof(IEnumerableConverter<,>)
                    ? CollectionKind.Enumerable
                    : CollectionKind.Mutable;

            _szArray = settings.UnknownArrayTypeHandling == UnknownArrayTypeHandling.SzArray;
            _contract = settings.GetContract(type);
            _elementType = elementType;
            _includeDerivedProperties = settings.IncludeDerivedProperties;

            if (itemConverter != null)
            {
                if (!itemConverter.CanConvert(elementType))
                {
                    throw new NotSupportedException();
                }

                _itemConverter = itemConverter is DataConverterFactory factory
                    ? factory.CreateConverter(elementType, settings)
                    : itemConverter;
            }
            else
            {
                _itemConverter = settings.GetConverter(elementType);
            }

            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Collections) != 0;

            if ((settings.PreserveReferencesHandling & PreserveReferencesHandling.Structs) == 0 && type.IsValueType)
            {
                _isReference = false;
            }
        }

        protected internal override Type TargetType => _contract.Type;

        public override void WriteObjectAsPropertyName(DataWriter writer, object? value)
        {
            throw new NotSupportedException();
        }

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            throw new NotSupportedException();
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            var itemConverter = _itemConverter.ResolvePropertyConverter(property, settings);

            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (DefaultConverter)MemberwiseClone();
                converter._isReference = false;
                converter._isReferenceExisting = true;
                converter._property = property;
                converter._itemConverter = itemConverter;
                return converter;
            }

            if (itemConverter != _itemConverter)
            {
                var conterter = (DefaultConverter)MemberwiseClone();
                conterter._itemConverter = itemConverter;
                return conterter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            var token = reader.GetToken(tokenId);

            if (token.Kind == DTokenKind.Null)
            {
                return null;
            }

            if (_kind != CollectionKind.Mutable &&
                existingValue != null &&
                TargetType.IsInterface &&
                _includeDerivedProperties)
            {
                return reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            if (token.Type == DTokenType.Map)
            {
                uint refId = 0;
                uint typeId = 0;
                uint valuesId = 0;

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    if (name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                    {
                        typeId = kv.Value;
                    }
                    else if (name.SequenceEqual(Utf8Helper.ValuesTag))
                    {
                        valuesId = kv.Value;
                    }
                    else if (name.SequenceEqual(Utf8Helper.IdTag))
                    {
                        refId = kv.Value;
                    }
                    else if (name.SequenceEqual(Utf8Helper.RefTag))
                    {
                        return reader.ReadReference(kv.Value);
                    }
                }

                if (valuesId == 0)
                {
                    throw new InvalidOperationException();
                }

                object? collection;
                if (typeId != 0)
                {
                    var typeConverter = reader.Settings.GetConverter(_contract, reader.ReadUtf8String(typeId));
                    if (existingValue != null && !existingValue.GetType().IsAssignableTo(typeConverter.TargetType))
                    {
                        existingValue = null;
                    }

                    collection = typeConverter.ReadObject(in reader, objectType, valuesId, existingValue);
                }
                else
                {
                    collection = ReadItems(reader, objectType, valuesId, existingValue);
                }

                if (refId > 0 && collection != null)
                {
                    reader.AddReference(refId, collection);
                }

                return collection;
            }

            return ReadItems(reader, objectType, tokenId, existingValue);
        }

        private object? ReadItems(DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            if (_kind != CollectionKind.Mutable)
            {
                return ReadImmutable(reader, objectType, tokenId);
            }

            if (existingValue != null)
            {
                if (existingValue is not Array)
                {
                    // Arrays cannot be extended. This is consistent with the generic
                    // collection converters, which leave an existing array untouched.
                    ReadInto(reader, tokenId, existingValue);
                }

                return existingValue;
            }

            var collectionType = GetCollectionType(objectType);
            var collection = TryCreateDefault(collectionType);
            if (collection != null)
            {
                ReadInto(reader, tokenId, collection);
                return collection;
            }

            var array = ReadArray(reader, tokenId);
            if (collectionType.IsAssignableFrom(array.GetType()))
            {
                return array;
            }

            return TryCreateFromArray(collectionType, array)
                   ?? throw new NotSupportedException($"Collection type '{collectionType}' cannot be created.");
        }

        private object? ReadImmutable(in DataReader reader, Type objectType, uint tokenId)
        {
            var array = ReadArray(reader, tokenId);

            if (_kind == CollectionKind.Enumerable)
            {
                var result = _szArray ? array : TryCreateFromArray(typeof(List<>).MakeGenericType(_elementType), array);

                return result != null && TargetType.IsInstanceOfType(result) ? result : null;
            }

            var collectionType = GetCollectionType(objectType);
            if (collectionType.IsAssignableFrom(array.GetType()))
            {
                return array;
            }

            return TryCreateFromArray(collectionType, array)
                   ?? throw new NotSupportedException($"Collection type '{collectionType}' cannot be created.");
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (TargetType.IsInterface && _includeDerivedProperties)
            {
                var converter = writer.Settings.GetConverter(value.GetType(), _property);

                converter.WriteObject(writer, objectType, value);
                return;
            }

            if (_contract.IsPolymorphic)
            {
                var valueType = value.GetType();
                if (valueType != TargetType)
                {
                    writer.Settings.GetConverter(valueType, _property).WriteObject(writer, objectType, value);
                    return;
                }
            }

            if (value is not IEnumerable enumerable)
            {
                throw new NotSupportedException();
            }

            var typeNeeded = IsTypeNeeded(objectType);

            if (_isReferenceExisting)
            {
                if (writer.CheckReferenced(value))
                {
                    writer.TryWriteReference(value);
                    return;
                }
            }

            var count = GetCount(enumerable);
            using var scope = writer.BeginWriteArray(count, _contract, typeNeeded, _isReference, value);

            if (scope.Skipped)
            {
                return;
            }

            var index = 0;
            foreach (var element in enumerable)
            {
                try
                {
                    _itemConverter.WriteObject(writer, _elementType, element);
                }
                catch (Exception e) when (e is not SerializationException)
                {
                    writer.HandleException(e, value, _contract, index);
                }

                index++;
            }
        }

        private bool IsTypeNeeded(Type objectType)
        {
            var targetType = TargetType;
            if (objectType == targetType)
            {
                return false;
            }

            if (targetType.IsGenericType && objectType.IsGenericType)
            {
                var definition = targetType.GetGenericTypeDefinition();
                var objectDefinition = objectType.GetGenericTypeDefinition();

                if (targetType.GenericTypeArguments.Length == 1 &&
                    objectType.GenericTypeArguments.Length == 1 &&
                    targetType.GenericTypeArguments[0] == objectType.GenericTypeArguments[0])
                {
                    if (definition == typeof(List<>) &&
                        (objectDefinition == typeof(IList<>) ||
                         objectDefinition == typeof(ICollection<>) ||
                         objectDefinition == typeof(IEnumerable<>)))
                    {
                        return false;
                    }

                    if (definition == typeof(HashSet<>) && objectDefinition == typeof(ISet<>))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Type GetCollectionType(Type objectType)
        {
            if (objectType != typeof(object) &&
                !objectType.IsInterface &&
                !objectType.IsAbstract &&
                TargetType.IsAssignableFrom(objectType))
            {
                return objectType;
            }

            return GetDefaultImplementation(TargetType);
        }

        private static Type GetDefaultImplementation(Type type)
        {
            if (!type.IsInterface)
            {
                return type;
            }

            if (type.IsGenericType &&
                _interfaceDefaults.TryGetValue(type.GetGenericTypeDefinition(), out var definition))
            {
                return definition.MakeGenericType(type.GetGenericArguments());
            }

            if (type == typeof(IList) || type == typeof(ICollection) || type == typeof(IEnumerable))
            {
                return typeof(List<object>);
            }

            return type;
        }

        private static object? TryCreateDefault(Type type)
        {
            if (type.IsInterface || type.IsAbstract)
            {
                return null;
            }

            if (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) == null)
            {
                return null;
            }

            return Activator.CreateInstance(type);
        }

        private object? TryCreateFromArray(Type type, Array array)
        {
            if (type.IsInterface || type.IsAbstract)
            {
                return null;
            }

            var arrayType = array.GetType();
            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(arrayType))
                {
                    return constructor.Invoke(new object?[] { array });
                }
            }

            return null;
        }

        private Array ReadArray(in DataReader reader, uint tokenId)
        {
            var values = new List<object?>(reader.GetValueCount(tokenId));
            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    values.Add(_itemConverter.ReadObject(in reader, _elementType, valueId, null));
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, values, _contract, values.Count,
                        valueId);
                }
            }

            var array = Array.CreateInstance(_elementType, values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                array.SetValue(values[index], index);
            }

            return array;
        }

        private void ReadInto(in DataReader reader, uint tokenId, object collection)
        {
            var add = GetAddAction(collection);
            var index = 0;

            foreach (var valueId in reader.EnumerateArray(tokenId))
            {
                try
                {
                    add(_itemConverter.ReadObject(in reader, _elementType, valueId, null));
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, collection, _contract, index,
                        valueId);
                }

                index++;
            }
        }

        private Action<object?> GetAddAction(object collection)
        {
            if (collection is IList list && !list.IsFixedSize && !list.IsReadOnly)
            {
                return value => list.Add(value);
            }

            var addMethod = FindAddMethod(collection.GetType())
                            ?? throw new NotSupportedException(
                                $"Collection type '{collection.GetType()}' cannot be populated.");

            return value => addMethod.Invoke(collection, new[] { value });
        }

        private MethodInfo? FindAddMethod(Type type)
        {
            var parameterTypes = new[] { _elementType };
            foreach (var methodName in new[] { "Add", "AddLast", "Enqueue", "Push", "TryAdd" })
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null,
                    parameterTypes, null);
                if (method != null)
                {
                    return method;
                }
            }

            foreach (var inf in type.GetInterfaces())
            {
                if (!inf.IsGenericType || inf.GetGenericArguments()[0] != _elementType)
                {
                    continue;
                }

                var definition = inf.GetGenericTypeDefinition();
                if (definition != typeof(ICollection<>) && definition != typeof(ISet<>))
                {
                    continue;
                }

                var method = inf.GetMethod("Add", parameterTypes);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static int GetCount(IEnumerable value)
        {
            if (value is ICollection collection)
            {
                return collection.Count;
            }

            var countProperty = value.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            if (countProperty?.PropertyType == typeof(int))
            {
                return (int)countProperty.GetValue(value)!;
            }

            var count = 0;
            foreach (var unused in value)
            {
                count++;
            }

            return count;
        }
    }
}