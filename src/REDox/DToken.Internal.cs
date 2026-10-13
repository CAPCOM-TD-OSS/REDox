// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace REDox;

public partial struct DToken
{
    internal const long MinInlineInteger = -(1L << 52);
    internal const long MaxInlineInteger = (1L << 52) - 1;
    internal const long InlinePayloadMask = 0x07000000_00000000L;

    internal bool IsEmptyToken => (ulong)_value >> 58 == 0b100000;

    internal bool IsEmptyExtend => (ulong)_value >> 58 == 0b100001;

    internal bool IsLeadingTrivia => (ulong)_value >> 59 == 0b00001;

    // Extended trivia uses TriviaId as a flag to keep parsed slots out of the free lists.
    internal bool IsTriviaReuseProhibited => Kind == DTokenKind.Trivia && IsExtended && TriviaId != 0;

    // Container LinkId bits overlap the inline marker.
    // InlineFloat uses the subkind bits for its exponent rather than a subtype.
    internal bool HasInlineSubKind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            const uint inlineFloatHeader = 0x80u | ((uint)DTokenKind.InlineFloat << 3) | 7u;
            var header = (uint)((ulong)_value >> 56);

            // E=1 and Type<Map occupy [0x80, 0xE0). With marker 111, InlineFloat is 0xBF.
            // Evaluate all three comparisons to avoid short-circuit branches.
            return ((header & 7) == 7) & (unchecked(header - 0x80u) < 0x60u) & (header != inlineFloatHeader);
        }
    }

    internal bool IsInlinePayload => IsExtended && !IsContainer && (Kind == DTokenKind.InlineFloat || Variant.IsInherit);

    internal uint SubKind
    {
        get
        {
            Debug.Assert(Kind != DTokenKind.InlineFloat);
            return (uint)GetInlineSubKind(Kind);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeExtendContainer(DTokenType type, uint extendId, uint linkId = 0)
    {
        Debug.Assert(type.ToVariant().IsContainer);
        Debug.Assert(linkId <= MaxTokenId, "linkId overflow");
        Debug.Assert(extendId <= MaxExtendId, "extendId overflow");
        return new DToken(long.MinValue | ((long)type << 60) | extendId | ((long)linkId << 30));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeExtendLiteral(DTokenVariant variant, uint triviaId)
    {
        Debug.Assert(triviaId <= MaxExtendId);
        Debug.Assert(variant.ToType() == DTokenType.Literal);
        return new DToken(long.MinValue | ((long)variant << 56) | ((long)triviaId << 28));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeExtend(DTokenVariant variant, uint triviaId, uint extendId)
    {
        Debug.Assert(variant.IsStringEncoded || variant.IsNumber || variant.ToKind() == DTokenKind.Trivia);
        Debug.Assert(triviaId <= MaxExtendId);
        Debug.Assert(extendId <= MaxExtendId);
        return new DToken(long.MinValue | ((long)variant << 56) | extendId | ((long)triviaId << 28));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeExtendInlineInteger(IntegerKind kind, long value)
    {
        Debug.Assert(value >= MinInlineInteger && value <= MaxInlineInteger);
        return new DToken(long.MinValue | ((long)DTokenKind.Integer << 59) | InlinePayloadMask | ((long)kind << 53) |
                          (value & 0x1fffffffffffffL));
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeEmptyExtend(uint emptyId, uint extendId)
    {
        Debug.Assert(emptyId <= MaxTokenId, "emptyId overflow");
        Debug.Assert(extendId <= MaxExtendId, "extendId overflow");

        return new DToken(long.MinValue | ((long)emptyId << 28) | extendId | (1L << 58));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeEmpty(uint emptyId)
    {
        Debug.Assert(emptyId <= MaxTokenId, "emptyId overflow");

        return new DToken(long.MinValue | ((long)emptyId << 28));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeExtendInlineSingle(FloatKind kind, float value)
    {
        var iValue = BitConverter.SingleToInt32Bits(value);

        return new DToken(long.MinValue | ((long)DTokenKind.Float << 59) | InlinePayloadMask | (uint)iValue |
                          ((long)kind << 53));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DToken MakeExtendInlineFloat(long payload)
    {
        Debug.Assert((payload & ~InlineFloatMask) == 0, "payload overflow");
        return new DToken(long.MinValue | ((long)DTokenKind.InlineFloat << 59) | payload);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Increment()
    {
        Debug.Assert(IsContainer);
        Debug.Assert(!IsExtended);

        _value++;
    }

    internal uint InlineKind
    {
        get
        {
            Debug.Assert(HasInlineSubKind);
            return (uint)(_value >> 53) & 7;
        }
        set
        {
            Debug.Assert(HasInlineSubKind);
            _value &= ~0x00e00000_00000000;
            _value |= (long)value << 53;
        }
    }

    internal uint ExtendId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(IsExtended);
            return (uint)(_value & MaxExtendId);
        }
    }

    internal uint TriviaId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(!IsInlinePayload && IsExtended && !IsContainer);
            return (uint)((_value >> 28) & MaxExtendId);
        }
        set
        {
            Debug.Assert(!IsInlinePayload && IsExtended && !IsContainer);
            Debug.Assert(value <= MaxExtendId);
            _value &= ~0x00ffffff_f0000000L;
            _value |= (long)value << 28;
        }
    }

    internal bool IsExtendInlineLiteral
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsExtended && (Type == DTokenType.Literal || IsInlinePayload);
    }

    internal uint LinkId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(IsContainer);

            return (uint)((_value >> 30) & MaxTokenId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            Debug.Assert(IsContainer);
            Debug.Assert(value <= MaxTokenId, "linkId overflow");

            _value &= ~0x0fffffffc0000000L;
            _value |= (long)value << 30;
        }
    }

    internal uint EmptyId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(IsEmpty);
            return (uint)((_value >> 28) & MaxTokenId);
        }
    }

    internal uint JumpId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(Kind == DTokenKind.Control && !IsExtended);
            return (uint)(_value & MaxTokenId);
        }
    }

    internal int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(IsContainer);
            Debug.Assert(!IsExtended);

            return (int)(_value & MaxTokenId);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            Debug.Assert(IsContainer);
            Debug.Assert(!IsExtended);
            Debug.Assert(value >= 0 && value <= MaxValueCount, "count overflow");

            _value = (_value & 0x7fffffffc0000000L) | (uint)value;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int EncodeStringParamPayload(int length, bool escaped)
    {
        if (length > 0x7fffff)
        {
            length >>= 8;
            length |= 0x800000;
        }

        if (escaped)
        {
            length |= 0x1000000;
        }

        return length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (bool escaped, int encodedLength) DecodeStringParamPayload(int param)
    {
        return ((param & 0x1000000) != 0, param & 0xffffff);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long EncodeLengthOffsetPayload(int length, int offset)
    {
        Debug.Assert((uint)length <= 0x1ffffff, "length overflow");
        Debug.Assert((uint)offset <= 0x7fffffff, "offset overflow");

        var payload = (long)offset;

        return payload | ((long)length << 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int length, int offset) DecodeLengthOffsetPayload(DToken param)
    {
        return ((int)((param._value >> 31) & 0x1ffffff), (int)(param._value & 0x7fffffff));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long EncodeInlineFloatPayload(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);

        var sign = (bits >> 63) & 1;
        var exponent = (bits >> 52) & 0x7ff;
        var mantissa = bits & 0x000f_ffff_ffff_ffffL;

        if ((bits & 0x7fff_ffff_ffff_ffffL) == 0)
        {
            return sign << 52;
        }

        if (exponent >= 992 && exponent <= 1055)
        {
            var expIdx = exponent - 992;

            if (expIdx == 0 && mantissa == 0)
            {
                return -1;
            }

            return (expIdx << 53) | (sign << 52) | mantissa;
        }

        return -1;
    }

    internal static double DecodeInlineFloatPayload(DToken token)
    {
        Debug.Assert(token.Kind == DTokenKind.InlineFloat);
        var raw = (long)token;

        var expIdx = (raw >> 53) & 0x3f;
        var sign = (raw >> 52) & 1;
        var mantissa = raw & 0x000f_ffff_ffff_ffffL;

        if (expIdx == 0 && mantissa == 0)
        {
            var zeroBits = sign << 63;
            return BitConverter.Int64BitsToDouble(zeroBits);
        }

        var exponent = expIdx + 992;
        var bits = (sign << 63) | (exponent << 52) | mantissa;

        return BitConverter.Int64BitsToDouble(bits);
    }

    internal static long DecodeInlineIntegerPayload(DToken token)
    {
        Debug.Assert(token.Kind == DTokenKind.Integer && token.IsInlinePayload);
        return (token._value << 11) >> 11;
    }

    internal static float DecodeInlineSinglePayload(DToken token)
    {
        Debug.Assert(token.Kind == DTokenKind.Float && token.IsInlinePayload);
        return BitConverter.Int32BitsToSingle((int)token._value);
    }
}