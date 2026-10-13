using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Cnidaria.RiscV;

/// <summary>
/// A virtio sound card with one playback stream. Buffers the driver queues are played at the rate the
/// stream was set to, measured against guest time, and their samples land in a ring the host reads as
/// interleaved floats; a buffer goes back to the driver once it has been played, which is what paces it.
/// </summary>
public sealed class RVVirtioSound : RVVirtioMmioDevice
{
    public const int MaximumChannels = 2;

    private const uint DeviceId = 25;
    private const uint QueueSize = 64;
    private const int ControlQueue = 0;
    private const int EventQueue = 1;
    private const int TransmitQueue = 2;
    private const int ReceiveQueue = 3;

    private const uint RequestJackInfo = 1;
    private const uint RequestPcmInfo = 0x0100;
    private const uint RequestPcmSetParameters = 0x0101;
    private const uint RequestPcmPrepare = 0x0102;
    private const uint RequestPcmRelease = 0x0103;
    private const uint RequestPcmStart = 0x0104;
    private const uint RequestPcmStop = 0x0105;
    private const uint RequestChannelMapInfo = 0x0200;
    private const uint StatusOk = 0x8000;
    private const uint StatusBadMessage = 0x8001;
    private const uint StatusNotSupported = 0x8002;
    private const uint StatusIoError = 0x8003;

    private const byte FormatU8 = 4;
    private const byte FormatS16 = 5;
    private const byte FormatS32 = 17;
    private const byte FormatFloat = 19;
    private const ulong SupportedFormats = (1UL << FormatU8) | (1UL << FormatS16) | (1UL << FormatS32) | (1UL << FormatFloat);
    private static readonly int[] RateTable = [5512, 8000, 11025, 16000, 22050, 32000, 44100, 48000];
    private const ulong SupportedRates = 0xFE;
    private const int PcmInfoSize = 32;

    private enum StreamState
    {
        Idle,
        Configured,
        Prepared,
        Running,
    }

    private sealed class Transfer
    {
        public ushort Head;
        public byte[] Data = Array.Empty<byte>();
        public int Length;
        public int Played;
        public ulong StatusAddress;
    }

    private readonly ulong _timebase;
    private readonly Queue<Transfer> _transfers = new();
    private readonly float[] _samples;
    private readonly object _samplesLock = new();
    private StreamState _state;
    private int _channels;
    private byte _format;
    private int _rate;
    private ulong _now;
    private ulong _lastPlayed;
    private ulong _frameCredit;
    private int _sampleHead;
    private int _sampleCount;
    private long _droppedSamples;

    public RVVirtioSound(ulong baseAddress, ulong timebaseFrequency, int interruptSource, int sampleCapacity = 2 * 48000 * MaximumChannels)
        : base(baseAddress, DeviceId, interruptSource, queueCount: 4, QueueSize, FeatureVersion1)
    {
        if (timebaseFrequency == 0)
            throw new ArgumentOutOfRangeException(nameof(timebaseFrequency));
        if (sampleCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCapacity));
        _timebase = timebaseFrequency;
        _samples = new float[sampleCapacity];
    }

    /// <summary>The rate of the samples <see cref="ReadSamples"/> returns, or 0 before the guest has chosen one</summary>
    public int SampleRate => _rate;
    public int Channels => _channels;
    public bool Playing => _state == StreamState.Running;

    public int AvailableSamples
    {
        get
        {
            lock (_samplesLock)
                return _sampleCount;
        }
    }

    /// <summary>Samples the host let the ring overflow with, oldest first</summary>
    public long DroppedSamples
    {
        get
        {
            lock (_samplesLock)
                return _droppedSamples;
        }
    }

    /// <summary>Takes played samples, interleaved by channel and scaled to [-1, 1]</summary>
    public int ReadSamples(Span<float> destination)
    {
        lock (_samplesLock)
        {
            int count = Math.Min(destination.Length, _sampleCount);
            int first = Math.Min(count, _samples.Length - _sampleHead);
            _samples.AsSpan(_sampleHead, first).CopyTo(destination);
            _samples.AsSpan(0, count - first).CopyTo(destination[first..]);
            _sampleHead = (_sampleHead + count) % _samples.Length;
            _sampleCount -= count;
            return count;
        }
    }

    protected override void ResetDevice()
    {
        while (_transfers.Count != 0)
            Release(_transfers.Dequeue());
        _state = StreamState.Idle;
        _channels = 0;
        _format = 0;
        _rate = 0;
        _frameCredit = 0;
        lock (_samplesLock)
        {
            _sampleHead = 0;
            _sampleCount = 0;
        }
    }

    protected override ulong ReadConfig(ulong offset, int size)
    {
        ulong value = (offset & ~7UL) switch
        {
            0x00 => 1UL << 32,
            _ => 0,
        };
        return Slice(value, (int)(offset & 7), size);
    }

    protected override void QueueNotified(int queue, byte[] ram, ulong ramBase)
    {
        switch (queue)
        {
            case ControlQueue:
                while (TryTakeAvailable(ControlQueue, ram, ramBase, out ushort head))
                    PushUsed(ControlQueue, ram, ramBase, head, Control(ram, ramBase, head));
                break;
            case TransmitQueue:
                while (TryTakeAvailable(TransmitQueue, ram, ramBase, out ushort head))
                    Queue(ram, ramBase, head);
                break;
            case ReceiveQueue:
                while (TryTakeAvailable(ReceiveQueue, ram, ramBase, out ushort head))
                    PushUsed(ReceiveQueue, ram, ramBase, head, CompleteRejected(ReceiveQueue, ram, ramBase, head));
                break;
        }
    }

    public override void Poll(ulong time, byte[] ram, ulong ramBase)
    {
        _now = time;
        if (_state != StreamState.Running || _transfers.Count == 0)
        {
            // Time with nothing to play is not saved up, or the next buffer would go out in a burst
            _lastPlayed = time;
            _frameCredit = 0;
            return;
        }

        _frameCredit += (time - _lastPlayed) * (ulong)_rate;
        _lastPlayed = time;
        ulong frames = _frameCredit / _timebase;
        _frameCredit %= _timebase;
        int frameBytes = _channels * BytesPerSample(_format);
        while (frames != 0 && _transfers.Count != 0)
        {
            var transfer = _transfers.Peek();
            int available = (transfer.Length - transfer.Played) / frameBytes;
            int taken = (int)Math.Min((ulong)available, frames);
            Emit(transfer.Data.AsSpan(transfer.Played, taken * frameBytes));
            transfer.Played += taken * frameBytes;
            frames -= (ulong)taken;
            if (transfer.Length - transfer.Played < frameBytes)
            {
                _transfers.Dequeue();
                Complete(ram, ramBase, transfer, StatusOk);
            }
        }
    }

    private uint Control(byte[] ram, ulong ramBase, ushort head)
    {
        Span<byte> request = stackalloc byte[32];
        Span<byte> reply = stackalloc byte[4 + PcmInfoSize];
        if (!TryReadChain(ControlQueue, ram, ramBase, head, out var chain))
            return 0;
        int length = Gather(chain, ram, ramBase, request);
        uint status = StatusBadMessage;
        int replyLength = 4;
        if (length >= 4)
        {
            uint code = BinaryPrimitives.ReadUInt32LittleEndian(request);
            uint stream = length >= 8 ? BinaryPrimitives.ReadUInt32LittleEndian(request[4..]) : uint.MaxValue;
            status = code switch
            {
                RequestPcmInfo => PcmInfo(request[..length], reply, out replyLength),
                RequestPcmSetParameters when length >= 24 && stream == 0 => SetParameters(request),
                RequestPcmPrepare when stream == 0 => Prepare(),
                RequestPcmRelease when stream == 0 => ReleaseStream(ram, ramBase),
                RequestPcmStart when stream == 0 => Start(),
                RequestPcmStop when stream == 0 => Stop(),
                RequestJackInfo or RequestChannelMapInfo => StatusNotSupported,
                _ => StatusBadMessage,
            };
        }
        BinaryPrimitives.WriteUInt32LittleEndian(reply, status);
        int written = Scatter(chain, ram, ramBase, reply[..replyLength]);
        return written < 0 ? 0 : (uint)written;
    }

    private static uint PcmInfo(ReadOnlySpan<byte> request, Span<byte> reply, out int replyLength)
    {
        replyLength = 4;
        if (request.Length < 16)
            return StatusBadMessage;
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(request[4..]);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(request[8..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(request[12..]);
        if (count == 0)
            return StatusOk;
        if (start != 0 || count != 1 || size < PcmInfoSize)
            return StatusBadMessage;
        var info = reply.Slice(4, PcmInfoSize);
        info.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(info[8..], SupportedFormats);
        BinaryPrimitives.WriteUInt64LittleEndian(info[16..], SupportedRates);
        info[24] = 0;
        info[25] = 1;
        info[26] = MaximumChannels;
        replyLength = 4 + PcmInfoSize;
        return StatusOk;
    }

    private uint SetParameters(ReadOnlySpan<byte> request)
    {
        uint periodBytes = BinaryPrimitives.ReadUInt32LittleEndian(request[12..]);
        byte channels = request[20];
        byte format = request[21];
        byte rate = request[22];
        if (_state == StreamState.Running || channels < 1 || channels > MaximumChannels || periodBytes == 0 ||
            format >= 64 || (SupportedFormats & (1UL << format)) == 0 || rate >= RateTable.Length || (SupportedRates & (1UL << rate)) == 0)
            return StatusBadMessage;
        _channels = channels;
        _format = format;
        _rate = RateTable[rate];
        _state = StreamState.Configured;
        return StatusOk;
    }

    private uint Prepare()
    {
        if (_state == StreamState.Idle || _state == StreamState.Running)
            return StatusBadMessage;
        _state = StreamState.Prepared;
        return StatusOk;
    }

    private uint Start()
    {
        if (_state != StreamState.Prepared)
            return StatusBadMessage;
        _state = StreamState.Running;
        _lastPlayed = _now;
        _frameCredit = 0;
        return StatusOk;
    }

    private uint Stop()
    {
        if (_state != StreamState.Running)
            return StatusBadMessage;
        _state = StreamState.Prepared;
        return StatusOk;
    }

    // Releasing gives back every buffer the stream still holds, played or not
    private uint ReleaseStream(byte[] ram, ulong ramBase)
    {
        if (_state == StreamState.Running)
            return StatusBadMessage;
        while (_transfers.Count != 0)
            Complete(ram, ramBase, _transfers.Dequeue(), StatusOk);
        _state = StreamState.Configured;
        return StatusOk;
    }

    // A transfer is the stream id, the samples, and the status the device fills in when it is done with them
    private void Queue(byte[] ram, ulong ramBase, ushort head)
    {
        if (!TryReadChain(TransmitQueue, ram, ramBase, head, out var chain) || chain.Length < 2 || !chain[^1].Writable || chain[^1].Length < 8)
        {
            PushUsed(TransmitQueue, ram, ramBase, head, 0);
            return;
        }

        int length = 0;
        foreach (var segment in chain[..^1])
            length += segment.Writable ? 0 : (int)Math.Min(segment.Length, int.MaxValue - length);
        var transfer = new Transfer
        {
            Head = head,
            Data = ArrayPool<byte>.Shared.Rent(Math.Max(length, 4)),
            StatusAddress = chain[^1].Address,
        };
        int copied = Gather(chain[..^1], ram, ramBase, transfer.Data.AsSpan(0, length));
        bool streamZero = copied >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(transfer.Data) == 0;
        if (!streamZero || _state is StreamState.Idle or StreamState.Configured)
        {
            Complete(ram, ramBase, transfer, streamZero ? StatusIoError : StatusBadMessage);
            return;
        }
        transfer.Played = 4;
        transfer.Length = copied;
        _transfers.Enqueue(transfer);
    }

    private uint CompleteRejected(int queue, byte[] ram, ulong ramBase, ushort head)
    {
        if (!TryReadChain(queue, ram, ramBase, head, out var chain) || chain.Length == 0 || !chain[^1].Writable || chain[^1].Length < 8)
            return 0;
        RVGuestMemory.TryWriteU32(ram, ramBase, chain[^1].Address, StatusIoError);
        RVGuestMemory.TryWriteU32(ram, ramBase, chain[^1].Address + 4, 0);
        return 8;
    }

    private void Complete(byte[] ram, ulong ramBase, Transfer transfer, uint status)
    {
        uint latency = 0;
        foreach (var queued in _transfers)
            latency += (uint)(queued.Length - queued.Played);
        RVGuestMemory.TryWriteU32(ram, ramBase, transfer.StatusAddress, status);
        RVGuestMemory.TryWriteU32(ram, ramBase, transfer.StatusAddress + 4, latency);
        PushUsed(TransmitQueue, ram, ramBase, transfer.Head, 8);
        Release(transfer);
    }

    private static void Release(Transfer transfer)
    {
        if (transfer.Data.Length != 0)
            ArrayPool<byte>.Shared.Return(transfer.Data);
        transfer.Data = Array.Empty<byte>();
    }

    private void Emit(ReadOnlySpan<byte> pcm)
    {
        int width = BytesPerSample(_format);
        lock (_samplesLock)
        {
            for (int offset = 0; offset + width <= pcm.Length; offset += width)
            {
                float sample = _format switch
                {
                    FormatU8 => (pcm[offset] - 128) / 128f,
                    FormatS16 => BinaryPrimitives.ReadInt16LittleEndian(pcm[offset..]) / 32768f,
                    FormatS32 => BinaryPrimitives.ReadInt32LittleEndian(pcm[offset..]) / 2147483648f,
                    _ => BinaryPrimitives.ReadSingleLittleEndian(pcm[offset..]),
                };
                if (_sampleCount == _samples.Length)
                {
                    _sampleHead = _sampleHead + 1 == _samples.Length ? 0 : _sampleHead + 1;
                    _sampleCount--;
                    _droppedSamples++;
                }
                int index = _sampleHead + _sampleCount;
                _samples[index >= _samples.Length ? index - _samples.Length : index] = sample;
                _sampleCount++;
            }
        }
    }

    private static int BytesPerSample(byte format)
        => format switch
        {
            FormatU8 => 1,
            FormatS16 => 2,
            _ => 4,
        };
}
