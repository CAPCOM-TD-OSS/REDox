// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using REDox.Serialization;
using REDox.Serialization.Converters;
using REDox.Serialization.Metadata;
using ArrayConverter = REDox.Serialization.Converters.ArrayConverter;
using CollectionConverter = REDox.Serialization.Converters.CollectionConverter;
using DataObjectAttribute = REDox.Serialization.DataObjectAttribute;

namespace REDox;

public class DoxSerializerSettings : SerializerSettings
{
    private static readonly DataConverter[] DefaultConverters = new DataConverter[]
    {
        new SpanTypeConverter(),
        new BinaryConverter(),
        new EnumTypeConverter(),
        new DoxTypeConverter(),
        new BuiltInTypeConverter(),
        new ArrayConverter(),
        new GenericCollectionConverter(),
        new CollectionConverter(),
        new SerializableTypeConverter(),
        new ValueTypeConverter(),
        new ClassTypeConverter()
    };

    public DoxSerializerSettings()
    {
        NumberHandling = NumberHandling.AllowReadingFromString;
        AllowRelaxedScalarConversion = true;
    }

    public bool IgnoreSerializableInterface { get; init; }

    public bool IgnoreSerializableAttribute { get; init; } = true;

    public bool OverrideSpecifiedNames { get; init; }

    protected override DataContract ResolveContract(Type type)
    {
        var dataProps = new List<DataProperty>();

        var depth = 0;
        var declType = type;

        var bcontract = IsDataContractType(declType);
        var bserialize = false;

        if (!IgnoreSerializableAttribute)
        {
            bserialize = declType.IsDefined(typeof(SerializableAttribute), false);
        }

        while (declType != null)
        {
            var attr = declType.GetCustomAttribute<DataObjectAttribute>();
            var namingPolicy = PropertyNamingPolicy;

            if (attr != null)
            {
                if (attr.NamingPolicyType != null)
                {
                    namingPolicy = (NamingPolicy?)Activator.CreateInstance(attr.NamingPolicyType);
                }
            }

            foreach (var prop in declType.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                                                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                                        BindingFlags.Static))
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

                var dataProp = CreateDataProperty(prop, depth, bcontract, bserialize, namingPolicy);
                if (dataProp != null)
                {
                    dataProps.Add(dataProp);
                }
            }

            if (IncludeFields)
            {
                foreach (var field in declType.GetFields(BindingFlags.Public | BindingFlags.Instance |
                                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                                         BindingFlags.Static))
                {
                    if (field.IsStatic && declType != type)
                    {
                        continue;
                    }

                    var dataProp = CreateDataProperty(field, depth, bcontract, bserialize, namingPolicy);
                    if (dataProp != null)
                    {
                        dataProps.Add(dataProp);
                    }
                }
            }

            declType = declType.BaseType;
            depth++;
        }

        var contractAttr = type.GetCustomAttribute<DataContractAttribute>();
        var isReference = false;

        if (contractAttr != null)
        {
            isReference = contractAttr.IsReference;
        }

        var constructor = GetConstructor(type);

        if (constructor != null)
        {
            foreach (var param in constructor.GetParameters())
            {
                var paramName = param.Name;

                var index = dataProps.FindIndex(prop =>
                    prop.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    dataProps[index] = dataProps[index] with { IsReadOnly = false };
                }
            }
        }

        var onSerializing = ConverterHelper.GetAnnotatedMethods<OnSerializingAttribute>(type);
        var onSerialized = ConverterHelper.GetAnnotatedMethods<OnSerializedAttribute>(type);
        var onDeserializing = ConverterHelper.GetAnnotatedMethods<OnDeserializingAttribute>(type);
        var onDeserialized = ConverterHelper.GetAnnotatedMethods<OnDeserializedAttribute>(type);

        var dataContract = new DataContract(type, this)
        {
            Properties = dataProps,
            Constructor = GetConstructor(type),
            IsReference = isReference,
            OnDeserializedCallbacks = onDeserialized,
            OnSerializedCallbacks = onSerialized,
            OnDeserializingCallbacks = onDeserializing,
            OnSerializingCallbacks = onSerializing
        };

        if (bserialize && !bcontract)
        {
            Helper.StableSort(dataProps, SerializableComparer.Instance);
        }
        else
        {
            Helper.StableSort(dataProps, Comparer.Instance);
        }


        return dataContract;
    }

    protected override DataConverter ResolveConverter(Type type)
    {
        foreach (var cnv in EnumerateConverters())
        {
            if (cnv.CanConvert(type))
            {
                return cnv;
            }
        }

        throw new InvalidOperationException();
    }

    private IEnumerable<DataConverter> EnumerateConverters()
    {
        foreach (var conv in Converters)
        {
            yield return conv;
        }

        foreach (var conv in DefaultConverters)
        {
            if (IgnoreSerializableInterface && conv is SerializableTypeConverter)
            {
                continue;
            }

            yield return conv;
        }
    }

    private ConstructorInfo? GetConstructor(Type typeInfo)
    {
        foreach (var ctor in typeInfo.GetConstructors(BindingFlags.Public | BindingFlags.Instance |
                                                      BindingFlags.NonPublic))
        {
            if (ctor.IsDefined(typeof(DataConstructorAttribute)))
            {
                return ctor;
            }
        }

        var constructors = typeInfo.GetConstructors();

        foreach (var ctor in constructors)
        {
            if (ctor.GetParameters().Length == 0)
            {
                return ctor;
            }
        }

        if (constructors.Length == 1)
        {
            return constructors[0];
        }

        return null;
    }

    private DataProperty? CreateDataProperty(MemberInfo info, int depth, bool bcontract, bool bserialize,
        NamingPolicy? namingPolicy)
    {
        if (info.IsDefined(typeof(DataIgnoreAttribute)))
        {
            return null;
        }

        var name = namingPolicy != null ? namingPolicy.ConvertName(info.Name) : info.Name;

        object? defaultVal = null;

        var dvattr = info.GetCustomAttribute<DefaultValueAttribute>();

        if (dvattr != null)
        {
            defaultVal = dvattr.Value;
        }

        DataProperty? prop;
        Type? itemConverterType = null;
        object[]? itemConverterParameters = null;
        MethodInfo? conditionalMethod = null;

        if (info.DeclaringType!.GetMethod("ShouldSerialize" + info.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is MethodInfo method)
        {
            if (method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
            {
                conditionalMethod = method;
            }
        }

        if (info.IsDefined(typeof(DataPropertyAttribute)))
        {
            var attr = info.GetCustomAttribute<DataPropertyAttribute>()!;

            if (attr.NamingPolicyType != null)
            {
                namingPolicy = Activator.CreateInstance(attr.NamingPolicyType) as NamingPolicy;

                if (namingPolicy != null)
                {
                    name = namingPolicy.ConvertName(info.Name);
                }
            }

            if (!string.IsNullOrEmpty(attr.Name))
            {
                name = attr.Name;
            }

            prop = new DataProperty(info)
            {
                Name = name,
                ReferenceLoopHandling = ReferenceLoopHandling,
                Order = attr.Order,
                DefaultValue = defaultVal,
                NullValueHandling = attr._nullValue.HasValue ? attr.NullValueHandling : NullValueHandling,
                DefaultValueHandling =
                    attr._defaultValue.HasValue ? attr.DefaultValueHandling : DefaultValueHandling,
                ObjectCreationHandling =
                    attr._objectCreation.HasValue ? attr.ObjectCreationHandling : ObjectCreationHandling,
                ConditionalMethod = conditionalMethod
            };

            itemConverterType = attr.ItemConverterType;
            itemConverterParameters = attr.ItemConverterParameters;
        }
        else
        {
            if (bcontract)
            {
                var dataMember = info.GetCustomAttribute<DataMemberAttribute>(true);

                if (dataMember == null)
                {
                    return null;
                }

                if (dataMember.Name != null)
                {
                    name = dataMember.Name;

                    if (OverrideSpecifiedNames && PropertyNamingPolicy != null)
                    {
                        name = PropertyNamingPolicy.ConvertName(name);
                    }
                }

                var defaultValueHandling = DefaultValueHandling;

                if (!dataMember.EmitDefaultValue)
                {
                    defaultValueHandling |= DefaultValueHandling.Ignore;
                }

                prop = new DataProperty(info)
                {
                    Name = name,
                    ReferenceLoopHandling = ReferenceLoopHandling,
                    NullValueHandling = NullValueHandling,
                    DefaultValueHandling = defaultValueHandling,
                    ObjectCreationHandling = ObjectCreationHandling,
                    Depth = depth,
                    DefaultValue = defaultVal,
                    Order = dataMember.Order,
                    ConditionalMethod = conditionalMethod
                };
            }
            else
            {
                if (bserialize)
                {
                    if (info.IsDefined(typeof(NonSerializedAttribute)))
                    {
                        return null;
                    }

                    if (info is PropertyInfo)
                    {
                        return null;
                    }

                    prop = new DataProperty(info)
                    {
                        Name = name,
                        ReferenceLoopHandling = ReferenceLoopHandling,
                        NullValueHandling = NullValueHandling,
                        DefaultValueHandling = DefaultValueHandling,
                        ObjectCreationHandling = ObjectCreationHandling,
                        Depth = depth,
                        Writable = true,
                        DefaultValue = defaultVal,
                        ConditionalMethod = conditionalMethod
                    };

                    if (prop.IsStatic)
                    {
                        return null;
                    }
                }
                else
                {
                    if (info.IsDefined(typeof(IgnoreDataMemberAttribute)))
                    {
                        return null;
                    }

                    prop = new DataProperty(info)
                    {
                        Name = name,
                        ReferenceLoopHandling = ReferenceLoopHandling,
                        DefaultValue = defaultVal,
                        NullValueHandling = NullValueHandling,
                        DefaultValueHandling = DefaultValueHandling,
                        ObjectCreationHandling = ObjectCreationHandling,
                        ConditionalMethod = conditionalMethod
                    };

                    if (info is PropertyInfo propInfo && propInfo.SetMethod != null)
                    {
                        if ((propInfo.SetMethod.Attributes & MethodAttributes.MemberAccessMask) !=
                            MethodAttributes.Public)
                        {
                            prop = prop with { Writable = false };
                        }
                    }

                    if (!prop.IsPublic || prop.IsStatic)
                    {
                        return null;
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
                }
            }
        }

        if (prop.PropertyType.IsByRef || prop.PropertyType.IsByRefLike)
        {
            return null;
        }

        if (info.DeclaringType != null && info.DeclaringType.IsAssignableTo(typeof(Exception)) &&
            info.Name == "TargetSite")
        {
            return null;
        }

        var dataConverter = info.GetCustomAttribute<DataConverterAttribute>(true);
        if (dataConverter != null)
        {
            prop = prop with
            {
                Converter = CreateConverter(prop.PropertyType, dataConverter.ConverterType,
                    dataConverter.ConverterParameters,
                    itemConverterType, itemConverterParameters, this)
            };
        }
        else
        {
            prop = prop with
            {
                Converter =
                CreateConverter(prop.PropertyType, null, null, itemConverterType, itemConverterParameters, this)
            };
        }

        return prop;
    }

    private static bool IsDataContractType(Type? typeInfo)
    {
        while (typeInfo != null)
        {
            if (typeInfo.IsDefined(typeof(DataContractAttribute)))
            {
                return true;
            }

            typeInfo = typeInfo.BaseType;
        }

        return false;
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

            if (left.Order != right.Order)
            {
                return Math.Sign(left.Order - right.Order);
            }

            var af = left.Info is FieldInfo ? 0 : 10;
            var bf = right.Info is FieldInfo ? 0 : 10;

            if (left.Depth > 0)
            {
                af |= left.Info is FieldInfo ainfo &&
                      (ainfo.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
                    ? 1
                    : 0;
                bf |= right.Info is FieldInfo binfo &&
                      (binfo.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
                    ? 1
                    : 0;
            }

            if (af != bf)
            {
                return af - bf;
            }

            if (left.Name == right.Name)
            {
                throw new InvalidOperationException(left.Name);
            }

            return 0;
        }
    }

    private class SerializableComparer : IComparer<DataProperty>
    {
        public static readonly SerializableComparer Instance = new();

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
                return left.Depth - right.Depth;
            }

            if (left.Order != right.Order)
            {
                return Math.Sign(left.Order - right.Order);
            }

            var af = left.Info is FieldInfo ? 0 : 10;
            var bf = right.Info is FieldInfo ? 0 : 10;

            if (left.Depth > 0)
            {
                af |= left.Info is FieldInfo ainfo &&
                      (ainfo.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
                    ? 1
                    : 0;
                bf |= right.Info is FieldInfo binfo &&
                      (binfo.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
                    ? 1
                    : 0;
            }

            if (af != bf)
            {
                return af - bf;
            }

            if (left.Name == right.Name)
            {
                throw new InvalidOperationException(left.Name);
            }

            return 0;
        }
    }
}