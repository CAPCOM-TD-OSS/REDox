// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace REDox;

public sealed class DocumentWriter : IDisposable, IAsyncDisposable, IBufferWriter<byte>
{
    private const int DefaultStackSize = 64;
    private const int DefaultBufferSize = 1024 * 16;
    private const int MinBufferSize = 16;
    private const int MaxBufferSize = 1024 * 1024;
    private const int ChunkHeaderSize = 8;

    private byte[] _buffer = Array.Empty<byte>();
    private int _bufferOffset;
    private int _bufferPosition;
    private IBufferWriter<byte>? _bufferWriter;

    private List<byte[]>? _buffers;
    private long _bytesCommitted;

    private long[] _contextStack = Array.Empty<long>();
    private Stack<Memory<byte>>? _placeHolders;
    private Stream? _stream;

    internal DocumentWriter()
    {
    }

    public DocumentWriter(Stream stream, int bufferSize = DefaultBufferSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Reset(stream, bufferSize);
    }

    public DocumentWriter(IBufferWriter<byte> bufferWriter, int bufferSize = DefaultBufferSize)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        Reset(bufferWriter, bufferSize);
    }

    public DocumentWriter(int bufferSize = DefaultBufferSize)
    {
        Reset(bufferSize);
    }

    public long BytesWritten => _bytesCommitted + (_bufferPosition - _bufferOffset) - ChunkHeaderSize;

    public int ContextDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        private set;
    }

    public bool IsStreaming => _stream != null || _bufferWriter != null;

    private bool HasPlaceholders => _placeHolders != null && _placeHolders.Count > 0;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushAsync();
        }
        finally
        {
            ClearBuffers();
        }
    }

    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        Reserve(sizeHint == 0 ? 1 : sizeHint);

        return _buffer.AsSpan(_bufferPosition);
    }

    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        Reserve(sizeHint == 0 ? 1 : sizeHint);

        return _buffer.AsMemory(_bufferPosition);
    }

    void IBufferWriter<byte>.Advance(int count)
    {
        if ((uint)count > (uint)(_buffer.Length - _bufferPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _bufferPosition += count;

        Debug.Assert(_bufferPosition <= _buffer.Length);
    }

    public void Dispose()
    {
        try
        {
            Flush();
        }
        finally
        {
            ClearBuffers();
        }
    }

    private void ClearBuffers()
    {
        _stream = null;
        _bufferWriter = null;

        ReleaseBuffers();

        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = Array.Empty<byte>();
        }

        if (_contextStack.Length > 0)
        {
            ArrayPool<long>.Shared.Return(_contextStack);
            _contextStack = Array.Empty<long>();
        }

        if (_placeHolders != null)
        {
            _placeHolders.Clear();
            _placeHolders = null;
        }

        _bufferPosition = ChunkHeaderSize;
        _bufferOffset = 0;
        _bytesCommitted = 0;
        ContextDepth = 0;
    }

    private void CommitBufferWriter(IBufferWriter<byte> bufferWriter)
    {
        var total = BytesWritten;

        foreach (var buf in EnumerateWrittenBuffers())
        {
            bufferWriter.Write(buf.Span);
        }

        ReleaseBuffers();
        _bytesCommitted = total;
        _bufferPosition = ChunkHeaderSize;
        _bufferOffset = 0;
    }

    private void CommitStream(Stream stream)
    {
        var total = BytesWritten;

        foreach (var buf in EnumerateWrittenBuffers())
        {
            stream.Write(buf.Span);
        }

        ReleaseBuffers();
        _bytesCommitted = total;
        _bufferPosition = ChunkHeaderSize;
        _bufferOffset = 0;
    }

    private async ValueTask CommitStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var total = BytesWritten;

        foreach (var buf in EnumerateWrittenBuffers())
        {
            await stream.WriteAsync(buf, cancellationToken);
        }

        ReleaseBuffers();
        _bytesCommitted = total;
        _bufferPosition = ChunkHeaderSize;
        _bufferOffset = 0;
    }

    public void Flush()
    {
        if (!IsStreaming)
        {
            return;
        }

        ThrowIfUnpatchedPlaceholders();

        if (_stream != null)
        {
            CommitStream(_stream);
            _stream.Flush();
        }

        if (_bufferWriter != null)
        {
            CommitBufferWriter(_bufferWriter);
        }
    }

    private void ReleaseBuffers()
    {
        if (_buffers != null)
        {
            foreach (var buf in _buffers)
            {
                ArrayPool<byte>.Shared.Return(buf);
            }

            _buffers.Clear();
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStreaming)
        {
            return;
        }

        ThrowIfUnpatchedPlaceholders();

        if (_stream != null)
        {
            await CommitStreamAsync(_stream, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        if (_bufferWriter != null)
        {
            CommitBufferWriter(_bufferWriter);
        }
    }

    public void Reset(Stream stream, int bufferSize = DefaultBufferSize)
    {
        ResetInternal(bufferSize, stream, null);
    }

    public void Reset(IBufferWriter<byte> bufferWriter, int bufferSize = DefaultBufferSize)
    {
        ResetInternal(bufferSize, null, bufferWriter);
    }

    public void Reset(int bufferSize = DefaultBufferSize)
    {
        ResetInternal(bufferSize, null, null);
    }

    internal void ResetInternal(int bufferSize, Stream? stream, IBufferWriter<byte>? writer)
    {
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize));
        }

        bufferSize = Math.Max(MinBufferSize, bufferSize);

        _stream = stream;
        _bufferWriter = writer;

        ReleaseBuffers();

        if (_buffer.Length < bufferSize)
        {
            if (_buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }

            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        }

        if (_placeHolders != null)
        {
            _placeHolders.Clear();
        }

        _bufferPosition = ChunkHeaderSize;
        _bufferOffset = 0;
        _bytesCommitted = 0;
        ContextDepth = 0;
    }

    private void ExpandStack()
    {
        var old = _contextStack;
        var size = Math.Max(DefaultStackSize, old.Length * 2);
        var next = ArrayPool<long>.Shared.Rent(size);

        if (ContextDepth > 0)
        {
            old.AsSpan(0, ContextDepth).CopyTo(next);
        }

        _contextStack = next;

        if (old.Length > 0)
        {
            ArrayPool<long>.Shared.Return(old);
        }
    }

    public void PushPlaceholder(int maxSize)
    {
        if (maxSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSize));
        }

        if (_bufferPosition + maxSize + ChunkHeaderSize * 2 > _buffer.Length)
        {
            CommitBuffer(maxSize + ChunkHeaderSize * 2);
        }

        var next = _bufferPosition;
        var count = _bufferPosition - _bufferOffset - ChunkHeaderSize;

        if (count > 0)
        {
            var longCount = (long)count;
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_bufferOffset), ((long)next << 32) | longCount);
            _bufferOffset = _bufferPosition;
            _bytesCommitted += count;
            _bufferPosition += ChunkHeaderSize;
            Debug.Assert(_bufferPosition <= _buffer.Length);
        }

        next = _bufferPosition + maxSize;
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_bufferOffset), (long)next << 32);

        if (_placeHolders == null)
        {
            _placeHolders = new Stack<Memory<byte>>();
        }

        _placeHolders.Push(_buffer.AsMemory(_bufferOffset, maxSize + ChunkHeaderSize));

        _bufferOffset = next;
        _bufferPosition = _bufferOffset + ChunkHeaderSize;
        Debug.Assert(_bufferPosition <= _buffer.Length);
    }

    public void PopAndPatch(ReadOnlySpan<byte> bytes)
    {
        if (_placeHolders == null || _placeHolders.Count == 0)
        {
            throw new InvalidOperationException("No placeholders to pop.");
        }

        var placeHolder = _placeHolders.Pop();

        var capacity = placeHolder.Length - ChunkHeaderSize;

        if (bytes.Length > capacity)
        {
            throw new ArgumentException(
                "Patch data is larger than the reserved placeholder size.",
                nameof(bytes));
        }

        var header = BinaryPrimitives.ReadInt64LittleEndian(placeHolder.Span);
        var longCount = (long)bytes.Length;
        BinaryPrimitives.WriteInt64LittleEndian(placeHolder.Span, (header & 0x7fffffff00000000L) | longCount);

        bytes.CopyTo(placeHolder.Span.Slice(ChunkHeaderSize));

        _bytesCommitted += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushContext(long context)
    {
        if (ContextDepth >= _contextStack.Length)
        {
            ExpandStack();
        }

        _contextStack[ContextDepth++] = context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref long PeekContext()
    {
        Debug.Assert(ContextDepth > 0, "Context stack is empty.");

        return ref _contextStack[ContextDepth - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long PopContext()
    {
        Debug.Assert(ContextDepth > 0, "Context stack is empty.");

        return _contextStack[--ContextDepth];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> Allocate(int bytesCommit)
    {
        Debug.Assert(bytesCommit >= 0, "Bytes to commit must be non-negative.");

        Reserve(bytesCommit);

        var bytes = _buffer.AsSpan(_bufferPosition, bytesCommit);
        _bufferPosition += bytesCommit;
        Debug.Assert(_bufferPosition <= _buffer.Length);

        return bytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> BeginWrite(int bytesReserved)
    {
        Debug.Assert(bytesReserved >= 0, "Bytes to commit must be non-negative.");

        Reserve(bytesReserved);

        return _buffer.AsSpan(_bufferPosition);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndWrite(int bytesConsumed)
    {
        Debug.Assert(bytesConsumed >= 0 && bytesConsumed <= _buffer.Length - _bufferPosition,
            "Invalid byte count consumed.");

        _bufferPosition += bytesConsumed;
        Debug.Assert(_bufferPosition <= _buffer.Length);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        Reserve(bytes.Length);

        bytes.CopyTo(_buffer.AsSpan(_bufferPosition));
        _bufferPosition += bytes.Length;
        Debug.Assert(_bufferPosition <= _buffer.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        Reserve(1);
        _buffer[_bufferPosition++] = value;
        Debug.Assert(_bufferPosition <= _buffer.Length);
    }

    public void CopyTo(Span<byte> buffer)
    {
        if (IsStreaming)
        {
            throw new NotSupportedException();
        }

        ThrowIfUnpatchedPlaceholders();

        var size = BytesWritten;

        if (size == 0)
        {
            return;
        }

        if (size > buffer.Length)
        {
            throw new ArgumentException("The destination buffer is too small to hold the written data.",
                nameof(buffer));
        }

        var pt = 0;
        foreach (var buf in EnumerateWrittenBuffers())
        {
            buf.Span.Slice(0, buf.Length).CopyTo(buffer.Slice(pt));
            pt += buf.Length;
        }
    }

    private void ThrowIfUnpatchedPlaceholders()
    {
        if (HasPlaceholders)
        {
            throw new InvalidOperationException("Cannot output while there are unpatched placeholders.");
        }
    }

    public byte[] ToArray()
    {
        if (IsStreaming)
        {
            throw new NotSupportedException();
        }

        ThrowIfUnpatchedPlaceholders();

        var size = BytesWritten;

        if (size == 0)
        {
            return Array.Empty<byte>();
        }

        if (size > Array.MaxLength)
        {
            throw new InvalidOperationException("The written data is too large to fit in a single byte array.");
        }

        var bytes = new byte[(int)size];
        var pt = 0;

        foreach (var buf in EnumerateWrittenBuffers())
        {
            buf.Span.CopyTo(bytes.AsSpan(pt));
            pt += buf.Length;
        }

        return bytes;
    }

    private void CommitBuffer(int size)
    {
        size += ChunkHeaderSize;

        if (IsStreaming && !HasPlaceholders)
        {
            if (_stream != null)
            {
                CommitStream(_stream);
            }

            if (_bufferWriter != null)
            {
                CommitBufferWriter(_bufferWriter);
            }

            if (_buffer.Length < size)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = ArrayPool<byte>.Shared.Rent(size);
            }

            return;
        }

        if (_buffers == null)
        {
            _buffers = new List<byte[]>();
        }

        var byteCount = _bufferPosition - _bufferOffset - ChunkHeaderSize;

        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_bufferOffset), byteCount);

        _buffers.Add(_buffer);

        var bufSize = Math.Min(_buffer.Length * 2, MaxBufferSize);

        if (size < bufSize)
        {
            size = bufSize;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(size);

        _bytesCommitted += byteCount;
        _bufferPosition = ChunkHeaderSize;
        _bufferOffset = 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Reserve(int size)
    {
        if (_bufferPosition + size > _buffer.Length)
        {
            CommitBuffer(size);
        }
    }

    internal BufferEnumerator EnumerateWrittenBuffers()
    {
        return new BufferEnumerator(this);
    }

    internal struct BufferEnumerator
    {
        private int _bufferListIndex;
        private byte[]? _currentBuffer;
        private int _next;
        private bool _readingCurrentBuffer;
        private readonly DocumentWriter _writer;

        internal BufferEnumerator(DocumentWriter writer)
        {
            _writer = writer;
            _bufferListIndex = -1;
            _currentBuffer = null;
            _next = 0;
            _readingCurrentBuffer = false;
            Current = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferEnumerator GetEnumerator()
        {
            return new BufferEnumerator(_writer);
        }

        public ReadOnlyMemory<byte> Current { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                if (_currentBuffer == null)
                {
                    if (_writer._buffers != null && ++_bufferListIndex < _writer._buffers.Count)
                    {
                        _currentBuffer = _writer._buffers[_bufferListIndex];
                        _next = 0;
                    }
                    else if (!_readingCurrentBuffer)
                    {
                        _readingCurrentBuffer = true;
                        _currentBuffer = _writer._buffer;
                        _next = 0;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (_readingCurrentBuffer && _next == _writer._bufferOffset)
                {
                    var count = _writer._bufferPosition - _writer._bufferOffset - ChunkHeaderSize;
                    if (count > 0)
                    {
                        Current = _currentBuffer.AsMemory(_next + ChunkHeaderSize, count);
                        _next = -1;
                        return true;
                    }

                    _currentBuffer = null;
                    return false;
                }

                if (_next < 0)
                {
                    _currentBuffer = null;
                    continue;
                }

                var header = BinaryPrimitives.ReadInt64LittleEndian(
                    _currentBuffer.AsSpan(_next, ChunkHeaderSize));

                var size = (int)header;
                var next = (int)(header >> 32);

                Current = _currentBuffer.AsMemory(_next + ChunkHeaderSize, size);
                _next = next;

                if (next <= 0)
                {
                    _currentBuffer = null;
                }

                return true;
            }
        }
    }
}