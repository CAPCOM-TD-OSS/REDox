// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;

namespace REDox.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Field |
                AttributeTargets.Interface |
                AttributeTargets.Property | AttributeTargets.Struct)]
public sealed class DataConverterAttribute : DataAttribute
{
    public DataConverterAttribute(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type converterType)
    {
        ConverterType = converterType;
        ConverterParameters = Array.Empty<object>();
    }

    public DataConverterAttribute(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type converterType,
        params object[] converterParameters)
    {
        ConverterType = converterType;
        ConverterParameters = converterParameters;
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type ConverterType { get; }

    public object[] ConverterParameters { get; }
}