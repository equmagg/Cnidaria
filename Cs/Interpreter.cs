using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Cnidaria.Cs
{
    internal sealed class RegisterBasedVm
    {
        internal sealed class VmUnhandledException : Exception
        {
            public string ManagedStackTrace { get; }
            public VmUnhandledException(string message) : base(message)
            {
                ManagedStackTrace = string.Empty;
            }
            public VmUnhandledException(string message, string managedStackTrace)
                : base(string.IsNullOrEmpty(managedStackTrace) ? message : message + Environment.NewLine + managedStackTrace)
            {
                ManagedStackTrace = managedStackTrace ?? string.Empty;
            }
        }

        private enum PendingContinuationKind : byte
        {
            None,
            Leave,
            Return,
            Throw,
            ReturnFloat,
        }

        private const int GprCount = 32;
        private const int FprCount = 32;
        private const int HeapBlockAlignment = 8;
        private const int BlockHeaderSize = 8;
        private const int BlockSizeOffset = 0;
        private const int BlockMetaOffset = 4;
        private const int BlockMetaObject = 1;
        private const int BlockMetaRaw = 2;
        private const int ObjectHeaderSize = 8;
        private const int GcFlagMark = 1 << 0;
        private const int GcFlagAllocated = 1 << 1;
        private const int ArrayLengthOffset = ObjectHeaderSize;
        private const int ArrayDataOffset = ObjectHeaderSize + 8;
        private const int StringLengthOffset = ObjectHeaderSize;
        private const int StringCharsOffset = ObjectHeaderSize + 4;
        private const int MaxCallFramesHard = 4096;

        private const int ShadowFrameSize = 88;
        private const int ShadowFrameCallerSp = 0;
        private const int ShadowFrameCallerFp = 8;
        private const int ShadowFrameContinuationI0 = 16;
        private const int ShadowFrameContinuationF0 = 24;
        private const int ShadowFrameAllocaSp = 32;
        private const int ShadowFrameIncomingStackArgBase = 40;
        private const int ShadowFramePostReturnObjectRef = 48;
        private const int ShadowFrameMethodIndex = 56;
        private const int ShadowFrameReturnPc = 60;
        private const int ShadowFrameCallerMethodIndex = 64;
        private const int ShadowFrameContinuationTargetPc = 68;
        private const int ShadowFramePackedFlags = 72;
        private const int ShadowFrameIncomingTargetMethodId = 76;
        private const int ShadowFrameRegisterSnapshotIndex = 80;
        private const int ShadowFrameSafePointPc = 84;
        private const int ShadowFrameCallFlagsMask = 0x0000FFFF;
        private const int ShadowFrameContinuationKindShift = 16;
        private const int ShadowFramePostReturnRegisterShift = 24;

        private readonly byte[] _mem;
        private readonly int _staticEnd;
        private readonly int _stackBase;
        private readonly int _stackEnd;
        private readonly int _heapBase;
        private readonly int _heapEnd;
        private readonly RuntimeTypeSystem _rts;
        private readonly Dictionary<string, RuntimeModule> _modules;
        private readonly TextWriter _textWriter;
        private readonly CodeImage _image;
        private readonly long[] _x = new long[GprCount];
        private readonly long[] _f = new long[FprCount];
        private int _frameStackTop;
        private int _frameStackPeakTop;
        private int _frameCount;
        private readonly Dictionary<int, RuntimeField> _fieldById;
        private readonly Dictionary<int, int> _staticBaseByTypeId = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _internPool = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, byte> _typeInitState = new Dictionary<int, byte>();
        private readonly List<RegisterSnapshot?> _registerSnapshots = new List<RegisterSnapshot?>();
        private readonly Dictionary<int, HostOverride> _hostOverrides = new Dictionary<int, HostOverride>();
        private readonly VmCallContext _hostCtx;
        private int _gcMarkHead;

        private int _heapPtr;
        private int _heapFloor;
        private int _allocDebtBytes;
        private bool _gcRunning;
        private int _pc;
        private int _currentMethodIndex = -1;
        private int _currentSafePointPc = -1;
        private long _fuel;
        private int _tick;
        private long _instructionsElapsed;
        private int _stackLowWatermark;
        private int _heapPeakAbs;
        private int _currentExceptionRef;
        private int _currentExceptionThrowMethodIndex = -1;
        private int _currentExceptionThrowPc = -1;
        private CallFlags _activeCallFlags;
        private RuntimeMethod? _activeCallTargetMethod;

        private readonly struct RegisterSnapshot
        {
            public readonly long[] General;
            public readonly long[] Float;

            public RegisterSnapshot(long[] general, long[] @float)
            {
                General = general;
                Float = @float;
            }
        }

        private readonly struct GprStorageLocation
        {
            public const byte CurrentRegister = 0;
            public const byte StackSlot = 1;
            public const byte Snapshot = 2;

            public readonly byte Kind;
            public readonly int Address;
            public readonly int SnapshotIndex;

            public GprStorageLocation(byte kind, int address = 0, int snapshotIndex = -1)
            {
                Kind = kind;
                Address = address;
                SnapshotIndex = snapshotIndex;
            }
        }

        public long InctructionsElapsed => _instructionsElapsed;
        public long InstructionsElapsed => _instructionsElapsed;
        public int StackPeakBytes => (_stackEnd - _stackLowWatermark) + (_frameStackPeakTop - _stackBase);
        public int HeapPeakBytes => _heapPeakAbs - _heapBase;

        public RegisterBasedVm(
            byte[] memory,
            int staticEnd,
            int stackBase,
            int stackEnd,
            RuntimeTypeSystem rts,
            Dictionary<string, RuntimeModule> modules,
            CodeImage image,
            TextWriter? textWriter = null)
        {
            _mem = memory ?? throw new ArgumentNullException(nameof(memory));
            _staticEnd = staticEnd;
            _stackBase = stackBase;
            _stackEnd = stackEnd;
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _image = image ?? throw new ArgumentNullException(nameof(image));
            _textWriter = textWriter ?? TextWriter.Null;
            _hostCtx = new VmCallContext(this);

            if (!(0 <= staticEnd && staticEnd <= stackBase && stackBase < stackEnd && stackEnd <= memory.Length))
                throw new ArgumentOutOfRangeException(nameof(stackEnd), "Bad VM memory layout.");

            _heapBase = AlignBlockStart(stackEnd);
            _heapEnd = memory.Length;
            if (_heapBase > _heapEnd)
                throw new ArgumentOutOfRangeException(nameof(memory), "Heap region is empty or invalid.");

            _heapPtr = _heapBase;
            _heapFloor = _heapBase;
            _heapPeakAbs = _heapBase;
            _stackLowWatermark = stackEnd;
            _frameStackTop = stackBase;
            _frameStackPeakTop = stackBase;
            X(MachineRegister.X0, 0);
            X(MachineRegisters.StackPointer, stackEnd);
            X(MachineRegisters.FramePointer, stackEnd);
            X(MachineRegisters.ThreadPointer, 0);
            _fieldById = BuildFieldIdMap(rts);
        }

        public void Execute(RuntimeMethod entryMethod, CancellationToken ct, ExecutionLimits limits)
        {
            Execute(entryMethod, ct, limits, default);
        }

        public void Execute(RuntimeMethod entryMethod, CancellationToken ct, ExecutionLimits limits, ReadOnlySpan<VmValue> initialArgs)
        {
            if (entryMethod is null) throw new ArgumentNullException(nameof(entryMethod));
            if (limits is null) throw new ArgumentNullException(nameof(limits));

            if (!_image.MethodIndexByRuntimeMethodId.TryGetValue(entryMethod.MethodId, out int entryIndex))
                throw new MissingMethodException("Entry method is not present in register code image: M" + entryMethod.MethodId);

            _fuel = limits.MaxInstructions;
            _tick = 0;
            _instructionsElapsed = 0;
            _stackLowWatermark = _stackEnd;
            _frameStackPeakTop = _stackBase;
            _heapPeakAbs = _heapPtr;
            _currentExceptionRef = 0;
            _currentExceptionThrowMethodIndex = -1;
            _currentExceptionThrowPc = -1;
            _currentSafePointPc = -1;
            _frameCount = 0;
            _frameStackTop = _stackBase;
            _frameStackPeakTop = _stackBase;
            _registerSnapshots.Clear();
            _gcMarkHead = 0;
            _allocDebtBytes = 0;
            _gcRunning = false;
            Array.Clear(_x, 0, _x.Length);
            Array.Clear(_f, 0, _f.Length);
            X(MachineRegisters.StackPointer, _stackEnd);
            X(MachineRegisters.FramePointer, _stackEnd);
            X(MachineRegisters.ThreadPointer, 0);
            X(MachineRegisters.ReturnAddress, -1);

            int incomingStackArgBase = PrepareEntryArguments(entryMethod, initialArgs);

            _pc = _image.Methods[entryIndex].EntryPc;
            _currentMethodIndex = entryIndex;
            PushFrame(
                entryIndex,
                -1,
                -1,
                X(MachineRegisters.StackPointer),
                X(MachineRegisters.FramePointer),
                CallFlags.None,
                entryMethod.MethodId,
                incomingStackArgBase);
            X(MachineRegisters.ThreadPointer, incomingStackArgBase);

            Run(ct, limits);
        }

        private int PrepareEntryArguments(RuntimeMethod method, ReadOnlySpan<VmValue> initialArgs)
        {
            if (initialArgs.Length == 0)
                return 0;
            if (method.HasThis)
                throw new InvalidOperationException("Instance entrypoints are not supported by the register VM entry adapter.");
            if (initialArgs.Length != method.ParameterTypes.Length)
                throw new InvalidOperationException("Initial argument count does not match entrypoint signature.");

            int stackAreaSize = ComputeEntryArgumentStackAreaSize(method);
            int incomingStackArgBase = 0;
            if (stackAreaSize != 0)
            {
                incomingStackArgBase = checked((int)AlignDown(_stackEnd - stackAreaSize, MachineAbi.StackArgumentSlotSize));
                if (incomingStackArgBase < _stackBase || incomingStackArgBase < _frameStackTop + ShadowFrameSize)
                    throw new StackOverflowException();
                Array.Clear(_mem, incomingStackArgBase, stackAreaSize);
                X(MachineRegisters.StackPointer, incomingStackArgBase);
                X(MachineRegisters.FramePointer, incomingStackArgBase);
                TrackStackPeak(incomingStackArgBase);
            }

            for (int i = 0; i < initialArgs.Length; i++)
                WriteEntryArgument(method, i, initialArgs[i], incomingStackArgBase);

            return incomingStackArgBase;
        }

        private int ComputeEntryArgumentStackAreaSize(RuntimeMethod method)
        {
            int max = 0;
            for (int i = 0; i < method.ParameterTypes.Length; i++)
            {
                var slices = GetAbiArgumentSlices(method, i);
                for (int s = 0; s < slices.Length; s++)
                {
                    AbiArgumentLocation loc = slices[s].Location;
                    if (loc.IsStack)
                        max = Math.Max(max, checked((loc.StackSlotIndex + 1) * MachineAbi.StackArgumentSlotSize));
                }
            }
            return max == 0 ? 0 : checked((int)AlignUp(max, MachineAbi.StackArgumentSlotSize));
        }

        private void WriteEntryArgument(RuntimeMethod method, int logicalIndex, VmValue value, int incomingStackArgBase)
        {
            RuntimeType type = method.ParameterTypes[logicalIndex];
            var abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: false);
            var slices = GetAbiArgumentSlices(method, logicalIndex);

            if (abi.PassingKind == AbiValuePassingKind.Void || slices.Length == 0)
                return;

            if (abi.PassingKind != AbiValuePassingKind.ScalarRegister)
            {
                if (value.Kind == VmValueKind.Value)
                {
                    int source = checked((int)value.Payload);
                    int size = value.Aux != 0 ? value.Aux : StorageSizeOf(type);
                    for (int i = 0; i < slices.Length; i++)
                    {
                        AbiArgumentSlice slice = slices[i];
                        int count = Math.Min(slice.Size, size - slice.Offset);
                        if (count <= 0)
                            continue;

                        if (slice.Location.IsRegister)
                        {
                            long bits = ReadRawBits(checked(source + slice.Offset), count);
                            if (slice.RegisterClass == RegisterClass.Float)
                                SetFpr((byte)slice.Location.Register, bits);
                            else
                                SetGpr(slice.Location.Register, bits);
                        }
                        else
                        {
                            if (incomingStackArgBase == 0)
                                throw new InvalidOperationException("Entry stack argument area was not allocated.");
                            int target = checked(incomingStackArgBase + slice.Location.StackSlotIndex * MachineAbi.StackArgumentSlotSize + slice.Location.StackOffset);
                            CopyBlock(target, checked(source + slice.Offset), count);
                        }
                    }
                    return;
                }

                throw new NotSupportedException("Only scalar and address-like entry arguments are supported by the register VM entry adapter.");
            }

            if (slices.Length != 1)
                throw new InvalidOperationException("Scalar ABI argument unexpectedly has multiple slices.");

            AbiArgumentSlice scalar = slices[0];
            {
                long bits = ConvertEntryArgumentToAbiBits(type, value, scalar.Size);
                if (scalar.Location.IsRegister)
                {
                    if (scalar.RegisterClass == RegisterClass.Float)
                        SetFpr((byte)scalar.Location.Register, bits);
                    else
                        SetGpr(scalar.Location.Register, bits);
                }
                else
                {
                    if (incomingStackArgBase == 0)
                        throw new InvalidOperationException("Entry stack argument area was not allocated.");
                    int target = checked(incomingStackArgBase + scalar.Location.StackSlotIndex * MachineAbi.StackArgumentSlotSize + scalar.Location.StackOffset);
                    WriteRawBits(target, bits, scalar.Size);
                }
            }
        }

        private long ConvertEntryArgumentToAbiBits(RuntimeType type, VmValue value, int size)
        {
            if (type.IsReferenceType || type.Kind == RuntimeTypeKind.Pointer || type.Kind == RuntimeTypeKind.ByRef)
                return value.Kind == VmValueKind.Null ? 0 : value.Payload;


            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Single":
                        return unchecked((uint)BitConverter.SingleToInt32Bits((float)value.AsDouble()));
                    case "Double":
                        return BitConverter.DoubleToInt64Bits(value.AsDouble());
                    case "Boolean":
                        return value.AsBool() ? 1 : 0;
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return unchecked((uint)value.AsInt32());
                    case "Int64":
                    case "UInt64":
                    case "IntPtr":
                    case "UIntPtr":
                        return value.AsInt64();
                }
            }

            if (value.Kind == VmValueKind.I4 || size <= 4)
                return unchecked((uint)value.AsInt32());
            if (value.Kind == VmValueKind.I8)
                return value.AsInt64();

            throw new NotSupportedException("Unsupported register VM entry argument type: " + type.Namespace + "." + type.Name);
        }

        private void Run(CancellationToken ct, ExecutionLimits limits)
        {
            int tokenCheckPeriod = Math.Max(1, limits.TokenCheckPeriod);
            var code = _image.Code;
            int codeLength = code.Length;

            while (_currentMethodIndex >= 0)
            {
                if (--_fuel < 0)
                    throw new OperationCanceledException("Instruction budget exceeded.");

                _instructionsElapsed++;
                if (++_tick >= tokenCheckPeriod)
                {
                    _tick = 0;
                    ct.ThrowIfCancellationRequested();
                }

                if ((uint)_pc >= (uint)codeLength)
                    throw new InvalidOperationException($"PC out of code range: {_pc}");

                int executingPc = _pc;
                InstrDesc ins = code[executingPc];
                _pc = executingPc + 1;
                _currentSafePointPc = executingPc;

                if ((IsRegisterVmStaticFieldInstruction(ins.Op) || ins.Op == Op.NewObj) &&
                    TryDeferRequiredTypeInitializationBeforeInstruction(ins, executingPc, ct, limits))
                    continue;

                try
                {
                    switch (ins.Op)
                    {
                        case Op.Nop:
                            break;
                        case Op.Break:
                            break;
                        case Op.Trap:
                            throw new InvalidOperationException($"Register VM trap at PC {executingPc}");
                        case Op.GcPoll:
                            ct.ThrowIfCancellationRequested();
                            MaybeCollectGarbage();
                            break;

                        case Op.J:
                            _pc = CheckedTarget(ins.Imm);
                            break;
                        case Op.Leave:
                            Leave(executingPc, CheckedTarget(ins.Imm));
                            break;
                        case Op.EndFinally:
                            EndFinally();
                            break;
                        case Op.Throw:
                            ThrowManaged(GetGpr(ins.Rs1), executingPc, preserveExistingThrowSite: false);
                            break;
                        case Op.Rethrow:
                            ThrowManaged(_currentExceptionRef, executingPc, preserveExistingThrowSite: true);
                            break;
                        case Op.LdExceptionRef:
                            SetGpr(ins.Rd, _currentExceptionRef);
                            break;

                        case Op.RetVoid:
                            ReturnFromCurrentFrame(hasInteger: false, hasFloat: false, hasRef: false, hasValue: false, valueSize: 0);
                            break;
                        case Op.RetI:
                            SetGpr(MachineRegisters.ReturnValue0, GetGpr(ins.Rs1));
                            ReturnFromCurrentFrame(hasInteger: true, hasFloat: false, hasRef: false, hasValue: false, valueSize: 0);
                            break;
                        case Op.RetF:
                            SetFpr(MachineRegisters.FloatReturnValue0, GetFprBits(ins.Rs1));
                            ReturnFromCurrentFrame(hasInteger: false, hasFloat: true, hasRef: false, hasValue: false, valueSize: 0);
                            break;
                        case Op.RetRef:
                            SetGpr(MachineRegisters.ReturnValue0, GetGpr(ins.Rs1));
                            ReturnFromCurrentFrame(hasInteger: true, hasFloat: false, hasRef: true, hasValue: false, valueSize: 0);
                            break;
                        case Op.RetValue:
                            SetGpr(MachineRegisters.ReturnValue0, GetGpr(ins.Rs1));
                            ReturnFromCurrentFrame(hasInteger: true, hasFloat: false, hasRef: false, hasValue: true, valueSize: checked((int)ins.Imm));
                            break;

                        case Op.SwitchI32:
                            Switch((int)GetGpr(ins.Rs1), ins);
                            break;
                        case Op.SwitchI64:
                            Switch(GetGpr(ins.Rs1), ins);
                            break;

                        case Op.BrTrueI32:
                            if ((int)GetGpr(ins.Rs1) != 0) _pc = CheckedTarget(ins.Imm);
                            break;
                        case Op.BrFalseI32:
                            if ((int)GetGpr(ins.Rs1) == 0) _pc = CheckedTarget(ins.Imm);
                            break;
                        case Op.BrTrueI64:
                        case Op.BrTrueRef:
                            if (GetGpr(ins.Rs1) != 0) _pc = CheckedTarget(ins.Imm);
                            break;
                        case Op.BrFalseI64:
                        case Op.BrFalseRef:
                            if (GetGpr(ins.Rs1) == 0) _pc = CheckedTarget(ins.Imm);
                            break;

                        case Op.BrI32Eq: if ((int)GetGpr(ins.Rs1) == (int)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI32Ne: if ((int)GetGpr(ins.Rs1) != (int)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI32Lt: if ((int)GetGpr(ins.Rs1) < (int)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI32Le: if ((int)GetGpr(ins.Rs1) <= (int)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI32Gt: if ((int)GetGpr(ins.Rs1) > (int)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI32Ge: if ((int)GetGpr(ins.Rs1) >= (int)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU32Lt: if ((uint)GetGpr(ins.Rs1) < (uint)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU32Le: if ((uint)GetGpr(ins.Rs1) <= (uint)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU32Gt: if ((uint)GetGpr(ins.Rs1) > (uint)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU32Ge: if ((uint)GetGpr(ins.Rs1) >= (uint)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI64Eq: if (GetGpr(ins.Rs1) == GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI64Ne: if (GetGpr(ins.Rs1) != GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI64Lt: if (GetGpr(ins.Rs1) < GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI64Le: if (GetGpr(ins.Rs1) <= GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI64Gt: if (GetGpr(ins.Rs1) > GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrI64Ge: if (GetGpr(ins.Rs1) >= GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU64Lt: if ((ulong)GetGpr(ins.Rs1) < (ulong)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU64Le: if ((ulong)GetGpr(ins.Rs1) <= (ulong)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU64Gt: if ((ulong)GetGpr(ins.Rs1) > (ulong)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrU64Ge: if ((ulong)GetGpr(ins.Rs1) >= (ulong)GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrRefEq: if (GetGpr(ins.Rs1) == GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrRefNe: if (GetGpr(ins.Rs1) != GetGpr(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF32Eq: if (F32(ins.Rs1) == F32(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF32Ne: if (F32(ins.Rs1) != F32(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF32Lt: if (F32(ins.Rs1) < F32(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF32Le: if (F32(ins.Rs1) <= F32(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF32Gt: if (F32(ins.Rs1) > F32(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF32Ge: if (F32(ins.Rs1) >= F32(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF64Eq: if (F64(ins.Rs1) == F64(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF64Ne: if (F64(ins.Rs1) != F64(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF64Lt: if (F64(ins.Rs1) < F64(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF64Le: if (F64(ins.Rs1) <= F64(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF64Gt: if (F64(ins.Rs1) > F64(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;
                        case Op.BrF64Ge: if (F64(ins.Rs1) >= F64(ins.Rs2)) _pc = CheckedTarget(ins.Imm); break;

                        case Op.MovI:
                        case Op.MovRef:
                        case Op.MovPtr:
                            SetGpr(ins.Rd, GetGpr(ins.Rs1));
                            break;
                        case Op.MovF:
                            SetFpr(ins.Rd, GetFprBits(ins.Rs1));
                            break;
                        case Op.LiI32:
                            SetGpr(ins.Rd, (int)ins.Imm);
                            break;
                        case Op.LiI64:
                            SetGpr(ins.Rd, ins.Imm);
                            break;
                        case Op.LiF32Bits:
                            SetFpr(ins.Rd, (uint)(int)ins.Imm);
                            break;
                        case Op.LiF64Bits:
                            SetFpr(ins.Rd, ins.Imm);
                            break;
                        case Op.LiNull:
                            SetGpr(ins.Rd, 0);
                            break;
                        case Op.LiString:
                            SetGpr(ins.Rd, InternString(CurrentModule().Md.GetUserString(checked((int)ins.Imm))));
                            break;
                        case Op.LiTypeHandle:
                        case Op.LiMethodHandle:
                        case Op.LiFieldHandle:
                            SetGpr(ins.Rd, ins.Imm);
                            break;
                        case Op.LiStaticBase:
                            SetGpr(ins.Rd, EnsureStaticStorage(_rts.GetTypeById(checked((int)ins.Imm))));
                            break;

                        case Op.I32Add: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1) + (int)GetGpr(ins.Rs2))); break;
                        case Op.I32Sub: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1) - (int)GetGpr(ins.Rs2))); break;
                        case Op.I32Mul: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1) * (int)GetGpr(ins.Rs2))); break;
                        case Op.I32Div: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) / (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Rem: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) % (int)GetGpr(ins.Rs2)); break;
                        case Op.U32Div: SetI32(ins.Rd, unchecked((int)((uint)GetGpr(ins.Rs1) / (uint)GetGpr(ins.Rs2)))); break;
                        case Op.U32Rem: SetI32(ins.Rd, unchecked((int)((uint)GetGpr(ins.Rs1) % (uint)GetGpr(ins.Rs2)))); break;
                        case Op.I32Neg: SetI32(ins.Rd, unchecked(-(int)GetGpr(ins.Rs1))); break;
                        case Op.I32AddOvf: SetI32(ins.Rd, checked((int)GetGpr(ins.Rs1) + (int)GetGpr(ins.Rs2))); break;
                        case Op.I32SubOvf: SetI32(ins.Rd, checked((int)GetGpr(ins.Rs1) - (int)GetGpr(ins.Rs2))); break;
                        case Op.I32MulOvf: SetI32(ins.Rd, checked((int)GetGpr(ins.Rs1) * (int)GetGpr(ins.Rs2))); break;
                        case Op.U32AddOvf: SetI32(ins.Rd, unchecked((int)checked((uint)GetGpr(ins.Rs1) + (uint)GetGpr(ins.Rs2)))); break;
                        case Op.U32SubOvf: SetI32(ins.Rd, unchecked((int)checked((uint)GetGpr(ins.Rs1) - (uint)GetGpr(ins.Rs2)))); break;
                        case Op.U32MulOvf: SetI32(ins.Rd, unchecked((int)checked((uint)GetGpr(ins.Rs1) * (uint)GetGpr(ins.Rs2)))); break;
                        case Op.I32And: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) & (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Or: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) | (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Xor: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) ^ (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Not: SetI32(ins.Rd, ~(int)GetGpr(ins.Rs1)); break;
                        case Op.I32Shl: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) << ((int)GetGpr(ins.Rs2) & 31)); break;
                        case Op.I32Shr: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) >> ((int)GetGpr(ins.Rs2) & 31)); break;
                        case Op.U32Shr: SetI32(ins.Rd, unchecked((int)((uint)GetGpr(ins.Rs1) >> ((int)GetGpr(ins.Rs2) & 31)))); break;
                        case Op.I32Rol: SetI32(ins.Rd, BitOperationsRotateLeft((int)GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2) & 31)); break;
                        case Op.I32Ror: SetI32(ins.Rd, BitOperationsRotateRight((int)GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2) & 31)); break;
                        case Op.I32Eq: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) == (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Ne: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) != (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Lt: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) < (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Le: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) <= (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Gt: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) > (int)GetGpr(ins.Rs2)); break;
                        case Op.I32Ge: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) >= (int)GetGpr(ins.Rs2)); break;
                        case Op.U32Lt: SetBool(ins.Rd, (uint)GetGpr(ins.Rs1) < (uint)GetGpr(ins.Rs2)); break;
                        case Op.U32Le: SetBool(ins.Rd, (uint)GetGpr(ins.Rs1) <= (uint)GetGpr(ins.Rs2)); break;
                        case Op.U32Gt: SetBool(ins.Rd, (uint)GetGpr(ins.Rs1) > (uint)GetGpr(ins.Rs2)); break;
                        case Op.U32Ge: SetBool(ins.Rd, (uint)GetGpr(ins.Rs1) >= (uint)GetGpr(ins.Rs2)); break;
                        case Op.I32Min: SetI32(ins.Rd, Math.Min((int)GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2))); break;
                        case Op.I32Max: SetI32(ins.Rd, Math.Max((int)GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2))); break;
                        case Op.U32Min: SetI32(ins.Rd, unchecked((int)Math.Min((uint)GetGpr(ins.Rs1), (uint)GetGpr(ins.Rs2)))); break;
                        case Op.U32Max: SetI32(ins.Rd, unchecked((int)Math.Max((uint)GetGpr(ins.Rs1), (uint)GetGpr(ins.Rs2)))); break;

                        case Op.I32AddImm: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1) + (int)ins.Imm)); break;
                        case Op.I32SubImm: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1) - (int)ins.Imm)); break;
                        case Op.I32MulImm: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1) * (int)ins.Imm)); break;
                        case Op.I32AndImm: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) & (int)ins.Imm); break;
                        case Op.I32OrImm: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) | (int)ins.Imm); break;
                        case Op.I32XorImm: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) ^ (int)ins.Imm); break;
                        case Op.I32ShlImm: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) << ((int)ins.Imm & 31)); break;
                        case Op.I32ShrImm: SetI32(ins.Rd, (int)GetGpr(ins.Rs1) >> ((int)ins.Imm & 31)); break;
                        case Op.U32ShrImm: SetI32(ins.Rd, unchecked((int)((uint)GetGpr(ins.Rs1) >> ((int)ins.Imm & 31)))); break;
                        case Op.I32EqImm: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) == (int)ins.Imm); break;
                        case Op.I32NeImm: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) != (int)ins.Imm); break;
                        case Op.I32LtImm: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) < (int)ins.Imm); break;
                        case Op.I32LeImm: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) <= (int)ins.Imm); break;
                        case Op.I32GtImm: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) > (int)ins.Imm); break;
                        case Op.I32GeImm: SetBool(ins.Rd, (int)GetGpr(ins.Rs1) >= (int)ins.Imm); break;
                        case Op.U32LtImm: SetBool(ins.Rd, (uint)GetGpr(ins.Rs1) < (uint)(int)ins.Imm); break;

                        case Op.I64Add: SetGpr(ins.Rd, unchecked(GetGpr(ins.Rs1) + GetGpr(ins.Rs2))); break;
                        case Op.I64Sub: SetGpr(ins.Rd, unchecked(GetGpr(ins.Rs1) - GetGpr(ins.Rs2))); break;
                        case Op.I64Mul: SetGpr(ins.Rd, unchecked(GetGpr(ins.Rs1) * GetGpr(ins.Rs2))); break;
                        case Op.I64Div: SetGpr(ins.Rd, GetGpr(ins.Rs1) / GetGpr(ins.Rs2)); break;
                        case Op.I64Rem: SetGpr(ins.Rd, GetGpr(ins.Rs1) % GetGpr(ins.Rs2)); break;
                        case Op.U64Div: SetGpr(ins.Rd, unchecked((long)((ulong)GetGpr(ins.Rs1) / (ulong)GetGpr(ins.Rs2)))); break;
                        case Op.U64Rem: SetGpr(ins.Rd, unchecked((long)((ulong)GetGpr(ins.Rs1) % (ulong)GetGpr(ins.Rs2)))); break;
                        case Op.I64Neg: SetGpr(ins.Rd, unchecked(-GetGpr(ins.Rs1))); break;
                        case Op.I64AddOvf: SetGpr(ins.Rd, checked(GetGpr(ins.Rs1) + GetGpr(ins.Rs2))); break;
                        case Op.I64SubOvf: SetGpr(ins.Rd, checked(GetGpr(ins.Rs1) - GetGpr(ins.Rs2))); break;
                        case Op.I64MulOvf: SetGpr(ins.Rd, checked(GetGpr(ins.Rs1) * GetGpr(ins.Rs2))); break;
                        case Op.U64AddOvf: SetGpr(ins.Rd, unchecked((long)checked((ulong)GetGpr(ins.Rs1) + (ulong)GetGpr(ins.Rs2)))); break;
                        case Op.U64SubOvf: SetGpr(ins.Rd, unchecked((long)checked((ulong)GetGpr(ins.Rs1) - (ulong)GetGpr(ins.Rs2)))); break;
                        case Op.U64MulOvf: SetGpr(ins.Rd, unchecked((long)checked((ulong)GetGpr(ins.Rs1) * (ulong)GetGpr(ins.Rs2)))); break;
                        case Op.I64And: SetGpr(ins.Rd, GetGpr(ins.Rs1) & GetGpr(ins.Rs2)); break;
                        case Op.I64Or: SetGpr(ins.Rd, GetGpr(ins.Rs1) | GetGpr(ins.Rs2)); break;
                        case Op.I64Xor: SetGpr(ins.Rd, GetGpr(ins.Rs1) ^ GetGpr(ins.Rs2)); break;
                        case Op.I64Not: SetGpr(ins.Rd, ~GetGpr(ins.Rs1)); break;
                        case Op.I64Shl: SetGpr(ins.Rd, GetGpr(ins.Rs1) << ((int)GetGpr(ins.Rs2) & 63)); break;
                        case Op.I64Shr: SetGpr(ins.Rd, GetGpr(ins.Rs1) >> ((int)GetGpr(ins.Rs2) & 63)); break;
                        case Op.U64Shr: SetGpr(ins.Rd, unchecked((long)((ulong)GetGpr(ins.Rs1) >> ((int)GetGpr(ins.Rs2) & 63)))); break;
                        case Op.I64Rol: SetGpr(ins.Rd, RotateLeft(GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2) & 63)); break;
                        case Op.I64Ror: SetGpr(ins.Rd, RotateRight(GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2) & 63)); break;
                        case Op.I64Eq: SetBool(ins.Rd, GetGpr(ins.Rs1) == GetGpr(ins.Rs2)); break;
                        case Op.I64Ne: SetBool(ins.Rd, GetGpr(ins.Rs1) != GetGpr(ins.Rs2)); break;
                        case Op.I64Lt: SetBool(ins.Rd, GetGpr(ins.Rs1) < GetGpr(ins.Rs2)); break;
                        case Op.I64Le: SetBool(ins.Rd, GetGpr(ins.Rs1) <= GetGpr(ins.Rs2)); break;
                        case Op.I64Gt: SetBool(ins.Rd, GetGpr(ins.Rs1) > GetGpr(ins.Rs2)); break;
                        case Op.I64Ge: SetBool(ins.Rd, GetGpr(ins.Rs1) >= GetGpr(ins.Rs2)); break;
                        case Op.U64Lt: SetBool(ins.Rd, (ulong)GetGpr(ins.Rs1) < (ulong)GetGpr(ins.Rs2)); break;
                        case Op.U64Le: SetBool(ins.Rd, (ulong)GetGpr(ins.Rs1) <= (ulong)GetGpr(ins.Rs2)); break;
                        case Op.U64Gt: SetBool(ins.Rd, (ulong)GetGpr(ins.Rs1) > (ulong)GetGpr(ins.Rs2)); break;
                        case Op.U64Ge: SetBool(ins.Rd, (ulong)GetGpr(ins.Rs1) >= (ulong)GetGpr(ins.Rs2)); break;
                        case Op.I64Min: SetGpr(ins.Rd, Math.Min(GetGpr(ins.Rs1), GetGpr(ins.Rs2))); break;
                        case Op.I64Max: SetGpr(ins.Rd, Math.Max(GetGpr(ins.Rs1), GetGpr(ins.Rs2))); break;
                        case Op.U64Min: SetGpr(ins.Rd, unchecked((long)Math.Min((ulong)GetGpr(ins.Rs1), (ulong)GetGpr(ins.Rs2)))); break;
                        case Op.U64Max: SetGpr(ins.Rd, unchecked((long)Math.Max((ulong)GetGpr(ins.Rs1), (ulong)GetGpr(ins.Rs2)))); break;

                        case Op.I64AddImm: SetGpr(ins.Rd, unchecked(GetGpr(ins.Rs1) + ins.Imm)); break;
                        case Op.I64SubImm: SetGpr(ins.Rd, unchecked(GetGpr(ins.Rs1) - ins.Imm)); break;
                        case Op.I64MulImm: SetGpr(ins.Rd, unchecked(GetGpr(ins.Rs1) * ins.Imm)); break;
                        case Op.I64AndImm: SetGpr(ins.Rd, GetGpr(ins.Rs1) & ins.Imm); break;
                        case Op.I64OrImm: SetGpr(ins.Rd, GetGpr(ins.Rs1) | ins.Imm); break;
                        case Op.I64XorImm: SetGpr(ins.Rd, GetGpr(ins.Rs1) ^ ins.Imm); break;
                        case Op.I64ShlImm: SetGpr(ins.Rd, GetGpr(ins.Rs1) << ((int)ins.Imm & 63)); break;
                        case Op.I64ShrImm: SetGpr(ins.Rd, GetGpr(ins.Rs1) >> ((int)ins.Imm & 63)); break;
                        case Op.U64ShrImm: SetGpr(ins.Rd, unchecked((long)((ulong)GetGpr(ins.Rs1) >> ((int)ins.Imm & 63)))); break;
                        case Op.I64EqImm: SetBool(ins.Rd, GetGpr(ins.Rs1) == ins.Imm); break;
                        case Op.I64NeImm: SetBool(ins.Rd, GetGpr(ins.Rs1) != ins.Imm); break;
                        case Op.I64LtImm: SetBool(ins.Rd, GetGpr(ins.Rs1) < ins.Imm); break;
                        case Op.I64LeImm: SetBool(ins.Rd, GetGpr(ins.Rs1) <= ins.Imm); break;
                        case Op.I64GtImm: SetBool(ins.Rd, GetGpr(ins.Rs1) > ins.Imm); break;
                        case Op.I64GeImm: SetBool(ins.Rd, GetGpr(ins.Rs1) >= ins.Imm); break;
                        case Op.U64LtImm: SetBool(ins.Rd, (ulong)GetGpr(ins.Rs1) < (ulong)ins.Imm); break;

                        case Op.F32Add: SetF32(ins.Rd, F32(ins.Rs1) + F32(ins.Rs2)); break;
                        case Op.F32Sub: SetF32(ins.Rd, F32(ins.Rs1) - F32(ins.Rs2)); break;
                        case Op.F32Mul: SetF32(ins.Rd, F32(ins.Rs1) * F32(ins.Rs2)); break;
                        case Op.F32Div: SetF32(ins.Rd, F32(ins.Rs1) / F32(ins.Rs2)); break;
                        case Op.F32Rem: SetF32(ins.Rd, F32(ins.Rs1) % F32(ins.Rs2)); break;
                        case Op.F32Neg: SetF32(ins.Rd, -F32(ins.Rs1)); break;
                        case Op.F32Abs: SetF32(ins.Rd, MathF.Abs(F32(ins.Rs1))); break;
                        case Op.F32Eq: SetBool(ins.Rd, F32(ins.Rs1) == F32(ins.Rs2)); break;
                        case Op.F32Ne: SetBool(ins.Rd, F32(ins.Rs1) != F32(ins.Rs2)); break;
                        case Op.F32Lt: SetBool(ins.Rd, F32(ins.Rs1) < F32(ins.Rs2)); break;
                        case Op.F32Le: SetBool(ins.Rd, F32(ins.Rs1) <= F32(ins.Rs2)); break;
                        case Op.F32Gt: SetBool(ins.Rd, F32(ins.Rs1) > F32(ins.Rs2)); break;
                        case Op.F32Ge: SetBool(ins.Rd, F32(ins.Rs1) >= F32(ins.Rs2)); break;
                        case Op.F32Min: SetF32(ins.Rd, MathF.Min(F32(ins.Rs1), F32(ins.Rs2))); break;
                        case Op.F32Max: SetF32(ins.Rd, MathF.Max(F32(ins.Rs1), F32(ins.Rs2))); break;
                        case Op.F32IsNaN: SetBool(ins.Rd, float.IsNaN(F32(ins.Rs1))); break;
                        case Op.F32IsFinite: SetBool(ins.Rd, !float.IsNaN(F32(ins.Rs1)) && !float.IsInfinity(F32(ins.Rs1))); break;
                        case Op.F64Add: SetF64(ins.Rd, F64(ins.Rs1) + F64(ins.Rs2)); break;
                        case Op.F64Sub: SetF64(ins.Rd, F64(ins.Rs1) - F64(ins.Rs2)); break;
                        case Op.F64Mul: SetF64(ins.Rd, F64(ins.Rs1) * F64(ins.Rs2)); break;
                        case Op.F64Div: SetF64(ins.Rd, F64(ins.Rs1) / F64(ins.Rs2)); break;
                        case Op.F64Rem: SetF64(ins.Rd, F64(ins.Rs1) % F64(ins.Rs2)); break;
                        case Op.F64Neg: SetF64(ins.Rd, -F64(ins.Rs1)); break;
                        case Op.F64Abs: SetF64(ins.Rd, Math.Abs(F64(ins.Rs1))); break;
                        case Op.F64Eq: SetBool(ins.Rd, F64(ins.Rs1) == F64(ins.Rs2)); break;
                        case Op.F64Ne: SetBool(ins.Rd, F64(ins.Rs1) != F64(ins.Rs2)); break;
                        case Op.F64Lt: SetBool(ins.Rd, F64(ins.Rs1) < F64(ins.Rs2)); break;
                        case Op.F64Le: SetBool(ins.Rd, F64(ins.Rs1) <= F64(ins.Rs2)); break;
                        case Op.F64Gt: SetBool(ins.Rd, F64(ins.Rs1) > F64(ins.Rs2)); break;
                        case Op.F64Ge: SetBool(ins.Rd, F64(ins.Rs1) >= F64(ins.Rs2)); break;
                        case Op.F64Min: SetF64(ins.Rd, Math.Min(F64(ins.Rs1), F64(ins.Rs2))); break;
                        case Op.F64Max: SetF64(ins.Rd, Math.Max(F64(ins.Rs1), F64(ins.Rs2))); break;
                        case Op.F64IsNaN: SetBool(ins.Rd, double.IsNaN(F64(ins.Rs1))); break;
                        case Op.F64IsFinite: SetBool(ins.Rd, !double.IsNaN(F64(ins.Rs1)) && !double.IsInfinity(F64(ins.Rs1))); break;

                        case Op.I32ToI64: SetGpr(ins.Rd, (int)GetGpr(ins.Rs1)); break;
                        case Op.U32ToI64: SetGpr(ins.Rd, (uint)GetGpr(ins.Rs1)); break;
                        case Op.I64ToI32: SetI32(ins.Rd, unchecked((int)GetGpr(ins.Rs1))); break;
                        case Op.I64ToI32Ovf: SetI32(ins.Rd, checked((int)GetGpr(ins.Rs1))); break;
                        case Op.U64ToI32Ovf: SetI32(ins.Rd, checked((int)(ulong)GetGpr(ins.Rs1))); break;
                        case Op.I32ToF32: SetF32(ins.Rd, (int)GetGpr(ins.Rs1)); break;
                        case Op.I32ToF64: SetF64(ins.Rd, (int)GetGpr(ins.Rs1)); break;
                        case Op.U32ToF32: SetF32(ins.Rd, (uint)GetGpr(ins.Rs1)); break;
                        case Op.U32ToF64: SetF64(ins.Rd, (uint)GetGpr(ins.Rs1)); break;
                        case Op.I64ToF32: SetF32(ins.Rd, GetGpr(ins.Rs1)); break;
                        case Op.I64ToF64: SetF64(ins.Rd, GetGpr(ins.Rs1)); break;
                        case Op.U64ToF32: SetF32(ins.Rd, (ulong)GetGpr(ins.Rs1)); break;
                        case Op.U64ToF64: SetF64(ins.Rd, (ulong)GetGpr(ins.Rs1)); break;
                        case Op.F32ToF64: SetF64(ins.Rd, F32(ins.Rs1)); break;
                        case Op.F64ToF32: SetF32(ins.Rd, (float)F64(ins.Rs1)); break;
                        case Op.F32ToI32: SetI32(ins.Rd, unchecked((int)F32(ins.Rs1))); break;
                        case Op.F32ToI64: SetGpr(ins.Rd, unchecked((long)F32(ins.Rs1))); break;
                        case Op.F64ToI32: SetI32(ins.Rd, unchecked((int)F64(ins.Rs1))); break;
                        case Op.F64ToI64: SetGpr(ins.Rd, unchecked((long)F64(ins.Rs1))); break;
                        case Op.F32ToI32Ovf: SetI32(ins.Rd, checked((int)F32(ins.Rs1))); break;
                        case Op.F32ToI64Ovf: SetGpr(ins.Rd, checked((long)F32(ins.Rs1))); break;
                        case Op.F64ToI32Ovf: SetI32(ins.Rd, checked((int)F64(ins.Rs1))); break;
                        case Op.F64ToI64Ovf: SetGpr(ins.Rd, checked((long)F64(ins.Rs1))); break;
                        case Op.BitcastI32F32: SetFpr(ins.Rd, (uint)(int)GetGpr(ins.Rs1)); break;
                        case Op.BitcastF32I32: SetI32(ins.Rd, unchecked((int)(uint)GetFprBits(ins.Rs1))); break;
                        case Op.BitcastI64F64: SetFpr(ins.Rd, GetGpr(ins.Rs1)); break;
                        case Op.BitcastF64I64: SetGpr(ins.Rd, GetFprBits(ins.Rs1)); break;
                        case Op.SignExtendI8ToI32: SetI32(ins.Rd, (sbyte)(int)GetGpr(ins.Rs1)); break;
                        case Op.SignExtendI16ToI32: SetI32(ins.Rd, (short)(int)GetGpr(ins.Rs1)); break;
                        case Op.ZeroExtendI8ToI32: SetI32(ins.Rd, (byte)(int)GetGpr(ins.Rs1)); break;
                        case Op.ZeroExtendI16ToI32: SetI32(ins.Rd, (ushort)(int)GetGpr(ins.Rs1)); break;
                        case Op.TruncI32ToI8: SetI32(ins.Rd, (byte)(int)GetGpr(ins.Rs1)); break;
                        case Op.TruncI32ToI16: SetI32(ins.Rd, (ushort)(int)GetGpr(ins.Rs1)); break;

                        case Op.LdI1: SetI32(ins.Rd, (sbyte)ReadU8(EA(ins))); break;
                        case Op.LdU1: SetI32(ins.Rd, ReadU8(EA(ins))); break;
                        case Op.LdI2: SetI32(ins.Rd, (short)ReadU16(EA(ins))); break;
                        case Op.LdU2: SetI32(ins.Rd, ReadU16(EA(ins))); break;
                        case Op.LdI4: SetI32(ins.Rd, ReadI32(EA(ins))); break;
                        case Op.LdU4: SetGpr(ins.Rd, (uint)ReadI32(EA(ins))); break;
                        case Op.LdI8: SetGpr(ins.Rd, ReadI64(EA(ins))); break;
                        case Op.LdN: SetGpr(ins.Rd, ReadNative(EA(ins))); break;
                        case Op.LdF32: SetFpr(ins.Rd, (uint)ReadI32(EA(ins))); break;
                        case Op.LdF64: SetFpr(ins.Rd, ReadI64(EA(ins))); break;
                        case Op.LdRef:
                        case Op.LdPtr:
                            SetGpr(ins.Rd, ReadNative(EA(ins)));
                            break;
                        case Op.LdAddr:
                            SetGpr(ins.Rd, EA(ins));
                            break;
                        case Op.LdObj:
                            throw new InvalidOperationException("LdObj requires a type/size-carrying opcode; use CpObj/CpBlk or typed field/array object load.");
                        case Op.StI1: WriteU8(EA(ins), unchecked((byte)GetGpr(ins.Rd))); break;
                        case Op.StI2: WriteU16(EA(ins), unchecked((ushort)GetGpr(ins.Rd))); break;
                        case Op.StI4: WriteI32(EA(ins), (int)GetGpr(ins.Rd)); break;
                        case Op.StI8: WriteI64(EA(ins), GetGpr(ins.Rd)); break;
                        case Op.StN:
                        case Op.StRef:
                        case Op.StPtr:
                            WriteNative(EA(ins), GetGpr(ins.Rd));
                            break;
                        case Op.StF32: WriteI32(EA(ins), unchecked((int)(uint)GetFprBits(ins.Rd))); break;
                        case Op.StF64: WriteI64(EA(ins), GetFprBits(ins.Rd)); break;
                        case Op.StObj:
                            throw new InvalidOperationException("StObj requires a type/size-carrying opcode; use CpObj/CpBlk or typed field/array object store.");
                        case Op.CpBlk:
                            CopyBlock(GetAddress(ins.Rd), GetAddress(ins.Rs1), checked((int)ins.Imm));
                            break;
                        case Op.InitBlk:
                            InitBlock(GetAddress(ins.Rd), checked((byte)GetGpr(ins.Rs1)), checked((int)ins.Imm));
                            break;
                        case Op.CpObj:
                            CopyTypedObject(GetAddress(ins.Rd), GetAddress(ins.Rs1), _rts.GetTypeById(checked((int)ins.Imm)));
                            break;
                        case Op.NullCheck:
                            if (GetGpr(ins.Rs1) == 0) throw new NullReferenceException();
                            SetGpr(ins.Rd, GetGpr(ins.Rs1));
                            break;
                        case Op.BoundsCheck:
                            BoundsCheck((int)GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2));
                            break;
                        case Op.WriteBarrier:
                            break;
                        case Op.LdFldAddr:
                            SetGpr(ins.Rd, GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), writable: false));
                            break;
                        case Op.LdFldI1: LoadFieldInt(ins, 1, signed: true); break;
                        case Op.LdFldU1: LoadFieldInt(ins, 1, signed: false); break;
                        case Op.LdFldI2: LoadFieldInt(ins, 2, signed: true); break;
                        case Op.LdFldU2: LoadFieldInt(ins, 2, signed: false); break;
                        case Op.LdFldI4: SetI32(ins.Rd, ReadI32(GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), false))); break;
                        case Op.LdFldU4: SetGpr(ins.Rd, (uint)ReadI32(GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), false))); break;
                        case Op.LdFldI8:
                        case Op.LdFldN:
                        case Op.LdFldRef:
                        case Op.LdFldPtr:
                            SetGpr(ins.Rd, ReadSizedInteger(GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), false), FieldById(ins.Imm).FieldType));
                            break;
                        case Op.LdFldF32: SetFpr(ins.Rd, (uint)ReadI32(GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), false))); break;
                        case Op.LdFldF64: SetFpr(ins.Rd, ReadI64(GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), false))); break;
                        case Op.LdFldObj:
                            {
                                RuntimeField field = FieldById(ins.Imm);
                                CopyTypedObject(GetAddress(ins.Rd), GetInstanceFieldAddress(field, GetGpr(ins.Rs1), false), field.FieldType);
                                break;
                            }
                        case Op.StFldI1:
                        case Op.StFldI2:
                        case Op.StFldI4:
                        case Op.StFldI8:
                        case Op.StFldN:
                        case Op.StFldRef:
                        case Op.StFldPtr:
                        case Op.StFldF32:
                        case Op.StFldF64:
                        case Op.StFldObj:
                            StoreField(ins);
                            break;

                        case Op.LdSFldAddr:
                            SetGpr(ins.Rd, GetStaticFieldAddress(FieldById(ins.Imm), false));
                            break;
                        case Op.LdSFldI1: LoadStaticFieldInt(ins, 1, signed: true); break;
                        case Op.LdSFldU1: LoadStaticFieldInt(ins, 1, signed: false); break;
                        case Op.LdSFldI2: LoadStaticFieldInt(ins, 2, signed: true); break;
                        case Op.LdSFldU2: LoadStaticFieldInt(ins, 2, signed: false); break;
                        case Op.LdSFldI4: SetI32(ins.Rd, ReadI32(GetStaticFieldAddress(FieldById(ins.Imm), false))); break;
                        case Op.LdSFldU4: SetGpr(ins.Rd, (uint)ReadI32(GetStaticFieldAddress(FieldById(ins.Imm), false))); break;
                        case Op.LdSFldI8:
                        case Op.LdSFldN:
                        case Op.LdSFldRef:
                        case Op.LdSFldPtr:
                            SetGpr(ins.Rd, ReadSizedInteger(GetStaticFieldAddress(FieldById(ins.Imm), false), FieldById(ins.Imm).FieldType));
                            break;
                        case Op.LdSFldF32: SetFpr(ins.Rd, (uint)ReadI32(GetStaticFieldAddress(FieldById(ins.Imm), false))); break;
                        case Op.LdSFldF64: SetFpr(ins.Rd, ReadI64(GetStaticFieldAddress(FieldById(ins.Imm), false))); break;
                        case Op.LdSFldObj:
                            {
                                RuntimeField field = FieldById(ins.Imm);
                                CopyTypedObject(GetAddress(ins.Rd), GetStaticFieldAddress(field, false), field.FieldType);
                                break;
                            }
                        case Op.StSFldI1:
                        case Op.StSFldI2:
                        case Op.StSFldI4:
                        case Op.StSFldI8:
                        case Op.StSFldN:
                        case Op.StSFldRef:
                        case Op.StSFldPtr:
                        case Op.StSFldF32:
                        case Op.StSFldF64:
                        case Op.StSFldObj:
                            StoreStaticField(ins);
                            break;

                        case Op.LdLen:
                            ValidateArrayRef(GetGpr(ins.Rs1), out int arrAbs, out _);
                            SetI32(ins.Rd, ReadI32(arrAbs + ArrayLengthOffset));
                            break;
                        case Op.LdArrayDataAddr:
                            ValidateArrayRef(GetGpr(ins.Rs1), out int dataArrAbs, out _);
                            SetGpr(ins.Rd, dataArrAbs + ArrayDataOffset);
                            break;
                        case Op.LdElemAddr:
                            SetGpr(ins.Rd, GetArrayElementAddress(GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2), _rts.GetTypeById(checked((int)ins.Imm))));
                            break;
                        case Op.LdElemI1: LoadElemInt(ins, 1, true); break;
                        case Op.LdElemU1: LoadElemInt(ins, 1, false); break;
                        case Op.LdElemI2: LoadElemInt(ins, 2, true); break;
                        case Op.LdElemU2: LoadElemInt(ins, 2, false); break;
                        case Op.LdElemI4: SetI32(ins.Rd, ReadI32(ArrayEA(ins))); break;
                        case Op.LdElemU4: SetGpr(ins.Rd, (uint)ReadI32(ArrayEA(ins))); break;
                        case Op.LdElemI8:
                        case Op.LdElemN:
                        case Op.LdElemRef:
                        case Op.LdElemPtr:
                            SetGpr(ins.Rd, ReadNativeOrI64(ArrayEA(ins), ElementTypeOf(ins)));
                            break;
                        case Op.LdElemF32: SetFpr(ins.Rd, (uint)ReadI32(ArrayEA(ins))); break;
                        case Op.LdElemF64: SetFpr(ins.Rd, ReadI64(ArrayEA(ins))); break;
                        case Op.LdElemObj:
                            {
                                RuntimeType elem = ElementTypeOf(ins);
                                CopyTypedObject(GetAddress(ins.Rd), ArrayEA(ins), elem);
                                break;
                            }
                        case Op.StElemI1:
                        case Op.StElemI2:
                        case Op.StElemI4:
                        case Op.StElemI8:
                        case Op.StElemN:
                        case Op.StElemRef:
                        case Op.StElemPtr:
                        case Op.StElemF32:
                        case Op.StElemF64:
                        case Op.StElemObj:
                            StoreElement(ins);
                            break;

                        case Op.NewObj:
                            ExecNewObj(ins, ct, limits);
                            break;
                        case Op.NewArr:
                        case Op.NewSZArray:
                            SetGpr(ins.Rd, AllocArray(_rts.GetTypeById(checked((int)ins.Imm)), (int)GetGpr(ins.Rs1)));
                            break;
                        case Op.FastAllocateString:
                            SetGpr(ins.Rd, AllocStringUninitialized((int)GetGpr(ins.Rs1)));
                            break;
                        case Op.Box:
                            SetGpr(ins.Rd, BoxValue(_rts.GetTypeById(checked((int)ins.Imm)), ins.Rs1));
                            break;
                        case Op.UnboxAddr:
                            SetGpr(ins.Rd, UnboxAddress(GetGpr(ins.Rs1), _rts.GetTypeById(checked((int)ins.Imm))));
                            break;
                        case Op.UnboxAny:
                            UnboxAny(ins);
                            break;
                        case Op.CastClass:
                            SetGpr(ins.Rd, CastClass(GetGpr(ins.Rs1), _rts.GetTypeById(checked((int)ins.Imm))));
                            break;
                        case Op.IsInst:
                            SetGpr(ins.Rd, IsInst(GetGpr(ins.Rs1), _rts.GetTypeById(checked((int)ins.Imm))));
                            break;
                        case Op.SizeOf:
                            SetI32(ins.Rd, StorageSizeOf(_rts.GetTypeById(checked((int)ins.Imm))));
                            break;
                        case Op.InitObj:
                            InitTypedObject(GetAddress(ins.Rd), _rts.GetTypeById(checked((int)ins.Imm)));
                            break;
                        case Op.DefaultValue:
                            InitDefaultValue(ins);
                            break;
                        case Op.RefEq:
                            SetBool(ins.Rd, GetGpr(ins.Rs1) == GetGpr(ins.Rs2));
                            break;
                        case Op.RefNe:
                            SetBool(ins.Rd, GetGpr(ins.Rs1) != GetGpr(ins.Rs2));
                            break;
                        case Op.RuntimeTypeEquals:
                            {
                                long left = GetGpr(ins.Rs1);
                                long right = GetGpr(ins.Rs2);
                                SetBool(ins.Rd, left != 0 && right != 0 && GetObjectTypeFromRef(left).TypeId == GetObjectTypeFromRef(right).TypeId);
                                break;
                            }

                        case Op.CallVoid:
                        case Op.CallI:
                        case Op.CallF:
                        case Op.CallRef:
                        case Op.CallValue:
                        case Op.CallInternalVoid:
                        case Op.CallInternalI:
                        case Op.CallInternalF:
                        case Op.CallInternalRef:
                        case Op.CallInternalValue:
                            ExecCall(ins, isVirtual: false, executingPc, ct, limits);
                            break;
                        case Op.CallVirtVoid:
                        case Op.CallVirtI:
                        case Op.CallVirtF:
                        case Op.CallVirtRef:
                        case Op.CallVirtValue:
                        case Op.CallIfaceVoid:
                        case Op.CallIfaceI:
                        case Op.CallIfaceF:
                        case Op.CallIfaceRef:
                        case Op.CallIfaceValue:
                            ExecCall(ins, isVirtual: true, executingPc, ct, limits);
                            break;
                        case Op.CallIndirectVoid:
                        case Op.CallIndirectI:
                        case Op.CallIndirectF:
                        case Op.CallIndirectRef:
                        case Op.CallIndirectValue:
                        case Op.DelegateInvokeVoid:
                        case Op.DelegateInvokeI:
                        case Op.DelegateInvokeF:
                        case Op.DelegateInvokeRef:
                        case Op.DelegateInvokeValue:
                            throw new NotSupportedException($"Indirect/delegate calls are not supported by this register VM backend yet at PC {executingPc}");

                        case Op.StackAlloc:
                            SetGpr(ins.Rd, StackAlloc((int)GetGpr(ins.Rs1), checked((int)ins.Imm)));
                            break;
                        case Op.PtrAddI32:
                        case Op.ByRefAddI32:
                            SetGpr(ins.Rd, checked(GetGpr(ins.Rs1) + ((long)(int)GetGpr(ins.Rs2) * ins.Imm)));
                            break;
                        case Op.PtrAddI64:
                        case Op.ByRefAddI64:
                            SetGpr(ins.Rd, checked(GetGpr(ins.Rs1) + (GetGpr(ins.Rs2) * ins.Imm)));
                            break;
                        case Op.PtrSub:
                            SetGpr(ins.Rd, checked(GetGpr(ins.Rs1) - GetGpr(ins.Rs2)));
                            break;
                        case Op.PtrDiff:
                            SetGpr(ins.Rd, (GetGpr(ins.Rs1) - GetGpr(ins.Rs2)) / Math.Max(1, ins.Imm));
                            break;
                        case Op.ByRefToPtr:
                        case Op.PtrToByRef:
                            SetGpr(ins.Rd, GetGpr(ins.Rs1));
                            break;
                        default: throw new NotSupportedException($"Unsupported register VM opcode {ins.Op} at PC {executingPc}");
                    }


                }
                catch (VmUnhandledException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!TryTranslateHostExceptionToManaged(ex, out int managedExceptionRef))
                        throw;
                    ThrowManaged(managedExceptionRef, executingPc, preserveExistingThrowSite: false);
                }
            }
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopFrameOffset()
        {
            if (_frameCount == 0) throw new InvalidOperationException("No current frame.");
            return _frameStackTop - ShadowFrameSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopMethodIndex()
            => ReadI32(TopFrameOffset() + ShadowFrameMethodIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private RuntimeMethod MethodForIndex(int methodIndex)
            => _rts.GetMethodById(_image.Methods[methodIndex].RuntimeMethodId);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private RuntimeMethod CurrentFrameMethod()
            => MethodForIndex(TopMethodIndex());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopReturnPc()
            => ReadI32(TopFrameOffset() + ShadowFrameReturnPc);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopCallerMethodIndex()
            => ReadI32(TopFrameOffset() + ShadowFrameCallerMethodIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopCallerSp()
            => ReadI64(TopFrameOffset() + ShadowFrameCallerSp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopCallerFp()
            => ReadI64(TopFrameOffset() + ShadowFrameCallerFp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopPackedFlags()
            => ReadI32(TopFrameOffset() + ShadowFramePackedFlags);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTopPackedFlags(int value)
            => WriteI32(TopFrameOffset() + ShadowFramePackedFlags, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private CallFlags TopIncomingCallFlags()
            => (CallFlags)(TopPackedFlags() & ShadowFrameCallFlagsMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopIncomingTargetMethodId()
            => ReadI32(TopFrameOffset() + ShadowFrameIncomingTargetMethodId);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopIncomingStackArgBase()
            => ReadI64(TopFrameOffset() + ShadowFrameIncomingStackArgBase);

        private CallFlags CurrentInvocationCallFlags()
        {
            if (_activeCallTargetMethod is not null)
                return _activeCallFlags;
            if (_frameCount == 0)
                return CallFlags.None;
            return TopIncomingCallFlags();
        }

        private bool IsReadingCurrentManagedInvocation(RuntimeMethod method)
        {
            if (_activeCallTargetMethod is not null || _frameCount == 0)
                return false;
            return TopIncomingTargetMethodId() == method.MethodId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopAllocaSp()
            => ReadI64(TopFrameOffset() + ShadowFrameAllocaSp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTopAllocaSp(long value)
            => WriteI64(TopFrameOffset() + ShadowFrameAllocaSp, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long CurrentFrameStackLow()
        {
            long sp = X(MachineRegisters.StackPointer);
            if (_frameCount == 0) return sp;

            long allocaSp = TopAllocaSp();
            return allocaSp != 0 && allocaSp < sp ? allocaSp : sp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PendingContinuationKind TopContinuationKind()
            => (PendingContinuationKind)((TopPackedFlags() >> ShadowFrameContinuationKindShift) & 0xFF);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopContinuationTargetPc()
            => ReadI32(TopFrameOffset() + ShadowFrameContinuationTargetPc);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopContinuationI0()
            => ReadI64(TopFrameOffset() + ShadowFrameContinuationI0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopContinuationF0()
            => ReadI64(TopFrameOffset() + ShadowFrameContinuationF0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TopContinuationIsFloat()
            => TopContinuationKind() == PendingContinuationKind.ReturnFloat;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTopContinuation(PendingContinuationKind kind, int targetPc, long i0, long f0, bool isFloat)
        {
            if (kind == PendingContinuationKind.Return && isFloat)
                kind = PendingContinuationKind.ReturnFloat;

            int frame = TopFrameOffset();
            int packed = ReadI32(frame + ShadowFramePackedFlags);
            packed = (packed & ~(0xFF << ShadowFrameContinuationKindShift)) | ((byte)kind << ShadowFrameContinuationKindShift);
            WriteI32(frame + ShadowFramePackedFlags, packed);
            WriteI32(frame + ShadowFrameContinuationTargetPc, targetPc);
            WriteI64(frame + ShadowFrameContinuationI0, i0);
            WriteI64(frame + ShadowFrameContinuationF0, f0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PushFrame(
            int methodIndex,
            int returnPc,
            int callerMethodIndex,
            long callerSp,
            long callerFp,
            CallFlags incomingCallFlags,
            int incomingTargetMethodId,
            long incomingStackArgBase,
            byte postReturnRegister = RegisterVmIsa.InvalidRegister,
            long postReturnObjectRef = 0)
        {
            if ((uint)_frameCount >= (uint)MaxCallFramesHard)
                throw new InvalidOperationException("Register VM call stack limit exceeded.");

            int frame = _frameStackTop;
            int newTop = checked(frame + ShadowFrameSize);
            if (newTop > (int)X(MachineRegisters.StackPointer))
                throw new StackOverflowException();

            Array.Clear(_mem, frame, ShadowFrameSize);
            WriteI32(frame + ShadowFrameMethodIndex, methodIndex);
            WriteI32(frame + ShadowFrameReturnPc, returnPc);
            WriteI32(frame + ShadowFrameCallerMethodIndex, callerMethodIndex);
            WriteI32(frame + ShadowFrameContinuationTargetPc, -1);
            WriteI64(frame + ShadowFrameCallerSp, callerSp);
            WriteI64(frame + ShadowFrameCallerFp, callerFp);
            WriteI32(frame + ShadowFramePackedFlags, ((int)incomingCallFlags & ShadowFrameCallFlagsMask) | ((int)postReturnRegister << ShadowFramePostReturnRegisterShift));
            WriteI32(frame + ShadowFrameIncomingTargetMethodId, incomingTargetMethodId);
            WriteI64(frame + ShadowFrameIncomingStackArgBase, incomingStackArgBase);
            WriteI32(frame + ShadowFrameRegisterSnapshotIndex, -1);
            WriteI32(frame + ShadowFrameSafePointPc, -1);
            WriteI64(frame + ShadowFramePostReturnObjectRef, postReturnObjectRef);
            _frameStackTop = newTop;
            if (newTop > _frameStackPeakTop) _frameStackPeakTop = newTop;
            _frameCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PopFrame()
        {
            if (_frameCount == 0) throw new InvalidOperationException("No current frame.");
            _frameStackTop -= ShadowFrameSize;
            Array.Clear(_mem, _frameStackTop, ShadowFrameSize);
            _frameCount--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TopRegisterSnapshotIndex()
            => ReadI32(TopFrameOffset() + ShadowFrameRegisterSnapshotIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTopRegisterSnapshotIndex(int value)
            => WriteI32(TopFrameOffset() + ShadowFrameRegisterSnapshotIndex, value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte TopPostReturnRegister()
            => unchecked((byte)(TopPackedFlags() >> ShadowFramePostReturnRegisterShift));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long TopPostReturnObjectRef()
            => ReadI64(TopFrameOffset() + ShadowFramePostReturnObjectRef);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FrameSafePointPc(int frameOffset)
            => ReadI32(frameOffset + ShadowFrameSafePointPc);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFrameSafePointPc(int frameOffset, int pc)
            => WriteI32(frameOffset + ShadowFrameSafePointPc, pc);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreCurrentFrameSafePoint(int pc)
        {
            if (_frameCount != 0)
                SetFrameSafePointPc(TopFrameOffset(), pc);
        }

        private long FrameStackPointer(int frameOffset)
        {
            int top = TopFrameOffset();
            if (frameOffset == top)
                return X(MachineRegisters.StackPointer);

            int childFrame = checked(frameOffset + ShadowFrameSize);
            if (childFrame > top)
                throw new InvalidOperationException("Invalid shadow frame chain.");
            return ReadI64(childFrame + ShadowFrameCallerSp);
        }

        private long FramePointer(int frameOffset)
        {
            int top = TopFrameOffset();
            if (frameOffset == top)
                return X(MachineRegisters.FramePointer);

            int childFrame = checked(frameOffset + ShadowFrameSize);
            if (childFrame > top)
                throw new InvalidOperationException("Invalid shadow frame chain.");
            return ReadI64(childFrame + ShadowFrameCallerFp);
        }

        private long FrameIncomingArgumentBase(int frameOffset)
            => ReadI64(frameOffset + ShadowFrameIncomingStackArgBase);

        private RuntimeModule CurrentModule()
        {
            if (_frameCount == 0) throw new InvalidOperationException("No current frame.");
            RuntimeMethod method = CurrentFrameMethod();
            if (method.BodyModule != null) return method.BodyModule;
            if (_modules.TryGetValue(method.DeclaringType.AssemblyName, out RuntimeModule? module)) return module;
            throw new InvalidOperationException("Unable to resolve current runtime module for method M" + method.MethodId.ToString());
        }

        private void EnterManagedFrame(
            int targetMethodIndex,
            int returnPc,
            CallFlags incomingCallFlags,
            RuntimeMethod? targetMethod,
            byte postReturnRegister = RegisterVmIsa.InvalidRegister,
            long postReturnObjectRef = 0)
        {
            long callerVisibleSp = X(MachineRegisters.StackPointer);
            long incomingStackArgBase = _currentMethodIndex >= 0 ? GetCurrentOutgoingArgumentBase() : 0;
            StoreCurrentFrameSafePoint(_currentSafePointPc);
            long callSp = CurrentFrameStackLow();
            X(MachineRegisters.StackPointer, callSp);

            PushFrame(
                targetMethodIndex,
                returnPc,
                _currentMethodIndex,
                callerVisibleSp,
                X(MachineRegisters.FramePointer),
                incomingCallFlags,
                targetMethod?.MethodId ?? -1,
                incomingStackArgBase,
                postReturnRegister,
                postReturnObjectRef);

            _currentMethodIndex = targetMethodIndex;
            _pc = _image.Methods[targetMethodIndex].EntryPc;
            X(MachineRegisters.ReturnAddress, returnPc);
            X(MachineRegisters.ThreadPointer, incomingStackArgBase);
        }

        private RuntimeMethod ResolveCallTarget(InstrDesc ins, bool isVirtual)
        {
            if (!isVirtual)
                return _rts.GetMethodById(checked((int)ins.Imm));

            int callSiteIndex = checked((int)ins.Imm);
            if ((uint)callSiteIndex >= (uint)_image.CallSites.Length)
                throw new InvalidOperationException("Invalid register VM call-site index " + callSiteIndex.ToString());

            CallSiteRecord site = _image.CallSites[callSiteIndex];
            RuntimeMethod declared = _rts.GetMethodById(site.DeclaredRuntimeMethodId);
            long receiverRef = ReadThisArgumentReference(declared);

            if (receiverRef == 0)
                throw new NullReferenceException();

            RuntimeType receiverType;
            bool receiverIsManagedObject = TryGetObjectTypeFromExactRef(receiverRef, out receiverType);

            if (!receiverIsManagedObject)
            {
                if (declared.DeclaringType.IsValueType)
                    return declared;

                throw new AccessViolationException("Virtual dispatch receiver is not a managed object reference.");
            }

            if (site.DispatchSlot >= 0)
            {
                if ((uint)site.DispatchSlot >= (uint)receiverType.VTable.Length)
                    throw new MissingMethodException("Invalid virtual dispatch slot " + site.DispatchSlot.ToString());
                return receiverType.VTable[site.DispatchSlot];
            }

            return ResolveVirtualDispatch(receiverType, declared);
        }

        private RuntimeMethod ReadDeclaredCallMethod(InstrDesc ins, bool isVirtual)
        {
            if (!isVirtual)
                return _rts.GetMethodById(checked((int)ins.Imm));

            int callSiteIndex = checked((int)ins.Imm);
            if ((uint)callSiteIndex >= (uint)_image.CallSites.Length)
                throw new InvalidOperationException("Invalid register VM call-site index " + callSiteIndex.ToString());
            return _rts.GetMethodById(_image.CallSites[callSiteIndex].DeclaredRuntimeMethodId);
        }

        private CallFlags ReadCallFlags(InstrDesc ins, bool isVirtual)
        {
            CallFlags flags = (CallFlags)ins.Aux;
            if (isVirtual)
            {
                int callSiteIndex = checked((int)ins.Imm);
                if ((uint)callSiteIndex < (uint)_image.CallSites.Length)
                    flags |= (CallFlags)_image.CallSites[callSiteIndex].Flags;
            }
            return flags;
        }

        private static bool RequiresTypeInitializationBeforeCall(RuntimeMethod target)
        {
            if (StringComparer.Ordinal.Equals(target.Name, ".cctor"))
                return false;

            if (target.IsStatic)
                return true;

            return target.DeclaringType.IsValueType && StringComparer.Ordinal.Equals(target.Name, ".ctor");
        }

        private void NormalizeValueTypeThisArgument(RuntimeMethod method)
        {
            if (!method.HasThis || !method.DeclaringType.IsValueType)
                return;

            long thisRef = ReadThisArgumentReference(method);
            if (thisRef == 0)
                throw new NullReferenceException();

            if (!TryGetObjectTypeFromExactRef(thisRef, out RuntimeType actual))
                return;
            if (actual.TypeId != method.DeclaringType.TypeId)
                return;

            SetThisArgumentReference(method, checked(thisRef + ObjectHeaderSize));
        }

        private void ExecCall(InstrDesc ins, bool isVirtual, int callPc, CancellationToken ct, ExecutionLimits limits)
        {
            RuntimeMethod declared = ReadDeclaredCallMethod(ins, isVirtual);
            RuntimeMethod target = ResolveCallTarget(ins, isVirtual);
            CallFlags callFlags = ReadCallFlags(ins, isVirtual);

            NormalizeValueTypeThisArgument(target);

            if (RequiresTypeInitializationBeforeCall(target))
            {
                if (TryRunTypeInitializer(target.DeclaringType, callPc, ct, limits))
                    return;
            }

            CallFlags previousActiveCallFlags = _activeCallFlags;
            RuntimeMethod? previousActiveCallTargetMethod = _activeCallTargetMethod;
            _activeCallFlags = callFlags;
            _activeCallTargetMethod = target;
            try
            {
                if (TryInvokeHostOverride(target, ct))
                    return;
                if (TryInvokeIntrinsic(target, ct))
                    return;
            }
            finally
            {
                _activeCallFlags = previousActiveCallFlags;
                _activeCallTargetMethod = previousActiveCallTargetMethod;
            }

            if (!_image.MethodIndexByRuntimeMethodId.TryGetValue(target.MethodId, out int targetMethodIndex))
            {
                if (target.BodyModule is null || target.Body is null)
                    throw new MissingMethodException("No body for register call target M" + target.MethodId.ToString());
                throw new MissingMethodException("Target method exists in metadata but not in register image: M" + target.MethodId.ToString());
            }

            if (_frameCount >= MaxCallFramesHard)
                throw new InvalidOperationException("Register VM call stack limit exceeded.");
            if (_frameCount >= limits.MaxCallDepth)
                throw new InvalidOperationException("Configured call depth limit exceeded.");

            EnterManagedFrame(targetMethodIndex, _pc, callFlags, target);
        }

        private void ExecNewObj(InstrDesc ins, CancellationToken ct, ExecutionLimits limits)
        {
            var ctor = _rts.GetMethodById(checked((int)ins.Imm));
            if (ctor.DeclaringType.IsValueType)
            {
                if (!_image.MethodIndexByRuntimeMethodId.TryGetValue(ctor.MethodId, out int ctorIndex))
                    throw new MissingMethodException("Value-type constructor not in register image: M" + ctor.MethodId.ToString());
                EnterManagedFrame(ctorIndex, _pc, CallFlags.None, ctor);
                return;
            }

            // System.String is a variable sized object
            int obj = IsSystemStringType(ctor.DeclaringType)
                ? AllocStringForNewObjConstructor(ctor)
                : AllocObject(ctor.DeclaringType);
            SetThisArgumentReference(ctor, obj);

            if (!_image.MethodIndexByRuntimeMethodId.TryGetValue(ctor.MethodId, out int methodIndex))
            {
                SetGpr(ins.Rd, obj);
                return;
            }


            EnterManagedFrame(methodIndex, _pc, CallFlags.None, ctor, ins.Rd, obj);
        }

        private int AllocStringForNewObjConstructor(RuntimeMethod ctor)
        {
            int length = TryGetStringConstructorResultLength(ctor, out int computedLength) ? computedLength : 0;
            return AllocStringUninitialized(length);
        }

        private bool TryGetStringConstructorResultLength(RuntimeMethod ctor, out int length)
        {
            length = 0;
            RuntimeType[] parameters = ctor.ParameterTypes;

            if (parameters.Length == 0)
                return true;

            if (parameters.Length == 1)
            {
                RuntimeType p0 = parameters[0];
                if (IsCharArrayType(p0))
                {
                    long arrayRef = ReadAbiScalarArgument(ctor, 1);
                    if (arrayRef == 0)
                        return true;

                    ValidateArrayRef(arrayRef, out int arrAbs, out _);
                    length = ReadI32(arrAbs + ArrayLengthOffset);
                    if (length < 0)
                        throw new InvalidOperationException("Corrupted char[] length.");
                    return true;
                }

                if (IsCharPointerType(p0))
                {
                    long ptr = ReadAbiScalarArgument(ctor, 1);
                    length = ptr == 0 ? 0 : NullTerminatedCharPointerLength(checked((int)ptr));
                    return true;
                }
            }

            if (parameters.Length == 2 && IsCharType(parameters[0]) && IsInt32Type(parameters[1]))
            {
                int requestedLength = unchecked((int)ReadAbiScalarArgument(ctor, 2));
                length = requestedLength < 0 ? 0 : requestedLength;
                return true;
            }

            if (parameters.Length == 3 && IsCharArrayType(parameters[0]) && IsInt32Type(parameters[1]) && IsInt32Type(parameters[2]))
            {
                long arrayRef = ReadAbiScalarArgument(ctor, 1);
                int startIndex = unchecked((int)ReadAbiScalarArgument(ctor, 2));
                int requestedLength = unchecked((int)ReadAbiScalarArgument(ctor, 3));

                if (arrayRef == 0 || startIndex < 0 || requestedLength < 0)
                    return true;

                ValidateArrayRef(arrayRef, out int arrAbs, out _);
                int arrayLength = ReadI32(arrAbs + ArrayLengthOffset);
                if (arrayLength < 0)
                    throw new InvalidOperationException("Corrupted char[] length.");

                length = (uint)startIndex <= (uint)arrayLength && requestedLength <= arrayLength - startIndex
                    ? requestedLength
                    : 0;
                return true;
            }

            return false;
        }

        private int NullTerminatedCharPointerLength(int charsAbs)
        {
            const int MaxStringConstructorChars = 8 * 1024 * 1024;
            for (int i = 0; i < MaxStringConstructorChars; i++)
            {
                int pos = checked(charsAbs + checked(i * 2));
                CheckIndirectAccess(pos, 2, false);
                if (ReadU16(pos) == 0)
                    return i;
            }

            throw new AccessViolationException("Unterminated char* string constructor input.");
        }

        private bool IsCharArrayType(RuntimeType type)
            => type.Kind == RuntimeTypeKind.Array && type.ElementType is not null && IsCharType(type.ElementType);

        private bool IsCharPointerType(RuntimeType type)
            => type.Kind == RuntimeTypeKind.Pointer && type.ElementType is not null && IsCharType(type.ElementType);

        private bool IsInt32Type(RuntimeType type)
            => type.Namespace == "System" && type.Name == "Int32";

        private bool TryRunTypeInitializer(RuntimeType type, int returnPc, CancellationToken ct, ExecutionLimits limits)
        {
            _rts.EnsureConstructedMembers(type);

            if (_typeInitState.TryGetValue(type.TypeId, out byte state))
            {
                // 1 == running
                // 2 == completed or known noop
                return false;
            }

            _ = EnsureStaticStorage(type);

            RuntimeMethod? cctor = FindTypeInitializer(type);
            if (cctor is null || cctor.BodyModule is null || cctor.Body is null)
            {
                _typeInitState[type.TypeId] = 2;
                return false;
            }
            if (!_image.MethodIndexByRuntimeMethodId.TryGetValue(cctor.MethodId, out int index))
            {
                _typeInitState.Remove(type.TypeId);
                throw new MissingMethodException(
                    $"Type initializer is reachable at runtime but is absent from register image: " +
                    $"{type.Namespace}.{type.Name}; cctor=M{cctor.MethodId}");
            }

            _typeInitState[type.TypeId] = 1;
            int snapshotIndex = SaveRegisterSnapshot();
            EnterManagedFrame(index, returnPc, CallFlags.None, cctor);
            SetTopRegisterSnapshotIndex(snapshotIndex);
            return true;
        }

        private static RuntimeMethod? FindTypeInitializer(RuntimeType type)
        {
            for (int i = 0; i < type.Methods.Length; i++)
            {
                RuntimeMethod m = type.Methods[i];
                if (m.IsStatic && m.ParameterTypes.Length == 0 && StringComparer.Ordinal.Equals(m.Name, ".cctor"))
                    return m;
            }
            return null;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRegisterVmStaticFieldInstruction(Op op)
            => (ushort)op >= (ushort)Op.LdSFldI1 && (ushort)op <= (ushort)Op.StSFldObj;

        private bool TryDeferRequiredTypeInitializationBeforeInstruction(InstrDesc ins, int executingPc, CancellationToken ct, ExecutionLimits limits)
        {
            if (IsRegisterVmStaticFieldInstruction(ins.Op))
            {
                RuntimeField field = FieldById(ins.Imm);
                if (field.IsStatic && TryRunTypeInitializer(field.DeclaringType, executingPc, ct, limits))
                    return true;
            }

            if (ins.Op == Op.NewObj)
            {
                RuntimeMethod ctor = _rts.GetMethodById(checked((int)ins.Imm));
                if (TryRunTypeInitializer(ctor.DeclaringType, executingPc, ct, limits))
                    return true;
            }

            return false;
        }

        private void ReturnFromCurrentFrame(bool hasInteger, bool hasFloat, bool hasRef, bool hasValue, int valueSize)
        {
            int retPc = TopReturnPc();
            int callerMethod = TopCallerMethodIndex();
            RuntimeMethod returningMethod = CurrentFrameMethod();
            long retI0 = X(MachineRegisters.ReturnValue0);
            long retF0 = GetFprBits((byte)MachineRegisters.FloatReturnValue0);

            if (hasValue && valueSize > 0 && (CurrentInvocationCallFlags() & CallFlags.HiddenReturnBuffer) != 0)
            {
                int source = checked((int)retI0);
                int target = checked((int)ReadHiddenReturnBufferAddress(returningMethod));
                CopyBlock(target, source, valueSize);
                retI0 = target;
            }

            if (TryBeginFinallyForReturn(_pc - 1, hasInteger, hasFloat, retI0, retF0))
                return;

            bool returnedFromCctor = StringComparer.Ordinal.Equals(returningMethod.Name, ".cctor");
            int cctorTypeId = returnedFromCctor ? returningMethod.DeclaringType.TypeId : -1;
            int registerSnapshotIndex = TopRegisterSnapshotIndex();
            byte postReturnRegister = TopPostReturnRegister();
            long postReturnObjectRef = TopPostReturnObjectRef();
            long callerSp = TopCallerSp();
            long callerFp = TopCallerFp();

            PopFrame();
            if (_frameCount == 0 || retPc < 0 || callerMethod < 0)
            {
                if (returnedFromCctor)
                    _typeInitState[cctorTypeId] = 2;

                if (registerSnapshotIndex >= 0)
                    ReleaseRegisterSnapshot(registerSnapshotIndex);

                _currentMethodIndex = -1;
                _pc = -1;
                _currentSafePointPc = -1;
                X(MachineRegisters.ThreadPointer, 0);
                return;
            }

            _currentMethodIndex = callerMethod;
            _pc = retPc;
            X(MachineRegisters.StackPointer, callerSp);
            X(MachineRegisters.FramePointer, callerFp);
            X(MachineRegisters.ThreadPointer, TopIncomingStackArgBase());
            if (registerSnapshotIndex >= 0)
            {
                RestoreRegisterSnapshot(registerSnapshotIndex);
                X(MachineRegisters.StackPointer, callerSp);
                X(MachineRegisters.FramePointer, callerFp);
                X(MachineRegisters.ThreadPointer, TopIncomingStackArgBase());
            }
            if (hasFloat) SetFpr(MachineRegisters.FloatReturnValue0, retF0);
            if (hasInteger || hasRef || hasValue) SetGpr(MachineRegisters.ReturnValue0, retI0);
            if (postReturnRegister != RegisterVmIsa.InvalidRegister) SetGpr(postReturnRegister, postReturnObjectRef);
            if (returnedFromCctor) _typeInitState[cctorTypeId] = 2;
        }

        private int SaveRegisterSnapshot()
        {
            var snapshot = new RegisterSnapshot((long[])_x.Clone(), (long[])_f.Clone());
            for (int i = 0; i < _registerSnapshots.Count; i++)
            {
                if (_registerSnapshots[i] is null)
                {
                    _registerSnapshots[i] = snapshot;
                    return i;
                }
            }

            _registerSnapshots.Add(snapshot);
            return _registerSnapshots.Count - 1;
        }

        private void RestoreRegisterSnapshot(int index)
        {
            if ((uint)index >= (uint)_registerSnapshots.Count || _registerSnapshots[index] is not RegisterSnapshot snapshot)
                throw new InvalidOperationException("Invalid register snapshot index.");

            Array.Copy(snapshot.General, _x, GprCount);
            Array.Copy(snapshot.Float, _f, FprCount);
            _registerSnapshots[index] = null;
        }
        private void ReleaseRegisterSnapshot(int index)
        {
            if ((uint)index >= (uint)_registerSnapshots.Count)
                throw new InvalidOperationException("Invalid register snapshot index.");

            _registerSnapshots[index] = null;
        }
        private bool TryBeginFinallyForReturn(int fromPc, bool hasInteger, bool hasFloat, long retI0, long retF0)
        {
            if (!TryFindEnclosingFinally(fromPc, targetPc: -1, out var finallyRegion))
                return false;
            SetTopContinuation(PendingContinuationKind.Return, -1, retI0, retF0, hasFloat);
            _pc = finallyRegion.HandlerStartPc;
            return true;
        }

        private void Leave(int fromPc, int targetPc)
        {
            if (TryFindEnclosingFinally(fromPc, targetPc, out var finallyRegion))
            {
                SetTopContinuation(PendingContinuationKind.Leave, targetPc, 0, 0, false);
                _pc = finallyRegion.HandlerStartPc;
                return;
            }
            _pc = targetPc;
        }

        private void EndFinally()
        {
            PendingContinuationKind kind = TopContinuationKind();
            int targetPc = TopContinuationTargetPc();
            long i0 = TopContinuationI0();
            long f0 = TopContinuationF0();
            bool isFloat = TopContinuationIsFloat();
            SetTopContinuation(PendingContinuationKind.None, -1, 0, 0, false);

            switch (kind)
            {
                case PendingContinuationKind.None:
                    return;
                case PendingContinuationKind.Leave:
                    _pc = targetPc;
                    return;
                case PendingContinuationKind.Throw:
                    ThrowManaged(_currentExceptionRef, _pc - 1, preserveExistingThrowSite: true);
                    return;
                case PendingContinuationKind.Return:
                case PendingContinuationKind.ReturnFloat:
                    isFloat = kind == PendingContinuationKind.ReturnFloat;
                    if (isFloat) SetFpr(MachineRegisters.FloatReturnValue0, f0);
                    else SetGpr(MachineRegisters.ReturnValue0, i0);
                    ReturnFromCurrentFrame(hasInteger: !isFloat, hasFloat: isFloat, hasRef: false, hasValue: false, valueSize: 0);
                    return;
                default:
                    throw new InvalidOperationException("Invalid finally continuation.");
            }
        }

        private void ThrowManaged(long exceptionRef, int throwPc, bool preserveExistingThrowSite)
        {
            if (exceptionRef == 0)
            {
                exceptionRef = AllocExceptionRef("System", "NullReferenceException", "NullReferenceException");
                preserveExistingThrowSite = false;
            }
            if (exceptionRef < int.MinValue || exceptionRef > int.MaxValue)
                throw new AccessViolationException("Managed exception reference is outside VM address space.");

            int exceptionRef32 = (int)exceptionRef;
            if (!preserveExistingThrowSite || _currentExceptionRef != exceptionRef32 || _currentExceptionThrowMethodIndex < 0)
            {
                _currentExceptionThrowMethodIndex = _currentMethodIndex;
                _currentExceptionThrowPc = throwPc;
            }
            _currentExceptionRef = exceptionRef32;
            List<ManagedStackFrameInfo>? unhandledTrace = null;

            while (_frameCount != 0)
            {
                unhandledTrace ??= new List<ManagedStackFrameInfo>(Math.Min(_frameCount, 64));
                unhandledTrace.Add(CaptureManagedStackFrame(throwPc));

                if (TryFindHandler(throwPc, _currentExceptionRef, out var h))
                {
                    if ((EhRegionKind)h.Kind == EhRegionKind.Finally || (EhRegionKind)h.Kind == EhRegionKind.Fault)
                    {
                        SetTopContinuation(PendingContinuationKind.Throw, -1, 0, 0, false);
                        _pc = h.HandlerStartPc;
                        return;
                    }

                    _pc = h.HandlerStartPc;
                    return;
                }

                RuntimeMethod unwindingMethod = CurrentFrameMethod();
                if (StringComparer.Ordinal.Equals(unwindingMethod.Name, ".cctor"))
                    _typeInitState.Remove(unwindingMethod.DeclaringType.TypeId);

                int registerSnapshotIndex = TopRegisterSnapshotIndex();
                int retPc = TopReturnPc();
                int callerMethod = TopCallerMethodIndex();
                long callerSp = TopCallerSp();
                long callerFp = TopCallerFp();
                PopFrame();
                if (registerSnapshotIndex >= 0)
                    ReleaseRegisterSnapshot(registerSnapshotIndex);
                if (_frameCount == 0 || retPc < 0 || callerMethod < 0)
                    break;
                _currentMethodIndex = callerMethod;
                _pc = retPc;
                X(MachineRegisters.StackPointer, callerSp);
                X(MachineRegisters.FramePointer, callerFp);
                X(MachineRegisters.ThreadPointer, TopIncomingStackArgBase());
                throwPc = _pc - 1;
            }

            string message = TryReadExceptionMessage(_currentExceptionRef);
            string exceptionType = FormatExceptionTypeName(_currentExceptionRef);
            string header = FormatUnhandledExceptionHeader(exceptionType, message);
            string managedTrace = FormatManagedStackTrace(
                _currentExceptionThrowMethodIndex, _currentExceptionThrowPc, unhandledTrace);
            _currentExceptionThrowMethodIndex = -1;
            _currentExceptionThrowPc = -1;
            throw new VmUnhandledException(header, $"<{managedTrace}>");
        }
        private readonly struct ManagedStackFrameInfo
        {
            public readonly int MethodIndex;
            public readonly int RuntimeMethodId;
            public readonly int Pc;
            public readonly string MethodName;
            public readonly string InstructionText;
            public ManagedStackFrameInfo(int methodIndex, int runtimeMethodId, int pc, string methodName, string instructionText)
            {
                MethodIndex = methodIndex;
                RuntimeMethodId = runtimeMethodId;
                Pc = pc;
                MethodName = methodName;
                InstructionText = instructionText;
            }
        }
        private ManagedStackFrameInfo CaptureManagedStackFrame(int pc)
        {
            int methodIndex = _currentMethodIndex;
            RuntimeMethod? method = null;
            int runtimeMethodId = -1;
            try
            {
                if ((uint)methodIndex < (uint)_image.Methods.Length)
                {
                    runtimeMethodId = _image.Methods[methodIndex].RuntimeMethodId;
                    method = _rts.GetMethodById(runtimeMethodId);
                }
            }
            catch
            {
                method = null;
            }
            return new ManagedStackFrameInfo(
                methodIndex,
                runtimeMethodId,
                pc,
                method is null ? "<unknown>" : FormatMethodName(method),
                FormatInstructionAtPc(pc));
        }
        private string FormatUnhandledExceptionHeader(string exceptionType, string message)
        {
            if (message.Length == 0)
                return exceptionType.Length == 0 ? "Unhandled managed exception." : "Unhandled managed exception: " + exceptionType;
            if (exceptionType.Length == 0)
                return message;
            return exceptionType + ": " + message;
        }
        private string FormatExceptionTypeName(int exceptionRef)
        {
            try
            {
                if (exceptionRef == 0) return string.Empty;
                return FormatTypeName(GetObjectTypeFromRef(exceptionRef));
            }
            catch
            {
                return string.Empty;
            }
        }
        private string FormatManagedStackTrace(int originalThrowMethodIndex, int originalThrowPc, List<ManagedStackFrameInfo>? frames)
        {
            var sb = new System.Text.StringBuilder();
            if ((uint)originalThrowMethodIndex < (uint)_image.Methods.Length)
            {
                RuntimeMethod? originalMethod = null;
                try
                {
                    originalMethod = MethodForIndex(originalThrowMethodIndex);
                }
                catch
                {
                    originalMethod = null;
                }
                sb.Append("at: ");
                sb.Append(originalMethod is null ? "<unknown>" : FormatMethodName(originalMethod));
                sb.Append(" [methodIndex=");
                sb.Append(originalThrowMethodIndex);
                sb.Append(", pc=");
                sb.Append(originalThrowPc);
                sb.Append(']');
                string instruction = FormatInstructionAtPc(originalThrowPc);
                if (instruction.Length != 0)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append("instr: ");
                    sb.Append(instruction);
                }
            }
            if (frames is not null && frames.Count != 0)
            {
                if (sb.Length != 0) sb.Append(Environment.NewLine);
                for (int i = 0; i < frames.Count; i++)
                {
                    ManagedStackFrameInfo f = frames[i];
                    sb.Append(Environment.NewLine);
                    sb.Append("  at ");
                    sb.Append(f.MethodName);
                    sb.Append(']');
                    if (f.InstructionText.Length != 0)
                    {
                        sb.Append(" :: ");
                        sb.Append(f.InstructionText);
                    }
                }
            }
            return sb.ToString();
        }
        private string FormatInstructionAtPc(int pc)
        {
            if ((uint)pc >= (uint)_image.Code.Length) return string.Empty;
            return _image.Code[pc].ToString();
        }
        private static string FormatMethodName(RuntimeMethod method)
            => FormatTypeName(method.DeclaringType) + "." + method.Name;
        private static string FormatTypeName(RuntimeType type)
        {
            string ns = type.Namespace;
            if (string.IsNullOrEmpty(ns))
                return type.Name;
            return ns + "." + type.Name;
        }
        private bool TryFindHandler(int pc, int exceptionRef, out EhRegionRecord handler)
        {
            var m = _image.Methods[_currentMethodIndex];
            int bestSpan = int.MaxValue;
            handler = default;
            bool found = false;
            RuntimeType? exceptionType = null;
            if (exceptionRef != 0)
                exceptionType = GetObjectTypeFromRef(exceptionRef);

            for (int i = 0; i < m.EhCount; i++)
            {
                var h = _image.EhRegions[m.EhStartIndex + i];
                if (pc < h.TryStartPc || pc >= h.TryEndPc)
                    continue;
                int span = h.TryEndPc - h.TryStartPc;
                if (span >= bestSpan)
                    continue;
                var kind = (EhRegionKind)h.Kind;
                if (kind == EhRegionKind.Finally || kind == EhRegionKind.Fault)
                {
                    bestSpan = span;
                    handler = h;
                    found = true;
                    continue;
                }
                if (kind == EhRegionKind.CatchAll)
                {
                    bestSpan = span;
                    handler = h;
                    found = true;
                    continue;
                }
                if (kind == EhRegionKind.Catch && exceptionType is not null)
                {
                    var catchType = _rts.GetTypeById(h.CatchTypeId);
                    if (IsAssignableTo(exceptionType, catchType))
                    {
                        bestSpan = span;
                        handler = h;
                        found = true;
                    }
                }
            }
            return found;
        }

        private bool TryFindEnclosingFinally(int fromPc, int targetPc, out EhRegionRecord region)
        {
            var m = _image.Methods[_currentMethodIndex];
            int bestSpan = int.MaxValue;
            region = default;
            bool found = false;
            for (int i = 0; i < m.EhCount; i++)
            {
                var h = _image.EhRegions[m.EhStartIndex + i];
                if ((EhRegionKind)h.Kind != EhRegionKind.Finally)
                    continue;
                if (fromPc < h.TryStartPc || fromPc >= h.TryEndPc)
                    continue;
                if (targetPc >= h.TryStartPc && targetPc < h.TryEndPc)
                    continue;
                int span = h.TryEndPc - h.TryStartPc;
                if (span < bestSpan)
                {
                    bestSpan = span;
                    region = h;
                    found = true;
                }
            }
            return found;
        }

        internal string? HostReadString(VmValue v, CancellationToken ct)
        {
            if (v.Kind == VmValueKind.Null) return null;
            if (v.Kind != VmValueKind.Ref)
                throw new InvalidOperationException($"Expected string ref, got {v.Kind}.");

            ValidateStringRef(v.Payload, out int strAbs);
            int len = ReadI32(strAbs + StringLengthOffset);
            if (len < 0) throw new InvalidOperationException("Corrupted string length.");

            char[] chars = new char[len];
            int charsAbs = strAbs + StringCharsOffset;
            CheckIndirectAccess(charsAbs, checked(len * 2), false);
            for (int i = 0; i < len; i++)
            {
                if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested();
                chars[i] = (char)ReadU16(charsAbs + i * 2);
            }
            return new string(chars);
        }

        internal VmValue HostAllocString(string? s)
        {
            if (s is null) return VmValue.Null;
            return new VmValue(VmValueKind.Ref, AllocStringFromManaged(s));
        }

        internal VmValue HostAllocArray(RuntimeType arrayType, int length)
        {
            if (arrayType.Kind != RuntimeTypeKind.Array)
                throw new ArgumentException("Type is not an array.", nameof(arrayType));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            return new VmValue(VmValueKind.Ref, AllocArray(arrayType, length));
        }

        internal int HostGetArrayLength(VmValue array)
        {
            ValidateHostArrayRef(array, out int arrAbs, out _);
            return ReadI32(arrAbs + ArrayLengthOffset);
        }

        internal VmValue HostGetArrayElement(VmValue array, int index)
        {
            ValidateHostArrayRef(array, out int arrAbs, out RuntimeType arrayType);
            int length = ReadI32(arrAbs + ArrayLengthOffset);
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();

            RuntimeType elemType = arrayType.ElementType
                ?? throw new InvalidOperationException("Array type has no element type.");
            int elemSize = StorageSizeOf(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));
            CheckHeapAccess(elemAbs, elemSize, false);
            return LoadHostValue(elemAbs, elemType);
        }

        internal void HostSetArrayElement(VmValue array, int index, VmValue value)
        {
            ValidateHostArrayRef(array, out int arrAbs, out RuntimeType arrayType);
            int length = ReadI32(arrAbs + ArrayLengthOffset);
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();

            RuntimeType elemType = arrayType.ElementType
                ?? throw new InvalidOperationException("Array type has no element type.");
            int elemSize = StorageSizeOf(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));
            CheckHeapAccess(elemAbs, elemSize, true);
            StoreHostValue(elemAbs, elemType, value);
        }

        internal int HostGetAddress(VmValue v)
        {
            return v.Kind switch
            {
                VmValueKind.Ref or VmValueKind.Ptr or VmValueKind.ByRef or VmValueKind.Value => checked((int)v.Payload),
                VmValueKind.Null => 0,
                _ => throw new InvalidOperationException($"Expected address-like VM value, got {v.Kind}.")
            };
        }

        internal Span<byte> HostGetSpan(int abs, int size, bool writable)
        {
            CheckIndirectAccess(abs, size, writable);
            return _mem.AsSpan(abs, size);
        }

        internal void RegisterHostOverride(HostOverride ov)
        {
            if (ov is null) throw new ArgumentNullException(nameof(ov));
            _hostOverrides[ov.MethodId] = ov;
        }

        private void ValidateHostArrayRef(VmValue array, out int arrAbs, out RuntimeType arrayType)
        {
            if (array.Kind == VmValueKind.Null)
                throw new NullReferenceException();
            if (array.Kind != VmValueKind.Ref)
                throw new InvalidOperationException($"Expected array ref, got {array.Kind}.");

            arrayType = ValidateArrayRef(array.Payload, out arrAbs, out _);
        }

        private VmValue LoadHostValue(int abs, RuntimeType type)
        {
            if (type.IsReferenceType)
            {
                long value = ReadNative(abs);
                return value == 0 ? VmValue.Null : new VmValue(VmValueKind.Ref, value);
            }

            if (type.Kind == RuntimeTypeKind.Pointer)
            {
                long value = ReadNative(abs);
                return value == 0 ? VmValue.Null : new VmValue(VmValueKind.Ptr, value);
            }

            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                long value = ReadNative(abs);
                return value == 0 ? VmValue.Null : new VmValue(VmValueKind.ByRef, value);
            }

            if (type.Kind == RuntimeTypeKind.Enum && type.ElementType != null)
                return LoadHostValue(abs, type.ElementType);

            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return VmValue.FromInt32(unchecked((int)ReadSizedInteger(abs, type)));

                    case "Int64":
                    case "UInt64":
                        return VmValue.FromInt64(ReadSizedInteger(abs, type));

                    case "Single":
                        return VmValue.FromDouble(BitConverter.Int32BitsToSingle(ReadI32(abs)));

                    case "Double":
                        return VmValue.FromDouble(BitConverter.Int64BitsToDouble(ReadI64(abs)));

                    case "IntPtr":
                    case "UIntPtr":
                        return TargetArchitecture.PointerSize == 8
                            ? VmValue.FromInt64(ReadNative(abs))
                            : VmValue.FromInt32(unchecked((int)ReadNative(abs)));
                }
            }

            if (StorageSizeOf(type) <= 8 && !type.ContainsGcPointers)
                return StorageSizeOf(type) <= 4 ? VmValue.FromInt32(unchecked((int)ReadSizedInteger(abs, type))) : VmValue.FromInt64(ReadSizedInteger(abs, type));

            return new VmValue(VmValueKind.Value, abs, type.TypeId);
        }

        private void StoreHostValue(int abs, RuntimeType type, VmValue value)
        {
            if (type.IsReferenceType)
            {
                if (value.Kind is not (VmValueKind.Ref or VmValueKind.Null))
                    throw new InvalidOperationException($"Storing {value.Kind} into managed ref.");
                WriteNative(abs, value.Kind == VmValueKind.Null ? 0 : value.Payload);
                return;
            }

            if (type.Kind == RuntimeTypeKind.Pointer)
            {
                if (value.Kind is not (VmValueKind.Ptr or VmValueKind.ByRef or VmValueKind.Null))
                    throw new InvalidOperationException($"Storing {value.Kind} into pointer.");
                WriteNative(abs, value.Kind == VmValueKind.Null ? 0 : value.Payload);
                return;
            }

            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                if (value.Kind is not (VmValueKind.ByRef or VmValueKind.Null))
                    throw new InvalidOperationException($"Storing {value.Kind} into byref.");
                WriteNative(abs, value.Kind == VmValueKind.Null ? 0 : value.Payload);
                return;
            }

            if (type.Kind == RuntimeTypeKind.Enum)
            {
                if (type.ElementType != null)
                {
                    StoreHostValue(abs, type.ElementType, value);
                    return;
                }

                if (StorageSizeOf(type) <= 4) WriteSizedInteger(abs, type, value.AsInt32());
                else WriteSizedInteger(abs, type, value.AsInt64());
                return;
            }

            if (type.Namespace == "System" && type.Name == "Single")
            {
                WriteI32(abs, BitConverter.SingleToInt32Bits((float)value.AsDouble()));
                return;
            }

            if (type.Namespace == "System" && type.Name == "Double")
            {
                WriteI64(abs, BitConverter.DoubleToInt64Bits(value.AsDouble()));
                return;
            }

            if (type.Namespace == "System" && (type.Name == "Int64" || type.Name == "UInt64"))
            {
                WriteSizedInteger(abs, type, value.AsInt64());
                return;
            }

            if (type.Namespace == "System" && (type.Name == "IntPtr" || type.Name == "UIntPtr"))
            {
                WriteNative(abs, TargetArchitecture.PointerSize == 8 ? value.AsInt64() : value.AsInt32());
                return;
            }

            if (IsHostScalarInt32Type(type))
            {
                WriteSizedInteger(abs, type, value.AsInt32());
                return;
            }

            if (value.Kind == VmValueKind.Value)
            {
                RuntimeType valueType = _rts.GetTypeById(value.Aux);
                if (valueType.TypeId != type.TypeId)
                    throw new InvalidOperationException($"Struct type mismatch: value={valueType.Namespace}.{valueType.Name}, target={type.Namespace}.{type.Name}.");
                CopyTypedObject(abs, checked((int)value.Payload), type);
                return;
            }

            throw new NotSupportedException($"Host value marshal not supported for value type: {type.Namespace}.{type.Name}");
        }

        private static bool IsHostScalarInt32Type(RuntimeType type)
            => type.Namespace == "System" && (type.Name == "Boolean" || type.Name == "Char" || type.Name == "SByte" ||
                                              type.Name == "Byte" || type.Name == "Int16" || type.Name == "UInt16" ||
                                              type.Name == "Int32" || type.Name == "UInt32");

        private VmValue ReadHostArgument(RuntimeMethod rm, int logicalIndex)
        {
            RuntimeType type = GetLogicalArgumentType(rm, logicalIndex);

            if (type.Kind == RuntimeTypeKind.Enum && type.ElementType != null)
            {
                long rawEnum = ReadAbiScalarArgument(rm, logicalIndex);
                return StorageSizeOf(type.ElementType) <= 4 ? VmValue.FromInt32(unchecked((int)rawEnum)) : VmValue.FromInt64(rawEnum);
            }

            if (type.IsReferenceType)
            {
                long raw = ReadAbiScalarArgument(rm, logicalIndex);
                return raw == 0 ? VmValue.Null : new VmValue(VmValueKind.Ref, raw);
            }

            if (type.Kind == RuntimeTypeKind.Pointer)
            {
                long raw = ReadAbiScalarArgument(rm, logicalIndex);
                return raw == 0 ? VmValue.Null : new VmValue(VmValueKind.Ptr, raw);
            }

            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                long raw = ReadAbiScalarArgument(rm, logicalIndex);
                return raw == 0 ? VmValue.Null : new VmValue(VmValueKind.ByRef, raw);
            }

            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return VmValue.FromInt32(unchecked((int)ReadAbiScalarArgument(rm, logicalIndex)));

                    case "Int64":
                    case "UInt64":
                        return VmValue.FromInt64(ReadAbiScalarArgument(rm, logicalIndex));

                    case "Single":
                        return VmValue.FromDouble(BitConverter.Int32BitsToSingle(unchecked((int)ReadAbiScalarArgument(rm, logicalIndex))));

                    case "Double":
                        return VmValue.FromDouble(BitConverter.Int64BitsToDouble(ReadAbiScalarArgument(rm, logicalIndex)));

                    case "IntPtr":
                    case "UIntPtr":
                        return TargetArchitecture.PointerSize == 8
                            ? VmValue.FromInt64(ReadAbiScalarArgument(rm, logicalIndex))
                            : VmValue.FromInt32(unchecked((int)ReadAbiScalarArgument(rm, logicalIndex)));
                }
            }

            int address = checked((int)ReadAbiAggregateAddress(rm, logicalIndex));
            return new VmValue(VmValueKind.Value, address, type.TypeId);
        }

        private void SetHostReturn(RuntimeMethod rm, VmValue value)
        {
            RuntimeType type = rm.ReturnType;
            var abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: true);
            if (abi.PassingKind == AbiValuePassingKind.Void)
                return;

            if (type.Kind == RuntimeTypeKind.Enum)
            {
                if (type.ElementType != null && SetHostScalarReturn(type.ElementType, value))
                    return;

                if (StorageSizeOf(type) <= 4) SetReturnI4(value.AsInt32());
                else SetGpr(MachineRegisters.ReturnValue0, value.AsInt64());
                return;
            }

            if (type.IsReferenceType)
            {
                if (value.Kind is not (VmValueKind.Ref or VmValueKind.Null))
                    throw new InvalidOperationException($"Return type mismatch: expected managed ref, got {value.Kind}.");
                SetReturnRef(value.Kind == VmValueKind.Null ? 0 : value.Payload);
                return;
            }

            if (type.Kind == RuntimeTypeKind.Pointer)
            {
                if (value.Kind is not (VmValueKind.Ptr or VmValueKind.ByRef or VmValueKind.Null))
                    throw new InvalidOperationException($"Return type mismatch: expected ptr, got {value.Kind}.");
                SetReturnRef(value.Kind == VmValueKind.Null ? 0 : value.Payload);
                return;
            }

            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                if (value.Kind is not (VmValueKind.ByRef or VmValueKind.Null))
                    throw new InvalidOperationException($"Return type mismatch: expected byref, got {value.Kind}.");
                SetReturnRef(value.Kind == VmValueKind.Null ? 0 : value.Payload);
                return;
            }

            if (SetHostScalarReturn(type, value))
                return;

            if (value.Kind == VmValueKind.Value)
            {
                RuntimeType valueType = _rts.GetTypeById(value.Aux);
                if (valueType.TypeId != type.TypeId)
                    throw new InvalidOperationException($"Struct return type mismatch: value={valueType.Namespace}.{valueType.Name}, target={type.Namespace}.{type.Name}.");
                LoadTypedValueToReturn(checked((int)value.Payload), type);
                return;
            }

            throw new NotSupportedException($"Host return marshal not supported for value type: {type.Namespace}.{type.Name}");
        }

        private bool SetHostScalarReturn(RuntimeType type, VmValue value)
        {
            if (type.Namespace != "System")
                return false;

            switch (type.Name)
            {
                case "Boolean":
                case "Char":
                case "SByte":
                case "Byte":
                case "Int16":
                case "UInt16":
                case "Int32":
                case "UInt32":
                    SetReturnI4(value.AsInt32());
                    return true;

                case "Int64":
                case "UInt64":
                    SetGpr(MachineRegisters.ReturnValue0, value.AsInt64());
                    return true;

                case "Single":
                    SetFpr(MachineRegisters.FloatReturnValue0, unchecked((uint)BitConverter.SingleToInt32Bits((float)value.AsDouble())));
                    return true;

                case "Double":
                    SetFpr(MachineRegisters.FloatReturnValue0, BitConverter.DoubleToInt64Bits(value.AsDouble()));
                    return true;

                case "IntPtr":
                case "UIntPtr":
                    SetGpr(MachineRegisters.ReturnValue0, TargetArchitecture.PointerSize == 8 ? value.AsInt64() : value.AsInt32());
                    return true;
            }

            return false;
        }

        private bool TryInvokeHostOverride(RuntimeMethod rm, CancellationToken ct)
        {
            if (!_hostOverrides.TryGetValue(rm.MethodId, out var ov))
                return false;

            if (!rm.HasInternalCall)
                throw new InvalidOperationException($"Host override target is not InternalCall: {rm.DeclaringType.Namespace}.{rm.DeclaringType.Name}.{rm.Name}");
            if (!rm.IsStatic || rm.HasThis)
                throw new InvalidOperationException("Only static host overrides are supported by the register VM.");

            int argCount = rm.ParameterTypes.Length;
            Span<VmValue> args = argCount <= 32 ? stackalloc VmValue[argCount] : new VmValue[argCount];
            for (int i = 0; i < argCount; i++)
                args[i] = ReadHostArgument(rm, i);

            _hostCtx.SetToken(ct);
            VmValue ret = ov.Handler(_hostCtx, args);
            SetHostReturn(rm, ret);
            return true;
        }

        private bool TryInvokeIntrinsic(RuntimeMethod rm, CancellationToken ct)
        {
            if (rm.DeclaringType.Namespace == "System" && rm.DeclaringType.Name == "Console" && rm.Name == "_Write")
                return IntrinsicConsoleWrite(rm, ct);

            if (IsSystemStringType(rm.DeclaringType))
            {
                if (rm.HasThis && rm.Name == "get_Length" && rm.ParameterTypes.Length == 0)
                {
                    long s = ReadThisArgumentReference(rm);
                    ValidateStringRef(s, out int strAbs);
                    SetReturnI4(ReadI32(strAbs + StringLengthOffset));
                    return true;
                }
                if (rm.HasThis && (rm.Name == "GetPinnableReference" || rm.Name == "GetRawStringData") && rm.ParameterTypes.Length == 0)
                {
                    long s = ReadThisArgumentReference(rm);
                    ValidateStringRef(s, out int strAbs);
                    SetReturnRef(strAbs + StringCharsOffset);
                    return true;
                }
                if (!rm.HasThis && rm.Name == "FastAllocateString" && rm.ParameterTypes.Length == 1)
                {
                    int len = (int)ReadAbiScalarArgument(rm, 0);
                    SetReturnRef(AllocStringUninitialized(len));
                    return true;
                }
            }

            if (rm.DeclaringType.Namespace == "System" && rm.DeclaringType.Name == "Array")
            {
                if (rm.HasThis && rm.Name == "get_Length" && rm.ParameterTypes.Length == 0)
                {
                    long arr = ReadThisArgumentReference(rm);
                    ValidateArrayRef(arr, out int arrAbs, out _);
                    SetReturnI4(ReadI32(arrAbs + ArrayLengthOffset));
                    return true;
                }
                if (!rm.HasThis && rm.Name == "ClearInternal" && rm.ParameterTypes.Length == 3)
                {
                    long arr = ReadAbiScalarArgument(rm, 0);
                    int index = (int)ReadAbiScalarArgument(rm, 1);
                    int len = (int)ReadAbiScalarArgument(rm, 2);
                    ClearArray(arr, index, len);
                    return true;
                }
                if (!rm.HasThis && rm.Name == "CopyInternal" && rm.ParameterTypes.Length == 5)
                {
                    bool ok = CopyArray(
                        ReadAbiScalarArgument(rm, 0),
                        (int)ReadAbiScalarArgument(rm, 1),
                        ReadAbiScalarArgument(rm, 2),
                        (int)ReadAbiScalarArgument(rm, 3),
                        (int)ReadAbiScalarArgument(rm, 4));
                    SetReturnI4(ok ? 1 : 0);
                    return true;
                }
            }

            if (rm.DeclaringType.Namespace == "System.Runtime.CompilerServices" && rm.DeclaringType.Name == "RuntimeHelpers")
            {
                if (!rm.HasThis && rm.Name == "IsReferenceOrContainsReferences" && rm.ParameterTypes.Length == 0)
                {
                    bool result = rm.MethodGenericArguments.Length == 1 && TypeIsReferenceOrContainsReferences(rm.MethodGenericArguments[0]);
                    SetReturnI4(result ? 1 : 0);
                    return true;
                }
                if (!rm.HasThis && rm.Name == "IsKnownConstant" && rm.ParameterTypes.Length == 1)
                {
                    SetReturnI4(0);
                    return true;
                }
                if (!rm.HasThis && rm.Name == "GetHashCode" && rm.ParameterTypes.Length == 1)
                {
                    long obj = ReadAbiScalarArgument(rm, 0);
                    SetReturnI4(unchecked((int)obj));
                    return true;
                }
            }

            if (rm.DeclaringType.Namespace == "System.Runtime.CompilerServices" && rm.DeclaringType.Name == "Unsafe")
                return IntrinsicUnsafe(rm);

            if (rm.DeclaringType.Namespace == "System.Runtime.InteropServices" && rm.DeclaringType.Name == "MemoryMarshal")
                return IntrinsicMemoryMarshal(rm);

            if (IsCoreLibRandomLike(rm.DeclaringType) && rm.Name == "Next")
            {
                if (rm.ParameterTypes.Length == 0)
                    SetReturnI4(Random.Shared.Next());
                else if (rm.ParameterTypes.Length == 1)
                    SetReturnI4(Random.Shared.Next((int)ReadAbiScalarArgument(rm, rm.HasThis ? 1 : 0)));
                else if (rm.ParameterTypes.Length == 2)
                    SetReturnI4(Random.Shared.Next((int)ReadAbiScalarArgument(rm, rm.HasThis ? 1 : 0), (int)ReadAbiScalarArgument(rm, rm.HasThis ? 2 : 1)));
                else
                    return false;
                return true;
            }

            if (rm.HasInternalCall)
                throw new NotSupportedException("InternalCall is not implemented: " + rm.DeclaringType.Namespace + "." + rm.DeclaringType.Name + "." + rm.Name);

            return false;
        }

        private bool IntrinsicConsoleWrite(RuntimeMethod rm, CancellationToken ct)
        {
            if (rm.HasThis || rm.ParameterTypes.Length != 1)
                throw new NotSupportedException("Console._Write intrinsic arity mismatch.");

            var p = rm.ParameterTypes[0];
            if (IsSystemStringType(p))
            {
                long s = ReadAbiScalarArgument(rm, 0);
                if (s == 0) return true;
                ValidateStringRef(s, out int strAbs);
                int len = ReadI32(strAbs + StringLengthOffset);
                int chars = strAbs + StringCharsOffset;
                CheckIndirectAccess(chars, checked(len * 2), false);
                for (int i = 0; i < len; i++)
                {
                    if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                    _textWriter.Write((char)ReadU16(chars + i * 2));
                }
                return true;
            }

            if (p.Kind == RuntimeTypeKind.Pointer && p.ElementType is { Namespace: "System", Name: "Char" })
            {
                int abs = checked((int)ReadAbiScalarArgument(rm, 0));
                if (abs == 0) return true;
                const int MaxChars = 8 * 1024;
                for (int i = 0; i < MaxChars; i++)
                {
                    if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                    int pos = checked(abs + i * 2);
                    CheckIndirectAccess(pos, 2, false);
                    ushort ch = ReadU16(pos);
                    if (ch == 0) break;
                    _textWriter.Write((char)ch);
                }
                return true;
            }

            if (p.Name.StartsWith("ReadOnlySpan`1", StringComparison.Ordinal))
            {
                long addr = ReadAbiAggregateAddress(rm, 0);
                RuntimeField? reference = null;
                RuntimeField? length = null;
                for (int i = 0; i < p.InstanceFields.Length; i++)
                {
                    if (p.InstanceFields[i].Name == "_reference") reference = p.InstanceFields[i];
                    else if (p.InstanceFields[i].Name == "_length") length = p.InstanceFields[i];
                }
                if (reference is null || length is null)
                    throw new NotSupportedException("ReadOnlySpan<char> intrinsic cannot locate fields.");

                int chars = checked((int)ReadSizedInteger(checked((int)addr + reference.Offset), reference.FieldType));
                int len = ReadI32(checked((int)addr + length.Offset));
                CheckIndirectAccess(chars, checked(len * 2), false);
                for (int i = 0; i < len; i++)
                {
                    if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                    _textWriter.Write((char)ReadU16(chars + i * 2));
                }
                return true;
            }

            throw new NotSupportedException("Console._Write intrinsic unsupported parameter: " + p.Namespace + "." + p.Name);
        }

        private bool IntrinsicUnsafe(RuntimeMethod rm)
        {
            string n = rm.Name;
            if (n == "SizeOf" && rm.MethodGenericArguments.Length == 1)
            {
                SetReturnI4(StorageSizeOf(rm.MethodGenericArguments[0]));
                return true;
            }
            if (n == "AreSame")
            {
                SetReturnI4(ReadAbiScalarArgument(rm, 0) == ReadAbiScalarArgument(rm, 1) ? 1 : 0);
                return true;
            }
            if (n == "ByteOffset")
            {
                SetReturnRef(checked(ReadAbiScalarArgument(rm, 1) - ReadAbiScalarArgument(rm, 0)));
                return true;
            }
            if (n == "Add" || n == "AddByteOffset")
            {
                long src = ReadAbiScalarArgument(rm, 0);
                long offs = ReadAbiScalarArgument(rm, 1);
                int elemSize = rm.MethodGenericArguments.Length >= 1 ? StorageSizeOf(rm.MethodGenericArguments[^1]) : 1;
                if (n == "Add") offs = checked(offs * elemSize);
                SetReturnRef(checked(src + offs));
                return true;
            }
            if (n == "As" || n == "AsRef")
            {
                SetReturnRef(ReadAbiScalarArgument(rm, 0));
                return true;
            }
            if (n == "ReadUnaligned")
            {
                long src = ReadAbiScalarArgument(rm, 0);
                RuntimeType t = rm.MethodGenericArguments[0];
                LoadTypedValueToReturn(checked((int)src), t);
                return true;
            }
            if (n == "WriteUnaligned")
            {
                long dst = ReadAbiScalarArgument(rm, 0);
                RuntimeType t = rm.MethodGenericArguments[0];
                StoreAbiArgumentToAddress(rm, 1, checked((int)dst), t);
                return true;
            }
            if (n == "BitCast" && rm.MethodGenericArguments.Length == 2)
            {
                RuntimeType from = rm.MethodGenericArguments[0];
                RuntimeType to = rm.MethodGenericArguments[1];
                if (StorageSizeOf(from) != StorageSizeOf(to))
                    throw new NotSupportedException("Unsafe.BitCast source and destination sizes differ.");
                LoadAbiArgumentBitsToReturn(rm, 0, from, to);
                return true;
            }
            return false;
        }

        private bool IntrinsicMemoryMarshal(RuntimeMethod rm)
        {
            if (rm.Name == "GetArrayDataReference" && rm.ParameterTypes.Length == 1)
            {
                long arr = ReadAbiScalarArgument(rm, 0);
                ValidateArrayRef(arr, out int arrAbs, out _);
                SetReturnRef(arrAbs + ArrayDataOffset);
                return true;
            }
            if (rm.Name == "GetReference" && rm.ParameterTypes.Length == 1)
            {
                long spanAddr = ReadAbiAggregateAddress(rm, 0);
                RuntimeType spanType = rm.ParameterTypes[0];
                RuntimeField? refField = null;
                for (int i = 0; i < spanType.InstanceFields.Length; i++)
                    if (spanType.InstanceFields[i].Name == "_reference") refField = spanType.InstanceFields[i];
                if (refField is null) throw new NotSupportedException("Span reference field not found.");
                SetReturnRef(ReadNative(checked((int)spanAddr + refField.Offset)));
                return true;
            }
            if ((rm.Name == "Read" || rm.Name == "TryRead") && rm.ParameterTypes.Length >= 1 && rm.MethodGenericArguments.Length == 1)
            {
                RuntimeType valueType = rm.MethodGenericArguments[0];
                int spanAddr = checked((int)ReadAbiAggregateAddress(rm, 0));
                var span = ReadSpanFields(rm.ParameterTypes[0], spanAddr);
                int size = StorageSizeOf(valueType);
                if (rm.Name == "TryRead")
                {
                    if (span.Length < size)
                    {
                        int dst = checked((int)ReadAbiScalarArgument(rm, 1));
                        InitTypedObject(dst, valueType);
                        SetReturnI4(0);
                        return true;
                    }
                    CopyTypedObject(checked((int)ReadAbiScalarArgument(rm, 1)), span.Reference, valueType);
                    SetReturnI4(1);
                    return true;
                }

                if (span.Length < size)
                    throw new ArgumentOutOfRangeException();
                LoadTypedValueToReturn(span.Reference, valueType);
                return true;
            }
            if ((rm.Name == "Write" || rm.Name == "TryWrite") && rm.ParameterTypes.Length >= 2 && rm.MethodGenericArguments.Length == 1)
            {
                RuntimeType valueType = rm.MethodGenericArguments[0];
                int spanAddr = checked((int)ReadAbiAggregateAddress(rm, 0));
                var span = ReadSpanFields(rm.ParameterTypes[0], spanAddr);
                int size = StorageSizeOf(valueType);
                if (span.Length < size)
                {
                    if (rm.Name == "TryWrite")
                    {
                        SetReturnI4(0);
                        return true;
                    }
                    throw new ArgumentOutOfRangeException();
                }

                StoreAbiArgumentToAddress(rm, 1, span.Reference, valueType);
                if (rm.Name == "TryWrite")
                    SetReturnI4(1);
                return true;
            }
            return false;
        }

        private readonly struct AbiArgumentSlice
        {
            public readonly AbiArgumentLocation Location;
            public readonly RegisterClass RegisterClass;
            public readonly int Offset;
            public readonly int Size;

            public AbiArgumentSlice(AbiArgumentLocation location, RegisterClass registerClass, int offset, int size)
            {
                Location = location;
                RegisterClass = registerClass;
                Offset = offset;
                Size = size;
            }
        }

        private int LogicalArgumentCount(RuntimeMethod method)
            => method.ParameterTypes.Length + (method.HasThis ? 1 : 0);

        private int HiddenReturnBufferInsertionIndex(RuntimeMethod method)
        {
            if ((CurrentInvocationCallFlags() & CallFlags.HiddenReturnBuffer) == 0)
                return -1;

            if (method.DeclaringType.IsValueType && StringComparer.Ordinal.Equals(method.Name, ".ctor"))
                return -1;

            return MachineAbi.HiddenReturnBufferInsertionIndex(method, LogicalArgumentCount(method));
        }

        private long ReadHiddenReturnBufferAddress(RuntimeMethod method)
        {
            int insertion = HiddenReturnBufferInsertionIndex(method);
            if (insertion < 0)
                throw new InvalidOperationException("Current call has no hidden return buffer.");

            int general = 0, floating = 0, stack = 0;
            for (int i = 0; i <= insertion; i++)
            {
                if (i == insertion)
                {
                    AbiArgumentLocation loc = AssignScalarAbiLocation(RegisterClass.General, TargetArchitecture.PointerSize, ref general, ref floating, ref stack);
                    return loc.IsRegister ? X(loc.Register) : ReadNative(GetAbiStackArgumentAddress(method, loc));
                }

                RuntimeType argType = GetLogicalArgumentType(method, i);
                ConsumeAbiValue(argType, ref general, ref floating, ref stack);
            }

            throw new InvalidOperationException("Invalid hidden return buffer position.");
        }

        private ImmutableArray<AbiArgumentSlice> GetAbiArgumentSlices(RuntimeMethod method, int logicalIndex)
        {
            int argCount = LogicalArgumentCount(method);
            if ((uint)logicalIndex >= (uint)argCount)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex));

            int hiddenIndex = HiddenReturnBufferInsertionIndex(method);
            int general = 0, floating = 0, stack = 0;

            for (int i = 0; i <= logicalIndex; i++)
            {
                if (hiddenIndex == i)
                    ConsumeHiddenReturnBuffer(ref general, ref stack);

                RuntimeType argType = GetLogicalArgumentType(method, i);
                var abi = MachineAbi.ClassifyValue(argType, MachineAbi.StackKindForType(argType), isReturn: false);

                if (abi.PassingKind == AbiValuePassingKind.Void)
                    continue;

                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    AbiArgumentLocation loc = AssignScalarAbiLocation(abi.RegisterClass, abi.Size, ref general, ref floating, ref stack);
                    if (i == logicalIndex)
                    {
                        return ImmutableArray.Create(new AbiArgumentSlice(
                            loc,
                            abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass,
                            0,
                            abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size));
                    }
                    continue;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    var result = i == logicalIndex ? ImmutableArray.CreateBuilder<AbiArgumentSlice>(segments.Length) : null;
                    int aggregateStackSlot = -1;
                    int aggregateStackBaseOffset = 0;

                    for (int s = 0; s < segments.Length; s++)
                    {
                        AbiRegisterSegment segment = segments[s];
                        AbiArgumentLocation loc = AssignAggregateSegmentAbiLocation(
                            segment,
                            ref general,
                            ref floating,
                            ref stack,
                            ref aggregateStackSlot,
                            ref aggregateStackBaseOffset);

                        result?.Add(new AbiArgumentSlice(loc, segment.RegisterClass, segment.Offset, segment.Size));
                    }

                    if (i == logicalIndex)
                        return result!.ToImmutable();
                    continue;
                }

                int stackSize = abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size;
                AbiArgumentLocation stackLoc = AbiArgumentLocation.ForStack(RegisterClass.General, stack++, 0, stackSize);
                if (i == logicalIndex)
                {
                    return ImmutableArray.Create(new AbiArgumentSlice(
                        stackLoc,
                        RegisterClass.General,
                        0,
                        stackSize));
                }
            }

            throw new InvalidOperationException("Invalid ABI argument index.");
        }

        private void ConsumeAbiValue(RuntimeType type, ref int general, ref int floating, ref int stack)
        {
            var abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: false);
            if (abi.PassingKind == AbiValuePassingKind.Void)
                return;
            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                _ = AssignScalarAbiLocation(abi.RegisterClass, abi.Size, ref general, ref floating, ref stack);
                return;
            }
            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                int aggregateStackSlot = -1;
                int aggregateStackBaseOffset = 0;
                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    _ = AssignAggregateSegmentAbiLocation(
                        segments[i],
                        ref general,
                        ref floating,
                        ref stack,
                        ref aggregateStackSlot,
                        ref aggregateStackBaseOffset);
                }
                return;
            }
            stack++;
        }

        private static void ConsumeHiddenReturnBuffer(ref int general, ref int stack)
        {
            MachineRegister r = MachineRegisters.GetIntegerArgumentRegister(general++);
            if (r == MachineRegister.Invalid)
                stack++;
        }

        private long ReadAbiValueBits(RuntimeMethod method, int logicalIndex, RuntimeType type, int size)
        {
            var slices = GetAbiArgumentSlices(method, logicalIndex);
            if (slices.Length == 1)
            {
                AbiArgumentSlice slice = slices[0];
                if (slice.Location.IsRegister)
                    return slice.RegisterClass == RegisterClass.Float ? GetFprBits((byte)slice.Location.Register) : X(slice.Location.Register);

                int addr = GetAbiStackArgumentAddress(method, slice.Location);
                return ReadRawBits(addr, Math.Min(size, slice.Size));
            }

            int temp = MaterializeAbiArgumentToStack(method, logicalIndex, type);
            return ReadRawBits(temp, size);
        }

        private int MaterializeAbiArgumentToStack(RuntimeMethod method, int logicalIndex, RuntimeType type)
        {
            int size = StorageSizeOf(type);
            int align = StorageAlignOf(type);
            int dst = StackAllocBytes(size, align);
            var slices = GetAbiArgumentSlices(method, logicalIndex);
            for (int i = 0; i < slices.Length; i++)
            {
                AbiArgumentSlice slice = slices[i];
                int count = Math.Min(slice.Size, size - slice.Offset);
                if (count <= 0)
                    continue;

                int target = checked(dst + slice.Offset);
                if (slice.Location.IsRegister)
                {
                    long bits = slice.RegisterClass == RegisterClass.Float
                        ? GetFprBits((byte)slice.Location.Register)
                        : X(slice.Location.Register);
                    WriteRawBits(target, bits, count);
                }
                else
                {
                    int source = GetAbiStackArgumentAddress(method, slice.Location);
                    CopyBlock(target, source, count);
                }
            }
            return dst;
        }

        private long ReadRawBits(int abs, int size)
        {
            return size switch
            {
                1 => ReadU8(abs),
                2 => ReadU16(abs),
                4 => unchecked((uint)ReadI32(abs)),
                8 => ReadI64(abs),
                _ => throw new InvalidOperationException("Unsupported ABI scalar bit size: " + size.ToString()),
            };
        }

        private void WriteRawBits(int abs, long value, int size)
        {
            switch (size)
            {
                case 1: WriteU8(abs, unchecked((byte)value)); return;
                case 2: WriteU16(abs, unchecked((ushort)value)); return;
                case 4: WriteI32(abs, unchecked((int)value)); return;
                case 8: WriteI64(abs, value); return;
                default: throw new InvalidOperationException("Unsupported ABI scalar bit size: " + size.ToString());
            }
        }

        private static MachineRegister ReturnRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
        {
            if (registerClass == RegisterClass.Float)
            {
                return floatIndex++ switch
                {
                    0 => MachineRegisters.FloatReturnValue0,
                    1 => MachineRegisters.FloatReturnValue1,
                    _ => throw new InvalidOperationException("Too many floating return registers."),
                };
            }

            if (registerClass == RegisterClass.General)
            {
                return generalIndex++ switch
                {
                    0 => MachineRegisters.ReturnValue0,
                    1 => MachineRegisters.ReturnValue1,
                    _ => throw new InvalidOperationException("Too many integer return registers."),
                };
            }

            throw new InvalidOperationException("Invalid ABI return register class.");
        }

        private (int Reference, int Length) ReadSpanFields(RuntimeType spanType, int spanAddr)
        {
            RuntimeField? refField = null;
            RuntimeField? lenField = null;
            for (int i = 0; i < spanType.InstanceFields.Length; i++)
            {
                RuntimeField f = spanType.InstanceFields[i];
                if (f.Name == "_reference") refField = f;
                else if (f.Name == "_length") lenField = f;
            }
            if (refField is null || lenField is null)
                throw new NotSupportedException("Span-like intrinsic cannot locate _reference/_length fields.");

            int reference = checked((int)ReadSizedInteger(spanAddr + refField.Offset, refField.FieldType));
            int length = ReadI32(spanAddr + lenField.Offset);
            if (length < 0)
                throw new InvalidOperationException("Corrupted span length.");
            return (reference, length);
        }

        private long ReadThisArgumentReference(RuntimeMethod method)
        {
            if (!method.HasThis)
                throw new InvalidOperationException("Method has no this argument.");
            return ReadAbiScalarArgument(method, 0);
        }

        private void SetThisArgumentReference(RuntimeMethod method, long value)
        {
            int general = 0, floating = 0, stack = 0;
            var loc = AssignScalarAbiLocation(RegisterClass.General, TargetArchitecture.PointerSize, ref general, ref floating, ref stack);
            if (loc.IsRegister)
                SetGpr(loc.Register, value);
            else
                WriteNative(GetOutgoingArgAddress(loc.StackSlotIndex) + loc.StackOffset, value);
        }

        private long ReadAbiScalarArgument(RuntimeMethod method, int logicalIndex)
        {
            RuntimeType type = GetLogicalArgumentType(method, logicalIndex);
            var abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: false);
            var slices = GetAbiArgumentSlices(method, logicalIndex);
            if (slices.Length == 0)
                return 0;

            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                AbiArgumentSlice slice = slices[0];
                if (slice.Location.IsRegister)
                    return slice.RegisterClass == RegisterClass.Float ? GetFprBits((byte)slice.Location.Register) : X(slice.Location.Register);
                return ReadSizedInteger(GetAbiStackArgumentAddress(method, slice.Location), type);
            }

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister && StorageSizeOf(type) <= 8)
            {
                int temp = MaterializeAbiArgumentToStack(method, logicalIndex, type);
                return ReadSizedInteger(temp, type);
            }

            return ReadAbiAggregateAddress(method, logicalIndex);
        }

        private long ReadAbiAggregateAddress(RuntimeMethod method, int logicalIndex)
        {
            RuntimeType t = GetLogicalArgumentType(method, logicalIndex);
            var abi = MachineAbi.ClassifyValue(t, MachineAbi.StackKindForType(t), isReturn: false);
            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister && (t.Kind == RuntimeTypeKind.ByRef || t.Kind == RuntimeTypeKind.Pointer || t.IsReferenceType))
                return ReadAbiScalarArgument(method, logicalIndex);

            var slices = GetAbiArgumentSlices(method, logicalIndex);
            if (slices.Length == 1 && slices[0].Location.IsStack && abi.PassingKind != AbiValuePassingKind.MultiRegister)
                return GetAbiStackArgumentAddress(method, slices[0].Location);

            return MaterializeAbiArgumentToStack(method, logicalIndex, t);
        }

        private RuntimeType GetLogicalArgumentType(RuntimeMethod method, int logicalIndex)
        {
            if (method.HasThis)
            {
                if (logicalIndex == 0)
                    return method.DeclaringType.IsValueType ? _rts.GetByRefType(method.DeclaringType) : method.DeclaringType;
                logicalIndex--;
            }
            if ((uint)logicalIndex >= (uint)method.ParameterTypes.Length)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex));
            return method.ParameterTypes[logicalIndex];
        }

        private AbiArgumentLocation AssignScalarAbiLocation(RegisterClass rc, int size, ref int general, ref int floating, ref int stack)
        {
            int actualSize = size <= 0 ? TargetArchitecture.PointerSize : size;
            if (rc == RegisterClass.Float)
            {
                var r = MachineRegisters.GetFloatArgumentRegister(floating++);
                if (r != MachineRegister.Invalid) return AbiArgumentLocation.ForRegister(RegisterClass.Float, r, actualSize);
                return AbiArgumentLocation.ForStack(RegisterClass.Float, stack++, 0, actualSize);
            }
            if (rc == RegisterClass.General || rc == RegisterClass.Invalid)
            {
                var r = MachineRegisters.GetIntegerArgumentRegister(general++);
                if (r != MachineRegister.Invalid) return AbiArgumentLocation.ForRegister(RegisterClass.General, r, actualSize);
                return AbiArgumentLocation.ForStack(RegisterClass.General, stack++, 0, actualSize);
            }
            throw new InvalidOperationException("Invalid ABI register class.");
        }

        private AbiArgumentLocation AssignAggregateSegmentAbiLocation(
            AbiRegisterSegment segment,
            ref int general,
            ref int floating,
            ref int stack,
            ref int aggregateStackSlot,
            ref int aggregateStackBaseOffset)
        {
            MachineRegister reg;
            if (segment.RegisterClass == RegisterClass.Float)
            {
                reg = MachineRegisters.GetFloatArgumentRegister(floating++);
                if (reg != MachineRegister.Invalid)
                    return AbiArgumentLocation.ForRegister(RegisterClass.Float, reg, segment.Size);
            }
            else if (segment.RegisterClass == RegisterClass.General)
            {
                reg = MachineRegisters.GetIntegerArgumentRegister(general++);
                if (reg != MachineRegister.Invalid)
                    return AbiArgumentLocation.ForRegister(RegisterClass.General, reg, segment.Size);
            }
            else
            {
                throw new InvalidOperationException("Invalid ABI aggregate segment register class.");
            }

            if (aggregateStackSlot < 0)
            {
                aggregateStackSlot = stack++;
                aggregateStackBaseOffset = segment.Offset;
            }

            return AbiArgumentLocation.ForStack(
                segment.RegisterClass,
                aggregateStackSlot,
                checked(segment.Offset - aggregateStackBaseOffset),
                segment.Size);
        }

        private int GetCurrentOutgoingArgumentBase()
        {
            if (_currentMethodIndex < 0)
                throw new InvalidOperationException("No current method for outgoing argument address.");

            MethodRecord method = _image.Methods[_currentMethodIndex];
            int outgoingBaseOffset = 0;
            if (method.OutgoingArgumentAreaSize != 0)
            {
                outgoingBaseOffset = method.OutgoingArgumentAreaOffset;
                if (outgoingBaseOffset < 0)
                    throw new InvalidOperationException("Corrupted method outgoing argument area metadata.");
            }

            long frameBase = (((MethodFlags)method.Flags & MethodFlags.UsesFramePointer) != 0)
                ? X(MachineRegisters.FramePointer)
                : X(MachineRegisters.StackPointer);

            int addr = checked((int)frameBase + outgoingBaseOffset);
            CheckStackRange(addr, method.OutgoingArgumentAreaSize);
            return addr;
        }

        private int GetOutgoingArgAddress(int stackIndex)
        {
            if (stackIndex < 0) throw new ArgumentOutOfRangeException(nameof(stackIndex));

            MethodRecord method = _image.Methods[_currentMethodIndex];
            int slotSize = MachineAbi.StackArgumentSlotSize;
            int slotOffset = checked(stackIndex * slotSize);
            if (method.OutgoingArgumentAreaSize != 0 && slotOffset + slotSize > method.OutgoingArgumentAreaSize)
                throw new InvalidOperationException("ABI stack argument slot is outside caller outgoing argument area.");

            int addr = checked(GetCurrentOutgoingArgumentBase() + slotOffset);
            CheckStackRange(addr, slotSize);
            return addr;
        }

        private int GetIncomingArgAddress(int stackIndex)
        {
            if (stackIndex < 0) throw new ArgumentOutOfRangeException(nameof(stackIndex));
            long baseAddress = TopIncomingStackArgBase();
            if (baseAddress == 0)
                return GetOutgoingArgAddress(stackIndex);

            int slotSize = MachineAbi.StackArgumentSlotSize;
            int addr = checked((int)baseAddress + checked(stackIndex * slotSize));
            CheckStackRange(addr, slotSize);
            return addr;
        }

        private int GetAbiStackArgumentAddress(RuntimeMethod method, AbiArgumentLocation location)
        {
            int slotBase = IsReadingCurrentManagedInvocation(method)
                ? GetIncomingArgAddress(location.StackSlotIndex)
                : GetOutgoingArgAddress(location.StackSlotIndex);
            return checked(slotBase + location.StackOffset);
        }

        private void LoadAbiArgumentToReturn(RuntimeMethod rm, int logicalIndex, RuntimeType type)
            => LoadAbiArgumentBitsToReturn(rm, logicalIndex, type, type);

        private void LoadAbiArgumentBitsToReturn(RuntimeMethod rm, int logicalIndex, RuntimeType sourceType, RuntimeType returnType)
        {
            int size = StorageSizeOf(returnType);
            var returnAbi = MachineAbi.ClassifyValue(returnType, MachineAbi.StackKindForType(returnType), isReturn: true);
            if (returnAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                long raw = ReadAbiValueBits(rm, logicalIndex, sourceType, size);
                if (returnAbi.RegisterClass == RegisterClass.Float)
                    SetFpr(MachineRegisters.FloatReturnValue0, raw);
                else
                    SetGpr(MachineRegisters.ReturnValue0, raw);
                return;
            }

            int source = MaterializeAbiArgumentToStack(rm, logicalIndex, sourceType);
            LoadTypedValueToReturn(source, returnType);
        }

        private void LoadTypedValueToReturn(int abs, RuntimeType type)
        {
            var abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: true);
            if (abi.PassingKind == AbiValuePassingKind.Void)
                return;

            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                if (abi.RegisterClass == RegisterClass.Float)
                    SetFpr(MachineRegisters.FloatReturnValue0, StorageSizeOf(type) == 4 ? (uint)ReadI32(abs) : ReadI64(abs));
                else
                    SetGpr(MachineRegisters.ReturnValue0, ReadSizedInteger(abs, type));
                return;
            }

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                int generalReturnIndex = 0;
                int floatReturnIndex = 0;
                for (int i = 0; i < segments.Length; i++)
                {
                    AbiRegisterSegment segment = segments[i];
                    long bits = ReadRawBits(abs + segment.Offset, segment.Size);
                    MachineRegister reg = ReturnRegister(segment.RegisterClass, ref generalReturnIndex, ref floatReturnIndex);
                    if (segment.RegisterClass == RegisterClass.Float)
                        SetFpr(reg, bits);
                    else
                        SetGpr(reg, bits);
                }
                return;
            }

            int retBuffer = checked((int)ReadHiddenReturnBufferAddress(_activeCallTargetMethod ?? CurrentFrameMethod()));
            CopyTypedObject(retBuffer, abs, type);
            SetReturnRef(retBuffer);
        }

        private void StoreAbiArgumentToAddress(RuntimeMethod rm, int logicalIndex, int abs, RuntimeType type)
        {
            var abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: false);
            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                long raw = ReadAbiScalarArgument(rm, logicalIndex);
                if (abi.RegisterClass == RegisterClass.Float)
                {
                    if (StorageSizeOf(type) == 4) WriteI32(abs, unchecked((int)(uint)raw));
                    else WriteI64(abs, raw);
                }
                else
                {
                    WriteSizedInteger(abs, type, raw);
                }
                return;
            }

            int source = checked((int)ReadAbiAggregateAddress(rm, logicalIndex));
            CopyTypedObject(abs, source, type);
        }

        private int EA(InstrDesc ins)
        {
            long baseValue = Aux.MemoryBase(ins.Aux) switch
            {
                MemoryBase.Register => GetGpr(ins.Rs1),
                MemoryBase.StackPointer => X(MachineRegisters.StackPointer),
                MemoryBase.FramePointer => X(MachineRegisters.FramePointer),
                MemoryBase.GlobalPointer => X(MachineRegisters.GlobalPointer),
                MemoryBase.ThreadPointer => X(MachineRegisters.ThreadPointer),
                _ => throw new InvalidOperationException("Invalid memory base."),
            };
            long index = ins.Rs2 == RegisterVmIsa.InvalidRegister ? 0 : GetGpr(ins.Rs2) << Aux.MemoryScaleLog2(ins.Aux);
            long abs64 = checked(baseValue + index + ins.Imm);
            if (abs64 < int.MinValue || abs64 > int.MaxValue)
                throw new InvalidOperationException("Effective address overflow.");
            return (int)abs64;
        }

        private int GetAddress(byte reg)
        {
            long v = GetGpr(reg);
            if (v == 0) throw new NullReferenceException();
            if (v < int.MinValue || v > int.MaxValue) throw new InvalidOperationException("Address outside VM address space.");
            return (int)v;
        }

        private void LoadFieldInt(InstrDesc ins, int size, bool signed)
        {
            int abs = GetInstanceFieldAddress(FieldById(ins.Imm), GetGpr(ins.Rs1), false);
            long v = size switch
            {
                1 => signed ? (sbyte)ReadU8(abs) : ReadU8(abs),
                2 => signed ? (short)ReadU16(abs) : ReadU16(abs),
                _ => throw new ArgumentOutOfRangeException(nameof(size)),
            };
            SetGpr(ins.Rd, v);
        }

        private void LoadStaticFieldInt(InstrDesc ins, int size, bool signed)
        {
            int abs = GetStaticFieldAddress(FieldById(ins.Imm), false);
            long v = size switch
            {
                1 => signed ? (sbyte)ReadU8(abs) : ReadU8(abs),
                2 => signed ? (short)ReadU16(abs) : ReadU16(abs),
                _ => throw new ArgumentOutOfRangeException(nameof(size)),
            };
            SetGpr(ins.Rd, v);
        }

        private void StoreField(InstrDesc ins)
        {
            var field = FieldById(ins.Imm);
            int abs = GetInstanceFieldAddress(field, GetGpr(ins.Rs1), true);
            StoreRegisterToTypedAddress(ins.Op, ins.Rd, abs, field.FieldType);
        }

        private void StoreStaticField(InstrDesc ins)
        {
            var field = FieldById(ins.Imm);
            int abs = GetStaticFieldAddress(field, true);
            StoreRegisterToTypedAddress(ins.Op, ins.Rd, abs, field.FieldType);
        }

        private RuntimeType ElementTypeOf(InstrDesc ins)
            => ins.Imm > 0 ? _rts.GetTypeById(checked((int)ins.Imm)) : GetObjectTypeFromRef(GetGpr(ins.Rs1)).ElementType ?? throw new InvalidOperationException("Array has no element type.");

        private int ArrayEA(InstrDesc ins)
        {
            return GetArrayElementAddress(GetGpr(ins.Rs1), (int)GetGpr(ins.Rs2), ElementTypeOf(ins));
        }

        private void LoadElemInt(InstrDesc ins, int size, bool signed)
        {
            int abs = ArrayEA(ins);
            long v = size switch
            {
                1 => signed ? (sbyte)ReadU8(abs) : ReadU8(abs),
                2 => signed ? (short)ReadU16(abs) : ReadU16(abs),
                _ => throw new ArgumentOutOfRangeException(nameof(size)),
            };
            SetGpr(ins.Rd, v);
        }

        private void StoreElement(InstrDesc ins)
        {
            RuntimeType elem = ElementTypeOf(ins);
            int abs = ArrayEA(ins);
            StoreRegisterToTypedAddress(ins.Op, ins.Rd, abs, elem);
        }

        private void StoreRegisterToTypedAddress(Op op, byte reg, int abs, RuntimeType type)
        {
            int size = StorageSizeOf(type);
            if (op is Op.StFldF32 or Op.StSFldF32 or Op.StElemF32 or Op.StF32)
            {
                WriteI32(abs, unchecked((int)(uint)GetFprBits(reg)));
                return;
            }
            if (op is Op.StFldF64 or Op.StSFldF64 or Op.StElemF64 or Op.StF64)
            {
                WriteI64(abs, GetFprBits(reg));
                return;
            }
            if (op is Op.StFldObj or Op.StSFldObj or Op.StElemObj or Op.StObj)
            {
                CopyTypedObject(abs, GetAddress(reg), type);
                return;
            }
            long v = GetGpr(reg);
            switch (size)
            {
                case 1: WriteU8(abs, unchecked((byte)v)); return;
                case 2: WriteU16(abs, unchecked((ushort)v)); return;
                case 4: WriteI32(abs, unchecked((int)v)); return;
                case 8: WriteI64(abs, v); return;
                default:
                    CopyBlock(abs, checked((int)v), size);
                    return;
            }
        }

        private void InitDefaultValue(InstrDesc ins)
        {
            var t = _rts.GetTypeById(checked((int)ins.Imm));
            if (t.IsReferenceType || t.Kind == RuntimeTypeKind.Pointer || t.Kind == RuntimeTypeKind.ByRef)
            {
                SetGpr(ins.Rd, 0);
                return;
            }
            if (MachineRegisters.GetClass((MachineRegister)ins.Rd) == RegisterClass.Float)
            {
                SetFpr(ins.Rd, 0);
                return;
            }
            SetGpr(ins.Rd, 0);
        }

        private int GetInstanceFieldAddress(RuntimeField field, long receiver, bool writable)
        {
            if (field.IsStatic)
                throw new InvalidOperationException("Instance field access used for static field.");

            if (receiver == 0)
                throw new NullReferenceException();
            if (receiver < int.MinValue || receiver > int.MaxValue)
                throw new AccessViolationException("Field receiver is outside VM address space.");

            int receiverAbs = (int)receiver;

            if (TryGetObjectTypeFromExactRef(receiverAbs, out RuntimeType actual))
            {
                field = _rts.BindFieldToReceiver(field, actual);
                if (field.DeclaringType.IsValueType)
                {
                    if (actual.TypeId != field.DeclaringType.TypeId)
                        throw new InvalidOperationException("Boxed value field receiver type mismatch.");
                    int boxed = checked(receiverAbs + ObjectHeaderSize + field.Offset);
                    CheckHeapAccess(boxed, StorageSizeOf(field.FieldType), writable);
                    return boxed;
                }

                if (!IsAssignableTo(actual, field.DeclaringType))
                    throw new InvalidOperationException("Field receiver is not assignable to declaring type.");

                int abs = checked(receiverAbs + field.Offset);
                CheckHeapAccess(abs, StorageSizeOf(field.FieldType), writable);
                return abs;
            }

            if (!field.DeclaringType.IsValueType)
                throw new InvalidOperationException("Address receiver is only valid for value-type field access.");

            int byrefAbs = checked(receiverAbs + field.Offset);
            CheckIndirectAccess(byrefAbs, StorageSizeOf(field.FieldType), writable);
            return byrefAbs;
        }

        private int GetStaticFieldAddress(RuntimeField field, bool writable)
        {
            if (!field.IsStatic)
                throw new InvalidOperationException("Static field access used for instance field.");
            int baseAbs = EnsureStaticStorage(field.DeclaringType);
            int abs = checked(baseAbs + field.Offset);
            CheckHeapAccess(abs, StorageSizeOf(field.FieldType), writable);
            return abs;
        }

        private int EnsureStaticStorage(RuntimeType type)
        {
            if (_staticBaseByTypeId.TryGetValue(type.TypeId, out int abs))
                return abs;
            _ = _rts.GetStorageSizeAlign(type);

            if (type.StaticSize <= 0)
            {
                if (type.StaticFields.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"Static layout was not computed for '{type.Namespace}.{type.Name}'. " +
                        $"StaticFields={type.StaticFields.Length}, StaticSize={type.StaticSize}.");
                }

                _staticBaseByTypeId[type.TypeId] = 0;
                return 0;
            }
            abs = AllocRawHeapBytes(type.StaticSize, Math.Max(8, type.StaticAlign));
            Array.Clear(_mem, abs, type.StaticSize);
            _staticBaseByTypeId[type.TypeId] = abs;
            if (abs + type.StaticSize > _heapFloor)
                _heapFloor = abs + type.StaticSize;
            return abs;
        }

        private int AllocObject(RuntimeType type)
        {
            int size = Math.Max(ObjectHeaderSize, type.IsValueType ? ObjectHeaderSize + StorageSizeOf(type) : type.InstanceSize);
            int obj = AllocHeapBytes(size, Math.Max(8, type.AlignOf));
            WriteI32(obj, type.TypeId);
            WriteI32(obj + 4, GcFlagAllocated);
            if (size > ObjectHeaderSize)
                _mem.AsSpan(obj + ObjectHeaderSize, size - ObjectHeaderSize).Clear();
            return obj;
        }

        private int AllocBoxedValueObject(RuntimeType type)
        {
            int payload = StorageSizeOf(type);
            int size = checked(ObjectHeaderSize + payload);
            int obj = AllocHeapBytes(size, Math.Max(8, type.AlignOf));
            WriteI32(obj, type.TypeId);
            WriteI32(obj + 4, GcFlagAllocated);
            _mem.AsSpan(obj + ObjectHeaderSize, payload).Clear();
            return obj;
        }

        private int BoxValue(RuntimeType type, byte sourceReg)
        {
            int obj = AllocBoxedValueObject(type);
            int payload = obj + ObjectHeaderSize;
            int size = StorageSizeOf(type);

            if (RegisterVmIsa.IsFloatRegister(sourceReg))
            {
                if (size == 4) WriteI32(payload, unchecked((int)(uint)GetFprBits(sourceReg)));
                else WriteI64(payload, GetFprBits(sourceReg));
                return obj;
            }

            long raw = GetGpr(sourceReg);
            if (size <= 8 && !type.ContainsGcPointers)
            {
                WriteSizedInteger(payload, type, raw);
            }
            else
            {
                CopyTypedObject(payload, checked((int)raw), type);
            }
            return obj;
        }

        private int UnboxAddress(long objRef, RuntimeType type)
        {
            if (objRef == 0) throw new NullReferenceException();
            var actual = GetObjectTypeFromRef(objRef);
            if (actual.TypeId != type.TypeId)
                throw new InvalidCastException();
            return checked((int)objRef + ObjectHeaderSize);
        }

        private void UnboxAny(InstrDesc ins)
        {
            var type = _rts.GetTypeById(checked((int)ins.Imm));
            int addr = UnboxAddress(GetGpr(ins.Rs1), type);
            LoadTypedAddressToRegister(ins.Rd, addr, type);
        }

        private void LoadTypedAddressToRegister(byte rd, int abs, RuntimeType type)
        {
            if (MachineRegisters.GetClass((MachineRegister)rd) == RegisterClass.Float)
            {
                SetFpr(rd, StorageSizeOf(type) == 4 ? (uint)ReadI32(abs) : ReadI64(abs));
                return;
            }
            SetGpr(rd, ReadSizedInteger(abs, type));
        }

        private void InitTypedObject(int abs, RuntimeType type)
        {
            int size = StorageSizeOf(type);
            CheckIndirectAccess(abs, size, true);
            _mem.AsSpan(abs, size).Clear();
        }

        private void CopyTypedObject(int dst, int src, RuntimeType type)
        {
            int size = StorageSizeOf(type);
            CheckIndirectAccess(src, size, false);
            CheckIndirectAccess(dst, size, true);
            _mem.AsSpan(src, size).CopyTo(_mem.AsSpan(dst, size));
        }

        private void CopyBlock(int dst, int src, int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            CheckIndirectAccess(src, size, false);
            CheckIndirectAccess(dst, size, true);
            _mem.AsSpan(src, size).CopyTo(_mem.AsSpan(dst, size));
        }

        private void InitBlock(int dst, byte value, int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            CheckIndirectAccess(dst, size, true);
            _mem.AsSpan(dst, size).Fill(value);
        }

        private int AllocArray(RuntimeType typeOrElementType, int length)
        {
            if (length < 0) throw new OverflowException();
            RuntimeType arrayType = typeOrElementType.Kind == RuntimeTypeKind.Array ? typeOrElementType : _rts.GetArrayType(typeOrElementType);
            RuntimeType elemType = arrayType.ElementType ?? throw new InvalidOperationException("Array type has no element type.");
            int elemSize = StorageSizeOf(elemType);
            int dataBytes = checked(length * elemSize);
            int size = AlignUp(ArrayDataOffset + dataBytes, 8);
            int obj = AllocHeapBytes(size, 8);
            WriteI32(obj, arrayType.TypeId);
            WriteI32(obj + 4, GcFlagAllocated);
            WriteI32(obj + ArrayLengthOffset, length);
            WriteI32(obj + ArrayLengthOffset + 4, 0);
            if (dataBytes != 0) _mem.AsSpan(obj + ArrayDataOffset, dataBytes).Clear();
            return obj;
        }

        private int GetArrayElementAddress(long arrRef, int index, RuntimeType elemType)
        {
            ValidateArrayRef(arrRef, out int arrAbs, out RuntimeType arrType);
            int len = ReadI32(arrAbs + ArrayLengthOffset);
            if ((uint)index >= (uint)len) throw new IndexOutOfRangeException();
            RuntimeType actualElem = arrType.ElementType ?? elemType;
            int elemSize = StorageSizeOf(actualElem);
            int abs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));
            CheckHeapAccess(abs, elemSize, true);
            return abs;
        }

        private int AllocStringUninitialized(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            RuntimeType stringType = ResolveRequiredType("std", "System", "String");
            int byteLen = checked(length * 2);
            int size = AlignUp(StringCharsOffset + byteLen, 2);
            int obj = AllocHeapBytes(size, 8);
            WriteI32(obj, stringType.TypeId);
            WriteI32(obj + 4, GcFlagAllocated);
            WriteI32(obj + StringLengthOffset, length);
            if (byteLen != 0) _mem.AsSpan(obj + StringCharsOffset, byteLen).Clear();
            return obj;
        }

        private int AllocStringFromManaged(string value)
        {
            int obj = AllocStringUninitialized(value.Length);
            int chars = obj + StringCharsOffset;
            for (int i = 0; i < value.Length; i++)
                WriteU16(chars + i * 2, value[i]);
            return obj;
        }

        private int InternString(string value)
        {
            if (_internPool.TryGetValue(value, out int obj))
                return obj;
            obj = AllocStringFromManaged(value);
            _internPool[value] = obj;
            return obj;
        }

        private int CastClass(long objRef, RuntimeType target)
        {
            if (objRef == 0) return 0;
            RuntimeType actual = GetObjectTypeFromRef(objRef);
            if (!IsAssignableTo(actual, target)) throw new InvalidCastException();
            return checked((int)objRef);
        }

        private int IsInst(long objRef, RuntimeType target)
        {
            if (objRef == 0) return 0;
            RuntimeType actual = GetObjectTypeFromRef(objRef);
            return IsAssignableTo(actual, target) ? checked((int)objRef) : 0;
        }

        private void BoundsCheck(int index, int length)
        {
            if ((uint)index >= (uint)length) throw new IndexOutOfRangeException();
        }

        private int StackAlloc(int count, int elementSize)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (elementSize <= 0) throw new ArgumentOutOfRangeException(nameof(elementSize));

            return StackAllocBytes(checked(count * elementSize), elementSize);
        }

        private int StackAllocBytes(int byteCount, int alignment)
        {
            if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
            alignment = NormalizeStackAlignment(alignment);

            long top = CurrentFrameStackLow();
            long aligned = AlignDown(checked(top - byteCount), alignment);
            if (aligned < _stackBase || aligned < _frameStackTop)
                throw new StackOverflowException();

            SetTopAllocaSp(aligned);
            TrackStackPeak(aligned);
            if (byteCount != 0)
                _mem.AsSpan(checked((int)aligned), byteCount).Clear();
            return checked((int)aligned);
        }

        private static int NormalizeStackAlignment(int alignment)
        {
            if (alignment <= 0)
                return TargetArchitecture.PointerSize;

            alignment = Math.Min(16, Math.Max(TargetArchitecture.PointerSize, alignment));
            return (alignment & (alignment - 1)) == 0 ? alignment : TargetArchitecture.PointerSize;
        }

        private int AllocHeapBytes(int size, int align)
        {
            return AllocBlock(size, align, BlockMetaObject, out int obj) + BlockHeaderSize;
        }

        private int AllocRawHeapBytes(int size, int align)
        {
            return AllocBlock(size, align, BlockMetaRaw, out int payload) + BlockHeaderSize;
        }

        private int AllocBlock(int payloadSize, int align, int meta, out int payload)
        {
            if (payloadSize < 0) throw new ArgumentOutOfRangeException(nameof(payloadSize));
            if (payloadSize == 0) payloadSize = 1;
            align = NormalizeHeapPayloadAlignment(align);

            int blockSize = AlignUp(checked(BlockHeaderSize + payloadSize), HeapBlockAlignment);
            int minTailFree = Math.Max(GcAllocationBudgetBytes, blockSize + HeapBlockAlignment);

            if (!_gcRunning && _heapPtr > _heapBase &&
                (_allocDebtBytes >= GcAllocationBudgetBytes || _heapEnd - _heapPtr < minTailFree))
            {
                CollectGarbage(compact: false);
            }

            if (TryAllocFromFreeBlock(blockSize, align, meta, out int reusedBlock, out payload))
                return reusedBlock;

            int block = AlignPayloadStart(_heapPtr, align) - BlockHeaderSize;
            if (block != _heapPtr)
                throw new InvalidOperationException("Heap block over-alignment requires padding block support.");

            int end = checked(block + blockSize);

            if (end > _heapEnd)
            {
                CollectGarbage(compact: false);
                if (TryAllocFromFreeBlock(blockSize, align, meta, out reusedBlock, out payload))
                    return reusedBlock;

                CollectGarbage(compact: true);
                if (TryAllocFromFreeBlock(blockSize, align, meta, out reusedBlock, out payload))
                    return reusedBlock;

                block = AlignPayloadStart(_heapPtr, align) - BlockHeaderSize;
                if (block != _heapPtr)
                    throw new InvalidOperationException("Heap block over-alignment requires padding block support.");

                end = checked(block + blockSize);
                if (end > _heapEnd)
                    throw new OutOfMemoryException();
            }

            WriteI32(block + BlockSizeOffset, blockSize);
            WriteI32(block + BlockMetaOffset, meta);
            payload = checked(block + BlockHeaderSize);
            _heapPtr = end;
            if (_heapPtr > _heapPeakAbs) _heapPeakAbs = _heapPtr;
            _allocDebtBytes = Math.Min(int.MaxValue - blockSize, _allocDebtBytes) + blockSize;
            return block;
        }

        private bool TryAllocFromFreeBlock(int blockSize, int align, int meta, out int block, out int payload)
        {
            block = 0;
            payload = 0;
            if (_heapPtr <= _heapBase)
                return false;

            int scan = _heapBase;
            while (scan < _heapPtr)
            {
                int size = ReadBlockSize(scan);
                if (ReadI32(scan + BlockMetaOffset) == 0 && size >= blockSize)
                {
                    int alignedPayload = AlignPayloadStart(scan, align);
                    if (alignedPayload == scan + BlockHeaderSize)
                    {
                        int remainder = size - blockSize;
                        if (remainder >= BlockHeaderSize + HeapBlockAlignment)
                        {
                            WriteI32(scan + BlockSizeOffset, blockSize);
                            WriteI32(scan + BlockMetaOffset, meta);
                            int next = checked(scan + blockSize);
                            WriteI32(next + BlockSizeOffset, remainder);
                            WriteI32(next + BlockMetaOffset, 0);
                        }
                        else
                        {
                            blockSize = size;
                            WriteI32(scan + BlockMetaOffset, meta);
                        }

                        block = scan;
                        payload = checked(scan + BlockHeaderSize);
                        _allocDebtBytes = Math.Min(int.MaxValue - blockSize, _allocDebtBytes) + blockSize;
                        return true;
                    }
                }
                scan += size;
            }
            return false;
        }
        private static int NormalizeHeapPayloadAlignment(int align)
        {
            if (align <= 1)
                return 1;
            if ((align & (align - 1)) != 0)
                throw new ArgumentException("Alignment must be power of two.", nameof(align));
            return Math.Min(align, HeapBlockAlignment);
        }
        private static int GcAllocationBudgetBytes = 256;

        private void MaybeCollectGarbage()
        {
            if (_gcRunning)
                return;

            int sinceLastCollection = _heapPtr - _heapFloor;
            int usefulThreshold = 128;
            if (_allocDebtBytes < GcAllocationBudgetBytes && sinceLastCollection < usefulThreshold && _heapEnd - _heapPtr >= usefulThreshold)
                return;

            CollectGarbage(compact: false);
        }

        private void CollectGarbage(bool compact = true)
        {
            if (_gcRunning)
                return;

            _gcRunning = true;
            try
            {
                if (_heapPtr <= _heapBase)
                {
                    _heapPtr = _heapBase;
                    _heapFloor = _heapBase;
                    _allocDebtBytes = 0;
                    return;
                }

                MarkReachableObjects();

                if (!compact)
                {
                    SweepHeapWithoutCompaction();
                    return;
                }

                int dst = _heapBase;
                int src = _heapBase;
                while (src < _heapPtr)
                {
                    int size = ReadBlockSize(src);
                    int meta = ReadI32(src + BlockMetaOffset);
                    if (meta == BlockMetaRaw)
                    {
                        int targetBlock = AlignBlockStart(dst);
                        WriteI32(src + BlockMetaOffset, -targetBlock);
                        dst = checked(targetBlock + size);
                    }
                    else if (meta == BlockMetaObject)
                    {
                        int obj = src + BlockHeaderSize;
                        int flags = ReadI32(obj + 4);
                        if ((flags & GcFlagAllocated) != 0 && (flags & GcFlagMark) != 0)
                        {
                            int targetBlock = AlignBlockStart(dst);
                            WriteI32(src + BlockMetaOffset, targetBlock + BlockHeaderSize);
                            dst = checked(targetBlock + size);
                        }
                        else
                        {
                            WriteI32(src + BlockMetaOffset, 0);
                        }
                    }
                    else
                    {
                        WriteI32(src + BlockMetaOffset, 0);
                    }
                    src += size;
                }

                UpdateRootsAfterCompaction();

                src = _heapBase;
                dst = _heapBase;
                while (src < _heapPtr)
                {
                    int size = ReadBlockSize(src);
                    int forward = ReadI32(src + BlockMetaOffset);
                    if (forward != 0)
                    {
                        bool isObject = forward > 0;
                        int targetBlock = isObject ? forward - BlockHeaderSize : -forward;
                        if (isObject)
                            UpdateObjectReferencesBeforeMove(src + BlockHeaderSize);
                        if (targetBlock != src)
                            Buffer.BlockCopy(_mem, src, _mem, targetBlock, size);
                        WriteI32(targetBlock + BlockSizeOffset, size);
                        WriteI32(targetBlock + BlockMetaOffset, isObject ? BlockMetaObject : BlockMetaRaw);
                        if (isObject)
                        {
                            int targetObj = targetBlock + BlockHeaderSize;
                            int flags = ReadI32(targetObj + 4);
                            WriteI32(targetObj + 4, flags & ~GcFlagMark);
                        }
                        dst = checked(targetBlock + size);
                    }
                    src += size;
                }

                if (dst < _heapPtr)
                    Array.Clear(_mem, dst, _heapPtr - dst);

                _heapPtr = dst;
                _heapFloor = _heapPtr;
                _allocDebtBytes = 0;
            }
            finally
            {
                _gcRunning = false;
            }
        }

        private void MarkReachableObjects()
        {
            _gcMarkHead = 0;
            MarkRoots();

            while (_gcMarkHead != 0)
            {
                int obj = PopGcMarkObject();
                RuntimeType t = _rts.GetTypeById(ReadI32(obj));
                MarkObjectReferences(obj, t);
            }
        }

        private void SweepHeapWithoutCompaction()
        {
            int block = _heapBase;
            while (block < _heapPtr)
            {
                int size = ReadBlockSize(block);
                int meta = ReadI32(block + BlockMetaOffset);
                if (meta == BlockMetaObject)
                {
                    int obj = block + BlockHeaderSize;
                    int flags = ReadI32(obj + 4);
                    if ((flags & GcFlagAllocated) != 0 && (flags & GcFlagMark) != 0)
                    {
                        WriteI32(obj + 4, flags & ~GcFlagMark);
                    }
                    else
                    {
                        Array.Clear(_mem, block + BlockHeaderSize, size - BlockHeaderSize);
                        WriteI32(block + BlockMetaOffset, 0);
                    }
                }
                block += size;
            }

            CoalesceFreeHeapBlocksAndTrimTail();
            _heapFloor = _heapPtr;
            _allocDebtBytes = 0;
        }

        private void CoalesceFreeHeapBlocksAndTrimTail()
        {
            int block = _heapBase;
            int lastNonFreeEnd = _heapBase;

            while (block < _heapPtr)
            {
                int size = ReadBlockSize(block);
                int meta = ReadI32(block + BlockMetaOffset);
                if (meta == 0)
                {
                    int total = size;
                    int next = checked(block + size);
                    while (next < _heapPtr && ReadI32(next + BlockMetaOffset) == 0)
                    {
                        int nextSize = ReadBlockSize(next);
                        total = checked(total + nextSize);
                        next = checked(next + nextSize);
                    }

                    if (total != size)
                    {
                        WriteI32(block + BlockSizeOffset, total);
                        WriteI32(block + BlockMetaOffset, 0);
                    }

                    block = next;
                    continue;
                }

                lastNonFreeEnd = checked(block + size);
                block = lastNonFreeEnd;
            }

            if (lastNonFreeEnd < _heapPtr)
            {
                Array.Clear(_mem, lastNonFreeEnd, _heapPtr - lastNonFreeEnd);
                _heapPtr = lastNonFreeEnd;
            }
        }


        private void MarkRoots()
        {
            MarkFrameRoots(update: false);

            foreach (var kv in _staticBaseByTypeId)
            {
                if (kv.Value == 0) continue;
                RuntimeType t = _rts.GetTypeById(kv.Key);
                for (int i = 0; i < t.StaticFields.Length; i++)
                {
                    RuntimeField f = t.StaticFields[i];
                    MarkManagedRefCellsInTypedStorage(kv.Value + f.Offset, f.FieldType);
                }
            }

            foreach (var kv in _internPool)
                TryMarkObject(kv.Value);

            if (_currentExceptionRef != 0)
                TryMarkObject(_currentExceptionRef);

            MarkPendingPostReturnObjects();
        }

        private void UpdateRootsAfterCompaction()
        {
            MarkFrameRoots(update: true);

            foreach (var kv in _staticBaseByTypeId)
            {
                if (kv.Value == 0) continue;
                RuntimeType t = _rts.GetTypeById(kv.Key);
                for (int i = 0; i < t.StaticFields.Length; i++)
                {
                    RuntimeField f = t.StaticFields[i];
                    UpdateManagedRefCellsInTypedStorage(kv.Value + f.Offset, f.FieldType);
                }
            }

            if (_currentExceptionRef != 0)
                _currentExceptionRef = TranslateObjectRef(_currentExceptionRef);

            if (_staticBaseByTypeId.Count != 0)
            {
                foreach (var kv in _staticBaseByTypeId)
                {
                    if (kv.Value != 0)
                        _staticBaseByTypeId[kv.Key] = TranslateRawBase(kv.Value);
                }
            }

            if (_internPool.Count != 0)
            {
                foreach (var kv in _internPool)
                    _internPool[kv.Key] = TranslateObjectRef(kv.Value);
            }

            UpdatePendingPostReturnObjectsAfterCompaction();
        }

        private void MarkFrameRoots(bool update)
        {
            if (_frameCount == 0)
                return;

            int top = TopFrameOffset();
            for (int frame = _stackBase; frame <= top; frame += ShadowFrameSize)
            {
                int methodIndex = ReadI32(frame + ShadowFrameMethodIndex);
                int safePointPc = frame == top ? (_currentSafePointPc >= 0 ? _currentSafePointPc : _pc) : FrameSafePointPc(frame);
                if (methodIndex < 0 || safePointPc < 0)
                    continue;
                if (!TryGetSafePoint(methodIndex, safePointPc, out GcSafePointRecord sp))
                    continue;

                for (int r = 0; r < sp.RootCount; r++)
                {
                    GcRootRecord root = _image.GcRoots[sp.RootStartIndex + r];
                    if (update) UpdateRoot(frame, root);
                    else MarkRoot(frame, root);
                }
            }
        }

        private bool TryGetSafePoint(int methodIndex, int pc, out GcSafePointRecord safePoint)
        {
            safePoint = default;
            if ((uint)methodIndex >= (uint)_image.Methods.Length)
                return false;

            MethodRecord method = _image.Methods[methodIndex];
            int start = method.GcSafePointStartIndex;
            int end = start + method.GcSafePointCount;
            for (int i = start; i < end; i++)
            {
                GcSafePointRecord sp = _image.GcSafePoints[i];
                if (sp.Pc == pc)
                {
                    safePoint = sp;
                    return true;
                }
            }
            return false;
        }

        private void UpdateObjectReferencesBeforeMove(int obj)
        {
            RuntimeType t = _rts.GetTypeById(ReadI32(obj));
            if (t.Kind == RuntimeTypeKind.Array)
            {
                RuntimeType elem = t.ElementType ?? throw new InvalidOperationException("Array has no element type.");
                if (!TypeIsReferenceOrContainsReferences(elem)) return;
                int len = ReadI32(obj + ArrayLengthOffset);
                int elemSize = StorageSizeOf(elem);
                int baseAbs = obj + ArrayDataOffset;
                for (int i = 0; i < len; i++)
                    UpdateManagedRefCellsInTypedStorage(baseAbs + i * elemSize, elem);
                return;
            }

            if (t.IsValueType)
            {
                UpdateManagedRefCellsInTypedStorage(obj + ObjectHeaderSize, t);
                return;
            }

            for (RuntimeType? cur = t; cur != null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                {
                    RuntimeField f = cur.InstanceFields[i];
                    if (!f.IsStatic)
                        UpdateManagedRefCellsInTypedStorage(obj + f.Offset, f.FieldType);
                }
            }
        }

        private void MarkRoot(int frameOffset, GcRootRecord root)
        {
            GcRootKind kind = (GcRootKind)root.Kind;
            if (kind == GcRootKind.RegisterRef)
            {
                long value = ReadFrameGpr(frameOffset, root.Register);
                if (value != 0) MarkRootValue(root, value);
                return;
            }

            if (kind == GcRootKind.RegisterByRef || kind == GcRootKind.InteriorRegister)
            {
                long value = ReadFrameGpr(frameOffset, root.Register);
                if (value != 0) TryMarkObjectFromInteriorPointer(checked((int)value));
                return;
            }

            if (kind == GcRootKind.FrameRef || kind == GcRootKind.FrameByRef || kind == GcRootKind.InteriorFrame)
            {
                int cell = FrameRootCellAddress(frameOffset, root);
                long value = ReadNative(cell);
                if (value == 0)
                    return;

                if (kind == GcRootKind.FrameRef)
                    MarkRootValue(root, value);
                else
                    TryMarkObjectFromInteriorPointer(checked((int)value));
            }
        }

        private void UpdateRoot(int frameOffset, GcRootRecord root)
        {
            GcRootKind kind = (GcRootKind)root.Kind;
            if (kind == GcRootKind.RegisterRef)
            {
                long value = ReadFrameGpr(frameOffset, root.Register);
                if (value != 0) WriteFrameGpr(frameOffset, root.Register, TranslateRootValue(root, value));
                return;
            }

            if (kind == GcRootKind.RegisterByRef || kind == GcRootKind.InteriorRegister)
            {
                long value = ReadFrameGpr(frameOffset, root.Register);
                if (value != 0)
                    WriteFrameGpr(frameOffset, root.Register, TranslateInteriorPointer(checked((int)value)));
                return;
            }

            if (kind == GcRootKind.FrameRef || kind == GcRootKind.FrameByRef || kind == GcRootKind.InteriorFrame)
            {
                int cell = FrameRootCellAddress(frameOffset, root);
                long value = ReadNative(cell);
                if (value == 0)
                    return;

                long translated = kind == GcRootKind.FrameRef
                    ? TranslateRootValue(root, value)
                    : TranslateInteriorPointer(checked((int)value));
                WriteNative(cell, translated);
            }
        }

        private void MarkRootValue(GcRootRecord root, long value)
        {
            if (RootCellIsInteriorPointer(root))
                TryMarkObjectFromInteriorPointer(checked((int)value));
            else
                TryMarkObject(checked((int)value));
        }

        private long TranslateRootValue(GcRootRecord root, long value)
        {
            return RootCellIsInteriorPointer(root)
                ? TranslateInteriorPointer(checked((int)value))
                : TranslateObjectRef(checked((int)value));
        }

        private bool RootCellIsInteriorPointer(GcRootRecord root)
        {
            if (TryResolveGcCellType(root, out RuntimeType cellType))
                return cellType.Kind == RuntimeTypeKind.ByRef;
            return false;
        }

        private bool TryResolveGcCellType(GcRootRecord root, out RuntimeType cellType)
        {
            cellType = null!;
            if (root.RuntimeTypeId < 0)
                return false;

            RuntimeType type = _rts.GetTypeById(root.RuntimeTypeId);
            return TryResolveGcCellTypeAtOffset(type, root.CellOffset, out cellType, depth: 0);
        }

        private bool TryResolveGcCellTypeAtOffset(RuntimeType type, int offset, out RuntimeType cellType, int depth)
        {
            cellType = null!;
            if (depth > 64 || offset < 0)
                return false;

            if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam || type.Kind == RuntimeTypeKind.ByRef)
            {
                if (offset == 0)
                {
                    cellType = type;
                    return true;
                }
                return false;
            }

            if (type.Kind == RuntimeTypeKind.Pointer || !type.ContainsGcPointers)
                return false;

            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                RuntimeField f = type.InstanceFields[i];
                if (f.IsStatic)
                    continue;

                int fieldSize = StorageSizeOf(f.FieldType);
                int rel = offset - f.Offset;
                if ((uint)rel >= (uint)fieldSize)
                    continue;

                return TryResolveGcCellTypeAtOffset(f.FieldType, rel, out cellType, depth + 1);
            }

            return false;
        }
        private void MarkPendingPostReturnObjects()
        {
            for (int frame = _stackBase; frame < _frameStackTop; frame += ShadowFrameSize)
            {
                long obj = ReadI64(frame + ShadowFramePostReturnObjectRef);
                if (obj != 0) TryMarkObject(checked((int)obj));
            }
        }
        private void UpdatePendingPostReturnObjectsAfterCompaction()
        {
            for (int frame = _stackBase; frame < _frameStackTop; frame += ShadowFrameSize)
            {
                long obj = ReadI64(frame + ShadowFramePostReturnObjectRef);
                if (obj != 0) WriteI64(frame + ShadowFramePostReturnObjectRef, TranslateObjectRef(checked((int)obj)));
            }
        }
        private int CurrentFrameRootAddress(GcRootRecord root)
            => checked((int)CurrentFrameBase((RegisterFrameBase)root.FrameBase) + root.FrameOffset);
        private long CurrentFrameBase(RegisterFrameBase frameBase)
            => frameBase switch
            {
                RegisterFrameBase.StackPointer => X(MachineRegisters.StackPointer),
                RegisterFrameBase.FramePointer => X(MachineRegisters.FramePointer),
                RegisterFrameBase.IncomingArgumentBase => X(MachineRegisters.ThreadPointer),
                RegisterFrameBase.None => X(MachineRegisters.FramePointer),
                _ => throw new InvalidOperationException("Invalid GC root frame base."),
            };
        private int FrameRootAddress(int frameOffset, GcRootRecord root)
            => checked((int)FrameBase(frameOffset, (RegisterFrameBase)root.FrameBase) + root.FrameOffset);
        private int FrameRootCellAddress(int frameOffset, GcRootRecord root)
            => checked(FrameRootAddress(frameOffset, root) + root.CellOffset);
        private long FrameBase(int frameOffset, RegisterFrameBase frameBase)
            => frameBase switch
            {
                RegisterFrameBase.StackPointer => FrameStackPointer(frameOffset),
                RegisterFrameBase.FramePointer => FramePointer(frameOffset),
                RegisterFrameBase.IncomingArgumentBase => FrameIncomingArgumentBase(frameOffset),
                RegisterFrameBase.None => FramePointer(frameOffset),
                _ => throw new InvalidOperationException("Invalid GC root frame base."),
            };

        private long ReadFrameGpr(int frameOffset, byte encodedRegister)
        {
            if (encodedRegister == (byte)MachineRegister.X0)
                return 0;

            GprStorageLocation location = ResolveFrameGprLocation(frameOffset, encodedRegister);
            if (location.Kind == GprStorageLocation.StackSlot)
                return ReadNative(location.Address);
            if (location.Kind == GprStorageLocation.Snapshot)
            {
                if ((uint)location.SnapshotIndex >= (uint)_registerSnapshots.Count ||
                    _registerSnapshots[location.SnapshotIndex] is not RegisterSnapshot snapshot)
                    throw new InvalidOperationException("Invalid register snapshot index.");
                return snapshot.General[encodedRegister];
            }
            return GetGpr(encodedRegister);
        }

        private void WriteFrameGpr(int frameOffset, byte encodedRegister, long value)
        {
            if (encodedRegister == (byte)MachineRegister.X0)
                return;

            GprStorageLocation location = ResolveFrameGprLocation(frameOffset, encodedRegister);
            if (location.Kind == GprStorageLocation.StackSlot)
            {
                WriteNative(location.Address, value);
                return;
            }
            if (location.Kind == GprStorageLocation.Snapshot)
            {
                if ((uint)location.SnapshotIndex >= (uint)_registerSnapshots.Count ||
                    _registerSnapshots[location.SnapshotIndex] is not RegisterSnapshot snapshot)
                    throw new InvalidOperationException("Invalid register snapshot index.");
                snapshot.General[encodedRegister] = value;
                return;
            }
            SetGpr(encodedRegister, value);
        }

        private GprStorageLocation ResolveFrameGprLocation(int frameOffset, byte encodedRegister)
        {
            int top = TopFrameOffset();
            if (frameOffset == top)
                return new GprStorageLocation(GprStorageLocation.CurrentRegister);

            int directChild = checked(frameOffset + ShadowFrameSize);
            if (directChild <= top)
            {
                int snapshotIndex = ReadI32(directChild + ShadowFrameRegisterSnapshotIndex);
                if (snapshotIndex >= 0)
                    return new GprStorageLocation(GprStorageLocation.Snapshot, snapshotIndex: snapshotIndex);
            }

            MachineRegister register = (MachineRegister)encodedRegister;
            GprStorageLocation location = new GprStorageLocation(GprStorageLocation.CurrentRegister);
            for (int child = top; child > frameOffset; child -= ShadowFrameSize)
            {
                if (TryGetSavedRegisterSlot(child, register, out int slotAddress))
                    location = new GprStorageLocation(GprStorageLocation.StackSlot, address: slotAddress);
            }
            return location;
        }

        private bool TryGetSavedRegisterSlot(int frameOffset, MachineRegister register, out int slotAddress)
        {
            slotAddress = 0;
            int methodIndex = ReadI32(frameOffset + ShadowFrameMethodIndex);
            if ((uint)methodIndex >= (uint)_image.Methods.Length)
                return false;

            int safePointPc = frameOffset != TopFrameOffset()
                ? FrameSafePointPc(frameOffset)
                : _currentSafePointPc >= 0
                    ? _currentSafePointPc
                    : _pc;
            MethodRecord method = _image.Methods[methodIndex];
            int start = method.UnwindStartIndex;
            int end = start + method.UnwindCount;
            for (int i = start; i < end; i++)
            {
                UnwindRecord unwind = _image.Unwind[i];
                if ((UnwindCodeKind)unwind.Kind != UnwindCodeKind.SaveCalleeSavedRegister)
                    continue;
                if ((MachineRegister)unwind.Register != register)
                    continue;
                if (safePointPc >= 0 && unwind.Pc > safePointPc)
                    continue;
                slotAddress = checked((int)FrameStackPointer(frameOffset) + unwind.StackOffset);
                return true;
            }
            return false;
        }

        private void PushGcMarkObject(int obj)
        {
            int block = obj - BlockHeaderSize;
            WriteI32(block + BlockMetaOffset, -(_gcMarkHead + 1));
            _gcMarkHead = obj;
        }

        private int PopGcMarkObject()
        {
            int obj = _gcMarkHead;
            int block = obj - BlockHeaderSize;
            int encodedNext = ReadI32(block + BlockMetaOffset);
            _gcMarkHead = -encodedNext - 1;
            WriteI32(block + BlockMetaOffset, BlockMetaObject);
            return obj;
        }

        private void MarkObjectReferences(int obj, RuntimeType type)
        {
            if (type.Kind == RuntimeTypeKind.Array)
            {
                RuntimeType elem = type.ElementType ?? throw new InvalidOperationException("Array has no element type.");
                if (!TypeIsReferenceOrContainsReferences(elem)) return;

                int len = ReadI32(obj + ArrayLengthOffset);
                int elemSize = StorageSizeOf(elem);
                int data = obj + ArrayDataOffset;
                for (int i = 0; i < len; i++)
                    MarkManagedRefCellsInTypedStorage(data + i * elemSize, elem);
                return;
            }

            if (type.IsValueType)
            {
                MarkManagedRefCellsInTypedStorage(obj + ObjectHeaderSize, type);
                return;
            }

            for (RuntimeType? cur = type; cur != null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                {
                    RuntimeField f = cur.InstanceFields[i];
                    if (!f.IsStatic)
                        MarkManagedRefCellsInTypedStorage(obj + f.Offset, f.FieldType);
                }
            }
        }

        private void MarkManagedRefCellsInTypedStorage(int abs, RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
            {
                long v = ReadNative(abs);
                if (v != 0) TryMarkObject(checked((int)v));
                return;
            }
            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                long v = ReadNative(abs);
                if (v != 0) TryMarkObjectFromInteriorPointer(checked((int)v));
                return;
            }

            if (type.Kind == RuntimeTypeKind.Pointer || !type.ContainsGcPointers)
                return;

            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                RuntimeField f = type.InstanceFields[i];
                if (!f.IsStatic)
                    MarkManagedRefCellsInTypedStorage(abs + f.Offset, f.FieldType);
            }
        }

        private void MarkObjectFromCell(int obj) => TryMarkObject(obj);

        private void VisitObjectReferences(int obj, RuntimeType type, Action<int> visitor)
        {
            if (type.Kind == RuntimeTypeKind.Array)
            {
                RuntimeType elem = type.ElementType ?? throw new InvalidOperationException("Array has no element type.");
                if (!TypeIsReferenceOrContainsReferences(elem)) return;

                int len = ReadI32(obj + ArrayLengthOffset);
                int elemSize = StorageSizeOf(elem);
                int data = obj + ArrayDataOffset;
                for (int i = 0; i < len; i++)
                    VisitManagedRefCellsInTypedStorage(data + i * elemSize, elem, visitor);
                return;
            }

            if (type.IsValueType)
            {
                VisitManagedRefCellsInTypedStorage(obj + ObjectHeaderSize, type, visitor);
                return;
            }

            for (RuntimeType? cur = type; cur != null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                {
                    RuntimeField f = cur.InstanceFields[i];
                    if (!f.IsStatic)
                        VisitManagedRefCellsInTypedStorage(obj + f.Offset, f.FieldType, visitor);
                }
            }
        }

        private void VisitManagedRefCellsInTypedStorage(int abs, RuntimeType type, Action<int> visitor)
        {
            if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
            {
                long v = ReadNative(abs);
                if (v != 0) visitor(checked((int)v));
                return;
            }
            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                long v = ReadNative(abs);
                if (v != 0) TryMarkObjectFromInteriorPointer(checked((int)v));
                return;
            }

            if (type.Kind == RuntimeTypeKind.Pointer || !type.ContainsGcPointers)
                return;

            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                RuntimeField f = type.InstanceFields[i];
                if (!f.IsStatic)
                    VisitManagedRefCellsInTypedStorage(abs + f.Offset, f.FieldType, visitor);
            }
        }

        private bool TryMarkObject(int obj)
        {
            if (obj == 0) return false;
            if (!TryGetBlockFromObjectRef(obj, out int block)) return false;
            int flags = ReadI32(obj + 4);
            if ((flags & GcFlagAllocated) == 0 || (flags & GcFlagMark) != 0) return false;
            WriteI32(obj + 4, flags | GcFlagMark);
            PushGcMarkObject(obj);
            return true;
        }

        private bool TryMarkObjectFromInteriorPointer(int targetAbs)
        {
            if (targetAbs == 0)
                return false;
            if (TryGetBlockFromObjectRef(targetAbs, out _))
                return TryMarkObject(targetAbs);
            if (!TryGetObjectBlockContaining(targetAbs, out int block))
                return false;
            return TryMarkObject(block + BlockHeaderSize);
        }

        private int TranslateObjectRef(int oldObj)
        {
            if (oldObj == 0) return 0;
            if (!TryGetBlockFromObjectRef(oldObj, out int block))
                throw new AccessViolationException("Dangling object reference during GC compaction.");
            int target = ReadI32(block + BlockMetaOffset);
            if (target <= 0)
                throw new AccessViolationException("Unmarked object reference during GC compaction.");
            return target;
        }

        private int TranslateInteriorPointer(int oldPtr)
        {
            if (oldPtr == 0)
                return 0;

            if (TryTranslateObjectInteriorPointer(oldPtr, out int translated))
                return translated;

            if (TryTranslateRawInteriorPointer(oldPtr, out translated))
                return translated;

            if (oldPtr < _heapBase)
                return oldPtr;

            if (oldPtr < _heapEnd)
                throw new AccessViolationException("Dangling interior pointer during GC compaction.");

            return oldPtr;
        }
        private bool TryTranslateObjectInteriorPointer(int oldPtr, out int translated)
        {
            if (TryGetBlockFromObjectRef(oldPtr, out _))
            {
                translated = TranslateObjectRef(oldPtr);
                return true;
            }

            if (TryGetObjectBlockContaining(oldPtr, out int block))
            {
                int oldObj = block + BlockHeaderSize;
                int targetObj = TranslateObjectRef(oldObj);
                translated = checked(targetObj + (oldPtr - oldObj));
                return true;
            }

            translated = 0;
            return false;
        }

        private bool TryTranslateRawInteriorPointer(int oldPtr, out int translated)
        {
            if (!TryGetRawBlockContaining(oldPtr, out int block))
            {
                translated = 0;
                return false;
            }

            int meta = ReadI32(block + BlockMetaOffset);

            if (meta == BlockMetaRaw)
            {
                translated = oldPtr;
                return true;
            }

            if (meta >= 0)
                throw new AccessViolationException("Invalid raw block forwarding pointer during GC compaction.");

            int oldPayload = block + BlockHeaderSize;
            int newPayload = checked(-meta + BlockHeaderSize);
            translated = checked(newPayload + (oldPtr - oldPayload));
            return true;
        }
        private int TranslateRawBase(int oldBase)
        {
            int block = oldBase - BlockHeaderSize;
            if (!TryGetRawBlock(oldBase, out block))
                throw new AccessViolationException("Dangling static storage reference during GC compaction.");
            int target = ReadI32(block + BlockMetaOffset);
            if (target >= 0)
                throw new AccessViolationException("Invalid static storage forwarding pointer during GC compaction.");
            return checked(-target + BlockHeaderSize);
        }

        private void UpdateManagedRefCellsInTypedStorage(int abs, RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
            {
                long v = ReadNative(abs);
                if (v != 0) WriteNative(abs, TranslateObjectRef(checked((int)v)));
                return;
            }
            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                long v = ReadNative(abs);
                if (v != 0) WriteNative(abs, TranslateInteriorPointer(checked((int)v)));
                return;
            }

            if (type.Kind == RuntimeTypeKind.Pointer || !type.ContainsGcPointers)
                return;

            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                RuntimeField f = type.InstanceFields[i];
                if (!f.IsStatic)
                    UpdateManagedRefCellsInTypedStorage(abs + f.Offset, f.FieldType);
            }
        }

        private int ReadBlockSize(int block)
        {
            if (block < _heapBase || block + BlockHeaderSize > _heapPtr)
                throw new AccessViolationException("Bad heap block header.");
            int size = ReadI32(block + BlockSizeOffset);
            if (size < BlockHeaderSize + 8 || (size & 7) != 0 || block + size > _heapPtr)
                throw new AccessViolationException("Corrupted heap block size.");
            return size;
        }

        private bool TryGetBlockFromObjectRef(int obj, out int block)
        {
            block = obj - BlockHeaderSize;
            if (block < _heapBase || obj < _heapBase + BlockHeaderSize || obj + ObjectHeaderSize > _heapPtr)
                return false;

            try { ReadBlockSize(block); }
            catch { return false; }

            if (block + BlockHeaderSize != obj)
                return false;

            int meta = ReadI32(block + BlockMetaOffset);
            return meta == BlockMetaObject || IsGcMarkLink(meta) || IsForwardedObjectPayload(meta);
        }

        private bool TryGetObjectBlockContaining(int abs, out int block)
        {
            block = _heapBase;
            while (block < _heapPtr)
            {
                int size;
                try { size = ReadBlockSize(block); }
                catch { block = 0; return false; }
                int meta = ReadI32(block + BlockMetaOffset);
                if ((meta == BlockMetaObject || IsGcMarkLink(meta) || IsForwardedObjectPayload(meta))
                    && abs >= block + BlockHeaderSize && abs < block + size)
                    return true;
                block += size;
            }
            block = 0;
            return false;
        }

        private bool TryGetRawBlock(int payload, out int block)
        {
            block = payload - BlockHeaderSize;
            if (block < _heapBase || payload < _heapBase + BlockHeaderSize || payload > _heapPtr)
                return false;

            try { ReadBlockSize(block); }
            catch { return false; }

            if (block + BlockHeaderSize != payload)
                return false;

            int meta = ReadI32(block + BlockMetaOffset);
            return meta == BlockMetaRaw || IsForwardedRawBlock(meta);
        }
        private bool TryGetRawBlockContaining(int abs, out int block)
        {
            block = _heapBase;

            while (block < _heapPtr)
            {
                int size;
                try { size = ReadBlockSize(block); }
                catch
                {
                    block = 0;
                    return false;
                }

                int meta = ReadI32(block + BlockMetaOffset);
                if ((meta == BlockMetaRaw || IsForwardedRawBlock(meta)) && abs >= block + BlockHeaderSize && abs < block + size)
                    return true;

                block += size;
            }

            block = 0;
            return false;
        }
        private bool IsGcMarkLink(int meta)
        {
            if (!_gcRunning || meta >= 0 || meta == int.MinValue)
                return false;

            int next = -meta - 1;
            if (next == 0)
                return true;

            return next >= _heapBase + BlockHeaderSize
                && next <= _heapPtr
                && ((next - BlockHeaderSize) & (HeapBlockAlignment - 1)) == 0;
        }

        private bool IsForwardedObjectPayload(int meta)
        {
            if (meta <= 0)
                return false;

            int block = meta - BlockHeaderSize;
            return meta >= _heapBase + BlockHeaderSize
                && meta <= _heapEnd
                && block >= _heapBase
                && block <= _heapEnd
                && (block & (HeapBlockAlignment - 1)) == 0;
        }

        private bool IsForwardedRawBlock(int meta)
        {
            if (meta >= 0 || meta == int.MinValue)
                return false;

            int block = -meta;
            return block >= _heapBase
                && block <= _heapEnd
                && (block & (HeapBlockAlignment - 1)) == 0;
        }


        private int CheckedTarget(long target)
        {
            if (target < 0 || target >= _image.Code.Length)
                throw new InvalidOperationException("Invalid branch target PC: " + target.ToString());
            return (int)target;
        }

        private void Switch(long key, InstrDesc ins)
        {
            int start = checked((int)ins.Imm);
            int count = ins.Aux;
            if ((uint)start > (uint)_image.SwitchTable.Length || start + count > _image.SwitchTable.Length)
                throw new InvalidOperationException("Invalid switch table range.");

            for (int i = 0; i < count; i++)
            {
                var e = _image.SwitchTable[start + i];
                if (e.Key == key)
                {
                    _pc = CheckedTarget(e.TargetPc);
                    return;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long X(MachineRegister register)
        {
            byte r = RegisterVmIsa.EncodeRegister(register);
            return GetGpr(r);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void X(MachineRegister register, long value)
        {
            byte r = RegisterVmIsa.EncodeRegister(register);
            SetGpr(r, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetGpr(byte register)
            => register == (byte)MachineRegister.X0 ? 0 : _x[register];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetGpr(byte register, long value)
        {
            if (register == (byte)MachineRegister.X0)
                return;

            _x[register] = value;
            if (register == (byte)MachineRegister.X2)
                TrackStackPeak(value);
        }

        private void SetGpr(MachineRegister register, long value)
            => SetGpr(RegisterVmIsa.EncodeRegister(register), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetFprBits(byte register)
            => _f[register - (byte)MachineRegister.F0];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFpr(byte register, long bits)
            => _f[register - (byte)MachineRegister.F0] = bits;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFpr(MachineRegister register, long bits)
            => SetFpr(RegisterVmIsa.EncodeRegister(register), bits);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float F32(byte register)
            => BitConverter.Int32BitsToSingle(unchecked((int)(uint)GetFprBits(register)));

        private double F64(byte register)
            => BitConverter.Int64BitsToDouble(GetFprBits(register));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetF32(byte register, float value)
            => SetFpr(register, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetF64(byte register, double value)
            => SetFpr(register, BitConverter.DoubleToInt64Bits(value));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetI32(byte register, int value)
            => SetGpr(register, value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetBool(byte register, bool value)
            => SetGpr(register, value ? 1 : 0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetReturnI4(int value)
            => SetGpr(MachineRegisters.ReturnValue0, value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetReturnRef(long value)
            => SetGpr(MachineRegisters.ReturnValue0, value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ReadU8(int abs)
        {
            CheckRange(abs, 1);
            return _mem[abs];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteU8(int abs, byte value)
        {
            CheckWritableRange(abs, 1);
            _mem[abs] = value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadU16(int abs)
        {
            CheckRange(abs, 2);
            return Unsafe.ReadUnaligned<ushort>(ref _mem[abs]);
        }

        private void WriteU16(int abs, ushort value)
        {
            CheckWritableRange(abs, 2);
            Unsafe.WriteUnaligned(ref _mem[abs], value);
        }

        private int ReadI32(int abs)
        {
            CheckRange(abs, 4);
            return Unsafe.ReadUnaligned<int>(ref _mem[abs]);
        }

        private void WriteI32(int abs, int value)
        {
            CheckWritableRange(abs, 4);
            Unsafe.WriteUnaligned(ref _mem[abs], value);
        }

        private long ReadI64(int abs)
        {
            CheckRange(abs, 8);
            return Unsafe.ReadUnaligned<long>(ref _mem[abs]);
        }

        private void WriteI64(int abs, long value)
        {
            CheckWritableRange(abs, 8);
            Unsafe.WriteUnaligned(ref _mem[abs], value);
        }

        private long ReadNative(int abs)
        {
#pragma warning disable CS0162
            if (TargetArchitecture.PointerSize == 8) return ReadI64(abs);
            return ReadI32(abs);
#pragma warning restore CS0162
        }

        private void WriteNative(int abs, long value)
        {
#pragma warning disable CS0162
            if (TargetArchitecture.PointerSize == 8) WriteI64(abs, value);
            else WriteI32(abs, checked((int)value));
#pragma warning restore CS0162
        }

        private long ReadNativeOrI64(int abs, RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef || IsNativeIntType(type))
                return ReadNative(abs);
            return ReadI64(abs);
        }

        private long ReadSizedInteger(int abs, RuntimeType type)
        {
            int size = StorageSizeOf(type);
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef || IsNativeIntType(type))
                return ReadNative(abs);
            switch (size)
            {
                case 1: return IsUnsignedSmall(type) ? ReadU8(abs) : unchecked((sbyte)ReadU8(abs));
                case 2: return IsUnsignedSmall(type) || IsCharType(type) ? ReadU16(abs) : unchecked((short)ReadU16(abs));
                case 4: return IsUnsignedSmall(type) ? unchecked((uint)ReadI32(abs)) : ReadI32(abs);
                case 8: return ReadI64(abs);
                default: throw new InvalidOperationException("Unsupported scalar size: " + size.ToString());
            }
        }

        private void WriteSizedInteger(int abs, RuntimeType type, long value)
        {
            int size = StorageSizeOf(type);
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef || IsNativeIntType(type))
            {
                WriteNative(abs, value);
                return;
            }
            switch (size)
            {
                case 1: WriteU8(abs, unchecked((byte)value)); return;
                case 2: WriteU16(abs, unchecked((ushort)value)); return;
                case 4: WriteI32(abs, unchecked((int)value)); return;
                case 8: WriteI64(abs, value); return;
                default: throw new InvalidOperationException("Unsupported scalar size: " + size.ToString());
            }
        }

        private void CheckRange(int abs, int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            if ((uint)abs > (uint)_mem.Length || abs + size < abs || abs + size > _mem.Length)
                throw new AccessViolationException();
        }

        private void CheckWritableRange(int abs, int size)
        {
            CheckRange(abs, size);
            if (abs < _staticEnd)
                throw new AccessViolationException("Attempt to write read-only/static metadata memory.");
        }


        private void CheckHeapAccess(int abs, int size, bool writable)
        {
            CheckHeapAccess(abs, size);
            if (writable) CheckWritableRange(abs, size);
        }

        private void CheckIndirectAccess(int abs, int size, bool writable)
        {
            CheckIndirectAccess(abs, size);
            if (writable) CheckWritableRange(abs, size);
        }

        private void CheckStackRange(int abs, int size)
        {
            CheckRange(abs, size);
            long stackLow = CurrentFrameStackLow();
            if (abs < stackLow || abs + size > _stackEnd)
                throw new AccessViolationException("Stack access out of active frame range.");
        }

        private void CheckHeapAccess(int abs, int size)
        {
            CheckRange(abs, size);
            if (abs < _heapBase || abs + size > _heapPtr)
                throw new AccessViolationException("Heap access out of range.");
        }

        private void CheckIndirectAccess(int abs, int size)
        {
            CheckRange(abs, size);
            if (abs >= _heapBase)
            {
                CheckHeapAccess(abs, size);
                return;
            }
            if (abs >= _stackBase && abs < _stackEnd)
            {
                CheckStackRange(abs, size);
                return;
            }
            if (abs >= _staticEnd && abs < _heapBase)
                return;
            throw new AccessViolationException();
        }

        private void TrackStackPeak(long stackLow)
        {
            int sp = checked((int)stackLow);
            if (sp < _frameStackTop) throw new StackOverflowException();
            if (sp > _stackEnd) throw new AccessViolationException("Stack pointer above stack end.");
            if (sp < _stackLowWatermark) _stackLowWatermark = sp;
        }

        private static int AlignPayloadStart(int blockLowerBound, int payloadAlignment)
            => AlignUp(checked(blockLowerBound + BlockHeaderSize), payloadAlignment);

        private static int AlignBlockStart(int lowerBound)
            => AlignPayloadStart(lowerBound, HeapBlockAlignment) - BlockHeaderSize;

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1) return value;
            int mask = alignment - 1;
            if ((alignment & mask) != 0) throw new ArgumentException("Alignment must be power of two.");
            return checked((value + mask) & ~mask);
        }

        private static long AlignDown(long value, int alignment)
        {
            if (alignment <= 1) return value;
            int mask = alignment - 1;
            if ((alignment & mask) != 0) throw new ArgumentException("Alignment must be power of two.");
            return value & ~((long)mask);
        }

        private RuntimeType ValidateArrayRef(long objRef)
        {
            if (objRef == 0) throw new NullReferenceException();
            RuntimeType t = GetObjectTypeFromRef(objRef);
            if (t.Kind != RuntimeTypeKind.Array) throw new ArrayTypeMismatchException();
            return t;
        }


        private RuntimeType ValidateArrayRef(long objRef, out int abs, out RuntimeType type)
        {
            type = ValidateArrayRef(objRef);
            abs = checked((int)objRef);
            return type;
        }

        private RuntimeType ValidateStringRef(long objRef)
        {
            if (objRef == 0) throw new NullReferenceException();
            RuntimeType t = GetObjectTypeFromRef(objRef);
            if (!IsSystemStringType(t)) throw new InvalidCastException();
            return t;
        }

        private bool TryGetObjectTypeFromExactRef(long objRef, out RuntimeType type)
        {
            type = null!;
            if (objRef < int.MinValue || objRef > int.MaxValue) return false;
            int obj = (int)objRef;
            if (!TryGetBlockFromObjectRef(obj, out _)) return false;
            int flags = ReadI32(obj + 4);
            if ((flags & GcFlagAllocated) == 0) return false;
            type = _rts.GetTypeById(ReadI32(obj));
            return true;
        }

        private RuntimeType GetObjectTypeFromRef(long objRef)
        {
            int obj = checked((int)objRef);
            if (!TryGetBlockFromObjectRef(obj, out _)) throw new AccessViolationException();
            int flags = ReadI32(obj + 4);
            if ((flags & GcFlagAllocated) == 0) throw new AccessViolationException();
            return _rts.GetTypeById(ReadI32(obj));
        }


        private RuntimeType ValidateStringRef(long objRef, out int abs)
        {
            RuntimeType type = ValidateStringRef(objRef);
            abs = checked((int)objRef);
            return type;
        }


        private RuntimeType ResolveRequiredType(string assemblyName, string ns, string name)
        {
            RuntimeType? fallback = null;
            foreach (RuntimeType t in EnumerateRuntimeTypes(_rts))
            {
                if (t.Namespace != ns || t.Name != name) continue;
                if (t.AssemblyName == assemblyName) return t;
                fallback ??= t;
            }
            if (fallback != null) return fallback;
            throw new TypeLoadException(ns + "." + name);
        }

        private RuntimeField FieldById(long id)
        {
            int fieldId = checked((int)id);
            if (_fieldById.TryGetValue(fieldId, out RuntimeField? f)) return f;
            var fresh = BuildFieldIdMap(_rts);
            foreach (var kv in fresh) _fieldById[kv.Key] = kv.Value;
            if (_fieldById.TryGetValue(fieldId, out f)) return f;
            throw new MissingFieldException("Runtime field not found: F" + fieldId.ToString());
        }

        private static Dictionary<int, RuntimeField> BuildFieldIdMap(RuntimeTypeSystem rts)
        {
            var result = new Dictionary<int, RuntimeField>();
            foreach (RuntimeType t in EnumerateRuntimeTypes(rts))
            {
                for (int i = 0; i < t.InstanceFields.Length; i++)
                    result[t.InstanceFields[i].FieldId] = t.InstanceFields[i];
                for (int i = 0; i < t.StaticFields.Length; i++)
                    result[t.StaticFields[i].FieldId] = t.StaticFields[i];
            }
            return result;
        }

        private static IEnumerable<RuntimeType> EnumerateRuntimeTypes(RuntimeTypeSystem rts)
        {
            var fi = typeof(RuntimeTypeSystem).GetField("_typeById", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fi?.GetValue(rts) is Dictionary<int, RuntimeType> byId)
            {
                foreach (var kv in byId) yield return kv.Value;
            }
        }

        private bool IsAssignableTo(RuntimeType source, RuntimeType target)
        {
            if (ReferenceEquals(source, target) || source.TypeId == target.TypeId) return true;

            if (target.Namespace == "System" && target.Name == "Object")
                return true;
            if (source.IsValueType && target.Namespace == "System" && (target.Name == "ValueType" || target.Name == "Object"))
                return true;
            if (source.Kind == RuntimeTypeKind.Enum && target.Namespace == "System" && target.Name == "Enum")
                return true;

            for (RuntimeType? cur = source.BaseType; cur != null; cur = cur.BaseType)
            {
                if (cur.TypeId == target.TypeId) return true;
            }

            for (int i = 0; i < source.Interfaces.Length; i++)
            {
                if (source.Interfaces[i].TypeId == target.TypeId) return true;
            }

            if (source.Kind == RuntimeTypeKind.Array && target.Kind == RuntimeTypeKind.Array)
            {
                RuntimeType? se = source.ElementType;
                RuntimeType? te = target.ElementType;
                if (se == null || te == null) return false;
                if (se.TypeId == te.TypeId) return true;
                if (se.IsReferenceType && te.IsReferenceType) return IsAssignableTo(se, te);
            }

            return false;
        }

        private RuntimeMethod ResolveVirtualDispatch(RuntimeType actual, RuntimeMethod declared)
        {
            if (declared.VTableSlot >= 0 && declared.VTableSlot < actual.VTable.Length)
                return actual.VTable[declared.VTableSlot];
            return FindMostDerivedMethodByNameAndSig(actual, declared) ?? declared;
        }

        private static RuntimeMethod? FindMostDerivedMethodByNameAndSig(RuntimeType actual, RuntimeMethod declared)
        {
            for (RuntimeType? cur = actual; cur != null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.Methods.Length; i++)
                {
                    RuntimeMethod m = cur.Methods[i];
                    if (m.Name != declared.Name) continue;
                    if (m.ParameterTypes.Length != declared.ParameterTypes.Length) continue;
                    bool same = true;
                    for (int p = 0; p < m.ParameterTypes.Length; p++)
                    {
                        if (m.ParameterTypes[p].TypeId != declared.ParameterTypes[p].TypeId)
                        {
                            same = false;
                            break;
                        }
                    }
                    if (same) return m;
                }
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TypeIsReferenceOrContainsReferences(RuntimeType type)
        {
            return type.IsReferenceType
                || type.Kind == RuntimeTypeKind.ByRef
                || type.Kind == RuntimeTypeKind.TypeParam
                || type.ContainsGcPointers;
        }

        private int StorageSizeOf(RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                return TargetArchitecture.PointerSize;
            return Math.Max(1, type.SizeOf);
        }

        private int StorageAlignOf(RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                return TargetArchitecture.PointerSize;
            return Math.Max(1, type.AlignOf);
        }

        private bool IsNativeIntType(RuntimeType type)
            => type.Namespace == "System" && (type.Name == "IntPtr" || type.Name == "UIntPtr" || type.Name == "nint" || type.Name == "nuint");

        private bool IsCharType(RuntimeType type)
            => type.Namespace == "System" && type.Name == "Char";

        private bool IsUnsignedSmall(RuntimeType type)
            => type.Namespace == "System" && (type.Name == "Byte" || type.Name == "UInt16" || type.Name == "UInt32" || type.Name == "UInt64" || type.Name == "UIntPtr");

        private bool IsCoreLibRandomLike(RuntimeType type)
            => type.Namespace == "System" && (type.Name == "Random" || type.Name == "ThreadSafeRandom");

        private bool IsSystemStringType(RuntimeType type)
            => type.Namespace == "System" && type.Name == "String";

        private RuntimeType? FindRuntimeType(string ns, string name)
        {
            foreach (RuntimeType t in EnumerateRuntimeTypes(_rts))
            {
                if (t.Namespace == ns && t.Name == name) return t;
            }
            return null;
        }

        private string ReadManagedString(int obj)
        {
            if (obj == 0) return string.Empty;
            ValidateStringRef(obj);
            int len = ReadI32(obj + StringLengthOffset);
            if (len < 0) throw new InvalidOperationException("Corrupted string length.");
            char[] chars = new char[len];
            int p = obj + StringCharsOffset;
            for (int i = 0; i < len; i++)
                chars[i] = (char)ReadU16(p + i * 2);
            return new string(chars);
        }

        private bool TryTranslateHostExceptionToManaged(Exception ex, out int exceptionRef)
        {
            string name = ex switch
            {
                OverflowException => "OverflowException",
                DivideByZeroException => "DivideByZeroException",
                NullReferenceException => "NullReferenceException",
                IndexOutOfRangeException => "IndexOutOfRangeException",
                InvalidCastException => "InvalidCastException",
                OutOfMemoryException => "OutOfMemoryException",
                ArgumentNullException => "ArgumentNullException",
                ArgumentOutOfRangeException => "ArgumentOutOfRangeException",
                ArgumentException => "ArgumentException",
                InvalidOperationException => "InvalidOperationException",
                NotSupportedException => "NotSupportedException",
                AccessViolationException => "AccessViolationException",
                _ => "Exception"
            };

            try
            {
                exceptionRef = AllocExceptionRef("System", name, ex.Message ?? string.Empty);
                return true;
            }
            catch
            {
                exceptionRef = 0;
                return false;
            }
        }

        private int AllocExceptionRef(string ns, string name, string message)
        {
            RuntimeType? t = FindRuntimeType(ns, name) ?? FindRuntimeType("System", "Exception");
            if (t == null) return 0;
            int obj = AllocObject(t);
            RuntimeField? messageField = null;
            for (RuntimeType? cur = t; cur != null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                {
                    if (cur.InstanceFields[i].Name == "_message")
                    {
                        messageField = cur.InstanceFields[i];
                        break;
                    }
                }
                if (messageField != null) break;
            }
            if (messageField != null)
            {
                int s = InternString(message);
                WriteNative(obj + messageField.Offset, s);
            }
            return obj;
        }

        private string TryReadExceptionMessage(int exceptionRef)
        {
            try
            {
                if (exceptionRef == 0) return string.Empty;
                int obj = exceptionRef;
                RuntimeType t = GetObjectTypeFromRef(obj);
                for (RuntimeType? cur = t; cur != null; cur = cur.BaseType)
                {
                    for (int i = 0; i < cur.InstanceFields.Length; i++)
                    {
                        RuntimeField f = cur.InstanceFields[i];
                        if (f.Name == "_message")
                        {
                            int s = checked((int)ReadNative(obj + f.Offset));
                            return ReadManagedString(s);
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private void ClearArray(long arrayRef, int index, int length)
        {
            RuntimeType arrayType = ValidateArrayRef(arrayRef);
            int arr = checked((int)arrayRef);
            int len = ReadI32(arr + ArrayLengthOffset);
            if (index < 0 || length < 0 || index > len - length) throw new IndexOutOfRangeException();
            RuntimeType elem = arrayType.ElementType ?? throw new InvalidOperationException("Array has no element type.");
            int elemSize = StorageSizeOf(elem);
            int abs = arr + ArrayDataOffset + checked(index * elemSize);
            Array.Clear(_mem, abs, checked(length * elemSize));
        }

        private bool CopyArray(long sourceRef, int sourceIndex, long destinationRef, int destinationIndex, int length)
        {
            RuntimeType srcType = ValidateArrayRef(sourceRef);
            RuntimeType dstType = ValidateArrayRef(destinationRef);
            int src = checked((int)sourceRef);
            int dst = checked((int)destinationRef);
            int srcLen = ReadI32(src + ArrayLengthOffset);
            int dstLen = ReadI32(dst + ArrayLengthOffset);
            if (sourceIndex < 0 || destinationIndex < 0 || length < 0) throw new ArgumentOutOfRangeException();
            if (sourceIndex > srcLen - length || destinationIndex > dstLen - length) throw new ArgumentException();
            RuntimeType srcElem = srcType.ElementType ?? throw new InvalidOperationException("Source array has no element type.");
            RuntimeType dstElem = dstType.ElementType ?? throw new InvalidOperationException("Destination array has no element type.");
            if (srcElem.TypeId != dstElem.TypeId)
            {
                if (!(srcElem.IsReferenceType && dstElem.IsReferenceType && IsAssignableTo(srcElem, dstElem)))
                    return false;
            }
            int srcSize = StorageSizeOf(srcElem);
            int dstSize = StorageSizeOf(dstElem);
            if (srcSize != dstSize) return false;
            int bytes = checked(length * srcSize);
            int srcAbs = src + ArrayDataOffset + checked(sourceIndex * srcSize);
            int dstAbs = dst + ArrayDataOffset + checked(destinationIndex * dstSize);
            Buffer.BlockCopy(_mem, srcAbs, _mem, dstAbs, bytes);
            return true;
        }

        private static int BitOperationsRotateLeft(int value, int offset)
            => (int)RotateLeft((uint)value, offset);

        private static int BitOperationsRotateRight(int value, int offset)
            => (int)RotateRight((uint)value, offset);

        private static long RotateLeft(long value, int offset)
            => (long)RotateLeft((ulong)value, offset);

        private static long RotateRight(long value, int offset)
            => (long)RotateRight((ulong)value, offset);

        private static uint RotateLeft(uint value, int offset)
        {
            offset &= 31;
            return (value << offset) | (value >> ((32 - offset) & 31));
        }

        private static uint RotateRight(uint value, int offset)
        {
            offset &= 31;
            return (value >> offset) | (value << ((32 - offset) & 31));
        }

        private static ulong RotateLeft(ulong value, int offset)
        {
            offset &= 63;
            return (value << offset) | (value >> ((64 - offset) & 63));
        }

        private static ulong RotateRight(ulong value, int offset)
        {
            offset &= 63;
            return (value >> offset) | (value << ((64 - offset) & 63));
        }
    }
}
