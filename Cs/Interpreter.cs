using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cnidaria.Cs
{
    internal enum SlotKind : byte
    {
        I4,
        I8,
        R8,
        Ref,   // payload: handle/offset, aux: type id/token if needed
        Ptr,   // payload: offset in mem, aux: elementSize
        ByRef, // payload: offset in mem, aux: elementSize
        Value, // payload: absolute address of value bytes in current frame blob arena, aux: byte size
        Null,
    }
    internal readonly struct Slot
    {
        public readonly SlotKind Kind;
        public readonly int Aux;
        public readonly long Payload;

        public Slot(SlotKind kind, long payload, int aux = 0)
        {
            Kind = kind;
            Payload = payload;
            Aux = aux;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AsI4Checked()
        {
            if (Kind != SlotKind.I4) throw new InvalidOperationException($"Expected I4, got {Kind}");
            return unchecked((int)Payload);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long AsI8Checked()
        {
            if (Kind != SlotKind.I8) throw new InvalidOperationException($"Expected I8, got {Kind}");
            return Payload;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double AsR8Checked()
        {
            if (Kind != SlotKind.R8) throw new InvalidOperationException($"Expected R8, got {Kind}");
            return BitConverter.Int64BitsToDouble(Payload);
        }
    }
    public sealed class ExecutionLimits
    {
        public int MaxCallDepth { get; init; } = 128;
        public long MaxInstructions { get; init; } = 100_000_000;
        public int TokenCheckPeriod { get; init; } = 256;
    }
    internal sealed class Vm
    {
        private enum PendingCtorResultKind : byte
        {
            None, Ref, Value
        }
        internal sealed class VmUnhandledException : Exception
        {
            public VmUnhandledException(string message) : base(message) { }
        }
        private readonly struct PendingCtorResult
        {
            public readonly PendingCtorResultKind Kind;
            public readonly long Payload;
            public readonly RuntimeType? Type;

            public PendingCtorResult(PendingCtorResultKind kind, long payload, RuntimeType? type)
            {
                Kind = kind;
                Payload = payload;
                Type = type;
            }

            public static PendingCtorResult ForRef(long objRef) =>
                new PendingCtorResult(PendingCtorResultKind.Ref, objRef, null);

            public static PendingCtorResult ForValue(RuntimeType t, int tempAbs) =>
                new PendingCtorResult(PendingCtorResultKind.Value, tempAbs, t);
        }
        private readonly struct CatchContext
        {
            public readonly int FrameBase;
            public readonly int HandlerStartPc;
            public readonly int HandlerEndPc;
            public readonly Slot Exception;

            public CatchContext(int frameBase, int handlerStartPc, int handlerEndPc, Slot exception)
            {
                FrameBase = frameBase;
                HandlerStartPc = handlerStartPc;
                HandlerEndPc = handlerEndPc;
                Exception = exception;
            }
        }
        private readonly struct FinallyContext
        {
            public readonly int FrameBase;
            public readonly int FinallyStartPc;
            public readonly int FinallyEndPc;

            public readonly int NextFromPc;

            public readonly FinallyContinuationKind Kind;

            // Jump continuation
            public readonly int TargetPc;

            // Return continuation
            public readonly bool HasReturnValue;
            public readonly Slot ReturnValue;

            // Throw continuation
            public readonly bool HasPendingException;
            public readonly Slot PendingException;

            public FinallyContext(
                int frameBase,
                int finallyStartPc,
                int finallyEndPc,
                int nextFromPc,
                FinallyContinuationKind kind,
                int targetPc,
                bool hasReturnValue,
                Slot returnValue,
                bool hasPendingException,
                Slot pendingException)
            {
                FrameBase = frameBase;
                FinallyStartPc = finallyStartPc;
                FinallyEndPc = finallyEndPc;
                NextFromPc = nextFromPc;
                Kind = kind;
                TargetPc = targetPc;
                HasReturnValue = hasReturnValue;
                ReturnValue = returnValue;
                HasPendingException = hasPendingException;
                PendingException = pendingException;
            }

            public static FinallyContext ForJump(int frameBase, ExceptionHandler h, int targetPc)
                => new FinallyContext(
                    frameBase,
                    finallyStartPc: h.HandlerStartPc,
                    finallyEndPc: h.HandlerEndPc,
                    nextFromPc: h.TryEndPc,
                    kind: FinallyContinuationKind.Jump,
                    targetPc: targetPc,
                    hasReturnValue: false,
                    returnValue: default,
                    hasPendingException: false,
                    pendingException: default);

            public static FinallyContext ForReturn(int frameBase, ExceptionHandler h, bool hasRet, Slot retVal)
                => new FinallyContext(
                    frameBase,
                    finallyStartPc: h.HandlerStartPc,
                    finallyEndPc: h.HandlerEndPc,
                    nextFromPc: h.TryEndPc,
                    kind: FinallyContinuationKind.Return,
                    targetPc: -1,
                    hasReturnValue: hasRet,
                    returnValue: retVal,
                    hasPendingException: false,
                    pendingException: default);

            public static FinallyContext ForThrow(int frameBase, ExceptionHandler h, Slot ex)
                => new FinallyContext(
                    frameBase,
                    finallyStartPc: h.HandlerStartPc,
                    finallyEndPc: h.HandlerEndPc,
                    nextFromPc: h.TryEndPc,
                    kind: FinallyContinuationKind.Throw,
                    targetPc: -1,
                    hasReturnValue: false,
                    returnValue: default,
                    hasPendingException: true,
                    pendingException: ex);
        }
        private readonly struct FreeBlock
        {
            public readonly int Abs;
            public readonly int Size;
            public int End => checked(Abs + Size);

            public FreeBlock(int abs, int size)
            {
                Abs = abs;
                Size = size;
            }
        }
        private enum FinallyContinuationKind : byte
        {
            Jump,
            Return,
            Throw,
        }
        private enum FastCellKind : byte
        {
            None = 0,
            I4 = 1,
            I8 = 2,
            R8 = 3,
        }
        private sealed class MethodExecLayout
        {
            public RuntimeMethod Method = null!;

            public RuntimeType[] ArgTypes = Array.Empty<RuntimeType>();
            public int[] ArgOffsets = Array.Empty<int>();
            public int[] ArgSizes = Array.Empty<int>();
            public FastCellKind[] ArgFastKinds = Array.Empty<FastCellKind>();
            public int ArgsAreaSize;

            public RuntimeType[] LocalTypes = Array.Empty<RuntimeType>();
            public int[] LocalOffsets = Array.Empty<int>();
            public int[] LocalSizes = Array.Empty<int>();
            public FastCellKind[] LocalFastKinds = Array.Empty<FastCellKind>();
            public int LocalsAreaSize;
        }
        private const int FinallyCatchTypeToken = -1;

        private const int SlotSize = 16;
        private const int ObjectHeaderSize = 8;

        private const int GcFlagMark = 1 << 0;
        private const int GcFlagAllocated = 1 << 1;

        private const int ArrayLengthOffset = ObjectHeaderSize + 0;   // +8
        private const int ArrayDataOffset = ObjectHeaderSize + 8;   // +16 aligned

        private const int StringLengthOffset = ObjectHeaderSize + 0;  // +8
        private const int StringCharsOffset = ObjectHeaderSize + 4;  // +12

        private readonly int _heapBase;
        private readonly int _heapEnd;
        private int _heapPtr;
        private int _heapFloor;

        private readonly byte[] _mem;
        private readonly int _metaEnd;
        private readonly int _stackBase;
        private readonly int _stackEnd;

        private int _stackPeakAbs;
        private int _heapPeakAbs;
        public int StackPeakBytes => _stackPeakAbs - _stackBase;
        public int HeapPeakBytes => _heapPeakAbs - _heapBase;

        private int _sp;          // absolute offset into _mem
        private int _frameBase;   // absolute offset to current frame header, -1 if none
        private int _callDepth;

        private long _fuel;
        private int _tick;
        private readonly RuntimeModule[] _moduleById;
        private readonly Dictionary<string, int> _moduleIdByName;
        private readonly Dictionary<int, int> _staticBaseByTypeId = new();
        private readonly Dictionary<int, byte> _typeInitState = new(); // 0 = not started, 1 = running, 2 = done
        private readonly Dictionary<int, int> _pendingTypeInitFrames = new();
        private readonly Domain _domain;
        private readonly RuntimeTypeSystem _rts;
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;
        private readonly Dictionary<int, PendingCtorResult> _pendingCtorResults = new();
        private readonly List<CatchContext> _catchStack = new();
        private readonly List<FinallyContext> _finallyStack = new();
        private readonly Dictionary<int, MethodExecLayout> _methodLayouts = new();
        private readonly List<int> _heapObjects = new();
        private readonly Dictionary<string, int> _internPool = new(StringComparer.Ordinal);
        private readonly TextWriter _textWriter;
        private readonly Dictionary<int, HostOverride> _hostOverrides = new();
        private readonly VmCallContext _hostCtx;

        private RuntimeModule? _curModule;
        private BytecodeFunction? _curFn;
        private MethodExecLayout? _curLayout;

        private int _curArgsAbs;
        private int _curLocalsAbs;
        private int _curEvalBase;
        private int _curEvalSp;
        private int _curEvalMax;
        private int _curPc;
        private Slot[] _hotEvalSlots = Array.Empty<Slot>();

        private int _exceptionTranslationDepth;
        private long _instructionLimit = 0;
        public long InctructionsElapsed => _instructionLimit - _fuel;


        private readonly List<FreeBlock> _freeBlocks = new();
        private readonly HashSet<int> _heapAllocFrames = new();

        private long _allocDebtBytes;
        private int _allocBudgetBytes;
        private int _minTailFreeBytes;
        private bool _gcRequested;
        private bool _gcRunning;
        public Vm(
            byte[] memory,
            int metaEnd,
            int stackBase,
            int stackEnd,
            Domain domain,
            RuntimeTypeSystem rts,
            IReadOnlyDictionary<string, RuntimeModule> modules,
            TextWriter textWriter)
        {
            _mem = memory ?? throw new ArgumentNullException(nameof(memory));
            _metaEnd = metaEnd;
            _stackBase = stackBase;
            _stackEnd = stackEnd;
            _sp = stackBase;
            _frameBase = -1;
            _stackPeakAbs = _sp;

            _heapBase = AlignUp(_stackEnd, 8);
            _heapEnd = _mem.Length;
            _heapPtr = _heapBase;
            _heapFloor = _heapPtr;
            _heapPeakAbs = _heapPtr;
            if (_heapBase > _heapEnd)
                throw new ArgumentOutOfRangeException("Heap region is empty or invalid.");

            _domain = domain ?? throw new ArgumentNullException(nameof(domain));
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));

            var list = new List<RuntimeModule>(_modules.Count);
            foreach (var kv in _modules) list.Add(kv.Value);
            list.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
            _moduleById = list.ToArray();
            _moduleIdByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _moduleById.Length; i++)
                _moduleIdByName[_moduleById[i].Name] = i;

            if (!(0 <= _metaEnd && _metaEnd <= _stackBase && _stackBase < _stackEnd && _stackEnd <= _mem.Length))
                throw new ArgumentOutOfRangeException("Bad memory layout.");
            _textWriter = textWriter;
            _hostCtx = new VmCallContext(this);

            RecomputeGcThresholds();
        }
        // Frame header layout (all int32, little-endian)
        // 0:  prevFrameBase
        // 4:  returnPc
        // 8:  returnMethodToken
        // 12: returnModuleId
        // 16: thisMethodToken
        // 20: thisModuleId
        // 24: pc
        // 28: evalBase
        // 32: evalSp (slot index)
        // 36: maxEval (slot capacity)
        // 40: argsBase
        // 44: localsBase
        // 48: evalBlobBase
        // 52: evalBlobSp (bytes)
        // 56: evalBlobCap (bytes)
        // 60: stackallocBase
        // 64: stackallocSp (bytes)
        // 68: frameEnd (absolute)
        // 72: runtimeMethodId (0 if unresolved)
        private const int FrameHeaderSize = 76;
        public void Execute(
            RuntimeModule entryModule,
            BytecodeFunction entry,
            CancellationToken ct,
            ExecutionLimits limits,
            ReadOnlySpan<Slot> initialArgs = default)
        {
            if (entryModule is null) throw new ArgumentNullException(nameof(entryModule));
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            if (limits is null) throw new ArgumentNullException(nameof(limits));

            _instructionLimit = limits.MaxInstructions;
            _fuel = limits.MaxInstructions;
            _tick = 0;

            // Push initial frame
            PushFrame(entryModule, entry, returnPc: -1, returnMethodToken: 0, returnModuleId: -1, ct, limits,
                runtimeMethod: ResolveRuntimeMethodOrThrow(entryModule, entry.MethodToken), initialArgs: initialArgs);

            while (_frameBase >= 0)
            {
                if (--_fuel < 0)
                    throw new OperationCanceledException("Instruction budget exceeded.");

                _tick++;
                if (_tick == limits.TokenCheckPeriod)
                {
                    _tick = 0;
                    ct.ThrowIfCancellationRequested();
                }
                MaybeCollectGarbage(force: false);

                PruneCatchContextsForPc(_curPc);

                var mod = _curModule ?? throw new InvalidOperationException("No current module.");
                var fn = _curFn ?? throw new InvalidOperationException("No current function.");
                int pc = _curPc;
                if ((uint)pc >= (uint)fn.Instructions.Length)
                    throw new InvalidOperationException($"PC out of range: {pc}");

                var ins = fn.Instructions[pc];
                _curPc = pc + 1;
                try
                {
                    switch (ins.Op)
                    {
                        case BytecodeOp.Nop:
                            break;

                        case BytecodeOp.Ldc_I4:
                            PushSlot(new Slot(SlotKind.I4, ins.Operand0));
                            break;

                        case BytecodeOp.Ldc_I8:
                            PushSlot(new Slot(SlotKind.I8, ins.Operand2));
                            break;

                        case BytecodeOp.Ldc_R8:
                            PushSlot(new Slot(SlotKind.R8, ins.Operand2));
                            break;

                        case BytecodeOp.Ldnull:
                            PushSlot(new Slot(SlotKind.Null, 0));
                            break;

                        case BytecodeOp.DefaultValue:
                            ExecDefaultValue(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Sizeof:
                            ExecSizeof(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Pop:
                            _ = PopSlot();
                            break;

                        case BytecodeOp.Dup:
                            {
                                var v = PeekSlot();
                                PushSlot(v);
                            }
                            break;

                        case BytecodeOp.Ldloc:
                            {
                                int loc = ins.Operand0;
                                LoadLocal(loc);
                            }
                            break;

                        case BytecodeOp.Stloc:
                            {
                                int loc = ins.Operand0;
                                StoreLocal(loc, PopSlot());
                            }
                            break;

                        case BytecodeOp.Ldarg:
                            {
                                int arg = ins.Operand0;
                                LoadArg(arg);
                            }
                            break;

                        case BytecodeOp.Starg:
                            {
                                int arg = ins.Operand0;
                                StoreArg(arg, PopSlot());
                            }
                            break;

                        case BytecodeOp.Ldthis:
                            LoadArg(0);
                            break;

                        case BytecodeOp.Neg:
                            ExecNeg();
                            break;

                        case BytecodeOp.Not:
                            ExecNot();
                            break;

                        case BytecodeOp.Add:
                            ExecAdd();
                            break;

                        case BytecodeOp.Sub:
                            ExecSubtract();
                            break;

                        case BytecodeOp.Mul:
                            ExecMultiply();
                            break;

                        case BytecodeOp.Div:
                            ExecDivide();
                            break;

                        case BytecodeOp.Div_Un:
                            ExecUnsignedDivide();
                            break;

                        case BytecodeOp.Rem:
                            ExecRemeinder();
                            break;

                        case BytecodeOp.Rem_Un:
                            ExecUnsignedRemeinder();
                            break;

                        case BytecodeOp.And:
                            ExecBitwiseAnd();
                            break;

                        case BytecodeOp.Or:
                            ExecBitwiseOr();
                            break;

                        case BytecodeOp.Xor:
                            ExecBitwiseXor();
                            break;

                        case BytecodeOp.Shl:
                            ExecShiftLeft();
                            break;

                        case BytecodeOp.Shr:
                            ExecShiftRight();
                            break;

                        case BytecodeOp.Shr_Un:
                            ExecUnsignedShiftRight();
                            break;

                        case BytecodeOp.Clt:
                            {
                                var b = PopSlot();
                                var a = PopSlot();
                                PushSlot(new Slot(SlotKind.I4, CompareLess(a, b)));
                            }
                            break;

                        case BytecodeOp.Clt_Un:
                            {
                                var b = PopSlot();
                                var a = PopSlot();
                                PushSlot(new Slot(SlotKind.I4, CompareLessUnsigned(a, b)));
                            }
                            break;

                        case BytecodeOp.Cgt:
                            {
                                var b = PopSlot();
                                var a = PopSlot();
                                PushSlot(new Slot(SlotKind.I4, CompareGreater(a, b)));
                            }
                            break;

                        case BytecodeOp.Cgt_Un:
                            {
                                var b = PopSlot();
                                var a = PopSlot();
                                PushSlot(new Slot(SlotKind.I4, CompareGreaterUnsigned(a, b)));
                            }
                            break;

                        case BytecodeOp.Ceq:
                            {
                                var b = PopSlot();
                                var a = PopSlot();
                                int res = CompareEqual(a, b);
                                PushSlot(new Slot(SlotKind.I4, res));
                            }
                            break;

                        case BytecodeOp.Br:
                            if (!TryBeginFinallyForJump(fn, fromPc: pc, targetPc: ins.Operand0))
                                _curPc = ins.Operand0;
                            break;

                        case BytecodeOp.Brtrue:
                            {
                                var cond = PopSlot();
                                if (ToBool(cond))
                                {
                                    if (!TryBeginFinallyForJump(fn, fromPc: pc, targetPc: ins.Operand0))
                                        _curPc = ins.Operand0;
                                }
                            }
                            break;

                        case BytecodeOp.Brfalse:
                            {
                                var cond = PopSlot();
                                if (!ToBool(cond))
                                {
                                    if (!TryBeginFinallyForJump(fn, fromPc: pc, targetPc: ins.Operand0))
                                        _curPc = ins.Operand0;
                                }
                            }
                            break;

                        case BytecodeOp.Conv:
                            {
                                var v = PopSlot();
                                var kind = (NumericConvKind)ins.Operand0;
                                var flags = (NumericConvFlags)ins.Operand1;
                                PushSlot(DoConv(v, kind, flags));
                            }
                            break;

                        case BytecodeOp.Call:
                            {
                                int callTok = ins.Operand0;
                                int packed = ins.Operand1;
                                int argCount = packed & 0x7FFF;
                                int hasThis = (packed >> 15) & 1;
                                int total = argCount + hasThis;

                                var (targetModuleOpt, targetFn) = _domain.ResolveCall(mod, callTok);
                                var targetModule = targetModuleOpt ?? mod;
                                var rm = ResolveRuntimeMethodOrThrow(mod, callTok, _curLayout?.Method);
                                if (rm.IsStatic && !StringComparer.Ordinal.Equals(rm.Name, ".cctor"))
                                {
                                    if (TryDeferTypeInitialization(rm.DeclaringType, resumePc: pc, ct, limits))
                                        break;
                                }
                                if (hasThis != 0 && rm.DeclaringType.IsValueType)
                                {
                                    int thisIndex = _curEvalSp - total;
                                    if ((uint)thisIndex >= (uint)_curEvalSp)
                                        throw new InvalidOperationException("Eval stack underflow for value-type 'this'.");

                                    var thisSlot = _hotEvalSlots[thisIndex];
                                    if (thisSlot.Kind == SlotKind.Ref)
                                    {
                                        var actualThisType = GetObjectTypeFromRef(thisSlot);
                                        if (actualThisType.TypeId != rm.DeclaringType.TypeId)
                                            throw new InvalidOperationException(
                                                $"Boxed receiver type mismatch: have '{actualThisType.Namespace}.{actualThisType.Name}', need '{rm.DeclaringType.Namespace}.{rm.DeclaringType.Name}'.");

                                        int boxedObjAbs = checked((int)thisSlot.Payload);
                                        int payloadAbs = GetBoxedValuePayloadAbs(boxedObjAbs);
                                        var (sz, _) = GetStorageSizeAlign(rm.DeclaringType);
                                        _hotEvalSlots[thisIndex] = new Slot(SlotKind.ByRef, payloadAbs, aux: sz);
                                    }
                                }

                                if (rm.IsStatic && TryInvokeHostOverride(rm, total, ct))
                                    break;

                                if (TryInvokeIntrinsic(rm, total, ct))
                                    break;



                                int callerModuleId = ReadI32(_frameBase + 20);

                                PushFrame(
                                    targetModule,
                                    targetFn,
                                    returnPc: _curPc,
                                    returnMethodToken: fn.MethodToken,
                                    returnModuleId: callerModuleId,
                                    ct, limits,
                                    totalArgsOnCallerStack: total,
                                    runtimeMethod: rm);

                            }
                            break;

                        case BytecodeOp.CallVirt:
                            {
                                int callTok = ins.Operand0;
                                int packed = ins.Operand1;
                                int argCount = packed & 0x7FFF;
                                int hasThis = (packed >> 15) & 1;
                                int total = argCount + hasThis;

                                if (hasThis == 0)
                                    throw new InvalidOperationException("CallVirt without 'this' is not supported.");

                                int thisIndex = _curEvalSp - total;
                                if ((uint)thisIndex >= (uint)_curEvalSp)
                                    throw new InvalidOperationException("Eval stack underflow for CallVirt.");

                                var receiver = _hotEvalSlots[thisIndex];
                                var receiverType = GetObjectTypeFromRef(receiver); // includes null check

                                var declared = ResolveRuntimeMethodOrThrow(mod, callTok, _curLayout?.Method);
                                var targetRm = ResolveVirtualDispatch(receiverType, declared);
                                if (targetRm.DeclaringType.IsValueType && receiver.Kind == SlotKind.Ref)
                                {
                                    int boxedObjAbs = checked((int)receiver.Payload);
                                    int payloadAbs = GetBoxedValuePayloadAbs(boxedObjAbs);
                                    var (sz, _) = GetStorageSizeAlign(targetRm.DeclaringType);
                                    _hotEvalSlots[thisIndex] = new Slot(SlotKind.ByRef, payloadAbs, aux: sz);
                                }
                                if (TryInvokeIntrinsic(targetRm, total, ct))
                                    break;

                                var targetModule = targetRm.BodyModule;
                                var targetFn = targetRm.Body;
                                if (targetModule is null || targetFn is null)
                                    throw new MissingMethodException(
                                        $"No body for virtual target: {targetRm.DeclaringType.Namespace}.{targetRm.DeclaringType.Name}.{targetRm.Name}");

                                int callerModuleId = ReadI32(_frameBase + 20);

                                PushFrame(
                                    targetModule,
                                    targetFn,
                                    returnPc: _curPc,
                                    returnMethodToken: fn.MethodToken,
                                    returnModuleId: callerModuleId,
                                    ct, limits,
                                    totalArgsOnCallerStack: total,
                                    runtimeMethod: targetRm);
                            }
                            break;

                        case BytecodeOp.Ret:
                            {
                                Slot retVal = default;
                                bool hasRet = ins.Pop == 1;
                                if (hasRet)
                                    retVal = PopSlot();

                                if (TryBeginFinallyForReturn(fn, fromPc: pc, hasRet: hasRet, retVal: retVal))
                                    break;

                                CompleteReturnFromCurrentFrame(hasRet, retVal);
                            }
                            break;
                        case BytecodeOp.Throw:
                            {
                                var ex = NormalizeThrownException(PopSlot());
                                ThrowException(ex, throwPc: pc);
                            }
                            break;
                        case BytecodeOp.Rethrow:
                            {
                                var ex = GetCurrentCatchExceptionOrThrow();
                                ThrowException(ex, throwPc: pc);
                            }
                            break;
                        case BytecodeOp.Ldexception:
                            PushSlot(GetCurrentCatchExceptionOrThrow());
                            break;
                        case BytecodeOp.Endfinally:
                            ExecEndfinally(pc);
                            break;
                        case BytecodeOp.Ldloca:
                            {
                                int loc = ins.Operand0;
                                var layout = _curLayout ?? throw new InvalidOperationException("No current layout.");
                                int abs = _curLocalsAbs + layout.LocalOffsets[loc];
                                int sz = layout.LocalSizes[loc];
                                PushSlot(new Slot(SlotKind.ByRef, abs, aux: sz));
                            }
                            break;

                        case BytecodeOp.Ldarga:
                            {
                                int arg = ins.Operand0;
                                var layout = _curLayout ?? throw new InvalidOperationException("No current layout.");
                                int abs = _curArgsAbs + layout.ArgOffsets[arg];
                                int sz = layout.ArgSizes[arg];
                                PushSlot(new Slot(SlotKind.ByRef, abs, aux: sz));
                            }
                            break;

                        case BytecodeOp.StackAlloc:
                            ExecStackAlloc(ins.Operand0);
                            break;

                        case BytecodeOp.PtrToByRef:
                            ExecPtrToByRef();
                            break;

                        case BytecodeOp.LdArrayDataRef:
                            ExecLdArrayDataRef();
                            break;

                        case BytecodeOp.PtrElemAddr:
                            ExecPtrElemAddr(ins.Operand0);
                            break;

                        case BytecodeOp.PtrDiff:
                            ExecPtrDiff(ins.Operand0);
                            break;

                        case BytecodeOp.Ldobj:
                            ExecLdobj(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Stobj:
                            ExecStobj(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Newobj:
                            if (TryDeferTypeInitializationForNewobj(mod, ctorToken: ins.Operand0, resumePc: pc, ct, limits))
                                break;
                            ExecNewobj(mod, ins.Operand0, ins.Operand1, ct, limits);
                            break;

                        case BytecodeOp.Ldfld:
                            ExecLdfld(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Ldflda:
                            ExecLdflda(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Stfld:
                            ExecStfld(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Ldsfld:
                            if (!TryDeferTypeInitializationForStaticField(mod, fieldToken: ins.Operand0, resumePc: pc, ct, limits))
                                ExecLdsfld(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Ldsflda:
                            if (!TryDeferTypeInitializationForStaticField(mod, fieldToken: ins.Operand0, resumePc: pc, ct, limits))
                                ExecLdsflda(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Stsfld:
                            if (!TryDeferTypeInitializationForStaticField(mod, fieldToken: ins.Operand0, resumePc: pc, ct, limits))
                                ExecStsfld(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Ldstr:
                            ExecLdstr(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Newarr:
                            ExecNewarr(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Ldelem:
                            ExecLdelem(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Ldelema:
                            ExecLdelema(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Stelem:
                            ExecStelem(mod, ins.Operand0);
                            break;

                        case BytecodeOp.CastClass:
                            ExecCastClass(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Isinst:
                            ExecIsinst(mod, ins.Operand0);
                            break;

                        case BytecodeOp.Box:
                            ExecBox(mod, ins.Operand0);
                            break;

                        case BytecodeOp.UnboxAny:
                            ExecUnboxAny(mod, ins.Operand0);
                            break;


                        default:
                            throw new NotSupportedException($"Opcode not supported: {ins.Op}");
                    }
                }
                catch (Exception hostEx) when (hostEx is DivideByZeroException || hostEx is OverflowException)
                {
                    if (_exceptionTranslationDepth != 0)
                        throw;

                    _exceptionTranslationDepth++;
                    try
                    {
                        if (!TryTranslateHostExceptionToVm(hostEx, out var vmEx))
                            throw;

                        ThrowException(vmEx, throwPc: pc);
                    }
                    finally
                    {
                        _exceptionTranslationDepth--;
                    }

                }
            }
        }
        private void CollectGarbage()
        {
            if (_heapObjects.Count == 0)
            {
                _freeBlocks.Clear();
                _heapPtr = _heapFloor;
                return;
            }

            var allocated = new HashSet<int>(_heapObjects);
            var markStack = new Stack<int>();

            var objRanges = new List<(int Start, int End, int ObjAbs)>(_heapObjects.Count);
            for (int i = 0; i < _heapObjects.Count; i++)
            {
                int objAbs = _heapObjects[i];
                if (!allocated.Contains(objAbs))
                    continue;

                CheckHeapAccess(objAbs, ObjectHeaderSize, writable: false);
                int flags = ReadI32(objAbs + 4);
                if ((flags & GcFlagAllocated) == 0)
                    continue;

                int typeId = ReadI32(objAbs + 0);
                var t = _rts.GetTypeById(typeId);
                int size = GetHeapObjectSize(objAbs, t);
                int end = checked(objAbs + size);
                objRanges.Add((objAbs, end, objAbs));
            }
            objRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

            void TryMarkObject(int objAbs)
            {
                if (!allocated.Contains(objAbs))
                    return; // stale/invalid ref, or already freed

                CheckHeapAccess(objAbs, ObjectHeaderSize, writable: false);

                int flags = ReadI32(objAbs + 4);
                if ((flags & GcFlagAllocated) == 0)
                    return;

                if ((flags & GcFlagMark) != 0)
                    return;

                WriteI32(objAbs + 4, flags | GcFlagMark);
                markStack.Push(objAbs);
            }
            void TryMarkObjectFromInteriorPointer(int targetAbs)
            {
                if (targetAbs == 0)
                    return;

                if (targetAbs < _heapBase || targetAbs > _heapPtr)
                    return;

                // Binary search
                int lo = 0, hi = objRanges.Count - 1, idx = -1;
                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    if (objRanges[mid].Start <= targetAbs) { idx = mid; lo = mid + 1; }
                    else { hi = mid - 1; }
                }
                if (idx < 0)
                    return;

                var r = objRanges[idx];
                if (targetAbs < r.End)
                    TryMarkObject(r.ObjAbs);
            }
            void VisitRefCell(int cellAbs)
            {
                CheckRange(cellAbs, RuntimeTypeSystem.PointerSize);

                long raw = ReadNativeInt(cellAbs);
                if (raw == 0)
                    return;

                int objAbs = checked((int)raw);
                TryMarkObject(objAbs);
            }

            // Mark roots from all active frames
            for (int frame = _frameBase; frame >= 0; frame = ReadI32(frame + 0))
            {
                int methodTok = ReadI32(frame + 16);
                int moduleId = ReadI32(frame + 20);

                if ((uint)moduleId >= (uint)_moduleById.Length)
                    throw new InvalidOperationException($"Bad moduleId in frame: {moduleId}");

                var mod = _moduleById[moduleId];
                if (!mod.MethodsByDefToken.TryGetValue(methodTok, out var fn))
                    throw new MissingMethodException($"Frame method not found: {mod.Name} 0x{methodTok:X8}");

                int runtimeMethodId = ReadI32(frame + 72);
                var rm = runtimeMethodId != 0
                    ? _rts.GetMethodById(runtimeMethodId)
                    : ResolveRuntimeMethodOrThrow(mod, methodTok);
                var frameLayout = GetOrCreateMethodLayout(mod, fn, rm);

                // Args
                int argsAbs = ReadI32(frame + 40);
                for (int i = 0; i < frameLayout.ArgTypes.Length; i++)
                {
                    var t = frameLayout.ArgTypes[i];
                    int off = frameLayout.ArgOffsets[i];
                    VisitManagedRefCellsInTypedStorage(argsAbs + off, t, VisitRefCell, TryMarkObjectFromInteriorPointer);
                }

                // Locals
                int localsAbs = ReadI32(frame + 44);
                for (int i = 0; i < frameLayout.LocalTypes.Length; i++)
                {
                    var t = frameLayout.LocalTypes[i];
                    int off = frameLayout.LocalOffsets[i];
                    VisitManagedRefCellsInTypedStorage(localsAbs + off, t, VisitRefCell, TryMarkObjectFromInteriorPointer);
                }

                // Eval stack
                int evalBase = ReadI32(frame + 28);
                int evalSp = ReadI32(frame + 32);
                for (int i = 0; i < evalSp; i++)
                {
                    var slot = ReadSlot(evalBase + i * SlotSize);

                    if (slot.Kind == SlotKind.Ref)
                    {
                        TryMarkObject(checked((int)slot.Payload));
                        continue;
                    }

                    if (slot.Kind is SlotKind.ByRef or SlotKind.Ptr)
                    {
                        TryMarkObjectFromInteriorPointer(checked((int)slot.Payload));
                        continue;
                    }

                    if (slot.Kind == SlotKind.Value)
                    {
                        var vt = _rts.GetTypeById(slot.Aux);
                        VisitManagedRefCellsInTypedStorage(checked((int)slot.Payload), vt, VisitRefCell, TryMarkObjectFromInteriorPointer);
                    }
                }
            }

            // Pending ctor temporaries
            foreach (var kv in _pendingCtorResults)
            {
                var pending = kv.Value;

                if (pending.Kind == PendingCtorResultKind.Ref)
                {
                    TryMarkObject(checked((int)pending.Payload));
                    continue;
                }

                if (pending.Kind == PendingCtorResultKind.Value && pending.Type != null)
                {
                    VisitManagedRefCellsInTypedStorage(checked((int)pending.Payload), pending.Type, VisitRefCell);
                }
            }
            // Active catch contexts
            for (int i = 0; i < _catchStack.Count; i++)
            {
                var ex = _catchStack[i].Exception;
                if (ex.Kind == SlotKind.Ref)
                {
                    TryMarkObject(checked((int)ex.Payload));
                }
                else if (ex.Kind == SlotKind.Value)
                {
                    var vt = _rts.GetTypeById(ex.Aux);
                    VisitManagedRefCellsInTypedStorage(checked((int)ex.Payload), vt, VisitRefCell);
                }
            }
            // Active finally contexts
            for (int i = 0; i < _finallyStack.Count; i++)
            {
                var ctx = _finallyStack[i];

                if (ctx.HasPendingException)
                {
                    var ex = ctx.PendingException;
                    if (ex.Kind == SlotKind.Ref)
                        TryMarkObject(checked((int)ex.Payload));
                    else if (ex.Kind == SlotKind.Value)
                    {
                        var vt = _rts.GetTypeById(ex.Aux);
                        VisitManagedRefCellsInTypedStorage(checked((int)ex.Payload), vt, VisitRefCell);
                    }
                }

                if (ctx.HasReturnValue)
                {
                    var rv = ctx.ReturnValue;
                    if (rv.Kind == SlotKind.Ref)
                        TryMarkObject(checked((int)rv.Payload));
                    else if (rv.Kind == SlotKind.Value)
                    {
                        var vt = _rts.GetTypeById(rv.Aux);
                        VisitManagedRefCellsInTypedStorage(checked((int)rv.Payload), vt, VisitRefCell);
                    }
                }
            }
            // Static field roots
            foreach (var kv in _staticBaseByTypeId)
            {
                int typeId = kv.Key;
                int staticsAbs = kv.Value;
                if (staticsAbs == 0)
                    continue;

                var type = _rts.GetTypeById(typeId);
                for (int i = 0; i < type.StaticFields.Length; i++)
                {
                    var f = type.StaticFields[i];
                    int fieldAbs = checked(staticsAbs + f.Offset);
                    VisitManagedRefCellsInTypedStorage(fieldAbs, f.FieldType, VisitRefCell);
                }
            }
            // Intern pool roots
            foreach (var kv in _internPool)
            {
                TryMarkObject(kv.Value);
            }
            // Transitively mark object graph
            while (markStack.Count != 0)
            {
                int objAbs = markStack.Pop();
                int typeId = ReadI32(objAbs + 0);
                var objType = _rts.GetTypeById(typeId);

                VisitManagedRefCellsInObject(objAbs, objType, VisitRefCell, TryMarkObjectFromInteriorPointer);
            }
            // Sweep
            var live = new List<int>(_heapObjects.Count);
            for (int i = 0; i < _heapObjects.Count; i++)
            {
                int objAbs = _heapObjects[i];

                int typeId = ReadI32(objAbs + 0);
                var t = _rts.GetTypeById(typeId);
                int size = GetHeapObjectSize(objAbs, t);

                int flags = ReadI32(objAbs + 4);
                if ((flags & GcFlagAllocated) == 0)
                    continue;

                if ((flags & GcFlagMark) != 0)
                {
                    WriteI32(objAbs + 4, (flags & ~GcFlagMark) | GcFlagAllocated);
                    live.Add(objAbs);
                }
                else
                {
                    Array.Clear(_mem, objAbs, size);
                    AddFreeBlock(objAbs, size);
                }
            }

            _heapObjects.Clear();
            _heapObjects.AddRange(live);

            DefragmentFreeListAndTrimTail();
        }
        private void DefragmentFreeListAndTrimTail()
        {
            if (_freeBlocks.Count == 0)
                return;

            _freeBlocks.Sort((a, b) => a.Abs.CompareTo(b.Abs));

            int write = 0;
            for (int read = 1; read < _freeBlocks.Count; read++)
            {
                var cur = _freeBlocks[write];
                var nxt = _freeBlocks[read];

                if (nxt.Abs < cur.End)
                    throw new InvalidOperationException("Free list overlap.");

                if (nxt.Abs == cur.End)
                {
                    _freeBlocks[write] = new FreeBlock(cur.Abs, checked(nxt.End - cur.Abs));
                }
                else
                {
                    write++;
                    if (write != read)
                        _freeBlocks[write] = nxt;
                }
            }
            if (write + 1 < _freeBlocks.Count)
                _freeBlocks.RemoveRange(write + 1, _freeBlocks.Count - (write + 1));

            // Trim top free blocks
            while (_freeBlocks.Count > 0)
            {
                int last = _freeBlocks.Count - 1;
                var top = _freeBlocks[last];

                if (top.End != _heapPtr)
                    break;
                if (top.Abs < _heapFloor)
                    break;

                _heapPtr = top.Abs;
                _freeBlocks.RemoveAt(last);
            }
            if (_heapPtr < _heapFloor)
                _heapPtr = _heapFloor;
        }
        private void AddFreeBlock(int abs, int size)
        {
            if (size <= 0)
                return;

            int newAbs = abs;
            int newEnd = checked(abs + size);

            int idx = 0;
            while (idx < _freeBlocks.Count && _freeBlocks[idx].Abs < newAbs)
                idx++;

            // Merge with previous if adjacent
            if (idx > 0)
            {
                var prev = _freeBlocks[idx - 1];
                if (prev.End > newAbs)
                    throw new InvalidOperationException("Free list overlap (prev).");

                if (prev.End == newAbs)
                {
                    newAbs = prev.Abs;
                    newEnd = Math.Max(newEnd, prev.End);
                    _freeBlocks.RemoveAt(idx - 1);
                    idx--;
                }
            }

            // Merge with next blocks while adjacent
            while (idx < _freeBlocks.Count)
            {
                var next = _freeBlocks[idx];

                if (next.Abs > newEnd)
                    break;

                if (next.Abs < newEnd)
                    throw new InvalidOperationException("Free list overlap (next).");

                // adjacent
                newEnd = Math.Max(newEnd, next.End);
                _freeBlocks.RemoveAt(idx);
            }

            _freeBlocks.Insert(idx, new FreeBlock(newAbs, checked(newEnd - newAbs)));
        }
        private void RecomputeGcThresholds()
        {
            int heapSize = _heapEnd - _heapBase;
            _allocBudgetBytes = Math.Max(128, heapSize / 8);
            _minTailFreeBytes = Math.Max(128, heapSize / 10);
        }
        private void MaybeCollectGarbage(bool force)
        {
            if (_gcRunning) return;

            if (!force)
            {
                if (!_gcRequested) return;

                int tailFree = _heapEnd - _heapPtr;
                if (_allocDebtBytes < _allocBudgetBytes && tailFree >= _minTailFreeBytes)
                    return;
            }

            _gcRunning = true;
            SpillCurrentFrameHotState();
            CollectGarbage();
            _allocDebtBytes = 0;
            _gcRequested = false;
            RecomputeGcThresholds();
            // no try finally for better inlining, since GC failure is fatal
            _gcRunning = false;
        }
        private void MarkCurrentFrameHeapAllocated()
        {
            if (_frameBase >= 0)
                _heapAllocFrames.Add(_frameBase);
        }
        private void OnHeapAllocated(int bytes)
        {
            MarkCurrentFrameHeapAllocated();
            if (bytes > 0) _allocDebtBytes += bytes;

            int tailFree = _heapEnd - _heapPtr;
            if (_allocDebtBytes >= _allocBudgetBytes || tailFree < _minTailFreeBytes)
                _gcRequested = true;
        }
        private void VisitManagedRefCellsInTypedStorage(
            int abs, RuntimeType t, Action<int> callback, Action<int>? interiorPointerCallback = null)
        {
            const int MaxNodes = 2_000_000;

            var work = new Stack<(int Abs, RuntimeType Type)>();
            var visited = new HashSet<ulong>(); // key = (abs << 32) | typeId

            work.Push((abs, t));

            int nodes = 0;
            while (work.Count != 0)
            {
                if (++nodes > MaxNodes)
                    throw new InvalidOperationException(
                        "GC typed walk exceeded safety budget.");

                var (curAbs, curType) = work.Pop();

                // Managed reference storage
                if (curType.IsReferenceType)
                {
                    callback(curAbs);
                    continue;
                }

                // Unmanaged / not GC tracked
                if (curType.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                {
                    if (interiorPointerCallback != null)
                    {
                        long raw = ReadNativeInt(curAbs);
                        if (raw != 0)
                            interiorPointerCallback(checked((int)raw));
                    }
                    continue;
                }

                if (!curType.IsValueType)
                    continue;

                // Scalars never contain managed refs
                if (IsEvalScalarValueType(curType) || (curType.Namespace == "System" && curType.Name == "Decimal"))
                    continue;

                // Stop cycles even for malformed by value type graphs
                ulong key = ((ulong)(uint)curAbs << 32) | (uint)curType.TypeId;
                if (!visited.Add(key))
                    continue;

                var fields = curType.InstanceFields;
                for (int i = fields.Length - 1; i >= 0; i--)
                {
                    var f = fields[i];
                    int fieldAbs = checked(curAbs + f.Offset);
                    work.Push((fieldAbs, f.FieldType));
                }
            }
        }
        private bool IsSystemStringType(RuntimeType t)
            => t.Namespace == "System" && t.Name == "String";
        private bool IsSystemBooleanType(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Boolean";
        private int GetStringLengthFromObject(int strObjAbs)
        {
            CheckHeapAccess(strObjAbs, ObjectHeaderSize, writable: false);
            int len = ReadI32(strObjAbs + StringLengthOffset);
            if (len < 0) throw new InvalidOperationException("Corrupted string length.");
            return len;
        }

        private int GetStringCharsAbs(int strObjAbs) => checked(strObjAbs + StringCharsOffset);

        private int GetArrayLengthFromObject(int arrObjAbs)
        {
            CheckHeapAccess(arrObjAbs, ObjectHeaderSize, writable: false);
            int len = ReadI32(arrObjAbs + ArrayLengthOffset);
            if (len < 0) throw new InvalidOperationException("Corrupted array length.");
            return len;
        }
        private void VisitManagedRefCellsInObject(
            int objAbs, RuntimeType objType, Action<int> callback, Action<int>? interiorPointerCallback = null)
        {
            if (objType.IsValueType)
            {
                int payloadAbs = checked(objAbs + ObjectHeaderSize);
                VisitManagedRefCellsInTypedStorage(payloadAbs, objType, callback);
                return;
            }

            if (!objType.IsReferenceType)
                throw new InvalidOperationException($"Expected object or boxed value, got {objType.Name}");

            if (objType.Kind == RuntimeTypeKind.Array)
            {
                VisitManagedRefCellsInArray(objAbs, objType, callback);
                return;
            }

            if (IsSystemStringType(objType))
                return;

            var seen = new HashSet<int>();
            for (RuntimeType? t = objType; t != null && t.IsReferenceType; t = t.BaseType)
            {
                if (!seen.Add(t.TypeId))
                    return;

                for (int i = 0; i < t.InstanceFields.Length; i++)
                {
                    var f = t.InstanceFields[i];
                    int fieldAbs = checked(objAbs + f.Offset);
                    VisitManagedRefCellsInTypedStorage(fieldAbs, f.FieldType, callback, interiorPointerCallback);
                }
            }
        }
        private int AllocArrayObject(RuntimeType arrayType, int length)
        {
            if (arrayType.Kind != RuntimeTypeKind.Array)
                throw new InvalidOperationException($"Type '{arrayType.Name}' is not an array.");

            if (arrayType.ElementType is null)
                throw new InvalidOperationException("Array type has no element type.");

            if (length < 0)
                throw new InvalidOperationException("Negative array length.");

            var (elemSize, _) = GetStorageSizeAlign(arrayType.ElementType);
            int payloadBytes = checked(length * elemSize);
            int totalSize = AlignUp(checked(ArrayDataOffset + payloadBytes), 8);

            int abs = AllocHeapBytes(totalSize, align: 8);
            Array.Clear(_mem, abs, totalSize);

            WriteI32(abs + 0, arrayType.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            WriteI32(abs + ArrayLengthOffset, length);

            _heapObjects.Add(abs);
            return abs;
        }

        private int AllocStringUninitialized(int length)
        {
            if (length < 0)
                throw new InvalidOperationException("Negative string length.");

            var stringType = _rts.SystemString;
            int totalSize = AlignUp(checked(StringCharsOffset + (length * 2) + 2), 8);

            int abs = AllocHeapBytes(totalSize, align: 8);
            Array.Clear(_mem, abs, totalSize);

            WriteI32(abs + 0, stringType.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            WriteI32(abs + StringLengthOffset, length);

            // null terminator
            BinaryPrimitives.WriteUInt16LittleEndian(_mem.AsSpan(abs + StringCharsOffset + length * 2, 2), 0);

            _heapObjects.Add(abs);
            return abs;
        }
        private int GetHeapObjectSize(int objAbs, RuntimeType t)
        {
            if (t.Kind == RuntimeTypeKind.Array)
            {
                if (t.ElementType is null)
                    throw new InvalidOperationException("Array type without element type.");

                int len = GetArrayLengthFromObject(objAbs);
                var (elemSize, _) = GetStorageSizeAlign(t.ElementType);

                int bytes = checked(len * elemSize);
                return AlignUp(checked(ArrayDataOffset + bytes), 8);
            }

            if (IsSystemStringType(t))
            {
                int len = GetStringLengthFromObject(objAbs);
                int bytes = checked(len * 2); // UTF-16 char
                return AlignUp(checked(StringCharsOffset + bytes + 2), 8); // + null terminator
            }

            if (t.IsValueType)
                return AlignUp(checked(ObjectHeaderSize + t.SizeOf), 8);

            // Normal fixed size object
            return t.InstanceSize;
        }
        private int AllocStringFromManaged(string s)
        {
            if (s is null) throw new ArgumentNullException(nameof(s));

            int abs = AllocStringUninitialized(s.Length);
            int charsAbs = GetStringCharsAbs(abs);

            for (int i = 0; i < s.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(_mem.AsSpan(charsAbs + i * 2, 2), s[i]);
            }

            return abs;
        }
        private void ValidateStringRef(Slot s, out int strObjAbs)
        {
            if (s.Kind == SlotKind.Null)
                throw new NullReferenceException();

            if (s.Kind != SlotKind.Ref)
                throw new InvalidOperationException($"Expected string ref, got {s.Kind}");

            strObjAbs = checked((int)s.Payload);
            var t = GetObjectTypeFromRef(s);
            if (!IsSystemStringType(t))
                throw new InvalidOperationException($"Expected System.String, got {t.Namespace}.{t.Name}");
        }
        private void ValidateArrayRefExact(
    Slot s,
    RuntimeType expectedElemType,
    out int arrObjAbs,
    out int length,
    out RuntimeType actualElemType)
        {
            if (s.Kind == SlotKind.Null)
                throw new NullReferenceException();

            if (s.Kind != SlotKind.Ref)
                throw new InvalidOperationException($"Expected array ref, got {s.Kind}");

            arrObjAbs = checked((int)s.Payload);

            var actualType = GetObjectTypeFromRef(s);
            if (actualType.Kind != RuntimeTypeKind.Array || actualType.ElementType is null)
                throw new InvalidOperationException($"Expected array, got {actualType.Namespace}.{actualType.Name}");

            actualElemType = actualType.ElementType;

            if (actualElemType.TypeId != expectedElemType.TypeId)
                throw new ArrayTypeMismatchException(
                    $"Array element type mismatch: actual={actualElemType.Namespace}.{actualElemType.Name}, " +
                    $"expected={expectedElemType.Namespace}.{expectedElemType.Name}");

            length = GetArrayLengthFromObject(arrObjAbs);
        }

        private void ValidateArrayRefReadable(
            Slot s,
            RuntimeType expectedElemType,
            out int arrObjAbs,
            out int length,
            out RuntimeType actualElemType)
        {
            if (s.Kind == SlotKind.Null)
                throw new NullReferenceException();

            if (s.Kind != SlotKind.Ref)
                throw new InvalidOperationException($"Expected array ref, got {s.Kind}");

            arrObjAbs = checked((int)s.Payload);

            var actualType = GetObjectTypeFromRef(s);
            if (actualType.Kind != RuntimeTypeKind.Array || actualType.ElementType is null)
                throw new InvalidOperationException($"Expected array, got {actualType.Namespace}.{actualType.Name}");

            actualElemType = actualType.ElementType;

            bool ok =
                actualElemType.TypeId == expectedElemType.TypeId ||
                (actualElemType.IsReferenceType &&
                 expectedElemType.IsReferenceType &&
                 IsAssignableTo(actualElemType, expectedElemType));

            if (!ok)
                throw new ArrayTypeMismatchException(
                    $"Array element type mismatch: actual={actualElemType.Namespace}.{actualElemType.Name}, " +
                    $"expected={expectedElemType.Namespace}.{expectedElemType.Name}");

            length = GetArrayLengthFromObject(arrObjAbs);
        }

        private void VisitManagedRefCellsInArray(
            int objAbs, RuntimeType arrayType, Action<int> callback, Action<int>? interiorPointerCallback = null)
        {
            if (arrayType.ElementType is null)
                throw new InvalidOperationException("Array type has no element type.");

            int length = GetArrayLengthFromObject(objAbs);
            var (elemSize, _) = GetStorageSizeAlign(arrayType.ElementType);
            int dataAbs = checked(objAbs + ArrayDataOffset);

            for (int i = 0; i < length; i++)
            {
                int elemAbs = checked(dataAbs + checked(i * elemSize));
                VisitManagedRefCellsInTypedStorage(elemAbs, arrayType.ElementType, callback, interiorPointerCallback);
            }
        }
        private int AllocHeapBytes(int bytes, int align)
        {
            if (bytes < 0) throw new InvalidOperationException("Negative allocation size.");
            if (align <= 0) align = 1;

            if (TryAllocFromFreeList(bytes, align, out int reusedAbs))
            {
                OnHeapAllocated(bytes);
                return reusedAbs;
            }

            int abs = AlignUp(_heapPtr, align);
            int end = checked(abs + bytes);

            if (end > _heapEnd)
                throw new OutOfMemoryException("Out of memory.");

            _heapPtr = end;
            if (_heapPtr > _heapPeakAbs) _heapPeakAbs = _heapPtr;

            OnHeapAllocated(bytes);
            return abs;
        }
        private bool TryAllocFromFreeList(int bytes, int align, out int abs)
        {
            int bestIndex = -1;
            int bestAligned = 0;
            int bestBlockSize = int.MaxValue;

            for (int i = 0; i < _freeBlocks.Count; i++)
            {
                var b = _freeBlocks[i];
                int aligned = AlignUp(b.Abs, align);
                int pad = aligned - b.Abs;

                long need = (long)pad + bytes;
                if (need > b.Size)
                    continue;

                if (b.Size < bestBlockSize)
                {
                    bestBlockSize = b.Size;
                    bestIndex = i;
                    bestAligned = aligned;
                }
            }

            if (bestIndex < 0)
            {
                abs = 0;
                return false;
            }

            var block = _freeBlocks[bestIndex];
            int prefixSize = bestAligned - block.Abs;
            int used = checked(prefixSize + bytes);
            int suffixAbs = checked(bestAligned + bytes);
            int suffixSize = block.Size - used;

            _freeBlocks.RemoveAt(bestIndex);

            if (prefixSize > 0)
            {
                _freeBlocks.Insert(bestIndex, new FreeBlock(block.Abs, prefixSize));
                bestIndex++;
            }

            if (suffixSize > 0)
            {
                _freeBlocks.Insert(bestIndex, new FreeBlock(suffixAbs, suffixSize));
            }

            abs = bestAligned;
            return true;
        }
        private int AllocObject(RuntimeType t)
        {
            if (!t.IsReferenceType)
                throw new InvalidOperationException($"Cannot heap-allocate value type '{t.Namespace}.{t.Name}' as object.");

            if (t.Kind == RuntimeTypeKind.Array)
                throw new InvalidOperationException("Use AllocArrayObject for arrays.");

            if (IsSystemStringType(t))
                throw new InvalidOperationException("Use string allocation helpers for System.String.");

            int size = t.InstanceSize;
            if (size < ObjectHeaderSize)
                throw new InvalidOperationException("Bad instance size.");

            int abs = AllocHeapBytes(size, align: Math.Max(8, t.AlignOf));

            Array.Clear(_mem, abs, size);


            // header
            // +0 int32 typeId
            // +4 int32 gc flags
            WriteI32(abs + 0, t.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            _heapObjects.Add(abs);
            return abs;
        }

        private RuntimeType GetObjectTypeFromRef(Slot s)
        {
            if (s.Kind == SlotKind.Null)
                throw new NullReferenceException();

            if (s.Kind != SlotKind.Ref)
                throw new InvalidOperationException($"Expected object ref, got {s.Kind}");

            int objAbs = checked((int)s.Payload);
            CheckHeapAccess(objAbs, ObjectHeaderSize, writable: false);

            int flags = ReadI32(objAbs + 4);
            if ((flags & GcFlagAllocated) == 0)
                throw new InvalidOperationException("Dangling object reference.");

            int typeId = ReadI32(objAbs + 0);
            return _rts.GetTypeById(typeId);
        }

        private bool IsAssignableTo(RuntimeType actual, RuntimeType target)
        {
            if (ReferenceEquals(actual, target))
                return true;

            if (actual.Kind == RuntimeTypeKind.Array &&
                target.Kind == RuntimeTypeKind.Array)
            {
                if (actual.ArrayRank == target.ArrayRank &&
                    actual.ElementType is RuntimeType actualElem &&
                    target.ElementType is RuntimeType targetElem &&
                    actualElem.IsReferenceType &&
                    targetElem.IsReferenceType &&
                    IsAssignableTo(actualElem, targetElem))
                {
                    return true;
                }
            }

            for (var t = actual.BaseType; t != null; t = t.BaseType)
            {
                if (ReferenceEquals(t, target))
                    return true;
            }

            return false;
        }

        private void CheckHeapAccess(int abs, int size, bool writable)
        {
            if (writable) CheckWritableRange(abs, size);
            else CheckRange(abs, size);

            if (abs < _heapBase || abs + size > _heapPtr)
                throw new InvalidOperationException("Invalid heap access.");
        }
        private static bool IsEvalScalarValueType(RuntimeType t)
        {
            if (!t.IsValueType) return false;

            if (t.Kind == RuntimeTypeKind.Enum)
                return true;

            if (t.Namespace != "System")
                return false;

            return t.Name switch
            {
                "Boolean" => true,
                "Char" => true,
                "SByte" => true,
                "Byte" => true,
                "Int16" => true,
                "UInt16" => true,
                "Int32" => true,
                "UInt32" => true,
                "Int64" => true,
                "UInt64" => true,
                "IntPtr" => true,
                "UIntPtr" => true,
                "Single" => true,
                "Double" => true,
                _ => false
            };
        }
        private static bool UsesBlobOnEvalStack(RuntimeType t)
            => t.IsValueType && !IsEvalScalarValueType(t);
        private int ComputeEvalBlobCapacity(RuntimeModule module, BytecodeFunction fn, RuntimeMethod rm)
        {
            int maxBlobSize = 0;

            void Consider(RuntimeType? t)
            {
                if (t is null) return;
                if (!UsesBlobOnEvalStack(t)) return;

                var (sz, _) = GetStorageSizeAlign(t);
                if (sz > maxBlobSize)
                    maxBlobSize = sz;
            }

            // Method signature / locals
            Consider(rm.ReturnType);

            int argCount = rm.HasThis ? 1 + rm.ParameterTypes.Length : rm.ParameterTypes.Length;
            for (int i = 0; i < argCount; i++)
                Consider(GetArgType(rm, i));

            for (int i = 0; i < fn.LocalTypeTokens.Length; i++)
                Consider(_rts.ResolveTypeInMethodContext(module, fn.LocalTypeTokens[i], rm));

            // Instruction operands
            foreach (var ins in fn.Instructions)
            {
                switch (ins.Op)
                {
                    case BytecodeOp.Ldfld:
                    case BytecodeOp.Stfld:
                    case BytecodeOp.Ldsfld:
                    case BytecodeOp.Stsfld:
                        {
                            var f = _rts.ResolveField(module, ins.Operand0);
                            Consider(f.FieldType);
                            break;
                        }

                    case BytecodeOp.Newobj:
                        {
                            var (tmOpt, tfn) = _domain.ResolveCall(module, ins.Operand0);
                            var tm = tmOpt ?? module;
                            var ctor = ResolveRuntimeMethodOrThrow(tm, tfn.MethodToken);
                            Consider(ctor.DeclaringType);
                            break;
                        }

                    case BytecodeOp.Call:
                    case BytecodeOp.CallVirt:
                        {
                            var (tmOpt, tfn) = _domain.ResolveCall(module, ins.Operand0);
                            var tm = tmOpt ?? module;
                            var callee = ResolveRuntimeMethodOrThrow(tm, tfn.MethodToken);
                            Consider(callee.ReturnType);
                            break;
                        }

                    case BytecodeOp.UnboxAny:
                    case BytecodeOp.Ldelem:
                    case BytecodeOp.Stelem:
                    case BytecodeOp.Newarr:
                        {
                            var t = _rts.ResolveTypeInMethodContext(module, ins.Operand0, rm);
                            Consider(t);
                            break;
                        }
                }
            }

            if (maxBlobSize == 0 || fn.MaxStack <= 0)
                return 0;

            return checked(fn.MaxStack * maxBlobSize);
        }
        private void SyncEvalBlobSpToEvalStack()
        {
            if (_frameBase < 0)
                return;

            int blobBase = ReadI32(_frameBase + 48);
            int spSlots = _curEvalSp;

            int newBlobSp = 0;

            for (int i = spSlots - 1; i >= 0; i--)
            {
                var s = _hotEvalSlots[i];
                if (s.Kind != SlotKind.Value)
                    continue;

                int typeId = s.Aux;
                int addr = checked((int)s.Payload);

                int size = _rts.GetTypeById(typeId).SizeOf;
                newBlobSp = checked((addr + size) - blobBase);
                break;
            }

            WriteI32(_frameBase + 52, newBlobSp);
        }
        private int AllocEvalBlobBytes(int bytes)
        {
            if (bytes < 0)
                throw new InvalidOperationException("Negative eval blob size.");

            int blobBase = ReadI32(_frameBase + 48);
            int blobSp = ReadI32(_frameBase + 52);
            int blobCap = ReadI32(_frameBase + 56);

            int dst = checked(blobBase + blobSp);
            int newSp = checked(blobSp + bytes);

            if (newSp > blobCap)
                throw new InvalidOperationException(
                    $"Eval blob arena overflow. Need {bytes} bytes, used {blobSp}/{blobCap}.");

            WriteI32(_frameBase + 52, newSp);
            return dst;
        }
        private void ExecSizeof(RuntimeModule mod, int typeToken)
        {
            var rm = _curLayout?.Method ?? throw new InvalidOperationException("No current method layout.");
            var t = _rts.ResolveTypeInMethodContext(mod, typeToken, rm);
            if (t.Kind == RuntimeTypeKind.TypeParam)
                throw new InvalidOperationException($"Unbound generic type parameter in sizeof: {t.Name}");
            int size = _rts.GetStorageSizeAlign(t).size;
            PushSlot(new Slot(SlotKind.I4, size));
        }
        private void ExecDefaultValue(RuntimeModule mod, int typeToken)
        {
            var t = ResolveTypeTokenInCurrentMethod(mod, typeToken);

            // Reference types default to null
            if (t.IsReferenceType)
            {
                PushSlot(new Slot(SlotKind.Null, 0));
                return;
            }

            // Pointers and byrefs default to null ptr
            if (t.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
            {
                PushSlot(new Slot(SlotKind.Null, 0));
                return;
            }

            if (!t.IsValueType)
                throw new InvalidOperationException($"DefaultValue for '{t.Namespace}.{t.Name}' is not supported.");

            PushDefaultValue(t);
        }
        private RuntimeType ResolveTypeTokenInCurrentMethod(RuntimeModule mod, int typeToken)
        {
            var rm = _curLayout?.Method;
            var t = _rts.ResolveTypeInMethodContext(mod, typeToken, rm);
            if (t.Kind == RuntimeTypeKind.TypeParam)
                throw new InvalidOperationException($"Unbound generic type parameter in runtime operation: {t.Name}");
            return t;
        }
        private static bool IsNullableTypeDefinitionName(string name)
            => name.StartsWith("Nullable", StringComparison.Ordinal);
        private int AllocBoxedValueObject(RuntimeType valueType)
        {
            if (!valueType.IsValueType)
                throw new InvalidOperationException($"Cannot box non-value type '{valueType.Namespace}.{valueType.Name}'.");

            int totalSize = AlignUp(checked(ObjectHeaderSize + valueType.SizeOf), 8);
            int abs = AllocHeapBytes(totalSize, align: 8);
            Array.Clear(_mem, abs, totalSize);

            WriteI32(abs + 0, valueType.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            _heapObjects.Add(abs);
            return abs;
        }

        private int GetBoxedValuePayloadAbs(int objAbs)
            => checked(objAbs + ObjectHeaderSize);

        private void PushValueCopyFromStorage(int srcAbs, RuntimeType valueType)
        {
            if (!valueType.IsValueType)
                throw new InvalidOperationException($"Expected value type, got '{valueType.Namespace}.{valueType.Name}'.");

            int size = valueType.SizeOf;
            CheckRange(srcAbs, size);

            if (!UsesBlobOnEvalStack(valueType))
            {
                PushSlot(LoadValueAsSlot(srcAbs, 0, valueType));
                return;
            }

            int dstAbs = AllocEvalBlobBytes(size);
            Buffer.BlockCopy(_mem, srcAbs, _mem, dstAbs, size);
            PushSlot(new Slot(SlotKind.Value, dstAbs, valueType.TypeId));
        }

        private void PushDefaultValue(RuntimeType valueType)
        {
            if (!valueType.IsValueType)
                throw new InvalidOperationException($"Expected value type, got '{valueType.Namespace}.{valueType.Name}'.");

            int size = valueType.SizeOf;
            if (!UsesBlobOnEvalStack(valueType))
            {
                int tmpAbs = AllocFrameScratch(size, Math.Max(1, valueType.AlignOf));
                Array.Clear(_mem, tmpAbs, size);
                PushSlot(LoadValueAsSlot(tmpAbs, 0, valueType));
                return;
            }

            int dstAbs = AllocEvalBlobBytes(size);
            Array.Clear(_mem, dstAbs, size);
            PushSlot(new Slot(SlotKind.Value, dstAbs, valueType.TypeId));
        }
        private void ExecIsinst(RuntimeModule mod, int typeToken)
        {
            var targetType = ResolveTypeTokenInCurrentMethod(mod, typeToken);
            var value = PopSlot();

            if (value.Kind == SlotKind.Null)
            {
                PushSlot(value);
                return;
            }

            if (value.Kind != SlotKind.Ref)
                throw new InvalidCastException($"isinst expects object reference, got {value.Kind}.");

            var actualType = GetObjectTypeFromRef(value);
            if (IsAssignableTo(actualType, targetType))
                PushSlot(value);
            else
                PushSlot(new Slot(SlotKind.Null, 0));
        }
        private void ExecCastClass(RuntimeModule mod, int typeToken)
        {
            var targetType = ResolveTypeTokenInCurrentMethod(mod, typeToken);
            var value = PopSlot();

            if (value.Kind == SlotKind.Null)
            {
                PushSlot(value);
                return;
            }

            if (value.Kind != SlotKind.Ref)
                throw new InvalidCastException($"castclass expects object reference, got {value.Kind}.");

            if (!targetType.IsReferenceType)
                throw new InvalidCastException($"castclass target must be a reference type: {targetType.Namespace}.{targetType.Name}");

            var actualType = GetObjectTypeFromRef(value);
            if (!IsAssignableTo(actualType, targetType))
                throw new InvalidCastException($"Cannot cast '{actualType.Namespace}.{actualType.Name}' " +
                    $"to '{targetType.Namespace}.{targetType.Name}'.");

            PushSlot(value);
        }
        private void ExecBox(RuntimeModule mod, int typeToken)
        {
            var valueType = ResolveTypeTokenInCurrentMethod(mod, typeToken);
            var value = PopSlot();

            if (!valueType.IsValueType)
                throw new InvalidOperationException($"box expects value type, got '{valueType.Namespace}.{valueType.Name}'.");

            // C# nullable boxing semantics
            if (TryGetNullableInfo(valueType, out var nullableUnderlying, out var hasValueField, out var nullableValueField))
            {
                if (value.Kind != SlotKind.Value)
                    throw new InvalidOperationException($"Nullable boxing expects struct value slot, got {value.Kind}.");

                var slotType = GetValueSlotType(value);
                if (slotType.TypeId != valueType.TypeId)
                    throw new InvalidOperationException($"Nullable boxing type mismatch: slot={slotType.Name}, token={valueType.Name}.");

                int srcAbs = checked((int)value.Payload);

                var hasValueSlot = LoadValueAsSlot(srcAbs, hasValueField.Offset, hasValueField.FieldType);
                bool hasValue = hasValueSlot.Kind == SlotKind.I4 && hasValueSlot.AsI4Checked() != 0;
                if (!hasValue)
                {
                    PushSlot(new Slot(SlotKind.Null, 0));
                    return;
                }

                var underlyingSlot = LoadValueAsSlot(srcAbs, nullableValueField.Offset, nullableUnderlying);
                int boxedUnderlyingAbs = AllocBoxedValueObject(nullableUnderlying);
                int boxedUnderlyingPayloadAbs = GetBoxedValuePayloadAbs(boxedUnderlyingAbs);
                StoreSlotAsValue(boxedUnderlyingPayloadAbs, 0, nullableUnderlying, underlyingSlot);
                PushSlot(new Slot(SlotKind.Ref, boxedUnderlyingAbs));
                return;
            }

            int boxedAbs = AllocBoxedValueObject(valueType);
            int payloadAbs = GetBoxedValuePayloadAbs(boxedAbs);
            StoreSlotAsValue(payloadAbs, 0, valueType, value);
            PushSlot(new Slot(SlotKind.Ref, boxedAbs));
        }
        private void ExecUnboxAny(RuntimeModule mod, int typeToken)
        {
            var targetType = ResolveTypeTokenInCurrentMethod(mod, typeToken);
            var boxed = PopSlot();

            if (!targetType.IsValueType)
            {
                if (boxed.Kind == SlotKind.Null)
                {
                    PushSlot(boxed);
                    return;
                }

                if (boxed.Kind != SlotKind.Ref)
                    throw new InvalidCastException($"unbox.any expects object reference, got {boxed.Kind}.");

                var actualRefType = GetObjectTypeFromRef(boxed);
                if (!IsAssignableTo(actualRefType, targetType))
                    throw new InvalidCastException($"Cannot cast '{actualRefType.Namespace}.{actualRefType.Name}'" +
                        $" to '{targetType.Namespace}.{targetType.Name}'.");

                PushSlot(boxed);
                return;
            }

            if (TryGetNullableInfo(targetType, out var nullableUnderlying, out var hasValueField, out var nullableValueField))
            {
                if (boxed.Kind == SlotKind.Null)
                {
                    PushDefaultValue(targetType);
                    return;
                }

                if (boxed.Kind != SlotKind.Ref)
                    throw new InvalidCastException($"unbox.any expects object reference, got {boxed.Kind}.");

                int objAbs = checked((int)boxed.Payload);
                var actualType = GetObjectTypeFromRef(boxed);

                if (actualType.TypeId == targetType.TypeId)
                {
                    int srcPayloadAbs = GetBoxedValuePayloadAbs(objAbs);
                    PushValueCopyFromStorage(srcPayloadAbs, targetType);
                    return;
                }

                if (actualType.TypeId != nullableUnderlying.TypeId)
                    throw new InvalidCastException($"Cannot unbox '{actualType.Namespace}.{actualType.Name}'" +
                        $" as '{targetType.Namespace}.{targetType.Name}'.");

                int dstAbs = AllocEvalBlobBytes(targetType.SizeOf);
                Array.Clear(_mem, dstAbs, targetType.SizeOf);

                StoreSlotAsValue(dstAbs, hasValueField.Offset, hasValueField.FieldType, new Slot(SlotKind.I4, 1));

                int srcUnderlyingAbs = GetBoxedValuePayloadAbs(objAbs);
                var underlyingSlot = LoadValueAsSlot(srcUnderlyingAbs, 0, nullableUnderlying);
                StoreSlotAsValue(dstAbs, nullableValueField.Offset, nullableUnderlying, underlyingSlot);

                PushSlot(new Slot(SlotKind.Value, dstAbs, targetType.TypeId));
                return;
            }

            if (boxed.Kind == SlotKind.Null)
                throw new NullReferenceException();

            if (boxed.Kind != SlotKind.Ref)
                throw new InvalidCastException($"unbox.any expects object reference, got {boxed.Kind}.");

            int boxedObjAbs = checked((int)boxed.Payload);
            var actualBoxedType = GetObjectTypeFromRef(boxed);
            if (actualBoxedType.TypeId != targetType.TypeId)
                throw new InvalidCastException($"Cannot unbox '{actualBoxedType.Namespace}.{actualBoxedType.Name}'" +
                    $" as '{targetType.Namespace}.{targetType.Name}'.");

            int payloadAbs = GetBoxedValuePayloadAbs(boxedObjAbs);
            PushValueCopyFromStorage(payloadAbs, targetType);
        }
        private bool TryGetNullableInfo(RuntimeType t, out RuntimeType underlying, out RuntimeField hasValueField, out RuntimeField valueField)
        {
            underlying = null!;
            hasValueField = null!;
            valueField = null!;

            if (!t.IsValueType)
                return false;

            RuntimeType def = t.GenericTypeDefinition ?? t;
            if (def.Namespace != "System" || !IsNullableTypeDefinitionName(def.Name))
                return false;

            if (t.GenericTypeArguments.Length != 1)
                return false;

            underlying = t.GenericTypeArguments[0];

            RuntimeField? hv = null;
            RuntimeField? vv = null;
            for (int i = 0; i < t.InstanceFields.Length; i++)
            {
                var f = t.InstanceFields[i];
                if (f.Name == "hasValue")
                    hv = f;
                else if (f.Name == "value")
                    vv = f;
            }

            if (hv is null || vv is null)
                return false;
            if (!IsSystemBooleanType(hv.FieldType))
                return false;
            if (vv.FieldType.TypeId != underlying.TypeId)
                return false;

            hasValueField = hv;
            valueField = vv;
            return true;
        }
        private void ExecLdstr(RuntimeModule mod, int userStringToken)
        {
            if (MetadataToken.Table(userStringToken) != MetadataToken.UserString)
                throw new InvalidOperationException($"Ldstr expects UserString token, got 0x{userStringToken:X8}");

            int rid = MetadataToken.Rid(userStringToken);
            string s = mod.Md.GetUserString(rid);

            if (!_internPool.TryGetValue(s, out int objAbs))
            {
                objAbs = AllocStringFromManaged(s);
                _internPool.Add(s, objAbs);
            }

            PushSlot(new Slot(SlotKind.Ref, objAbs));
        }
        private void ExecNewarr(RuntimeModule mod, int elemTypeToken)
        {
            int len = PopSlot().AsI4Checked();
            if (len < 0)
                throw new InvalidOperationException("Negative array length.");

            var elemType = ResolveTypeTokenInCurrentMethod(mod, elemTypeToken);
            var arrayType = _rts.GetArrayType(elemType);

            int arrAbs = AllocArrayObject(arrayType, len);
            PushSlot(new Slot(SlotKind.Ref, arrAbs));
        }

        private void ExecLdelema(RuntimeModule mod, int elemTypeToken)
        {
            int index = PopSlot().AsI4Checked();
            var arr = PopSlot();

            var elemType = ResolveTypeTokenInCurrentMethod(mod, elemTypeToken);
            ValidateArrayRefExact(arr, elemType, out int arrAbs, out int length, out var actualElemType);

            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();

            var (elemSize, _) = GetStorageSizeAlign(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));

            CheckHeapAccess(elemAbs, elemSize, writable: false);
            PushSlot(new Slot(SlotKind.ByRef, elemAbs, aux: elemSize));
        }
        private void ExecLdelem(RuntimeModule mod, int elemTypeToken)
        {
            int index = PopSlot().AsI4Checked();
            var arr = PopSlot();

            var elemType = ResolveTypeTokenInCurrentMethod(mod, elemTypeToken);
            ValidateArrayRefReadable(arr, elemType, out int arrAbs, out int length, out var actualElemType);

            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();

            var (elemSize, _) = GetStorageSizeAlign(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));

            CheckHeapAccess(elemAbs, elemSize, writable: false);
            var v = LoadValueAsSlot(elemAbs, 0, elemType);
            PushSlot(v);
        }
        private void ExecStelem(RuntimeModule mod, int elemTypeToken)
        {
            var value = PopSlot();
            int index = PopSlot().AsI4Checked();
            var arr = PopSlot();

            var elemType = ResolveTypeTokenInCurrentMethod(mod, elemTypeToken);
            ValidateArrayRefReadable(arr, elemType, out int arrAbs, out int length, out var actualElemType);

            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();

            var (elemSize, _) = GetStorageSizeAlign(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));

            CheckHeapAccess(elemAbs, elemSize, writable: true);
            StoreSlotAsValue(elemAbs, 0, elemType, value);
        }
        private void ExecPtrToByRef()
        {
            var p = PopSlot();

            if (p.Kind == SlotKind.Null)
            {
                PushSlot(p);
                return;
            }

            if (p.Kind == SlotKind.ByRef)
            {
                PushSlot(p);
                return;
            }

            if (p.Kind != SlotKind.Ptr)
                throw new InvalidOperationException($"PtrToByRef expects ptr, got {p.Kind}.");

            PushSlot(new Slot(SlotKind.ByRef, p.Payload, p.Aux));
        }
        private void ExecLdArrayDataRef()
        {
            var arr = PopSlot();
            if (arr.Kind == SlotKind.Null)
                throw new NullReferenceException();
            if (arr.Kind != SlotKind.Ref)
                throw new InvalidOperationException($"LdArrayDataRef expects array ref, got {arr.Kind}.");

            var actualType = GetObjectTypeFromRef(arr);
            if (actualType.Kind != RuntimeTypeKind.Array)
                throw new InvalidOperationException($"LdArrayDataRef expects array instance, got '{actualType.Namespace}.{actualType.Name}'.");

            int arrAbs = checked((int)arr.Payload);
            int dataAbs = checked(arrAbs + ArrayDataOffset);

            PushSlot(new Slot(SlotKind.ByRef, dataAbs));
        }
        private void ExecStackAlloc(int elemSize)
        {
            if (elemSize <= 0) throw new InvalidOperationException("Bad element size.");

            int count = PopSlot().AsI4Checked();
            if (count < 0) throw new InvalidOperationException("Negative stackalloc size.");

            int bytes = checked(count * elemSize);
            int align = AlignForSize(elemSize);

            int baseAbs = ReadI32(_frameBase + 60);
            int spBytes = ReadI32(_frameBase + 64);

            int curAbs = baseAbs + spBytes;
            int alignedAbs = AlignUp(curAbs, align);

            int newSpBytes = checked((alignedAbs - baseAbs) + bytes);
            int newEndAbs = checked(baseAbs + newSpBytes);

            if (newEndAbs > _stackEnd)
                throw new InvalidOperationException("Stack overflow (stackalloc).");

            WriteI32(_frameBase + 64, newSpBytes);

            int oldFrameEnd = ReadI32(_frameBase + 68);
            if (newEndAbs > oldFrameEnd)
            {
                WriteI32(_frameBase + 68, newEndAbs);
                _sp = newEndAbs;
                if (_sp > _stackPeakAbs) _stackPeakAbs = _sp;
            }

            PushSlot(new Slot(SlotKind.Ptr, alignedAbs, aux: elemSize));
        }
        private void ExecPtrElemAddr(int elemSize)
        {
            var idxSlot = PopSlot();
            long idx = idxSlot.Kind switch
            {
                SlotKind.I4 => idxSlot.AsI4Checked(),
                SlotKind.I8 => idxSlot.AsI8Checked(),
                _ => throw new InvalidOperationException($"Pointer index must be I4 or I8, got {idxSlot.Kind}.")
            };

            var a = PopSlot();
            int baseAbs = GetAddressAbsOrThrow(a);

            long delta = checked(idx * (long)elemSize);
            int resAbs = checked(baseAbs + checked((int)delta));

            PushSlot(new Slot(a.Kind, resAbs, aux: elemSize));
        }
        private void ExecPtrDiff(int elemSize)
        {
            if (elemSize <= 0) throw new InvalidOperationException("Bad element size.");

            var b = PopSlot();
            var a = PopSlot();

            int aAbs = a.Kind == SlotKind.Null ? 0 : GetAddressAbsOrThrow(a);
            int bAbs = b.Kind == SlotKind.Null ? 0 : GetAddressAbsOrThrow(b);

            long diffBytes = (long)aAbs - (long)bAbs;
            long diffElems = diffBytes / elemSize;

            int PointerSize = RuntimeTypeSystem.PointerSize;
            if (PointerSize == 8)
                PushSlot(new Slot(SlotKind.I8, diffElems));
            else
                PushSlot(new Slot(SlotKind.I4, unchecked((int)diffElems)));
        }

        private void ExecLdobj(RuntimeModule mod, int typeToken)
        {
            var a = PopSlot();
            int abs = GetAddressAbsOrThrow(a);

            var t = ResolveTypeTokenInCurrentMethod(mod, typeToken);
            var (sz, _) = GetStorageSizeAlign(t);
            CheckIndirectAccess(abs, sz, writable: false);

            PushSlot(LoadValueAsSlot(abs, 0, t));
        }
        private void ExecStobj(RuntimeModule mod, int typeToken)
        {
            var v = PopSlot();
            var a = PopSlot();
            int abs = GetAddressAbsOrThrow(a);

            var t = ResolveTypeTokenInCurrentMethod(mod, typeToken);
            var (sz, _) = GetStorageSizeAlign(t);
            CheckIndirectAccess(abs, sz, writable: true);

            StoreSlotAsValue(abs, 0, t, v);
        }
        private void ExecNewobj(RuntimeModule callerModule, int ctorToken, int argCount, CancellationToken ct, ExecutionLimits limits)
        {
            // Resolve ctor body
            var (targetModuleOpt, targetFn) = _domain.ResolveCall(callerModule, ctorToken);
            var targetModule = targetModuleOpt ?? callerModule;
            var ctor = ResolveRuntimeMethodOrThrow(callerModule, ctorToken, _curLayout?.Method);

            // Pop explicit args from caller eval stack
            var args = new Slot[argCount];
            for (int i = argCount - 1; i >= 0; i--)
                args[i] = PopSlot();

            int callerMethodToken = ReadI32(_frameBase + 16);
            int callerModuleId = ReadI32(_frameBase + 20);
            int returnPc = _curPc;
            if (IsSystemStringType(ctor.DeclaringType))
            {
                int length = 0;
                if (args.Length == 0)
                {
                    length = 0;
                }
                else if (args.Length == 2 &&
                         ctor.ParameterTypes[0].Namespace == "System" && ctor.ParameterTypes[0].Name == "Char" &&
                         ctor.ParameterTypes[1].Namespace == "System" && ctor.ParameterTypes[1].Name == "Int32")
                {
                    length = args[1].AsI4Checked();
                    if (length < 0) throw new InvalidOperationException("Negative string length.");
                }
                else if (args.Length == 1 && ctor.ParameterTypes[0].Kind == RuntimeTypeKind.Array &&
                         ctor.ParameterTypes[0].ElementType is { Namespace: "System", Name: "Char" })
                {
                    // If value is null, ctor body should throw
                    if (args[0].Kind == SlotKind.Null) length = 0;
                    else
                    {
                        ValidateArrayRefExact(args[0], ctor.ParameterTypes[0].ElementType!, out _, out int arrLen, out _);
                        length = arrLen;
                    }
                }
                else if (args.Length == 3 && ctor.ParameterTypes[0].Kind == RuntimeTypeKind.Array &&
                         ctor.ParameterTypes[0].ElementType is { Namespace: "System", Name: "Char" } &&
                         ctor.ParameterTypes[1].Namespace == "System" && ctor.ParameterTypes[1].Name == "Int32" &&
                         ctor.ParameterTypes[2].Namespace == "System" && ctor.ParameterTypes[2].Name == "Int32")
                {
                    length = args[2].AsI4Checked();
                    if (length < 0) throw new InvalidOperationException("Negative string length.");
                }
                else if (args.Length == 1 && ctor.ParameterTypes[0].Kind == RuntimeTypeKind.Pointer &&
                         ctor.ParameterTypes[0].ElementType is { Namespace: "System", Name: "Char" })
                {
                    if (args[0].Kind == SlotKind.Null) length = 0;
                    else
                    {
                        int abs = GetAddressAbsOrThrow(args[0]);

                        const int MaxChars = 1 * 1024 * 1024; // hard safety cap
                        int n = 0;
                        for (; n < MaxChars; n++)
                        {
                            int pos = abs + (n * 2);
                            if (pos >= _heapBase && pos + 2 <= _heapPtr)
                            {
                                CheckHeapAccess(pos, 2, writable: false);
                            }
                            else
                            {
                                CheckActiveStackAccess(pos, 2, writable: false);
                            }

                            ushort ch = BinaryPrimitives.ReadUInt16LittleEndian(_mem.AsSpan(pos, 2));
                            if (ch == 0) break;
                        }
                        length = n;
                    }
                }
                else
                {
                    throw new NotSupportedException(
                        $"System.String constructor not supported (arity={args.Length}).");
                }

                int objAbs = AllocStringUninitialized(length);
                var objRef = new Slot(SlotKind.Ref, objAbs);

                // Resolve ctor body and run it
                PushFrame(
                    targetModule,
                    targetFn,
                    returnPc: returnPc,
                    returnMethodToken: callerMethodToken,
                    returnModuleId: callerModuleId,
                    ct: ct,
                    limits: limits,
                    totalArgsOnCallerStack: 0,
                    runtimeMethod: ctor);

                var calleeLayout = _curLayout ?? throw new InvalidOperationException("Ctor frame layout not initialized.");
                int calleeArgsAbs = _curArgsAbs;

                // arg0 = this
                StoreArgRawAt(calleeLayout, 0, calleeArgsAbs, objRef);
                for (int i = 0; i < args.Length; i++)
                    StoreArgRawAt(calleeLayout, i + 1, calleeArgsAbs, args[i]);

                _pendingCtorResults[_frameBase] = PendingCtorResult.ForRef(objAbs);
                return;
            }
            if (ctor.DeclaringType.IsReferenceType)
            {
                int objAbs = AllocObject(ctor.DeclaringType);
                var objRef = new Slot(SlotKind.Ref, objAbs);

                PushFrame(
                    targetModule,
                    targetFn,
                    returnPc: returnPc,
                    returnMethodToken: callerMethodToken,
                    returnModuleId: callerModuleId,
                    ct: ct,
                    limits: limits,
                    totalArgsOnCallerStack: 0,
                    runtimeMethod: ctor);

                var calleeLayout = _curLayout ?? throw new InvalidOperationException("Ctor frame layout not initialized.");
                int calleeArgsAbs = _curArgsAbs;

                StoreArgRawAt(calleeLayout, 0, calleeArgsAbs, objRef);
                for (int i = 0; i < args.Length; i++)
                    StoreArgRawAt(calleeLayout, i + 1, calleeArgsAbs, args[i]);

                _pendingCtorResults[_frameBase] = PendingCtorResult.ForRef(objAbs);
                return;
            }

            // Value type ctor
            {
                var vt = ctor.DeclaringType;
                var (sz, al) = GetStorageSizeAlign(vt);

                int tempAbs = AllocFrameScratch(sz, al);
                Array.Clear(_mem, tempAbs, sz);

                PushFrame(
                    targetModule,
                    targetFn,
                    returnPc: returnPc,
                    returnMethodToken: callerMethodToken,
                    returnModuleId: callerModuleId,
                    ct: ct,
                    limits: limits,
                    totalArgsOnCallerStack: 0,
                    runtimeMethod: ctor);

                var calleeLayoutVt = _curLayout ?? throw new InvalidOperationException("Ctor frame layout not initialized.");
                int calleeArgsAbsVt = _curArgsAbs;

                StoreArgRawAt(calleeLayoutVt, 0, calleeArgsAbsVt, new Slot(SlotKind.ByRef, tempAbs, aux: sz));
                for (int i = 0; i < args.Length; i++)
                    StoreArgRawAt(calleeLayoutVt, i + 1, calleeArgsAbsVt, args[i]);

                _pendingCtorResults[_frameBase] = PendingCtorResult.ForValue(vt, tempAbs);
            }

        }
        private int AllocFrameScratch(int bytes, int align)
        {
            if (bytes < 0)
                throw new InvalidOperationException("Negative frame scratch size.");
            if (align <= 0)
                throw new InvalidOperationException($"Invalid frame scratch alignment: {align}.");

            int baseAbs = ReadI32(_frameBase + 60);
            int spBytes = ReadI32(_frameBase + 64);

            int curAbs = baseAbs + spBytes;
            int alignedAbs = AlignUp(curAbs, align);

            int newSpBytes = checked((alignedAbs - baseAbs) + bytes);
            int newEndAbs = checked(baseAbs + newSpBytes);

            if (newEndAbs > _stackEnd)
                throw new InvalidOperationException("Stack overflow (frame scratch).");

            WriteI32(_frameBase + 64, newSpBytes);

            int oldFrameEnd = ReadI32(_frameBase + 68);
            if (newEndAbs > oldFrameEnd)
            {
                WriteI32(_frameBase + 68, newEndAbs);
                _sp = newEndAbs;
                if (_sp > _stackPeakAbs) _stackPeakAbs = _sp;
            }

            return alignedAbs;
        }
        private int GetInstanceFieldAddress(RuntimeField field, Slot receiver, bool writable)
        {
            if (field.IsStatic)
                throw new InvalidOperationException($"Field '{field.Name}' is static, use ldsfld/stsfld.");

            // Reference type receiver
            if (receiver.Kind == SlotKind.Ref || receiver.Kind == SlotKind.Null)
            {
                if (receiver.Kind == SlotKind.Null)
                    throw new NullReferenceException($"receiver.Kind for field '{field.Name}' is Null");

                int objAbs = checked((int)receiver.Payload);
                var actualType = GetObjectTypeFromRef(receiver);

                if (field.DeclaringType.IsValueType)
                {
                    if (actualType.TypeId != field.DeclaringType.TypeId)
                        throw new InvalidOperationException(
                            $"Field '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}.{field.Name}'" +
                            $" requires boxed '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}', " +
                            $"got '{actualType.Namespace}.{actualType.Name}'.");

                    int boxedFieldAbs = checked(objAbs + ObjectHeaderSize + field.Offset);
                    var (boxedFieldSize, _) = GetStorageSizeAlign(field.FieldType);
                    CheckHeapAccess(boxedFieldAbs, boxedFieldSize, writable);
                    return boxedFieldAbs;
                }

                if (!IsAssignableTo(actualType, field.DeclaringType))
                    throw new InvalidOperationException(
                        $"Field '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}.{field.Name}' " +
                        $"is not valid for object of type '{actualType.Namespace}.{actualType.Name}'.");

                int fieldAbs = checked(objAbs + field.Offset);
                var (sz, _) = GetStorageSizeAlign(field.FieldType);
                CheckHeapAccess(fieldAbs, sz, writable);
                return fieldAbs;
            }
            // Value type receiver by address
            if (receiver.Kind == SlotKind.ByRef || receiver.Kind == SlotKind.Ptr)
            {
                if (!field.DeclaringType.IsValueType)
                    throw new InvalidOperationException($"Address receiver is only valid for value-type fields, got '{field.DeclaringType.Name}'.");

                int baseAbs = checked((int)receiver.Payload);
                int fieldAbs = checked(baseAbs + field.Offset);

                var (sz, _) = GetStorageSizeAlign(field.FieldType);

                CheckIndirectAccess(fieldAbs, sz, writable);

                return fieldAbs;
            }

            throw new InvalidOperationException($"Invalid receiver kind for ldfld/stfld: {receiver.Kind}");
        }

        private void ExecLdfld(RuntimeModule mod, int fieldToken)
        {
            var field = _rts.ResolveField(mod, fieldToken);
            field = RemapFieldForGenericContext(field);
            var receiver = PopSlot();

            int fieldAbs = GetInstanceFieldAddress(field, receiver, writable: false);
            var value = LoadValueAsSlot(fieldAbs, 0, field.FieldType);
            PushSlot(value);
        }
        private void ExecLdflda(RuntimeModule mod, int fieldToken)
        {
            var field = _rts.ResolveField(mod, fieldToken);
            field = RemapFieldForGenericContext(field);
            var receiver = PopSlot();
            int fieldAbs = GetInstanceFieldAddress(field, receiver, writable: false);
            var (sz, _) = GetStorageSizeAlign(field.FieldType);
            PushSlot(new Slot(SlotKind.ByRef, fieldAbs, aux: sz));
        }
        private void ExecStfld(RuntimeModule mod, int fieldToken)
        {
            var field = _rts.ResolveField(mod, fieldToken);
            field = RemapFieldForGenericContext(field);
            var value = PopSlot();
            var receiver = PopSlot();

            int fieldAbs = GetInstanceFieldAddress(field, receiver, writable: true);
            StoreSlotAsValue(fieldAbs, 0, field.FieldType, value);
        }
        private void ExecLdsfld(RuntimeModule mod, int fieldToken)
        {
            var field = _rts.ResolveField(mod, fieldToken);
            field = RemapFieldForGenericContext(field);
            if (!field.IsStatic)
                throw new InvalidOperationException($"Field '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}.{field.Name}' is not static.");

            int fieldAbs = GetStaticFieldAddress(field, writable: false);
            var value = LoadValueAsSlot(fieldAbs, 0, field.FieldType);
            PushSlot(value);
        }
        private void ExecLdsflda(RuntimeModule mod, int fieldToken)
        {
            var field = _rts.ResolveField(mod, fieldToken);
            field = RemapFieldForGenericContext(field);
            if (!field.IsStatic)
                throw new InvalidOperationException($"Field '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}.{field.Name}' is not static.");

            int fieldAbs = GetStaticFieldAddress(field, writable: false);
            var (sz, _) = GetStorageSizeAlign(field.FieldType);
            PushSlot(new Slot(SlotKind.ByRef, fieldAbs, aux: sz));
        }
        private void ExecStsfld(RuntimeModule mod, int fieldToken)
        {
            var field = _rts.ResolveField(mod, fieldToken);
            field = RemapFieldForGenericContext(field);
            if (!field.IsStatic)
                throw new InvalidOperationException($"Field '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}.{field.Name}' is not static.");

            var value = PopSlot();
            int fieldAbs = GetStaticFieldAddress(field, writable: true);
            StoreSlotAsValue(fieldAbs, 0, field.FieldType, value);
        }

        private int GetStaticFieldAddress(RuntimeField field, bool writable)
        {
            int baseAbs = EnsureStaticStorage(field.DeclaringType);
            if (baseAbs == 0)
                throw new InvalidOperationException($"Static storage not allocated for '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}'.");

            int abs = checked(baseAbs + field.Offset);
            var (sz, _) = GetStorageSizeAlign(field.FieldType);
            CheckHeapAccess(abs, sz, writable);
            return abs;
        }
        private int EnsureStaticStorage(RuntimeType t)
        {
            if (_staticBaseByTypeId.TryGetValue(t.TypeId, out int abs))
                return abs;

            int size = t.StaticSize;
            if (size <= 0)
            {
                _staticBaseByTypeId[t.TypeId] = 0;
                return 0;
            }

            int align = Math.Max(8, t.StaticAlign);
            abs = AllocHeapBytes(size, align);
            Array.Clear(_mem, abs, size);

            _staticBaseByTypeId[t.TypeId] = abs;

            int end = checked(abs + size);
            if (end > _heapFloor)
                _heapFloor = end;

            return abs;
        }
        private bool TryDeferTypeInitializationForStaticField(
            RuntimeModule contextModule,
            int fieldToken,
            int resumePc,
            CancellationToken ct,
            ExecutionLimits limits)
        {
            var f = _rts.ResolveField(contextModule, fieldToken);
            f = RemapFieldForGenericContext(f);
            if (!f.IsStatic)
                return false;

            return TryDeferTypeInitialization(f.DeclaringType, resumePc, ct, limits);
        }

        private bool TryDeferTypeInitializationForNewobj(
            RuntimeModule callerModule,
            int ctorToken,
            int resumePc,
            CancellationToken ct,
            ExecutionLimits limits)
        {
            var ctor = ResolveRuntimeMethodOrThrow(callerModule, ctorToken, _curLayout?.Method);
            return TryDeferTypeInitialization(ctor.DeclaringType, resumePc, ct, limits);
        }
        private static RuntimeMethod? FindTypeInitializer(RuntimeType t)
        {
            for (int i = 0; i < t.Methods.Length; i++)
            {
                var m = t.Methods[i];
                if (!m.IsStatic) continue;
                if (m.ParameterTypes.Length != 0) continue;
                if (!StringComparer.Ordinal.Equals(m.Name, ".cctor")) continue;
                return m;
            }
            return null;
        }
        private bool TryDeferTypeInitialization(RuntimeType t, int resumePc, CancellationToken ct, ExecutionLimits limits)
        {
            if (_typeInitState.TryGetValue(t.TypeId, out byte state))
            {
                if (state == 2) return false;
                if (state == 1) return false; // allow in progress access from within .cctor
            }
            _ = EnsureStaticStorage(t);

            var cctor = FindTypeInitializer(t);
            if (cctor is null || cctor.BodyModule is null || cctor.Body is null)
            {
                _typeInitState[t.TypeId] = 2;
                return false;
            }
            _typeInitState[t.TypeId] = 1;
            if (_frameBase < 0)
                throw new InvalidOperationException("Type initialization requires an active frame.");

            int callerMethodToken = ReadI32(_frameBase + 16);
            int callerModuleId = ReadI32(_frameBase + 20);

            _curPc = resumePc;

            PushFrame(
                module: cctor.BodyModule,
                fn: cctor.Body,
                returnPc: resumePc,
                returnMethodToken: callerMethodToken,
                returnModuleId: callerModuleId,
                ct: ct,
                limits: limits,
                totalArgsOnCallerStack: 0,
                runtimeMethod: cctor);

            _pendingTypeInitFrames[_frameBase] = t.TypeId;
            return true;
        }
        private RuntimeField RemapFieldForGenericContext(RuntimeField field)
        {
            var rm = _curLayout?.Method;
            if (rm is null)
                return field;

            var decl = rm.DeclaringType;
            var gdef = decl.GenericTypeDefinition;
            if (gdef is null)
                return field;

            if (!ReferenceEquals(field.DeclaringType, gdef))
                return field;

            var list = field.IsStatic ? decl.StaticFields : decl.InstanceFields;
            for (int i = 0; i < list.Length; i++)
            {
                var cand = list[i];
                if (cand.IsStatic != field.IsStatic)
                    continue;
                if (!StringComparer.Ordinal.Equals(cand.Name, field.Name))
                    continue;
                return cand;
            }
            return field;
        }
        private static int AlignForSize(int size)
        {
            if (size <= 1) return 1;
            if (size == 2) return 2;
            if (size == 4) return 4;
            return 8;
        }
        private int GetModuleId(RuntimeModule m) => _moduleIdByName[m.Name];
        private void PushFrame(
            RuntimeModule module,
            BytecodeFunction fn,
            int returnPc,
            int returnMethodToken,
            int returnModuleId,
            CancellationToken ct,
            ExecutionLimits limits,
            int totalArgsOnCallerStack = 0,
            RuntimeMethod? runtimeMethod = null,
            ReadOnlySpan<Slot> initialArgs = default)
        {
            if (++_callDepth > limits.MaxCallDepth)
                throw new InvalidOperationException("Max call depth exceeded.");

            var layout = GetOrCreateMethodLayout(module, fn, runtimeMethod);
            var rm = layout.Method;

            int argsCount = layout.ArgTypes.Length;

            if (!initialArgs.IsEmpty && initialArgs.Length != argsCount)
                throw new InvalidOperationException($"Initial arg count mismatch: expected {argsCount}, got {initialArgs.Length}");

            if (totalArgsOnCallerStack != 0 && totalArgsOnCallerStack != argsCount)
                throw new InvalidOperationException($"Stack arg count mismatch: expected {argsCount}, got {totalArgsOnCallerStack}");

            int cursor = FrameHeaderSize;

            cursor = AlignUp(cursor, 8);
            int argsBase = cursor;
            cursor = checked(cursor + layout.ArgsAreaSize);

            cursor = AlignUp(cursor, 8);
            int localsBase = cursor;
            cursor = checked(cursor + layout.LocalsAreaSize);

            cursor = AlignUp(cursor, 8);
            int evalBase = cursor;
            int evalBytes = checked(fn.MaxStack * SlotSize);
            cursor = checked(cursor + evalBytes);

            cursor = AlignUp(cursor, 8);
            int evalBlobBase = cursor;
            int evalBlobCap = ComputeEvalBlobCapacity(module, fn, rm);
            cursor = checked(cursor + evalBlobCap);

            cursor = AlignUp(cursor, 8);
            int stackallocBase = cursor;
            int frameEnd = cursor;

            int frameSize = frameEnd;
            int newBase = _sp;
            int newSp = checked(_sp + frameSize);
            if (newSp > _stackEnd)
                throw new InvalidOperationException("VM stack overflow.");

            Array.Clear(_mem, newBase, FrameHeaderSize);

            int initBytes = evalBase - argsBase;
            if (initBytes > 0)
                Array.Clear(_mem, newBase + argsBase, initBytes);

            WriteI32(newBase + 0, _frameBase);
            WriteI32(newBase + 4, returnPc);
            WriteI32(newBase + 8, returnMethodToken);
            WriteI32(newBase + 12, returnModuleId);
            WriteI32(newBase + 16, fn.MethodToken);
            WriteI32(newBase + 20, GetModuleId(module));
            WriteI32(newBase + 24, 0);               // pc
            WriteI32(newBase + 28, newBase + evalBase);
            WriteI32(newBase + 32, 0);               // evalSp
            WriteI32(newBase + 36, fn.MaxStack);
            WriteI32(newBase + 40, newBase + argsBase);
            WriteI32(newBase + 44, newBase + localsBase);
            WriteI32(newBase + 48, newBase + evalBlobBase);
            WriteI32(newBase + 52, 0);               // evalBlobSp
            WriteI32(newBase + 56, evalBlobCap);     // evalBlobCap
            WriteI32(newBase + 60, newBase + stackallocBase);
            WriteI32(newBase + 64, 0);               // stackallocSp
            WriteI32(newBase + 68, newBase + frameEnd);
            WriteI32(newBase + 72, rm.MethodId);

            int calleeArgsAbs = newBase + argsBase;

            if (!initialArgs.IsEmpty)
            {
                for (int i = 0; i < argsCount; i++)
                    StoreArgRawAt(layout, i, calleeArgsAbs, initialArgs[i]);
            }
            else if (totalArgsOnCallerStack != 0)
            {
                for (int i = argsCount - 1; i >= 0; i--)
                {
                    var slot = PopSlot();
                    StoreArgRawAt(layout, i, calleeArgsAbs, slot);
                }
            }
            SpillCurrentFrameHotState();

            _frameBase = newBase;
            _sp = newSp;
            if (_sp > _stackPeakAbs) _stackPeakAbs = _sp;
            _curModule = module;
            _curFn = fn;
            _curLayout = layout;
            _curArgsAbs = newBase + argsBase;
            _curLocalsAbs = newBase + localsBase;
            _curEvalBase = newBase + evalBase;
            _curEvalSp = 0;
            _curEvalMax = fn.MaxStack;
            EnsureHotEvalCapacity(_curEvalMax);
            _curPc = 0;

            ct.ThrowIfCancellationRequested();
        }
        private void PopFrame()
        {
            int prev = ReadI32(_frameBase + 0);
            int frameEnd = ReadI32(_frameBase + 68);

            // Release stack to frame base
            _sp = _frameBase;
            _frameBase = prev;

            _callDepth--;
            RefreshCurrentFrameCache();
        }
        private void ClearCatchContextsForFrame(int frameBase)
        {
            while (_catchStack.Count != 0 && _catchStack[_catchStack.Count - 1].FrameBase == frameBase)
                _catchStack.RemoveAt(_catchStack.Count - 1);
        }
        private void PruneCatchContextsForPc(int pc)
        {
            for (int i = _catchStack.Count - 1; i >= 0; i--)
            {
                var ctx = _catchStack[i];
                if (ctx.FrameBase != _frameBase)
                    break;

                if (pc < ctx.HandlerStartPc || pc >= ctx.HandlerEndPc)
                    _catchStack.RemoveAt(i);
            }
        }
        private Slot GetCurrentCatchExceptionOrThrow()
        {
            if (_catchStack.Count == 0 || _catchStack[_catchStack.Count - 1].FrameBase != _frameBase)
                throw new InvalidOperationException("No active catch context.");
            return _catchStack[_catchStack.Count - 1].Exception;
        }
        private void ExecEndfinally(int pc)
        {
            if (_finallyStack.Count == 0 || _finallyStack[_finallyStack.Count - 1].FrameBase != _frameBase)
                throw new InvalidOperationException("Endfinally without an active finally context.");

            var ctx = _finallyStack[_finallyStack.Count - 1];
            _finallyStack.RemoveAt(_finallyStack.Count - 1);

            var fn = _curFn ?? throw new InvalidOperationException("No current function.");

            if (ctx.Kind == FinallyContinuationKind.Throw)
            {
                if (!ctx.HasPendingException)
                    throw new InvalidOperationException("Finally throw continuation missing exception.");
                ThrowException(ctx.PendingException, throwPc: ctx.NextFromPc);
                return;
            }

            if (ctx.Kind == FinallyContinuationKind.Jump)
            {
                if (TryBeginFinallyForJump(fn, fromPc: ctx.NextFromPc, targetPc: ctx.TargetPc))
                    return;

                _curPc = ctx.TargetPc;
                return;
            }

            if (ctx.Kind == FinallyContinuationKind.Return)
            {
                if (TryBeginFinallyForReturn(fn, fromPc: ctx.NextFromPc, hasRet: ctx.HasReturnValue, retVal: ctx.ReturnValue))
                    return;

                CompleteReturnFromCurrentFrame(ctx.HasReturnValue, ctx.ReturnValue);
                return;
            }

            throw new InvalidOperationException("Unknown finally continuation kind.");
        }
        private void CompleteReturnFromCurrentFrame(bool hasRet, Slot retVal)
        {
            int finishedFrame = _frameBase;
            ClearCatchContextsForFrame(finishedFrame);
            ClearFinallyContextsForFrame(finishedFrame);

            if (_pendingTypeInitFrames.TryGetValue(finishedFrame, out int typeId))
            {
                _pendingTypeInitFrames.Remove(finishedFrame);
                _typeInitState[typeId] = 2;
            }

            bool frameAllocatedHeap = _heapAllocFrames.Remove(finishedFrame);

            PopFrame();

            if (_pendingCtorResults.TryGetValue(finishedFrame, out var pending))
            {
                _pendingCtorResults.Remove(finishedFrame);

                if (_frameBase < 0)
                    throw new InvalidOperationException("Ctor returned but there is no caller frame.");

                if (pending.Kind == PendingCtorResultKind.Ref)
                    PushSlot(new Slot(SlotKind.Ref, pending.Payload));
                else if (pending.Kind == PendingCtorResultKind.Value)
                    PushSlot(LoadValueAsSlot((int)pending.Payload, 0, pending.Type!));
                else
                    throw new InvalidOperationException("Unknown pending ctor result kind.");
            }
            else if (_frameBase >= 0 && hasRet)
            {
                PushSlot(retVal);
            }

            if (frameAllocatedHeap)
            {
                if ((_heapEnd - _heapPtr) < _minTailFreeBytes)
                    _gcRequested = true;
                MaybeCollectGarbage(force: false);
            }
        }
        private void ClearFinallyContextsForFrame(int frameBase)
        {
            for (int i = _finallyStack.Count - 1; i >= 0; i--)
            {
                if (_finallyStack[i].FrameBase == frameBase)
                    _finallyStack.RemoveAt(i);
            }
        }

        private void AbandonActiveFinallyContextIfThrowingInsideFinally(int pcInFrame)
        {
            while (_finallyStack.Count != 0)
            {
                var top = _finallyStack[_finallyStack.Count - 1];
                if (top.FrameBase != _frameBase)
                    break;

                if (pcInFrame >= top.FinallyStartPc && pcInFrame < top.FinallyEndPc)
                {
                    _finallyStack.RemoveAt(_finallyStack.Count - 1);
                    continue;
                }

                break;
            }
        }

        private static int SpanOf(in ExceptionHandler h) => h.TryEndPc - h.TryStartPc;

        private bool TryFindFinallyHandlerForPc(BytecodeFunction fn, int pcInFrame, out ExceptionHandler match)
        {
            match = default;
            var handlers = fn.ExceptionHandlers;
            if (handlers.IsDefaultOrEmpty || pcInFrame < 0)
                return false;

            int bestSpan = int.MaxValue;
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (h.CatchTypeToken != FinallyCatchTypeToken)
                    continue;
                if (pcInFrame < h.TryStartPc || pcInFrame >= h.TryEndPc)
                    continue;

                int span = SpanOf(h);
                if (span < bestSpan)
                {
                    bestSpan = span;
                    match = h;
                }
            }

            return bestSpan != int.MaxValue;
        }

        private bool TryFindFinallyHandlerForLeave(BytecodeFunction fn, int fromPc, int toPc, out ExceptionHandler match)
        {
            match = default;
            var handlers = fn.ExceptionHandlers;
            if (handlers.IsDefaultOrEmpty || fromPc < 0)
                return false;

            int bestSpan = int.MaxValue;
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (h.CatchTypeToken != FinallyCatchTypeToken)
                    continue;
                if (fromPc < h.TryStartPc || fromPc >= h.TryEndPc)
                    continue;
                if (toPc >= h.TryStartPc && toPc < h.TryEndPc)
                    continue;

                int span = SpanOf(h);
                if (span < bestSpan)
                {
                    bestSpan = span;
                    match = h;
                }
            }

            return bestSpan != int.MaxValue;
        }

        private void BeginFinally(in ExceptionHandler h, in FinallyContext ctx)
        {
            PruneCatchContextsForPc(h.HandlerStartPc);
            ResetEvalStackForExceptionHandler();
            _finallyStack.Add(ctx);
            _curPc = h.HandlerStartPc;
        }

        private bool TryBeginFinallyForJump(BytecodeFunction fn, int fromPc, int targetPc)
        {
            if (!TryFindFinallyHandlerForLeave(fn, fromPc, targetPc, out var h))
                return false;

            BeginFinally(h, FinallyContext.ForJump(_frameBase, h, targetPc));
            return true;
        }

        private bool TryBeginFinallyForReturn(BytecodeFunction fn, int fromPc, bool hasRet, Slot retVal)
        {
            if (!TryFindFinallyHandlerForLeave(fn, fromPc, toPc: -1, out var h))
                return false;

            BeginFinally(h, FinallyContext.ForReturn(_frameBase, h, hasRet, retVal));
            return true;
        }
        private RuntimeModule FindCoreLibModuleOrThrow()
        {
            // Prefer the conventional name
            if (_modules.TryGetValue("std", out var std))
                return std;
            // fallback
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                if (m.TypeDefByFullName.ContainsKey(("System", "Object")))
                    return m;
            }

            throw new InvalidOperationException(
                "Core library module not found (expected a module defining System.Object).");
        }

        private Slot NormalizeThrownException(Slot ex)
        {
            if (ex.Kind == SlotKind.Null)
            {
                var core = FindCoreLibModuleOrThrow();
                if (!core.TypeDefByFullName.TryGetValue(("System", "NullReferenceException"), out int tdTok))
                    throw new NullReferenceException(); // host fallback

                var t = _rts.ResolveType(core, tdTok);
                int objAbs = AllocObject(t);
                return new Slot(SlotKind.Ref, objAbs);
            }

            if (ex.Kind != SlotKind.Ref)
                throw new InvalidOperationException($"Throw expects object reference, got {ex.Kind}.");

            return ex;
        }
        private bool TryCreateCoreException(string ns, string name, string? message, out Slot ex)
        {
            ex = default;
            RuntimeModule core;
            try { core = FindCoreLibModuleOrThrow(); }
            catch { return false; }

            if (!core.TypeDefByFullName.TryGetValue((ns, name), out int tdTok))
                return false;

            RuntimeType t;
            int objAbs;
            try
            {
                t = _rts.ResolveType(core, tdTok);
                objAbs = AllocObject(t);
            }
            catch
            {
                return false;
            }
            TryInitExceptionMessage(objAbs, t, message);

            ex = new Slot(SlotKind.Ref, objAbs);
            return true;
        }

        private void TryInitExceptionMessage(int exObjAbs, RuntimeType exType, string? message)
        {
            int msgStrAbs;
            try
            {
                msgStrAbs = AllocStringFromManaged(message ?? string.Empty);
            }
            catch
            {
                try { msgStrAbs = AllocStringUninitialized(0); }
                catch { return; }
            }
            var t = exType;
            while (t is not null)
            {
                for (int i = 0; i < t.InstanceFields.Length; i++)
                {
                    var f = t.InstanceFields[i];
                    if (!StringComparer.Ordinal.Equals(f.Name, "_message"))
                        continue;

                    int fieldAbs = checked(exObjAbs + f.Offset);
                    StoreSlotAsValue(fieldAbs, 0, f.FieldType, new Slot(SlotKind.Ref, msgStrAbs));
                    return;
                }

                t = t.BaseType;
            }
        }

        private bool TryTranslateHostExceptionToVm(Exception hostEx, out Slot vmEx)
        {
            vmEx = default;
            string msg = hostEx.Message;

            if (hostEx is DivideByZeroException)
                return TryCreateCoreException("System", "DivideByZeroException", msg, out vmEx)
                    || TryCreateCoreException("System", "ArithmeticException", msg, out vmEx)
                    || TryCreateCoreException("System", "Exception", msg, out vmEx);

            if (hostEx is OverflowException)
                return TryCreateCoreException("System", "DivideByZeroException", msg, out vmEx)
                    || TryCreateCoreException("System", "ArithmeticException", msg, out vmEx)
                    || TryCreateCoreException("System", "Exception", msg, out vmEx);

            return false;
        }
        private void ResetEvalStackForExceptionHandler()
        {
            _curEvalSp = 0;
            WriteI32(_frameBase + 52, 0);
        }
        private bool TryFindCatchHandler(RuntimeModule mod, BytecodeFunction fn, int throwPc, Slot ex, out ExceptionHandler match)
        {
            match = default;
            var handlers = fn.ExceptionHandlers;
            if (handlers.IsDefaultOrEmpty)
                return false;
            if (throwPc < 0)
                return false;

            var regions = new List<(int start, int end, int span)>();
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (throwPc < h.TryStartPc || throwPc >= h.TryEndPc)
                    continue;
                if (h.CatchTypeToken == FinallyCatchTypeToken)
                    continue;
                bool seen = false;
                for (int j = 0; j < regions.Count; j++)
                {
                    if (regions[j].start == h.TryStartPc && regions[j].end == h.TryEndPc)
                    {
                        seen = true;
                        break;
                    }
                }
                if (!seen)
                    regions.Add((h.TryStartPc, h.TryEndPc, h.TryEndPc - h.TryStartPc));
            }
            if (regions.Count == 0)
                return false;

            regions.Sort(static (a, b) => a.span.CompareTo(b.span));
            var actualType = GetObjectTypeFromRef(ex);
            for (int r = 0; r < regions.Count; r++)
            {
                var reg = regions[r];
                for (int i = 0; i < handlers.Length; i++)
                {
                    var h = handlers[i];
                    if (h.TryStartPc != reg.start || h.TryEndPc != reg.end)
                        continue;
                    if (h.CatchTypeToken == 0)
                    {
                        match = h;
                        return true;
                    }
                    var catchType = ResolveTypeTokenInCurrentMethod(mod, h.CatchTypeToken);
                    if (IsAssignableTo(actualType, catchType))
                    {
                        match = h;
                        return true;
                    }
                }
            }
            return false;
        }
        private void ThrowException(Slot ex, int throwPc)
        {
            ex = NormalizeThrownException(ex);
            int pcInFrame = throwPc;
            while (true)
            {
                AbandonActiveFinallyContextIfThrowingInsideFinally(pcInFrame);
                PruneCatchContextsForPc(pcInFrame);
                var mod = _curModule ?? throw new InvalidOperationException("No current module.");
                var fn = _curFn ?? throw new InvalidOperationException("No current function.");
                if (TryFindCatchHandler(mod, fn, pcInFrame, ex, out var handler))
                {
                    PruneCatchContextsForPc(handler.HandlerStartPc);
                    ResetEvalStackForExceptionHandler();
                    _catchStack.Add(new CatchContext(_frameBase, handler.HandlerStartPc, handler.HandlerEndPc, ex));
                    _curPc = handler.HandlerStartPc;
                    MaybeCollectGarbage(force: false);
                    return;
                }
                if (TryFindFinallyHandlerForPc(fn, pcInFrame, out var fin))
                {
                    BeginFinally(fin, FinallyContext.ForThrow(_frameBase, fin, ex));
                    MaybeCollectGarbage(force: false);
                    return;
                }
                int returnPc = ReadI32(_frameBase + 4);
                if (returnPc < 0)
                {
                    var t = GetObjectTypeFromRef(ex);
                    throw new VmUnhandledException($"Unhandled exception: {t.AssemblyName}:{t.Namespace}.{t.Name}");
                }

                int returnMethodToken = ReadI32(_frameBase + 8);
                int returnModuleId = ReadI32(_frameBase + 12);

                int curBase = _frameBase;
                ClearCatchContextsForFrame(curBase);
                _pendingCtorResults.Remove(curBase);
                if (_pendingTypeInitFrames.TryGetValue(curBase, out int typeId))
                {
                    _pendingTypeInitFrames.Remove(curBase);
                    _typeInitState[typeId] = 0; // allow retry
                }
                if (_heapAllocFrames.Remove(curBase))
                {
                    if ((_heapEnd - _heapPtr) < _minTailFreeBytes)
                        _gcRequested = true;
                }
                ClearFinallyContextsForFrame(curBase);
                PopFrame();
                pcInFrame = returnPc - 1;
                if (pcInFrame < 0)
                    pcInFrame = 0;

                if ((uint)returnModuleId >= (uint)_moduleById.Length)
                    throw new InvalidOperationException("Return module id out of range.");

                var callerModule = _moduleById[returnModuleId];
                _curModule = callerModule;

                if (!callerModule.MethodsByDefToken.TryGetValue(returnMethodToken, out var callerFn))
                    throw new InvalidOperationException("Return method not found.");

                _curFn = callerFn;
            }
        }
        private void PushSlot(Slot v)
        {
            if (v.Kind == SlotKind.Value)
            {
                SyncEvalBlobSpToEvalStack();

                var vt = _rts.GetTypeById(v.Aux);
                int size = vt.SizeOf;
                int srcAbs = checked((int)v.Payload);

                CheckRange(srcAbs, size);

                int dstAbs = AllocEvalBlobBytes(size);
                Buffer.BlockCopy(_mem, srcAbs, _mem, dstAbs, size);

                v = new Slot(SlotKind.Value, dstAbs, v.Aux);
            }

            if (_curEvalSp >= _curEvalMax)
                throw new InvalidOperationException("Eval stack overflow.");

            _hotEvalSlots[_curEvalSp++] = v;
        }

        private Slot PopSlot()
        {
            if (_curEvalSp <= 0)
                throw new InvalidOperationException("Eval stack underflow.");

            return _hotEvalSlots[--_curEvalSp];
        }

        private Slot PeekSlot()
        {
            if (_curEvalSp <= 0)
                throw new InvalidOperationException("Eval stack underflow.");

            return _hotEvalSlots[_curEvalSp - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSlot(int absOffset, Slot v)
        {
            // [0]=kind, [4]=aux, [8]=payload
            ref byte p = ref Unsafe.Add(ref Mem0, absOffset);

            p = (byte)v.Kind;

            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, 4), v.Aux);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, 8), v.Payload);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Slot ReadSlot(int absOffset)
        {
            ref byte p = ref Unsafe.Add(ref Mem0, absOffset);

            var kind = (SlotKind)p;
            int aux = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref p, 4));
            long payload = Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref p, 8));

            return new Slot(kind, payload, aux);
        }
        // locals/args raw storage
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LoadLocal(int localIndex)
        {
            var layout = _curLayout ?? throw new InvalidOperationException("No current layout.");
            int off = layout.LocalOffsets[localIndex];
            int abs = _curLocalsAbs + off;

            var fk = layout.LocalFastKinds[localIndex];
            if (fk != FastCellKind.None)
            {
                PushSlot(LoadFastCellAsSlot(fk, abs));
                return;
            }

            var t = layout.LocalTypes[localIndex];
            var slot = LoadValueAsSlot(_curLocalsAbs, off, t);
            PushSlot(slot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreLocal(int localIndex, Slot value)
        {
            var layout = _curLayout ?? throw new InvalidOperationException("No current layout.");
            int off = layout.LocalOffsets[localIndex];
            int abs = _curLocalsAbs + off;

            var fk = layout.LocalFastKinds[localIndex];
            if (fk != FastCellKind.None && TryStoreFastCell(fk, abs, value))
                return;

            var t = layout.LocalTypes[localIndex];
            StoreSlotAsValue(_curLocalsAbs, off, t, value);
        }

        private void LoadArg(int argIndex)
        {
            var layout = _curLayout ?? throw new InvalidOperationException("No current layout.");
            int off = layout.ArgOffsets[argIndex];
            int abs = _curArgsAbs + off;

            var fk = layout.ArgFastKinds[argIndex];
            if (fk != FastCellKind.None)
            {
                PushSlot(LoadFastCellAsSlot(fk, abs));
                return;
            }

            var t = layout.ArgTypes[argIndex];
            var slot = LoadValueAsSlot(_curArgsAbs, off, t);
            PushSlot(slot);
        }

        private void StoreArg(int argIndex, Slot value)
        {
            var layout = _curLayout ?? throw new InvalidOperationException("No current layout.");
            StoreArgRawAt(layout, argIndex, _curArgsAbs, value);
        }

        private void StoreArgRawAt(MethodExecLayout layout, int argIndex, int argsAbs, Slot value)
        {
            int off = layout.ArgOffsets[argIndex];
            int abs = argsAbs + off;

            var fk = layout.ArgFastKinds[argIndex];
            if (fk != FastCellKind.None && TryStoreFastCell(fk, abs, value))
                return;

            var t = layout.ArgTypes[argIndex];
            StoreSlotAsValue(argsAbs, off, t, value);
        }
        private RuntimeType GetArgType(RuntimeMethod rm, int argIndex)
        {
            if (rm.HasThis)
            {
                if (argIndex == 0) // this
                {
                    if (rm.DeclaringType.IsValueType)
                        return _rts.GetByRefType(rm.DeclaringType);
                    return rm.DeclaringType;
                }
                return rm.ParameterTypes[argIndex - 1];
            }
            return rm.ParameterTypes[argIndex];
        }
        private long ReadNativeInt(int abs)
        {
            return RuntimeTypeSystem.PointerSize switch
            {
                4 => BinaryPrimitives.ReadInt32LittleEndian(_mem.AsSpan(abs, 4)),
                8 => BinaryPrimitives.ReadInt64LittleEndian(_mem.AsSpan(abs, 8)),
                _ => throw new InvalidOperationException("Unsupported pointer size.")
            };
        }
        private void WriteNativeInt(int abs, long value)
        {
            int ptrSize = RuntimeTypeSystem.PointerSize;
            switch (ptrSize)
            {
                case 4:
                    BinaryPrimitives.WriteInt32LittleEndian(_mem.AsSpan(abs, 4), checked((int)value));
                    return;
                case 8:
                    BinaryPrimitives.WriteInt64LittleEndian(_mem.AsSpan(abs, 8), value);
                    return;
                default:
                    throw new InvalidOperationException("Unsupported pointer size.");
            }
        }
        private Slot LoadValueAsSlot(int baseAbs, int offset, RuntimeType t)
        {
            var (sz, _) = GetStorageSizeAlign(t);
            int abs = baseAbs + offset;

            CheckRange(abs, sz);

            if (t.IsReferenceType)
            {
                long h = ReadNativeInt(abs);
                return h == 0 ? new Slot(SlotKind.Null, 0) : new Slot(SlotKind.Ref, h);
            }

            if (t.Kind == RuntimeTypeKind.Pointer)
            {
                long p = ReadNativeInt(abs);
                return p == 0 ? new Slot(SlotKind.Null, 0) : new Slot(SlotKind.Ptr, p);
            }

            if (t.Kind == RuntimeTypeKind.ByRef)
            {
                long p = ReadNativeInt(abs);
                return p == 0 ? new Slot(SlotKind.Null, 0) : new Slot(SlotKind.ByRef, p);
            }

            if (UsesBlobOnEvalStack(t))
            {
                return new Slot(SlotKind.Value, abs, t.TypeId);
            }

            if (t.Kind == RuntimeTypeKind.Enum)
            {
                var ut = TryGetEnumUnderlyingType(t);
                if (ut != null)
                    return LoadValueAsSlot(baseAbs, offset, ut);
            }

            if (t.Namespace == "System")
            {
                switch (t.Name)
                {
                    case "SByte":
                        return new Slot(SlotKind.I4, unchecked((sbyte)_mem[abs]));
                    case "Byte":
                    case "Boolean":
                        return new Slot(SlotKind.I4, _mem[abs]);

                    case "Int16":
                        return new Slot(SlotKind.I4, BinaryPrimitives.ReadInt16LittleEndian(_mem.AsSpan(abs, 2)));
                    case "UInt16":
                    case "Char":
                        return new Slot(SlotKind.I4, BinaryPrimitives.ReadUInt16LittleEndian(_mem.AsSpan(abs, 2)));

                    case "Int32":
                    case "UInt32":
                        return new Slot(SlotKind.I4, BinaryPrimitives.ReadInt32LittleEndian(_mem.AsSpan(abs, 4)));

                    case "Int64":
                    case "UInt64":
                        return new Slot(SlotKind.I8, BinaryPrimitives.ReadInt64LittleEndian(_mem.AsSpan(abs, 8)));
                    case "Single":
                        {
                            int bits = BinaryPrimitives.ReadInt32LittleEndian(_mem.AsSpan(abs, 4));
                            float f = BitConverter.Int32BitsToSingle(bits);
                            return new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits((double)f));
                        }

                    case "Double":
                        {
                            long bits = BinaryPrimitives.ReadInt64LittleEndian(_mem.AsSpan(abs, 8));
                            return new Slot(SlotKind.R8, bits);
                        }
                }
            }

            return sz switch
            {
                1 => new Slot(SlotKind.I4, _mem[abs]),
                2 => new Slot(SlotKind.I4, BinaryPrimitives.ReadUInt16LittleEndian(_mem.AsSpan(abs, 2))),
                4 => new Slot(SlotKind.I4, BinaryPrimitives.ReadInt32LittleEndian(_mem.AsSpan(abs, 4))),
                8 => new Slot(SlotKind.I8, BinaryPrimitives.ReadInt64LittleEndian(_mem.AsSpan(abs, 8))),
                _ => throw new NotSupportedException($"Value type size {sz} not supported on eval stack.")
            };
        }

        private void StoreSlotAsValue(int baseAbs, int offset, RuntimeType t, Slot v)
        {
            var (sz, _) = GetStorageSizeAlign(t);
            int abs = baseAbs + offset;

            // Writes must not touch metadata
            CheckWritableRange(abs, sz);
            //MarkHeapWriteIfInsideHeap(abs, sz);
            if (t.IsReferenceType)
            {
                if (v.Kind is not (SlotKind.Ref or SlotKind.Null))
                    throw new InvalidOperationException($"Storing {v.Kind} into managed ref.");

                if ((_heapEnd - _heapPtr) < _minTailFreeBytes)
                    _gcRequested = true;

                long h = v.Kind == SlotKind.Null ? 0 : v.Payload;
                WriteNativeInt(abs, h);
                return;
            }

            if (t.Kind == RuntimeTypeKind.Pointer)
            {
                if (v.Kind is not (SlotKind.Ptr or SlotKind.Null))
                    throw new InvalidOperationException($"Storing {v.Kind} into pointer.");

                long p = v.Kind == SlotKind.Null ? 0 : v.Payload;
                WriteNativeInt(abs, p);
                return;
            }

            if (t.Kind == RuntimeTypeKind.ByRef)
            {
                if (v.Kind is not (SlotKind.ByRef or SlotKind.Null))
                    throw new InvalidOperationException($"Storing {v.Kind} into byref.");

                long p = v.Kind == SlotKind.Null ? 0 : v.Payload;
                WriteNativeInt(abs, p);
                return;
            }

            if (UsesBlobOnEvalStack(t))
            {
                if (v.Kind != SlotKind.Value)
                    throw new InvalidOperationException($"Cannot store {v.Kind} into struct '{t.Namespace}.{t.Name}'.");

                var slotType = GetValueSlotType(v);
                if (slotType.TypeId != t.TypeId)
                    throw new InvalidOperationException(
                        $"Struct type mismatch: slot={slotType.Namespace}.{slotType.Name}, target={t.Namespace}.{t.Name}.");

                int sz2 = t.SizeOf;
                int srcAbs = checked((int)v.Payload);
                CheckRange(srcAbs, sz2);

                Buffer.BlockCopy(_mem, srcAbs, _mem, abs, sz2);
                return;
            }

            if (t.Kind == RuntimeTypeKind.Enum)
            {
                var ut = TryGetEnumUnderlyingType(t);
                if (ut != null)
                {
                    StoreSlotAsValue(baseAbs, offset, ut, v);
                    return;
                }
            }

            if (t.Namespace == "System" && t.Name == "Single")
            {
                float f = v.Kind switch
                {
                    SlotKind.R8 => (float)v.AsR8Checked(),
                    SlotKind.I4 => (float)v.AsI4Checked(),
                    SlotKind.I8 => (float)v.AsI8Checked(),
                    _ => throw new InvalidOperationException($"Cannot store {v.Kind} into System.Single")
                };

                BinaryPrimitives.WriteInt32LittleEndian(_mem.AsSpan(abs, 4), BitConverter.SingleToInt32Bits(f));
                return;
            }

            if (t.Namespace == "System" && t.Name == "Double")
            {
                double d = v.Kind switch
                {
                    SlotKind.R8 => v.AsR8Checked(),
                    SlotKind.I4 => v.AsI4Checked(),
                    SlotKind.I8 => v.AsI8Checked(),
                    _ => throw new InvalidOperationException($"Cannot store {v.Kind} into System.Double")
                };

                BinaryPrimitives.WriteInt64LittleEndian(_mem.AsSpan(abs, 8), BitConverter.DoubleToInt64Bits(d));
                return;
            }
            switch (sz)
            {
                case 1:
                    _mem[abs] = unchecked((byte)v.AsI4Checked());
                    return;
                case 2:
                    BinaryPrimitives.WriteInt16LittleEndian(_mem.AsSpan(abs, 2), unchecked((short)v.AsI4Checked()));
                    return;
                case 4:
                    BinaryPrimitives.WriteInt32LittleEndian(_mem.AsSpan(abs, 4), v.AsI4Checked());
                    return;
                case 8:
                    BinaryPrimitives.WriteInt64LittleEndian(_mem.AsSpan(abs, 8), v.Kind == SlotKind.I8 ? v.Payload : v.AsI4Checked());
                    return;
                default:
                    throw new NotSupportedException($"Value type size {sz} store not supported.");
            }
        }

        private void CheckRange(int abs, int size)
        {
            if ((uint)abs > (uint)_mem.Length) throw new InvalidOperationException("OOB");
            if ((uint)size > (uint)_mem.Length) throw new InvalidOperationException("OOB");
            if ((uint)abs > (uint)(_mem.Length - size)) throw new InvalidOperationException("OOB");
        }

        private void CheckWritableRange(int abs, int size)
        {
            CheckRange(abs, size);
            if (abs < _metaEnd)
                throw new InvalidOperationException("Write to metadata region is forbidden.");
        }
        private int GetAddressAbsOrThrow(Slot a)
        {
            if (a.Kind == SlotKind.Ptr || a.Kind == SlotKind.ByRef)
                return checked((int)a.Payload);

            if (a.Kind == SlotKind.Null)
                throw new NullReferenceException("Null pointer dereference.");

            throw new InvalidOperationException($"Expected address, got {a.Kind}.");
        }

        private void CheckActiveStackAccess(int abs, int size, bool writable)
        {
            if ((uint)abs > (uint)_mem.Length) throw new InvalidOperationException("OOB");
            if ((uint)size > (uint)_mem.Length) throw new InvalidOperationException("OOB");
            if ((uint)abs > (uint)(_mem.Length - size)) throw new InvalidOperationException("OOB");


            if (abs < _metaEnd)
                throw new InvalidOperationException("Access to metadata region is forbidden.");

            if (abs < _stackBase || abs + size > _sp)
                throw new InvalidOperationException("Invalid stack access (inactive or out of range).");
        }
        private void CheckIndirectAccess(int abs, int size, bool writable)
        {
            if ((uint)abs > (uint)_mem.Length) throw new InvalidOperationException("OOB");
            if ((uint)size > (uint)_mem.Length) throw new InvalidOperationException("OOB");
            if ((uint)abs > (uint)(_mem.Length - size)) throw new InvalidOperationException("OOB");

            if (abs < _metaEnd)
                throw new InvalidOperationException("Access to metadata region is forbidden.");

            if (abs >= _heapBase)
            {
                CheckHeapAccess(abs, size, writable);
                return;
            }

            CheckActiveStackAccess(abs, size, writable);
        }
        private static int AlignUp(int v, int a) => (v + (a - 1)) & ~(a - 1);

        private void ExecNeg()
        {
            var v = PopSlot();

            switch (v.Kind)
            {
                case SlotKind.I4:
                    PushSlot(new Slot(SlotKind.I4, unchecked(-v.AsI4Checked())));
                    return;

                case SlotKind.I8:
                    PushSlot(new Slot(SlotKind.I8, unchecked(-v.AsI8Checked())));
                    return;

                case SlotKind.R8:
                    {
                        double d = -v.AsR8Checked();
                        PushSlot(new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(d)));
                        return;
                    }

                default:
                    throw new InvalidOperationException($"Neg not supported for {v.Kind}");
            }
        }
        private void ExecNot()
        {
            var v = PopSlot();

            switch (v.Kind)
            {
                case SlotKind.I4:
                    PushSlot(new Slot(SlotKind.I4, ~v.AsI4Checked()));
                    return;

                case SlotKind.I8:
                    PushSlot(new Slot(SlotKind.I8, ~v.AsI8Checked()));
                    return;

                default:
                    throw new InvalidOperationException($"Not not supported for {v.Kind}");
            }
        }
        private void ExecUnsignedDivide()
        {
            var b = PopSlot();
            var a = PopSlot();

            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                uint ra = unchecked((uint)a.AsI4Checked());
                uint rb = unchecked((uint)b.AsI4Checked());
                int res = unchecked((int)(ra / rb));
                PushSlot(new Slot(SlotKind.I4, res));
                return;
            }

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                ulong ra = unchecked((ulong)a.AsI8Checked());
                ulong rb = unchecked((ulong)b.AsI8Checked());
                ulong res = ra / rb;
                PushSlot(new Slot(SlotKind.I8, unchecked((long)res)));
                return;
            }

            throw new InvalidOperationException($"Unsigned numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private void ExecUnsignedRemeinder()
        {
            var b = PopSlot();
            var a = PopSlot();

            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                uint ra = unchecked((uint)a.AsI4Checked());
                uint rb = unchecked((uint)b.AsI4Checked());
                int res = unchecked((int)(ra % rb));
                PushSlot(new Slot(SlotKind.I4, res));
                return;
            }

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                ulong ra = unchecked((ulong)a.AsI8Checked());
                ulong rb = unchecked((ulong)b.AsI8Checked());
                ulong res = ra % rb;
                PushSlot(new Slot(SlotKind.I8, unchecked((long)res)));
                return;
            }

            throw new InvalidOperationException($"Unsigned numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private void ExecAdd()
        {
            var b = PopSlot();
            var a = PopSlot();
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, I4Unchecked(in a) + I4Unchecked(in b)));
                return;
            }
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                long res = a.AsI8Checked() + b.AsI8Checked();
                PushSlot(new Slot(SlotKind.I8, res));
                return;
            }
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8)
            {
                double res = a.AsR8Checked() + b.AsR8Checked();
                PushSlot(new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(res)));
                return;
            }
            throw new InvalidOperationException($"Numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private void ExecSubtract()
        {
            var b = PopSlot();
            var a = PopSlot();
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, I4Unchecked(in a) - I4Unchecked(in b)));
                return;
            }
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                long res = a.AsI8Checked() - b.AsI8Checked();
                PushSlot(new Slot(SlotKind.I8, res));
                return;
            }
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8)
            {
                double res = a.AsR8Checked() - b.AsR8Checked();
                PushSlot(new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(res)));
                return;
            }
            throw new InvalidOperationException($"Numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private void ExecMultiply()
        {
            var b = PopSlot();
            var a = PopSlot();
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, I4Unchecked(in a) * I4Unchecked(in b)));
                return;
            }
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                long res = a.AsI8Checked() * b.AsI8Checked();
                PushSlot(new Slot(SlotKind.I8, res));
                return;
            }
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8)
            {
                double res = a.AsR8Checked() * b.AsR8Checked();
                PushSlot(new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(res)));
                return;
            }
            throw new InvalidOperationException($"Numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private void ExecDivide()
        {
            var b = PopSlot();
            var a = PopSlot();
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, I4Unchecked(in a) / I4Unchecked(in b)));
                return;
            }
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                long res = a.AsI8Checked() / b.AsI8Checked();
                PushSlot(new Slot(SlotKind.I8, res));
                return;
            }
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8)
            {
                double res = a.AsR8Checked() / b.AsR8Checked();
                PushSlot(new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(res)));
                return;
            }
            throw new InvalidOperationException($"Numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private void ExecRemeinder()
        {
            var b = PopSlot();
            var a = PopSlot();
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, I4Unchecked(in a) % I4Unchecked(in b)));
                return;
            }
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                long res = a.AsI8Checked() % b.AsI8Checked();
                PushSlot(new Slot(SlotKind.I8, res));
                return;
            }
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8)
            {
                double res = a.AsR8Checked() % b.AsR8Checked();
                PushSlot(new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(res)));
                return;
            }
            throw new InvalidOperationException($"Numeric op type mismatch: {a.Kind} vs {b.Kind}");
        }
        private static int GetShiftCount32(Slot shift)
        {
            return shift.Kind switch
            {
                SlotKind.I4 => shift.AsI4Checked() & 31,
                SlotKind.I8 => unchecked((int)shift.AsI8Checked()) & 31,
                _ => throw new InvalidOperationException($"Shift count must be integral, got {shift.Kind}")
            };
        }

        private static int GetShiftCount64(Slot shift)
        {
            return shift.Kind switch
            {
                SlotKind.I4 => shift.AsI4Checked() & 63,
                SlotKind.I8 => unchecked((int)shift.AsI8Checked()) & 63,
                _ => throw new InvalidOperationException($"Shift count must be integral, got {shift.Kind}")
            };
        }

        private void ExecBitwiseAnd()
        {
            var b = PopSlot();
            var a = PopSlot();

            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, a.AsI4Checked() & b.AsI4Checked()));
                return;
            }

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                PushSlot(new Slot(SlotKind.I8, a.AsI8Checked() & b.AsI8Checked()));
                return;
            }

            throw new InvalidOperationException($"Bitwise AND type mismatch: {a.Kind} vs {b.Kind}");
        }

        private void ExecBitwiseOr()
        {
            var b = PopSlot();
            var a = PopSlot();

            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, a.AsI4Checked() | b.AsI4Checked()));
                return;
            }

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                PushSlot(new Slot(SlotKind.I8, a.AsI8Checked() | b.AsI8Checked()));
                return;
            }

            throw new InvalidOperationException($"Bitwise OR type mismatch: {a.Kind} vs {b.Kind}");
        }

        private void ExecBitwiseXor()
        {
            var b = PopSlot();
            var a = PopSlot();

            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
            {
                PushSlot(new Slot(SlotKind.I4, a.AsI4Checked() ^ b.AsI4Checked()));
                return;
            }

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
            {
                PushSlot(new Slot(SlotKind.I8, a.AsI8Checked() ^ b.AsI8Checked()));
                return;
            }

            throw new InvalidOperationException($"Bitwise XOR type mismatch: {a.Kind} vs {b.Kind}");
        }

        private void ExecShiftLeft()
        {
            var shift = PopSlot();
            var value = PopSlot();

            if (value.Kind == SlotKind.I4)
            {
                int count = GetShiftCount32(shift);
                PushSlot(new Slot(SlotKind.I4, unchecked(value.AsI4Checked() << count)));
                return;
            }

            if (value.Kind == SlotKind.I8)
            {
                int count = GetShiftCount64(shift);
                PushSlot(new Slot(SlotKind.I8, unchecked(value.AsI8Checked() << count)));
                return;
            }

            throw new InvalidOperationException($"Shift-left type not supported: {value.Kind}");
        }

        private void ExecShiftRight()
        {
            var shift = PopSlot();
            var value = PopSlot();

            if (value.Kind == SlotKind.I4)
            {
                int count = GetShiftCount32(shift);
                PushSlot(new Slot(SlotKind.I4, value.AsI4Checked() >> count));
                return;
            }

            if (value.Kind == SlotKind.I8)
            {
                int count = GetShiftCount64(shift);
                PushSlot(new Slot(SlotKind.I8, value.AsI8Checked() >> count));
                return;
            }

            throw new InvalidOperationException($"Shift-right type not supported: {value.Kind}");
        }

        private void ExecUnsignedShiftRight()
        {
            var shift = PopSlot();
            var value = PopSlot();

            if (value.Kind == SlotKind.I4)
            {
                int count = GetShiftCount32(shift);
                uint u = unchecked((uint)value.AsI4Checked());
                PushSlot(new Slot(SlotKind.I4, unchecked((int)(u >> count))));
                return;
            }

            if (value.Kind == SlotKind.I8)
            {
                int count = GetShiftCount64(shift);
                ulong u = unchecked((ulong)value.AsI8Checked());
                PushSlot(new Slot(SlotKind.I8, unchecked((long)(u >> count))));
                return;
            }

            throw new InvalidOperationException($"Unsigned shift-right type not supported: {value.Kind}");
        }
        private static int CompareLess(Slot a, Slot b)
        {
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4) return a.AsI4Checked() < b.AsI4Checked() ? 1 : 0;
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8) return a.AsI8Checked() < b.AsI8Checked() ? 1 : 0;
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8) return a.AsR8Checked() < b.AsR8Checked() ? 1 : 0;
            throw new InvalidOperationException($"Clt type mismatch: {a.Kind} vs {b.Kind}");
        }

        private static int CompareLessUnsigned(Slot a, Slot b)
        {
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
                return unchecked((uint)a.AsI4Checked()) < unchecked((uint)b.AsI4Checked()) ? 1 : 0;

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
                return unchecked((ulong)a.AsI8Checked()) < unchecked((ulong)b.AsI8Checked()) ? 1 : 0;

            throw new InvalidOperationException($"Clt_Un type mismatch: {a.Kind} vs {b.Kind}");
        }

        private static int CompareGreaterUnsigned(Slot a, Slot b)
        {
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4)
                return unchecked((uint)a.AsI4Checked()) > unchecked((uint)b.AsI4Checked()) ? 1 : 0;

            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8)
                return unchecked((ulong)a.AsI8Checked()) > unchecked((ulong)b.AsI8Checked()) ? 1 : 0;

            throw new InvalidOperationException($"Cgt_Un type mismatch: {a.Kind} vs {b.Kind}");
        }
        private static int CompareGreater(Slot a, Slot b)
        {
            if (a.Kind == SlotKind.I4 && b.Kind == SlotKind.I4) return a.AsI4Checked() > b.AsI4Checked() ? 1 : 0;
            if (a.Kind == SlotKind.I8 && b.Kind == SlotKind.I8) return a.AsI8Checked() > b.AsI8Checked() ? 1 : 0;
            if (a.Kind == SlotKind.R8 && b.Kind == SlotKind.R8) return a.AsR8Checked() > b.AsR8Checked() ? 1 : 0;
            throw new InvalidOperationException($"Cgt type mismatch: {a.Kind} vs {b.Kind}");
        }

        private int CompareEqual(Slot a, Slot b)
        {
            if (a.Kind == SlotKind.Null && b.Kind == SlotKind.Null) return 1;
            if (a.Kind != b.Kind) return 0;

            switch (a.Kind)
            {
                case SlotKind.I4:
                    return a.AsI4Checked() == b.AsI4Checked() ? 1 : 0;
                case SlotKind.I8:
                    return a.AsI8Checked() == b.AsI8Checked() ? 1 : 0;
                case SlotKind.R8:
                    return a.AsR8Checked() == b.AsR8Checked() ? 1 : 0;
                case SlotKind.Ptr:
                case SlotKind.ByRef:
                case SlotKind.Ref:
                    return a.Payload == b.Payload ? 1 : 0;
                case SlotKind.Value:
                    {
                        if (a.Aux != b.Aux)
                            return 0;

                        var t = _rts.GetTypeById(a.Aux);
                        int size = t.SizeOf;

                        int aAbs = checked((int)a.Payload);
                        int bAbs = checked((int)b.Payload);
                        CheckRange(aAbs, size);
                        CheckRange(bAbs, size);

                        // Nullable<T> equality
                        if (TryGetNullableInfo(t, out var underlying, out var hasValueField, out var valueField))
                        {
                            bool ha = LoadValueAsSlot(aAbs, hasValueField.Offset, hasValueField.FieldType).AsI4Checked() != 0;
                            bool hb = LoadValueAsSlot(bAbs, hasValueField.Offset, hasValueField.FieldType).AsI4Checked() != 0;

                            if (ha != hb)
                                return 0;
                            if (!ha)
                                return 1;

                            var av = LoadValueAsSlot(aAbs, valueField.Offset, underlying);
                            var bv = LoadValueAsSlot(bAbs, valueField.Offset, underlying);
                            return CompareEqual(av, bv);
                        }
                        return _mem.AsSpan(aAbs, size).SequenceEqual(_mem.AsSpan(bAbs, size)) ? 1 : 0;
                    }
                default:
                    throw new NotSupportedException($"Ceq not supported for {a.Kind}");
            }
        }

        private static bool ToBool(Slot v)
        {
            if (v.Kind == SlotKind.I4) return v.AsI4Checked() != 0;
            if (v.Kind == SlotKind.Null) return false;
            if (v.Kind == SlotKind.Ref) return v.Payload != 0;
            if (v.Kind == SlotKind.Ptr || v.Kind == SlotKind.ByRef) return v.Payload != 0;
            throw new InvalidOperationException($"Cannot convert {v.Kind} to bool");
        }

        private static Slot DoConv(Slot v, NumericConvKind kind, NumericConvFlags flags)
        {
            bool @checked = (flags & NumericConvFlags.Checked) != 0;
            bool srcUnsigned = (flags & NumericConvFlags.SourceUnsigned) != 0;

            static long AsSigned64(Slot s) => s.Kind switch
            {
                SlotKind.I4 => s.AsI4Checked(),
                SlotKind.I8 => s.AsI8Checked(),
                _ => throw new InvalidOperationException($"Conv source must be numeric, got {s.Kind}")
            };

            static ulong AsUnsigned64(Slot s) => s.Kind switch
            {
                SlotKind.I4 => unchecked((uint)s.AsI4Checked()),
                SlotKind.I8 => unchecked((ulong)s.AsI8Checked()),
                _ => throw new InvalidOperationException($"Conv source must be numeric, got {s.Kind}")
            };

            static Slot MakeR4(float value) =>
                new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits((double)value));

            static Slot MakeR8(double value) =>
                new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(value));



            try
            {
                if (v.Kind == SlotKind.R8)
                {
                    double d = v.AsR8Checked();

                    switch (kind)
                    {
                        case NumericConvKind.Char:
                            {
                                char r = @checked ? checked((char)d) : unchecked((char)d);
                                return new Slot(SlotKind.I4, r);
                            }

                        case NumericConvKind.Bool:
                            return new Slot(SlotKind.I4, d != 0.0 ? 1 : 0);

                        case NumericConvKind.I1:
                            {
                                sbyte r = @checked ? checked((sbyte)d) : unchecked((sbyte)d);
                                return new Slot(SlotKind.I4, r);
                            }

                        case NumericConvKind.U1:
                            {
                                byte r = @checked ? checked((byte)d) : unchecked((byte)d);
                                return new Slot(SlotKind.I4, r);
                            }

                        case NumericConvKind.I2:
                            {
                                short r = @checked ? checked((short)d) : unchecked((short)d);
                                return new Slot(SlotKind.I4, r);
                            }

                        case NumericConvKind.U2:
                            {
                                ushort r = @checked ? checked((ushort)d) : unchecked((ushort)d);
                                return new Slot(SlotKind.I4, r);
                            }

                        case NumericConvKind.I4:
                            {
                                int r = @checked ? checked((int)d) : unchecked((int)d);
                                return new Slot(SlotKind.I4, r);
                            }

                        case NumericConvKind.U4:
                            {
                                uint r = @checked ? checked((uint)d) : unchecked((uint)d);
                                return new Slot(SlotKind.I4, unchecked((int)r));
                            }

                        case NumericConvKind.I8:
                            {
                                long r = @checked ? checked((long)d) : unchecked((long)d);
                                return new Slot(SlotKind.I8, r);
                            }

                        case NumericConvKind.U8:
                            {
                                ulong r = @checked ? checked((ulong)d) : unchecked((ulong)d);
                                return new Slot(SlotKind.I8, unchecked((long)r));
                            }

                        case NumericConvKind.R4:
                            return MakeR4((float)d);

                        case NumericConvKind.R8:

                            return v;

                        default:
                            throw new NotSupportedException($"Conv {kind} not implemented");
                    }
                }

                long s64 = AsSigned64(v);
                ulong u64 = AsUnsigned64(v);

                switch (kind)
                {
                    case NumericConvKind.Char:
                        {
                            ushort r = srcUnsigned
                                ? (@checked ? checked((ushort)u64) : unchecked((ushort)u64))
                                : (@checked ? checked((ushort)s64) : unchecked((ushort)s64));
                            return new Slot(SlotKind.I4, (int)r);
                        }

                    case NumericConvKind.Bool:
                        {
                            bool r = srcUnsigned ? (u64 != 0) : (s64 != 0);
                            return new Slot(SlotKind.I4, r ? 1 : 0);
                        }

                    case NumericConvKind.I1:
                        {
                            sbyte r = srcUnsigned
                                ? (@checked ? checked((sbyte)u64) : unchecked((sbyte)u64))
                                : (@checked ? checked((sbyte)s64) : unchecked((sbyte)s64));
                            return new Slot(SlotKind.I4, (int)r);
                        }

                    case NumericConvKind.U1:
                        {
                            byte r = srcUnsigned
                                ? (@checked ? checked((byte)u64) : unchecked((byte)u64))
                                : (@checked ? checked((byte)s64) : unchecked((byte)s64));
                            return new Slot(SlotKind.I4, (int)r);
                        }

                    case NumericConvKind.I2:
                        {
                            short r = srcUnsigned
                                ? (@checked ? checked((short)u64) : unchecked((short)u64))
                                : (@checked ? checked((short)s64) : unchecked((short)s64));
                            return new Slot(SlotKind.I4, (int)r);
                        }

                    case NumericConvKind.U2:
                        {
                            ushort r = srcUnsigned
                                ? (@checked ? checked((ushort)u64) : unchecked((ushort)u64))
                                : (@checked ? checked((ushort)s64) : unchecked((ushort)s64));
                            return new Slot(SlotKind.I4, (int)r);
                        }

                    case NumericConvKind.I4:
                        {
                            int r = srcUnsigned
                                ? (@checked ? checked((int)u64) : unchecked((int)u64))
                                : (@checked ? checked((int)s64) : unchecked((int)s64));
                            return new Slot(SlotKind.I4, r);
                        }

                    case NumericConvKind.U4:
                        {
                            uint r = srcUnsigned
                                ? (@checked ? checked((uint)u64) : unchecked((uint)u64))
                                : (@checked ? checked((uint)s64) : unchecked((uint)s64));
                            return new Slot(SlotKind.I4, unchecked((int)r));
                        }

                    case NumericConvKind.I8:
                        {
                            long r = srcUnsigned
                                ? (@checked ? checked((long)u64) : unchecked((long)u64))
                                : s64;
                            return new Slot(SlotKind.I8, r);
                        }

                    case NumericConvKind.U8:
                        {
                            ulong r = srcUnsigned
                                ? u64
                                : (@checked ? checked((ulong)s64) : unchecked((ulong)s64));
                            return new Slot(SlotKind.I8, unchecked((long)r));
                        }

                    case NumericConvKind.R4:
                        {
                            float r = srcUnsigned ? (float)u64 : (float)s64;
                            return MakeR4(r);
                        }

                    case NumericConvKind.R8:
                        {
                            double r = srcUnsigned ? (double)u64 : (double)s64;
                            return MakeR8(r);
                        }

                    case NumericConvKind.NativeInt:
                        {
                            int PointerSize = RuntimeTypeSystem.PointerSize;
                            if (PointerSize == 8)
                            {
                                long r = srcUnsigned
                                    ? (@checked ? checked((long)u64) : unchecked((long)u64))
                                    : s64;
                                return new Slot(SlotKind.I8, r);
                            }

                            int r32 = srcUnsigned
                                ? (@checked ? checked((int)u64) : unchecked((int)u64))
                                : (@checked ? checked((int)s64) : unchecked((int)s64));
                            return new Slot(SlotKind.I4, r32);
                        }

                    case NumericConvKind.NativeUInt:
                        {
                            int PointerSize = RuntimeTypeSystem.PointerSize;
                            if (PointerSize == 8)
                            {
                                ulong r = srcUnsigned
                                    ? u64
                                    : (@checked ? checked((ulong)s64) : unchecked((ulong)s64));
                                return new Slot(SlotKind.I8, unchecked((long)r));
                            }

                            uint r32 = srcUnsigned
                                ? (@checked ? checked((uint)u64) : unchecked((uint)u64))
                                : (@checked ? checked((uint)s64) : unchecked((uint)s64));
                            return new Slot(SlotKind.I4, unchecked((int)r32));
                        }

                    default:
                        throw new NotSupportedException($"Conv {kind} not implemented");
                }
            }
            catch (OverflowException) when (@checked)
            {
                throw;
            }
        }
        private (int size, int align) GetStorageSizeAlign(RuntimeType t)
        {
            return _rts.GetStorageSizeAlign(t);
        }
        private RuntimeMethod ResolveVirtualDispatch(RuntimeType receiverType, RuntimeMethod declared)
        {
            if (receiverType is null) throw new ArgumentNullException(nameof(receiverType));
            if (declared is null) throw new ArgumentNullException(nameof(declared));

            if (declared.DeclaringType.Kind == RuntimeTypeKind.Interface)
            {
                var m = FindMostDerivedMethodByNameAndSig(receiverType, declared);
                if (m is null)
                    throw new MissingMethodException(
                        $"Interface method not implemented: {declared.DeclaringType.Namespace}." +
                        $"{declared.DeclaringType.Name}.{declared.Name}");
                return m;
            }
            // Class virtual dispatch
            int slot = declared.VTableSlot;
            if (slot >= 0 && (uint)slot < (uint)receiverType.VTable.Length)
                return receiverType.VTable[slot];

            // Fallback
            return FindMostDerivedMethodByNameAndSig(receiverType, declared) ?? declared;
        }
        private static RuntimeMethod? FindMostDerivedMethodByNameAndSig(RuntimeType receiverType, RuntimeMethod declared)
        {
            for (var t = receiverType; t != null; t = t.BaseType)
            {
                var ms = t.Methods;
                for (int i = 0; i < ms.Length; i++)
                {
                    var cand = ms[i];
                    if (cand.IsStatic) continue;
                    if (!StringComparer.Ordinal.Equals(cand.Name, declared.Name)) continue;
                    if (!SameSig(cand, declared)) continue;
                    return cand;
                }
            }
            return null;
            static bool SameSig(RuntimeMethod a, RuntimeMethod b)
            {
                if (!ReferenceEquals(a.ReturnType, b.ReturnType)) return false;
                if (a.ParameterTypes.Length != b.ParameterTypes.Length) return false;
                if (a.GenericArity != b.GenericArity) return false;
                for (int i = 0; i < a.ParameterTypes.Length; i++)
                    if (!ReferenceEquals(a.ParameterTypes[i], b.ParameterTypes[i])) return false;
                return true;
            }
        }
        private RuntimeMethod ResolveRuntimeMethodOrThrow(RuntimeModule mod, int methodToken)
            => ResolveRuntimeMethodOrThrow(mod, methodToken, methodContext: null);
        private RuntimeMethod ResolveRuntimeMethodOrThrow(RuntimeModule mod, int methodToken, RuntimeMethod? methodContext)
        {
            try
            {
                return _rts.ResolveMethodInMethodContext(mod, methodToken, methodContext);
            }
            catch (Exception ex)
            {
                throw new MissingMethodException($"RuntimeMethod not found: {mod.Name} 0x{methodToken:X8}", ex);
            }
        }
        private RuntimeType GetValueSlotType(Slot v)
        {
            if (v.Kind != SlotKind.Value)
                throw new InvalidOperationException($"Expected Value slot, got {v.Kind}");
            return _rts.GetTypeById(v.Aux);
        }
        private int GetValueSlotSize(Slot v)
        {
            var t = GetValueSlotType(v);
            return t.SizeOf;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureHotEvalCapacity(int needed)
        {
            if (_hotEvalSlots.Length >= needed)
                return;

            Array.Resize(ref _hotEvalSlots, Math.Max(needed, _hotEvalSlots.Length == 0 ? 8 : _hotEvalSlots.Length * 2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int I4Unchecked(in Slot s) => unchecked((int)s.Payload);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long I8Unchecked(in Slot s) => s.Payload;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double R8Unchecked(in Slot s) => BitConverter.Int64BitsToDouble(s.Payload);
        private ref byte Mem0 => ref MemoryMarshal.GetArrayDataReference(_mem);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadI32Safe(int abs)
        {
            CheckRange(abs, 4);
            return Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref Mem0, abs));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteI32Safe(int abs, int v)
        {
            CheckWritableRange(abs, 4);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), v);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadI32(int abs) =>
            Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteI32(int abs, int v) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private short ReadI16(int abs) =>
            Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadU16(int abs) =>
            Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref Mem0, abs));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteI16(int abs, short v) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteU16(int abs, ushort v) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteI64(int abs, long v) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadI64(int abs) =>
            Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref Mem0, abs));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Slot LoadFastCellAsSlot(FastCellKind kind, int abs)
        {
            return kind switch
            {
                FastCellKind.I4 => new Slot(SlotKind.I4, ReadI32(abs)),
                FastCellKind.I8 => new Slot(SlotKind.I8, ReadI64(abs)),
                FastCellKind.R8 => new Slot(SlotKind.R8, ReadI64(abs)),
                _ => throw new InvalidOperationException("Not a fast cell.")
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryStoreFastCell(FastCellKind kind, int abs, Slot v)
        {
            switch (kind)
            {
                case FastCellKind.I4:
                    if (v.Kind != SlotKind.I4)
                        throw new InvalidOperationException($"Cannot store {v.Kind} into Int32/UInt32 local/arg.");
                    WriteI32(abs, unchecked((int)v.Payload));
                    return true;

                case FastCellKind.I8:
                    {
                        long x = v.Kind switch
                        {
                            SlotKind.I8 => v.Payload,
                            SlotKind.I4 => unchecked((int)v.Payload),
                            _ => throw new InvalidOperationException($"Cannot store {v.Kind} into Int64/UInt64 local/arg.")
                        };
                        WriteI64(abs, x);
                        return true;
                    }

                case FastCellKind.R8:
                    {
                        double d = v.Kind switch
                        {
                            SlotKind.R8 => BitConverter.Int64BitsToDouble(v.Payload),
                            SlotKind.I4 => unchecked((int)v.Payload),
                            SlotKind.I8 => v.Payload,
                            _ => throw new InvalidOperationException($"Cannot store {v.Kind} into Double local/arg.")
                        };
                        WriteI64(abs, BitConverter.DoubleToInt64Bits(d));
                        return true;
                    }

                default:
                    return false;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SpillCurrentFrameHotState()
        {
            if (_frameBase < 0)
                return;

            WriteI32(_frameBase + 24, _curPc);
            WriteI32(_frameBase + 32, _curEvalSp);

            int dst = _curEvalBase;
            for (int i = 0; i < _curEvalSp; i++)
            {
                WriteSlot(dst, _hotEvalSlots[i]);
                dst += SlotSize;
            }
        }
        private void RefreshCurrentFrameCache()
        {
            if (_frameBase < 0)
            {
                _curModule = null;
                _curFn = null;
                _curLayout = null;
                _curArgsAbs = _curLocalsAbs = _curEvalBase = _curEvalSp = _curEvalMax = 0;
                return;
            }

            int methodTok = ReadI32(_frameBase + 16);
            int moduleId = ReadI32(_frameBase + 20);

            if ((uint)moduleId >= (uint)_moduleById.Length)
                throw new InvalidOperationException($"Bad moduleId in frame: {moduleId}");

            var mod = _moduleById[moduleId];
            if (!mod.MethodsByDefToken.TryGetValue(methodTok, out var fn))
                throw new MissingMethodException($"Current frame method not found: {mod.Name} 0x{methodTok:X8}");
            int runtimeMethodId = ReadI32(_frameBase + 72);
            RuntimeMethod? rm = runtimeMethodId != 0 ? _rts.GetMethodById(runtimeMethodId) : null;
            _curModule = mod;
            _curFn = fn;
            _curLayout = GetOrCreateMethodLayout(mod, fn, rm);
            _curPc = ReadI32(_frameBase + 24);
            _curArgsAbs = ReadI32(_frameBase + 40);
            _curLocalsAbs = ReadI32(_frameBase + 44);
            _curEvalBase = ReadI32(_frameBase + 28);
            _curEvalSp = ReadI32(_frameBase + 32);
            _curEvalMax = ReadI32(_frameBase + 36);

            EnsureHotEvalCapacity(_curEvalMax);

            int src = _curEvalBase;
            for (int i = 0; i < _curEvalSp; i++)
            {
                _hotEvalSlots[i] = ReadSlot(src);
                src += SlotSize;
            }
        }
        private MethodExecLayout GetOrCreateMethodLayout(RuntimeModule mod, BytecodeFunction fn, RuntimeMethod? runtimeMethod = null)
        {
            var rm = runtimeMethod ?? ResolveRuntimeMethodOrThrow(mod, fn.MethodToken);

            if (_methodLayouts.TryGetValue(rm.MethodId, out var cached))
                return cached;

            var layout = new MethodExecLayout();
            layout.Method = rm;

            int argCount = rm.HasThis ? 1 + rm.ParameterTypes.Length : rm.ParameterTypes.Length;
            layout.ArgTypes = new RuntimeType[argCount];
            layout.ArgOffsets = new int[argCount];
            layout.ArgSizes = new int[argCount];
            layout.ArgFastKinds = new FastCellKind[argCount];

            int cur = 0;
            for (int i = 0; i < argCount; i++)
            {
                var t = GetArgType(rm, i);
                var (sz, al) = GetStorageSizeAlign(t);
                cur = AlignUp(cur, al);

                layout.ArgTypes[i] = t;
                layout.ArgOffsets[i] = cur;
                layout.ArgSizes[i] = sz;
                layout.ArgFastKinds[i] = ClassifyFastCell(t);

                cur = checked(cur + sz);
            }
            layout.ArgsAreaSize = cur;

            int localCount = fn.LocalTypeTokens.Length;
            layout.LocalTypes = new RuntimeType[localCount];
            layout.LocalOffsets = new int[localCount];
            layout.LocalSizes = new int[localCount];
            layout.LocalFastKinds = new FastCellKind[localCount];

            cur = 0;
            for (int i = 0; i < localCount; i++)
            {
                var t = _rts.ResolveTypeInMethodContext(mod, fn.LocalTypeTokens[i], rm);
                var (sz, al) = GetStorageSizeAlign(t);
                cur = AlignUp(cur, al);

                layout.LocalTypes[i] = t;
                layout.LocalOffsets[i] = cur;
                layout.LocalSizes[i] = sz;
                layout.LocalFastKinds[i] = ClassifyFastCell(t);

                cur = checked(cur + sz);
            }
            layout.LocalsAreaSize = cur;

            _methodLayouts.Add(rm.MethodId, layout);
            return layout;
        }
        private static FastCellKind ClassifyFastCell(RuntimeType t)
        {
            if (t.IsReferenceType || t.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                return FastCellKind.None;

            if (t.Kind == RuntimeTypeKind.Enum)
            {
                var ut = TryGetEnumUnderlyingType(t);
                if (ut != null)
                    t = ut;
            }

            if (t.Namespace != "System")
                return FastCellKind.None;

            return t.Name switch
            {
                "Int32" or "UInt32" => FastCellKind.I4,
                "Int64" or "UInt64" => FastCellKind.I8,
                "Double" => FastCellKind.R8,
                "IntPtr" or "UIntPtr" => RuntimeTypeSystem.PointerSize == 8
                                            ? FastCellKind.I8 : FastCellKind.I4,
                _ => FastCellKind.None
            };
        }
        private static bool IsNoRefPrimitive(RuntimeType t)
        {
            if (t.Namespace != "System")
                return false;

            switch (t.Name)
            {
                case "Void":
                case "Boolean":
                case "Char":
                case "SByte":
                case "Byte":
                case "Int16":
                case "UInt16":
                case "Int32":
                case "UInt32":
                case "Int64":
                case "UInt64":
                case "Single":
                case "Double":
                case "Decimal":
                case "IntPtr":
                case "UIntPtr":
                    return true;
                default:
                    return false;
            }
        }
        private bool TypeIsReferenceOrContainsReferences(RuntimeType t)
        {
            if (t is null)
                return true;
            if (t.IsReferenceType)
                return true;
            if (t.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                return false;
            if (IsNoRefPrimitive(t))
                return false;
            if (t.Kind == RuntimeTypeKind.TypeParam)
                return true;

            // cycle guard
            var visiting = new HashSet<int>();
            return TypeIsReferenceOrContainsReferencesCore(t, visiting);
        }
        private bool TypeIsReferenceOrContainsReferencesCore(RuntimeType t, HashSet<int> visiting)
        {
            if (t is null)
                return true;

            if (t.IsReferenceType)
                return true;

            if (t.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                return false;

            if (IsNoRefPrimitive(t))
                return false;

            if (t.Kind == RuntimeTypeKind.TypeParam)
                return true;

            if (!visiting.Add(t.TypeId))
                return true;

            _ = _rts.GetStorageSizeAlign(t);

            bool contains = false;

            if (t.IsValueType)
            {
                var fs = t.InstanceFields;
                for (int i = 0; i < fs.Length; i++)
                {
                    if (TypeIsReferenceOrContainsReferencesCore(fs[i].FieldType, visiting))
                    {
                        contains = true;
                        break;
                    }
                }
            }
            else
            {
                contains = true;
            }

            visiting.Remove(t.TypeId);
            return contains;
        }
        private static RuntimeType? TryGetEnumUnderlyingType(RuntimeType t)
        {
            if (t.Kind != RuntimeTypeKind.Enum)
                return null;

            if (t.ElementType != null)
                return t.ElementType;

            for (int i = 0; i < t.InstanceFields.Length; i++)
            {
                var f = t.InstanceFields[i];
                if (f.Name == "value__")
                    return f.FieldType;
            }

            return t.InstanceFields.Length > 0 ? t.InstanceFields[0].FieldType : null;
        }
        private static bool IsVoidReturn(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Void";

        private Slot NormalizeReturnValue(RuntimeType t, VmValue v)
        {
            // ref
            if (t.IsReferenceType)
            {
                if (v.Kind == VmValueKind.Null) return new Slot(SlotKind.Null, 0);
                if (v.Kind == VmValueKind.Ref) return new Slot(SlotKind.Ref, v.Payload);
                throw new InvalidOperationException($"Return type mismatch: expected managed ref, got {v.Kind}");
            }

            // ptr / byref
            if (t.Kind == RuntimeTypeKind.Pointer)
            {
                if (v.Kind == VmValueKind.Null) return new Slot(SlotKind.Null, 0);
                if (v.Kind == VmValueKind.Ptr) return new Slot(SlotKind.Ptr, v.Payload, v.Aux);
                throw new InvalidOperationException($"Return type mismatch: expected ptr, got {v.Kind}");
            }
            if (t.Kind == RuntimeTypeKind.ByRef)
            {
                if (v.Kind == VmValueKind.Null) return new Slot(SlotKind.Null, 0);
                if (v.Kind == VmValueKind.ByRef) return new Slot(SlotKind.ByRef, v.Payload, v.Aux);
                throw new InvalidOperationException($"Return type mismatch: expected byref, got {v.Kind}");
            }

            // scalar value types
            if (t.Namespace == "System")
            {
                switch (t.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return new Slot(SlotKind.I4, v.AsInt32());

                    case "Int64":
                    case "UInt64":
                        return new Slot(SlotKind.I8, v.AsInt64());

                    case "Single":
                    case "Double":
                        return new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits(v.AsDouble()));

                    case "IntPtr":
                    case "UIntPtr":
                        return RuntimeTypeSystem.PointerSize == 8
                            ? new Slot(SlotKind.I8, v.AsInt64())
                            : new Slot(SlotKind.I4, v.AsInt32());
                }
            }
            throw new NotSupportedException($"Host return marshal not supported for value type: {t.Namespace}.{t.Name}");
        }
        internal string? HostReadString(VmValue v, CancellationToken ct)
        {
            if (v.Kind == VmValueKind.Null) return null;

            var s = v.ToSlot();
            ValidateStringRef(s, out int strObjAbs);

            int len = GetStringLengthFromObject(strObjAbs);
            int charsAbs = GetStringCharsAbs(strObjAbs);

            var chars = new char[len];
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
            int abs = AllocStringFromManaged(s);
            return new VmValue(VmValueKind.Ref, abs);
        }
        internal VmValue HostAllocStringArray(RuntimeType arrayType, ReadOnlySpan<string?> values)
        {
            if (arrayType.Kind != RuntimeTypeKind.Array)
                throw new ArgumentException("Type is not an array.", nameof(arrayType));
            if (arrayType.ElementType is null)
                throw new ArgumentException("Array type has no element type.", nameof(arrayType));
            if (arrayType.ElementType.TypeId != _rts.SystemString.TypeId)
                throw new NotSupportedException("Only string[] is supported for host array allocation.");

            int arrAbs = AllocArrayObject(arrayType, values.Length);

            var elemType = arrayType.ElementType;
            var (elemSize, _) = GetStorageSizeAlign(elemType);

            for (int i = 0; i < values.Length; i++)
            {
                int elemAbs = checked(arrAbs + ArrayDataOffset + checked(i * elemSize));
                if (values[i] is null)
                {
                    StoreSlotAsValue(elemAbs, 0, elemType, new Slot(SlotKind.Null, 0));
                }
                else
                {
                    int strAbs = AllocStringFromManaged(values[i]!);
                    StoreSlotAsValue(elemAbs, 0, elemType, new Slot(SlotKind.Ref, strAbs));
                }
            }

            return new VmValue(VmValueKind.Ref, arrAbs);
        }
        internal int HostGetAddress(VmValue v)
        {
            return GetAddressAbsOrThrow(v.ToSlot());
        }

        internal Span<byte> HostGetSpan(int abs, int size, bool writable)
        {
            CheckIndirectAccess(abs, size, writable);
            return _mem.AsSpan(abs, size);
        }
        internal void RegisterHostOverride(HostOverride ov)
        {
            if (ov is null) throw new ArgumentNullException(nameof(ov));
            _hostOverrides[ov.Method.MethodId] = ov;
        }
        private bool TryInvokeHostOverride(RuntimeMethod rm, int totalArgs, CancellationToken ct)
        {
            if (!_hostOverrides.TryGetValue(rm.MethodId, out var ov))
                return false;

            if (!rm.IsStatic || rm.HasThis)
                return false;

            Span<VmValue> args = totalArgs <= 32
                ? stackalloc VmValue[totalArgs]
                : new VmValue[totalArgs];

            for (int i = totalArgs - 1; i >= 0; i--)
                args[i] = new VmValue(PopSlot());

            _hostCtx.SetToken(ct);

            try
            {
                VmValue ret = ov.Handler(_hostCtx, args);

                if (!IsVoidReturn(rm.ReturnType))
                {
                    var retSlot = NormalizeReturnValue(rm.ReturnType, ret);
                    PushSlot(retSlot);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (TryTranslateHostExceptionToVm(ex, out var vmEx))
                {
                    ThrowException(vmEx, throwPc: _curPc - 1);
                    return true;
                }
                throw;
            }
        }
        private bool TryInvokeIntrinsic(RuntimeMethod rm, int totalArgs, CancellationToken ct)
        {
            if (rm.DeclaringType.Namespace == "System" && rm.DeclaringType.Name == "Array")
            {
                // instance int get_Length()
                if (rm.HasThis &&
                    rm.Name == "get_Length" &&
                    rm.ParameterTypes.Length == 0 &&
                    totalArgs == 1 &&
                    rm.ReturnType.Namespace == "System" && rm.ReturnType.Name == "Int32")
                {
                    var arr = PopSlot();
                    // callvirt on null should already have faulted, but keep consistent
                    var t = GetObjectTypeFromRef(arr);
                    if (t.Kind != RuntimeTypeKind.Array)
                        throw new InvalidOperationException($"System.Array.get_Length expects array, got {t.Namespace}.{t.Name}");

                    int arrAbs = checked((int)arr.Payload);
                    int len = GetArrayLengthFromObject(arrAbs);
                    PushSlot(new Slot(SlotKind.I4, len));
                    return true;
                }
                // private static void ClearInternal(Array, int, int)
                if (!rm.HasThis &&
                    rm.Name == "ClearInternal" &&
                    rm.ParameterTypes.Length == 3 &&
                    totalArgs == 3 &&
                    IsVoidReturn(rm.ReturnType) &&
                    rm.ParameterTypes[0].Namespace == "System" && rm.ParameterTypes[0].Name == "Array" &&
                    rm.ParameterTypes[1].Namespace == "System" && rm.ParameterTypes[1].Name == "Int32" &&
                    rm.ParameterTypes[2].Namespace == "System" && rm.ParameterTypes[2].Name == "Int32")
                {
                    int length = PopSlot().AsI4Checked();
                    int index = PopSlot().AsI4Checked();
                    var arrRef = PopSlot();

                    if (arrRef.Kind == SlotKind.Null)
                        throw new NullReferenceException();

                    var arrType = GetObjectTypeFromRef(arrRef);
                    if (arrType.Kind != RuntimeTypeKind.Array)
                        throw new InvalidOperationException("System.Array._ClearImpl expects array.");

                    if (arrType.ArrayRank != 1)
                        throw new NotSupportedException("Only single dimensional arrays are supported.");

                    if (length <= 0)
                        return true;

                    int arrAbs = checked((int)arrRef.Payload);
                    var elem = arrType.ElementType ?? throw new InvalidOperationException("Corrupted array type (no ElementType).");
                    var (elemSize, _) = GetStorageSizeAlign(elem);

                    int bytes = checked(length * elemSize);
                    int start = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));

                    CheckRange(start, bytes);
                    CheckWritableRange(start, bytes);
                    _mem.AsSpan(start, bytes).Clear();

                    return true;
                }
                // private static bool _CopyImpl(Array, int, Array, int, int)
                if (!rm.HasThis &&
                rm.Name == "CopyInternal" &&
                rm.ParameterTypes.Length == 5 &&
                totalArgs == 5 &&
                rm.ReturnType.Namespace == "System" && rm.ReturnType.Name == "Boolean" &&
                rm.ParameterTypes[0].Namespace == "System" && rm.ParameterTypes[0].Name == "Array" &&
                rm.ParameterTypes[1].Namespace == "System" && rm.ParameterTypes[1].Name == "Int32" &&
                rm.ParameterTypes[2].Namespace == "System" && rm.ParameterTypes[2].Name == "Array" &&
                rm.ParameterTypes[3].Namespace == "System" && rm.ParameterTypes[3].Name == "Int32" &&
                rm.ParameterTypes[4].Namespace == "System" && rm.ParameterTypes[4].Name == "Int32")
                {
                    int length = PopSlot().AsI4Checked();
                    int dstIndex = PopSlot().AsI4Checked();
                    var dstArr = PopSlot();
                    int srcIndex = PopSlot().AsI4Checked();
                    var srcArr = PopSlot();

                    // Wrapper already validated args & bounds, but keep hard safety
                    if (length < 0 || srcIndex < 0 || dstIndex < 0)
                    {
                        PushSlot(new Slot(SlotKind.I4, 0));
                        return true;
                    }

                    var srcArrType = GetObjectTypeFromRef(srcArr);
                    var dstArrType = GetObjectTypeFromRef(dstArr);
                    if (srcArrType.Kind != RuntimeTypeKind.Array || dstArrType.Kind != RuntimeTypeKind.Array)
                    {
                        PushSlot(new Slot(SlotKind.I4, 0));
                        return true;
                    }

                    if (srcArrType.ArrayRank != 1 || dstArrType.ArrayRank != 1)
                    {
                        PushSlot(new Slot(SlotKind.I4, 0));
                        return true;
                    }

                    int srcAbs = checked((int)srcArr.Payload);
                    int dstAbs = checked((int)dstArr.Payload);

                    int srcLen = GetArrayLengthFromObject(srcAbs);
                    int dstLen = GetArrayLengthFromObject(dstAbs);

                    if ((uint)srcIndex > (uint)srcLen || (uint)dstIndex > (uint)dstLen)
                    {
                        PushSlot(new Slot(SlotKind.I4, 0));
                        return true;
                    }
                    if (srcLen - srcIndex < length || dstLen - dstIndex < length)
                    {
                        PushSlot(new Slot(SlotKind.I4, 0));
                        return true;
                    }

                    if (length == 0)
                    {
                        PushSlot(new Slot(SlotKind.I4, 1));
                        return true;
                    }

                    var srcElem = srcArrType.ElementType ?? throw new InvalidOperationException("Corrupted array type (no ElementType).");
                    var dstElem = dstArrType.ElementType ?? throw new InvalidOperationException("Corrupted array type (no ElementType).");

                    bool ok = false;

                    // Exact same element type
                    if (srcElem.TypeId == dstElem.TypeId)
                    {
                        var (elemSize, _) = GetStorageSizeAlign(srcElem);
                        int bytes = checked(length * elemSize);

                        int srcStart = checked(srcAbs + ArrayDataOffset + checked(srcIndex * elemSize));
                        int dstStart = checked(dstAbs + ArrayDataOffset + checked(dstIndex * elemSize));

                        CheckRange(srcStart, bytes);
                        CheckWritableRange(dstStart, bytes);
                        //MarkHeapWriteIfInsideHeap(dstStart, bytes);

                        _mem.AsSpan(srcStart, bytes).CopyTo(_mem.AsSpan(dstStart, bytes));
                        ok = true;
                    }
                    // Reference arrays
                    else if (srcElem.IsReferenceType && dstElem.IsReferenceType)
                    {
                        // srcElem assignable to dstElem
                        if (IsAssignableTo(srcElem, dstElem))
                        {
                            int ptrSize = RuntimeTypeSystem.PointerSize;
                            int bytes = checked(length * ptrSize);

                            int srcStart = checked(srcAbs + ArrayDataOffset + checked(srcIndex * ptrSize));
                            int dstStart = checked(dstAbs + ArrayDataOffset + checked(dstIndex * ptrSize));

                            CheckRange(srcStart, bytes);
                            CheckWritableRange(dstStart, bytes);
                            //MarkHeapWriteIfInsideHeap(dstStart, bytes);

                            _mem.AsSpan(srcStart, bytes).CopyTo(_mem.AsSpan(dstStart, bytes));
                            ok = true;
                        }
                        // dstElem more derived
                        else if (IsAssignableTo(dstElem, srcElem))
                        {
                            int ptrSize = RuntimeTypeSystem.PointerSize;
                            int srcStart = checked(srcAbs + ArrayDataOffset + checked(srcIndex * ptrSize));

                            for (int i = 0; i < length; i++)
                            {
                                if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested();

                                int cellAbs = checked(srcStart + checked(i * ptrSize));
                                long h = ReadNativeInt(cellAbs);
                                if (h == 0)
                                    continue;

                                var actual = GetObjectTypeFromRef(new Slot(SlotKind.Ref, h));
                                if (!IsAssignableTo(actual, dstElem))
                                {
                                    ok = false;
                                    goto DoneCopy;
                                }
                            }

                            // memmove refs
                            {
                                int bytes = checked(length * ptrSize);
                                int dstStart = checked(dstAbs + ArrayDataOffset + checked(dstIndex * ptrSize));

                                CheckRange(srcStart, bytes);
                                CheckWritableRange(dstStart, bytes);
                                //MarkHeapWriteIfInsideHeap(dstStart, bytes);

                                _mem.AsSpan(srcStart, bytes).CopyTo(_mem.AsSpan(dstStart, bytes));
                                ok = true;
                            }
                        }
                        else
                        {
                            ok = false;
                        }
                    }
                    // Value[] -> ref[]
                    else if (srcElem.IsValueType && dstElem.IsReferenceType && IsAssignableTo(srcElem, dstElem))
                    {
                        var (srcElemSize, _) = GetStorageSizeAlign(srcElem);
                        int ptrSize = RuntimeTypeSystem.PointerSize;

                        int srcBase = checked(srcAbs + ArrayDataOffset + checked(srcIndex * srcElemSize));
                        int dstBase = checked(dstAbs + ArrayDataOffset + checked(dstIndex * ptrSize));

                        for (int i = 0; i < length; i++)
                        {
                            if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested();

                            int srcElemAbs = checked(srcBase + checked(i * srcElemSize));
                            Slot val = LoadValueAsSlot(srcElemAbs, 0, srcElem);

                            int boxedAbs = AllocBoxedValueObject(srcElem);
                            int payloadAbs = GetBoxedValuePayloadAbs(boxedAbs);
                            StoreSlotAsValue(payloadAbs, 0, srcElem, val);

                            int dstCellAbs = checked(dstBase + checked(i * ptrSize));
                            CheckWritableRange(dstCellAbs, ptrSize);
                            //MarkHeapWriteIfInsideHeap(dstCellAbs, ptrSize);
                            WriteNativeInt(dstCellAbs, boxedAbs);
                        }

                        ok = true;
                    }
                    else
                    {
                        ok = false;
                    }

                DoneCopy:
                    PushSlot(new Slot(SlotKind.I4, ok ? 1 : 0));
                    return true;
                }
            }
            if (rm.DeclaringType.Namespace == "System.Runtime.CompilerServices" &&
                rm.DeclaringType.Name == "RuntimeHelpers")
            {
                // static bool IsReferenceOrContainsReferences<T>()
                if (!rm.HasThis &&
                    rm.Name == "IsReferenceOrContainsReferences" &&
                    rm.ParameterTypes.Length == 0 &&
                    totalArgs == 0 &&
                    rm.ReturnType.Namespace == "System" &&
                    rm.ReturnType.Name == "Boolean")
                {
                    bool result;
                    if (rm.MethodGenericArguments.Length == 1)
                    {
                        result = TypeIsReferenceOrContainsReferences(rm.MethodGenericArguments[0]);
                    }
                    else
                    {
                        // Unconstructed generic
                        result = true;
                    }

                    PushSlot(new Slot(SlotKind.I4, result ? 1 : 0));
                    return true;
                }
            }
            if (rm.DeclaringType.Namespace == "System" && rm.DeclaringType.Name == "Number")
            {
                // private static string _DoubleToStringImpl(double)
                if (!rm.HasThis &&
                    rm.Name == "_DoubleToStringImpl" &&
                    rm.ParameterTypes.Length == 1 &&
                    totalArgs == 1 &&
                    rm.ParameterTypes[0].Namespace == "System" && rm.ParameterTypes[0].Name == "Double" &&
                    IsSystemStringType(rm.ReturnType))
                {
                    double d = PopSlot().AsR8Checked();

                    // Invariant formatting
                    string s = d.ToString("G", CultureInfo.InvariantCulture);

                    int strAbs = AllocStringFromManaged(s);
                    PushSlot(new Slot(SlotKind.Ref, strAbs));
                    return true;
                }
            }
            static bool IsCoreLibRandomLike(RuntimeType t)
            {
                if (!StringComparer.Ordinal.Equals(t.AssemblyName, "std"))
                    return false;

                for (var cur = t; cur != null; cur = cur.BaseType)
                {
                    if (StringComparer.Ordinal.Equals(cur.AssemblyName, "std") &&
                        StringComparer.Ordinal.Equals(cur.Namespace, "System") &&
                        StringComparer.Ordinal.Equals(cur.Name, "Random"))
                        return true;
                }
                return false;
            }
            if (IsCoreLibRandomLike(rm.DeclaringType))
            {
                if (rm.HasThis &&
                    rm.Name == "Next" &&
                    rm.ReturnType.Namespace == "System" &&
                    rm.ReturnType.Name == "Int32")
                {
                    int r;
                    if (rm.ParameterTypes.Length == 0 && totalArgs == 1)
                    { PopSlot(); r = System.Random.Shared.Next(); }
                    else if (rm.ParameterTypes.Length == 1 && totalArgs == 2)
                    {
                        int max = PopSlot().AsI4Checked();
                        PopSlot();
                        r = System.Random.Shared.Next(max);
                    }
                    else if (rm.ParameterTypes.Length == 2 && totalArgs == 3)
                    {
                        int max = PopSlot().AsI4Checked();
                        int min = PopSlot().AsI4Checked();
                        PopSlot();
                        r = System.Random.Shared.Next(min, max);
                    }
                    else return false;

                    PushSlot(new Slot(SlotKind.I4, r));
                    return true;
                }
            }
            if (IsSystemStringType(rm.DeclaringType))
            {
                // instance int get_Length()
                if (rm.HasThis &&
                    rm.Name == "get_Length" &&
                    rm.ParameterTypes.Length == 0 &&
                    totalArgs == 1 &&
                    rm.ReturnType.Namespace == "System" && rm.ReturnType.Name == "Int32")
                {
                    var s = PopSlot();
                    ValidateStringRef(s, out int strObjAbs);
                    int len = GetStringLengthFromObject(strObjAbs);
                    PushSlot(new Slot(SlotKind.I4, len));
                    return true;
                }

                // instance ref char GetPinnableReference()
                if (rm.HasThis &&
                    rm.Name == "GetPinnableReference" &&
                    rm.ParameterTypes.Length == 0 &&
                    totalArgs == 1 &&
                    rm.ReturnType.Kind == RuntimeTypeKind.ByRef &&
                    rm.ReturnType.ElementType is { Namespace: "System", Name: "Char" })
                {
                    var s = PopSlot();
                    ValidateStringRef(s, out int strObjAbs);
                    int charsAbs = GetStringCharsAbs(strObjAbs);
                    PushSlot(new Slot(SlotKind.ByRef, charsAbs));
                    return true;
                }

                // internal instance ref char GetRawStringData()
                if (rm.HasThis &&
                    rm.Name == "GetRawStringData" &&
                    rm.ParameterTypes.Length == 0 &&
                    totalArgs == 1 &&
                    rm.ReturnType.Kind == RuntimeTypeKind.ByRef &&
                    rm.ReturnType.ElementType is { Namespace: "System", Name: "Char" })
                {
                    var s = PopSlot();
                    ValidateStringRef(s, out int strObjAbs);
                    int charsAbs = GetStringCharsAbs(strObjAbs);
                    PushSlot(new Slot(SlotKind.ByRef, charsAbs));
                    return true;
                }

                // internal static string FastAllocateString(int length)
                if (!rm.HasThis &&
                    rm.Name == "FastAllocateString" &&
                    rm.ParameterTypes.Length == 1 &&
                    totalArgs == 1 &&
                    rm.ParameterTypes[0].Namespace == "System" && rm.ParameterTypes[0].Name == "Int32" &&
                    IsSystemStringType(rm.ReturnType))
                {
                    int len = PopSlot().AsI4Checked();
                    int objAbs = AllocStringUninitialized(len);
                    PushSlot(new Slot(SlotKind.Ref, objAbs));
                    return true;
                }
            }
            if (rm.DeclaringType.Namespace == "System" && rm.DeclaringType.Name == "Console")
            {

                if (rm.HasThis)
                    throw new NotSupportedException("Intrinsic System.Console.Write with 'this' is not supported.");

                if (rm.ParameterTypes.Length != 1 || totalArgs != 1)
                    throw new NotSupportedException("Intrinsic System.Console.Write overload is not supported (arity mismatch).");

                ct.ThrowIfCancellationRequested();
                if (rm.Name == "_Write")
                {
                    if (rm.ParameterTypes.Length != 1 || totalArgs != 1)
                        throw new NotSupportedException("Intrinsic System.Console.Write overload is not supported (arity mismatch).");

                    ct.ThrowIfCancellationRequested();

                    var p0 = rm.ParameterTypes[0];

                    if (IsSystemStringType(p0))
                    {
                        var s = PopSlot();
                        if (s.Kind != SlotKind.Null)
                        {
                            ValidateStringRef(s, out int strObjAbs);

                            int len = GetStringLengthFromObject(strObjAbs);
                            int charsAbs = GetStringCharsAbs(strObjAbs);
                            CheckHeapAccess(charsAbs, checked(len * 2), writable: false);

                            for (int i = 0; i < len; i++)
                            {
                                if ((i & 0xFF) == 0)
                                    ct.ThrowIfCancellationRequested();

                                _textWriter.Write((char)ReadU16(charsAbs + (i * 2)));
                            }
                        }
                        return true;
                    }
                    if (p0.Kind == RuntimeTypeKind.Pointer &&
                        p0.ElementType is { Namespace: "System", Name: "Char" })
                    {
                        ct.ThrowIfCancellationRequested();

                        var s = PopSlot();
                        if (s.Kind == SlotKind.Null)
                        {
                            return true;
                        }

                        int abs = GetAddressAbsOrThrow(s);

                        const int MaxChars = 8 * 1024;
                        for (int i = 0; i < MaxChars; i++)
                        {
                            if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested();

                            int pos = abs + (i * 2);
                            if (pos < _stackBase || pos + 2 > _sp)
                                throw new InvalidOperationException("Unterminated or out of range char* for Console.Write(char*).");

                            ushort ch = BinaryPrimitives.ReadUInt16LittleEndian(_mem.AsSpan(pos, 2));
                            if (ch == 0)
                                break;

                            _textWriter.Write((char)ch);
                        }

                        return true;
                    }
                    if (p0.Name.Equals("ReadOnlySpan`1<Char>", StringComparison.Ordinal))
                    {
                        ct.ThrowIfCancellationRequested();
                        var span = PopSlot();
                        if (span.Kind != SlotKind.Value)
                            throw new NotSupportedException(
                                "Intrinsic System.Console._Write(ReadOnlySpan<char>) expects a value type slot.");

                        var spanType = GetValueSlotType(span);
                        int spanAbs = checked((int)span.Payload);

                        RuntimeField? refField = null;
                        RuntimeField? lenField = null;
                        for (int i = 0; i < spanType.InstanceFields.Length; i++)
                        {
                            var f = spanType.InstanceFields[i];
                            if (StringComparer.Ordinal.Equals(f.Name, "_reference")) refField = f;
                            else if (StringComparer.Ordinal.Equals(f.Name, "_length")) lenField = f;
                        }
                        if (refField is null || lenField is null)
                        {
                            throw new NotSupportedException(
                                "Intrinsic System.Console._Write(ReadOnlySpan<char>) cannot locate Span fields (_reference/_length).");
                        }
                        var byref = LoadValueAsSlot(spanAbs, refField.Offset, refField.FieldType);
                        int len = LoadValueAsSlot(spanAbs, lenField.Offset, lenField.FieldType).AsI4Checked();
                        if (len <= 0)
                            return true;
                        if (byref.Kind == SlotKind.Null)
                            throw new InvalidOperationException("ReadOnlySpan<char> has non-zero length but null reference.");
                        int charsAbs = GetAddressAbsOrThrow(byref);
                        int bytes = checked(len * 2);
                        if (charsAbs >= _heapBase && charsAbs + bytes <= _heapPtr)
                            CheckHeapAccess(charsAbs, bytes, writable: false);
                        else
                            CheckActiveStackAccess(charsAbs, bytes, writable: false);
                        for (int i = 0; i < len; i++)
                        {
                            if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested();
                            _textWriter.Write((char)ReadU16(charsAbs + (i * 2)));
                        }
                        return true;
                    }
                    throw new NotSupportedException($"Intrinsic System.Console._Write({p0.Namespace}.{p0.Name}) is not supported.");
                }
            }

            return false;
        }
    }
}