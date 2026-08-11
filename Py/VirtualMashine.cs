using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Cnidaria.Python
{
    public enum VmStopReason : byte
    {
        Completed,
        Cancelled,
        TimeLimitExceeded,
        InstructionLimitExceeded,
        MemoryLimitExceeded,
        CallDepthLimitExceeded,
        IntegerLimitExceeded,
        InvalidBytecode,
        UnsupportedOpcode,
        SecurityViolation,
        UnhandledException,
        OutputLimitExceeded,
        OutputFailure,
    }

    public enum ValueSnapshotKind : byte
    {
        None,
        Boolean,
        Integer,
        Float,
        String,
        Other,
    }

    public readonly struct ValueSnapshot
    {
        internal ValueSnapshot(
            ValueSnapshotKind kind,
            bool booleanValue,
            long integerValue,
            double floatValue,
            string? text)
        {
            Kind = kind;
            BooleanValue = booleanValue;
            IntegerValue = integerValue;
            FloatValue = floatValue;
            Text = text;
        }

        public ValueSnapshotKind Kind { get; }
        public bool BooleanValue { get; }
        public long IntegerValue { get; }
        public double FloatValue { get; }
        public string? Text { get; }

        public override string ToString()
        {
            return Kind switch
            {
                ValueSnapshotKind.None => "None",
                ValueSnapshotKind.Boolean => BooleanValue ? "True" : "False",
                ValueSnapshotKind.Integer => Text ?? IntegerValue.ToString(CultureInfo.InvariantCulture),
                ValueSnapshotKind.Float => FloatValue.ToString("R", CultureInfo.InvariantCulture),
                ValueSnapshotKind.String => Text ?? string.Empty,
                _ => Text ?? "<value>",
            };
        }
    }

    public readonly struct ExecutionLimits : IEquatable<ExecutionLimits>
    {
        public readonly long MaxInstructions { get; init; } = 10_000_000;
        public readonly int MaxCallDepth { get; init; } = 128;
        public readonly int CancellationCheckPeriod { get; init; } = 256;
        public readonly int MaxIntegerBits { get; init; } = 16_384;
        public readonly int MaxOutputBytes { get; init; } = 1_048_576;
        public readonly int GcThreshold0 { get; init; } = 700;
        public readonly int GcThreshold1 { get; init; } = 10;
        public readonly int GcThreshold2 { get; init; } = 10;
        public readonly TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(5);
        public ExecutionLimits() { }

        public static bool operator ==(ExecutionLimits left, ExecutionLimits right)
            => left.MaxInstructions == right.MaxInstructions &&
               left.MaxCallDepth == right.MaxCallDepth &&
               left.CancellationCheckPeriod == right.CancellationCheckPeriod &&
               left.MaxIntegerBits == right.MaxIntegerBits &&
               left.MaxOutputBytes == right.MaxOutputBytes &&
               left.GcThreshold0 == right.GcThreshold0 &&
               left.GcThreshold1 == right.GcThreshold1 &&
               left.GcThreshold2 == right.GcThreshold2 &&
               left.TimeLimit == right.TimeLimit;

        public static bool operator !=(ExecutionLimits left, ExecutionLimits right) => !(left == right);

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is ExecutionLimits other && Equals(other);
        }

        public bool Equals(ExecutionLimits other) => this == other;

        public override int GetHashCode()
        {
            return HashCode.Combine(
                MaxInstructions,
                MaxCallDepth,
                CancellationCheckPeriod,
                MaxIntegerBits,
                MaxOutputBytes,
                HashCode.Combine(GcThreshold0, GcThreshold1, GcThreshold2),
                TimeLimit);
        }
    }

    public sealed class VmResult
    {
        internal VmResult(
            VmStopReason stopReason,
            long instructionsExecuted,
            int currentHeapBytes,
            int peakHeapBytes,
            int peakFrameBytes,
            int garbageCollections,
            long collectedObjects,
            long collectedBytes,
            ValueSnapshot returnValue,
            int outputBytesProduced,
            string? diagnosticMessage)
        {
            StopReason = stopReason;
            InstructionsExecuted = instructionsExecuted;
            CurrentHeapBytes = currentHeapBytes;
            PeakHeapBytes = peakHeapBytes;
            PeakFrameBytes = peakFrameBytes;
            GarbageCollections = garbageCollections;
            CollectedObjects = collectedObjects;
            CollectedBytes = collectedBytes;
            ReturnValue = returnValue;
            OutputBytesProduced = outputBytesProduced;
            DiagnosticMessage = diagnosticMessage;
        }

        public VmStopReason StopReason { get; }
        public long InstructionsExecuted { get; }
        public int CurrentHeapBytes { get; }
        public int PeakHeapBytes { get; }
        public int PeakFrameBytes { get; }
        public int GarbageCollections { get; }
        public long CollectedObjects { get; }
        public long CollectedBytes { get; }
        public ValueSnapshot ReturnValue { get; }
        public int OutputBytesProduced { get; }
        public string? DiagnosticMessage { get; }
        public bool Success => StopReason == VmStopReason.Completed;
    }

    public static class PythonVirtualMachine
    {
        public const int MinimumMemorySize = 32 * 1024;

        public static VmResult Execute(
            PythonCodeObject codeObject,
            Span<byte> memory,
            TextWriter standardOutput,
            ExecutionLimits limits = default,
            CancellationToken cancellationToken = default)
        {
            return Execute(
                codeObject,
                memory,
                standardOutput,
                PythonStandardLibrary.Default,
                limits,
                cancellationToken);
        }

        public static VmResult Execute(
            PythonCodeObject codeObject,
            Span<byte> memory,
            TextWriter standardOutput,
            PythonModuleCatalog moduleCatalog,
            ExecutionLimits limits = default,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(codeObject);
            ArgumentNullException.ThrowIfNull(standardOutput);
            ArgumentNullException.ThrowIfNull(moduleCatalog);
            if (limits == default) limits = new ExecutionLimits();
            ValidateLimits(limits);
            if (memory.Length < MinimumMemorySize)
            {
                throw new ArgumentException(
                    $"Python VM memory must contain at least {MinimumMemorySize} bytes.",
                    nameof(memory));
            }

            using var timeoutSource = new CancellationTokenSource();
            if (limits.TimeLimit != Timeout.InfiniteTimeSpan)
                timeoutSource.CancelAfter(limits.TimeLimit);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

            var runtime = new VmRuntime(
                memory,
                standardOutput,
                limits,
                cancellationToken,
                timeoutSource.Token,
                linkedSource.Token,
                moduleCatalog);
            return runtime.Execute(codeObject);
        }

        private static void ValidateLimits(ExecutionLimits limits)
        {
            if (limits.MaxInstructions <= 0)
                throw new ArgumentOutOfRangeException(nameof(limits.MaxInstructions));
            if (limits.MaxCallDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(limits.MaxCallDepth));
            if (limits.CancellationCheckPeriod <= 0)
                throw new ArgumentOutOfRangeException(nameof(limits.CancellationCheckPeriod));
            if (limits.MaxIntegerBits < 64)
                throw new ArgumentOutOfRangeException(nameof(limits.MaxIntegerBits));
            if (limits.MaxOutputBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(limits.MaxOutputBytes));
            if (limits.GcThreshold0 < 0)
                throw new ArgumentOutOfRangeException(nameof(limits.GcThreshold0));
            if (limits.GcThreshold1 < 0)
                throw new ArgumentOutOfRangeException(nameof(limits.GcThreshold1));
            if (limits.GcThreshold2 < 0)
                throw new ArgumentOutOfRangeException(nameof(limits.GcThreshold2));
            if (limits.TimeLimit < TimeSpan.Zero && limits.TimeLimit != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(limits.TimeLimit));
        }
    }

    internal enum VmObjectType : uint
    {
        Free = 0,
        Storage = 1,
        Integer = 2,
        Float = 3,
        Complex = 4,
        String = 5,
        Bytes = 6,
        Tuple = 7,
        List = 8,
        Dictionary = 9,
        Set = 10,
        FrozenSet = 11,
        Code = 12,
        Function = 13,
        Iterator = 14,
        Slice = 15,
        Range = 16,
        BoundMethod = 17,
        Exception = 18,
        Cell = 19,
        Generator = 20,
        Module = 21,
        Interpolation = 22,
        Template = 23,
        Class = 24,
        Instance = 25,
        PythonBoundMethod = 26,
        Super = 27,
        MappingProxy = 28,
        BuiltinIterator = 29,
        StaticMethod = 30,
        ClassMethod = 31,
        Property = 32,
    }

    internal enum VmBuiltin : ulong
    {
        Print = 1,
        Len = 2,
        Range = 3,
        List = 4,
        Tuple = 5,
        Set = 6,
        Dict = 7,
        Bool = 8,
        Int = 9,
        Str = 10,
        All = 11,
        Any = 12,
        Iter = 13,
        Next = 14,
        Import = 15,
        BuildClass = 16,
        Super = 17,
        ObjectInit = 18,
        Abs = 80,
        Ascii = 81,
        Bin = 82,
        Bytes = 83,
        Callable = 84,
        Chr = 85,
        ClassMethod = 86,
        Complex = 87,
        DelAttr = 88,
        Dir = 89,
        DivMod = 90,
        Enumerate = 91,
        Filter = 92,
        Float = 93,
        FrozenSet = 94,
        GetAttr = 95,
        Globals = 96,
        HasAttr = 97,
        Hash = 98,
        Hex = 99,
        Id = 100,
        IsInstance = 101,
        IsSubclass = 102,
        Locals = 103,
        Map = 104,
        Max = 105,
        Min = 106,
        Oct = 107,
        Ord = 108,
        Pow = 109,
        Property = 110,
        Repr = 111,
        Reversed = 112,
        Round = 113,
        SetAttr = 114,
        Slice = 115,
        Sorted = 116,
        StaticMethod = 117,
        Sum = 118,
        Vars = 119,
        Zip = 120,
        Format = 121,
        Exception = 32,
        TypeError = 33,
        ValueError = 34,
        RuntimeError = 35,
        AssertionError = 36,
        NotImplementedError = 37,
        BaseException = 38,
        KeyError = 39,
        IndexError = 40,
        NameError = 41,
        UnboundLocalError = 42,
        StopIteration = 43,
        ZeroDivisionError = 44,
        ArithmeticError = 45,
        LookupError = 46,
        AttributeError = 47,
        ImportError = 48,
        OverflowError = 49,
        SystemError = 50,
        ModuleNotFoundError = 51,
        SysGetRecursionLimit = 64,
        MathSqrt = 128,
        MathFloor = 129,
        MathCeil = 130,
        MathTrunc = 131,
        MathFabs = 132,
        MathIsFinite = 133,
        MathIsInf = 134,
        MathIsNaN = 135,
        MathCopySign = 136,
        MathFmod = 137,
        MathPow = 138,
        MathSin = 139,
        MathCos = 140,
        MathTan = 141,
        MathAsin = 142,
        MathAcos = 143,
        MathAtan = 144,
        MathAtan2 = 145,
        MathExp = 146,
        MathLog = 147,
        MathLog2 = 148,
        MathLog10 = 149,
        MathDegrees = 150,
        MathRadians = 151,
        MathHypot = 152,
        MathGcd = 153,
        MathLcm = 154,
        MathFactorial = 155,
        MathComb = 156,
        MathPerm = 157,
        MathProd = 158,
        MathIsClose = 159,
        MathSinh = 160,
        MathCosh = 161,
        MathTanh = 162,
        MathAsinh = 163,
        MathAcosh = 164,
        MathAtanh = 165,
    }

    internal enum VmBoundMethod : int
    {
        ListAppend = 1,
        ListExtend = 2,
        ListPop = 3,
        DictionaryGet = 16,
        DictionaryKeys = 17,
        DictionaryValues = 18,
        SetAdd = 32,
        SetDiscard = 33,
        StringStartsWith = 48,
        StringEndsWith = 49,
        GeneratorIter = 64,
        GeneratorNext = 65,
        GeneratorSend = 66,
        IteratorIter = 67,
        IteratorNext = 68,
        PropertyGetter = 80,
        PropertySetter = 81,
        PropertyDeleter = 82,
    }

    internal enum VmBuiltinIteratorKind : int
    {
        Enumerate = 1,
        Zip = 2,
        Map = 3,
        Filter = 4,
        Reversed = 5,
        CallableSentinel = 6,
        Sequence = 7,
    }

    internal enum VmSuspensionState : int
    {
        Created = 0,
        Running = 1,
        Suspended = 2,
        Completed = 3,
    }

    internal readonly struct VmValue : IEquatable<VmValue>
    {
        private const ulong TagMask = 7;
        private const ulong NoneTag = 1;
        private const ulong FalseTag = 2;
        private const ulong TrueTag = 3;
        private const ulong SmallIntegerTag = 4;
        private const ulong BuiltinTag = 5;
        private const ulong EllipsisTag = 6;
        private const ulong DeletedTag = 7;

        public VmValue(ulong raw)
        {
            Raw = raw;
        }

        public ulong Raw { get; }
        public static VmValue Null => default;
        public static VmValue None => new(NoneTag);
        public static VmValue False => new(FalseTag);
        public static VmValue True => new(TrueTag);
        public static VmValue Ellipsis => new(EllipsisTag);
        public static VmValue Deleted => new(DeletedTag);

        public bool IsNull => Raw == 0;
        public bool IsNone => Raw == NoneTag;
        public bool IsBoolean => Raw is FalseTag or TrueTag;
        public bool IsEllipsis => Raw == EllipsisTag;
        public bool IsSmallInteger => (Raw & TagMask) == SmallIntegerTag;
        public bool IsBuiltin => (Raw & TagMask) == BuiltinTag;
        public bool IsDeleted => Raw == DeletedTag;
        public bool IsAddress => Raw != 0 && (Raw & TagMask) == 0;
        public bool BooleanValue => Raw == TrueTag;
        public long SmallIntegerValue => unchecked((long)Raw) >> 3;
        public VmBuiltin Builtin => (VmBuiltin)(Raw >> 3);
        public int Address => checked((int)Raw);

        public static bool CanEncodeSmallInteger(long value)
        {
            return value >= -(1L << 60) && value <= (1L << 60) - 1;
        }

        public static VmValue FromSmallInteger(long value)
        {
            if (!CanEncodeSmallInteger(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new VmValue(unchecked(((ulong)value << 3) | SmallIntegerTag));
        }

        public static VmValue FromBuiltin(VmBuiltin builtin)
        {
            return new VmValue(((ulong)builtin << 3) | BuiltinTag);
        }

        public static VmValue FromAddress(int address)
        {
            if (address <= 0 || (address & 7) != 0)
                throw new ArgumentOutOfRangeException(nameof(address));
            return new VmValue((ulong)address);
        }

        public bool Equals(VmValue other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is VmValue other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public static bool operator ==(VmValue left, VmValue right) => left.Raw == right.Raw;
        public static bool operator !=(VmValue left, VmValue right) => left.Raw != right.Raw;
    }

    internal sealed class VmTrapException : Exception
    {
        public VmTrapException(VmStopReason reason, string message)
            : base(message)
        {
            Reason = reason;
        }

        public VmStopReason Reason { get; }
    }

    internal sealed class VmGuestExceptionSignal : Exception
    {
    }

    internal sealed class VmControlTransferSignal : Exception
    {
    }

    internal readonly struct VmGcSweepResult
    {
        public VmGcSweepResult(int collectedObjects, int collectedBytes)
        {
            CollectedObjects = collectedObjects;
            CollectedBytes = collectedBytes;
        }

        public int CollectedObjects { get; }
        public int CollectedBytes { get; }
    }

    internal ref struct VmMemory
    {
        private const int RuntimeHeaderSize = 64;

        private const int ObjectHeaderSize = 24;
        private const int MinimumFreeBlockSize = ObjectHeaderSize + 8;
        private const int ObjectFlagsOffset = 16;
        private const int ObjectLinkOffset = 20;
        private const uint ObjectMarkFlag = 1u << 0;
        private const int ObjectGenerationShift = 8;
        private const uint ObjectGenerationMask = 3u << ObjectGenerationShift;
        private const uint RuntimeMagic = 0x4D565950; // PYVM

        private Span<byte> _buffer;
        private int _heapTop;
        private int _stackTop;
        private int _peakHeap;
        private int _peakFrames;
        private int _freeListHead;
        private int _allocatedBytes;
        private int _freeBytes;
        private long _totalAllocations;

        public VmMemory(Span<byte> buffer)
        {
            _buffer = buffer;
            _buffer.Clear();
            _heapTop = Align8(RuntimeHeaderSize);
            _stackTop = buffer.Length & ~7;
            _peakHeap = _heapTop;
            _peakFrames = 0;
            _freeListHead = 0;
            _allocatedBytes = 0;
            _freeBytes = 0;
            _totalAllocations = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer, RuntimeMagic);
            SynchronizeHeader();
        }

        public int PeakHeapBytes => _peakHeap;
        public int PeakFrameBytes => _peakFrames;
        public int Capacity => _buffer.Length;
        public int CurrentHeapBytes => checked(RuntimeHeaderSize + _allocatedBytes);
        public int HeapTop => _heapTop;
        public int StackTop => _stackTop;
        public int FirstObjectAddress => Align8(RuntimeHeaderSize);
        public int UnallocatedBytes => _stackTop - _heapTop;
        public int AvailableBytes => checked(UnallocatedBytes + _freeBytes);
        public long TotalAllocations => _totalAllocations;

        public bool CanAllocateObjectPayload(int payloadSize)
        {
            if (payloadSize < 0)
                return false;
            var total = ((long)ObjectHeaderSize + payloadSize + 7L) & ~7L;
            return total <= int.MaxValue && CanAllocateRaw((int)total);
        }

        public int AllocateObject(VmObjectType type, int payloadSize, int aux0 = 0, int aux1 = 0)
        {
            if (type == VmObjectType.Free)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Cannot allocate the synthetic free-block type.");
            if (payloadSize < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative guest allocation size.");

            var requestedSize = Align8(checked(ObjectHeaderSize + payloadSize));
            var address = AllocateHeapRaw(requestedSize, out var allocatedSize);
            WriteUInt32(address, (uint)type);
            WriteInt32(address + 4, allocatedSize);
            WriteInt32(address + 8, aux0);
            WriteInt32(address + 12, aux1);
            WriteUInt32(address + ObjectFlagsOffset, 0);
            WriteInt32(address + ObjectLinkOffset, 0);
            _totalAllocations++;
            SynchronizeHeader();
            return address;
        }

        public int AllocateStorage(int byteLength)
        {
            return AllocateObject(VmObjectType.Storage, byteLength, byteLength, 0);
        }

        public int PushFrameStorage(int byteLength)
        {
            var aligned = Align8(byteLength);
            TrimTrailingFreeBlock();
            var next = checked(_stackTop - aligned);
            if (next < _heapTop)
                throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Synthetic RAM is exhausted by Python frames.");
            _stackTop = next;
            Slice(_stackTop, aligned).Clear();
            var used = _buffer.Length - _stackTop;
            if (used > _peakFrames)
                _peakFrames = used;
            SynchronizeHeader();
            return _stackTop;
        }

        public void PopFrameStorage(int address, int byteLength)
        {
            var aligned = Align8(byteLength);
            if (address != _stackTop || checked(address + aligned) > _buffer.Length)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Corrupt synthetic frame stack.");
            Slice(address, aligned).Clear();
            _stackTop = checked(_stackTop + aligned);
            SynchronizeHeader();
        }

        public VmObjectType GetObjectType(VmValue value)
        {
            if (!value.IsAddress)
                throw new VmTrapException(VmStopReason.UnhandledException, "TypeError: expected a heap object.");
            ValidateObject(value.Address);
            return (VmObjectType)ReadUInt32(value.Address);
        }

        public VmObjectType GetObjectType(int address)
        {
            ValidateObject(address);
            return (VmObjectType)ReadUInt32(address);
        }

        public int GetObjectPayloadAddress(VmValue value)
        {
            _ = GetObjectType(value);
            return checked(value.Address + ObjectHeaderSize);
        }

        public int GetObjectPayloadAddress(int address)
        {
            ValidateObject(address);
            return checked(address + ObjectHeaderSize);
        }

        public int GetObjectAux0(VmValue value)
        {
            _ = GetObjectType(value);
            return ReadInt32(value.Address + 8);
        }

        public int GetObjectAux1(VmValue value)
        {
            _ = GetObjectType(value);
            return ReadInt32(value.Address + 12);
        }

        public void SetObjectAux0(VmValue value, int data)
        {
            _ = GetObjectType(value);
            WriteInt32(value.Address + 8, data);
        }

        public void SetObjectAux1(VmValue value, int data)
        {
            _ = GetObjectType(value);
            WriteInt32(value.Address + 12, data);
        }

        public int GetObjectSize(int address)
        {
            ValidateBlock(address);
            return ReadInt32(address + 4);
        }

        public int GetObjectPayloadSize(int address)
        {
            ValidateObject(address);
            return checked(ReadInt32(address + 4) - ObjectHeaderSize);
        }

        public int GetObjectGeneration(int address)
        {
            ValidateObject(address);
            return (int)((ReadUInt32(address + ObjectFlagsOffset) & ObjectGenerationMask) >> ObjectGenerationShift);
        }

        public void PrepareCollection(CancellationToken cancellationToken)
        {
            var address = FirstObjectAddress;
            var blocksUntilCancellationPoll = 256;
            while (address < _heapTop)
            {
                ValidateBlock(address);
                if ((VmObjectType)ReadUInt32(address) != VmObjectType.Free)
                {
                    var flags = ReadUInt32(address + ObjectFlagsOffset);
                    WriteUInt32(address + ObjectFlagsOffset, flags & ~ObjectMarkFlag);
                    WriteInt32(address + ObjectLinkOffset, 0);
                }
                address = checked(address + ReadInt32(address + 4));
                if (--blocksUntilCancellationPoll == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    blocksUntilCancellationPoll = 256;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void MarkOlderGenerationRoots(
            int collectedGeneration,
            ref int markStackHead,
            CancellationToken cancellationToken)
        {
            var address = FirstObjectAddress;
            var blocksUntilCancellationPoll = 256;
            while (address < _heapTop)
            {
                ValidateBlock(address);
                if ((VmObjectType)ReadUInt32(address) != VmObjectType.Free &&
                    GetObjectGenerationUnchecked(address) > collectedGeneration)
                {
                    TryMarkObject(VmValue.FromAddress(address), ref markStackHead);
                }
                address = checked(address + ReadInt32(address + 4));
                if (--blocksUntilCancellationPoll == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    blocksUntilCancellationPoll = 256;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public bool TryMarkObject(VmValue value, ref int markStackHead)
        {
            if (!value.IsAddress)
                return false;

            ValidateObject(value.Address);
            var flags = ReadUInt32(value.Address + ObjectFlagsOffset);
            if ((flags & ObjectMarkFlag) != 0)
                return false;

            WriteUInt32(value.Address + ObjectFlagsOffset, flags | ObjectMarkFlag);
            WriteInt32(value.Address + ObjectLinkOffset, markStackHead);
            markStackHead = value.Address;
            return true;
        }

        public int PopMarkedObject(ref int markStackHead)
        {
            if (markStackHead == 0)
                return 0;
            var address = markStackHead;
            ValidateObject(address);
            markStackHead = ReadInt32(address + ObjectLinkOffset);
            WriteInt32(address + ObjectLinkOffset, 0);
            return address;
        }

        public VmGcSweepResult Sweep(int collectedGeneration, CancellationToken cancellationToken)
        {
            if ((uint)collectedGeneration > 2u)
                throw new ArgumentOutOfRangeException(nameof(collectedGeneration));

            var address = FirstObjectAddress;
            var freeHead = 0;
            var freeTail = 0;
            var freeTailPrevious = 0;
            var coalescingFree = 0;
            var allocatedBytes = 0;
            var freeBytes = 0;
            var collectedObjects = 0;
            var collectedBytes = 0;
            var blocksUntilCancellationPoll = 256;

            while (address < _heapTop)
            {
                ValidateBlock(address);
                var size = ReadInt32(address + 4);
                var type = (VmObjectType)ReadUInt32(address);
                var isAllocated = type != VmObjectType.Free;
                var shouldFree = !isAllocated;

                if (isAllocated)
                {
                    var flags = ReadUInt32(address + ObjectFlagsOffset);
                    var generation = (int)((flags & ObjectGenerationMask) >> ObjectGenerationShift);
                    var marked = (flags & ObjectMarkFlag) != 0;
                    shouldFree = generation <= collectedGeneration && !marked;
                    if (!shouldFree)
                    {
                        if (generation <= collectedGeneration && generation < 2)
                            generation++;
                        flags &= ~(ObjectMarkFlag | ObjectGenerationMask);
                        flags |= (uint)generation << ObjectGenerationShift;
                        WriteUInt32(address + ObjectFlagsOffset, flags);
                        WriteInt32(address + ObjectLinkOffset, 0);
                        allocatedBytes = checked(allocatedBytes + size);
                        coalescingFree = 0;
                    }
                }

                if (shouldFree)
                {
                    if (isAllocated)
                    {
                        collectedObjects++;
                        collectedBytes = checked(collectedBytes + size);
                    }

                    Slice(address, size).Clear();
                    if (coalescingFree != 0 &&
                        checked(coalescingFree + ReadInt32(coalescingFree + 4)) == address)
                    {
                        WriteInt32(
                            coalescingFree + 4,
                            checked(ReadInt32(coalescingFree + 4) + size));
                    }
                    else
                    {
                        InitializeFreeBlock(address, size, 0);
                        if (freeTail == 0)
                        {
                            freeHead = address;
                        }
                        else
                        {
                            WriteInt32(freeTail + ObjectLinkOffset, address);
                        }
                        freeTailPrevious = freeTail;
                        freeTail = address;
                        coalescingFree = address;
                    }
                    freeBytes = checked(freeBytes + size);
                }

                address = checked(address + size);
                if (--blocksUntilCancellationPoll == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    blocksUntilCancellationPoll = 256;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (coalescingFree != 0 &&
                checked(coalescingFree + ReadInt32(coalescingFree + 4)) == _heapTop)
            {
                var trailingSize = ReadInt32(coalescingFree + 4);
                _heapTop = coalescingFree;
                freeBytes -= trailingSize;
                if (freeTailPrevious == 0)
                {
                    freeHead = 0;
                }
                else
                {
                    WriteInt32(freeTailPrevious + ObjectLinkOffset, 0);
                }
            }

            _freeListHead = freeHead;
            _allocatedBytes = allocatedBytes;
            _freeBytes = freeBytes;
            SynchronizeHeader();
            return new VmGcSweepResult(collectedObjects, collectedBytes);
        }

        public byte ReadByte(int address)
        {
            ValidateRange(address, 1);
            return _buffer[address];
        }

        public void WriteByte(int address, byte value)
        {
            ValidateRange(address, 1);
            _buffer[address] = value;
        }

        public int ReadInt32(int address)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(Slice(address, 4));
        }

        public uint ReadUInt32(int address)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(Slice(address, 4));
        }

        public long ReadInt64(int address)
        {
            return BinaryPrimitives.ReadInt64LittleEndian(Slice(address, 8));
        }

        public ulong ReadUInt64(int address)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(Slice(address, 8));
        }

        public double ReadDouble(int address)
        {
            return BitConverter.Int64BitsToDouble(ReadInt64(address));
        }

        public VmValue ReadValue(int address)
        {
            return new VmValue(ReadUInt64(address));
        }

        public void WriteInt32(int address, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(Slice(address, 4), value);
        }

        public void WriteUInt32(int address, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(Slice(address, 4), value);
        }

        public void WriteInt64(int address, long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(Slice(address, 8), value);
        }

        public void WriteUInt64(int address, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(Slice(address, 8), value);
        }

        public void WriteDouble(int address, double value)
        {
            WriteInt64(address, BitConverter.DoubleToInt64Bits(value));
        }

        public void WriteValue(int address, VmValue value)
        {
            WriteUInt64(address, value.Raw);
        }

        public Span<byte> GetSpan(int address, int length)
        {
            return Slice(address, length);
        }

        public ReadOnlySpan<byte> GetReadOnlySpan(int address, int length)
        {
            return Slice(address, length);
        }

        public void Copy(int sourceAddress, int destinationAddress, int byteLength)
        {
            Slice(sourceAddress, byteLength).CopyTo(Slice(destinationAddress, byteLength));
        }

        private bool CanAllocateRaw(int byteLength)
        {
            var current = _freeListHead;
            while (current != 0)
            {
                ValidateFreeBlock(current);
                if (ReadInt32(current + 4) >= byteLength)
                    return true;
                current = ReadInt32(current + ObjectLinkOffset);
            }
            return byteLength <= UnallocatedBytes;
        }

        private int AllocateHeapRaw(int byteLength, out int allocatedSize)
        {
            var previous = 0;
            var current = _freeListHead;
            while (current != 0)
            {
                ValidateFreeBlock(current);
                var size = ReadInt32(current + 4);
                var next = ReadInt32(current + ObjectLinkOffset);
                if (size >= byteLength)
                {
                    var remainder = size - byteLength;
                    if (remainder >= MinimumFreeBlockSize)
                    {
                        var split = checked(current + byteLength);
                        InitializeFreeBlock(split, remainder, next);
                        if (previous == 0)
                            _freeListHead = split;
                        else
                            WriteInt32(previous + ObjectLinkOffset, split);
                        allocatedSize = byteLength;
                        _freeBytes -= byteLength;
                    }
                    else
                    {
                        if (previous == 0)
                            _freeListHead = next;
                        else
                            WriteInt32(previous + ObjectLinkOffset, next);
                        allocatedSize = size;
                        _freeBytes -= size;
                    }

                    Slice(current, allocatedSize).Clear();
                    _allocatedBytes = checked(_allocatedBytes + allocatedSize);
                    return current;
                }
                previous = current;
                current = next;
            }

            var address = _heapTop;
            var nextHeapTop = checked(address + byteLength);
            if (nextHeapTop > _stackTop)
                throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Synthetic RAM is exhausted by Python objects.");

            _heapTop = nextHeapTop;
            allocatedSize = byteLength;
            Slice(address, byteLength).Clear();
            _allocatedBytes = checked(_allocatedBytes + byteLength);
            if (_heapTop > _peakHeap)
                _peakHeap = _heapTop;
            return address;
        }

        private void InitializeFreeBlock(int address, int size, int next)
        {
            if ((address & 7) != 0 || size < ObjectHeaderSize || (size & 7) != 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Invalid synthetic free block.");
            WriteUInt32(address, (uint)VmObjectType.Free);
            WriteInt32(address + 4, size);
            WriteInt32(address + 8, 0);
            WriteInt32(address + 12, 0);
            WriteUInt32(address + ObjectFlagsOffset, 0);
            WriteInt32(address + ObjectLinkOffset, next);
        }

        private void TrimTrailingFreeBlock()
        {
            if (_freeListHead == 0)
                return;

            var previous = 0;
            var current = _freeListHead;
            while (true)
            {
                ValidateFreeBlock(current);
                var next = ReadInt32(current + ObjectLinkOffset);
                if (next == 0)
                    break;
                previous = current;
                current = next;
            }

            var size = ReadInt32(current + 4);
            if (checked(current + size) != _heapTop)
                return;

            _heapTop = current;
            _freeBytes -= size;
            if (previous == 0)
                _freeListHead = 0;
            else
                WriteInt32(previous + ObjectLinkOffset, 0);
            SynchronizeHeader();
        }

        private int GetObjectGenerationUnchecked(int address)
        {
            return (int)((ReadUInt32(address + ObjectFlagsOffset) & ObjectGenerationMask) >> ObjectGenerationShift);
        }

        private void ValidateObject(int address)
        {
            ValidateBlock(address);
            if ((VmObjectType)ReadUInt32(address) == VmObjectType.Free)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic address references a freed object.");
        }

        private void ValidateFreeBlock(int address)
        {
            ValidateBlock(address);
            if ((VmObjectType)ReadUInt32(address) != VmObjectType.Free)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Corrupt synthetic heap free list.");
        }

        private void ValidateBlock(int address)
        {
            ValidateRange(address, ObjectHeaderSize);
            if ((address & 7) != 0 || address < RuntimeHeaderSize || address >= _heapTop)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Invalid synthetic heap block address.");
            var size = ReadInt32(address + 4);
            if (size < ObjectHeaderSize || (size & 7) != 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Corrupt synthetic heap block header.");
            if (size > _heapTop - address)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic heap block extends outside the allocated guest heap.");
        }

        private Span<byte> Slice(int address, int length)
        {
            ValidateRange(address, length);
            return _buffer.Slice(address, length);
        }

        private void ValidateRange(int address, int length)
        {
            if (address < 0 || length < 0 || address > _buffer.Length - length)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic address-space access is out of bounds.");
        }

        private void SynchronizeHeader()
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(4, 4), _heapTop);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(8, 4), _stackTop);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(12, 4), _peakHeap);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(16, 4), _peakFrames);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(20, 4), _freeListHead);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(24, 4), _allocatedBytes);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(28, 4), _freeBytes);
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.Slice(32, 8), _totalAllocations);
        }

        private static int Align8(int value)
        {
            return checked((value + 7) & ~7);
        }
    }

    internal ref struct VmRuntime
    {
        private const int FrameHeaderSize = 80;
        private const int CodePayloadSize = 96;
        private const int FunctionPayloadSize = 56;
        private const int CellPayloadSize = 8;
        private const int GeneratorPayloadSize = 80;
        private const int ListPayloadSize = 16;
        private const int DictionaryPayloadSize = 16;
        private const int DictionaryEntrySize = 24;
        private const int IteratorPayloadSize = 24;
        private const int SlicePayloadSize = 24;
        private const int RangePayloadSize = 24;
        private const int BoundMethodPayloadSize = 16;
        private const int ExceptionPayloadSize = 16;
        private const int ModulePayloadSize = 24;
        private const int InterpolationPayloadSize = 32;
        private const int TemplatePayloadSize = 16;
        private const int ClassPayloadSize = 64;
        private const int InstancePayloadSize = 16;
        private const int PythonBoundMethodPayloadSize = 16;
        private const int SuperPayloadSize = 16;
        private const int MappingProxyPayloadSize = 8;
        private const int BuiltinIteratorPayloadSize = 40;
        private const int StaticMethodPayloadSize = 8;
        private const int ClassMethodPayloadSize = 8;
        private const int PropertyPayloadSize = 32;
        private const int ModulePackageFlag = 1 << 0;
        private const int ModuleInitializingFlag = 1 << 1;

        private VmMemory _memory;
        private readonly TextWriter _standardOutput;
        private readonly ExecutionLimits _limits;
        private readonly CancellationToken _externalCancellation;
        private readonly CancellationToken _timeoutCancellation;
        private readonly CancellationToken _combinedCancellation;
        private readonly PythonModuleCatalog _moduleCatalog;

        private VmValue _builtins;
        private VmValue _globals;
        private VmValue _modules;
        private VmValue _builtinsModule;
        private VmValue _objectClass;
        private VmValue _typeClass;
        private VmValue _hostRoots;
        private int _outputBytes;
        private int _currentFrame;
        private int _callDepth;
        private long _instructions;
        private VmValue _returnValue;
        private VmValue _raisedException;
        private int _raisedLastInstruction;
        private bool _snapshotMode;
        private int _cancellationPollCountdown;
        private int _constantLoadDepth;
        private long _lastObservedAllocations;
        private long _lastCollectionAllocations;
        private long _gcCount0;
        private int _gcCount1;
        private int _gcCount2;
        private int _garbageCollections;
        private long _collectedObjects;
        private long _collectedBytes;

        public VmRuntime(
            Span<byte> memory,
            TextWriter standardOutput,
            ExecutionLimits limits,
            CancellationToken externalCancellation,
            CancellationToken timeoutCancellation,
            CancellationToken combinedCancellation,
            PythonModuleCatalog moduleCatalog)
        {
            _memory = new VmMemory(memory);
            _standardOutput = standardOutput;
            _limits = limits;
            _externalCancellation = externalCancellation;
            _timeoutCancellation = timeoutCancellation;
            _combinedCancellation = combinedCancellation;
            _moduleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
            _builtins = VmValue.Null;
            _globals = VmValue.Null;
            _modules = VmValue.Null;
            _builtinsModule = VmValue.Null;
            _objectClass = VmValue.Null;
            _typeClass = VmValue.Null;
            _hostRoots = VmValue.Null;
            _outputBytes = 0;
            _currentFrame = 0;
            _callDepth = 0;
            _instructions = 0;
            _returnValue = VmValue.None;
            _raisedException = VmValue.Null;
            _raisedLastInstruction = -1;
            _snapshotMode = false;
            _cancellationPollCountdown = limits.CancellationCheckPeriod;
            _constantLoadDepth = 0;
            _lastObservedAllocations = 0;
            _lastCollectionAllocations = 0;
            _gcCount0 = 0;
            _gcCount1 = 0;
            _gcCount2 = 0;
            _garbageCollections = 0;
            _collectedObjects = 0;
            _collectedBytes = 0;
        }

        public VmResult Execute(PythonCodeObject codeObject)
        {
            VmStopReason reason;
            string? message = null;
            try
            {
                _combinedCancellation.ThrowIfCancellationRequested();
                if (codeObject.Version != PythonBytecodeVersion.CPython3_14_6)
                {
                    throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        $"Unsupported Python bytecode version {codeObject.Version}.");
                }
                _hostRoots = CreateList(16);
                _modules = CreateDictionary(32);
                _builtins = CreateDictionary(64);
                BootstrapTypeSystem();
                InstallBuiltins();
                _builtinsModule = CreateModule("builtins", isPackage: false, _builtins);
                CacheModule("builtins", _builtinsModule);
                _globals = CreateDictionary(32);
                var mainModule = CreateModule("__main__", isPackage: false, _globals);
                DictionarySet(_globals, CreateString("__package__"), VmValue.None, rejectDuplicate: false);
                CacheModule("__main__", mainModule);
                var code = LoadCodeObject(codeObject);
                _currentFrame = PushFrame(previousFrame: 0, code, _globals, VmValue.Null, VmValue.Null);
                _callDepth = 1;
                RunLoop();
                reason = VmStopReason.Completed;
            }
            catch (VmTrapException trap)
            {
                reason = trap.Reason;
                message = trap.Message;
            }
            catch (OperationCanceledException)
            {
                if (_timeoutCancellation.IsCancellationRequested && !_externalCancellation.IsCancellationRequested)
                    reason = VmStopReason.TimeLimitExceeded;
                else
                    reason = VmStopReason.Cancelled;
                message = reason == VmStopReason.TimeLimitExceeded
                    ? "Python execution exceeded its time limit."
                    : "Python execution was cancelled.";
            }
            catch (OverflowException)
            {
                reason = VmStopReason.InvalidBytecode;
                message = "An integer overflow occurred while validating guest bytecode or guest memory.";
            }
            catch (Exception exception)
            {
                reason = VmStopReason.InvalidBytecode;
                message = $"Internal VM fault: {exception.GetType().Name}: {exception.Message}";
            }

            ValueSnapshot snapshot;
            _snapshotMode = true;
            try
            {
                snapshot = Snapshot(_returnValue);
            }
            catch
            {
                snapshot = new ValueSnapshot(
                    ValueSnapshotKind.Other,
                    false,
                    0,
                    0.0,
                    "<snapshot unavailable>");
            }
            return new VmResult(
                reason,
                _instructions,
                _memory.CurrentHeapBytes,
                _memory.PeakHeapBytes,
                _memory.PeakFrameBytes,
                _garbageCollections,
                _collectedObjects,
                _collectedBytes,
                snapshot,
                _outputBytes,
                message);
        }

        private void MaybeCollectGarbage()
        {
            if (_snapshotMode || _currentFrame == 0)
                return;

            var totalAllocations = _memory.TotalAllocations;
            var newAllocations = totalAllocations - _lastObservedAllocations;
            if (newAllocations < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic allocation counter moved backwards.");
            _lastObservedAllocations = totalAllocations;
            _gcCount0 = checked(_gcCount0 + newAllocations);

            var generation = -1;
            if (_limits.GcThreshold0 > 0 && _gcCount0 > _limits.GcThreshold0)
            {
                generation = 0;
                if (_gcCount1 > _limits.GcThreshold1)
                {
                    generation = 1;
                    if (_gcCount2 > _limits.GcThreshold2)
                        generation = 2;
                }
            }

            var pressureThreshold = Math.Max(4096, _memory.Capacity / 4);
            if (generation < 0 &&
                _memory.AvailableBytes < pressureThreshold &&
                totalAllocations != _lastCollectionAllocations)
            {
                generation = 2;
            }

            if (generation >= 0)
                CollectGarbage(generation);
        }

        private void CollectGarbage(int generation)
        {
            PollCancellation();

            // Collection is only entered at Python instruction boundaries. Any value that
            // must survive a nested interpreter run is first copied to the guest host root list
            _memory.PrepareCollection(_combinedCancellation);

            var markStackHead = 0;
            MarkValue(_builtins, ref markStackHead);
            MarkValue(_globals, ref markStackHead);
            MarkValue(_modules, ref markStackHead);
            MarkValue(_builtinsModule, ref markStackHead);
            MarkValue(_objectClass, ref markStackHead);
            MarkValue(_typeClass, ref markStackHead);
            MarkValue(_hostRoots, ref markStackHead);
            MarkValue(_returnValue, ref markStackHead);
            MarkValue(_raisedException, ref markStackHead);
            MarkFrameRoots(ref markStackHead);

            // Older objects are conservative roots for a young collection
            _memory.MarkOlderGenerationRoots(
                generation,
                ref markStackHead,
                _combinedCancellation);

            while (markStackHead != 0)
            {
                var address = _memory.PopMarkedObject(ref markStackHead);
                TraceObject(address, ref markStackHead);
                PollCancellation();
            }

            var sweep = _memory.Sweep(generation, _combinedCancellation);
            _garbageCollections = checked(_garbageCollections + 1);
            _collectedObjects = checked(_collectedObjects + sweep.CollectedObjects);
            _collectedBytes = checked(_collectedBytes + sweep.CollectedBytes);
            _lastCollectionAllocations = _memory.TotalAllocations;
            _lastObservedAllocations = _memory.TotalAllocations;
            _gcCount0 = 0;

            switch (generation)
            {
                case 0:
                    _gcCount1 = checked(_gcCount1 + 1);
                    break;
                case 1:
                    _gcCount1 = 0;
                    _gcCount2 = checked(_gcCount2 + 1);
                    break;
                case 2:
                    _gcCount1 = 0;
                    _gcCount2 = 0;
                    break;
                default:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Invalid synthetic GC generation.");
            }
        }

        private void MarkFrameRoots(ref int markStackHead)
        {
            var frame = _currentFrame;
            var expectedFrame = _memory.StackTop;
            var depth = 0;
            while (frame != 0)
            {
                if (++depth > _limits.MaxCallDepth + 1)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic frame chain contains a cycle.");
                if (frame != expectedFrame ||
                    (frame & 7) != 0 ||
                    frame < _memory.StackTop ||
                    frame > _memory.Capacity - FrameHeaderSize)
                {
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic frame chain is outside frame RAM.");
                }

                MarkValue(_memory.ReadValue(frame + 8), ref markStackHead);
                MarkValue(_memory.ReadValue(frame + 16), ref markStackHead);
                MarkValue(_memory.ReadValue(frame + 24), ref markStackHead);
                MarkValue(_memory.ReadValue(frame + 32), ref markStackHead);
                MarkValue(_memory.ReadValue(frame + 64), ref markStackHead);
                MarkValue(_memory.ReadValue(frame + 72), ref markStackHead);

                var localCount = _memory.ReadInt32(frame + 48);
                var stackCount = _memory.ReadInt32(frame + 44);
                var frameSize = _memory.ReadInt32(frame + 52);
                var stackCapacity = GetFrameStackCapacity(frame);
                var expectedFrameSize = FrameHeaderSize + ((long)localCount + stackCapacity) * 8L;
                if (localCount < 0 ||
                    stackCapacity < 0 ||
                    stackCount < 0 ||
                    stackCount > stackCapacity ||
                    expectedFrameSize > int.MaxValue ||
                    frameSize != (int)expectedFrameSize ||
                    frameSize > _memory.Capacity - frame)
                {
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic frame contains invalid GC roots.");
                }

                var localsAddress = frame + FrameHeaderSize;
                TraceValueSlots(localsAddress, localCount, ref markStackHead);

                var stackAddress = checked(localsAddress + localCount * 8);
                TraceValueSlots(stackAddress, stackCount, ref markStackHead);

                expectedFrame = checked(frame + frameSize);
                frame = _memory.ReadInt32(frame);
                if (frame != 0 && frame != expectedFrame)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic frame chain is not contiguous.");
            }

            if (expectedFrame != _memory.Capacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic frame stack has an invalid root boundary.");
        }

        private void MarkValue(VmValue value, ref int markStackHead)
        {
            _memory.TryMarkObject(value, ref markStackHead);
        }

        private void TraceObject(int address, ref int markStackHead)
        {
            var value = VmValue.FromAddress(address);
            var type = _memory.GetObjectType(address);
            var payload = _memory.GetObjectPayloadAddress(address);
            var payloadSize = _memory.GetObjectPayloadSize(address);

            switch (type)
            {
                case VmObjectType.Storage:
                case VmObjectType.Integer:
                case VmObjectType.Float:
                case VmObjectType.Complex:
                case VmObjectType.String:
                case VmObjectType.Bytes:
                case VmObjectType.Range:
                    return;

                case VmObjectType.Tuple:
                    {
                        var count = _memory.GetObjectAux0(value);
                        ValidateValueSlots(count, payloadSize, "tuple");
                        TraceValueSlots(payload, count, ref markStackHead);
                        return;
                    }

                case VmObjectType.List:
                    {
                        RequirePayloadSize(payloadSize, ListPayloadSize, "list");
                        var storage = _memory.ReadValue(payload);
                        MarkValue(storage, ref markStackHead);
                        RequireObjectType(storage, VmObjectType.Storage);
                        var count = _memory.ReadInt32(payload + 8);
                        var capacity = _memory.ReadInt32(payload + 12);
                        if (count < 0 || capacity < count)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "List contains invalid GC metadata.");
                        ValidateStorageValueSlots(storage, capacity, "list");
                        TraceValueSlots(_memory.GetObjectPayloadAddress(storage), count, ref markStackHead);
                        return;
                    }

                case VmObjectType.Dictionary:
                    {
                        RequirePayloadSize(payloadSize, DictionaryPayloadSize, "dictionary");
                        var entries = _memory.ReadValue(payload);
                        MarkValue(entries, ref markStackHead);
                        RequireObjectType(entries, VmObjectType.Storage);
                        var count = _memory.ReadInt32(payload + 8);
                        var capacity = _memory.ReadInt32(payload + 12);
                        if (count < 0 || capacity <= 0 || count > capacity ||
                            (capacity & (capacity - 1)) != 0)
                        {
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "Dictionary contains invalid GC metadata.");
                        }

                        var requiredBytes = checked(capacity * DictionaryEntrySize);
                        if (_memory.GetObjectAux0(entries) < requiredBytes ||
                            _memory.GetObjectPayloadSize(entries.Address) < requiredBytes)
                        {
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "Dictionary entry storage is truncated.");
                        }
                        var entriesPayload = _memory.GetObjectPayloadAddress(entries);
                        var entriesUntilCancellationPoll = 256;
                        for (var index = 0; index < capacity; index++)
                        {
                            var entry = entriesPayload + index * DictionaryEntrySize;
                            var key = _memory.ReadValue(entry + 8);
                            if (!key.IsNull && !key.IsDeleted)
                            {
                                MarkValue(key, ref markStackHead);
                                MarkValue(_memory.ReadValue(entry + 16), ref markStackHead);
                            }

                            if (--entriesUntilCancellationPoll == 0)
                            {
                                _combinedCancellation.ThrowIfCancellationRequested();
                                entriesUntilCancellationPoll = 256;
                            }
                        }
                        _combinedCancellation.ThrowIfCancellationRequested();
                        return;
                    }

                case VmObjectType.Set:
                case VmObjectType.FrozenSet:
                    RequirePayloadSize(payloadSize, 8, "set");
                    MarkValue(_memory.ReadValue(payload), ref markStackHead);
                    return;

                case VmObjectType.Code:
                    RequirePayloadSize(payloadSize, CodePayloadSize, "code");
                    MarkValue(_memory.ReadValue(payload + 32), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 40), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 48), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 56), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 64), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 72), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 80), ref markStackHead);
                    return;

                case VmObjectType.Function:
                    RequirePayloadSize(payloadSize, FunctionPayloadSize, "function");
                    for (var offset = 0; offset < FunctionPayloadSize; offset += 8)
                        MarkValue(_memory.ReadValue(payload + offset), ref markStackHead);
                    return;

                case VmObjectType.Iterator:
                    RequirePayloadSize(payloadSize, IteratorPayloadSize, "iterator");
                    MarkValue(_memory.ReadValue(payload), ref markStackHead);
                    return;

                case VmObjectType.Slice:
                    RequirePayloadSize(payloadSize, SlicePayloadSize, "slice");
                    TraceValueSlots(payload, 3, ref markStackHead);
                    return;

                case VmObjectType.BoundMethod:
                    RequirePayloadSize(payloadSize, BoundMethodPayloadSize, "bound method");
                    MarkValue(_memory.ReadValue(payload), ref markStackHead);
                    return;

                case VmObjectType.Exception:
                    RequirePayloadSize(payloadSize, ExceptionPayloadSize, "exception");
                    TraceValueSlots(payload, 2, ref markStackHead);
                    return;

                case VmObjectType.Cell:
                    RequirePayloadSize(payloadSize, CellPayloadSize, "cell");
                    MarkValue(_memory.ReadValue(payload), ref markStackHead);
                    return;

                case VmObjectType.Generator:
                    TraceGenerator(payload, payloadSize, ref markStackHead);
                    return;

                case VmObjectType.Module:
                    RequirePayloadSize(payloadSize, ModulePayloadSize, "module");
                    MarkValue(_memory.ReadValue(payload), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 8), ref markStackHead);
                    return;

                case VmObjectType.Interpolation:
                    RequirePayloadSize(payloadSize, InterpolationPayloadSize, "interpolation");
                    TraceValueSlots(payload, 4, ref markStackHead);
                    return;

                case VmObjectType.Template:
                    RequirePayloadSize(payloadSize, TemplatePayloadSize, "template");
                    TraceValueSlots(payload, 2, ref markStackHead);
                    return;

                case VmObjectType.Class:
                    RequirePayloadSize(payloadSize, ClassPayloadSize, "class");
                    TraceValueSlots(payload, 8, ref markStackHead);
                    return;

                case VmObjectType.Instance:
                    RequirePayloadSize(payloadSize, InstancePayloadSize, "instance");
                    TraceValueSlots(payload, 2, ref markStackHead);
                    return;

                case VmObjectType.PythonBoundMethod:
                    RequirePayloadSize(payloadSize, PythonBoundMethodPayloadSize, "Python bound method");
                    TraceValueSlots(payload, 2, ref markStackHead);
                    return;

                case VmObjectType.Super:
                    RequirePayloadSize(payloadSize, SuperPayloadSize, "super");
                    TraceValueSlots(payload, 2, ref markStackHead);
                    return;

                case VmObjectType.MappingProxy:
                    RequirePayloadSize(payloadSize, MappingProxyPayloadSize, "mappingproxy");
                    TraceValueSlots(payload, 1, ref markStackHead);
                    return;

                case VmObjectType.BuiltinIterator:
                    RequirePayloadSize(payloadSize, BuiltinIteratorPayloadSize, "built-in iterator");
                    MarkValue(_memory.ReadValue(payload + 8), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 16), ref markStackHead);
                    MarkValue(_memory.ReadValue(payload + 24), ref markStackHead);
                    return;

                case VmObjectType.StaticMethod:
                    RequirePayloadSize(payloadSize, StaticMethodPayloadSize, "staticmethod");
                    TraceValueSlots(payload, 1, ref markStackHead);
                    return;

                case VmObjectType.ClassMethod:
                    RequirePayloadSize(payloadSize, ClassMethodPayloadSize, "classmethod");
                    TraceValueSlots(payload, 1, ref markStackHead);
                    return;

                case VmObjectType.Property:
                    RequirePayloadSize(payloadSize, PropertyPayloadSize, "property");
                    TraceValueSlots(payload, 4, ref markStackHead);
                    return;

                case VmObjectType.Free:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "GC work list contains a free block.");

                default:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, $"GC cannot traverse object type {type}.");
            }
        }

        private void TraceGenerator(int payload, int payloadSize, ref int markStackHead)
        {
            RequirePayloadSize(payloadSize, GeneratorPayloadSize, "generator");
            var code = _memory.ReadValue(payload + 0);
            var globals = _memory.ReadValue(payload + 8);
            var function = _memory.ReadValue(payload + 16);
            var locals = _memory.ReadValue(payload + 24);
            var stack = _memory.ReadValue(payload + 32);

            MarkValue(code, ref markStackHead);
            MarkValue(globals, ref markStackHead);
            MarkValue(function, ref markStackHead);
            MarkValue(locals, ref markStackHead);
            MarkValue(stack, ref markStackHead);
            MarkValue(_memory.ReadValue(payload + 56), ref markStackHead);
            MarkValue(_memory.ReadValue(payload + 72), ref markStackHead);

            RequireObjectType(code, VmObjectType.Code);
            RequireObjectType(globals, VmObjectType.Dictionary);
            if (!function.IsNull)
                RequireObjectType(function, VmObjectType.Function);
            RequireObjectType(locals, VmObjectType.Storage);
            RequireObjectType(stack, VmObjectType.Storage);

            var localCount = ReadCodeInt32(code, 24);
            var stackCapacity = ReadCodeInt32(code, 12);
            var stackCount = _memory.ReadInt32(payload + 44);
            if (localCount < 0 || stackCapacity < 0 || stackCount < 0 || stackCount > stackCapacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator contains invalid GC metadata.");

            ValidateStorageValueSlots(locals, localCount, "generator locals");
            ValidateStorageValueSlots(stack, stackCapacity, "generator stack");
            TraceValueSlots(_memory.GetObjectPayloadAddress(locals), localCount, ref markStackHead);
            TraceValueSlots(_memory.GetObjectPayloadAddress(stack), stackCount, ref markStackHead);
        }

        private void ValidateStorageValueSlots(VmValue storage, int count, string owner)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, $"{owner} contains a negative slot count.");
            var requiredBytes = checked(count * 8);
            if (_memory.GetObjectAux0(storage) < requiredBytes ||
                _memory.GetObjectPayloadSize(storage.Address) < requiredBytes)
            {
                throw new VmTrapException(VmStopReason.InvalidBytecode, $"{owner} storage is truncated.");
            }
        }

        private static void ValidateValueSlots(int count, int payloadSize, string owner)
        {
            if (count < 0 || (long)count * 8 > payloadSize)
                throw new VmTrapException(VmStopReason.InvalidBytecode, $"{owner} contains invalid GC slot metadata.");
        }

        private static void RequirePayloadSize(int actual, int required, string owner)
        {
            if (actual < required)
                throw new VmTrapException(VmStopReason.InvalidBytecode, $"{owner} payload is truncated.");
        }

        private void TraceValueSlots(int address, int count, ref int markStackHead)
        {
            var slotsUntilCancellationPoll = 256;
            for (var index = 0; index < count; index++)
            {
                MarkValue(_memory.ReadValue(address + index * 8), ref markStackHead);
                if (--slotsUntilCancellationPoll == 0)
                {
                    _combinedCancellation.ThrowIfCancellationRequested();
                    slotsUntilCancellationPoll = 256;
                }
            }
            _combinedCancellation.ThrowIfCancellationRequested();
        }

        private void RunLoop()
        {
            while (_currentFrame != 0)
            {
                try
                {
                    ExecuteOneInstruction();
                }
                catch (VmControlTransferSignal)
                {
                    // A guest exception crossed a nested CLR helper call, such as
                    // generator resumption. The synthetic frame stack already points
                    // at the selected Python handler
                }
            }
        }

        private void ExecuteOneInstruction()
        {
            MaybeCollectGarbage();
            ConsumeInstructionBudget();
            var code = GetFrameCode(_currentFrame);
            var bytecodeStorage = ReadCodeValue(code, 32);
            RequireObjectType(bytecodeStorage, VmObjectType.Storage);
            var bytecodeLength = ReadCodeInt32(code, 20);
            var bytecodeUnits = bytecodeLength / 2;
            var ip = GetFrameInstructionPointer(_currentFrame);
            if ((uint)ip >= (uint)bytecodeUnits)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Instruction pointer is outside the code object.");

            var bytecode = _memory.GetReadOnlySpan(
                _memory.GetObjectPayloadAddress(bytecodeStorage),
                bytecodeLength);
            var operand = 0;
            var extendedCount = 0;
            PythonOpcode opcode;
            while (true)
            {
                var byteOffset = checked(ip * 2);
                opcode = (PythonOpcode)bytecode[byteOffset];
                var part = bytecode[byteOffset + 1];
                if (opcode != PythonOpcode.ExtendedArgument)
                {
                    operand = checked((operand << 8) | part);
                    break;
                }
                if (++extendedCount > 3)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Too many EXTENDED_ARG prefixes.");
                operand = checked((operand << 8) | part);
                ip++;
                if ((uint)ip >= (uint)bytecodeUnits)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Code object ends with EXTENDED_ARG.");
            }

            var caches = CPython3146OpcodeProfile.GetInlineCacheEntries(opcode);
            var nextIp = checked(ip + 1 + caches);
            if (nextIp > bytecodeUnits)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Inline cache entries extend past the code object.");
            for (var cache = ip + 1; cache < nextIp; cache++)
            {
                if ((PythonOpcode)bytecode[cache * 2] != PythonOpcode.Cache)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "A CPython inline-cache slot is not CACHE.");
            }
            var executingFrame = _currentFrame;
            SetFrameLastInstruction(executingFrame, ip);
            SetFrameInstructionPointer(executingFrame, nextIp);
            try
            {
                Dispatch(opcode, operand, nextIp, bytecodeUnits);
            }
            catch (VmGuestExceptionSignal)
            {
                var handlerFrame = HandleGuestException();
                if (handlerFrame != executingFrame)
                    throw new VmControlTransferSignal();
            }
        }

        private void Dispatch(PythonOpcode opcode, int operand, int nextIp, int bytecodeUnits)
        {
            switch (opcode)
            {
                case PythonOpcode.Resume:
                case PythonOpcode.Nop:
                case PythonOpcode.NotTaken:
                    return;

                case PythonOpcode.PopTop:
                case PythonOpcode.PopIterator:
                    _ = Pop();
                    return;

                case PythonOpcode.PushNull:
                    Push(VmValue.Null);
                    return;

                case PythonOpcode.ReturnValue:
                    ReturnFromFrame(Pop());
                    return;

                case PythonOpcode.ReturnGenerator:
                    ReturnGeneratorFromFrame();
                    return;

                case PythonOpcode.YieldValue:
                    YieldFromFrame(Pop());
                    return;

                case PythonOpcode.LoadConstant:
                    Push(GetTupleItem(ReadCodeValue(GetFrameCode(_currentFrame), 32 + 8), operand));
                    return;

                case PythonOpcode.LoadSmallInteger:
                    Push(CreateInteger(new BigInteger(operand)));
                    return;

                case PythonOpcode.LoadCommonConstant:
                    Push(LoadCommonConstant(operand));
                    return;

                case PythonOpcode.LoadBuildClass:
                    if (!TryGetDictionaryString(_builtins, "__build_class__", out var buildClass))
                        Raise("NameError", "__build_class__ not found");
                    Push(buildClass);
                    return;

                case PythonOpcode.LoadLocals:
                    {
                        var locals = GetFrameLocalsMapping(_currentFrame);
                        if (locals.IsNull)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "LOAD_LOCALS requires a frame locals mapping.");
                        Push(locals);
                        return;
                    }

                case PythonOpcode.LoadName:
                    Push(LoadName(operand, globalOnly: false));
                    return;

                case PythonOpcode.LoadFromDictionaryOrGlobals:
                    {
                        var mapping = Pop();
                        RequireObjectType(mapping, VmObjectType.Dictionary);
                        var name = GetCodeNameValue(GetFrameCode(_currentFrame), operand);
                        if (DictionaryTryGet(mapping, name, out var value))
                        {
                            Push(value);
                            return;
                        }
                        Push(LoadName(operand, globalOnly: true));
                        return;
                    }

                case PythonOpcode.LoadFromDictionaryOrDereference:
                    {
                        var mapping = Pop();
                        RequireObjectType(mapping, VmObjectType.Dictionary);
                        var name = GetLocalNameValue(_currentFrame, operand);
                        if (DictionaryTryGet(mapping, name, out var value))
                        {
                            Push(value);
                            return;
                        }

                        var kind = GetLocalKind(_currentFrame, operand);
                        if ((kind & (LocalKind.Cell | LocalKind.Free)) == 0)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "LOAD_FROM_DICT_OR_DEREF does not reference a closure slot.");
                        value = GetCellValue(GetLocal(_currentFrame, operand));
                        if (value.IsNull)
                        {
                            var nameText = GetString(name);
                            if ((kind & LocalKind.Free) != 0)
                                Raise("NameError", $"free variable '{nameText}' referenced before assignment in enclosing scope");
                            Raise("UnboundLocalError", $"local variable '{nameText}' referenced before assignment");
                        }
                        Push(value);
                        return;
                    }

                case PythonOpcode.LoadGlobal:
                    {
                        var value = LoadName(operand >> 1, globalOnly: true);
                        Push(value);
                        if ((operand & 1) != 0)
                            Push(VmValue.Null);
                        return;
                    }

                case PythonOpcode.StoreName:
                    StoreName(operand, Pop());
                    return;

                case PythonOpcode.StoreGlobal:
                    StoreGlobal(operand, Pop());
                    return;

                case PythonOpcode.DeleteName:
                    DeleteName(operand);
                    return;

                case PythonOpcode.DeleteGlobal:
                    DeleteGlobal(operand);
                    return;

                case PythonOpcode.LoadFast:
                case PythonOpcode.LoadFastBorrow:
                case PythonOpcode.LoadFastCheck:
                    {
                        var value = GetLocal(_currentFrame, operand);
                        if (value.IsNull)
                            Raise("UnboundLocalError", $"local variable '{GetLocalName(_currentFrame, operand)}' referenced before assignment");
                        Push(value);
                        return;
                    }

                case PythonOpcode.LoadFastAndClear:
                    {
                        var value = GetLocal(_currentFrame, operand);
                        SetLocal(_currentFrame, operand, VmValue.Null);
                        Push(value);
                        return;
                    }

                case PythonOpcode.StoreFast:
                    SetLocal(_currentFrame, operand, Pop());
                    return;

                case PythonOpcode.DeleteFast:
                    {
                        var value = GetLocal(_currentFrame, operand);
                        if (value.IsNull)
                            Raise("UnboundLocalError", $"local variable '{GetLocalName(_currentFrame, operand)}' is not bound");
                        SetLocal(_currentFrame, operand, VmValue.Null);
                        return;
                    }

                case PythonOpcode.MakeCell:
                    {
                        var kind = GetLocalKind(_currentFrame, operand);
                        if ((kind & LocalKind.Cell) == 0 || (kind & LocalKind.Free) != 0)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "MAKE_CELL does not reference a cell local.");
                        var value = GetLocal(_currentFrame, operand);
                        SetLocal(_currentFrame, operand, CreateCell(value));
                        return;
                    }

                case PythonOpcode.CopyFreeVariables:
                    CopyFreeVariables(_currentFrame, operand);
                    return;

                case PythonOpcode.LoadDereference:
                    {
                        var kind = GetLocalKind(_currentFrame, operand);
                        if ((kind & (LocalKind.Cell | LocalKind.Free)) == 0)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "LOAD_DEREF does not reference a closure slot.");
                        var value = GetCellValue(GetLocal(_currentFrame, operand));
                        if (value.IsNull)
                        {
                            var name = GetLocalName(_currentFrame, operand);
                            if ((kind & LocalKind.Free) != 0)
                                Raise("NameError", $"free variable '{name}' referenced before assignment in enclosing scope");
                            Raise("UnboundLocalError", $"local variable '{name}' referenced before assignment");
                        }
                        Push(value);
                        return;
                    }

                case PythonOpcode.StoreDereference:
                    {
                        var kind = GetLocalKind(_currentFrame, operand);
                        if ((kind & (LocalKind.Cell | LocalKind.Free)) == 0)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "STORE_DEREF does not reference a closure slot.");
                        SetCellValue(GetLocal(_currentFrame, operand), Pop());
                        return;
                    }

                case PythonOpcode.DeleteDereference:
                    {
                        var kind = GetLocalKind(_currentFrame, operand);
                        if ((kind & (LocalKind.Cell | LocalKind.Free)) == 0)
                            throw new VmTrapException(VmStopReason.InvalidBytecode, "DELETE_DEREF does not reference a closure slot.");
                        var cell = GetLocal(_currentFrame, operand);
                        if (GetCellValue(cell).IsNull)
                        {
                            var name = GetLocalName(_currentFrame, operand);
                            if ((kind & LocalKind.Free) != 0)
                                Raise("NameError", $"free variable '{name}' is not bound in enclosing scope");
                            Raise("UnboundLocalError", $"local variable '{name}' is not bound");
                        }
                        SetCellValue(cell, VmValue.Null);
                        return;
                    }

                case PythonOpcode.Copy:
                    Push(Peek(operand));
                    return;

                case PythonOpcode.Swap:
                    Swap(operand);
                    return;

                case PythonOpcode.ToBoolean:
                    Push(IsTruthy(Pop()) ? VmValue.True : VmValue.False);
                    return;

                case PythonOpcode.UnaryNot:
                    {
                        var value = Pop();
                        if (!value.IsBoolean)
                            Raise("SystemError", "UNARY_NOT requires the boolean result of TO_BOOL.");
                        Push(value.BooleanValue ? VmValue.False : VmValue.True);
                        return;
                    }

                case PythonOpcode.UnaryNegative:
                    Push(UnaryNegative(Pop()));
                    return;

                case PythonOpcode.UnaryInvert:
                    Push(CreateInteger(~GetInteger(Pop())));
                    return;

                case PythonOpcode.BinaryOperation:
                    {
                        var right = Pop();
                        var left = Pop();
                        Push(BinaryOperation((PythonBinaryOperation)operand, left, right));
                        return;
                    }

                case PythonOpcode.CompareOperation:
                    {
                        var right = Pop();
                        var left = Pop();
                        Push(Compare(operand >> 5, left, right) ? VmValue.True : VmValue.False);
                        return;
                    }

                case PythonOpcode.ContainsOperation:
                    {
                        var container = Pop();
                        var item = Pop();
                        var contains = Contains(container, item);
                        if ((operand & 1) != 0)
                            contains = !contains;
                        Push(contains ? VmValue.True : VmValue.False);
                        return;
                    }

                case PythonOpcode.IsOperation:
                    {
                        var right = Pop();
                        var left = Pop();
                        var equal = left == right;
                        if ((operand & 1) != 0)
                            equal = !equal;
                        Push(equal ? VmValue.True : VmValue.False);
                        return;
                    }

                case PythonOpcode.JumpForward:
                    JumpTo(checked(nextIp + operand), bytecodeUnits);
                    return;

                case PythonOpcode.JumpBackward:
                case PythonOpcode.JumpBackwardNoInterrupt:
                    JumpTo(checked(nextIp - operand), bytecodeUnits);
                    return;

                case PythonOpcode.PopJumpIfFalse:
                    if (!GetBooleanForJump(Pop()))
                        JumpTo(checked(nextIp + operand), bytecodeUnits);
                    return;

                case PythonOpcode.PopJumpIfTrue:
                    if (GetBooleanForJump(Pop()))
                        JumpTo(checked(nextIp + operand), bytecodeUnits);
                    return;

                case PythonOpcode.PopJumpIfNone:
                    if (Pop().IsNone)
                        JumpTo(checked(nextIp + operand), bytecodeUnits);
                    return;

                case PythonOpcode.PopJumpIfNotNone:
                    if (!Pop().IsNone)
                        JumpTo(checked(nextIp + operand), bytecodeUnits);
                    return;

                case PythonOpcode.BuildTuple:
                    Push(BuildTupleFromStack(operand));
                    return;

                case PythonOpcode.BuildString:
                    Push(BuildStringFromStack(operand));
                    return;

                case PythonOpcode.BuildInterpolation:
                    Push(BuildInterpolationFromStack(operand));
                    return;

                case PythonOpcode.BuildTemplate:
                    Push(BuildTemplateFromStack());
                    return;

                case PythonOpcode.BuildList:
                    Push(BuildListFromStack(operand));
                    return;

                case PythonOpcode.BuildSet:
                    Push(BuildSetFromStack(operand));
                    return;

                case PythonOpcode.BuildMap:
                    Push(BuildMapFromStack(operand));
                    return;

                case PythonOpcode.BuildSlice:
                    Push(BuildSlice(operand));
                    return;

                case PythonOpcode.ListAppend:
                    {
                        var value = Pop();
                        var list = Peek(operand);
                        ListAdd(list, value);
                        return;
                    }

                case PythonOpcode.ListExtend:
                    {
                        var iterable = Pop();
                        var list = Peek(operand);
                        ExtendList(list, iterable);
                        return;
                    }

                case PythonOpcode.SetAdd:
                    {
                        var value = Pop();
                        var set = Peek(operand);
                        SetAdd(set, value);
                        return;
                    }

                case PythonOpcode.SetUpdate:
                    {
                        var iterable = Pop();
                        var set = Peek(operand);
                        ExtendSet(set, iterable);
                        return;
                    }

                case PythonOpcode.DictionaryUpdate:
                case PythonOpcode.DictionaryMerge:
                    {
                        var update = Pop();
                        var target = Peek(operand);
                        DictionaryUpdate(target, update, rejectDuplicate: opcode == PythonOpcode.DictionaryMerge);
                        return;
                    }

                case PythonOpcode.MapAdd:
                    {
                        var value = Pop();
                        var key = Pop();
                        var dict = Peek(operand);
                        DictionarySet(dict, key, value, rejectDuplicate: false);
                        return;
                    }

                case PythonOpcode.StoreSubscript:
                    {
                        var key = Pop();
                        var container = Pop();
                        var value = Pop();
                        StoreSubscript(container, key, value);
                        return;
                    }

                case PythonOpcode.DeleteSubscript:
                    {
                        var key = Pop();
                        var container = Pop();
                        DeleteSubscript(container, key);
                        return;
                    }

                case PythonOpcode.GetIterator:
                    Push(CreateIterator(Pop()));
                    return;

                case PythonOpcode.GetYieldFromIterator:
                    {
                        var iterable = Pop();
                        Push(IsObjectType(iterable, VmObjectType.Generator)
                            ? iterable
                            : CreateIterator(iterable));
                        return;
                    }

                case PythonOpcode.Send:
                    SendToDelegatedIterator(operand, nextIp, bytecodeUnits);
                    return;

                case PythonOpcode.EndSend:
                    {
                        var result = Pop();
                        _ = Pop();
                        Push(result);
                        return;
                    }

                case PythonOpcode.ForIterator:
                    {
                        var iterator = Peek(1);
                        if (IteratorMoveNext(iterator, out var value))
                        {
                            Push(value);
                        }
                        else
                        {
                            JumpTo(checked(nextIp + operand + 1), bytecodeUnits);
                        }
                        return;
                    }

                case PythonOpcode.EndFor:
                    _ = Pop();
                    return;

                case PythonOpcode.MakeFunction:
                    Push(CreateFunction(Pop(), GetFrameGlobals(_currentFrame)));
                    return;

                case PythonOpcode.SetFunctionAttribute:
                    {
                        var function = Pop();
                        var attribute = Pop();
                        SetFunctionAttribute(function, operand, attribute);
                        Push(function);
                        return;
                    }

                case PythonOpcode.Call:
                    Call(operand, hasKeywords: false);
                    return;

                case PythonOpcode.CallKeyword:
                    Call(operand, hasKeywords: true);
                    return;

                case PythonOpcode.CallFunctionEx:
                    CallFunctionEx();
                    return;

                case PythonOpcode.ConvertValue:
                    Push(ConvertValue(Pop(), operand));
                    return;

                case PythonOpcode.FormatSimple:
                    {
                        var value = Peek(1);
                        var formatted = FormatValue(value, string.Empty);
                        _ = Pop();
                        Push(formatted);
                        return;
                    }

                case PythonOpcode.FormatWithSpec:
                    {
                        var formatSpec = Peek(1);
                        var value = Peek(2);
                        if (!IsObjectType(formatSpec, VmObjectType.String))
                            Raise("TypeError", $"format() argument 2 must be str, not {GetTypeName(formatSpec)}");
                        var formatted = FormatValue(value, GetString(formatSpec));
                        _ = Pop();
                        _ = Pop();
                        Push(formatted);
                        return;
                    }

                case PythonOpcode.CallIntrinsic1:
                    Push(CallIntrinsic1((PythonIntrinsic1)operand, Pop()));
                    return;

                case PythonOpcode.UnpackSequence:
                    UnpackSequence(Pop(), operand);
                    return;

                case PythonOpcode.UnpackExtended:
                    UnpackExtended(Pop(), operand & 0xFF, operand >> 8);
                    return;

                case PythonOpcode.LoadSuperAttribute:
                    LoadSuperAttribute(operand);
                    return;

                case PythonOpcode.LoadAttribute:
                    LoadAttribute(operand);
                    return;

                case PythonOpcode.StoreAttribute:
                    {
                        var owner = Pop();
                        var value = Pop();
                        StoreAttribute(owner, operand, value);
                        return;
                    }

                case PythonOpcode.DeleteAttribute:
                    DeleteAttribute(Pop(), operand);
                    return;

                case PythonOpcode.PushExceptionInfo:
                    PushExceptionInfo();
                    return;

                case PythonOpcode.PopExcept:
                    PopExcept();
                    return;

                case PythonOpcode.CheckExceptionMatch:
                    CheckExceptionMatch();
                    return;

                case PythonOpcode.RaiseVariableArguments:
                    RaiseVariableArguments(operand);
                    return;

                case PythonOpcode.Reraise:
                    Reraise(operand);
                    return;

                case PythonOpcode.ImportName:
                    ImportName(operand);
                    return;

                case PythonOpcode.ImportFrom:
                    ImportFrom(operand);
                    return;

                default:
                    throw new VmTrapException(
                        VmStopReason.UnsupportedOpcode,
                        $"Opcode {opcode} ({(byte)opcode}) is not implemented by the VM yet.");
            }
        }

        private VmValue LoadCodeObject(PythonCodeObject source)
        {
            if (source.Version != PythonBytecodeVersion.CPython3_14_6)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Nested code object has the wrong bytecode version.");
            if ((source.Bytecode.Length & 1) != 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Wordcode has an odd byte count.");
            const CodeFlags unsupportedFlags = CodeFlags.Coroutine | CodeFlags.AsyncGenerator;
            if ((source.Flags & unsupportedFlags) != 0)
                throw new VmTrapException(VmStopReason.UnsupportedOpcode, "Coroutines and async generators are not executable yet.");

            if (source.LocalsPlusNames.Length != source.LocalsPlusKinds.Length)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Locals-plus names and kinds have different lengths.");

            var freeVariableCount = 0;
            var reachedFreeVariables = false;
            for (var index = 0; index < source.LocalsPlusKinds.Length; index++)
            {
                var kind = source.LocalsPlusKinds[index];
                if ((kind & (LocalKind.Cell | LocalKind.Free)) == (LocalKind.Cell | LocalKind.Free))
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "A locals-plus entry cannot be both cell and free.");

                if ((kind & LocalKind.Free) != 0)
                {
                    reachedFreeVariables = true;
                    freeVariableCount++;
                }
                else if (reachedFreeVariables)
                {
                    throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        "Free-variable entries must be the trailing locals-plus entries.");
                }
            }

            ValidateExceptionTable(
                source.ExceptionTable.AsSpan(),
                source.Bytecode.Length / 2,
                source.StackSize);

            var bytecodeStorage = VmValue.FromAddress(_memory.AllocateStorage(source.Bytecode.Length));
            source.Bytecode.AsSpan().CopyTo(_memory.GetSpan(
                _memory.GetObjectPayloadAddress(bytecodeStorage),
                source.Bytecode.Length));

            var exceptionTableStorage = VmValue.FromAddress(
                _memory.AllocateStorage(source.ExceptionTable.Length));
            source.ExceptionTable.AsSpan().CopyTo(_memory.GetSpan(
                _memory.GetObjectPayloadAddress(exceptionTableStorage),
                source.ExceptionTable.Length));

            var constants = CreateTuple(source.Constants.Length);
            for (var index = 0; index < source.Constants.Length; index++)
            {
                SetTupleItem(constants, index, LoadConstant(source.Constants[index]));
                PollCancellation();
            }

            var names = CreateTuple(source.Names.Length);
            for (var index = 0; index < source.Names.Length; index++)
            {
                SetTupleItem(names, index, CreateString(source.Names[index]));
                PollCancellation();
            }

            var localNames = CreateTuple(source.LocalsPlusNames.Length);
            for (var index = 0; index < source.LocalsPlusNames.Length; index++)
            {
                SetTupleItem(localNames, index, CreateString(source.LocalsPlusNames[index]));
                PollCancellation();
            }

            var localKinds = VmValue.FromAddress(_memory.AllocateStorage(source.LocalsPlusKinds.Length));
            var kindsSpan = _memory.GetSpan(
                _memory.GetObjectPayloadAddress(localKinds),
                source.LocalsPlusKinds.Length);
            for (var index = 0; index < source.LocalsPlusKinds.Length; index++)
            {
                kindsSpan[index] = (byte)source.LocalsPlusKinds[index];
                PollCancellation();
            }

            var codeAddress = _memory.AllocateObject(VmObjectType.Code, CodePayloadSize);
            var code = VmValue.FromAddress(codeAddress);
            var payload = _memory.GetObjectPayloadAddress(code);
            _memory.WriteInt32(payload + 0, source.ArgumentCount);
            _memory.WriteInt32(payload + 4, source.PositionalOnlyArgumentCount);
            _memory.WriteInt32(payload + 8, source.KeywordOnlyArgumentCount);
            _memory.WriteInt32(payload + 12, source.StackSize);
            _memory.WriteInt32(payload + 16, (int)source.Flags);
            _memory.WriteInt32(payload + 20, source.Bytecode.Length);
            _memory.WriteInt32(payload + 24, source.LocalsPlusNames.Length);
            _memory.WriteInt32(payload + 28, freeVariableCount);
            _memory.WriteValue(payload + 32, bytecodeStorage);
            _memory.WriteValue(payload + 40, constants);
            _memory.WriteValue(payload + 48, names);
            _memory.WriteValue(payload + 56, localNames);
            _memory.WriteValue(payload + 64, localKinds);
            _memory.WriteValue(payload + 72, CreateString(source.QualifiedName));
            _memory.WriteValue(payload + 80, exceptionTableStorage);
            _memory.WriteInt32(payload + 88, source.ExceptionTable.Length);
            return code;
        }

        private VmValue LoadConstant(PythonConstant constant)
        {
            if (constant is null)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Code object contains a null constant.");
            if (_constantLoadDepth >= Math.Min(_limits.MaxCallDepth, 256))
                throw new VmTrapException(VmStopReason.CallDepthLimitExceeded, "Constant graph nesting exceeds the VM call depth policy.");
            _constantLoadDepth++;
            try
            {
                return constant.Kind switch
                {
                    ConstantKind.None => VmValue.None,
                    ConstantKind.Boolean => ((BooleanConstant)constant).Value ? VmValue.True : VmValue.False,
                    ConstantKind.Integer => CreateInteger(((IntegerConstant)constant).Value),
                    ConstantKind.Float => CreateFloat(((FloatConstant)constant).Value),
                    ConstantKind.Complex => CreateComplex(
                        ((ComplexConstant)constant).Real,
                        ((ComplexConstant)constant).Imaginary),
                    ConstantKind.String => CreateString(((StringConstant)constant).Value),
                    ConstantKind.Bytes => CreateBytes(((BytesConstant)constant).Value.AsSpan()),
                    ConstantKind.Tuple => LoadTupleConstant((TupleConstant)constant),
                    ConstantKind.FrozenSet => LoadFrozenSetConstant((FrozenSetConstant)constant),
                    ConstantKind.Code => LoadCodeObject(((CodeConstant)constant).Value),
                    ConstantKind.Ellipsis => VmValue.Ellipsis,
                    _ => throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        $"Unsupported constant kind {constant.Kind}."),
                };
            }
            finally
            {
                _constantLoadDepth--;
            }
        }

        private VmValue LoadTupleConstant(TupleConstant constant)
        {
            var tuple = CreateTuple(constant.Items.Length);
            for (var index = 0; index < constant.Items.Length; index++)
            {
                SetTupleItem(tuple, index, LoadConstant(constant.Items[index]));
                PollCancellation();
            }
            return tuple;
        }

        private VmValue LoadFrozenSetConstant(FrozenSetConstant constant)
        {
            var dictionary = CreateDictionary(Math.Max(8, constant.Items.Length * 2));
            for (var index = 0; index < constant.Items.Length; index++)
            {
                DictionarySet(dictionary, LoadConstant(constant.Items[index]), VmValue.None, rejectDuplicate: false);
                PollCancellation();
            }
            var address = _memory.AllocateObject(VmObjectType.FrozenSet, 8);
            var value = VmValue.FromAddress(address);
            _memory.WriteValue(_memory.GetObjectPayloadAddress(value), dictionary);
            return value;
        }

        private VmValue CreateInteger(BigInteger value)
        {
            EnsureIntegerSize(value);
            if (value >= -(BigInteger.One << 60) && value <= (BigInteger.One << 60) - 1)
                return VmValue.FromSmallInteger((long)value);

            var byteCount = value.GetByteCount(isUnsigned: false);
            var address = _memory.AllocateObject(VmObjectType.Integer, byteCount, byteCount, 0);
            var result = VmValue.FromAddress(address);
            if (!value.TryWriteBytes(
                    _memory.GetSpan(_memory.GetObjectPayloadAddress(result), byteCount),
                    out var written,
                    isUnsigned: false,
                    isBigEndian: false) || written != byteCount)
            {
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Failed to encode a Python integer.");
            }
            return result;
        }

        private BigInteger GetInteger(VmValue value)
        {
            if (value.IsSmallInteger)
                return new BigInteger(value.SmallIntegerValue);
            if (value.IsBoolean)
                return value.BooleanValue ? BigInteger.One : BigInteger.Zero;
            RequireObjectType(value, VmObjectType.Integer);
            var length = _memory.GetObjectAux0(value);
            var result = new BigInteger(
                _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(value), length),
                isUnsigned: false,
                isBigEndian: false);
            EnsureIntegerSize(result);
            return result;
        }

        private bool IsInteger(VmValue value)
        {
            return value.IsSmallInteger || value.IsBoolean ||
                (value.IsAddress && _memory.GetObjectType(value) == VmObjectType.Integer);
        }

        private VmValue CreateFloat(double value)
        {
            var result = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Float, 8));
            _memory.WriteDouble(_memory.GetObjectPayloadAddress(result), value);
            return result;
        }

        private double GetFloat(VmValue value)
        {
            if (IsInteger(value))
                return (double)GetInteger(value);
            RequireObjectType(value, VmObjectType.Float);
            return _memory.ReadDouble(_memory.GetObjectPayloadAddress(value));
        }

        private bool IsFloat(VmValue value)
        {
            return value.IsAddress && _memory.GetObjectType(value) == VmObjectType.Float;
        }

        private VmValue CreateComplex(double real, double imaginary)
        {
            var result = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Complex, 16));
            var payload = _memory.GetObjectPayloadAddress(result);
            _memory.WriteDouble(payload, real);
            _memory.WriteDouble(payload + 8, imaginary);
            return result;
        }

        private VmValue CreateString(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var address = _memory.AllocateObject(VmObjectType.String, byteCount, byteCount, 0);
            var result = VmValue.FromAddress(address);
            var written = Encoding.UTF8.GetBytes(
                value.AsSpan(),
                _memory.GetSpan(_memory.GetObjectPayloadAddress(result), byteCount));
            if (written != byteCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Failed to encode a Python string.");
            return result;
        }

        private string GetString(VmValue value)
        {
            RequireObjectType(value, VmObjectType.String);
            var length = _memory.GetObjectAux0(value);
            return Encoding.UTF8.GetString(
                _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(value), length));
        }

        private VmValue ConcatenateStrings(VmValue left, VmValue right)
        {
            RequireObjectType(left, VmObjectType.String);
            RequireObjectType(right, VmObjectType.String);
            var leftLength = _memory.GetObjectAux0(left);
            var rightLength = _memory.GetObjectAux0(right);
            var totalLength = (long)leftLength + rightLength;
            if (totalLength > int.MaxValue || !_memory.CanAllocateObjectPayload((int)totalLength))
            {
                throw new VmTrapException(
                    VmStopReason.MemoryLimitExceeded,
                    "Concatenated string exceeds the VM memory policy.");
            }

            var address = _memory.AllocateObject(
                VmObjectType.String,
                (int)totalLength,
                (int)totalLength,
                0);
            var result = VmValue.FromAddress(address);
            var destination = _memory.GetSpan(_memory.GetObjectPayloadAddress(result), (int)totalLength);
            _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(left), leftLength).CopyTo(destination);
            _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(right), rightLength).CopyTo(destination[leftLength..]);
            return result;
        }

        private VmValue CreateInterpolation(
            VmValue value,
            VmValue expression,
            VmValue conversion,
            VmValue formatSpec)
        {
            RequireObjectType(expression, VmObjectType.String);
            if (!conversion.IsNone)
                RequireObjectType(conversion, VmObjectType.String);
            RequireObjectType(formatSpec, VmObjectType.String);

            var interpolation = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Interpolation,
                InterpolationPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(interpolation);
            _memory.WriteValue(payload, value);
            _memory.WriteValue(payload + 8, expression);
            _memory.WriteValue(payload + 16, conversion);
            _memory.WriteValue(payload + 24, formatSpec);
            return interpolation;
        }

        private VmValue CreateTemplate(VmValue strings, VmValue interpolations)
        {
            RequireObjectType(strings, VmObjectType.Tuple);
            RequireObjectType(interpolations, VmObjectType.Tuple);
            var interpolationCount = GetTupleCount(interpolations);
            if (GetTupleCount(strings) != checked(interpolationCount + 1))
            {
                throw new VmTrapException(
                    VmStopReason.InvalidBytecode,
                    "BUILD_TEMPLATE requires exactly one more string than interpolation.");
            }
            for (var index = 0; index < GetTupleCount(strings); index++)
            {
                if (!IsObjectType(GetTupleItem(strings, index), VmObjectType.String))
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_TEMPLATE strings tuple contains a non-string value.");
            }
            for (var index = 0; index < interpolationCount; index++)
            {
                if (!IsObjectType(GetTupleItem(interpolations, index), VmObjectType.Interpolation))
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_TEMPLATE interpolation tuple contains an invalid value.");
            }

            var template = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Template,
                TemplatePayloadSize));
            var payload = _memory.GetObjectPayloadAddress(template);
            _memory.WriteValue(payload, strings);
            _memory.WriteValue(payload + 8, interpolations);
            return template;
        }

        private VmValue CreateBytes(ReadOnlySpan<byte> bytes)
        {
            var address = _memory.AllocateObject(VmObjectType.Bytes, bytes.Length, bytes.Length, 0);
            var result = VmValue.FromAddress(address);
            bytes.CopyTo(_memory.GetSpan(_memory.GetObjectPayloadAddress(result), bytes.Length));
            return result;
        }

        private VmValue CreateTuple(int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative tuple size.");
            return VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Tuple,
                checked(count * 8),
                count,
                0));
        }

        private int GetTupleCount(VmValue tuple)
        {
            RequireObjectType(tuple, VmObjectType.Tuple);
            return _memory.GetObjectAux0(tuple);
        }

        private VmValue GetTupleItem(VmValue tuple, int index)
        {
            var count = GetTupleCount(tuple);
            if ((uint)index >= (uint)count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Tuple index in bytecode metadata is out of range.");
            return _memory.ReadValue(checked(_memory.GetObjectPayloadAddress(tuple) + index * 8));
        }

        private void SetTupleItem(VmValue tuple, int index, VmValue value)
        {
            var count = GetTupleCount(tuple);
            if ((uint)index >= (uint)count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Tuple initialization index is out of range.");
            _memory.WriteValue(checked(_memory.GetObjectPayloadAddress(tuple) + index * 8), value);
        }

        private VmValue CreateList(int capacity)
        {
            capacity = NormalizeCapacity(capacity);
            var items = VmValue.FromAddress(_memory.AllocateStorage(checked(capacity * 8)));
            var list = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.List, ListPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(list);
            _memory.WriteValue(payload, items);
            _memory.WriteInt32(payload + 8, 0);
            _memory.WriteInt32(payload + 12, capacity);
            return list;
        }

        private int GetListCount(VmValue list)
        {
            RequireObjectType(list, VmObjectType.List);
            return _memory.ReadInt32(_memory.GetObjectPayloadAddress(list) + 8);
        }

        private VmValue GetListItem(VmValue list, int index)
        {
            var count = GetListCount(list);
            index = NormalizeIndex(index, count);
            var payload = _memory.GetObjectPayloadAddress(list);
            var storage = _memory.ReadValue(payload);
            return _memory.ReadValue(checked(_memory.GetObjectPayloadAddress(storage) + index * 8));
        }

        private void SetListItem(VmValue list, int index, VmValue value)
        {
            var count = GetListCount(list);
            index = NormalizeIndex(index, count);
            var payload = _memory.GetObjectPayloadAddress(list);
            var storage = _memory.ReadValue(payload);
            _memory.WriteValue(checked(_memory.GetObjectPayloadAddress(storage) + index * 8), value);
        }

        private void ListAdd(VmValue list, VmValue value)
        {
            RequireObjectType(list, VmObjectType.List);
            var payload = _memory.GetObjectPayloadAddress(list);
            var count = _memory.ReadInt32(payload + 8);
            EnsureListCapacity(list, checked(count + 1));
            payload = _memory.GetObjectPayloadAddress(list);
            var storage = _memory.ReadValue(payload);
            _memory.WriteValue(checked(_memory.GetObjectPayloadAddress(storage) + count * 8), value);
            _memory.WriteInt32(payload + 8, count + 1);
        }

        private VmValue ListPop(VmValue list, int index)
        {
            RequireObjectType(list, VmObjectType.List);
            var payload = _memory.GetObjectPayloadAddress(list);
            var count = _memory.ReadInt32(payload + 8);
            if (count == 0)
                Raise("IndexError", "pop from empty list");
            index = NormalizeIndex(index, count);
            var storage = _memory.ReadValue(payload);
            var storagePayload = _memory.GetObjectPayloadAddress(storage);
            var value = _memory.ReadValue(storagePayload + index * 8);
            var tail = count - index - 1;
            if (tail > 0)
                _memory.Copy(storagePayload + (index + 1) * 8, storagePayload + index * 8, tail * 8);
            _memory.WriteValue(storagePayload + (count - 1) * 8, VmValue.Null);
            _memory.WriteInt32(payload + 8, count - 1);
            return value;
        }

        private void EnsureListCapacity(VmValue list, int required)
        {
            var payload = _memory.GetObjectPayloadAddress(list);
            var capacity = _memory.ReadInt32(payload + 12);
            if (required <= capacity)
                return;
            var nextCapacity = capacity;
            while (nextCapacity < required)
                nextCapacity = checked(nextCapacity * 2);
            var oldStorage = _memory.ReadValue(payload);
            var newStorage = VmValue.FromAddress(_memory.AllocateStorage(checked(nextCapacity * 8)));
            var count = _memory.ReadInt32(payload + 8);
            _memory.Copy(
                _memory.GetObjectPayloadAddress(oldStorage),
                _memory.GetObjectPayloadAddress(newStorage),
                checked(count * 8));
            _memory.WriteValue(payload, newStorage);
            _memory.WriteInt32(payload + 12, nextCapacity);
        }

        private VmValue CreateDictionary(int capacity)
        {
            capacity = NormalizeCapacity(capacity);
            var entries = VmValue.FromAddress(_memory.AllocateStorage(checked(capacity * DictionaryEntrySize)));
            var dictionary = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Dictionary,
                DictionaryPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            _memory.WriteValue(payload, entries);
            _memory.WriteInt32(payload + 8, 0);
            _memory.WriteInt32(payload + 12, capacity);
            return dictionary;
        }

        private int GetDictionaryCount(VmValue dictionary)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            return _memory.ReadInt32(_memory.GetObjectPayloadAddress(dictionary) + 8);
        }

        private bool DictionaryTryGet(VmValue dictionary, VmValue key, out VmValue value)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            var hash = GetHash(key);
            var slot = FindDictionarySlot(dictionary, key, hash, out var found);
            if (!found)
            {
                value = VmValue.Null;
                return false;
            }
            var entries = GetDictionaryEntriesPayload(dictionary);
            value = _memory.ReadValue(entries + slot * DictionaryEntrySize + 16);
            return true;
        }

        private void DictionarySet(
            VmValue dictionary,
            VmValue key,
            VmValue value,
            bool rejectDuplicate)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            EnsureHashable(key);
            EnsureDictionaryCapacity(dictionary);
            var hash = GetHash(key);
            var slot = FindDictionarySlot(dictionary, key, hash, out var found);
            if (found && rejectDuplicate)
                Raise("KeyError", $"duplicate key: {Repr(key, 0)}");
            var entries = GetDictionaryEntriesPayload(dictionary);
            var entry = entries + slot * DictionaryEntrySize;
            if (!found)
            {
                _memory.WriteUInt64(entry, hash);
                _memory.WriteValue(entry + 8, key);
                var payload = _memory.GetObjectPayloadAddress(dictionary);
                _memory.WriteInt32(payload + 8, checked(_memory.ReadInt32(payload + 8) + 1));
            }
            _memory.WriteValue(entry + 16, value);
        }

        private bool DictionaryDelete(VmValue dictionary, VmValue key)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            var hash = GetHash(key);
            var slot = FindDictionarySlot(dictionary, key, hash, out var found);
            if (!found)
                return false;
            var entries = GetDictionaryEntriesPayload(dictionary);
            var entry = entries + slot * DictionaryEntrySize;
            _memory.WriteUInt64(entry, 0);
            _memory.WriteValue(entry + 8, VmValue.Deleted);
            _memory.WriteValue(entry + 16, VmValue.Null);
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            _memory.WriteInt32(payload + 8, _memory.ReadInt32(payload + 8) - 1);
            return true;
        }

        private void EnsureDictionaryCapacity(VmValue dictionary)
        {
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            var count = _memory.ReadInt32(payload + 8);
            var capacity = _memory.ReadInt32(payload + 12);
            if (checked((count + 1) * 3) < checked(capacity * 2))
                return;

            var oldEntries = _memory.ReadValue(payload);
            var oldEntriesPayload = _memory.GetObjectPayloadAddress(oldEntries);
            var newCapacity = checked(capacity * 2);
            var newEntries = VmValue.FromAddress(_memory.AllocateStorage(
                checked(newCapacity * DictionaryEntrySize)));
            _memory.WriteValue(payload, newEntries);
            _memory.WriteInt32(payload + 8, 0);
            _memory.WriteInt32(payload + 12, newCapacity);

            for (var index = 0; index < capacity; index++)
            {
                var entry = oldEntriesPayload + index * DictionaryEntrySize;
                var key = _memory.ReadValue(entry + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                DictionarySet(
                    dictionary,
                    key,
                    _memory.ReadValue(entry + 16),
                    rejectDuplicate: false);
            }
        }

        private int FindDictionarySlot(
            VmValue dictionary,
            VmValue key,
            ulong hash,
            out bool found)
        {
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            var capacity = _memory.ReadInt32(payload + 12);
            var mask = capacity - 1;
            var entries = GetDictionaryEntriesPayload(dictionary);
            var slot = (int)(hash & (uint)mask);
            var firstDeleted = -1;
            for (var probe = 0; probe < capacity; probe++)
            {
                var entry = entries + slot * DictionaryEntrySize;
                var currentKey = _memory.ReadValue(entry + 8);
                if (currentKey.IsNull)
                {
                    found = false;
                    return firstDeleted >= 0 ? firstDeleted : slot;
                }
                if (currentKey.IsDeleted)
                {
                    if (firstDeleted < 0)
                        firstDeleted = slot;
                }
                else if (_memory.ReadUInt64(entry) == hash &&
                    (currentKey == key || ValuesEqual(currentKey, key)))
                {
                    found = true;
                    return slot;
                }
                slot = (slot + 1) & mask;
            }
            if (firstDeleted >= 0)
            {
                found = false;
                return firstDeleted;
            }
            throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Python dictionary has no free slot.");
        }

        private int GetDictionaryEntriesPayload(VmValue dictionary)
        {
            var entries = _memory.ReadValue(_memory.GetObjectPayloadAddress(dictionary));
            RequireObjectType(entries, VmObjectType.Storage);
            return _memory.GetObjectPayloadAddress(entries);
        }

        private VmValue CreateSet()
        {
            var dictionary = CreateDictionary(8);
            var set = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Set, 8));
            _memory.WriteValue(_memory.GetObjectPayloadAddress(set), dictionary);
            return set;
        }

        private VmValue CreateFrozenSet(VmValue iterable)
        {
            var dictionary = CreateDictionary(8);
            if (!iterable.IsNull)
            {
                var iterator = CreateIterator(iterable);
                var rootBase = PushHostRoots(dictionary, iterable, iterator);
                try
                {
                    while (IteratorMoveNext(iterator, out var item))
                    {
                        DictionarySet(dictionary, item, VmValue.None, rejectDuplicate: false);
                        ConsumeInstructionBudget();
                    }
                }
                finally
                {
                    PopHostRoots(rootBase);
                }
            }
            var value = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.FrozenSet, 8));
            _memory.WriteValue(_memory.GetObjectPayloadAddress(value), dictionary);
            return value;
        }

        private VmValue GetSetDictionary(VmValue set)
        {
            var type = _memory.GetObjectType(set);
            if (type is not (VmObjectType.Set or VmObjectType.FrozenSet))
                Raise("TypeError", $"expected set, got {GetTypeName(set)}");
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(set));
        }

        private void SetAdd(VmValue set, VmValue value)
        {
            if (_memory.GetObjectType(set) == VmObjectType.FrozenSet)
                Raise("AttributeError", "frozenset is immutable");
            DictionarySet(GetSetDictionary(set), value, VmValue.None, rejectDuplicate: false);
        }

        private VmValue CreateSlice(VmValue start, VmValue stop, VmValue step)
        {
            var slice = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Slice, SlicePayloadSize));
            var payload = _memory.GetObjectPayloadAddress(slice);
            _memory.WriteValue(payload, start);
            _memory.WriteValue(payload + 8, stop);
            _memory.WriteValue(payload + 16, step);
            return slice;
        }

        private VmValue CreateRange(long start, long stop, long step)
        {
            if (step == 0)
                Raise("ValueError", "range() arg 3 must not be zero");
            var range = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Range, RangePayloadSize));
            var payload = _memory.GetObjectPayloadAddress(range);
            _memory.WriteInt64(payload, start);
            _memory.WriteInt64(payload + 8, stop);
            _memory.WriteInt64(payload + 16, step);
            return range;
        }

        private VmValue CreateIterator(VmValue iterable)
        {
            if (iterable.IsAddress)
            {
                var type = _memory.GetObjectType(iterable);
                if (type is VmObjectType.Iterator or VmObjectType.Generator or VmObjectType.BuiltinIterator)
                    return iterable;
                if (type == VmObjectType.Instance)
                {
                    if (HasSpecialMethod(iterable, "__iter__"))
                    {
                        var result = CallZeroArgumentSpecialMethod(iterable, "__iter__");
                        if (IsObjectType(result, VmObjectType.Iterator) ||
                            IsObjectType(result, VmObjectType.BuiltinIterator) ||
                            IsObjectType(result, VmObjectType.Generator) ||
                            (IsObjectType(result, VmObjectType.Instance) && HasSpecialMethod(result, "__next__")))
                        {
                            return result;
                        }
                        Raise("TypeError", $"iter() returned non-iterator of type '{GetTypeName(result)}'");
                    }
                    if (HasSpecialMethod(iterable, "__getitem__"))
                        return CreateBuiltinIterator(VmBuiltinIteratorKind.Sequence, iterable);
                }
            }
            if (!IsIterable(iterable))
                Raise("TypeError", $"'{GetTypeName(iterable)}' object is not iterable");
            var iterator = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Iterator,
                IteratorPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(iterator);
            _memory.WriteValue(payload, iterable);
            _memory.WriteInt64(payload + 8, 0);
            _memory.WriteInt32(payload + 16, 0);
            _memory.WriteInt32(payload + 20, 0);
            return iterator;
        }

        private VmValue CreateFunction(VmValue code, VmValue globals)
        {
            RequireObjectType(code, VmObjectType.Code);
            RequireObjectType(globals, VmObjectType.Dictionary);
            var function = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Function,
                FunctionPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(function);
            _memory.WriteValue(payload, code);
            _memory.WriteValue(payload + 8, globals);
            _memory.WriteValue(payload + 16, VmValue.None);
            _memory.WriteValue(payload + 24, VmValue.None);
            _memory.WriteValue(payload + 32, VmValue.None);
            _memory.WriteValue(payload + 40, ReadCodeValue(code, 72));
            _memory.WriteValue(payload + 48, CreateDictionary(4));
            return function;
        }

        private void BootstrapTypeSystem()
        {
            _objectClass = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Class,
                ClassPayloadSize));
            _typeClass = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Class,
                ClassPayloadSize));

            var objectNamespace = CreateDictionary(8);
            var typeNamespace = CreateDictionary(8);
            var objectBases = CreateTuple(0);
            var objectMro = CreateTuple(1);
            SetTupleItem(objectMro, 0, _objectClass);
            var typeBases = CreateTuple(1);
            SetTupleItem(typeBases, 0, _objectClass);
            var typeMro = CreateTuple(2);
            SetTupleItem(typeMro, 0, _typeClass);
            SetTupleItem(typeMro, 1, _objectClass);
            var builtinsModuleName = CreateString("builtins");

            InitializeClassPayload(
                _objectClass,
                CreateString("object"),
                objectNamespace,
                objectBases,
                objectMro,
                _typeClass,
                builtinsModuleName);
            InitializeClassPayload(
                _typeClass,
                CreateString("type"),
                typeNamespace,
                typeBases,
                typeMro,
                _typeClass,
                builtinsModuleName);

            SetDictionaryString(objectNamespace, "__doc__", VmValue.None);
            SetDictionaryString(objectNamespace, "__init__", VmValue.FromBuiltin(VmBuiltin.ObjectInit));
            SetDictionaryString(typeNamespace, "__doc__", VmValue.None);
            DictionarySet(_builtins, CreateString("object"), _objectClass, rejectDuplicate: false);
            DictionarySet(_builtins, CreateString("type"), _typeClass, rejectDuplicate: false);
        }

        private void InitializeClassPayload(
            VmValue classObject,
            VmValue name,
            VmValue namespaceDictionary,
            VmValue bases,
            VmValue mro,
            VmValue metaclass,
            VmValue moduleName)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            RequireObjectType(name, VmObjectType.String);
            RequireObjectType(namespaceDictionary, VmObjectType.Dictionary);
            RequireObjectType(bases, VmObjectType.Tuple);
            RequireObjectType(mro, VmObjectType.Tuple);
            RequireObjectType(metaclass, VmObjectType.Class);
            RequireObjectType(moduleName, VmObjectType.String);
            var payload = _memory.GetObjectPayloadAddress(classObject);
            _memory.WriteValue(payload + 0, name);
            _memory.WriteValue(payload + 8, name);
            _memory.WriteValue(payload + 16, namespaceDictionary);
            _memory.WriteValue(payload + 24, bases);
            _memory.WriteValue(payload + 32, mro);
            _memory.WriteValue(payload + 40, metaclass);
            _memory.WriteValue(payload + 48, moduleName);
            _memory.WriteValue(payload + 56, CreateMappingProxy(namespaceDictionary));
        }

        private VmValue CreateMappingProxy(VmValue dictionary)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            var proxy = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.MappingProxy,
                MappingProxyPayloadSize));
            _memory.WriteValue(_memory.GetObjectPayloadAddress(proxy), dictionary);
            return proxy;
        }

        private VmValue GetMappingProxyDictionary(VmValue proxy)
        {
            RequireObjectType(proxy, VmObjectType.MappingProxy);
            var dictionary = _memory.ReadValue(_memory.GetObjectPayloadAddress(proxy));
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            return dictionary;
        }

        private VmValue CreateBuiltinIterator(
            VmBuiltinIteratorKind kind,
            VmValue primary,
            VmValue secondary = default,
            VmValue tertiary = default,
            int flags = 0,
            long index = 0)
        {
            var iterator = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.BuiltinIterator,
                BuiltinIteratorPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(iterator);
            _memory.WriteInt32(payload, (int)kind);
            _memory.WriteInt32(payload + 4, flags);
            _memory.WriteValue(payload + 8, primary);
            _memory.WriteValue(payload + 16, secondary.IsNull ? VmValue.None : secondary);
            _memory.WriteValue(payload + 24, tertiary.IsNull ? VmValue.None : tertiary);
            _memory.WriteInt64(payload + 32, index);
            return iterator;
        }

        private VmValue CreateStaticMethod(VmValue callable)
        {
            if (!IsCallableValue(callable))
                Raise("TypeError", $"'{GetTypeName(callable)}' object is not callable");
            var descriptor = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.StaticMethod,
                StaticMethodPayloadSize));
            _memory.WriteValue(_memory.GetObjectPayloadAddress(descriptor), callable);
            return descriptor;
        }

        private VmValue CreateClassMethod(VmValue callable)
        {
            if (!IsCallableValue(callable))
                Raise("TypeError", $"'{GetTypeName(callable)}' object is not callable");
            var descriptor = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.ClassMethod,
                ClassMethodPayloadSize));
            _memory.WriteValue(_memory.GetObjectPayloadAddress(descriptor), callable);
            return descriptor;
        }

        private VmValue CreateProperty(VmValue getter, VmValue setter, VmValue deleter, VmValue doc)
        {
            if (!getter.IsNone && !IsCallableValue(getter))
                Raise("TypeError", "property getter must be callable");
            if (!setter.IsNone && !IsCallableValue(setter))
                Raise("TypeError", "property setter must be callable");
            if (!deleter.IsNone && !IsCallableValue(deleter))
                Raise("TypeError", "property deleter must be callable");
            if (!doc.IsNone && !IsObjectType(doc, VmObjectType.String))
                Raise("TypeError", "property doc must be a string or None");
            var descriptor = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Property,
                PropertyPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(descriptor);
            _memory.WriteValue(payload, getter);
            _memory.WriteValue(payload + 8, setter);
            _memory.WriteValue(payload + 16, deleter);
            _memory.WriteValue(payload + 24, doc);
            return descriptor;
        }

        private VmValue GetDescriptorCallable(VmValue descriptor)
        {
            if (!descriptor.IsAddress)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Descriptor is not an object.");
            var type = _memory.GetObjectType(descriptor);
            if (type is not (VmObjectType.StaticMethod or VmObjectType.ClassMethod))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Object is not a method descriptor.");
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(descriptor));
        }

        private VmValue BindClassAttribute(VmValue attribute, VmValue receiver, VmValue receiverClass)
        {
            if (!attribute.IsAddress)
                return attribute;
            switch (_memory.GetObjectType(attribute))
            {
                case VmObjectType.Function:
                    return receiver.IsNull ? attribute : CreatePythonBoundMethod(attribute, receiver);
                case VmObjectType.StaticMethod:
                    return GetDescriptorCallable(attribute);
                case VmObjectType.ClassMethod:
                    return CreatePythonBoundMethod(GetDescriptorCallable(attribute), receiverClass);
                case VmObjectType.Property:
                    if (receiver.IsNull)
                        return attribute;
                    {
                        var getter = _memory.ReadValue(_memory.GetObjectPayloadAddress(attribute));
                        if (getter.IsNone)
                            Raise("AttributeError", "property has no getter");
                        var arguments = CreateTuple(1);
                        SetTupleItem(arguments, 0, receiver);
                        return ExecuteCallableSynchronously(getter, arguments, 1, VmValue.None);
                    }
                default:
                    return attribute;
            }
        }

        private bool TryGetSpecialMethod(VmValue instance, string name, out VmValue method)
        {
            if (!IsObjectType(instance, VmObjectType.Instance))
            {
                method = VmValue.Null;
                return false;
            }
            var nameValue = CreateString(name);
            if (!TryLookupClassAttribute(GetInstanceClass(instance), nameValue, out var attribute))
            {
                method = VmValue.Null;
                return false;
            }
            method = BindClassAttribute(attribute, instance, GetInstanceClass(instance));
            return IsCallableValue(method);
        }

        private bool HasSpecialMethod(VmValue instance, string name)
        {
            return TryGetSpecialMethod(instance, name, out _);
        }

        private VmValue CallSpecialMethod(VmValue instance, string name, VmValue arguments, int argumentCount)
        {
            var argumentRootBase = PushHostRoots(instance, arguments, VmValue.Null);
            try
            {
                if (!TryGetSpecialMethod(instance, name, out var method))
                    Raise("TypeError", $"'{GetTypeName(instance)}' object does not define {name}");
                var methodRootBase = PushHostRoots(method, VmValue.Null, VmValue.Null);
                try
                {
                    return ExecuteCallableSynchronously(method, arguments, argumentCount, VmValue.None);
                }
                finally
                {
                    PopHostRoots(methodRootBase);
                }
            }
            finally
            {
                PopHostRoots(argumentRootBase);
            }
        }

        private VmValue CallZeroArgumentSpecialMethod(VmValue instance, string name)
        {
            EnsureHostRootCapacity(6);
            return CallSpecialMethod(instance, name, CreateTuple(0), 0);
        }

        private VmValue CallBinarySpecialMethod(VmValue instance, string name, VmValue argument)
        {
            EnsureHostRootCapacity(6);
            var arguments = CreateTuple(1);
            SetTupleItem(arguments, 0, argument);
            return CallSpecialMethod(instance, name, arguments, 1);
        }

        private bool IsCallableValue(VmValue value)
        {
            if (value.IsBuiltin)
                return true;
            if (!value.IsAddress)
                return false;
            switch (_memory.GetObjectType(value))
            {
                case VmObjectType.Function:
                case VmObjectType.BoundMethod:
                case VmObjectType.PythonBoundMethod:
                case VmObjectType.Class:
                case VmObjectType.StaticMethod:
                    return true;
                case VmObjectType.Instance:
                    return HasSpecialMethod(value, "__call__");
                default:
                    return false;
            }
        }

        private bool IsSubclassOf(VmValue candidate, VmValue expected)
        {
            RequireObjectType(candidate, VmObjectType.Class);
            RequireObjectType(expected, VmObjectType.Class);
            var mro = GetClassMro(candidate);
            for (var index = 0; index < GetTupleCount(mro); index++)
            {
                if (GetTupleItem(mro, index) == expected)
                    return true;
                PollCancellation();
            }
            return false;
        }

        private VmValue GetRuntimeTypeToken(VmValue value)
        {
            if (value.IsNone)
                return _objectClass;
            if (value.IsBoolean)
                return VmValue.FromBuiltin(VmBuiltin.Bool);
            if (IsInteger(value))
                return VmValue.FromBuiltin(VmBuiltin.Int);
            if (value.IsBuiltin)
                return _typeClass;
            if (!value.IsAddress)
                return _objectClass;
            return _memory.GetObjectType(value) switch
            {
                VmObjectType.Float => VmValue.FromBuiltin(VmBuiltin.Float),
                VmObjectType.Complex => VmValue.FromBuiltin(VmBuiltin.Complex),
                VmObjectType.String => VmValue.FromBuiltin(VmBuiltin.Str),
                VmObjectType.Bytes => VmValue.FromBuiltin(VmBuiltin.Bytes),
                VmObjectType.Tuple => VmValue.FromBuiltin(VmBuiltin.Tuple),
                VmObjectType.List => VmValue.FromBuiltin(VmBuiltin.List),
                VmObjectType.Dictionary => VmValue.FromBuiltin(VmBuiltin.Dict),
                VmObjectType.Set => VmValue.FromBuiltin(VmBuiltin.Set),
                VmObjectType.FrozenSet => VmValue.FromBuiltin(VmBuiltin.FrozenSet),
                VmObjectType.Range => VmValue.FromBuiltin(VmBuiltin.Range),
                VmObjectType.Slice => VmValue.FromBuiltin(VmBuiltin.Slice),
                VmObjectType.Instance => GetInstanceClass(value),
                VmObjectType.Class => _typeClass,
                _ => _objectClass,
            };
        }

        private bool MatchesClassInfo(VmValue value, VmValue classInfo, bool subclassCheck)
        {
            if (IsObjectType(classInfo, VmObjectType.Tuple))
            {
                var count = GetTupleCount(classInfo);
                for (var index = 0; index < count; index++)
                {
                    if (MatchesClassInfo(value, GetTupleItem(classInfo, index), subclassCheck))
                        return true;
                    PollCancellation();
                }
                return false;
            }

            if (classInfo.IsBuiltin)
            {
                var actual = subclassCheck ? value : GetRuntimeTypeToken(value);
                if (actual == classInfo)
                    return true;
                return classInfo.Builtin == VmBuiltin.Int && actual == VmValue.FromBuiltin(VmBuiltin.Bool);
            }

            if (!IsObjectType(classInfo, VmObjectType.Class))
            {
                Raise(
                    "TypeError",
                    subclassCheck
                        ? "issubclass() arg 2 must be a class, a tuple of classes, or a union"
                        : "isinstance() arg 2 must be a type, a tuple of types, or a union");
            }

            if (subclassCheck)
            {
                if (value.IsBuiltin)
                    return classInfo == _objectClass;
                if (!IsObjectType(value, VmObjectType.Class))
                    Raise("TypeError", "issubclass() arg 1 must be a class");
                return IsSubclassOf(value, classInfo);
            }

            if (IsObjectType(value, VmObjectType.Instance))
                return IsSubclassOf(GetInstanceClass(value), classInfo);
            if (IsObjectType(value, VmObjectType.Class))
                return IsSubclassOf(_typeClass, classInfo);
            return classInfo == _objectClass;
        }

        private VmValue CloneDictionary(VmValue source)
        {
            RequireObjectType(source, VmObjectType.Dictionary);
            var count = GetDictionaryCount(source);
            var capacity = count == 0 ? 4 : checked(count * 2);
            var clone = CreateDictionary(capacity);
            DictionaryUpdate(clone, source, rejectDuplicate: false);
            return clone;
        }

        private VmValue CreateClass(
            VmValue name,
            VmValue namespaceDictionary,
            VmValue bases,
            VmValue metaclass)
        {
            RequireObjectType(name, VmObjectType.String);
            RequireObjectType(namespaceDictionary, VmObjectType.Dictionary);
            RequireObjectType(bases, VmObjectType.Tuple);
            RequireObjectType(metaclass, VmObjectType.Class);
            if (GetTupleCount(bases) == 0)
            {
                bases = CreateTuple(1);
                SetTupleItem(bases, 0, _objectClass);
            }
            if (metaclass != _typeClass)
                Raise("TypeError", "custom metaclasses are not supported by the safe VM");

            var baseCount = GetTupleCount(bases);
            for (var index = 0; index < baseCount; index++)
            {
                var baseClass = GetTupleItem(bases, index);
                RequireObjectType(baseClass, VmObjectType.Class);
                if (baseClass == _typeClass)
                    Raise("TypeError", "subclassing type requires custom metaclass support, which is disabled in the safe VM");
                for (var prior = 0; prior < index; prior++)
                {
                    if (GetTupleItem(bases, prior) == baseClass)
                        Raise("TypeError", $"duplicate base class {GetClassName(baseClass)}");
                    PollCancellation();
                }
                PollCancellation();
            }

            var classNamespace = CloneDictionary(namespaceDictionary);
            var classObject = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Class,
                ClassPayloadSize));
            var mro = ComputeClassMro(classObject, bases);
            var moduleName = DictionaryTryGet(
                classNamespace,
                CreateString("__module__"),
                out var moduleValue) && IsObjectType(moduleValue, VmObjectType.String)
                    ? moduleValue
                    : CreateString("builtins");

            InitializeClassPayload(
                classObject,
                name,
                classNamespace,
                bases,
                mro,
                metaclass,
                moduleName);
            var payload = _memory.GetObjectPayloadAddress(classObject);
            if (DictionaryTryGet(classNamespace, CreateString("__qualname__"), out var qualname) &&
                IsObjectType(qualname, VmObjectType.String))
            {
                _memory.WriteValue(payload + 8, qualname);
            }
            if (!DictionaryTryGet(classNamespace, CreateString("__doc__"), out _))
                SetDictionaryString(classNamespace, "__doc__", VmValue.None);
            return classObject;
        }

        private VmValue ComputeClassMro(VmValue classObject, VmValue bases)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            RequireObjectType(bases, VmObjectType.Tuple);

            var baseCount = GetTupleCount(bases);
            var sequenceCount = checked(baseCount + 1);
            var sequences = CreateTuple(sequenceCount);
            for (var index = 0; index < baseCount; index++)
            {
                var baseClass = GetTupleItem(bases, index);
                SetTupleItem(sequences, index, GetClassMro(baseClass));
                PollCancellation();
            }
            SetTupleItem(sequences, baseCount, bases);

            // C3 merge cursors and output live in synthetic RAM. No guest-sized CLR
            // collection is allocated outside the VM memory budget.
            var cursors = VmValue.FromAddress(_memory.AllocateStorage(checked(sequenceCount * 4)));
            var cursorPayload = _memory.GetObjectPayloadAddress(cursors);
            var result = CreateList(checked(baseCount + 1));
            ListAdd(result, classObject);

            while (true)
            {
                var hasActiveSequence = false;
                var candidate = VmValue.Null;

                for (var sequenceIndex = 0; sequenceIndex < sequenceCount; sequenceIndex++)
                {
                    var sequence = GetTupleItem(sequences, sequenceIndex);
                    var cursor = _memory.ReadInt32(cursorPayload + sequenceIndex * 4);
                    var sequenceLength = GetTupleCount(sequence);
                    if (cursor >= sequenceLength)
                        continue;

                    hasActiveSequence = true;
                    var head = GetTupleItem(sequence, cursor);
                    var appearsInTail = false;
                    for (var otherIndex = 0; otherIndex < sequenceCount && !appearsInTail; otherIndex++)
                    {
                        var other = GetTupleItem(sequences, otherIndex);
                        var otherCursor = _memory.ReadInt32(cursorPayload + otherIndex * 4);
                        var otherLength = GetTupleCount(other);
                        for (var item = otherCursor + 1; item < otherLength; item++)
                        {
                            if (GetTupleItem(other, item) == head)
                            {
                                appearsInTail = true;
                                break;
                            }
                            PollCancellation();
                        }
                    }

                    if (!appearsInTail)
                    {
                        candidate = head;
                        break;
                    }
                    PollCancellation();
                }

                if (!hasActiveSequence)
                    break;
                if (candidate.IsNull)
                    Raise("TypeError", "Cannot create a consistent method resolution order (MRO) for bases");

                ListAdd(result, candidate);
                for (var sequenceIndex = 0; sequenceIndex < sequenceCount; sequenceIndex++)
                {
                    var sequence = GetTupleItem(sequences, sequenceIndex);
                    var cursorAddress = cursorPayload + sequenceIndex * 4;
                    var cursor = _memory.ReadInt32(cursorAddress);
                    if (cursor < GetTupleCount(sequence) && GetTupleItem(sequence, cursor) == candidate)
                        _memory.WriteInt32(cursorAddress, checked(cursor + 1));
                }
                PollCancellation();
            }

            var resultCount = GetListCount(result);
            var mro = CreateTuple(resultCount);
            for (var index = 0; index < resultCount; index++)
            {
                SetTupleItem(mro, index, GetListItem(result, index));
                PollCancellation();
            }
            return mro;
        }

        private VmValue GetClassNamespace(VmValue classObject)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(classObject) + 16);
        }

        private VmValue GetClassBases(VmValue classObject)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(classObject) + 24);
        }

        private VmValue GetClassMro(VmValue classObject)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(classObject) + 32);
        }

        private string GetClassName(VmValue classObject)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            return GetString(_memory.ReadValue(_memory.GetObjectPayloadAddress(classObject)));
        }

        private bool TryLookupClassAttribute(VmValue classObject, VmValue name, out VmValue value)
        {
            var mro = GetClassMro(classObject);
            for (var index = 0; index < GetTupleCount(mro); index++)
            {
                var candidate = GetTupleItem(mro, index);
                if (DictionaryTryGet(GetClassNamespace(candidate), name, out value))
                    return true;
                PollCancellation();
            }
            value = VmValue.Null;
            return false;
        }

        private VmValue CreateInstance(VmValue classObject)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            var instance = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Instance,
                InstancePayloadSize));
            var payload = _memory.GetObjectPayloadAddress(instance);
            _memory.WriteValue(payload, classObject);
            _memory.WriteValue(payload + 8, CreateDictionary(8));
            return instance;
        }

        private VmValue GetInstanceClass(VmValue instance)
        {
            RequireObjectType(instance, VmObjectType.Instance);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(instance));
        }

        private VmValue GetInstanceDictionary(VmValue instance)
        {
            RequireObjectType(instance, VmObjectType.Instance);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(instance) + 8);
        }

        private bool IsMethodDescriptor(VmValue value)
        {
            return IsObjectType(value, VmObjectType.Function) ||
                (value.IsBuiltin && value.Builtin == VmBuiltin.ObjectInit);
        }

        private VmValue CreatePythonBoundMethod(VmValue function, VmValue receiver)
        {
            if (function.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Cannot bind the VM NULL sentinel as a method.");
            var result = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.PythonBoundMethod,
                PythonBoundMethodPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(result);
            _memory.WriteValue(payload, function);
            _memory.WriteValue(payload + 8, receiver);
            return result;
        }

        private VmValue CreateSuper(VmValue startClass, VmValue receiver)
        {
            RequireObjectType(startClass, VmObjectType.Class);
            var receiverClass = GetSuperReceiverClass(receiver);
            var mro = GetClassMro(receiverClass);
            var found = false;
            for (var index = 0; index < GetTupleCount(mro); index++)
            {
                if (GetTupleItem(mro, index) == startClass)
                {
                    found = true;
                    break;
                }
                PollCancellation();
            }
            if (!found)
                Raise("TypeError", "super(type, obj): obj must be an instance or subtype of type");

            var result = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Super,
                SuperPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(result);
            _memory.WriteValue(payload, startClass);
            _memory.WriteValue(payload + 8, receiver);
            return result;
        }

        private VmValue CreateZeroArgumentSuper()
        {
            var frame = _currentFrame;
            var function = GetFrameFunction(frame);
            if (!IsObjectType(function, VmObjectType.Function))
                Raise("RuntimeError", "super(): no arguments");
            var code = GetFrameCode(frame);
            if (ReadCodeInt32(code, 0) == 0)
                Raise("RuntimeError", "super(): no arguments");
            var receiver = GetLocal(frame, 0);
            if ((GetLocalKind(frame, 0) & LocalKind.Cell) != 0 &&
                IsObjectType(receiver, VmObjectType.Cell))
            {
                receiver = GetCellValue(receiver);
            }
            if (receiver.IsNull)
                Raise("RuntimeError", "super(): arg[0] deleted");

            var localCount = GetFrameLocalCount(frame);
            for (var index = 0; index < localCount; index++)
            {
                PollCancellation();
                if (!string.Equals(GetLocalName(frame, index), "__class__", StringComparison.Ordinal))
                    continue;
                var kind = GetLocalKind(frame, index);
                if ((kind & (LocalKind.Cell | LocalKind.Free)) == 0)
                    continue;
                var cell = GetLocal(frame, index);
                if (!IsObjectType(cell, VmObjectType.Cell))
                {
                    PollCancellation();
                    continue;
                }
                var startClass = GetCellValue(cell);
                if (!IsObjectType(startClass, VmObjectType.Class))
                    Raise("RuntimeError", "super(): bad __class__ cell");
                return CreateSuper(startClass, receiver);
            }

            Raise("RuntimeError", "super(): __class__ cell not found");
            return VmValue.None;
        }

        private VmValue GetSuperReceiverClass(VmValue receiver)
        {
            if (IsObjectType(receiver, VmObjectType.Instance))
                return GetInstanceClass(receiver);
            if (IsObjectType(receiver, VmObjectType.Class))
                return receiver;
            Raise("TypeError", "super(type, obj): obj must be an instance or subtype of type");
            return VmValue.None;
        }

        private bool TryLookupSuperAttribute(VmValue superObject, VmValue name, out VmValue value)
        {
            RequireObjectType(superObject, VmObjectType.Super);
            var payload = _memory.GetObjectPayloadAddress(superObject);
            var startClass = _memory.ReadValue(payload);
            var receiver = _memory.ReadValue(payload + 8);
            var mro = GetClassMro(GetSuperReceiverClass(receiver));
            var afterStart = false;
            for (var index = 0; index < GetTupleCount(mro); index++)
            {
                var candidate = GetTupleItem(mro, index);
                if (!afterStart)
                {
                    if (candidate == startClass)
                        afterStart = true;
                    continue;
                }
                if (DictionaryTryGet(GetClassNamespace(candidate), name, out value))
                    return true;
                PollCancellation();
            }
            value = VmValue.Null;
            return false;
        }

        private VmValue CreateCell(VmValue value)
        {
            var cell = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Cell,
                CellPayloadSize));
            _memory.WriteValue(_memory.GetObjectPayloadAddress(cell), value);
            return cell;
        }

        private VmValue GetCellValue(VmValue cell)
        {
            RequireObjectType(cell, VmObjectType.Cell);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(cell));
        }

        private void SetCellValue(VmValue cell, VmValue value)
        {
            RequireObjectType(cell, VmObjectType.Cell);
            _memory.WriteValue(_memory.GetObjectPayloadAddress(cell), value);
        }

        private void ReturnGeneratorFromFrame()
        {
            var frame = _currentFrame;
            if (!GetFrameSuspensionOwner(frame).IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "RETURN_GENERATOR was executed by an already suspended frame.");

            var code = GetFrameCode(frame);
            var flags = (CodeFlags)ReadCodeInt32(code, 16);
            if ((flags & CodeFlags.Generator) == 0 || (flags & (CodeFlags.Coroutine | CodeFlags.AsyncGenerator)) != 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "RETURN_GENERATOR requires synchronous generator code.");
            if (GetFrameStackCount(frame) != 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "RETURN_GENERATOR requires an empty value stack.");

            var generator = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Generator,
                GeneratorPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(generator);
            var localBytes = checked(GetFrameLocalCount(frame) * 8);
            var stackBytes = checked(GetFrameStackCapacity(frame) * 8);
            var locals = VmValue.FromAddress(_memory.AllocateStorage(localBytes));
            var stack = VmValue.FromAddress(_memory.AllocateStorage(stackBytes));
            _memory.WriteValue(payload + 0, code);
            _memory.WriteValue(payload + 8, GetFrameGlobals(frame));
            _memory.WriteValue(payload + 16, GetFrameFunction(frame));
            _memory.WriteValue(payload + 24, locals);
            _memory.WriteValue(payload + 32, stack);
            _memory.WriteInt32(payload + 40, GetFrameInstructionPointer(frame));
            _memory.WriteInt32(payload + 44, GetFrameStackCount(frame));
            _memory.WriteInt32(payload + 48, (int)VmSuspensionState.Created);
            _memory.WriteInt32(payload + 52, 0);
            _memory.WriteValue(payload + 56, VmValue.None);
            _memory.WriteInt32(payload + 64, GetFrameLastInstruction(frame));
            _memory.WriteInt32(payload + 68, 0);
            _memory.WriteValue(payload + 72, GetFrameHandledException(frame));
            CopyFrameStateToSuspension(generator, frame);

            var previous = _memory.ReadInt32(frame);
            DetachCurrentFrame(frame, previous);
            if (previous == 0)
                _returnValue = generator;
            else
                Push(generator);
        }

        private void YieldFromFrame(VmValue value)
        {
            var frame = _currentFrame;
            var generator = GetFrameSuspensionOwner(frame);
            if (generator.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "YIELD_VALUE was executed outside a resumed generator frame.");
            RequireObjectType(generator, VmObjectType.Generator);
            var generatorPayload = _memory.GetObjectPayloadAddress(generator);
            if ((VmSuspensionState)_memory.ReadInt32(generatorPayload + 48) != VmSuspensionState.Running)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "YIELD_VALUE requires a running generator.");

            SaveSuspendedFrame(generator, frame, VmSuspensionState.Suspended, value);
            var previous = _memory.ReadInt32(frame);
            DetachCurrentFrame(frame, previous);
        }

        private void SaveSuspendedFrame(
            VmValue generator,
            int frame,
            VmSuspensionState state,
            VmValue result)
        {
            RequireObjectType(generator, VmObjectType.Generator);
            CopyFrameStateToSuspension(generator, frame);
            var payload = _memory.GetObjectPayloadAddress(generator);
            _memory.WriteInt32(payload + 40, GetFrameInstructionPointer(frame));
            _memory.WriteInt32(payload + 44, GetFrameStackCount(frame));
            _memory.WriteInt32(payload + 48, (int)state);
            _memory.WriteValue(payload + 56, result);
            _memory.WriteInt32(payload + 64, GetFrameLastInstruction(frame));
            _memory.WriteValue(payload + 72, GetFrameHandledException(frame));
        }

        private void CopyFrameStateToSuspension(VmValue generator, int frame)
        {
            var payload = _memory.GetObjectPayloadAddress(generator);
            var locals = _memory.ReadValue(payload + 24);
            var stack = _memory.ReadValue(payload + 32);
            RequireObjectType(locals, VmObjectType.Storage);
            RequireObjectType(stack, VmObjectType.Storage);

            var localBytes = checked(GetFrameLocalCount(frame) * 8);
            var stackCapacity = GetFrameStackCapacity(frame);
            var stackStorageBytes = checked(stackCapacity * 8);
            if (_memory.GetObjectAux0(locals) != localBytes || _memory.GetObjectAux0(stack) != stackStorageBytes)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator frame storage has an inconsistent size.");

            var stackCount = GetFrameStackCount(frame);
            if (stackCount < 0 || stackCount > stackCapacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator value-stack count is invalid.");
            var previousStackCount = _memory.ReadInt32(payload + 44);
            if (previousStackCount < 0 || previousStackCount > stackCapacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator snapshot has an invalid previous stack count.");

            PollCancellation();
            _memory.Copy(
                GetFrameLocalsAddress(frame),
                _memory.GetObjectPayloadAddress(locals),
                localBytes);

            var activeStackBytes = checked(stackCount * 8);
            _memory.Copy(
                GetFrameStackAddress(frame),
                _memory.GetObjectPayloadAddress(stack),
                activeStackBytes);
            if (previousStackCount > stackCount)
            {
                var staleBytes = checked((previousStackCount - stackCount) * 8);
                _memory.GetSpan(
                    _memory.GetObjectPayloadAddress(stack) + activeStackBytes,
                    staleBytes).Clear();
            }
            PollCancellation();
        }

        private void RestoreSuspendedFrame(VmValue generator, int frame)
        {
            var payload = _memory.GetObjectPayloadAddress(generator);
            var locals = _memory.ReadValue(payload + 24);
            var stack = _memory.ReadValue(payload + 32);
            RequireObjectType(locals, VmObjectType.Storage);
            RequireObjectType(stack, VmObjectType.Storage);

            var localBytes = checked(GetFrameLocalCount(frame) * 8);
            var stackCapacity = GetFrameStackCapacity(frame);
            var stackStorageBytes = checked(stackCapacity * 8);
            if (_memory.GetObjectAux0(locals) != localBytes || _memory.GetObjectAux0(stack) != stackStorageBytes)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator frame storage has an inconsistent size.");

            var instructionPointer = _memory.ReadInt32(payload + 40);
            var bytecodeLength = ReadCodeInt32(GetFrameCode(frame), 20);
            if (instructionPointer < 0 || instructionPointer >= bytecodeLength / 2)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator instruction pointer is outside its code object.");
            var stackCount = _memory.ReadInt32(payload + 44);
            if (stackCount < 0 || stackCount > stackCapacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator value-stack count is invalid.");

            PollCancellation();
            _memory.Copy(
                _memory.GetObjectPayloadAddress(locals),
                GetFrameLocalsAddress(frame),
                localBytes);
            _memory.Copy(
                _memory.GetObjectPayloadAddress(stack),
                GetFrameStackAddress(frame),
                checked(stackCount * 8));
            PollCancellation();
            SetFrameInstructionPointer(frame, instructionPointer);
            SetFrameStackCount(frame, stackCount);
            SetFrameLastInstruction(frame, _memory.ReadInt32(payload + 64));
            SetFrameHandledException(frame, _memory.ReadValue(payload + 72));
        }

        private bool ResumeGenerator(VmValue generator, out VmValue yielded)
        {
            return ResumeGenerator(generator, VmValue.None, out yielded, out _);
        }

        private bool ResumeGenerator(VmValue generator, VmValue sentValue, out VmValue yielded)
        {
            return ResumeGenerator(generator, sentValue, out yielded, out _);
        }

        private bool ResumeGenerator(
            VmValue generator,
            VmValue sentValue,
            out VmValue yielded,
            out VmValue completionValue)
        {
            RequireObjectType(generator, VmObjectType.Generator);
            if (sentValue.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "A generator cannot be sent the VM NULL sentinel.");
            var payload = _memory.GetObjectPayloadAddress(generator);
            var state = (VmSuspensionState)_memory.ReadInt32(payload + 48);
            switch (state)
            {
                case VmSuspensionState.Completed:
                    yielded = VmValue.Null;
                    completionValue = VmValue.None;
                    return false;
                case VmSuspensionState.Running:
                    Raise("ValueError", "generator already executing");
                    yielded = VmValue.Null;
                    completionValue = VmValue.None;
                    return false;
                case VmSuspensionState.Created:
                    if (!sentValue.IsNone)
                        Raise("TypeError", "can't send non-None value to a just-started generator");
                    break;
                case VmSuspensionState.Suspended:
                    break;
                default:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator has an invalid suspension state.");
            }

            var caller = _currentFrame;
            var code = _memory.ReadValue(payload + 0);
            var globals = _memory.ReadValue(payload + 8);
            var function = _memory.ReadValue(payload + 16);
            RequireObjectType(code, VmObjectType.Code);
            RequireObjectType(globals, VmObjectType.Dictionary);
            if (!function.IsNull)
                RequireObjectType(function, VmObjectType.Function);
            var flags = (CodeFlags)ReadCodeInt32(code, 16);
            if ((flags & CodeFlags.Generator) == 0 || (flags & (CodeFlags.Coroutine | CodeFlags.AsyncGenerator)) != 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator object references non-generator code.");
            var frame = PushFrame(caller, code, globals, function, generator);
            RestoreSuspendedFrame(generator, frame);
            _memory.WriteInt32(payload + 48, (int)VmSuspensionState.Running);
            _currentFrame = frame;

            // next() sends None into the suspended yield expression
            Push(sentValue);
            while (_currentFrame != caller)
                ExecuteOneInstruction();

            state = (VmSuspensionState)_memory.ReadInt32(payload + 48);
            if (state == VmSuspensionState.Suspended)
            {
                yielded = _memory.ReadValue(payload + 56);
                completionValue = VmValue.None;
                return true;
            }
            if (state == VmSuspensionState.Completed)
            {
                yielded = VmValue.Null;
                completionValue = _memory.ReadValue(payload + 56);
                _memory.WriteValue(payload + 56, VmValue.None);
                return false;
            }
            completionValue = VmValue.None;
            throw new VmTrapException(VmStopReason.InvalidBytecode, "Generator resume ended without yielding or completing.");
        }

        private void SendToDelegatedIterator(int operand, int nextIp, int bytecodeUnits)
        {
            var sentValue = Pop();
            var iterator = Peek(1);

            bool yielded;
            VmValue result;
            VmValue completionValue;
            if (IsObjectType(iterator, VmObjectType.Generator))
            {
                yielded = ResumeGenerator(
                    iterator,
                    sentValue,
                    out result,
                    out completionValue);
            }
            else if (IsObjectType(iterator, VmObjectType.Iterator))
            {
                if (!sentValue.IsNone)
                {
                    Raise(
                        "AttributeError",
                        $"'{GetTypeName(iterator)}' object has no attribute 'send'");
                }

                yielded = IteratorMoveNext(iterator, out result);
                completionValue = VmValue.None;
            }
            else
            {
                Raise("TypeError", $"'{GetTypeName(iterator)}' object is not an iterator");
                yielded = false;
                result = VmValue.Null;
                completionValue = VmValue.None;
            }

            if (yielded)
            {
                Push(result);
                return;
            }

            Push(completionValue);
            JumpTo(checked(nextIp + operand), bytecodeUnits);
        }

        private VmValue CreateBoundMethod(VmBoundMethod method, VmValue receiver)
        {
            var result = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.BoundMethod,
                BoundMethodPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(result);
            _memory.WriteValue(payload, receiver);
            _memory.WriteInt32(payload + 8, (int)method);
            return result;
        }

        private VmValue CreateModule(string name, bool isPackage, VmValue namespaceDictionary, string origin = "<built-in>")
        {
            RequireObjectType(namespaceDictionary, VmObjectType.Dictionary);
            var module = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Module,
                ModulePayloadSize));
            var moduleName = CreateString(name);
            var payload = _memory.GetObjectPayloadAddress(module);
            _memory.WriteValue(payload, namespaceDictionary);
            _memory.WriteValue(payload + 8, moduleName);
            _memory.WriteInt32(payload + 16, isPackage ? ModulePackageFlag : 0);
            _memory.WriteInt32(payload + 20, 0);

            SetDictionaryString(namespaceDictionary, "__name__", moduleName);
            SetDictionaryString(
                namespaceDictionary,
                "__package__",
                CreateString(GetModulePackageName(name, isPackage)));
            SetDictionaryString(namespaceDictionary, "__loader__", VmValue.None);
            SetDictionaryString(namespaceDictionary, "__spec__", VmValue.None);
            SetDictionaryString(namespaceDictionary, "__doc__", VmValue.None);
            SetDictionaryString(namespaceDictionary, "__cached__", VmValue.None);
            SetDictionaryString(namespaceDictionary, "__file__", CreateString(origin));
            SetDictionaryString(namespaceDictionary, "__dict__", namespaceDictionary);
            if (namespaceDictionary != _builtins)
            {
                SetDictionaryString(
                    namespaceDictionary,
                    "__builtins__",
                    _builtinsModule.IsNull ? _builtins : _builtinsModule);
            }
            if (isPackage)
            {
                var path = CreateList(1);
                ListAdd(path, CreateString($"<stdlib>/{name.Replace('.', '/')}"));
                SetDictionaryString(namespaceDictionary, "__path__", path);
            }

            return module;
        }

        private static string GetModulePackageName(string name, bool isPackage)
        {
            if (isPackage)
                return name;
            var separator = name.LastIndexOf('.');
            return separator < 0 ? string.Empty : name[..separator];
        }

        private VmValue GetModuleNamespace(VmValue module)
        {
            RequireObjectType(module, VmObjectType.Module);
            var result = _memory.ReadValue(_memory.GetObjectPayloadAddress(module));
            RequireObjectType(result, VmObjectType.Dictionary);
            return result;
        }

        private string GetModuleName(VmValue module)
        {
            RequireObjectType(module, VmObjectType.Module);
            return GetString(_memory.ReadValue(_memory.GetObjectPayloadAddress(module) + 8));
        }

        private bool IsPackageModule(VmValue module)
        {
            RequireObjectType(module, VmObjectType.Module);
            return (_memory.ReadInt32(_memory.GetObjectPayloadAddress(module) + 16) & ModulePackageFlag) != 0;
        }

        private void SetModuleInitializing(VmValue module, bool initializing)
        {
            RequireObjectType(module, VmObjectType.Module);
            var payload = _memory.GetObjectPayloadAddress(module);
            var flags = _memory.ReadInt32(payload + 16);
            flags = initializing
                ? flags | ModuleInitializingFlag
                : flags & ~ModuleInitializingFlag;
            _memory.WriteInt32(payload + 16, flags);
        }

        private void CacheModule(string name, VmValue module)
        {
            DictionarySet(
                _modules,
                CreateString(name),
                module,
                rejectDuplicate: false);
        }

        private bool TryGetCachedModule(string name, out VmValue module)
        {
            return DictionaryTryGet(_modules, CreateString(name), out module);
        }

        private void SetDictionaryString(VmValue dictionary, string name, VmValue value)
        {
            DictionarySet(
                dictionary,
                CreateString(name),
                value,
                rejectDuplicate: false);
        }

        private bool TryGetDictionaryString(VmValue dictionary, string name, out VmValue value)
        {
            return DictionaryTryGet(dictionary, CreateString(name), out value);
        }

        private VmValue LoadSingleModule(string fullName)
        {
            var moduleKey = CreateString(fullName);
            if (DictionaryTryGet(_modules, moduleKey, out var cached))
            {
                if (cached.IsNone)
                    Raise("ModuleNotFoundError", $"import of '{fullName}' halted; None in sys.modules");
                if (!IsObjectType(cached, VmObjectType.Module))
                    Raise("ImportError", $"sys.modules['{fullName}'] is not a module object in the safe VM");
                return cached;
            }

            if (!_moduleCatalog.TryGetModule(fullName, out var definition))
                Raise("ModuleNotFoundError", $"No module named '{fullName}'");

            if (definition.NativeKind == PythonNativeModuleKind.Builtins)
            {
                DictionarySet(_modules, moduleKey, _builtinsModule, rejectDuplicate: false);
                return _builtinsModule;
            }

            var namespaceDictionary = CreateDictionary(32);
            var origin = definition.IsNative
                ? "<built-in>"
                : definition.CodeObject?.FileName ?? $"<stdlib>/{fullName}.py";
            var module = CreateModule(fullName, definition.IsPackage, namespaceDictionary, origin);
            DictionarySet(_modules, moduleKey, module, rejectDuplicate: false);
            SetModuleInitializing(module, true);
            var hostRootBase = PushHostRoots(moduleKey, module, namespaceDictionary);
            try
            {
                if (definition.IsNative)
                {
                    InitializeNativeModule(definition.NativeKind, module);
                }
                else
                {
                    ExecuteModuleCode(definition.CodeObject!, module);
                }

                SetModuleInitializing(module, false);
                return module;
            }
            catch
            {
                // Reuse the rooted key so OOM failure is not replaced by another allocation while rolling back sys.modules
                _ = DictionaryDelete(_modules, moduleKey);
                throw;
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private void ExecuteModuleCode(PythonCodeObject source, VmValue module)
        {
            var caller = _currentFrame;
            var code = LoadCodeObject(source);
            var frame = PushFrame(
                caller,
                code,
                GetModuleNamespace(module),
                VmValue.Null,
                VmValue.Null);
            _currentFrame = frame;
            while (_currentFrame != caller)
            {
                try
                {
                    ExecuteOneInstruction();
                }
                catch (VmControlTransferSignal) when (IsFrameDescendantOrSelf(_currentFrame, frame))
                {
                    // The selected handler still belongs to the importing module
                    // synthetic frame subtree, so module execution continues locally
                }
            }

            // Module top level return values are not observable through import
            _ = Pop();
        }

        private bool IsFrameDescendantOrSelf(int frame, int ancestor)
        {
            for (var current = frame; current != 0; current = _memory.ReadInt32(current))
            {
                if (current == ancestor)
                    return true;
            }
            return false;
        }

        private void InitializeNativeModule(PythonNativeModuleKind kind, VmValue module)
        {
            var dictionary = GetModuleNamespace(module);
            switch (kind)
            {
                case PythonNativeModuleKind.Sys:
                    {
                        SetDictionaryString(dictionary, "modules", _modules);
                        SetDictionaryString(dictionary, "version", CreateString("3.14.6 (Cnidaria safe VM)"));
                        SetDictionaryString(dictionary, "hexversion", CreateInteger(new BigInteger(0x030E06F0)));
                        SetDictionaryString(dictionary, "platform", CreateString("cnidaria"));
                        SetDictionaryString(dictionary, "byteorder", CreateString("little"));
                        SetDictionaryString(dictionary, "maxsize", CreateInteger(new BigInteger(int.MaxValue)));
                        SetDictionaryString(dictionary, "path", CreateList(0));
                        SetDictionaryString(dictionary, "meta_path", CreateList(0));
                        SetDictionaryString(dictionary, "path_hooks", CreateList(0));
                        SetDictionaryString(dictionary, "path_importer_cache", CreateDictionary(8));
                        var builtinModuleNames = CreateTuple(3);
                        SetTupleItem(builtinModuleNames, 0, CreateString("builtins"));
                        SetTupleItem(builtinModuleNames, 1, CreateString("math"));
                        SetTupleItem(builtinModuleNames, 2, CreateString("sys"));
                        SetDictionaryString(dictionary, "builtin_module_names", builtinModuleNames);
                        var versionInfo = CreateTuple(5);
                        SetTupleItem(versionInfo, 0, CreateInteger(new BigInteger(3)));
                        SetTupleItem(versionInfo, 1, CreateInteger(new BigInteger(14)));
                        SetTupleItem(versionInfo, 2, CreateInteger(new BigInteger(6)));
                        SetTupleItem(versionInfo, 3, CreateString("final"));
                        SetTupleItem(versionInfo, 4, CreateInteger(BigInteger.Zero));
                        SetDictionaryString(dictionary, "version_info", versionInfo);
                        SetDictionaryString(
                            dictionary,
                            "getrecursionlimit",
                            VmValue.FromBuiltin(VmBuiltin.SysGetRecursionLimit));
                        return;
                    }

                case PythonNativeModuleKind.Math:
                    InstallMathModule(dictionary);
                    return;

                case PythonNativeModuleKind.Builtins:
                    return;

                default:
                    throw new VmTrapException(
                        VmStopReason.SecurityViolation,
                        $"Native module capability {kind} is not installed.");
            }
        }

        private void InstallMathModule(VmValue dictionary)
        {
            SetDictionaryString(dictionary, "pi", CreateFloat(Math.PI));
            SetDictionaryString(dictionary, "e", CreateFloat(Math.E));
            SetDictionaryString(dictionary, "tau", CreateFloat(Math.Tau));
            SetDictionaryString(dictionary, "inf", CreateFloat(double.PositiveInfinity));
            SetDictionaryString(dictionary, "nan", CreateFloat(double.NaN));
            SetDictionaryString(dictionary, "sqrt", VmValue.FromBuiltin(VmBuiltin.MathSqrt));
            SetDictionaryString(dictionary, "floor", VmValue.FromBuiltin(VmBuiltin.MathFloor));
            SetDictionaryString(dictionary, "ceil", VmValue.FromBuiltin(VmBuiltin.MathCeil));
            SetDictionaryString(dictionary, "trunc", VmValue.FromBuiltin(VmBuiltin.MathTrunc));
            SetDictionaryString(dictionary, "fabs", VmValue.FromBuiltin(VmBuiltin.MathFabs));
            SetDictionaryString(dictionary, "isfinite", VmValue.FromBuiltin(VmBuiltin.MathIsFinite));
            SetDictionaryString(dictionary, "isinf", VmValue.FromBuiltin(VmBuiltin.MathIsInf));
            SetDictionaryString(dictionary, "isnan", VmValue.FromBuiltin(VmBuiltin.MathIsNaN));
            SetDictionaryString(dictionary, "copysign", VmValue.FromBuiltin(VmBuiltin.MathCopySign));
            SetDictionaryString(dictionary, "fmod", VmValue.FromBuiltin(VmBuiltin.MathFmod));
            SetDictionaryString(dictionary, "pow", VmValue.FromBuiltin(VmBuiltin.MathPow));
            SetDictionaryString(dictionary, "sin", VmValue.FromBuiltin(VmBuiltin.MathSin));
            SetDictionaryString(dictionary, "cos", VmValue.FromBuiltin(VmBuiltin.MathCos));
            SetDictionaryString(dictionary, "tan", VmValue.FromBuiltin(VmBuiltin.MathTan));
            SetDictionaryString(dictionary, "asin", VmValue.FromBuiltin(VmBuiltin.MathAsin));
            SetDictionaryString(dictionary, "acos", VmValue.FromBuiltin(VmBuiltin.MathAcos));
            SetDictionaryString(dictionary, "atan", VmValue.FromBuiltin(VmBuiltin.MathAtan));
            SetDictionaryString(dictionary, "atan2", VmValue.FromBuiltin(VmBuiltin.MathAtan2));
            SetDictionaryString(dictionary, "exp", VmValue.FromBuiltin(VmBuiltin.MathExp));
            SetDictionaryString(dictionary, "log", VmValue.FromBuiltin(VmBuiltin.MathLog));
            SetDictionaryString(dictionary, "log2", VmValue.FromBuiltin(VmBuiltin.MathLog2));
            SetDictionaryString(dictionary, "log10", VmValue.FromBuiltin(VmBuiltin.MathLog10));
            SetDictionaryString(dictionary, "degrees", VmValue.FromBuiltin(VmBuiltin.MathDegrees));
            SetDictionaryString(dictionary, "radians", VmValue.FromBuiltin(VmBuiltin.MathRadians));
            SetDictionaryString(dictionary, "hypot", VmValue.FromBuiltin(VmBuiltin.MathHypot));
            SetDictionaryString(dictionary, "gcd", VmValue.FromBuiltin(VmBuiltin.MathGcd));
            SetDictionaryString(dictionary, "lcm", VmValue.FromBuiltin(VmBuiltin.MathLcm));
            SetDictionaryString(dictionary, "factorial", VmValue.FromBuiltin(VmBuiltin.MathFactorial));
            SetDictionaryString(dictionary, "comb", VmValue.FromBuiltin(VmBuiltin.MathComb));
            SetDictionaryString(dictionary, "perm", VmValue.FromBuiltin(VmBuiltin.MathPerm));
            SetDictionaryString(dictionary, "prod", VmValue.FromBuiltin(VmBuiltin.MathProd));
            SetDictionaryString(dictionary, "isclose", VmValue.FromBuiltin(VmBuiltin.MathIsClose));
            SetDictionaryString(dictionary, "sinh", VmValue.FromBuiltin(VmBuiltin.MathSinh));
            SetDictionaryString(dictionary, "cosh", VmValue.FromBuiltin(VmBuiltin.MathCosh));
            SetDictionaryString(dictionary, "tanh", VmValue.FromBuiltin(VmBuiltin.MathTanh));
            SetDictionaryString(dictionary, "asinh", VmValue.FromBuiltin(VmBuiltin.MathAsinh));
            SetDictionaryString(dictionary, "acosh", VmValue.FromBuiltin(VmBuiltin.MathAcosh));
            SetDictionaryString(dictionary, "atanh", VmValue.FromBuiltin(VmBuiltin.MathAtanh));
        }

        private VmValue CreateException(string typeName, string message)
        {
            var exception = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.Exception,
                ExceptionPayloadSize));
            var payload = _memory.GetObjectPayloadAddress(exception);
            _memory.WriteValue(payload, CreateString(typeName));
            _memory.WriteValue(payload + 8, CreateString(message));
            return exception;
        }

        private int PushFrame(
            int previousFrame,
            VmValue code,
            VmValue globals,
            VmValue function,
            VmValue suspensionOwner,
            VmValue localsMapping = default)
        {
            RequireObjectType(code, VmObjectType.Code);
            RequireObjectType(globals, VmObjectType.Dictionary);
            if (!function.IsNull)
                RequireObjectType(function, VmObjectType.Function);
            if (!suspensionOwner.IsNull)
                RequireObjectType(suspensionOwner, VmObjectType.Generator);
            if (localsMapping.IsNull &&
                (((CodeFlags)ReadCodeInt32(code, 16) & CodeFlags.Optimized) == 0))
            {
                localsMapping = globals;
            }
            if (!localsMapping.IsNull)
                RequireObjectType(localsMapping, VmObjectType.Dictionary);
            if (_callDepth >= _limits.MaxCallDepth)
                throw new VmTrapException(VmStopReason.CallDepthLimitExceeded, "Python call-depth limit exceeded.");
            var localCount = ReadCodeInt32(code, 24);
            var stackCapacity = ReadCodeInt32(code, 12);
            if (localCount < 0 || stackCapacity < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative frame layout in code object.");
            var frameSize = checked(FrameHeaderSize + checked((localCount + stackCapacity) * 8));
            var frame = _memory.PushFrameStorage(frameSize);
            _memory.WriteInt32(frame + 0, previousFrame);
            _memory.WriteValue(frame + 8, code);
            _memory.WriteValue(frame + 16, globals);
            _memory.WriteValue(frame + 24, function);
            _memory.WriteValue(frame + 32, suspensionOwner);
            _memory.WriteInt32(frame + 40, 0);
            _memory.WriteInt32(frame + 44, 0);
            _memory.WriteInt32(frame + 48, localCount);
            _memory.WriteInt32(frame + 52, frameSize);
            _memory.WriteInt32(frame + 56, -1);
            _memory.WriteValue(
                frame + 64,
                previousFrame == 0 ? VmValue.None : GetFrameHandledException(previousFrame));
            _memory.WriteValue(frame + 72, localsMapping);
            _callDepth++;
            return frame;
        }

        private void ReturnFromFrame(VmValue value)
        {
            var frame = _currentFrame;
            var previous = _memory.ReadInt32(frame);
            var suspensionOwner = GetFrameSuspensionOwner(frame);
            if (!suspensionOwner.IsNull)
            {
                SaveSuspendedFrame(suspensionOwner, frame, VmSuspensionState.Completed, value);
                DetachCurrentFrame(frame, previous);
                return;
            }

            var size = _memory.ReadInt32(frame + 52);
            _memory.PopFrameStorage(frame, size);
            _callDepth--;
            _currentFrame = previous;
            if (previous == 0)
            {
                _returnValue = value;
                return;
            }
            Push(value);
        }

        private void DetachCurrentFrame(int frame, int previous)
        {
            if (frame != _currentFrame)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Attempted to detach a non-current Python frame.");
            var size = _memory.ReadInt32(frame + 52);
            _memory.PopFrameStorage(frame, size);
            _callDepth--;
            _currentFrame = previous;
        }

        private VmValue GetFrameCode(int frame)
        {
            return _memory.ReadValue(frame + 8);
        }

        private VmValue GetFrameGlobals(int frame)
        {
            return _memory.ReadValue(frame + 16);
        }

        private VmValue GetFrameFunction(int frame)
        {
            return _memory.ReadValue(frame + 24);
        }

        private VmValue GetFrameSuspensionOwner(int frame)
        {
            return _memory.ReadValue(frame + 32);
        }

        private int GetFrameInstructionPointer(int frame)
        {
            return _memory.ReadInt32(frame + 40);
        }

        private void SetFrameInstructionPointer(int frame, int value)
        {
            _memory.WriteInt32(frame + 40, value);
        }

        private int GetFrameStackCount(int frame)
        {
            return _memory.ReadInt32(frame + 44);
        }

        private int GetFrameLastInstruction(int frame)
        {
            return _memory.ReadInt32(frame + 56);
        }

        private void SetFrameLastInstruction(int frame, int value)
        {
            _memory.WriteInt32(frame + 56, value);
        }

        private VmValue GetFrameHandledException(int frame)
        {
            return _memory.ReadValue(frame + 64);
        }

        private void SetFrameHandledException(int frame, VmValue value)
        {
            _memory.WriteValue(frame + 64, value);
        }

        private VmValue GetFrameLocalsMapping(int frame)
        {
            return _memory.ReadValue(frame + 72);
        }

        private void SetFrameStackCount(int frame, int value)
        {
            _memory.WriteInt32(frame + 44, value);
        }

        private int GetFrameLocalCount(int frame)
        {
            return _memory.ReadInt32(frame + 48);
        }

        private int GetFrameStackCapacity(int frame)
        {
            return ReadCodeInt32(GetFrameCode(frame), 12);
        }

        private int GetFrameLocalsAddress(int frame)
        {
            return frame + FrameHeaderSize;
        }

        private int GetFrameStackAddress(int frame)
        {
            return checked(GetFrameLocalsAddress(frame) + GetFrameLocalCount(frame) * 8);
        }

        private VmValue GetLocal(int frame, int index)
        {
            var count = GetFrameLocalCount(frame);
            if ((uint)index >= (uint)count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Fast-local index is out of range.");
            return _memory.ReadValue(GetFrameLocalsAddress(frame) + index * 8);
        }

        private void SetLocal(int frame, int index, VmValue value)
        {
            var count = GetFrameLocalCount(frame);
            if ((uint)index >= (uint)count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Fast-local index is out of range.");
            _memory.WriteValue(GetFrameLocalsAddress(frame) + index * 8, value);
        }

        private LocalKind GetLocalKind(int frame, int index)
        {
            var count = GetFrameLocalCount(frame);
            if ((uint)index >= (uint)count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Locals-plus kind index is out of range.");
            var storage = ReadCodeValue(GetFrameCode(frame), 64);
            RequireObjectType(storage, VmObjectType.Storage);
            return (LocalKind)_memory.ReadByte(_memory.GetObjectPayloadAddress(storage) + index);
        }

        private void CopyFreeVariables(int frame, int operand)
        {
            var code = GetFrameCode(frame);
            var freeVariableCount = ReadCodeInt32(code, 28);
            if (operand != freeVariableCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "COPY_FREE_VARS count does not match the code object.");
            if (freeVariableCount == 0)
                return;

            var function = GetFrameFunction(frame);
            if (function.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "A frame with free variables has no owning function.");
            RequireObjectType(function, VmObjectType.Function);

            var functionPayload = _memory.GetObjectPayloadAddress(function);
            var closure = _memory.ReadValue(functionPayload + 32);
            RequireObjectType(closure, VmObjectType.Tuple);
            if (GetTupleCount(closure) != freeVariableCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Function closure size does not match the code object.");

            var firstFree = checked(GetFrameLocalCount(frame) - freeVariableCount);
            if (firstFree < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Code object contains more free variables than locals-plus slots.");

            for (var index = 0; index < freeVariableCount; index++)
            {
                var localIndex = firstFree + index;
                if ((GetLocalKind(frame, localIndex) & LocalKind.Free) == 0)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "COPY_FREE_VARS target is not a free-variable slot.");
                var cell = GetTupleItem(closure, index);
                RequireObjectType(cell, VmObjectType.Cell);
                SetLocal(frame, localIndex, cell);
            }
        }

        private VmValue ReadCodeValue(VmValue code, int offset)
        {
            RequireObjectType(code, VmObjectType.Code);
            return _memory.ReadValue(_memory.GetObjectPayloadAddress(code) + offset);
        }

        private int ReadCodeInt32(VmValue code, int offset)
        {
            RequireObjectType(code, VmObjectType.Code);
            return _memory.ReadInt32(_memory.GetObjectPayloadAddress(code) + offset);
        }

        private readonly struct ExceptionHandlerEntry
        {
            public ExceptionHandlerEntry(int target, int stackDepth, bool preserveLastInstruction)
            {
                Target = target;
                StackDepth = stackDepth;
                PreserveLastInstruction = preserveLastInstruction;
            }

            public int Target { get; }
            public int StackDepth { get; }
            public bool PreserveLastInstruction { get; }
        }

        private void ValidateExceptionTable(
            ReadOnlySpan<byte> table,
            int bytecodeUnits,
            int stackSize)
        {
            var offset = 0;
            var previousStart = -1;
            while (offset < table.Length)
            {
                if (!TryReadExceptionTableItem(table, ref offset, requireEntryStart: true, out var start) ||
                    !TryReadExceptionTableItem(table, ref offset, requireEntryStart: false, out var size) ||
                    !TryReadExceptionTableItem(table, ref offset, requireEntryStart: false, out var target) ||
                    !TryReadExceptionTableItem(table, ref offset, requireEntryStart: false, out var depthAndLastInstruction))
                {
                    throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        "Malformed CPython exception table.");
                }

                var end = checked(start + size);
                var depth = depthAndLastInstruction >> 1;
                var preserveLastInstruction = (depthAndLastInstruction & 1) != 0;
                var handlerStackDepth = checked(depth + 1 + (preserveLastInstruction ? 1 : 0));
                if (size <= 0 || start < previousStart || start < 0 || end > bytecodeUnits ||
                    target < 0 || target >= bytecodeUnits || depth < 0 || handlerStackDepth > stackSize)
                {
                    throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        "CPython exception table contains an out-of-range entry.");
                }

                previousStart = start;
                PollCancellation();
            }
        }

        private bool TryFindExceptionHandler(
            VmValue code,
            int instruction,
            out ExceptionHandlerEntry entry)
        {
            var storage = ReadCodeValue(code, 80);
            RequireObjectType(storage, VmObjectType.Storage);
            var length = ReadCodeInt32(code, 88);
            if (length < 0 || length != _memory.GetObjectAux0(storage))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Corrupt exception-table storage.");

            var table = _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(storage), length);
            var offset = 0;
            while (offset < table.Length)
            {
                if (!TryReadExceptionTableItem(table, ref offset, requireEntryStart: true, out var start) ||
                    !TryReadExceptionTableItem(table, ref offset, requireEntryStart: false, out var size) ||
                    !TryReadExceptionTableItem(table, ref offset, requireEntryStart: false, out var target) ||
                    !TryReadExceptionTableItem(table, ref offset, requireEntryStart: false, out var depthAndLastInstruction))
                {
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Malformed exception table in synthetic RAM.");
                }

                if (instruction < start)
                    break;
                if (instruction < checked(start + size))
                {
                    entry = new ExceptionHandlerEntry(
                        target,
                        depthAndLastInstruction >> 1,
                        (depthAndLastInstruction & 1) != 0);
                    return true;
                }
                PollCancellation();
            }

            entry = default;
            return false;
        }

        private static bool TryReadExceptionTableItem(
            ReadOnlySpan<byte> table,
            ref int offset,
            bool requireEntryStart,
            out int value)
        {
            value = 0;
            if ((uint)offset >= (uint)table.Length)
                return false;

            var current = table[offset++];
            if (requireEntryStart)
            {
                if ((current & 0x80) == 0)
                    return false;
            }
            else if ((current & 0x80) != 0)
            {
                return false;
            }

            value = current & 0x3F;
            var groups = 1;
            while ((current & 0x40) != 0)
            {
                if ((uint)offset >= (uint)table.Length || groups++ >= 5)
                    return false;
                current = table[offset++];
                if ((current & 0x80) != 0)
                    return false;
                value = checked((value << 6) | (current & 0x3F));
            }
            return true;
        }

        private int HandleGuestException()
        {
            if (!IsObjectType(_raisedException, VmObjectType.Exception))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Guest exception signal has no synthetic exception object.");

            var exception = _raisedException;
            var originInstruction = _raisedLastInstruction;
            while (_currentFrame != 0)
            {
                var frame = _currentFrame;
                var lookupInstruction = GetFrameLastInstruction(frame);
                if (lookupInstruction >= 0 &&
                    TryFindExceptionHandler(GetFrameCode(frame), lookupInstruction, out var handler))
                {
                    TruncateFrameStack(frame, handler.StackDepth);
                    if (handler.PreserveLastInstruction)
                    {
                        var preservedInstruction = originInstruction >= 0
                            ? originInstruction
                            : lookupInstruction;
                        Push(CreateInteger(new BigInteger(preservedInstruction)));
                    }
                    Push(exception);
                    SetFrameInstructionPointer(frame, handler.Target);
                    _raisedException = VmValue.Null;
                    _raisedLastInstruction = -1;
                    return frame;
                }

                var previous = _memory.ReadInt32(frame);
                var suspensionOwner = GetFrameSuspensionOwner(frame);
                if (!suspensionOwner.IsNull)
                {
                    var generatorPayload = _memory.GetObjectPayloadAddress(suspensionOwner);
                    _memory.WriteInt32(generatorPayload + 48, (int)VmSuspensionState.Completed);
                    _memory.WriteValue(generatorPayload + 56, VmValue.None);
                }

                var size = _memory.ReadInt32(frame + 52);
                _memory.PopFrameStorage(frame, size);
                _callDepth--;
                _currentFrame = previous;
                originInstruction = previous == 0 ? -1 : GetFrameLastInstruction(previous);
            }

            _raisedException = VmValue.Null;
            _raisedLastInstruction = -1;
            throw new VmTrapException(
                VmStopReason.UnhandledException,
                FormatException(exception));
        }

        private void TruncateFrameStack(int frame, int depth)
        {
            var count = GetFrameStackCount(frame);
            if (depth < 0 || depth > count || depth > GetFrameStackCapacity(frame))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Exception handler has an invalid stack unwind depth.");
            var stack = GetFrameStackAddress(frame);
            for (var index = depth; index < count; index++)
                _memory.WriteValue(stack + index * 8, VmValue.Null);
            SetFrameStackCount(frame, depth);
        }

        private string FormatException(VmValue exception)
        {
            RequireObjectType(exception, VmObjectType.Exception);
            var payload = _memory.GetObjectPayloadAddress(exception);
            var typeName = GetString(_memory.ReadValue(payload));
            var message = GetString(_memory.ReadValue(payload + 8));
            return message.Length == 0 ? typeName : $"{typeName}: {message}";
        }

        private void Push(VmValue value)
        {
            var count = GetFrameStackCount(_currentFrame);
            var capacity = GetFrameStackCapacity(_currentFrame);
            if ((uint)count >= (uint)capacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Python value stack overflow.");
            _memory.WriteValue(GetFrameStackAddress(_currentFrame) + count * 8, value);
            SetFrameStackCount(_currentFrame, count + 1);
        }

        private VmValue Pop()
        {
            var count = GetFrameStackCount(_currentFrame);
            if (count <= 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Python value stack underflow.");
            var address = GetFrameStackAddress(_currentFrame) + (count - 1) * 8;
            var value = _memory.ReadValue(address);
            _memory.WriteValue(address, VmValue.Null);
            SetFrameStackCount(_currentFrame, count - 1);
            return value;
        }

        private VmValue Peek(int depth)
        {
            if (depth <= 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "COPY/SWAP depth must be positive.");
            var count = GetFrameStackCount(_currentFrame);
            var index = count - depth;
            if (index < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Python value stack access underflow.");
            return _memory.ReadValue(GetFrameStackAddress(_currentFrame) + index * 8);
        }

        private void Swap(int depth)
        {
            if (depth < 2)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "SWAP depth must be at least two.");
            var count = GetFrameStackCount(_currentFrame);
            var lowerIndex = count - depth;
            var topIndex = count - 1;
            if (lowerIndex < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "SWAP underflows the Python value stack.");
            var stack = GetFrameStackAddress(_currentFrame);
            var lower = _memory.ReadValue(stack + lowerIndex * 8);
            var top = _memory.ReadValue(stack + topIndex * 8);
            _memory.WriteValue(stack + lowerIndex * 8, top);
            _memory.WriteValue(stack + topIndex * 8, lower);
        }

        private void EnsureHostRootCapacity(int additional)
        {
            if (_hostRoots.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic host-root stack is not initialized.");
            if (additional < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative host-root reservation.");
            EnsureListCapacity(_hostRoots, checked(GetListCount(_hostRoots) + additional));
        }

        private int PushHostRoots(VmValue first, VmValue second, VmValue third)
        {
            if (_hostRoots.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic host-root stack is not initialized.");
            var baseCount = GetListCount(_hostRoots);
            ListAdd(_hostRoots, first);
            ListAdd(_hostRoots, second);
            ListAdd(_hostRoots, third);
            return baseCount;
        }

        private void PopHostRoots(int baseCount)
        {
            RequireObjectType(_hostRoots, VmObjectType.List);
            var payload = _memory.GetObjectPayloadAddress(_hostRoots);
            var count = _memory.ReadInt32(payload + 8);
            if (baseCount < 0 || baseCount > count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Synthetic host-root stack is corrupt.");
            var storage = _memory.ReadValue(payload);
            var storagePayload = _memory.GetObjectPayloadAddress(storage);
            for (var index = baseCount; index < count; index++)
                _memory.WriteValue(storagePayload + index * 8, VmValue.Null);
            _memory.WriteInt32(payload + 8, baseCount);
        }

        private void Call(int argumentCount, bool hasKeywords)
        {
            if (argumentCount < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative CALL argument count.");
            var caller = _currentFrame;
            var stackCount = GetFrameStackCount(caller);
            var required = checked(argumentCount + 2 + (hasKeywords ? 1 : 0));
            if (stackCount < required)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "CALL underflows the Python value stack.");

            var baseIndex = stackCount - required;
            var stack = GetFrameStackAddress(caller);
            var callable = _memory.ReadValue(stack + baseIndex * 8);
            var selfOrNull = _memory.ReadValue(stack + (baseIndex + 1) * 8);
            var keywordNames = hasKeywords
                ? _memory.ReadValue(stack + (baseIndex + 2 + argumentCount) * 8)
                : VmValue.None;
            var keywordCount = hasKeywords ? GetTupleCount(keywordNames) : 0;
            if (keywordCount > argumentCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "CALL_KW has more keyword names than arguments.");

            var implicitSelf = selfOrNull.IsNull ? 0 : 1;
            var allArguments = CreateTuple(checked(argumentCount + implicitSelf));
            var destination = 0;
            if (implicitSelf != 0)
                SetTupleItem(allArguments, destination++, selfOrNull);
            for (var index = 0; index < argumentCount; index++)
            {
                SetTupleItem(
                    allArguments,
                    destination++,
                    _memory.ReadValue(stack + (baseIndex + 2 + index) * 8));
            }

            for (var index = baseIndex; index < stackCount; index++)
                _memory.WriteValue(stack + index * 8, VmValue.Null);
            SetFrameStackCount(caller, baseIndex);

            var positionalCount = checked(argumentCount - keywordCount + implicitSelf);
            var hostRootBase = PushHostRoots(callable, allArguments, keywordNames);
            try
            {
                InvokeCallable(callable, allArguments, positionalCount, keywordNames);
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private void CallFunctionEx()
        {
            var caller = _currentFrame;
            var initialStackCount = GetFrameStackCount(caller);
            if (initialStackCount < 4)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "CALL_FUNCTION_EX underflows the Python value stack.");

            var baseIndex = initialStackCount - 4;
            var stack = GetFrameStackAddress(caller);
            var callable = _memory.ReadValue(stack + baseIndex * 8);
            var selfOrNull = _memory.ReadValue(stack + (baseIndex + 1) * 8);
            var positionalSource = _memory.ReadValue(stack + (baseIndex + 2) * 8);
            var keywordSource = _memory.ReadValue(stack + (baseIndex + 3) * 8);

            var positionalArguments = MaterializeTuple(positionalSource);

            var keywordCount = 0;
            if (!keywordSource.IsNull)
            {
                RequireObjectType(keywordSource, VmObjectType.Dictionary);
                keywordCount = GetDictionaryCount(keywordSource);
            }

            var keywordNames = keywordCount == 0
                ? VmValue.None
                : CreateTuple(keywordCount);

            var implicitSelf = selfOrNull.IsNull ? 0 : 1;
            var positionalCount = checked(
                GetTupleCount(positionalArguments) + implicitSelf);

            var allArguments = CreateTuple(checked(positionalCount + keywordCount));

            var destination = 0;

            if (implicitSelf != 0)
                SetTupleItem(allArguments, destination++, selfOrNull);

            for (var index = 0; index < GetTupleCount(positionalArguments); index++)
            {
                SetTupleItem(allArguments, destination++, GetTupleItem(positionalArguments, index));
            }

            if (keywordCount != 0)
            {
                var dictionaryPayload =
                    _memory.GetObjectPayloadAddress(keywordSource);

                var capacity =
                    _memory.ReadInt32(dictionaryPayload + 12);

                var entries =
                    GetDictionaryEntriesPayload(keywordSource);

                var keywordIndex = 0;

                for (var slot = 0; slot < capacity; slot++)
                {
                    var entry = entries + slot * DictionaryEntrySize;
                    var key = _memory.ReadValue(entry + 8);

                    if (key.IsNull || key.IsDeleted)
                        continue;

                    if (!IsObjectType(key, VmObjectType.String))
                        Raise("TypeError", "keywords must be strings");

                    SetTupleItem(keywordNames, keywordIndex++, key);
                    SetTupleItem(
                        allArguments,
                        destination++,
                        _memory.ReadValue(entry + 16));

                    PollCancellation();
                }

                if (keywordIndex != keywordCount)
                {
                    throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        "Dictionary keyword count changed during CALL_FUNCTION_EX.");
                }
            }

            var hostRootBase = PushHostRoots(callable, allArguments, keywordNames);

            try
            {
                for (var index = baseIndex; index < initialStackCount; index++)
                    _memory.WriteValue(stack + index * 8, VmValue.Null);

                SetFrameStackCount(caller, baseIndex);

                InvokeCallable(callable, allArguments, positionalCount, keywordNames);
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private void InvokeCallable(VmValue callable, VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            if (callable.IsBuiltin)
            {
                if (callable.Builtin == VmBuiltin.BuildClass)
                {
                    Push(BuildClass(arguments, positionalCount, keywordNames));
                    return;
                }
                Push(CallBuiltin(callable.Builtin, arguments, positionalCount, keywordNames));
                return;
            }

            if (!callable.IsAddress)
                Raise("TypeError", $"'{GetTypeName(callable)}' object is not callable");

            switch (_memory.GetObjectType(callable))
            {
                case VmObjectType.Function:
                    InvokePythonFunction(callable, arguments, positionalCount, keywordNames);
                    return;

                case VmObjectType.BoundMethod:
                    Push(CallBoundMethod(callable, arguments, positionalCount, keywordNames));
                    return;

                case VmObjectType.PythonBoundMethod:
                    InvokePythonBoundMethod(callable, arguments, positionalCount, keywordNames);
                    return;

                case VmObjectType.Class:
                    InvokeClass(callable, arguments, positionalCount, keywordNames);
                    return;

                case VmObjectType.StaticMethod:
                    InvokeCallable(
                        GetDescriptorCallable(callable),
                        arguments,
                        positionalCount,
                        keywordNames);
                    return;

                case VmObjectType.Instance:
                    {
                        var callName = CreateString("__call__");
                        if (!TryLookupClassAttribute(GetInstanceClass(callable), callName, out var callAttribute))
                        {
                            Raise("TypeError", $"'{GetTypeName(callable)}' object is not callable");
                            return;
                        }
                        var bound = BindClassAttribute(
                            callAttribute,
                            callable,
                            GetInstanceClass(callable));
                        InvokeCallable(bound, arguments, positionalCount, keywordNames);
                        return;
                    }

                default:
                    Raise("TypeError", $"'{GetTypeName(callable)}' object is not callable");
                    return;
            }
        }

        private void InvokePythonFunction(
            VmValue function,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames,
            VmValue localsMapping = default)
        {
            RequireObjectType(function, VmObjectType.Function);
            var payload = _memory.GetObjectPayloadAddress(function);
            var code = _memory.ReadValue(payload);
            var globals = _memory.ReadValue(payload + 8);
            ValidateFunctionClosure(function, code);
            var caller = _currentFrame;
            var frame = PushFrame(caller, code, globals, function, VmValue.Null, localsMapping);
            try
            {
                BindArguments(function, frame, arguments, positionalCount, keywordNames);
            }
            catch
            {
                var size = _memory.ReadInt32(frame + 52);
                _memory.PopFrameStorage(frame, size);
                _callDepth--;
                throw;
            }
            _currentFrame = frame;
        }

        private void InvokePythonBoundMethod(
            VmValue method,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames)
        {
            RequireObjectType(method, VmObjectType.PythonBoundMethod);
            var payload = _memory.GetObjectPayloadAddress(method);
            var function = _memory.ReadValue(payload);
            var receiver = _memory.ReadValue(payload + 8);
            var count = GetTupleCount(arguments);
            var expanded = CreateTuple(checked(count + 1));
            SetTupleItem(expanded, 0, receiver);
            for (var index = 0; index < count; index++)
            {
                SetTupleItem(expanded, index + 1, GetTupleItem(arguments, index));
                PollCancellation();
            }
            InvokeCallable(function, expanded, checked(positionalCount + 1), keywordNames);
        }

        private VmValue ExecutePythonFunctionSynchronously(
            VmValue function,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames,
            VmValue localsMapping = default)
        {
            var hostRootBase = PushHostRoots(function, arguments, keywordNames);
            try
            {
                var caller = _currentFrame;
                var callerStackCount = GetFrameStackCount(caller);
                InvokePythonFunction(function, arguments, positionalCount, keywordNames, localsMapping);
                var nestedFrame = _currentFrame;
                while (_currentFrame != caller)
                {
                    try
                    {
                        ExecuteOneInstruction();
                    }
                    catch (VmControlTransferSignal) when (IsFrameDescendantOrSelf(_currentFrame, nestedFrame))
                    {
                        // The selected handler still belongs to this nested Python call.
                    }
                }

                if (GetFrameStackCount(caller) != callerStackCount + 1)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Nested Python call returned an invalid stack shape.");
                return Pop();
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private VmValue ExecuteCallableSynchronously(
            VmValue callable,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames)
        {
            var hostRootBase = PushHostRoots(callable, arguments, keywordNames);
            try
            {
                var caller = _currentFrame;
                var callerStackCount = GetFrameStackCount(caller);
                InvokeCallable(callable, arguments, positionalCount, keywordNames);
                var nestedFrame = _currentFrame;
                while (_currentFrame != caller)
                {
                    try
                    {
                        ExecuteOneInstruction();
                    }
                    catch (VmControlTransferSignal) when (IsFrameDescendantOrSelf(_currentFrame, nestedFrame))
                    {
                        // The selected handler still belongs to this nested callable.
                    }
                }

                if (GetFrameStackCount(caller) != callerStackCount + 1)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Nested callable returned an invalid stack shape.");
                return Pop();
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private VmValue BuildClass(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            var totalCount = GetTupleCount(arguments);
            var keywordCount = keywordNames.IsNone ? 0 : GetTupleCount(keywordNames);
            if (positionalCount < 2 || positionalCount + keywordCount != totalCount)
                Raise("TypeError", "__build_class__: not enough arguments");

            var bodyFunction = GetTupleItem(arguments, 0);
            var name = GetTupleItem(arguments, 1);
            if (!IsObjectType(bodyFunction, VmObjectType.Function))
                Raise("TypeError", "__build_class__: func must be a function");
            if (!IsObjectType(name, VmObjectType.String))
                Raise("TypeError", "__build_class__: name is not a string");

            var metaclass = _typeClass;
            for (var index = 0; index < keywordCount; index++)
            {
                PollCancellation();
                var keyword = GetTupleItem(keywordNames, index);
                RequireObjectType(keyword, VmObjectType.String);
                var keywordText = GetString(keyword);
                var value = GetTupleItem(arguments, positionalCount + index);
                if (string.Equals(keywordText, "metaclass", StringComparison.Ordinal))
                {
                    if (!IsObjectType(value, VmObjectType.Class))
                        Raise("TypeError", "metaclass must be a class in the safe VM");
                    metaclass = value;
                    continue;
                }
                Raise("TypeError", $"__init_subclass__() takes no keyword arguments: '{keywordText}'");
            }
            if (metaclass != _typeClass)
                Raise("TypeError", "custom metaclasses are not supported by the safe VM");

            var baseCount = positionalCount - 2;
            var bases = CreateTuple(baseCount == 0 ? 1 : baseCount);
            if (baseCount == 0)
            {
                SetTupleItem(bases, 0, _objectClass);
            }
            else
            {
                for (var index = 0; index < baseCount; index++)
                {
                    var baseClass = GetTupleItem(arguments, index + 2);
                    if (!IsObjectType(baseClass, VmObjectType.Class))
                        Raise("TypeError", "bases must be classes; __mro_entries__ is not supported by the safe VM");
                    SetTupleItem(bases, index, baseClass);
                    PollCancellation();
                }
            }

            var namespaceDictionary = CreateDictionary(16);
            var emptyArguments = CreateTuple(0);
            var hostRootBase = PushHostRoots(namespaceDictionary, bases, emptyArguments);
            try
            {
                var bodyResult = ExecutePythonFunctionSynchronously(
                    bodyFunction,
                    emptyArguments,
                    0,
                    VmValue.None,
                    namespaceDictionary);
                var classObject = CreateClass(name, namespaceDictionary, bases, metaclass);

                var classCellKey = CreateString("__classcell__");
                if (DictionaryTryGet(namespaceDictionary, classCellKey, out var classCell))
                {
                    if (!IsObjectType(classCell, VmObjectType.Cell))
                        Raise("TypeError", "__classcell__ must be a nonlocal cell");
                    if (bodyResult != classCell)
                        Raise("TypeError", "__class__ set to a different cell than __classcell__");
                    SetCellValue(classCell, classObject);
                    _ = DictionaryDelete(namespaceDictionary, classCellKey);
                    _ = DictionaryDelete(GetClassNamespace(classObject), classCellKey);
                }
                else if (!bodyResult.IsNone)
                {
                    Raise("TypeError", "class body returned a non-cell value");
                }

                var classDictionaryCellKey = CreateString("__classdictcell__");
                if (DictionaryTryGet(namespaceDictionary, classDictionaryCellKey, out var classDictionaryCell))
                {
                    if (!IsObjectType(classDictionaryCell, VmObjectType.Cell))
                        Raise("TypeError", "__classdictcell__ must be a nonlocal cell");
                    SetCellValue(classDictionaryCell, GetClassNamespace(classObject));
                    _ = DictionaryDelete(namespaceDictionary, classDictionaryCellKey);
                    _ = DictionaryDelete(GetClassNamespace(classObject), classDictionaryCellKey);
                }

                return classObject;
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private void InvokeClass(
            VmValue classObject,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames)
        {
            RequireObjectType(classObject, VmObjectType.Class);
            var totalCount = GetTupleCount(arguments);
            var keywordCount = keywordNames.IsNone ? 0 : GetTupleCount(keywordNames);
            if (positionalCount + keywordCount != totalCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Class CALL argument layout is inconsistent.");

            if (classObject == _typeClass)
            {
                if (keywordCount != 0)
                    Raise("TypeError", "type() takes no keyword arguments");
                if (positionalCount == 1)
                {
                    Push(GetRuntimeTypeToken(GetTupleItem(arguments, 0)));
                    return;
                }
                if (positionalCount == 3)
                {
                    var name = GetTupleItem(arguments, 0);
                    var bases = GetTupleItem(arguments, 1);
                    var namespaceDictionary = GetTupleItem(arguments, 2);
                    RequireObjectType(name, VmObjectType.String);
                    RequireObjectType(bases, VmObjectType.Tuple);
                    RequireObjectType(namespaceDictionary, VmObjectType.Dictionary);
                    Push(CreateClass(name, namespaceDictionary, bases, _typeClass));
                    return;
                }
                Raise("TypeError", "type() takes 1 or 3 arguments");
            }

            var newName = CreateString("__new__");
            if (TryLookupClassAttribute(classObject, newName, out _))
                Raise("NotImplementedError", "custom __new__ is not supported by the safe VM");

            var instance = CreateInstance(classObject);
            var initName = CreateString("__init__");
            if (TryLookupClassAttribute(classObject, initName, out var initializer))
            {
                if (!IsObjectType(initializer, VmObjectType.Function) &&
                    !(initializer.IsBuiltin && initializer.Builtin == VmBuiltin.ObjectInit))
                {
                    Raise("TypeError", $"'{GetTypeName(initializer)}' object is not callable");
                }
                var expanded = CreateTuple(checked(totalCount + 1));
                SetTupleItem(expanded, 0, instance);
                for (var index = 0; index < totalCount; index++)
                {
                    SetTupleItem(expanded, index + 1, GetTupleItem(arguments, index));
                    PollCancellation();
                }
                var hostRootBase = PushHostRoots(instance, initializer, expanded);
                try
                {
                    var result = initializer.IsBuiltin
                        ? CallBuiltin(
                            initializer.Builtin,
                            expanded,
                            checked(positionalCount + 1),
                            keywordNames)
                        : ExecutePythonFunctionSynchronously(
                            initializer,
                            expanded,
                            checked(positionalCount + 1),
                            keywordNames);
                    if (!result.IsNone)
                        Raise("TypeError", $"__init__() should return None, not '{GetTypeName(result)}'");
                }
                finally
                {
                    PopHostRoots(hostRootBase);
                }
            }
            else if (totalCount != 0)
            {
                Raise("TypeError", $"{GetClassName(classObject)}() takes no arguments");
            }

            Push(instance);
        }

        private void BindArguments(VmValue function, int frame, VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            var code = GetFrameCode(frame);
            var argumentCount = ReadCodeInt32(code, 0);
            var positionalOnlyCount = ReadCodeInt32(code, 4);
            var keywordOnlyCount = ReadCodeInt32(code, 8);
            var flags = (CodeFlags)ReadCodeInt32(code, 16);
            var totalArguments = GetTupleCount(arguments);
            var keywordCount = keywordNames.IsNone ? 0 : GetTupleCount(keywordNames);
            if (positionalCount < 0 || keywordCount != totalArguments - positionalCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Malformed CALL_KW argument layout.");

            var varArgsIndex = argumentCount + keywordOnlyCount;
            var varKeywordsIndex = varArgsIndex + ((flags & CodeFlags.VarArgs) != 0 ? 1 : 0);
            var positionalToBind = Math.Min(positionalCount, argumentCount);
            for (var index = 0; index < positionalToBind; index++)
                SetLocal(frame, index, GetTupleItem(arguments, index));

            if (positionalCount > argumentCount)
            {
                if ((flags & CodeFlags.VarArgs) == 0)
                    Raise("TypeError", $"{GetCodeName(code)}() takes {argumentCount} positional arguments but {positionalCount} were given");
                var extras = CreateTuple(positionalCount - argumentCount);
                for (var index = argumentCount; index < positionalCount; index++)
                    SetTupleItem(extras, index - argumentCount, GetTupleItem(arguments, index));
                SetLocal(frame, varArgsIndex, extras);
            }
            else if ((flags & CodeFlags.VarArgs) != 0)
            {
                SetLocal(frame, varArgsIndex, CreateTuple(0));
            }

            VmValue extraKeywords = VmValue.Null;
            if ((flags & CodeFlags.VarKeywords) != 0)
            {
                extraKeywords = CreateDictionary(8);
                SetLocal(frame, varKeywordsIndex, extraKeywords);
            }

            for (var keywordIndex = 0; keywordIndex < keywordCount; keywordIndex++)
            {
                var keywordName = GetTupleItem(keywordNames, keywordIndex);
                RequireObjectType(keywordName, VmObjectType.String);
                var value = GetTupleItem(arguments, positionalCount + keywordIndex);
                var localIndex = FindKeywordLocal(code, keywordName, argumentCount, keywordOnlyCount);
                if (localIndex >= 0)
                {
                    if (localIndex < positionalOnlyCount)
                        Raise("TypeError", $"{GetCodeName(code)}() got a positional-only argument passed as keyword: '{GetString(keywordName)}'");
                    if (!GetLocal(frame, localIndex).IsNull)
                        Raise("TypeError", $"{GetCodeName(code)}() got multiple values for argument '{GetString(keywordName)}'");
                    SetLocal(frame, localIndex, value);
                }
                else if ((flags & CodeFlags.VarKeywords) != 0)
                {
                    DictionarySet(extraKeywords, keywordName, value, rejectDuplicate: true);
                }
                else
                {
                    Raise("TypeError", $"{GetCodeName(code)}() got an unexpected keyword argument '{GetString(keywordName)}'");
                }
            }

            var functionPayload = _memory.GetObjectPayloadAddress(function);
            var defaults = _memory.ReadValue(functionPayload + 16);
            var defaultCount = defaults.IsNone ? 0 : GetTupleCount(defaults);
            var firstDefault = argumentCount - defaultCount;
            for (var index = 0; index < argumentCount; index++)
            {
                if (!GetLocal(frame, index).IsNull)
                    continue;
                if (index >= firstDefault)
                {
                    SetLocal(frame, index, GetTupleItem(defaults, index - firstDefault));
                    continue;
                }
                Raise("TypeError", $"{GetCodeName(code)}() missing required positional argument: '{GetLocalName(frame, index)}'");
            }

            var keywordDefaults = _memory.ReadValue(functionPayload + 24);
            for (var index = 0; index < keywordOnlyCount; index++)
            {
                var localIndex = argumentCount + index;
                if (!GetLocal(frame, localIndex).IsNull)
                    continue;
                var name = GetTupleItem(ReadCodeValue(code, 56), localIndex);
                if (!keywordDefaults.IsNone && DictionaryTryGet(keywordDefaults, name, out var defaultValue))
                {
                    SetLocal(frame, localIndex, defaultValue);
                    continue;
                }
                Raise("TypeError", $"{GetCodeName(code)}() missing required keyword-only argument: '{GetString(name)}'");
            }
        }

        private int FindKeywordLocal(VmValue code, VmValue keywordName, int argumentCount, int keywordOnlyCount)
        {
            var names = ReadCodeValue(code, 56);
            var kindsStorage = ReadCodeValue(code, 64);
            var kinds = _memory.GetReadOnlySpan(
                _memory.GetObjectPayloadAddress(kindsStorage),
                ReadCodeInt32(code, 24));
            var candidateCount = argumentCount + keywordOnlyCount;
            for (var index = 0; index < candidateCount; index++)
            {
                if (((LocalKind)kinds[index] & LocalKind.KeywordArgument) == 0)
                    continue;
                if (ValuesEqual(GetTupleItem(names, index), keywordName))
                    return index;
            }
            return -1;
        }

        private void SetFunctionAttribute(VmValue function, int attribute, VmValue value)
        {
            RequireObjectType(function, VmObjectType.Function);
            var payload = _memory.GetObjectPayloadAddress(function);
            switch (attribute)
            {
                case 0x01:
                    RequireObjectType(value, VmObjectType.Tuple);
                    _memory.WriteValue(payload + 16, value);
                    return;
                case 0x02:
                    RequireObjectType(value, VmObjectType.Dictionary);
                    _memory.WriteValue(payload + 24, value);
                    return;
                case 0x08:
                    if (!IsObjectType(value, VmObjectType.Tuple))
                        throw new VmTrapException(VmStopReason.InvalidBytecode, "Function closure attribute is not a tuple.");
                    _memory.WriteValue(payload + 32, value);
                    ValidateFunctionClosure(function, _memory.ReadValue(payload));
                    return;
                default:
                    throw new VmTrapException(
                        VmStopReason.UnsupportedOpcode,
                        $"Function attribute flag 0x{attribute:x} is not implemented.");
            }
        }

        private void ValidateFunctionClosure(VmValue function, VmValue code)
        {
            RequireObjectType(function, VmObjectType.Function);
            RequireObjectType(code, VmObjectType.Code);
            var expectedCount = ReadCodeInt32(code, 28);
            if (expectedCount < 0 || expectedCount > ReadCodeInt32(code, 24))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Code object has an invalid free-variable count.");

            var closure = _memory.ReadValue(_memory.GetObjectPayloadAddress(function) + 32);
            if (expectedCount == 0 && closure.IsNone)
                return;
            if (!IsObjectType(closure, VmObjectType.Tuple))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Function closure is missing or is not a tuple.");
            if (GetTupleCount(closure) != expectedCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Function closure size does not match its code object.");
            for (var index = 0; index < expectedCount; index++)
            {
                if (!IsObjectType(GetTupleItem(closure, index), VmObjectType.Cell))
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Function closure contains a non-cell value.");
            }
        }

        private int GetKeywordCount(VmValue keywordNames)
        {
            return keywordNames.IsNone ? 0 : GetTupleCount(keywordNames);
        }

        private void RequireNoKeywordArguments(VmBuiltin builtin, VmValue keywordNames)
        {
            if (GetKeywordCount(keywordNames) != 0)
                Raise("TypeError", $"{GetBuiltinName(builtin)}() takes no keyword arguments");
        }

        private bool TryGetKeywordArgument(
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames,
            string requestedName,
            out VmValue value)
        {
            var keywordCount = GetKeywordCount(keywordNames);
            for (var index = 0; index < keywordCount; index++)
            {
                var nameValue = GetTupleItem(keywordNames, index);
                RequireObjectType(nameValue, VmObjectType.String);
                if (string.Equals(GetString(nameValue), requestedName, StringComparison.Ordinal))
                {
                    value = GetTupleItem(arguments, positionalCount + index);
                    return true;
                }
            }
            value = VmValue.Null;
            return false;
        }

        private void RequireOnlyKeywords(VmValue keywordNames, params string[] allowedNames)
        {
            var keywordCount = GetKeywordCount(keywordNames);
            for (var index = 0; index < keywordCount; index++)
            {
                var nameValue = GetTupleItem(keywordNames, index);
                RequireObjectType(nameValue, VmObjectType.String);
                var name = GetString(nameValue);
                var allowed = false;
                for (var allowedIndex = 0; allowedIndex < allowedNames.Length; allowedIndex++)
                {
                    if (string.Equals(name, allowedNames[allowedIndex], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed)
                    Raise("TypeError", $"got an unexpected keyword argument '{name}'");
            }
        }

        private VmValue CreateSingleArgumentTuple(VmValue value)
        {
            var arguments = CreateTuple(1);
            SetTupleItem(arguments, 0, value);
            return arguments;
        }

        private VmValue CallUnaryKey(VmValue key, VmValue value)
        {
            if (key.IsNone)
                return value;
            return ExecuteCallableSynchronously(
                key,
                CreateSingleArgumentTuple(value),
                1,
                VmValue.None);
        }

        private VmValue CallAbsBuiltin(VmValue value)
        {
            if (IsInteger(value))
                return CreateInteger(BigInteger.Abs(GetInteger(value)));
            if (IsFloat(value))
                return CreateFloat(Math.Abs(GetFloat(value)));
            if (IsObjectType(value, VmObjectType.Complex))
            {
                var payload = _memory.GetObjectPayloadAddress(value);
                var real = Math.Abs(_memory.ReadDouble(payload));
                var imaginary = Math.Abs(_memory.ReadDouble(payload + 8));
                var maximum = Math.Max(real, imaginary);
                if (maximum == 0.0)
                    return CreateFloat(0.0);
                var scaledReal = real / maximum;
                var scaledImaginary = imaginary / maximum;
                return CreateFloat(maximum * Math.Sqrt(scaledReal * scaledReal + scaledImaginary * scaledImaginary));
            }
            if (IsObjectType(value, VmObjectType.Instance) && HasSpecialMethod(value, "__abs__"))
                return CallZeroArgumentSpecialMethod(value, "__abs__");
            Raise("TypeError", $"bad operand type for abs(): '{GetTypeName(value)}'");
            return VmValue.None;
        }

        private string EscapeNonAscii(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (var offset = 0; offset < text.Length;)
            {
                var codePoint = ReadCodePoint(text, offset, out var consumed);
                offset += consumed;
                if (codePoint <= 0x7f)
                {
                    builder.Append((char)codePoint);
                }
                else if (codePoint <= 0xff)
                {
                    builder.Append("\\x").Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
                }
                else if (codePoint <= 0xffff)
                {
                    builder.Append("\\u").Append(codePoint.ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append("\\U").Append(codePoint.ToString("x8", CultureInfo.InvariantCulture));
                }
                EnsureBoundedTextLength(builder, 0);
            }
            return builder.ToString();
        }

        private string FormatIntegerBase(BigInteger value, int radix, string prefix)
        {
            EnsureIntegerSize(value);
            if (value.IsZero)
                return prefix + "0";
            var negative = value.Sign < 0;
            var remaining = BigInteger.Abs(value);
            var builder = new StringBuilder(Math.Max(8, GetBitLength(value) + 1));
            const string digits = "0123456789abcdef";
            while (!remaining.IsZero)
            {
                remaining = BigInteger.DivRem(remaining, radix, out var remainder);
                builder.Append(digits[(int)remainder]);
                ConsumeInstructionBudget();
            }
            for (var left = 0; left < builder.Length / 2; left++)
            {
                var right = builder.Length - left - 1;
                var temporary = builder[left];
                builder[left] = builder[right];
                builder[right] = temporary;
            }
            return (negative ? "-" : string.Empty) + prefix + builder.ToString();
        }

        private VmValue CallBytesBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireNoKeywordArguments(VmBuiltin.Bytes, keywordNames);
            RequireArgumentCount(VmBuiltin.Bytes, positionalCount, 0, 3);
            if (positionalCount == 0)
                return CreateBytes(ReadOnlySpan<byte>.Empty);

            var source = GetTupleItem(arguments, 0);
            if (IsObjectType(source, VmObjectType.String))
            {
                if (positionalCount < 2)
                    Raise("TypeError", "string argument without an encoding");
                var encodingName = GetStringArgument(GetTupleItem(arguments, 1), "bytes");
                if (!string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(encodingName, "utf8", StringComparison.OrdinalIgnoreCase))
                {
                    Raise("LookupError", "only utf-8 encoding is available in the safe VM");
                }
                if (positionalCount == 3)
                {
                    var errors = GetStringArgument(GetTupleItem(arguments, 2), "bytes");
                    if (!string.Equals(errors, "strict", StringComparison.Ordinal))
                        Raise("LookupError", "only strict encoding errors are available in the safe VM");
                }
                var text = GetString(source);
                var byteCount = Encoding.UTF8.GetByteCount(text);
                var result = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Bytes, byteCount, byteCount, 0));
                var written = Encoding.UTF8.GetBytes(
                    text.AsSpan(),
                    _memory.GetSpan(_memory.GetObjectPayloadAddress(result), byteCount));
                if (written != byteCount)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Failed to encode bytes.");
                return result;
            }

            if (positionalCount != 1)
                Raise("TypeError", "encoding without a string argument");
            if (IsObjectType(source, VmObjectType.Bytes))
                return source;
            if (IsInteger(source) || IsObjectType(source, VmObjectType.Instance) && HasSpecialMethod(source, "__index__"))
            {
                var length = GetIndexInteger(source);
                if (length < 0)
                    Raise("ValueError", "negative count");
                if (length > int.MaxValue)
                    throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "bytes object is too large.");
                return VmValue.FromAddress(_memory.AllocateObject(
                    VmObjectType.Bytes,
                    (int)length,
                    (int)length,
                    0));
            }

            var items = MaterializeList(source);
            var count = GetListCount(items);
            var bytes = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Bytes, count, count, 0));
            var payload = _memory.GetObjectPayloadAddress(bytes);
            for (var index = 0; index < count; index++)
            {
                var item = GetListItem(items, index);
                var integer = GetIndexInteger(item);
                if (integer < 0 || integer > 255)
                    Raise("ValueError", "bytes must be in range(0, 256)");
                _memory.WriteByte(payload + index, (byte)integer);
                ConsumeInstructionBudget();
            }
            return bytes;
        }

        private bool TryParseComplex(string text, out double real, out double imaginary)
        {
            text = text.Trim();
            if (text.Length >= 2 && text[0] == '(' && text[^1] == ')')
                text = text[1..^1].Trim();
            real = 0.0;
            imaginary = 0.0;
            if (!text.EndsWith('j') && !text.EndsWith('J'))
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out real);

            var body = text[..^1];
            var split = -1;
            for (var index = 1; index < body.Length; index++)
            {
                if ((body[index] == '+' || body[index] == '-') && body[index - 1] != 'e' && body[index - 1] != 'E')
                    split = index;
            }
            if (split < 0)
            {
                var imaginaryText = body is "" or "+" ? "1" : body == "-" ? "-1" : body;
                return double.TryParse(imaginaryText, NumberStyles.Float, CultureInfo.InvariantCulture, out imaginary);
            }
            var realText = body[..split];
            var imaginaryPart = body[split..];
            if (imaginaryPart is "+" or "-")
                imaginaryPart += "1";
            return double.TryParse(realText, NumberStyles.Float, CultureInfo.InvariantCulture, out real) &&
                double.TryParse(imaginaryPart, NumberStyles.Float, CultureInfo.InvariantCulture, out imaginary);
        }

        private VmValue CallComplexBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "real", "imag");
            if (positionalCount > 2)
                Raise("TypeError", $"complex() expected at most 2 arguments, got {positionalCount}");
            var realValue = positionalCount >= 1 ? GetTupleItem(arguments, 0) : VmValue.FromSmallInteger(0);
            var imaginaryValue = positionalCount >= 2 ? GetTupleItem(arguments, 1) : VmValue.FromSmallInteger(0);
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "real", out var keywordReal))
            {
                if (positionalCount >= 1) Raise("TypeError", "complex() got multiple values for argument 'real'");
                realValue = keywordReal;
            }
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "imag", out var keywordImaginary))
            {
                if (positionalCount >= 2) Raise("TypeError", "complex() got multiple values for argument 'imag'");
                imaginaryValue = keywordImaginary;
            }

            if (IsObjectType(realValue, VmObjectType.String))
            {
                if (positionalCount >= 2 || !imaginaryValue.Equals(VmValue.FromSmallInteger(0)))
                    Raise("TypeError", "complex() can't take second arg if first is a string");
                if (!TryParseComplex(GetString(realValue), out var parsedReal, out var parsedImaginary))
                    Raise("ValueError", "complex() arg is a malformed string");
                return CreateComplex(parsedReal, parsedImaginary);
            }

            double real;
            double imaginary;
            if (IsObjectType(realValue, VmObjectType.Complex))
            {
                var payload = _memory.GetObjectPayloadAddress(realValue);
                real = _memory.ReadDouble(payload);
                imaginary = _memory.ReadDouble(payload + 8);
            }
            else if (IsInteger(realValue) || IsFloat(realValue))
            {
                real = GetFloat(realValue);
                imaginary = 0.0;
            }
            else
            {
                Raise("TypeError", $"complex() first argument must be a string or a number, not '{GetTypeName(realValue)}'");
                return VmValue.None;
            }

            if (IsObjectType(imaginaryValue, VmObjectType.Complex))
            {
                var payload = _memory.GetObjectPayloadAddress(imaginaryValue);
                real -= _memory.ReadDouble(payload + 8);
                imaginary += _memory.ReadDouble(payload);
            }
            else if (IsInteger(imaginaryValue) || IsFloat(imaginaryValue))
            {
                imaginary += GetFloat(imaginaryValue);
            }
            else
            {
                Raise("TypeError", $"complex() second argument must be a number, not '{GetTypeName(imaginaryValue)}'");
            }
            return CreateComplex(real, imaginary);
        }

        private bool IsRaisedException(string typeName)
        {
            if (!IsObjectType(_raisedException, VmObjectType.Exception))
                return false;
            return string.Equals(
                GetString(_memory.ReadValue(_memory.GetObjectPayloadAddress(_raisedException))),
                typeName,
                StringComparison.Ordinal);
        }

        private bool HasAttributeValue(VmValue owner, VmValue nameValue)
        {
            try
            {
                _ = GetAttributeValue(owner, nameValue);
                return true;
            }
            catch (VmGuestExceptionSignal) when (IsRaisedException("AttributeError"))
            {
                _raisedException = VmValue.Null;
                _raisedLastInstruction = -1;
                return false;
            }
        }

        private void AddDictionaryStringKeys(VmValue destination, VmValue source)
        {
            RequireObjectType(destination, VmObjectType.Dictionary);
            RequireObjectType(source, VmObjectType.Dictionary);
            var payload = _memory.GetObjectPayloadAddress(source);
            var capacity = _memory.ReadInt32(payload + 12);
            var entries = GetDictionaryEntriesPayload(source);
            for (var slot = 0; slot < capacity; slot++)
            {
                var key = _memory.ReadValue(entries + slot * DictionaryEntrySize + 8);
                if (key.IsNull || key.IsDeleted || !IsObjectType(key, VmObjectType.String))
                    continue;
                DictionarySet(destination, key, VmValue.None, rejectDuplicate: false);
                ConsumeInstructionBudget();
            }
        }

        private VmValue CallDirBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireNoKeywordArguments(VmBuiltin.Dir, keywordNames);
            RequireArgumentCount(VmBuiltin.Dir, positionalCount, 0, 1);
            var names = CreateDictionary(16);
            if (positionalCount == 0)
            {
                var locals = GetFrameLocalsMapping(_currentFrame);
                AddDictionaryStringKeys(names, locals.IsNull ? GetFrameGlobals(_currentFrame) : locals);
            }
            else
            {
                var owner = GetTupleItem(arguments, 0);
                if (IsObjectType(owner, VmObjectType.Module))
                {
                    AddDictionaryStringKeys(names, GetModuleNamespace(owner));
                }
                else if (IsObjectType(owner, VmObjectType.Class))
                {
                    var mro = GetClassMro(owner);
                    for (var index = 0; index < GetTupleCount(mro); index++)
                        AddDictionaryStringKeys(names, GetClassNamespace(GetTupleItem(mro, index)));
                }
                else if (IsObjectType(owner, VmObjectType.Instance))
                {
                    AddDictionaryStringKeys(names, GetInstanceDictionary(owner));
                    var mro = GetClassMro(GetInstanceClass(owner));
                    for (var index = 0; index < GetTupleCount(mro); index++)
                        AddDictionaryStringKeys(names, GetClassNamespace(GetTupleItem(mro, index)));
                }
                else if (IsObjectType(owner, VmObjectType.Function))
                {
                    AddDictionaryStringKeys(names, _memory.ReadValue(_memory.GetObjectPayloadAddress(owner) + 48));
                }
            }
            var result = DictionaryKeys(names, values: false);
            SortListInPlace(result, VmValue.None, reverse: false);
            return result;
        }

        private VmValue CallEnumerateBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "start");
            if (positionalCount is < 1 or > 2)
                Raise("TypeError", $"enumerate() expected 1 or 2 arguments, got {positionalCount}");
            var start = positionalCount == 2 ? GetTupleItem(arguments, 1) : VmValue.FromSmallInteger(0);
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "start", out var keywordStart))
            {
                if (positionalCount == 2) Raise("TypeError", "enumerate() got multiple values for argument 'start'");
                start = keywordStart;
            }
            var startInteger = GetIndexInteger(start);
            EnsureHostRootCapacity(3);
            var sourceIterator = CreateIterator(GetTupleItem(arguments, 0));
            var rootBase = PushHostRoots(sourceIterator, start, VmValue.Null);
            try
            {
                return CreateBuiltinIterator(
                    VmBuiltinIteratorKind.Enumerate,
                    sourceIterator,
                    CreateInteger(startInteger));
            }
            finally
            {
                PopHostRoots(rootBase);
            }
        }

        private static bool IsIntegerLiteralWhitespace(byte value)
        {
            return value is 0x09 or 0x0a or 0x0b or 0x0c or 0x0d or 0x20;
        }

        private static int GetIntegerLiteralDigit(byte value)
        {
            if (value >= (byte)'0' && value <= (byte)'9')
                return value - (byte)'0';
            if (value >= (byte)'a' && value <= (byte)'z')
                return value - (byte)'a' + 10;
            if (value >= (byte)'A' && value <= (byte)'Z')
                return value - (byte)'A' + 10;
            return -1;
        }

        private VmValue ParseIntegerLiteral(VmValue source, int radix)
        {
            var requestedRadix = radix;
            var length = _memory.GetObjectAux0(source);
            var text = _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(source), length);
            var start = 0;
            var end = text.Length;
            while (start < end && IsIntegerLiteralWhitespace(text[start]))
                start++;
            while (end > start && IsIntegerLiteralWhitespace(text[end - 1]))
                end--;

            var negative = false;
            if (start < end && (text[start] == (byte)'+' || text[start] == (byte)'-'))
            {
                negative = text[start] == (byte)'-';
                start++;
            }

            var autoRadix = radix == 0;
            var prefixed = false;
            if (start + 1 < end && text[start] == (byte)'0')
            {
                var prefix = text[start + 1];
                var prefixRadix = prefix == (byte)'b' || prefix == (byte)'B' ? 2
                    : prefix == (byte)'o' || prefix == (byte)'O' ? 8
                    : prefix == (byte)'x' || prefix == (byte)'X' ? 16
                    : 0;
                if (prefixRadix != 0 && (radix == 0 || radix == prefixRadix))
                {
                    radix = prefixRadix;
                    start += 2;
                    prefixed = true;
                    if (start < end && text[start] == (byte)'_')
                        start++;
                }
            }
            if (radix == 0)
                radix = 10;

            var autoDecimalWithLeadingZero = autoRadix && !prefixed && start < end && text[start] == (byte)'0';
            var result = BigInteger.Zero;
            var sawDigit = false;
            var previousUnderscore = false;
            for (var index = start; index < end; index++)
            {
                var current = text[index];
                if (current == (byte)'_')
                {
                    if (!sawDigit || previousUnderscore)
                        goto InvalidLiteral;
                    previousUnderscore = true;
                    ConsumeInstructionBudget();
                    continue;
                }

                var digit = GetIntegerLiteralDigit(current);
                if (digit < 0 || digit >= radix)
                    goto InvalidLiteral;
                if (autoDecimalWithLeadingZero && digit != 0)
                    goto InvalidLiteral;
                result = result * radix + digit;
                EnsureIntegerSize(result);
                sawDigit = true;
                previousUnderscore = false;
                ConsumeInstructionBudget();
            }

            if (!sawDigit || previousUnderscore)
                goto InvalidLiteral;
            return CreateInteger(negative ? -result : result);

        InvalidLiteral:
            Raise("ValueError", $"invalid literal for int() with base {requestedRadix}: {Repr(source, 0)}");
            return VmValue.None;
        }

        private VmValue CallIntBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "base");
            if (positionalCount is < 0 or > 2)
                Raise("TypeError", $"int() takes at most 2 arguments ({positionalCount} given)");

            var hasBase = positionalCount == 2;
            var baseValue = hasBase ? GetTupleItem(arguments, 1) : VmValue.FromSmallInteger(10);
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "base", out var keywordBase))
            {
                if (hasBase)
                    Raise("TypeError", "int() got multiple values for argument 'base'");
                hasBase = true;
                baseValue = keywordBase;
            }

            if (positionalCount == 0)
            {
                if (hasBase)
                    Raise("TypeError", "int() missing string argument");
                return VmValue.FromSmallInteger(0);
            }

            var source = GetTupleItem(arguments, 0);
            if (hasBase)
            {
                var radixInteger = GetIndexInteger(baseValue);
                if (radixInteger < int.MinValue || radixInteger > int.MaxValue)
                    Raise("ValueError", "int() base must be >= 2 and <= 36, or 0");
                var radix = (int)radixInteger;
                if (radix != 0 && (radix < 2 || radix > 36))
                    Raise("ValueError", "int() base must be >= 2 and <= 36, or 0");
                if (!IsObjectType(source, VmObjectType.String) && !IsObjectType(source, VmObjectType.Bytes))
                    Raise("TypeError", "int() can't convert non-string with explicit base");
                return ParseIntegerLiteral(source, radix);
            }

            if (IsInteger(source))
                return CreateInteger(GetInteger(source));
            if (IsFloat(source))
            {
                var floatingPoint = GetFloat(source);
                if (double.IsNaN(floatingPoint))
                    Raise("ValueError", "cannot convert float NaN to integer");
                if (double.IsInfinity(floatingPoint))
                    Raise("OverflowError", "cannot convert float infinity to integer");
                return CreateInteger(new BigInteger(floatingPoint));
            }
            if (IsObjectType(source, VmObjectType.String) || IsObjectType(source, VmObjectType.Bytes))
                return ParseIntegerLiteral(source, 10);
            if (IsObjectType(source, VmObjectType.Instance))
            {
                if (HasSpecialMethod(source, "__int__"))
                {
                    var converted = CallZeroArgumentSpecialMethod(source, "__int__");
                    if (!IsInteger(converted))
                        Raise("TypeError", $"__int__ returned non-int (type {GetTypeName(converted)})");
                    return CreateInteger(GetInteger(converted));
                }
                if (HasSpecialMethod(source, "__index__"))
                {
                    var converted = CallZeroArgumentSpecialMethod(source, "__index__");
                    if (!IsInteger(converted))
                        Raise("TypeError", $"__index__ returned non-int (type {GetTypeName(converted)})");
                    return CreateInteger(GetInteger(converted));
                }
            }
            Raise("TypeError", $"int() argument must be a string, a bytes-like object or a real number, not '{GetTypeName(source)}'");
            return VmValue.None;
        }

        private VmValue CallFloatBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireNoKeywordArguments(VmBuiltin.Float, keywordNames);
            RequireArgumentCount(VmBuiltin.Float, positionalCount, 0, 1);
            if (positionalCount == 0)
                return CreateFloat(0.0);
            var value = GetTupleItem(arguments, 0);
            if (IsInteger(value) || IsFloat(value))
                return CreateFloat(GetFloat(value));
            if (IsObjectType(value, VmObjectType.String))
            {
                var text = GetString(value).Trim().Replace("_", string.Empty, StringComparison.Ordinal);
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                    return CreateFloat(result);
                Raise("ValueError", $"could not convert string to float: {QuoteString(GetString(value))}");
            }
            if (IsObjectType(value, VmObjectType.Instance))
            {
                if (HasSpecialMethod(value, "__float__"))
                {
                    var converted = CallZeroArgumentSpecialMethod(value, "__float__");
                    if (!IsFloat(converted))
                        Raise("TypeError", $"__float__ returned non-float (type {GetTypeName(converted)})");
                    return converted;
                }
                if (HasSpecialMethod(value, "__index__"))
                {
                    var converted = CallZeroArgumentSpecialMethod(value, "__index__");
                    if (!IsInteger(converted))
                        Raise("TypeError", $"__index__ returned non-int (type {GetTypeName(converted)})");
                    return CreateFloat(GetFloat(converted));
                }
            }
            Raise("TypeError", $"float() argument must be a string or a real number, not '{GetTypeName(value)}'");
            return VmValue.None;
        }

        private VmValue CallMinMaxBuiltin(
            VmBuiltin builtin,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "key", "default");
            if (positionalCount == 0)
                Raise("TypeError", $"{GetBuiltinName(builtin)} expected at least 1 argument, got 0");
            var key = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "key", out var keyValue)
                ? keyValue : VmValue.None;
            var hasDefault = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "default", out var defaultValue);
            if (!key.IsNone && !IsCallableValue(key))
                Raise("TypeError", $"'{GetTypeName(key)}' object is not callable");
            if (positionalCount > 1 && hasDefault)
                Raise("TypeError", $"Cannot specify a default for {GetBuiltinName(builtin)}() with multiple positional arguments");

            VmValue iterator;
            if (positionalCount == 1)
            {
                iterator = CreateIterator(GetTupleItem(arguments, 0));
            }
            else
            {
                var positionalValues = CreateTuple(positionalCount);
                for (var index = 0; index < positionalCount; index++)
                    SetTupleItem(positionalValues, index, GetTupleItem(arguments, index));
                iterator = CreateIterator(positionalValues);
            }

            if (!IteratorMoveNext(iterator, out var best))
            {
                if (hasDefault)
                    return defaultValue;
                Raise("ValueError", $"{GetBuiltinName(builtin)}() arg is an empty sequence");
            }
            var bestKey = CallUnaryKey(key, best);
            var selector = builtin == VmBuiltin.Min ? 0 : 4;
            while (IteratorMoveNext(iterator, out var current))
            {
                var currentKey = CallUnaryKey(key, current);
                if (Compare(selector, currentKey, bestKey))
                {
                    best = current;
                    bestKey = currentKey;
                }
                ConsumeInstructionBudget();
            }
            return best;
        }

        private VmValue CallMapBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "strict");
            if (positionalCount < 2)
                Raise("TypeError", $"map() must have at least two arguments, got {positionalCount}");
            var callable = GetTupleItem(arguments, 0);
            if (!IsCallableValue(callable))
                Raise("TypeError", $"'{GetTypeName(callable)}' object is not callable");
            var strict = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "strict", out var strictValue) &&
                IsTruthy(strictValue);
            EnsureHostRootCapacity(3);
            var iterators = CreateTuple(positionalCount - 1);
            var rootBase = PushHostRoots(iterators, callable, VmValue.Null);
            try
            {
                for (var index = 1; index < positionalCount; index++)
                    SetTupleItem(iterators, index - 1, CreateIterator(GetTupleItem(arguments, index)));
                return CreateBuiltinIterator(VmBuiltinIteratorKind.Map, callable, iterators, flags: strict ? 1 : 0);
            }
            finally
            {
                PopHostRoots(rootBase);
            }
        }

        private VmValue CallPowBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "mod");
            if (positionalCount is < 2 or > 3)
                Raise("TypeError", $"pow() expected 2 or 3 arguments, got {positionalCount}");
            var modulus = positionalCount == 3 ? GetTupleItem(arguments, 2) : VmValue.None;
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "mod", out var keywordModulus))
            {
                if (positionalCount == 3) Raise("TypeError", "pow() got multiple values for argument 'mod'");
                modulus = keywordModulus;
            }
            var left = GetTupleItem(arguments, 0);
            var exponent = GetTupleItem(arguments, 1);
            if (modulus.IsNone)
                return BinaryOperation(PythonBinaryOperation.Power, left, exponent);
            if (!IsInteger(left) || !IsInteger(exponent) || !IsInteger(modulus))
                Raise("TypeError", "pow() 3rd argument not allowed unless all arguments are integers");
            var modulusInteger = GetInteger(modulus);
            if (modulusInteger.IsZero)
                Raise("ValueError", "pow() 3rd argument cannot be 0");
            var exponentInteger = GetInteger(exponent);
            if (exponentInteger.Sign < 0)
                Raise("ValueError", "pow() negative exponent with modulus is not supported by the safe core");
            var positiveModulus = BigInteger.Abs(modulusInteger);
            var factor = GetInteger(left) % positiveModulus;
            if (factor.Sign < 0)
                factor += positiveModulus;
            var result = BigInteger.One % positiveModulus;
            var remainingExponent = exponentInteger;
            while (remainingExponent.Sign > 0)
            {
                if (!remainingExponent.IsEven)
                    result = result * factor % positiveModulus;
                remainingExponent >>= 1;
                if (remainingExponent.Sign > 0)
                    factor = factor * factor % positiveModulus;
                ConsumeInstructionBudget();
            }
            if (modulusInteger.Sign < 0 && !result.IsZero)
                result -= positiveModulus;
            return CreateInteger(result);
        }

        private VmValue CallPropertyBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "fget", "fset", "fdel", "doc");
            if (positionalCount > 4)
                Raise("TypeError", $"property() takes at most 4 arguments ({positionalCount} given)");
            var values = new[] { VmValue.None, VmValue.None, VmValue.None, VmValue.None };
            for (var index = 0; index < positionalCount; index++)
                values[index] = GetTupleItem(arguments, index);
            var names = new[] { "fget", "fset", "fdel", "doc" };
            for (var index = 0; index < names.Length; index++)
            {
                if (!TryGetKeywordArgument(arguments, positionalCount, keywordNames, names[index], out var keywordValue))
                    continue;
                if (index < positionalCount)
                    Raise("TypeError", $"property() got multiple values for argument '{names[index]}'");
                values[index] = keywordValue;
            }
            return CreateProperty(values[0], values[1], values[2], values[3]);
        }

        private VmValue CallReversedBuiltin(VmValue value)
        {
            if (IsObjectType(value, VmObjectType.Instance))
            {
                if (HasSpecialMethod(value, "__reversed__"))
                    return CreateIterator(CallZeroArgumentSpecialMethod(value, "__reversed__"));
                if (!HasSpecialMethod(value, "__len__") || !HasSpecialMethod(value, "__getitem__"))
                    Raise("TypeError", $"'{GetTypeName(value)}' object is not reversible");
            }
            else if (!value.IsAddress || _memory.GetObjectType(value) is not (
                VmObjectType.List or VmObjectType.Tuple or VmObjectType.String or
                VmObjectType.Bytes or VmObjectType.Range))
            {
                Raise("TypeError", $"'{GetTypeName(value)}' object is not reversible");
            }
            return CreateBuiltinIterator(
                VmBuiltinIteratorKind.Reversed,
                value,
                index: GetLength(value) - 1L);
        }

        private VmValue CallRoundBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "ndigits");
            if (positionalCount is < 1 or > 2)
                Raise("TypeError", $"round() expected 1 or 2 arguments, got {positionalCount}");
            var hasDigits = positionalCount == 2;
            var digitsValue = hasDigits ? GetTupleItem(arguments, 1) : VmValue.None;
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "ndigits", out var keywordDigits))
            {
                if (hasDigits) Raise("TypeError", "round() got multiple values for argument 'ndigits'");
                hasDigits = true;
                digitsValue = keywordDigits;
            }
            var value = GetTupleItem(arguments, 0);
            if (IsObjectType(value, VmObjectType.Instance) && HasSpecialMethod(value, "__round__"))
            {
                if (!hasDigits)
                    return CallZeroArgumentSpecialMethod(value, "__round__");
                return CallBinarySpecialMethod(value, "__round__", digitsValue);
            }
            if (IsInteger(value))
            {
                if (!hasDigits)
                    return CreateInteger(GetInteger(value));
                var integerDigits = GetIndexInteger(digitsValue);
                if (integerDigits >= 0)
                    return CreateInteger(GetInteger(value));
                if (integerDigits <= int.MinValue)
                    return VmValue.FromSmallInteger(0);
                var places = checked((int)-integerDigits);
                var maximumDecimalPlaces = (int)Math.Floor(_limits.MaxIntegerBits / Math.Log2(10.0));
                if (places > maximumDecimalPlaces)
                    return VmValue.FromSmallInteger(0);
                var factor = BigInteger.Pow(10, places);
                EnsureIntegerSize(factor);
                var integer = GetInteger(value);
                var quotient = BigInteger.DivRem(integer, factor, out var remainder);
                var doubled = BigInteger.Abs(remainder) * 2;
                if (doubled > factor || (doubled == factor && !quotient.IsEven))
                    quotient += integer.Sign >= 0 ? BigInteger.One : BigInteger.MinusOne;
                return CreateInteger(quotient * factor);
            }
            if (!IsFloat(value))
                Raise("TypeError", $"type {GetTypeName(value)} doesn't define __round__ method");
            var number = GetFloat(value);
            if (!hasDigits)
            {
                if (double.IsNaN(number)) Raise("ValueError", "cannot convert float NaN to integer");
                if (double.IsInfinity(number)) Raise("OverflowError", "cannot convert float infinity to integer");
                return CreateInteger(new BigInteger(Math.Round(number, MidpointRounding.ToEven)));
            }
            var digitsInteger = GetIndexInteger(digitsValue);
            if (digitsInteger > 308) return CreateFloat(number);
            if (digitsInteger < -308) return CreateFloat(Math.CopySign(0.0, number));
            var digits = (int)digitsInteger;
            if (digits is >= 0 and <= 15)
                return CreateFloat(Math.Round(number, digits, MidpointRounding.ToEven));
            var scale = Math.Pow(10.0, Math.Abs(digits));
            if (double.IsInfinity(scale))
                return CreateFloat(digits > 0 ? number : Math.CopySign(0.0, number));
            var rounded = digits > 0
                ? Math.Round(number * scale, MidpointRounding.ToEven) / scale
                : Math.Round(number / scale, MidpointRounding.ToEven) * scale;
            return CreateFloat(rounded);
        }

        private void SortListInPlace(VmValue list, VmValue key, bool reverse)
        {
            RequireObjectType(list, VmObjectType.List);
            var count = GetListCount(list);
            var keys = CreateList(count);
            var rootBase = PushHostRoots(list, keys, key);
            try
            {
                for (var index = 0; index < count; index++)
                {
                    ListAdd(keys, CallUnaryKey(key, GetListItem(list, index)));
                    ConsumeInstructionBudget();
                }
                var selector = reverse ? 4 : 0;
                for (var index = 1; index < count; index++)
                {
                    var value = GetListItem(list, index);
                    var valueKey = GetListItem(keys, index);
                    var destination = index;
                    while (destination > 0 && Compare(selector, valueKey, GetListItem(keys, destination - 1)))
                    {
                        SetListItem(list, destination, GetListItem(list, destination - 1));
                        SetListItem(keys, destination, GetListItem(keys, destination - 1));
                        destination--;
                        ConsumeInstructionBudget();
                    }
                    SetListItem(list, destination, value);
                    SetListItem(keys, destination, valueKey);
                }
            }
            finally
            {
                PopHostRoots(rootBase);
            }
        }

        private VmValue CallPrintBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "sep", "end", "file", "flush");
            var separator = " ";
            var ending = "\n";
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "sep", out var separatorValue) && !separatorValue.IsNone)
            {
                if (!IsObjectType(separatorValue, VmObjectType.String))
                    Raise("TypeError", $"sep must be None or a string, not {GetTypeName(separatorValue)}");
                separator = GetString(separatorValue);
            }
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "end", out var endingValue) && !endingValue.IsNone)
            {
                if (!IsObjectType(endingValue, VmObjectType.String))
                    Raise("TypeError", $"end must be None or a string, not {GetTypeName(endingValue)}");
                ending = GetString(endingValue);
            }
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "file", out var fileValue) && !fileValue.IsNone)
                Raise("TypeError", "print() file redirection is not available in the safe VM");
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "flush", out var flushValue))
                _ = IsTruthy(flushValue);

            var builder = new StringBuilder();
            for (var index = 0; index < positionalCount; index++)
            {
                if (index != 0)
                    AppendBoundedText(builder, separator);
                AppendBoundedText(builder, Str(GetTupleItem(arguments, index)));
            }
            AppendBoundedText(builder, ending);
            AppendOutput(builder.ToString());
            return VmValue.None;
        }

        private VmValue CallSortedBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "key", "reverse");
            if (positionalCount != 1)
                Raise("TypeError", $"sorted expected 1 argument, got {positionalCount}");
            var key = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "key", out var keyValue)
                ? keyValue : VmValue.None;
            if (!key.IsNone && !IsCallableValue(key))
                Raise("TypeError", $"'{GetTypeName(key)}' object is not callable");
            var reverse = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "reverse", out var reverseValue) &&
                IsTruthy(reverseValue);
            var result = MaterializeList(GetTupleItem(arguments, 0));
            SortListInPlace(result, key, reverse);
            return result;
        }

        private VmValue CallSumBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "start");
            if (positionalCount is < 1 or > 2)
                Raise("TypeError", $"sum() expected 1 or 2 arguments, got {positionalCount}");
            var total = positionalCount == 2 ? GetTupleItem(arguments, 1) : VmValue.FromSmallInteger(0);
            if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "start", out var keywordStart))
            {
                if (positionalCount == 2) Raise("TypeError", "sum() got multiple values for argument 'start'");
                total = keywordStart;
            }
            if (IsObjectType(total, VmObjectType.String) || IsObjectType(total, VmObjectType.Bytes))
                Raise("TypeError", "sum() can't sum strings or bytes");
            var iterator = CreateIterator(GetTupleItem(arguments, 0));
            while (IteratorMoveNext(iterator, out var item))
            {
                total = BinaryOperation(PythonBinaryOperation.Add, total, item);
                ConsumeInstructionBudget();
            }
            return total;
        }

        private VmValue CallVarsBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireNoKeywordArguments(VmBuiltin.Vars, keywordNames);
            RequireArgumentCount(VmBuiltin.Vars, positionalCount, 0, 1);
            if (positionalCount == 0)
            {
                var locals = GetFrameLocalsMapping(_currentFrame);
                return locals.IsNull ? GetFrameGlobals(_currentFrame) : locals;
            }
            var owner = GetTupleItem(arguments, 0);
            if (IsObjectType(owner, VmObjectType.Module)) return GetModuleNamespace(owner);
            if (IsObjectType(owner, VmObjectType.Instance)) return GetInstanceDictionary(owner);
            if (IsObjectType(owner, VmObjectType.Class)) return CreateMappingProxy(GetClassNamespace(owner));
            if (IsObjectType(owner, VmObjectType.Function)) return _memory.ReadValue(_memory.GetObjectPayloadAddress(owner) + 48);
            Raise("TypeError", "vars() argument must have __dict__ attribute");
            return VmValue.None;
        }

        private VmValue CallZipBuiltin(VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            RequireOnlyKeywords(keywordNames, "strict");
            var strict = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "strict", out var strictValue) &&
                IsTruthy(strictValue);
            EnsureHostRootCapacity(3);
            var iterators = CreateTuple(positionalCount);
            var rootBase = PushHostRoots(iterators, VmValue.Null, VmValue.Null);
            try
            {
                for (var index = 0; index < positionalCount; index++)
                    SetTupleItem(iterators, index, CreateIterator(GetTupleItem(arguments, index)));
                return CreateBuiltinIterator(VmBuiltinIteratorKind.Zip, iterators, flags: strict ? 1 : 0);
            }
            finally
            {
                PopHostRoots(rootBase);
            }
        }

        private VmValue CallBuiltin(VmBuiltin builtin, VmValue arguments, int positionalCount, VmValue keywordNames)
        {
            var argumentCount = GetTupleCount(arguments);
            var keywordCount = GetKeywordCount(keywordNames);
            if (positionalCount < 0 || positionalCount + keywordCount != argumentCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Builtin CALL argument layout is inconsistent.");

            switch (builtin)
            {
                case VmBuiltin.Print:
                    return CallPrintBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Int:
                    return CallIntBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Abs:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CallAbsBuiltin(GetTupleItem(arguments, 0));
                case VmBuiltin.Ascii:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CreateString(EscapeNonAscii(Repr(GetTupleItem(arguments, 0), 0)));
                case VmBuiltin.Bin:
                case VmBuiltin.Oct:
                case VmBuiltin.Hex:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    var integerValue = GetIndexInteger(GetTupleItem(arguments, 0));
                    return CreateString(builtin switch
                    {
                        VmBuiltin.Bin => FormatIntegerBase(integerValue, 2, "0b"),
                        VmBuiltin.Oct => FormatIntegerBase(integerValue, 8, "0o"),
                        _ => FormatIntegerBase(integerValue, 16, "0x"),
                    });
                case VmBuiltin.Bytes:
                    return CallBytesBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Callable:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return IsCallableValue(GetTupleItem(arguments, 0)) ? VmValue.True : VmValue.False;
                case VmBuiltin.Chr:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    {
                        var codeValue = GetTupleItem(arguments, 0);
                        var codePoint = GetIndexInteger(codeValue);
                        if (codePoint < 0 || codePoint > 0x10ffff)
                            Raise("ValueError", "chr() arg not in range(0x110000)");
                        var integer = (int)codePoint;
                        return CreateString(integer <= 0xffff
                            ? new string((char)integer, 1)
                            : char.ConvertFromUtf32(integer));
                    }
                case VmBuiltin.ClassMethod:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CreateClassMethod(GetTupleItem(arguments, 0));
                case VmBuiltin.Complex:
                    return CallComplexBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.DelAttr:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 2);
                    DeleteAttributeValue(GetTupleItem(arguments, 0), GetTupleItem(arguments, 1));
                    return VmValue.None;
                case VmBuiltin.Dir:
                    return CallDirBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.DivMod:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 2);
                    {
                        var result = CreateTuple(2);
                        SetTupleItem(result, 0, BinaryOperation(PythonBinaryOperation.FloorDivide, GetTupleItem(arguments, 0), GetTupleItem(arguments, 1)));
                        SetTupleItem(result, 1, BinaryOperation(PythonBinaryOperation.Remainder, GetTupleItem(arguments, 0), GetTupleItem(arguments, 1)));
                        return result;
                    }
                case VmBuiltin.Enumerate:
                    return CallEnumerateBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Filter:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 2);
                    {
                        var predicate = GetTupleItem(arguments, 0);
                        if (!predicate.IsNone && !IsCallableValue(predicate))
                            Raise("TypeError", $"'{GetTypeName(predicate)}' object is not callable");
                        EnsureHostRootCapacity(3);
                        var sourceIterator = CreateIterator(GetTupleItem(arguments, 1));
                        var rootBase = PushHostRoots(sourceIterator, predicate, VmValue.Null);
                        try
                        {
                            return CreateBuiltinIterator(
                                VmBuiltinIteratorKind.Filter,
                                predicate,
                                sourceIterator);
                        }
                        finally
                        {
                            PopHostRoots(rootBase);
                        }
                    }
                case VmBuiltin.Float:
                    return CallFloatBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.FrozenSet:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 0, 1);
                    return CreateFrozenSet(positionalCount == 0 ? VmValue.Null : GetTupleItem(arguments, 0));
                case VmBuiltin.Format:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 2);
                    {
                        var specification = positionalCount == 2
                            ? GetStringArgument(GetTupleItem(arguments, 1), "format")
                            : string.Empty;
                        return FormatValue(GetTupleItem(arguments, 0), specification);
                    }
                case VmBuiltin.GetAttr:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 3);
                    {
                        var owner = GetTupleItem(arguments, 0);
                        var name = GetTupleItem(arguments, 1);
                        RequireObjectType(name, VmObjectType.String);
                        if (positionalCount == 2)
                            return GetAttributeValue(owner, name);
                        try
                        {
                            return GetAttributeValue(owner, name);
                        }
                        catch (VmGuestExceptionSignal) when (IsRaisedException("AttributeError"))
                        {
                            _raisedException = VmValue.Null;
                            _raisedLastInstruction = -1;
                            return GetTupleItem(arguments, 2);
                        }
                    }
                case VmBuiltin.Globals:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 0, 0);
                    return GetFrameGlobals(_currentFrame);
                case VmBuiltin.HasAttr:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 2);
                    RequireObjectType(GetTupleItem(arguments, 1), VmObjectType.String);
                    return HasAttributeValue(GetTupleItem(arguments, 0), GetTupleItem(arguments, 1)) ? VmValue.True : VmValue.False;
                case VmBuiltin.Hash:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    {
                        var hash = unchecked((long)GetHash(GetTupleItem(arguments, 0)));
                        if (hash == -1) hash = -2;
                        return CreateInteger(new BigInteger(hash));
                    }
                case VmBuiltin.Id:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CreateInteger(new BigInteger(GetTupleItem(arguments, 0).Raw));
                case VmBuiltin.IsInstance:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 2);
                    return MatchesClassInfo(GetTupleItem(arguments, 0), GetTupleItem(arguments, 1), subclassCheck: false)
                        ? VmValue.True : VmValue.False;
                case VmBuiltin.IsSubclass:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 2, 2);
                    return MatchesClassInfo(GetTupleItem(arguments, 0), GetTupleItem(arguments, 1), subclassCheck: true)
                        ? VmValue.True : VmValue.False;
                case VmBuiltin.Iter:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 2);
                    if (positionalCount == 1)
                        return CreateIterator(GetTupleItem(arguments, 0));
                    if (!IsCallableValue(GetTupleItem(arguments, 0)))
                        Raise("TypeError", "iter(v, w): v must be callable");
                    return CreateBuiltinIterator(
                        VmBuiltinIteratorKind.CallableSentinel,
                        GetTupleItem(arguments, 0),
                        GetTupleItem(arguments, 1));
                case VmBuiltin.Locals:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 0, 0);
                    {
                        var locals = GetFrameLocalsMapping(_currentFrame);
                        return locals.IsNull ? GetFrameGlobals(_currentFrame) : locals;
                    }
                case VmBuiltin.Map:
                    return CallMapBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Max:
                case VmBuiltin.Min:
                    return CallMinMaxBuiltin(builtin, arguments, positionalCount, keywordNames);
                case VmBuiltin.Ord:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    {
                        var value = GetTupleItem(arguments, 0);
                        if (IsObjectType(value, VmObjectType.Bytes))
                        {
                            if (_memory.GetObjectAux0(value) != 1)
                                Raise("TypeError", "ord() expected a character, but string of length != 1 found");
                            return VmValue.FromSmallInteger(_memory.ReadByte(_memory.GetObjectPayloadAddress(value)));
                        }
                        if (!IsObjectType(value, VmObjectType.String))
                            Raise("TypeError", $"ord() expected string of length 1, but {GetTypeName(value)} found");
                        var text = GetString(value);
                        if (CountCodePoints(text) != 1)
                            Raise("TypeError", $"ord() expected a character, but string of length {CountCodePoints(text)} found");
                        return CreateInteger(new BigInteger(ReadCodePoint(text, 0, out _)));
                    }
                case VmBuiltin.Pow:
                    return CallPowBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Property:
                    return CallPropertyBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Repr:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CreateString(Repr(GetTupleItem(arguments, 0), 0));
                case VmBuiltin.Reversed:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CallReversedBuiltin(GetTupleItem(arguments, 0));
                case VmBuiltin.Round:
                    return CallRoundBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.SetAttr:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 3, 3);
                    SetAttributeValue(GetTupleItem(arguments, 0), GetTupleItem(arguments, 1), GetTupleItem(arguments, 2));
                    return VmValue.None;
                case VmBuiltin.Slice:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 3);
                    return positionalCount == 1
                        ? CreateSlice(VmValue.None, GetTupleItem(arguments, 0), VmValue.None)
                        : CreateSlice(
                            GetTupleItem(arguments, 0),
                            GetTupleItem(arguments, 1),
                            positionalCount == 3 ? GetTupleItem(arguments, 2) : VmValue.None);
                case VmBuiltin.Sorted:
                    return CallSortedBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.StaticMethod:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    return CreateStaticMethod(GetTupleItem(arguments, 0));
                case VmBuiltin.Sum:
                    return CallSumBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Vars:
                    return CallVarsBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.Zip:
                    return CallZipBuiltin(arguments, positionalCount, keywordNames);
                case VmBuiltin.MathDegrees:
                case VmBuiltin.MathRadians:
                case VmBuiltin.MathHypot:
                case VmBuiltin.MathGcd:
                case VmBuiltin.MathLcm:
                case VmBuiltin.MathFactorial:
                case VmBuiltin.MathComb:
                case VmBuiltin.MathPerm:
                case VmBuiltin.MathProd:
                case VmBuiltin.MathIsClose:
                case VmBuiltin.MathSinh:
                case VmBuiltin.MathCosh:
                case VmBuiltin.MathTanh:
                case VmBuiltin.MathAsinh:
                case VmBuiltin.MathAcosh:
                case VmBuiltin.MathAtanh:
                    return CallExtendedMathBuiltin(builtin, arguments, positionalCount, keywordNames);
                case VmBuiltin.Dict:
                    if (positionalCount > 1)
                        Raise("TypeError", $"dict expected at most 1 argument, got {positionalCount}");
                    {
                        var dictionary = CreateDictionary(8);
                        if (positionalCount == 1)
                            DictionaryUpdate(dictionary, GetTupleItem(arguments, 0), rejectDuplicate: false);
                        for (var index = 0; index < keywordCount; index++)
                        {
                            DictionarySet(
                                dictionary,
                                GetTupleItem(keywordNames, index),
                                GetTupleItem(arguments, positionalCount + index),
                                rejectDuplicate: false);
                        }
                        return dictionary;
                    }
            }

            if (keywordCount != 0)
                Raise("TypeError", $"{GetBuiltinName(builtin)}() takes no keyword arguments");
            if (positionalCount != argumentCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Builtin CALL argument layout is inconsistent.");

            switch (builtin)
            {
                case VmBuiltin.Print:
                    {
                        var builder = new StringBuilder();
                        for (var index = 0; index < argumentCount; index++)
                        {
                            if (index != 0)
                                AppendBoundedText(builder, " ");
                            AppendBoundedText(builder, Str(GetTupleItem(arguments, index)));
                        }
                        AppendBoundedText(builder, "\n");
                        AppendOutput(builder.ToString());
                        return VmValue.None;
                    }

                case VmBuiltin.Len:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    return CreateInteger(new BigInteger(GetLength(GetTupleItem(arguments, 0))));

                case VmBuiltin.Range:
                    RequireArgumentCount(builtin, argumentCount, 1, 3);
                    {
                        long start;
                        long stop;
                        long step;
                        if (argumentCount == 1)
                        {
                            start = 0;
                            stop = GetInt64(GetTupleItem(arguments, 0), "range");
                            step = 1;
                        }
                        else
                        {
                            start = GetInt64(GetTupleItem(arguments, 0), "range");
                            stop = GetInt64(GetTupleItem(arguments, 1), "range");
                            step = argumentCount == 3
                                ? GetInt64(GetTupleItem(arguments, 2), "range")
                                : 1;
                        }
                        return CreateRange(start, stop, step);
                    }

                case VmBuiltin.List:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    return argumentCount == 0
                        ? CreateList(0)
                        : MaterializeList(GetTupleItem(arguments, 0));

                case VmBuiltin.Tuple:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    return argumentCount == 0
                        ? CreateTuple(0)
                        : MaterializeTuple(GetTupleItem(arguments, 0));

                case VmBuiltin.Set:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    {
                        var set = CreateSet();
                        if (argumentCount != 0)
                            ExtendSet(set, GetTupleItem(arguments, 0));
                        return set;
                    }

                case VmBuiltin.Dict:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    {
                        var dictionary = CreateDictionary(8);
                        if (argumentCount != 0)
                            DictionaryUpdate(dictionary, GetTupleItem(arguments, 0), rejectDuplicate: false);
                        return dictionary;
                    }

                case VmBuiltin.Bool:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    return argumentCount == 0 || !IsTruthy(GetTupleItem(arguments, 0))
                        ? VmValue.False
                        : VmValue.True;

                case VmBuiltin.Int:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    if (argumentCount == 0)
                        return VmValue.FromSmallInteger(0);
                    {
                        var value = GetTupleItem(arguments, 0);
                        if (IsInteger(value))
                            return CreateInteger(GetInteger(value));
                        if (IsFloat(value))
                        {
                            var floatingPoint = GetFloat(value);
                            if (double.IsNaN(floatingPoint))
                                Raise("ValueError", "cannot convert float NaN to integer");
                            if (double.IsInfinity(floatingPoint))
                                Raise("OverflowError", "cannot convert float infinity to integer");
                            return CreateInteger(new BigInteger(floatingPoint));
                        }
                        if (value.IsAddress && _memory.GetObjectType(value) == VmObjectType.String)
                        {
                            if (!BigInteger.TryParse(
                                    GetString(value),
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out var parsed))
                            {
                                Raise("ValueError", "invalid literal for int()");
                            }
                            return CreateInteger(parsed);
                        }
                        Raise("TypeError", $"int() argument must be a number or string, not {GetTypeName(value)}");
                        return VmValue.None;
                    }

                case VmBuiltin.Str:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    return CreateString(argumentCount == 0 ? string.Empty : Str(GetTupleItem(arguments, 0)));

                case VmBuiltin.All:
                case VmBuiltin.Any:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    {
                        var iterator = CreateIterator(GetTupleItem(arguments, 0));
                        var desired = builtin == VmBuiltin.Any;
                        while (IteratorMoveNext(iterator, out var item))
                        {
                            if (IsTruthy(item) == desired)
                                return desired ? VmValue.True : VmValue.False;
                            PollCancellation();
                        }
                        return desired ? VmValue.False : VmValue.True;
                    }

                case VmBuiltin.Iter:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    return CreateIterator(GetTupleItem(arguments, 0));

                case VmBuiltin.Next:
                    RequireArgumentCount(builtin, argumentCount, 1, 2);
                    {
                        var iterator = GetTupleItem(arguments, 0);
                        if (!IsObjectType(iterator, VmObjectType.Iterator) &&
                            !IsObjectType(iterator, VmObjectType.BuiltinIterator) &&
                            !IsObjectType(iterator, VmObjectType.Generator) &&
                            !(IsObjectType(iterator, VmObjectType.Instance) && HasSpecialMethod(iterator, "__next__")))
                        {
                            Raise("TypeError", $"'{GetTypeName(iterator)}' object is not an iterator");
                        }
                        if (IteratorMoveNext(iterator, out var item))
                            return item;
                        if (argumentCount == 2)
                            return GetTupleItem(arguments, 1);
                        Raise("StopIteration", string.Empty);
                        return VmValue.None;
                    }

                case VmBuiltin.ObjectInit:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    return VmValue.None;

                case VmBuiltin.Super:
                    RequireArgumentCount(builtin, argumentCount, 0, 2);
                    if (argumentCount == 0)
                        return CreateZeroArgumentSuper();
                    if (argumentCount == 1)
                        Raise("TypeError", "super() with one argument is not supported by the safe VM");
                    return CreateSuper(GetTupleItem(arguments, 0), GetTupleItem(arguments, 1));

                case VmBuiltin.Import:
                    RequireArgumentCount(builtin, argumentCount, 1, 5);
                    {
                        var nameValue = GetTupleItem(arguments, 0);
                        if (!IsObjectType(nameValue, VmObjectType.String))
                            Raise("TypeError", "module name must be a string");
                        var globals = argumentCount >= 2
                            ? GetTupleItem(arguments, 1)
                            : GetFrameGlobals(_currentFrame);
                        if (globals.IsNone)
                            globals = GetFrameGlobals(_currentFrame);
                        RequireObjectType(globals, VmObjectType.Dictionary);
                        var fromList = argumentCount >= 4
                            ? GetTupleItem(arguments, 3)
                            : VmValue.None;
                        var level = argumentCount >= 5
                            ? GetImportLevel(GetTupleItem(arguments, 4))
                            : 0;
                        return ImportRequested(GetString(nameValue), level, fromList, globals);
                    }

                case VmBuiltin.SysGetRecursionLimit:
                    RequireArgumentCount(builtin, argumentCount, 0, 0);
                    return CreateInteger(new BigInteger(_limits.MaxCallDepth));

                case VmBuiltin.MathSqrt:
                case VmBuiltin.MathFloor:
                case VmBuiltin.MathCeil:
                case VmBuiltin.MathTrunc:
                case VmBuiltin.MathFabs:
                case VmBuiltin.MathIsFinite:
                case VmBuiltin.MathIsInf:
                case VmBuiltin.MathIsNaN:
                case VmBuiltin.MathCopySign:
                case VmBuiltin.MathFmod:
                case VmBuiltin.MathPow:
                case VmBuiltin.MathSin:
                case VmBuiltin.MathCos:
                case VmBuiltin.MathTan:
                case VmBuiltin.MathAsin:
                case VmBuiltin.MathAcos:
                case VmBuiltin.MathAtan:
                case VmBuiltin.MathAtan2:
                case VmBuiltin.MathExp:
                case VmBuiltin.MathLog:
                case VmBuiltin.MathLog2:
                case VmBuiltin.MathLog10:
                    return CallMathBuiltin(builtin, arguments, argumentCount);

                case VmBuiltin.BaseException:
                case VmBuiltin.Exception:
                case VmBuiltin.TypeError:
                case VmBuiltin.ValueError:
                case VmBuiltin.RuntimeError:
                case VmBuiltin.AssertionError:
                case VmBuiltin.NotImplementedError:
                case VmBuiltin.KeyError:
                case VmBuiltin.IndexError:
                case VmBuiltin.NameError:
                case VmBuiltin.UnboundLocalError:
                case VmBuiltin.StopIteration:
                case VmBuiltin.ZeroDivisionError:
                case VmBuiltin.ArithmeticError:
                case VmBuiltin.LookupError:
                case VmBuiltin.AttributeError:
                case VmBuiltin.ImportError:
                case VmBuiltin.ModuleNotFoundError:
                case VmBuiltin.OverflowError:
                case VmBuiltin.SystemError:
                    RequireArgumentCount(builtin, argumentCount, 0, 1);
                    return CreateException(
                        GetBuiltinName(builtin),
                        argumentCount == 0 ? string.Empty : Str(GetTupleItem(arguments, 0)));

                default:
                    throw new VmTrapException(
                        VmStopReason.SecurityViolation,
                        $"Builtin capability {builtin} is not installed.");
            }
        }

        private BigInteger GetMathInteger(VmValue value, string functionName)
        {
            if (!IsInteger(value))
                Raise("TypeError", $"'{GetTypeName(value)}' object cannot be interpreted as an integer in math.{functionName}()");
            return GetInteger(value);
        }

        private VmValue CallExtendedMathBuiltin(
            VmBuiltin builtin,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames)
        {
            switch (builtin)
            {
                case VmBuiltin.MathDegrees:
                case VmBuiltin.MathRadians:
                case VmBuiltin.MathSinh:
                case VmBuiltin.MathCosh:
                case VmBuiltin.MathTanh:
                case VmBuiltin.MathAsinh:
                case VmBuiltin.MathAcosh:
                case VmBuiltin.MathAtanh:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    {
                        var number = GetMathNumber(GetTupleItem(arguments, 0), builtin);
                        double result;
                        switch (builtin)
                        {
                            case VmBuiltin.MathDegrees:
                                result = number * (180.0 / Math.PI);
                                break;
                            case VmBuiltin.MathRadians:
                                result = number * (Math.PI / 180.0);
                                break;
                            case VmBuiltin.MathSinh:
                                result = Math.Sinh(number);
                                break;
                            case VmBuiltin.MathCosh:
                                result = Math.Cosh(number);
                                break;
                            case VmBuiltin.MathTanh:
                                result = Math.Tanh(number);
                                break;
                            case VmBuiltin.MathAsinh:
                                result = Math.Asinh(number);
                                break;
                            case VmBuiltin.MathAcosh:
                                if (number < 1.0) Raise("ValueError", "math domain error");
                                result = Math.Acosh(number);
                                break;
                            default:
                                if (number is <= -1.0 or >= 1.0) Raise("ValueError", "math domain error");
                                result = Math.Atanh(number);
                                break;
                        }
                        if (double.IsInfinity(result) && double.IsFinite(number) && (builtin is VmBuiltin.MathSinh or VmBuiltin.MathCosh))
                            Raise("OverflowError", "math range error");
                        return CreateFloat(result);
                    }

                case VmBuiltin.MathHypot:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    {
                        var maximum = 0.0;
                        for (var index = 0; index < positionalCount; index++)
                        {
                            var magnitude = Math.Abs(GetMathNumber(GetTupleItem(arguments, index), builtin));
                            if (double.IsInfinity(magnitude)) return CreateFloat(double.PositiveInfinity);
                            maximum = Math.Max(maximum, magnitude);
                            ConsumeInstructionBudget();
                        }
                        if (maximum == 0.0) return CreateFloat(0.0);
                        var sum = 0.0;
                        for (var index = 0; index < positionalCount; index++)
                        {
                            var scaled = GetMathNumber(GetTupleItem(arguments, index), builtin) / maximum;
                            sum += scaled * scaled;
                            ConsumeInstructionBudget();
                        }
                        return CreateFloat(maximum * Math.Sqrt(sum));
                    }

                case VmBuiltin.MathGcd:
                case VmBuiltin.MathLcm:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    {
                        var result = builtin == VmBuiltin.MathGcd ? BigInteger.Zero : BigInteger.One;
                        for (var index = 0; index < positionalCount; index++)
                        {
                            var current = BigInteger.Abs(GetMathInteger(GetTupleItem(arguments, index), GetBuiltinName(builtin)));
                            if (builtin == VmBuiltin.MathGcd)
                            {
                                result = BigInteger.GreatestCommonDivisor(result, current);
                            }
                            else
                            {
                                result = result.IsZero || current.IsZero
                                    ? BigInteger.Zero
                                    : BigInteger.Abs((result / BigInteger.GreatestCommonDivisor(result, current)) * current);
                                EnsureIntegerSize(result);
                            }
                            ConsumeInstructionBudget();
                        }
                        return CreateInteger(result);
                    }

                case VmBuiltin.MathFactorial:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, 1, 1);
                    {
                        var number = GetMathInteger(GetTupleItem(arguments, 0), "factorial");
                        if (number < 0) Raise("ValueError", "factorial() not defined for negative values");
                        var result = BigInteger.One;
                        for (var factor = BigInteger.One; factor <= number; factor++)
                        {
                            result *= factor;
                            EnsureIntegerSize(result);
                            ConsumeInstructionBudget();
                        }
                        return CreateInteger(result);
                    }

                case VmBuiltin.MathComb:
                case VmBuiltin.MathPerm:
                    RequireNoKeywordArguments(builtin, keywordNames);
                    RequireArgumentCount(builtin, positionalCount, builtin == VmBuiltin.MathComb ? 2 : 1, 2);
                    {
                        var name = GetBuiltinName(builtin);
                        var n = GetMathInteger(GetTupleItem(arguments, 0), name);
                        var k = positionalCount == 2 ? GetMathInteger(GetTupleItem(arguments, 1), name) : n;
                        if (n < 0 || k < 0) Raise("ValueError", $"{name}() arguments must be non-negative");
                        if (k > n) return VmValue.FromSmallInteger(0);
                        var result = BigInteger.One;
                        if (builtin == VmBuiltin.MathComb)
                        {
                            k = BigInteger.Min(k, n - k);
                            for (var index = BigInteger.One; index <= k; index++)
                            {
                                result = (result * (n - k + index)) / index;
                                EnsureIntegerSize(result);
                                ConsumeInstructionBudget();
                            }
                        }
                        else
                        {
                            for (var index = BigInteger.Zero; index < k; index++)
                            {
                                result *= n - index;
                                EnsureIntegerSize(result);
                                ConsumeInstructionBudget();
                            }
                        }
                        return CreateInteger(result);
                    }

                case VmBuiltin.MathProd:
                    RequireOnlyKeywords(keywordNames, "start");
                    if (positionalCount is < 1 or > 2)
                        Raise("TypeError", $"prod() expected 1 or 2 arguments, got {positionalCount}");
                    {
                        var total = positionalCount == 2 ? GetTupleItem(arguments, 1) : VmValue.FromSmallInteger(1);
                        if (TryGetKeywordArgument(arguments, positionalCount, keywordNames, "start", out var start))
                        {
                            if (positionalCount == 2) Raise("TypeError", "prod() got multiple values for argument 'start'");
                            total = start;
                        }
                        var iterator = CreateIterator(GetTupleItem(arguments, 0));
                        while (IteratorMoveNext(iterator, out var item))
                        {
                            total = BinaryOperation(PythonBinaryOperation.Multiply, total, item);
                            ConsumeInstructionBudget();
                        }
                        return total;
                    }

                case VmBuiltin.MathIsClose:
                    RequireOnlyKeywords(keywordNames, "rel_tol", "abs_tol");
                    if (positionalCount != 2)
                        Raise("TypeError", $"isclose() expected 2 arguments, got {positionalCount}");
                    {
                        var left = GetMathNumber(GetTupleItem(arguments, 0), builtin);
                        var right = GetMathNumber(GetTupleItem(arguments, 1), builtin);
                        var relativeTolerance = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "rel_tol", out var relativeValue)
                            ? GetMathNumber(relativeValue, builtin) : 1e-9;
                        var absoluteTolerance = TryGetKeywordArgument(arguments, positionalCount, keywordNames, "abs_tol", out var absoluteValue)
                            ? GetMathNumber(absoluteValue, builtin) : 0.0;
                        if (relativeTolerance < 0.0 || absoluteTolerance < 0.0)
                            Raise("ValueError", "tolerances must be non-negative");
                        if (left == right) return VmValue.True;
                        if (double.IsInfinity(left) || double.IsInfinity(right)) return VmValue.False;
                        var difference = Math.Abs(left - right);
                        return difference <= Math.Max(relativeTolerance * Math.Max(Math.Abs(left), Math.Abs(right)), absoluteTolerance)
                            ? VmValue.True : VmValue.False;
                    }

                default:
                    throw new VmTrapException(
                        VmStopReason.SecurityViolation,
                        $"Math capability {builtin} is not installed.");
            }
        }

        private VmValue CallMathBuiltin(VmBuiltin builtin, VmValue arguments, int argumentCount)
        {
            switch (builtin)
            {
                case VmBuiltin.MathFloor:
                case VmBuiltin.MathCeil:
                case VmBuiltin.MathTrunc:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    {
                        var value = GetTupleItem(arguments, 0);
                        if (IsInteger(value))
                            return CreateInteger(GetInteger(value));
                        var number = GetMathNumber(value, builtin);
                        if (double.IsNaN(number))
                            Raise("ValueError", $"cannot convert float NaN to integer in {GetBuiltinName(builtin)}()");
                        if (double.IsInfinity(number))
                            Raise("OverflowError", $"cannot convert float infinity to integer in {GetBuiltinName(builtin)}()");
                        var integral = builtin switch
                        {
                            VmBuiltin.MathFloor => Math.Floor(number),
                            VmBuiltin.MathCeil => Math.Ceiling(number),
                            _ => Math.Truncate(number),
                        };
                        return CreateInteger(new BigInteger(integral));
                    }

                case VmBuiltin.MathIsFinite:
                case VmBuiltin.MathIsInf:
                case VmBuiltin.MathIsNaN:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    {
                        var number = GetMathNumber(GetTupleItem(arguments, 0), builtin);
                        var result = builtin switch
                        {
                            VmBuiltin.MathIsFinite => double.IsFinite(number),
                            VmBuiltin.MathIsInf => double.IsInfinity(number),
                            _ => double.IsNaN(number),
                        };
                        return result ? VmValue.True : VmValue.False;
                    }

                case VmBuiltin.MathCopySign:
                case VmBuiltin.MathFmod:
                case VmBuiltin.MathPow:
                case VmBuiltin.MathAtan2:
                    RequireArgumentCount(builtin, argumentCount, 2, 2);
                    {
                        var left = GetMathNumber(GetTupleItem(arguments, 0), builtin);
                        var right = GetMathNumber(GetTupleItem(arguments, 1), builtin);
                        double result;
                        switch (builtin)
                        {
                            case VmBuiltin.MathCopySign:
                                result = Math.CopySign(left, right);
                                break;
                            case VmBuiltin.MathFmod:
                                result = left % right;
                                if (double.IsNaN(result) && !double.IsNaN(left) && !double.IsNaN(right))
                                    Raise("ValueError", "math domain error");
                                break;
                            case VmBuiltin.MathPow:
                                if (left == 0.0 && right < 0.0)
                                    Raise("ValueError", "math domain error");
                                result = Math.Pow(left, right);
                                if (double.IsNaN(result) && !double.IsNaN(left) && !double.IsNaN(right))
                                    Raise("ValueError", "math domain error");
                                if (double.IsInfinity(result) && double.IsFinite(left) && double.IsFinite(right))
                                    Raise("OverflowError", "math range error");
                                break;
                            default:
                                result = Math.Atan2(left, right);
                                break;
                        }
                        return CreateFloat(result);
                    }

                case VmBuiltin.MathLog:
                    RequireArgumentCount(builtin, argumentCount, 1, 2);
                    {
                        var number = GetMathNumber(GetTupleItem(arguments, 0), builtin);
                        if (number <= 0.0)
                            Raise("ValueError", "math domain error");
                        var result = Math.Log(number);
                        if (argumentCount == 2)
                        {
                            var @base = GetMathNumber(GetTupleItem(arguments, 1), builtin);
                            if (@base <= 0.0)
                                Raise("ValueError", "math domain error");
                            if (@base == 1.0)
                                Raise("ZeroDivisionError", "float division by zero");
                            result /= Math.Log(@base);
                        }
                        return CreateFloat(result);
                    }

                default:
                    RequireArgumentCount(builtin, argumentCount, 1, 1);
                    {
                        var number = GetMathNumber(GetTupleItem(arguments, 0), builtin);
                        double result;
                        switch (builtin)
                        {
                            case VmBuiltin.MathSqrt:
                                if (number < 0.0)
                                    Raise("ValueError", "math domain error");
                                result = Math.Sqrt(number);
                                break;
                            case VmBuiltin.MathFabs:
                                result = Math.Abs(number);
                                break;
                            case VmBuiltin.MathSin:
                                result = Math.Sin(number);
                                break;
                            case VmBuiltin.MathCos:
                                result = Math.Cos(number);
                                break;
                            case VmBuiltin.MathTan:
                                result = Math.Tan(number);
                                break;
                            case VmBuiltin.MathAsin:
                                if (number is < -1.0 or > 1.0)
                                    Raise("ValueError", "math domain error");
                                result = Math.Asin(number);
                                break;
                            case VmBuiltin.MathAcos:
                                if (number is < -1.0 or > 1.0)
                                    Raise("ValueError", "math domain error");
                                result = Math.Acos(number);
                                break;
                            case VmBuiltin.MathAtan:
                                result = Math.Atan(number);
                                break;
                            case VmBuiltin.MathExp:
                                result = Math.Exp(number);
                                if (double.IsInfinity(result) && double.IsFinite(number))
                                    Raise("OverflowError", "math range error");
                                break;
                            case VmBuiltin.MathLog2:
                                if (number <= 0.0)
                                    Raise("ValueError", "math domain error");
                                result = Math.Log2(number);
                                break;
                            case VmBuiltin.MathLog10:
                                if (number <= 0.0)
                                    Raise("ValueError", "math domain error");
                                result = Math.Log10(number);
                                break;
                            default:
                                throw new VmTrapException(
                                    VmStopReason.SecurityViolation,
                                    $"Math capability {builtin} is not installed.");
                        }

                        if (double.IsNaN(result) && !double.IsNaN(number))
                            Raise("ValueError", "math domain error");
                        return CreateFloat(result);
                    }
            }
        }

        private double GetMathNumber(VmValue value, VmBuiltin builtin)
        {
            if (!IsInteger(value) && !IsFloat(value))
                Raise("TypeError", $"must be real number, not {GetTypeName(value)} in {GetBuiltinName(builtin)}()");
            var result = GetFloat(value);
            if (IsInteger(value) && double.IsInfinity(result))
                Raise("OverflowError", "int too large to convert to float");
            return result;
        }

        private VmValue CallBoundMethod(
            VmValue method,
            VmValue arguments,
            int positionalCount,
            VmValue keywordNames)
        {
            RequireObjectType(method, VmObjectType.BoundMethod);
            if (!keywordNames.IsNone && GetTupleCount(keywordNames) != 0)
                Raise("TypeError", "keyword arguments are not supported for this built-in method");
            if (positionalCount != GetTupleCount(arguments))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Bound-method argument layout is inconsistent.");
            var payload = _memory.GetObjectPayloadAddress(method);
            var receiver = _memory.ReadValue(payload);
            var methodId = (VmBoundMethod)_memory.ReadInt32(payload + 8);
            var count = GetTupleCount(arguments);

            switch (methodId)
            {
                case VmBoundMethod.ListAppend:
                    RequireMethodArgumentCount("list.append", count, 1, 1);
                    ListAdd(receiver, GetTupleItem(arguments, 0));
                    return VmValue.None;

                case VmBoundMethod.ListExtend:
                    RequireMethodArgumentCount("list.extend", count, 1, 1);
                    ExtendList(receiver, GetTupleItem(arguments, 0));
                    return VmValue.None;

                case VmBoundMethod.ListPop:
                    RequireMethodArgumentCount("list.pop", count, 0, 1);
                    return ListPop(receiver, count == 0 ? -1 : GetIndex(GetTupleItem(arguments, 0)));

                case VmBoundMethod.DictionaryGet:
                    RequireMethodArgumentCount("dict.get", count, 1, 2);
                    return DictionaryTryGet(receiver, GetTupleItem(arguments, 0), out var value)
                        ? value
                        : count == 2 ? GetTupleItem(arguments, 1) : VmValue.None;

                case VmBoundMethod.DictionaryKeys:
                    RequireMethodArgumentCount("dict.keys", count, 0, 0);
                    return DictionaryKeys(receiver, values: false);

                case VmBoundMethod.DictionaryValues:
                    RequireMethodArgumentCount("dict.values", count, 0, 0);
                    return DictionaryKeys(receiver, values: true);

                case VmBoundMethod.SetAdd:
                    RequireMethodArgumentCount("set.add", count, 1, 1);
                    SetAdd(receiver, GetTupleItem(arguments, 0));
                    return VmValue.None;

                case VmBoundMethod.SetDiscard:
                    RequireMethodArgumentCount("set.discard", count, 1, 1);
                    _ = DictionaryDelete(GetSetDictionary(receiver), GetTupleItem(arguments, 0));
                    return VmValue.None;

                case VmBoundMethod.StringStartsWith:
                    RequireMethodArgumentCount("str.startswith", count, 1, 1);
                    return GetString(receiver).StartsWith(
                        GetStringArgument(GetTupleItem(arguments, 0), "startswith"),
                        StringComparison.Ordinal)
                        ? VmValue.True : VmValue.False;

                case VmBoundMethod.StringEndsWith:
                    RequireMethodArgumentCount("str.endswith", count, 1, 1);
                    return GetString(receiver).EndsWith(
                        GetStringArgument(GetTupleItem(arguments, 0), "endswith"),
                        StringComparison.Ordinal)
                        ? VmValue.True : VmValue.False;

                case VmBoundMethod.GeneratorIter:
                    RequireMethodArgumentCount("generator.__iter__", count, 0, 0);
                    return receiver;

                case VmBoundMethod.GeneratorNext:
                    RequireMethodArgumentCount("generator.__next__", count, 0, 0);
                    if (ResumeGenerator(receiver, out var nextValue))
                        return nextValue;
                    Raise("StopIteration", string.Empty);
                    return VmValue.None;

                case VmBoundMethod.GeneratorSend:
                    RequireMethodArgumentCount("generator.send", count, 1, 1);
                    if (ResumeGenerator(receiver, GetTupleItem(arguments, 0), out var sentValue))
                        return sentValue;
                    Raise("StopIteration", string.Empty);
                    return VmValue.None;

                case VmBoundMethod.IteratorIter:
                    RequireMethodArgumentCount("iterator.__iter__", count, 0, 0);
                    return receiver;

                case VmBoundMethod.IteratorNext:
                    RequireMethodArgumentCount("iterator.__next__", count, 0, 0);
                    if (IteratorMoveNext(receiver, out var iteratorValue))
                        return iteratorValue;
                    Raise("StopIteration", string.Empty);
                    return VmValue.None;

                case VmBoundMethod.PropertyGetter:
                case VmBoundMethod.PropertySetter:
                case VmBoundMethod.PropertyDeleter:
                    RequireMethodArgumentCount(
                        methodId == VmBoundMethod.PropertyGetter ? "property.getter" :
                        methodId == VmBoundMethod.PropertySetter ? "property.setter" : "property.deleter",
                        count,
                        1,
                        1);
                    {
                        var descriptorPayload = _memory.GetObjectPayloadAddress(receiver);
                        var getter = _memory.ReadValue(descriptorPayload);
                        var setter = _memory.ReadValue(descriptorPayload + 8);
                        var deleter = _memory.ReadValue(descriptorPayload + 16);
                        var doc = _memory.ReadValue(descriptorPayload + 24);
                        var replacement = GetTupleItem(arguments, 0);
                        return methodId switch
                        {
                            VmBoundMethod.PropertyGetter => CreateProperty(replacement, setter, deleter, doc),
                            VmBoundMethod.PropertySetter => CreateProperty(getter, replacement, deleter, doc),
                            _ => CreateProperty(getter, setter, replacement, doc),
                        };
                    }

                default:
                    throw new VmTrapException(VmStopReason.UnsupportedOpcode, $"Bound method {methodId} is not implemented.");
            }
        }

        private void InstallBuiltins()
        {
            AddBuiltin("print", VmBuiltin.Print);
            AddBuiltin("len", VmBuiltin.Len);
            AddBuiltin("range", VmBuiltin.Range);
            AddBuiltin("list", VmBuiltin.List);
            AddBuiltin("tuple", VmBuiltin.Tuple);
            AddBuiltin("set", VmBuiltin.Set);
            AddBuiltin("dict", VmBuiltin.Dict);
            AddBuiltin("bool", VmBuiltin.Bool);
            AddBuiltin("int", VmBuiltin.Int);
            AddBuiltin("str", VmBuiltin.Str);
            AddBuiltin("all", VmBuiltin.All);
            AddBuiltin("any", VmBuiltin.Any);
            AddBuiltin("abs", VmBuiltin.Abs);
            AddBuiltin("ascii", VmBuiltin.Ascii);
            AddBuiltin("bin", VmBuiltin.Bin);
            AddBuiltin("bytes", VmBuiltin.Bytes);
            AddBuiltin("callable", VmBuiltin.Callable);
            AddBuiltin("chr", VmBuiltin.Chr);
            AddBuiltin("classmethod", VmBuiltin.ClassMethod);
            AddBuiltin("complex", VmBuiltin.Complex);
            AddBuiltin("delattr", VmBuiltin.DelAttr);
            AddBuiltin("dir", VmBuiltin.Dir);
            AddBuiltin("divmod", VmBuiltin.DivMod);
            AddBuiltin("enumerate", VmBuiltin.Enumerate);
            AddBuiltin("filter", VmBuiltin.Filter);
            AddBuiltin("float", VmBuiltin.Float);
            AddBuiltin("format", VmBuiltin.Format);
            AddBuiltin("frozenset", VmBuiltin.FrozenSet);
            AddBuiltin("getattr", VmBuiltin.GetAttr);
            AddBuiltin("globals", VmBuiltin.Globals);
            AddBuiltin("hasattr", VmBuiltin.HasAttr);
            AddBuiltin("hash", VmBuiltin.Hash);
            AddBuiltin("hex", VmBuiltin.Hex);
            AddBuiltin("id", VmBuiltin.Id);
            AddBuiltin("isinstance", VmBuiltin.IsInstance);
            AddBuiltin("issubclass", VmBuiltin.IsSubclass);
            AddBuiltin("locals", VmBuiltin.Locals);
            AddBuiltin("map", VmBuiltin.Map);
            AddBuiltin("max", VmBuiltin.Max);
            AddBuiltin("min", VmBuiltin.Min);
            AddBuiltin("oct", VmBuiltin.Oct);
            AddBuiltin("ord", VmBuiltin.Ord);
            AddBuiltin("pow", VmBuiltin.Pow);
            AddBuiltin("property", VmBuiltin.Property);
            AddBuiltin("repr", VmBuiltin.Repr);
            AddBuiltin("reversed", VmBuiltin.Reversed);
            AddBuiltin("round", VmBuiltin.Round);
            AddBuiltin("setattr", VmBuiltin.SetAttr);
            AddBuiltin("slice", VmBuiltin.Slice);
            AddBuiltin("sorted", VmBuiltin.Sorted);
            AddBuiltin("staticmethod", VmBuiltin.StaticMethod);
            AddBuiltin("sum", VmBuiltin.Sum);
            AddBuiltin("vars", VmBuiltin.Vars);
            AddBuiltin("zip", VmBuiltin.Zip);
            AddBuiltin("iter", VmBuiltin.Iter);
            AddBuiltin("next", VmBuiltin.Next);
            AddBuiltin("__import__", VmBuiltin.Import);
            AddBuiltin("__build_class__", VmBuiltin.BuildClass);
            AddBuiltin("super", VmBuiltin.Super);
            AddBuiltin("BaseException", VmBuiltin.BaseException);
            AddBuiltin("Exception", VmBuiltin.Exception);
            AddBuiltin("TypeError", VmBuiltin.TypeError);
            AddBuiltin("ValueError", VmBuiltin.ValueError);
            AddBuiltin("RuntimeError", VmBuiltin.RuntimeError);
            AddBuiltin("AssertionError", VmBuiltin.AssertionError);
            AddBuiltin("NotImplementedError", VmBuiltin.NotImplementedError);
            AddBuiltin("KeyError", VmBuiltin.KeyError);
            AddBuiltin("IndexError", VmBuiltin.IndexError);
            AddBuiltin("NameError", VmBuiltin.NameError);
            AddBuiltin("UnboundLocalError", VmBuiltin.UnboundLocalError);
            AddBuiltin("StopIteration", VmBuiltin.StopIteration);
            AddBuiltin("ZeroDivisionError", VmBuiltin.ZeroDivisionError);
            AddBuiltin("ArithmeticError", VmBuiltin.ArithmeticError);
            AddBuiltin("LookupError", VmBuiltin.LookupError);
            AddBuiltin("AttributeError", VmBuiltin.AttributeError);
            AddBuiltin("ImportError", VmBuiltin.ImportError);
            AddBuiltin("ModuleNotFoundError", VmBuiltin.ModuleNotFoundError);
            AddBuiltin("OverflowError", VmBuiltin.OverflowError);
            AddBuiltin("SystemError", VmBuiltin.SystemError);
            DictionarySet(_builtins, CreateString("None"), VmValue.None, rejectDuplicate: false);
            DictionarySet(_builtins, CreateString("True"), VmValue.True, rejectDuplicate: false);
            DictionarySet(_builtins, CreateString("False"), VmValue.False, rejectDuplicate: false);
        }

        private void AddBuiltin(string name, VmBuiltin builtin)
        {
            DictionarySet(
                _builtins,
                CreateString(name),
                VmValue.FromBuiltin(builtin),
                rejectDuplicate: false);
        }

        private VmValue LoadCommonConstant(int operand)
        {
            return operand switch
            {
                0 => VmValue.FromBuiltin(VmBuiltin.AssertionError),
                1 => VmValue.FromBuiltin(VmBuiltin.NotImplementedError),
                2 => VmValue.FromBuiltin(VmBuiltin.Tuple),
                3 => VmValue.FromBuiltin(VmBuiltin.All),
                4 => VmValue.FromBuiltin(VmBuiltin.Any),
                _ => throw new VmTrapException(VmStopReason.InvalidBytecode, "LOAD_COMMON_CONSTANT index is out of range."),
            };
        }

        private void AppendOutput(string text)
        {
            var bytes = Encoding.UTF8.GetByteCount(text);
            if ((long)_outputBytes + bytes > _limits.MaxOutputBytes)
                throw new VmTrapException(VmStopReason.OutputLimitExceeded, "Python output limit exceeded.");
            _combinedCancellation.ThrowIfCancellationRequested();
            _outputBytes += bytes;
            try
            {
                _standardOutput.Write(text);
                _combinedCancellation.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (_combinedCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new VmTrapException(VmStopReason.OutputFailure,
                    $"Python output writer failed: {exception.Message}");
            }
        }

        private VmValue UnaryNegative(VmValue value)
        {
            if (IsInteger(value))
                return CreateInteger(-GetInteger(value));
            if (IsFloat(value))
                return CreateFloat(-GetFloat(value));
            if (value.IsAddress && _memory.GetObjectType(value) == VmObjectType.Complex)
            {
                var payload = _memory.GetObjectPayloadAddress(value);
                return CreateComplex(-_memory.ReadDouble(payload), -_memory.ReadDouble(payload + 8));
            }
            Raise("TypeError", $"bad operand type for unary -: '{GetTypeName(value)}'");
            return VmValue.None;
        }

        private VmValue BinaryOperation(PythonBinaryOperation operation, VmValue left, VmValue right)
        {
            if ((int)operation >= (int)PythonBinaryOperation.InPlaceAdd &&
                (int)operation <= (int)PythonBinaryOperation.InPlaceXor)
                operation = (PythonBinaryOperation)((int)operation - (int)PythonBinaryOperation.InPlaceAdd);
            if (operation == PythonBinaryOperation.Subscript)
                return LoadSubscript(left, right);

            if (IsInteger(left) && IsInteger(right))
                return BinaryIntegerOperation(operation, GetInteger(left), GetInteger(right));
            if ((IsInteger(left) || IsFloat(left)) && (IsInteger(right) || IsFloat(right)))
                return BinaryFloatOperation(operation, GetFloat(left), GetFloat(right));

            if (operation == PythonBinaryOperation.Add)
            {
                if (IsObjectType(left, VmObjectType.String) && IsObjectType(right, VmObjectType.String))
                    return ConcatenateStrings(left, right);
                if (IsObjectType(left, VmObjectType.List) && IsObjectType(right, VmObjectType.List))
                    return ConcatenateLists(left, right);
                if (IsObjectType(left, VmObjectType.Tuple) && IsObjectType(right, VmObjectType.Tuple))
                    return ConcatenateTuples(left, right);
                if (IsObjectType(left, VmObjectType.Template) && IsObjectType(right, VmObjectType.Template))
                    return ConcatenateTemplates(left, right);
            }

            if (operation == PythonBinaryOperation.Multiply)
            {
                if (IsInteger(right))
                    return RepeatSequence(left, GetInteger(right));
                if (IsInteger(left))
                    return RepeatSequence(right, GetInteger(left));
            }

            Raise(
                "TypeError",
                $"unsupported operand type(s) for {GetBinaryOperatorText(operation)}: '{GetTypeName(left)}' and '{GetTypeName(right)}'");
            return VmValue.None;
        }

        private VmValue BinaryIntegerOperation(
            PythonBinaryOperation operation,
            BigInteger left,
            BigInteger right)
        {
            BigInteger result;
            switch (operation)
            {
                case PythonBinaryOperation.Add:
                    result = left + right;
                    break;
                case PythonBinaryOperation.Subtract:
                    result = left - right;
                    break;
                case PythonBinaryOperation.Multiply:
                    result = MultiplyIntegersWithLimit(left, right);
                    break;
                case PythonBinaryOperation.FloorDivide:
                    if (right.IsZero)
                        Raise("ZeroDivisionError", "integer division or modulo by zero");
                    result = FloorDivide(left, right);
                    break;
                case PythonBinaryOperation.Remainder:
                    if (right.IsZero)
                        Raise("ZeroDivisionError", "integer modulo by zero");
                    result = left - FloorDivide(left, right) * right;
                    break;
                case PythonBinaryOperation.TrueDivide:
                    if (right.IsZero)
                        Raise("ZeroDivisionError", "division by zero");
                    return CreateFloat((double)left / (double)right);
                case PythonBinaryOperation.And:
                    result = left & right;
                    break;
                case PythonBinaryOperation.Or:
                    result = left | right;
                    break;
                case PythonBinaryOperation.Xor:
                    result = left ^ right;
                    break;
                case PythonBinaryOperation.LeftShift:
                    {
                        var shift = GetShiftCount(right);
                        if (!left.IsZero)
                            EnsureProspectiveIntegerBits(checked(GetBitLength(left) + shift));
                        result = left << shift;
                        break;
                    }
                case PythonBinaryOperation.RightShift:
                    result = left >> GetShiftCount(right);
                    break;
                case PythonBinaryOperation.Power:
                    {
                        if (right.Sign < 0)
                            return CreateFloat(Math.Pow((double)left, (double)right));
                        if (right > int.MaxValue)
                        {
                            if (left.IsZero)
                                result = BigInteger.Zero;
                            else if (left.IsOne)
                                result = BigInteger.One;
                            else if (left == BigInteger.MinusOne)
                                result = right.IsEven ? BigInteger.One : BigInteger.MinusOne;
                            else
                                throw new VmTrapException(VmStopReason.IntegerLimitExceeded, "Integer exponent exceeds the VM limit.");
                            break;
                        }
                        result = PowIntegerWithLimit(left, (int)right);
                        break;
                    }
                case PythonBinaryOperation.MatrixMultiply:
                    Raise("TypeError", "matrix multiplication is not supported by core numeric types");
                    return VmValue.None;
                default:
                    throw new VmTrapException(VmStopReason.UnsupportedOpcode, $"Integer operation {operation} is not implemented.");
            }
            return CreateInteger(result);
        }

        private BigInteger MultiplyIntegersWithLimit(BigInteger left, BigInteger right)
        {
            var result = left * right;
            EnsureIntegerSize(result);
            return result;
        }

        private BigInteger PowIntegerWithLimit(BigInteger value, int exponent)
        {
            if (exponent < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative exponent reached integer exponentiation.");
            if (exponent == 0)
                return BigInteger.One;
            if (value.IsZero || value.IsOne)
                return value;
            if (value == BigInteger.MinusOne)
                return (exponent & 1) == 0 ? BigInteger.One : BigInteger.MinusOne;

            var result = BigInteger.One;
            var factor = value;
            var remaining = exponent;
            while (remaining != 0)
            {
                if ((remaining & 1) != 0)
                    result = MultiplyIntegersWithLimit(result, factor);
                remaining >>= 1;
                if (remaining != 0)
                    factor = MultiplyIntegersWithLimit(factor, factor);
                PollCancellation();
            }
            return result;
        }

        private VmValue BinaryFloatOperation(PythonBinaryOperation operation, double left, double right)
        {
            return operation switch
            {
                PythonBinaryOperation.Add => CreateFloat(left + right),
                PythonBinaryOperation.Subtract => CreateFloat(left - right),
                PythonBinaryOperation.Multiply => CreateFloat(left * right),
                PythonBinaryOperation.TrueDivide => right == 0.0
                    ? RaiseValue("ZeroDivisionError", "float division by zero")
                    : CreateFloat(left / right),
                PythonBinaryOperation.FloorDivide => right == 0.0
                    ? RaiseValue("ZeroDivisionError", "float floor division by zero")
                    : CreateFloat(Math.Floor(left / right)),
                PythonBinaryOperation.Remainder => right == 0.0
                    ? RaiseValue("ZeroDivisionError", "float modulo")
                    : CreateFloat(left - Math.Floor(left / right) * right),
                PythonBinaryOperation.Power => CreateFloat(Math.Pow(left, right)),
                _ => RaiseValue(
                    "TypeError",
                    $"unsupported numeric operation {GetBinaryOperatorText(operation)} for float"),
            };
        }

        private bool Compare(int selector, VmValue left, VmValue right)
        {
            if ((uint)selector > 5)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "COMPARE_OP selector is out of range.");
            if (selector is 2 or 3)
            {
                var equal = ValuesEqual(left, right);
                return selector == 2 ? equal : !equal;
            }

            int? comparison;
            if ((IsInteger(left) || IsFloat(left)) && (IsInteger(right) || IsFloat(right)))
            {
                comparison = CompareNumbers(left, right);
            }
            else if (IsObjectType(left, VmObjectType.String) && IsObjectType(right, VmObjectType.String))
            {
                comparison = CompareBytePayload(left, right);
            }
            else if (IsObjectType(left, VmObjectType.Bytes) && IsObjectType(right, VmObjectType.Bytes))
            {
                comparison = CompareBytePayload(left, right);
            }
            else
            {
                var leftMethod = selector switch
                {
                    0 => "__lt__",
                    1 => "__le__",
                    4 => "__gt__",
                    5 => "__ge__",
                    _ => string.Empty,
                };
                var rightMethod = selector switch
                {
                    0 => "__gt__",
                    1 => "__ge__",
                    4 => "__lt__",
                    5 => "__le__",
                    _ => string.Empty,
                };
                if (IsObjectType(left, VmObjectType.Instance) && HasSpecialMethod(left, leftMethod))
                    return IsTruthy(CallBinarySpecialMethod(left, leftMethod, right));
                if (IsObjectType(right, VmObjectType.Instance) && HasSpecialMethod(right, rightMethod))
                    return IsTruthy(CallBinarySpecialMethod(right, rightMethod, left));
                Raise("TypeError", $"'<' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'");
                return false;
            }

            if (!comparison.HasValue)
                return false;
            return selector switch
            {
                0 => comparison.Value < 0,
                1 => comparison.Value <= 0,
                4 => comparison.Value > 0,
                5 => comparison.Value >= 0,
                _ => false,
            };
        }

        private int? CompareNumbers(VmValue left, VmValue right)
        {
            if (IsInteger(left) && IsInteger(right))
                return GetInteger(left).CompareTo(GetInteger(right));
            if (IsInteger(left))
                return CompareIntegerToFloat(GetInteger(left), GetFloat(right));
            if (IsInteger(right))
            {
                var comparison = CompareIntegerToFloat(GetInteger(right), GetFloat(left));
                return comparison.HasValue ? -comparison.Value : null;
            }

            var leftFloat = GetFloat(left);
            var rightFloat = GetFloat(right);
            if (double.IsNaN(leftFloat) || double.IsNaN(rightFloat))
                return null;
            return leftFloat.CompareTo(rightFloat);
        }

        private static int? CompareIntegerToFloat(BigInteger integer, double number)
        {
            if (double.IsNaN(number))
                return null;
            if (double.IsPositiveInfinity(number))
                return -1;
            if (double.IsNegativeInfinity(number))
                return 1;

            var truncated = new BigInteger(number);
            var comparison = integer.CompareTo(truncated);
            if (comparison != 0)
                return comparison;
            if (number > 0.0 && number != Math.Truncate(number))
                return -1;
            if (number < 0.0 && number != Math.Truncate(number))
                return 1;
            return 0;
        }

        private int CompareBytePayload(VmValue left, VmValue right)
        {
            var leftLength = _memory.GetObjectAux0(left);
            var rightLength = _memory.GetObjectAux0(right);
            return _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(left), leftLength)
                .SequenceCompareTo(_memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(right), rightLength));
        }

        private bool ValuesEqual(VmValue left, VmValue right)
        {
            return ValuesEqual(left, right, 0);
        }

        private bool ValuesEqual(VmValue left, VmValue right, int depth)
        {
            if (left == right)
            {
                if (IsFloat(left))
                {
                    var number = GetFloat(left);
                    return !double.IsNaN(number);
                }
                if (IsObjectType(left, VmObjectType.Complex))
                {
                    var payload = _memory.GetObjectPayloadAddress(left);
                    var real = _memory.ReadDouble(payload);
                    var imaginary = _memory.ReadDouble(payload + 8);
                    return !double.IsNaN(real) && !double.IsNaN(imaginary);
                }
                return true;
            }
            if (IsInteger(left) && IsInteger(right))
                return GetInteger(left) == GetInteger(right);
            if (IsInteger(left) && IsFloat(right))
                return IntegerEqualsFloat(GetInteger(left), GetFloat(right));
            if (IsFloat(left) && IsInteger(right))
                return IntegerEqualsFloat(GetInteger(right), GetFloat(left));
            if (IsFloat(left) && IsFloat(right))
                return GetFloat(left) == GetFloat(right);
            if (IsObjectType(left, VmObjectType.Instance) && HasSpecialMethod(left, "__eq__"))
                return IsTruthy(CallBinarySpecialMethod(left, "__eq__", right));
            if (IsObjectType(right, VmObjectType.Instance) && HasSpecialMethod(right, "__eq__"))
                return IsTruthy(CallBinarySpecialMethod(right, "__eq__", left));
            if (!left.IsAddress || !right.IsAddress)
                return false;
            var leftType = _memory.GetObjectType(left);
            var rightType = _memory.GetObjectType(right);
            if (leftType is VmObjectType.Set or VmObjectType.FrozenSet &&
                rightType is VmObjectType.Set or VmObjectType.FrozenSet)
            {
                return SetEquals(left, right);
            }
            if (leftType != rightType)
                return false;
            if (depth >= Math.Min(_limits.MaxCallDepth, 64))
                return false;

            switch (leftType)
            {
                case VmObjectType.String:
                case VmObjectType.Bytes:
                    {
                        var leftLength = _memory.GetObjectAux0(left);
                        var rightLength = _memory.GetObjectAux0(right);
                        return leftLength == rightLength &&
                            _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(left), leftLength)
                                .SequenceEqual(_memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(right), rightLength));
                    }
                case VmObjectType.Float:
                    return GetFloat(left) == GetFloat(right);
                case VmObjectType.Complex:
                    {
                        var leftPayload = _memory.GetObjectPayloadAddress(left);
                        var rightPayload = _memory.GetObjectPayloadAddress(right);
                        return _memory.ReadDouble(leftPayload) == _memory.ReadDouble(rightPayload) &&
                            _memory.ReadDouble(leftPayload + 8) == _memory.ReadDouble(rightPayload + 8);
                    }
                case VmObjectType.Tuple:
                    {
                        var count = GetTupleCount(left);
                        if (count != GetTupleCount(right))
                            return false;
                        for (var index = 0; index < count; index++)
                        {
                            if (!ValuesEqual(GetTupleItem(left, index), GetTupleItem(right, index), depth + 1))
                                return false;
                            PollCancellation();
                        }
                        return true;
                    }
                case VmObjectType.List:
                    {
                        var count = GetListCount(left);
                        if (count != GetListCount(right))
                            return false;
                        for (var index = 0; index < count; index++)
                        {
                            if (!ValuesEqual(GetListItem(left, index), GetListItem(right, index), depth + 1))
                                return false;
                            PollCancellation();
                        }
                        return true;
                    }
                case VmObjectType.Dictionary:
                    return DictionariesEqual(left, right, depth + 1);
                default:
                    return false;
            }
        }

        private static bool IntegerEqualsFloat(BigInteger integer, double number)
        {
            return double.IsFinite(number) &&
                Math.Truncate(number) == number &&
                integer == new BigInteger(number);
        }

        private bool DictionariesEqual(VmValue left, VmValue right, int depth)
        {
            if (GetDictionaryCount(left) != GetDictionaryCount(right))
                return false;
            var payload = _memory.GetObjectPayloadAddress(left);
            var capacity = _memory.ReadInt32(payload + 12);
            var entries = GetDictionaryEntriesPayload(left);
            for (var slot = 0; slot < capacity; slot++)
            {
                var entry = entries + slot * DictionaryEntrySize;
                var key = _memory.ReadValue(entry + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                if (!DictionaryTryGet(right, key, out var rightValue) ||
                    !ValuesEqual(_memory.ReadValue(entry + 16), rightValue, depth))
                {
                    return false;
                }
                PollCancellation();
            }
            return true;
        }

        private static BigInteger FloorDivide(BigInteger left, BigInteger right)
        {
            var quotient = BigInteger.DivRem(left, right, out var remainder);
            if (!remainder.IsZero && left.Sign != right.Sign)
                quotient -= BigInteger.One;
            return quotient;
        }

        private VmValue LoadSubscript(VmValue container, VmValue key)
        {
            if (!container.IsAddress)
                Raise("TypeError", $"'{GetTypeName(container)}' object is not subscriptable");
            switch (_memory.GetObjectType(container))
            {
                case VmObjectType.List:
                    return IsObjectType(key, VmObjectType.Slice)
                        ? SliceList(container, key)
                        : GetListItem(container, GetIndex(key));
                case VmObjectType.Tuple:
                    return IsObjectType(key, VmObjectType.Slice)
                        ? SliceTuple(container, key)
                        : GetTupleItemNormalized(container, GetIndex(key));
                case VmObjectType.String:
                    return IsObjectType(key, VmObjectType.Slice)
                        ? SliceString(container, key)
                        : IndexString(container, GetIndex(key));
                case VmObjectType.Bytes:
                    return IsObjectType(key, VmObjectType.Slice)
                        ? SliceBytes(container, key)
                        : IndexBytes(container, GetIndex(key));
                case VmObjectType.Dictionary:
                    if (DictionaryTryGet(container, key, out var value))
                        return value;
                    Raise("KeyError", Repr(key, 0));
                    return VmValue.None;
                case VmObjectType.MappingProxy:
                    if (DictionaryTryGet(GetMappingProxyDictionary(container), key, out var proxyValue))
                        return proxyValue;
                    Raise("KeyError", Repr(key, 0));
                    return VmValue.None;
                case VmObjectType.Range:
                    return IndexRange(container, GetIndex(key));
                case VmObjectType.Instance:
                    if (HasSpecialMethod(container, "__getitem__"))
                        return CallBinarySpecialMethod(container, "__getitem__", key);
                    Raise("TypeError", $"'{GetTypeName(container)}' object is not subscriptable");
                    return VmValue.None;
                default:
                    Raise("TypeError", $"'{GetTypeName(container)}' object is not subscriptable");
                    return VmValue.None;
            }
        }

        private void StoreSubscript(VmValue container, VmValue key, VmValue value)
        {
            if (!container.IsAddress)
                Raise("TypeError", $"'{GetTypeName(container)}' object does not support item assignment");
            switch (_memory.GetObjectType(container))
            {
                case VmObjectType.List:
                    if (IsObjectType(key, VmObjectType.Slice))
                        Raise("NotImplementedError", "slice assignment is not implemented yet");
                    SetListItem(container, GetIndex(key), value);
                    return;
                case VmObjectType.Dictionary:
                    DictionarySet(container, key, value, rejectDuplicate: false);
                    return;
                case VmObjectType.Instance:
                    if (HasSpecialMethod(container, "__setitem__"))
                    {
                        var arguments = CreateTuple(2);
                        SetTupleItem(arguments, 0, key);
                        SetTupleItem(arguments, 1, value);
                        _ = CallSpecialMethod(container, "__setitem__", arguments, 2);
                        return;
                    }
                    Raise("TypeError", $"'{GetTypeName(container)}' object does not support item assignment");
                    return;
                default:
                    Raise("TypeError", $"'{GetTypeName(container)}' object does not support item assignment");
                    return;
            }
        }

        private void DeleteSubscript(VmValue container, VmValue key)
        {
            if (!container.IsAddress)
                Raise("TypeError", $"'{GetTypeName(container)}' object does not support item deletion");
            switch (_memory.GetObjectType(container))
            {
                case VmObjectType.List:
                    _ = ListPop(container, GetIndex(key));
                    return;
                case VmObjectType.Dictionary:
                    if (!DictionaryDelete(container, key))
                        Raise("KeyError", Repr(key, 0));
                    return;
                case VmObjectType.Instance:
                    if (HasSpecialMethod(container, "__delitem__"))
                    {
                        _ = CallBinarySpecialMethod(container, "__delitem__", key);
                        return;
                    }
                    Raise("TypeError", $"'{GetTypeName(container)}' object does not support item deletion");
                    return;
                default:
                    Raise("TypeError", $"'{GetTypeName(container)}' object does not support item deletion");
                    return;
            }
        }

        private VmValue BuildStringFromStack(int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative BUILD_STRING count.");
            var frame = _currentFrame;
            var stackCount = GetFrameStackCount(frame);
            if (stackCount < count)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_STRING underflows the Python value stack.");

            var baseIndex = stackCount - count;
            var stack = GetFrameStackAddress(frame);
            long totalLength = 0;
            for (var index = 0; index < count; index++)
            {
                var item = _memory.ReadValue(stack + (baseIndex + index) * 8);
                if (!IsObjectType(item, VmObjectType.String))
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_STRING received a non-string value.");
                totalLength += _memory.GetObjectAux0(item);
                if (totalLength > int.MaxValue || !_memory.CanAllocateObjectPayload((int)totalLength))
                    throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Formatted string exceeds the VM memory policy.");
            }

            var result = VmValue.FromAddress(_memory.AllocateObject(
                VmObjectType.String,
                (int)totalLength,
                (int)totalLength,
                0));
            var destination = _memory.GetSpan(_memory.GetObjectPayloadAddress(result), (int)totalLength);
            var offset = 0;
            for (var index = 0; index < count; index++)
            {
                var item = _memory.ReadValue(stack + (baseIndex + index) * 8);
                var length = _memory.GetObjectAux0(item);
                _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(item), length)
                    .CopyTo(destination[offset..]);
                offset += length;
                _memory.WriteValue(stack + (baseIndex + index) * 8, VmValue.Null);
            }
            SetFrameStackCount(frame, baseIndex);
            return result;
        }

        private VmValue BuildInterpolationFromStack(int operand)
        {
            var hasFormatSpec = (operand & 1) != 0;
            var conversionCode = operand >> 2;
            if ((uint)conversionCode > 3)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_INTERPOLATION conversion is invalid.");

            var frame = _currentFrame;
            var originalStackCount = GetFrameStackCount(frame);
            var required = hasFormatSpec ? 3 : 2;
            if (originalStackCount < required)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_INTERPOLATION underflows the Python value stack.");
            var baseIndex = originalStackCount - required;
            var stack = GetFrameStackAddress(frame);
            var value = _memory.ReadValue(stack + baseIndex * 8);
            var expression = _memory.ReadValue(stack + (baseIndex + 1) * 8);
            var formatSpec = hasFormatSpec
                ? _memory.ReadValue(stack + (baseIndex + 2) * 8)
                : CreateString(string.Empty);
            Push(formatSpec);

            var conversion = conversionCode switch
            {
                0 => VmValue.None,
                1 => CreateString("s"),
                2 => CreateString("r"),
                3 => CreateString("a"),
                _ => VmValue.None,
            };
            Push(conversion);
            var interpolation = CreateInterpolation(value, expression, conversion, formatSpec);

            var currentStackCount = GetFrameStackCount(frame);
            for (var index = baseIndex; index < currentStackCount; index++)
                _memory.WriteValue(stack + index * 8, VmValue.Null);
            SetFrameStackCount(frame, baseIndex);
            return interpolation;
        }

        private VmValue BuildTemplateFromStack()
        {
            var frame = _currentFrame;
            var stackCount = GetFrameStackCount(frame);
            if (stackCount < 2)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_TEMPLATE underflows the Python value stack.");
            var baseIndex = stackCount - 2;
            var stack = GetFrameStackAddress(frame);
            var strings = _memory.ReadValue(stack + baseIndex * 8);
            var interpolations = _memory.ReadValue(stack + (baseIndex + 1) * 8);
            var template = CreateTemplate(strings, interpolations);
            _memory.WriteValue(stack + baseIndex * 8, VmValue.Null);
            _memory.WriteValue(stack + (baseIndex + 1) * 8, VmValue.Null);
            SetFrameStackCount(frame, baseIndex);
            return template;
        }

        private VmValue GetTemplateValues(VmValue template)
        {
            RequireObjectType(template, VmObjectType.Template);
            var payload = _memory.GetObjectPayloadAddress(template);
            var interpolations = _memory.ReadValue(payload + 8);
            var count = GetTupleCount(interpolations);
            var values = CreateTuple(count);
            for (var index = 0; index < count; index++)
            {
                var interpolation = GetTupleItem(interpolations, index);
                RequireObjectType(interpolation, VmObjectType.Interpolation);
                SetTupleItem(values, index, _memory.ReadValue(_memory.GetObjectPayloadAddress(interpolation)));
            }
            return values;
        }

        private VmValue BuildTupleFromStack(int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative BUILD_TUPLE count.");
            var tuple = CreateTuple(count);
            for (var index = count - 1; index >= 0; index--)
                SetTupleItem(tuple, index, Pop());
            return tuple;
        }

        private VmValue BuildListFromStack(int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative BUILD_LIST count.");
            var temporary = CreateTuple(count);
            for (var index = count - 1; index >= 0; index--)
                SetTupleItem(temporary, index, Pop());
            var list = CreateList(count);
            for (var index = 0; index < count; index++)
                ListAdd(list, GetTupleItem(temporary, index));
            return list;
        }

        private VmValue BuildSetFromStack(int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative BUILD_SET count.");
            var temporary = CreateTuple(count);
            for (var index = count - 1; index >= 0; index--)
                SetTupleItem(temporary, index, Pop());
            var set = CreateSet();
            for (var index = 0; index < count; index++)
                SetAdd(set, GetTupleItem(temporary, index));
            return set;
        }

        private VmValue BuildMapFromStack(int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative BUILD_MAP count.");
            var temporary = CreateTuple(checked(count * 2));
            for (var index = count * 2 - 1; index >= 0; index--)
                SetTupleItem(temporary, index, Pop());
            var dictionary = CreateDictionary(Math.Max(8, count * 2));
            for (var index = 0; index < count; index++)
            {
                DictionarySet(
                    dictionary,
                    GetTupleItem(temporary, index * 2),
                    GetTupleItem(temporary, index * 2 + 1),
                    rejectDuplicate: false);
            }
            return dictionary;
        }

        private VmValue BuildSlice(int count)
        {
            if (count == 2)
            {
                var stop = Pop();
                var start = Pop();
                return CreateSlice(start, stop, VmValue.None);
            }
            if (count == 3)
            {
                var step = Pop();
                var stop = Pop();
                var start = Pop();
                return CreateSlice(start, stop, step);
            }
            throw new VmTrapException(VmStopReason.InvalidBytecode, "BUILD_SLICE operand must be two or three.");
        }

        private VmValue ConcatenateTemplates(VmValue left, VmValue right)
        {
            RequireObjectType(left, VmObjectType.Template);
            RequireObjectType(right, VmObjectType.Template);
            var leftPayload = _memory.GetObjectPayloadAddress(left);
            var rightPayload = _memory.GetObjectPayloadAddress(right);
            var leftStrings = _memory.ReadValue(leftPayload);
            var rightStrings = _memory.ReadValue(rightPayload);
            var leftInterpolations = _memory.ReadValue(leftPayload + 8);
            var rightInterpolations = _memory.ReadValue(rightPayload + 8);

            var leftStringCount = GetTupleCount(leftStrings);
            var rightStringCount = GetTupleCount(rightStrings);
            var strings = CreateTuple(checked(leftStringCount + rightStringCount - 1));
            var destination = 0;
            for (var index = 0; index < leftStringCount - 1; index++)
                SetTupleItem(strings, destination++, GetTupleItem(leftStrings, index));
            SetTupleItem(
                strings,
                destination++,
                ConcatenateStrings(
                    GetTupleItem(leftStrings, leftStringCount - 1),
                    GetTupleItem(rightStrings, 0)));
            for (var index = 1; index < rightStringCount; index++)
                SetTupleItem(strings, destination++, GetTupleItem(rightStrings, index));

            var leftInterpolationCount = GetTupleCount(leftInterpolations);
            var rightInterpolationCount = GetTupleCount(rightInterpolations);
            var interpolations = CreateTuple(checked(leftInterpolationCount + rightInterpolationCount));
            destination = 0;
            for (var index = 0; index < leftInterpolationCount; index++)
                SetTupleItem(interpolations, destination++, GetTupleItem(leftInterpolations, index));
            for (var index = 0; index < rightInterpolationCount; index++)
                SetTupleItem(interpolations, destination++, GetTupleItem(rightInterpolations, index));
            return CreateTemplate(strings, interpolations);
        }

        private VmValue ConcatenateLists(VmValue left, VmValue right)
        {
            var leftCount = GetListCount(left);
            var rightCount = GetListCount(right);
            var totalCount = (long)leftCount + rightCount;
            if (totalCount > int.MaxValue)
                throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Concatenated list is too large for the guest address space.");
            var result = CreateList((int)totalCount);
            for (var index = 0; index < leftCount; index++)
            {
                ListAdd(result, GetListItem(left, index));
                PollCancellation();
            }
            for (var index = 0; index < rightCount; index++)
            {
                ListAdd(result, GetListItem(right, index));
                PollCancellation();
            }
            return result;
        }

        private VmValue ConcatenateTuples(VmValue left, VmValue right)
        {
            var leftCount = GetTupleCount(left);
            var rightCount = GetTupleCount(right);
            var totalCount = (long)leftCount + rightCount;
            if (totalCount > int.MaxValue)
                throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Concatenated tuple is too large for the guest address space.");
            var result = CreateTuple((int)totalCount);
            for (var index = 0; index < leftCount; index++)
            {
                SetTupleItem(result, index, GetTupleItem(left, index));
                PollCancellation();
            }
            for (var index = 0; index < rightCount; index++)
            {
                SetTupleItem(result, leftCount + index, GetTupleItem(right, index));
                PollCancellation();
            }
            return result;
        }

        private VmValue RepeatSequence(VmValue sequence, BigInteger repeatCount)
        {
            if (!sequence.IsAddress)
                Raise("TypeError", $"can't multiply sequence by non-int of type '{GetTypeName(sequence)}'");
            var type = _memory.GetObjectType(sequence);
            if (type is not (VmObjectType.String or VmObjectType.List or VmObjectType.Tuple))
                Raise("TypeError", $"can't multiply sequence by non-int of type '{GetTypeName(sequence)}'");

            if (repeatCount <= 0)
            {
                return type switch
                {
                    VmObjectType.String => CreateString(string.Empty),
                    VmObjectType.List => CreateList(0),
                    VmObjectType.Tuple => CreateTuple(0),
                    _ => VmValue.None,
                };
            }

            switch (type)
            {
                case VmObjectType.String:
                    {
                        var sourceByteCount = _memory.GetObjectAux0(sequence);
                        if (sourceByteCount == 0)
                            return CreateString(string.Empty);
                        var count = GetRepeatCount(repeatCount);
                        var text = GetString(sequence);
                        var repeatedByteCount = (long)sourceByteCount * count;
                        var repeatedCharacterCount = (long)text.Length * count;
                        if (repeatedByteCount > int.MaxValue ||
                            repeatedCharacterCount > int.MaxValue ||
                            !_memory.CanAllocateObjectPayload((int)repeatedByteCount))
                        {
                            throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Repeated string exceeds the VM memory policy.");
                        }
                        var builder = new StringBuilder((int)repeatedCharacterCount);
                        for (var repeat = 0; repeat < count; repeat++)
                        {
                            builder.Append(text);
                            PollCancellation();
                        }
                        return CreateString(builder.ToString());
                    }
                case VmObjectType.List:
                    {
                        var sourceCount = GetListCount(sequence);
                        if (sourceCount == 0)
                            return CreateList(0);
                        var count = GetRepeatCount(repeatCount);
                        var totalCount = (long)sourceCount * count;
                        if (totalCount > int.MaxValue)
                            throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Repeated list is too large for the guest address space.");
                        var result = CreateList((int)totalCount);
                        for (var repeat = 0; repeat < count; repeat++)
                        {
                            for (var index = 0; index < sourceCount; index++)
                            {
                                ListAdd(result, GetListItem(sequence, index));
                                PollCancellation();
                            }
                        }
                        return result;
                    }
                case VmObjectType.Tuple:
                    {
                        var sourceCount = GetTupleCount(sequence);
                        if (sourceCount == 0)
                            return CreateTuple(0);
                        var count = GetRepeatCount(repeatCount);
                        var totalCount = (long)sourceCount * count;
                        if (totalCount > int.MaxValue)
                            throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Repeated tuple is too large for the guest address space.");
                        var result = CreateTuple((int)totalCount);
                        var destination = 0;
                        for (var repeat = 0; repeat < count; repeat++)
                        {
                            for (var index = 0; index < sourceCount; index++)
                            {
                                SetTupleItem(result, destination++, GetTupleItem(sequence, index));
                                PollCancellation();
                            }
                        }
                        return result;
                    }
                default:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Invalid repeatable sequence type.");
            }
        }

        private void ExtendList(VmValue list, VmValue iterable)
        {
            var iterator = CreateIterator(iterable);
            var hostRootBase = PushHostRoots(list, iterable, iterator);
            try
            {
                while (IteratorMoveNext(iterator, out var value))
                {
                    ListAdd(list, value);
                    PollCancellation();
                }
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private void ExtendSet(VmValue set, VmValue iterable)
        {
            var iterator = CreateIterator(iterable);
            var hostRootBase = PushHostRoots(set, iterable, iterator);
            try
            {
                while (IteratorMoveNext(iterator, out var value))
                {
                    SetAdd(set, value);
                    PollCancellation();
                }
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private VmValue MaterializeList(VmValue iterable)
        {
            if (IsObjectType(iterable, VmObjectType.List))
            {
                var result = CreateList(GetListCount(iterable));
                ExtendList(result, iterable);
                return result;
            }
            var list = CreateList(0);
            ExtendList(list, iterable);
            return list;
        }

        private VmValue MaterializeTuple(VmValue iterable)
        {
            if (IsObjectType(iterable, VmObjectType.Tuple))
                return iterable;
            var list = MaterializeList(iterable);
            var count = GetListCount(list);
            var tuple = CreateTuple(count);
            for (var index = 0; index < count; index++)
                SetTupleItem(tuple, index, GetListItem(list, index));
            return tuple;
        }

        private bool IsIterable(VmValue value)
        {
            if (!value.IsAddress)
                return false;
            if (_memory.GetObjectType(value) == VmObjectType.Instance)
                return HasSpecialMethod(value, "__iter__") || HasSpecialMethod(value, "__getitem__");
            return _memory.GetObjectType(value) is
                VmObjectType.Tuple or
                VmObjectType.List or
                VmObjectType.String or
                VmObjectType.Bytes or
                VmObjectType.Dictionary or
                VmObjectType.MappingProxy or
                VmObjectType.Set or
                VmObjectType.FrozenSet or
                VmObjectType.Range or
                VmObjectType.Iterator or
                VmObjectType.BuiltinIterator or
                VmObjectType.Generator or
                VmObjectType.Template;
        }

        private bool IteratorMoveNext(VmValue iterator, out VmValue value)
        {
            if (IsObjectType(iterator, VmObjectType.Generator))
                return ResumeGenerator(iterator, out value);
            if (IsObjectType(iterator, VmObjectType.BuiltinIterator))
                return BuiltinIteratorMoveNext(iterator, out value);
            if (IsObjectType(iterator, VmObjectType.Instance))
            {
                try
                {
                    value = CallZeroArgumentSpecialMethod(iterator, "__next__");
                    return true;
                }
                catch (VmGuestExceptionSignal) when (IsRaisedException("StopIteration"))
                {
                    _raisedException = VmValue.Null;
                    _raisedLastInstruction = -1;
                    value = VmValue.Null;
                    return false;
                }
            }

            RequireObjectType(iterator, VmObjectType.Iterator);
            var payload = _memory.GetObjectPayloadAddress(iterator);
            var iterable = _memory.ReadValue(payload);
            var index = _memory.ReadInt64(payload + 8);
            if (index < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Iterator index is negative.");

            switch (_memory.GetObjectType(iterable))
            {
                case VmObjectType.List:
                    if (index >= GetListCount(iterable))
                        break;
                    value = GetListItem(iterable, checked((int)index));
                    _memory.WriteInt64(payload + 8, checked(index + 1));
                    return true;

                case VmObjectType.Tuple:
                    if (index >= GetTupleCount(iterable))
                        break;
                    value = GetTupleItem(iterable, checked((int)index));
                    _memory.WriteInt64(payload + 8, checked(index + 1));
                    return true;

                case VmObjectType.String:
                    {
                        var text = GetString(iterable);
                        var utf16Offset = checked((int)index);
                        if (utf16Offset >= text.Length)
                            break;
                        var codePoint = ReadCodePoint(text, utf16Offset, out var consumed);
                        value = CreateString(char.ConvertFromUtf32(codePoint));
                        _memory.WriteInt64(payload + 8, checked(index + consumed));
                        return true;
                    }

                case VmObjectType.Bytes:
                    {
                        var length = _memory.GetObjectAux0(iterable);
                        if (index >= length)
                            break;
                        var item = _memory.ReadByte(checked(_memory.GetObjectPayloadAddress(iterable) + (int)index));
                        value = VmValue.FromSmallInteger(item);
                        _memory.WriteInt64(payload + 8, checked(index + 1));
                        return true;
                    }

                case VmObjectType.Range:
                    {
                        var rangePayload = _memory.GetObjectPayloadAddress(iterable);
                        var start = _memory.ReadInt64(rangePayload);
                        var stop = _memory.ReadInt64(rangePayload + 8);
                        var step = _memory.ReadInt64(rangePayload + 16);
                        var current = new BigInteger(start) + new BigInteger(step) * index;
                        var inRange = step > 0 ? current < stop : current > stop;
                        if (!inRange)
                            break;
                        value = CreateInteger(current);
                        _memory.WriteInt64(payload + 8, checked(index + 1));
                        return true;
                    }

                case VmObjectType.Template:
                    {
                        var templatePayload = _memory.GetObjectPayloadAddress(iterable);
                        var strings = _memory.ReadValue(templatePayload);
                        var interpolations = _memory.ReadValue(templatePayload + 8);
                        var totalItems = checked(GetTupleCount(strings) + GetTupleCount(interpolations));
                        while (index < totalItems)
                        {
                            var current = index;
                            index = checked(index + 1);
                            _memory.WriteInt64(payload + 8, index);
                            if ((current & 1) == 0)
                            {
                                var text = GetTupleItem(strings, checked((int)(current / 2)));
                                if (_memory.GetObjectAux0(text) == 0)
                                    continue;
                                value = text;
                                return true;
                            }

                            value = GetTupleItem(interpolations, checked((int)(current / 2)));
                            return true;
                        }
                        break;
                    }

                case VmObjectType.Dictionary:
                    if (TryMoveDictionaryIterator(iterable, payload, values: false, out value))
                        return true;
                    break;

                case VmObjectType.MappingProxy:
                    if (TryMoveDictionaryIterator(GetMappingProxyDictionary(iterable), payload, values: false, out value))
                        return true;
                    break;

                case VmObjectType.Set:
                case VmObjectType.FrozenSet:
                    if (TryMoveDictionaryIterator(GetSetDictionary(iterable), payload, values: false, out value))
                        return true;
                    break;

                default:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "Iterator refers to a non-iterable object.");
            }

            value = VmValue.Null;
            _memory.WriteInt32(payload + 20, 1);
            return false;
        }

        private bool BuiltinIteratorMoveNext(VmValue iterator, out VmValue value)
        {
            RequireObjectType(iterator, VmObjectType.BuiltinIterator);
            var payload = _memory.GetObjectPayloadAddress(iterator);
            var kind = (VmBuiltinIteratorKind)_memory.ReadInt32(payload);
            var flags = _memory.ReadInt32(payload + 4);
            var primary = _memory.ReadValue(payload + 8);
            var secondary = _memory.ReadValue(payload + 16);
            ConsumeInstructionBudget();

            switch (kind)
            {
                case VmBuiltinIteratorKind.Enumerate:
                    EnsureHostRootCapacity(3);
                    if (!IteratorMoveNext(primary, out var enumeratedItem))
                    {
                        value = VmValue.Null;
                        return false;
                    }
                    {
                        var currentIndex = secondary;
                        var rootBase = PushHostRoots(iterator, enumeratedItem, currentIndex);
                        try
                        {
                            var nextIndex = CreateInteger(GetInteger(currentIndex) + BigInteger.One);
                            _memory.WriteValue(payload + 16, nextIndex);
                            var result = CreateTuple(2);
                            SetTupleItem(result, 0, currentIndex);
                            SetTupleItem(result, 1, enumeratedItem);
                            value = result;
                            return true;
                        }
                        finally
                        {
                            PopHostRoots(rootBase);
                        }
                    }

                case VmBuiltinIteratorKind.Zip:
                    {
                        var iterators = primary;
                        var count = GetTupleCount(iterators);
                        if (count == 0)
                        {
                            value = VmValue.Null;
                            return false;
                        }
                        EnsureHostRootCapacity(3);
                        var result = CreateTuple(count);
                        var rootBase = PushHostRoots(iterator, iterators, result);
                        try
                        {
                            for (var index = 0; index < count; index++)
                            {
                                var current = GetTupleItem(iterators, index);
                                if (IteratorMoveNext(current, out var item))
                                {
                                    SetTupleItem(result, index, item);
                                    continue;
                                }
                                if ((flags & 1) == 0)
                                {
                                    value = VmValue.Null;
                                    return false;
                                }
                                if (index != 0)
                                    Raise("ValueError", $"zip() argument {index + 1} is shorter than argument 1");
                                for (var other = 1; other < count; other++)
                                {
                                    if (IteratorMoveNext(GetTupleItem(iterators, other), out _))
                                        Raise("ValueError", $"zip() argument {other + 1} is longer than argument 1");
                                }
                                value = VmValue.Null;
                                return false;
                            }
                            value = result;
                            return true;
                        }
                        finally
                        {
                            PopHostRoots(rootBase);
                        }
                    }

                case VmBuiltinIteratorKind.Map:
                    {
                        var callable = primary;
                        var iterators = secondary;
                        var count = GetTupleCount(iterators);
                        EnsureHostRootCapacity(3);
                        var arguments = CreateTuple(count);
                        var rootBase = PushHostRoots(iterator, callable, arguments);
                        try
                        {
                            for (var index = 0; index < count; index++)
                            {
                                if (IteratorMoveNext(GetTupleItem(iterators, index), out var item))
                                {
                                    SetTupleItem(arguments, index, item);
                                    continue;
                                }
                                if ((flags & 1) != 0 && index != 0)
                                    Raise("ValueError", $"map() argument {index + 2} is shorter than argument 2");
                                if ((flags & 1) != 0 && index == 0)
                                {
                                    for (var other = 1; other < count; other++)
                                    {
                                        if (IteratorMoveNext(GetTupleItem(iterators, other), out _))
                                            Raise("ValueError", $"map() argument {other + 2} is longer than argument 2");
                                    }
                                }
                                value = VmValue.Null;
                                return false;
                            }
                            value = ExecuteCallableSynchronously(callable, arguments, count, VmValue.None);
                            return true;
                        }
                        finally
                        {
                            PopHostRoots(rootBase);
                        }
                    }

                case VmBuiltinIteratorKind.Filter:
                    {
                        var predicate = primary;
                        var source = secondary;
                        EnsureHostRootCapacity(3);
                        while (IteratorMoveNext(source, out var item))
                        {
                            var itemRootBase = PushHostRoots(iterator, predicate, item);
                            try
                            {
                                var accepted = IsTruthy(item);
                                if (!predicate.IsNone)
                                {
                                    var arguments = CreateTuple(1);
                                    SetTupleItem(arguments, 0, item);
                                    accepted = IsTruthy(ExecuteCallableSynchronously(predicate, arguments, 1, VmValue.None));
                                }
                                if (accepted)
                                {
                                    value = item;
                                    return true;
                                }
                            }
                            finally
                            {
                                PopHostRoots(itemRootBase);
                            }
                            ConsumeInstructionBudget();
                        }
                        value = VmValue.Null;
                        return false;
                    }

                case VmBuiltinIteratorKind.Reversed:
                    {
                        var index = _memory.ReadInt64(payload + 32);
                        if (index < 0)
                        {
                            value = VmValue.Null;
                            return false;
                        }
                        EnsureHostRootCapacity(6);
                        var key = CreateInteger(new BigInteger(index));
                        value = IsObjectType(primary, VmObjectType.Instance)
                            ? CallBinarySpecialMethod(primary, "__getitem__", key)
                            : LoadSubscript(primary, key);
                        _memory.WriteInt64(payload + 32, index - 1);
                        return true;
                    }

                case VmBuiltinIteratorKind.CallableSentinel:
                    {
                        EnsureHostRootCapacity(3);
                        var arguments = CreateTuple(0);
                        var result = ExecuteCallableSynchronously(primary, arguments, 0, VmValue.None);
                        if (ValuesEqual(result, secondary))
                        {
                            value = VmValue.Null;
                            return false;
                        }
                        value = result;
                        return true;
                    }

                case VmBuiltinIteratorKind.Sequence:
                    {
                        var index = _memory.ReadInt64(payload + 32);
                        EnsureHostRootCapacity(9);
                        var arguments = CreateTuple(1);
                        var rootBase = PushHostRoots(iterator, primary, arguments);
                        try
                        {
                            SetTupleItem(arguments, 0, CreateInteger(new BigInteger(index)));
                            value = CallSpecialMethod(primary, "__getitem__", arguments, 1);
                            _memory.WriteInt64(payload + 32, checked(index + 1));
                            return true;
                        }
                        catch (VmGuestExceptionSignal) when (IsRaisedException("IndexError") || IsRaisedException("StopIteration"))
                        {
                            _raisedException = VmValue.Null;
                            _raisedLastInstruction = -1;
                            value = VmValue.Null;
                            return false;
                        }
                        finally
                        {
                            PopHostRoots(rootBase);
                        }
                    }

                default:
                    throw new VmTrapException(
                        VmStopReason.InvalidBytecode,
                        $"Unknown built-in iterator kind {kind}.");
            }
        }

        private bool TryMoveDictionaryIterator(
            VmValue dictionary,
            int iteratorPayload,
            bool values,
            out VmValue value)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            var dictionaryPayload = _memory.GetObjectPayloadAddress(dictionary);
            var capacity = _memory.ReadInt32(dictionaryPayload + 12);
            var slot = _memory.ReadInt32(iteratorPayload + 16);
            if ((uint)slot > (uint)capacity)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Dictionary iterator slot is corrupt.");
            var entries = GetDictionaryEntriesPayload(dictionary);
            while (slot < capacity)
            {
                var entry = entries + slot * DictionaryEntrySize;
                slot++;
                _memory.WriteInt32(iteratorPayload + 16, slot);
                var key = _memory.ReadValue(entry + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                value = values ? _memory.ReadValue(entry + 16) : key;
                return true;
            }
            value = VmValue.Null;
            return false;
        }

        private void DictionaryUpdate(VmValue target, VmValue update, bool rejectDuplicate)
        {
            RequireObjectType(target, VmObjectType.Dictionary);
            if (IsObjectType(update, VmObjectType.MappingProxy))
                update = GetMappingProxyDictionary(update);
            if (IsObjectType(update, VmObjectType.Dictionary))
            {
                var payload = _memory.GetObjectPayloadAddress(update);
                var capacity = _memory.ReadInt32(payload + 12);
                var entries = GetDictionaryEntriesPayload(update);
                for (var slot = 0; slot < capacity; slot++)
                {
                    var entry = entries + slot * DictionaryEntrySize;
                    var key = _memory.ReadValue(entry + 8);
                    if (key.IsNull || key.IsDeleted)
                        continue;
                    DictionarySet(target, key, _memory.ReadValue(entry + 16), rejectDuplicate);
                    PollCancellation();
                }
                return;
            }

            var iterator = CreateIterator(update);
            var hostRootBase = PushHostRoots(target, update, iterator);
            try
            {
                var position = 0;
                while (IteratorMoveNext(iterator, out var item))
                {
                    VmValue key;
                    VmValue value;
                    if (IsObjectType(item, VmObjectType.Tuple))
                    {
                        if (GetTupleCount(item) != 2)
                            Raise("ValueError", $"dictionary update sequence element #{position} has length {GetTupleCount(item)}; 2 is required");
                        key = GetTupleItem(item, 0);
                        value = GetTupleItem(item, 1);
                    }
                    else if (IsObjectType(item, VmObjectType.List))
                    {
                        if (GetListCount(item) != 2)
                            Raise("ValueError", $"dictionary update sequence element #{position} has length {GetListCount(item)}; 2 is required");
                        key = GetListItem(item, 0);
                        value = GetListItem(item, 1);
                    }
                    else
                    {
                        Raise("TypeError", $"cannot convert dictionary update sequence element #{position} to a sequence");
                        return;
                    }
                    DictionarySet(target, key, value, rejectDuplicate);
                    position++;
                    PollCancellation();
                }
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private VmValue DictionaryKeys(VmValue dictionary, bool values)
        {
            RequireObjectType(dictionary, VmObjectType.Dictionary);
            var result = CreateList(GetDictionaryCount(dictionary));
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            var capacity = _memory.ReadInt32(payload + 12);
            var entries = GetDictionaryEntriesPayload(dictionary);
            for (var slot = 0; slot < capacity; slot++)
            {
                var entry = entries + slot * DictionaryEntrySize;
                var key = _memory.ReadValue(entry + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                ListAdd(result, values ? _memory.ReadValue(entry + 16) : key);
                PollCancellation();
            }
            return result;
        }

        private bool SetEquals(VmValue left, VmValue right)
        {
            var leftDictionary = GetSetDictionary(left);
            var rightDictionary = GetSetDictionary(right);
            if (GetDictionaryCount(leftDictionary) != GetDictionaryCount(rightDictionary))
                return false;
            var payload = _memory.GetObjectPayloadAddress(leftDictionary);
            var capacity = _memory.ReadInt32(payload + 12);
            var entries = GetDictionaryEntriesPayload(leftDictionary);
            for (var slot = 0; slot < capacity; slot++)
            {
                var key = _memory.ReadValue(entries + slot * DictionaryEntrySize + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                if (!DictionaryTryGet(rightDictionary, key, out _))
                    return false;
                PollCancellation();
            }
            return true;
        }

        private bool Contains(VmValue container, VmValue item)
        {
            if (!container.IsAddress)
                Raise("TypeError", $"argument of type '{GetTypeName(container)}' is not iterable");
            switch (_memory.GetObjectType(container))
            {
                case VmObjectType.String:
                    if (!IsObjectType(item, VmObjectType.String))
                        Raise("TypeError", "'in <string>' requires string as left operand");
                    return GetString(container).Contains(GetString(item), StringComparison.Ordinal);

                case VmObjectType.Dictionary:
                    return DictionaryTryGet(container, item, out _);

                case VmObjectType.MappingProxy:
                    return DictionaryTryGet(GetMappingProxyDictionary(container), item, out _);

                case VmObjectType.Set:
                case VmObjectType.FrozenSet:
                    return DictionaryTryGet(GetSetDictionary(container), item, out _);

                default:
                    {
                        var iterator = CreateIterator(container);
                        var hostRootBase = PushHostRoots(container, item, iterator);
                        try
                        {
                            while (IteratorMoveNext(iterator, out var value))
                            {
                                if (ValuesEqual(value, item))
                                    return true;
                                PollCancellation();
                            }
                            return false;
                        }
                        finally
                        {
                            PopHostRoots(hostRootBase);
                        }
                    }
            }
        }

        private bool IsTruthy(VmValue value)
        {
            if (value.IsNull)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "NULL call-protocol sentinel escaped into Python code.");
            if (value.IsNone)
                return false;
            if (value.IsBoolean)
                return value.BooleanValue;
            if (value.IsEllipsis)
                return true;
            if (value.IsSmallInteger)
                return value.SmallIntegerValue != 0;
            if (value.IsBuiltin)
                return true;
            if (!value.IsAddress)
                return true;

            switch (_memory.GetObjectType(value))
            {
                case VmObjectType.Integer:
                    return !GetInteger(value).IsZero;
                case VmObjectType.Float:
                    return GetFloat(value) != 0.0;
                case VmObjectType.Complex:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return _memory.ReadDouble(payload) != 0.0 || _memory.ReadDouble(payload + 8) != 0.0;
                    }
                case VmObjectType.String:
                case VmObjectType.Bytes:
                case VmObjectType.Tuple:
                    return _memory.GetObjectAux0(value) != 0;
                case VmObjectType.List:
                    return GetListCount(value) != 0;
                case VmObjectType.Dictionary:
                    return GetDictionaryCount(value) != 0;
                case VmObjectType.MappingProxy:
                    return GetDictionaryCount(GetMappingProxyDictionary(value)) != 0;
                case VmObjectType.Set:
                case VmObjectType.FrozenSet:
                    return GetDictionaryCount(GetSetDictionary(value)) != 0;
                case VmObjectType.Range:
                    return GetRangeLength(value) != 0;
                case VmObjectType.Instance:
                    if (HasSpecialMethod(value, "__bool__"))
                    {
                        var result = CallZeroArgumentSpecialMethod(value, "__bool__");
                        if (!result.IsBoolean)
                            Raise("TypeError", $"__bool__ should return bool, returned {GetTypeName(result)}");
                        return result.BooleanValue;
                    }
                    if (HasSpecialMethod(value, "__len__"))
                        return GetLength(value) != 0;
                    return true;
                default:
                    return true;
            }
        }

        private int GetLength(VmValue value)
        {
            if (!value.IsAddress)
                Raise("TypeError", $"object of type '{GetTypeName(value)}' has no len()");
            switch (_memory.GetObjectType(value))
            {
                case VmObjectType.String:
                    return CountCodePoints(GetString(value));
                case VmObjectType.Bytes:
                case VmObjectType.Tuple:
                    return _memory.GetObjectAux0(value);
                case VmObjectType.List:
                    return GetListCount(value);
                case VmObjectType.Dictionary:
                    return GetDictionaryCount(value);
                case VmObjectType.MappingProxy:
                    return GetDictionaryCount(GetMappingProxyDictionary(value));
                case VmObjectType.Set:
                case VmObjectType.FrozenSet:
                    return GetDictionaryCount(GetSetDictionary(value));
                case VmObjectType.Range:
                    return GetRangeLength(value);
                case VmObjectType.Instance:
                    if (!HasSpecialMethod(value, "__len__"))
                        Raise("TypeError", $"object of type '{GetTypeName(value)}' has no len()");
                    {
                        var result = CallZeroArgumentSpecialMethod(value, "__len__");
                        if (!IsInteger(result))
                            Raise("TypeError", $"'{GetTypeName(result)}' object cannot be interpreted as an integer");
                        var length = GetInteger(result);
                        if (length < 0)
                            Raise("ValueError", "__len__() should return >= 0");
                        if (length > int.MaxValue)
                            Raise("OverflowError", "__len__() result is too large");
                        return (int)length;
                    }
                default:
                    Raise("TypeError", $"object of type '{GetTypeName(value)}' has no len()");
                    return 0;
            }
        }

        private int GetRangeLength(VmValue range)
        {
            RequireObjectType(range, VmObjectType.Range);
            var payload = _memory.GetObjectPayloadAddress(range);
            var start = new BigInteger(_memory.ReadInt64(payload));
            var stop = new BigInteger(_memory.ReadInt64(payload + 8));
            var step = new BigInteger(_memory.ReadInt64(payload + 16));
            BigInteger length;
            if (step.Sign > 0)
                length = start >= stop ? BigInteger.Zero : ((stop - start - 1) / step) + 1;
            else
                length = start <= stop ? BigInteger.Zero : ((start - stop - 1) / -step) + 1;
            if (length > int.MaxValue)
                Raise("OverflowError", "range object has too many items for this VM's len() result");
            return (int)length;
        }

        private void EnsureHashable(VmValue value)
        {
            _ = GetHash(value);
        }

        private ulong GetHash(VmValue value)
        {
            return GetHash(value, 0);
        }

        private ulong GetHash(VmValue value, int depth)
        {
            if (depth > _limits.MaxCallDepth)
                throw new VmTrapException(VmStopReason.CallDepthLimitExceeded, "Hash nesting exceeds the VM call-depth policy.");
            if (value.IsNone)
                return MixHash(0x4e6f6e65UL);
            if (value.IsBoolean)
                return HashInteger(value.BooleanValue ? BigInteger.One : BigInteger.Zero);
            if (value.IsEllipsis)
                return MixHash(0x456c6c6970736973UL);
            if (value.IsSmallInteger)
                return HashInteger(new BigInteger(value.SmallIntegerValue));
            if (value.IsBuiltin)
                return MixHash(0xb0170000UL ^ (ulong)value.Builtin);
            if (!value.IsAddress)
                Raise("TypeError", $"unhashable type: '{GetTypeName(value)}'");

            switch (_memory.GetObjectType(value))
            {
                case VmObjectType.Integer:
                    return HashInteger(GetInteger(value));
                case VmObjectType.Float:
                    {
                        var number = GetFloat(value);
                        if (double.IsFinite(number) && Math.Truncate(number) == number)
                            return HashInteger(new BigInteger(number));
                        return MixHash(unchecked((ulong)BitConverter.DoubleToInt64Bits(number)));
                    }
                case VmObjectType.String:
                case VmObjectType.Bytes:
                    return HashBytes(_memory.GetReadOnlySpan(
                        _memory.GetObjectPayloadAddress(value),
                        _memory.GetObjectAux0(value)));
                case VmObjectType.Tuple:
                    {
                        var hash = 0x345678UL;
                        var count = GetTupleCount(value);
                        for (var index = 0; index < count; index++)
                        {
                            hash = MixHash(hash ^ GetHash(GetTupleItem(value, index), depth + 1));
                            PollCancellation();
                        }
                        return MixHash(hash ^ (ulong)count);
                    }
                case VmObjectType.FrozenSet:
                    {
                        var dictionary = GetSetDictionary(value);
                        var payload = _memory.GetObjectPayloadAddress(dictionary);
                        var capacity = _memory.ReadInt32(payload + 12);
                        var entries = GetDictionaryEntriesPayload(dictionary);
                        ulong hash = 0xf05e7UL;
                        for (var slot = 0; slot < capacity; slot++)
                        {
                            var key = _memory.ReadValue(entries + slot * DictionaryEntrySize + 8);
                            if (key.IsNull || key.IsDeleted)
                                continue;
                            hash ^= MixHash(GetHash(key, depth + 1));
                            PollCancellation();
                        }
                        return MixHash(hash ^ (ulong)GetDictionaryCount(dictionary));
                    }
                case VmObjectType.Function:
                case VmObjectType.BoundMethod:
                case VmObjectType.Exception:
                case VmObjectType.Cell:
                case VmObjectType.Generator:
                case VmObjectType.Module:
                case VmObjectType.Interpolation:
                case VmObjectType.Template:
                case VmObjectType.Class:
                case VmObjectType.Instance:
                case VmObjectType.PythonBoundMethod:
                case VmObjectType.Super:
                case VmObjectType.BuiltinIterator:
                case VmObjectType.StaticMethod:
                case VmObjectType.ClassMethod:
                case VmObjectType.Property:
                    return MixHash((ulong)value.Address);
                default:
                    Raise("TypeError", $"unhashable type: '{GetTypeName(value)}'");
                    return 0;
            }
        }

        private ulong HashInteger(BigInteger value)
        {
            if (value >= long.MinValue && value <= long.MaxValue)
                return MixHash(unchecked((ulong)(long)value));
            var byteCount = value.GetByteCount(isUnsigned: false);
            if (byteCount > (_limits.MaxIntegerBits + 7) / 8 + 1)
                throw new VmTrapException(VmStopReason.IntegerLimitExceeded, "Integer hash input exceeds the VM limit.");
            var bytes = new byte[byteCount];
            if (!value.TryWriteBytes(bytes, out var written, isUnsigned: false, isBigEndian: false) || written != byteCount)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Failed to hash a Python integer.");
            return HashBytes(bytes);
        }

        private static ulong HashBytes(ReadOnlySpan<byte> bytes)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var item in bytes)
            {
                hash ^= item;
                hash *= prime;
            }
            return MixHash(hash ^ (ulong)bytes.Length);
        }

        private static ulong MixHash(ulong value)
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9UL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebUL;
            value ^= value >> 31;
            return value;
        }

        private VmValue GetTupleItemNormalized(VmValue tuple, int index)
        {
            var count = GetTupleCount(tuple);
            return GetTupleItem(tuple, NormalizeIndex(index, count));
        }

        private VmValue IndexString(VmValue textValue, int index)
        {
            var text = GetString(textValue);
            var count = CountCodePoints(text);
            index = NormalizeIndex(index, count);
            var utf16Offset = GetCodePointUtf16Offset(text, index);
            var codePoint = ReadCodePoint(text, utf16Offset, out _);
            return CreateString(char.ConvertFromUtf32(codePoint));
        }

        private VmValue IndexBytes(VmValue bytes, int index)
        {
            RequireObjectType(bytes, VmObjectType.Bytes);
            var length = _memory.GetObjectAux0(bytes);
            index = NormalizeIndex(index, length);
            return VmValue.FromSmallInteger(_memory.ReadByte(_memory.GetObjectPayloadAddress(bytes) + index));
        }

        private VmValue IndexRange(VmValue range, int index)
        {
            var length = GetRangeLength(range);
            index = NormalizeIndex(index, length);
            var payload = _memory.GetObjectPayloadAddress(range);
            var start = new BigInteger(_memory.ReadInt64(payload));
            var step = new BigInteger(_memory.ReadInt64(payload + 16));
            return CreateInteger(start + step * index);
        }

        private VmValue SliceList(VmValue list, VmValue slice)
        {
            var count = GetListCount(list);
            ResolveSlice(slice, count, out var start, out _, out var step, out var resultCount);
            var result = CreateList(resultCount);
            var index = start;
            for (var outputIndex = 0; outputIndex < resultCount; outputIndex++, index += step)
            {
                ListAdd(result, GetListItem(list, index));
                PollCancellation();
            }
            return result;
        }

        private VmValue SliceTuple(VmValue tuple, VmValue slice)
        {
            var count = GetTupleCount(tuple);
            ResolveSlice(slice, count, out var start, out _, out var step, out var resultCount);
            var result = CreateTuple(resultCount);
            var index = start;
            for (var outputIndex = 0; outputIndex < resultCount; outputIndex++, index += step)
            {
                SetTupleItem(result, outputIndex, GetTupleItem(tuple, index));
                PollCancellation();
            }
            return result;
        }

        private VmValue SliceString(VmValue textValue, VmValue slice)
        {
            var text = GetString(textValue);
            var count = CountCodePoints(text);
            ResolveSlice(slice, count, out var start, out _, out var step, out var resultCount);
            if (resultCount == 0)
                return CreateString(string.Empty);
            var builder = new StringBuilder();
            var index = start;
            for (var outputIndex = 0; outputIndex < resultCount; outputIndex++, index += step)
            {
                var offset = GetCodePointUtf16Offset(text, index);
                var codePoint = ReadCodePoint(text, offset, out _);
                builder.Append(char.ConvertFromUtf32(codePoint));
                PollCancellation();
            }
            return CreateString(builder.ToString());
        }

        private VmValue SliceBytes(VmValue bytes, VmValue slice)
        {
            RequireObjectType(bytes, VmObjectType.Bytes);
            var count = _memory.GetObjectAux0(bytes);
            ResolveSlice(slice, count, out var start, out _, out var step, out var resultCount);
            var result = VmValue.FromAddress(_memory.AllocateObject(VmObjectType.Bytes, resultCount, resultCount, 0));
            var source = _memory.GetObjectPayloadAddress(bytes);
            var destination = _memory.GetObjectPayloadAddress(result);
            var index = start;
            for (var outputIndex = 0; outputIndex < resultCount; outputIndex++, index += step)
            {
                _memory.WriteByte(destination + outputIndex, _memory.ReadByte(source + index));
                PollCancellation();
            }
            return result;
        }

        private void ResolveSlice(
            VmValue slice,
            int length,
            out int start,
            out int stop,
            out int step,
            out int sliceLength)
        {
            RequireObjectType(slice, VmObjectType.Slice);
            var payload = _memory.GetObjectPayloadAddress(slice);
            var startValue = _memory.ReadValue(payload);
            var stopValue = _memory.ReadValue(payload + 8);
            var stepValue = _memory.ReadValue(payload + 16);
            step = stepValue.IsNone ? 1 : GetIndex(stepValue);
            if (step == 0)
                Raise("ValueError", "slice step cannot be zero");

            if (step > 0)
            {
                start = startValue.IsNone ? 0 : ClampPositiveSliceIndex(GetIndex(startValue), length);
                stop = stopValue.IsNone ? length : ClampPositiveSliceIndex(GetIndex(stopValue), length);
                sliceLength = start >= stop ? 0 : 1 + (stop - start - 1) / step;
            }
            else
            {
                start = startValue.IsNone ? length - 1 : ClampNegativeSliceIndex(GetIndex(startValue), length);
                stop = stopValue.IsNone ? -1 : ClampNegativeSliceIndex(GetIndex(stopValue), length);
                sliceLength = start <= stop ? 0 : 1 + (start - stop - 1) / -step;
            }
        }

        private static int ClampPositiveSliceIndex(int index, int length)
        {
            if (index < 0)
                index = Math.Max(index + length, 0);
            return Math.Min(index, length);
        }

        private static int ClampNegativeSliceIndex(int index, int length)
        {
            if (index < 0)
                index += length;
            if (index < 0)
                return -1;
            return Math.Min(index, length - 1);
        }

        private void ImportName(int nameIndex)
        {
            var fromList = Pop();
            var level = GetImportLevel(Pop());
            var requestedName = GetString(GetCodeNameValue(GetFrameCode(_currentFrame), nameIndex));
            Push(ImportRequested(
                requestedName,
                level,
                fromList,
                GetFrameGlobals(_currentFrame)));
        }

        private void ImportFrom(int nameIndex)
        {
            var module = Peek(1);
            if (!IsObjectType(module, VmObjectType.Module))
                Raise("ImportError", "IMPORT_FROM requires a module object");

            var nameValue = GetCodeNameValue(GetFrameCode(_currentFrame), nameIndex);
            var namespaceDictionary = GetModuleNamespace(module);
            if (DictionaryTryGet(namespaceDictionary, nameValue, out var value))
            {
                Push(value);
                return;
            }

            var name = GetString(nameValue);
            if (IsPackageModule(module))
            {
                var fullName = GetModuleName(module) + "." + name;
                if (TryGetCachedModule(fullName, out _) || _moduleCatalog.Contains(fullName))
                {
                    Push(ImportAbsolute(fullName, out _));
                    return;
                }
            }

            Raise(
                "ImportError",
                $"cannot import name '{name}' from '{GetModuleName(module)}'");
        }

        private VmValue ImportRequested(string requestedName, int level, VmValue fromList, VmValue globals)
        {
            var hostRootBase = PushHostRoots(fromList, globals, VmValue.Null);
            try
            {
                var fullName = level == 0
                    ? requestedName
                    : ResolveRelativeImportName(requestedName, level, globals);
                if (string.IsNullOrEmpty(fullName))
                    Raise("ImportError", "empty module name");

                var imported = ImportAbsolute(fullName, out var topLevel);
                return HasImportFromList(fromList) ? imported : topLevel;
            }
            finally
            {
                PopHostRoots(hostRootBase);
            }
        }

        private VmValue ImportAbsolute(string fullName, out VmValue topLevel)
        {
            if (!IsValidModuleName(fullName))
                Raise("ModuleNotFoundError", $"No module named '{fullName}'");

            topLevel = VmValue.Null;
            var parent = VmValue.Null;
            var segmentStart = 0;
            while (segmentStart < fullName.Length)
            {
                var separator = fullName.IndexOf('.', segmentStart);
                var segmentEnd = separator < 0 ? fullName.Length : separator;
                var prefix = fullName[..segmentEnd];
                if (!parent.IsNull && !IsPackageModule(parent))
                {
                    Raise(
                        "ModuleNotFoundError",
                        $"No module named '{prefix}'; '{GetModuleName(parent)}' is not a package");
                }

                VmValue module;
                var hostRootBase = PushHostRoots(parent, topLevel, VmValue.Null);
                try
                {
                    module = LoadSingleModule(prefix);
                }
                finally
                {
                    PopHostRoots(hostRootBase);
                }
                if (topLevel.IsNull)
                    topLevel = module;
                if (!parent.IsNull)
                {
                    SetDictionaryString(
                        GetModuleNamespace(parent),
                        fullName[segmentStart..segmentEnd],
                        module);
                }

                parent = module;
                if (separator < 0)
                    break;
                segmentStart = separator + 1;
            }

            return parent;
        }

        private string ResolveRelativeImportName(
            string requestedName,
            int level,
            VmValue globals)
        {
            RequireObjectType(globals, VmObjectType.Dictionary);
            var package = GetImportPackage(globals);
            if (string.IsNullOrEmpty(package))
                Raise("ImportError", "attempted relative import with no known parent package");

            var anchor = package;
            for (var currentLevel = 1; currentLevel < level; currentLevel++)
            {
                var separator = anchor.LastIndexOf('.');
                if (separator < 0)
                    Raise("ImportError", "attempted relative import beyond top-level package");
                anchor = anchor[..separator];
            }

            return string.IsNullOrEmpty(requestedName)
                ? anchor
                : anchor + "." + requestedName;
        }

        private string GetImportPackage(VmValue globals)
        {
            if (TryGetDictionaryString(globals, "__package__", out var packageValue))
            {
                if (packageValue.IsNone)
                    return GetImportPackageFromName(globals);
                if (!IsObjectType(packageValue, VmObjectType.String))
                    Raise("TypeError", "package must be a string");
                return GetString(packageValue);
            }
            return GetImportPackageFromName(globals);
        }

        private string GetImportPackageFromName(VmValue globals)
        {
            if (!TryGetDictionaryString(globals, "__name__", out var nameValue) ||
                !IsObjectType(nameValue, VmObjectType.String))
            {
                return string.Empty;
            }

            var name = GetString(nameValue);
            if (TryGetDictionaryString(globals, "__path__", out _))
                return name;
            var separator = name.LastIndexOf('.');
            return separator < 0 ? string.Empty : name[..separator];
        }

        private int GetImportLevel(VmValue value)
        {
            if (!IsInteger(value))
                Raise("TypeError", "level must be an integer");
            var level = GetInteger(value);
            if (level < 0)
                Raise("ValueError", "level must be >= 0");
            if (level > int.MaxValue)
                Raise("OverflowError", "import level is too large");
            return (int)level;
        }

        private bool HasImportFromList(VmValue fromList)
        {
            if (fromList.IsNone)
                return false;
            if (IsObjectType(fromList, VmObjectType.Tuple))
                return GetTupleCount(fromList) != 0;
            if (IsObjectType(fromList, VmObjectType.List))
                return GetListCount(fromList) != 0;
            Raise("TypeError", "fromlist must be a tuple or list");
            return false;
        }

        private bool IsValidModuleName(string name)
        {
            if (name.Length == 0)
                return false;
            var segmentStart = 0;
            var charactersUntilCancellationPoll = 256;
            for (var index = 0; index <= name.Length; index++)
            {
                if (--charactersUntilCancellationPoll == 0)
                {
                    PollCancellation();
                    charactersUntilCancellationPoll = 256;
                }
                if (index != name.Length && name[index] != '.')
                    continue;
                if (index == segmentStart || !IsModuleIdentifierStart(name[segmentStart]))
                    return false;
                for (var part = segmentStart + 1; part < index; part++)
                {
                    if (--charactersUntilCancellationPoll == 0)
                    {
                        PollCancellation();
                        charactersUntilCancellationPoll = 256;
                    }
                    if (!IsModuleIdentifierPart(name[part]))
                        return false;
                }
                segmentStart = index + 1;
            }
            return true;
        }

        private static bool IsModuleIdentifierStart(char value)
        {
            return value == '_' || char.IsLetter(value);
        }

        private static bool IsModuleIdentifierPart(char value)
        {
            return value == '_' || char.IsLetterOrDigit(value);
        }

        private VmValue LoadName(int operand, bool globalOnly)
        {
            var name = GetCodeNameValue(GetFrameCode(_currentFrame), operand);
            if (!globalOnly)
            {
                var locals = GetFrameLocalsMapping(_currentFrame);
                if (!locals.IsNull && DictionaryTryGet(locals, name, out var localValue))
                    return localValue;
            }

            var globals = GetFrameGlobals(_currentFrame);
            if (DictionaryTryGet(globals, name, out var value))
                return value;
            if (DictionaryTryGet(_builtins, name, out value))
                return value;
            var nameText = GetString(name);
            Raise("NameError", $"name '{nameText}' is not defined");
            return VmValue.None;
        }

        private void StoreName(int operand, VmValue value)
        {
            var name = GetCodeNameValue(GetFrameCode(_currentFrame), operand);
            var locals = GetFrameLocalsMapping(_currentFrame);
            DictionarySet(locals.IsNull ? GetFrameGlobals(_currentFrame) : locals, name, value, rejectDuplicate: false);
        }

        private void StoreGlobal(int operand, VmValue value)
        {
            var name = GetCodeNameValue(GetFrameCode(_currentFrame), operand);
            DictionarySet(GetFrameGlobals(_currentFrame), name, value, rejectDuplicate: false);
        }

        private void DeleteName(int operand)
        {
            var name = GetCodeNameValue(GetFrameCode(_currentFrame), operand);
            var locals = GetFrameLocalsMapping(_currentFrame);
            var target = locals.IsNull ? GetFrameGlobals(_currentFrame) : locals;
            if (!DictionaryDelete(target, name))
                Raise("NameError", $"name '{GetString(name)}' is not defined");
        }

        private void DeleteGlobal(int operand)
        {
            var name = GetCodeNameValue(GetFrameCode(_currentFrame), operand);
            if (!DictionaryDelete(GetFrameGlobals(_currentFrame), name))
                Raise("NameError", $"name '{GetString(name)}' is not defined");
        }

        private VmValue GetCodeNameValue(VmValue code, int index)
        {
            var names = ReadCodeValue(code, 48);
            return GetTupleItem(names, index);
        }

        private VmValue GetLocalNameValue(int frame, int index)
        {
            var names = ReadCodeValue(GetFrameCode(frame), 56);
            if ((uint)index >= (uint)GetTupleCount(names))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Locals-plus name index is out of range.");
            return GetTupleItem(names, index);
        }

        private string GetLocalName(int frame, int index)
        {
            var code = GetFrameCode(frame);
            var names = ReadCodeValue(code, 56);
            if ((uint)index >= (uint)GetTupleCount(names))
                return $"<local {index}>";
            return GetString(GetTupleItem(names, index));
        }

        private string GetCodeName(VmValue code)
        {
            return GetString(ReadCodeValue(code, 72));
        }

        private void LoadSuperAttribute(int operand)
        {
            var receiver = Pop();
            var startClass = Pop();
            var globalSuper = Pop();
            var explicitArguments = (operand & 2) != 0;
            var arguments = CreateTuple(explicitArguments ? 2 : 0);
            if (explicitArguments)
            {
                SetTupleItem(arguments, 0, startClass);
                SetTupleItem(arguments, 1, receiver);
            }

            var superObject = ExecuteCallableSynchronously(
                globalSuper,
                arguments,
                explicitArguments ? 2 : 0,
                VmValue.None);
            Push(superObject);

            var nameIndex = operand >> 2;
            var methodProtocol = operand & 1;
            LoadAttribute((nameIndex << 1) | methodProtocol);
        }

        private void LoadAttribute(int operand)
        {
            var owner = Pop();
            var methodProtocol = (operand & 1) != 0;
            var nameValue = GetCodeNameValue(GetFrameCode(_currentFrame), operand >> 1);
            Push(GetAttributeValue(owner, nameValue));
            if (methodProtocol)
                Push(VmValue.Null);
        }

        private VmValue GetAttributeValue(VmValue owner, VmValue nameValue)
        {
            RequireObjectType(nameValue, VmObjectType.String);
            var name = GetString(nameValue);
            if (!owner.IsAddress)
                return RaiseValue("AttributeError", $"'{GetTypeName(owner)}' object has no attribute '{name}'");

            switch (_memory.GetObjectType(owner))
            {
                case VmObjectType.List:
                    return name switch
                    {
                        "append" => CreateBoundMethod(VmBoundMethod.ListAppend, owner),
                        "extend" => CreateBoundMethod(VmBoundMethod.ListExtend, owner),
                        "pop" => CreateBoundMethod(VmBoundMethod.ListPop, owner),
                        _ => RaiseValue("AttributeError", $"'list' object has no attribute '{name}'"),
                    };

                case VmObjectType.Dictionary:
                    return name switch
                    {
                        "get" => CreateBoundMethod(VmBoundMethod.DictionaryGet, owner),
                        "keys" => CreateBoundMethod(VmBoundMethod.DictionaryKeys, owner),
                        "values" => CreateBoundMethod(VmBoundMethod.DictionaryValues, owner),
                        _ => RaiseValue("AttributeError", $"'dict' object has no attribute '{name}'"),
                    };

                case VmObjectType.MappingProxy:
                    {
                        var dictionary = GetMappingProxyDictionary(owner);
                        return name switch
                        {
                            "get" => CreateBoundMethod(VmBoundMethod.DictionaryGet, dictionary),
                            "keys" => CreateBoundMethod(VmBoundMethod.DictionaryKeys, dictionary),
                            "values" => CreateBoundMethod(VmBoundMethod.DictionaryValues, dictionary),
                            _ => RaiseValue("AttributeError", $"'mappingproxy' object has no attribute '{name}'"),
                        };
                    }

                case VmObjectType.Set:
                    return name switch
                    {
                        "add" => CreateBoundMethod(VmBoundMethod.SetAdd, owner),
                        "discard" => CreateBoundMethod(VmBoundMethod.SetDiscard, owner),
                        _ => RaiseValue("AttributeError", $"'set' object has no attribute '{name}'"),
                    };

                case VmObjectType.String:
                    return name switch
                    {
                        "startswith" => CreateBoundMethod(VmBoundMethod.StringStartsWith, owner),
                        "endswith" => CreateBoundMethod(VmBoundMethod.StringEndsWith, owner),
                        _ => RaiseValue("AttributeError", $"'str' object has no attribute '{name}'"),
                    };

                case VmObjectType.Iterator:
                case VmObjectType.BuiltinIterator:
                    return name switch
                    {
                        "__iter__" => CreateBoundMethod(VmBoundMethod.IteratorIter, owner),
                        "__next__" => CreateBoundMethod(VmBoundMethod.IteratorNext, owner),
                        _ => RaiseValue("AttributeError", $"'{GetTypeName(owner)}' object has no attribute '{name}'"),
                    };

                case VmObjectType.Instance:
                    {
                        var instanceClass = GetInstanceClass(owner);
                        var hasClassAttribute = TryLookupClassAttribute(instanceClass, nameValue, out var classAttribute);
                        if (hasClassAttribute && IsObjectType(classAttribute, VmObjectType.Property))
                            return BindClassAttribute(classAttribute, owner, instanceClass);
                        if (string.Equals(name, "__class__", StringComparison.Ordinal))
                            return instanceClass;
                        if (string.Equals(name, "__dict__", StringComparison.Ordinal))
                            return GetInstanceDictionary(owner);
                        if (DictionaryTryGet(GetInstanceDictionary(owner), nameValue, out var instanceAttribute))
                            return instanceAttribute;
                        if (!hasClassAttribute)
                            return RaiseValue("AttributeError", $"'{GetClassName(instanceClass)}' object has no attribute '{name}'");
                        return BindClassAttribute(classAttribute, owner, instanceClass);
                    }

                case VmObjectType.Class:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        if (name == "__name__") return _memory.ReadValue(payload + 0);
                        if (name == "__qualname__") return _memory.ReadValue(payload + 8);
                        if (name == "__dict__") return _memory.ReadValue(payload + 56);
                        if (name == "__bases__") return _memory.ReadValue(payload + 24);
                        if (name == "__mro__") return _memory.ReadValue(payload + 32);
                        if (name == "__class__") return _memory.ReadValue(payload + 40);
                        if (name == "__module__") return _memory.ReadValue(payload + 48);
                        if (!TryLookupClassAttribute(owner, nameValue, out var classAttribute))
                            return RaiseValue("AttributeError", $"type object '{GetClassName(owner)}' has no attribute '{name}'");
                        return BindClassAttribute(classAttribute, VmValue.Null, owner);
                    }

                case VmObjectType.PythonBoundMethod:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        return name switch
                        {
                            "__func__" => _memory.ReadValue(payload),
                            "__self__" => _memory.ReadValue(payload + 8),
                            _ => RaiseValue("AttributeError", $"'method' object has no attribute '{name}'"),
                        };
                    }

                case VmObjectType.Super:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        var startClass = _memory.ReadValue(payload);
                        var receiver = _memory.ReadValue(payload + 8);
                        if (name == "__thisclass__") return startClass;
                        if (name == "__self__") return receiver;
                        if (name == "__self_class__") return GetSuperReceiverClass(receiver);
                        if (!TryLookupSuperAttribute(owner, nameValue, out var superAttribute))
                            return RaiseValue("AttributeError", $"'super' object has no attribute '{name}'");
                        return BindClassAttribute(superAttribute, receiver, GetSuperReceiverClass(receiver));
                    }

                case VmObjectType.Function:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        if (name == "__name__") return _memory.ReadValue(payload + 40);
                        if (name == "__defaults__") return _memory.ReadValue(payload + 16);
                        if (name == "__kwdefaults__") return _memory.ReadValue(payload + 24);
                        if (name == "__closure__") return _memory.ReadValue(payload + 32);
                        if (DictionaryTryGet(_memory.ReadValue(payload + 48), nameValue, out var dynamicAttribute))
                            return dynamicAttribute;
                        return RaiseValue("AttributeError", $"'function' object has no attribute '{name}'");
                    }

                case VmObjectType.StaticMethod:
                case VmObjectType.ClassMethod:
                    if (name is "__func__" or "__wrapped__")
                        return GetDescriptorCallable(owner);
                    return RaiseValue("AttributeError", $"'{GetTypeName(owner)}' object has no attribute '{name}'");

                case VmObjectType.Property:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        return name switch
                        {
                            "fget" => _memory.ReadValue(payload),
                            "fset" => _memory.ReadValue(payload + 8),
                            "fdel" => _memory.ReadValue(payload + 16),
                            "__doc__" => _memory.ReadValue(payload + 24),
                            "getter" => CreateBoundMethod(VmBoundMethod.PropertyGetter, owner),
                            "setter" => CreateBoundMethod(VmBoundMethod.PropertySetter, owner),
                            "deleter" => CreateBoundMethod(VmBoundMethod.PropertyDeleter, owner),
                            _ => RaiseValue("AttributeError", $"'property' object has no attribute '{name}'"),
                        };
                    }

                case VmObjectType.Cell:
                    if (name != "cell_contents")
                        return RaiseValue("AttributeError", $"'cell' object has no attribute '{name}'");
                    {
                        var value = GetCellValue(owner);
                        return value.IsNull ? RaiseValue("ValueError", "Cell is empty") : value;
                    }

                case VmObjectType.Generator:
                    return name switch
                    {
                        "__iter__" => CreateBoundMethod(VmBoundMethod.GeneratorIter, owner),
                        "__next__" => CreateBoundMethod(VmBoundMethod.GeneratorNext, owner),
                        "send" => CreateBoundMethod(VmBoundMethod.GeneratorSend, owner),
                        "gi_code" => _memory.ReadValue(_memory.GetObjectPayloadAddress(owner)),
                        _ => RaiseValue("AttributeError", $"'generator' object has no attribute '{name}'"),
                    };

                case VmObjectType.Interpolation:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        return name switch
                        {
                            "value" => _memory.ReadValue(payload),
                            "expression" => _memory.ReadValue(payload + 8),
                            "conversion" => _memory.ReadValue(payload + 16),
                            "format_spec" => _memory.ReadValue(payload + 24),
                            _ => RaiseValue("AttributeError", $"'Interpolation' object has no attribute '{name}'"),
                        };
                    }

                case VmObjectType.Template:
                    {
                        var payload = _memory.GetObjectPayloadAddress(owner);
                        return name switch
                        {
                            "strings" => _memory.ReadValue(payload),
                            "interpolations" => _memory.ReadValue(payload + 8),
                            "values" => GetTemplateValues(owner),
                            _ => RaiseValue("AttributeError", $"'Template' object has no attribute '{name}'"),
                        };
                    }

                case VmObjectType.Module:
                    if (DictionaryTryGet(GetModuleNamespace(owner), nameValue, out var moduleAttribute))
                        return moduleAttribute;
                    return RaiseValue("AttributeError", $"module '{GetModuleName(owner)}' has no attribute '{name}'");

                default:
                    return RaiseValue("AttributeError", $"'{GetTypeName(owner)}' object has no attribute '{name}'");
            }
        }

        private void StoreAttribute(VmValue owner, int nameIndex, VmValue value)
        {
            SetAttributeValue(owner, GetCodeNameValue(GetFrameCode(_currentFrame), nameIndex), value);
        }

        private void SetAttributeValue(VmValue owner, VmValue nameValue, VmValue value)
        {
            RequireObjectType(nameValue, VmObjectType.String);
            var name = GetString(nameValue);
            if (IsObjectType(owner, VmObjectType.Cell))
            {
                if (name != "cell_contents")
                    Raise("AttributeError", $"'cell' object has no writable attribute '{name}'");
                SetCellValue(owner, value);
                return;
            }
            if (IsObjectType(owner, VmObjectType.Module))
            {
                if (name == "__dict__") Raise("AttributeError", "readonly attribute");
                DictionarySet(GetModuleNamespace(owner), nameValue, value, rejectDuplicate: false);
                return;
            }
            if (IsObjectType(owner, VmObjectType.Instance))
            {
                if (name is "__class__" or "__dict__") Raise("AttributeError", "readonly attribute");
                if (TryLookupClassAttribute(GetInstanceClass(owner), nameValue, out var descriptor) &&
                    IsObjectType(descriptor, VmObjectType.Property))
                {
                    var setter = _memory.ReadValue(_memory.GetObjectPayloadAddress(descriptor) + 8);
                    if (setter.IsNone) Raise("AttributeError", "property has no setter");
                    var arguments = CreateTuple(2);
                    SetTupleItem(arguments, 0, owner);
                    SetTupleItem(arguments, 1, value);
                    _ = ExecuteCallableSynchronously(setter, arguments, 2, VmValue.None);
                    return;
                }
                DictionarySet(GetInstanceDictionary(owner), nameValue, value, rejectDuplicate: false);
                return;
            }
            if (IsObjectType(owner, VmObjectType.Class))
            {
                var classPayload = _memory.GetObjectPayloadAddress(owner);
                switch (name)
                {
                    case "__dict__":
                    case "__bases__":
                    case "__mro__":
                    case "__class__":
                        Raise("AttributeError", "readonly attribute"); return;
                    case "__name__":
                        RequireObjectType(value, VmObjectType.String); _memory.WriteValue(classPayload, value); return;
                    case "__qualname__":
                        RequireObjectType(value, VmObjectType.String); _memory.WriteValue(classPayload + 8, value); return;
                    case "__module__":
                        RequireObjectType(value, VmObjectType.String);
                        _memory.WriteValue(classPayload + 48, value);
                        DictionarySet(GetClassNamespace(owner), nameValue, value, rejectDuplicate: false);
                        return;
                    default:
                        DictionarySet(GetClassNamespace(owner), nameValue, value, rejectDuplicate: false); return;
                }
            }
            if (!IsObjectType(owner, VmObjectType.Function))
                Raise("AttributeError", $"'{GetTypeName(owner)}' object has no writable attribute '{name}'");
            var payload = _memory.GetObjectPayloadAddress(owner);
            switch (name)
            {
                case "__name__": RequireObjectType(value, VmObjectType.String); _memory.WriteValue(payload + 40, value); return;
                case "__defaults__":
                    if (!value.IsNone) RequireObjectType(value, VmObjectType.Tuple);
                    _memory.WriteValue(payload + 16, value); return;
                case "__kwdefaults__":
                    if (!value.IsNone) RequireObjectType(value, VmObjectType.Dictionary);
                    _memory.WriteValue(payload + 24, value); return;
                case "__closure__": Raise("AttributeError", "readonly attribute"); return;
                default: DictionarySet(_memory.ReadValue(payload + 48), nameValue, value, rejectDuplicate: false); return;
            }
        }

        private void DeleteAttribute(VmValue owner, int nameIndex)
        {
            DeleteAttributeValue(owner, GetCodeNameValue(GetFrameCode(_currentFrame), nameIndex));
        }

        private void DeleteAttributeValue(VmValue owner, VmValue nameValue)
        {
            RequireObjectType(nameValue, VmObjectType.String);
            var name = GetString(nameValue);
            if (IsObjectType(owner, VmObjectType.Cell))
            {
                if (name != "cell_contents") Raise("AttributeError", $"'cell' object has no deletable attribute '{name}'");
                SetCellValue(owner, VmValue.Null); return;
            }
            if (IsObjectType(owner, VmObjectType.Module))
            {
                if (name == "__dict__") Raise("AttributeError", "readonly attribute");
                if (!DictionaryDelete(GetModuleNamespace(owner), nameValue))
                    Raise("AttributeError", $"module '{GetModuleName(owner)}' has no attribute '{name}'");
                return;
            }
            if (IsObjectType(owner, VmObjectType.Instance))
            {
                if (name is "__class__" or "__dict__") Raise("AttributeError", "readonly attribute");
                if (TryLookupClassAttribute(GetInstanceClass(owner), nameValue, out var descriptor) &&
                    IsObjectType(descriptor, VmObjectType.Property))
                {
                    var deleter = _memory.ReadValue(_memory.GetObjectPayloadAddress(descriptor) + 16);
                    if (deleter.IsNone) Raise("AttributeError", "property has no deleter");
                    var arguments = CreateTuple(1);
                    SetTupleItem(arguments, 0, owner);
                    _ = ExecuteCallableSynchronously(deleter, arguments, 1, VmValue.None);
                    return;
                }
                if (!DictionaryDelete(GetInstanceDictionary(owner), nameValue))
                    Raise("AttributeError", $"'{GetClassName(GetInstanceClass(owner))}' object has no attribute '{name}'");
                return;
            }
            if (IsObjectType(owner, VmObjectType.Class))
            {
                if (name is "__name__" or "__qualname__" or "__module__" or "__dict__" or "__bases__" or "__mro__" or "__class__")
                    Raise("TypeError", $"cannot delete {name}");
                if (!DictionaryDelete(GetClassNamespace(owner), nameValue))
                    Raise("AttributeError", $"type object '{GetClassName(owner)}' has no attribute '{name}'");
                return;
            }
            if (!IsObjectType(owner, VmObjectType.Function))
                Raise("AttributeError", $"'{GetTypeName(owner)}' object has no deletable attribute '{name}'");
            var payload = _memory.GetObjectPayloadAddress(owner);
            switch (name)
            {
                case "__defaults__": _memory.WriteValue(payload + 16, VmValue.None); return;
                case "__kwdefaults__": _memory.WriteValue(payload + 24, VmValue.None); return;
                case "__name__": Raise("TypeError", "__name__ must be set to a string object"); return;
                case "__closure__": Raise("AttributeError", "readonly attribute"); return;
                default:
                    if (!DictionaryDelete(_memory.ReadValue(payload + 48), nameValue))
                        Raise("AttributeError", $"'function' object has no attribute '{name}'");
                    return;
            }
        }

        private VmValue CallIntrinsic1(PythonIntrinsic1 intrinsic, VmValue argument)
        {
            switch (intrinsic)
            {
                case PythonIntrinsic1.Print:
                    AppendOutput(Str(argument) + "\n");
                    return VmValue.None;
                case PythonIntrinsic1.UnaryPositive:
                    if (IsInteger(argument))
                        return CreateInteger(GetInteger(argument));
                    if (IsFloat(argument))
                        return CreateFloat(GetFloat(argument));
                    Raise("TypeError", $"bad operand type for unary +: '{GetTypeName(argument)}'");
                    return VmValue.None;
                case PythonIntrinsic1.ListToTuple:
                    RequireObjectType(argument, VmObjectType.List);
                    return MaterializeTuple(argument);
                default:
                    throw new VmTrapException(
                        VmStopReason.UnsupportedOpcode,
                        $"Intrinsic {intrinsic} is not implemented by the safe VM.");
            }
        }

        private void UnpackSequence(VmValue sequence, int count)
        {
            if (count < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative UNPACK_SEQUENCE count.");
            var values = MaterializeList(sequence);
            var actual = GetListCount(values);
            if (actual < count)
                Raise("ValueError", $"not enough values to unpack (expected {count}, got {actual})");
            if (actual > count)
                Raise("ValueError", $"too many values to unpack (expected {count})");
            for (var index = count - 1; index >= 0; index--)
                Push(GetListItem(values, index));
        }

        private void UnpackExtended(VmValue sequence, int before, int after)
        {
            if (before < 0 || after < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative UNPACK_EX count.");
            var values = MaterializeList(sequence);
            var actual = GetListCount(values);
            var required = checked(before + after);
            if (actual < required)
                Raise("ValueError", $"not enough values to unpack (expected at least {required}, got {actual})");

            var starredCount = actual - required;
            var starred = CreateList(starredCount);
            for (var index = 0; index < starredCount; index++)
                ListAdd(starred, GetListItem(values, before + index));

            for (var index = after - 1; index >= 0; index--)
                Push(GetListItem(values, actual - after + index));
            Push(starred);
            for (var index = before - 1; index >= 0; index--)
                Push(GetListItem(values, index));
        }

        private bool TryGetInt32(VmValue value, out int result)
        {
            result = 0;
            if (!IsInteger(value))
                return false;
            var integer = GetInteger(value);
            if (integer < int.MinValue || integer > int.MaxValue)
                return false;
            result = (int)integer;
            return true;
        }

        private void PushExceptionInfo()
        {
            var exception = Pop();
            if (!IsObjectType(exception, VmObjectType.Exception))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "PUSH_EXC_INFO requires an exception instance.");
            var previous = GetFrameHandledException(_currentFrame);
            Push(previous.IsNull ? VmValue.None : previous);
            Push(exception);
            SetFrameHandledException(_currentFrame, exception);
        }

        private void PopExcept()
        {
            var previous = Pop();
            if (!previous.IsNone && !IsObjectType(previous, VmObjectType.Exception))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "POP_EXCEPT requires None or an exception instance.");
            SetFrameHandledException(_currentFrame, previous);
        }

        private void CheckExceptionMatch()
        {
            var expected = Pop();
            var exception = Peek(1);
            if (!IsObjectType(exception, VmObjectType.Exception))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "CHECK_EXC_MATCH requires an exception instance.");
            Push(ExceptionMatches(exception, expected) ? VmValue.True : VmValue.False);
        }

        private void Reraise(int operand)
        {
            if ((uint)operand > 2u)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "RERAISE operand must be between zero and two.");

            var exception = Peek(1);
            if (!IsObjectType(exception, VmObjectType.Exception))
                throw new VmTrapException(VmStopReason.InvalidBytecode, "RERAISE requires an exception instance.");

            // Handler lookup remains anchored at this RERAISE instruction
            // A nonzero operand only changes the origin propagated to a later handler
            var originInstruction = GetFrameLastInstruction(_currentFrame);
            if (operand != 0)
            {
                var lastInstruction = Peek(operand + 1);
                if (!TryGetInt32(lastInstruction, out originInstruction) || originInstruction < 0)
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "RERAISE last-instruction value is invalid.");
            }

            _ = Pop();
            SignalException(exception, originInstruction);
        }

        private void RaiseVariableArguments(int operand)
        {
            switch (operand)
            {
                case 0:
                    {
                        var active = GetFrameHandledException(_currentFrame);
                        if (active.IsNone || active.IsNull)
                            Raise("RuntimeError", "No active exception to reraise");
                        SignalException(active);
                        return;
                    }
                case 1:
                    SignalException(Pop());
                    return;
                case 2:
                    {
                        var cause = Pop();
                        var exception = Pop();
                        if (!cause.IsNone && !IsExceptionClassOrInstance(cause))
                            Raise("TypeError", "exception causes must derive from BaseException");
                        SignalException(exception);
                        return;
                    }
                default:
                    throw new VmTrapException(VmStopReason.InvalidBytecode, "RAISE_VARARGS operand must be between zero and two.");
            }
        }

        private bool IsExceptionClassOrInstance(VmValue value)
        {
            return (value.IsBuiltin && IsExceptionBuiltin(value.Builtin)) ||
                   IsObjectType(value, VmObjectType.Exception);
        }

        private void SignalException(VmValue exception)
        {
            var originInstruction = _currentFrame == 0
                ? -1
                : GetFrameLastInstruction(_currentFrame);
            SignalException(exception, originInstruction);
        }

        private void SignalException(VmValue exception, int originInstruction)
        {
            if (exception.IsBuiltin && IsExceptionBuiltin(exception.Builtin))
            {
                _raisedException = CreateException(GetBuiltinName(exception.Builtin), string.Empty);
                _raisedLastInstruction = originInstruction;
                throw new VmGuestExceptionSignal();
            }
            if (IsObjectType(exception, VmObjectType.Exception))
            {
                _raisedException = exception;
                _raisedLastInstruction = originInstruction;
                throw new VmGuestExceptionSignal();
            }
            Raise("TypeError", "exceptions must derive from BaseException");
        }

        private bool ExceptionMatches(VmValue exception, VmValue expected)
        {
            return ExceptionMatches(exception, expected, nestingDepth: 0);
        }

        private bool ExceptionMatches(VmValue exception, VmValue expected, int nestingDepth)
        {
            if (IsObjectType(expected, VmObjectType.Tuple))
            {
                if (nestingDepth >= Math.Min(_limits.MaxCallDepth, 256))
                {
                    throw new VmTrapException(
                        VmStopReason.CallDepthLimitExceeded,
                        "Exception-match tuple nesting exceeds the VM call-depth policy.");
                }

                var count = GetTupleCount(expected);
                for (var index = 0; index < count; index++)
                {
                    if (ExceptionMatches(exception, GetTupleItem(expected, index), nestingDepth + 1))
                        return true;
                    PollCancellation();
                }
                return false;
            }

            if (!expected.IsBuiltin || !IsExceptionBuiltin(expected.Builtin))
                Raise("TypeError", "catching classes that do not inherit from BaseException is not allowed");

            var payload = _memory.GetObjectPayloadAddress(exception);
            var actualName = GetString(_memory.ReadValue(payload));
            var expectedName = GetBuiltinName(expected.Builtin);
            return IsExceptionTypeOrSubclass(actualName, expectedName);
        }

        private static bool IsExceptionTypeOrSubclass(string actual, string expected)
        {
            for (var current = actual; ; current = GetExceptionBaseName(current))
            {
                if (StringComparer.Ordinal.Equals(current, expected))
                    return true;
                if (StringComparer.Ordinal.Equals(current, "BaseException"))
                    return false;
            }
        }

        private static string GetExceptionBaseName(string typeName)
        {
            return typeName switch
            {
                "BaseException" => "BaseException",
                "Exception" => "BaseException",
                "UnboundLocalError" => "NameError",
                "NameError" => "Exception",
                "KeyError" or "IndexError" => "LookupError",
                "LookupError" => "Exception",
                "ZeroDivisionError" or "OverflowError" => "ArithmeticError",
                "ArithmeticError" => "Exception",
                "NotImplementedError" => "RuntimeError",
                "RuntimeError" => "Exception",
                "ModuleNotFoundError" => "ImportError",
                "ImportError" => "Exception",
                _ => "Exception",
            };
        }

        private static bool IsExceptionBuiltin(VmBuiltin builtin)
        {
            return builtin is
                VmBuiltin.BaseException or
                VmBuiltin.Exception or
                VmBuiltin.TypeError or
                VmBuiltin.ValueError or
                VmBuiltin.RuntimeError or
                VmBuiltin.AssertionError or
                VmBuiltin.NotImplementedError or
                VmBuiltin.KeyError or
                VmBuiltin.IndexError or
                VmBuiltin.NameError or
                VmBuiltin.UnboundLocalError or
                VmBuiltin.StopIteration or
                VmBuiltin.ZeroDivisionError or
                VmBuiltin.ArithmeticError or
                VmBuiltin.LookupError or
                VmBuiltin.AttributeError or
                VmBuiltin.ImportError or
                VmBuiltin.ModuleNotFoundError or
                VmBuiltin.OverflowError or
                VmBuiltin.SystemError;
        }

        private void Raise(VmValue exception)
        {
            SignalException(exception);
        }

        private void Raise(string typeName, string message)
        {
            _raisedException = CreateException(typeName, message);
            _raisedLastInstruction = _currentFrame == 0
                ? -1
                : GetFrameLastInstruction(_currentFrame);
            throw new VmGuestExceptionSignal();
        }

        private VmValue RaiseValue(string typeName, string message)
        {
            Raise(typeName, message);
            return VmValue.None;
        }

        private ValueSnapshot Snapshot(VmValue value)
        {
            if (value.IsNull || value.IsNone)
                return new ValueSnapshot(ValueSnapshotKind.None, false, 0, 0.0, null);
            if (value.IsBoolean)
                return new ValueSnapshot(ValueSnapshotKind.Boolean, value.BooleanValue, 0, 0.0, null);
            if (value.IsEllipsis)
                return new ValueSnapshot(ValueSnapshotKind.Other, false, 0, 0.0, "Ellipsis");
            if (IsInteger(value))
            {
                var integer = GetInteger(value);
                if (integer >= long.MinValue && integer <= long.MaxValue)
                    return new ValueSnapshot(ValueSnapshotKind.Integer, false, (long)integer, 0.0, null);
                return new ValueSnapshot(ValueSnapshotKind.Integer, false, 0, 0.0, integer.ToString(CultureInfo.InvariantCulture));
            }
            if (IsFloat(value))
                return new ValueSnapshot(ValueSnapshotKind.Float, false, 0, GetFloat(value), null);
            if (IsObjectType(value, VmObjectType.String))
                return new ValueSnapshot(ValueSnapshotKind.String, false, 0, 0.0, GetString(value));
            return new ValueSnapshot(ValueSnapshotKind.Other, false, 0, 0.0, Repr(value, 0));
        }

        private VmValue ConvertValue(VmValue value, int conversion)
        {
            return conversion switch
            {
                1 => IsObjectType(value, VmObjectType.String) ? value : CreateString(Str(value)),
                2 => CreateString(Repr(value, 0)),
                3 => CreateString(Ascii(value)),
                _ => throw new VmTrapException(VmStopReason.InvalidBytecode, "CONVERT_VALUE operand is invalid."),
            };
        }

        private string Ascii(VmValue value)
        {
            var representation = Repr(value, 0);
            var builder = new StringBuilder(representation.Length);
            for (var offset = 0; offset < representation.Length;)
            {
                var codePoint = ReadCodePoint(representation, offset, out var consumed);
                offset += consumed;
                if (codePoint < 0x80)
                {
                    EnsureBoundedTextLength(builder, 1);
                    builder.Append((char)codePoint);
                }
                else if (codePoint <= 0xFF)
                {
                    EnsureBoundedTextLength(builder, 4);
                    builder.Append("\\x").Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
                }
                else if (codePoint <= 0xFFFF)
                {
                    EnsureBoundedTextLength(builder, 6);
                    builder.Append("\\u").Append(codePoint.ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    EnsureBoundedTextLength(builder, 10);
                    builder.Append("\\U").Append(codePoint.ToString("x8", CultureInfo.InvariantCulture));
                }
            }
            return builder.ToString();
        }

        private VmValue FormatValue(VmValue value, string specification)
        {
            if (IsObjectType(value, VmObjectType.Instance) && HasSpecialMethod(value, "__format__"))
            {
                EnsureHostRootCapacity(9);
                var arguments = CreateTuple(1);
                var rootBase = PushHostRoots(value, arguments, VmValue.Null);
                try
                {
                    SetTupleItem(arguments, 0, CreateString(specification));
                    var formatted = CallSpecialMethod(value, "__format__", arguments, 1);
                    if (!IsObjectType(formatted, VmObjectType.String))
                        Raise("TypeError", $"__format__ must return a str, not {GetTypeName(formatted)}");
                    return formatted;
                }
                finally
                {
                    PopHostRoots(rootBase);
                }
            }
            if (specification.Length == 0)
                return IsObjectType(value, VmObjectType.String) ? value : CreateString(Str(value));

            var format = ParseFormatSpec(specification, GetTypeName(value));
            string result;
            if (IsObjectType(value, VmObjectType.String))
            {
                result = FormatStringValue(GetString(value), format, specification);
            }
            else if (IsInteger(value))
            {
                result = FormatIntegerValue(GetInteger(value), format, specification);
            }
            else if (IsFloat(value))
            {
                result = FormatFloatValue(GetFloat(value), format, specification);
            }
            else
            {
                Raise("TypeError", $"unsupported format string passed to {GetTypeName(value)}.__format__");
                return VmValue.None;
            }
            return CreateString(result);
        }

        private struct ParsedFormatSpec
        {
            public char Fill;
            public char Align;
            public char Sign;
            public char Grouping;
            public char Type;
            public int Width;
            public int Precision;
            public bool Alternate;
            public bool Zero;
            public bool CoerceNegativeZero;
        }

        private ParsedFormatSpec ParseFormatSpec(string specification, string typeName)
        {
            var result = new ParsedFormatSpec
            {
                Fill = ' ',
                Width = -1,
                Precision = -1,
            };
            var index = 0;
            if (specification.Length >= 2 && IsAlignment(specification[1]))
            {
                result.Fill = specification[0];
                result.Align = specification[1];
                index = 2;
            }
            else if (index < specification.Length && IsAlignment(specification[index]))
            {
                result.Align = specification[index++];
            }

            if (index < specification.Length && specification[index] is '+' or '-' or ' ')
                result.Sign = specification[index++];
            if (index < specification.Length && specification[index] == 'z')
            {
                result.CoerceNegativeZero = true;
                index++;
            }
            if (index < specification.Length && specification[index] == '#')
            {
                result.Alternate = true;
                index++;
            }
            if (index < specification.Length && specification[index] == '0')
            {
                result.Zero = true;
                index++;
            }

            var widthStart = index;
            while (index < specification.Length && char.IsAsciiDigit(specification[index]))
                index++;
            if (index != widthStart)
            {
                if (!int.TryParse(specification.AsSpan(widthStart, index - widthStart), NumberStyles.None, CultureInfo.InvariantCulture, out result.Width))
                    Raise("ValueError", "Too many decimal digits in format string");
            }

            if (index < specification.Length && specification[index] is ',' or '_')
                result.Grouping = specification[index++];

            if (index < specification.Length && specification[index] == '.')
            {
                index++;
                var precisionStart = index;
                while (index < specification.Length && char.IsAsciiDigit(specification[index]))
                    index++;
                if (precisionStart == index ||
                    !int.TryParse(specification.AsSpan(precisionStart, index - precisionStart), NumberStyles.None, CultureInfo.InvariantCulture, out result.Precision))
                {
                    Raise("ValueError", "Format specifier missing precision");
                }
            }

            if (index < specification.Length)
                result.Type = specification[index++];
            if (index != specification.Length)
                Raise("ValueError", $"Invalid format specifier '{specification}' for object of type '{typeName}'");
            if (result.Zero && result.Align == '\0')
            {
                result.Fill = '0';
                result.Align = '=';
            }
            return result;
        }

        private static bool IsAlignment(char value) => value is '<' or '>' or '=' or '^';

        private string FormatStringValue(string value, ParsedFormatSpec format, string original)
        {
            if (format.Zero && format.Align == '=')
            {
                format.Zero = false;
                format.Align = '<';
                format.Fill = '0';
            }
            if (format.Type is not ('\0' or 's') || format.Sign != '\0' || format.Alternate ||
                format.Zero || format.Grouping != '\0' || format.CoerceNegativeZero || format.Align == '=')
            {
                Raise("ValueError", $"Invalid format specifier '{original}' for object of type 'str'");
            }
            if (format.Precision >= 0)
            {
                var count = CountCodePoints(value);
                if (count > format.Precision)
                    value = value[..GetCodePointUtf16Offset(value, format.Precision)];
            }
            return ApplyAlignment(value, format, defaultAlignment: '<', prefixLength: 0);
        }

        private string FormatIntegerValue(BigInteger value, ParsedFormatSpec format, string original)
        {
            if (format.Precision >= 0)
                Raise("ValueError", "Precision not allowed in integer format specifier");
            if (format.CoerceNegativeZero)
                Raise("ValueError", "Negative zero coercion (z) not allowed in integer format specifier");

            var type = format.Type == '\0' ? 'd' : format.Type;
            if (type == 'c')
            {
                if (format.Sign != '\0' || format.Alternate || format.Grouping != '\0' || format.Zero)
                    Raise("ValueError", $"Invalid format specifier '{original}' for object of type 'int'");
                if (value < 0 || value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF))
                    Raise("OverflowError", "%c arg not in range(0x110000)");
                return ApplyAlignment(char.ConvertFromUtf32((int)value), format, defaultAlignment: '>', prefixLength: 0);
            }

            var negative = value.Sign < 0;
            var magnitude = BigInteger.Abs(value);
            string digits;
            string prefix;
            var groupingSize = 0;
            switch (type)
            {
                case 'b':
                    digits = FormatUnsignedInteger(magnitude, 2, upper: false);
                    prefix = format.Alternate ? "0b" : string.Empty;
                    groupingSize = 4;
                    break;
                case 'o':
                    digits = FormatUnsignedInteger(magnitude, 8, upper: false);
                    prefix = format.Alternate ? "0o" : string.Empty;
                    groupingSize = 4;
                    break;
                case 'x':
                case 'X':
                    digits = FormatUnsignedInteger(magnitude, 16, upper: type == 'X');
                    prefix = format.Alternate ? (type == 'X' ? "0X" : "0x") : string.Empty;
                    groupingSize = 4;
                    break;
                case 'd':
                case 'n':
                    digits = magnitude.ToString(CultureInfo.InvariantCulture);
                    prefix = string.Empty;
                    groupingSize = 3;
                    break;
                default:
                    Raise("ValueError", $"Unknown format code '{type}' for object of type 'int'");
                    return string.Empty;
            }

            if (format.Grouping == ',' && type is not ('d' or 'n'))
                Raise("ValueError", $"Cannot specify ',' with '{type}'.");
            if (format.Grouping != '\0')
                digits = GroupDigits(digits, groupingSize, format.Grouping);

            var sign = negative ? "-" : format.Sign == '+' ? "+" : format.Sign == ' ' ? " " : string.Empty;
            var text = sign + prefix + digits;
            return ApplyAlignment(text, format, defaultAlignment: '>', prefixLength: sign.Length + prefix.Length);
        }

        private string FormatFloatValue(double value, ParsedFormatSpec format, string original)
        {
            var unspecifiedType = format.Type == '\0';
            var type = unspecifiedType ? 'g' : format.Type;
            if (type is not ('e' or 'E' or 'f' or 'F' or 'g' or 'G' or 'n' or '%'))
            {
                Raise("ValueError", $"Unknown format code '{type}' for object of type 'float'");
                return string.Empty;
            }

            var negative = BitConverter.DoubleToInt64Bits(value) < 0;
            if (format.CoerceNegativeZero && value == 0.0)
                negative = false;
            var magnitude = Math.Abs(value);
            string digits;
            if (double.IsNaN(magnitude))
            {
                digits = type is 'E' or 'F' or 'G' ? "NAN" : "nan";
                negative = false;
            }
            else if (double.IsPositiveInfinity(magnitude))
            {
                digits = type is 'E' or 'F' or 'G' ? "INF" : "inf";
            }
            else
            {
                if (unspecifiedType && format.Precision < 0)
                {
                    digits = FormatPositiveFloatRepr(magnitude);
                }
                else
                {
                    var precision = format.Precision >= 0 ? format.Precision : 6;
                    switch (type)
                    {
                        case 'f':
                        case 'F':
                            digits = magnitude.ToString("F" + precision, CultureInfo.InvariantCulture);
                            break;
                        case 'e':
                        case 'E':
                            digits = NormalizeExponent(magnitude.ToString("E" + precision, CultureInfo.InvariantCulture), type == 'e');
                            break;
                        case '%':
                            digits = (magnitude * 100.0).ToString("F" + precision, CultureInfo.InvariantCulture) + "%";
                            break;
                        default:
                            precision = precision == 0 ? 1 : precision;
                            digits = NormalizeExponent(magnitude.ToString("G" + precision, CultureInfo.InvariantCulture), type is 'g' or 'n');
                            if (type == 'G')
                                digits = digits.ToUpperInvariant();
                            break;
                    }
                }
            }

            if (format.Alternate && !digits.Contains('.') && !digits.Contains("inf", StringComparison.OrdinalIgnoreCase) &&
                !digits.Contains("nan", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = digits.EndsWith('%') ? "%" : string.Empty;
                if (suffix.Length != 0)
                    digits = digits[..^1];
                var exponent = digits.IndexOfAny(['e', 'E']);
                digits = exponent < 0 ? digits + "." : digits.Insert(exponent, ".");
                digits += suffix;
            }
            if (format.Grouping != '\0')
                digits = GroupFloatDigits(digits, format.Grouping);

            var sign = negative ? "-" : format.Sign == '+' ? "+" : format.Sign == ' ' ? " " : string.Empty;
            var text = sign + digits;
            return ApplyAlignment(text, format, defaultAlignment: '>', prefixLength: sign.Length);
        }

        private string ApplyAlignment(string text, ParsedFormatSpec format, char defaultAlignment, int prefixLength)
        {
            if (format.Width < 0)
                return text;
            var padding = format.Width - CountCodePoints(text);
            if (padding <= 0)
                return text;
            var fill = new string(format.Fill, padding);
            var alignment = format.Align == '\0' ? defaultAlignment : format.Align;
            return alignment switch
            {
                '<' => text + fill,
                '>' => fill + text,
                '^' => new string(format.Fill, padding / 2) + text + new string(format.Fill, padding - padding / 2),
                '=' => text.Insert(prefixLength, fill),
                _ => text,
            };
        }

        private static string FormatUnsignedInteger(BigInteger value, int radix, bool upper)
        {
            if (value.IsZero)
                return "0";
            const string lowerDigits = "0123456789abcdef";
            const string upperDigits = "0123456789ABCDEF";
            var alphabet = upper ? upperDigits : lowerDigits;
            var builder = new StringBuilder();
            while (!value.IsZero)
            {
                value = BigInteger.DivRem(value, radix, out var remainder);
                builder.Append(alphabet[(int)remainder]);
            }
            var characters = builder.ToString().ToCharArray();
            Array.Reverse(characters);
            return new string(characters);
        }

        private static string GroupDigits(string digits, int groupSize, char separator)
        {
            if (digits.Length <= groupSize)
                return digits;
            var first = digits.Length % groupSize;
            if (first == 0)
                first = groupSize;
            var builder = new StringBuilder(digits.Length + digits.Length / groupSize);
            builder.Append(digits, 0, first);
            for (var index = first; index < digits.Length; index += groupSize)
                builder.Append(separator).Append(digits, index, groupSize);
            return builder.ToString();
        }

        private static string GroupFloatDigits(string digits, char separator)
        {
            var suffixStart = digits.IndexOfAny(['e', 'E', '%']);
            var suffix = suffixStart < 0 ? string.Empty : digits[suffixStart..];
            var main = suffixStart < 0 ? digits : digits[..suffixStart];
            var decimalPoint = main.IndexOf('.');
            var integerPart = decimalPoint < 0 ? main : main[..decimalPoint];
            var fractionalPart = decimalPoint < 0 ? string.Empty : main[decimalPoint..];
            return GroupDigits(integerPart, 3, separator) + fractionalPart + suffix;
        }

        private static string FormatFloatRepr(double value)
        {
            if (double.IsNaN(value))
                return "nan";
            if (double.IsPositiveInfinity(value))
                return "inf";
            if (double.IsNegativeInfinity(value))
                return "-inf";
            var negative = BitConverter.DoubleToInt64Bits(value) < 0;
            var result = FormatPositiveFloatRepr(Math.Abs(value));
            return negative ? "-" + result : result;
        }

        private static string FormatPositiveFloatRepr(double value)
        {
            var text = NormalizeExponent(value.ToString("R", CultureInfo.InvariantCulture), lower: true);
            if (text.IndexOfAny(['.', 'e']) < 0)
                text += ".0";
            return text;
        }

        private static string NormalizeExponent(string text, bool lower)
        {
            var exponent = text.IndexOf('E');
            if (exponent < 0)
                exponent = text.IndexOf('e');
            if (exponent < 0)
                return lower ? text.ToLowerInvariant() : text;
            var marker = lower ? 'e' : 'E';
            var signIndex = exponent + 1;
            var digitsIndex = signIndex;
            if (digitsIndex < text.Length && text[digitsIndex] is '+' or '-')
                digitsIndex++;
            while (text.Length - digitsIndex > 2 && text[digitsIndex] == '0')
                text = text.Remove(digitsIndex, 1);
            return text[..exponent] + marker + text[(exponent + 1)..];
        }

        private string Str(VmValue value)
        {
            if (IsObjectType(value, VmObjectType.String))
                return GetString(value);
            if (IsObjectType(value, VmObjectType.Exception))
            {
                var payload = _memory.GetObjectPayloadAddress(value);
                return GetString(_memory.ReadValue(payload + 8));
            }
            if (IsObjectType(value, VmObjectType.Instance) && HasSpecialMethod(value, "__str__"))
            {
                var result = CallZeroArgumentSpecialMethod(value, "__str__");
                if (!IsObjectType(result, VmObjectType.String))
                    Raise("TypeError", $"__str__ returned non-string (type {GetTypeName(result)})");
                return GetString(result);
            }
            return Repr(value, 0);
        }

        private string Repr(VmValue value, int depth)
        {
            if (depth > 16)
                return "...";
            if (value.IsNull)
                return "<NULL>";
            if (value.IsNone)
                return "None";
            if (value.IsBoolean)
                return value.BooleanValue ? "True" : "False";
            if (value.IsEllipsis)
                return "Ellipsis";
            if (IsInteger(value))
                return GetInteger(value).ToString(CultureInfo.InvariantCulture);
            if (value.IsBuiltin)
                return $"<built-in function {GetBuiltinName(value.Builtin)}>";
            if (!value.IsAddress)
                return "<invalid value>";

            switch (_memory.GetObjectType(value))
            {
                case VmObjectType.Float:
                    return FormatFloatRepr(GetFloat(value));
                case VmObjectType.Complex:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        var real = FormatFloatRepr(_memory.ReadDouble(payload));
                        var imaginaryValue = _memory.ReadDouble(payload + 8);
                        var imaginary = FormatPositiveFloatRepr(Math.Abs(imaginaryValue));
                        return imaginaryValue < 0 ? $"({real}-{imaginary}j)" : $"({real}+{imaginary}j)";
                    }
                case VmObjectType.String:
                    return QuoteString(GetString(value));
                case VmObjectType.Bytes:
                    return ReprBytes(value);
                case VmObjectType.Tuple:
                    return ReprTuple(value, depth);
                case VmObjectType.List:
                    return ReprList(value, depth);
                case VmObjectType.Dictionary:
                    return ReprDictionary(value, depth);
                case VmObjectType.Set:
                    return ReprSet(value, depth, frozen: false);
                case VmObjectType.FrozenSet:
                    return ReprSet(value, depth, frozen: true);
                case VmObjectType.Range:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        var start = _memory.ReadInt64(payload);
                        var stop = _memory.ReadInt64(payload + 8);
                        var step = _memory.ReadInt64(payload + 16);
                        return step == 1 ? $"range({start}, {stop})" : $"range({start}, {stop}, {step})";
                    }
                case VmObjectType.Function:
                    return $"<function {GetString(_memory.ReadValue(_memory.GetObjectPayloadAddress(value) + 40))}>";
                case VmObjectType.Cell:
                    {
                        var cellValue = GetCellValue(value);
                        return cellValue.IsNull
                            ? "<cell: empty>"
                            : $"<cell: {GetTypeName(cellValue)} object>";
                    }
                case VmObjectType.BoundMethod:
                    return "<built-in method>";
                case VmObjectType.Iterator:
                    return "<iterator>";
                case VmObjectType.BuiltinIterator:
                    return $"<{GetTypeName(value)} object>";
                case VmObjectType.StaticMethod:
                    return $"<staticmethod({Repr(GetDescriptorCallable(value), depth + 1)})>";
                case VmObjectType.ClassMethod:
                    return $"<classmethod({Repr(GetDescriptorCallable(value), depth + 1)})>";
                case VmObjectType.Property:
                    return "<property object>";
                case VmObjectType.Generator:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"<generator object {GetCodeName(_memory.ReadValue(payload))}>";
                    }
                case VmObjectType.Slice:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"slice({Repr(_memory.ReadValue(payload), depth + 1)}, {Repr(_memory.ReadValue(payload + 8), depth + 1)}, {Repr(_memory.ReadValue(payload + 16), depth + 1)})";
                    }
                case VmObjectType.Code:
                    return $"<code object {GetCodeName(value)}>";
                case VmObjectType.Exception:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"{GetString(_memory.ReadValue(payload))}({QuoteString(GetString(_memory.ReadValue(payload + 8)))})";
                    }
                case VmObjectType.Module:
                    return $"<module '{GetModuleName(value)}'>";
                case VmObjectType.Interpolation:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"Interpolation({Repr(_memory.ReadValue(payload), depth + 1)}, {Repr(_memory.ReadValue(payload + 8), depth + 1)}, {Repr(_memory.ReadValue(payload + 16), depth + 1)}, {Repr(_memory.ReadValue(payload + 24), depth + 1)})";
                    }
                case VmObjectType.Template:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"Template(strings={Repr(_memory.ReadValue(payload), depth + 1)}, interpolations={Repr(_memory.ReadValue(payload + 8), depth + 1)})";
                    }
                case VmObjectType.Class:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"<class '{GetString(_memory.ReadValue(payload + 48))}.{GetString(_memory.ReadValue(payload + 8))}'>";
                    }
                case VmObjectType.Instance:
                    {
                        if (HasSpecialMethod(value, "__repr__"))
                        {
                            var result = CallZeroArgumentSpecialMethod(value, "__repr__");
                            if (!IsObjectType(result, VmObjectType.String))
                                Raise("TypeError", $"__repr__ returned non-string (type {GetTypeName(result)})");
                            return GetString(result);
                        }
                        var classObject = GetInstanceClass(value);
                        var classPayload = _memory.GetObjectPayloadAddress(classObject);
                        return $"<{GetString(_memory.ReadValue(classPayload + 48))}.{GetString(_memory.ReadValue(classPayload + 8))} object at 0x{value.Address:x}>";
                    }
                case VmObjectType.PythonBoundMethod:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        var function = _memory.ReadValue(payload);
                        var name = function.IsBuiltin
                            ? GetBuiltinName(function.Builtin)
                            : GetString(_memory.ReadValue(_memory.GetObjectPayloadAddress(function) + 40));
                        return $"<bound method {name}>";
                    }
                case VmObjectType.Super:
                    {
                        var payload = _memory.GetObjectPayloadAddress(value);
                        return $"<super: <class '{GetClassName(_memory.ReadValue(payload))}'>, <{GetTypeName(_memory.ReadValue(payload + 8))} object>>";
                    }
                case VmObjectType.MappingProxy:
                    return $"mappingproxy({Repr(GetMappingProxyDictionary(value), depth + 1)})";
                default:
                    return $"<{GetTypeName(value)} object>";
            }
        }

        private string ReprTuple(VmValue tuple, int depth)
        {
            var count = GetTupleCount(tuple);
            var builder = new StringBuilder("(");
            for (var index = 0; index < count; index++)
            {
                if (index != 0)
                    AppendBoundedText(builder, ", ");
                AppendBoundedText(builder, Repr(GetTupleItem(tuple, index), depth + 1));
                PollCancellation();
            }
            if (count == 1)
                AppendBoundedText(builder, ",");
            AppendBoundedText(builder, ")");
            return builder.ToString();
        }

        private string ReprList(VmValue list, int depth)
        {
            var count = GetListCount(list);
            var builder = new StringBuilder("[");
            for (var index = 0; index < count; index++)
            {
                if (index != 0)
                    AppendBoundedText(builder, ", ");
                AppendBoundedText(builder, Repr(GetListItem(list, index), depth + 1));
                PollCancellation();
            }
            AppendBoundedText(builder, "]");
            return builder.ToString();
        }

        private string ReprDictionary(VmValue dictionary, int depth)
        {
            var builder = new StringBuilder("{");
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            var capacity = _memory.ReadInt32(payload + 12);
            var entries = GetDictionaryEntriesPayload(dictionary);
            var written = 0;
            for (var slot = 0; slot < capacity; slot++)
            {
                var entry = entries + slot * DictionaryEntrySize;
                var key = _memory.ReadValue(entry + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                if (written++ != 0)
                    AppendBoundedText(builder, ", ");
                AppendBoundedText(builder, Repr(key, depth + 1));
                AppendBoundedText(builder, ": ");
                AppendBoundedText(builder, Repr(_memory.ReadValue(entry + 16), depth + 1));
                PollCancellation();
            }
            AppendBoundedText(builder, "}");
            return builder.ToString();
        }

        private string ReprSet(VmValue set, int depth, bool frozen)
        {
            var dictionary = GetSetDictionary(set);
            if (GetDictionaryCount(dictionary) == 0)
                return frozen ? "frozenset()" : "set()";
            var builder = new StringBuilder(frozen ? "frozenset({" : "{");
            var payload = _memory.GetObjectPayloadAddress(dictionary);
            var capacity = _memory.ReadInt32(payload + 12);
            var entries = GetDictionaryEntriesPayload(dictionary);
            var written = 0;
            for (var slot = 0; slot < capacity; slot++)
            {
                var key = _memory.ReadValue(entries + slot * DictionaryEntrySize + 8);
                if (key.IsNull || key.IsDeleted)
                    continue;
                if (written++ != 0)
                    AppendBoundedText(builder, ", ");
                AppendBoundedText(builder, Repr(key, depth + 1));
                PollCancellation();
            }
            AppendBoundedText(builder, "}");
            if (frozen)
                AppendBoundedText(builder, ")");
            return builder.ToString();
        }

        private void AppendBoundedText(StringBuilder builder, string text)
        {
            var maximumCharacters = Math.Max(4096L, _limits.MaxOutputBytes);
            if ((long)builder.Length + text.Length > maximumCharacters)
            {
                throw new VmTrapException(
                    VmStopReason.MemoryLimitExceeded,
                    "Python text representation exceeds the configured host-text limit.");
            }
            builder.Append(text);
        }

        private string ReprBytes(VmValue bytes)
        {
            var span = _memory.GetReadOnlySpan(_memory.GetObjectPayloadAddress(bytes), _memory.GetObjectAux0(bytes));
            var builder = new StringBuilder("b'");
            foreach (var item in span)
            {
                if (item >= 32 && item <= 126 && item != (byte)'\\' && item != (byte)'\'')
                    AppendBoundedText(builder, ((char)item).ToString());
                else if (item == (byte)'\\')
                    AppendBoundedText(builder, "\\\\");
                else if (item == (byte)'\'')
                    AppendBoundedText(builder, "\\'");
                else
                    AppendBoundedText(builder, "\\x" + item.ToString("x2", CultureInfo.InvariantCulture));
            }
            AppendBoundedText(builder, "'");
            return builder.ToString();
        }

        private string QuoteString(string text)
        {
            var builder = new StringBuilder("'");
            foreach (var character in text)
            {
                EnsureBoundedTextLength(builder, 6);
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\'': builder.Append("\\'"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (char.IsControl(character))
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }
            AppendBoundedText(builder, "'");
            return builder.ToString();
        }

        private void EnsureBoundedTextLength(StringBuilder builder, int additionalCharacters)
        {
            var maximumCharacters = Math.Max(4096L, _limits.MaxOutputBytes);
            if ((long)builder.Length + additionalCharacters > maximumCharacters)
            {
                throw new VmTrapException(
                    VmStopReason.MemoryLimitExceeded,
                    "Python text representation exceeds the configured host-text limit.");
            }
        }

        private string GetTypeName(VmValue value)
        {
            if (value.IsNull)
                return "NULL";
            if (value.IsNone)
                return "NoneType";
            if (value.IsBoolean)
                return "bool";
            if (value.IsEllipsis)
                return "ellipsis";
            if (value.IsSmallInteger)
                return "int";
            if (value.IsBuiltin)
                return "builtin_function_or_method";
            if (!value.IsAddress)
                return "invalid";
            return _memory.GetObjectType(value) switch
            {
                VmObjectType.Storage => "storage",
                VmObjectType.Integer => "int",
                VmObjectType.Float => "float",
                VmObjectType.Complex => "complex",
                VmObjectType.String => "str",
                VmObjectType.Bytes => "bytes",
                VmObjectType.Tuple => "tuple",
                VmObjectType.List => "list",
                VmObjectType.Dictionary => "dict",
                VmObjectType.Set => "set",
                VmObjectType.FrozenSet => "frozenset",
                VmObjectType.Code => "code",
                VmObjectType.Function => "function",
                VmObjectType.Iterator => "iterator",
                VmObjectType.Slice => "slice",
                VmObjectType.Range => "range",
                VmObjectType.BoundMethod => "builtin_function_or_method",
                VmObjectType.Exception => "BaseException",
                VmObjectType.Cell => "cell",
                VmObjectType.Generator => "generator",
                VmObjectType.Module => "module",
                VmObjectType.Interpolation => "Interpolation",
                VmObjectType.Template => "Template",
                VmObjectType.Class => "type",
                VmObjectType.Instance => GetClassName(GetInstanceClass(value)),
                VmObjectType.PythonBoundMethod => "method",
                VmObjectType.Super => "super",
                VmObjectType.MappingProxy => "mappingproxy",
                VmObjectType.BuiltinIterator => "iterator",
                VmObjectType.StaticMethod => "staticmethod",
                VmObjectType.ClassMethod => "classmethod",
                VmObjectType.Property => "property",
                _ => "object",
            };
        }

        private void ConsumeInstructionBudget()
        {
            if (_snapshotMode)
                return;
            _instructions = checked(_instructions + 1);
            if (_instructions > _limits.MaxInstructions)
                throw new VmTrapException(VmStopReason.InstructionLimitExceeded, "Python instruction limit exceeded.");
            PollCancellation();
        }

        private void PollCancellation()
        {
            if (_snapshotMode)
                return;
            _cancellationPollCountdown--;
            if (_cancellationPollCountdown > 0)
                return;
            _cancellationPollCountdown = _limits.CancellationCheckPeriod;
            _combinedCancellation.ThrowIfCancellationRequested();
        }

        private void JumpTo(int target, int bytecodeUnits)
        {
            if ((uint)target >= (uint)bytecodeUnits)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Jump target is outside the code object.");
            SetFrameInstructionPointer(_currentFrame, target);
        }

        private bool GetBooleanForJump(VmValue value)
        {
            return value.IsBoolean ? value.BooleanValue : IsTruthy(value);
        }

        private void RequireObjectType(VmValue value, VmObjectType expected)
        {
            if (!value.IsAddress || _memory.GetObjectType(value) != expected)
                Raise("TypeError", $"expected {GetObjectTypeName(expected)}, got {GetTypeName(value)}");
        }

        private bool IsObjectType(VmValue value, VmObjectType expected)
        {
            return value.IsAddress && _memory.GetObjectType(value) == expected;
        }

        private static string GetObjectTypeName(VmObjectType type)
        {
            return type switch
            {
                VmObjectType.Storage => "storage",
                VmObjectType.Integer => "int",
                VmObjectType.Float => "float",
                VmObjectType.Complex => "complex",
                VmObjectType.String => "str",
                VmObjectType.Bytes => "bytes",
                VmObjectType.Tuple => "tuple",
                VmObjectType.List => "list",
                VmObjectType.Dictionary => "dict",
                VmObjectType.Set => "set",
                VmObjectType.FrozenSet => "frozenset",
                VmObjectType.Code => "code",
                VmObjectType.Function => "function",
                VmObjectType.Iterator => "iterator",
                VmObjectType.Slice => "slice",
                VmObjectType.Range => "range",
                VmObjectType.BoundMethod => "bound method",
                VmObjectType.Exception => "exception",
                VmObjectType.Cell => "cell",
                VmObjectType.Generator => "generator",
                VmObjectType.Module => "module",
                VmObjectType.Interpolation => "Interpolation",
                VmObjectType.Template => "Template",
                VmObjectType.Class => "class",
                VmObjectType.Instance => "instance",
                VmObjectType.PythonBoundMethod => "method",
                VmObjectType.Super => "super",
                VmObjectType.MappingProxy => "mappingproxy",
                _ => "object",
            };
        }

        private static int NormalizeCapacity(int capacity)
        {
            if (capacity < 0)
                throw new VmTrapException(VmStopReason.InvalidBytecode, "Negative container capacity.");
            var result = 8;
            while (result < capacity)
                result = checked(result * 2);
            return result;
        }

        private int NormalizeIndex(int index, int count)
        {
            if (index < 0)
                index = checked(index + count);
            if ((uint)index >= (uint)count)
                Raise("IndexError", "sequence index out of range");
            return index;
        }

        private BigInteger GetIndexInteger(VmValue value)
        {
            if (IsInteger(value))
                return GetInteger(value);
            if (IsObjectType(value, VmObjectType.Instance) && HasSpecialMethod(value, "__index__"))
            {
                var converted = CallZeroArgumentSpecialMethod(value, "__index__");
                if (!IsInteger(converted))
                    Raise("TypeError", $"__index__ returned non-int (type {GetTypeName(converted)})");
                return GetInteger(converted);
            }
            Raise("TypeError", $"'{GetTypeName(value)}' object cannot be interpreted as an integer");
            return BigInteger.Zero;
        }

        private int GetIndex(VmValue value)
        {
            var integer = GetIndexInteger(value);
            if (integer < int.MinValue || integer > int.MaxValue)
                Raise("OverflowError", "Python integer is too large for this VM index");
            return (int)integer;
        }

        private long GetInt64(VmValue value, string functionName)
        {
            var integer = GetIndexInteger(value);
            if (integer < long.MinValue || integer > long.MaxValue)
                Raise("OverflowError", $"Python integer is too large for {functionName}()");
            return (long)integer;
        }

        private int GetRepeatCount(BigInteger value)
        {
            if (value <= 0)
                return 0;
            if (value > int.MaxValue)
                throw new VmTrapException(VmStopReason.MemoryLimitExceeded, "Sequence repeat count exceeds the VM address-space limit.");
            return (int)value;
        }

        private int GetShiftCount(BigInteger value)
        {
            if (value.Sign < 0)
                Raise("ValueError", "negative shift count");
            if (value > int.MaxValue)
                throw new VmTrapException(VmStopReason.IntegerLimitExceeded, "Shift count exceeds the VM integer policy.");
            return (int)value;
        }

        private void EnsureIntegerSize(BigInteger value)
        {
            if (GetBitLength(value) > _limits.MaxIntegerBits)
                throw new VmTrapException(VmStopReason.IntegerLimitExceeded, "Python integer exceeds the configured bit limit.");
        }

        private void EnsureProspectiveIntegerBits(int bits)
        {
            if (bits > _limits.MaxIntegerBits)
                throw new VmTrapException(VmStopReason.IntegerLimitExceeded, "Python integer operation would exceed the configured bit limit.");
        }

        private static int GetBitLength(BigInteger value)
        {
            if (value.IsZero)
                return 0;
            var length = BigInteger.Abs(value).GetBitLength();
            return length > int.MaxValue ? int.MaxValue : (int)length;
        }

        private string GetStringArgument(VmValue value, string methodName)
        {
            if (!IsObjectType(value, VmObjectType.String))
                Raise("TypeError", $"{methodName} first arg must be str, not {GetTypeName(value)}");
            return GetString(value);
        }

        private void RequireArgumentCount(VmBuiltin builtin, int actual, int minimum, int maximum)
        {
            if (actual >= minimum && actual <= maximum)
                return;
            var expected = minimum == maximum ? minimum.ToString(CultureInfo.InvariantCulture) : $"from {minimum} to {maximum}";
            Raise("TypeError", $"{GetBuiltinName(builtin)}() expected {expected} arguments, got {actual}");
        }

        private void RequireMethodArgumentCount(string name, int actual, int minimum, int maximum)
        {
            if (actual >= minimum && actual <= maximum)
                return;
            var expected = minimum == maximum ? minimum.ToString(CultureInfo.InvariantCulture) : $"from {minimum} to {maximum}";
            Raise("TypeError", $"{name}() expected {expected} arguments, got {actual}");
        }

        private static string GetBuiltinName(VmBuiltin builtin)
        {
            return builtin switch
            {
                VmBuiltin.Print => "print",
                VmBuiltin.Len => "len",
                VmBuiltin.Range => "range",
                VmBuiltin.List => "list",
                VmBuiltin.Tuple => "tuple",
                VmBuiltin.Set => "set",
                VmBuiltin.Dict => "dict",
                VmBuiltin.Bool => "bool",
                VmBuiltin.Int => "int",
                VmBuiltin.Str => "str",
                VmBuiltin.All => "all",
                VmBuiltin.Any => "any",
                VmBuiltin.Abs => "abs",
                VmBuiltin.Ascii => "ascii",
                VmBuiltin.Bin => "bin",
                VmBuiltin.Bytes => "bytes",
                VmBuiltin.Callable => "callable",
                VmBuiltin.Chr => "chr",
                VmBuiltin.ClassMethod => "classmethod",
                VmBuiltin.Complex => "complex",
                VmBuiltin.DelAttr => "delattr",
                VmBuiltin.Dir => "dir",
                VmBuiltin.DivMod => "divmod",
                VmBuiltin.Enumerate => "enumerate",
                VmBuiltin.Filter => "filter",
                VmBuiltin.Float => "float",
                VmBuiltin.Format => "format",
                VmBuiltin.FrozenSet => "frozenset",
                VmBuiltin.GetAttr => "getattr",
                VmBuiltin.Globals => "globals",
                VmBuiltin.HasAttr => "hasattr",
                VmBuiltin.Hash => "hash",
                VmBuiltin.Hex => "hex",
                VmBuiltin.Id => "id",
                VmBuiltin.IsInstance => "isinstance",
                VmBuiltin.IsSubclass => "issubclass",
                VmBuiltin.Locals => "locals",
                VmBuiltin.Map => "map",
                VmBuiltin.Max => "max",
                VmBuiltin.Min => "min",
                VmBuiltin.Oct => "oct",
                VmBuiltin.Ord => "ord",
                VmBuiltin.Pow => "pow",
                VmBuiltin.Property => "property",
                VmBuiltin.Repr => "repr",
                VmBuiltin.Reversed => "reversed",
                VmBuiltin.Round => "round",
                VmBuiltin.SetAttr => "setattr",
                VmBuiltin.Slice => "slice",
                VmBuiltin.Sorted => "sorted",
                VmBuiltin.StaticMethod => "staticmethod",
                VmBuiltin.Sum => "sum",
                VmBuiltin.Vars => "vars",
                VmBuiltin.Zip => "zip",
                VmBuiltin.Iter => "iter",
                VmBuiltin.Next => "next",
                VmBuiltin.Import => "__import__",
                VmBuiltin.BuildClass => "__build_class__",
                VmBuiltin.Super => "super",
                VmBuiltin.ObjectInit => "object.__init__",
                VmBuiltin.BaseException => "BaseException",
                VmBuiltin.Exception => "Exception",
                VmBuiltin.TypeError => "TypeError",
                VmBuiltin.ValueError => "ValueError",
                VmBuiltin.RuntimeError => "RuntimeError",
                VmBuiltin.AssertionError => "AssertionError",
                VmBuiltin.NotImplementedError => "NotImplementedError",
                VmBuiltin.KeyError => "KeyError",
                VmBuiltin.IndexError => "IndexError",
                VmBuiltin.NameError => "NameError",
                VmBuiltin.UnboundLocalError => "UnboundLocalError",
                VmBuiltin.StopIteration => "StopIteration",
                VmBuiltin.ZeroDivisionError => "ZeroDivisionError",
                VmBuiltin.ArithmeticError => "ArithmeticError",
                VmBuiltin.LookupError => "LookupError",
                VmBuiltin.AttributeError => "AttributeError",
                VmBuiltin.ImportError => "ImportError",
                VmBuiltin.ModuleNotFoundError => "ModuleNotFoundError",
                VmBuiltin.OverflowError => "OverflowError",
                VmBuiltin.SystemError => "SystemError",
                VmBuiltin.SysGetRecursionLimit => "getrecursionlimit",
                VmBuiltin.MathSqrt => "sqrt",
                VmBuiltin.MathFloor => "floor",
                VmBuiltin.MathCeil => "ceil",
                VmBuiltin.MathTrunc => "trunc",
                VmBuiltin.MathFabs => "fabs",
                VmBuiltin.MathIsFinite => "isfinite",
                VmBuiltin.MathIsInf => "isinf",
                VmBuiltin.MathIsNaN => "isnan",
                VmBuiltin.MathCopySign => "copysign",
                VmBuiltin.MathFmod => "fmod",
                VmBuiltin.MathPow => "pow",
                VmBuiltin.MathSin => "sin",
                VmBuiltin.MathCos => "cos",
                VmBuiltin.MathTan => "tan",
                VmBuiltin.MathAsin => "asin",
                VmBuiltin.MathAcos => "acos",
                VmBuiltin.MathAtan => "atan",
                VmBuiltin.MathAtan2 => "atan2",
                VmBuiltin.MathExp => "exp",
                VmBuiltin.MathLog => "log",
                VmBuiltin.MathLog2 => "log2",
                VmBuiltin.MathLog10 => "log10",
                VmBuiltin.MathDegrees => "degrees",
                VmBuiltin.MathRadians => "radians",
                VmBuiltin.MathHypot => "hypot",
                VmBuiltin.MathGcd => "gcd",
                VmBuiltin.MathLcm => "lcm",
                VmBuiltin.MathFactorial => "factorial",
                VmBuiltin.MathComb => "comb",
                VmBuiltin.MathPerm => "perm",
                VmBuiltin.MathProd => "prod",
                VmBuiltin.MathIsClose => "isclose",
                VmBuiltin.MathSinh => "sinh",
                VmBuiltin.MathCosh => "cosh",
                VmBuiltin.MathTanh => "tanh",
                VmBuiltin.MathAsinh => "asinh",
                VmBuiltin.MathAcosh => "acosh",
                VmBuiltin.MathAtanh => "atanh",
                _ => "<builtin>",
            };
        }

        private static string GetBinaryOperatorText(PythonBinaryOperation operation)
        {
            return operation switch
            {
                PythonBinaryOperation.Add => "+",
                PythonBinaryOperation.And => "&",
                PythonBinaryOperation.FloorDivide => "//",
                PythonBinaryOperation.LeftShift => "<<",
                PythonBinaryOperation.MatrixMultiply => "@",
                PythonBinaryOperation.Multiply => "*",
                PythonBinaryOperation.Remainder => "%",
                PythonBinaryOperation.Or => "|",
                PythonBinaryOperation.Power => "**",
                PythonBinaryOperation.RightShift => ">>",
                PythonBinaryOperation.Subtract => "-",
                PythonBinaryOperation.TrueDivide => "/",
                PythonBinaryOperation.Xor => "^",
                _ => operation.ToString(),
            };
        }

        private static int CountCodePoints(string text)
        {
            var count = 0;
            for (var offset = 0; offset < text.Length; count++)
            {
                _ = ReadCodePoint(text, offset, out var consumed);
                offset += consumed;
            }
            return count;
        }

        private static int GetCodePointUtf16Offset(string text, int codePointIndex)
        {
            var offset = 0;
            for (var index = 0; index < codePointIndex; index++)
            {
                _ = ReadCodePoint(text, offset, out var consumed);
                offset += consumed;
            }
            return offset;
        }

        private static int ReadCodePoint(string text, int offset, out int consumed)
        {
            if ((uint)offset >= (uint)text.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var first = text[offset];
            if (char.IsHighSurrogate(first) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]))
            {
                consumed = 2;
                return char.ConvertToUtf32(first, text[offset + 1]);
            }
            consumed = 1;
            return first;
        }
    }
}
