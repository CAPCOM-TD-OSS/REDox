// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

static class ConverterHelper
{
    public delegate T ConstructDelegate<out T>(in DataReader reader, DataConverter[] converters, uint[] tokens);

    public delegate T DefaultConstructDelegate<out T>();

    public delegate void ReadDelegate<T>(in DataReader reader, DataConverter converter, T instance, uint tokenId);

    public delegate void ReadRefDelegate<T>(in DataReader reader, DataConverter converter, ref T instance,
        uint tokenId);

    public delegate void StreamingEventDelegate<T>(T instance, StreamingContext context);

    public delegate void StreamingEventRefDelegate<T>(ref T instance, StreamingContext context);

    public delegate void WriteDelegate<T>(DataWriter writer, DataConverter converter, T instance);

    public delegate void WriteRefDelegate<T>(DataWriter writer, DataConverter converter, ref T instance);

    internal const string DynamicCode =
        "This helper generates delegates or generic handlers for dynamic converters. Use the non-generic converter when dynamic code is unavailable.";

    private const string ContractMetadata =
        "The application must preserve reflection metadata for contract members, property types, and base types used by the converters.";

    private static readonly MethodInfo s_dataReaderIsNullMethod =
        typeof(DataReader).GetMethod(nameof(DataReader.IsNullToken))!;

    private static readonly MethodInfo s_objectEqualsMethod =
        typeof(object).GetMethod("Equals", BindingFlags.Instance | BindingFlags.Public)!;

    private static readonly MethodInfo s_dataWriterWriteSymbolMethod =
        typeof(DataWriter).GetMethod(nameof(DataWriter.WriteSymbol), new[] { typeof(Utf8Symbol), typeof(SymbolKind) })!;

    private static readonly MethodInfo s_dataWriterContainsReferenceMethod =
        typeof(DataWriter).GetMethod(nameof(DataWriter.IsCycleReference))!;

    private static readonly MethodInfo s_dataWriterWriteNullMethod =
        typeof(DataWriter).GetMethod(nameof(DataWriter.WriteNull), Type.EmptyTypes)!;

    private static readonly MethodInfo s_createReferenceLoopExceptionMethod =
        typeof(ConverterHelper).GetMethod(nameof(CreateReferenceLoopException),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Exception CreateReferenceLoopException(object member)
    {
        return new SerializationException(SerializationError.ReferenceLoopDetected)
        {
            Member = member
        };
    }

    public static DataConverter CreateConverter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type converterType,
        SerializerSettings settings)
    {
        return (DataConverter)Activator.CreateInstance(converterType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object[] { settings },
            null)!;
    }

    public static DataConverter CreateCollectionConverter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type converterType,
        DataConverter? itemConverter,
        SerializerSettings settings)
    {
        return (DataConverter)Activator.CreateInstance(converterType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object?[] { itemConverter, settings },
            null)!;
    }

    public static DataConverter CreateConverter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type converterType,
        SerializerSettings settings,
        DataConverterFactory factory)
    {
        return (DataConverter)Activator.CreateInstance(converterType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object[] { settings, factory },
            null)!;
    }

    public static DataConverter<T> CreateItemConverter<T>(DataConverter? itemConverter, SerializerSettings settings)
    {
        if (itemConverter != null)
        {
            if (!itemConverter.CanConvert(typeof(T)))
            {
                throw new NotSupportedException();
            }

            if (itemConverter is DataConverterFactory factory)
            {
                return (DataConverter<T>)factory.CreateConverter(typeof(T), settings);
            }

            return (DataConverter<T>)itemConverter;
        }

        return (DataConverter<T>)settings.GetConverter(typeof(T));
    }

    [RequiresDynamicCode(DynamicCode)]
    public static DefaultConstructDelegate<T> CreateDefaultConstructFunction<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        T>()
    {
        return Expression.Lambda<DefaultConstructDelegate<T>>(Expression.New(typeof(T))).Compile();
    }

    [RequiresDynamicCode(DynamicCode)]
    public static ConstructDelegate<T> CreateConstructFunction<T>(ConstructorInfo ctor)
    {
        var reader = Expression.Parameter(typeof(DataReader).MakeByRefType(), "reader");
        var converters = Expression.Parameter(typeof(DataConverter[]), "converters");
        var tokens = Expression.Parameter(typeof(uint[]), "tokens");

        var parameters = ctor.GetParameters();

        var expressions = new Expression[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var type = param.ParameterType;

            var converterType = typeof(DataConverter<>).MakeGenericType(type);
            var readDataMethodInfo = converterType.GetMethod(nameof(DataConverter<>.Read));

            var converter = Expression.ArrayAccess(converters, Expression.Constant(i));
            var tokenId = Expression.ArrayAccess(tokens, Expression.Constant(i));

            expressions[i] = Expression.Call(Expression.Convert(converter, converterType), readDataMethodInfo!,
                reader,
                tokenId, Expression.Default(type));
        }

        return Expression
            .Lambda<ConstructDelegate<T>>(Expression.New(ctor, expressions), reader, converters, tokens)
            .Compile();
    }

    [RequiresDynamicCode(DynamicCode)]
    public static Func<U[], T> CreateReadOnlyCollectionFunction<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        T, U>()
    {
        var target = Expression.Parameter(typeof(U[]), "list");
        var ctor = typeof(T).GetConstructor(new[] { typeof(U[]) });

        return Expression.Lambda<Func<U[], T>>(
            Expression.New(ctor!, target), target
        ).Compile();
    }

    private static Expression FieldOrPropertyExpression(Expression value, MemberInfo memberInfo)
    {
        if (memberInfo is PropertyInfo propInfo)
        {
            if (propInfo.GetMethod != null)
            {
                return Expression.Property(propInfo.GetMethod.IsStatic ? null : value, propInfo);
            }

            return Expression.Property(propInfo.SetMethod!.IsStatic ? null : value, propInfo);
        }

        if (memberInfo is FieldInfo fieldInfo)
        {
            return Expression.Field(fieldInfo.IsStatic ? null : value, fieldInfo);
        }

        throw new NotSupportedException();
    }

    [RequiresDynamicCode(DynamicCode)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = ContractMetadata)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ContractMetadata)]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(DataConverter<>))]
    public static TDelegate CreateReadDelegate<TDelegate, T>(DataProperty member, DataConverter dataConverter,
        bool existingValue)
    {
        var memberInfo = member.Info;
        var isValueType = memberInfo.DeclaringType!.IsValueType;

        var reader = Expression.Parameter(typeof(DataReader).MakeByRefType());
        var converter = Expression.Parameter(typeof(DataConverter));
        var value = Expression.Parameter(isValueType ? typeof(T).MakeByRefType() : typeof(T));
        var tokenId = Expression.Parameter(typeof(uint));

        var left = FieldOrPropertyExpression(value, memberInfo);

        var type = memberInfo is PropertyInfo
            ? ((PropertyInfo)memberInfo).PropertyType
            : ((FieldInfo)memberInfo).FieldType;

//            var converterType = typeof(DataConverter<>).MakeGenericType(type);
        var converterType = dataConverter.GetType();
        var convType = dataConverter.TargetType;

        var readDataMethodInfo = converterType.GetMethod(nameof(DataConverter<>.Read))!;

        var right = Expression.Convert(
            Expression.Call(Expression.Convert(converter, converterType), readDataMethodInfo, reader, tokenId,
                existingValue ? Expression.Convert(left, convType) : Expression.Default(convType)), type);

        Expression? codeBlock;

        var defaultVal = (Expression)Expression.Default(type);

        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
        {
            defaultVal = Expression.Constant(null);
        }

        if (member.DefaultValue != null)
        {
            defaultVal = Expression.Constant(member.DefaultValue);
        }

        Expression readData;

        if (member.Writable && (member.DefaultValueHandling & DefaultValueHandling.IgnoreRead) != 0)
        {
            // The member keeps its current value when the deserialized value equals the default value.
            var temp = Expression.Variable(type);

            readData = Expression.Block(new[] { temp },
                Expression.Assign(temp, right),
                Expression.IfThen(CreateNotDefaultCondition(temp, defaultVal, type, nullableType),
                    Expression.Assign(left, temp)));
        }
        else
        {
            readData = member.Writable ? Expression.Assign(left, right) : right;
        }


        if ((member.NullValueHandling & NullValueHandling.IgnoreRead) != 0)
        {
            codeBlock = Expression.IfThen(
                Expression.Equal(Expression.Call(reader, s_dataReaderIsNullMethod, tokenId),
                    Expression.Constant(false)),
                readData);
        }
        else
        {
            codeBlock = readData;
        }

        if (member.Writable && (member.DefaultValueHandling & DefaultValueHandling.Populate) != 0)
        {
            codeBlock = Expression.IfThenElse(Expression.Equal(tokenId, Expression.Constant(0U)),
                Expression.Assign(left, Expression.Convert(defaultVal, type)), codeBlock);
        }

        var lambda =
            Expression.Lambda<TDelegate>(
                codeBlock
                , reader, converter, value, tokenId);

        return lambda.Compile();
    }

    private static Expression CreateNotDefaultCondition(Expression value, Expression defaultVal,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type type,
        Type? nullableType)
    {
        if (defaultVal.Type != type)
        {
            defaultVal = Expression.Convert(defaultVal, type);
        }

        if (type.IsValueType && !type.IsPrimitive && nullableType == null)
        {
            var eqMethod = type.GetMethod("Equals", BindingFlags.Instance | BindingFlags.Public,
                new[] { type });

            if (eqMethod != null && eqMethod.GetParameters()[0].ParameterType == type)
            {
                return Expression.Not(Expression.Call(value, eqMethod, defaultVal));
            }

            return Expression.Not(Expression.Call(value, s_objectEqualsMethod,
                Expression.Convert(defaultVal, typeof(object))));
        }

        return Expression.NotEqual(value, defaultVal);
    }

    [RequiresDynamicCode(DynamicCode)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ContractMetadata)]
    public static TDelegate CreateWriteDelegate<TDelegate, T>(
        DataProperty member,
        DataConverter dataConverter,
        Utf8Symbol? propertyName)
    {
        var memberInfo = member.Info;
        var isValueType = memberInfo.DeclaringType!.IsValueType;

        var writer = Expression.Parameter(typeof(DataWriter), "writer");
        var converter = Expression.Parameter(typeof(DataConverter), "converter");
        var value = Expression.Parameter(
            isValueType ? typeof(T).MakeByRefType() : typeof(T),
            "value");

        // obj.Property / obj.Field
        var right = FieldOrPropertyExpression(value, memberInfo);

        var type = memberInfo switch
        {
            PropertyInfo propertyInfo => propertyInfo.PropertyType,
            FieldInfo fieldInfo => fieldInfo.FieldType,
            _ => throw new NotSupportedException()
        };

        var converterType = typeof(DataConverter<>).MakeGenericType(type);

        var memberValue = Expression.Variable(type, "memberValue");

        Expression writeData;

        if (propertyName != null)
        {
            var directWriteMethod =
                BuiltInTypeConverter.GetWritePropertyMethod(dataConverter);

            if (directWriteMethod != null)
            {
                writeData = Expression.Call(
                    writer,
                    directWriteMethod,
                    Expression.Constant(propertyName, typeof(Utf8Symbol)),
                    memberValue);
            }
            else
            {
                var writeDataMethodInfo =
                    converterType.GetMethod(nameof(DataConverter<>.Write))!;

                var typedConverter =
                    Expression.Convert(converter, converterType);

                writeData = Expression.Block(
                    Expression.Call(
                        writer,
                        s_dataWriterWriteSymbolMethod,
                        Expression.Constant(propertyName, typeof(Utf8Symbol)),
                        Expression.Constant(SymbolKind.Identifier)),
                    Expression.Call(
                        typedConverter,
                        writeDataMethodInfo,
                        writer,
                        memberValue));
            }
        }
        else
        {
            var writeDataMethodInfo =
                converterType.GetMethod(nameof(DataConverter<>.Write))!;

            writeData = Expression.Call(
                Expression.Convert(converter, converterType),
                writeDataMethodInfo,
                writer,
                memberValue);
        }

        var ignoreCheck = false;

        Expression defaultVal = Expression.Default(type);

        var nullableType = Nullable.GetUnderlyingType(type);

        if (nullableType != null)
        {
            defaultVal = Expression.Constant(null, type);
        }

        if ((member.DefaultValueHandling & DefaultValueHandling.Ignore) != 0)
        {
            ignoreCheck = true;
        }

        if ((member.NullValueHandling & NullValueHandling.IgnoreWrite) != 0)
        {
            if (!type.IsValueType || nullableType != null)
            {
                ignoreCheck = true;
            }
        }

        if (member.DefaultValue != null)
        {
            defaultVal = Expression.Constant(member.DefaultValue, type);
        }

        Expression? valueCondition = null;

        if (ignoreCheck)
        {
            valueCondition = CreateNotDefaultCondition(
                memberValue,
                defaultVal,
                type,
                nullableType);
        }

        Expression writeMember;

        if (valueCondition != null)
        {
            writeMember = Expression.IfThen(
                valueCondition,
                writeData);
        }
        else
        {
            writeMember = writeData;
        }

        if (!type.IsValueType && member.ReferenceLoopHandling != ReferenceLoopHandling.Serialize)
        {
            var containsReference = Expression.AndAlso(
                Expression.NotEqual(memberValue, Expression.Constant(null, type)),
                Expression.Call(
                    writer,
                    s_dataWriterContainsReferenceMethod,
                    Expression.Convert(memberValue, typeof(object))));

            switch (member.ReferenceLoopHandling)
            {
                case ReferenceLoopHandling.Ignore:
                    writeMember = Expression.IfThen(
                        Expression.Not(containsReference),
                        writeMember);
                    break;

                case ReferenceLoopHandling.Null:
                    Expression writeNull = Expression.Call(writer, s_dataWriterWriteNullMethod);

                    if (propertyName != null)
                    {
                        writeNull = Expression.Block(
                            Expression.Call(
                                writer,
                                s_dataWriterWriteSymbolMethod,
                                Expression.Constant(propertyName, typeof(Utf8Symbol)),
                                Expression.Constant(SymbolKind.Identifier)),
                            writeNull);
                    }

                    writeMember = Expression.IfThenElse(
                        containsReference,
                        writeNull,
                        writeMember);
                    break;

                case ReferenceLoopHandling.Error:
                    writeMember = Expression.Block(
                        Expression.IfThen(
                            containsReference,
                            Expression.Throw(Expression.Call(
                                s_createReferenceLoopExceptionMethod,
                                Expression.Constant(member, typeof(object))))),
                        writeMember);
                    break;
            }
        }

        var getValueAndWrite = Expression.Block(
            Expression.Assign(
                memberValue,
                right),
            writeMember);

        Expression body;

        if (member.ConditionalMethod != null)
        {
            var conditionCall = Expression.Call(
                value,
                member.ConditionalMethod);

            body = Expression.IfThen(
                conditionCall,
                getValueAndWrite);
        }
        else
        {
            body = getValueAndWrite;
        }

        var codeBlock = Expression.Block(
            new[] { memberValue },
            body);

        var lambda = Expression.Lambda<TDelegate>(
            codeBlock,
            writer,
            converter,
            value);

        return lambda.Compile();
    }

    [RequiresDynamicCode(DynamicCode)]
    public static StreamingEventDelegate<T> CreateStreamingEventDelegate<T>(MethodInfo methodInfo)
    {
        var target = Expression.Parameter(typeof(T), "target");
        var context = Expression.Parameter(typeof(StreamingContext), "context");

        var call = Expression.Call(Expression.Convert(target, methodInfo.DeclaringType!), methodInfo, context);

        var lambda = Expression.Lambda<StreamingEventDelegate<T>>(call, target, context);

        return lambda.Compile();
    }

    [RequiresDynamicCode(DynamicCode)]
    public static StreamingEventRefDelegate<T> CreateStreamingEventRefDelegate<T>(MethodInfo methodInfo)
    {
        var target = Expression.Parameter(typeof(T).MakeByRefType(), "target");
        var context = Expression.Parameter(typeof(StreamingContext), "context");

        var call = Expression.Call(target, methodInfo, context);

        var lambda = Expression.Lambda<StreamingEventRefDelegate<T>>(call, target, context);

        return lambda.Compile();
    }

    // Callback discovery is also used by the reflection converter in Native AOT.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = ContractMetadata)]
    public static MethodInfo[] GetAnnotatedMethods<T>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods |
                                    DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type type) where T : Attribute
    {
        var declType = type;
        List<MethodInfo>? methodList = null;

        while (declType != null)
        {
            foreach (var method in declType.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsVirtual)
                {
                    continue;
                }

                if (method.IsDefined(typeof(T)))
                {
                    if (methodList == null)
                    {
                        methodList = new List<MethodInfo>();
                    }

                    methodList.Add(method);
                }
            }

            declType = declType.BaseType;
        }

        if (methodList != null)
        {
            return methodList.ToArray();
        }

        return [];
    }


    /*
    public static void CreateCallBackMethod(Type typeInfo, DataContract contract)
    {
        var declType = typeInfo;
        List<MethodInfo>? onSerializing = null;
        List<MethodInfo>? onSerialized = null;
        List<MethodInfo>? onDeserializing = null;
        List<MethodInfo>? onDeserialized = null;

        while (declType != null)
        {
            foreach (var method in declType.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsVirtual)
                {
                    continue;
                }

                if (method.IsDefined(typeof(OnSerializingAttribute)))
                {
                    if (onSerializing == null)
                    {
                        onSerializing = new List<MethodInfo>();
                    }

                    onSerializing.Add(method);
                }

                if (method.IsDefined(typeof(OnSerializedAttribute)))
                {
                    if (onSerialized == null)
                    {
                        onSerialized = new List<MethodInfo>();
                    }

                    onSerialized.Add(method);
                }

                if (method.IsDefined(typeof(OnDeserializingAttribute)))
                {
                    if (onDeserializing == null)
                    {
                        onDeserializing = new List<MethodInfo>();
                    }

                    onDeserializing.Add(method);
                }

                if (method.IsDefined(typeof(OnDeserializedAttribute)))
                {
                    if (onDeserialized == null)
                    {
                        onDeserialized = new List<MethodInfo>();
                    }

                    onDeserialized.Add(method);
                }
            }

            declType = declType.BaseType;
        }

        if (onSerialized != null)
        {
            contract.OnSerializedCallbacks = onSerialized.ToArray();
        }

        if (onSerializing != null)
        {
            contract.OnSerializingCallbacks = onSerializing.ToArray();
        }

        if (onDeserialized != null)
        {
            contract.OnDeserializedCallbacks = onDeserialized.ToArray();
        }

        if (onDeserializing != null)
        {
            contract.OnDeserializingCallbacks = onDeserializing.ToArray();
        }
    }
    */

    /// <summary>
    ///     Reads unmapped members into an extension data dictionary and writes its entries
    ///     as properties of the enclosing object.
    /// </summary>
    internal abstract class ExtensionDataHandler<T> where T : class
    {
        protected ExtensionDataHandler(DataProperty info)
        {
            Info = info;
        }

        public DataProperty Info { get; }

        [RequiresDynamicCode(DynamicCode)]
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = ContractMetadata)]
        [UnconditionalSuppressMessage("Trimming", "IL2076", Justification = ContractMetadata)]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ExtensionDataHandler<,,>))]
        public static ExtensionDataHandler<T>? Create(DataProperty? member, SerializerSettings settings)
        {
            if (member == null || member.PropertyType.IsValueType)
            {
                return null;
            }

            var dictType = member.PropertyType;
            Type? valueType = null;

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
                    valueType = iface.GetGenericArguments()[1];
                    break;
                }
            }

            if (valueType == null)
            {
                return null;
            }

            return (ExtensionDataHandler<T>)Activator.CreateInstance(
                typeof(ExtensionDataHandler<,,>).MakeGenericType(typeof(T), dictType, valueType),
                member, settings)!;
        }

        public abstract void Read(in DataReader reader, T instance, uint keyId, uint valueId);

        public abstract void Write(DataWriter writer, T instance);
    }

    private sealed class ExtensionDataHandler<T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        TDictionary,
        TValue> : ExtensionDataHandler<T>
        where T : class
        where TDictionary : class, IDictionary<string, TValue>
    {
        private readonly DataConverter _converter;
        private readonly Func<T, TDictionary?>? _getter;
        private readonly Action<T, TDictionary>? _setter;

        [RequiresDynamicCode(DynamicCode)]
        public ExtensionDataHandler(DataProperty info, SerializerSettings settings) : base(info)
        {
            _converter = settings.GetConverter(typeof(TValue));

            var target = Expression.Parameter(typeof(T), "target");
            var member = info.Info is PropertyInfo propertyInfo
                ? Expression.Property(target, propertyInfo)
                : Expression.Field(target, (FieldInfo)info.Info);

            if (info.Readable)
            {
                _getter = Expression.Lambda<Func<T, TDictionary?>>(member, target).Compile();
            }

            if (info.Writable)
            {
                var value = Expression.Parameter(typeof(TDictionary), "value");
                _setter = Expression.Lambda<Action<T, TDictionary>>(Expression.Assign(member, value), target, value)
                    .Compile();
            }
        }

        public override void Read(in DataReader reader, T instance, uint keyId, uint valueId)
        {
            var dict = _getter?.Invoke(instance);

            if (dict == null)
            {
                if (_setter == null)
                {
                    return;
                }

                dict = typeof(TDictionary).IsInterface || typeof(TDictionary).IsAbstract
                    ? (TDictionary)(object)new Dictionary<string, TValue>()
                    : Activator.CreateInstance<TDictionary>();

                _setter(instance, dict);
            }

            dict[reader.ReadString(keyId)] =
                (TValue)_converter.ReadObject(reader, typeof(TValue), valueId, null)!;
        }

        public override void Write(DataWriter writer, T instance)
        {
            var dict = _getter?.Invoke(instance);

            if (dict == null)
            {
                return;
            }

            foreach (var kv in dict)
            {
                writer.WriteSymbol(kv.Key, SymbolKind.Identifier);
                _converter.WriteObject(writer, typeof(TValue), kv.Value);
            }
        }
    }
}