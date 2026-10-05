// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using REDox.Serialization;
using REDox.Serialization.Metadata;

namespace REDox;

public abstract class SerializerSettings
{
    internal const int DefaultMaxDepth = 64;

    private static int _rootConverterId;
    private readonly Dictionary<Type, DataContract> _contractDict = new();
    private ParallelOptions? _parallelOptions;

    private DataConverter?[] _rootConverterTable = new DataConverter?[16];

    public bool AllowDynamicGenericConverters { get; init; } = RuntimeFeature.IsDynamicCodeSupported;

    public static SerializerSettings Default { get; } = new DoxSerializerSettings();

    public int DefaultBufferSize { get; init; } = 1024 * 16;

    public UnmappedMemberHandling UnmappedMemberHandling { get; init; }

    public DictionaryFormatHandling DictionaryFormatHandling { get; init; }

    public CultureInfo? Culture { get; init; } = CultureInfo.InvariantCulture;

    public bool IgnoreReadOnlyFields { get; init; }

    public bool IgnoreReadOnlyProperties { get; init; }

    public bool IncludeFields { get; init; } = true;

    public bool PropertyNameCaseInsensitive { get; init; }

    public NullValueHandling NullValueHandling { get; init; }

    public EmptyArrayHandling EmptyArrayHandling { get; init; }

    public DefaultValueHandling DefaultValueHandling { get; init; }

    public NamingPolicy? PropertyNamingPolicy { get; init; }

    public NamingPolicy? DictionaryKeyPolicy { get; init; }

    public TextEncoderPolicy TextEncoderPolicy { get; init; } = TextEncoderPolicy.Minimum;

    public UnknownObjectTypeHandling UnknownObjectTypeHandling { get; init; }

    public UnknownArrayTypeHandling UnknownArrayTypeHandling { get; init; }

    public ConstructorHandling ConstructorHandling { get; init; }

    public ObjectCreationHandling ObjectCreationHandling { get; init; }

    public NumberHandling NumberHandling { get; init; }

    public FloatFormatHandling FloatFormatHandling { get; init; }

    public PreserveReferencesHandling PreserveReferencesHandling { get; init; }

    public ReferenceLoopHandling ReferenceLoopHandling { get; init; }

    public bool IncludeDerivedProperties { get; init; }

    public ParallelDeserializeOptions ParallelOptions { get; init; }

    public DateFormatHandling DateFormatHandling { get; init; } = DateFormatHandling.IsoDateFormat;

    public DateTimeZoneHandling DateTimeZoneHandling { get; init; } = DateTimeZoneHandling.RoundtripKind;

    public string? DateFormatString { get; init; }

    public IReadOnlyList<DataConverter> Converters { get; init; } = new List<DataConverter>();

    // Internal until error-recovery behavior is fully validated; planned to become public in a future release.
    internal EventHandler<SerializationErrorEventArgs>? Error { get; init; }

    public StreamingContext Context { get; init; }

    public bool AllowRelaxedScalarConversion { get; init; }

    internal object LockObj => _contractDict;

    protected internal virtual ReferenceResolver CreateReferenceResolver()
    {
        return new DefaultReferenceResolver();
    }

    public DataContract GetContract(Type type)
    {
        lock (_contractDict)
        {
            if (_contractDict.TryGetValue(type, out var value))
            {
                return value;
            }

            var contract = ResolveContract(type);

            _contractDict[type] = contract;

            return contract;
        }
    }

    internal ParallelOptions GetParallelOptions()
    {
        if (_parallelOptions == null)
        {
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = ParallelOptions.MaxDegreeOfParallelism
            };
        }

        return _parallelOptions;
    }

    public DataConverter GetConverter(Type type, DataProperty? property)
    {
        var converter = GetConverter(type);

        return property != null ? converter.ResolvePropertyConverter(property, this) : converter;
    }

    public DataConverter GetConverter(DataContract contract, ReadOnlySpan<byte> typeDiscriminatorSymbol)
    {
        contract = ResolveContract(contract, typeDiscriminatorSymbol);

        if (contract.Converter != null)
        {
            return contract.Converter;
        }

        return contract.FixConverter(GetConverter(contract.Type));
    }

    public DataConverter GetConverter(DataContract contract, int typeDiscriminatorId)
    {
        contract = ResolveContract(contract, typeDiscriminatorId);

        if (contract.Converter != null)
        {
            return contract.Converter;
        }

        return contract.FixConverter(GetConverter(contract.Type));
    }

    public DataConverter GetConverter(Type type)
    {
        lock (_contractDict)
        {
            var contract = GetContract(type);

            if (contract.Converter != null)
            {
                return contract.Converter;
            }

            return contract.FixConverter(CreateConverter(type));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DataConverter GetRootConverter<T>()
    {
        var id = RootConverter<T>.Id;
        var table = _rootConverterTable;

        if ((uint)id < (uint)table.Length)
        {
            var converter = table[id];

            if (converter != null)
            {
                return converter;
            }
        }

        return ResolveRootConverter(id, typeof(T));
    }

    private DataConverter ResolveRootConverter(int id, Type type)
    {
        var table = _rootConverterTable;

        if (id >= table.Length)
        {
            lock (_contractDict)
            {
                table = _rootConverterTable;

                if (id >= table.Length)
                {
                    Array.Resize(
                        ref _rootConverterTable,
                        Math.Max(table.Length * 2, id + 1)
                    );

                    table = _rootConverterTable;
                }
            }
        }

        var converter = table[id];

        if (converter == null)
        {
            converter = GetConverter(type);
            table[id] = converter;
        }

        return converter;
    }

    protected DataConverter CreateConverter(Type type)
    {
        var converter = ResolveConverter(type);

        if (converter is DataConverterFactory factory)
        {
            return factory.CreateConverter(type, this);
        }

        return converter;
    }

    protected DataConverter? CreateConverter(Type type, Type? converterType, object[]? converterParameters,
        Type? itemConverterType, object[]? itemConverterParameters, SerializerSettings settings)
    {
        DataConverter? itemConverter = null;

        if (itemConverterType != null)
        {
            itemConverter = (DataConverter)Activator.CreateInstance(itemConverterType, itemConverterParameters)!;
        }

        if (converterType != null || itemConverter != null)
        {
            DataConverter? converter;

            if (converterType != null)
            {
                converter = Activator.CreateInstance(converterType, converterParameters) as DataConverter;
            }
            else
            {
                converter = settings.ResolveConverter(type);
            }

            if (converter == null)
            {
                throw new InvalidOperationException();
            }

            if (converter is DataConverterFactory factory)
            {
                if (factory is DataCollectionConverterFactory collectionFactory)
                {
                    return collectionFactory.CreateCollectionConverter(type, itemConverter, settings);
                }

                return factory.CreateConverter(type, settings);
            }

            return converter;
        }

        return null;
    }

    protected abstract DataContract ResolveContract(Type type);

    protected abstract DataConverter ResolveConverter(Type type);

    protected virtual DataContract ResolveContract(DataContract baseType, ReadOnlySpan<byte> typeDiscriminatorSymbol)
    {
        foreach (var derivedType in baseType.DerivedTypes)
        {
            var symbol = derivedType.TypeDiscriminator.Symbol;

            if (symbol != null && typeDiscriminatorSymbol.SequenceEqual(symbol))
            {
                return derivedType;
            }
        }

        return baseType;
    }

    protected virtual DataContract ResolveContract(DataContract baseType, int typeDiscriminatorId)
    {
        foreach (var derivedType in baseType.DerivedTypes)
        {
            if (derivedType.TypeDiscriminator.Symbol == null && derivedType.TypeDiscriminator.Id == typeDiscriminatorId)
            {
                return derivedType;
            }
        }

        return baseType;
    }

    private static class RootConverter<T>
    {
        public static readonly int Id;

        static RootConverter()
        {
            Id = Interlocked.Increment(ref _rootConverterId) - 1;
        }
    }
}