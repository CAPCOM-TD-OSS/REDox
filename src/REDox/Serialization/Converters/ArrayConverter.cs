// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class ArrayConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(double[]))
        {
            return new SzArrayConverter<double>(settings);
        }

        if (type == typeof(byte[]))
        {
            return new SzArrayConverter<byte>(settings);
        }

        if (type == typeof(sbyte[]))
        {
            return new SzArrayConverter<sbyte>(settings);
        }

        if (type == typeof(short[]))
        {
            return new SzArrayConverter<short>(settings);
        }

        if (type == typeof(ushort[]))
        {
            return new SzArrayConverter<ushort>(settings);
        }

        if (type == typeof(Half[]))
        {
            return new SzArrayConverter<Half>(settings);
        }

        if (type == typeof(int[]))
        {
            return new SzArrayConverter<int>(settings);
        }

        if (type == typeof(decimal[]))
        {
            return new SzArrayConverter<decimal>(settings);
        }

        if (type == typeof(bool[]))
        {
            return new SzArrayConverter<bool>(settings);
        }

        if (type == typeof(string[]))
        {
            return new SzArrayConverter<string>(settings);
        }

        if (type == typeof(float[]))
        {
            return new SzArrayConverter<float>(settings);
        }

        if (type == typeof(uint[]))
        {
            return new SzArrayConverter<uint>(settings);
        }

        if (type == typeof(ulong[]))
        {
            return new SzArrayConverter<ulong>(settings);
        }

        if (type == typeof(long[]))
        {
            return new SzArrayConverter<long>(settings);
        }

        if (type == typeof(Guid[]))
        {
            return new SzArrayConverter<Guid>(settings);
        }

        if (!RuntimeFeature.IsDynamicCodeSupported || !settings.AllowDynamicGenericConverters)
        {
            return new DefaultConverter(type, settings);
        }

        if (type.GetArrayRank() == 1)
        {
            if (settings.ParallelOptions.ParallelDeserializeEnabled && settings.PreserveReferencesHandling == PreserveReferencesHandling.None)
            {
                return ConverterHelper.CreateConverter(
                    typeof(ParallelSzArrayConverter<>).MakeGenericType(type.GetElementType()!), settings);
            }

            return ConverterHelper.CreateConverter(
                typeof(SzArrayConverter<>).MakeGenericType(type.GetElementType()!), settings);
        }

        return ConverterHelper.CreateConverter(
            typeof(MultidimensionalArrayConverter<,>).MakeGenericType(type, type.GetElementType()!), settings);
    }

    private sealed class DefaultConverter : DataConverter
    {
        private readonly DataContract _contract;
        private readonly DataConverter _converter;
        private readonly Type _elementType;
        private readonly int _rank;
        private bool _isReference;

        public DefaultConverter(Type type, SerializerSettings settings)
        {
            _elementType = type.GetElementType()!;
            _converter = settings.GetConverter(_elementType);
            _contract = settings.GetContract(type);
            _rank = type.GetArrayRank();
            _isReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Arrays) != 0;
        }

        protected internal override Type TargetType => _contract.Type;

        public override void WriteObjectAsPropertyName(DataWriter writer, object? value)
        {
            throw new NotSupportedException();
        }

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            throw new NotSupportedException();
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (DefaultConverter)MemberwiseClone();
                converter._isReference = false;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            if (reader.IsNullToken(tokenId))
            {
                return null;
            }

            if (reader.ReadTypedArray(_contract, objectType, tokenId, ref existingValue))
            {
                return existingValue;
            }

            if (_rank > 1)
            {
                return ReadMultidimensional(reader, tokenId);
            }

            var count = reader.GetValueCount(tokenId);
            var array = existingValue as Array;
            if (array == null || array.GetType() != _contract.Type)
            {
                array = Array.CreateInstanceFromArrayType(_contract.Type, count);
            }

            var index = 0;
            foreach (var elementId in reader.EnumerateArray(tokenId))
            {
                if (index >= array.Length)
                {
                    break;
                }

                array.SetValue(_converter.ReadObject(in reader, _elementType, elementId, null), index);
                index++;
            }

            return array;
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var typeNeeded = objectType != _contract.Type;

            WriteData(writer, (Array)value, typeNeeded);
        }

        private void WriteData(DataWriter writer, Array value, bool typeNeeded)
        {
            if (_rank > 1)
            {
                WriteMultidimensional(writer, value, typeNeeded, _isReference);
            }
            else
            {
                using var scope = writer.BeginWriteArray(value.Length, _contract, typeNeeded, _isReference, value);

                if (scope.Skipped)
                {
                    return;
                }

                for (var i = 0; i < value.Length; i++)
                {
                    try
                    {
                        _converter.WriteObject(writer, _elementType, value.GetValue(i));
                    }
                    catch (Exception e) when (!(e is SerializationException))
                    {
                        writer.HandleException(e, value, _contract, i);
                    }
                }
            }
        }

        private Array ReadMultidimensional(in DataReader reader, uint tokenId)
        {
            var lengths = new int[_rank];
            var valueId = tokenId;

            for (var i = 0; i < _rank; i++)
            {
                lengths[i] = reader.GetValueCount(valueId);

                var elements = reader.EnumerateArray(valueId);
                if (!elements.MoveNext())
                {
                    break;
                }

                valueId = elements.Current;
            }

            var array = Array.CreateInstanceFromArrayType(_contract.Type, lengths);

            ReadDimension(reader, tokenId, array, new int[_rank], 0);

            return array;
        }

        private void ReadDimension(in DataReader reader, uint tokenId, Array array, int[] indices, int dimension)
        {
            var length = array.GetLength(dimension);
            var index = 0;

            foreach (var elementId in reader.EnumerateArray(tokenId))
            {
                if (index >= length)
                {
                    break;
                }

                indices[dimension] = index++;

                if (dimension == _rank - 1)
                {
                    try
                    {
                        array.SetValue(_converter.ReadObject(in reader, _elementType, elementId, null), indices);
                    }
                    catch (Exception e)
                    {
                        reader.HandleException(SerializationError.FailedToRead, e, array, _contract, indices[dimension],
                            elementId);
                    }
                }
                else
                {
                    ReadDimension(reader, elementId, array, indices, dimension + 1);
                }
            }
        }

        private void WriteMultidimensional(DataWriter writer, Array value, bool typeNeeded, bool isReference)
        {
            var lengths = new int[_rank];
            for (var i = 0; i < _rank; i++)
            {
                lengths[i] = 1;
                for (var j = i; j < _rank; j++)
                {
                    lengths[i] *= value.GetLength(j);
                }
            }

            var index = 0;
            using var scope = writer.BeginWriteArray(value.Length, _contract, typeNeeded, isReference, value);

            if (scope.Skipped)
            {
                return;
            }

            foreach (var element in value)
            {
                for (var i = 1; i < _rank; i++)
                {
                    if (index % lengths[i] == 0)
                    {
                        writer.WriteStartArray(lengths[i]);
                    }
                }

                try
                {
                    _converter.WriteObject(writer, _elementType, element);
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, _contract, index);
                }

                index++;

                for (var i = 1; i < _rank; i++)
                {
                    if (index % lengths[i] == 0)
                    {
                        writer.WriteEndArray();
                    }
                }
            }
        }
    }

    private class SzArrayConverter<T> : DataConverter<T?[]>
    {
        protected readonly DataContract _contract;
        protected readonly DataConverter<T> _converter;

        private readonly bool _fastPass;
        private readonly NumberHandling _numberHandling;
        private readonly bool _useArrayEmpty;
        private bool _isReference;

        public SzArrayConverter(SerializerSettings settings)
        {
            _converter = (DataConverter<T>)settings.GetConverter(typeof(T));
            _useArrayEmpty = settings.EmptyArrayHandling == EmptyArrayHandling.Shared;
            _numberHandling = settings.NumberHandling;

            _contract = settings.GetContract(typeof(T[]));
            _fastPass = settings.Error == null;

            if ((settings.PreserveReferencesHandling & PreserveReferencesHandling.Arrays) != 0)
            {
                _fastPass = false;
                _isReference = true;
            }

            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                _fastPass = false;
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 && _isReference)
            {
                var converter = (SzArrayConverter<T>)MemberwiseClone();
                converter._isReference = false;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override T?[]? Read(in DataReader reader, uint tokenId, T?[]? existingValue)
        {
            return _fastPass
                ? ReadFast(in reader, tokenId, existingValue)
                : ReadCore(in reader, tokenId, existingValue);
        }

        protected T?[] CreateSzArray(int length)
        {
            return _useArrayEmpty && length == 0 ? Array.Empty<T>() : new T?[length];
        }

        private T?[]? ReadCore(in DataReader reader, uint tokenId, T?[]? existingValue)
        {
            T[]? value = null;
            if (reader.ReadTypedArray(_contract, tokenId, ref value))
            {
                return value;
            }

            var list = existingValue ?? CreateSzArray(reader.GetValueCount(tokenId));

            var i = 0;
            foreach (var element in reader.EnumerateArray(tokenId))
            {
                try
                {
                    list[i] = _converter.Read(in reader, element, list[i]);
                }
                catch (Exception e)
                {
                    reader.HandleException(SerializationError.FailedToRead, e, list, _contract, i, element);
                }

                i++;
            }

            return list;
        }


        private T?[]? ReadFast(in DataReader reader, uint tokenId, T?[]? existingValue)
        {
            T?[]? value = null;
            if (reader.ReadTypedArray(_contract, tokenId, ref value))
            {
                return value;
            }

            var list = existingValue ?? CreateSzArray(reader.GetValueCount(tokenId));

            var i = 0;
            foreach (var element in reader.EnumerateArray(tokenId))
            {
                list[i] = _converter.Read(in reader, element, list[i]);
                i++;
            }

            return list;
        }


        public override void Write(DataWriter writer, T?[]? arr)
        {
            if (arr == null)
            {
                writer.WriteNull();
                return;
            }

            if (_fastPass)
            {
                WriteFast(writer, arr, false);
            }
            else
            {
                WriteData(writer, arr, false);
            }
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var typeNeeded = objectType != typeof(T[]);

            WriteData(writer, (T[])value, typeNeeded);
        }

        private void WriteData(DataWriter writer, T?[] value, bool typeNeeded)
        {
            using var scope = writer.BeginWriteArray(value.Length, _contract, typeNeeded, _isReference, value);

            if (scope.Skipped)
            {
                return;
            }

            for (var i = 0; i < value.Length; i++)
            {
                try
                {
                    _converter.Write(writer, value[i]);
                }
                catch (Exception e) when (!(e is SerializationException))
                {
                    writer.HandleException(e, value, _contract, i);
                }
            }
        }

        private void WriteFast(DataWriter writer, T?[] value, bool typeNeeded)
        {
            using var scope = writer.BeginWriteArray(value.Length, _contract, typeNeeded, false, value);

            if (scope.Skipped)
            {
                return;
            }

            if (typeof(T) == typeof(double))
            {
                writer.WriteDoubleValues(Unsafe.As<T?[], double[]>(ref value));
            }
            else if (typeof(T) == typeof(float))
            {
                writer.WriteSingleValues(Unsafe.As<T?[], float[]>(ref value));
            }
            else if (typeof(T) == typeof(Guid))
            {
                writer.WriteGuidValues(Unsafe.As<T?[], Guid[]>(ref value));
            }
            else if (typeof(T) == typeof(string))
            {
                writer.WriteStringValues(Unsafe.As<T?[], string[]>(ref value));
            }
            else if (typeof(T) == typeof(int))
            {
                writer.WriteInt32Values(Unsafe.As<T?[], int[]>(ref value));
            }
            else if (typeof(T) == typeof(long))
            {
                writer.WriteInt64Values(Unsafe.As<T?[], long[]>(ref value));
            }
            else if (typeof(T) == typeof(uint))
            {
                writer.WriteUInt32Values(Unsafe.As<T?[], uint[]>(ref value));
            }
            else if (typeof(T) == typeof(ulong))
            {
                writer.WriteUInt64Values(Unsafe.As<T?[], ulong[]>(ref value));
            }
            else if (typeof(T) == typeof(bool))
            {
                writer.WriteBooleanValues(Unsafe.As<T?[], bool[]>(ref value));
            }
            else
            {
                foreach (var element in value)
                {
                    _converter.Write(writer, element);
                }
            }
        }
    }


    private class ParallelSzArrayConverter<T> : SzArrayConverter<T>
    {
        private readonly int _minimumNumberOfElements;
        private readonly int _minimumNumberOfTokens;

        private readonly ParallelOptions _parallelOptions;

        public ParallelSzArrayConverter(SerializerSettings settings) : base(settings)
        {
            _parallelOptions = settings.GetParallelOptions();
            _minimumNumberOfElements = settings.ParallelOptions.MinimumNumberOfElements;
            _minimumNumberOfTokens = settings.ParallelOptions.MinimumNumberOfTokens;
        }

        public override T?[]? Read(in DataReader reader, uint tokenId, T?[]? existingValue)
        {
            if (reader.ReadTypedArray(_contract, tokenId, ref existingValue))
            {
                return existingValue;
            }

            var token = reader.GetToken(tokenId);
            var count = reader.GetValueCount(tokenId);

            var list = existingValue ?? CreateSzArray(count);

            var tokens = 0;

            if (count >= _minimumNumberOfElements)
            {
                if (token.LinkId > 0)
                {
                    tokens = (int)(token.LinkId - tokenId);
                }
                else
                {
                    var array = reader.EnumerateArray(tokenId);
                    array.MoveNext();

                    var valueId = array.Current;
                    var value = reader.GetToken(valueId);

                    if (value.IsContainer)
                    {
                        tokens = count * (int)(value.LinkId - valueId);
                    }
                    else
                    {
                        tokens = count;
                    }
                }
            }

            if (tokens >= _minimumNumberOfTokens)
            {
                var src = ArrayPool<uint>.Shared.Rent(count);

                var i = 0;
                foreach (var element in reader.EnumerateArray(tokenId))
                {
                    src[i] = element;
                    i++;
                }

                var rootElement = reader.RootElement;

                Parallel.For(0, count, _parallelOptions,
                    index => { list[index] = _converter.Read(new DataReader(rootElement), src[index], list[index]); });

                ArrayPool<uint>.Shared.Return(src);
            }
            else
            {
                var i = 0;
                foreach (var element in reader.EnumerateArray(tokenId))
                {
                    list[i] = _converter.Read(in reader, element, list[i]);
                    i++;
                }
            }

            return list;
        }
    }

    private class MultidimensionalArrayConverter<T, U> : DataConverter<T> where T : class, IList
    {
        private readonly int _arrayRank;
        private readonly DataContract _contract;

        private readonly DataConverter<U> _converter;
        private readonly bool _isReference;

        public MultidimensionalArrayConverter(SerializerSettings settings)
        {
            _converter = (DataConverter<U>)settings.GetConverter(typeof(U));
            _arrayRank = typeof(T).GetArrayRank();
            _contract = settings.GetContract(typeof(T));

            if ((settings.PreserveReferencesHandling & PreserveReferencesHandling.Arrays) != 0)
            {
                _isReference = true;
            }
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (reader.ReadTypedArray(_contract, tokenId, ref existingValue))
            {
                return existingValue;
            }

            var lengths = new int[_arrayRank];

            var valueId = tokenId;
            for (var i = 0; i < _arrayRank; i++)
            {
                lengths[i] = reader.GetValueCount(valueId);

                var array = reader.EnumerateArray(valueId);
                if (array.MoveNext())
                {
                    valueId = array.Current;
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            var arr = Array.CreateInstanceFromArrayType(_contract.Type, lengths);

            var iterator = new Document.ValueEnumerator[_arrayRank];
            var indices = new int[_arrayRank];
            iterator[0] = reader.EnumerateArray(tokenId).GetEnumerator();
            var sp = 0;
            var esp = _arrayRank - 1;

            for (;;)
            {
                if (iterator[sp].MoveNext())
                {
                    valueId = iterator[sp].Current;

                    if (sp == esp)
                    {
                        var value = _converter.Read(reader, valueId, default);
                        arr.SetValue(value, indices);
                        indices[sp]++;
                    }
                    else
                    {
                        iterator[++sp] = reader.EnumerateArray(valueId).GetEnumerator();
                        indices[sp] = 0;
                    }
                }
                else
                {
                    if (--sp < 0)
                    {
                        break;
                    }

                    indices[sp]++;
                }
            }

            return (T)(object)arr;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteData(writer, value, false);
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteData(writer, (T)value, objectType != typeof(T));
        }

        private void WriteData(DataWriter writer, T value, bool typeNeeded)
        {
            var arr = value as Array;

            if (arr == null)
            {
                throw new ArgumentNullException(nameof(value),
                    $"Expected an array instance for type '{_contract.Type}'.");
            }

            var lengths = new int[_arrayRank];
            for (var i = 0; i < _arrayRank; i++)
            {
                lengths[i] = 1;
                for (var j = i; j < _arrayRank; j++)
                {
                    lengths[i] *= arr.GetLength(j);
                }
            }

            var index = 0;
            using var scope = writer.BeginWriteArray(value.Count, _contract, typeNeeded, _isReference, value);

            if (scope.Skipped)
            {
                return;
            }

            foreach (var element in value)
            {
                for (var i = 1; i < lengths.Length; i++)
                {
                    if (index % lengths[i] == 0)
                    {
                        writer.WriteStartArray(lengths[i]);
                    }
                }

                _converter.Write(writer, (U)element);
                index++;

                for (var i = 1; i < lengths.Length; i++)
                {
                    if (index % lengths[i] == 0)
                    {
                        writer.WriteEndArray();
                    }
                }
            }
        }
    }
}