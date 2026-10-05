// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using REDox.Serialization.Converters;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.DataContractJson;

public class DataContractJsonSerializerSettings : SerializerSettings
{
    private static readonly Utf8Symbol DataContractTypeDiscriminatorPropertyName = new("__type");

    private static readonly System.Runtime.Serialization.Json.DataContractJsonSerializerSettings s_defaultSettings =
        new();

    private static readonly ParseKeyConverterFactory s_classTypeConverter = new(new ClassTypeConverter());
    private static readonly ParseKeyConverterFactory s_valueTypeConverter = new(new ValueTypeConverter());
    private static readonly ArrayConverter s_arrayConverter = new();
    private static readonly GenericCollectionConverter s_genericCollectionConverter = new();
    private static readonly CollectionConverter s_collectionConverter = new();

    private static readonly DataConverter[] DefaultConverters = new DataConverter[]
    {
        new EnumTypeConverter(),
        new DoxTypeConverter(),
        new BuiltInTypeConverter(),
        s_arrayConverter,
        s_genericCollectionConverter,
        s_collectionConverter,
        new SerializableTypeConverter(),
        s_valueTypeConverter,
        s_classTypeConverter
    };

    private static readonly XsdDataContractExporter s_exporter = new();

    private static readonly TextEncoderPolicy DataContractEncoder = new(TextEscapeMask.Slash | TextEscapeMask.NonBmp);
    private readonly DateTimeFormatConverter? _dateTimeFormatConverter;
    private readonly ExtensibleDataObjectConverter? _extensibleDataObjectConverter;

    private readonly Dictionary<byte[], Type> _knownTypeDict = new(ByteArrayComparer.Instance);
    private readonly IEnumerable<Type>? _knownTypes;

    public DataContractJsonSerializerSettings(
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings? settings = default)
    {
        if (settings == null)
        {
            settings = s_defaultSettings;
        }

        EmitTypeInformation = settings.EmitTypeInformation;

        if (!settings.IgnoreExtensionDataObject)
        {
            _extensibleDataObjectConverter = new ExtensibleDataObjectConverter(settings);
        }

        Culture = null;
        if (settings.DateTimeFormat != null)
        {
            _dateTimeFormatConverter = new DateTimeFormatConverter(settings.DateTimeFormat);
        }

        AllowRelaxedScalarConversion = true;
        KnownTypes = settings.KnownTypes != null ? settings.KnownTypes : Array.Empty<Type>();
        EmptyArrayHandling = EmptyArrayHandling.Unique;
        UnknownObjectTypeHandling = UnknownObjectTypeHandling.Default;
        UnknownArrayTypeHandling = UnknownArrayTypeHandling.SzArray;
        TextEncoderPolicy = DataContractEncoder;
        ConstructorHandling = ConstructorHandling.IgnoreStructDefaultConstructor;
        IncludeFields = true;
        FloatFormatHandling = FloatFormatHandling.SpecialFloatAsXmlSymbol;
        DateFormatHandling = DateFormatHandling.MicrosoftDateFormat;
        DateFormatString = settings.DateTimeFormat?.FormatString;
        ObjectCreationHandling = ObjectCreationHandling.WhenReadOnly | ObjectCreationHandling.ReuseObject |
                                 ObjectCreationHandling.ReuseArray;
        DictionaryFormatHandling = settings.UseSimpleDictionaryFormat
            ? DictionaryFormatHandling.Object
            : DictionaryFormatHandling.KeyValuePair;
    }

    public EmitTypeInformation EmitTypeInformation { get; init; }

    public IEnumerable<Type> KnownTypes
    {
        get
        {
            if (_knownTypes == null)
            {
                return Array.Empty<Type>();
            }

            return _knownTypes;
        }
        init
        {
            _knownTypes = value;

            if (value != null)
            {
                foreach (var knownType in value)
                {
                    RegisterKnownType(knownType);
                }
            }
        }
    }

    private bool AllowTypeDiscriminator(Type type)
    {
        if (type == typeof(Stack) ||
            type == typeof(Queue) ||
            type == typeof(DBNull) ||
            type == typeof(Version) ||
            type == typeof(StringDictionary) ||
            type == typeof(BitArray))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            var defType = type.GetGenericTypeDefinition();

            if (defType == typeof(KeyValuePair<,>) ||
                defType == typeof(ArraySegment<>))
            {
                return true;
            }

            if (defType == typeof(Stack<>) ||
                defType == typeof(Queue<>) ||
                defType == typeof(ReadOnlyObservableCollection<>) ||
                defType == typeof(ReadOnlyDictionary<,>) ||
                defType == typeof(ReadOnlyCollection<>))
            {
                return true;
            }
        }

        if (s_arrayConverter.CanConvert(type))
        {
            return false;
        }

        if (s_genericCollectionConverter.CanConvert(type))
        {
            return false;
        }

        if (s_collectionConverter.CanConvert(type))
        {
            return false;
        }

        return true;
    }

    protected override DataContract ResolveContract(Type type)
    {
        var dataProps = new List<DataProperty>();

        var depth = 0;
        var declType = type;

        var isContract = false;
        var isSerialize = false;

        while (declType != null)
        {
            isContract = declType.IsDefined(typeof(DataContractAttribute), false);
            isSerialize = declType.IsDefined(typeof(SerializableAttribute), false);

            foreach (var prop in declType.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                                                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var getMethod = prop.GetGetMethod();
                if (getMethod != null)
                {
                    if (getMethod.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    if (getMethod.GetBaseDefinition().DeclaringType != declType)
                    {
                        continue;
                    }
                }

                var dataProp = CreateDataProperty(prop, depth, isContract, isSerialize);
                if (dataProp != null)
                {
                    dataProps.Add(dataProp);
                }
            }

            if (IncludeFields)
            {
                foreach (var field in declType.GetFields(BindingFlags.Public | BindingFlags.Instance |
                                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var dataProp = CreateDataProperty(field, depth, isContract, isSerialize);
                    if (dataProp != null)
                    {
                        dataProps.Add(dataProp);
                    }
                }
            }

            declType = declType.BaseType;
            depth++;
        }

        var onSerializing = ConverterHelper.GetAnnotatedMethods<OnSerializingAttribute>(type);
        var onSerialized = ConverterHelper.GetAnnotatedMethods<OnSerializedAttribute>(type);
        var onDeserializing = ConverterHelper.GetAnnotatedMethods<OnDeserializingAttribute>(type);
        var onDeserialized = ConverterHelper.GetAnnotatedMethods<OnDeserializedAttribute>(type);
        var alwaysWriteTypeDiscriminator = false;
        var isPolymorphic = false;

        if (AllowTypeDiscriminator(type))
        {
            if (EmitTypeInformation == EmitTypeInformation.Always)
            {
                alwaysWriteTypeDiscriminator = true;
            }

            if (EmitTypeInformation != EmitTypeInformation.Never)
            {
                isPolymorphic = true;
            }
        }

        var dataContract = new DataContract(type, this)
        {
            Properties = dataProps.OrderBy(v => v, Comparer.Instance).ToList(),
            Constructor = GetConstructor(type),
            OnDeserializedCallbacks = onDeserialized,
            OnSerializedCallbacks = onSerialized,
            OnDeserializingCallbacks = onDeserializing,
            OnSerializingCallbacks = onSerializing,
            TypeDiscriminatorPropertyName = DataContractTypeDiscriminatorPropertyName,
            AlwaysWriteTypeDiscriminator = alwaysWriteTypeDiscriminator,
            IsPolymorphic = isPolymorphic,
            TypeDiscriminator = new TypeDiscriminator(new Utf8Symbol(GetDataContractTypeName(type)))
        };

        return dataContract;
    }

    protected override DataContract ResolveContract(DataContract contract, ReadOnlySpan<byte> typeName)
    {
        Type? type = null;

        lock (_knownTypeDict)
        {
            var lookup =
                _knownTypeDict.GetAlternateLookup<ReadOnlySpan<byte>>();

            lookup.TryGetValue(typeName, out type);
        }

        if (type == null)
        {
            return contract;
        }

        return GetContract(type);
    }

    protected override DataConverter ResolveConverter(Type type)
    {
        RegisterKnownType(type);

        foreach (var converter in Converters)
        {
            if (converter.CanConvert(type))
            {
                return converter;
            }
        }

        if (_extensibleDataObjectConverter != null && _extensibleDataObjectConverter.CanConvert(type))
        {
            return _extensibleDataObjectConverter;
        }

        if (type == typeof(object) && UnknownObjectTypeHandling == UnknownObjectTypeHandling.Default)
        {
            return new ObjectConverter(this);
        }

        if (type == typeof(TimeSpan))
        {
            return new TimeSpanConverter();
        }

        if (type == typeof(Uri))
        {
            return new BuiltInTypeConverter.UriConverter(false);
        }

        if (type == typeof(DateTimeOffset))
        {
            return _dateTimeFormatConverter != null
                ? new BuiltInTypeConverter.DateTimeOffsetConverter(this, true, _dateTimeFormatConverter)
                : new BuiltInTypeConverter.DateTimeOffsetConverter(this, true);
        }

        if (type == typeof(DateTime) && _dateTimeFormatConverter != null)
        {
            return _dateTimeFormatConverter;
        }

        if (type == typeof(Half) ||
            type == typeof(Int128) ||
            type == typeof(UInt128))
        {
            return s_valueTypeConverter;
        }

        if (type == typeof(Stack) ||
            type == typeof(Queue) ||
            type == typeof(DBNull) ||
            type == typeof(Version) ||
            type == typeof(StringDictionary) ||
            type == typeof(BitArray))
        {
            return s_classTypeConverter;
        }

        if (type.IsGenericType)
        {
            var defType = type.GetGenericTypeDefinition();

            if (defType == typeof(KeyValuePair<,>) ||
                defType == typeof(ArraySegment<>))
            {
                return s_valueTypeConverter;
            }

            if (defType == typeof(Stack<>) ||
                defType == typeof(Queue<>) ||
                defType == typeof(ReadOnlyObservableCollection<>) ||
                defType == typeof(ReadOnlyDictionary<,>) ||
                defType == typeof(ReadOnlyCollection<>))
            {
                return s_classTypeConverter;
            }
        }

        foreach (var converter in DefaultConverters)
        {
            if (converter.CanConvert(type))
            {
                return converter;
            }
        }

        throw new NotSupportedException();
    }

    private void RegisterKnownType(Type type)
    {
        if (type == null)
        {
            return;
        }

        var name = Utf8Helper.GetUtf8String(GetDataContractTypeName(type));

        lock (_knownTypeDict)
        {
            if (!_knownTypeDict.TryAdd(name, type))
            {
                return;
            }
        }

        foreach (var knownTypeAttribute in type.GetCustomAttributes<KnownTypeAttribute>())
        {
            if (!string.IsNullOrEmpty(knownTypeAttribute.MethodName))
            {
                var methodInfo = type.GetMethod(knownTypeAttribute.MethodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

                var types = methodInfo?.Invoke(null, null) as IEnumerable<Type>;

                if (types != null)
                {
                    foreach (var knownType in types)
                    {
                        RegisterKnownType(knownType);
                    }
                }
            }

            if (knownTypeAttribute.Type != null)
            {
                RegisterKnownType(knownTypeAttribute.Type);
            }
        }
    }

    private ConstructorInfo? GetConstructor(Type typeInfo)
    {
        if (typeInfo.IsDefined(typeof(DataContractAttribute), true))
        {
            return null;
        }

        var constructors = typeInfo.GetConstructors();

        foreach (var constructorInfo in constructors)
        {
            if (constructorInfo.GetParameters().Length == 0)
            {
                return constructorInfo;
            }
        }

        return null;
    }

    private DataProperty? CreateDataProperty(MemberInfo info, int depth, bool isContract, bool isSerialize)
    {
        var name = info.Name;
        if (PropertyNamingPolicy != null)
        {
            name = PropertyNamingPolicy.ConvertName(name);
        }

        if (isContract)
        {
            var dataMember = info.GetCustomAttribute<DataMemberAttribute>(true);
            if (dataMember != null)
            {
                if (dataMember.Name != null)
                {
                    name = dataMember.Name;
                }

                var defaultValueHandling = DefaultValueHandling;

                if (!dataMember.EmitDefaultValue)
                {
                    defaultValueHandling |= DefaultValueHandling.Ignore;
                }

                var prop = new DataProperty(info)
                {
                    Name = name,
                    ReferenceLoopHandling = ReferenceLoopHandling,
                    NullValueHandling = NullValueHandling,
                    NumberHandling = NumberHandling,
                    DefaultValueHandling = defaultValueHandling,
                    ObjectCreationHandling = ObjectCreationHandling,
                    Order = dataMember.Order,
                    IsRequired = dataMember.IsRequired,
                    Depth = depth
                };

                if (prop.Info is FieldInfo fieldInfo && fieldInfo.IsInitOnly)
                {
                    prop = prop with { Writable = true };
                }

                return prop;
            }

            return null;
        }

        if (isSerialize)
        {
            if (info.IsDefined(typeof(NonSerializedAttribute)))
            {
                return null;
            }

            if (info is PropertyInfo)
            {
                return null;
            }

            var prop = new DataProperty(info)
            {
                Name = name,
                ReferenceLoopHandling = ReferenceLoopHandling,
                NullValueHandling = NullValueHandling,
                NumberHandling = NumberHandling,
                DefaultValueHandling = DefaultValueHandling,
                ObjectCreationHandling = ObjectCreationHandling,
                Depth = depth,
                Writable = true
            };

            return prop;
        }
        else
        {
            if (info.IsDefined(typeof(IgnoreDataMemberAttribute)))
            {
                return null;
            }

            var prop = new DataProperty(info)
            {
                NullValueHandling = NullValueHandling,
                ReferenceLoopHandling = ReferenceLoopHandling,
                NumberHandling = NumberHandling,
                DefaultValueHandling = DefaultValueHandling,
                ObjectCreationHandling = ObjectCreationHandling,
                Depth = depth
            };

            if (!prop.IsPublic)
            {
                return null;
            }

            if (info is PropertyInfo propInfo)
            {
                if (propInfo.SetMethod != null)
                {
                    if ((propInfo.SetMethod.Attributes & MethodAttributes.MemberAccessMask) !=
                        MethodAttributes.Public)
                    {
                        return null;
                    }
                }
            }

            if (IsInvalidDataContractCollection(prop.PropertyType))
            {
                return null;
            }

            if (prop.IsReadOnly)
            {
                if (info is FieldInfo fieldInfo)
                {
                    if ((fieldInfo.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) != 0)
                    {
                        return null;
                    }
                }

                if (prop.PropertyType == typeof(string))
                {
                    return null;
                }

                if (!prop.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                {
                    return null;
                }

                if (prop.PropertyType.IsGenericType)
                {
                    var def = prop.PropertyType.GetGenericTypeDefinition();
                    if (def == typeof(IReadOnlyCollection<>) ||
                        def == typeof(IReadOnlyList<>) ||
                        def == typeof(IReadOnlySet<>) ||
                        def == typeof(IReadOnlyDictionary<,>) ||
                        def == typeof(ISet<>))
                    {
                        return null;
                    }
                }
            }

            if (prop.IsReadOnly)
            {
                if (info is FieldInfo && IgnoreReadOnlyFields)
                {
                    return null;
                }

                if (info is PropertyInfo && IgnoreReadOnlyProperties)
                {
                    return null;
                }
            }

            return prop;
        }
    }

    private bool IsInvalidDataContractCollection(Type type)
    {
        if (type == typeof(string))
        {
            return false;
        }

        if (type.IsInterface)
        {
            return false;
        }

        if (!type.IsAssignableTo(typeof(IEnumerable)))
        {
            return false;
        }

        if (type == typeof(StringDictionary))
        {
            return false;
        }

        bool IsValidCollection(Type inf)
        {
            if (inf.IsGenericType)
            {
                var defType = inf.GetGenericTypeDefinition();
                if (defType == typeof(ICollection<>) ||
                    defType == typeof(IReadOnlyCollection<>) ||
                    defType == typeof(ISet<>) ||
                    defType == typeof(IReadOnlySet<>) ||
                    defType == typeof(IDictionary<,>) ||
                    defType == typeof(IReadOnlyDictionary<,>))
                {
                    return true;
                }
            }
            else
            {
                if (inf == typeof(ICollection) ||
                    inf == typeof(IDictionary))
                {
                    return true;
                }
            }

            return false;
        }

        if (IsValidCollection(type))
        {
            return false;
        }

        foreach (var inf in type.GetInterfaces())
        {
            if (IsValidCollection(inf))
            {
                return false;
            }
        }

        return true;
    }


    private static string GetDataContractTypeName(Type type)
    {
        var qns = string.Empty;

        var attr = type.GetCustomAttribute<DataContractAttribute>();
        if (attr != null)
        {
            if (attr.Namespace != null)
            {
                if (attr.Namespace.Length != 0)
                {
                    qns = ":" + attr.Namespace;
                }
            }
            else
            {
                qns = ":#" + type.Namespace;
            }

            if (!string.IsNullOrEmpty(attr.Name))
            {
                var ss = new StringBuilder();

                foreach (var c in attr.Name)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        ss.Append(c);
                    }
                    else
                    {
                        ss.AppendFormat("_x{0:X4}_", (int)c);
                    }
                }

                return ss + qns;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                qns = ":#" + type.Namespace;
            }
        }

        var typeName = s_exporter.GetSchemaTypeName(type);

        return typeName.Name + qns;
    }

    private class Comparer : IComparer<DataProperty>
    {
        public static readonly Comparer Instance = new();

        public int Compare(DataProperty? left, DataProperty? right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null || right == null)
            {
                throw new ArgumentNullException();
            }

            if (left.Depth != right.Depth)
            {
                return right.Depth - left.Depth;
            }

            if (left.Order != right.Order)
            {
                return Math.Sign(left.Order - right.Order);
            }

            if (left.Name == right.Name)
            {
                throw new InvalidOperationException(left.Name);
            }

            var af = left.Name.StartsWith('<') ? 1 : 0;
            var bf = right.Name.StartsWith('<') ? 1 : 0;

            if (af != bf)
            {
                return af - bf;
            }

            return string.CompareOrdinal(EncodeName(left.Name), EncodeName(right.Name));
        }

        private static string EncodeName(string name)
        {
            return XmlConvert.EncodeLocalName(name) ?? name;
        }
    }

    private sealed class ByteArrayComparer :
        IEqualityComparer<byte[]>,
        IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        private ByteArrayComparer()
        {
        }

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other)
        {
            return alternate.SequenceEqual(other);
        }

        public int GetHashCode(ReadOnlySpan<byte> alternate)
        {
            var hash = new HashCode();
            hash.AddBytes(alternate);
            return hash.ToHashCode();
        }

        public byte[] Create(ReadOnlySpan<byte> alternate)
        {
            return alternate.ToArray();
        }

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            return GetHashCode(obj.AsSpan());
        }
    }
}