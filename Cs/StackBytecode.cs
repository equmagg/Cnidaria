using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{

    [Flags]
    internal enum NumericConvFlags : byte
    {
        None = 0,
        Checked = 1 << 0,
        SourceUnsigned = 1 << 1,
    }
    internal enum IndirLoadKind : byte
    {
        I1,
        U1,
        I2,
        U2,
        I4,
        U4,
        I8,
        Ref,
    }
    public enum BytecodeOp : byte
    {
        Nop,
        // Stack / constants
        Pop,
        Dup,
        Ldnull,
        Ldc_I4,
        Ldc_I8,
        Ldc_R4,
        Ldc_R8,
        Ldstr,          // operand0: UserString token
        DefaultValue,   // operand0: Type token

        // Locals / args
        Ldloc,
        Stloc,
        Ldloca,

        Ldarg,
        Starg,
        Ldarga,

        Ldthis,
        // Arithmetic / bitwise / compare
        Add,
        Sub,
        Mul,
        Div,
        Div_Un,
        Rem,
        Rem_Un,

        And,
        Or,
        Xor,
        Shl,
        Shr,
        Shr_Un,

        Neg,
        Not,

        Ceq,
        Clt,
        Clt_Un,
        Cgt,
        Cgt_Un,

        // Calls / object
        Call,   // operand0: method token, operand1: argCountWithThisPacked
        CallVirt, // operand0: method token, operand1: argCountWithThisPacked
        Newobj, // operand0: ctor token, operand1: argCount

        Ldfld,   // operand0: field token, stack: obj -> value
        Stfld,   // operand0: field token, stack: obj, value ->
        Ldsfld,  // operand0: field token, stack: -> value
        Stsfld,  // operand0: field token, stack: value ->

        Ldflda,  // operand0: field token, stack: obj -> byref
        Ldsflda, // operand0: field token, stack: -> byref

        // Conversions / runtime type ops
        Conv,       // operand0: NumericConvKind, operand1: NumericConvFlags
        CastClass,  // operand0: type token
        Box,        // operand0: type token
        UnboxAny,   // operand0: type token

        // Control flow
        Br,          // operand0: target PC
        Brtrue,      // operand0: target PC
        Brfalse,     // operand0: target PC
        Ret,

        // Exceptions
        Throw,       // stack: ex
        Rethrow,     // rethrow current catch exception
        Ldexception, // stack: ex
        Endfinally, // end of finally

        // Pointers
        StackAlloc,  // operand0: elementSize
        PtrElemAddr, // operand0: elementSize
        PtrToByRef,  // stack: ptr -> byref
        // Typed indirect
        Ldobj,   // operand0: Type token, stack: addr(byref/ptr) -> value
        Stobj,   // operand0: Type token, stack: addr(byref/ptr), value ->

        Newarr,  // operand0: element Type token, stack: length -> arrayref
        Ldelem,  // operand0: element Type token, stack: arrayref, index -> value
        Ldelema, // operand0: element Type token, stack: arrayref, index -> byref
        Stelem,  // operand0: element Type token, stack: arrayref, index, value ->
        LdArrayDataRef, // stack: arrayref -> byref

        Sizeof,  // operand0: Type token, stack: -> int32
        PtrDiff, // operand0: elementSize, stack: ptrA, ptrB -> nint
        Isinst, // operand0: type token, stack: obj -> obj

    }
    internal enum NumericConvKind : byte
    {
        I1,
        U1,
        I2,
        U2,
        I4,
        U4,
        I8,
        U8,
        R4,
        R8,
        Char,
        Bool,
        NativeInt,
        NativeUInt
    }
    public readonly struct Instruction
    {
        public readonly BytecodeOp Op;
        public readonly int Operand0;
        public readonly int Operand1;
        public readonly long Operand2;
        public readonly short Pop;
        public readonly short Push;

        public Instruction(BytecodeOp op, int operand0, int operand1, long operand2, short pop, short push)
        {
            Op = op;
            Operand0 = operand0;
            Operand1 = operand1;
            Operand2 = operand2;
            Pop = pop;
            Push = push;
        }

        public override string ToString() => $"{Op} {Operand0} {Operand1} {Operand2}";
    }
    internal readonly struct BcLabel
    {
        public readonly int Id;
        public BcLabel(int id) => Id = id;
        public override string ToString() => $"L{Id}";
    }
    public readonly struct ExceptionHandler
    {
        public readonly int TryStartPc;
        public readonly int TryEndPc;
        public readonly int HandlerStartPc;
        public readonly int HandlerEndPc;
        public readonly int CatchTypeToken; // 0 catch all
        public ExceptionHandler(int tryStartPc, int tryEndPc, int handlerStartPc, int handlerEndPc, int catchTypeToken)
        {
            TryStartPc = tryStartPc;
            TryEndPc = tryEndPc;
            HandlerStartPc = handlerStartPc;
            HandlerEndPc = handlerEndPc;
            CatchTypeToken = catchTypeToken;
        }
    }
    internal sealed class BytecodeBuilder
    {
        public static int FindEntryPointMethodDef(RuntimeModule module)
        {
            if (TryFindEntryByName(module, "<Main>$", out var tok))
                return tok;

            if (TryFindStaticMain(module, out tok))
                return tok;

            throw new InvalidOperationException("Entry point not found in module metadata.");
        }

        private static bool TryFindEntryByName(RuntimeModule m, string name, out int methodDefToken)
        {
            var md = m.Md;
            for (int rid = 1; rid <= md.GetRowCount(MetadataTableKind.MethodDef); rid++)
            {
                var row = md.GetMethodDef(rid);
                if (!StringComparer.Ordinal.Equals(md.GetString(row.Name), name))
                    continue;

                methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                return true;
            }

            methodDefToken = 0;
            return false;
        }

        private static bool TryFindStaticMain(RuntimeModule m, out int methodDefToken)
        {
            var md = m.Md;

            for (int rid = 1; rid <= md.GetRowCount(MetadataTableKind.MethodDef); rid++)
            {
                var row = md.GetMethodDef(rid);
                if (!StringComparer.Ordinal.Equals(md.GetString(row.Name), "Main"))
                    continue;

                if (!IsStaticMainStringArraySignature(md.GetBlob(row.Signature)))
                    continue;

                methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                return true;
            }

            methodDefToken = 0;
            return false;
        }

        private static bool IsStaticMainStringArraySignature(ReadOnlySpan<byte> sig)
        {
            var r = new SigReader(sig);
            byte cc = r.ReadByte();

            // Must be static (no HASTHIS)
            if ((cc & 0x20) != 0)
                return false;

            // Reject generic mains
            if ((cc & 0x10) != 0)
            {
                r.ReadCompressedUInt(); // generic arity
                return false;
            }

            uint paramCount = r.ReadCompressedUInt();
            if (paramCount != 1)
                return false;

            // ret: void
            if ((SigElementType)r.ReadByte() != SigElementType.VOID)
                return false;

            // arg0: string[]
            if ((SigElementType)r.ReadByte() != SigElementType.SZARRAY)
                return false;
            if ((SigElementType)r.ReadByte() != SigElementType.STRING)
                return false;

            return true;
        }
        private readonly List<Instruction> _insns = new();
        private readonly List<int> _labelToPc = new();
        private readonly List<(int pc, BcLabel label)> _fixups = new();

        public int Count => _insns.Count;

        public BcLabel DefineLabel()
        {
            var id = _labelToPc.Count;
            _labelToPc.Add(-1);
            return new BcLabel(id);
        }

        public void MarkLabel(BcLabel label)
        {
            if ((uint)label.Id >= (uint)_labelToPc.Count)
                throw new ArgumentOutOfRangeException(nameof(label));

            if (_labelToPc[label.Id] >= 0)
                throw new InvalidOperationException($"Label '{label}' already marked.");

            _labelToPc[label.Id] = _insns.Count;
        }

        public void Emit(BytecodeOp op, int operand0 = 0, int operand1 = 0, long operand2 = 0, short pop = 0, short push = 0)
        {
            _insns.Add(new Instruction(op, operand0, operand1, operand2, pop, push));
        }
        public void EmitBranch(BytecodeOp op, BcLabel target, short pop)
        {
            if (op is not (BytecodeOp.Br or BytecodeOp.Brtrue or BytecodeOp.Brfalse))
                throw new ArgumentOutOfRangeException(nameof(op));

            int pc = _insns.Count;
            _insns.Add(new Instruction(op, target.Id, operand1: 0, operand2: 0, pop: pop, push: 0));
            _fixups.Add((pc, target));
        }
        public bool TryGetLastOp(out BytecodeOp op)
        {
            if (_insns.Count == 0)
            {
                op = default;
                return false;
            }

            op = _insns[_insns.Count - 1].Op;
            return true;
        }
        public ImmutableArray<Instruction> Bake(out ImmutableArray<int> labelToPc)
        {
            // Validate labels
            for (int i = 0; i < _labelToPc.Count; i++)
            {
                if (_labelToPc[i] < 0)
                    throw new InvalidOperationException($"Label L{i} was never marked.");
            }

            var baked = _insns.ToArray();
            foreach (var (pc, label) in _fixups)
            {
                var targetPc = _labelToPc[label.Id];
                var old = baked[pc];
                baked[pc] = new Instruction(old.Op, targetPc, old.Operand1, old.Operand2, old.Pop, old.Push);
            }

            labelToPc = _labelToPc.ToImmutableArray();
            return baked.ToImmutableArray();
        }
    }
    public sealed class BytecodeFunction
    {
        public int MethodToken { get; }
        public ImmutableArray<int> LocalTypeTokens { get; }
        public ImmutableArray<Instruction> Instructions { get; }
        public ImmutableArray<ExceptionHandler> ExceptionHandlers { get; }
        public int MaxStack { get; }

        public BytecodeFunction(int methodToken,
            ImmutableArray<int> localTypeTokens,
            ImmutableArray<Instruction> instructions,
            int maxStack,
            ImmutableArray<ExceptionHandler> exceptionHandlers)
        {
            MethodToken = methodToken;
            LocalTypeTokens = localTypeTokens;
            Instructions = instructions;
            MaxStack = maxStack;
            ExceptionHandlers = exceptionHandlers;
        }
    }
    internal sealed class BytecodeEmitResult
    {
        public BytecodeFunction Entry { get; }
        public ImmutableArray<BytecodeFunction> AdditionalMethods { get; }

        public BytecodeEmitResult(BytecodeFunction entry, ImmutableArray<BytecodeFunction> additionalMethods)
        {
            Entry = entry;
            AdditionalMethods = additionalMethods;
        }
    }
    internal static class BytecodeEmitter
    {
        public static BytecodeEmitResult Emit(BoundMethodBody loweredBody, ITokenProvider tokens)
        {
            if (loweredBody is null) throw new ArgumentNullException(nameof(loweredBody));
            if (tokens is null) throw new ArgumentNullException(nameof(tokens));

            var module = new EmitterModule(tokens);
            var entry = module.EmitRoot(loweredBody);
            return new BytecodeEmitResult(entry, module.BakeAdditionalMethods());
        }
        private sealed class EmitterModule
        {
            private readonly ITokenProvider _tokens;
            private readonly Dictionary<MethodSymbol, BytecodeFunction> _compiled;
            private readonly List<BytecodeFunction> _additional;

            public EmitterModule(ITokenProvider tokens)
            {
                _tokens = tokens;
                _compiled = new Dictionary<MethodSymbol, BytecodeFunction>(ReferenceEqualityComparer<MethodSymbol>.Instance);
                _additional = new List<BytecodeFunction>();
            }
            public BytecodeFunction EmitRoot(BoundMethodBody body)
                => Compile(body, addToAdditional: false);
            public BytecodeFunction EmitNonRoot(BoundMethodBody body)
                => Compile(body, addToAdditional: true);
            private BytecodeFunction Compile(BoundMethodBody body, bool addToAdditional)
            {
                if (_compiled.TryGetValue(body.Method, out var existing))
                    return existing;

                var emitter = new Emitter(_tokens, this, body.Method);
                var fn = emitter.Emit(body);

                _compiled.Add(body.Method, fn);
                if (addToAdditional)
                    _additional.Add(fn);

                return fn;
            }
            public ImmutableArray<BytecodeFunction> BakeAdditionalMethods()
                => _additional.ToImmutableArray();
        }
        private enum EmitMode : byte
        {
            Discard,
            Value
        }
        private sealed class Emitter
        {
            private readonly struct ExceptionHandlerSpec
            {
                public readonly BcLabel TryStart;
                public readonly BcLabel TryEnd;
                public readonly BcLabel HandlerStart;
                public readonly BcLabel HandlerEnd;
                public readonly int CatchTypeToken;
                public ExceptionHandlerSpec(BcLabel tryStart, BcLabel tryEnd, BcLabel handlerStart, BcLabel handlerEnd, int catchTypeToken)
                {
                    TryStart = tryStart;
                    TryEnd = tryEnd;
                    HandlerStart = handlerStart;
                    HandlerEnd = handlerEnd;
                    CatchTypeToken = catchTypeToken;
                }
            }
            private readonly ITokenProvider _tokens;
            private readonly EmitterModule _module;
            private readonly MethodSymbol _method;
            private readonly BytecodeBuilder _il = new();

            private readonly Dictionary<LocalSymbol, int> _localsBySymbol;
            private readonly List<TypeSymbol> _localTypes;
            private readonly Dictionary<ParameterSymbol, int> _argsBySymbol;
            private readonly Dictionary<LabelSymbol, BcLabel> _labelsBySymbol;
            private readonly List<ExceptionHandlerSpec> _ehSpecs = new();
            private int _spillId;
            public Emitter(ITokenProvider tokens, EmitterModule module, MethodSymbol method)
            {
                _tokens = tokens;
                _module = module;
                _method = method;

                _localsBySymbol = new Dictionary<LocalSymbol, int>(ReferenceEqualityComparer<LocalSymbol>.Instance);
                _localTypes = new List<TypeSymbol>();
                _argsBySymbol = BuildArgsMap(method);
                _labelsBySymbol = new Dictionary<LabelSymbol, BcLabel>(ReferenceEqualityComparer<LabelSymbol>.Instance);
            }
            private static Dictionary<ParameterSymbol, int> BuildArgsMap(MethodSymbol method)
            {
                var map = new Dictionary<ParameterSymbol, int>(ReferenceEqualityComparer<ParameterSymbol>.Instance);

                int start = method.IsStatic ? 0 : 1; // arg0 reserved for 'this'
                var ps = method.Parameters;
                for (int i = 0; i < ps.Length; i++)
                    map[ps[i]] = start + i;

                return map;
            }

            public BytecodeFunction Emit(BoundMethodBody body)
            {
                CollectLabels(body.Body);

                EmitStatement(body.Body);

                if (!_il.TryGetLastOp(out var lastOp) || lastOp != BytecodeOp.Ret)
                    EmitImplicitReturn(body);

                var instructions = _il.Bake(out var labelToPc);
                var exceptionHandlers = BakeExceptionHandlers(labelToPc);
                int maxStack = StackAnalyzer.ComputeMaxStack(instructions, entryPc: 0,
                    additionalEntryPcs: GetExceptionHandlerEntryPcs(exceptionHandlers));

                var localTypeTokens = ImmutableArray.CreateBuilder<int>(_localTypes.Count);
                for (int i = 0; i < _localTypes.Count; i++)
                    localTypeTokens.Add(_tokens.GetTypeToken(_localTypes[i]));

                int methodToken = _tokens.GetMethodToken(_method);
                return new BytecodeFunction(methodToken, localTypeTokens.ToImmutable(), instructions, maxStack, exceptionHandlers);
            }
            private ImmutableArray<ExceptionHandler> BakeExceptionHandlers(ImmutableArray<int> labelToPc)
            {
                if (_ehSpecs.Count == 0)
                    return ImmutableArray<ExceptionHandler>.Empty;

                var b = ImmutableArray.CreateBuilder<ExceptionHandler>(_ehSpecs.Count);
                for (int i = 0; i < _ehSpecs.Count; i++)
                {
                    var s = _ehSpecs[i];
                    int ts = labelToPc[s.TryStart.Id];
                    int te = labelToPc[s.TryEnd.Id];
                    int hs = labelToPc[s.HandlerStart.Id];
                    int he = labelToPc[s.HandlerEnd.Id];

                    if (ts > te) throw new InvalidOperationException("Bad try region in exception handler.");
                    if (hs > he) throw new InvalidOperationException("Bad handler region in exception handler.");

                    b.Add(new ExceptionHandler(ts, te, hs, he, s.CatchTypeToken));
                }
                return b.ToImmutable();
            }
            private static ImmutableArray<int> GetExceptionHandlerEntryPcs(ImmutableArray<ExceptionHandler> handlers)
            {
                if (handlers.IsDefaultOrEmpty)
                    return ImmutableArray<int>.Empty;

                var b = ImmutableArray.CreateBuilder<int>(handlers.Length);
                var seen = new HashSet<int>();
                for (int i = 0; i < handlers.Length; i++)
                {
                    int pc = handlers[i].HandlerStartPc;
                    if (seen.Add(pc))
                        b.Add(pc);
                }
                return b.ToImmutable();
            }
            private void EmitImplicitReturn(BoundMethodBody body)
            {
                if (IsVoid(body.Method.ReturnType))
                {
                    _il.Emit(BytecodeOp.Ret, pop: 0, push: 0);
                    return;
                }

                EmitDefaultValue(body.Method.ReturnType);
                _il.Emit(BytecodeOp.Ret, pop: 1, push: 0);
            }

            private void CollectLabels(BoundStatement s)
            {
                switch (s)
                {
                    case BoundBlockStatement b:
                        for (int i = 0; i < b.Statements.Length; i++)
                            CollectLabels(b.Statements[i]);
                        break;

                    case BoundLabelStatement ls:
                        GetOrCreateLabel(ls.Label);
                        break;

                    case BoundLocalFunctionStatement lfs:
                        // Labels inside local function bodies are independent
                        break;

                    default:
                        break;
                }
            }
            private BcLabel GetOrCreateLabel(LabelSymbol label)
            {
                if (_labelsBySymbol.TryGetValue(label, out var bc))
                    return bc;

                bc = _il.DefineLabel();
                _labelsBySymbol.Add(label, bc);
                return bc;
            }

            private int GetOrCreateLocal(LocalSymbol local)
            {
                if (_localsBySymbol.TryGetValue(local, out var idx))
                    return idx;

                idx = _localTypes.Count;
                _localsBySymbol.Add(local, idx);

                TypeSymbol storageType = local.IsByRef
                    ? new ByRefTypeSymbol(local.Type)
                    : local.Type;

                _localTypes.Add(storageType);
                return idx;
            }
            private int AllocateSpillLocal(TypeSymbol type)
            {
                _spillId++;
                int idx = _localTypes.Count;
                _localTypes.Add(type);
                return idx;
            }
            private int GetArgIndex(ParameterSymbol p)
            {
                if (!_argsBySymbol.TryGetValue(p, out var idx))
                    throw new InvalidOperationException($"Parameter '{p.Name}' not found in method '{_method.Name}'.");
                return idx;
            }

            private static bool IsVoid(TypeSymbol type) => type.SpecialType == SpecialType.System_Void;

            private static bool IsBool(TypeSymbol type) => type.SpecialType == SpecialType.System_Boolean;

            private static int GetElementSizeOrThrow(TypeSymbol type)
            {
                return type.SpecialType switch
                {
                    SpecialType.System_Boolean => 1,
                    SpecialType.System_Void => 1,
                    SpecialType.System_Int8 => 1,
                    SpecialType.System_UInt8 => 1,
                    SpecialType.System_Char => 2,
                    SpecialType.System_Int16 => 2,
                    SpecialType.System_UInt16 => 2,
                    SpecialType.System_Int32 => 4,
                    SpecialType.System_UInt32 => 4,
                    SpecialType.System_Single => 4,
                    SpecialType.System_Int64 => 8,
                    SpecialType.System_UInt64 => 8,
                    SpecialType.System_Double => 8,
                    SpecialType.System_Decimal => 16,
                    _ => (type.IsReferenceType || type is PointerTypeSymbol || type is ByRefTypeSymbol)
                        ? TargetArchitecture.PointerSize
                        : throw new NotSupportedException($"No known size for '{type.Name}'.")
                };
            }
            private static bool IsAddressableValueTypeReceiver(BoundExpression receiver)
            {
                return receiver.IsLValue || receiver is BoundThisExpression;
            }
            private void EmitStatement(BoundStatement s)
            {
                switch (s)
                {
                    case BoundEmptyStatement:
                        return;

                    case BoundBlockStatement b:
                        for (int i = 0; i < b.Statements.Length; i++)
                            EmitStatement(b.Statements[i]);
                        return;

                    case BoundExpressionStatement es:
                        EmitExpression(es.Expression, EmitMode.Discard);
                        return;

                    case BoundLocalDeclarationStatement ld:
                        EmitLocalDeclaration(ld);
                        return;

                    case BoundReturnStatement ret:
                        EmitReturn(ret);
                        return;

                    case BoundLabelStatement ls:
                        _il.MarkLabel(GetOrCreateLabel(ls.Label));
                        return;

                    case BoundGotoStatement gs:
                        _il.EmitBranch(BytecodeOp.Br, GetOrCreateLabel(gs.TargetLabel), pop: 0);
                        return;

                    case BoundConditionalGotoStatement cgs:
                        EmitConditionalGoto(cgs);
                        return;

                    case BoundLocalFunctionStatement lfs:
                        var lfBody = new BoundMethodBody(lfs.Syntax, lfs.LocalFunction, lfs.Body);
                        _module.EmitNonRoot(lfBody);
                        return;

                    case BoundThrowStatement ts:
                        EmitThrow(ts);
                        return;

                    case BoundTryStatement t:
                        EmitTry(t);
                        return;

                    default:
                        throw new NotSupportedException($"Statement '{s.GetType().Name}' is not supported by bytecode emitter.");
                }
            }
            private void EmitThrow(BoundThrowStatement ts)
            {
                if (ts.ExpressionOpt is null)
                {
                    _il.Emit(BytecodeOp.Rethrow, pop: 0, push: 0);
                    return;
                }
                EmitExpression(ts.ExpressionOpt, EmitMode.Value);
                _il.Emit(BytecodeOp.Throw, pop: 1, push: 0);
            }
            private void EmitTry(BoundTryStatement t)
            {
                bool hasCatches = !t.CatchBlocks.IsDefaultOrEmpty;
                bool hasFinally = t.FinallyBlockOpt is not null;

                if (!hasCatches && !hasFinally)
                    throw new NotSupportedException("try statement must have at least one catch or a finally.");

                for (int i = 0; i < t.CatchBlocks.Length; i++)
                    if (t.CatchBlocks[i].FilterOpt is not null)
                        throw new NotSupportedException("catch filters are not supported.");

                const int FinallyCatchTypeToken = -1;

                if (hasFinally && !hasCatches)
                {
                    // try/finally
                    var tryStart = _il.DefineLabel();
                    var finallyStart = _il.DefineLabel();
                    var after = _il.DefineLabel();

                    _il.MarkLabel(tryStart);
                    EmitStatement(t.TryBlock);

                    bool tryFallsThrough = !_il.TryGetLastOp(out var lastTryOp) ||
                        (lastTryOp != BytecodeOp.Ret && lastTryOp != BytecodeOp.Throw && lastTryOp != BytecodeOp.Rethrow);

                    if (tryFallsThrough)
                        _il.EmitBranch(BytecodeOp.Br, after, pop: 0);

                    _il.MarkLabel(finallyStart);
                    EmitStatement(t.FinallyBlockOpt!);
                    _il.Emit(BytecodeOp.Endfinally, pop: 0, push: 0);

                    _il.MarkLabel(after);

                    _ehSpecs.Add(new ExceptionHandlerSpec(tryStart, finallyStart, finallyStart, after, FinallyCatchTypeToken));
                    return;
                }

                if (hasCatches && !hasFinally)
                {
                    // try/catch
                    var tryStart = _il.DefineLabel();
                    var tryEnd = _il.DefineLabel();
                    var endLabel = _il.DefineLabel();
                    int n = t.CatchBlocks.Length;
                    var handlerStarts = new BcLabel[n];
                    for (int i = 0; i < n; i++)
                        handlerStarts[i] = _il.DefineLabel();

                    _il.MarkLabel(tryStart);
                    EmitStatement(t.TryBlock);

                    bool tryFallsThrough = !_il.TryGetLastOp(out var lastTryOp) ||
                        (lastTryOp != BytecodeOp.Ret && lastTryOp != BytecodeOp.Throw && lastTryOp != BytecodeOp.Rethrow);

                    _il.MarkLabel(tryEnd);
                    if (tryFallsThrough)
                        _il.EmitBranch(BytecodeOp.Br, endLabel, pop: 0);

                    for (int i = 0; i < n; i++)
                    {
                        var c = t.CatchBlocks[i];
                        var handlerStart = handlerStarts[i];
                        var handlerEnd = (i + 1 < n) ? handlerStarts[i + 1] : endLabel;

                        _il.MarkLabel(handlerStart);

                        if (c.ExceptionLocalOpt is not null)
                        {
                            int loc = GetOrCreateLocal(c.ExceptionLocalOpt);
                            _il.Emit(BytecodeOp.Ldexception, pop: 0, push: 1);
                            _il.Emit(BytecodeOp.Stloc, operand0: loc, pop: 1, push: 0);
                        }

                        EmitStatement(c.Body);

                        if (!_il.TryGetLastOp(out var lastOp) ||
                            (lastOp != BytecodeOp.Ret && lastOp != BytecodeOp.Throw && lastOp != BytecodeOp.Rethrow))
                        {
                            _il.EmitBranch(BytecodeOp.Br, endLabel, pop: 0);
                        }

                        int catchTypeTok = _tokens.GetTypeToken(c.ExceptionType);
                        _ehSpecs.Add(new ExceptionHandlerSpec(tryStart, tryEnd, handlerStart, handlerEnd, catchTypeTok));
                    }

                    _il.MarkLabel(endLabel);
                    return;
                }

                {
                    var tryStart = _il.DefineLabel();
                    var tryEnd = _il.DefineLabel();
                    var afterCatches = _il.DefineLabel();
                    var finallyStart = _il.DefineLabel();
                    var after = _il.DefineLabel();

                    int n = t.CatchBlocks.Length;
                    var handlerStarts = new BcLabel[n];
                    for (int i = 0; i < n; i++)
                        handlerStarts[i] = _il.DefineLabel();

                    _il.MarkLabel(tryStart);
                    EmitStatement(t.TryBlock);

                    bool tryFallsThrough = !_il.TryGetLastOp(out var lastTryOp) ||
                        (lastTryOp != BytecodeOp.Ret && lastTryOp != BytecodeOp.Throw && lastTryOp != BytecodeOp.Rethrow);

                    _il.MarkLabel(tryEnd);
                    if (tryFallsThrough)
                        _il.EmitBranch(BytecodeOp.Br, afterCatches, pop: 0);

                    for (int i = 0; i < n; i++)
                    {
                        var c = t.CatchBlocks[i];
                        var handlerStart = handlerStarts[i];
                        var handlerEnd = (i + 1 < n) ? handlerStarts[i + 1] : afterCatches;

                        _il.MarkLabel(handlerStart);

                        if (c.ExceptionLocalOpt is not null)
                        {
                            int loc = GetOrCreateLocal(c.ExceptionLocalOpt);
                            _il.Emit(BytecodeOp.Ldexception, pop: 0, push: 1);
                            _il.Emit(BytecodeOp.Stloc, operand0: loc, pop: 1, push: 0);
                        }

                        EmitStatement(c.Body);

                        if (!_il.TryGetLastOp(out var lastOp) ||
                            (lastOp != BytecodeOp.Ret && lastOp != BytecodeOp.Throw && lastOp != BytecodeOp.Rethrow))
                        {
                            _il.EmitBranch(BytecodeOp.Br, afterCatches, pop: 0);
                        }

                        int catchTypeTok = _tokens.GetTypeToken(c.ExceptionType);
                        _ehSpecs.Add(new ExceptionHandlerSpec(tryStart, tryEnd, handlerStart, handlerEnd, catchTypeTok));
                    }

                    _il.MarkLabel(afterCatches);
                    _il.EmitBranch(BytecodeOp.Br, after, pop: 0);

                    _il.MarkLabel(finallyStart);
                    EmitStatement(t.FinallyBlockOpt!);
                    _il.Emit(BytecodeOp.Endfinally, pop: 0, push: 0);

                    _il.MarkLabel(after);

                    _ehSpecs.Add(new ExceptionHandlerSpec(tryStart, finallyStart, finallyStart, after, FinallyCatchTypeToken));
                }
            }
            private void EmitLocalDeclaration(BoundLocalDeclarationStatement ld)
            {
                int localIndex = GetOrCreateLocal(ld.Local);
                if (ld.Local.IsByRef)
                {
                    if (ld.Initializer is null)
                        throw new InvalidOperationException("Ref local must have an initializer.");

                    EmitExpression(ld.Initializer, EmitMode.Value);
                    _il.Emit(BytecodeOp.Stloc, operand0: localIndex, pop: 1, push: 0);
                    return;
                }
                if (ld.Initializer is null)
                {
                    EmitDefaultValue(ld.Local.Type);
                    _il.Emit(BytecodeOp.Stloc, operand0: localIndex, pop: 1, push: 0);
                    return;
                }

                EmitExpression(ld.Initializer, EmitMode.Value);
                _il.Emit(BytecodeOp.Stloc, operand0: localIndex, pop: 1, push: 0);
            }

            private void EmitReturn(BoundReturnStatement ret)
            {
                if (ret.Expression is null)
                {
                    _il.Emit(BytecodeOp.Ret, pop: 0, push: 0);
                    return;
                }

                EmitExpression(ret.Expression, EmitMode.Value);
                _il.Emit(BytecodeOp.Ret, pop: 1, push: 0);
            }

            private void EmitConditionalGoto(BoundConditionalGotoStatement cgs)
            {
                EmitExpression(cgs.Condition, EmitMode.Value);

                var label = GetOrCreateLabel(cgs.TargetLabel);

                if (cgs.JumpIfTrue)
                    _il.EmitBranch(BytecodeOp.Brtrue, label, pop: 1);
                else
                    _il.EmitBranch(BytecodeOp.Brfalse, label, pop: 1);
            }

            private void EmitExpression(BoundExpression e, EmitMode mode)
            {
                switch (e)
                {
                    case BoundLiteralExpression lit:
                        EmitLiteral(lit, mode);
                        return;

                    case BoundLocalExpression loc:
                        EmitLocal(loc, mode);
                        return;

                    case BoundParameterExpression par:
                        EmitParameter(par, mode);
                        return;

                    case BoundThisExpression:
                        if (mode == EmitMode.Value)
                            _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                        return;

                    case BoundBaseExpression:
                        if (mode == EmitMode.Value)
                            _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                        return;

                    case BoundUnaryExpression un:
                        EmitUnary(un, mode);
                        return;

                    case BoundBinaryExpression bin:
                        EmitBinary(bin, mode);
                        return;

                    case BoundConditionalExpression c:
                        EmitConditionalExpression(c, mode);
                        return;

                    case BoundAssignmentExpression ass:
                        EmitAssignment(ass, mode);
                        return;

                    case BoundCallExpression call:
                        if (mode == EmitMode.Value && call.Method.ReturnType is ByRefTypeSymbol br)
                        {
                            EmitCall(call, EmitMode.Value);

                            _il.Emit(BytecodeOp.Ldobj, operand0: _tokens.GetTypeToken(br.ElementType), pop: 1, push: 1);
                            return;
                        }
                        EmitCall(call, mode);
                        return;

                    case BoundObjectCreationExpression obj:
                        EmitObjectCreation(obj, mode);
                        return;

                    case BoundConversionExpression conv:
                        EmitConversion(conv, mode);
                        return;

                    case BoundThrowExpression tex:
                        EmitExpression(tex.Exception, EmitMode.Value);
                        _il.Emit(BytecodeOp.Throw, pop: 1, push: 0);
                        return;

                    case BoundAsExpression @as:
                        EmitAs(@as, mode);
                        return;

                    case BoundIsPatternExpression isPattern:
                        EmitIsPattern(isPattern, mode);
                        return;

                    case BoundSequenceExpression seq:
                        EmitSequence(seq, mode);
                        return;

                    case BoundRefExpression re:
                        EmitRefExpression(re, mode);
                        return;

                    case BoundAddressOfExpression addrof:
                        EmitAddressOf(addrof, mode);
                        return;

                    case BoundPointerIndirectionExpression pind:
                        EmitPointerIndirection(pind, mode);
                        return;

                    case BoundPointerElementAccessExpression pea:
                        EmitPointerElementAccess(pea, mode);
                        return;

                    case BoundStackAllocArrayCreationExpression sa:
                        EmitStackAlloc(sa, mode);
                        return;

                    case BoundArrayCreationExpression ac:
                        EmitArrayCreation(ac, mode);
                        return;

                    case BoundArrayElementAccessExpression aea:
                        EmitArrayElementAccess(aea, mode);
                        return;

                    case BoundMemberAccessExpression ma:
                        EmitMemberAccess(ma, mode);
                        return;

                    case BoundSizeOfExpression so:
                        EmitSizeOf(so, mode);
                        return;

                    default:
                        throw new NotSupportedException($"Expression '{e.GetType().Name}' is not supported by bytecode emitter.");
                }
            }
            private void EmitConstantValue(TypeSymbol type, object? value, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return;

                if (value is null)
                {
                    _il.Emit(BytecodeOp.Ldnull, pop: 0, push: 1);
                    return;
                }

                switch (value)
                {
                    case bool b:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: b ? 1 : 0, pop: 0, push: 1);
                        return;
                    case char ch:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: ch, pop: 0, push: 1);
                        return;
                    case sbyte sb:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: sb, pop: 0, push: 1);
                        return;
                    case byte bb:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: bb, pop: 0, push: 1);
                        return;
                    case short s:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: s, pop: 0, push: 1);
                        return;
                    case ushort us:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: us, pop: 0, push: 1);
                        return;
                    case int i:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: i, pop: 0, push: 1);
                        return;
                    case uint ui:
                        unchecked { _il.Emit(BytecodeOp.Ldc_I4, operand0: (int)ui, pop: 0, push: 1); }
                        return;
                    case long l:
                        _il.Emit(BytecodeOp.Ldc_I8, operand2: l, pop: 0, push: 1);
                        return;
                    case ulong ul:
                        unchecked { _il.Emit(BytecodeOp.Ldc_I8, operand2: (long)ul, pop: 0, push: 1); }
                        return;
                    case float f:
                        _il.Emit(BytecodeOp.Ldc_R4, operand0: BitConverter.SingleToInt32Bits(f), pop: 0, push: 1);
                        return;
                    case double d:
                        _il.Emit(BytecodeOp.Ldc_R8, operand2: BitConverter.DoubleToInt64Bits(d), pop: 0, push: 1);
                        return;
                    case string str:
                        _il.Emit(BytecodeOp.Ldstr, operand0: _tokens.GetUserStringToken(str), pop: 0, push: 1);
                        return;
                    default:
                        throw new NotSupportedException($"Constant of type '{value.GetType().Name}' is not supported.");
                }
            }
            private void EmitLiteral(BoundLiteralExpression lit, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return;

                var v = lit.Value;
                if (v is null)
                {
                    _il.Emit(BytecodeOp.Ldnull, pop: 0, push: 1);
                    return;
                }

                switch (v)
                {
                    case bool b:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: b ? 1 : 0, pop: 0, push: 1);
                        return;

                    case char ch:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: ch, pop: 0, push: 1);
                        return;

                    case sbyte sb:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: sb, pop: 0, push: 1);
                        return;

                    case byte bb:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: bb, pop: 0, push: 1);
                        return;

                    case short s:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: s, pop: 0, push: 1);
                        return;

                    case ushort us:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: us, pop: 0, push: 1);
                        return;

                    case int i:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: i, pop: 0, push: 1);
                        return;

                    case uint ui:
                        unchecked { _il.Emit(BytecodeOp.Ldc_I4, operand0: (int)ui, pop: 0, push: 1); }
                        return;

                    case long l:
                        _il.Emit(BytecodeOp.Ldc_I8, operand2: l, pop: 0, push: 1);
                        return;

                    case ulong ul:
                        unchecked { _il.Emit(BytecodeOp.Ldc_I8, operand2: (long)ul, pop: 0, push: 1); }
                        return;

                    case float f:
                        _il.Emit(BytecodeOp.Ldc_R4, operand0: BitConverter.SingleToInt32Bits(f), pop: 0, push: 1);
                        return;

                    case double d:
                        _il.Emit(BytecodeOp.Ldc_R8, operand2: BitConverter.DoubleToInt64Bits(d), pop: 0, push: 1);
                        return;

                    case string str:
                        _il.Emit(BytecodeOp.Ldstr, operand0: _tokens.GetUserStringToken(str), pop: 0, push: 1);
                        return;

                    default:
                        throw new NotSupportedException($"Literal of type '{v.GetType().Name}' is not supported.");
                }
            }
            private void EmitMemberAccess(BoundMemberAccessExpression ma, EmitMode mode)
            {
                if (ma.ConstantValueOpt.HasValue)
                {
                    EmitConstantValue(ma.Type, ma.ConstantValueOpt.Value, mode);
                    return;
                }

                if (ma.Member is not FieldSymbol fs)
                    throw new NotSupportedException("BoundMemberAccessExpression must be lowered to FieldSymbol access before emission.");

                if (fs.IsConst && !fs.ConstantValueOpt.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Const field '{fs.Name}' has no constant value in metadata.");
                }

                if (fs.IsConst && fs.ConstantValueOpt.HasValue)
                {
                    EmitConstantValue(fs.Type, fs.ConstantValueOpt.Value, mode);
                    return;
                }

                int tok = _tokens.GetFieldToken(fs);

                if (fs.IsStatic)
                {
                    _il.Emit(BytecodeOp.Ldsfld, operand0: tok, pop: 0, push: 1);
                }
                else
                {
                    if (ma.ReceiverOpt is null)
                        throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                    if (ma.ReceiverOpt.Type.IsValueType)
                    {
                        if (!IsAddressableValueTypeReceiver(ma.ReceiverOpt))
                        {
                            int spill = AllocateSpillLocal(ma.ReceiverOpt.Type);
                            EmitExpression(ma.ReceiverOpt, EmitMode.Value);
                            _il.Emit(BytecodeOp.Stloc, operand0: spill, pop: 1, push: 0);
                            _il.Emit(BytecodeOp.Ldloca, operand0: spill, pop: 0, push: 1);
                        }
                        else
                        {
                            EmitLoadAddressOfLValue(ma.ReceiverOpt);
                        }
                    }
                    else
                    {
                        EmitExpression(ma.ReceiverOpt, EmitMode.Value);
                    }
                    _il.Emit(BytecodeOp.Ldfld, operand0: tok, pop: 1, push: 1);
                }

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);

            }
            private void EmitLocal(BoundLocalExpression loc, EmitMode mode)
            {
                int idx = GetOrCreateLocal(loc.Local);

                if (mode != EmitMode.Value)
                    return;

                if (loc.Local.IsByRef)
                {
                    _il.Emit(BytecodeOp.Ldloc, operand0: idx, pop: 0, push: 1);
                    _il.Emit(BytecodeOp.Ldobj, operand0: _tokens.GetTypeToken(loc.Local.Type), pop: 1, push: 1);
                    return;
                }

                _il.Emit(BytecodeOp.Ldloc, operand0: idx, pop: 0, push: 1);
            }

            private void EmitParameter(BoundParameterExpression par, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return;

                int idx = GetArgIndex(par.Parameter);
                if (par.Parameter.Type is ByRefTypeSymbol br)
                {
                    _il.Emit(BytecodeOp.Ldarg, operand0: idx, pop: 0, push: 1);
                    _il.Emit(BytecodeOp.Ldobj, operand0: _tokens.GetTypeToken(br.ElementType), pop: 1, push: 1);
                    return;
                }

                _il.Emit(BytecodeOp.Ldarg, operand0: idx, pop: 0, push: 1);
            }
            private void EmitUnary(BoundUnaryExpression un, EmitMode mode)
            {
                EmitExpression(un.Operand, mode == EmitMode.Value ? EmitMode.Value : EmitMode.Discard);

                if (mode == EmitMode.Discard)
                    return;

                switch (un.OperatorKind)
                {
                    case BoundUnaryOperatorKind.UnaryPlus:
                        // No-op
                        return;

                    case BoundUnaryOperatorKind.UnaryMinus:
                        _il.Emit(BytecodeOp.Neg, pop: 1, push: 1);
                        return;

                    case BoundUnaryOperatorKind.LogicalNot:
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        return;
                    case BoundUnaryOperatorKind.BitwiseNot:
                        _il.Emit(BytecodeOp.Not, pop: 1, push: 1);
                        return;

                    default:
                        throw new NotSupportedException($"Unary operator '{un.OperatorKind}' is not supported.");
                }
            }
            private static bool UsesUnsignedIntegerSemantics(TypeSymbol t)
            {
                if (t is PointerTypeSymbol)
                    return true;

                var st = t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum
                    ? (nt.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32)
                    : t.SpecialType;

                return st is
                    SpecialType.System_Char or
                    SpecialType.System_UInt8 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_UInt64 or
                    SpecialType.System_UIntPtr;
            }
            private void EmitBinary(BoundBinaryExpression bin, EmitMode mode)
            {
                if (bin.OperatorKind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr)
                {
                    EmitShortCircuitLogical(bin, mode);
                    return;
                }

                EmitExpression(bin.Left, EmitMode.Value);
                EmitExpression(bin.Right, EmitMode.Value);

                if (mode == EmitMode.Discard)
                {
                    EmitBinaryOperator(bin);
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    return;
                }

                EmitBinaryOperator(bin);
            }

            private void EmitBinaryOperator(BoundBinaryExpression bin)
            {
                var op = bin.OperatorKind;

                if (op == BoundBinaryOperatorKind.Subtract &&
                    bin.Left.Type is PointerTypeSymbol lpt &&
                    bin.Right.Type is PointerTypeSymbol rpt)
                {
                    if (!ReferenceEquals(lpt, rpt))
                        throw new NotSupportedException("Pointer subtraction requires both operands to have the same pointer type.");

                    _il.Emit(
                        BytecodeOp.PtrDiff,
                        operand0: GetElementSizeOrThrow(lpt.PointedAtType),
                        pop: 2,
                        push: 1);
                    return;
                }


                if (bin.Type is PointerTypeSymbol ptrType &&
                    op is BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Subtract)
                {
                    if (bin.Left.Type is not PointerTypeSymbol)
                        throw new NotSupportedException("Pointer arithmetic expects the pointer operand on the left.");

                    if (op == BoundBinaryOperatorKind.Subtract)
                        _il.Emit(BytecodeOp.Neg, pop: 1, push: 1); // [ptr, -index]

                    _il.Emit(
                        BytecodeOp.PtrElemAddr,
                        operand0: GetElementSizeOrThrow(ptrType.PointedAtType),
                        pop: 2,
                        push: 1);

                    return;
                }
                bool u = UsesUnsignedIntegerSemantics(bin.Left.Type);
                switch (op)
                {
                    case BoundBinaryOperatorKind.Add:
                        _il.Emit(BytecodeOp.Add, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.Subtract:
                        _il.Emit(BytecodeOp.Sub, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.Multiply:
                        _il.Emit(BytecodeOp.Mul, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.Divide:
                        _il.Emit(u ? BytecodeOp.Div_Un : BytecodeOp.Div, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.Modulo:
                        _il.Emit(u ? BytecodeOp.Rem_Un : BytecodeOp.Rem, pop: 2, push: 1);
                        return;

                    case BoundBinaryOperatorKind.BitwiseAnd:
                        _il.Emit(BytecodeOp.And, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.BitwiseOr:
                        _il.Emit(BytecodeOp.Or, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.ExclusiveOr:
                        _il.Emit(BytecodeOp.Xor, pop: 2, push: 1);
                        return;

                    case BoundBinaryOperatorKind.LeftShift:
                        _il.Emit(BytecodeOp.Shl, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.RightShift:
                        _il.Emit(u ? BytecodeOp.Shr_Un : BytecodeOp.Shr, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.UnsignedRightShift:
                        _il.Emit(BytecodeOp.Shr_Un, pop: 2, push: 1);
                        return;

                    case BoundBinaryOperatorKind.Equals:
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.NotEquals:
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        return;

                    case BoundBinaryOperatorKind.LessThan:
                        _il.Emit(u ? BytecodeOp.Clt_Un : BytecodeOp.Clt, pop: 2, push: 1);
                        return;
                    case BoundBinaryOperatorKind.GreaterThan:
                        _il.Emit(u ? BytecodeOp.Cgt_Un : BytecodeOp.Cgt, pop: 2, push: 1);
                        return;

                    case BoundBinaryOperatorKind.LessThanOrEqual:
                        // !(a > b) => (a > b) == 0
                        _il.Emit(u ? BytecodeOp.Cgt_Un : BytecodeOp.Cgt, pop: 2, push: 1);
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        return;

                    case BoundBinaryOperatorKind.GreaterThanOrEqual:
                        // !(a < b) => (a < b) == 0
                        _il.Emit(u ? BytecodeOp.Clt_Un : BytecodeOp.Clt, pop: 2, push: 1);
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        return;

                    default:
                        throw new NotSupportedException($"Binary operator '{op}' is not supported.");
                }
            }
            private void EmitShortCircuitLogical(BoundBinaryExpression bin, EmitMode mode)
            {
                if (!IsBool(bin.Type))
                    throw new InvalidOperationException($"Logical operator expected bool result, got '{bin.Type.Name}'.");

                if (mode == EmitMode.Discard)
                {
                    var skipLabel = _il.DefineLabel();

                    EmitExpression(bin.Left, EmitMode.Value);
                    if (bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd)
                        _il.EmitBranch(BytecodeOp.Brfalse, skipLabel, pop: 1);
                    else
                        _il.EmitBranch(BytecodeOp.Brtrue, skipLabel, pop: 1);

                    EmitExpression(bin.Right, EmitMode.Discard);
                    _il.MarkLabel(skipLabel);
                    return;
                }

                var lFalse = _il.DefineLabel();
                var lEnd = _il.DefineLabel();

                EmitExpression(bin.Left, EmitMode.Value);

                if (bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd)
                    _il.EmitBranch(BytecodeOp.Brfalse, lFalse, pop: 1);
                else
                    _il.EmitBranch(BytecodeOp.Brtrue, lFalse, pop: 1);

                EmitExpression(bin.Right, EmitMode.Value);

                if (bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd)
                    _il.EmitBranch(BytecodeOp.Brfalse, lFalse, pop: 1);
                else
                    _il.EmitBranch(BytecodeOp.Brtrue, lFalse, pop: 1);

                // Both operands satisfied condition
                _il.Emit(BytecodeOp.Ldc_I4, operand0: bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd ? 1 : 0, pop: 0, push: 1);

                _il.EmitBranch(BytecodeOp.Br, lEnd, pop: 0);

                _il.MarkLabel(lFalse);
                _il.Emit(BytecodeOp.Ldc_I4, operand0: bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd ? 0 : 1, pop: 0, push: 1);

                _il.MarkLabel(lEnd);
            }
            private void EmitConditionalExpression(BoundConditionalExpression c, EmitMode mode)
            {
                var lElse = _il.DefineLabel();
                var lEnd = _il.DefineLabel();

                EmitExpression(c.Condition, EmitMode.Value);
                _il.EmitBranch(BytecodeOp.Brfalse, lElse, pop: 1);

                EmitExpression(c.WhenTrue, mode);
                _il.EmitBranch(BytecodeOp.Br, lEnd, pop: 0);

                _il.MarkLabel(lElse);
                EmitExpression(c.WhenFalse, mode);

                _il.MarkLabel(lEnd);
            }
            private void EmitAssignment(BoundAssignmentExpression assignment, EmitMode mode)
            {
                if (assignment.HasErrors || assignment.Left.HasErrors || assignment.Right.HasErrors)
                {
                    if (mode == EmitMode.Value)
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                    return;
                }
                if (assignment.Left is BoundArrayElementAccessExpression aea)
                {
                    if (aea.Indices.Length != 1)
                        throw new NotSupportedException("Only single dimensional arrays are supported.");

                    EmitExpression(aea.Expression, EmitMode.Value);
                    EmitExpression(aea.Indices[0], EmitMode.Value);
                    EmitExpression(assignment.Right, EmitMode.Value);

                    int elemTok = _tokens.GetTypeToken(aea.Type);

                    if (mode == EmitMode.Discard)
                    {
                        _il.Emit(BytecodeOp.Stelem, operand0: elemTok, pop: 3, push: 0);
                        return;
                    }

                    int spill = AllocateSpillLocal(assignment.Type);
                    _il.Emit(BytecodeOp.Dup, pop: 1, push: 2); // arr, idx, val, val
                    _il.Emit(BytecodeOp.Stloc, operand0: spill, pop: 1, push: 0); // arr, idx, val
                    _il.Emit(BytecodeOp.Stelem, operand0: elemTok, pop: 3, push: 0);
                    _il.Emit(BytecodeOp.Ldloc, operand0: spill, pop: 0, push: 1);
                    return;
                }
                if (assignment.Left is BoundMemberAccessExpression { Member: FieldSymbol fs } leftField)
                {
                    if (fs.IsConst)
                        throw new NotSupportedException("Cannot assign to a const field in lowered form.");

                    int tok = _tokens.GetFieldToken(fs);

                    if (fs.IsStatic)
                    {
                        EmitExpression(assignment.Right, EmitMode.Value);

                        if (mode == EmitMode.Value)
                            _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);

                        _il.Emit(BytecodeOp.Stsfld, operand0: tok, pop: 1, push: 0);
                        return;
                    }

                    if (leftField.ReceiverOpt is null)
                        throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                    if (leftField.ReceiverOpt.Type.IsValueType)
                    {
                        if (!IsAddressableValueTypeReceiver(leftField.ReceiverOpt))
                            throw new InvalidOperationException("Cannot assign to a field of a non-lvalue value-type receiver.");

                        EmitLoadAddressOfLValue(leftField.ReceiverOpt);
                    }
                    else
                    {
                        EmitExpression(leftField.ReceiverOpt, EmitMode.Value);
                    }

                    EmitExpression(assignment.Right, EmitMode.Value);

                    if (mode == EmitMode.Value)
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2); // recv, val, val

                    _il.Emit(BytecodeOp.Stfld, operand0: tok, pop: 2, push: 0);
                    return;
                }
                if (assignment.Left is BoundLocalExpression leftLocal)
                {
                    int idx = GetOrCreateLocal(leftLocal.Local);
                    if (leftLocal.Local.IsByRef)
                    {
                        _il.Emit(BytecodeOp.Ldloc, operand0: idx, pop: 0, push: 1); // load address
                        EmitExpression(assignment.Right, EmitMode.Value);

                        int elemTok2 = _tokens.GetTypeToken(leftLocal.Local.Type);

                        if (mode == EmitMode.Discard)
                        {
                            _il.Emit(BytecodeOp.Stobj, operand0: elemTok2, pop: 2, push: 0);
                            return;
                        }

                        int spill2 = AllocateSpillLocal(assignment.Type);

                        // Stack: addr, val
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);                       // addr, val, val
                        _il.Emit(BytecodeOp.Stloc, operand0: spill2, pop: 1, push: 0);    // addr, val
                        _il.Emit(BytecodeOp.Stobj, operand0: elemTok2, pop: 2, push: 0);
                        _il.Emit(BytecodeOp.Ldloc, operand0: spill2, pop: 0, push: 1);
                        return;
                    }
                    EmitExpression(assignment.Right, EmitMode.Value);

                    if (mode == EmitMode.Value)
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);

                    _il.Emit(BytecodeOp.Stloc, operand0: idx, pop: 1, push: 0);
                    return;
                }

                if (assignment.Left is BoundParameterExpression leftPar)
                {
                    int idx = GetArgIndex(leftPar.Parameter);
                    if (leftPar.Parameter.Type is ByRefTypeSymbol byRefPar)
                    {
                        _il.Emit(BytecodeOp.Ldarg, operand0: idx, pop: 0, push: 1);
                        EmitExpression(assignment.Right, EmitMode.Value);

                        int byRefElemTok = _tokens.GetTypeToken(byRefPar.ElementType);

                        if (mode == EmitMode.Discard)
                        {
                            _il.Emit(BytecodeOp.Stobj, operand0: byRefElemTok, pop: 2, push: 0);
                            return;
                        }

                        int spill2 = AllocateSpillLocal(assignment.Type);
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);
                        _il.Emit(BytecodeOp.Stloc, operand0: spill2, pop: 1, push: 0);
                        _il.Emit(BytecodeOp.Stobj, operand0: byRefElemTok, pop: 2, push: 0);
                        _il.Emit(BytecodeOp.Ldloc, operand0: spill2, pop: 0, push: 1);
                        return;
                    }
                    EmitExpression(assignment.Right, EmitMode.Value);

                    if (mode == EmitMode.Value)
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);

                    _il.Emit(BytecodeOp.Starg, operand0: idx, pop: 1, push: 0);
                    return;
                }
                {
                    EmitLoadAddressOfLValue(assignment.Left);
                    EmitExpression(assignment.Right, EmitMode.Value);

                    int elemTok = _tokens.GetTypeToken(assignment.Left.Type);

                    if (mode == EmitMode.Discard)
                    {
                        _il.Emit(BytecodeOp.Stobj, operand0: elemTok, pop: 2, push: 0);
                        return;
                    }

                    int spill = AllocateSpillLocal(assignment.Type);

                    // Stack: addr, val
                    _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);             // addr, val, val
                    _il.Emit(BytecodeOp.Stloc, operand0: spill, pop: 1, push: 0); // addr, val
                    _il.Emit(BytecodeOp.Stobj, operand0: elemTok, pop: 2, push: 0);
                    _il.Emit(BytecodeOp.Ldloc, operand0: spill, pop: 0, push: 1);
                }
            }
            private void EmitArrayCreation(BoundArrayCreationExpression ac, EmitMode mode)
            {
                if (ac.Type is not ArrayTypeSymbol at)
                    throw new InvalidOperationException("BoundArrayCreationExpression.Type is not an array type.");

                if (at.Rank != 1 || ac.DimensionSizes.Length > 1)
                    throw new NotSupportedException("Only single dimensional arrays are supported.");

                if (ac.DimensionSizes.Length == 0)
                {
                    int len = ac.InitializerOpt?.Elements.Length ?? 0;
                    _il.Emit(BytecodeOp.Ldc_I4, operand0: len, pop: 0, push: 1);
                }
                else
                {
                    EmitExpression(ac.DimensionSizes[0], EmitMode.Value);
                }
                int elemTok = _tokens.GetTypeToken(ac.ElementType);
                _il.Emit(BytecodeOp.Newarr, operand0: elemTok, pop: 1, push: 1);

                if (ac.InitializerOpt is not null)
                {
                    var elems = ac.InitializerOpt.Elements;
                    for (int i = 0; i < elems.Length; i++)
                    {
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);                 // arr, arr
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: i, pop: 0, push: 1); // arr, arr, i
                        EmitExpression(elems[i], EmitMode.Value);                  // arr, arr, i, val
                        _il.Emit(BytecodeOp.Stelem, operand0: elemTok, pop: 3, push: 0);
                    }
                }
                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private void EmitArrayElementAccess(BoundArrayElementAccessExpression aea, EmitMode mode)
            {
                if (aea.Indices.Length != 1)
                    throw new NotSupportedException("Only single dimensional arrays are supported.");

                EmitExpression(aea.Expression, EmitMode.Value);
                EmitExpression(aea.Indices[0], EmitMode.Value);

                int elemTok = _tokens.GetTypeToken(aea.Type);
                _il.Emit(BytecodeOp.Ldelem, operand0: elemTok, pop: 2, push: 1);

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private void EmitCall(BoundCallExpression call, EmitMode mode)
            {
                if (TryEmitIntrinsic(call, mode))
                    return;

                int argCount = 0;
                int hasThis = call.Method.IsStatic ? 0 : 1;

                bool thisIsManagedByRef = false; // valuetype this passed byref

                if (!call.Method.IsStatic)
                {
                    if (call.ReceiverOpt is null)
                    {
                        if (_method.IsStatic)
                            throw new InvalidOperationException(
                                $"Instance call to '{call.Method.Name}' without receiver (in static method).");

                        var thisType = _method.ContainingSymbol as NamedTypeSymbol
                            ?? throw new InvalidOperationException("Cannot resolve implicit 'this' receiver type.");

                        bool recvIsValueType = thisType.IsValueType;

                        if (recvIsValueType)
                        {
                            bool methodDeclaredOnRefType =
                                call.Method.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;

                            if (methodDeclaredOnRefType)
                            {
                                _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                                _il.Emit(
                                    BytecodeOp.Box,
                                    operand0: _tokens.GetTypeToken(thisType),
                                    pop: 1,
                                    push: 1);
                            }
                            else
                            {
                                thisIsManagedByRef = true;
                                _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                            }
                        }
                        else
                        {
                            _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                        }
                    }
                    else
                    {
                        var recv = call.ReceiverOpt;
                        bool recvIsValueType = recv.Type.IsValueType;

                        if (recvIsValueType)
                        {
                            // If method is declared on reference type
                            bool methodDeclaredOnRefType =
                                call.Method.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;

                            if (methodDeclaredOnRefType)
                            {
                                EmitExpression(recv, EmitMode.Value);
                                _il.Emit(
                                    BytecodeOp.Box,
                                    operand0: _tokens.GetTypeToken(recv.Type),
                                    pop: 1,
                                    push: 1);

                            }
                            else
                            {
                                // Method declared on the value type itself
                                thisIsManagedByRef = true;

                                if (!IsAddressableValueTypeReceiver(recv))
                                {
                                    int spill = AllocateSpillLocal(recv.Type);
                                    EmitExpression(recv, EmitMode.Value);
                                    _il.Emit(BytecodeOp.Stloc, operand0: spill, pop: 1, push: 0);
                                    _il.Emit(BytecodeOp.Ldloca, operand0: spill, pop: 0, push: 1);
                                }
                                else
                                {
                                    EmitLoadAddressOfLValue(recv);
                                }
                            }
                        }
                        else
                        {
                            // reference type receiver
                            EmitExpression(recv, EmitMode.Value);
                        }
                    }

                }

                var args = call.Arguments;
                for (int i = 0; i < args.Length; i++)
                {
                    EmitExpression(args[i], EmitMode.Value);
                    argCount++;
                }

                int token = _tokens.GetMethodToken(call.Method);
                int packed = (argCount & 0x7FFF) | (hasThis << 15);

                short pop = checked((short)(argCount + hasThis));
                short push = (short)(IsVoid(call.Method.ReturnType) ? 0 : 1);

                bool isBaseReceiver = call.ReceiverOpt is BoundBaseExpression;

                BytecodeOp op =
                    thisIsManagedByRef || isBaseReceiver
                        ? BytecodeOp.Call
                        : (!call.Method.IsStatic && (call.Method.IsVirtual || call.Method.IsAbstract || call.Method.IsOverride))
                            ? BytecodeOp.CallVirt
                            : BytecodeOp.Call;

                _il.Emit(op, operand0: token, operand1: packed, pop: pop, push: push);

                if (mode == EmitMode.Discard && push == 1)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private bool TryEmitIntrinsic(BoundCallExpression call, EmitMode mode)
            {
                var def = call.Method.OriginalDefinition;
                if (!def.IsStatic)
                    return false;
                if (def.ContainingSymbol is not NamedTypeSymbol containingType)
                    return false;

                // Unsafe
                if (containingType.Name == "Unsafe" && IsInNamespace(containingType, "System", "Runtime", "CompilerServices"))
                {
                    var ps = call.Method.Parameters;

                    // Unsafe.SizeOf<T>()
                    if (def.Name == "SizeOf" && ps.Length == 0)
                    {
                        var tas = call.Method.TypeArguments;
                        if (tas.Length != 1) return false;

                        _il.Emit(BytecodeOp.Sizeof, operand0: _tokens.GetTypeToken(tas[0]), pop: 0, push: 1);

                        if (mode == EmitMode.Discard)
                            _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);

                        return true;
                    }
                    // Unsafe.As<T>
                    if (def.Name == "As" && ps.Length == 1)
                    {
                        // Unsafe.As<T>(object)
                        if (ps[0].Type.SpecialType == SpecialType.System_Object)
                        {
                            EmitExpression(call.Arguments[0], EmitMode.Value);
                            if (mode == EmitMode.Discard)
                                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                            return true;
                        }

                        // Unsafe.As<TFrom, TTo>(ref TFrom)
                        if (ps[0].Type is ByRefTypeSymbol)
                        {
                            EmitExpression(call.Arguments[0], EmitMode.Value);
                            if (mode == EmitMode.Discard)
                                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                            return true;
                        }
                    }

                    // Unsafe.AsRef<T>
                    if (def.Name == "AsRef" && ps.Length == 1)
                    {
                        // Unsafe.AsRef<T>(void*)
                        if (ps[0].Type is PointerTypeSymbol)
                        {
                            EmitExpression(call.Arguments[0], EmitMode.Value);
                            _il.Emit(BytecodeOp.PtrToByRef, pop: 1, push: 1);
                            if (mode == EmitMode.Discard)
                                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                            return true;
                        }

                        // Unsafe.AsRef<T>(scoped ref readonly T)
                        if (ps[0].Type is ByRefTypeSymbol)
                        {
                            EmitExpression(call.Arguments[0], EmitMode.Value);
                            if (mode == EmitMode.Discard)
                                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                            return true;
                        }
                    }

                    // Unsafe.ReadUnaligned<T>(scoped ref readonly byte source)
                    if (def.Name == "ReadUnaligned" && ps.Length == 1 &&
                        ps[0].Type is ByRefTypeSymbol br0 &&
                        br0.ElementType.SpecialType == SpecialType.System_UInt8)
                    {
                        EmitExpression(call.Arguments[0], EmitMode.Value); // byref

                        int tTok = _tokens.GetTypeToken(call.Type); // !!T
                        _il.Emit(BytecodeOp.Ldobj, operand0: tTok, pop: 1, push: 1);

                        if (mode == EmitMode.Discard)
                            _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                        return true;
                    }
                    // Unsafe.WriteUnaligned<T>(ref byte destination, T value)
                    if (def.Name == "WriteUnaligned" && ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol brDst &&
                        brDst.ElementType.SpecialType == SpecialType.System_UInt8)
                    {
                        var tas = call.Method.TypeArguments;
                        if (tas.Length != 1)
                            return false;

                        EmitExpression(call.Arguments[0], EmitMode.Value); // byref destination
                        EmitExpression(call.Arguments[1], EmitMode.Value); // value

                        int tTok = _tokens.GetTypeToken(tas[0]); // !!T
                        _il.Emit(BytecodeOp.Stobj, operand0: tTok, pop: 2, push: 0);

                        return true;
                    }

                    // Unsafe.Add<T>(ref T, int) / Unsafe.Add<T>(ref T, IntPtr) / Unsafe.Add<T>(void*, int)
                    if (def.Name == "Add" && ps.Length == 2)
                    {
                        var p0 = ps[0].Type;
                        var p1 = ps[1].Type;

                        bool isRefSource = p0 is ByRefTypeSymbol;
                        bool isVoidPtrSource = p0 is PointerTypeSymbol ptr0 && ptr0.PointedAtType.SpecialType == SpecialType.System_Void;

                        if ((isRefSource || isVoidPtrSource) &&
                            (p1.SpecialType == SpecialType.System_Int32 || p1.SpecialType == SpecialType.System_IntPtr))
                        {
                            TypeSymbol elemType = p0 switch
                            {
                                ByRefTypeSymbol br => br.ElementType,
                                PointerTypeSymbol ptr => ptr.PointedAtType,
                                _ => throw new InvalidOperationException()
                            };

                            EmitExpression(call.Arguments[0], EmitMode.Value); // base (byref/ptr)
                            EmitExpression(call.Arguments[1], EmitMode.Value); // elementOffset

                            if (p1.SpecialType == SpecialType.System_Int32)
                                EmitIntrinsicConv(NumericConvKind.NativeInt);

                            _il.Emit(BytecodeOp.Sizeof, operand0: _tokens.GetTypeToken(elemType), pop: 0, push: 1);
                            EmitIntrinsicConv(NumericConvKind.NativeInt);
                            _il.Emit(BytecodeOp.Mul, pop: 2, push: 1); // scaled byte offset
                            _il.Emit(BytecodeOp.PtrElemAddr, operand0: 1, pop: 2, push: 1);

                            if (mode == EmitMode.Discard)
                                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                            return true;
                        }
                    }

                    // Unsafe.ByteOffset<T>(ref T origin, ref T target)
                    if (def.Name == "ByteOffset" && ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol && ps[1].Type is ByRefTypeSymbol)
                    {
                        EmitExpression(call.Arguments[0], EmitMode.Value); // origin
                        EmitExpression(call.Arguments[1], EmitMode.Value); // target

                        _il.Emit(BytecodeOp.PtrDiff, operand0: 1, pop: 2, push: 1); // origin - target
                        _il.Emit(BytecodeOp.Neg, pop: 1, push: 1);// target - origin

                        if (mode == EmitMode.Discard)
                            _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);

                        return true;
                    }
                    // Unsafe.AddByteOffset<T>(ref T, IntPtr/nuint)
                    if (def.Name == "AddByteOffset" && ps.Length == 2 && ps[0].Type is ByRefTypeSymbol)
                    {
                        var p1 = ps[1].Type;
                        if (p1.SpecialType == SpecialType.System_IntPtr || p1.SpecialType == SpecialType.System_UIntPtr)
                        {
                            EmitExpression(call.Arguments[0], EmitMode.Value);
                            EmitExpression(call.Arguments[1], EmitMode.Value);

                            if (p1.SpecialType == SpecialType.System_UIntPtr)
                                EmitIntrinsicConv(NumericConvKind.U8);

                            _il.Emit(BytecodeOp.PtrElemAddr, operand0: 1, pop: 2, push: 1);

                            if (mode == EmitMode.Discard)
                                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                            return true;
                        }
                    }
                    // Unsafe.AreSame<T>(ref readonly T, ref readonly T)
                    if (def.Name == "AreSame" && ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol && ps[1].Type is ByRefTypeSymbol)
                    {
                        EmitExpression(call.Arguments[0], EmitMode.Value);
                        EmitExpression(call.Arguments[1], EmitMode.Value);
                        _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
                        if (mode == EmitMode.Discard)
                            _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                        return true;
                    }
                    return false;
                }
                // MemoryMarshal
                if (containingType.Name == "MemoryMarshal" && IsInNamespace(containingType, "System", "Runtime", "InteropServices"))
                {
                    // MemoryMarshal.GetArrayDataReference
                    if (def.Name == "GetArrayDataReference" && call.Method.Parameters.Length == 1)
                    {
                        EmitExpression(call.Arguments[0], EmitMode.Value);
                        _il.Emit(BytecodeOp.LdArrayDataRef, pop: 1, push: 1);

                        if (mode == EmitMode.Discard)
                            _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                        return true;
                    }
                }
                return false;
            }
            private void EmitIntrinsicConv(NumericConvKind kind, NumericConvFlags flags = NumericConvFlags.None)
            {
                _il.Emit(BytecodeOp.Conv, operand0: (int)kind, operand1: (int)flags, pop: 1, push: 1);
            }
            private static bool IsInNamespace(NamedTypeSymbol type, params string[] nsParts)
            {
                Symbol? s = type.ContainingSymbol;
                for (int i = nsParts.Length - 1; i >= 0; i--)
                {
                    if (s is not NamespaceSymbol ns)
                        return false;

                    if (!string.Equals(ns.Name, nsParts[i], StringComparison.Ordinal))
                        return false;

                    s = ns.ContainingSymbol;
                }

                return s is null
                    || (s is NamespaceSymbol g && (g.IsGlobalNamespace || string.IsNullOrEmpty(g.Name)));
            }
            private void EmitObjectCreation(BoundObjectCreationExpression obj, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                {
                    // Still must allocate/run ctor if it has side effects
                }

                if (obj.ConstructorOpt is null)
                {
                    // Struct default construction
                    EmitDefaultValue(obj.Type);
                    if (mode == EmitMode.Discard && !IsVoid(obj.Type))
                        _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    return;
                }

                var args = obj.Arguments;
                for (int i = 0; i < args.Length; i++)
                    EmitExpression(args[i], EmitMode.Value);

                int token = _tokens.GetMethodToken(obj.ConstructorOpt);
                short pop = checked((short)args.Length);

                _il.Emit(BytecodeOp.Newobj, operand0: token, operand1: args.Length, pop: pop, push: 1);

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private static bool IsSystemNullableValueType(TypeSymbol t)
            {
                if (t is not NamedTypeSymbol nt || !nt.IsValueType)
                    return false;

                var def = nt.OriginalDefinition;
                if (def.Arity != 1 || !string.Equals(def.Name, "Nullable", StringComparison.Ordinal))
                    return false;

                return def.ContainingSymbol is NamespaceSymbol ns
                    && string.Equals(ns.Name, "System", StringComparison.Ordinal);
            }
            private static bool TryGetSystemNullableUnderlying(TypeSymbol t, out TypeSymbol underlying)
            {
                if (t is NamedTypeSymbol nt && IsSystemNullableValueType(t))
                {
                    underlying = nt.TypeArguments[0];
                    return true;
                }

                underlying = null!;
                return false;
            }
            private void EmitAs(BoundAsExpression @as, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                {
                    EmitExpression(@as.Operand, EmitMode.Discard);
                    return;
                }

                // Evaluate operand
                EmitExpression(@as.Operand, EmitMode.Value);

                if (@as.Operand.Type.IsValueType)
                    _il.Emit(BytecodeOp.Box, operand0: _tokens.GetTypeToken(@as.Operand.Type), pop: 1, push: 1);

                // Nullable<T> target
                if (TryGetSystemNullableUnderlying(@as.Type, out var underlying))
                {
                    var lNull = _il.DefineLabel();
                    var lFail = _il.DefineLabel();
                    var lEnd = _il.DefineLabel();

                    // if (obj == null) return default(Nullable<T>);
                    _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);
                    _il.EmitBranch(BytecodeOp.Brfalse, lNull, pop: 1);

                    // obj = obj is T ? obj : null
                    _il.Emit(BytecodeOp.Isinst, operand0: _tokens.GetTypeToken(underlying), pop: 1, push: 1);

                    _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);
                    _il.EmitBranch(BytecodeOp.Brfalse, lFail, pop: 1);

                    _il.Emit(BytecodeOp.UnboxAny, operand0: _tokens.GetTypeToken(@as.Type), pop: 1, push: 1);
                    _il.EmitBranch(BytecodeOp.Br, lEnd, pop: 0);

                    _il.MarkLabel(lFail);
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    EmitDefaultValue(@as.Type);
                    _il.EmitBranch(BytecodeOp.Br, lEnd, pop: 0);

                    // null operand
                    _il.MarkLabel(lNull);
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    EmitDefaultValue(@as.Type);

                    _il.MarkLabel(lEnd);
                    return;
                }

                // Reference type target
                _il.Emit(BytecodeOp.Isinst, operand0: _tokens.GetTypeToken(@as.Type), pop: 1, push: 1);
            }
            private void EmitBoolNot()
            {
                _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);
            }
            private void EmitIsPattern(BoundIsPatternExpression isPattern, EmitMode mode)
            {
                switch (isPattern.PatternKind)
                {
                    case BoundIsPatternKind.Type:
                        EmitTypePattern(isPattern, mode);
                        return;

                    case BoundIsPatternKind.Null:
                        EmitNullPattern(isPattern, mode);
                        return;

                    case BoundIsPatternKind.Constant:
                        EmitConstantPattern(isPattern, mode);
                        return;

                    default:
                        throw new NotSupportedException($"Unexpected pattern kind '{isPattern.PatternKind}'.");
                }
            }
            private void EmitTypePattern(BoundIsPatternExpression p, EmitMode mode)
            {
                EmitExpression(p.Operand, EmitMode.Value);

                if (p.Operand.Type.IsValueType)
                    _il.Emit(BytecodeOp.Box, operand0: _tokens.GetTypeToken(p.Operand.Type), pop: 1, push: 1);

                _il.Emit(BytecodeOp.Isinst, operand0: _tokens.GetTypeToken(p.PatternTypeOpt!), pop: 1, push: 1);

                if (mode == EmitMode.Discard)
                {
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    return;
                }

                var lFalse = _il.DefineLabel();
                var lEnd = _il.DefineLabel();

                _il.Emit(BytecodeOp.Dup, pop: 1, push: 2);
                _il.EmitBranch(BytecodeOp.Brfalse, lFalse, pop: 1);

                if (p.DeclaredLocalOpt is not null && !p.IsDiscard)
                {
                    int localIndex = GetOrCreateLocal(p.DeclaredLocalOpt);
                    if (p.PatternTypeOpt!.IsValueType)
                        _il.Emit(BytecodeOp.UnboxAny, operand0: _tokens.GetTypeToken(p.PatternTypeOpt), pop: 1, push: 1);

                    _il.Emit(BytecodeOp.Stloc, operand0: localIndex, pop: 1, push: 0);
                }
                else
                {
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                }

                _il.Emit(BytecodeOp.Ldc_I4, operand0: 1, pop: 0, push: 1);
                if (p.IsNegated)
                    EmitBoolNot();
                _il.EmitBranch(BytecodeOp.Br, lEnd, pop: 0);

                _il.MarkLabel(lFalse);
                _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                _il.Emit(BytecodeOp.Ldc_I4, operand0: 0, pop: 0, push: 1);
                if (p.IsNegated)
                    EmitBoolNot();

                _il.MarkLabel(lEnd);
            }
            private void EmitNullPattern(BoundIsPatternExpression p, EmitMode mode)
            {
                EmitExpression(p.Operand, EmitMode.Value);

                if (p.Operand.Type.IsValueType)
                    _il.Emit(BytecodeOp.Box, operand0: _tokens.GetTypeToken(p.Operand.Type), pop: 1, push: 1);

                _il.Emit(BytecodeOp.Ldnull, pop: 0, push: 1);
                _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);

                if (p.IsNegated)
                    EmitBoolNot();

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private void EmitConstantPattern(BoundIsPatternExpression p, EmitMode mode)
            {
                EmitExpression(p.Operand, EmitMode.Value);
                EmitExpression(p.ConstantOpt!, EmitMode.Value);

                _il.Emit(BytecodeOp.Ceq, pop: 2, push: 1);

                if (p.IsNegated)
                    EmitBoolNot();

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private void EmitConversion(BoundConversionExpression conv, EmitMode mode)
            {
                if (conv.Conversion.Kind == ConversionKind.NullLiteral)
                {
                    EmitExpression(conv.Operand, EmitMode.Discard);

                    if (mode == EmitMode.Value)
                    {
                        if (IsSystemNullableValueType(conv.Type))
                            EmitDefaultValue(conv.Type);
                        else
                            _il.Emit(BytecodeOp.Ldnull, pop: 0, push: 1);
                    }
                    return;
                }
                EmitExpression(conv.Operand, mode == EmitMode.Value ? EmitMode.Value : EmitMode.Discard);

                if (mode == EmitMode.Discard)
                    return;

                switch (conv.Conversion.Kind)
                {
                    case ConversionKind.Identity:
                        return;

                    case ConversionKind.NullLiteral:
                        _il.Emit(BytecodeOp.Ldnull, pop: 0, push: 1);
                        return;

                    case ConversionKind.ImplicitNumeric:
                    case ConversionKind.ExplicitNumeric:
                    case ConversionKind.ImplicitConstant:
                        if (conv.Operand.Type is PointerTypeSymbol && conv.Type is PointerTypeSymbol)
                            return;
                        EmitNumericConv(conv.Operand.Type, conv.Type, conv.IsChecked);
                        return;

                    case ConversionKind.ImplicitReference:
                    case ConversionKind.ExplicitReference:
                        _il.Emit(BytecodeOp.CastClass, operand0: _tokens.GetTypeToken(conv.Type), pop: 1, push: 1);
                        return;

                    case ConversionKind.Boxing:
                        _il.Emit(BytecodeOp.Box, operand0: _tokens.GetTypeToken(conv.Operand.Type), pop: 1, push: 1);
                        return;

                    case ConversionKind.Unboxing:
                        _il.Emit(BytecodeOp.UnboxAny, operand0: _tokens.GetTypeToken(conv.Type), pop: 1, push: 1);
                        return;

                    default:
                        throw new NotSupportedException($"Conversion kind '{conv.Conversion.Kind}' is not supported.");
                }
            }
            private void EmitNumericConv(TypeSymbol sourceType, TypeSymbol targetType, bool isChecked)
            {
                var targetSpecial = targetType is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum
                    ? (nt.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32)
                    : targetType.SpecialType;

                var kind = targetSpecial switch
                {
                    SpecialType.System_Boolean => NumericConvKind.Bool,
                    SpecialType.System_Char => NumericConvKind.Char,
                    SpecialType.System_Int8 => NumericConvKind.I1,
                    SpecialType.System_UInt8 => NumericConvKind.U1,
                    SpecialType.System_Int16 => NumericConvKind.I2,
                    SpecialType.System_UInt16 => NumericConvKind.U2,
                    SpecialType.System_Int32 => NumericConvKind.I4,
                    SpecialType.System_UInt32 => NumericConvKind.U4,
                    SpecialType.System_Int64 => NumericConvKind.I8,
                    SpecialType.System_UInt64 => NumericConvKind.U8,
                    SpecialType.System_Single => NumericConvKind.R4,
                    SpecialType.System_Double => NumericConvKind.R8,
                    SpecialType.System_IntPtr => NumericConvKind.NativeInt,
                    SpecialType.System_UIntPtr => NumericConvKind.NativeUInt,
                    _ => targetType is PointerTypeSymbol ? NumericConvKind.NativeUInt
                         : throw new NotSupportedException($"Numeric conversion to '{targetType.Name}' is not supported.")
                };

                int flags = 0;
                if (isChecked)
                    flags |= (int)NumericConvFlags.Checked;

                if (UsesUnsignedIntegerSemantics(sourceType))
                    flags |= (int)NumericConvFlags.SourceUnsigned;

                _il.Emit(BytecodeOp.Conv, operand0: (int)kind, operand1: flags, pop: 1, push: 1);
            }

            private void EmitSequence(BoundSequenceExpression seq, EmitMode mode)
            {
                // Ensure locals exist in the local sig
                for (int i = 0; i < seq.Locals.Length; i++)
                    GetOrCreateLocal(seq.Locals[i]);

                for (int i = 0; i < seq.SideEffects.Length; i++)
                    EmitStatement(seq.SideEffects[i]);

                EmitExpression(seq.Value, mode == EmitMode.Value ? EmitMode.Value : EmitMode.Discard);
            }
            private void EmitDefaultValue(TypeSymbol type)
            {
                _il.Emit(BytecodeOp.DefaultValue, operand0: _tokens.GetTypeToken(type), pop: 0, push: 1);
            }
            private void EmitSizeOf(BoundSizeOfExpression so, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return;
                _il.Emit(BytecodeOp.Sizeof, operand0: _tokens.GetTypeToken(so.OperandType), pop: 0, push: 1);
            }
            private void EmitRefExpression(BoundRefExpression re, EmitMode mode)
            {
                EmitLoadAddressOfLValue(re.Operand);

                if (re.Operand is BoundPointerIndirectionExpression or BoundPointerElementAccessExpression)
                    _il.Emit(BytecodeOp.PtrToByRef, pop: 1, push: 1);

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private void EmitAddressOf(BoundAddressOfExpression addrof, EmitMode mode)
            {
                // &lvalue
                if (mode == EmitMode.Discard)
                {
                    EmitLoadAddressOfLValue(addrof.Operand);
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    return;
                }

                EmitLoadAddressOfLValue(addrof.Operand);
            }
            private void EmitPointerIndirection(BoundPointerIndirectionExpression pind, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                {
                    // Evaluate operand for side effects
                    EmitExpression(pind.Operand, EmitMode.Discard);
                    return;
                }

                EmitExpression(pind.Operand, EmitMode.Value);
                _il.Emit(BytecodeOp.Ldobj, operand0: _tokens.GetTypeToken(pind.Type), pop: 1, push: 1);
            }

            private void EmitPointerElementAccess(BoundPointerElementAccessExpression pea, EmitMode mode)
            {
                // *(expr + idx)
                EmitExpression(pea.Expression, EmitMode.Value);
                EmitExpression(pea.Index, EmitMode.Value);

                int size = GetElementSizeOrThrow(pea.Type);
                _il.Emit(BytecodeOp.PtrElemAddr, operand0: size, pop: 2, push: 1);

                if (mode == EmitMode.Discard)
                {
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
                    return;
                }

                _il.Emit(BytecodeOp.Ldobj, operand0: _tokens.GetTypeToken(pea.Type), pop: 1, push: 1);
            }

            private void EmitStackAlloc(BoundStackAllocArrayCreationExpression sa, EmitMode mode)
            {
                // count -> StackAlloc(elementSize)
                EmitExpression(sa.Count, EmitMode.Value);

                int size = GetElementSizeOrThrow(sa.ElementType);
                _il.Emit(BytecodeOp.StackAlloc, operand0: size, pop: 1, push: 1);
                int elemTok = _tokens.GetTypeToken(sa.ElementType);
                if (sa.InitializerOpt is not null)
                {
                    var elems = sa.InitializerOpt.Elements;
                    for (int i = 0; i < elems.Length; i++)
                    {
                        _il.Emit(BytecodeOp.Dup, pop: 1, push: 2); // ptr, ptr
                        _il.Emit(BytecodeOp.Ldc_I4, operand0: i, pop: 0, push: 1); // ptr, ptr, i
                        _il.Emit(BytecodeOp.PtrElemAddr, operand0: size, pop: 2, push: 1); // ptr, addr
                        EmitExpression(elems[i], EmitMode.Value); // ptr, addr, val
                        _il.Emit(BytecodeOp.Stobj, operand0: elemTok, pop: 2, push: 0); // ptr
                    }
                }

                if (mode == EmitMode.Discard)
                    _il.Emit(BytecodeOp.Pop, pop: 1, push: 0);
            }
            private void EmitLoadAddressOfLValue(BoundExpression lvalue)
            {
                switch (lvalue)
                {
                    case BoundLocalExpression loc:
                        {
                            int idx = GetOrCreateLocal(loc.Local);
                            if (loc.Local.IsByRef)
                            {
                                _il.Emit(BytecodeOp.Ldloc, operand0: idx, pop: 0, push: 1);
                            }
                            else
                            {
                                _il.Emit(BytecodeOp.Ldloca, operand0: idx, pop: 0, push: 1);
                            }
                            return;
                        }
                    case BoundArrayElementAccessExpression aea:
                        {
                            if (aea.Indices.Length != 1)
                                throw new NotSupportedException("Only single dimensional arrays are supported.");

                            EmitExpression(aea.Expression, EmitMode.Value);
                            EmitExpression(aea.Indices[0], EmitMode.Value);

                            int elemTok = _tokens.GetTypeToken(aea.Type);
                            _il.Emit(BytecodeOp.Ldelema, operand0: elemTok, pop: 2, push: 1);
                        }
                        return;
                    case BoundParameterExpression par:
                        if (par.Parameter.Type is ByRefTypeSymbol)
                        {
                            _il.Emit(BytecodeOp.Ldarg, operand0: GetArgIndex(par.Parameter), pop: 0, push: 1);
                        }
                        else
                        {
                            _il.Emit(BytecodeOp.Ldarga, operand0: GetArgIndex(par.Parameter), pop: 0, push: 1);
                        }
                        return;

                    case BoundThisExpression:
                        _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                        return;

                    case BoundBaseExpression:
                        _il.Emit(BytecodeOp.Ldthis, pop: 0, push: 1);
                        return;

                    case BoundCallExpression call when call.Method.ReturnType is ByRefTypeSymbol:
                        EmitCall(call, EmitMode.Value);
                        return;

                    case BoundIndexerAccessExpression ia when ia.Indexer.GetMethod?.ReturnType is ByRefTypeSymbol:
                        {
                            if (ia.Indexer.IsStatic)
                                throw new NotSupportedException("Static indexers are not supported as lvalues.");

                            bool thisIsManagedByRef = false;
                            if (ia.Receiver.Type.IsValueType)
                            {
                                bool methodDeclaredOnRefType =
                                    ia.Indexer.GetMethod!.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;

                                if (methodDeclaredOnRefType)
                                {
                                    EmitExpression(ia.Receiver, EmitMode.Value);
                                    _il.Emit(BytecodeOp.Box, operand0: _tokens.GetTypeToken(ia.Receiver.Type), pop: 1, push: 1);
                                }
                                else if (IsAddressableValueTypeReceiver(ia.Receiver))
                                {
                                    thisIsManagedByRef = true;
                                    EmitLoadAddressOfLValue(ia.Receiver);
                                }
                                else
                                {
                                    int spill = AllocateSpillLocal(ia.Receiver.Type);
                                    EmitExpression(ia.Receiver, EmitMode.Value);
                                    _il.Emit(BytecodeOp.Stloc, operand0: spill, pop: 1, push: 0);
                                    _il.Emit(BytecodeOp.Ldloca, operand0: spill, pop: 0, push: 1);
                                    thisIsManagedByRef = true;
                                }
                            }
                            else
                            {
                                EmitExpression(ia.Receiver, EmitMode.Value);
                            }

                            for (int i = 0; i < ia.Arguments.Length; i++)
                                EmitExpression(ia.Arguments[i], EmitMode.Value);

                            int token = _tokens.GetMethodToken(ia.Indexer.GetMethod!);
                            int packed = (ia.Arguments.Length & 0x7FFF) | (ia.Indexer.GetMethod!.IsStatic ? 0 : 1 << 15);
                            BytecodeOp op = thisIsManagedByRef ? BytecodeOp.Call : BytecodeOp.CallVirt;
                            _il.Emit(op, operand0: token, operand1: packed, pop: checked((short)(ia.Arguments.Length + 1)), push: 1);
                            return;
                        }

                    case BoundMemberAccessExpression { Member: FieldSymbol fs } ma when fs.Type is not ByRefTypeSymbol:
                        {
                            int tok = _tokens.GetFieldToken(fs);

                            if (fs.IsStatic)
                            {
                                _il.Emit(BytecodeOp.Ldsflda, operand0: tok, pop: 0, push: 1);
                                return;
                            }
                            if (ma.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                            if (ma.ReceiverOpt.Type.IsValueType)
                            {
                                EmitLoadAddressOfLValue(ma.ReceiverOpt); // pushes ByRef/Ptr to receiver
                            }
                            else
                            {
                                EmitExpression(ma.ReceiverOpt, EmitMode.Value); // pushes Ref
                            }

                            _il.Emit(BytecodeOp.Ldflda, operand0: tok, pop: 1, push: 1);
                            return;
                        }

                    case BoundMemberAccessExpression { Member: FieldSymbol fs } ma when fs.Type is ByRefTypeSymbol:
                        {
                            int tok = _tokens.GetFieldToken(fs);

                            if (fs.IsStatic)
                            {
                                _il.Emit(BytecodeOp.Ldsfld, operand0: tok, pop: 0, push: 1);
                                return;
                            }

                            if (ma.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                            if (ma.ReceiverOpt.Type.IsValueType)
                            {
                                if (!IsAddressableValueTypeReceiver(ma.ReceiverOpt))
                                    throw new InvalidOperationException("Cannot take ref to a field of a non-lvalue value-type receiver.");

                                EmitLoadAddressOfLValue(ma.ReceiverOpt);
                            }
                            else
                            {
                                EmitExpression(ma.ReceiverOpt, EmitMode.Value);
                            }

                            _il.Emit(BytecodeOp.Ldfld, operand0: tok, pop: 1, push: 1);
                            return;
                        }

                    case BoundPointerIndirectionExpression pind:
                        EmitExpression(pind.Operand, EmitMode.Value);
                        return;

                    case BoundPointerElementAccessExpression pea:
                        EmitExpression(pea.Expression, EmitMode.Value);
                        EmitExpression(pea.Index, EmitMode.Value);
                        _il.Emit(BytecodeOp.PtrElemAddr, operand0: GetElementSizeOrThrow(pea.Type), pop: 2, push: 1);
                        return;

                    default:
                        throw new NotSupportedException($"Cannot take address of lvalue '{lvalue.GetType().Name}'.");
                }
            }
        }
        private static class StackAnalyzer
        {
            public static int ComputeMaxStack(ImmutableArray<Instruction> insns, int entryPc, ImmutableArray<int> additionalEntryPcs)
            {
                if (insns.IsDefaultOrEmpty)
                    return 0;

                var stackAt = new int?[insns.Length];
                var work = new Queue<int>();

                stackAt[entryPc] = 0;
                work.Enqueue(entryPc);
                if (!additionalEntryPcs.IsDefaultOrEmpty)
                {
                    for (int i = 0; i < additionalEntryPcs.Length; i++)
                    {
                        int pc = additionalEntryPcs[i];
                        if ((uint)pc >= (uint)insns.Length)
                            throw new InvalidOperationException($"Invalid entry PC {pc}.");

                        if (stackAt[pc] is null)
                        {
                            stackAt[pc] = 0;
                            work.Enqueue(pc);
                        }
                        else if (stackAt[pc]!.Value != 0)
                        {
                            throw new InvalidOperationException($"Stack height mismatch at entry PC {pc}.");
                        }
                    }
                }
                int max = 0;

                while (work.Count != 0)
                {
                    int pc = work.Dequeue();
                    int stack = stackAt[pc]!.Value;

                    for (int i = pc; i < insns.Length; i++)
                    {
                        var ins = insns[i];

                        int nextStack = stack - ins.Pop + ins.Push;
                        if (nextStack < 0)
                            throw new InvalidOperationException($"Stack underflow at PC {i} ({ins.Op}).");

                        if (nextStack > max)
                            max = nextStack;

                        int fallthroughPc = i + 1;

                        switch (ins.Op)
                        {
                            case BytecodeOp.Br:
                                Propagate(ins.Operand0, nextStack);
                                goto NextWorkItem;

                            case BytecodeOp.Brtrue:
                            case BytecodeOp.Brfalse:
                                Propagate(ins.Operand0, nextStack);
                                stack = nextStack;
                                break;

                            case BytecodeOp.Ret:
                            case BytecodeOp.Throw:
                            case BytecodeOp.Rethrow:
                                goto NextWorkItem;

                            default:
                                stack = nextStack;
                                break;
                        }

                        if (fallthroughPc >= insns.Length)
                            goto NextWorkItem;

                        if (stackAt[fallthroughPc] is null)
                        {
                            stackAt[fallthroughPc] = stack;
                            continue;
                        }

                        if (stackAt[fallthroughPc]!.Value != stack)
                            throw new InvalidOperationException($"Stack height mismatch at join PC {fallthroughPc}. Expected {stackAt[fallthroughPc]}, got {stack}.");

                        // Already visited with the same stack height, stop linear scan
                        goto NextWorkItem;

                        void Propagate(int targetPc, int stackHeight)
                        {
                            if ((uint)targetPc >= (uint)insns.Length)
                                throw new InvalidOperationException($"Invalid branch target PC {targetPc}.");

                            if (stackAt[targetPc] is null)
                            {
                                stackAt[targetPc] = stackHeight;
                                work.Enqueue(targetPc);
                                return;
                            }

                            if (stackAt[targetPc]!.Value != stackHeight)
                                throw new InvalidOperationException($"Stack height mismatch at label PC {targetPc}. Expected {stackAt[targetPc]}, got {stackHeight}.");
                        }
                    }

                NextWorkItem:
                    ;
                }

                return max;
            }
        }
    }

}
