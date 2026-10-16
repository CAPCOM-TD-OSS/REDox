// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Runtime.CompilerServices;

namespace REDox.Dynamic;

public sealed class DDynamic : DynamicObject, IDoxNode
{
    private static readonly object s_trueValue = true;
    private static readonly object s_falseValue = false;
    private readonly DContainer _container;

    internal DDynamic(DContainer container)
    {
        _container = container;
    }

    public bool IsObject => _container is DObject;

    public bool IsArray => _container is DArray;

    public int Count => _container.Count;

    public int Length => Count;

    public DElement AsElement()
    {
        return _container.AsElement();
    }

    public T? Deserialize<T>()
    {
        return _container.To<T>();
    }

    public bool IsDefined(string name)
    {
        return _container is DObject obj && obj.ContainsKey(name);
    }

    public bool IsDefined(int index)
    {
        return IsArray && index >= 0 && index < Count;
    }

    public bool Delete(string name)
    {
        var obj = _container as DObject;
        if (obj == null)
        {
            return false;
        }

        return obj.Remove(name);
    }

    public bool Delete(int index)
    {
        var arr = _container as DArray;
        if (arr == null)
        {
            return false;
        }

        if (index < 0 || index >= Count)
        {
            return false;
        }

        arr.RemoveAt(index);
        return true;
    }

    public static bool operator ==(DDynamic? left, DDynamic? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.AsElement().Equals(right.AsElement());
    }

    public static bool operator !=(DDynamic? left, DDynamic? right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        if (obj is DDynamic element)
        {
            return AsElement().Equals(element.AsElement());
        }

        return false;
    }

    public override int GetHashCode()
    {
        return AsElement().GetHashCode();
    }

    public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
    {
        if (args?.Length == 1)
        {
            if (args[0] is string name)
            {
                result = Delete(name);
                return true;
            }

            if (args[0] is int index)
            {
                result = Delete(index);
                return true;
            }
        }

        return base.TryInvoke(binder, args, out result);
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        if (args?.Length == 0)
        {
            result = IsDefined(binder.Name);
            return true;
        }

        if (_container is DObject obj && obj.TryGetPropertyValue(binder.Name, out var member))
        {
            if (args?.Length == 1)
            {
                if (args[0] is string name)
                {
                    if (member.GetToken().Type == DTokenType.Map)
                    {
                        result = member.AsObject().Remove(name);
                        return true;
                    }
                }

                if (args[0] is int index)
                {
                    if (member.GetToken().Type == DTokenType.Array)
                    {
                        var arr = member.AsArray();

                        if (index >= 0 && index < arr.Count)
                        {
                            result = true;
                            arr.RemoveAt(index);
                            return true;
                        }
                    }
                }
            }
        }

        return base.TryInvokeMember(binder, args, out result);
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        if (_container is DObject obj)
        {
            result = ToValue(obj.GetValueElement(binder.Name));
            return true;
        }

        if (_container is DArray arr && int.TryParse(binder.Name, out var index))
        {
            result = ToValue(new DElement(arr.Document, arr.GetValueId(index)));
            return true;
        }

        result = null;
        return false;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        if (_container is DObject obj)
        {
            obj.SetProperty(binder.Name, value);
            return true;
        }

        if (_container is DArray arr && int.TryParse(binder.Name, out var index))
        {
            arr.SetValueInternal(value, index);
            return true;
        }

        return false;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        if (_container is DObject obj)
        {
            result = ToValue(obj.GetValueElement((string)indexes[0]));
            return true;
        }

        if (_container is DArray arr)
        {
            result = ToValue(new DElement(arr.Document, arr.GetValueId((int)indexes[0])));
            return true;
        }

        result = null;
        return false;
    }

    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)
    {
        if (_container is DObject obj)
        {
            obj.SetProperty((string)indexes[0], value);
            return true;
        }

        if (_container is DArray arr)
        {
            var index = (int)indexes[0];
            if (index < Count)
            {
                arr.SetValueInternal(value, index);
            }
            else
            {
                arr.Add(value);
            }

            return true;
        }

        return false;
    }

    public override string? ToString()
    {
        return _container.AsElement().ToString();
    }

    private IEnumerable<object?> GetArrayEnumerator(DArray arr)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            yield return ToValue(new DElement(arr.Document, arr.GetValueId(i)));
        }
    }

    private IEnumerable<KeyValuePair<string, object?>> GetObjectEnumerator(DObject obj)
    {
        var count = obj.Count;

        for (var i = 0; i < count; i++)
        {
            var key = obj.GetPropertyName(i);

            if (key != null)
            {
                var value = new DElement(obj.Document, obj.GetValueId(i));

                yield return new KeyValuePair<string, object?>(key, ToValue(value));
            }
        }
    }

    public override bool TryConvert(ConvertBinder binder, out object? result)
    {
        if (binder.Type == typeof(DElement))
        {
            result = _container.AsElement();
            return true;
        }

        if (binder.Type == typeof(object[]))
        {
            if (_container is DArray arr)
            {
                var list = new object?[arr.Count];
                for (var i = 0; i < arr.Count; i++)
                {
                    list[i] = ToValue(new DElement(arr.Document, arr.GetValueId(i)));
                }

                result = list;
                return true;
            }

            result = null;
            return false;
        }

        if (binder.Type == typeof(IEnumerable))
        {
            if (_container is DObject obj)
            {
                result = GetObjectEnumerator(obj);
            }
            else
            {
                if (_container is DArray arr)
                {
                    result = GetArrayEnumerator(arr);
                }
                else
                {
                    result = null;
                    return false;
                }
            }

            return true;
        }

        var value = _container.AsValue();

        if (!binder.Type.IsEnum)
        {
            switch (Type.GetTypeCode(binder.Type))
            {
                case TypeCode.Boolean:
                    result = (bool)value;
                    return true;
                case TypeCode.Byte:
                    result = (byte)value;
                    return true;
                case TypeCode.UInt16:
                    result = (ushort)value;
                    return true;
                case TypeCode.UInt32:
                    result = (uint)value;
                    return true;
                case TypeCode.UInt64:
                    result = (ulong)(long)value;
                    return true;
                case TypeCode.SByte:
                    result = (sbyte)value;
                    return true;
                case TypeCode.Int16:
                    result = (short)value;
                    return true;
                case TypeCode.Int32:
                    result = (int)value;
                    return true;
                case TypeCode.Int64:
                    result = (long)value;
                    return true;
                case TypeCode.Single:
                    result = (float)value;
                    return true;
                case TypeCode.Double:
                    result = (double)value;
                    return true;
                case TypeCode.String:
                    result = (string?)value;
                    return true;
                case TypeCode.Char:
                    result = (char)value;
                    return true;
                case TypeCode.DateTime:
                    result = (DateTime)value;
                    return true;
                case TypeCode.Decimal:
                    result = (decimal)value;
                    return true;
            }
        }

        if (binder.Type == typeof(byte[]))
        {
            result = ((ReadOnlySpan<byte>)value).ToArray();
            return true;
        }

        if (binder.Type == typeof(DateTimeOffset))
        {
            result = (DateTimeOffset)value;
            return true;
        }

        result = Serializer.DeserializeInternal(value.AsElement(), binder.Type);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static object? ToValue(DElement value)
    {
        var token = value.Token;

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Map)
            {
                return value.AsObject().AsDynamic();
            }

            return value.AsArray().AsDynamic();
        }

        var kind = token.Kind;

        if (kind.IsNumeric)
        {
            return (double)value.AsValue();
        }

        if (kind.IsStringEncoded)
        {
            return (string?)value.AsValue();
        }

        if (kind == DTokenKind.Boolean)
        {
            return (bool)value.AsValue() ? s_trueValue : s_falseValue;
        }

        return null;
    }
}