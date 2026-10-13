using System;
using System.Threading;

namespace Cnidaria.RiscV;

public enum RVInputDeviceKind
{
    Keyboard,
    Mouse,
    Tablet,
}

/// <summary>
/// A virtio input device: the host queues evdev events and the device hands them to the driver in the
/// buffers it posts on the event queue, each packet closed by a SYN_REPORT the way Linux expects it.
/// </summary>
public sealed class RVVirtioInput : RVVirtioMmioDevice
{
    public const ushort EventSync = 0x00;
    public const ushort EventKey = 0x01;
    public const ushort EventRelative = 0x02;
    public const ushort EventAbsolute = 0x03;
    public const ushort EventLed = 0x11;
    public const ushort ButtonLeft = 0x110;
    public const ushort ButtonRight = 0x111;
    public const ushort ButtonMiddle = 0x112;
    public const ushort ButtonSide = 0x113;
    public const ushort ButtonExtra = 0x114;
    public const ushort RelativeX = 0x00;
    public const ushort RelativeY = 0x01;
    public const ushort RelativeHorizontalWheel = 0x06;
    public const ushort RelativeWheel = 0x08;
    public const ushort AbsoluteX = 0x00;
    public const ushort AbsoluteY = 0x01;
    public const int AbsoluteMaximum = 32767;
    public const ushort HighestKeyCode = 248;

    private const uint DeviceId = 18;
    private const uint QueueSize = 64;
    private const int EventQueue = 0;
    private const int StatusQueue = 1;
    private const int ConfigDataOffset = 8;
    private const int ConfigDataSize = 128;
    private const byte SelectName = 0x01;
    private const byte SelectSerial = 0x02;
    private const byte SelectDeviceIds = 0x03;
    private const byte SelectProperties = 0x10;
    private const byte SelectEventBits = 0x11;
    private const byte SelectAbsoluteInfo = 0x12;
    private const ushort BusVirtual = 0x06;
    private const ushort VendorQemu = 0x0627;
    private const int PropertyPointer = 0;

    private readonly RVInputDeviceKind _kind;
    private readonly ulong[] _pending;
    private readonly object _pendingLock = new();
    private readonly byte[] _config = new byte[ConfigDataOffset + ConfigDataSize];
    private int _pendingHead;
    private int _pendingCount;
    private long _droppedPackets;
    private uint _ledState;

    public RVVirtioInput(ulong baseAddress, RVInputDeviceKind kind, int capacity, int interruptSource)
        : base(baseAddress, DeviceId, interruptSource, queueCount: 2, QueueSize, FeatureVersion1)
    {
        if (capacity <= 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _kind = kind;
        _pending = new ulong[capacity];
    }

    public RVInputDeviceKind Kind => _kind;

    /// <summary>The keyboard lights as the driver last set them: bit 0 num lock, bit 1 caps lock, bit 2 scroll lock</summary>
    public uint LedState => _ledState;

    /// <summary>Packets lost because the guest had not taken the earlier ones yet</summary>
    public long DroppedPackets => Interlocked.Read(ref _droppedPackets);

    public bool EnqueueKey(ushort code, bool pressed, bool repeat = false)
        => EnqueuePacket(Pack(EventKey, code, repeat ? 2 : pressed ? 1 : 0));

    public bool EnqueueButton(ushort code, bool pressed)
        => EnqueuePacket(Pack(EventKey, code, pressed ? 1 : 0));

    public bool EnqueueMotion(int dx, int dy)
        => EnqueuePacket(Pack(EventRelative, RelativeX, dx), Pack(EventRelative, RelativeY, dy));

    /// <summary>Places a tablet's pointer, with both axes running from 0 to <see cref="AbsoluteMaximum"/></summary>
    public bool EnqueuePosition(int x, int y)
        => EnqueuePacket(Pack(EventAbsolute, AbsoluteX, Math.Clamp(x, 0, AbsoluteMaximum)), Pack(EventAbsolute, AbsoluteY, Math.Clamp(y, 0, AbsoluteMaximum)));

    public bool EnqueueWheel(int delta)
        => EnqueuePacket(Pack(EventRelative, RelativeWheel, delta));

    /// <summary>Queues events as one packet, appending the SYN_REPORT that ends it; a packet that does not fit is dropped whole</summary>
    public bool EnqueuePacket(params ReadOnlySpan<ulong> events)
    {
        lock (_pendingLock)
        {
            if (_pending.Length - _pendingCount < events.Length + 1)
            {
                _droppedPackets++;
                return false;
            }
            foreach (ulong value in events)
                Append(value);
            Append(Pack(EventSync, 0, 0));
            return true;
        }
    }

    public static ulong Pack(ushort type, ushort code, int value)
        => type | ((ulong)code << 16) | ((ulong)(uint)value << 32);

    public override void Poll(ulong time, byte[] ram, ulong ramBase)
    {
        if (Volatile.Read(ref _pendingCount) != 0)
            Deliver(ram, ramBase);
    }

    protected override void ResetDevice()
    {
        lock (_pendingLock)
        {
            _pendingHead = 0;
            _pendingCount = 0;
        }
        _ledState = 0;
        Array.Clear(_config);
    }

    protected override ulong ReadConfig(ulong offset, int size)
    {
        ulong value = 0;
        for (int i = size - 1; i >= 0; i--)
        {
            ulong index = offset + (ulong)i;
            value = (value << 8) | (index < (ulong)_config.Length ? _config[index] : 0UL);
        }
        return value;
    }

    protected override void WriteConfig(ulong offset, int size, ulong value)
    {
        for (int i = 0; i < size; i++)
        {
            ulong index = offset + (ulong)i;
            if (index < 2)
                _config[index] = (byte)(value >> (8 * i));
        }
        SelectConfig(_config[0], _config[1]);
    }

    protected override void QueueNotified(int queue, byte[] ram, ulong ramBase)
    {
        if (queue == EventQueue)
        {
            Deliver(ram, ramBase);
            return;
        }

        Span<byte> status = stackalloc byte[8];
        while (TryTakeAvailable(StatusQueue, ram, ramBase, out ushort head))
        {
            if (TryReadChain(StatusQueue, ram, ramBase, head, out var chain) && Gather(chain, ram, ramBase, status) == 8 &&
                BitConverter.ToUInt16(status) == EventLed)
            {
                int led = BitConverter.ToUInt16(status[2..]);
                if (led < 32)
                    _ledState = BitConverter.ToInt32(status[4..]) != 0 ? _ledState | (1U << led) : _ledState & ~(1U << led);
            }
            PushUsed(StatusQueue, ram, ramBase, head, 0);
        }
    }

    private void Append(ulong value)
    {
        int index = _pendingHead + _pendingCount;
        if (index >= _pending.Length)
            index -= _pending.Length;
        _pending[index] = value;
        _pendingCount++;
    }

    private void Deliver(byte[] ram, ulong ramBase)
    {
        if (!QueueReady(EventQueue))
            return;
        Span<byte> encoded = stackalloc byte[8];
        lock (_pendingLock)
        {
            while (_pendingCount != 0 && TryTakeAvailable(EventQueue, ram, ramBase, out ushort head))
            {
                ulong value = _pending[_pendingHead];
                BitConverter.TryWriteBytes(encoded, value);
                if (TryReadChain(EventQueue, ram, ramBase, head, out var chain) && Scatter(chain, ram, ramBase, encoded) == 8)
                {
                    _pendingHead = _pendingHead + 1 == _pending.Length ? 0 : _pendingHead + 1;
                    _pendingCount--;
                    PushUsed(EventQueue, ram, ramBase, head, 8);
                }
                else
                {
                    PushUsed(EventQueue, ram, ramBase, head, 0);
                }
            }
        }
    }

    private void SelectConfig(byte select, byte subselect)
    {
        Span<byte> data = _config.AsSpan(ConfigDataOffset);
        data.Clear();
        int size = select switch
        {
            SelectName => WriteString(data, _kind switch
            {
                RVInputDeviceKind.Keyboard => "Cnidaria Virtio Keyboard",
                RVInputDeviceKind.Mouse => "Cnidaria Virtio Mouse",
                _ => "Cnidaria Virtio Tablet",
            }),
            SelectSerial => WriteString(data, "cnidaria"),
            SelectDeviceIds => WriteDeviceIds(data),
            SelectProperties => _kind == RVInputDeviceKind.Mouse ? SetBit(data, PropertyPointer) : 0,
            SelectEventBits => WriteEventBits(data, subselect),
            SelectAbsoluteInfo => _kind == RVInputDeviceKind.Tablet && subselect <= AbsoluteY ? WriteAbsoluteInfo(data) : 0,
            _ => 0,
        };
        _config[2] = (byte)size;
    }

    private int WriteEventBits(Span<byte> data, byte type)
    {
        int size = 0;
        switch ((ushort)type, _kind)
        {
            case (EventKey, RVInputDeviceKind.Keyboard):
                for (int code = 1; code <= HighestKeyCode; code++)
                    size = SetBit(data, code);
                break;
            case (EventLed, RVInputDeviceKind.Keyboard):
                for (int led = 0; led < 3; led++)
                    size = SetBit(data, led);
                break;
            case (EventKey, _):
                for (int button = ButtonLeft; button <= ButtonExtra; button++)
                    size = SetBit(data, button);
                break;
            case (EventRelative, RVInputDeviceKind.Mouse):
                SetBit(data, RelativeX);
                SetBit(data, RelativeY);
                SetBit(data, RelativeHorizontalWheel);
                size = SetBit(data, RelativeWheel);
                break;
            case (EventRelative, RVInputDeviceKind.Tablet):
                size = SetBit(data, RelativeWheel);
                break;
            case (EventAbsolute, RVInputDeviceKind.Tablet):
                SetBit(data, AbsoluteX);
                size = SetBit(data, AbsoluteY);
                break;
        }
        return size;
    }

    private int WriteDeviceIds(Span<byte> data)
    {
        BitConverter.TryWriteBytes(data, BusVirtual);
        BitConverter.TryWriteBytes(data[2..], VendorQemu);
        BitConverter.TryWriteBytes(data[4..], (ushort)(1 + (int)_kind));
        BitConverter.TryWriteBytes(data[6..], (ushort)1);
        return 8;
    }

    private static int WriteAbsoluteInfo(Span<byte> data)
    {
        BitConverter.TryWriteBytes(data[4..], AbsoluteMaximum);
        return 20;
    }

    private static int WriteString(Span<byte> data, string text)
    {
        for (int i = 0; i < text.Length; i++)
            data[i] = (byte)text[i];
        return text.Length;
    }

    // Returns the bitmap size the bit makes necessary, so the last bit set decides how long the bitmap is
    private static int SetBit(Span<byte> data, int bit)
    {
        data[bit >> 3] |= (byte)(1 << (bit & 7));
        return (bit >> 3) + 1;
    }
}

/// <summary>Evdev key codes for the keys of the USB HID keyboard usage page, matched key by key</summary>
public static class RVInputKeys
{
    private static readonly byte[] HidToLinux = BuildHidTable();

    /// <summary>The evdev key code for a HID keyboard usage, or 0 when Linux has none for it</summary>
    public static ushort FromHidUsage(ushort usage)
        => usage < HidToLinux.Length ? HidToLinux[usage] : (ushort)0;

    private static byte[] BuildHidTable()
    {
        var table = new byte[0xE8];
        ReadOnlySpan<byte> letters = [30, 48, 46, 32, 18, 33, 34, 35, 23, 36, 37, 38, 50, 49, 24, 25, 16, 19, 31, 20, 22, 47, 17, 45, 21, 44];
        letters.CopyTo(table.AsSpan(0x04));
        for (int i = 0; i < 10; i++)
            table[0x1E + i] = (byte)(2 + i);
        ReadOnlySpan<byte> controls = [28, 1, 14, 15, 57, 12, 13, 26, 27, 43, 43, 39, 40, 41, 51, 52, 53, 58];
        controls.CopyTo(table.AsSpan(0x28));
        for (int i = 0; i < 10; i++)
            table[0x3A + i] = (byte)(59 + i);
        ReadOnlySpan<byte> navigation = [87, 88, 99, 70, 119, 110, 102, 104, 111, 107, 109, 106, 105, 108, 103, 69, 98, 55, 74, 78, 96, 79, 80, 81, 75, 76, 77, 71, 72, 73, 82, 83, 86, 127, 116, 117];
        navigation.CopyTo(table.AsSpan(0x44));
        for (int i = 0; i < 12; i++)
            table[0x68 + i] = (byte)(183 + i);
        ReadOnlySpan<byte> modifiers = [29, 42, 56, 125, 97, 54, 100, 126];
        modifiers.CopyTo(table.AsSpan(0xE0));
        return table;
    }
}
