using System;

namespace Cnidaria.RiscV;

/// <summary>A virtio block device backed by a byte array, serving reads, writes and flushes</summary>
public sealed class RVMmioBlockDevice : RVVirtioMmioDevice
{
    public const ulong SectorSize = 512;

    private const uint DeviceId = 2;
    private const ulong FeatureBlockSize = 1UL << 6;
    private const uint QueueSize = 8;
    private const uint RequestIn = 0;
    private const uint RequestOut = 1;
    private const uint RequestFlush = 4;
    private const byte StatusOk = 0;
    private const byte StatusIoError = 1;
    private const byte StatusUnsupported = 2;

    private readonly byte[] _storage;

    public RVMmioBlockDevice(ulong baseAddress, int storageSize, int interruptSource = RVPlic.BlockDeviceSource)
        : base(baseAddress, DeviceId, interruptSource, queueCount: 1, QueueSize, FeatureBlockSize | FeatureVersion1)
    {
        if (storageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(storageSize));
        _storage = new byte[storageSize];
    }

    public byte[] Storage => _storage;
    public ulong CapacityBytes => (ulong)_storage.Length;
    public ulong CapacitySectors => (ulong)_storage.Length / SectorSize;

    public void LoadImage(byte[] image, int offset = 0)
    {
        if (image is null)
            throw new ArgumentNullException(nameof(image));
        if (offset < 0 || offset > _storage.Length || image.Length > _storage.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Buffer.BlockCopy(image, 0, _storage, offset, image.Length);
    }

    public void ClearStorage(int offset = 0, int length = -1)
    {
        if (length < 0)
            length = _storage.Length - offset;
        if (offset < 0 || length < 0 || offset > _storage.Length || length > _storage.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Array.Clear(_storage, offset, length);
    }

    protected override void ResetDevice()
    {
    }

    protected override ulong ReadConfig(ulong offset, int size)
    {
        ulong value = (offset & ~7UL) switch
        {
            0x00 => CapacitySectors,
            0x10 => SectorSize << 32,
            _ => 0,
        };
        return Slice(value, (int)(offset & 7), size);
    }

    protected override void QueueNotified(int queue, byte[] ram, ulong ramBase)
    {
        while (TryTakeAvailable(0, ram, ramBase, out ushort head))
            PushUsed(0, ram, ramBase, head, Execute(ram, ramBase, head));
    }

    // The header is the first buffer, the status byte the last, and the data whatever lies between
    private uint Execute(byte[] ram, ulong ramBase, ushort head)
    {
        if (!TryReadChain(0, ram, ramBase, head, out var chain) || chain.Length < 2)
            return 0;
        var header = chain[0];
        var status = chain[^1];
        if (!status.Writable || status.Length < 1)
            return 0;

        byte result = Transfer(chain, ram, ramBase, header, out uint transferred);
        if (RVGuestMemory.TryGetRamOffset(ram, ramBase, status.Address, 1, out int statusOffset))
            ram[statusOffset] = result;
        return transferred;
    }

    private byte Transfer(ReadOnlySpan<RVVirtqSegment> chain, byte[] ram, ulong ramBase, RVVirtqSegment header, out uint transferred)
    {
        transferred = 0;
        if (header.Writable || header.Length < 16 ||
            !RVGuestMemory.TryReadU32(ram, ramBase, header.Address, out uint type) ||
            !RVGuestMemory.TryReadU64(ram, ramBase, header.Address + 8, out ulong sector))
            return StatusIoError;
        if (type == RequestFlush)
            return StatusOk;
        if (type != RequestIn && type != RequestOut)
            return StatusUnsupported;
        if (sector > CapacitySectors)
            return StatusIoError;

        ulong storageOffset = sector * SectorSize;
        foreach (var data in chain[1..^1])
        {
            if (data.Writable != (type == RequestIn) || data.Length > (ulong)_storage.Length - storageOffset ||
                !RVGuestMemory.TryGetRamOffset(ram, ramBase, data.Address, (int)data.Length, out int ramOffset))
                return StatusIoError;
            if (type == RequestIn)
                Buffer.BlockCopy(_storage, (int)storageOffset, ram, ramOffset, (int)data.Length);
            else
                Buffer.BlockCopy(ram, ramOffset, _storage, (int)storageOffset, (int)data.Length);
            storageOffset += data.Length;
            transferred += data.Length;
        }
        return StatusOk;
    }
}
