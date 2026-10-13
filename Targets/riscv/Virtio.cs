using System;

namespace Cnidaria.RiscV;

/// <summary>One buffer of a virtqueue descriptor chain and the direction the device may use it in</summary>
public readonly struct RVVirtqSegment
{
    public readonly ulong Address;
    public readonly uint Length;
    public readonly bool Writable;

    public RVVirtqSegment(ulong address, uint length, bool writable)
    {
        Address = address;
        Length = length;
        Writable = writable;
    }
}

/// <summary>
/// The virtio-mmio transport, version 2, behind every virtio device of the machine: feature negotiation,
/// device status, split virtqueues and the device configuration space from offset 0x100 on.
/// </summary>
public abstract class RVVirtioMmioDevice
{
    public const ulong RegisterWindowSize = 0x1000UL;
    protected const ulong FeatureVersion1 = 1UL << 32;

    private const uint MagicValue = 0x74726976;
    private const uint Version = 2;
    private const uint VendorId = 0x434e4944;
    private const uint InterruptUsedBuffer = 1;
    private const uint StatusFeaturesOk = 8;
    private const ushort AvailableNoInterrupt = 1;
    private const ushort DescriptorNext = 1;
    private const ushort DescriptorWrite = 2;

    private struct Virtqueue
    {
        public uint Size;
        public bool Ready;
        public ulong Descriptors;
        public ulong Driver;
        public ulong Device;
        public ushort LastAvailable;
    }

    private readonly ulong _base;
    private readonly uint _deviceId;
    private readonly ulong _deviceFeatures;
    private readonly uint _queueSizeMax;
    private readonly Virtqueue[] _queues;
    private readonly RVVirtqSegment[] _chain;
    private uint _deviceFeaturesSel;
    private uint _driverFeaturesSel;
    private ulong _driverFeatures;
    private uint _queueSel;
    private uint _interruptStatus;
    private uint _status;
    private uint _configGeneration;

    protected RVVirtioMmioDevice(ulong baseAddress, uint deviceId, int interruptSource, int queueCount, uint queueSizeMax, ulong deviceFeatures)
    {
        if (baseAddress % RegisterWindowSize != 0)
            throw new ArgumentException("A virtio device occupies one page and starts on a page boundary.", nameof(baseAddress));
        if (queueCount <= 0 || queueSizeMax == 0 || (queueSizeMax & (queueSizeMax - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(queueSizeMax));
        _base = baseAddress;
        _deviceId = deviceId;
        _deviceFeatures = deviceFeatures;
        _queueSizeMax = queueSizeMax;
        _queues = new Virtqueue[queueCount];
        _chain = new RVVirtqSegment[queueSizeMax];
        InterruptSource = interruptSource;
    }

    public ulong BaseAddress => _base;
    public int InterruptSource { get; }
    public bool InterruptPending => _interruptStatus != 0;
    protected bool DriverReady => (_status & 4) != 0;

    public bool Contains(ulong address)
        => address - _base < RegisterWindowSize;

    public void Reset()
    {
        _deviceFeaturesSel = 0;
        _driverFeaturesSel = 0;
        _driverFeatures = 0;
        _queueSel = 0;
        Array.Clear(_queues);
        _interruptStatus = 0;
        _status = 0;
        _configGeneration++;
        ResetDevice();
    }

    /// <summary>Lets a device act on its own, such as delivering input or playing sound, at the guest time given in timebase ticks</summary>
    public virtual void Poll(ulong time, byte[] ram, ulong ramBase)
    {
    }

    protected abstract void ResetDevice();

    protected abstract ulong ReadConfig(ulong offset, int size);

    protected virtual void WriteConfig(ulong offset, int size, ulong value)
    {
    }

    protected abstract void QueueNotified(int queue, byte[] ram, ulong ramBase);

    public ulong Read(ulong address, int size)
    {
        ulong offset = address - _base;
        if (offset >= 0x100)
            return ReadConfig(offset - 0x100, size);

        bool selected = _queueSel < (uint)_queues.Length;
        ref Virtqueue queue = ref _queues[selected ? (int)_queueSel : 0];
        uint value = (offset & ~3UL) switch
        {
            0x000 => MagicValue,
            0x004 => Version,
            0x008 => _deviceId,
            0x00c => VendorId,
            0x010 => _deviceFeaturesSel switch { 0 => (uint)_deviceFeatures, 1 => (uint)(_deviceFeatures >> 32), _ => 0 },
            0x014 => _deviceFeaturesSel,
            0x020 => _driverFeaturesSel switch { 0 => (uint)_driverFeatures, 1 => (uint)(_driverFeatures >> 32), _ => 0 },
            0x024 => _driverFeaturesSel,
            0x030 => _queueSel,
            0x034 => selected ? _queueSizeMax : 0,
            0x038 => selected ? queue.Size : 0,
            0x044 => selected && queue.Ready ? 1U : 0,
            0x060 => _interruptStatus,
            0x070 => _status,
            0x080 => selected ? (uint)queue.Descriptors : 0,
            0x084 => selected ? (uint)(queue.Descriptors >> 32) : 0,
            0x090 => selected ? (uint)queue.Driver : 0,
            0x094 => selected ? (uint)(queue.Driver >> 32) : 0,
            0x0a0 => selected ? (uint)queue.Device : 0,
            0x0a4 => selected ? (uint)(queue.Device >> 32) : 0,
            0x0fc => _configGeneration,
            _ => 0,
        };
        return Slice(value, (int)(offset & 3), size);
    }

    public void Write(ulong address, int size, ulong value, byte[] ram, ulong ramBase)
    {
        ulong offset = address - _base;
        if (offset >= 0x100)
        {
            WriteConfig(offset - 0x100, size, value);
            return;
        }

        ulong register = offset & ~3UL;
        uint old = (uint)Read(_base + register, 4);
        uint written = (uint)Merge(old, (int)(offset & 3), size, value);
        bool selected = _queueSel < (uint)_queues.Length;
        ref Virtqueue queue = ref _queues[selected ? (int)_queueSel : 0];
        switch (register)
        {
            case 0x014:
                _deviceFeaturesSel = written;
                break;
            case 0x020:
                if (_driverFeaturesSel == 0)
                    _driverFeatures = (_driverFeatures & 0xffffffff00000000UL) | written;
                else if (_driverFeaturesSel == 1)
                    _driverFeatures = (_driverFeatures & 0xffffffffUL) | ((ulong)written << 32);
                break;
            case 0x024:
                _driverFeaturesSel = written;
                break;
            case 0x030:
                _queueSel = written;
                break;
            case 0x038:
                if (selected)
                    queue.Size = Math.Min(written, _queueSizeMax);
                break;
            case 0x044:
                if (selected)
                {
                    queue.Ready = (written & 1) != 0;
                    if (!queue.Ready)
                        queue.LastAvailable = 0;
                }
                break;
            case 0x050:
                if (written < (uint)_queues.Length && _queues[written].Ready)
                    QueueNotified((int)written, ram, ramBase);
                break;
            case 0x064:
                _interruptStatus &= ~written;
                break;
            case 0x070:
                if (written == 0)
                {
                    Reset();
                }
                else
                {
                    _status = written;
                    if ((_status & StatusFeaturesOk) != 0 && (_driverFeatures & ~_deviceFeatures) != 0)
                        _status &= ~StatusFeaturesOk;
                }
                break;
            case 0x080:
                if (selected)
                    queue.Descriptors = (queue.Descriptors & 0xffffffff00000000UL) | written;
                break;
            case 0x084:
                if (selected)
                    queue.Descriptors = (queue.Descriptors & 0xffffffffUL) | ((ulong)written << 32);
                break;
            case 0x090:
                if (selected)
                    queue.Driver = (queue.Driver & 0xffffffff00000000UL) | written;
                break;
            case 0x094:
                if (selected)
                    queue.Driver = (queue.Driver & 0xffffffffUL) | ((ulong)written << 32);
                break;
            case 0x0a0:
                if (selected)
                    queue.Device = (queue.Device & 0xffffffff00000000UL) | written;
                break;
            case 0x0a4:
                if (selected)
                    queue.Device = (queue.Device & 0xffffffffUL) | ((ulong)written << 32);
                break;
        }
    }

    protected bool QueueReady(int queue)
        => _queues[queue].Ready && _queues[queue].Size != 0;

    /// <summary>Takes the head of the next chain the driver made available, if there is one</summary>
    protected bool TryTakeAvailable(int queueIndex, byte[] ram, ulong ramBase, out ushort head)
    {
        head = 0;
        ref Virtqueue queue = ref _queues[queueIndex];
        if (!queue.Ready || queue.Size == 0)
            return false;
        if (!RVGuestMemory.TryReadU16(ram, ramBase, queue.Driver + 2, out ushort available) || available == queue.LastAvailable)
            return false;
        if (!RVGuestMemory.TryReadU16(ram, ramBase, queue.Driver + 4 + 2UL * (queue.LastAvailable % queue.Size), out head))
            return false;
        queue.LastAvailable++;
        return true;
    }

    /// <summary>The buffers of a chain in order; the span is only valid until the next chain is read</summary>
    protected bool TryReadChain(int queueIndex, byte[] ram, ulong ramBase, ushort head, out ReadOnlySpan<RVVirtqSegment> segments)
    {
        segments = default;
        ref Virtqueue queue = ref _queues[queueIndex];
        ushort index = head;
        int count = 0;
        while (true)
        {
            if (index >= queue.Size || count == _chain.Length)
                return false;
            ulong address = queue.Descriptors + 16UL * index;
            if (!RVGuestMemory.TryReadU64(ram, ramBase, address, out ulong bufferAddress) ||
                !RVGuestMemory.TryReadU32(ram, ramBase, address + 8, out uint length) ||
                !RVGuestMemory.TryReadU16(ram, ramBase, address + 12, out ushort flags) ||
                !RVGuestMemory.TryReadU16(ram, ramBase, address + 14, out ushort next))
                return false;
            _chain[count++] = new RVVirtqSegment(bufferAddress, length, (flags & DescriptorWrite) != 0);
            if ((flags & DescriptorNext) == 0)
                break;
            index = next;
        }
        segments = new ReadOnlySpan<RVVirtqSegment>(_chain, 0, count);
        return true;
    }

    /// <summary>Hands a chain back to the driver with the number of bytes the device wrote into it</summary>
    protected void PushUsed(int queueIndex, byte[] ram, ulong ramBase, ushort head, uint written)
    {
        ref Virtqueue queue = ref _queues[queueIndex];
        if (!RVGuestMemory.TryReadU16(ram, ramBase, queue.Device + 2, out ushort usedIndex))
            return;
        ulong element = queue.Device + 4 + 8UL * (usedIndex % queue.Size);
        if (!RVGuestMemory.TryWriteU32(ram, ramBase, element, head) ||
            !RVGuestMemory.TryWriteU32(ram, ramBase, element + 4, written) ||
            !RVGuestMemory.TryWriteU16(ram, ramBase, queue.Device + 2, unchecked((ushort)(usedIndex + 1))))
            return;
        if (!RVGuestMemory.TryReadU16(ram, ramBase, queue.Driver, out ushort flags) || (flags & AvailableNoInterrupt) == 0)
            _interruptStatus |= InterruptUsedBuffer;
    }

    /// <summary>Copies the device-readable part of a chain into a buffer, returning how much there was to copy</summary>
    protected static int Gather(ReadOnlySpan<RVVirtqSegment> segments, byte[] ram, ulong ramBase, Span<byte> destination)
    {
        int copied = 0;
        foreach (var segment in segments)
        {
            if (segment.Writable || copied == destination.Length)
                continue;
            int count = (int)Math.Min(segment.Length, (uint)(destination.Length - copied));
            if (!RVGuestMemory.TryGetRamOffset(ram, ramBase, segment.Address, count, out int offset))
                return -1;
            ram.AsSpan(offset, count).CopyTo(destination[copied..]);
            copied += count;
        }
        return copied;
    }

    /// <summary>Copies a reply into the device-writable part of a chain, returning how much of it fit</summary>
    protected static int Scatter(ReadOnlySpan<RVVirtqSegment> segments, byte[] ram, ulong ramBase, ReadOnlySpan<byte> source)
    {
        int copied = 0;
        foreach (var segment in segments)
        {
            if (!segment.Writable || copied == source.Length)
                continue;
            int count = (int)Math.Min(segment.Length, (uint)(source.Length - copied));
            if (!RVGuestMemory.TryGetRamOffset(ram, ramBase, segment.Address, count, out int offset))
                return -1;
            source.Slice(copied, count).CopyTo(ram.AsSpan(offset, count));
            copied += count;
        }
        return copied;
    }

    protected static ulong Slice(ulong value, int byteOffset, int size)
    {
        if (size == 8 && byteOffset == 0)
            return value;
        int bits = size * 8;
        ulong mask = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;
        return (value >> (byteOffset * 8)) & mask;
    }

    private static ulong Merge(ulong old, int byteOffset, int size, ulong value)
    {
        if (size >= 4 && byteOffset == 0)
            return value;
        int bits = size * 8;
        ulong mask = ((1UL << bits) - 1) << (byteOffset * 8);
        return (old & ~mask) | ((value << (byteOffset * 8)) & mask);
    }
}

/// <summary>Bounds-checked little-endian access to guest RAM for devices that read and write it directly</summary>
internal static class RVGuestMemory
{
    public static bool TryGetRamOffset(byte[] ram, ulong ramBase, ulong address, int length, out int offset)
    {
        offset = 0;
        if (length < 0 || length > ram.Length)
            return false;
        ulong relative = address - ramBase;
        if (address < ramBase || relative > (ulong)(ram.Length - length))
            return false;
        offset = (int)relative;
        return true;
    }

    public static bool TryReadU16(byte[] ram, ulong ramBase, ulong address, out ushort value)
    {
        value = 0;
        if (!TryGetRamOffset(ram, ramBase, address, 2, out int offset))
            return false;
        value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(ram.AsSpan(offset));
        return true;
    }

    public static bool TryReadU32(byte[] ram, ulong ramBase, ulong address, out uint value)
    {
        value = 0;
        if (!TryGetRamOffset(ram, ramBase, address, 4, out int offset))
            return false;
        value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(ram.AsSpan(offset));
        return true;
    }

    public static bool TryReadU64(byte[] ram, ulong ramBase, ulong address, out ulong value)
    {
        value = 0;
        if (!TryGetRamOffset(ram, ramBase, address, 8, out int offset))
            return false;
        value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(ram.AsSpan(offset));
        return true;
    }

    public static bool TryWriteU16(byte[] ram, ulong ramBase, ulong address, ushort value)
    {
        if (!TryGetRamOffset(ram, ramBase, address, 2, out int offset))
            return false;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(ram.AsSpan(offset), value);
        return true;
    }

    public static bool TryWriteU32(byte[] ram, ulong ramBase, ulong address, uint value)
    {
        if (!TryGetRamOffset(ram, ramBase, address, 4, out int offset))
            return false;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ram.AsSpan(offset), value);
        return true;
    }
}
