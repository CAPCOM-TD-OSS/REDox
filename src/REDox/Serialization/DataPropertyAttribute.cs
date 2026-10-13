// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;

namespace REDox.Serialization;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class DataPropertyAttribute : DataAttribute
{
    internal DefaultValueHandling? _defaultValue;

    internal NullValueHandling? _nullValue;
    internal ObjectCreationHandling? _objectCreation;

    public DataPropertyAttribute()
    {
    }

    public DataPropertyAttribute(string name)
    {
        Name = name;
    }

    public DataPropertyAttribute(int order)
    {
        Order = order;
    }

    public string? Name { get; set; }

    public int Order { get; set; }

    public NullValueHandling NullValueHandling
    {
        get => _nullValue.GetValueOrDefault();
        set => _nullValue = value;
    }

    public DefaultValueHandling DefaultValueHandling
    {
        get => _defaultValue.GetValueOrDefault();
        set => _defaultValue = value;
    }

    public ObjectCreationHandling ObjectCreationHandling
    {
        get => _objectCreation.GetValueOrDefault();
        set => _objectCreation = value;
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? ItemConverterType { get; set; }

    public object[]? ItemConverterParameters { get; set; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? NamingPolicyType { get; set; }
}