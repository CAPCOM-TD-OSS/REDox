// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;

namespace REDox.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class DataObjectAttribute : DataAttribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? NamingPolicyType { get; set; }
}