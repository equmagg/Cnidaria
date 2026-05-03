using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    public enum Op : byte
    {
        Nop,
        Move,
        Ldnull,
        Ldc_I4,
        Ldc_I8,
        Ldc_R8,
        Ldstr,
        DefaultValue,
        Ldloc,
        Stloc,
        Ldloca,
        Ldarg,
        Starg,
        Ldarga,
        Ldthis,
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
        Call,
        CallVirt,
        Newobj,
        Ldfld,
        Stfld,
        Ldsfld,
        Stsfld,
        Ldflda,
        Ldsflda,
        Conv,
        CastClass,
        Box,
        UnboxAny,
        Br,
        Brtrue,
        Brfalse,
        Ret,
        Throw,
        Rethrow,
        Ldexception,
        Endfinally,
        StackAlloc,
        PtrElemAddr,
        PtrToByRef,
        Ldobj,
        Stobj,
        Newarr,
        Ldelem,
        Ldelema,
        Stelem,
        LdArrayDataRef,
        Sizeof,
        PtrDiff,
        Isinst,

        Ldarg_0,
        Ldarg_1,
        Ldarg_2,
        Ldarg_3,
        Starg_0,
        Starg_1,
        Starg_2,
        Starg_3,
        Ldarga_0,
        Ldarga_1,
        Ldarga_2,
        Ldarga_3,
        Ldloc_0,
        Ldloc_1,
        Ldloc_2,
        Ldloc_3,
        Stloc_0,
        Stloc_1,
        Stloc_2,
        Stloc_3,
        Ldloca_0,
        Ldloca_1,
        Ldloca_2,
        Ldloca_3,

        Ldc_I4_M1,
        Ldc_I4_0,
        Ldc_I4_1,
        Ldc_I4_2,
        Ldc_I4_3,
        Ldc_I4_4,
        Ldc_I4_5,
        Ldc_I4_6,
        Ldc_I4_7,
        Ldc_I4_8,
        Ldc_I4_S,
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
    [Flags]
    internal enum NumericConvFlags : byte
    {
        None = 0,
        Checked = 1 << 0,
        SourceUnsigned = 1 << 1,
    }
    public readonly struct Instruction
    {
        public const int NoValue = -1;

        public readonly Op Op;
        public readonly int Result;
        public readonly int ValueCount;
        public readonly int Value0;
        public readonly int Value1;
        public readonly int Value2;
        private readonly int[]? _overflowValues;
        public readonly int Operand0;
        public readonly int Operand1;
        public readonly long Operand2;

        public Instruction(Op op, int result, ImmutableArray<int> values, int operand0, int operand1, long operand2)
        {
            Op = op;
            Result = result;

            if (values.IsDefaultOrEmpty)
            {
                ValueCount = 0;
                Value0 = NoValue;
                Value1 = NoValue;
                Value2 = NoValue;
                _overflowValues = null;
            }
            else
            {
                int count = values.Length;
                ValueCount = count;
                Value0 = count > 0 ? values[0] : NoValue;
                Value1 = count > 1 ? values[1] : NoValue;
                Value2 = count > 2 ? values[2] : NoValue;
                _overflowValues = count > 3 ? values.ToArray() : null;
            }

            Operand0 = operand0;
            Operand1 = operand1;
            Operand2 = operand2;
        }

        internal Instruction(
            Op op,
            int result,
            int valueCount,
            int value0,
            int value1,
            int value2,
            int[]? overflowValues,
            int operand0,
            int operand1,
            long operand2)
        {
            Op = op;
            Result = result;
            ValueCount = valueCount;
            Value0 = value0;
            Value1 = value1;
            Value2 = value2;
            _overflowValues = overflowValues;
            Operand0 = operand0;
            Operand1 = operand1;
            Operand2 = operand2;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetValue(int index)
        {
            if ((uint)index >= (uint)ValueCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (_overflowValues is not null)
                return _overflowValues[index];

            return index switch
            {
                0 => Value0,
                1 => Value1,
                2 => Value2,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        public Instruction WithOperand0(int operand0)
            => new Instruction(Op, Result, ValueCount, Value0, Value1, Value2, _overflowValues, operand0, Operand1, Operand2);

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Result >= 0)
                sb.Append('%').Append(Result).Append(" = ");
            sb.Append(Op);
            if (ValueCount != 0)
            {
                sb.Append(' ');
                for (int i = 0; i < ValueCount; i++)
                {
                    if (i != 0) sb.Append(", ");
                    sb.Append('%').Append(GetValue(i));
                }
            }
            if (Operand0 != 0 || Operand1 != 0 || Operand2 != 0)
                sb.Append(" ; ").Append(Operand0).Append(' ').Append(Operand1).Append(' ').Append(Operand2);
            return sb.ToString();
        }
    }

    internal readonly struct Label
    {
        public readonly int Id;
        public Label(int id) => Id = id;
        public override string ToString() => $"L{Id}";
    }

    public readonly struct ExceptionHandler
    {
        public readonly int TryStartPc;
        public readonly int TryEndPc;
        public readonly int HandlerStartPc;
        public readonly int HandlerEndPc;
        public readonly int CatchTypeToken;

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
        private readonly List<Instruction> _insns = new();
        private readonly List<int> _labelToPc = new();
        private readonly List<(int pc, Label label)> _fixups = new();

        public int Count => _insns.Count;

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
            var r = new Cnidaria.Cs.Stack.SigReader(sig);
            byte cc = r.ReadByte();

            if ((cc & 0x20) != 0)
                return false;

            if ((cc & 0x10) != 0)
            {
                r.ReadCompressedUInt();
                return false;
            }

            uint paramCount = r.ReadCompressedUInt();
            if (paramCount != 1)
                return false;

            if ((SigElementType)r.ReadByte() != SigElementType.VOID)
                return false;

            if ((SigElementType)r.ReadByte() != SigElementType.SZARRAY)
                return false;

            if ((SigElementType)r.ReadByte() != SigElementType.STRING)
                return false;

            return true;
        }
        public Label DefineLabel()
        {
            var id = _labelToPc.Count;
            _labelToPc.Add(-1);
            return new Label(id);
        }

        public void MarkLabel(Label label)
        {
            if ((uint)label.Id >= (uint)_labelToPc.Count)
                throw new ArgumentOutOfRangeException(nameof(label));

            if (_labelToPc[label.Id] >= 0)
                throw new InvalidOperationException($"Label '{label}' already marked.");

            _labelToPc[label.Id] = _insns.Count;
        }

        public void Emit(Op op, int result = Instruction.NoValue, ImmutableArray<int> values = default, int operand0 = 0, int operand1 = 0, long operand2 = 0)
        {
            op = Canonicalize(op, result, values.IsDefault ? 0 : values.Length, operand0, operand1, operand2);
            _insns.Add(new Instruction(op, result, values, operand0, operand1, operand2));
        }

        public void Emit(Op op, int result, int value0, int operand0 = 0, int operand1 = 0, long operand2 = 0)
        {
            op = Canonicalize(op, result, 1, operand0, operand1, operand2);
            _insns.Add(new Instruction(op, result, 1, value0, Instruction.NoValue, Instruction.NoValue, null, operand0, operand1, operand2));
        }

        public void Emit2(Op op, int result, int value0, int value1, int operand0 = 0, int operand1 = 0, long operand2 = 0)
        {
            op = Canonicalize(op, result, 2, operand0, operand1, operand2);
            _insns.Add(new Instruction(op, result, 2, value0, value1, Instruction.NoValue, null, operand0, operand1, operand2));
        }

        public void Emit3(Op op, int result, int value0, int value1, int value2, int operand0 = 0, int operand1 = 0, long operand2 = 0)
        {
            op = Canonicalize(op, result, 3, operand0, operand1, operand2);
            _insns.Add(new Instruction(op, result, 3, value0, value1, value2, null, operand0, operand1, operand2));
        }

        private static Op Canonicalize(Op op, int result, int valueCount, int operand0, int operand1, long operand2)
        {
            return op switch
            {
                Op.Ldthis => Op.Ldarg_0,

                Op.Ldarg when valueCount == 0 => operand0 switch
                {
                    0 => Op.Ldarg_0,
                    1 => Op.Ldarg_1,
                    2 => Op.Ldarg_2,
                    3 => Op.Ldarg_3,
                    _ => Op.Ldarg
                },
                Op.Starg when valueCount == 1 => operand0 switch
                {
                    0 => Op.Starg_0,
                    1 => Op.Starg_1,
                    2 => Op.Starg_2,
                    3 => Op.Starg_3,
                    _ => Op.Starg
                },
                Op.Ldarga when valueCount == 0 => operand0 switch
                {
                    0 => Op.Ldarga_0,
                    1 => Op.Ldarga_1,
                    2 => Op.Ldarga_2,
                    3 => Op.Ldarga_3,
                    _ => Op.Ldarga
                },
                Op.Ldloc when valueCount == 0 => operand0 switch
                {
                    0 => Op.Ldloc_0,
                    1 => Op.Ldloc_1,
                    2 => Op.Ldloc_2,
                    3 => Op.Ldloc_3,
                    _ => Op.Ldloc
                },
                Op.Stloc when valueCount == 1 => operand0 switch
                {
                    0 => Op.Stloc_0,
                    1 => Op.Stloc_1,
                    2 => Op.Stloc_2,
                    3 => Op.Stloc_3,
                    _ => Op.Stloc
                },
                Op.Ldloca when valueCount == 0 => operand0 switch
                {
                    0 => Op.Ldloca_0,
                    1 => Op.Ldloca_1,
                    2 => Op.Ldloca_2,
                    3 => Op.Ldloca_3,
                    _ => Op.Ldloca
                },
                Op.Ldc_I4 when valueCount == 0 => operand0 switch
                {
                    -1 => Op.Ldc_I4_M1,
                    0 => Op.Ldc_I4_0,
                    1 => Op.Ldc_I4_1,
                    2 => Op.Ldc_I4_2,
                    3 => Op.Ldc_I4_3,
                    4 => Op.Ldc_I4_4,
                    5 => Op.Ldc_I4_5,
                    6 => Op.Ldc_I4_6,
                    7 => Op.Ldc_I4_7,
                    8 => Op.Ldc_I4_8,
                    >= sbyte.MinValue and <= sbyte.MaxValue => Op.Ldc_I4_S,
                    _ => Op.Ldc_I4
                },
                _ => op
            };
        }

        public void EmitBranch(Op op, Label target, ImmutableArray<int> values = default)
        {
            if (op is not (Op.Br or Op.Brtrue or Op.Brfalse))
                throw new ArgumentOutOfRangeException(nameof(op));

            int pc = _insns.Count;
            _insns.Add(new Instruction(op, Instruction.NoValue, values, target.Id, 0, 0));
            _fixups.Add((pc, target));
        }

        public void EmitBranch(Op op, Label target, int value0)
        {
            if (op is not (Op.Brtrue or Op.Brfalse))
                throw new ArgumentOutOfRangeException(nameof(op));

            int pc = _insns.Count;
            _insns.Add(new Instruction(op, Instruction.NoValue, 1, value0, Instruction.NoValue, Instruction.NoValue, null, target.Id, 0, 0));
            _fixups.Add((pc, target));
        }

        public bool TryGetLastOp(out Op op)
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
                baked[pc] = old.WithOperand0(targetPc);
            }

            labelToPc = _labelToPc.ToImmutableArray();
            return baked.ToImmutableArray();
        }
    }

    public sealed class BytecodeFunction
    {
        public int MethodToken { get; }
        public ImmutableArray<int> LocalTypeTokens { get; }
        public ImmutableArray<int> ValueTypeTokens { get; }
        public ImmutableArray<Instruction> Instructions { get; }
        public ImmutableArray<ExceptionHandler> ExceptionHandlers { get; }

        public BytecodeFunction(
            int methodToken,
            ImmutableArray<int> localTypeTokens,
            ImmutableArray<int> valueTypeTokens,
            ImmutableArray<Instruction> instructions,
            ImmutableArray<ExceptionHandler> exceptionHandlers)
        {
            MethodToken = methodToken;
            LocalTypeTokens = localTypeTokens;
            ValueTypeTokens = valueTypeTokens;
            Instructions = instructions;
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
                public readonly Label TryStart;
                public readonly Label TryEnd;
                public readonly Label HandlerStart;
                public readonly Label HandlerEnd;
                public readonly int CatchTypeToken;

                public ExceptionHandlerSpec(Label tryStart, Label tryEnd, Label handlerStart, Label handlerEnd, int catchTypeToken)
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
            private readonly Dictionary<LabelSymbol, Label> _labelsBySymbol;
            private readonly List<ExceptionHandlerSpec> _ehSpecs = new();
            private readonly List<TypeSymbol> _valueTypes = new();
            private readonly Dictionary<SpecialType, TypeSymbol> _knownSpecialTypes = new();
            private int _spillId;

            public Emitter(ITokenProvider tokens, EmitterModule module, MethodSymbol method)
            {
                _tokens = tokens;
                _module = module;
                _method = method;
                _localsBySymbol = new Dictionary<LocalSymbol, int>(ReferenceEqualityComparer<LocalSymbol>.Instance);
                _localTypes = new List<TypeSymbol>();
                _argsBySymbol = BuildArgsMap(method);
                _labelsBySymbol = new Dictionary<LabelSymbol, Label>(ReferenceEqualityComparer<LabelSymbol>.Instance);
            }

            private static Dictionary<ParameterSymbol, int> BuildArgsMap(MethodSymbol method)
            {
                var map = new Dictionary<ParameterSymbol, int>(ReferenceEqualityComparer<ParameterSymbol>.Instance);
                int start = method.IsStatic ? 0 : 1;
                var ps = method.Parameters;
                for (int i = 0; i < ps.Length; i++)
                    map[ps[i]] = start + i;
                return map;
            }

            public BytecodeFunction Emit(BoundMethodBody body)
            {
                CollectLabels(body.Body);
                EmitStatement(body.Body);

                if (!_il.TryGetLastOp(out var lastOp) || lastOp != Op.Ret)
                    EmitImplicitReturn(body);

                var instructions = _il.Bake(out var labelToPc);
                var exceptionHandlers = BakeExceptionHandlers(labelToPc);

                var localTypeTokens = ImmutableArray.CreateBuilder<int>(_localTypes.Count);
                for (int i = 0; i < _localTypes.Count; i++)
                    localTypeTokens.Add(_tokens.GetTypeToken(_localTypes[i]));

                var valueTypeTokens = ImmutableArray.CreateBuilder<int>(_valueTypes.Count);
                for (int i = 0; i < _valueTypes.Count; i++)
                    valueTypeTokens.Add(_tokens.GetTypeToken(_valueTypes[i]));

                int methodToken = _tokens.GetMethodToken(_method);
                return new BytecodeFunction(methodToken, localTypeTokens.ToImmutable(), valueTypeTokens.ToImmutable(), instructions, exceptionHandlers);
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

            private void EmitImplicitReturn(BoundMethodBody body)
            {
                if (IsVoid(body.Method.ReturnType))
                {
                    _il.Emit(Op.Ret);
                    return;
                }

                int value = EmitDefaultValue(body.Method.ReturnType);
                _il.Emit(Op.Ret, Instruction.NoValue, value);
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
                    case BoundLocalFunctionStatement:
                        break;
                }
            }

            private Label GetOrCreateLabel(LabelSymbol label)
            {
                if (_labelsBySymbol.TryGetValue(label, out var flat))
                    return flat;

                flat = _il.DefineLabel();
                _labelsBySymbol.Add(label, flat);
                return flat;
            }

            private int NewValue(TypeSymbol type)
            {
                RegisterKnownType(type);
                int id = _valueTypes.Count;
                _valueTypes.Add(type);
                return id;
            }

            private int NewByRefValue(TypeSymbol elementType)
                => NewValue(new ByRefTypeSymbol(elementType));

            private void RegisterKnownType(TypeSymbol type)
            {
                if (type.SpecialType != SpecialType.None)
                    _knownSpecialTypes.TryAdd(type.SpecialType, type);

                if (type is ByRefTypeSymbol br)
                    RegisterKnownType(br.ElementType);
                else if (type is PointerTypeSymbol ptr)
                    RegisterKnownType(ptr.PointedAtType);
                else if (type is ArrayTypeSymbol arr)
                    RegisterKnownType(arr.ElementType);
                else if (type is NamedTypeSymbol nt)
                {
                    if (nt.TypeKind == TypeKind.Enum && nt.EnumUnderlyingType is not null)
                        RegisterKnownType(nt.EnumUnderlyingType);

                    var args = nt.TypeArguments;
                    for (int i = 0; i < args.Length; i++)
                        RegisterKnownType(args[i]);
                }
            }

            private TypeSymbol GetSyntheticInt32Type(TypeSymbol fallback)
                => GetKnownSystemTypeOrFallback(fallback, "Int32", SpecialType.System_Int32);
            private TypeSymbol GetSyntheticNativeIntType(TypeSymbol fallback)
                => GetKnownSystemTypeOrFallback(fallback, "IntPtr", SpecialType.System_IntPtr);
            private TypeSymbol GetSyntheticNativeUIntType(TypeSymbol fallback)
                => GetKnownSystemTypeOrFallback(fallback, "UIntPtr", SpecialType.System_UIntPtr);
            private TypeSymbol GetSyntheticObjectType(TypeSymbol fallback)
                => GetKnownSystemTypeOrFallback(fallback, "Object", SpecialType.System_Object);
            private TypeSymbol GetKnownSystemTypeOrFallback(TypeSymbol fallback, string name, SpecialType specialType)
            {
                if (_knownSpecialTypes.TryGetValue(specialType, out var known))
                    return known;

                if (TryFindSystemType(fallback, name, specialType, out var found) ||
                    TryFindSystemType(_method, name, specialType, out found))
                {
                    RegisterKnownType(found);
                    return found;
                }

                return fallback;
            }
            private static bool TryFindSystemType(Symbol anchor, string name, SpecialType specialType, out TypeSymbol type)
            {
                Symbol? s = anchor;
                while (s is not null && s is not NamespaceSymbol { IsGlobalNamespace: true })
                    s = s.ContainingSymbol;

                if (s is NamespaceSymbol root)
                {
                    var namespaces = root.GetNamespaceMembers();
                    for (int i = 0; i < namespaces.Length; i++)
                    {
                        if (!string.Equals(namespaces[i].Name, "System", StringComparison.Ordinal))
                            continue;

                        var types = namespaces[i].GetTypeMembers(name, 0);
                        for (int j = 0; j < types.Length; j++)
                        {
                            if (types[j].SpecialType == specialType)
                            {
                                type = types[j];
                                return true;
                            }
                        }
                    }
                }

                type = null!;
                return false;
            }

            private int GetOrCreateLocal(LocalSymbol local)
            {
                if (_localsBySymbol.TryGetValue(local, out var idx))
                    return idx;

                idx = _localTypes.Count;
                _localsBySymbol.Add(local, idx);
                TypeSymbol storageType = local.IsByRef ? new ByRefTypeSymbol(local.Type) : local.Type;
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
                if (type is NamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: not null } nt)
                    return GetElementSizeOrThrow(nt.EnumUnderlyingType);

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
                        ? Cnidaria.Cs.Stack.RuntimeTypeSystem.PointerSize
                        : throw new NotSupportedException($"No known size for '{type.Name}'.")
                };
            }

            private static bool IsAddressableValueTypeReceiver(BoundExpression receiver)
                => receiver.IsLValue || receiver is BoundThisExpression;

            private int EmitBoxedReference(int value, TypeSymbol valueType, TypeSymbol referenceFallback)
            {
                TypeSymbol resultType = referenceFallback.IsReferenceType
                    ? referenceFallback
                    : GetSyntheticObjectType(valueType);
                int boxed = NewValue(resultType);
                _il.Emit(Op.Box, boxed, value, operand0: _tokens.GetTypeToken(valueType));
                return boxed;
            }

            private TypeSymbol GetIsInstResultType(TypeSymbol targetType, TypeSymbol fallback)
                => targetType.IsReferenceType ? targetType : GetSyntheticObjectType(fallback);

            private static TypeSymbol GetRawCallResultType(BoundCallExpression call)
                => call.Method.ReturnType;

            private int EmitReinterpretValue(int value, TypeSymbol targetType)
            {
                int result = NewValue(targetType);
                _il.Emit(Op.Move, result, value);
                return result;
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
                        _il.EmitBranch(Op.Br, GetOrCreateLabel(gs.TargetLabel));
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
                        throw new NotSupportedException($"Statement '{s.GetType().Name}' is not supported by flat bytecode emitter.");
                }
            }

            private void EmitThrow(BoundThrowStatement ts)
            {
                if (ts.ExpressionOpt is null)
                {
                    _il.Emit(Op.Rethrow);
                    return;
                }

                int ex = EmitExpressionValue(ts.ExpressionOpt);
                _il.Emit(Op.Throw, Instruction.NoValue, ex);
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
                    var tryStart = _il.DefineLabel();
                    var finallyStart = _il.DefineLabel();
                    var after = _il.DefineLabel();

                    _il.MarkLabel(tryStart);
                    EmitStatement(t.TryBlock);

                    bool tryFallsThrough = !_il.TryGetLastOp(out var lastTryOp) ||
                        (lastTryOp != Op.Ret && lastTryOp != Op.Throw && lastTryOp != Op.Rethrow);

                    if (tryFallsThrough)
                        _il.EmitBranch(Op.Br, after);

                    _il.MarkLabel(finallyStart);
                    EmitStatement(t.FinallyBlockOpt!);
                    _il.Emit(Op.Endfinally);
                    _il.MarkLabel(after);
                    _ehSpecs.Add(new ExceptionHandlerSpec(tryStart, finallyStart, finallyStart, after, FinallyCatchTypeToken));
                    return;
                }

                if (hasCatches && !hasFinally)
                {
                    var tryStart = _il.DefineLabel();
                    var tryEnd = _il.DefineLabel();
                    var endLabel = _il.DefineLabel();
                    int n = t.CatchBlocks.Length;
                    var handlerStarts = new Label[n];
                    for (int i = 0; i < n; i++)
                        handlerStarts[i] = _il.DefineLabel();

                    _il.MarkLabel(tryStart);
                    EmitStatement(t.TryBlock);

                    bool tryFallsThrough = !_il.TryGetLastOp(out var lastTryOp) ||
                        (lastTryOp != Op.Ret && lastTryOp != Op.Throw && lastTryOp != Op.Rethrow);

                    _il.MarkLabel(tryEnd);
                    if (tryFallsThrough)
                        _il.EmitBranch(Op.Br, endLabel);

                    for (int i = 0; i < n; i++)
                    {
                        var c = t.CatchBlocks[i];
                        var handlerStart = handlerStarts[i];
                        var handlerEnd = (i + 1 < n) ? handlerStarts[i + 1] : endLabel;

                        _il.MarkLabel(handlerStart);

                        if (c.ExceptionLocalOpt is not null)
                        {
                            int loc = GetOrCreateLocal(c.ExceptionLocalOpt);
                            int ex = NewValue(c.ExceptionLocalOpt.Type);
                            _il.Emit(Op.Ldexception, ex);
                            _il.Emit(Op.Stloc, Instruction.NoValue, ex, operand0: loc);
                        }

                        EmitStatement(c.Body);

                        if (!_il.TryGetLastOp(out var lastOp) ||
                            (lastOp != Op.Ret && lastOp != Op.Throw && lastOp != Op.Rethrow))
                        {
                            _il.EmitBranch(Op.Br, endLabel);
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
                    var handlerStarts = new Label[n];
                    for (int i = 0; i < n; i++)
                        handlerStarts[i] = _il.DefineLabel();

                    _il.MarkLabel(tryStart);
                    EmitStatement(t.TryBlock);

                    bool tryFallsThrough = !_il.TryGetLastOp(out var lastTryOp) ||
                        (lastTryOp != Op.Ret && lastTryOp != Op.Throw && lastTryOp != Op.Rethrow);

                    _il.MarkLabel(tryEnd);
                    if (tryFallsThrough)
                        _il.EmitBranch(Op.Br, afterCatches);

                    for (int i = 0; i < n; i++)
                    {
                        var c = t.CatchBlocks[i];
                        var handlerStart = handlerStarts[i];
                        var handlerEnd = (i + 1 < n) ? handlerStarts[i + 1] : afterCatches;

                        _il.MarkLabel(handlerStart);

                        if (c.ExceptionLocalOpt is not null)
                        {
                            int loc = GetOrCreateLocal(c.ExceptionLocalOpt);
                            int ex = NewValue(c.ExceptionLocalOpt.Type);
                            _il.Emit(Op.Ldexception, ex);
                            _il.Emit(Op.Stloc, Instruction.NoValue, ex, operand0: loc);
                        }

                        EmitStatement(c.Body);

                        if (!_il.TryGetLastOp(out var lastOp) ||
                            (lastOp != Op.Ret && lastOp != Op.Throw && lastOp != Op.Rethrow))
                        {
                            _il.EmitBranch(Op.Br, afterCatches);
                        }

                        int catchTypeTok = _tokens.GetTypeToken(c.ExceptionType);
                        _ehSpecs.Add(new ExceptionHandlerSpec(tryStart, tryEnd, handlerStart, handlerEnd, catchTypeTok));
                    }

                    _il.MarkLabel(afterCatches);
                    _il.EmitBranch(Op.Br, after);

                    _il.MarkLabel(finallyStart);
                    EmitStatement(t.FinallyBlockOpt!);
                    _il.Emit(Op.Endfinally);
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

                    int init = EmitExpressionValue(ld.Initializer);
                    _il.Emit(Op.Stloc, Instruction.NoValue, init, operand0: localIndex);
                    return;
                }

                int value = ld.Initializer is null
                    ? EmitDefaultValue(ld.Local.Type)
                    : EmitExpressionValue(ld.Initializer);

                _il.Emit(Op.Stloc, Instruction.NoValue, value, operand0: localIndex);
            }

            private void EmitReturn(BoundReturnStatement ret)
            {
                if (ret.Expression is null)
                {
                    _il.Emit(Op.Ret);
                    return;
                }

                int value = EmitExpressionValue(ret.Expression);
                _il.Emit(Op.Ret, Instruction.NoValue, value);
            }

            private void EmitConditionalGoto(BoundConditionalGotoStatement cgs)
            {
                int condition = EmitExpressionValue(cgs.Condition);
                var label = GetOrCreateLabel(cgs.TargetLabel);
                _il.EmitBranch(cgs.JumpIfTrue ? Op.Brtrue : Op.Brfalse, label, condition);
            }

            private int EmitExpressionValue(BoundExpression e)
            {
                int value = EmitExpression(e, EmitMode.Value);
                if (value < 0)
                    throw new InvalidOperationException($"Expression '{e.GetType().Name}' did not produce a value.");
                return value;
            }

            private int EmitExpression(BoundExpression e, EmitMode mode)
            {
                switch (e)
                {
                    case BoundLiteralExpression lit:
                        return EmitLiteral(lit, mode);
                    case BoundLocalExpression loc:
                        return EmitLocal(loc, mode);
                    case BoundParameterExpression par:
                        return EmitParameter(par, mode);
                    case BoundThisExpression:
                        return EmitThis(e.Type, mode);
                    case BoundBaseExpression:
                        return EmitThis(e.Type, mode);
                    case BoundUnaryExpression un:
                        return EmitUnary(un, mode);
                    case BoundBinaryExpression bin:
                        return EmitBinary(bin, mode);
                    case BoundConditionalExpression c:
                        return EmitConditionalExpression(c, mode);
                    case BoundAssignmentExpression ass:
                        return EmitAssignment(ass, mode);
                    case BoundCallExpression call:
                        if (mode == EmitMode.Value && call.Method.ReturnType is ByRefTypeSymbol br)
                        {
                            int addr = EmitCall(call, EmitMode.Value);
                            int result = NewValue(br.ElementType);
                            _il.Emit(Op.Ldobj, result, addr, operand0: _tokens.GetTypeToken(br.ElementType));
                            return result;
                        }
                        return EmitCall(call, mode);
                    case BoundObjectCreationExpression obj:
                        return EmitObjectCreation(obj, mode);
                    case BoundConversionExpression conv:
                        return EmitConversion(conv, mode);
                    case BoundThrowExpression tex:
                        {
                            int ex = EmitExpressionValue(tex.Exception);
                            _il.Emit(Op.Throw, Instruction.NoValue, ex);
                            return Instruction.NoValue;
                        }
                    case BoundAsExpression @as:
                        return EmitAs(@as, mode);
                    case BoundIsPatternExpression isPattern:
                        return EmitIsPattern(isPattern, mode);
                    case BoundSequenceExpression seq:
                        return EmitSequence(seq, mode);
                    case BoundRefExpression re:
                        return EmitRefExpression(re, mode);
                    case BoundAddressOfExpression addrof:
                        return EmitAddressOf(addrof, mode);
                    case BoundPointerIndirectionExpression pind:
                        return EmitPointerIndirection(pind, mode);
                    case BoundPointerElementAccessExpression pea:
                        return EmitPointerElementAccess(pea, mode);
                    case BoundStackAllocArrayCreationExpression sa:
                        return EmitStackAlloc(sa, mode);
                    case BoundArrayCreationExpression ac:
                        return EmitArrayCreation(ac, mode);
                    case BoundArrayElementAccessExpression aea:
                        return EmitArrayElementAccess(aea, mode);
                    case BoundMemberAccessExpression ma:
                        return EmitMemberAccess(ma, mode);
                    case BoundSizeOfExpression so:
                        return EmitSizeOf(so, mode);
                    default:
                        throw new NotSupportedException($"Expression '{e.GetType().Name}' is not supported by flat bytecode emitter.");
                }
            }

            private int EmitThis(TypeSymbol type, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                if (type.IsValueType)
                {
                    int addr = EmitThisAddress(type);
                    int result = NewValue(type);
                    _il.Emit(Op.Ldobj, result, addr, operand0: _tokens.GetTypeToken(type));
                    return result;
                }

                int reference = NewValue(type);
                _il.Emit(Op.Ldthis, reference);
                return reference;
            }

            private int EmitThisAddress(TypeSymbol type)
            {
                if (_method.IsStatic)
                    throw new InvalidOperationException("Cannot load 'this' in a static method.");

                if (!type.IsValueType)
                    return EmitThis(type, EmitMode.Value);

                int addr = NewByRefValue(type);
                _il.Emit(Op.Ldthis, addr);
                return addr;
            }

            private int EmitConstantValue(TypeSymbol type, object? value, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                int result = NewValue(type);
                if (value is null)
                {
                    _il.Emit(Op.Ldnull, result);
                    return result;
                }

                switch (value)
                {
                    case bool b:
                        _il.Emit(Op.Ldc_I4, result, operand0: b ? 1 : 0);
                        return result;
                    case char ch:
                        _il.Emit(Op.Ldc_I4, result, operand0: ch);
                        return result;
                    case sbyte sb:
                        _il.Emit(Op.Ldc_I4, result, operand0: sb);
                        return result;
                    case byte bb:
                        _il.Emit(Op.Ldc_I4, result, operand0: bb);
                        return result;
                    case short s:
                        _il.Emit(Op.Ldc_I4, result, operand0: s);
                        return result;
                    case ushort us:
                        _il.Emit(Op.Ldc_I4, result, operand0: us);
                        return result;
                    case int i:
                        _il.Emit(Op.Ldc_I4, result, operand0: i);
                        return result;
                    case uint ui:
                        unchecked { _il.Emit(Op.Ldc_I4, result, operand0: (int)ui); }
                        return result;
                    case long l:
                        _il.Emit(Op.Ldc_I8, result, operand2: l);
                        return result;
                    case ulong ul:
                        unchecked { _il.Emit(Op.Ldc_I8, result, operand2: (long)ul); }
                        return result;
                    case float f:
                        _il.Emit(Op.Ldc_R8, result, operand2: BitConverter.DoubleToInt64Bits(f));
                        return result;
                    case double d:
                        _il.Emit(Op.Ldc_R8, result, operand2: BitConverter.DoubleToInt64Bits(d));
                        return result;
                    case string str:
                        _il.Emit(Op.Ldstr, result, operand0: _tokens.GetUserStringToken(str));
                        return result;
                    default:
                        throw new NotSupportedException($"Constant of type '{value.GetType().Name}' is not supported.");
                }
            }

            private int EmitLiteral(BoundLiteralExpression lit, EmitMode mode)
                => EmitConstantValue(lit.Type, lit.Value, mode);

            private int EmitMemberAccess(BoundMemberAccessExpression ma, EmitMode mode)
            {
                if (ma.ConstantValueOpt.HasValue)
                    return EmitConstantValue(ma.Type, ma.ConstantValueOpt.Value, mode);

                if (ma.Member is not FieldSymbol fs)
                    throw new NotSupportedException("BoundMemberAccessExpression must be lowered to FieldSymbol access before emission.");

                if (fs.IsConst && !fs.ConstantValueOpt.HasValue)
                    throw new InvalidOperationException($"Const field '{fs.Name}' has no constant value in metadata.");

                if (fs.IsConst && fs.ConstantValueOpt.HasValue)
                    return EmitConstantValue(fs.Type, fs.ConstantValueOpt.Value, mode);

                int tok = _tokens.GetFieldToken(fs);

                if (fs.IsStatic)
                {
                    int result = mode == EmitMode.Value ? NewValue(ma.Type) : Instruction.NoValue;
                    _il.Emit(Op.Ldsfld, result, operand0: tok);
                    return result;
                }

                if (ma.ReceiverOpt is null)
                    throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                int receiver;
                if (ma.ReceiverOpt.Type.IsValueType)
                {
                    receiver = IsAddressableValueTypeReceiver(ma.ReceiverOpt)
                        ? EmitLoadAddressOfLValue(ma.ReceiverOpt)
                        : SpillAddressableReceiver(ma.ReceiverOpt);
                }
                else
                {
                    receiver = EmitExpressionValue(ma.ReceiverOpt);
                }

                int value = mode == EmitMode.Value ? NewValue(ma.Type) : Instruction.NoValue;
                _il.Emit(Op.Ldfld, value, receiver, operand0: tok);
                return value;
            }

            private int EmitLocal(BoundLocalExpression loc, EmitMode mode)
            {
                int idx = GetOrCreateLocal(loc.Local);
                if (mode != EmitMode.Value)
                    return Instruction.NoValue;

                if (loc.Local.IsByRef)
                {
                    int addr = NewByRefValue(loc.Local.Type);
                    _il.Emit(Op.Ldloc, addr, operand0: idx);
                    int value = NewValue(loc.Local.Type);
                    _il.Emit(Op.Ldobj, value, addr, operand0: _tokens.GetTypeToken(loc.Local.Type));
                    return value;
                }

                int result = NewValue(loc.Local.Type);
                _il.Emit(Op.Ldloc, result, operand0: idx);
                return result;
            }

            private int EmitParameter(BoundParameterExpression par, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                int idx = GetArgIndex(par.Parameter);
                if (par.Parameter.Type is ByRefTypeSymbol br)
                {
                    int addr = NewValue(par.Parameter.Type);
                    _il.Emit(Op.Ldarg, addr, operand0: idx);
                    int value = NewValue(br.ElementType);
                    _il.Emit(Op.Ldobj, value, addr, operand0: _tokens.GetTypeToken(br.ElementType));
                    return value;
                }

                int result = NewValue(par.Parameter.Type);
                _il.Emit(Op.Ldarg, result, operand0: idx);
                return result;
            }

            private int EmitUnary(BoundUnaryExpression un, EmitMode mode)
            {
                int operand = EmitExpression(un.Operand, mode == EmitMode.Value ? EmitMode.Value : EmitMode.Discard);
                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                switch (un.OperatorKind)
                {
                    case BoundUnaryOperatorKind.UnaryPlus:
                        return operand;
                    case BoundUnaryOperatorKind.UnaryMinus:
                        {
                            int result = NewValue(un.Type);
                            _il.Emit(Op.Neg, result, operand);
                            return result;
                        }
                    case BoundUnaryOperatorKind.LogicalNot:
                        return EmitBoolNot(operand, un.Type);
                    case BoundUnaryOperatorKind.BitwiseNot:
                        {
                            int result = NewValue(un.Type);
                            _il.Emit(Op.Not, result, operand);
                            return result;
                        }
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

            private int EmitBinary(BoundBinaryExpression bin, EmitMode mode)
            {
                if (bin.OperatorKind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr)
                    return EmitShortCircuitLogical(bin, mode);

                int left = EmitExpressionValue(bin.Left);
                int right = EmitExpressionValue(bin.Right);

                int result = mode == EmitMode.Value ? NewValue(bin.Type) : Instruction.NoValue;
                EmitBinaryOperator(bin, result, left, right);
                return result;
            }

            private void EmitBinaryOperator(BoundBinaryExpression bin, int result, int left, int right)
            {
                var op = bin.OperatorKind;

                if (op == BoundBinaryOperatorKind.Subtract &&
                    bin.Left.Type is PointerTypeSymbol lpt &&
                    bin.Right.Type is PointerTypeSymbol rpt)
                {
                    if (!ReferenceEquals(lpt, rpt))
                        throw new NotSupportedException("Pointer subtraction requires both operands to have the same pointer type.");

                    _il.Emit2(Op.PtrDiff, result, left, right, operand0: GetElementSizeOrThrow(lpt.PointedAtType));
                    return;
                }

                if (bin.Type is PointerTypeSymbol ptrType &&
                    op is BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Subtract)
                {
                    if (bin.Left.Type is not PointerTypeSymbol)
                        throw new NotSupportedException("Pointer arithmetic expects the pointer operand on the left.");

                    if (op == BoundBinaryOperatorKind.Subtract)
                    {
                        int neg = NewValue(bin.Right.Type);
                        _il.Emit(Op.Neg, neg, right);
                        right = neg;
                    }

                    _il.Emit2(Op.PtrElemAddr, result, left, right, operand0: GetElementSizeOrThrow(ptrType.PointedAtType));
                    return;
                }

                bool u = UsesUnsignedIntegerSemantics(bin.Left.Type);
                switch (op)
                {
                    case BoundBinaryOperatorKind.Add:
                        _il.Emit2(Op.Add, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.Subtract:
                        _il.Emit2(Op.Sub, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.Multiply:
                        _il.Emit2(Op.Mul, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.Divide:
                        _il.Emit2(u ? Op.Div_Un : Op.Div, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.Modulo:
                        _il.Emit2(u ? Op.Rem_Un : Op.Rem, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.BitwiseAnd:
                        _il.Emit2(Op.And, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.BitwiseOr:
                        _il.Emit2(Op.Or, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.ExclusiveOr:
                        _il.Emit2(Op.Xor, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.LeftShift:
                        _il.Emit2(Op.Shl, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.RightShift:
                        _il.Emit2(Op.Shr, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.UnsignedRightShift:
                        _il.Emit2(Op.Shr_Un, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.Equals:
                        _il.Emit2(Op.Ceq, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.NotEquals:
                        {
                            int eq = NewValue(bin.Type);
                            _il.Emit2(Op.Ceq, eq, left, right);
                            int not = EmitBoolNot(eq, bin.Type);
                            if (result >= 0)
                                _il.Emit(Op.Move, result, not);
                            return;
                        }
                    case BoundBinaryOperatorKind.LessThan:
                        _il.Emit2(u ? Op.Clt_Un : Op.Clt, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.GreaterThan:
                        _il.Emit2(u ? Op.Cgt_Un : Op.Cgt, result, left, right);
                        return;
                    case BoundBinaryOperatorKind.LessThanOrEqual:
                        {
                            int gt = NewValue(bin.Type);
                            _il.Emit2(u ? Op.Cgt_Un : Op.Cgt, gt, left, right);
                            int not = EmitBoolNot(gt, bin.Type);
                            if (result >= 0)
                                _il.Emit(Op.Move, result, not);
                            return;
                        }
                    case BoundBinaryOperatorKind.GreaterThanOrEqual:
                        {
                            int lt = NewValue(bin.Type);
                            _il.Emit2(u ? Op.Clt_Un : Op.Clt, lt, left, right);
                            int not = EmitBoolNot(lt, bin.Type);
                            if (result >= 0)
                                _il.Emit(Op.Move, result, not);
                            return;
                        }
                    default:
                        throw new NotSupportedException($"Binary operator '{op}' is not supported.");
                }
            }

            private int EmitShortCircuitLogical(BoundBinaryExpression bin, EmitMode mode)
            {
                if (!IsBool(bin.Type))
                    throw new InvalidOperationException($"Logical operator expected bool result, got '{bin.Type.Name}'.");

                if (mode == EmitMode.Discard)
                {
                    var skipLabel = _il.DefineLabel();
                    int left = EmitExpressionValue(bin.Left);
                    _il.EmitBranch(bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd ? Op.Brfalse : Op.Brtrue, skipLabel, left);
                    EmitExpression(bin.Right, EmitMode.Discard);
                    _il.MarkLabel(skipLabel);
                    return Instruction.NoValue;
                }

                var lFalse = _il.DefineLabel();
                var lEnd = _il.DefineLabel();
                int spill = AllocateSpillLocal(bin.Type);

                int leftValue = EmitExpressionValue(bin.Left);
                _il.EmitBranch(bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd ? Op.Brfalse : Op.Brtrue, lFalse, leftValue);

                int rightValue = EmitExpressionValue(bin.Right);
                _il.EmitBranch(bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd ? Op.Brfalse : Op.Brtrue, lFalse, rightValue);

                int trueValue = EmitConstantValue(bin.Type, bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd, EmitMode.Value);
                _il.Emit(Op.Stloc, Instruction.NoValue, trueValue, operand0: spill);
                _il.EmitBranch(Op.Br, lEnd);

                _il.MarkLabel(lFalse);
                int falseValue = EmitConstantValue(bin.Type, bin.OperatorKind != BoundBinaryOperatorKind.LogicalAnd, EmitMode.Value);
                _il.Emit(Op.Stloc, Instruction.NoValue, falseValue, operand0: spill);

                _il.MarkLabel(lEnd);
                int result = NewValue(bin.Type);
                _il.Emit(Op.Ldloc, result, operand0: spill);
                return result;
            }

            private int EmitConditionalExpression(BoundConditionalExpression c, EmitMode mode)
            {
                var lElse = _il.DefineLabel();
                var lEnd = _il.DefineLabel();

                int condition = EmitExpressionValue(c.Condition);
                _il.EmitBranch(Op.Brfalse, lElse, condition);

                if (mode == EmitMode.Discard)
                {
                    EmitExpression(c.WhenTrue, EmitMode.Discard);
                    _il.EmitBranch(Op.Br, lEnd);
                    _il.MarkLabel(lElse);
                    EmitExpression(c.WhenFalse, EmitMode.Discard);
                    _il.MarkLabel(lEnd);
                    return Instruction.NoValue;
                }

                int spill = AllocateSpillLocal(c.Type);
                int whenTrue = EmitExpressionValue(c.WhenTrue);
                _il.Emit(Op.Stloc, Instruction.NoValue, whenTrue, operand0: spill);
                _il.EmitBranch(Op.Br, lEnd);

                _il.MarkLabel(lElse);
                int whenFalse = EmitExpressionValue(c.WhenFalse);
                _il.Emit(Op.Stloc, Instruction.NoValue, whenFalse, operand0: spill);

                _il.MarkLabel(lEnd);
                int result = NewValue(c.Type);
                _il.Emit(Op.Ldloc, result, operand0: spill);
                return result;
            }

            private int EmitAssignment(BoundAssignmentExpression assignment, EmitMode mode)
            {
                if (assignment.HasErrors || assignment.Left.HasErrors || assignment.Right.HasErrors)
                {
                    return mode == EmitMode.Value
                        ? EmitConstantValue(assignment.Type, 0, EmitMode.Value)
                        : Instruction.NoValue;
                }

                int value;
                switch (assignment.Left)
                {
                    case BoundArrayElementAccessExpression aea:
                        {
                            if (aea.Indices.Length != 1)
                                throw new NotSupportedException("Only single dimensional arrays are supported.");

                            int array = EmitExpressionValue(aea.Expression);
                            int index = EmitExpressionValue(aea.Indices[0]);
                            value = EmitExpressionValue(assignment.Right);
                            _il.Emit3(Op.Stelem, Instruction.NoValue, array, index, value, operand0: _tokens.GetTypeToken(aea.Type));
                            return mode == EmitMode.Value ? value : Instruction.NoValue;
                        }

                    case BoundMemberAccessExpression { Member: FieldSymbol fs } leftField:
                        {
                            if (fs.IsConst)
                                throw new NotSupportedException("Cannot assign to a const field in lowered form.");

                            int fieldTok = _tokens.GetFieldToken(fs);
                            if (fs.IsStatic)
                            {
                                value = EmitExpressionValue(assignment.Right);
                                _il.Emit(Op.Stsfld, Instruction.NoValue, value, operand0: fieldTok);
                                return mode == EmitMode.Value ? value : Instruction.NoValue;
                            }

                            if (leftField.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                            int receiver;
                            if (leftField.ReceiverOpt.Type.IsValueType)
                            {
                                if (!IsAddressableValueTypeReceiver(leftField.ReceiverOpt))
                                    throw new InvalidOperationException("Cannot assign to a field of a non-lvalue value-type receiver.");

                                receiver = EmitLoadAddressOfLValue(leftField.ReceiverOpt);
                            }
                            else
                            {
                                receiver = EmitExpressionValue(leftField.ReceiverOpt);
                            }

                            value = EmitExpressionValue(assignment.Right);
                            _il.Emit2(Op.Stfld, Instruction.NoValue, receiver, value, operand0: fieldTok);
                            return mode == EmitMode.Value ? value : Instruction.NoValue;
                        }

                    case BoundLocalExpression local:
                        {
                            int localIndex = GetOrCreateLocal(local.Local);
                            if (local.Local.IsByRef)
                            {
                                int addr = NewByRefValue(local.Local.Type);
                                _il.Emit(Op.Ldloc, addr, operand0: localIndex);
                                value = EmitExpressionValue(assignment.Right);
                                _il.Emit2(Op.Stobj, Instruction.NoValue, addr, value, operand0: _tokens.GetTypeToken(local.Local.Type));
                                return mode == EmitMode.Value ? value : Instruction.NoValue;
                            }

                            value = EmitExpressionValue(assignment.Right);
                            _il.Emit(Op.Stloc, Instruction.NoValue, value, operand0: localIndex);
                            return mode == EmitMode.Value ? value : Instruction.NoValue;
                        }

                    case BoundParameterExpression par:
                        {
                            int argIndex = GetArgIndex(par.Parameter);
                            if (par.Parameter.Type is ByRefTypeSymbol byRefPar)
                            {
                                int addr = NewValue(par.Parameter.Type);
                                _il.Emit(Op.Ldarg, addr, operand0: argIndex);
                                value = EmitExpressionValue(assignment.Right);
                                _il.Emit2(Op.Stobj, Instruction.NoValue, addr, value, operand0: _tokens.GetTypeToken(byRefPar.ElementType));
                                return mode == EmitMode.Value ? value : Instruction.NoValue;
                            }

                            value = EmitExpressionValue(assignment.Right);
                            _il.Emit(Op.Starg, Instruction.NoValue, value, operand0: argIndex);
                            return mode == EmitMode.Value ? value : Instruction.NoValue;
                        }

                    default:
                        {
                            int addr = EmitLoadAddressOfLValue(assignment.Left);
                            value = EmitExpressionValue(assignment.Right);
                            _il.Emit2(Op.Stobj, Instruction.NoValue, addr, value, operand0: _tokens.GetTypeToken(assignment.Left.Type));
                            return mode == EmitMode.Value ? value : Instruction.NoValue;
                        }
                }
            }

            private void EmitStoreToLValue(BoundExpression left, int value)
            {
                switch (left)
                {
                    case BoundArrayElementAccessExpression aea:
                        if (aea.Indices.Length != 1)
                            throw new NotSupportedException("Only single dimensional arrays are supported.");

                        int array = EmitExpressionValue(aea.Expression);
                        int index = EmitExpressionValue(aea.Indices[0]);
                        _il.Emit3(Op.Stelem, Instruction.NoValue, array, index, value, operand0: _tokens.GetTypeToken(aea.Type));
                        return;

                    case BoundMemberAccessExpression { Member: FieldSymbol fs } leftField:
                        if (fs.IsConst)
                            throw new NotSupportedException("Cannot assign to a const field in lowered form.");

                        int fieldTok = _tokens.GetFieldToken(fs);
                        if (fs.IsStatic)
                        {
                            _il.Emit(Op.Stsfld, Instruction.NoValue, value, operand0: fieldTok);
                            return;
                        }

                        if (leftField.ReceiverOpt is null)
                            throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                        int receiver = leftField.ReceiverOpt.Type.IsValueType
                            ? EmitLoadAddressOfLValue(leftField.ReceiverOpt)
                            : EmitExpressionValue(leftField.ReceiverOpt);

                        _il.Emit2(Op.Stfld, Instruction.NoValue, receiver, value, operand0: fieldTok);
                        return;

                    case BoundLocalExpression local:
                        int localIndex = GetOrCreateLocal(local.Local);
                        if (local.Local.IsByRef)
                        {
                            int addr = NewByRefValue(local.Local.Type);
                            _il.Emit(Op.Ldloc, addr, operand0: localIndex);
                            _il.Emit2(Op.Stobj, Instruction.NoValue, addr, value, operand0: _tokens.GetTypeToken(local.Local.Type));
                            return;
                        }

                        _il.Emit(Op.Stloc, Instruction.NoValue, value, operand0: localIndex);
                        return;

                    case BoundParameterExpression par:
                        int argIndex = GetArgIndex(par.Parameter);
                        if (par.Parameter.Type is ByRefTypeSymbol byRefPar)
                        {
                            int addr = NewValue(par.Parameter.Type);
                            _il.Emit(Op.Ldarg, addr, operand0: argIndex);
                            _il.Emit2(Op.Stobj, Instruction.NoValue, addr, value, operand0: _tokens.GetTypeToken(byRefPar.ElementType));
                            return;
                        }

                        _il.Emit(Op.Starg, Instruction.NoValue, value, operand0: argIndex);
                        return;

                    default:
                        int laddr = EmitLoadAddressOfLValue(left);
                        _il.Emit2(Op.Stobj, Instruction.NoValue, laddr, value, operand0: _tokens.GetTypeToken(left.Type));
                        return;
                }
            }

            private int EmitArrayCreation(BoundArrayCreationExpression ac, EmitMode mode)
            {
                if (ac.Type is not ArrayTypeSymbol at)
                    throw new InvalidOperationException("BoundArrayCreationExpression.Type is not an array type.");

                if (at.Rank != 1 || ac.DimensionSizes.Length > 1)
                    throw new NotSupportedException("Only single dimensional arrays are supported.");

                int length;
                if (ac.DimensionSizes.Length == 0)
                    length = EmitConstantValue(GetSyntheticInt32Type(ac.Type), ac.InitializerOpt?.Elements.Length ?? 0, EmitMode.Value);
                else
                    length = EmitExpressionValue(ac.DimensionSizes[0]);

                int elemTok = _tokens.GetTypeToken(ac.ElementType);
                int array = NewValue(ac.Type);
                _il.Emit(Op.Newarr, array, length, operand0: elemTok);

                if (ac.InitializerOpt is not null)
                {
                    var elems = ac.InitializerOpt.Elements;
                    for (int i = 0; i < elems.Length; i++)
                    {
                        int index = EmitConstantValue(GetSyntheticInt32Type(elems[i].Type), i, EmitMode.Value);
                        int value = EmitExpressionValue(elems[i]);
                        _il.Emit3(Op.Stelem, Instruction.NoValue, array, index, value, operand0: elemTok);
                    }
                }

                return mode == EmitMode.Value ? array : Instruction.NoValue;
            }

            private int EmitArrayElementAccess(BoundArrayElementAccessExpression aea, EmitMode mode)
            {
                if (aea.Indices.Length != 1)
                    throw new NotSupportedException("Only single dimensional arrays are supported.");

                int array = EmitExpressionValue(aea.Expression);
                int index = EmitExpressionValue(aea.Indices[0]);

                int result = mode == EmitMode.Value ? NewValue(aea.Type) : Instruction.NoValue;
                _il.Emit2(Op.Ldelem, result, array, index, operand0: _tokens.GetTypeToken(aea.Type));
                return result;
            }

            private int EmitCall(BoundCallExpression call, EmitMode mode)
            {
                if (TryEmitIntrinsic(call, mode, out var intrinsicResult))
                    return intrinsicResult;

                int argCount = 0;
                int hasThis = call.Method.IsStatic ? 0 : 1;
                bool thisIsManagedByRef = false;
                var values = ImmutableArray.CreateBuilder<int>();

                if (!call.Method.IsStatic)
                {
                    int receiver;
                    if (call.ReceiverOpt is null)
                    {
                        if (_method.IsStatic)
                            throw new InvalidOperationException($"Instance call to '{call.Method.Name}' without receiver (in static method).");

                        var thisType = _method.ContainingSymbol as NamedTypeSymbol
                            ?? throw new InvalidOperationException("Cannot resolve implicit 'this' receiver type.");

                        if (thisType.IsValueType)
                        {
                            bool methodDeclaredOnRefType =
                                call.Method.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;

                            if (methodDeclaredOnRefType)
                            {
                                int thisValue = EmitThis(thisType, EmitMode.Value);
                                TypeSymbol receiverRefType = call.Method.ContainingSymbol is NamedTypeSymbol refDecl && refDecl.IsReferenceType
                                    ? refDecl
                                    : GetSyntheticObjectType(thisType);
                                receiver = EmitBoxedReference(thisValue, thisType, receiverRefType);
                            }
                            else
                            {
                                receiver = EmitThisAddress(thisType);
                                thisIsManagedByRef = true;
                            }
                        }
                        else
                        {
                            receiver = EmitThis(thisType, EmitMode.Value);
                        }
                    }
                    else
                    {
                        var recv = call.ReceiverOpt;
                        if (recv.Type.IsValueType)
                        {
                            bool methodDeclaredOnRefType =
                                call.Method.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;

                            if (methodDeclaredOnRefType)
                            {
                                int recvValue = EmitExpressionValue(recv);
                                TypeSymbol receiverRefType = call.Method.ContainingSymbol is NamedTypeSymbol refDecl && refDecl.IsReferenceType
                                    ? refDecl
                                    : GetSyntheticObjectType(recv.Type);
                                receiver = EmitBoxedReference(recvValue, recv.Type, receiverRefType);
                            }
                            else
                            {
                                thisIsManagedByRef = true;
                                receiver = IsAddressableValueTypeReceiver(recv)
                                    ? EmitLoadAddressOfLValue(recv)
                                    : SpillAddressableReceiver(recv);
                            }
                        }
                        else
                        {
                            receiver = EmitExpressionValue(recv);
                        }
                    }

                    values.Add(receiver);
                }

                var args = call.Arguments;
                for (int i = 0; i < args.Length; i++)
                {
                    values.Add(EmitExpressionValue(args[i]));
                    argCount++;
                }

                int token = _tokens.GetMethodToken(call.Method);
                int packed = (argCount & 0x7FFF) | (hasThis << 15);
                bool returnsValue = !IsVoid(call.Method.ReturnType);
                int result = mode == EmitMode.Value && returnsValue ? NewValue(call.Method.ReturnType) : Instruction.NoValue;
                bool isBaseReceiver = call.ReceiverOpt is BoundBaseExpression;

                Op op = thisIsManagedByRef || isBaseReceiver
                    ? Op.Call
                    : (!call.Method.IsStatic && (call.Method.IsVirtual || call.Method.IsAbstract || call.Method.IsOverride))
                        ? Op.CallVirt
                        : Op.Call;

                _il.Emit(op, result, values.ToImmutable(), operand0: token, operand1: packed);
                return result;
            }

            private bool TryEmitIntrinsic(BoundCallExpression call, EmitMode mode, out int result)
            {
                result = Instruction.NoValue;
                var def = call.Method.OriginalDefinition;
                if (!def.IsStatic)
                    return false;
                if (def.ContainingSymbol is not NamedTypeSymbol containingType)
                    return false;

                if (containingType.Name == "Unsafe" && IsInNamespace(containingType, "System", "Runtime", "CompilerServices"))
                {
                    var ps = call.Method.Parameters;

                    if (def.Name == "SizeOf" && ps.Length == 0)
                    {
                        var tas = call.Method.TypeArguments;
                        if (tas.Length != 1) return false;
                        if (mode == EmitMode.Value)
                        {
                            result = NewValue(call.Type);
                            _il.Emit(Op.Sizeof, result, operand0: _tokens.GetTypeToken(tas[0]));
                        }
                        return true;
                    }

                    if (def.Name == "As" && ps.Length == 1)
                    {
                        if (ps[0].Type.SpecialType == SpecialType.System_Object)
                        {
                            int value = EmitExpressionValue(call.Arguments[0]);
                            if (mode == EmitMode.Value)
                                result = EmitReinterpretValue(value, GetRawCallResultType(call));
                            return true;
                        }

                        if (ps[0].Type is ByRefTypeSymbol)
                        {
                            int value = EmitExpressionValue(call.Arguments[0]);
                            if (mode == EmitMode.Value)
                                result = EmitReinterpretValue(value, GetRawCallResultType(call));
                            return true;
                        }
                    }

                    if (def.Name == "AsRef" && ps.Length == 1)
                    {
                        if (ps[0].Type is PointerTypeSymbol)
                        {
                            int ptr = EmitExpressionValue(call.Arguments[0]);
                            if (mode == EmitMode.Value)
                            {
                                result = NewValue(GetRawCallResultType(call));
                                _il.Emit(Op.PtrToByRef, result, ptr);
                            }
                            return true;
                        }

                        if (ps[0].Type is ByRefTypeSymbol)
                        {
                            int value = EmitExpressionValue(call.Arguments[0]);
                            if (mode == EmitMode.Value)
                                result = EmitReinterpretValue(value, GetRawCallResultType(call));
                            return true;
                        }
                    }

                    if (def.Name == "ReadUnaligned" && ps.Length == 1 &&
                        ps[0].Type is ByRefTypeSymbol br0 &&
                        br0.ElementType.SpecialType == SpecialType.System_UInt8)
                    {
                        int addr = EmitExpressionValue(call.Arguments[0]);
                        if (mode == EmitMode.Value)
                        {
                            result = NewValue(call.Type);
                            _il.Emit(Op.Ldobj, result, addr, operand0: _tokens.GetTypeToken(call.Type));
                        }
                        return true;
                    }

                    if (def.Name == "WriteUnaligned" && ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol brDst &&
                        brDst.ElementType.SpecialType == SpecialType.System_UInt8)
                    {
                        var tas = call.Method.TypeArguments;
                        if (tas.Length != 1)
                            return false;

                        int dst = EmitExpressionValue(call.Arguments[0]);
                        int value = EmitExpressionValue(call.Arguments[1]);
                        _il.Emit2(Op.Stobj, Instruction.NoValue, dst, value, operand0: _tokens.GetTypeToken(tas[0]));
                        return true;
                    }

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

                            int source = EmitExpressionValue(call.Arguments[0]);
                            int offset = EmitExpressionValue(call.Arguments[1]);
                            TypeSymbol nativeIntType = GetSyntheticNativeIntType(call.Type);

                            if (p1.SpecialType == SpecialType.System_Int32)
                                offset = EmitIntrinsicConv(offset, nativeIntType, NumericConvKind.NativeInt);

                            TypeSymbol int32Type = GetSyntheticInt32Type(elemType);
                            int size = NewValue(int32Type);
                            _il.Emit(Op.Sizeof, size, operand0: _tokens.GetTypeToken(elemType));
                            size = EmitIntrinsicConv(size, nativeIntType, NumericConvKind.NativeInt);
                            int scaled = NewValue(nativeIntType);
                            _il.Emit2(Op.Mul, scaled, offset, size);

                            if (mode == EmitMode.Value)
                            {
                                result = NewValue(GetRawCallResultType(call));
                                _il.Emit2(Op.PtrElemAddr, result, source, scaled, operand0: 1);
                            }
                            else
                            {
                                _il.Emit2(Op.PtrElemAddr, Instruction.NoValue, source, scaled, operand0: 1);
                            }
                            return true;
                        }
                    }

                    if (def.Name == "ByteOffset" && ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol && ps[1].Type is ByRefTypeSymbol)
                    {
                        int origin = EmitExpressionValue(call.Arguments[0]);
                        int target = EmitExpressionValue(call.Arguments[1]);
                        int diff = mode == EmitMode.Value ? NewValue(call.Type) : Instruction.NoValue;
                        _il.Emit2(Op.PtrDiff, diff, origin, target, operand0: 1);
                        if (mode == EmitMode.Value)
                        {
                            result = NewValue(call.Type);
                            _il.Emit(Op.Neg, result, diff);
                        }
                        return true;
                    }

                    if (def.Name == "AddByteOffset" && ps.Length == 2 && ps[0].Type is ByRefTypeSymbol)
                    {
                        var p1 = ps[1].Type;
                        if (p1.SpecialType == SpecialType.System_IntPtr || p1.SpecialType == SpecialType.System_UIntPtr)
                        {
                            int source = EmitExpressionValue(call.Arguments[0]);
                            int offset = EmitExpressionValue(call.Arguments[1]);

                            if (p1.SpecialType == SpecialType.System_UIntPtr)
                                offset = EmitIntrinsicConv(offset, GetSyntheticNativeUIntType(call.Type), NumericConvKind.NativeUInt);

                            if (mode == EmitMode.Value)
                            {
                                result = NewValue(GetRawCallResultType(call));
                                _il.Emit2(Op.PtrElemAddr, result, source, offset, operand0: 1);
                            }
                            else
                            {
                                _il.Emit2(Op.PtrElemAddr, Instruction.NoValue, source, offset, operand0: 1);
                            }
                            return true;
                        }
                    }

                    if (def.Name == "AreSame" && ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol && ps[1].Type is ByRefTypeSymbol)
                    {
                        int left = EmitExpressionValue(call.Arguments[0]);
                        int right = EmitExpressionValue(call.Arguments[1]);
                        if (mode == EmitMode.Value)
                        {
                            result = NewValue(call.Type);
                            _il.Emit2(Op.Ceq, result, left, right);
                        }
                        else
                        {
                            _il.Emit2(Op.Ceq, Instruction.NoValue, left, right);
                        }
                        return true;
                    }
                    return false;
                }

                if (containingType.Name == "MemoryMarshal" && IsInNamespace(containingType, "System", "Runtime", "InteropServices"))
                {
                    if (def.Name == "GetArrayDataReference" && call.Method.Parameters.Length == 1)
                    {
                        int array = EmitExpressionValue(call.Arguments[0]);
                        if (mode == EmitMode.Value)
                        {
                            result = NewValue(GetRawCallResultType(call));
                            _il.Emit(Op.LdArrayDataRef, result, array);
                        }
                        else
                        {
                            _il.Emit(Op.LdArrayDataRef, Instruction.NoValue, array);
                        }
                        return true;
                    }
                }

                return false;
            }

            private int EmitIntrinsicConv(int source, TypeSymbol targetType, NumericConvKind kind, NumericConvFlags flags = NumericConvFlags.None)
            {
                int result = NewValue(targetType);
                _il.Emit(Op.Conv, result, source, operand0: (int)kind, operand1: (int)flags);
                return result;
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

                return s is null || (s is NamespaceSymbol g && (g.IsGlobalNamespace || string.IsNullOrEmpty(g.Name)));
            }

            private int EmitObjectCreation(BoundObjectCreationExpression obj, EmitMode mode)
            {
                if (obj.ConstructorOpt is null)
                {
                    int value = EmitDefaultValue(obj.Type);
                    return mode == EmitMode.Value ? value : Instruction.NoValue;
                }

                var args = obj.Arguments;
                var values = ImmutableArray.CreateBuilder<int>(args.Length);
                for (int i = 0; i < args.Length; i++)
                    values.Add(EmitExpressionValue(args[i]));

                int result = mode == EmitMode.Value ? NewValue(obj.Type) : Instruction.NoValue;
                _il.Emit(Op.Newobj, result, values.ToImmutable(), operand0: _tokens.GetMethodToken(obj.ConstructorOpt), operand1: args.Length);
                return result;
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

            private int EmitAs(BoundAsExpression @as, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                {
                    EmitExpression(@as.Operand, EmitMode.Discard);
                    return Instruction.NoValue;
                }

                int operand = EmitExpressionValue(@as.Operand);

                if (@as.Operand.Type.IsValueType)
                    operand = EmitBoxedReference(operand, @as.Operand.Type, GetSyntheticObjectType(@as.Operand.Type));

                if (TryGetSystemNullableUnderlying(@as.Type, out var underlying))
                {
                    var lNull = _il.DefineLabel();
                    var lFail = _il.DefineLabel();
                    var lEnd = _il.DefineLabel();
                    int spill = AllocateSpillLocal(@as.Type);

                    _il.EmitBranch(Op.Brfalse, lNull, operand);

                    int inst = NewValue(GetSyntheticObjectType(@as.Type));
                    _il.Emit(Op.Isinst, inst, operand, operand0: _tokens.GetTypeToken(underlying));
                    _il.EmitBranch(Op.Brfalse, lFail, inst);

                    int unboxed = NewValue(@as.Type);
                    _il.Emit(Op.UnboxAny, unboxed, inst, operand0: _tokens.GetTypeToken(@as.Type));
                    _il.Emit(Op.Stloc, Instruction.NoValue, unboxed, operand0: spill);
                    _il.EmitBranch(Op.Br, lEnd);

                    _il.MarkLabel(lFail);
                    int failDefault = EmitDefaultValue(@as.Type);
                    _il.Emit(Op.Stloc, Instruction.NoValue, failDefault, operand0: spill);
                    _il.EmitBranch(Op.Br, lEnd);

                    _il.MarkLabel(lNull);
                    int nullDefault = EmitDefaultValue(@as.Type);
                    _il.Emit(Op.Stloc, Instruction.NoValue, nullDefault, operand0: spill);

                    _il.MarkLabel(lEnd);
                    int result = NewValue(@as.Type);
                    _il.Emit(Op.Ldloc, result, operand0: spill);
                    return result;
                }

                int finalResult = NewValue(@as.Type);
                _il.Emit(Op.Isinst, finalResult, operand, operand0: _tokens.GetTypeToken(@as.Type));
                return finalResult;
            }

            private int EmitBoolNot(int value, TypeSymbol boolType)
            {
                int zero = EmitConstantValue(boolType, false, EmitMode.Value);
                int result = NewValue(boolType);
                _il.Emit2(Op.Ceq, result, value, zero);
                return result;
            }

            private int EmitIsPattern(BoundIsPatternExpression isPattern, EmitMode mode)
            {
                switch (isPattern.PatternKind)
                {
                    case BoundIsPatternKind.Type:
                        return EmitTypePattern(isPattern, mode);
                    case BoundIsPatternKind.Null:
                        return EmitNullPattern(isPattern, mode);
                    case BoundIsPatternKind.Constant:
                        return EmitConstantPattern(isPattern, mode);
                    default:
                        throw new NotSupportedException($"Unexpected pattern kind '{isPattern.PatternKind}'.");
                }
            }

            private int EmitTypePattern(BoundIsPatternExpression p, EmitMode mode)
            {
                int operand = EmitExpressionValue(p.Operand);

                if (p.Operand.Type.IsValueType)
                    operand = EmitBoxedReference(operand, p.Operand.Type, GetSyntheticObjectType(p.Operand.Type));

                int inst = NewValue(GetIsInstResultType(p.PatternTypeOpt!, p.Operand.Type));
                _il.Emit(Op.Isinst, inst, operand, operand0: _tokens.GetTypeToken(p.PatternTypeOpt!));

                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                var lFalse = _il.DefineLabel();
                var lEnd = _il.DefineLabel();
                int spill = AllocateSpillLocal(p.Type);

                _il.EmitBranch(Op.Brfalse, lFalse, inst);

                if (p.DeclaredLocalOpt is not null && !p.IsDiscard)
                {
                    int localIndex = GetOrCreateLocal(p.DeclaredLocalOpt);
                    int stored = inst;
                    if (p.PatternTypeOpt!.IsValueType)
                    {
                        stored = NewValue(p.PatternTypeOpt);
                        _il.Emit(Op.UnboxAny, stored, inst, operand0: _tokens.GetTypeToken(p.PatternTypeOpt));
                    }
                    _il.Emit(Op.Stloc, Instruction.NoValue, stored, operand0: localIndex);
                }

                int trueValue = EmitConstantValue(p.Type, !p.IsNegated, EmitMode.Value);
                _il.Emit(Op.Stloc, Instruction.NoValue, trueValue, operand0: spill);
                _il.EmitBranch(Op.Br, lEnd);

                _il.MarkLabel(lFalse);
                int falseValue = EmitConstantValue(p.Type, p.IsNegated, EmitMode.Value);
                _il.Emit(Op.Stloc, Instruction.NoValue, falseValue, operand0: spill);

                _il.MarkLabel(lEnd);
                int result = NewValue(p.Type);
                _il.Emit(Op.Ldloc, result, operand0: spill);
                return result;
            }

            private int EmitNullPattern(BoundIsPatternExpression p, EmitMode mode)
            {
                int operand = EmitExpressionValue(p.Operand);

                TypeSymbol nullableCompareType = p.Operand.Type;
                if (p.Operand.Type.IsValueType)
                {
                    nullableCompareType = GetSyntheticObjectType(p.Operand.Type);
                    operand = EmitBoxedReference(operand, p.Operand.Type, nullableCompareType);
                }

                int nullValue = NewValue(nullableCompareType);
                _il.Emit(Op.Ldnull, nullValue);
                int result = mode == EmitMode.Value ? NewValue(p.Type) : Instruction.NoValue;
                _il.Emit2(Op.Ceq, result, operand, nullValue);

                if (p.IsNegated && mode == EmitMode.Value)
                    return EmitBoolNot(result, p.Type);

                return result;
            }

            private int EmitConstantPattern(BoundIsPatternExpression p, EmitMode mode)
            {
                int operand = EmitExpressionValue(p.Operand);
                int constant = EmitExpressionValue(p.ConstantOpt!);

                int result = mode == EmitMode.Value ? NewValue(p.Type) : Instruction.NoValue;
                _il.Emit2(Op.Ceq, result, operand, constant);

                if (p.IsNegated && mode == EmitMode.Value)
                    return EmitBoolNot(result, p.Type);

                return result;
            }

            private int EmitConversion(BoundConversionExpression conv, EmitMode mode)
            {
                if (conv.Conversion.Kind == ConversionKind.NullLiteral)
                {
                    EmitExpression(conv.Operand, EmitMode.Discard);

                    if (mode == EmitMode.Value)
                    {
                        if (IsSystemNullableValueType(conv.Type))
                            return EmitDefaultValue(conv.Type);

                        int value = NewValue(conv.Type);
                        _il.Emit(Op.Ldnull, value);
                        return value;
                    }

                    return Instruction.NoValue;
                }

                int operand = EmitExpression(conv.Operand, mode == EmitMode.Value ? EmitMode.Value : EmitMode.Discard);
                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                switch (conv.Conversion.Kind)
                {
                    case ConversionKind.Identity:
                        return operand;
                    case ConversionKind.NullLiteral:
                        {
                            int value = NewValue(conv.Type);
                            _il.Emit(Op.Ldnull, value);
                            return value;
                        }
                    case ConversionKind.ImplicitNumeric:
                    case ConversionKind.ExplicitNumeric:
                    case ConversionKind.ImplicitConstant:
                        if (conv.Operand.Type is PointerTypeSymbol && conv.Type is PointerTypeSymbol)
                            return operand;
                        return EmitNumericConv(operand, conv.Operand.Type, conv.Type, conv.IsChecked);
                    case ConversionKind.ImplicitReference:
                    case ConversionKind.ExplicitReference:
                        {
                            int result = NewValue(conv.Type);
                            _il.Emit(Op.CastClass, result, operand, operand0: _tokens.GetTypeToken(conv.Type));
                            return result;
                        }
                    case ConversionKind.Boxing:
                        {
                            int result = NewValue(conv.Type);
                            _il.Emit(Op.Box, result, operand, operand0: _tokens.GetTypeToken(conv.Operand.Type));
                            return result;
                        }
                    case ConversionKind.Unboxing:
                        {
                            int result = NewValue(conv.Type);
                            _il.Emit(Op.UnboxAny, result, operand, operand0: _tokens.GetTypeToken(conv.Type));
                            return result;
                        }
                    default:
                        throw new NotSupportedException($"Conversion kind '{conv.Conversion.Kind}' is not supported.");
                }
            }

            private int EmitNumericConv(int source, TypeSymbol sourceType, TypeSymbol targetType, bool isChecked)
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

                int result = NewValue(targetType);
                _il.Emit(Op.Conv, result, source, operand0: (int)kind, operand1: flags);
                return result;
            }

            private int EmitSequence(BoundSequenceExpression seq, EmitMode mode)
            {
                for (int i = 0; i < seq.Locals.Length; i++)
                    GetOrCreateLocal(seq.Locals[i]);

                for (int i = 0; i < seq.SideEffects.Length; i++)
                    EmitStatement(seq.SideEffects[i]);

                return EmitExpression(seq.Value, mode == EmitMode.Value ? EmitMode.Value : EmitMode.Discard);
            }

            private int EmitDefaultValue(TypeSymbol type)
            {
                int value = NewValue(type);
                _il.Emit(Op.DefaultValue, value, operand0: _tokens.GetTypeToken(type));
                return value;
            }

            private int EmitSizeOf(BoundSizeOfExpression so, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                int result = NewValue(so.Type);
                _il.Emit(Op.Sizeof, result, operand0: _tokens.GetTypeToken(so.OperandType));
                return result;
            }

            private int EmitRefExpression(BoundRefExpression re, EmitMode mode)
            {
                int addr = EmitLoadAddressOfLValue(re.Operand);

                if (re.Operand is BoundPointerIndirectionExpression or BoundPointerElementAccessExpression)
                {
                    int br = NewValue(re.Type);
                    _il.Emit(Op.PtrToByRef, br, addr);
                    addr = br;
                }

                return mode == EmitMode.Value ? addr : Instruction.NoValue;
            }

            private int EmitAddressOf(BoundAddressOfExpression addrof, EmitMode mode)
            {
                int addr = EmitLoadAddressOfLValue(addrof.Operand);
                return mode == EmitMode.Value ? addr : Instruction.NoValue;
            }

            private int EmitPointerIndirection(BoundPointerIndirectionExpression pind, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                {
                    EmitExpression(pind.Operand, EmitMode.Discard);
                    return Instruction.NoValue;
                }

                int addr = EmitExpressionValue(pind.Operand);
                int result = NewValue(pind.Type);
                _il.Emit(Op.Ldobj, result, addr, operand0: _tokens.GetTypeToken(pind.Type));
                return result;
            }

            private int EmitPointerElementAccess(BoundPointerElementAccessExpression pea, EmitMode mode)
            {
                int ptr = EmitExpressionValue(pea.Expression);
                int index = EmitExpressionValue(pea.Index);
                int addr = NewValue(pea.Type is ByRefTypeSymbol ? pea.Type : new PointerTypeSymbol(pea.Type));
                _il.Emit2(Op.PtrElemAddr, addr, ptr, index, operand0: GetElementSizeOrThrow(pea.Type));

                if (mode == EmitMode.Discard)
                    return Instruction.NoValue;

                int result = NewValue(pea.Type);
                _il.Emit(Op.Ldobj, result, addr, operand0: _tokens.GetTypeToken(pea.Type));
                return result;
            }

            private int EmitStackAlloc(BoundStackAllocArrayCreationExpression sa, EmitMode mode)
            {
                int count = EmitExpressionValue(sa.Count);
                int size = GetElementSizeOrThrow(sa.ElementType);
                int ptr = NewValue(sa.Type);
                _il.Emit(Op.StackAlloc, ptr, count, operand0: size);
                int elemTok = _tokens.GetTypeToken(sa.ElementType);

                if (sa.InitializerOpt is not null)
                {
                    var elems = sa.InitializerOpt.Elements;
                    for (int i = 0; i < elems.Length; i++)
                    {
                        int index = EmitConstantValue(GetSyntheticInt32Type(elems[i].Type), i, EmitMode.Value);
                        int addr = NewValue(sa.Type);
                        _il.Emit2(Op.PtrElemAddr, addr, ptr, index, operand0: size);
                        int value = EmitExpressionValue(elems[i]);
                        _il.Emit2(Op.Stobj, Instruction.NoValue, addr, value, operand0: elemTok);
                    }
                }

                return mode == EmitMode.Value ? ptr : Instruction.NoValue;
            }

            private int SpillAddressableReceiver(BoundExpression receiver)
            {
                int spill = AllocateSpillLocal(receiver.Type);
                int value = EmitExpressionValue(receiver);
                _il.Emit(Op.Stloc, Instruction.NoValue, value, operand0: spill);
                int addr = NewByRefValue(receiver.Type);
                _il.Emit(Op.Ldloca, addr, operand0: spill);
                return addr;
            }

            private int EmitLoadAddressOfLValue(BoundExpression lvalue)
            {
                switch (lvalue)
                {
                    case BoundLocalExpression loc:
                        {
                            int idx = GetOrCreateLocal(loc.Local);
                            if (loc.Local.IsByRef)
                            {
                                int addr = NewByRefValue(loc.Local.Type);
                                _il.Emit(Op.Ldloc, addr, operand0: idx);
                                return addr;
                            }
                            else
                            {
                                int addr = NewByRefValue(loc.Local.Type);
                                _il.Emit(Op.Ldloca, addr, operand0: idx);
                                return addr;
                            }
                        }
                    case BoundArrayElementAccessExpression aea:
                        {
                            if (aea.Indices.Length != 1)
                                throw new NotSupportedException("Only single dimensional arrays are supported.");

                            int array = EmitExpressionValue(aea.Expression);
                            int index = EmitExpressionValue(aea.Indices[0]);
                            int elemTok = _tokens.GetTypeToken(aea.Type);
                            int addr = NewByRefValue(aea.Type);
                            _il.Emit2(Op.Ldelema, addr, array, index, operand0: elemTok);
                            return addr;
                        }
                    case BoundParameterExpression par:
                        if (par.Parameter.Type is ByRefTypeSymbol br)
                        {
                            int addr = NewValue(par.Parameter.Type);
                            _il.Emit(Op.Ldarg, addr, operand0: GetArgIndex(par.Parameter));
                            return addr;
                        }
                        else
                        {
                            int addr = NewByRefValue(par.Parameter.Type);
                            _il.Emit(Op.Ldarga, addr, operand0: GetArgIndex(par.Parameter));
                            return addr;
                        }
                    case BoundThisExpression:
                        return EmitThisAddress(lvalue.Type);
                    case BoundBaseExpression:
                        return EmitThisAddress(lvalue.Type);
                    case BoundCallExpression call when call.Method.ReturnType is ByRefTypeSymbol:
                        return EmitCall(call, EmitMode.Value);
                    case BoundIndexerAccessExpression ia when ia.Indexer.GetMethod?.ReturnType is ByRefTypeSymbol:
                        {
                            if (ia.Indexer.IsStatic)
                                throw new NotSupportedException("Static indexers are not supported as lvalues.");

                            bool thisIsManagedByRef = false;
                            int receiver;
                            if (ia.Receiver.Type.IsValueType)
                            {
                                bool methodDeclaredOnRefType =
                                    ia.Indexer.GetMethod!.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;

                                if (methodDeclaredOnRefType)
                                {
                                    int recv = EmitExpressionValue(ia.Receiver);
                                    TypeSymbol receiverRefType = ia.Indexer.GetMethod!.ContainingSymbol is NamedTypeSymbol refDecl && refDecl.IsReferenceType
                                        ? refDecl
                                        : GetSyntheticObjectType(ia.Receiver.Type);
                                    receiver = EmitBoxedReference(recv, ia.Receiver.Type, receiverRefType);
                                }
                                else if (IsAddressableValueTypeReceiver(ia.Receiver))
                                {
                                    thisIsManagedByRef = true;
                                    receiver = EmitLoadAddressOfLValue(ia.Receiver);
                                }
                                else
                                {
                                    receiver = SpillAddressableReceiver(ia.Receiver);
                                    thisIsManagedByRef = true;
                                }
                            }
                            else
                            {
                                receiver = EmitExpressionValue(ia.Receiver);
                            }

                            var values = ImmutableArray.CreateBuilder<int>();
                            values.Add(receiver);
                            for (int i = 0; i < ia.Arguments.Length; i++)
                                values.Add(EmitExpressionValue(ia.Arguments[i]));

                            int token = _tokens.GetMethodToken(ia.Indexer.GetMethod!);
                            int packed = (ia.Arguments.Length & 0x7FFF) | (ia.Indexer.GetMethod!.IsStatic ? 0 : 1 << 15);
                            int result = NewValue(ia.Indexer.GetMethod!.ReturnType);
                            _il.Emit(thisIsManagedByRef ? Op.Call : Op.CallVirt, result, values.ToImmutable(), operand0: token, operand1: packed);
                            return result;
                        }
                    case BoundMemberAccessExpression { Member: FieldSymbol fs } ma when fs.Type is not ByRefTypeSymbol:
                        {
                            int tok = _tokens.GetFieldToken(fs);

                            if (fs.IsStatic)
                            {
                                int addr = NewByRefValue(fs.Type);
                                _il.Emit(Op.Ldsflda, addr, operand0: tok);
                                return addr;
                            }

                            if (ma.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                            int receiver = ma.ReceiverOpt.Type.IsValueType
                                ? EmitLoadAddressOfLValue(ma.ReceiverOpt)
                                : EmitExpressionValue(ma.ReceiverOpt);

                            int faddr = NewByRefValue(fs.Type);
                            _il.Emit(Op.Ldflda, faddr, receiver, operand0: tok);
                            return faddr;
                        }
                    case BoundMemberAccessExpression { Member: FieldSymbol fs } ma when fs.Type is ByRefTypeSymbol:
                        {
                            int tok = _tokens.GetFieldToken(fs);

                            if (fs.IsStatic)
                            {
                                int addr = NewValue(fs.Type);
                                _il.Emit(Op.Ldsfld, addr, operand0: tok);
                                return addr;
                            }

                            if (ma.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");

                            int receiver;
                            if (ma.ReceiverOpt.Type.IsValueType)
                            {
                                if (!IsAddressableValueTypeReceiver(ma.ReceiverOpt))
                                    throw new InvalidOperationException("Cannot take ref to a field of a non-lvalue value-type receiver.");

                                receiver = EmitLoadAddressOfLValue(ma.ReceiverOpt);
                            }
                            else
                            {
                                receiver = EmitExpressionValue(ma.ReceiverOpt);
                            }
                            {
                                int addr = NewValue(fs.Type);
                                _il.Emit(Op.Ldfld, addr, receiver, operand0: tok);
                                return addr;
                            }
                        }
                    case BoundPointerIndirectionExpression pind:
                        return EmitExpressionValue(pind.Operand);
                    case BoundPointerElementAccessExpression pea:
                        {
                            int ptr = EmitExpressionValue(pea.Expression);
                            int index = EmitExpressionValue(pea.Index);
                            int addr = NewValue(new PointerTypeSymbol(pea.Type));
                            _il.Emit2(Op.PtrElemAddr, addr, ptr, index, operand0: GetElementSizeOrThrow(pea.Type));
                            return addr;
                        }
                    default:
                        throw new NotSupportedException($"Cannot take address of lvalue '{lvalue.GetType().Name}'.");
                }
            }
        }
    }
}
