// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;

namespace REDox.Serialization.Converters;

class EnumConverterHelper
{
    private static readonly byte[] s_separator = ", "u8.ToArray();
    private readonly bool _allowIntegerValues;

    private readonly EnumMember[] _enumNames;
    private readonly EnumMember[] _enumValues;
    private readonly bool _isFlags;
    private readonly bool _isULong;

    public EnumConverterHelper(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        Type type,
        NamingPolicy? namingPolicy, bool allowIntegerValues,
        SerializerSettings settings)
    {
        if (namingPolicy == null)
        {
            namingPolicy = settings.DictionaryKeyPolicy;
        }

        var names = type.GetEnumNames();

        var members = new EnumMember[names.Length];

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var value = Enum.Parse(type, name);
            var member = new EnumMember();

            var fieldInfo = type.GetField(name,
                BindingFlags.Static | BindingFlags.Public);

            if (fieldInfo != null)
            {
                var enumMember = fieldInfo.GetCustomAttribute<EnumMemberAttribute>();
                if (enumMember != null)
                {
                    name = enumMember.Value;
                }
            }

            if (namingPolicy != null)
            {
                name = namingPolicy.ConvertName(name!);
            }

            member.Name = Utf8Helper.GetUtf8String(name!);

            switch (Type.GetTypeCode(type.GetEnumUnderlyingType()))
            {
                case TypeCode.Int16:
                    member.Value = (short)value;
                    break;
                case TypeCode.Int32:
                    member.Value = (int)value;
                    break;
                case TypeCode.Int64:
                    member.Value = (long)value;
                    break;
                case TypeCode.SByte:
                    member.Value = (sbyte)value;
                    break;
                case TypeCode.UInt16:
                    member.Value = (ushort)value;
                    break;
                case TypeCode.UInt32:
                    member.Value = (uint)value;
                    break;
                case TypeCode.UInt64:
                    member.Value = (long)(ulong)value;
                    break;
                case TypeCode.Byte:
                    member.Value = (byte)value;
                    break;
            }

            members[i] = member;
        }

        Array.Sort(members, (a, b) => Utf8Helper.Compare(a.Name, b.Name, false));

        var enumValues = new EnumMember[members.Length];
        Array.Copy(members, enumValues, enumValues.Length);

        Array.Sort(enumValues, (a, b) => Math.Sign(a.Value - b.Value));

        _enumNames = members;
        _enumValues = enumValues;

        _isULong = Type.GetTypeCode(type.GetEnumUnderlyingType()) == TypeCode.UInt64;
        _isFlags = type.IsDefined(typeof(FlagsAttribute));
        _allowIntegerValues = allowIntegerValues;
    }

    public long Read(in DataReader reader, uint tokenId, bool isProperty = false)
    {
        var token = reader.GetToken(tokenId);

        if (token.Kind == DTokenKind.Integer)
        {
            if (_isULong)
            {
                return (long)reader.ReadUInt64(tokenId);
            }

            return reader.ReadInt64(tokenId);
        }

        if (token.Type == DTokenType.Text)
        {
            var value = reader.ReadUtf8String(tokenId);

            if (_isFlags)
            {
                long v = 0;
                var start = 0;
                var index = 0;

                while (index < value.Length)
                {
                    if (value[index] == ',' || value[index] == ' ')
                    {
                        if (start >= 0)
                        {
                            var member = FindEnumMember(value.Slice(start, index - start), isProperty);
                            if (member != null)
                            {
                                v |= member.Value;
                            }
                        }

                        start = -1;
                    }
                    else
                    {
                        if (start < 0)
                        {
                            start = index;
                        }
                    }

                    index++;
                }

                if (start >= 0)
                {
                    var member = FindEnumMember(value.Slice(start, index - start), isProperty);
                    if (member != null)
                    {
                        v |= member.Value;
                    }
                }

                return v;
            }

            {
                var member = FindEnumMember(value, isProperty);
                if (member != null)
                {
                    return member.Value;
                }
            }
        }

        throw new InvalidCastException();
    }

    public void Write(DataWriter writer, long value, bool isProperty = false)
    {
        void WriteValue(DataWriter w, bool isProp, ReadOnlySpan<byte> val)
        {
            if (isProp)
            {
                if (w.Settings.DictionaryKeyPolicy != null)
                {
                    w.WriteString(
                        w.Settings.DictionaryKeyPolicy.ConvertName(Utf8Helper.GetUtf16String(val)));
                }
                else
                {
                    w.WriteString(val);
                }
            }
            else
            {
                w.WriteString(val);
            }
        }

        if (_isFlags)
        {
            var sb = new Helper.LocalStringBuilder(stackalloc byte[256]);

            var members = _enumValues;
            var sValue = value;
            var firstTime = true;

            for (var i = members.Length - 1; i >= 0; i--)
            {
                var member = members[i];
                var tValue = member.Value;

                if (i == 0 && tValue == 0)
                {
                    continue;
                }

                if ((sValue & tValue) == tValue)
                {
                    sValue -= tValue;

                    if (firstTime)
                    {
                        firstTime = false;
                    }
                    else
                    {
                        sb.Insert(0, s_separator);
                    }

                    sb.Insert(0, member.Name);
                }
            }

            if (sValue == 0)
            {
                if (sb.Length > 0)
                {
                    WriteValue(writer, isProperty, sb.ToSpan());
                    return;
                }

                if (members.Length > 0 && members[0].Value == 0)
                {
                    WriteValue(writer, isProperty, members[0].Name);
                    return;
                }

                value = 0;
            }
        }
        else
        {
            var member = FindEnumMember(value);
            if (member != null)
            {
                WriteValue(writer, isProperty, member.Name);
                return;
            }
        }

        if (isProperty)
        {
            Span<byte> temp = stackalloc byte[64];

            if (_isULong)
            {
                if (Utf8Formatter.TryFormat((ulong)value, temp, out var bytes))
                {
                    writer.WriteString(temp.Slice(0, bytes));
                }
                else
                {
                    throw new FormatException();
                }
            }
            else
            {
                if (Utf8Formatter.TryFormat(value, temp, out var bytes))
                {
                    writer.WriteString(temp.Slice(0, bytes));
                }
                else
                {
                    throw new FormatException();
                }
            }
        }
        else
        {
            if (_isULong)
            {
                writer.WriteUInt64((ulong)value);
            }
            else
            {
                writer.WriteInt64(value);
            }
        }
    }

    private EnumMember? FindEnumMember(ReadOnlySpan<byte> name, bool isProperty)
    {
        var lut = _enumNames;
        var start = 0;
        var end = lut.Length - 1;

        while (start <= end)
        {
            var pos = (start + end) >> 1;
            var sign = Utf8Helper.Compare(lut[pos].Name, name, false);

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

    private EnumMember? FindEnumMember(long value)
    {
        var members = _enumValues;
        var start = 0;
        var end = members.Length - 1;
        int pos;
        while (start <= end)
        {
            pos = (start + end) >> 1;
            if (members[pos].Value == value)
            {
                return members[pos];
            }

            if (members[pos].Value < value)
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

    private class EnumMember
    {
        public byte[] Name = Array.Empty<byte>();
        public long Value;
    }
}