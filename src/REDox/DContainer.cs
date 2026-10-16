// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;
using REDox.Json;
using REDox.Serialization;

namespace REDox;

public abstract class DContainer : IDoxNode
{
    protected int _count;
    protected DElement _element;
    private int _version;

    internal DContainer()
    {
    }

    public DTriviaCollection LeadingTrivia => new(_element);

    internal uint TriviaId { get; set; }

    public DValue Root
    {
        get
        {
            ThrowIfInvalid();
            return _element.AsValue().Root;
        }
    }

    public DValue? Parent
    {
        get
        {
            ThrowIfInvalid();
            return _element.AsValue().Parent;
        }
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_element.IsValid)
            {
                return true;
            }

            if (_element.Id == 0)
            {
                return false;
            }

            if (_element.Document.ValidateContainer(_element.Id, this))
            {
                _element = new DElement(_element.Document, _element.Id);
                return true;
            }

            _element = default;
            return false;
        }
    }

    protected internal Document Document => _element.Document;

    internal uint Id => _element.Id;

    public int Count => _count;

    public abstract int Capacity { get; set; }

    public DElement AsElement()
    {
        ThrowIfInvalid();
        return _element;
    }

    internal void Invalidate()
    {
        _element = default;
    }

    internal int Version => _version;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void IncrementVersion()
    {
        _version++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ValidateValue(int version)
    {
        return _version == version && IsValid;
    }

    public T To<T>(T target)
    {
        ThrowIfInvalid();
        return Serializer.DeserializeToInternal(_element, target);
    }

    public T? To<T>()
    {
        ThrowIfInvalid();
        return Serializer.DeserializeInternal<T>(_element);
    }

    public object? ToObject(Type returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ThrowIfInvalid();
        return Serializer.DeserializeInternal(_element, returnType);
    }

    public object ToObject(Type returnType, object target)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfInvalid();
        return Serializer.DeserializeToInternal(_element, returnType, target);
    }

    public void WriteTo(DataWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ThrowIfInvalid();
        AsElement().WriteTo(writer);
    }

    public string GetPath()
    {
        ThrowIfInvalid();
        return _element.AsValue().GetPath();
    }

    internal virtual void Release()
    {
        _version++;
        _element = default;
    }

    protected internal abstract DElement GetKeyElement(int index);

    protected internal abstract DElement GetValueElement(int index);

    protected internal abstract DValue GetValue(int index);

    protected internal abstract void SetValue(DValue value, int index);

    protected void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    public DValue AsValue()
    {
        ThrowIfInvalid();
        return _element.AsValue();
    }

    public override string? ToString()
    {
        ThrowIfInvalid();

        if (_element.Token.IsContainer)
        {
            return JsonDocument.EncodeToString(_element,
                new JsonWriteOptions { WriteIndented = true });
        }

        return _element.ToString();
    }

    public static implicit operator DElement(DContainer value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value._element;
    }
}