using System;

namespace Cnidaria.RiscV;

/// <summary>
/// A linear framebuffer: one page of registers describing the mode, then the pixels themselves,
/// which the guest writes with ordinary stores so a fill costs no more than a store to RAM.
/// </summary>
public sealed class RVFramebuffer
{
    public const ulong RegisterWindowSize = 0x1000UL;
    public const int BytesPerPixel = 4;

    private const ulong OffsetMagic = 0x00;
    private const ulong OffsetVersion = 0x08;
    private const ulong OffsetWidth = 0x10;
    private const ulong OffsetHeight = 0x18;
    private const ulong OffsetStride = 0x20;
    private const ulong OffsetPixelOffset = 0x28;
    private const ulong OffsetPixelBytes = 0x30;
    private const ulong OffsetFrameCount = 0x38;
    private const ulong OffsetPresent = 0x40;

    private const ulong MagicValue = 0x465542454D415246UL; // "FRAMEBUF"
    private const ulong Version = 1;

    private readonly ulong _base;
    private readonly byte[] _pixels;
    private ulong _frameCount;

    public ulong BaseAddress => _base;
    public ulong PixelBaseAddress => checked(_base + RegisterWindowSize);
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels => _pixels;
    public ulong WindowSize => checked(RegisterWindowSize + (ulong)_pixels.Length);

    /// <summary>Counts the frames the guest has finished, so a host knows when the pixels are worth reading.</summary>
    public ulong FrameCount => _frameCount;

    public RVFramebuffer(ulong baseAddress, int width, int height)
    {
        if (baseAddress % RegisterWindowSize != 0)
            throw new ArgumentException("A framebuffer starts on a page boundary.", nameof(baseAddress));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        _base = baseAddress;
        Width = width;
        Height = height;
        Stride = checked(width * BytesPerPixel);
        _pixels = new byte[checked(Stride * height)];
    }

    public void Reset()
    {
        Array.Clear(_pixels, 0, _pixels.Length);
        _frameCount = 0;
    }

    public bool ContainsRegister(ulong address)
        => address - _base < RegisterWindowSize;

    public uint GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        var offset = y * Stride + x * BytesPerPixel;
        return (uint)(_pixels[offset] | (_pixels[offset + 1] << 8) | (_pixels[offset + 2] << 16) | (_pixels[offset + 3] << 24));
    }

    public ulong Read(ulong address, int size)
    {
        var offset = address - _base;
        var value = offset switch
        {
            OffsetMagic => MagicValue,
            OffsetVersion => Version,
            OffsetWidth => (ulong)Width,
            OffsetHeight => (ulong)Height,
            OffsetStride => (ulong)Stride,
            OffsetPixelOffset => RegisterWindowSize,
            OffsetPixelBytes => (ulong)_pixels.Length,
            OffsetFrameCount => _frameCount,
            _ => 0UL,
        };
        return Slice(value, (int)(offset & 7), size);
    }

    public void Write(ulong address, int size, ulong value)
    {
        var offset = address - _base;
        if ((offset & 7) != 0 || size != 8)
            return;
        if (offset == OffsetPresent && value != 0)
            _frameCount++;
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
