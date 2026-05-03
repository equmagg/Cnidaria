using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cnidaria.Cs
{
    public sealed class ExecutionLimits
    {
        public int MaxCallDepth { get; init; } = 128;
        public long MaxInstructions { get; init; } = 100_000_000;
        public int TokenCheckPeriod { get; init; } = 256;
    }

    internal enum CellKind : byte
    {
        I4,
        I8,
        R8,
        Ref,
        Ptr,
        ByRef,
        Value,
        Null
    }

    internal readonly struct Cell
    {
        public readonly CellKind Kind;
        public readonly long Payload;
        public readonly int Aux;

        public Cell(CellKind kind, long payload, int aux = 0)
        {
            Kind = kind;
            Payload = payload;
            Aux = aux;
        }

        public int AsI4()
        {
            return Kind switch
            {
                CellKind.I4 => unchecked((int)Payload),
                CellKind.I8 => checked((int)Payload),
                CellKind.R8 => checked((int)BitConverter.Int64BitsToDouble(Payload)),
                CellKind.Null => 0,
                _ => throw new InvalidOperationException($"Expected integer cell, got {Kind}.")
            };
        }

        public long AsI8()
        {
            return Kind switch
            {
                CellKind.I8 => Payload,
                CellKind.I4 => unchecked((int)Payload),
                CellKind.R8 => checked((long)BitConverter.Int64BitsToDouble(Payload)),
                CellKind.Null => 0,
                _ => throw new InvalidOperationException($"Expected integer cell, got {Kind}.")
            };
        }

        public double AsR8()
        {
            return Kind switch
            {
                CellKind.R8 => BitConverter.Int64BitsToDouble(Payload),
                CellKind.I4 => unchecked((int)Payload),
                CellKind.I8 => Payload,
                _ => throw new InvalidOperationException($"Expected numeric cell, got {Kind}.")
            };
        }

        public bool AsBool()
        {
            return Kind switch
            {
                CellKind.Null => false,
                CellKind.Ref or CellKind.Ptr or CellKind.ByRef => Payload != 0,
                CellKind.R8 => BitConverter.Int64BitsToDouble(Payload) != 0.0,
                CellKind.I8 => Payload != 0,
                CellKind.I4 => unchecked((int)Payload) != 0,
                CellKind.Value => true,
                _ => false
            };
        }

        public static Cell Null => new(CellKind.Null, 0);
        public static Cell I4(int value) => new(CellKind.I4, value);
        public static Cell I8(long value) => new(CellKind.I8, value);
        public static Cell R8(double value) => new(CellKind.R8, BitConverter.DoubleToInt64Bits(value));
        public static Cell Ref(int value) => value == 0 ? Null : new Cell(CellKind.Ref, value);
        public static Cell Ptr(int value, int elementSize = 1) => new(CellKind.Ptr, value, elementSize);
        public static Cell ByRef(int value, int size = 1) => new(CellKind.ByRef, value, size);
    }

    internal sealed class Vm
    {
        internal sealed class VmUnhandledException : Exception
        {
            public VmUnhandledException(string message) : base(message) { }
        }

        private sealed class VmThrownException : Exception
        {
            public readonly Cell ExceptionObject;
            public VmThrownException(Cell exceptionObject)
            {
                ExceptionObject = exceptionObject;
            }
        }

        private enum FastCellKind : byte
        {
            None,
            I4,
            I8,
            R4,
            R8,
            NativeInt,
            NativeUInt
        }

        private enum FinallyContinuationKind : byte
        {
            Jump,
            Return,
            Throw
        }

        private readonly struct FinallyContext
        {
            public readonly Frame Frame;
            public readonly int FinallyStartPc;
            public readonly int FinallyEndPc;
            public readonly int NextFromPc;
            public readonly FinallyContinuationKind Kind;
            public readonly int TargetPc;
            public readonly bool HasReturnValue;
            public readonly Cell ReturnValue;
            public readonly Cell PendingException;

            private FinallyContext(Frame frame, int finallyStartPc, int finallyEndPc, int nextFromPc, FinallyContinuationKind kind, int targetPc, bool hasReturnValue, Cell returnValue, Cell pendingException)
            {
                Frame = frame;
                FinallyStartPc = finallyStartPc;
                FinallyEndPc = finallyEndPc;
                NextFromPc = nextFromPc;
                Kind = kind;
                TargetPc = targetPc;
                HasReturnValue = hasReturnValue;
                ReturnValue = returnValue;
                PendingException = pendingException;
            }

            public static FinallyContext ForJump(Frame frame, ExceptionHandler h, int targetPc)
                => new(frame, h.HandlerStartPc, h.HandlerEndPc, h.TryEndPc, FinallyContinuationKind.Jump, targetPc, false, default, default);

            public static FinallyContext ForReturn(Frame frame, ExceptionHandler h, bool hasReturnValue, Cell returnValue)
                => new(frame, h.HandlerStartPc, h.HandlerEndPc, h.TryEndPc, FinallyContinuationKind.Return, -1, hasReturnValue, returnValue, default);

            public static FinallyContext ForThrow(Frame frame, ExceptionHandler h, Cell exception)
                => new(frame, h.HandlerStartPc, h.HandlerEndPc, h.TryEndPc, FinallyContinuationKind.Throw, -1, false, default, exception);
        }

        private sealed class MethodExecLayout
        {
            public RuntimeMethod Method = null!;
            public RuntimeType[] ArgTypes = Array.Empty<RuntimeType>();
            public int[] ArgOffsets = Array.Empty<int>();
            public int[] ArgSizes = Array.Empty<int>();
            public FastCellKind[] ArgFastKinds = Array.Empty<FastCellKind>();
            public bool[] ArgAddressTaken = Array.Empty<bool>();
            public bool[] ArgCellCached = Array.Empty<bool>();
            public bool HasArgCellCache;
            public int ArgsAreaSize;
            public RuntimeType[] LocalTypes = Array.Empty<RuntimeType>();
            public int[] LocalOffsets = Array.Empty<int>();
            public int[] LocalSizes = Array.Empty<int>();
            public FastCellKind[] LocalFastKinds = Array.Empty<FastCellKind>();
            public bool[] LocalAddressTaken = Array.Empty<bool>();
            public bool[] LocalCellCached = Array.Empty<bool>();
            public bool HasLocalCellCache;
            public int LocalsAreaSize;
        }

        private sealed class Frame
        {
            public RuntimeModule Module = null!;
            public BytecodeFunction Function = null!;
            public RuntimeMethod Method = null!;
            public RuntimeType[] ValueTypes = Array.Empty<RuntimeType>();
            public MethodExecLayout Layout = null!;
            public int ArgsBase;
            public int LocalsBase;
            public int FrameBase;
            public int Pc;
            public Cell[] ArgCells = Array.Empty<Cell>();
            public Cell[] LocalCells = Array.Empty<Cell>();
            public Cell[] Values = Array.Empty<Cell>();
            public FastCellKind[] ValueFastKinds = Array.Empty<FastCellKind>();
            public int ValuesBase;
            public int[] ValueOffsets = Array.Empty<int>();
            public int[] ValueSizes = Array.Empty<int>();
            public Cell Exception;
        }

        private const int ObjectHeaderSize = 8;
        private const int ArrayLengthOffset = ObjectHeaderSize;
        private const int ArrayDataOffset = 16;
        private const int StringLengthOffset = ObjectHeaderSize;
        private const int StringCharsOffset = 12;
        private const int FinallyCatchTypeToken = -1;
        private const int GcFlagAllocated = 1 << 1;

        private readonly byte[] _mem;
        private readonly int _staticEnd;
        private readonly int _stackBase;
        private readonly int _stackEnd;
        private readonly int _heapBase;
        private readonly int _heapEnd;
        private int _heapPtr;
        private int _heapFloor;
        private int _sp;
        private int _stackPeakAbs;
        private int _heapPeakAbs;
        private readonly Domain _domain;
        private readonly RuntimeTypeSystem _rts;
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;
        private readonly TextWriter _textWriter;
        private readonly Dictionary<int, int> _staticBaseByTypeId = new();
        private readonly Dictionary<int, byte> _typeInitState = new();
        private readonly Dictionary<string, int> _internPool = new(StringComparer.Ordinal);
        private readonly Dictionary<int, HostOverride> _hostOverrides = new();
        private readonly VmCallContext _hostCtx;
        private readonly Dictionary<int, MethodExecLayout> _methodLayouts = new();
        private readonly List<FinallyContext> _finallyStack = new();
        private int _exceptionTranslationDepth;
        private Frame? _currentFrame;
        private long _fuel;
        private long _instructionLimit;
        private int _tick;
        private int _callDepth;

        public int StackPeakBytes => _stackPeakAbs - _stackBase;
        public int HeapPeakBytes => _heapPeakAbs - _heapBase;
        public long InctructionsElapsed => _instructionLimit - _fuel;

        public Vm(
            byte[] memory,
            int staticEnd,
            int stackBase,
            int stackEnd,
            Domain domain,
            RuntimeTypeSystem rts,
            IReadOnlyDictionary<string, RuntimeModule> modules,
            TextWriter textWriter)
        {
            _mem = memory ?? throw new ArgumentNullException(nameof(memory));
            _staticEnd = staticEnd;
            _stackBase = stackBase;
            _stackEnd = stackEnd;
            _sp = stackBase;
            _stackPeakAbs = _sp;
            _heapBase = AlignUp(stackEnd, 8);
            _heapEnd = _mem.Length;
            _heapPtr = _heapBase;
            _heapFloor = _heapPtr;
            _heapPeakAbs = _heapPtr;
            _domain = domain ?? throw new ArgumentNullException(nameof(domain));
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _textWriter = textWriter ?? TextWriter.Null;
            _hostCtx = new VmCallContext(this);

            if (!(0 <= _staticEnd && _staticEnd <= _stackBase && _stackBase <= _stackEnd && _stackEnd <= _mem.Length && _heapBase <= _heapEnd))
                throw new ArgumentOutOfRangeException(nameof(memory), "Bad VM memory layout.");
        }

        public void Execute(RuntimeModule entryModule, BytecodeFunction entry, CancellationToken ct, ExecutionLimits limits, ReadOnlySpan<Cell> initialArgs = default)
        {
            if (entryModule is null) throw new ArgumentNullException(nameof(entryModule));
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            if (limits is null) throw new ArgumentNullException(nameof(limits));

            _instructionLimit = limits.MaxInstructions;
            _fuel = limits.MaxInstructions;
            _tick = 0;
            _callDepth = 0;

            var method = ResolveRuntimeMethod(entryModule, entry.MethodToken, null);
            var args = initialArgs.IsEmpty ? Array.Empty<Cell>() : initialArgs.ToArray();
            try
            {
                _ = Invoke(entryModule, entry, method, args, ct, limits, allowMissingArgs: initialArgs.IsEmpty);
            }
            catch (VmThrownException ex)
            {
                throw new VmUnhandledException(FormatThrownException(ex.ExceptionObject));
            }
        }

        private Cell Invoke(
            RuntimeModule module,
            BytecodeFunction fn,
            RuntimeMethod method,
            Cell[] args,
            CancellationToken ct,
            ExecutionLimits limits,
            bool allowMissingArgs = false)
        {
            if (++_callDepth > limits.MaxCallDepth)
                throw new InvalidOperationException("Max call depth exceeded.");

            var previousFrame = _currentFrame;
            var frame = CreateFrame(module, fn, method, args, allowMissingArgs);
            _currentFrame = frame;
            Cell result = default;
            byte[]? detachedValue = null;
            RuntimeType? detachedType = null;

            try
            {
                while (true)
                {
                    if (--_fuel < 0)
                        throw new OperationCanceledException("Instruction budget exceeded.");

                    _tick++;
                    if (_tick == limits.TokenCheckPeriod)
                    {
                        _tick = 0;
                        ct.ThrowIfCancellationRequested();
                    }

                    if ((uint)frame.Pc >= (uint)fn.Instructions.Length)
                        throw new InvalidOperationException($"PC out of range: {frame.Pc}");

                    int pc = frame.Pc;
                    var ins = fn.Instructions[pc];
                    frame.Pc = pc + 1;

                    int instructionStackMark = _sp;
                    bool keepInstructionStack = ins.Op == Op.StackAlloc;
                    try
                    {

                        Cell returned = default;
                        var currentModule = frame.Module;

                        switch (ins.Op)
                        {
                            case Op.Nop:
                                goto InstructionFinished;
                            case Op.Move:
                                Set(frame, ins.Result, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Ldnull:
                                Set(frame, ins.Result, Cell.Null);
                                goto InstructionFinished;
                            case Op.Ldc_I4:
                                SetI4(frame, ins.Result, ins.Operand0);
                                goto InstructionFinished;
                            case Op.Ldc_I4_M1:
                                SetI4(frame, ins.Result, -1);
                                goto InstructionFinished;
                            case Op.Ldc_I4_0:
                                SetI4(frame, ins.Result, 0);
                                goto InstructionFinished;
                            case Op.Ldc_I4_1:
                                SetI4(frame, ins.Result, 1);
                                goto InstructionFinished;
                            case Op.Ldc_I4_2:
                                SetI4(frame, ins.Result, 2);
                                goto InstructionFinished;
                            case Op.Ldc_I4_3:
                                SetI4(frame, ins.Result, 3);
                                goto InstructionFinished;
                            case Op.Ldc_I4_4:
                                SetI4(frame, ins.Result, 4);
                                goto InstructionFinished;
                            case Op.Ldc_I4_5:
                                SetI4(frame, ins.Result, 5);
                                goto InstructionFinished;
                            case Op.Ldc_I4_6:
                                SetI4(frame, ins.Result, 6);
                                goto InstructionFinished;
                            case Op.Ldc_I4_7:
                                SetI4(frame, ins.Result, 7);
                                goto InstructionFinished;
                            case Op.Ldc_I4_8:
                                SetI4(frame, ins.Result, 8);
                                goto InstructionFinished;
                            case Op.Ldc_I4_S:
                                SetI4(frame, ins.Result, unchecked((sbyte)ins.Operand0));
                                goto InstructionFinished;
                            case Op.Ldc_I8:
                                Set(frame, ins.Result, Cell.I8(ins.Operand2));
                                goto InstructionFinished;
                            case Op.Ldc_R8:
                                Set(frame, ins.Result, new Cell(CellKind.R8, ins.Operand2));
                                goto InstructionFinished;
                            case Op.Ldstr:
                                Set(frame, ins.Result, ExecLdstr(currentModule, ins.Operand0));
                                goto InstructionFinished;
                            case Op.DefaultValue:
                                Set(frame, ins.Result, DefaultValue(ResolveTypeToken(currentModule, ins.Operand0, frame.Method)));
                                goto InstructionFinished;
                            case Op.Ldloc:
                                Set(frame, ins.Result, LoadLocal(frame, ins.Operand0));
                                goto InstructionFinished;
                            case Op.Stloc:
                                StoreLocal(frame, ins.Operand0, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Ldloca:
                                Set(frame, ins.Result, LocalAddress(frame, ins.Operand0));
                                goto InstructionFinished;
                            case Op.Ldarg:
                                Set(frame, ins.Result, LoadArg(frame, ins.Operand0));
                                goto InstructionFinished;
                            case Op.Starg:
                                StoreArg(frame, ins.Operand0, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Ldarga:
                                Set(frame, ins.Result, ArgAddress(frame, ins.Operand0));
                                goto InstructionFinished;
                            case Op.Ldthis:
                                Set(frame, ins.Result, LoadArg(frame, 0));
                                goto InstructionFinished;
                            case Op.Ldarg_0:
                                Set(frame, ins.Result, LoadArg(frame, 0));
                                goto InstructionFinished;
                            case Op.Ldarg_1:
                                Set(frame, ins.Result, LoadArg(frame, 1));
                                goto InstructionFinished;
                            case Op.Ldarg_2:
                                Set(frame, ins.Result, LoadArg(frame, 2));
                                goto InstructionFinished;
                            case Op.Ldarg_3:
                                Set(frame, ins.Result, LoadArg(frame, 3));
                                goto InstructionFinished;
                            case Op.Starg_0:
                                StoreArg(frame, 0, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Starg_1:
                                StoreArg(frame, 1, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Starg_2:
                                StoreArg(frame, 2, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Starg_3:
                                StoreArg(frame, 3, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Ldarga_0:
                                Set(frame, ins.Result, ArgAddress(frame, 0));
                                goto InstructionFinished;
                            case Op.Ldarga_1:
                                Set(frame, ins.Result, ArgAddress(frame, 1));
                                goto InstructionFinished;
                            case Op.Ldarga_2:
                                Set(frame, ins.Result, ArgAddress(frame, 2));
                                goto InstructionFinished;
                            case Op.Ldarga_3:
                                Set(frame, ins.Result, ArgAddress(frame, 3));
                                goto InstructionFinished;
                            case Op.Ldloc_0:
                                Set(frame, ins.Result, LoadLocal(frame, 0));
                                goto InstructionFinished;
                            case Op.Ldloc_1:
                                Set(frame, ins.Result, LoadLocal(frame, 1));
                                goto InstructionFinished;
                            case Op.Ldloc_2:
                                Set(frame, ins.Result, LoadLocal(frame, 2));
                                goto InstructionFinished;
                            case Op.Ldloc_3:
                                Set(frame, ins.Result, LoadLocal(frame, 3));
                                goto InstructionFinished;
                            case Op.Stloc_0:
                                StoreLocal(frame, 0, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Stloc_1:
                                StoreLocal(frame, 1, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Stloc_2:
                                StoreLocal(frame, 2, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Stloc_3:
                                StoreLocal(frame, 3, Get(frame, ins.Value0));
                                goto InstructionFinished;
                            case Op.Ldloca_0:
                                Set(frame, ins.Result, LocalAddress(frame, 0));
                                goto InstructionFinished;
                            case Op.Ldloca_1:
                                Set(frame, ins.Result, LocalAddress(frame, 1));
                                goto InstructionFinished;
                            case Op.Ldloca_2:
                                Set(frame, ins.Result, LocalAddress(frame, 2));
                                goto InstructionFinished;
                            case Op.Ldloca_3:
                                Set(frame, ins.Result, LocalAddress(frame, 3));
                                goto InstructionFinished;
                            case Op.Add:
                                ExecAdd(frame, ins);
                                goto InstructionFinished;
                            case Op.Sub:
                                ExecSub(frame, ins);
                                goto InstructionFinished;
                            case Op.Mul:
                                ExecMul(frame, ins);
                                goto InstructionFinished;
                            case Op.Div:
                                ExecDiv(frame, ins);
                                goto InstructionFinished;
                            case Op.Div_Un:
                                ExecDivUn(frame, ins);
                                goto InstructionFinished;
                            case Op.Rem:
                                ExecRem(frame, ins);
                                goto InstructionFinished;
                            case Op.Rem_Un:
                                ExecRemUn(frame, ins);
                                goto InstructionFinished;
                            case Op.And:
                                ExecAnd(frame, ins);
                                goto InstructionFinished;
                            case Op.Or:
                                ExecOr(frame, ins);
                                goto InstructionFinished;
                            case Op.Xor:
                                ExecXor(frame, ins);
                                goto InstructionFinished;
                            case Op.Shl:
                                ExecShl(frame, ins);
                                goto InstructionFinished;
                            case Op.Shr:
                                ExecShr(frame, ins);
                                goto InstructionFinished;
                            case Op.Shr_Un:
                                ExecShrUn(frame, ins);
                                goto InstructionFinished;
                            case Op.Neg:
                                ExecNeg(frame, ins);
                                goto InstructionFinished;
                            case Op.Not:
                                ExecNot(frame, ins);
                                goto InstructionFinished;
                            case Op.Ceq:
                                ExecCeq(frame, ins);
                                goto InstructionFinished;
                            case Op.Clt:
                                ExecClt(frame, ins);
                                goto InstructionFinished;
                            case Op.Clt_Un:
                                ExecCltUn(frame, ins);
                                goto InstructionFinished;
                            case Op.Cgt:
                                ExecCgt(frame, ins);
                                goto InstructionFinished;
                            case Op.Cgt_Un:
                                ExecCgtUn(frame, ins);
                                goto InstructionFinished;

                            case Op.Call:
                                Set(frame, ins.Result, ExecCall(frame, ins, virtualDispatch: false, ct, limits));
                                goto InstructionFinished;
                            case Op.CallVirt:
                                Set(frame, ins.Result, ExecCall(frame, ins, virtualDispatch: true, ct, limits));
                                goto InstructionFinished;
                            case Op.Newobj:
                                Set(frame, ins.Result, ExecNewobj(frame, ins, ct, limits));
                                goto InstructionFinished;
                            case Op.Ldfld:
                                Set(frame, ins.Result, ExecLdfld(frame, ins));
                                goto InstructionFinished;
                            case Op.Stfld:
                                ExecStfld(frame, ins);
                                goto InstructionFinished;
                            case Op.Ldsfld:
                                Set(frame, ins.Result, ExecLdsfld(frame, ins, ct, limits));
                                goto InstructionFinished;
                            case Op.Stsfld:
                                ExecStsfld(frame, ins, ct, limits);
                                goto InstructionFinished;
                            case Op.Ldflda:
                                Set(frame, ins.Result, ExecLdflda(frame, ins));
                                goto InstructionFinished;
                            case Op.Ldsflda:
                                Set(frame, ins.Result, ExecLdsflda(frame, ins, ct, limits));
                                goto InstructionFinished;
                            case Op.Conv:
                                Set(frame, ins.Result, Conv(Get(frame, ins.Value0), (NumericConvKind)ins.Operand0, 
                                    (NumericConvFlags)ins.Operand1, ins.Result >= 0 ? frame.ValueTypes[ins.Result] : null));
                                goto InstructionFinished;
                            case Op.CastClass:
                                Set(frame, ins.Result, ExecCastClass(frame, ins));
                                goto InstructionFinished;
                            case Op.Box:
                                Set(frame, ins.Result, ExecBox(frame, ins));
                                goto InstructionFinished;
                            case Op.UnboxAny:
                                Set(frame, ins.Result, ExecUnboxAny(frame, ins));
                                goto InstructionFinished;
                            case Op.Br:
                                if (!TryBeginFinallyForJump(frame, pc, ins.Operand0))
                                    frame.Pc = ins.Operand0;
                                goto InstructionFinished;
                            case Op.Brtrue:
                                if (GetBool(frame, ins.Value0) && !TryBeginFinallyForJump(frame, pc, ins.Operand0))
                                    frame.Pc = ins.Operand0;
                                goto InstructionFinished;
                            case Op.Brfalse:
                                if (!GetBool(frame, ins.Value0) && !TryBeginFinallyForJump(frame, pc, ins.Operand0))
                                    frame.Pc = ins.Operand0;
                                goto InstructionFinished;
                            case Op.Ret:
                                returned = ins.ValueCount == 0 ? default : Get(frame, ins.Value0);
                                if (TryBeginFinallyForReturn(frame, pc, ins.ValueCount != 0, returned))
                                    goto InstructionFinished;
                                goto MethodReturned;
                            case Op.Throw:
                                throw new VmThrownException(NormalizeThrownException(Get(frame, ins.Value0)));
                            case Op.Rethrow:
                                throw new VmThrownException(NormalizeThrownException(frame.Exception));
                            case Op.Ldexception:
                                Set(frame, ins.Result, frame.Exception);
                                goto InstructionFinished;
                            case Op.Endfinally:
                                if (ExecEndfinally(frame, out returned))
                                    goto MethodReturned;
                                goto InstructionFinished;
                            case Op.StackAlloc:
                                Set(frame, ins.Result, ExecStackAlloc(Get(frame, ins.Value0), ins.Operand0));
                                goto InstructionFinished;
                            case Op.PtrElemAddr:
                                Set(frame, ins.Result, PtrElemAddr(Get(frame, ins.Value0), Get(frame, ins.Value1), ins.Operand0));
                                goto InstructionFinished;
                            case Op.PtrToByRef:
                                Set(frame, ins.Result, PtrToByRef(Get(frame, ins.Value0)));
                                goto InstructionFinished;
                            case Op.Ldobj:
                                Set(frame, ins.Result, ExecLdobj(frame, ins));
                                goto InstructionFinished;
                            case Op.Stobj:
                                ExecStobj(frame, ins);
                                goto InstructionFinished;
                            case Op.Newarr:
                                Set(frame, ins.Result, ExecNewarr(frame, ins));
                                goto InstructionFinished;
                            case Op.Ldelem:
                                Set(frame, ins.Result, ExecLdelem(frame, ins));
                                goto InstructionFinished;
                            case Op.Ldelema:
                                Set(frame, ins.Result, ExecLdelema(frame, ins));
                                goto InstructionFinished;
                            case Op.Stelem:
                                ExecStelem(frame, ins);
                                goto InstructionFinished;
                            case Op.LdArrayDataRef:
                                Set(frame, ins.Result, ExecLdArrayDataRef(Get(frame, ins.Value0)));
                                goto InstructionFinished;
                            case Op.Sizeof:
                                Set(frame, ins.Result, Cell.I4(GetStorageSizeAlign(ResolveTypeToken(currentModule, ins.Operand0, frame.Method)).size));
                                goto InstructionFinished;
                            case Op.PtrDiff:
                                Set(frame, ins.Result, PtrDiff(Get(frame, ins.Value0), Get(frame, ins.Value1), ins.Operand0));
                                goto InstructionFinished;
                            case Op.Isinst:
                                Set(frame, ins.Result, ExecIsinst(frame, ins));
                                goto InstructionFinished;
                            default:
                                throw new NotSupportedException($"Flat opcode not supported: {ins.Op}");
                        }


                    InstructionFinished:
                        if (!keepInstructionStack)
                            _sp = instructionStackMark;
                        continue;

                    MethodReturned:
                        result = returned;
                        if (result.Kind == CellKind.Value)
                        {
                            detachedType = GetValueType(result);
                            detachedValue = new byte[detachedType.SizeOf];
                            Buffer.BlockCopy(_mem, checked((int)result.Payload), detachedValue, 0, detachedValue.Length);
                        }
                        if (!keepInstructionStack)
                            _sp = instructionStackMark;
                        break;
                    }
                    catch (VmThrownException ex)
                    {
                        if (!keepInstructionStack)
                            _sp = instructionStackMark;
                        if (!TryEnterExceptionHandler(frame, pc, ex.ExceptionObject))
                            throw;
                    }
                    catch (Exception ex) when (ex is DivideByZeroException or OverflowException)
                    {
                        if (!keepInstructionStack)
                            _sp = instructionStackMark;
                        if (_exceptionTranslationDepth != 0)
                            throw;

                        _exceptionTranslationDepth++;
                        try
                        {
                            if (!TryTranslateHostExceptionToVm(ex, out var vmEx))
                                throw new VmUnhandledException(ex.Message);
                            if (!TryEnterExceptionHandler(frame, pc, vmEx))
                                throw new VmThrownException(vmEx);
                        }
                        finally
                        {
                            _exceptionTranslationDepth--;
                        }
                    }
                }
            }
            finally
            {
                ClearFinallyContextsForFrame(frame);
                _sp = frame.FrameBase;
                _currentFrame = previousFrame;
                _callDepth--;
            }

            if (detachedValue is not null && detachedType is not null)
            {
                int abs = AllocStackBytes(detachedValue.Length, Math.Max(1, detachedType.AlignOf));
                Buffer.BlockCopy(detachedValue, 0, _mem, abs, detachedValue.Length);
                result = new Cell(CellKind.Value, abs, detachedType.TypeId);
            }

            return result;
        }

        private static bool IsFloating(Cell value) => value.Kind == CellKind.R8;
        private static bool IsI8(Cell value) => value.Kind == CellKind.I8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetCell(Frame frame, int valueId, Cell value)
        {
            if (valueId >= 0)
                frame.Values[valueId] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecAdd(Frame frame, Instruction ins)
        {
            var values = frame.Values;
            var a = values[ins.Value0];
            var b = values[ins.Value1];
            int result = ins.Result;

            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, result, Cell.I4(unchecked((int)a.Payload + (int)b.Payload)));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, result, Cell.I8(unchecked(a.Payload + b.Payload)));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, result, Cell.R8(BitConverter.Int64BitsToDouble(a.Payload) + BitConverter.Int64BitsToDouble(b.Payload)));
                return;
            }

            if (IsFloating(a) || IsFloating(b)) SetR8(frame, result, FastR8(a) + FastR8(b));
            else if (IsI8(a) || IsI8(b)) SetI8(frame, result, unchecked(FastI8(a) + FastI8(b)));
            else SetI4(frame, result, unchecked(FastI4(a) + FastI4(b)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecSub(Frame frame, Instruction ins)
        {
            var values = frame.Values;
            var a = values[ins.Value0];
            var b = values[ins.Value1];
            int result = ins.Result;

            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, result, Cell.I4(unchecked((int)a.Payload - (int)b.Payload)));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, result, Cell.I8(unchecked(a.Payload - b.Payload)));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, result, Cell.R8(BitConverter.Int64BitsToDouble(a.Payload) - BitConverter.Int64BitsToDouble(b.Payload)));
                return;
            }

            if (IsFloating(a) || IsFloating(b)) SetR8(frame, result, FastR8(a) - FastR8(b));
            else if (IsI8(a) || IsI8(b)) SetI8(frame, result, unchecked(FastI8(a) - FastI8(b)));
            else SetI4(frame, result, unchecked(FastI4(a) - FastI4(b)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecMul(Frame frame, Instruction ins)
        {
            var values = frame.Values;
            var a = values[ins.Value0];
            var b = values[ins.Value1];
            int result = ins.Result;

            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, result, Cell.I4(unchecked((int)a.Payload * (int)b.Payload)));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, result, Cell.I8(unchecked(a.Payload * b.Payload)));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, result, Cell.R8(BitConverter.Int64BitsToDouble(a.Payload) * BitConverter.Int64BitsToDouble(b.Payload)));
                return;
            }

            if (IsFloating(a) || IsFloating(b)) SetR8(frame, result, FastR8(a) * FastR8(b));
            else if (IsI8(a) || IsI8(b)) SetI8(frame, result, unchecked(FastI8(a) * FastI8(b)));
            else SetI4(frame, result, unchecked(FastI4(a) * FastI4(b)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecDiv(Frame frame, Instruction ins)
        {
            var values = frame.Values;
            var a = values[ins.Value0];
            var b = values[ins.Value1];
            int result = ins.Result;

            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, result, Cell.I4(unchecked((int)a.Payload / (int)b.Payload)));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, result, Cell.I8(a.Payload / b.Payload));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, result, Cell.R8(BitConverter.Int64BitsToDouble(a.Payload) / BitConverter.Int64BitsToDouble(b.Payload)));
                return;
            }

            if (IsFloating(a) || IsFloating(b)) SetR8(frame, result, FastR8(a) / FastR8(b));
            else if (IsI8(a) || IsI8(b)) SetI8(frame, result, FastI8(a) / FastI8(b));
            else SetI4(frame, result, FastI4(a) / FastI4(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecDivUn(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4(unchecked((int)((uint)(int)a.Payload / (uint)(int)b.Payload))));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I8(unchecked((long)((ulong)a.Payload / (ulong)b.Payload))));
                return;
            }
            if (IsI8(a) || IsI8(b)) SetI8(frame, ins.Result, unchecked((long)((ulong)FastI8(a) / (ulong)FastI8(b))));
            else SetI4(frame, ins.Result, unchecked((int)((uint)FastI4(a) / (uint)FastI4(b))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecRem(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4(unchecked((int)a.Payload % (int)b.Payload)));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I8(a.Payload % b.Payload));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, ins.Result, Cell.R8(BitConverter.Int64BitsToDouble(a.Payload) % BitConverter.Int64BitsToDouble(b.Payload)));
                return;
            }
            if (IsFloating(a) || IsFloating(b)) SetR8(frame, ins.Result, FastR8(a) % FastR8(b));
            else if (IsI8(a) || IsI8(b)) SetI8(frame, ins.Result, FastI8(a) % FastI8(b));
            else SetI4(frame, ins.Result, FastI4(a) % FastI4(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecRemUn(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4(unchecked((int)((uint)(int)a.Payload % (uint)(int)b.Payload))));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I8(unchecked((long)((ulong)a.Payload % (ulong)b.Payload))));
                return;
            }
            if (IsI8(a) || IsI8(b)) SetI8(frame, ins.Result, unchecked((long)((ulong)FastI8(a) % (ulong)FastI8(b))));
            else SetI4(frame, ins.Result, unchecked((int)((uint)FastI4(a) % (uint)FastI4(b))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecAnd(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((int)a.Payload & (int)b.Payload));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I8(a.Payload & b.Payload));
                return;
            }
            if (IsI8(a) || IsI8(b)) SetI8(frame, ins.Result, FastI8(a) & FastI8(b));
            else SetI4(frame, ins.Result, FastI4(a) & FastI4(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecOr(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((int)a.Payload | (int)b.Payload));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I8(a.Payload | b.Payload));
                return;
            }
            if (IsI8(a) || IsI8(b)) SetI8(frame, ins.Result, FastI8(a) | FastI8(b));
            else SetI4(frame, ins.Result, FastI4(a) | FastI4(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecXor(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((int)a.Payload ^ (int)b.Payload));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I8(a.Payload ^ b.Payload));
                return;
            }
            if (IsI8(a) || IsI8(b)) SetI8(frame, ins.Result, FastI8(a) ^ FastI8(b));
            else SetI4(frame, ins.Result, FastI4(a) ^ FastI4(b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecShl(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            int shift = FastI4(frame.Values[ins.Value1]);
            if (a.Kind == CellKind.I8) SetCell(frame, ins.Result, Cell.I8(unchecked(a.Payload << (shift & 0x3F))));
            else if (a.Kind == CellKind.I4) SetCell(frame, ins.Result, Cell.I4(unchecked((int)a.Payload << (shift & 0x1F))));
            else if (IsI8(a)) SetI8(frame, ins.Result, unchecked(FastI8(a) << (shift & 0x3F)));
            else SetI4(frame, ins.Result, unchecked(FastI4(a) << (shift & 0x1F)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecShr(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            int shift = FastI4(frame.Values[ins.Value1]);
            if (a.Kind == CellKind.I8) SetCell(frame, ins.Result, Cell.I8(a.Payload >> (shift & 0x3F)));
            else if (a.Kind == CellKind.I4) SetCell(frame, ins.Result, Cell.I4((int)a.Payload >> (shift & 0x1F)));
            else if (IsI8(a)) SetI8(frame, ins.Result, FastI8(a) >> (shift & 0x3F));
            else SetI4(frame, ins.Result, FastI4(a) >> (shift & 0x1F));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecShrUn(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            int shift = FastI4(frame.Values[ins.Value1]);
            if (a.Kind == CellKind.I8) SetCell(frame, ins.Result, Cell.I8(unchecked((long)((ulong)a.Payload >> (shift & 0x3F)))));
            else if (a.Kind == CellKind.I4) SetCell(frame, ins.Result, Cell.I4(unchecked((int)((uint)(int)a.Payload >> (shift & 0x1F)))));
            else if (IsI8(a)) SetI8(frame, ins.Result, unchecked((long)((ulong)FastI8(a) >> (shift & 0x3F))));
            else SetI4(frame, ins.Result, unchecked((int)((uint)FastI4(a) >> (shift & 0x1F))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecNeg(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            if (a.Kind == CellKind.I4) SetCell(frame, ins.Result, Cell.I4(unchecked(-(int)a.Payload)));
            else if (a.Kind == CellKind.I8) SetCell(frame, ins.Result, Cell.I8(unchecked(-a.Payload)));
            else if (a.Kind == CellKind.R8) SetCell(frame, ins.Result, Cell.R8(-BitConverter.Int64BitsToDouble(a.Payload)));
            else if (IsFloating(a)) SetR8(frame, ins.Result, -FastR8(a));
            else if (IsI8(a)) SetI8(frame, ins.Result, unchecked(-FastI8(a)));
            else SetI4(frame, ins.Result, unchecked(-FastI4(a)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecNot(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            if (a.Kind == CellKind.I4) SetCell(frame, ins.Result, Cell.I4(~(int)a.Payload));
            else if (a.Kind == CellKind.I8) SetCell(frame, ins.Result, Cell.I8(~a.Payload));
            else if (IsI8(a)) SetI8(frame, ins.Result, ~FastI8(a));
            else SetI4(frame, ins.Result, ~FastI4(a));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecCeq(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((int)a.Payload == (int)b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I4(a.Payload == b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, ins.Result, Cell.I4(BitConverter.Int64BitsToDouble(a.Payload) == BitConverter.Int64BitsToDouble(b.Payload) ? 1 : 0));
                return;
            }
            SetI4(frame, ins.Result, CompareEqual(a, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecClt(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((int)a.Payload < (int)b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I4(a.Payload < b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, ins.Result, Cell.I4(BitConverter.Int64BitsToDouble(a.Payload) < BitConverter.Int64BitsToDouble(b.Payload) ? 1 : 0));
                return;
            }
            SetI4(frame, ins.Result, CompareLess(a, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecCltUn(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((uint)(int)a.Payload < (uint)(int)b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I4((ulong)a.Payload < (ulong)b.Payload ? 1 : 0));
                return;
            }
            SetI4(frame, ins.Result, CompareLessUn(a, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecCgt(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((int)a.Payload > (int)b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I4(a.Payload > b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.R8 && b.Kind == CellKind.R8)
            {
                SetCell(frame, ins.Result, Cell.I4(BitConverter.Int64BitsToDouble(a.Payload) > BitConverter.Int64BitsToDouble(b.Payload) ? 1 : 0));
                return;
            }
            SetI4(frame, ins.Result, CompareGreater(a, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecCgtUn(Frame frame, Instruction ins)
        {
            var a = frame.Values[ins.Value0];
            var b = frame.Values[ins.Value1];
            if (a.Kind == CellKind.I4 && b.Kind == CellKind.I4)
            {
                SetCell(frame, ins.Result, Cell.I4((uint)(int)a.Payload > (uint)(int)b.Payload ? 1 : 0));
                return;
            }
            if (a.Kind == CellKind.I8 && b.Kind == CellKind.I8)
            {
                SetCell(frame, ins.Result, Cell.I4((ulong)a.Payload > (ulong)b.Payload ? 1 : 0));
                return;
            }
            SetI4(frame, ins.Result, CompareGreaterUn(a, b));
        }

        private static int AlignUp(int value, int align)
        {
            int mask = align - 1;
            return (value + mask) & ~mask;
        }

        private static int GetI4(Frame frame, int valueId)
        {
            var c = frame.Values[valueId];
            return c.Kind == CellKind.I4 ? unchecked((int)c.Payload) : c.AsI4();
        }

        private static long GetI8(Frame frame, int valueId)
        {
            var c = frame.Values[valueId];
            return c.Kind == CellKind.I8 ? c.Payload : c.AsI8();
        }

        private static double GetR8(Frame frame, int valueId)
        {
            var c = frame.Values[valueId];
            return c.Kind == CellKind.R8 ? BitConverter.Int64BitsToDouble(c.Payload) : c.AsR8();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastI4(Cell c)
        {
            return c.Kind switch
            {
                CellKind.I4 => unchecked((int)c.Payload),
                CellKind.I8 => checked((int)c.Payload),
                CellKind.Null => 0,
                _ => c.AsI4()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long FastI8(Cell c)
        {
            return c.Kind switch
            {
                CellKind.I8 => c.Payload,
                CellKind.I4 => unchecked((int)c.Payload),
                CellKind.Null => 0,
                _ => c.AsI8()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double FastR8(Cell c)
        {
            return c.Kind switch
            {
                CellKind.R8 => BitConverter.Int64BitsToDouble(c.Payload),
                CellKind.I4 => unchecked((int)c.Payload),
                CellKind.I8 => c.Payload,
                _ => c.AsR8()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool FastBool(Cell c)
        {
            return c.Kind switch
            {
                CellKind.I4 => unchecked((int)c.Payload) != 0,
                CellKind.I8 => c.Payload != 0,
                CellKind.R8 => BitConverter.Int64BitsToDouble(c.Payload) != 0.0,
                CellKind.Null => false,
                CellKind.Ref or CellKind.Ptr or CellKind.ByRef => c.Payload != 0,
                CellKind.Value => true,
                _ => c.AsBool()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool GetBool(Frame frame, int valueId)
            => FastBool(frame.Values[valueId]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetI4(Frame frame, int valueId, int value)
        {
            if (valueId < 0)
                return;
            if ((uint)valueId >= (uint)frame.Values.Length)
                throw new InvalidOperationException($"Value %{valueId} is out of range.");
            if ((uint)valueId < (uint)frame.ValueFastKinds.Length && frame.ValueFastKinds[valueId] != FastCellKind.None)
            {
                frame.Values[valueId] = Cell.I4(value);
                return;
            }
            if ((uint)valueId < (uint)frame.ValueOffsets.Length && frame.ValueOffsets[valueId] >= 0)
            {
                Set(frame, valueId, Cell.I4(value));
                return;
            }
            frame.Values[valueId] = Cell.I4(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetI8(Frame frame, int valueId, long value)
        {
            if (valueId < 0)
                return;
            if ((uint)valueId >= (uint)frame.Values.Length)
                throw new InvalidOperationException($"Value %{valueId} is out of range.");
            if ((uint)valueId < (uint)frame.ValueFastKinds.Length && frame.ValueFastKinds[valueId] != FastCellKind.None)
            {
                frame.Values[valueId] = Cell.I8(value);
                return;
            }
            if ((uint)valueId < (uint)frame.ValueOffsets.Length && frame.ValueOffsets[valueId] >= 0)
            {
                Set(frame, valueId, Cell.I8(value));
                return;
            }
            frame.Values[valueId] = Cell.I8(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetR8(Frame frame, int valueId, double value)
        {
            if (valueId < 0)
                return;
            if ((uint)valueId >= (uint)frame.Values.Length)
                throw new InvalidOperationException($"Value %{valueId} is out of range.");
            if ((uint)valueId < (uint)frame.ValueFastKinds.Length && frame.ValueFastKinds[valueId] != FastCellKind.None)
            {
                frame.Values[valueId] = Cell.R8(value);
                return;
            }
            if ((uint)valueId < (uint)frame.ValueOffsets.Length && frame.ValueOffsets[valueId] >= 0)
            {
                Set(frame, valueId, Cell.R8(value));
                return;
            }
            frame.Values[valueId] = Cell.R8(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Cell Get(Frame frame, int valueId)
        {
            if ((uint)valueId >= (uint)frame.Values.Length)
                throw new InvalidOperationException($"Value %{valueId} is out of range.");
            return frame.Values[valueId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Set(Frame frame, int valueId, Cell value)
        {
            if (valueId < 0)
                return;
            if ((uint)valueId >= (uint)frame.Values.Length)
                throw new InvalidOperationException($"Value %{valueId} is out of range.");

            int valueAbs = GetValueStorageAbs(frame, valueId);
            if (valueAbs >= 0)
            {
                var targetType = frame.ValueTypes[valueId];
                StoreValue(valueAbs, targetType, value);
                frame.Values[valueId] = new Cell(CellKind.Value, valueAbs, targetType.TypeId);
                return;
            }

            frame.Values[valueId] = value;
        }

        private Frame CreateFrame(RuntimeModule module, BytecodeFunction fn, RuntimeMethod method, Cell[] args, bool allowMissingArgs)
        {
            var layout = GetOrCreateMethodLayout(module, fn, method);
            if (args.Length != layout.ArgTypes.Length && !(allowMissingArgs && args.Length == 0))
                throw new InvalidOperationException($"Argument count mismatch for '{method.Name}': expected {layout.ArgTypes.Length}, got {args.Length}.");

            var valueTypes = new RuntimeType[fn.ValueTypeTokens.Length];
            for (int i = 0; i < valueTypes.Length; i++)
                valueTypes[i] = ResolveTypeToken(module, fn.ValueTypeTokens[i], method);

            var valueOffsets = new int[valueTypes.Length];
            var valueSizes = new int[valueTypes.Length];
            var valueFastKinds = new FastCellKind[valueTypes.Length];
            Array.Fill(valueOffsets, -1);

            int frameBase = AlignUp(_sp, 8);
            int cursor = frameBase;
            cursor = AlignUp(cursor, 8);
            int argsBase = cursor;
            cursor = checked(cursor + layout.ArgsAreaSize);
            cursor = AlignUp(cursor, 8);
            int localsBase = cursor;
            cursor = checked(cursor + layout.LocalsAreaSize);
            cursor = AlignUp(cursor, 8);
            int valuesBase = cursor;
            for (int i = 0; i < valueTypes.Length; i++)
            {
                var t = valueTypes[i];
                valueFastKinds[i] = ClassifyFastCell(t);
                if (!NeedsFixedValueStorage(t))
                    continue;

                var (size, align) = GetStorageSizeAlign(t);
                cursor = AlignUp(cursor, Math.Max(1, align));
                valueOffsets[i] = cursor - valuesBase;
                valueSizes[i] = size;
                cursor = checked(cursor + size);
            }
            cursor = AlignUp(cursor, 8);
            EnsureStack(cursor);
            Array.Clear(_mem, frameBase, cursor - frameBase);
            _sp = cursor;
            if (_sp > _stackPeakAbs) _stackPeakAbs = _sp;

            var frame = new Frame
            {
                Module = module,
                Function = fn,
                Method = method,
                ValueTypes = valueTypes,
                Layout = layout,
                ArgsBase = argsBase,
                LocalsBase = localsBase,
                FrameBase = frameBase,
                Pc = 0,
                ArgCells = layout.HasArgCellCache ? new Cell[layout.ArgTypes.Length] : Array.Empty<Cell>(),
                LocalCells = layout.HasLocalCellCache ? new Cell[layout.LocalTypes.Length] : Array.Empty<Cell>(),
                Values = new Cell[valueTypes.Length],
                ValueFastKinds = valueFastKinds,
                ValuesBase = valuesBase,
                ValueOffsets = valueOffsets,
                ValueSizes = valueSizes
            };

            if (layout.HasArgCellCache)
                InitializeFastCellCache(frame.ArgCells, layout.ArgFastKinds, layout.ArgCellCached);

            if (layout.HasLocalCellCache)
                InitializeFastCellCache(frame.LocalCells, layout.LocalFastKinds, layout.LocalCellCached);

            for (int i = 0; i < args.Length; i++)
                StoreArg(frame, i, args[i]);

            return frame;
        }

        private static void InitializeFastCellCache(Cell[] cells, FastCellKind[] kinds, bool[] cached)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cached[i])
                    cells[i] = DefaultFastCell(kinds[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Cell DefaultFastCell(FastCellKind kind)
        {
            return kind switch
            {
                FastCellKind.I8 => Cell.I8(0),
                FastCellKind.R4 or FastCellKind.R8 => Cell.R8(0.0),
                FastCellKind.NativeInt or FastCellKind.NativeUInt => RuntimeTypeSystem.PointerSize == 8 ? Cell.I8(0) : Cell.I4(0),
                _ => Cell.I4(0)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Cell NormalizeFastCell(FastCellKind kind, Cell value)
        {
            return kind switch
            {
                FastCellKind.I4 => Cell.I4(FastI4(value)),
                FastCellKind.I8 => Cell.I8(FastI8(value)),
                FastCellKind.R4 => Cell.R8((float)FastR8(value)),
                FastCellKind.R8 => value.Kind == CellKind.R8 ? value : Cell.R8(value.AsR8()),
                FastCellKind.NativeInt or FastCellKind.NativeUInt => RuntimeTypeSystem.PointerSize == 8
                    ? Cell.I8(FastI8(value))
                    : Cell.I4(FastI4(value)),
                _ => value
            };
        }

        private MethodExecLayout GetOrCreateMethodLayout(RuntimeModule module, BytecodeFunction fn, RuntimeMethod method)
        {
            if (_methodLayouts.TryGetValue(method.MethodId, out var cached))
                return cached;

            var layout = new MethodExecLayout { Method = method };
            int argCount = method.HasThis ? method.ParameterTypes.Length + 1 : method.ParameterTypes.Length;
            layout.ArgTypes = new RuntimeType[argCount];
            layout.ArgOffsets = new int[argCount];
            layout.ArgSizes = new int[argCount];
            layout.ArgFastKinds = new FastCellKind[argCount];
            layout.ArgAddressTaken = new bool[argCount];
            layout.ArgCellCached = new bool[argCount];

            int cur = 0;
            for (int i = 0; i < argCount; i++)
            {
                var t = GetArgType(method, i);
                var (size, align) = GetStorageSizeAlign(t);
                cur = AlignUp(cur, align);
                layout.ArgTypes[i] = t;
                layout.ArgOffsets[i] = cur;
                layout.ArgSizes[i] = size;
                layout.ArgFastKinds[i] = ClassifyFastCell(t);
                cur = checked(cur + size);
            }
            layout.ArgsAreaSize = cur;

            layout.LocalTypes = new RuntimeType[fn.LocalTypeTokens.Length];
            layout.LocalOffsets = new int[fn.LocalTypeTokens.Length];
            layout.LocalSizes = new int[fn.LocalTypeTokens.Length];
            layout.LocalFastKinds = new FastCellKind[fn.LocalTypeTokens.Length];
            layout.LocalAddressTaken = new bool[fn.LocalTypeTokens.Length];
            layout.LocalCellCached = new bool[fn.LocalTypeTokens.Length];

            AnalyzeAddressTaken(fn.Instructions, layout.ArgAddressTaken, layout.LocalAddressTaken);

            cur = 0;
            for (int i = 0; i < fn.LocalTypeTokens.Length; i++)
            {
                var t = ResolveTypeToken(module, fn.LocalTypeTokens[i], method);
                var (size, align) = GetStorageSizeAlign(t);
                cur = AlignUp(cur, align);
                layout.LocalTypes[i] = t;
                layout.LocalOffsets[i] = cur;
                layout.LocalSizes[i] = size;
                layout.LocalFastKinds[i] = ClassifyFastCell(t);
                cur = checked(cur + size);
            }
            layout.LocalsAreaSize = cur;

            for (int i = 0; i < layout.ArgCellCached.Length; i++)
            {
                layout.ArgCellCached[i] = layout.ArgFastKinds[i] != FastCellKind.None && !layout.ArgAddressTaken[i];
                layout.HasArgCellCache |= layout.ArgCellCached[i];
            }

            for (int i = 0; i < layout.LocalCellCached.Length; i++)
            {
                layout.LocalCellCached[i] = layout.LocalFastKinds[i] != FastCellKind.None && !layout.LocalAddressTaken[i];
                layout.HasLocalCellCache |= layout.LocalCellCached[i];
            }

            _methodLayouts.Add(method.MethodId, layout);
            return layout;
        }

        private static void AnalyzeAddressTaken(ImmutableArray<Instruction> instructions, bool[] args, bool[] locals)
        {
            foreach (var ins in instructions)
            {
                switch (ins.Op)
                {
                    case Op.Ldarga:
                        Mark(args, ins.Operand0);
                        break;
                    case Op.Ldarga_0:
                        Mark(args, 0);
                        break;
                    case Op.Ldarga_1:
                        Mark(args, 1);
                        break;
                    case Op.Ldarga_2:
                        Mark(args, 2);
                        break;
                    case Op.Ldarga_3:
                        Mark(args, 3);
                        break;
                    case Op.Ldloca:
                        Mark(locals, ins.Operand0);
                        break;
                    case Op.Ldloca_0:
                        Mark(locals, 0);
                        break;
                    case Op.Ldloca_1:
                        Mark(locals, 1);
                        break;
                    case Op.Ldloca_2:
                        Mark(locals, 2);
                        break;
                    case Op.Ldloca_3:
                        Mark(locals, 3);
                        break;
                }
            }

            static void Mark(bool[] taken, int index)
            {
                if ((uint)index < (uint)taken.Length)
                    taken[index] = true;
            }
        }

        private RuntimeType GetArgType(RuntimeMethod method, int argIndex)
        {
            if (method.HasThis)
            {
                if (argIndex == 0)
                    return method.DeclaringType.IsValueType ? _rts.GetByRefType(method.DeclaringType) : method.DeclaringType;
                return method.ParameterTypes[argIndex - 1];
            }
            return method.ParameterTypes[argIndex];
        }

        private RuntimeMethod ResolveRuntimeMethod(RuntimeModule module, int methodToken, RuntimeMethod? context)
        {
            try
            {
                return _rts.ResolveMethodInMethodContext(module, methodToken, context);
            }
            catch (Exception ex)
            {
                throw new MissingMethodException($"RuntimeMethod not found: {module.Name} 0x{methodToken:X8}", ex);
            }
        }

        private RuntimeType ResolveTypeToken(RuntimeModule module, int typeToken, RuntimeMethod? context)
        {
            try
            {
                return _rts.ResolveTypeInMethodContext(module, typeToken, context);
            }
            catch (Exception ex)
            {
                throw new TypeLoadException($"RuntimeType not found: {module.Name} 0x{typeToken:X8}", ex);
            }
        }

        private RuntimeField ResolveField(Frame frame, int fieldToken)
            => _rts.ResolveFieldInMethodContext(frame.Module, fieldToken, frame.Method);

        private (int size, int align) GetStorageSizeAlign(RuntimeType type)
            => _rts.GetStorageSizeAlign(type);

        private static bool NeedsFixedValueStorage(RuntimeType type)
        {
            if (!type.IsValueType) return false;
            if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef) return false;
            if (IsScalarLike(type)) return false;
            if (IsSystemType(type, "Single") || IsSystemType(type, "Double")) return false;
            return true;
        }

        private int GetValueStorageAbs(Frame frame, int valueId)
        {
            if ((uint)valueId >= (uint)frame.ValueOffsets.Length)
                return -1;
            int offset = frame.ValueOffsets[valueId];
            return offset < 0 ? -1 : checked(frame.ValuesBase + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Cell LoadLocal(Frame frame, int index)
        {
            var layout = frame.Layout;
            if (layout.LocalCellCached[index])
                return frame.LocalCells[index];

            int abs = frame.LocalsBase + layout.LocalOffsets[index];
            var fk = layout.LocalFastKinds[index];
            if (fk != FastCellKind.None)
                return LoadFastCell(abs, fk);

            return LoadValue(abs, layout.LocalTypes[index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreLocal(Frame frame, int index, Cell value)
        {
            var layout = frame.Layout;
            if (layout.LocalCellCached[index])
            {
                frame.LocalCells[index] = NormalizeFastCell(layout.LocalFastKinds[index], value);
                return;
            }

            int abs = frame.LocalsBase + layout.LocalOffsets[index];
            var fk = layout.LocalFastKinds[index];
            if (fk != FastCellKind.None)
            {
                StoreFastCell(abs, fk, value);
                return;
            }

            StoreValue(abs, layout.LocalTypes[index], value);
        }

        private Cell LocalAddress(Frame frame, int index)
        {
            int abs = frame.LocalsBase + frame.Layout.LocalOffsets[index];
            return Cell.ByRef(abs, frame.Layout.LocalSizes[index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Cell LoadArg(Frame frame, int index)
        {
            var layout = frame.Layout;
            if (layout.ArgCellCached[index])
                return frame.ArgCells[index];

            int abs = frame.ArgsBase + layout.ArgOffsets[index];
            var fk = layout.ArgFastKinds[index];
            if (fk != FastCellKind.None)
                return LoadFastCell(abs, fk);

            return LoadValue(abs, layout.ArgTypes[index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreArg(Frame frame, int index, Cell value)
        {
            var layout = frame.Layout;
            if (layout.ArgCellCached[index])
            {
                frame.ArgCells[index] = NormalizeFastCell(layout.ArgFastKinds[index], value);
                return;
            }

            int abs = frame.ArgsBase + layout.ArgOffsets[index];
            var fk = layout.ArgFastKinds[index];
            if (fk != FastCellKind.None)
            {
                StoreFastCell(abs, fk, value);
                return;
            }

            StoreValue(abs, layout.ArgTypes[index], value);
        }

        private Cell ArgAddress(Frame frame, int index)
        {
            int abs = frame.ArgsBase + frame.Layout.ArgOffsets[index];
            return Cell.ByRef(abs, frame.Layout.ArgSizes[index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Cell LoadFastCell(int abs, FastCellKind kind)
        {
            return kind switch
            {
                FastCellKind.I4 => Cell.I4(ReadI32Unchecked(abs)),
                FastCellKind.I8 => Cell.I8(ReadI64Unchecked(abs)),
                FastCellKind.R4 => Cell.R8(ReadR4Unchecked(abs)),
                FastCellKind.R8 => new Cell(CellKind.R8, ReadI64Unchecked(abs)),
                FastCellKind.NativeInt or FastCellKind.NativeUInt => RuntimeTypeSystem.PointerSize == 8
                    ? Cell.I8(ReadI64Unchecked(abs))
                    : Cell.I4(ReadI32Unchecked(abs)),
                _ => throw new InvalidOperationException($"Unsupported fast cell kind: {kind}.")
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreFastCell(int abs, FastCellKind kind, Cell value)
        {
            switch (kind)
            {
                case FastCellKind.I4:
                    WriteI32Unchecked(abs, FastI4(value));
                    return;
                case FastCellKind.I8:
                    WriteI64Unchecked(abs, FastI8(value));
                    return;
                case FastCellKind.R4:
                    WriteR4Unchecked(abs, (float)FastR8(value));
                    return;
                case FastCellKind.R8:
                    WriteI64Unchecked(abs, value.Kind == CellKind.R8 ? value.Payload : BitConverter.DoubleToInt64Bits(value.AsR8()));
                    return;
                case FastCellKind.NativeInt:
                case FastCellKind.NativeUInt:
                    int pointerSize = RuntimeTypeSystem.PointerSize;
                    if (pointerSize == 8)
                        WriteI64Unchecked(abs, FastI8(value));
                    else
                        WriteI32Unchecked(abs, FastI4(value));
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported fast cell kind: {kind}.");
            }
        }

        private Cell LoadValue(int abs, RuntimeType type)
        {
            CheckRange(abs, Math.Max(0, GetStorageSizeAlign(type).size));

            if (type.IsReferenceType)
            {
                long raw = ReadNativeInt(abs);
                return raw == 0 ? Cell.Null : Cell.Ref(checked((int)raw));
            }

            if (type.Kind == RuntimeTypeKind.Pointer)
                return Cell.Ptr(checked((int)ReadNativeInt(abs)), GetPointedElementSize(type));

            if (type.Kind == RuntimeTypeKind.ByRef)
                return Cell.ByRef(checked((int)ReadNativeInt(abs)), GetPointedElementSize(type));

            if (IsSystemType(type, "Single"))
                return Cell.R8(ReadR4Unchecked(abs));

            if (IsSystemType(type, "Double"))
                return Cell.R8(ReadR8Unchecked(abs));

            if (type.Kind == RuntimeTypeKind.Enum)
            {
                var underlying = TryGetEnumUnderlyingType(type);
                if (underlying is not null)
                    return LoadValue(abs, underlying);
            }

            if (IsScalarLike(type))
            {
                if (type.Namespace == "System")
                {
                    switch (type.Name)
                    {
                        case "SByte":
                            return Cell.I4(unchecked((sbyte)_mem[abs]));
                        case "Byte":
                        case "Boolean":
                            return Cell.I4(_mem[abs]);
                        case "Int16":
                            return Cell.I4(ReadI16Unchecked(abs));
                        case "UInt16":
                        case "Char":
                            return Cell.I4(ReadU16Unchecked(abs));
                        case "Int32":
                        case "UInt32":
                            return Cell.I4(ReadI32Unchecked(abs));
                        case "Int64":
                        case "UInt64":
                            return Cell.I8(ReadI64Unchecked(abs));
                        case "IntPtr":
                        case "UIntPtr":
                            return RuntimeTypeSystem.PointerSize == 4
                                ? Cell.I4(ReadI32Unchecked(abs))
                                : Cell.I8(ReadI64Unchecked(abs));
                    }
                }

                return type.SizeOf switch
                {
                    1 => Cell.I4(_mem[abs]),
                    2 => Cell.I4(ReadU16Unchecked(abs)),
                    4 => Cell.I4(ReadI32Unchecked(abs)),
                    8 => Cell.I8(ReadI64Unchecked(abs)),
                    _ => LoadValueBlob(abs, type)
                };
            }

            return LoadValueBlob(abs, type);
        }

        private Cell LoadValueBlob(int abs, RuntimeType type)
        {
            int size = type.SizeOf;
            int dst = AllocStackBytes(size, Math.Max(1, type.AlignOf));
            Buffer.BlockCopy(_mem, abs, _mem, dst, size);
            return new Cell(CellKind.Value, dst, type.TypeId);
        }

        private void StoreValue(int abs, RuntimeType type, Cell value)
        {
            var (size, _) = GetStorageSizeAlign(type);
            CheckWritableRange(abs, Math.Max(0, size));

            if (type.IsReferenceType)
            {
                WriteNativeInt(abs, value.Kind == CellKind.Null ? 0 : RequireObjectRef(value));
                return;
            }

            if (type.Kind == RuntimeTypeKind.Pointer)
            {
                WriteNativeInt(abs, CoercePointerAddress(value));
                return;
            }

            if (type.Kind == RuntimeTypeKind.ByRef)
            {
                WriteNativeInt(abs, value.Kind == CellKind.Null ? 0 : RequireByRefAddress(value));
                return;
            }

            if (type.Kind == RuntimeTypeKind.Enum)
            {
                var underlying = TryGetEnumUnderlyingType(type);
                if (underlying is not null)
                {
                    StoreValue(abs, underlying, value);
                    return;
                }
            }

            if (value.Kind == CellKind.Value)
            {
                Buffer.BlockCopy(_mem, checked((int)value.Payload), _mem, abs, size);
                return;
            }

            if (IsSystemType(type, "Single"))
            {
                WriteR4Unchecked(abs, (float)value.AsR8());
                return;
            }

            if (IsSystemType(type, "Double"))
            {
                WriteR8Unchecked(abs, value.AsR8());
                return;
            }

            if (IsScalarLike(type))
            {
                switch (type.SizeOf)
                {
                    case 1:
                        _mem[abs] = unchecked((byte)value.AsI4());
                        return;
                    case 2:
                        WriteU16Unchecked(abs, unchecked((ushort)value.AsI4()));
                        return;
                    case 4:
                        WriteI32Unchecked(abs, value.AsI4());
                        return;
                    case 8:
                        WriteI64Unchecked(abs, value.AsI8());
                        return;
                }
            }

            throw new InvalidOperationException($"Cannot store {value.Kind} into '{type.Namespace}.{type.Name}'.");
        }

        private Cell DefaultValue(RuntimeType type)
        {
            if (type.IsReferenceType)
                return Cell.Null;
            if (type.Kind == RuntimeTypeKind.Pointer)
                return Cell.Ptr(0, GetPointedElementSize(type));
            if (type.Kind == RuntimeTypeKind.ByRef)
                return Cell.ByRef(0, GetPointedElementSize(type));
            if (IsSystemType(type, "Single") || IsSystemType(type, "Double"))
                return Cell.R8(0);
            if (type.Kind == RuntimeTypeKind.Enum)
            {
                var underlying = TryGetEnumUnderlyingType(type);
                if (underlying is not null)
                    return DefaultValue(underlying);
            }
            if (IsScalarLike(type))
                return type.SizeOf == 8 ? Cell.I8(0) : Cell.I4(0);
            int abs = AllocStackBytes(type.SizeOf, Math.Max(1, type.AlignOf));
            Array.Clear(_mem, abs, type.SizeOf);
            return new Cell(CellKind.Value, abs, type.TypeId);
        }

        private static bool IsSystemType(RuntimeType type, string name)
            => StringComparer.Ordinal.Equals(type.Namespace, "System") && StringComparer.Ordinal.Equals(type.Name, name);

        private static bool IsScalarLike(RuntimeType type)
        {
            if (type.Kind == RuntimeTypeKind.Enum)
                return true;
            if (type.Namespace != "System")
                return false;
            return type.Name is "Boolean" or "Char" or "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr";
        }

        private static FastCellKind ClassifyFastCell(RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                return FastCellKind.None;

            if (type.Kind == RuntimeTypeKind.Enum)
                type = TryGetEnumUnderlyingType(type) ?? type;

            if (IsSystemType(type, "Single"))
                return FastCellKind.R4;
            if (IsSystemType(type, "Double"))
                return FastCellKind.R8;

            if (type.Namespace != "System")
                return FastCellKind.None;

            return type.Name switch
            {
                "Int32" or "UInt32" => FastCellKind.I4,
                "Int64" or "UInt64" => FastCellKind.I8,
                "IntPtr" => FastCellKind.NativeInt,
                "UIntPtr" => FastCellKind.NativeUInt,
                _ => FastCellKind.None
            };
        }

        private static int CompareEqual(Cell a, Cell b)
        {
            if (a.Kind == CellKind.Null && b.Kind == CellKind.Null) return 1;
            if (a.Kind == CellKind.Null) return IsReferenceLike(b) && b.Payload == 0 ? 1 : 0;
            if (b.Kind == CellKind.Null) return IsReferenceLike(a) && a.Payload == 0 ? 1 : 0;
            if (IsReferenceLike(a) || IsReferenceLike(b)) return a.Payload == b.Payload ? 1 : 0;
            if (IsFloating(a) || IsFloating(b)) return a.AsR8() == b.AsR8() ? 1 : 0;
            if (IsI8(a) || IsI8(b)) return a.AsI8() == b.AsI8() ? 1 : 0;
            return a.AsI4() == b.AsI4() ? 1 : 0;
        }

        private static int CompareLess(Cell a, Cell b)
        {
            if (IsFloating(a) || IsFloating(b)) return a.AsR8() < b.AsR8() ? 1 : 0;
            if (IsI8(a) || IsI8(b)) return a.AsI8() < b.AsI8() ? 1 : 0;
            return a.AsI4() < b.AsI4() ? 1 : 0;
        }

        private static int CompareGreater(Cell a, Cell b)
        {
            if (IsFloating(a) || IsFloating(b)) return a.AsR8() > b.AsR8() ? 1 : 0;
            if (IsI8(a) || IsI8(b)) return a.AsI8() > b.AsI8() ? 1 : 0;
            return a.AsI4() > b.AsI4() ? 1 : 0;
        }

        private static int CompareLessUn(Cell a, Cell b)
        {
            if (IsI8(a) || IsI8(b)) return (ulong)a.AsI8() < (ulong)b.AsI8() ? 1 : 0;
            return (uint)a.AsI4() < (uint)b.AsI4() ? 1 : 0;
        }

        private static int CompareGreaterUn(Cell a, Cell b)
        {
            if (IsI8(a) || IsI8(b)) return (ulong)a.AsI8() > (ulong)b.AsI8() ? 1 : 0;
            return (uint)a.AsI4() > (uint)b.AsI4() ? 1 : 0;
        }

        private static bool IsReferenceLike(Cell c)
            => c.Kind is CellKind.Ref or CellKind.Ptr or CellKind.ByRef or CellKind.Null;

        private Cell Conv(Cell value, NumericConvKind kind, NumericConvFlags flags, RuntimeType? targetType)
        {
            bool sourceUnsigned = (flags & NumericConvFlags.SourceUnsigned) != 0;
            bool checkedConv = (flags & NumericConvFlags.Checked) != 0;

            if (targetType is not null && targetType.Kind == RuntimeTypeKind.Pointer)
            {
                long addr = ToNativeAddress(value, sourceUnsigned, checkedConv);
                return Cell.Ptr(checked((int)addr), GetPointedElementSize(targetType));
            }

            static Cell MakeR4(double value) => Cell.R8((float)value);
            static Cell MakeR8(double value) => Cell.R8(value);

            try
            {
                if (value.Kind == CellKind.R8)
                {
                    double d = value.AsR8();
                    return kind switch
                    {
                        NumericConvKind.I1 => Cell.I4(checkedConv ? checked((sbyte)d) : unchecked((sbyte)d)),
                        NumericConvKind.U1 => Cell.I4(checkedConv ? checked((byte)d) : unchecked((byte)d)),
                        NumericConvKind.I2 => Cell.I4(checkedConv ? checked((short)d) : unchecked((short)d)),
                        NumericConvKind.U2 or NumericConvKind.Char => Cell.I4(checkedConv ? checked((ushort)d) : unchecked((ushort)d)),
                        NumericConvKind.I4 => Cell.I4(checkedConv ? checked((int)d) : unchecked((int)d)),
                        NumericConvKind.U4 => Cell.I4(unchecked((int)(checkedConv ? checked((uint)d) : unchecked((uint)d)))),
                        NumericConvKind.I8 => Cell.I8(checkedConv ? checked((long)d) : unchecked((long)d)),
                        NumericConvKind.U8 => Cell.I8(unchecked((long)(checkedConv ? checked((ulong)d) : unchecked((ulong)d)))),
                        NumericConvKind.R4 => MakeR4(d),
                        NumericConvKind.R8 => value,
                        NumericConvKind.Bool => Cell.I4(d != 0.0 ? 1 : 0),
                        NumericConvKind.NativeInt => RuntimeTypeSystem.PointerSize == 8
                            ? Cell.I8(checkedConv ? checked((long)d) : unchecked((long)d))
                            : Cell.I4(checkedConv ? checked((int)d) : unchecked((int)d)),
                        NumericConvKind.NativeUInt => RuntimeTypeSystem.PointerSize == 8
                            ? Cell.I8(unchecked((long)(checkedConv ? checked((ulong)d) : unchecked((ulong)d))))
                            : Cell.I4(unchecked((int)(checkedConv ? checked((uint)d) : unchecked((uint)d)))),
                        _ => throw new NotSupportedException($"Conv {kind} is not supported.")
                    };
                }

                long s = ToSigned64(value);
                ulong u = ToUnsigned64(value);

                return kind switch
                {
                    NumericConvKind.I1 => Cell.I4(sourceUnsigned ? (checkedConv ? checked((sbyte)u) : unchecked((sbyte)u)) : (checkedConv ? checked((sbyte)s) : unchecked((sbyte)s))),
                    NumericConvKind.U1 => Cell.I4(sourceUnsigned ? (checkedConv ? checked((byte)u) : unchecked((byte)u)) : (checkedConv ? checked((byte)s) : unchecked((byte)s))),
                    NumericConvKind.I2 => Cell.I4(sourceUnsigned ? (checkedConv ? checked((short)u) : unchecked((short)u)) : (checkedConv ? checked((short)s) : unchecked((short)s))),
                    NumericConvKind.U2 or NumericConvKind.Char => Cell.I4(sourceUnsigned ? (checkedConv ? checked((ushort)u) : unchecked((ushort)u)) : (checkedConv ? checked((ushort)s) : unchecked((ushort)s))),
                    NumericConvKind.I4 => Cell.I4(sourceUnsigned ? (checkedConv ? checked((int)u) : unchecked((int)u)) : (checkedConv ? checked((int)s) : unchecked((int)s))),
                    NumericConvKind.U4 => Cell.I4(unchecked((int)(sourceUnsigned ? (checkedConv ? checked((uint)u) : unchecked((uint)u)) : (checkedConv ? checked((uint)s) : unchecked((uint)s))))),
                    NumericConvKind.I8 => Cell.I8(sourceUnsigned ? (checkedConv ? checked((long)u) : unchecked((long)u)) : s),
                    NumericConvKind.U8 => Cell.I8(unchecked((long)(sourceUnsigned ? u : (checkedConv ? checked((ulong)s) : unchecked((ulong)s))))),
                    NumericConvKind.R4 => MakeR4(sourceUnsigned ? (double)u : (double)s),
                    NumericConvKind.R8 => MakeR8(sourceUnsigned ? (double)u : (double)s),
                    NumericConvKind.Bool => Cell.I4(sourceUnsigned ? (u != 0 ? 1 : 0) : (s != 0 ? 1 : 0)),
                    NumericConvKind.NativeInt => RuntimeTypeSystem.PointerSize == 8
                        ? Cell.I8(sourceUnsigned ? (checkedConv ? checked((long)u) : unchecked((long)u)) : s)
                        : Cell.I4(sourceUnsigned ? (checkedConv ? checked((int)u) : unchecked((int)u)) : (checkedConv ? checked((int)s) : unchecked((int)s))),
                    NumericConvKind.NativeUInt => RuntimeTypeSystem.PointerSize == 8
                        ? Cell.I8(unchecked((long)(sourceUnsigned ? u : (checkedConv ? checked((ulong)s) : unchecked((ulong)s)))))
                        : Cell.I4(unchecked((int)(sourceUnsigned ? (checkedConv ? checked((uint)u) : unchecked((uint)u)) : (checkedConv ? checked((uint)s) : unchecked((uint)s))))),
                    _ => throw new NotSupportedException($"Conv {kind} is not supported.")
                };
            }
            catch (OverflowException) when (checkedConv)
            {
                throw;
            }
        }

        private static long ToSigned64(Cell value)
        {
            return value.Kind switch
            {
                CellKind.I4 => unchecked((int)value.Payload),
                CellKind.I8 => value.Payload,
                CellKind.Null => 0,
                CellKind.Ref or CellKind.Ptr or CellKind.ByRef => value.Payload,
                _ => throw new InvalidOperationException($"Conv source must be numeric or native address, got {value.Kind}.")
            };
        }

        private static ulong ToUnsigned64(Cell value)
        {
            return value.Kind switch
            {
                CellKind.I4 => unchecked((uint)(int)value.Payload),
                CellKind.I8 => unchecked((ulong)value.Payload),
                CellKind.Null => 0,
                CellKind.Ref or CellKind.Ptr or CellKind.ByRef => unchecked((ulong)value.Payload),
                _ => throw new InvalidOperationException($"Conv source must be numeric or native address, got {value.Kind}.")
            };
        }

        private static long ToNativeAddress(Cell value, bool sourceUnsigned, bool checkedConv)
        {
            if (value.Kind is CellKind.Ptr or CellKind.ByRef or CellKind.Ref)
                return value.Payload;
            if (value.Kind == CellKind.Null)
                return 0;
            if (sourceUnsigned)
            {
                ulong u = ToUnsigned64(value);
                return checkedConv ? checked((long)u) : unchecked((long)u);
            }
            return ToSigned64(value);
        }

        private Cell ExecCall(Frame frame, Instruction ins, bool virtualDispatch, CancellationToken ct, ExecutionLimits limits)
        {
            int packed = ins.Operand1;
            int argCount = packed & 0x7FFF;
            int hasThis = (packed >> 15) & 1;
            int total = argCount + hasThis;
            if (ins.ValueCount != total)
                throw new InvalidOperationException($"Call value count mismatch: expected {total}, got {ins.ValueCount}.");

            var args = new Cell[total];
            for (int i = 0; i < total; i++) args[i] = Get(frame, ins.GetValue(i));

            var declared = ResolveRuntimeMethod(frame.Module, ins.Operand0, frame.Method);
            RuntimeMethod target = declared;

            if (virtualDispatch)
            {
                if (total == 0)
                    throw new InvalidOperationException("callvirt without receiver.");
                var receiverType = GetObjectTypeFromRef(args[0]);
                target = ResolveVirtualDispatch(receiverType, declared);
            }

            if (target.HasThis && target.DeclaringType.IsValueType && args.Length != 0 && args[0].Kind == CellKind.Ref)
            {
                int objAbs = checked((int)args[0].Payload);
                var actual = GetObjectTypeFromRef(args[0]);
                if (actual.TypeId != target.DeclaringType.TypeId)
                    throw new InvalidOperationException("Boxed receiver type mismatch.");
                args[0] = Cell.ByRef(objAbs + ObjectHeaderSize, target.DeclaringType.SizeOf);
            }

            if (target.IsStatic && !StringComparer.Ordinal.Equals(target.Name, ".cctor"))
                EnsureTypeInitialized(target.DeclaringType, ct, limits);

            if (target.IsStatic && TryInvokeHostOverride(target, args, ct, out var hostResult))
                return hostResult;

            if (TryInvokeInternal(target, args, out var internalResult))
                return internalResult;

            if (target.BodyModule is null || target.Body is null)
            {
                var (moduleOpt, fn) = _domain.ResolveCall(frame.Module, ins.Operand0);
                var module = moduleOpt ?? frame.Module;
                return Invoke(module, fn, target, args, ct, limits);
            }

            return Invoke(target.BodyModule, target.Body, target, args, ct, limits);
        }

        private Cell ExecNewobj(Frame frame, Instruction ins, CancellationToken ct, ExecutionLimits limits)
        {
            var ctor = ResolveRuntimeMethod(frame.Module, ins.Operand0, frame.Method);
            EnsureTypeInitialized(ctor.DeclaringType, ct, limits);
            int argCount = ins.Operand1;
            if (ins.ValueCount != argCount)
                throw new InvalidOperationException($"newobj value count mismatch: expected {argCount}, got {ins.ValueCount}.");

            var rawArgs = new Cell[argCount];
            for (int i = 0; i < argCount; i++) rawArgs[i] = Get(frame, ins.GetValue(i));

            if (IsSystemType(ctor.DeclaringType, "String"))
                return ExecStringNewobj(ctor, rawArgs);

            var args = new Cell[argCount + 1];
            for (int i = 0; i < argCount; i++) args[i + 1] = rawArgs[i];

            Cell result;
            if (ctor.DeclaringType.IsValueType)
            {
                int abs = AllocStackBytes(ctor.DeclaringType.SizeOf, Math.Max(1, ctor.DeclaringType.AlignOf));
                Array.Clear(_mem, abs, ctor.DeclaringType.SizeOf);
                result = new Cell(CellKind.Value, abs, ctor.DeclaringType.TypeId);
                args[0] = Cell.ByRef(abs, ctor.DeclaringType.SizeOf);
            }
            else
            {
                int obj = IsSystemType(ctor.DeclaringType, "String") ? AllocStringUninitialized(0) : AllocObject(ctor.DeclaringType);
                result = Cell.Ref(obj);
                args[0] = result;
            }

            if (ctor.BodyModule is null || ctor.Body is null)
            {
                if (!TryInvokeInternal(ctor, args, out _))
                {
                    var (moduleOpt, fn) = _domain.ResolveCall(frame.Module, ins.Operand0);
                    var module = moduleOpt ?? frame.Module;
                    _ = Invoke(module, fn, ctor, args, ct, limits);
                }
            }
            else
            {
                _ = Invoke(ctor.BodyModule, ctor.Body, ctor, args, ct, limits);
            }

            return result;
        }


        private Cell ExecStringNewobj(RuntimeMethod ctor, Cell[] args)
        {
            if (args.Length == 2 && IsCharLike(ctor.ParameterTypes[0]) && IsInt32Like(ctor.ParameterTypes[1]))
            {
                int length = args[1].AsI4();
                if (length < 0) throw new ArgumentOutOfRangeException();
                int obj = AllocStringUninitialized(length);
                ushort ch = unchecked((ushort)args[0].AsI4());
                int dst = obj + StringCharsOffset;
                for (int i = 0; i < length; i++)
                    WriteU16Unchecked(dst + i * 2, ch);
                return Cell.Ref(obj);
            }

            if (args.Length == 1 && ctor.ParameterTypes[0].Kind == RuntimeTypeKind.Array)
            {
                return Cell.Ref(CreateStringFromCharArray(args[0], 0, ReadArrayLength(args[0])));
            }

            if (args.Length == 3 && ctor.ParameterTypes[0].Kind == RuntimeTypeKind.Array)
            {
                return Cell.Ref(CreateStringFromCharArray(args[0], args[1].AsI4(), args[2].AsI4()));
            }

            if (args.Length == 1 && ctor.ParameterTypes[0].Kind == RuntimeTypeKind.Pointer)
            {
                int src = RequireAddress(args[0]);
                int len = 0;
                while (ReadU16Unchecked(src + len * 2) != 0)
                    len++;
                int obj = AllocStringUninitialized(len);
                Buffer.BlockCopy(_mem, src, _mem, obj + StringCharsOffset, len * 2);
                return Cell.Ref(obj);
            }

            return Cell.Ref(AllocStringUninitialized(0));
        }

        private int CreateStringFromCharArray(Cell array, int start, int length)
        {
            if (array.Kind == CellKind.Null) throw new ArgumentNullException();
            if (start < 0 || length < 0) throw new ArgumentOutOfRangeException();
            int arr = RequireObjectRef(array);
            int arrLen = ReadI32(arr + ArrayLengthOffset);
            if (start > arrLen || length > arrLen - start) throw new ArgumentOutOfRangeException();
            int obj = AllocStringUninitialized(length);
            Buffer.BlockCopy(_mem, arr + ArrayDataOffset + start * 2, _mem, obj + StringCharsOffset, length * 2);
            return obj;
        }

        private static bool IsCharLike(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Char";

        private static bool IsInt32Like(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Int32";

        private Cell ExecLdfld(Frame frame, Instruction ins)
        {
            var field = ResolveField(frame, ins.Operand0);
            int abs = GetInstanceFieldAddress(field, Get(frame, ins.Value0));
            return LoadValue(abs, field.FieldType);
        }

        private void ExecStfld(Frame frame, Instruction ins)
        {
            var field = ResolveField(frame, ins.Operand0);
            int abs = GetInstanceFieldAddress(field, Get(frame, ins.Value0));
            StoreValue(abs, field.FieldType, Get(frame, ins.Value1));
        }

        private Cell ExecLdflda(Frame frame, Instruction ins)
        {
            var field = ResolveField(frame, ins.Operand0);
            int abs = GetInstanceFieldAddress(field, Get(frame, ins.Value0));
            var (size, _) = GetStorageSizeAlign(field.FieldType);
            return Cell.ByRef(abs, size);
        }

        private Cell ExecLdsfld(Frame frame, Instruction ins, CancellationToken ct, ExecutionLimits limits)
        {
            var field = ResolveField(frame, ins.Operand0);
            if (!field.IsStatic)
                throw new InvalidOperationException($"Field '{field.Name}' is not static.");
            EnsureTypeInitialized(field.DeclaringType, ct, limits);
            int abs = GetStaticFieldAddress(field);
            return LoadValue(abs, field.FieldType);
        }

        private void ExecStsfld(Frame frame, Instruction ins, CancellationToken ct, ExecutionLimits limits)
        {
            var field = ResolveField(frame, ins.Operand0);
            if (!field.IsStatic)
                throw new InvalidOperationException($"Field '{field.Name}' is not static.");
            EnsureTypeInitialized(field.DeclaringType, ct, limits);
            int abs = GetStaticFieldAddress(field);
            StoreValue(abs, field.FieldType, Get(frame, ins.Value0));
        }

        private Cell ExecLdsflda(Frame frame, Instruction ins, CancellationToken ct, ExecutionLimits limits)
        {
            var field = ResolveField(frame, ins.Operand0);
            if (!field.IsStatic)
                throw new InvalidOperationException($"Field '{field.Name}' is not static.");
            EnsureTypeInitialized(field.DeclaringType, ct, limits);
            int abs = GetStaticFieldAddress(field);
            var (size, _) = GetStorageSizeAlign(field.FieldType);
            return Cell.ByRef(abs, size);
        }

        private int GetInstanceFieldAddress(RuntimeField field, Cell receiver)
        {
            if (field.IsStatic)
                throw new InvalidOperationException($"Field '{field.Name}' is static.");

            if (receiver.Kind == CellKind.Ref)
            {
                int objAbs = checked((int)receiver.Payload);
                var actualType = GetObjectTypeFromRef(receiver);
                field = _rts.BindFieldToReceiver(field, actualType);

                if (field.DeclaringType.IsValueType)
                {
                    if (actualType.TypeId != field.DeclaringType.TypeId)
                        throw new InvalidOperationException("Boxed field receiver type mismatch.");
                    return checked(objAbs + ObjectHeaderSize + field.Offset);
                }

                if (!IsAssignableTo(actualType, field.DeclaringType))
                    throw new InvalidOperationException($"Field '{field.Name}' is not valid for receiver '{actualType.Namespace}.{actualType.Name}'.");

                return checked(objAbs + field.Offset);
            }

            if (receiver.Kind is CellKind.ByRef or CellKind.Ptr)
                return checked((int)receiver.Payload + field.Offset);

            if (receiver.Kind == CellKind.Value)
                return checked((int)receiver.Payload + field.Offset);

            if (receiver.Kind == CellKind.Null)
                throw new NullReferenceException();

            throw new InvalidOperationException($"Invalid field receiver kind: {receiver.Kind}.");
        }

        private int GetStaticFieldAddress(RuntimeField field)
        {
            int baseAbs = EnsureStaticStorage(field.DeclaringType);
            if (baseAbs == 0)
                throw new InvalidOperationException($"Static storage is empty for '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}'.");
            return checked(baseAbs + field.Offset);
        }

        private int EnsureStaticStorage(RuntimeType type)
        {
            if (_staticBaseByTypeId.TryGetValue(type.TypeId, out int existing))
                return existing;
            int size = type.StaticSize;
            if (size <= 0)
            {
                _staticBaseByTypeId[type.TypeId] = 0;
                return 0;
            }
            int abs = AllocHeapBytes(size, Math.Max(8, type.StaticAlign));
            Array.Clear(_mem, abs, size);
            _staticBaseByTypeId[type.TypeId] = abs;
            int end = checked(abs + size);
            if (end > _heapFloor) _heapFloor = end;
            return abs;
        }

        private void EnsureTypeInitialized(RuntimeType type, CancellationToken ct, ExecutionLimits limits)
        {
            if (_typeInitState.TryGetValue(type.TypeId, out byte state))
            {
                if (state == 2 || state == 1)
                    return;
            }

            _ = EnsureStaticStorage(type);
            var cctor = FindTypeInitializer(type);
            if (cctor is null || cctor.BodyModule is null || cctor.Body is null)
            {
                _typeInitState[type.TypeId] = 2;
                return;
            }

            _typeInitState[type.TypeId] = 1;
            _ = Invoke(cctor.BodyModule, cctor.Body, cctor, Array.Empty<Cell>(), ct, limits);
            _typeInitState[type.TypeId] = 2;
        }

        private static RuntimeMethod? FindTypeInitializer(RuntimeType type)
        {
            for (int i = 0; i < type.Methods.Length; i++)
            {
                var m = type.Methods[i];
                if (m.IsStatic && m.ParameterTypes.Length == 0 && StringComparer.Ordinal.Equals(m.Name, ".cctor"))
                    return m;
            }
            return null;
        }

        private Cell ExecLdobj(Frame frame, Instruction ins)
        {
            var t = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            int abs = RequireAddress(Get(frame, ins.Value0));
            return LoadValue(abs, t);
        }

        private void ExecStobj(Frame frame, Instruction ins)
        {
            var t = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            int abs = RequireAddress(Get(frame, ins.Value0));
            StoreValue(abs, t, Get(frame, ins.Value1));
        }

        private Cell ExecNewarr(Frame frame, Instruction ins)
        {
            int len = Get(frame, ins.Value0).AsI4();
            if (len < 0) throw new InvalidOperationException("Negative array length.");
            var elem = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            var arrType = _rts.GetArrayType(elem);
            return Cell.Ref(AllocArrayObject(arrType, len));
        }

        private Cell ExecLdelem(Frame frame, Instruction ins)
        {
            var elem = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            int abs = GetArrayElementAddress(Get(frame, ins.Value0), Get(frame, ins.Value1).AsI4(), elem, write: false, value: default);
            return LoadValue(abs, elem);
        }

        private Cell ExecLdelema(Frame frame, Instruction ins)
        {
            var elem = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            int abs = GetArrayElementAddress(Get(frame, ins.Value0), Get(frame, ins.Value1).AsI4(), elem, write: false, value: default);
            var (size, _) = GetStorageSizeAlign(elem);
            return Cell.ByRef(abs, size);
        }

        private void ExecStelem(Frame frame, Instruction ins)
        {
            var elem = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            var value = Get(frame, ins.Value2);
            int abs = GetArrayElementAddress(Get(frame, ins.Value0), Get(frame, ins.Value1).AsI4(), elem, write: true, value: value);
            StoreValue(abs, elem, value);
        }

        private Cell ExecLdArrayDataRef(Cell array)
        {
            if (array.Kind == CellKind.Null) throw new NullReferenceException();
            if (array.Kind != CellKind.Ref) throw new InvalidOperationException("Array reference expected.");
            int arrAbs = checked((int)array.Payload);
            var t = GetObjectTypeFromRef(array);
            if (t.Kind != RuntimeTypeKind.Array || t.ElementType is null)
                throw new InvalidOperationException("Array object expected.");
            var (size, _) = GetStorageSizeAlign(t.ElementType);
            return Cell.ByRef(arrAbs + ArrayDataOffset, size);
        }

        private int GetArrayElementAddress(Cell array, int index, RuntimeType elementType, bool write, Cell value)
        {
            if (array.Kind == CellKind.Null) throw new NullReferenceException();
            if (array.Kind != CellKind.Ref) throw new InvalidOperationException("Array reference expected.");
            int arrAbs = checked((int)array.Payload);
            var actual = GetObjectTypeFromRef(array);
            if (actual.Kind != RuntimeTypeKind.Array || actual.ElementType is null)
                throw new InvalidOperationException("Array object expected.");
            if (actual.ArrayRank != 1)
                throw new NotSupportedException("Only single dimensional arrays are supported.");
            int len = ReadI32(arrAbs + ArrayLengthOffset);
            if ((uint)index >= (uint)len)
                throw new IndexOutOfRangeException();

            var actualElem = actual.ElementType;
            bool compatible = actualElem.TypeId == elementType.TypeId;
            if (!compatible && actualElem.IsReferenceType && elementType.IsReferenceType && IsAssignableTo(actualElem, elementType))
                compatible = true;
            if (!compatible)
                throw new ArrayTypeMismatchException();

            if (write && actualElem.IsReferenceType && value.Kind == CellKind.Ref)
            {
                var valueType = GetObjectTypeFromRef(value);
                if (!IsAssignableTo(valueType, actualElem))
                    throw new ArrayTypeMismatchException();
            }

            var (size, _) = GetStorageSizeAlign(actualElem);
            return checked(arrAbs + ArrayDataOffset + index * size);
        }

        private Cell ExecStackAlloc(Cell count, int elemSize)
        {
            int n = count.AsI4();
            if (n < 0) throw new InvalidOperationException("Negative stackalloc length.");
            int bytes = checked(n * elemSize);
            int abs = AllocStackBytes(bytes, Math.Min(Math.Max(elemSize, 1), 8));
            Array.Clear(_mem, abs, bytes);
            return Cell.Ptr(abs, elemSize);
        }

        private static Cell PtrElemAddr(Cell source, Cell offset, int elemSize)
        {
            int baseAbs = RequireAddress(source);
            long off = offset.AsI8();
            int abs = checked(baseAbs + checked((int)(off * elemSize)));
            return source.Kind == CellKind.ByRef ? Cell.ByRef(abs, elemSize) : Cell.Ptr(abs, elemSize);
        }

        private static Cell PtrToByRef(Cell ptr)
        {
            if (ptr.Kind != CellKind.Ptr)
                throw new InvalidOperationException($"ptr-to-byref expects Ptr, got {ptr.Kind}.");
            return Cell.ByRef(checked((int)ptr.Payload), ptr.Aux);
        }

        private static Cell PtrDiff(Cell left, Cell right, int elemSize)
        {
            long diff = checked((long)RequireAddress(left) - RequireAddress(right));
            return RuntimeTypeSystem.PointerSize == 8 ? Cell.I8(diff / elemSize) : Cell.I4(checked((int)(diff / elemSize)));
        }

        private Cell ExecCastClass(Frame frame, Instruction ins)
        {
            var target = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            var value = Get(frame, ins.Value0);
            if (value.Kind == CellKind.Null)
                return value;
            if (value.Kind != CellKind.Ref)
                throw new InvalidCastException();
            var actual = GetObjectTypeFromRef(value);
            if (!IsAssignableTo(actual, target))
                throw new InvalidCastException();
            return value;
        }

        private Cell ExecIsinst(Frame frame, Instruction ins)
        {
            var target = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            var value = Get(frame, ins.Value0);
            if (value.Kind == CellKind.Null)
                return value;
            if (value.Kind != CellKind.Ref)
                return Cell.Null;
            var actual = GetObjectTypeFromRef(value);
            return IsAssignableTo(actual, target) ? value : Cell.Null;
        }

        private Cell ExecBox(Frame frame, Instruction ins)
        {
            var type = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            var value = Get(frame, ins.Value0);
            if (!type.IsValueType)
            {
                if (value.Kind is CellKind.Ref or CellKind.Null)
                    return value;
                throw new InvalidOperationException($"box of reference type expects ref/null, got {value.Kind}.");
            }

            if (TryGetNullableInfo(type, out var underlying, out var hasValueField, out var valueField))
            {
                if (value.Kind != CellKind.Value)
                    throw new InvalidOperationException($"Nullable boxing expects value cell, got {value.Kind}.");
                var actual = GetValueType(value);
                if (actual.TypeId != type.TypeId)
                    throw new InvalidOperationException("Nullable boxing type mismatch.");

                int src = checked((int)value.Payload);
                bool hasValue = LoadValue(src + hasValueField.Offset, hasValueField.FieldType).AsI4() != 0;
                if (!hasValue)
                    return Cell.Null;

                var underlyingValue = LoadValue(src + valueField.Offset, underlying);
                int obj = AllocBoxedValueObject(underlying);
                StoreValue(obj + ObjectHeaderSize, underlying, underlyingValue);
                return Cell.Ref(obj);
            }

            int boxed = AllocBoxedValueObject(type);
            StoreValue(boxed + ObjectHeaderSize, type, value);
            return Cell.Ref(boxed);
        }

        private Cell ExecUnboxAny(Frame frame, Instruction ins)
        {
            var target = ResolveTypeToken(frame.Module, ins.Operand0, frame.Method);
            var boxed = Get(frame, ins.Value0);
            if (!target.IsValueType)
            {
                if (boxed.Kind == CellKind.Null)
                    return boxed;
                if (boxed.Kind != CellKind.Ref)
                    throw new InvalidCastException();
                var actualRefType = GetObjectTypeFromRef(boxed);
                if (!IsAssignableTo(actualRefType, target))
                    throw new InvalidCastException();
                return boxed;
            }

            if (TryGetNullableInfo(target, out var underlying, out var hasValueField, out var valueField))
            {
                if (boxed.Kind == CellKind.Null)
                    return DefaultValue(target);
                if (boxed.Kind != CellKind.Ref)
                    throw new InvalidCastException();

                int obj = checked((int)boxed.Payload);
                var actual = GetObjectTypeFromRef(boxed);
                if (actual.TypeId == target.TypeId)
                    return LoadValue(obj + ObjectHeaderSize, target);
                if (actual.TypeId != underlying.TypeId)
                    throw new InvalidCastException();

                int dst = AllocStackBytes(target.SizeOf, Math.Max(1, target.AlignOf));
                Array.Clear(_mem, dst, target.SizeOf);
                StoreValue(dst + hasValueField.Offset, hasValueField.FieldType, Cell.I4(1));
                StoreValue(dst + valueField.Offset, underlying, LoadValue(obj + ObjectHeaderSize, underlying));
                return new Cell(CellKind.Value, dst, target.TypeId);
            }

            if (boxed.Kind == CellKind.Null)
                throw new NullReferenceException();
            if (boxed.Kind != CellKind.Ref)
                throw new InvalidCastException();
            var actualBoxed = GetObjectTypeFromRef(boxed);
            if (actualBoxed.TypeId != target.TypeId)
                throw new InvalidCastException();
            return LoadValue(checked((int)boxed.Payload) + ObjectHeaderSize, target);
        }

        private Cell ExecLdstr(RuntimeModule module, int userStringToken)
        {
            if (MetadataToken.Table(userStringToken) != MetadataToken.UserString)
                throw new InvalidOperationException($"ldstr expects UserString token, got 0x{userStringToken:X8}.");
            string s = module.Md.GetUserString(MetadataToken.Rid(userStringToken));
            if (!_internPool.TryGetValue(s, out int obj))
            {
                obj = AllocStringFromManaged(s);
                _internPool.Add(s, obj);
            }
            return Cell.Ref(obj);
        }

        private bool TryInvokeInternal(RuntimeMethod method, Cell[] args, out Cell result)
        {
            result = default;
            if (!method.HasInternalCall) return false;

            var t = method.DeclaringType;
            string ns = t.Namespace;
            string tn = t.Name;
            string mn = method.Name;

            if (ns == "System" && tn == "Console" && mn == "_Write")
            {
                if (args.Length != 1) throw new InvalidOperationException("Console._Write expects one argument.");
                WriteConsoleArg(args[0], method.ParameterTypes.Length == 1 ? method.ParameterTypes[0] : null);
                return true;
            }

            if (ns == "System" && tn == "String")
            {
                if (mn == "get_Length")
                {
                    result = Cell.I4(ReadStringLength(args[0]));
                    return true;
                }
                if (mn == "GetRawStringData" || mn == "GetPinnableReference")
                {
                    int obj = RequireObjectRef(args[0]);
                    result = Cell.ByRef(obj + StringCharsOffset, 2);
                    return true;
                }
                if (mn == "FastAllocateString")
                {
                    result = Cell.Ref(AllocStringUninitialized(args[0].AsI4()));
                    return true;
                }
            }

            if (ns == "System" && tn == "Array" && mn == "get_Length")
            {
                result = Cell.I4(ReadArrayLength(args[0]));
                return true;
            }

            if (ns == "System" && tn == "Array" && mn == "ClearInternal")
            {
                ClearArray(args[0], args[1].AsI4(), args[2].AsI4());
                return true;
            }

            if (ns == "System" && tn == "Array" && mn == "CopyInternal")
            {
                result = Cell.I4(CopyArray(args[0], args[1].AsI4(), args[2], args[3].AsI4(), args[4].AsI4()) ? 1 : 0);
                return true;
            }

            if (ns == "System" && tn == "Number" && mn == "_DoubleToStringImpl")
            {
                result = Cell.Ref(AllocStringFromManaged(args[0].AsR8().ToString("R", CultureInfo.InvariantCulture)));
                return true;
            }

            if (IsCoreLibRandomLike(t) && mn == "Next")
            {
                int r;
                if (method.HasThis && method.ParameterTypes.Length == 0 && args.Length == 1)
                    r = Random.Shared.Next();
                else if (method.HasThis && method.ParameterTypes.Length == 1 && args.Length == 2)
                    r = Random.Shared.Next(args[1].AsI4());
                else if (method.HasThis && method.ParameterTypes.Length == 2 && args.Length == 3)
                    r = Random.Shared.Next(args[1].AsI4(), args[2].AsI4());
                else
                    return false;

                result = Cell.I4(r);
                return true;
            }

            if (ns == "System.Runtime.InteropServices" && tn == "MemoryMarshal")
            {
                if (mn == "GetArrayDataReference")
                {
                    int arr = RequireObjectRef(args[0]);
                    var elemType = method.MethodGenericArguments.Length == 1
                        ? method.MethodGenericArguments[0]
                        : method.ReturnType;
                    result = Cell.ByRef(arr + ArrayDataOffset, GetStorageSizeAlign(elemType).size);
                    return true;
                }
                if (mn == "GetReference")
                {
                    result = ReadSpanReference(args[0]);
                    return true;
                }
                if (mn == "Read")
                {
                    var type = method.MethodGenericArguments.Length == 1 ? method.MethodGenericArguments[0] : method.ReturnType;
                    result = LoadValue(RequireAddress(ReadSpanReference(args[0])), type);
                    return true;
                }
                if (mn == "TryRead")
                {
                    var type = method.MethodGenericArguments.Length == 1 ? method.MethodGenericArguments[0] : method.ParameterTypes[1].ElementType!;
                    int len = ReadSpanLength(args[0]);
                    if (GetStorageSizeAlign(type).size > len)
                    {
                        StoreValue(RequireAddress(args[1]), type, DefaultValue(type));
                        result = Cell.I4(0);
                    }
                    else
                    {
                        StoreValue(RequireAddress(args[1]), type, LoadValue(RequireAddress(ReadSpanReference(args[0])), type));
                        result = Cell.I4(1);
                    }
                    return true;
                }
                if (mn == "Write" || mn == "TryWrite")
                {
                    var type = method.MethodGenericArguments.Length == 1 ? method.MethodGenericArguments[0] : method.ParameterTypes[^1];
                    int len = ReadSpanLength(args[0]);
                    bool ok = GetStorageSizeAlign(type).size <= len;
                    if (ok)
                    {
                        var source = args[1].Kind is CellKind.ByRef or CellKind.Ptr ? LoadValue(RequireAddress(args[1]), type) : args[1];
                        StoreValue(RequireAddress(ReadSpanReference(args[0])), type, source);
                    }
                    if (mn == "TryWrite") result = Cell.I4(ok ? 1 : 0);
                    if (!ok && mn == "Write") throw new ArgumentOutOfRangeException();
                    return true;
                }
            }

            if (ns == "System.Runtime.CompilerServices" && tn == "RuntimeHelpers")
            {
                if (mn == "IsReferenceOrContainsReferences")
                {
                    var type = method.MethodGenericArguments.Length == 1 ? method.MethodGenericArguments[0] : method.ReturnType;
                    result = Cell.I4(TypeIsReferenceOrContainsReferences(type) ? 1 : 0);
                    return true;
                }
                if (mn == "IsKnownConstant")
                {
                    result = Cell.I4(0);
                    return true;
                }
                if (mn == "GetHashCode")
                {
                    result = Cell.I4(args[0].Kind == CellKind.Ref ? checked((int)args[0].Payload) : 0);
                    return true;
                }
            }

            if (ns == "System.Runtime.CompilerServices" && tn == "Unsafe")
            {
                if (mn == "SizeOf")
                {
                    var type = method.MethodGenericArguments.Length >= 1 ? method.MethodGenericArguments[0] : method.ReturnType;
                    result = Cell.I4(GetStorageSizeAlign(type).size);
                    return true;
                }
                if (mn == "Add")
                {
                    var type = method.MethodGenericArguments.Length >= 1 ? method.MethodGenericArguments[0] : method.ReturnType.ElementType!;
                    int elemSize = GetStorageSizeAlign(type).size;
                    int abs = checked(RequireAddress(args[0]) + checked((int)args[1].AsI8()) * elemSize);
                    result = args[0].Kind == CellKind.Ptr ? Cell.Ptr(abs, elemSize) : Cell.ByRef(abs, elemSize);
                    return true;
                }
                if (mn == "AddByteOffset")
                {
                    int size = args[0].Aux == 0 ? 1 : args[0].Aux;
                    int abs = checked(RequireAddress(args[0]) + checked((int)args[1].AsI8()));
                    result = args[0].Kind == CellKind.Ptr ? Cell.Ptr(abs, size) : Cell.ByRef(abs, size);
                    return true;
                }
                if (mn == "As")
                {
                    if ((args[0].Kind is CellKind.ByRef or CellKind.Ptr) && method.MethodGenericArguments.Length >= 2)
                    {
                        var to = method.MethodGenericArguments[1];
                        int size = GetStorageSizeAlign(to).size;
                        result = args[0].Kind == CellKind.Ptr ? Cell.Ptr(RequireAddress(args[0]), size) : Cell.ByRef(RequireAddress(args[0]), size);
                    }
                    else
                    {
                        result = args[0];
                    }
                    return true;
                }
                if (mn == "AsRef")
                {
                    var type = method.MethodGenericArguments.Length >= 1 ? method.MethodGenericArguments[0] : method.ReturnType.ElementType!;
                    result = Cell.ByRef(RequireAddress(args[0]), GetStorageSizeAlign(type).size);
                    return true;
                }
                if (mn == "AreSame")
                {
                    result = Cell.I4(RequireAddress(args[0]) == RequireAddress(args[1]) ? 1 : 0);
                    return true;
                }
                if (mn == "ByteOffset")
                {
                    result = RuntimeTypeSystem.PointerSize == 8
                        ? Cell.I8(RequireAddress(args[1]) - RequireAddress(args[0]))
                        : Cell.I4(RequireAddress(args[1]) - RequireAddress(args[0]));
                    return true;
                }
                if (mn == "ReadUnaligned")
                {
                    var type = method.MethodGenericArguments.Length == 1 ? method.MethodGenericArguments[0] : method.ReturnType;
                    result = LoadValue(RequireAddress(args[0]), type);
                    return true;
                }
                if (mn == "WriteUnaligned")
                {
                    var type = method.MethodGenericArguments.Length == 1 ? method.MethodGenericArguments[0] : method.ParameterTypes[1];
                    StoreValue(RequireAddress(args[0]), type, args[1]);
                    return true;
                }
                if (mn == "BitCast")
                {
                    var from = method.MethodGenericArguments.Length >= 1 ? method.MethodGenericArguments[0] : method.ParameterTypes[0];
                    var to = method.MethodGenericArguments.Length >= 2 ? method.MethodGenericArguments[1] : method.ReturnType;
                    var (fromSize, _) = GetStorageSizeAlign(from);
                    var (toSize, _) = GetStorageSizeAlign(to);
                    if (fromSize != toSize)
                        throw new NotSupportedException();
                    int temp = AllocStackBytes(toSize, Math.Max(1, to.AlignOf));
                    StoreValue(temp, from, args[0]);
                    result = LoadValue(temp, to);
                    return true;
                }
            }

            return false;
        }


        private void WriteConsoleArg(Cell value, RuntimeType? parameterType)
        {
            if (parameterType != null && parameterType.Kind == RuntimeTypeKind.Pointer)
            {
                WriteUtf16ZeroTerminated(RequireAddress(value));
                return;
            }

            if (parameterType != null && parameterType.IsReferenceType && IsSystemType(parameterType, "String"))
            {
                var s = ReadManagedString(value);
                if (s != null) _textWriter.Write(s);
                return;
            }

            if (parameterType != null && parameterType.Name.StartsWith("ReadOnlySpan", StringComparison.Ordinal))
            {
                WriteSpanChars(value);
                return;
            }

            if (value.Kind == CellKind.Ref || value.Kind == CellKind.Null)
            {
                var s = ReadManagedString(value);
                if (s != null) _textWriter.Write(s);
                return;
            }

            if (value.Kind is CellKind.Ptr or CellKind.ByRef)
                WriteUtf16ZeroTerminated(RequireAddress(value));
        }

        private void WriteUtf16ZeroTerminated(int abs)
        {
            while (true)
            {
                CheckRange(abs, 2);
                char c = (char)ReadU16Unchecked(abs);
                if (c == '\0') return;
                _textWriter.Write(c);
                abs += 2;
            }
        }

        private void WriteSpanChars(Cell value)
        {
            Cell r = ReadSpanReference(value);
            int len = ReadSpanLength(value);
            int abs = RequireAddress(r);
            for (int i = 0; i < len; i++)
            {
                char c = (char)ReadU16Unchecked(abs + i * 2);
                _textWriter.Write(c);
            }
        }

        private Cell ReadSpanReference(Cell span)
        {
            RuntimeType t = GetValueType(span);
            RuntimeField? rf = FindField(t, "_reference");
            if (rf is null) throw new InvalidOperationException("Span reference field not found.");
            return LoadValue(checked((int)span.Payload) + rf.Offset, rf.FieldType);
        }

        private int ReadSpanLength(Cell span)
        {
            RuntimeType t = GetValueType(span);
            RuntimeField? lf = FindField(t, "_length");
            if (lf is null) throw new InvalidOperationException("Span length field not found.");
            return LoadValue(checked((int)span.Payload) + lf.Offset, lf.FieldType).AsI4();
        }

        private static RuntimeField? FindField(RuntimeType t, string name)
        {
            for (RuntimeType? cur = t; cur is not null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                    if (StringComparer.Ordinal.Equals(cur.InstanceFields[i].Name, name))
                        return cur.InstanceFields[i];
            }
            return null;
        }

        private void ClearArray(Cell array, int index, int length)
        {
            if (array.Kind == CellKind.Null) throw new NullReferenceException();
            int arr = RequireObjectRef(array);
            var type = GetObjectTypeFromRef(array);
            if (type.Kind != RuntimeTypeKind.Array || type.ElementType is null) throw new InvalidOperationException("Array expected.");
            int len = ReadI32(arr + ArrayLengthOffset);
            if (index < 0 || length < 0 || index > len - length) throw new IndexOutOfRangeException();
            var (size, _) = GetStorageSizeAlign(type.ElementType);
            Array.Clear(_mem, arr + ArrayDataOffset + index * size, length * size);
        }

        private bool CopyArray(Cell source, int sourceIndex, Cell dest, int destIndex, int length)
        {
            if (source.Kind == CellKind.Null || dest.Kind == CellKind.Null) throw new NullReferenceException();
            int src = RequireObjectRef(source);
            int dst = RequireObjectRef(dest);
            var srcType = GetObjectTypeFromRef(source);
            var dstType = GetObjectTypeFromRef(dest);
            if (srcType.Kind != RuntimeTypeKind.Array || dstType.Kind != RuntimeTypeKind.Array || srcType.ElementType is null || dstType.ElementType is null)
                return false;
            if (srcType.ArrayRank != 1 || dstType.ArrayRank != 1)
                return false;
            int srcLen = ReadI32(src + ArrayLengthOffset);
            int dstLen = ReadI32(dst + ArrayLengthOffset);
            if (sourceIndex < 0 || destIndex < 0 || length < 0 || sourceIndex > srcLen - length || destIndex > dstLen - length)
                throw new ArgumentException();
            if (length == 0)
                return true;

            var srcElem = srcType.ElementType;
            var dstElem = dstType.ElementType;
            if (srcElem.TypeId == dstElem.TypeId)
            {
                var (elemSize, _) = GetStorageSizeAlign(srcElem);
                Buffer.BlockCopy(_mem, src + ArrayDataOffset + sourceIndex * elemSize, _mem, dst + ArrayDataOffset + destIndex * elemSize, length * elemSize);
                return true;
            }

            if (srcElem.IsReferenceType && dstElem.IsReferenceType)
            {
                int ptrSize = RuntimeTypeSystem.PointerSize;
                int srcStart = checked(src + ArrayDataOffset + sourceIndex * ptrSize);
                int dstStart = checked(dst + ArrayDataOffset + destIndex * ptrSize);

                if (IsAssignableTo(srcElem, dstElem))
                {
                    Buffer.BlockCopy(_mem, srcStart, _mem, dstStart, length * ptrSize);
                    return true;
                }

                if (IsAssignableTo(dstElem, srcElem))
                {
                    for (int i = 0; i < length; i++)
                    {
                        long raw = ReadNativeInt(srcStart + i * ptrSize);
                        if (raw == 0)
                            continue;
                        var actual = GetObjectTypeFromRef(Cell.Ref(checked((int)raw)));
                        if (!IsAssignableTo(actual, dstElem))
                            return false;
                    }
                    Buffer.BlockCopy(_mem, srcStart, _mem, dstStart, length * ptrSize);
                    return true;
                }
            }

            if (srcElem.IsValueType && dstElem.IsReferenceType && IsAssignableTo(srcElem, dstElem))
            {
                var (srcSize, _) = GetStorageSizeAlign(srcElem);
                int ptrSize = RuntimeTypeSystem.PointerSize;
                int srcStart = checked(src + ArrayDataOffset + sourceIndex * srcSize);
                int dstStart = checked(dst + ArrayDataOffset + destIndex * ptrSize);
                for (int i = 0; i < length; i++)
                {
                    var value = LoadValue(srcStart + i * srcSize, srcElem);
                    int boxed = AllocBoxedValueObject(srcElem);
                    StoreValue(boxed + ObjectHeaderSize, srcElem, value);
                    WriteNativeInt(dstStart + i * ptrSize, boxed);
                }
                return true;
            }

            return false;
        }

        private bool TryEnterExceptionHandler(Frame frame, int throwPc, Cell exception)
        {
            AbandonActiveFinallyContextIfThrowingInsideFinally(frame, throwPc);

            if (TryEnterCatch(frame, throwPc, exception))
                return true;

            if (TryFindFinallyHandlerForPc(frame, throwPc, out var finallyHandler))
            {
                BeginFinally(frame, finallyHandler, FinallyContext.ForThrow(frame, finallyHandler, exception));
                return true;
            }

            return false;
        }

        private bool TryEnterCatch(Frame frame, int throwPc, Cell exception)
        {
            var handlers = frame.Function.ExceptionHandlers;
            int bestSpan = int.MaxValue;
            ExceptionHandler best = default;
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (h.CatchTypeToken == FinallyCatchTypeToken)
                    continue;
                if (throwPc < h.TryStartPc || throwPc >= h.TryEndPc)
                    continue;
                if (h.CatchTypeToken != 0)
                {
                    var catchType = ResolveTypeToken(frame.Module, h.CatchTypeToken, frame.Method);
                    var exType = GetObjectTypeFromRef(exception);
                    if (!IsAssignableTo(exType, catchType))
                        continue;
                }
                int span = h.TryEndPc - h.TryStartPc;
                if (span < bestSpan)
                {
                    bestSpan = span;
                    best = h;
                }
            }

            if (bestSpan == int.MaxValue)
                return false;

            frame.Exception = exception;
            frame.Pc = best.HandlerStartPc;
            return true;
        }

        private static int HandlerSpan(ExceptionHandler h) => h.TryEndPc - h.TryStartPc;

        private bool TryFindFinallyHandlerForPc(Frame frame, int pc, out ExceptionHandler match)
        {
            match = default;
            int bestSpan = int.MaxValue;
            var handlers = frame.Function.ExceptionHandlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (h.CatchTypeToken != FinallyCatchTypeToken)
                    continue;
                if (pc < h.TryStartPc || pc >= h.TryEndPc)
                    continue;
                int span = HandlerSpan(h);
                if (span < bestSpan)
                {
                    bestSpan = span;
                    match = h;
                }
            }
            return bestSpan != int.MaxValue;
        }

        private bool TryFindFinallyHandlerForLeave(Frame frame, int fromPc, int toPc, out ExceptionHandler match)
        {
            match = default;
            int bestSpan = int.MaxValue;
            var handlers = frame.Function.ExceptionHandlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (h.CatchTypeToken != FinallyCatchTypeToken)
                    continue;
                if (fromPc < h.TryStartPc || fromPc >= h.TryEndPc)
                    continue;
                if (toPc >= h.TryStartPc && toPc < h.TryEndPc)
                    continue;
                int span = HandlerSpan(h);
                if (span < bestSpan)
                {
                    bestSpan = span;
                    match = h;
                }
            }
            return bestSpan != int.MaxValue;
        }

        private void BeginFinally(Frame frame, ExceptionHandler handler, FinallyContext context)
        {
            _finallyStack.Add(context);
            frame.Pc = handler.HandlerStartPc;
        }

        private bool TryBeginFinallyForJump(Frame frame, int fromPc, int targetPc)
        {
            if (!TryFindFinallyHandlerForLeave(frame, fromPc, targetPc, out var handler))
                return false;
            BeginFinally(frame, handler, FinallyContext.ForJump(frame, handler, targetPc));
            return true;
        }

        private bool TryBeginFinallyForReturn(Frame frame, int fromPc, bool hasReturnValue, Cell returnValue)
        {
            if (!TryFindFinallyHandlerForLeave(frame, fromPc, -1, out var handler))
                return false;
            BeginFinally(frame, handler, FinallyContext.ForReturn(frame, handler, hasReturnValue, returnValue));
            return true;
        }

        private bool ExecEndfinally(Frame frame, out Cell returned)
        {
            returned = default;
            if (_finallyStack.Count == 0)
                throw new InvalidOperationException("Endfinally without active finally context.");

            int last = _finallyStack.Count - 1;
            var ctx = _finallyStack[last];
            _finallyStack.RemoveAt(last);

            if (!ReferenceEquals(ctx.Frame, frame))
                throw new InvalidOperationException("Endfinally frame mismatch.");

            if (ctx.Kind == FinallyContinuationKind.Throw)
            {
                if (TryFindFinallyHandlerForPc(frame, ctx.NextFromPc, out var next))
                {
                    BeginFinally(frame, next, FinallyContext.ForThrow(frame, next, ctx.PendingException));
                    return false;
                }
                throw new VmThrownException(ctx.PendingException);
            }

            if (ctx.Kind == FinallyContinuationKind.Jump)
            {
                if (!TryBeginFinallyForJump(frame, ctx.NextFromPc, ctx.TargetPc))
                    frame.Pc = ctx.TargetPc;
                return false;
            }

            if (ctx.Kind == FinallyContinuationKind.Return)
            {
                if (TryBeginFinallyForReturn(frame, ctx.NextFromPc, ctx.HasReturnValue, ctx.ReturnValue))
                    return false;
                returned = ctx.HasReturnValue ? ctx.ReturnValue : default;
                return true;
            }

            throw new InvalidOperationException("Unknown finally continuation kind.");
        }

        private void AbandonActiveFinallyContextIfThrowingInsideFinally(Frame frame, int pc)
        {
            while (_finallyStack.Count != 0)
            {
                int last = _finallyStack.Count - 1;
                var ctx = _finallyStack[last];
                if (!ReferenceEquals(ctx.Frame, frame))
                    break;
                if (pc >= ctx.FinallyStartPc && pc < ctx.FinallyEndPc)
                {
                    _finallyStack.RemoveAt(last);
                    continue;
                }
                break;
            }
        }

        private void ClearFinallyContextsForFrame(Frame frame)
        {
            for (int i = _finallyStack.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_finallyStack[i].Frame, frame))
                    _finallyStack.RemoveAt(i);
            }
        }

        private RuntimeMethod ResolveVirtualDispatch(RuntimeType receiverType, RuntimeMethod declared)
        {
            if (declared.DeclaringType.Kind == RuntimeTypeKind.Interface)
            {
                for (var t = receiverType; t != null; t = t.BaseType)
                {
                    var map = t.ExplicitInterfaceMethodImpls;
                    if (map is not null && map.TryGetValue(declared.MethodId, out var impl))
                        return impl;
                }
                return FindMostDerivedMethodByNameAndSig(receiverType, declared) ?? declared;
            }

            int slot = declared.VTableSlot;
            if (slot >= 0 && (uint)slot < (uint)receiverType.VTable.Length)
                return receiverType.VTable[slot];
            return FindMostDerivedMethodByNameAndSig(receiverType, declared) ?? declared;
        }

        private static RuntimeMethod? FindMostDerivedMethodByNameAndSig(RuntimeType receiverType, RuntimeMethod declared)
        {
            for (var t = receiverType; t != null; t = t.BaseType)
            {
                var methods = t.Methods;
                for (int i = 0; i < methods.Length; i++)
                {
                    var c = methods[i];
                    if (c.IsStatic || c.IsPrivate) continue;
                    if (!StringComparer.Ordinal.Equals(c.Name, declared.Name)) continue;
                    if (!SameSig(c, declared)) continue;
                    return c;
                }
            }
            return null;
        }

        private static bool SameSig(RuntimeMethod a, RuntimeMethod b)
        {
            if (a.ParameterTypes.Length != b.ParameterTypes.Length) return false;
            if (a.GenericArity != b.GenericArity) return false;
            for (int i = 0; i < a.ParameterTypes.Length; i++)
                if (!ReferenceEquals(a.ParameterTypes[i], b.ParameterTypes[i])) return false;
            return true;
        }

        private bool IsAssignableTo(RuntimeType actual, RuntimeType target)
        {
            if (ReferenceEquals(actual, target))
                return true;

            if (target.Kind == RuntimeTypeKind.Interface && ImplementsInterface(actual, target, new HashSet<int>()))
                return true;

            if (actual.Kind == RuntimeTypeKind.Array && target.Kind == RuntimeTypeKind.Array &&
                actual.ArrayRank == target.ArrayRank &&
                actual.ElementType is RuntimeType actualElem &&
                target.ElementType is RuntimeType targetElem &&
                actualElem.IsReferenceType && targetElem.IsReferenceType &&
                IsAssignableTo(actualElem, targetElem))
                return true;

            if (actual.IsValueType && target.IsReferenceType)
            {
                for (var t = actual.BaseType; t != null; t = t.BaseType)
                    if (ReferenceEquals(t, target)) return true;
            }

            for (var t = actual.BaseType; t != null; t = t.BaseType)
                if (ReferenceEquals(t, target)) return true;

            return false;
        }

        private static bool ImplementsInterface(RuntimeType current, RuntimeType target, HashSet<int> seen)
        {
            if (!seen.Add(current.TypeId)) return false;
            for (int i = 0; i < current.Interfaces.Length; i++)
            {
                var iface = current.Interfaces[i];
                if (ReferenceEquals(iface, target)) return true;
                if (ImplementsInterface(iface, target, seen)) return true;
            }
            return current.BaseType is not null && ImplementsInterface(current.BaseType, target, seen);
        }

        private static RuntimeType? TryGetEnumUnderlyingType(RuntimeType type)
        {
            if (type.Kind != RuntimeTypeKind.Enum)
                return null;
            if (type.ElementType is not null)
                return type.ElementType;
            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                var f = type.InstanceFields[i];
                if (StringComparer.Ordinal.Equals(f.Name, "value__"))
                    return f.FieldType;
            }
            return type.InstanceFields.Length != 0 ? type.InstanceFields[0].FieldType : null;
        }

        private static bool IsNullableTypeDefinitionName(string name)
            => name.StartsWith("Nullable", StringComparison.Ordinal);

        private static bool TryGetNullableInfo(RuntimeType type, out RuntimeType underlying, out RuntimeField hasValueField, out RuntimeField valueField)
        {
            underlying = null!;
            hasValueField = null!;
            valueField = null!;

            if (!type.IsValueType)
                return false;

            RuntimeType def = type.GenericTypeDefinition ?? type;
            if (def.Namespace != "System" || !IsNullableTypeDefinitionName(def.Name))
                return false;
            if (type.GenericTypeArguments.Length != 1)
                return false;

            underlying = type.GenericTypeArguments[0];
            RuntimeField? hv = null;
            RuntimeField? vv = null;
            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                var f = type.InstanceFields[i];
                if (StringComparer.Ordinal.Equals(f.Name, "hasValue")) hv = f;
                else if (StringComparer.Ordinal.Equals(f.Name, "value")) vv = f;
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

        private static bool IsSystemBooleanType(RuntimeType type)
            => type.Namespace == "System" && type.Name == "Boolean";

        private static bool IsCoreLibRandomLike(RuntimeType type)
        {
            if (!StringComparer.Ordinal.Equals(type.AssemblyName, "std"))
                return false;
            for (var cur = type; cur is not null; cur = cur.BaseType)
            {
                if (StringComparer.Ordinal.Equals(cur.AssemblyName, "std") &&
                    StringComparer.Ordinal.Equals(cur.Namespace, "System") &&
                    StringComparer.Ordinal.Equals(cur.Name, "Random"))
                    return true;
            }
            return false;
        }

        private bool TypeIsReferenceOrContainsReferences(RuntimeType type)
            => TypeIsReferenceOrContainsReferences(type, new HashSet<int>());

        private bool TypeIsReferenceOrContainsReferences(RuntimeType type, HashSet<int> visiting)
        {
            if (type.IsReferenceType) return true;
            if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef) return false;
            if (IsScalarLike(type) || IsSystemType(type, "Single") || IsSystemType(type, "Double") || IsSystemType(type, "Decimal")) return false;
            if (!visiting.Add(type.TypeId)) return false;
            for (int i = 0; i < type.InstanceFields.Length; i++)
            {
                if (TypeIsReferenceOrContainsReferences(type.InstanceFields[i].FieldType, visiting))
                {
                    visiting.Remove(type.TypeId);
                    return true;
                }
            }
            visiting.Remove(type.TypeId);
            return false;
        }

        private RuntimeType GetObjectTypeFromRef(Cell value)
        {
            if (value.Kind == CellKind.Null)
                throw new NullReferenceException();
            if (value.Kind != CellKind.Ref)
                throw new InvalidOperationException($"Object reference expected, got {value.Kind}.");
            int obj = checked((int)value.Payload);
            CheckHeapAccess(obj, ObjectHeaderSize, writable: false);
            int flags = ReadI32(obj + 4);
            if ((flags & GcFlagAllocated) == 0)
                throw new InvalidOperationException("Dangling object reference.");
            int typeId = ReadI32(obj);
            return _rts.GetTypeById(typeId);
        }

        private RuntimeType GetValueType(Cell value)
        {
            if (value.Kind != CellKind.Value)
                throw new InvalidOperationException($"Value cell expected, got {value.Kind}.");
            return _rts.GetTypeById(value.Aux);
        }

        private int AllocObject(RuntimeType type)
        {
            if (!type.IsReferenceType || type.Kind == RuntimeTypeKind.Array || IsSystemType(type, "String"))
                throw new InvalidOperationException($"Cannot allocate object for '{type.Namespace}.{type.Name}'.");
            int size = type.InstanceSize;
            int abs = AllocHeapBytes(size, Math.Max(8, type.AlignOf));
            Array.Clear(_mem, abs, size);
            WriteI32(abs, type.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            return abs;
        }

        private int AllocBoxedValueObject(RuntimeType valueType)
        {
            int size = AlignUp(ObjectHeaderSize + valueType.SizeOf, 8);
            int abs = AllocHeapBytes(size, 8);
            Array.Clear(_mem, abs, size);
            WriteI32(abs, valueType.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            return abs;
        }

        private int AllocArrayObject(RuntimeType arrayType, int length)
        {
            if (arrayType.Kind != RuntimeTypeKind.Array || arrayType.ElementType is null)
                throw new InvalidOperationException("Array type expected.");
            var (elemSize, _) = GetStorageSizeAlign(arrayType.ElementType);
            int size = AlignUp(ArrayDataOffset + checked(length * elemSize), 8);
            int abs = AllocHeapBytes(size, 8);
            Array.Clear(_mem, abs, size);
            WriteI32(abs, arrayType.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            WriteI32(abs + ArrayLengthOffset, length);
            return abs;
        }

        private int AllocStringUninitialized(int length)
        {
            if (length < 0) throw new InvalidOperationException("Negative string length.");
            int size = AlignUp(StringCharsOffset + length * 2 + 2, 8);
            int abs = AllocHeapBytes(size, 8);
            Array.Clear(_mem, abs, size);
            WriteI32(abs, _rts.SystemString.TypeId);
            WriteI32(abs + 4, GcFlagAllocated);
            WriteI32(abs + StringLengthOffset, length);
            WriteU16Unchecked(abs + StringCharsOffset + length * 2, 0);
            return abs;
        }

        private int AllocStringFromManaged(string value)
        {
            int abs = AllocStringUninitialized(value.Length);
            int dst = abs + StringCharsOffset;
            for (int i = 0; i < value.Length; i++)
                WriteU16Unchecked(dst + i * 2, value[i]);
            return abs;
        }

        private string? ReadManagedString(Cell value)
        {
            if (value.Kind == CellKind.Null) return null;
            int obj = RequireObjectRef(value);
            var type = GetObjectTypeFromRef(value);
            if (!IsSystemType(type, "String"))
                return type.Namespace + "." + type.Name;
            int len = ReadI32(obj + StringLengthOffset);
            var chars = new char[len];
            int src = obj + StringCharsOffset;
            for (int i = 0; i < len; i++)
                chars[i] = (char)ReadU16Unchecked(src + i * 2);
            return new string(chars);
        }

        private int ReadStringLength(Cell value)
        {
            int obj = RequireObjectRef(value);
            return ReadI32(obj + StringLengthOffset);
        }

        private int ReadArrayLength(Cell value)
        {
            int obj = RequireObjectRef(value);
            return ReadI32(obj + ArrayLengthOffset);
        }

        private RuntimeModule FindCoreLibModuleOrThrow()
        {
            if (_modules.TryGetValue("std", out var std))
                return std;

            foreach (var kv in _modules)
            {
                var m = kv.Value;
                if (m.TypeDefByFullName.ContainsKey(("System", "Object")))
                    return m;
            }

            throw new InvalidOperationException("Core library module not found.");
        }

        private Cell NormalizeThrownException(Cell exception)
        {
            if (exception.Kind == CellKind.Null)
            {
                var core = FindCoreLibModuleOrThrow();
                if (!core.TypeDefByFullName.TryGetValue(("System", "NullReferenceException"), out int tdTok))
                    throw new NullReferenceException();
                var type = _rts.ResolveType(core, tdTok);
                return Cell.Ref(AllocObject(type));
            }

            if (exception.Kind != CellKind.Ref)
                throw new InvalidOperationException($"Throw expects object reference, got {exception.Kind}.");

            return exception;
        }

        private bool TryCreateCoreException(string ns, string name, string? message, out Cell exception)
        {
            exception = default;
            RuntimeModule core;
            try
            {
                core = FindCoreLibModuleOrThrow();
            }
            catch
            {
                return false;
            }

            if (!core.TypeDefByFullName.TryGetValue((ns, name), out int tdTok))
                return false;

            RuntimeType type;
            int obj;
            try
            {
                type = _rts.ResolveType(core, tdTok);
                obj = AllocObject(type);
            }
            catch
            {
                return false;
            }

            TryInitExceptionMessage(obj, type, message);
            exception = Cell.Ref(obj);
            return true;
        }

        private void TryInitExceptionMessage(int obj, RuntimeType type, string? message)
        {
            int msg;
            try
            {
                msg = AllocStringFromManaged(message ?? string.Empty);
            }
            catch
            {
                try
                {
                    msg = AllocStringUninitialized(0);
                }
                catch
                {
                    return;
                }
            }

            for (var cur = type; cur is not null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                {
                    var f = cur.InstanceFields[i];
                    if (!StringComparer.Ordinal.Equals(f.Name, "_message"))
                        continue;
                    StoreValue(checked(obj + f.Offset), f.FieldType, Cell.Ref(msg));
                    return;
                }
            }
        }


        private static bool IsVoidReturn(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Void";

        private Cell NormalizeReturnValue(RuntimeType t, VmValue v)
        {
            if (IsVoidReturn(t))
                return default;
            if (t.IsReferenceType)
            {
                if (v.Kind == VmValueKind.Null) return Cell.Null;
                if (v.Kind == VmValueKind.Ref) return Cell.Ref(checked((int)v.Payload));
                throw new InvalidOperationException($"Return type mismatch: expected managed ref, got {v.Kind}");
            }
            if (t.Kind == RuntimeTypeKind.Pointer)
            {
                if (v.Kind == VmValueKind.Null) return Cell.Ptr(0, GetPointedElementSize(t));
                if (v.Kind == VmValueKind.Ptr) return Cell.Ptr(checked((int)v.Payload), v.Aux);
                throw new InvalidOperationException($"Return type mismatch: expected ptr, got {v.Kind}");
            }
            if (t.Kind == RuntimeTypeKind.ByRef)
            {
                if (v.Kind == VmValueKind.Null) return Cell.ByRef(0, GetPointedElementSize(t));
                if (v.Kind == VmValueKind.ByRef) return Cell.ByRef(checked((int)v.Payload), v.Aux);
                throw new InvalidOperationException($"Return type mismatch: expected byref, got {v.Kind}");
            }
            if (t.Kind == RuntimeTypeKind.Enum)
            {
                var ut = TryGetEnumUnderlyingType(t) ?? throw new NotSupportedException($"Enum '{t.Namespace}.{t.Name}' has no underlying type.");
                return NormalizeReturnValue(ut, v);
            }
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
                        return Cell.I4(v.AsInt32());
                    case "Int64":
                    case "UInt64":
                        return Cell.I8(v.AsInt64());
                    case "Single":
                    case "Double":
                        return Cell.R8(v.AsDouble());
                    case "IntPtr":
                    case "UIntPtr":
                        return RuntimeTypeSystem.PointerSize == 8 ? Cell.I8(v.AsInt64()) : Cell.I4(v.AsInt32());
                }
            }
            if (v.Kind == VmValueKind.Value)
                return new Cell(CellKind.Value, v.Payload, v.Aux);
            throw new NotSupportedException($"Host return marshal not supported for value type: {t.Namespace}.{t.Name}");
        }

        internal string? HostReadString(VmValue v, CancellationToken ct)
        {
            if (v.Kind == VmValueKind.Null) return null;
            var c = v.ToCell();
            int obj = RequireObjectRef(c);
            var type = GetObjectTypeFromRef(c);
            if (!IsSystemType(type, "String"))
                throw new InvalidOperationException($"Expected string ref, got '{type.Namespace}.{type.Name}'.");
            int len = ReadI32(obj + StringLengthOffset);
            var chars = new char[len];
            int src = obj + StringCharsOffset;
            for (int i = 0; i < len; i++)
            {
                if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested();
                chars[i] = (char)ReadU16Unchecked(src + i * 2);
            }
            return new string(chars);
        }

        private void ValidateArrayRefAny(Cell arr, out int arrAbs, out int length, out RuntimeType arrayType)
        {
            if (arr.Kind == CellKind.Null)
                throw new NullReferenceException();
            if (arr.Kind != CellKind.Ref)
                throw new InvalidOperationException($"Expected array ref, got {arr.Kind}.");
            arrayType = GetObjectTypeFromRef(arr);
            if (arrayType.Kind != RuntimeTypeKind.Array)
                throw new InvalidOperationException($"Expected array instance, got '{arrayType.Namespace}.{arrayType.Name}'.");
            arrAbs = checked((int)arr.Payload);
            length = ReadI32(arrAbs + ArrayLengthOffset);
        }

        internal VmValue HostAllocArray(RuntimeType arrayType, int length)
        {
            if (arrayType.Kind != RuntimeTypeKind.Array)
                throw new ArgumentException("Type is not an array.", nameof(arrayType));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            return new VmValue(VmValueKind.Ref, AllocArrayObject(arrayType, length));
        }

        internal int HostGetArrayLength(VmValue array)
        {
            ValidateArrayRefAny(array.ToCell(), out _, out int length, out _);
            return length;
        }

        internal VmValue HostGetArrayElement(VmValue array, int index)
        {
            ValidateArrayRefAny(array.ToCell(), out int arrAbs, out int length, out var arrayType);
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();
            var elemType = arrayType.ElementType ?? throw new InvalidOperationException("Array type has no element type.");
            var (elemSize, _) = GetStorageSizeAlign(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));
            return new VmValue(LoadValue(elemAbs, elemType));
        }

        internal void HostSetArrayElement(VmValue array, int index, VmValue value)
        {
            ValidateArrayRefAny(array.ToCell(), out int arrAbs, out int length, out var arrayType);
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException();
            var elemType = arrayType.ElementType ?? throw new InvalidOperationException("Array type has no element type.");
            var (elemSize, _) = GetStorageSizeAlign(elemType);
            int elemAbs = checked(arrAbs + ArrayDataOffset + checked(index * elemSize));
            StoreValue(elemAbs, elemType, value.ToCell());
        }

        internal VmValue HostAllocString(string? s)
        {
            if (s is null) return VmValue.Null;
            return new VmValue(VmValueKind.Ref, AllocStringFromManaged(s));
        }

        internal VmValue HostAllocStringArray(RuntimeType arrayType, ReadOnlySpan<string?> values)
        {
            var arr = HostAllocArray(arrayType, values.Length);
            for (int i = 0; i < values.Length; i++)
                HostSetArrayElement(arr, i, HostAllocString(values[i]));
            return arr;
        }

        internal int HostGetAddress(VmValue v)
        {
            var c = v.ToCell();
            if (c.Kind is CellKind.Ptr or CellKind.ByRef or CellKind.Ref)
                return checked((int)c.Payload);
            throw new InvalidOperationException($"Address-compatible value expected, got {c.Kind}.");
        }

        internal Span<byte> HostGetSpan(int abs, int size, bool writable)
        {
            if (writable) CheckWritableRange(abs, size); else CheckRange(abs, size);
            return _mem.AsSpan(abs, size);
        }

        internal void RegisterHostOverride(HostOverride ov)
        {
            if (ov is null) throw new ArgumentNullException(nameof(ov));
            _hostOverrides[ov.MethodId] = ov;
        }

        private bool TryInvokeHostOverride(RuntimeMethod rm, Cell[] callArgs, CancellationToken ct, out Cell result)
        {
            result = default;
            if (!_hostOverrides.TryGetValue(rm.MethodId, out var ov))
                return false;
            if (!rm.IsStatic || rm.HasThis)
                return false;
            var args = new VmValue[callArgs.Length];
            for (int i = 0; i < callArgs.Length; i++)
                args[i] = new VmValue(callArgs[i]);
            _hostCtx.SetToken(ct);
            try
            {
                var ret = ov.Handler(_hostCtx, args);
                if (!IsVoidReturn(rm.ReturnType))
                    result = NormalizeReturnValue(rm.ReturnType, ret);
                return true;
            }
            catch (Exception ex)
            {
                if (TryTranslateHostExceptionToVm(ex, out var vmEx))
                    throw new VmThrownException(vmEx);
                throw;
            }
        }

        private bool TryTranslateHostExceptionToVm(Exception hostEx, out Cell exception)
        {
            exception = default;
            string msg = hostEx.Message;

            if (hostEx is DivideByZeroException)
                return TryCreateCoreException("System", "DivideByZeroException", msg, out exception)
                    || TryCreateCoreException("System", "ArithmeticException", msg, out exception)
                    || TryCreateCoreException("System", "Exception", msg, out exception);

            if (hostEx is OverflowException)
                return TryCreateCoreException("System", "OverflowException", msg, out exception)
                    || TryCreateCoreException("System", "ArithmeticException", msg, out exception)
                    || TryCreateCoreException("System", "Exception", msg, out exception);

            return false;
        }

        private string FormatThrownException(Cell exception)
        {
            if (exception.Kind == CellKind.Null)
                return "VM threw null.";
            try
            {
                var t = GetObjectTypeFromRef(exception);
                return $"Unhandled VM exception: {t.Namespace}.{t.Name}";
            }
            catch
            {
                return "Unhandled VM exception.";
            }
        }

        private static int CoercePointerAddress(Cell value)
        {
            return value.Kind switch
            {
                CellKind.Null => 0,
                CellKind.Ptr or CellKind.ByRef or CellKind.Ref => checked((int)value.Payload),
                CellKind.I4 => value.AsI4(),
                CellKind.I8 => checked((int)value.AsI8()),
                _ => throw new InvalidOperationException($"Pointer address expected, got {value.Kind}.")
            };
        }

        private static int RequireByRefAddress(Cell value)
        {
            if (value.Kind == CellKind.ByRef)
                return checked((int)value.Payload);
            throw new InvalidOperationException($"ByRef cell expected, got {value.Kind}.");
        }

        private static int RequireAddress(Cell value)
        {
            if (value.Kind is CellKind.Ptr or CellKind.ByRef)
                return checked((int)value.Payload);
            throw new InvalidOperationException($"Address cell expected, got {value.Kind}.");
        }

        private static int RequireObjectRef(Cell value)
        {
            if (value.Kind == CellKind.Null)
                throw new NullReferenceException();
            if (value.Kind == CellKind.Ref)
                return checked((int)value.Payload);
            throw new InvalidOperationException($"Object reference expected, got {value.Kind}.");
        }

        private int GetPointedElementSize(RuntimeType type)
        {
            if (type.ElementType is null) return 1;
            return GetStorageSizeAlign(type.ElementType).size;
        }

        private int AllocStackBytes(int size, int align)
        {
            int abs = AlignUp(_sp, align);
            int end = checked(abs + Math.Max(0, size));
            EnsureStack(end);
            _sp = end;
            if (_sp > _stackPeakAbs) _stackPeakAbs = _sp;
            return abs;
        }

        private int AllocHeapBytes(int size, int align)
        {
            int abs = AlignUp(_heapPtr, align);
            int end = checked(abs + Math.Max(0, size));
            if (end > _heapEnd)
                throw new OutOfMemoryException();
            _heapPtr = end;
            if (_heapPtr > _heapPeakAbs) _heapPeakAbs = _heapPtr;
            return abs;
        }

        private void EnsureStack(int end)
        {
            if (end > _stackEnd || end > _heapBase)
                throw new StackOverflowException();
        }

        private void CheckRange(int abs, int size)
        {
            if (size < 0 || abs < 0 || abs > _mem.Length - size)
                throw new AccessViolationException();
        }

        private void CheckWritableRange(int abs, int size)
        {
            CheckRange(abs, size);
            if (abs < _staticEnd)
                throw new AccessViolationException();
        }

        private void CheckHeapAccess(int abs, int size, bool writable)
        {
            if (writable) CheckWritableRange(abs, size); else CheckRange(abs, size);
            if (abs < _heapBase || abs > _heapPtr - size)
                throw new AccessViolationException();
        }

        private ref byte Mem0 => ref MemoryMarshal.GetArrayDataReference(_mem);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private short ReadI16Unchecked(int abs) =>
            Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadU16Unchecked(int abs) =>
            Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadI32Unchecked(int abs) =>
            Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadI64Unchecked(int abs) =>
            Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ReadR4Unchecked(int abs) =>
            Unsafe.ReadUnaligned<float>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ReadR8Unchecked(int abs) =>
            Unsafe.ReadUnaligned<double>(ref Unsafe.Add(ref Mem0, abs));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteU16Unchecked(int abs, ushort value) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteI32Unchecked(int abs, int value) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteI64Unchecked(int abs, long value) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteR4Unchecked(int abs, float value) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteR8Unchecked(int abs, double value) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref Mem0, abs), value);

        private int ReadI32(int abs)
        {
            CheckRange(abs, 4);
            return ReadI32Unchecked(abs);
        }

        private void WriteI32(int abs, int value)
        {
            CheckWritableRange(abs, 4);
            WriteI32Unchecked(abs, value);
        }

        private long ReadNativeInt(int abs)
        {
            return RuntimeTypeSystem.PointerSize == 8
                ? ReadI64Unchecked(abs)
                : ReadI32Unchecked(abs);
        }

        private void WriteNativeInt(int abs, long value)
        {
            int pointerSize = RuntimeTypeSystem.PointerSize;
            if (pointerSize == 8)
                WriteI64Unchecked(abs, value);
            else
                WriteI32Unchecked(abs, checked((int)value));
        }
    }
}
