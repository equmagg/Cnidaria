using System;
using System.Collections.Generic;

namespace Cnidaria.RiscV;

public enum RVHostCallStatus : uint
{
    Ok = 0,
    UnknownFunction = 1,
    InvalidRequest = 2,
}

/// <summary>What a host function receives: the scalar arguments and the mailbox the guest shares with it.</summary>
public readonly ref struct RVHostCall
{
    public ulong Function { get; }
    public ReadOnlySpan<ulong> Arguments { get; }
    public Span<byte> Buffer { get; }

    internal RVHostCall(ulong function, ReadOnlySpan<ulong> arguments, Span<byte> buffer)
    {
        Function = function;
        Arguments = arguments;
        Buffer = buffer;
    }
}

public delegate long RVHostFunction(RVHostCall call);

/// <summary>
/// A mailbox the guest drives through plain loads and stores: it writes a function number, its
/// arguments and any payload, rings the doorbell, and reads the result back.
/// </summary>
public sealed class RVMmioHostBridge
{
    public const ulong RegisterWindowSize = 0x1000UL;
    public const ulong BufferOffset = 0x100UL;
    public const int ArgumentCount = 4;

    private const ulong OffsetMagic = 0x00;
    private const ulong OffsetVersion = 0x08;
    private const ulong OffsetBufferOffset = 0x10;
    private const ulong OffsetBufferSize = 0x18;
    private const ulong OffsetFunction = 0x20;
    private const ulong OffsetArguments = 0x28;
    private const ulong OffsetResult = 0x48;
    private const ulong OffsetStatus = 0x50;
    private const ulong OffsetDoorbell = 0x58;

    private const ulong MagicValue = 0x47445242_54534F48UL; // "HOSTBRDG"
    private const ulong Version = 1;

    private readonly ulong _base;
    private readonly byte[] _buffer;
    private readonly ulong[] _arguments = new ulong[ArgumentCount];
    private readonly Dictionary<ulong, RVHostFunction> _functions = new();
    private ulong _function;
    private ulong _result;
    private ulong _status;

    public ulong BaseAddress => _base;
    public int BufferSize => _buffer.Length;
    public Span<byte> Buffer => _buffer;

    public RVMmioHostBridge(ulong baseAddress)
    {
        if (baseAddress % RegisterWindowSize != 0)
            throw new ArgumentException("A host bridge occupies one page and starts on a page boundary.", nameof(baseAddress));
        _base = baseAddress;
        _buffer = new byte[RegisterWindowSize - BufferOffset];
    }

    public void Register(ulong function, RVHostFunction handler)
        => _functions[function] = handler ?? throw new ArgumentNullException(nameof(handler));

    public bool Unregister(ulong function)
        => _functions.Remove(function);

    public void Reset()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        Array.Clear(_arguments, 0, _arguments.Length);
        _function = 0;
        _result = 0;
        _status = 0;
    }

    public bool Contains(ulong address)
        => address - _base < RegisterWindowSize;

    public ulong Read(ulong address, int size)
    {
        var offset = address - _base;
        if (offset >= BufferOffset)
            return ReadBuffer((int)(offset - BufferOffset), size);

        var value = offset switch
        {
            OffsetMagic => MagicValue,
            OffsetVersion => Version,
            OffsetBufferOffset => BufferOffset,
            OffsetBufferSize => (ulong)_buffer.Length,
            OffsetFunction => _function,
            OffsetResult => _result,
            OffsetStatus => _status,
            _ => ReadArgument(offset),
        };
        return Slice(value, (int)(offset & 7), size);
    }

    public void Write(ulong address, int size, ulong value)
    {
        var offset = address - _base;
        if (offset >= BufferOffset)
        {
            WriteBuffer((int)(offset - BufferOffset), size, value);
            return;
        }
        if ((offset & 7) != 0 || size != 8)
            return;

        if (offset == OffsetFunction)
            _function = value;
        else if (offset >= OffsetArguments && offset < OffsetArguments + ArgumentCount * 8)
            _arguments[(int)((offset - OffsetArguments) / 8)] = value;
        else if (offset == OffsetDoorbell && value != 0)
            Invoke();
    }

    private void Invoke()
    {
        if (!_functions.TryGetValue(_function, out var handler))
        {
            _result = 0;
            _status = (ulong)RVHostCallStatus.UnknownFunction;
            return;
        }

        _result = (ulong)handler(new RVHostCall(_function, _arguments, _buffer));
        _status = (ulong)RVHostCallStatus.Ok;
    }

    private ulong ReadArgument(ulong offset)
        => offset >= OffsetArguments && offset < OffsetArguments + ArgumentCount * 8
            ? _arguments[(int)((offset - OffsetArguments) / 8)]
            : 0;

    private ulong ReadBuffer(int offset, int size)
    {
        if (offset < 0 || offset + size > _buffer.Length)
            return 0;
        ulong value = 0;
        for (var i = 0; i < size; i++)
            value |= (ulong)_buffer[offset + i] << (i * 8);
        return value;
    }

    private void WriteBuffer(int offset, int size, ulong value)
    {
        if (offset < 0 || offset + size > _buffer.Length)
            return;
        for (var i = 0; i < size; i++)
            _buffer[offset + i] = (byte)(value >> (i * 8));
    }

    private static ulong Slice(ulong value, int byteOffset, int size)
    {
        if (size == 8 && byteOffset == 0)
            return value;
        var bits = size * 8;
        var mask = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;
        return (value >> (byteOffset * 8)) & mask;
    }
}
