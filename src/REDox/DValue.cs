// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
[DebuggerTypeProxy(typeof(DebugView))]
public readonly partial struct DValue : IDoxNode, IEquatable<DValue>
{
    private static readonly object[] s_tokenVariants = new object[128];

    public static readonly DValue Null = new()
    {
        Instance = DTokenVariant.Null
    };

    public static implicit operator DElement(DValue value)
    {
        return value.AsElement();
    }

    public int Count => AsElement().GetValueCount();

    public DTriviaCollection LeadingTrivia => new(AsElement());

    private bool IsNull => GetToken().Kind == DTokenKind.Null;

    static DValue()
    {
        for (var i = 0; i < 128; i++)
        {
            s_tokenVariants[i] = (DTokenVariant)i;
        }
    }

    internal object? Instance { get; init; }

    internal long Payload { get; init; }

    internal DValue(DContainer value, uint id, int index)
    {
        Instance = value;
        Payload = id | ((long)index << 32);
    }

    internal DValue(Document doc, uint id)
    {
        Instance = doc;
        Payload = id | ((long)doc.Version << 32);
    }

    public static DValue Create(DElement value)
    {
        return new DValue
        {
            Instance = value.Document,
            Payload = value.Id | ((long)value.Version << 32)
        };
    }

    public static DValue Create(string value, StringKind kind = StringKind.Inherit)
    {
        return new DValue
        {
            Instance = value,
            Payload = (long)DToken.Make(kind.ToVariant(), 0)
        };
    }

    public static DValue Create(Utf8Symbol value, SymbolKind kind = SymbolKind.Inherit)
    {
        return new DValue
        {
            Instance = value,
            Payload = (long)DToken.Make(kind.ToVariant(), 0)
        };
    }

    public static DValue Create(bool value)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)(value ? DTokenVariant.BooleanTrue : DTokenVariant.BooleanFalse)]
        };
    }


    public static DValue Create(char value, StringKind kind = StringKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(byte value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(sbyte value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(ushort value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(short value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(int value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(uint value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(long value, IntegerKind kind = IntegerKind.Inherit)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = value
        };
    }

    public static DValue Create(ulong value)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)DTokenVariant.IntegerUnsigned],
            Payload = (long)value
        };
    }

    public static DValue Create(Half value)
    {
        var d = (double)value;

        return new DValue
        {
            Instance = s_tokenVariants[(int)DTokenVariant.FloatHalf],
            Payload = Unsafe.BitCast<double, long>(d)
        };
    }

    public static DValue Create(float value)
    {
        double d = value;

        return new DValue
        {
            Instance = s_tokenVariants[(int)DTokenVariant.FloatSingle],
            Payload = Unsafe.BitCast<double, long>(d)
        };
    }

    public static DValue Create(double value)
    {
        return new DValue
        {
            Instance = s_tokenVariants[(int)DTokenVariant.Float],
            Payload = Unsafe.BitCast<double, long>(value)
        };
    }

    public static DValue Create(decimal value)
    {
        return new DValue
        {
            Instance = value,
            Payload = (long)DToken.Make(DTokenVariant.FloatDecimal, 0)
        };
    }

    public static DValue Create(DateTime value, TimestampKind kind = TimestampKind.Inherit)
    {
        if (kind == TimestampKind.OffsetDateTime)
        {
            return Create(new DateTimeOffset(value));
        }

        return new DValue
        {
            Instance = s_tokenVariants[(int)kind.ToVariant()],
            Payload = Unsafe.BitCast<DateTime, long>(value)
        };
    }

    public static DValue Create(DateTimeOffset value)
    {
        return new DValue
        {
            Instance = value,
            Payload = (long)DToken.Make(DTokenVariant.TimestampOffsetDateTime, 0)
        };
    }


    public static DValue Create(Guid value)
    {
        Span<byte> guidBytes = stackalloc byte[16];
        value.TryWriteBytes(guidBytes, true, out _);

        return new DValue
        {
            Instance = guidBytes.ToArray(),
            Payload = (long)DToken.Make(DTokenVariant.ByteStringGuid, 0)
        };
    }

    public static DValue Create(byte[]? value, ByteStringKind kind)
    {
        if (value == null)
        {
            return Null;
        }

        return new DValue
        {
            Instance = value,
            Payload = (long)DToken.Make(kind.ToVariant(), 0)
        };
    }

    public static DValue Create(bool? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }


    public static DValue Create(char? value, StringKind kind = StringKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(byte? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(sbyte? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(ushort? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(short? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(int? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(uint? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(long? value, IntegerKind kind = IntegerKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(ulong? value)
    {
        if (value == null)
        {
            return Null;
        }

        return value.Value;
    }

    public static DValue Create(Half? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }

    public static DValue Create(float? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }

    public static DValue Create(double? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }

    public static DValue Create(decimal? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }

    public static DValue Create(DateTime? value, TimestampKind kind = TimestampKind.Inherit)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value, kind);
    }

    public static DValue Create(DateTimeOffset? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }


    public static DValue Create(Guid? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value.Value);
    }

    public static DValue Create(object? value)
    {
        if (value == null)
        {
            return Null;
        }

        return new DValue
        {
            Instance = value
        };
    }

    public DElement AsElement()
    {
        if (Instance is Document doc)
        {
            return new DElement(doc, (uint)Payload);
        }

        if (Instance is DContainer container)
        {
            return new DElement(container.Document, (uint)Payload);
        }

        return DoxNodeDocument.Create(this).RootElement;
    }

    public int GetElementIndex()
    {
        if (Instance is DContainer)
        {
            return (int)(Payload >> 32);
        }

        throw new InvalidOperationException(
            "GetElementIndex can only be used on a value that belongs to a container.");
    }

    public string GetPropertyName()
    {
        if (Instance is DObject obj)
        {
            var index = (int)(Payload >> 32);

            return obj.GetPropertyName(index) ?? string.Empty;
        }

        throw new InvalidOperationException(
            "GetPropertyName can only be used on a value that belongs to an object.");
    }

    public string GetPath()
    {
        ThrowIfInvalid();

        var ss = new StringBuilder();
        var (doc, id) = GetElementInfo();

        if (doc == null)
        {
            return "$";
        }

        var latestId = id;
        var parentId = doc.GetParentId(latestId);

        while (parentId != 0)
        {
            var token = doc.GetToken(parentId);

            if (token.IsContainer)
            {
                if (token.Type == DTokenType.Map)
                {
                    foreach (var kv in doc.EnumerateKeyValue(parentId))
                    {
                        if (kv.Value == latestId)
                        {
                            ss.Insert(0, "." + doc.GetStringValue(kv.Key));
                            break;
                        }
                    }
                }
                else
                {
                    var index = 0;
                    foreach (var v in doc.EnumerateValue(parentId))
                    {
                        if (v == latestId)
                        {
                            ss.Insert(0, "[" + index + "]");
                            break;
                        }

                        index++;
                    }
                }
            }

            latestId = parentId;
            parentId = doc.GetParentId(parentId);
        }

        ss.Insert(0, "$");

        return ss.ToString();
    }

    public DValue Root
    {
        get
        {
            ThrowIfInvalid();

            var (doc, id) = GetElementInfo();

            if (doc == null)
            {
                return this;
            }

            return new DValue(doc, doc.RootElement.Id);
        }
    }

    public DValue? Parent
    {
        get
        {
            ThrowIfInvalid();

            var (doc, id) = GetElementInfo();

            if (doc == null)
            {
                return null;
            }

            if (Instance is DContainer container)
            {
                return new DValue(container.Document, container.Id);
            }

            var parentId = doc.GetParentId(id);

            if (parentId == 0)
            {
                return null;
            }

            return new DValue(doc, parentId);
        }
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (Instance is DTokenVariant)
            {
                return true;
            }

            if (Instance is DContainer container)
            {
                var index = (int)(Payload >> 32);

                return container.ValidateValue(index, (uint)Payload);
            }

            if (Instance is Document doc)
            {
                return doc.Version == (int)(Payload >> 32);
            }

            if (Instance == null && Payload == 0)
            {
                return false;
            }

            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (Document?, uint) GetElementInfo()
    {
        if (Instance is DContainer container)
        {
            return (container.Document, (uint)Payload);
        }

        if (Instance is Document doc)
        {
            return (doc, (uint)Payload);
        }

        return default;
    }

    public DValue this[int index]
    {
        get => AsContainer().GetValue(index);
        set => AsContainer().SetValue(value, index);
    }

    public DValue this[string propertyName]
    {
        get
        {
            if (AsObject().TryGetPropertyValue(propertyName, out var value))
            {
                return value;
            }

            return default;
        }
        set => AsObject()[propertyName] = value;
    }


    public static implicit operator DValue(DContainer value)
    {
        return Create(value.AsElement());
    }

    public static implicit operator DValue(bool value)
    {
        return Create(value);
    }

    public static implicit operator DValue(bool? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(char value)
    {
        return Create(value);
    }

    public static implicit operator DValue(char? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(Guid value)
    {
        return Create(value);
    }

    public static implicit operator DValue(Guid? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(byte value)
    {
        return Create(value);
    }

    public static implicit operator DValue(byte? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(sbyte value)
    {
        return Create(value);
    }

    public static implicit operator DValue(sbyte? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(ushort value)
    {
        return Create(value);
    }

    public static implicit operator DValue(ushort? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(short value)
    {
        return Create(value);
    }

    public static implicit operator DValue(short? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(uint value)
    {
        return Create(value);
    }

    public static implicit operator DValue(uint? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(int value)
    {
        return Create(value);
    }

    public static implicit operator DValue(int? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(long value)
    {
        return Create(value);
    }

    public static implicit operator DValue(long? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(ulong value)
    {
        return Create(value);
    }

    public static implicit operator DValue(ulong? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(string? value)
    {
        if (value == null)
        {
            return Null;
        }

        return Create(value);
    }

    public static implicit operator DValue(Half value)
    {
        return Create(value);
    }

    public static implicit operator DValue(Half? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(float value)
    {
        return Create(value);
    }

    public static implicit operator DValue(float? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(double value)
    {
        return Create(value);
    }

    public static implicit operator DValue(double? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(decimal value)
    {
        return Create(value);
    }

    public static implicit operator DValue(decimal? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(DateTime value)
    {
        return Create(value);
    }

    public static implicit operator DValue(DateTime? value)
    {
        return Create(value);
    }

    public static implicit operator DValue(DateTimeOffset value)
    {
        return Create(value);
    }

    public static implicit operator DValue(DateTimeOffset? value)
    {
        return Create(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DToken GetToken()
    {
        if (Instance is DContainer container)
        {
            return container.Document.GetToken((uint)Payload);
        }

        if (Instance is Document doc)
        {
            return doc.GetToken((uint)Payload);
        }

        if (Instance is DTokenVariant exKind)
        {
            return DToken.Make(exKind, 0);
        }

        if (Payload == 0)
        {
            if (Instance == null)
            {
                return default;
            }

            return AsElement().Token;
        }

        return new DToken(Payload);
    }


    public DContainer AsContainer()
    {
        if (Instance is DContainer container)
        {
            return container.Document.GetElementContainer((uint)Payload);
        }

        if (Instance is Document doc)
        {
            return doc.GetElementContainer((uint)Payload);
        }

        return AsElement().AsContainer();
    }

    public DObject AsObject()
    {
        if (Instance is DContainer container)
        {
            return container.Document.GetElementObject((uint)Payload);
        }

        if (Instance is Document doc)
        {
            return doc.GetElementObject((uint)Payload);
        }

        return AsElement().AsObject();
    }

    public DMap AsMap()
    {
        if (Instance is DContainer container)
        {
            return container.Document.GetElementMap((uint)Payload);
        }

        if (Instance is Document doc)
        {
            return doc.GetElementMap((uint)Payload);
        }

        return AsElement().AsMap();
    }

    public DArray AsArray()
    {
        if (Instance is DContainer container)
        {
            return container.Document.GetElementArray((uint)Payload);
        }

        if (Instance is Document doc)
        {
            return doc.GetElementArray((uint)Payload);
        }

        return AsElement().AsArray();
    }

    public static explicit operator Guid(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is byte[] bytes && bytes.Length == 16)
            {
                return new Guid(bytes, true);
            }

            return value.To<Guid>();
        }

        if (!doc.TryGetGuidExact(id, out var guid))
        {
            guid = doc.GetGuidValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, guid);
            }
        }

        return guid;
    }

    public static explicit operator Guid?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (Guid)value;
    }

    public static explicit operator bool(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Boolean)
            {
                return exKind == DTokenVariant.BooleanTrue;
            }

            return value.To<bool>();
        }

        if (!doc.TryGetBooleanExact(id, out var bvalue))
        {
            bvalue = doc.GetBooleanValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, bvalue);
            }
        }

        return bvalue;
    }

    public static explicit operator bool?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (bool)value;
    }


    public static explicit operator char(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToType() == DTokenType.Text)
            {
                return (char)value.Payload;
            }

            return value.To<char>();
        }

        return doc.GetCharValue(id);
    }

    public static explicit operator char?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (char)value;
    }

    public static explicit operator byte(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (byte)value.Payload;
            }

            return value.To<byte>();
        }

        if (!doc.TryGetUnsignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetUnsignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        if (ivalue > byte.MaxValue)
        {
            throw new InvalidOperationException($"The value {ivalue} is outside the range of byte.");
        }

        return (byte)ivalue;
    }

    public static explicit operator byte?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (byte)value;
    }

    public static explicit operator sbyte(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (sbyte)value.Payload;
            }

            return value.To<sbyte>();
        }

        if (!doc.TryGetSignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetSignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        if (ivalue < sbyte.MinValue || ivalue > sbyte.MaxValue)
        {
            throw new InvalidOperationException($"The value {ivalue} is outside the range of sbyte.");
        }

        return (sbyte)ivalue;
    }

    public static explicit operator sbyte?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (sbyte)value;
    }

    public static explicit operator short(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (short)value.Payload;
            }

            return value.To<short>();
        }

        if (!doc.TryGetSignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetSignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        if (ivalue < short.MinValue || ivalue > short.MaxValue)
        {
            throw new InvalidOperationException($"The value {ivalue} is outside the range of short.");
        }

        return (short)ivalue;
    }

    public static explicit operator short?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (short)value;
    }

    public static explicit operator ushort(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (ushort)value.Payload;
            }

            return value.To<ushort>();
        }

        if (!doc.TryGetUnsignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetUnsignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        if (ivalue > ushort.MaxValue)
        {
            throw new InvalidOperationException($"The value {ivalue} is outside the range of ushort.");
        }

        return (ushort)ivalue;
    }

    public static explicit operator ushort?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (ushort)value;
    }

    public static explicit operator int(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (int)value.Payload;
            }

            return value.To<int>();
        }

        if (!doc.TryGetSignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetSignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        if (ivalue < int.MinValue || ivalue > int.MaxValue)
        {
            throw new InvalidOperationException($"The value {ivalue} is outside the range of int.");
        }

        return (int)ivalue;
    }

    public static explicit operator int?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (int)value;
    }

    public static explicit operator uint(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (uint)value.Payload;
            }

            return value.To<uint>();
        }

        if (!doc.TryGetUnsignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetUnsignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        if (ivalue > uint.MaxValue)
        {
            throw new InvalidOperationException($"The value {ivalue} is outside the range of uint.");
        }

        return (uint)ivalue;
    }

    public static explicit operator uint?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (uint)value;
    }


    public static explicit operator long(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return value.Payload;
            }

            return value.To<long>();
        }

        if (!doc.TryGetSignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetSignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        return ivalue;
    }

    public static explicit operator long?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (long)value;
    }

    public static explicit operator ulong(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Integer)
            {
                return (ulong)value.Payload;
            }

            return value.To<ulong>();
        }

        if (!doc.TryGetUnsignedIntegerExact(id, out var ivalue))
        {
            ivalue = doc.GetUnsignedIntegerValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, ivalue);
            }
        }

        return ivalue;
    }

    public static explicit operator ulong?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (ulong)value;
    }

    public static explicit operator string?(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            return value.To<string>();
        }

        var token = doc.GetToken(id);

        if (token.Kind == DTokenKind.Null)
        {
            return null;
        }

        if (token.IsContainer)
        {
            throw new InvalidOperationException();
        }

        if (!doc.TryGetStringExact(id, out var str))
        {
            str = doc.GetStringValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, str);
            }
        }

        return str;
    }

    public static explicit operator float(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Float &&
                exKind != DTokenVariant.FloatDecimal)
            {
                var payload = value.Payload;

                return (float)Unsafe.BitCast<long, double>(payload);
            }

            return value.To<float>();
        }

        if (!doc.TryGetFloatingExact(id, out var fvalue))
        {
            fvalue = doc.GetFloatingValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, fvalue);
            }
        }

        return (float)fvalue;
    }

    public static explicit operator float?(DValue v)
    {
        if (v.IsNull)
        {
            return null;
        }

        return (float)v;
    }

    public static explicit operator double(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Float &&
                exKind != DTokenVariant.FloatDecimal)
            {
                var payload = value.Payload;

                return Unsafe.BitCast<long, double>(payload);
            }

            return value.To<double>();
        }

        if (!doc.TryGetFloatingExact(id, out var fvalue))
        {
            fvalue = doc.GetFloatingValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, fvalue);
            }
        }

        return fvalue;
    }

    public static explicit operator double?(DValue v)
    {
        if (v.IsNull)
        {
            return null;
        }

        return (double)v;
    }


    public static explicit operator decimal(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is decimal decimalValue)
            {
                return decimalValue;
            }

            return value.To<decimal>();
        }

        if (!doc.TryGetDecimalExact(id, out var fvalue))
        {
            fvalue = doc.GetDecimalValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, fvalue);
            }
        }

        return fvalue;
    }

    public static explicit operator decimal?(DValue v)
    {
        if (v.IsNull)
        {
            return null;
        }

        return (decimal)v;
    }

    public static explicit operator DateTime(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DTokenVariant exKind && exKind.ToKind() == DTokenKind.Timestamp &&
                exKind != DTokenVariant.TimestampOffsetDateTime)
            {
                var payload = value.Payload;

                return Unsafe.BitCast<long, DateTime>(payload);
            }

            return value.To<DateTime>();
        }

        if (!doc.TryGetDateTimeExact(id, out var dateTime))
        {
            dateTime = doc.GetDateTimeValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, dateTime);
            }
        }

        return dateTime;
    }

    public static explicit operator DateTime?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (DateTime)value;
    }

    public static explicit operator DateTimeOffset(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is DateTimeOffset dto)
            {
                return dto;
            }

            return value.To<DateTimeOffset>();
        }

        if (!doc.TryGetDateTimeOffsetExact(id, out var dateTimeOffset))
        {
            dateTimeOffset = doc.GetDateTimeOffsetValue(id);
        }
        else
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, dateTimeOffset);
            }
        }

        return dateTimeOffset;
    }

    public static explicit operator DateTimeOffset?(DValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return (DateTimeOffset)value;
    }

    public static explicit operator ReadOnlySpan<byte>(DValue value)
    {
        var (doc, id) = value.GetElementInfo();

        if (doc == null)
        {
            if (value.Instance is byte[] bytes)
            {
                return bytes;
            }

            return value.To<byte[]>();
        }

        return doc.GetByteStringValue(id);
    }


    public static DValue From<T>(T value, SerializerSettings? settings = null)
    {
        return DoxNodeDocument.CreateFrom(value, settings ?? SerializerSettings.Default).RootElement.AsValue();
    }

    public static DValue FromObject(object? value, Type inputType, SerializerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        return DoxNodeDocument.CreateFromObject(value, inputType, settings ?? SerializerSettings.Default).RootElement
            .AsValue();
    }

    public T To<T>(T target)
    {
        return AsElement().To(target);
    }

    public T? To<T>()
    {
        return AsElement().To<T>();
    }

    public object? ToObject(Type returnType)
    {
        return AsElement().ToObject(returnType);
    }

    public object ToObject(Type returnType, object target)
    {
        return AsElement().ToObject(returnType, target);
    }

    public override string? ToString()
    {
        if (Instance is DContainer || Instance is Document)
        {
            var element = AsElement();

            if (element.Token.IsContainer)
            {
                return element.ToJsonString(new JsonWriteOptions { WriteIndented = true });
            }

            return element.ToString();
        }

        if (Instance is DTokenVariant exKind)
        {
            var kind = exKind.ToKind();

            if (kind == DTokenKind.Integer)
            {
                if (exKind == DTokenVariant.IntegerUnsigned)
                {
                    return ((ulong)Payload).ToString(CultureInfo.InvariantCulture);
                }

                return Payload.ToString(CultureInfo.InvariantCulture);
            }

            if (kind == DTokenKind.Boolean)
            {
                return exKind == DTokenVariant.BooleanTrue ? "True" : "False";
            }

            if (kind == DTokenKind.Timestamp)
            {
                var payload = Payload;
                return Unsafe.BitCast<long, DateTime>(payload).ToString("o", CultureInfo.InvariantCulture);
            }

            if (kind == DTokenKind.Float)
            {
                var payload = Payload;
                return Unsafe.BitCast<long, double>(payload).ToString("R", CultureInfo.InvariantCulture);
            }

            if (kind == DTokenKind.String || kind == DTokenKind.Symbol)
            {
                return $"\"{(char)Payload}\"";
            }

            if (kind == DTokenKind.Null)
            {
                return string.Empty;
            }
        }
        else
        {
            var token = new DToken(Payload);

            if (token.Kind == DTokenKind.ByteString)
            {
                if (token.Variant == DTokenVariant.ByteStringGuid)
                {
                    return new Guid((byte[]?)Instance, true).ToString();
                }
            }
        }

        if (Instance is decimal decimalValue)
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        return Instance?.ToString();
    }

    public void WriteTo(DataWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        AsElement().WriteTo(writer);
    }

    public IEnumerable<DTrivia> EnumerateTrivia()
    {
        return AsElement().EnumerateTrivia();
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(nameof(DValue));
        }
    }

    public DValue DeepClone()
    {
        ThrowIfInvalid();

        var token = GetToken();
        if (token.IsContainer)
        {
            var container = AsContainer();

            if (container is DArray arr)
            {
                return arr.DeepClone().AsValue();
            }

            if (container is DMap map)
            {
                return map.DeepClone().AsValue();
            }

            return ((DObject)container).DeepClone().AsValue();
        }

        return new DValue
        {
            Payload = Payload,
            Instance = Instance
        };
    }

    public DValue ReplaceWith<T>(T value)
    {
        ThrowIfInvalid();

        if (Instance is Document doc)
        {
            var tokenId = (uint)Payload;
            doc.JoinAny(tokenId, value);
            return new DValue(doc, tokenId);
        }

        if (Instance is DContainer container)
        {
            var tokenId = (uint)Payload;
            var document = container.Document;
            document.JoinAny(tokenId, value);
            return new DValue(document, tokenId);
        }

        if (value is DValue doxValue)
        {
            return doxValue.DeepClone();
        }

        if (value is IDoxNode node)
        {
            return node.AsElement().AsValue().DeepClone();
        }

        return From(value);
    }

    public bool Equals(DValue other)
    {
        return Equals(Instance, other.Instance) && Payload == other.Payload;
    }

    public override bool Equals(object? obj)
    {
        return obj is DValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Payload.GetHashCode() ^ (Instance?.GetHashCode() ?? 0);
    }

    private static ReadOnlySpan<byte> FormatNumber(Span<byte> buffer, DValue value, DToken token)
    {
        switch (token.Kind)
        {
            case DTokenKind.BigNumber:
                return (ReadOnlySpan<byte>)value;
            case DTokenKind.Integer:
                if (token.Variant == DTokenVariant.IntegerUnsigned)
                {
                    if (Utf8Formatter.TryFormat((ulong)value, buffer, out var bytesWritten))
                    {
                        return buffer.Slice(0, bytesWritten);
                    }
                }
                else
                {
                    if (Utf8Formatter.TryFormat((long)value, buffer, out var bytesWritten))
                    {
                        return buffer.Slice(0, bytesWritten);
                    }
                }

                break;
            case DTokenKind.Float:
                switch (token.FloatKind)
                {
                    case FloatKind.Half:
                    case FloatKind.Single:
                    case FloatKind.Inherit:
                        {
                            if (Utf8Formatter.TryFormat((float)value, buffer, out var bytesWritten))
                            {
                                return buffer.Slice(0, bytesWritten);
                            }
                        }
                        break;
                    case FloatKind.Decimal:
                        {
                            var decimalValue = (decimal)value;

                            if (decimalValue == 0)
                            {
                                decimalValue = 0;
                            }

                            if (Utf8Formatter.TryFormat(decimalValue, buffer, out var bytesWritten))
                            {
                                return buffer.Slice(0, bytesWritten);
                            }
                        }
                        break;
                    default:
                        {
                            if (Utf8Formatter.TryFormat((double)value, buffer, out var bytesWritten))
                            {
                                return buffer.Slice(0, bytesWritten);
                            }
                        }
                        break;
                }

                break;
            case DTokenKind.InlineFloat:
                {
                    if (Utf8Formatter.TryFormat((double)value, buffer, out var bytesWritten))
                    {
                        return buffer.Slice(0, bytesWritten);
                    }
                }
                break;
        }

        return [];
    }

    public static bool DeepEquals(DValue left, DValue right)
    {
        if (left.Equals(right))
        {
            return true;
        }

        var tokenLeft = left.GetToken();
        var tokenRight = right.GetToken();

        if (tokenLeft.IsContainer)
        {
            if (tokenLeft.Type == DTokenType.Array)
            {
                if (tokenRight.Type != DTokenType.Array)
                {
                    return false;
                }

                var leftArr = left.AsArray();
                var rightArr = right.AsArray();

                if (leftArr.Count != rightArr.Count)
                {
                    return false;
                }

                for (var i = 0; i < leftArr.Count; i++)
                {
                    if (!DeepEquals(leftArr[i], rightArr[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (tokenRight.Type != DTokenType.Map)
            {
                return false;
            }

            var leftObj = left.AsObject();
            var rightObj = right.AsObject();

            if (leftObj.Count != rightObj.Count)
            {
                return false;
            }


            foreach (var kv in leftObj)
            {
                if (rightObj.TryGetPropertyValue(kv.Key, out var rvalue))
                {
                    if (!DeepEquals(kv.Value, rvalue))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        var valueKindLeft = tokenLeft.Kind;
        var valueKindRight = tokenRight.Kind;

        if (((int)valueKindLeft & 0xc) != ((int)valueKindRight & 0xc))
        {
            return false;
        }

        switch (valueKindLeft)
        {
            case DTokenKind.Null:
                if (valueKindRight != DTokenKind.Null)
                {
                    return false;
                }

                return true;
            case DTokenKind.Boolean:
                if (valueKindRight != DTokenKind.Boolean)
                {
                    return false;
                }

                return (bool)left == (bool)right;
            case DTokenKind.Integer:
            case DTokenKind.Float:
            case DTokenKind.InlineFloat:
            case DTokenKind.BigNumber:
                if (tokenLeft.Kind == tokenRight.Kind)
                {
                    switch (tokenLeft.Kind)
                    {
                        case DTokenKind.Float:
                            if (tokenLeft.Variant == tokenRight.Variant)
                            {
                                if (tokenLeft.Variant == DTokenVariant.FloatDecimal)
                                {
                                    return (decimal)left == (decimal)right;
                                }

                                return (double)left == (double)right;
                            }

                            break;
                        case DTokenKind.InlineFloat:
                            return (long)tokenLeft == (long)tokenRight;
                        case DTokenKind.Integer:
                            if (tokenLeft.Variant == DTokenVariant.IntegerUnsigned &&
                                tokenRight.Variant == DTokenVariant.IntegerUnsigned)
                            {
                                return (ulong)left == (ulong)right;
                            }

                            if (tokenLeft.Variant != DTokenVariant.IntegerUnsigned &&
                                tokenRight.Variant != DTokenVariant.IntegerUnsigned)
                            {
                                return (long)left == (long)right;
                            }

                            break;
                        case DTokenKind.BigNumber:
                            return ((ReadOnlySpan<byte>)left).SequenceEqual((ReadOnlySpan<byte>)right);
                    }
                }

                Span<byte> leftBuffer = stackalloc byte[64];
                Span<byte> rightBuffer = stackalloc byte[64];
                var leftBytes = FormatNumber(leftBuffer, left, tokenLeft);
                var rightBytes = FormatNumber(rightBuffer, right, tokenRight);

                return leftBytes.SequenceEqual(rightBytes);
            case DTokenKind.String:
            case DTokenKind.Symbol:
            case DTokenKind.Timestamp:
                return (string?)left == (string?)right;
            case DTokenKind.ByteString:
                return ((ReadOnlySpan<byte>)left).SequenceEqual((ReadOnlySpan<byte>)right);
        }

        return false;
    }
}