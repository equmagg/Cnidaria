using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal static class StaticDataEncoding
    {
        public static byte[] Encode(BoundStaticDataExpression node)
        {
            int elementSize = ElementSize(node.ElementType);
            var bytes = new byte[checked(node.Elements.Length * elementSize)];
            for (int i = 0; i < node.Elements.Length; i++)
            {
                var element = node.Elements[i];
                if (!element.ConstantValueOpt.HasValue)
                    throw new InvalidOperationException("Static data element is not a constant.");
                WriteElement(bytes.AsSpan(i * elementSize, elementSize), node.ElementType, element.ConstantValueOpt.Value);
            }
            return bytes;
        }
        private static int ElementSize(TypeSymbol type)
        {
            if (type is NamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
                type = enumType.EnumUnderlyingType ?? type;
            return type.SpecialType switch
            {
                SpecialType.System_Boolean or SpecialType.System_Int8 or SpecialType.System_UInt8 => 1,
                SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
                _ => throw new NotSupportedException("Unsupported static data element type: " + type.Name),
            };
        }
        private static void WriteElement(Span<byte> destination, TypeSymbol type, object? value)
        {
            if (type is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum)
                type = nt.EnumUnderlyingType ?? type;

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    destination[0] = Convert.ToBoolean(value) ? (byte)1 : (byte)0;
                    return;
                case SpecialType.System_Int8:
                    destination[0] = unchecked((byte)Convert.ToSByte(value));
                    return;
                case SpecialType.System_UInt8:
                    destination[0] = Convert.ToByte(value);
                    return;
                case SpecialType.System_Char:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, Convert.ToChar(value));
                    return;
                case SpecialType.System_Int16:
                    BinaryPrimitives.WriteInt16LittleEndian(destination, Convert.ToInt16(value));
                    return;
                case SpecialType.System_UInt16:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, Convert.ToUInt16(value));
                    return;
                case SpecialType.System_Int32:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, Convert.ToInt32(value));
                    return;
                case SpecialType.System_UInt32:
                    BinaryPrimitives.WriteUInt32LittleEndian(destination, Convert.ToUInt32(value));
                    return;
                case SpecialType.System_Int64:
                    BinaryPrimitives.WriteInt64LittleEndian(destination, Convert.ToInt64(value));
                    return;
                case SpecialType.System_UInt64:
                    BinaryPrimitives.WriteUInt64LittleEndian(destination, Convert.ToUInt64(value));
                    return;
                case SpecialType.System_Single:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(Convert.ToSingle(value)));
                    return;
                case SpecialType.System_Double:
                    BinaryPrimitives.WriteInt64LittleEndian(destination, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)));
                    return;
                default:
                    throw new NotSupportedException("Unsupported static data element type: " + type.Name);
            }
        }
    }

    internal static class ILEmitter
    {
        public static byte[] Emit(BoundMethodBody body, ITokenProvider tokens, TargetInfo target)
            => new MethodEmitter(tokens, body.Method, target).Emit(body);

        private enum EmitMode : byte
        {
            Discard,
            Value
        }

        private sealed class MethodEmitter
        {
            private readonly ITokenProvider _tokens;
            private readonly MethodSymbol _method;
            private readonly TargetInfo _target;
            private readonly ILBuilder _il = new();
            private readonly Dictionary<LocalSymbol, int> _localsBySymbol = new(ReferenceEqualityComparer<LocalSymbol>.Instance);
            private readonly List<TypeSymbol> _localTypes = new();
            private readonly Dictionary<ParameterSymbol, int> _argsBySymbol = new(ReferenceEqualityComparer<ParameterSymbol>.Instance);
            private readonly Dictionary<LabelSymbol, ILLabel> _labelsBySymbol = new(ReferenceEqualityComparer<LabelSymbol>.Instance);
            private readonly Dictionary<LabelSymbol, BoundStatement?> _labelRegions = new(ReferenceEqualityComparer<LabelSymbol>.Instance);
            private BoundStatement? _region;
            private ILLabel _returnLabel;
            private int _returnLocal = -1;
            private bool _returnsFromRegion;
            private readonly Dictionary<TypeSymbol, int> _defaultValueTemps = new(ReferenceEqualityComparer<TypeSymbol>.Instance);

            public MethodEmitter(ITokenProvider tokens, MethodSymbol method, TargetInfo target)
            {
                _tokens = tokens;
                _method = method;
                _target = target;
                int start = method.IsStatic ? 0 : 1;
                var ps = method.Parameters;
                for (int i = 0; i < ps.Length; i++)
                    _argsBySymbol[ps[i]] = start + i;
            }

            public byte[] Emit(BoundMethodBody body)
            {
                CollectLabels(body.Body, null);
                EmitStatement(body.Body);

                if (_il.IsReachable)
                    EmitImplicitReturn(body.Method);
                if (_returnsFromRegion)
                {
                    _il.MarkLabel(_returnLabel);
                    if (_returnLocal >= 0)
                        _il.EmitLocal(ILOpCode.Ldloc, _returnLocal);
                    _il.Emit(ILOpCode.Ret, _returnLocal >= 0 ? 1 : 0, 0);
                }

                return _il.Bake(_tokens.GetLocalSignatureToken(_localTypes), initLocals: true);
            }
            private void EmitImplicitReturn(MethodSymbol method)
            {
                if (IsVoid(method.ReturnType))
                {
                    _il.Emit(ILOpCode.Ret, 0, 0);
                    return;
                }
                EmitDefaultValue(method.ReturnType);
                _il.Emit(ILOpCode.Ret, 1, 0);
            }
            private void CollectLabels(BoundStatement s, BoundStatement? region)
            {
                switch (s)
                {
                    case BoundBlockStatement b:
                        for (int i = 0; i < b.Statements.Length; i++)
                            CollectLabels(b.Statements[i], region);
                        break;
                    case BoundLabelStatement ls:
                        GetOrCreateLabel(ls.Label);
                        _labelRegions.Add(ls.Label, region);
                        break;
                    case BoundTryStatement t:
                        CollectLabels(t.TryBlock, t.TryBlock);
                        foreach (var c in t.CatchBlocks)
                            CollectLabels(c.Body, c.Body);
                        if (t.FinallyBlockOpt is not null)
                            CollectLabels(t.FinallyBlockOpt, t.FinallyBlockOpt);
                        break;
                }
            }
            // Labels inside expressions are not collected; their gotos never cross a protected region.
            private bool LeavesRegion(LabelSymbol target)
                => _labelRegions.TryGetValue(target, out var region) && !ReferenceEquals(region, _region);
            private void EmitReturn(BoundExpression? expression)
            {
                if (expression is not null)
                    EmitExpression(expression, EmitMode.Value);
                if (_region is null)
                {
                    _il.Emit(ILOpCode.Ret, expression is null ? 0 : 1, 0);
                    return;
                }
                if (!_returnsFromRegion)
                {
                    _returnsFromRegion = true;
                    _returnLabel = _il.DefineLabel();
                    if (!IsVoid(_method.ReturnType))
                        _returnLocal = AllocateTemp(_method.ReturnType);
                }
                if (expression is not null)
                    _il.EmitLocal(ILOpCode.Stloc, _returnLocal);
                _il.EmitBranch(ILOpCode.Leave, _returnLabel, 0);
            }
            private void EmitRegion(BoundStatement region)
            {
                var outer = _region;
                _region = region;
                EmitStatement(region);
                _region = outer;
            }
            private ILLabel GetOrCreateLabel(LabelSymbol label)
            {
                if (_labelsBySymbol.TryGetValue(label, out var il))
                    return il;
                il = _il.DefineLabel();
                _labelsBySymbol.Add(label, il);
                return il;
            }
            private int GetOrCreateLocal(LocalSymbol local)
            {
                if (_localsBySymbol.TryGetValue(local, out var idx))
                    return idx;
                idx = _localTypes.Count;
                _localsBySymbol.Add(local, idx);
                _localTypes.Add(local.IsByRef ? new ByRefTypeSymbol(local.Type) : local.Type);
                return idx;
            }
            private int AllocateTemp(TypeSymbol type)
            {
                _localTypes.Add(type);
                return _localTypes.Count - 1;
            }
            private int GetArgIndex(ParameterSymbol p)
                => _argsBySymbol.TryGetValue(p, out var idx)
                    ? idx
                    : throw new InvalidOperationException($"Parameter '{p.Name}' not found in method '{_method.Name}'.");

            private static bool IsVoid(TypeSymbol type) => type.SpecialType == SpecialType.System_Void;

            private static TypeSymbol UnderlyingType(TypeSymbol type)
                => type is NamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                    ? enumType.EnumUnderlyingType ?? type
                    : type;

            private bool TryGetElementSize(TypeSymbol type, out int size)
            {
                size = UnderlyingType(type).SpecialType switch
                {
                    SpecialType.System_Boolean or SpecialType.System_Void or SpecialType.System_Int8 or SpecialType.System_UInt8 => 1,
                    SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
                    SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
                    SpecialType.System_Decimal => 16,
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr => _target.PointerSize,
                    _ => 0,
                };
                if (size != 0)
                    return true;
                if (type.IsReferenceType || type is PointerTypeSymbol or FunctionPointerTypeSymbol or ByRefTypeSymbol)
                {
                    size = _target.PointerSize;
                    return true;
                }
                return false;
            }
            private static bool UsesUnsignedIntegerSemantics(TypeSymbol t)
            {
                if (t is PointerTypeSymbol or FunctionPointerTypeSymbol)
                    return true;
                return UnderlyingType(t).SpecialType is
                    SpecialType.System_Char or
                    SpecialType.System_UInt8 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_UInt64 or
                    SpecialType.System_UIntPtr;
            }
            private static bool IsFloatingPoint(TypeSymbol t)
                => UnderlyingType(t).SpecialType is SpecialType.System_Single or SpecialType.System_Double;
            private static bool IsAddressableValueTypeReceiver(BoundExpression receiver)
                => receiver.IsLValue || receiver is BoundThisExpression;

            private void EmitStatement(BoundStatement s)
            {
                switch (s)
                {
                    case BoundEmptyStatement:
                    case BoundLocalFunctionStatement:
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
                        EmitReturn(ret.Expression);
                        return;
                    case BoundLabelStatement ls:
                        _il.MarkLabel(GetOrCreateLabel(ls.Label));
                        return;
                    case BoundGotoStatement gs:
                        _il.EmitBranch(LeavesRegion(gs.TargetLabel) ? ILOpCode.Leave : ILOpCode.Br, GetOrCreateLabel(gs.TargetLabel), 0);
                        return;
                    case BoundConditionalGotoStatement cgs:
                        EmitExpression(cgs.Condition, EmitMode.Value);
                        if (LeavesRegion(cgs.TargetLabel))
                        {
                            var skip = _il.DefineLabel();
                            _il.EmitBranch(cgs.JumpIfTrue ? ILOpCode.Brfalse : ILOpCode.Brtrue, skip, 1);
                            _il.EmitBranch(ILOpCode.Leave, GetOrCreateLabel(cgs.TargetLabel), 0);
                            _il.MarkLabel(skip);
                            return;
                        }
                        _il.EmitBranch(cgs.JumpIfTrue ? ILOpCode.Brtrue : ILOpCode.Brfalse, GetOrCreateLabel(cgs.TargetLabel), 1);
                        return;
                    case BoundThrowStatement ts:
                        if (ts.ExpressionOpt is null)
                        {
                            _il.Emit(ILOpCode.Rethrow, 0, 0);
                            return;
                        }
                        EmitExpression(ts.ExpressionOpt, EmitMode.Value);
                        _il.Emit(ILOpCode.Throw, 1, 0);
                        return;
                    case BoundTryStatement t:
                        EmitTry(t);
                        return;
                    default:
                        throw new NotSupportedException($"Statement '{s.GetType().Name}' is not supported by the IL emitter.");
                }
            }
            private void EmitTry(BoundTryStatement t)
            {
                bool hasCatches = !t.CatchBlocks.IsDefaultOrEmpty;
                bool hasFinally = t.FinallyBlockOpt is not null;
                if (!hasCatches && !hasFinally)
                    throw new NotSupportedException("try statement must have at least one catch or a finally.");
                for (int i = 0; i < t.CatchBlocks.Length; i++)
                {
                    if (t.CatchBlocks[i].FilterOpt is not null)
                        throw new NotSupportedException("catch filters are not supported.");
                }

                var tryStart = _il.DefineLabel();
                var after = _il.DefineLabel();
                var afterCatches = hasCatches && hasFinally ? _il.DefineLabel() : after;
                _il.MarkLabel(tryStart);
                EmitRegion(t.TryBlock);
                _il.EmitBranch(ILOpCode.Leave, afterCatches, 0);

                if (hasCatches)
                {
                    var tryEnd = _il.DefineLabel();
                    _il.MarkLabel(tryEnd);
                    int n = t.CatchBlocks.Length;
                    var handlerStarts = new ILLabel[n];
                    for (int i = 0; i < n; i++)
                        handlerStarts[i] = _il.DefineLabel();

                    for (int i = 0; i < n; i++)
                    {
                        var c = t.CatchBlocks[i];
                        _il.MarkLabel(handlerStarts[i]);
                        if (c.ExceptionLocalOpt is not null)
                            _il.EmitLocal(ILOpCode.Stloc, GetOrCreateLocal(c.ExceptionLocalOpt));
                        else
                            _il.Emit(ILOpCode.Pop, 1, 0);
                        EmitRegion(c.Body);
                        _il.EmitBranch(ILOpCode.Leave, afterCatches, 0);
                        _il.AddExceptionClause(
                            ILExceptionClauseKind.Catch,
                            tryStart,
                            tryEnd,
                            handlerStarts[i],
                            i + 1 < n ? handlerStarts[i + 1] : afterCatches,
                            _tokens.GetTypeToken(c.ExceptionType));
                    }
                    if (hasFinally)
                    {
                        _il.MarkLabel(afterCatches);
                        _il.EmitBranch(ILOpCode.Leave, after, 0);
                    }
                }

                if (hasFinally)
                {
                    var finallyStart = _il.DefineLabel();
                    _il.MarkLabel(finallyStart);
                    EmitRegion(t.FinallyBlockOpt!);
                    _il.Emit(ILOpCode.Endfinally, 0, 0);
                    _il.MarkLabel(after);
                    _il.AddExceptionClause(ILExceptionClauseKind.Finally, tryStart, finallyStart, finallyStart, after, 0);
                    return;
                }
                _il.MarkLabel(after);
            }
            private void EmitLocalDeclaration(BoundLocalDeclarationStatement ld)
            {
                int localIndex = GetOrCreateLocal(ld.Local);
                if (ld.Initializer is null)
                {
                    if (ld.Local.IsByRef)
                        throw new InvalidOperationException("Ref local must have an initializer.");
                    EmitInitializeLocal(localIndex, ld.Local.Type);
                    return;
                }
                EmitExpression(ld.Initializer, EmitMode.Value);
                _il.EmitLocal(ILOpCode.Stloc, localIndex);
            }
            private void EmitInitializeLocal(int localIndex, TypeSymbol type)
            {
                if (RequiresInitobj(type))
                {
                    _il.EmitLocal(ILOpCode.Ldloca, localIndex);
                    _il.EmitToken(ILOpCode.Initobj, _tokens.GetTypeToken(type), 1, 0);
                    return;
                }
                EmitDefaultValue(type);
                _il.EmitLocal(ILOpCode.Stloc, localIndex);
            }

            private void EmitExpression(BoundExpression e, EmitMode mode)
            {
                switch (e)
                {
                    case BoundLiteralExpression lit:
                        if (mode == EmitMode.Value)
                            EmitConstantValue(lit.Type, lit.Value);
                        return;
                    case BoundLocalExpression loc:
                        EmitLocal(loc, mode);
                        return;
                    case BoundParameterExpression par:
                        EmitParameter(par, mode);
                        return;
                    case BoundThisExpression:
                    case BoundBaseExpression:
                        if (mode == EmitMode.Value)
                        {
                            _il.EmitArg(ILOpCode.Ldarg, 0);
                            if (e.Type.IsValueType)
                                EmitLoadIndirect(e.Type);
                        }
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
                            EmitLoadIndirect(br.ElementType);
                            return;
                        }
                        EmitCall(call, mode);
                        return;
                    case BoundFunctionPointerLoadExpression functionPointerLoad:
                        if (mode == EmitMode.Value)
                            _il.EmitToken(ILOpCode.Ldftn, _tokens.GetMethodToken(functionPointerLoad.Method), 0, 1);
                        return;
                    case BoundFunctionPointerInvocationExpression invocation:
                        if (mode == EmitMode.Value && invocation.FunctionPointerType.ReturnRefKind != FunctionPointerRefKind.None)
                        {
                            EmitFunctionPointerInvocation(invocation, EmitMode.Value);
                            EmitLoadIndirect(invocation.FunctionPointerType.ReturnType);
                            return;
                        }
                        EmitFunctionPointerInvocation(invocation, mode);
                        return;
                    case BoundLambdaExpression lambda:
                        EmitLambda(lambda, mode);
                        return;
                    case BoundClosureCellCreationExpression closureCell:
                        EmitClosureCellCreation(closureCell, mode);
                        return;
                    case BoundClosureCreationExpression closure:
                        EmitClosureCreation(closure, mode);
                        return;
                    case BoundClosureSlotExpression closureSlot:
                        if (mode == EmitMode.Value)
                        {
                            EmitExpression(closureSlot.Closure, EmitMode.Value);
                            _il.EmitI4(closureSlot.SlotIndex);
                            _il.Emit(ILOpCode.Ldelem_Ref, 2, 1);
                        }
                        return;
                    case BoundClosureAccessExpression closureAccess:
                        if (mode == EmitMode.Value)
                        {
                            EmitExpression(closureAccess.Cell, EmitMode.Value);
                            _il.EmitI4(0);
                            _il.Emit(ILOpCode.Ldelem_Ref, 2, 1);
                            EmitUnboxClosureValue(closureAccess.Type);
                        }
                        return;
                    case BoundObjectCreationExpression obj:
                        EmitObjectCreation(obj, mode);
                        return;
                    case BoundConversionExpression conv:
                        EmitConversion(conv, mode);
                        return;
                    case BoundThrowExpression tex:
                        EmitExpression(tex.Exception, EmitMode.Value);
                        _il.Emit(ILOpCode.Throw, 1, 0);
                        return;
                    case BoundAsExpression @as:
                        EmitAs(@as, mode);
                        return;
                    case BoundIsPatternExpression isPattern:
                        EmitIsPattern(isPattern, mode);
                        return;
                    case BoundSequenceExpression seq:
                        for (int i = 0; i < seq.Locals.Length; i++)
                            GetOrCreateLocal(seq.Locals[i]);
                        for (int i = 0; i < seq.SideEffects.Length; i++)
                            EmitStatement(seq.SideEffects[i]);
                        EmitExpression(seq.Value, mode);
                        return;
                    case BoundRefExpression re:
                        EmitLoadAddressOfLValue(re.Operand);
                        if (mode == EmitMode.Discard)
                            _il.Emit(ILOpCode.Pop, 1, 0);
                        return;
                    case BoundAddressOfExpression addrof:
                        EmitLoadAddressOfLValue(addrof.Operand);
                        if (addrof.Type is PointerTypeSymbol)
                            _il.Emit(ILOpCode.Conv_U, 1, 1);
                        if (mode == EmitMode.Discard)
                            _il.Emit(ILOpCode.Pop, 1, 0);
                        return;
                    case BoundPointerIndirectionExpression pind:
                        if (mode == EmitMode.Discard)
                        {
                            EmitExpression(pind.Operand, EmitMode.Discard);
                            return;
                        }
                        EmitExpression(pind.Operand, EmitMode.Value);
                        EmitLoadIndirect(pind.Type);
                        return;
                    case BoundPointerElementAccessExpression pea:
                        EmitExpression(pea.Expression, EmitMode.Value);
                        EmitExpression(pea.Index, EmitMode.Value);
                        EmitPointerElementAddress(pea.Type, pea.Index.Type, subtract: false);
                        if (mode == EmitMode.Discard)
                        {
                            _il.Emit(ILOpCode.Pop, 1, 0);
                            return;
                        }
                        EmitLoadIndirect(pea.Type);
                        return;
                    case BoundStackAllocArrayCreationExpression sa:
                        EmitStackAlloc(sa, mode);
                        return;
                    case BoundStaticDataExpression sd:
                        if (mode == EmitMode.Value)
                        {
                            _il.EmitToken(ILOpCode.Ldsflda, _tokens.GetStaticDataFieldToken(StaticDataEncoding.Encode(sd)), 0, 1);
                            _il.Emit(ILOpCode.Conv_U, 1, 1);
                        }
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
                        if (mode == EmitMode.Value)
                            _il.EmitToken(ILOpCode.Sizeof, _tokens.GetTypeToken(so.OperandType), 0, 1);
                        return;
                    case BoundTypeOfExpression typeOf:
                        if (mode == EmitMode.Value)
                            EmitTypeObject(typeOf.OperandType);
                        return;
                    default:
                        throw new NotSupportedException($"Expression '{e.GetType().Name}' is not supported by the IL emitter.");
                }
            }

            private void EmitConstantValue(TypeSymbol type, object? value)
            {
                if (value is null)
                {
                    if (type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                    {
                        _il.EmitI4(0);
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                        return;
                    }
                    if (RequiresInitobj(type))
                    {
                        EmitDefaultValue(type);
                        return;
                    }
                    _il.Emit(ILOpCode.Ldnull, 0, 1);
                    return;
                }
                switch (value)
                {
                    case bool b: _il.EmitI4(b ? 1 : 0); break;
                    case char ch: _il.EmitI4(ch); break;
                    case sbyte sb: _il.EmitI4(sb); break;
                    case byte bb: _il.EmitI4(bb); break;
                    case short s: _il.EmitI4(s); break;
                    case ushort us: _il.EmitI4(us); break;
                    case int i: _il.EmitI4(i); break;
                    case uint ui: _il.EmitI4(unchecked((int)ui)); break;
                    case long l: _il.EmitI8(l); break;
                    case ulong ul: _il.EmitI8(unchecked((long)ul)); break;
                    case float f: _il.EmitR4(f); break;
                    case double d: _il.EmitR8(d); break;
                    case string str:
                        _il.EmitToken(ILOpCode.Ldstr, _tokens.GetUserStringToken(str), 0, 1);
                        return;
                    default:
                        throw new NotSupportedException($"Constant of type '{value.GetType().Name}' is not supported.");
                }

                if (value is not (long or ulong or float or double))
                {
                    var special = UnderlyingType(type).SpecialType;
                    if (special is SpecialType.System_Int64 or SpecialType.System_UInt64)
                        _il.Emit(value is uint or ushort or byte or char ? ILOpCode.Conv_U8 : ILOpCode.Conv_I8, 1, 1);
                    else if (special == SpecialType.System_IntPtr)
                        _il.Emit(ILOpCode.Conv_I, 1, 1);
                    else if (special == SpecialType.System_UIntPtr || type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                }
                else if (value is long or ulong)
                {
                    var special = UnderlyingType(type).SpecialType;
                    if (special == SpecialType.System_IntPtr)
                        _il.Emit(ILOpCode.Conv_I, 1, 1);
                    else if (special == SpecialType.System_UIntPtr || type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                }
            }
            private static bool RequiresInitobj(TypeSymbol type)
            {
                if (type is TypeParameterSymbol)
                    return true;
                if (type is not NamedTypeSymbol named || !named.IsValueType)
                    return false;
                if (named.TypeKind == TypeKind.Enum)
                    return false;
                return !IsPrimitiveValue(named.SpecialType);
            }
            private static bool IsPrimitiveValue(SpecialType special) => special is
                SpecialType.System_Boolean or SpecialType.System_Char or
                SpecialType.System_Int8 or SpecialType.System_UInt8 or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 or
                SpecialType.System_IntPtr or SpecialType.System_UIntPtr or
                SpecialType.System_Single or SpecialType.System_Double;
            private void EmitDefaultValue(TypeSymbol type)
            {
                if (RequiresInitobj(type))
                {
                    if (!_defaultValueTemps.TryGetValue(type, out int temp))
                    {
                        temp = AllocateTemp(type);
                        _defaultValueTemps.Add(type, temp);
                    }
                    _il.EmitLocal(ILOpCode.Ldloca, temp);
                    _il.EmitToken(ILOpCode.Initobj, _tokens.GetTypeToken(type), 1, 0);
                    _il.EmitLocal(ILOpCode.Ldloc, temp);
                    return;
                }
                if (type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                {
                    _il.EmitI4(0);
                    _il.Emit(ILOpCode.Conv_U, 1, 1);
                    return;
                }
                if (type.IsReferenceType || type is ByRefTypeSymbol || !type.IsValueType)
                {
                    _il.Emit(ILOpCode.Ldnull, 0, 1);
                    return;
                }
                switch (UnderlyingType(type).SpecialType)
                {
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                        _il.EmitI4(0);
                        _il.Emit(ILOpCode.Conv_I8, 1, 1);
                        return;
                    case SpecialType.System_IntPtr:
                        _il.EmitI4(0);
                        _il.Emit(ILOpCode.Conv_I, 1, 1);
                        return;
                    case SpecialType.System_UIntPtr:
                        _il.EmitI4(0);
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                        return;
                    case SpecialType.System_Single:
                        _il.EmitR4(0);
                        return;
                    case SpecialType.System_Double:
                        _il.EmitR8(0);
                        return;
                    default:
                        _il.EmitI4(0);
                        return;
                }
            }

            private void EmitLoadIndirect(TypeSymbol type)
            {
                ILOpCode op = LoadIndirectOpCode(type);
                if (op == ILOpCode.Ldobj)
                    _il.EmitToken(ILOpCode.Ldobj, _tokens.GetTypeToken(type), 1, 1);
                else
                    _il.Emit(op, 1, 1);
            }
            private void EmitStoreIndirect(TypeSymbol type)
            {
                ILOpCode op = StoreIndirectOpCode(type);
                if (op == ILOpCode.Stobj)
                    _il.EmitToken(ILOpCode.Stobj, _tokens.GetTypeToken(type), 2, 0);
                else
                    _il.Emit(op, 2, 0);
            }
            private static ILOpCode LoadIndirectOpCode(TypeSymbol type)
            {
                if (type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                    return ILOpCode.Ldind_I;
                if (type is TypeParameterSymbol)
                    return ILOpCode.Ldobj;
                if (type.IsReferenceType)
                    return ILOpCode.Ldind_Ref;
                return UnderlyingType(type).SpecialType switch
                {
                    SpecialType.System_Boolean or SpecialType.System_UInt8 => ILOpCode.Ldind_U1,
                    SpecialType.System_Int8 => ILOpCode.Ldind_I1,
                    SpecialType.System_Int16 => ILOpCode.Ldind_I2,
                    SpecialType.System_UInt16 or SpecialType.System_Char => ILOpCode.Ldind_U2,
                    SpecialType.System_Int32 => ILOpCode.Ldind_I4,
                    SpecialType.System_UInt32 => ILOpCode.Ldind_U4,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 => ILOpCode.Ldind_I8,
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr => ILOpCode.Ldind_I,
                    SpecialType.System_Single => ILOpCode.Ldind_R4,
                    SpecialType.System_Double => ILOpCode.Ldind_R8,
                    _ => ILOpCode.Ldobj,
                };
            }
            private static ILOpCode StoreIndirectOpCode(TypeSymbol type)
            {
                if (type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                    return ILOpCode.Stind_I;
                if (type is TypeParameterSymbol)
                    return ILOpCode.Stobj;
                if (type.IsReferenceType)
                    return ILOpCode.Stind_Ref;
                return UnderlyingType(type).SpecialType switch
                {
                    SpecialType.System_Boolean or SpecialType.System_UInt8 or SpecialType.System_Int8 => ILOpCode.Stind_I1,
                    SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => ILOpCode.Stind_I2,
                    SpecialType.System_Int32 or SpecialType.System_UInt32 => ILOpCode.Stind_I4,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 => ILOpCode.Stind_I8,
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr => ILOpCode.Stind_I,
                    SpecialType.System_Single => ILOpCode.Stind_R4,
                    SpecialType.System_Double => ILOpCode.Stind_R8,
                    _ => ILOpCode.Stobj,
                };
            }
            private static ILOpCode LoadElementOpCode(TypeSymbol type)
            {
                if (type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                    return ILOpCode.Ldelem_I;
                if (type is TypeParameterSymbol)
                    return ILOpCode.Ldelem;
                if (type.IsReferenceType)
                    return ILOpCode.Ldelem_Ref;
                return UnderlyingType(type).SpecialType switch
                {
                    SpecialType.System_Boolean or SpecialType.System_UInt8 => ILOpCode.Ldelem_U1,
                    SpecialType.System_Int8 => ILOpCode.Ldelem_I1,
                    SpecialType.System_Int16 => ILOpCode.Ldelem_I2,
                    SpecialType.System_UInt16 or SpecialType.System_Char => ILOpCode.Ldelem_U2,
                    SpecialType.System_Int32 => ILOpCode.Ldelem_I4,
                    SpecialType.System_UInt32 => ILOpCode.Ldelem_U4,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 => ILOpCode.Ldelem_I8,
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr => ILOpCode.Ldelem_I,
                    SpecialType.System_Single => ILOpCode.Ldelem_R4,
                    SpecialType.System_Double => ILOpCode.Ldelem_R8,
                    _ => ILOpCode.Ldelem,
                };
            }
            private static ILOpCode StoreElementOpCode(TypeSymbol type)
            {
                if (type is PointerTypeSymbol or FunctionPointerTypeSymbol)
                    return ILOpCode.Stelem_I;
                if (type is TypeParameterSymbol)
                    return ILOpCode.Stelem;
                if (type.IsReferenceType)
                    return ILOpCode.Stelem_Ref;
                return UnderlyingType(type).SpecialType switch
                {
                    SpecialType.System_Boolean or SpecialType.System_UInt8 or SpecialType.System_Int8 => ILOpCode.Stelem_I1,
                    SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => ILOpCode.Stelem_I2,
                    SpecialType.System_Int32 or SpecialType.System_UInt32 => ILOpCode.Stelem_I4,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 => ILOpCode.Stelem_I8,
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr => ILOpCode.Stelem_I,
                    SpecialType.System_Single => ILOpCode.Stelem_R4,
                    SpecialType.System_Double => ILOpCode.Stelem_R8,
                    _ => ILOpCode.Stelem,
                };
            }

            private void EmitLocal(BoundLocalExpression loc, EmitMode mode)
            {
                int idx = GetOrCreateLocal(loc.Local);
                if (mode != EmitMode.Value)
                    return;
                _il.EmitLocal(ILOpCode.Ldloc, idx);
                if (loc.Local.IsByRef)
                    EmitLoadIndirect(loc.Local.Type);
            }
            private void EmitParameter(BoundParameterExpression par, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return;
                _il.EmitArg(ILOpCode.Ldarg, GetArgIndex(par.Parameter));
                if (par.Parameter.Type is ByRefTypeSymbol br)
                    EmitLoadIndirect(br.ElementType);
            }
            private void EmitUnary(BoundUnaryExpression un, EmitMode mode)
            {
                EmitExpression(un.Operand, mode);
                if (mode == EmitMode.Discard)
                    return;
                switch (un.OperatorKind)
                {
                    case BoundUnaryOperatorKind.UnaryPlus:
                        return;
                    case BoundUnaryOperatorKind.UnaryMinus:
                        _il.Emit(ILOpCode.Neg, 1, 1);
                        return;
                    case BoundUnaryOperatorKind.LogicalNot:
                        EmitBooleanNot();
                        return;
                    case BoundUnaryOperatorKind.BitwiseNot:
                        _il.Emit(ILOpCode.Not, 1, 1);
                        return;
                    default:
                        throw new NotSupportedException($"Unary operator '{un.OperatorKind}' is not supported.");
                }
            }
            private void EmitBooleanNot()
            {
                _il.EmitI4(0);
                _il.Emit(ILOpCode.Ceq, 2, 1);
            }
            private static bool IsCheckedIntegerArithmetic(BoundBinaryExpression bin)
            {
                if (!bin.IsChecked || bin.OperatorKind is not (BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Subtract or BoundBinaryOperatorKind.Multiply))
                    return false;
                return UnderlyingType(bin.Type).SpecialType is
                    SpecialType.System_Int32 or SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or SpecialType.System_UInt64 or
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr;
            }
            private void EmitBinary(BoundBinaryExpression bin, EmitMode mode)
            {
                if (bin.OperatorKind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr)
                {
                    EmitShortCircuitLogical(bin, mode);
                    return;
                }
                if (TryEmitTypeEquality(bin, mode))
                    return;

                EmitExpression(bin.Left, EmitMode.Value);
                EmitExpression(bin.Right, EmitMode.Value);
                EmitBinaryOperator(bin);
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitBinaryOperator(BoundBinaryExpression bin)
            {
                var op = bin.OperatorKind;
                if (op == BoundBinaryOperatorKind.Subtract && bin.Left.Type is PointerTypeSymbol lpt && bin.Right.Type is PointerTypeSymbol)
                {
                    _il.Emit(ILOpCode.Sub, 2, 1);
                    if (!TryGetElementSize(lpt.PointedAtType, out int elementSize) || elementSize != 1)
                    {
                        EmitElementSize(lpt.PointedAtType, elementSize);
                        _il.Emit(ILOpCode.Div, 2, 1);
                    }
                    if (UnderlyingType(bin.Type).SpecialType == SpecialType.System_Int64)
                        _il.Emit(ILOpCode.Conv_I8, 1, 1);
                    return;
                }
                if (bin.Type is NamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType &&
                    op is BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Subtract)
                {
                    _il.EmitToken(ILOpCode.Call, _tokens.GetWellKnownMethodToken(
                        op == BoundBinaryOperatorKind.Add ? WellKnownMethod.DelegateCombine : WellKnownMethod.DelegateRemove), 2, 1);
                    _il.EmitToken(ILOpCode.Castclass, _tokens.GetTypeToken(delegateType), 1, 1);
                    return;
                }
                if (bin.Type is PointerTypeSymbol ptrType && op is BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Subtract)
                {
                    if (bin.Left.Type is not PointerTypeSymbol)
                        throw new NotSupportedException("Pointer arithmetic expects the pointer operand on the left.");
                    EmitPointerElementAddress(ptrType.PointedAtType, bin.Right.Type, subtract: op == BoundBinaryOperatorKind.Subtract);
                    return;
                }

                bool u = UsesUnsignedIntegerSemantics(bin.Left.Type);
                bool isFloat = IsFloatingPoint(bin.Left.Type);
                bool checkedArithmetic = IsCheckedIntegerArithmetic(bin);
                switch (op)
                {
                    case BoundBinaryOperatorKind.Add:
                        _il.Emit(checkedArithmetic ? (u ? ILOpCode.Add_Ovf_Un : ILOpCode.Add_Ovf) : ILOpCode.Add, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.Subtract:
                        _il.Emit(checkedArithmetic ? (u ? ILOpCode.Sub_Ovf_Un : ILOpCode.Sub_Ovf) : ILOpCode.Sub, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.Multiply:
                        _il.Emit(checkedArithmetic ? (u ? ILOpCode.Mul_Ovf_Un : ILOpCode.Mul_Ovf) : ILOpCode.Mul, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.Divide:
                        _il.Emit(u ? ILOpCode.Div_Un : ILOpCode.Div, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.Modulo:
                        _il.Emit(u ? ILOpCode.Rem_Un : ILOpCode.Rem, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.BitwiseAnd:
                        _il.Emit(ILOpCode.And, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.BitwiseOr:
                        _il.Emit(ILOpCode.Or, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.ExclusiveOr:
                        _il.Emit(ILOpCode.Xor, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.LeftShift:
                        EmitShiftCountMask(bin);
                        _il.Emit(ILOpCode.Shl, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.RightShift:
                        EmitShiftCountMask(bin);
                        _il.Emit(u ? ILOpCode.Shr_Un : ILOpCode.Shr, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.UnsignedRightShift:
                        EmitShiftCountMask(bin);
                        _il.Emit(ILOpCode.Shr_Un, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.Equals:
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.NotEquals:
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        EmitBooleanNot();
                        return;
                    case BoundBinaryOperatorKind.LessThan:
                        _il.Emit(u ? ILOpCode.Clt_Un : ILOpCode.Clt, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.GreaterThan:
                        _il.Emit(u ? ILOpCode.Cgt_Un : ILOpCode.Cgt, 2, 1);
                        return;
                    case BoundBinaryOperatorKind.LessThanOrEqual:
                        _il.Emit(u || isFloat ? ILOpCode.Cgt_Un : ILOpCode.Cgt, 2, 1);
                        EmitBooleanNot();
                        return;
                    case BoundBinaryOperatorKind.GreaterThanOrEqual:
                        _il.Emit(u || isFloat ? ILOpCode.Clt_Un : ILOpCode.Clt, 2, 1);
                        EmitBooleanNot();
                        return;
                    default:
                        throw new NotSupportedException($"Binary operator '{op}' is not supported.");
                }
            }
            private void EmitShiftCountMask(BoundBinaryExpression bin)
            {
                int mask = UnderlyingType(bin.Left.Type).SpecialType switch
                {
                    SpecialType.System_Int64 or SpecialType.System_UInt64 => 63,
                    SpecialType.System_IntPtr or SpecialType.System_UIntPtr => _target.PointerSize * 8 - 1,
                    _ => 31,
                };
                if (bin.Right.ConstantValueOpt.HasValue && bin.Right.ConstantValueOpt.Value is int count && count >= 0 && count <= mask)
                    return;
                _il.EmitI4(mask);
                _il.Emit(ILOpCode.And, 2, 1);
            }
            private void EmitShortCircuitLogical(BoundBinaryExpression bin, EmitMode mode)
            {
                bool isAnd = bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd;
                if (mode == EmitMode.Discard)
                {
                    var skip = _il.DefineLabel();
                    EmitExpression(bin.Left, EmitMode.Value);
                    _il.EmitBranch(isAnd ? ILOpCode.Brfalse : ILOpCode.Brtrue, skip, 1);
                    EmitExpression(bin.Right, EmitMode.Discard);
                    _il.MarkLabel(skip);
                    return;
                }

                var shortCircuit = _il.DefineLabel();
                var end = _il.DefineLabel();
                EmitExpression(bin.Left, EmitMode.Value);
                _il.EmitBranch(isAnd ? ILOpCode.Brfalse : ILOpCode.Brtrue, shortCircuit, 1);
                EmitExpression(bin.Right, EmitMode.Value);
                _il.EmitBranch(isAnd ? ILOpCode.Brfalse : ILOpCode.Brtrue, shortCircuit, 1);
                _il.EmitI4(isAnd ? 1 : 0);
                _il.EmitBranch(ILOpCode.Br, end, 0);
                _il.MarkLabel(shortCircuit);
                _il.EmitI4(isAnd ? 0 : 1);
                _il.MarkLabel(end);
            }
            private void EmitConditionalExpression(BoundConditionalExpression c, EmitMode mode)
            {
                var elseLabel = _il.DefineLabel();
                var end = _il.DefineLabel();
                EmitExpression(c.Condition, EmitMode.Value);
                _il.EmitBranch(ILOpCode.Brfalse, elseLabel, 1);
                EmitExpression(c.WhenTrue, mode);
                _il.EmitBranch(ILOpCode.Br, end, 0);
                _il.MarkLabel(elseLabel);
                EmitExpression(c.WhenFalse, mode);
                _il.MarkLabel(end);
            }

            private void EmitAssignment(BoundAssignmentExpression assignment, EmitMode mode)
            {
                if (assignment.HasErrors || assignment.Left.HasErrors || assignment.Right.HasErrors)
                {
                    if (mode == EmitMode.Value)
                        _il.EmitI4(0);
                    return;
                }
                switch (assignment.Left)
                {
                    case BoundArrayElementAccessExpression aea:
                        EmitArrayElementStore(aea, assignment, mode);
                        return;
                    case BoundMemberAccessExpression { Member: FieldSymbol fs } leftField:
                        EmitFieldStore(fs, leftField, assignment, mode);
                        return;
                    case BoundClosureAccessExpression closureAccess:
                        EmitExpression(closureAccess.Cell, EmitMode.Value);
                        _il.EmitI4(0);
                        EmitExpression(assignment.Right, EmitMode.Value);
                        int closureSpill = EmitKeepValue(assignment.Type, mode);
                        EmitBoxClosureValue(closureAccess.Type);
                        _il.Emit(ILOpCode.Stelem_Ref, 3, 0);
                        EmitReloadValue(closureSpill);
                        return;
                    case BoundLocalExpression leftLocal:
                        {
                            int idx = GetOrCreateLocal(leftLocal.Local);
                            if (leftLocal.Local.IsByRef && assignment.Right is not BoundRefExpression)
                            {
                                _il.EmitLocal(ILOpCode.Ldloc, idx);
                                EmitIndirectStore(leftLocal.Local.Type, assignment, mode);
                                return;
                            }
                            EmitExpression(assignment.Right, EmitMode.Value);
                            if (mode == EmitMode.Value)
                                _il.Emit(ILOpCode.Dup, 1, 2);
                            _il.EmitLocal(ILOpCode.Stloc, idx);
                            return;
                        }
                    case BoundParameterExpression leftPar:
                        {
                            int idx = GetArgIndex(leftPar.Parameter);
                            if (leftPar.Parameter.Type is ByRefTypeSymbol byRefPar && assignment.Right is not BoundRefExpression)
                            {
                                _il.EmitArg(ILOpCode.Ldarg, idx);
                                EmitIndirectStore(byRefPar.ElementType, assignment, mode);
                                return;
                            }
                            EmitExpression(assignment.Right, EmitMode.Value);
                            if (mode == EmitMode.Value)
                                _il.Emit(ILOpCode.Dup, 1, 2);
                            _il.EmitArg(ILOpCode.Starg, idx);
                            return;
                        }
                    default:
                        EmitLoadAddressOfLValue(assignment.Left);
                        EmitIndirectStore(assignment.Left.Type, assignment, mode);
                        return;
                }
            }
            private void EmitIndirectStore(TypeSymbol type, BoundAssignmentExpression assignment, EmitMode mode)
            {
                EmitExpression(assignment.Right, EmitMode.Value);
                int spill = EmitKeepValue(assignment.Type, mode);
                EmitStoreIndirect(type);
                EmitReloadValue(spill);
            }
            private int EmitKeepValue(TypeSymbol type, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return -1;
                int spill = AllocateTemp(type);
                _il.Emit(ILOpCode.Dup, 1, 2);
                _il.EmitLocal(ILOpCode.Stloc, spill);
                return spill;
            }
            private void EmitReloadValue(int spill)
            {
                if (spill >= 0)
                    _il.EmitLocal(ILOpCode.Ldloc, spill);
            }
            private void EmitFieldStore(FieldSymbol fs, BoundMemberAccessExpression leftField, BoundAssignmentExpression assignment, EmitMode mode)
            {
                if (fs.IsConst)
                    throw new NotSupportedException("Cannot assign to a const field in lowered form.");
                int tok = _tokens.GetFieldToken(fs);

                if (fs.Type is ByRefTypeSymbol byRefField && assignment.Right is not BoundRefExpression)
                {
                    EmitLoadAddressOfLValue(leftField);
                    EmitIndirectStore(byRefField.ElementType, assignment, mode);
                    return;
                }
                if (fs.IsStatic)
                {
                    EmitExpression(assignment.Right, EmitMode.Value);
                    if (mode == EmitMode.Value)
                        _il.Emit(ILOpCode.Dup, 1, 2);
                    _il.EmitToken(ILOpCode.Stsfld, tok, 1, 0);
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
                int spill = EmitKeepValue(assignment.Type, mode);
                _il.EmitToken(ILOpCode.Stfld, tok, 2, 0);
                EmitReloadValue(spill);
            }
            private void EmitArrayElementStore(BoundArrayElementAccessExpression aea, BoundAssignmentExpression assignment, EmitMode mode)
            {
                if (aea.Expression.Type is not ArrayTypeSymbol arrayType)
                    throw new InvalidOperationException("Array element access receiver is not an array type.");

                if (aea.Indices.Length == 1 && arrayType.Rank != 1)
                {
                    EmitLinearElementAddress(aea, arrayType);
                    EmitIndirectStore(aea.Type, assignment, mode);
                    return;
                }

                EmitExpression(aea.Expression, EmitMode.Value);
                for (int i = 0; i < aea.Indices.Length; i++)
                    EmitArrayIndex(aea.Indices[i]);
                EmitExpression(assignment.Right, EmitMode.Value);
                int spill = EmitKeepValue(assignment.Type, mode);
                if (arrayType.IsSZArray)
                {
                    EmitStoreElement(aea.Type);
                }
                else
                {
                    _il.EmitToken(ILOpCode.Call, _tokens.GetArrayMethodToken(arrayType, ArrayMethodKind.Set), arrayType.Rank + 2, 0);
                }
                EmitReloadValue(spill);
            }
            private void EmitStoreElement(TypeSymbol elementType)
            {
                ILOpCode op = StoreElementOpCode(elementType);
                if (op == ILOpCode.Stelem)
                    _il.EmitToken(ILOpCode.Stelem, _tokens.GetTypeToken(elementType), 3, 0);
                else
                    _il.Emit(op, 3, 0);
            }
            private void EmitLoadElement(TypeSymbol elementType)
            {
                ILOpCode op = LoadElementOpCode(elementType);
                if (op == ILOpCode.Ldelem)
                    _il.EmitToken(ILOpCode.Ldelem, _tokens.GetTypeToken(elementType), 2, 1);
                else
                    _il.Emit(op, 2, 1);
            }
            private void EmitLinearElementAddress(BoundArrayElementAccessExpression aea, ArrayTypeSymbol arrayType)
            {
                EmitExpression(aea.Expression, EmitMode.Value);
                _il.EmitToken(ILOpCode.Call, _tokens.GetWellKnownMethodToken(WellKnownMethod.ArrayDataReference), 1, 1);
                EmitExpression(aea.Indices[0], EmitMode.Value);
                EmitScaledOffset(arrayType.ElementType, aea.Indices[0].Type);
                _il.Emit(ILOpCode.Add, 2, 1);
            }
            private void EmitArrayIndex(BoundExpression index)
            {
                EmitExpression(index, EmitMode.Value);
                switch (UnderlyingType(index.Type).SpecialType)
                {
                    case SpecialType.System_Int64:
                    case SpecialType.System_IntPtr:
                        _il.Emit(ILOpCode.Conv_Ovf_I4, 1, 1);
                        break;
                    case SpecialType.System_UInt64:
                    case SpecialType.System_UIntPtr:
                        _il.Emit(ILOpCode.Conv_Ovf_I4_Un, 1, 1);
                        break;
                }
            }
            private void EmitArrayCreation(BoundArrayCreationExpression ac, EmitMode mode)
            {
                if (ac.Type is not ArrayTypeSymbol at)
                    throw new InvalidOperationException("BoundArrayCreationExpression.Type is not an array type.");

                if (at.IsSZArray)
                {
                    if (ac.DimensionSizes.Length == 0)
                        _il.EmitI4(ac.InitializerOpt?.Elements.Length ?? 0);
                    else
                        EmitExpression(ac.DimensionSizes[0], EmitMode.Value);
                    _il.EmitToken(ILOpCode.Newarr, _tokens.GetTypeToken(ac.ElementType), 1, 1);
                    if (ac.InitializerOpt is not null)
                    {
                        var elems = ac.InitializerOpt.Elements;
                        for (int i = 0; i < elems.Length; i++)
                        {
                            _il.Emit(ILOpCode.Dup, 1, 2);
                            _il.EmitI4(i);
                            EmitExpression(elems[i], EmitMode.Value);
                            EmitStoreElement(ac.ElementType);
                        }
                    }
                }
                else
                {
                    if (ac.DimensionSizes.Length != at.Rank)
                        throw new InvalidOperationException("Multidimensional array dimension count does not match its rank.");
                    for (int i = 0; i < ac.DimensionSizes.Length; i++)
                        EmitExpression(ac.DimensionSizes[i], EmitMode.Value);
                    _il.EmitToken(ILOpCode.Newobj, _tokens.GetArrayMethodToken(at, ArrayMethodKind.Constructor), at.Rank, 1);
                    if (ac.InitializerOpt is not null)
                        EmitMdArrayInitializer(at, ac.InitializerOpt);
                }

                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitMdArrayInitializer(ArrayTypeSymbol arrayType, BoundArrayInitializerExpression initializer)
            {
                var indices = new int[arrayType.Rank];
                int setToken = _tokens.GetArrayMethodToken(arrayType, ArrayMethodKind.Set);
                EmitLevel(initializer, 0);

                void EmitLevel(BoundArrayInitializerExpression current, int dimension)
                {
                    for (int i = 0; i < current.Elements.Length; i++)
                    {
                        indices[dimension] = i;
                        if (dimension + 1 < arrayType.Rank)
                        {
                            if (current.Elements[i] is not BoundArrayInitializerExpression nested)
                                throw new InvalidOperationException("Invalid multidimensional array initializer shape.");
                            EmitLevel(nested, dimension + 1);
                            continue;
                        }
                        _il.Emit(ILOpCode.Dup, 1, 2);
                        for (int d = 0; d < indices.Length; d++)
                            _il.EmitI4(indices[d]);
                        EmitExpression(current.Elements[i], EmitMode.Value);
                        _il.EmitToken(ILOpCode.Call, setToken, arrayType.Rank + 2, 0);
                    }
                }
            }
            private void EmitArrayElementAccess(BoundArrayElementAccessExpression aea, EmitMode mode)
            {
                if (aea.Expression.Type is not ArrayTypeSymbol arrayType)
                    throw new InvalidOperationException("Array element access receiver is not an array type.");

                if (aea.Indices.Length == 1 && arrayType.Rank != 1)
                {
                    EmitLinearElementAddress(aea, arrayType);
                    EmitLoadIndirect(aea.Type);
                }
                else
                {
                    EmitExpression(aea.Expression, EmitMode.Value);
                    for (int i = 0; i < aea.Indices.Length; i++)
                        EmitArrayIndex(aea.Indices[i]);
                    if (arrayType.IsSZArray)
                        EmitLoadElement(aea.Type);
                    else
                        _il.EmitToken(ILOpCode.Call, _tokens.GetArrayMethodToken(arrayType, ArrayMethodKind.Get), arrayType.Rank + 1, 1);
                }

                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }

            private void EmitMemberAccess(BoundMemberAccessExpression ma, EmitMode mode)
            {
                if (ma.ConstantValueOpt.HasValue)
                {
                    if (mode == EmitMode.Value)
                        EmitConstantValue(ma.Type, ma.ConstantValueOpt.Value);
                    return;
                }
                if (ma.Member is not FieldSymbol fs)
                    throw new NotSupportedException("BoundMemberAccessExpression must be lowered to FieldSymbol access before emission.");
                if (fs.IsConst)
                {
                    if (!fs.ConstantValueOpt.HasValue)
                        throw new InvalidOperationException($"Const field '{fs.Name}' has no constant value in metadata.");
                    if (mode == EmitMode.Value)
                        EmitConstantValue(fs.Type, fs.ConstantValueOpt.Value);
                    return;
                }

                int tok = _tokens.GetFieldToken(fs);
                if (fs.IsStatic)
                {
                    _il.EmitToken(ILOpCode.Ldsfld, tok, 0, 1);
                }
                else
                {
                    if (ma.ReceiverOpt is null)
                        throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");
                    EmitFieldReceiver(ma.ReceiverOpt);
                    _il.EmitToken(ILOpCode.Ldfld, tok, 1, 1);
                }

                if (fs.Type is ByRefTypeSymbol byRefField)
                    EmitLoadIndirect(byRefField.ElementType);
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitFieldReceiver(BoundExpression receiver)
            {
                if (receiver.Type.IsValueType)
                    EmitReceiverAddress(receiver);
                else
                    EmitExpression(receiver, EmitMode.Value);
            }

            private void EmitLambda(BoundLambdaExpression lambda, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                    return;
                if (lambda.Method is null)
                    throw new InvalidOperationException("Lambda has no synthesized target method.");
                if (!lambda.Method.IsStatic)
                    throw new NotSupportedException("Only lowered static lambda target methods are supported by the IL emitter.");

                if (lambda.TargetOpt is not null)
                    EmitExpression(lambda.TargetOpt, EmitMode.Value);
                else
                    _il.Emit(ILOpCode.Ldnull, 0, 1);
                _il.EmitToken(ILOpCode.Ldftn, GetMethodReferenceToken(lambda.Method), 0, 1);
                _il.EmitToken(ILOpCode.Newobj, _tokens.GetMethodToken(FindDelegateConstructor((NamedTypeSymbol)lambda.Type)), 2, 1);
            }
            private int GetMethodReferenceToken(MethodSymbol method)
            {
                var typeParameters = method.TypeParameters;
                if (typeParameters.IsDefaultOrEmpty)
                    return _tokens.GetMethodToken(method);
                var arguments = ImmutableArray.CreateBuilder<TypeSymbol>(typeParameters.Length);
                for (int i = 0; i < typeParameters.Length; i++)
                    arguments.Add(typeParameters[i]);
                return _tokens.GetGenericMethodInstanceToken(method, arguments.MoveToImmutable());
            }
            private static MethodSymbol FindDelegateConstructor(NamedTypeSymbol delegateType)
            {
                var members = delegateType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol { IsConstructor: true, IsStatic: false } ctor && ctor.Parameters.Length == 2)
                        return ctor;
                }
                throw new InvalidOperationException($"Delegate type '{delegateType.Name}' has no (object, native int) constructor.");
            }

            private void EmitClosureCellCreation(BoundClosureCellCreationExpression cell, EmitMode mode)
            {
                _il.EmitI4(1);
                _il.EmitToken(ILOpCode.Newarr, _tokens.GetTypeToken(((ArrayTypeSymbol)cell.Type).ElementType), 1, 1);
                _il.Emit(ILOpCode.Dup, 1, 2);
                _il.EmitI4(0);
                EmitExpression(cell.InitialValue, EmitMode.Value);
                EmitBoxClosureValue(cell.ValueType);
                _il.Emit(ILOpCode.Stelem_Ref, 3, 0);
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitClosureCreation(BoundClosureCreationExpression closure, EmitMode mode)
            {
                _il.EmitI4(closure.Cells.Length);
                _il.EmitToken(ILOpCode.Newarr, _tokens.GetTypeToken(((ArrayTypeSymbol)closure.Type).ElementType), 1, 1);
                for (int i = 0; i < closure.Cells.Length; i++)
                {
                    _il.Emit(ILOpCode.Dup, 1, 2);
                    _il.EmitI4(i);
                    EmitExpression(closure.Cells[i], EmitMode.Value);
                    _il.Emit(ILOpCode.Stelem_Ref, 3, 0);
                }
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitBoxClosureValue(TypeSymbol valueType)
            {
                if (valueType.IsValueType || valueType is TypeParameterSymbol)
                    _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(valueType), 1, 1);
            }
            private void EmitUnboxClosureValue(TypeSymbol valueType)
            {
                if (valueType.SpecialType == SpecialType.System_Object)
                    return;
                if (valueType.IsValueType || valueType is TypeParameterSymbol)
                    _il.EmitToken(ILOpCode.Unbox_Any, _tokens.GetTypeToken(valueType), 1, 1);
                else
                    _il.EmitToken(ILOpCode.Castclass, _tokens.GetTypeToken(valueType), 1, 1);
            }

            private static bool IsInNamespace(NamedTypeSymbol type, params string[] nsParts)
            {
                Symbol? s = type.ContainingSymbol;
                for (int i = nsParts.Length - 1; i >= 0; i--)
                {
                    if (s is not NamespaceSymbol ns || !string.Equals(ns.Name, nsParts[i], StringComparison.Ordinal))
                        return false;
                    s = ns.ContainingSymbol;
                }
                return s is null || (s is NamespaceSymbol g && (g.IsGlobalNamespace || string.IsNullOrEmpty(g.Name)));
            }
            private static bool IsDelegateInvokeCall(BoundCallExpression call)
                => !call.Method.IsStatic &&
                   StringComparer.Ordinal.Equals(call.Method.Name, "Invoke") &&
                   call.Method.ContainingSymbol is NamedTypeSymbol { TypeKind: TypeKind.Delegate };

            private void EmitCall(BoundCallExpression call, EmitMode mode)
            {
                if (TryEmitIntrinsic(call, mode))
                    return;
                if (TryEmitTypeOfPredicate(call, mode))
                    return;

                var method = call.Method;
                TypeSymbol? constrainedType = null;
                bool thisIsManagedByRef = false;
                if (!method.IsStatic)
                {
                    var containingType = method.ContainingSymbol as NamedTypeSymbol;
                    bool methodDeclaredOnRefType = containingType is not null && containingType.IsReferenceType;
                    if (call.ReceiverOpt is null)
                    {
                        if (_method.IsStatic)
                            throw new InvalidOperationException($"Instance call to '{method.Name}' without receiver (in static method).");
                        var thisType = _method.ContainingSymbol as NamedTypeSymbol
                            ?? throw new InvalidOperationException("Cannot resolve implicit 'this' receiver type.");
                        _il.EmitArg(ILOpCode.Ldarg, 0);
                        if (thisType.IsValueType)
                        {
                            if (methodDeclaredOnRefType)
                                EmitBoxAddress(thisType);
                            else
                                thisIsManagedByRef = true;
                        }
                    }
                    else
                    {
                        var recv = call.ReceiverOpt;
                        // A reference-type method on a value or type parameter is called through constrained. on its address, not on a box.
                        if (methodDeclaredOnRefType &&
                            recv is BoundConversionExpression { Conversion.Kind: ConversionKind.Boxing } boxing &&
                            (boxing.Operand.Type is TypeParameterSymbol || boxing.Operand.Type.IsValueType))
                        {
                            recv = boxing.Operand;
                        }
                        if ((recv.Type is TypeParameterSymbol || recv.Type.IsValueType) && methodDeclaredOnRefType)
                        {
                            EmitReceiverAddress(recv);
                            constrainedType = recv.Type;
                        }
                        else if (recv.Type.IsValueType)
                        {
                            thisIsManagedByRef = true;
                            EmitReceiverAddress(recv);
                        }
                        else
                        {
                            EmitExpression(recv, EmitMode.Value);
                        }
                    }
                }

                // An extension property accessor takes the property's receiver as its first argument
                bool receiverIsArgument = method.IsStatic && call.ReceiverOpt is not null && method.ExtensionMember is { IsStatic: false };
                if (receiverIsArgument)
                {
                    if (method.Parameters[0].Type is ByRefTypeSymbol)
                        EmitReceiverAddress(call.ReceiverOpt!);
                    else
                        EmitExpression(call.ReceiverOpt!, EmitMode.Value);
                }

                var args = call.Arguments;
                for (int i = 0; i < args.Length; i++)
                    EmitExpression(args[i], EmitMode.Value);

                int pop = args.Length + (method.IsStatic && !receiverIsArgument ? 0 : 1);
                int push = IsVoid(method.ReturnType) ? 0 : 1;
                bool isBaseReceiver = call.ReceiverOpt is BoundBaseExpression;
                bool requiresVirtualDispatch =
                    !method.IsStatic &&
                    (method.ContainingSymbol is NamedTypeSymbol { TypeKind: TypeKind.Interface } ||
                     method.IsVirtual || method.IsAbstract || method.IsOverride || IsDelegateInvokeCall(call));

                if (method.IsStatic && call.ConstrainedToTypeOpt is TypeSymbol staticConstraint)
                {
                    _il.EmitPrefix(ILOpCode.Constrained, _tokens.GetTypeToken(staticConstraint));
                    _il.EmitToken(ILOpCode.Call, _tokens.GetMethodToken(method), pop, push);
                }
                else if (constrainedType is not null)
                {
                    _il.EmitPrefix(ILOpCode.Constrained, _tokens.GetTypeToken(constrainedType));
                    _il.EmitToken(ILOpCode.Callvirt, _tokens.GetMethodToken(method), pop, push);
                }
                else
                {
                    ILOpCode op = thisIsManagedByRef || isBaseReceiver || !requiresVirtualDispatch ? ILOpCode.Call : ILOpCode.Callvirt;
                    _il.EmitToken(op, _tokens.GetMethodToken(method), pop, push);
                }

                if (mode == EmitMode.Discard && push == 1)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitBoxAddress(NamedTypeSymbol valueType)
            {
                EmitLoadIndirect(valueType);
                _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(valueType), 1, 1);
            }
            private void EmitReceiverAddress(BoundExpression receiver)
            {
                if (IsAddressableValueTypeReceiver(receiver))
                {
                    EmitLoadAddressOfLValue(receiver);
                    return;
                }
                int spill = AllocateTemp(receiver.Type);
                EmitExpression(receiver, EmitMode.Value);
                _il.EmitLocal(ILOpCode.Stloc, spill);
                _il.EmitLocal(ILOpCode.Ldloca, spill);
            }
            private void EmitFunctionPointerInvocation(BoundFunctionPointerInvocationExpression expression, EmitMode mode)
            {
                // The pointer is evaluated first, as C# requires, but calli takes it above the arguments.
                EmitExpression(expression.InvokedExpression, EmitMode.Value);
                int pointer = AllocateTemp(expression.InvokedExpression.Type);
                _il.EmitLocal(ILOpCode.Stloc, pointer);
                var arguments = expression.Arguments;
                for (int i = 0; i < arguments.Length; i++)
                    EmitExpression(arguments[i], EmitMode.Value);
                _il.EmitLocal(ILOpCode.Ldloc, pointer);
                int push = IsVoid(expression.FunctionPointerType.ReturnType) && expression.FunctionPointerType.ReturnRefKind == FunctionPointerRefKind.None ? 0 : 1;
                _il.EmitToken(ILOpCode.Calli, _tokens.GetCalliSignatureToken(expression.FunctionPointerType), arguments.Length + 1, push);
                if (mode == EmitMode.Discard && push != 0)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }

            private bool TryEmitTypeOfPredicate(BoundCallExpression call, EmitMode mode)
            {
                if (call.ReceiverOpt is not BoundTypeOfExpression typeOf || call.Arguments.Length != 0)
                    return false;
                if (!TryGetTypePredicate(call.Method, out var predicate))
                    return false;
                if (mode == EmitMode.Discard)
                    return true;

                if (TryGetCompileTimeTypePredicate(predicate, typeOf.OperandType, out bool value))
                {
                    _il.EmitI4(value ? 1 : 0);
                    return true;
                }
                EmitTypeObject(typeOf.OperandType);
                _il.EmitToken(ILOpCode.Callvirt, _tokens.GetWellKnownMethodToken(predicate), 1, 1);
                return true;
            }
            private void EmitTypeObject(TypeSymbol type)
            {
                _il.EmitToken(ILOpCode.Ldtoken, _tokens.GetTypeToken(type), 0, 1);
                _il.EmitToken(ILOpCode.Call, _tokens.GetWellKnownMethodToken(WellKnownMethod.TypeGetTypeFromHandle), 1, 1);
            }
            private static bool TryGetTypePredicate(MethodSymbol method, out WellKnownMethod predicate)
            {
                predicate = default;
                if (method.IsStatic ||
                    method.Parameters.Length != 0 ||
                    method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                    method.ContainingSymbol is not NamedTypeSymbol containingType ||
                    !string.Equals(containingType.Name, "Type", StringComparison.Ordinal) ||
                    !IsInNamespace(containingType, "System"))
                {
                    return false;
                }
                switch (method.Name)
                {
                    case "get_IsValueType":
                        predicate = WellKnownMethod.TypeIsValueType;
                        return true;
                    case "get_IsPrimitive":
                        predicate = WellKnownMethod.TypeIsPrimitive;
                        return true;
                    case "get_IsEnum":
                        predicate = WellKnownMethod.TypeIsEnum;
                        return true;
                    default:
                        return false;
                }
            }
            private static bool TryGetCompileTimeTypePredicate(WellKnownMethod predicate, TypeSymbol type, out bool value)
            {
                if (predicate == WellKnownMethod.TypeIsValueType)
                {
                    if (type is TypeParameterSymbol typeParameter)
                    {
                        value = (typeParameter.GenericConstraint & GenericConstraintsFlags.StructConstraint) != 0;
                        return value;
                    }
                    value = type.IsValueType;
                    return true;
                }
                if (type is TypeParameterSymbol)
                {
                    value = false;
                    return false;
                }
                value = predicate == WellKnownMethod.TypeIsPrimitive
                    ? IsPrimitiveValue(type.SpecialType)
                    : type is NamedTypeSymbol { TypeKind: TypeKind.Enum };
                return true;
            }
            private bool TryEmitTypeEquality(BoundBinaryExpression bin, EmitMode mode)
            {
                if (bin.OperatorKind is not (BoundBinaryOperatorKind.Equals or BoundBinaryOperatorKind.NotEquals))
                    return false;
                bool negate = bin.OperatorKind == BoundBinaryOperatorKind.NotEquals;

                if (bin.Left is BoundTypeOfExpression leftTypeOf && bin.Right is BoundTypeOfExpression rightTypeOf)
                {
                    if (mode == EmitMode.Discard)
                        return true;
                    if (AreSameRuntimeTypeIdentity(leftTypeOf.OperandType, rightTypeOf.OperandType))
                    {
                        _il.EmitI4(negate ? 0 : 1);
                        return true;
                    }
                    if (!ContainsTypeParameter(leftTypeOf.OperandType) && !ContainsTypeParameter(rightTypeOf.OperandType) &&
                        leftTypeOf.OperandType is not TupleTypeSymbol && rightTypeOf.OperandType is not TupleTypeSymbol)
                    {
                        _il.EmitI4(negate ? 1 : 0);
                        return true;
                    }
                    EmitTypeObject(leftTypeOf.OperandType);
                    EmitTypeObject(rightTypeOf.OperandType);
                    _il.Emit(ILOpCode.Ceq, 2, 1);
                    if (negate)
                        EmitBooleanNot();
                    return true;
                }
                if (TryGetObjectGetTypeCall(bin.Left, out var leftGetType) && bin.Right is BoundTypeOfExpression rightType)
                {
                    EmitObjectGetTypeEquality(leftGetType, rightType.OperandType, negate, mode);
                    return true;
                }
                if (bin.Left is BoundTypeOfExpression leftType && TryGetObjectGetTypeCall(bin.Right, out var rightGetType))
                {
                    EmitObjectGetTypeEquality(rightGetType, leftType.OperandType, negate, mode);
                    return true;
                }
                return false;
            }
            private void EmitObjectGetTypeEquality(BoundCallExpression getTypeCall, TypeSymbol targetType, bool negate, EmitMode mode)
            {
                TypeSymbol receiverType = getTypeCall.ReceiverOpt?.Type
                    ?? _method.ContainingSymbol as NamedTypeSymbol
                    ?? throw new InvalidOperationException("Cannot resolve implicit GetType receiver type.");
                if (receiverType.IsValueType && !IsSystemNullableValueType(receiverType))
                {
                    if (getTypeCall.ReceiverOpt is not null)
                        EmitExpression(getTypeCall.ReceiverOpt, EmitMode.Discard);
                    if (mode == EmitMode.Discard)
                        return;
                    bool equal = AreSameRuntimeTypeIdentity(receiverType, targetType);
                    if (!equal && (ContainsTypeParameter(receiverType) || ContainsTypeParameter(targetType)))
                    {
                        EmitTypeObject(receiverType);
                        EmitTypeObject(targetType);
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        if (negate)
                            EmitBooleanNot();
                        return;
                    }
                    _il.EmitI4(equal != negate ? 1 : 0);
                    return;
                }

                if (getTypeCall.ReceiverOpt is not null)
                {
                    EmitExpression(getTypeCall.ReceiverOpt, EmitMode.Value);
                }
                else
                {
                    if (_method.IsStatic)
                        throw new InvalidOperationException("GetType call without receiver in a static method.");
                    _il.EmitArg(ILOpCode.Ldarg, 0);
                    if (receiverType.IsValueType)
                        EmitLoadIndirect(receiverType);
                }
                if (receiverType.IsValueType)
                    _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(receiverType), 1, 1);
                _il.EmitToken(ILOpCode.Callvirt, _tokens.GetWellKnownMethodToken(WellKnownMethod.ObjectGetType), 1, 1);
                EmitTypeObject(targetType);
                _il.Emit(ILOpCode.Ceq, 2, 1);
                if (negate)
                    EmitBooleanNot();
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private static bool TryGetObjectGetTypeCall(BoundExpression expression, out BoundCallExpression call)
            {
                call = null!;
                if (expression is not BoundCallExpression candidate || candidate.Arguments.Length != 0)
                    return false;
                var definition = candidate.Method.OriginalDefinition;
                if (definition.IsStatic ||
                    definition.Parameters.Length != 0 ||
                    !string.Equals(definition.Name, "GetType", StringComparison.Ordinal) ||
                    definition.ContainingSymbol is not NamedTypeSymbol containingType ||
                    !string.Equals(containingType.Name, "Object", StringComparison.Ordinal) ||
                    !IsInNamespace(containingType, "System") ||
                    definition.ReturnType is not NamedTypeSymbol returnType ||
                    !string.Equals(returnType.Name, "Type", StringComparison.Ordinal) ||
                    !IsInNamespace(returnType, "System"))
                {
                    return false;
                }
                call = candidate;
                return true;
            }
            private static bool ContainsTypeParameter(TypeSymbol type)
            {
                switch (type)
                {
                    case TypeParameterSymbol:
                        return true;
                    case ArrayTypeSymbol array:
                        return ContainsTypeParameter(array.ElementType);
                    case PointerTypeSymbol pointer:
                        return ContainsTypeParameter(pointer.PointedAtType);
                    case ByRefTypeSymbol byRef:
                        return ContainsTypeParameter(byRef.ElementType);
                    case FunctionPointerTypeSymbol functionPointer:
                        if (ContainsTypeParameter(functionPointer.ReturnType))
                            return true;
                        for (int i = 0; i < functionPointer.Parameters.Length; i++)
                        {
                            if (ContainsTypeParameter(functionPointer.Parameters[i].Type))
                                return true;
                        }
                        return false;
                    case TupleTypeSymbol tuple:
                        for (int i = 0; i < tuple.ElementTypes.Length; i++)
                        {
                            if (ContainsTypeParameter(tuple.ElementTypes[i]))
                                return true;
                        }
                        return false;
                    case NamedTypeSymbol named:
                        if (named.ContainingSymbol is NamedTypeSymbol containingType && ContainsTypeParameter(containingType))
                            return true;
                        var arguments = named.TypeArguments;
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            if (ContainsTypeParameter(arguments[i]))
                                return true;
                        }
                        return false;
                    default:
                        return false;
                }
            }
            private static bool AreSameRuntimeTypeIdentity(TypeSymbol left, TypeSymbol right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left.SpecialType != SpecialType.None || right.SpecialType != SpecialType.None)
                    return left.SpecialType == right.SpecialType;
                if (left.Kind != right.Kind)
                    return false;

                switch (left, right)
                {
                    case (ArrayTypeSymbol a, ArrayTypeSymbol b):
                        return a.Rank == b.Rank && a.IsSZArray == b.IsSZArray && AreSameRuntimeTypeIdentity(a.ElementType, b.ElementType);
                    case (PointerTypeSymbol a, PointerTypeSymbol b):
                        return AreSameRuntimeTypeIdentity(a.PointedAtType, b.PointedAtType);
                    case (ByRefTypeSymbol a, ByRefTypeSymbol b):
                        return AreSameRuntimeTypeIdentity(a.ElementType, b.ElementType);
                    case (FunctionPointerTypeSymbol a, FunctionPointerTypeSymbol b):
                        if (a.CallingConvention != b.CallingConvention ||
                            (a.ReturnRefKind == FunctionPointerRefKind.None) != (b.ReturnRefKind == FunctionPointerRefKind.None) ||
                            a.Parameters.Length != b.Parameters.Length ||
                            !AreSameRuntimeTypeIdentity(a.ReturnType, b.ReturnType))
                        {
                            return false;
                        }
                        for (int i = 0; i < a.Parameters.Length; i++)
                        {
                            if ((a.Parameters[i].RefKind == FunctionPointerRefKind.None) != (b.Parameters[i].RefKind == FunctionPointerRefKind.None) ||
                                !AreSameRuntimeTypeIdentity(a.Parameters[i].Type, b.Parameters[i].Type))
                            {
                                return false;
                            }
                        }
                        return true;
                    case (TupleTypeSymbol a, TupleTypeSymbol b):
                        if (a.ElementTypes.Length != b.ElementTypes.Length)
                            return false;
                        for (int i = 0; i < a.ElementTypes.Length; i++)
                        {
                            if (!AreSameRuntimeTypeIdentity(a.ElementTypes[i], b.ElementTypes[i]))
                                return false;
                        }
                        return true;
                    case (NamedTypeSymbol a, NamedTypeSymbol b):
                        if (!ReferenceEquals(a.OriginalDefinition, b.OriginalDefinition))
                            return false;
                        var aContaining = a.ContainingSymbol as NamedTypeSymbol;
                        var bContaining = b.ContainingSymbol as NamedTypeSymbol;
                        if ((aContaining is not null || bContaining is not null) &&
                            (aContaining is null || bContaining is null || !AreSameRuntimeTypeIdentity(aContaining, bContaining)))
                        {
                            return false;
                        }
                        var aArguments = a.TypeArguments;
                        var bArguments = b.TypeArguments;
                        if (aArguments.Length != bArguments.Length)
                            return false;
                        for (int i = 0; i < aArguments.Length; i++)
                        {
                            if (!AreSameRuntimeTypeIdentity(aArguments[i], bArguments[i]))
                                return false;
                        }
                        return true;
                    case (TypeParameterSymbol a, TypeParameterSymbol b):
                        return a.Ordinal == b.Ordinal && ReferenceEquals(a.ContainingSymbol, b.ContainingSymbol);
                    default:
                        return false;
                }
            }

            private bool TryEmitIntrinsic(BoundCallExpression call, EmitMode mode)
            {
                var def = call.Method.OriginalDefinition;
                if (!def.IsStatic || def.ContainingSymbol is not NamedTypeSymbol containingType)
                    return false;
                if (containingType.Name != "Unsafe" || !IsInNamespace(containingType, "System", "Runtime", "CompilerServices"))
                    return false;

                var ps = call.Method.Parameters;
                var args = call.Arguments;
                switch (def.Name)
                {
                    case "SizeOf" when ps.Length == 0 && call.Method.TypeArguments.Length == 1:
                        if (mode == EmitMode.Value)
                            _il.EmitToken(ILOpCode.Sizeof, _tokens.GetTypeToken(call.Method.TypeArguments[0]), 0, 1);
                        return true;

                    case "As" when ps.Length == 1 && (ps[0].Type.SpecialType == SpecialType.System_Object || ps[0].Type is ByRefTypeSymbol):
                    case "AsRef" when ps.Length == 1 && ps[0].Type is PointerTypeSymbol or ByRefTypeSymbol:
                        EmitExpression(args[0], mode);
                        return true;

                    case "AsPointer" when ps.Length == 1 && ps[0].Type is ByRefTypeSymbol:
                        EmitExpression(args[0], EmitMode.Value);
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                        break;

                    case "ReadUnaligned" when ps.Length == 1 &&
                        (ps[0].Type is ByRefTypeSymbol { ElementType.SpecialType: SpecialType.System_UInt8 } ||
                         ps[0].Type is PointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void }):
                        EmitExpression(args[0], EmitMode.Value);
                        _il.EmitPrefix(ILOpCode.Unaligned, 1);
                        _il.EmitToken(ILOpCode.Ldobj, _tokens.GetTypeToken(call.Type), 1, 1);
                        break;

                    case "WriteUnaligned" when ps.Length == 2 &&
                        ps[0].Type is ByRefTypeSymbol { ElementType.SpecialType: SpecialType.System_UInt8 } &&
                        call.Method.TypeArguments.Length == 1:
                        EmitExpression(args[0], EmitMode.Value);
                        EmitExpression(args[1], EmitMode.Value);
                        _il.EmitPrefix(ILOpCode.Unaligned, 1);
                        _il.EmitToken(ILOpCode.Stobj, _tokens.GetTypeToken(call.Method.TypeArguments[0]), 2, 0);
                        return true;

                    case "CopyBlockUnaligned" when ps.Length == 3 && ps[2].Type.SpecialType == SpecialType.System_UInt32 && IsVoid(def.ReturnType):
                        return TryEmitCopyBlock(call);

                    case "Add" when ps.Length == 2 &&
                        (ps[0].Type is ByRefTypeSymbol || ps[0].Type is PointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void }) &&
                        ps[1].Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr:
                        {
                            TypeSymbol elementType = ps[0].Type is ByRefTypeSymbol br ? br.ElementType : ((PointerTypeSymbol)ps[0].Type).PointedAtType;
                            EmitExpression(args[0], EmitMode.Value);
                            EmitExpression(args[1], EmitMode.Value);
                            if (ps[1].Type.SpecialType == SpecialType.System_Int32)
                                _il.Emit(ILOpCode.Conv_I, 1, 1);
                            _il.EmitToken(ILOpCode.Sizeof, _tokens.GetTypeToken(elementType), 0, 1);
                            _il.Emit(ILOpCode.Mul, 2, 1);
                            _il.Emit(ILOpCode.Add, 2, 1);
                            break;
                        }

                    case "ByteOffset" when ps.Length == 2 && ps[0].Type is ByRefTypeSymbol && ps[1].Type is ByRefTypeSymbol:
                        EmitExpression(args[0], EmitMode.Value);
                        EmitExpression(args[1], EmitMode.Value);
                        _il.Emit(ILOpCode.Sub, 2, 1);
                        _il.Emit(ILOpCode.Neg, 1, 1);
                        break;

                    case "AddByteOffset" when ps.Length == 2 && ps[0].Type is ByRefTypeSymbol &&
                        ps[1].Type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr:
                        EmitExpression(args[0], EmitMode.Value);
                        EmitExpression(args[1], EmitMode.Value);
                        _il.Emit(ILOpCode.Add, 2, 1);
                        break;

                    case "NullRef" when ps.Length == 0 && call.Method.TypeArguments.Length == 1:
                        if (mode == EmitMode.Value)
                        {
                            _il.EmitI4(0);
                            _il.Emit(ILOpCode.Conv_U, 1, 1);
                        }
                        return true;

                    case "IsNullRef" when ps.Length == 1 && ps[0].Type is ByRefTypeSymbol:
                        EmitExpression(args[0], EmitMode.Value);
                        _il.EmitI4(0);
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        break;

                    case "AreSame" when ps.Length == 2 && ps[0].Type is ByRefTypeSymbol && ps[1].Type is ByRefTypeSymbol:
                        EmitExpression(args[0], EmitMode.Value);
                        EmitExpression(args[1], EmitMode.Value);
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        break;

                    default:
                        return false;
                }

                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
                return true;
            }
            private bool TryEmitCopyBlock(BoundCallExpression call)
            {
                var ps = call.Method.Parameters;
                bool pointerOverload =
                    ps[0].Type is PointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void } &&
                    ps[1].Type is PointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void };
                bool byRefOverload =
                    ps[0].Type is ByRefTypeSymbol { ElementType.SpecialType: SpecialType.System_UInt8 } &&
                    ps[1].Type is ByRefTypeSymbol { ElementType.SpecialType: SpecialType.System_UInt8 };
                if (!pointerOverload && !byRefOverload)
                    return false;

                var args = call.Arguments;
                int destination = AllocateTemp(args[0].Type);
                int source = AllocateTemp(args[1].Type);
                int count = AllocateTemp(args[2].Type);
                int index = AllocateTemp(args[2].Type);
                EmitExpression(args[0], EmitMode.Value);
                _il.EmitLocal(ILOpCode.Stloc, destination);
                EmitExpression(args[1], EmitMode.Value);
                _il.EmitLocal(ILOpCode.Stloc, source);
                EmitExpression(args[2], EmitMode.Value);
                _il.EmitLocal(ILOpCode.Stloc, count);
                _il.EmitI4(0);
                _il.EmitLocal(ILOpCode.Stloc, index);

                var loop = _il.DefineLabel();
                var done = _il.DefineLabel();
                _il.MarkLabel(loop);
                _il.EmitLocal(ILOpCode.Ldloc, index);
                _il.EmitLocal(ILOpCode.Ldloc, count);
                _il.Emit(ILOpCode.Clt_Un, 2, 1);
                _il.EmitBranch(ILOpCode.Brfalse, done, 1);

                _il.EmitLocal(ILOpCode.Ldloc, destination);
                _il.EmitLocal(ILOpCode.Ldloc, index);
                _il.Emit(ILOpCode.Conv_U, 1, 1);
                _il.Emit(ILOpCode.Add, 2, 1);
                _il.EmitLocal(ILOpCode.Ldloc, source);
                _il.EmitLocal(ILOpCode.Ldloc, index);
                _il.Emit(ILOpCode.Conv_U, 1, 1);
                _il.Emit(ILOpCode.Add, 2, 1);
                _il.Emit(ILOpCode.Ldind_U1, 1, 1);
                _il.Emit(ILOpCode.Stind_I1, 2, 0);

                _il.EmitLocal(ILOpCode.Ldloc, index);
                _il.EmitI4(1);
                _il.Emit(ILOpCode.Add, 2, 1);
                _il.EmitLocal(ILOpCode.Stloc, index);
                _il.EmitBranch(ILOpCode.Br, loop, 0);
                _il.MarkLabel(done);
                return true;
            }

            private void EmitObjectCreation(BoundObjectCreationExpression obj, EmitMode mode)
            {
                if (obj.ConstructorOpt is null)
                {
                    if (mode == EmitMode.Value)
                        EmitDefaultValue(obj.Type);
                    return;
                }
                var args = obj.Arguments;
                for (int i = 0; i < args.Length; i++)
                    EmitExpression(args[i], EmitMode.Value);
                _il.EmitToken(ILOpCode.Newobj, _tokens.GetMethodToken(obj.ConstructorOpt), args.Length, 1);
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private static bool IsSystemNullableValueType(TypeSymbol t)
            {
                if (t is not NamedTypeSymbol nt || !nt.IsValueType)
                    return false;
                var def = nt.OriginalDefinition;
                return def.Arity == 1 &&
                    string.Equals(def.Name, "Nullable", StringComparison.Ordinal) &&
                    def.ContainingSymbol is NamespaceSymbol ns &&
                    string.Equals(ns.Name, "System", StringComparison.Ordinal);
            }
            private void EmitAs(BoundAsExpression @as, EmitMode mode)
            {
                if (mode == EmitMode.Discard)
                {
                    EmitExpression(@as.Operand, EmitMode.Discard);
                    return;
                }
                EmitExpression(@as.Operand, EmitMode.Value);
                if (@as.Operand.Type.IsValueType || @as.Operand.Type is TypeParameterSymbol)
                    _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(@as.Operand.Type), 1, 1);

                if (IsSystemNullableValueType(@as.Type))
                {
                    var underlying = ((NamedTypeSymbol)@as.Type).TypeArguments[0];
                    var isNull = _il.DefineLabel();
                    var fail = _il.DefineLabel();
                    var end = _il.DefineLabel();
                    _il.Emit(ILOpCode.Dup, 1, 2);
                    _il.EmitBranch(ILOpCode.Brfalse, isNull, 1);
                    _il.EmitToken(ILOpCode.Isinst, _tokens.GetTypeToken(underlying), 1, 1);
                    _il.Emit(ILOpCode.Dup, 1, 2);
                    _il.EmitBranch(ILOpCode.Brfalse, fail, 1);
                    _il.EmitToken(ILOpCode.Unbox_Any, _tokens.GetTypeToken(@as.Type), 1, 1);
                    _il.EmitBranch(ILOpCode.Br, end, 0);
                    _il.MarkLabel(fail);
                    _il.Emit(ILOpCode.Pop, 1, 0);
                    EmitDefaultValue(@as.Type);
                    _il.EmitBranch(ILOpCode.Br, end, 0);
                    _il.MarkLabel(isNull);
                    _il.Emit(ILOpCode.Pop, 1, 0);
                    EmitDefaultValue(@as.Type);
                    _il.MarkLabel(end);
                    return;
                }
                _il.EmitToken(ILOpCode.Isinst, _tokens.GetTypeToken(@as.Type), 1, 1);
                if (@as.Type is TypeParameterSymbol)
                    _il.EmitToken(ILOpCode.Unbox_Any, _tokens.GetTypeToken(@as.Type), 1, 1);
            }
            private void EmitIsPattern(BoundIsPatternExpression p, EmitMode mode)
            {
                switch (p.PatternKind)
                {
                    case BoundIsPatternKind.Type:
                        EmitTypePattern(p, mode);
                        return;
                    case BoundIsPatternKind.Null:
                        EmitExpression(p.Operand, EmitMode.Value);
                        if (p.Operand.Type.IsValueType || p.Operand.Type is TypeParameterSymbol)
                            _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(p.Operand.Type), 1, 1);
                        _il.Emit(ILOpCode.Ldnull, 0, 1);
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        break;
                    case BoundIsPatternKind.Constant:
                        EmitExpression(p.Operand, EmitMode.Value);
                        EmitExpression(p.ConstantOpt!, EmitMode.Value);
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        break;
                    case BoundIsPatternKind.Var:
                        if (p.DeclaredLocalOpt is null || p.IsDiscard)
                        {
                            EmitExpression(p.Operand, EmitMode.Discard);
                        }
                        else
                        {
                            EmitExpression(p.Operand, EmitMode.Value);
                            _il.EmitLocal(ILOpCode.Stloc, GetOrCreateLocal(p.DeclaredLocalOpt));
                        }
                        _il.EmitI4(1);
                        break;
                    default:
                        throw new NotSupportedException($"Unexpected pattern kind '{p.PatternKind}'.");
                }
                if (p.IsNegated)
                    EmitBooleanNot();
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }
            private void EmitTypePattern(BoundIsPatternExpression p, EmitMode mode)
            {
                EmitExpression(p.Operand, EmitMode.Value);
                if (p.Operand.Type.IsValueType || p.Operand.Type is TypeParameterSymbol)
                    _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(p.Operand.Type), 1, 1);
                _il.EmitToken(ILOpCode.Isinst, _tokens.GetTypeToken(p.PatternTypeOpt!), 1, 1);
                if (mode == EmitMode.Discard)
                {
                    _il.Emit(ILOpCode.Pop, 1, 0);
                    return;
                }
                if (p.DeclaredLocalOpt is null || p.IsDiscard)
                {
                    _il.Emit(ILOpCode.Ldnull, 0, 1);
                    _il.Emit(p.IsNegated ? ILOpCode.Ceq : ILOpCode.Cgt_Un, 2, 1);
                    return;
                }

                var fail = _il.DefineLabel();
                var end = _il.DefineLabel();
                _il.Emit(ILOpCode.Dup, 1, 2);
                _il.EmitBranch(ILOpCode.Brfalse, fail, 1);
                int localIndex = GetOrCreateLocal(p.DeclaredLocalOpt);
                if (p.PatternTypeOpt!.IsValueType || p.PatternTypeOpt is TypeParameterSymbol)
                    _il.EmitToken(ILOpCode.Unbox_Any, _tokens.GetTypeToken(p.PatternTypeOpt), 1, 1);
                _il.EmitLocal(ILOpCode.Stloc, localIndex);
                _il.EmitI4(p.IsNegated ? 0 : 1);
                _il.EmitBranch(ILOpCode.Br, end, 0);
                _il.MarkLabel(fail);
                _il.Emit(ILOpCode.Pop, 1, 0);
                _il.EmitI4(p.IsNegated ? 1 : 0);
                _il.MarkLabel(end);
            }

            private void EmitConversion(BoundConversionExpression conv, EmitMode mode)
            {
                if (conv.Conversion.Kind == ConversionKind.NullLiteral)
                {
                    EmitExpression(conv.Operand, EmitMode.Discard);
                    if (mode == EmitMode.Value)
                        EmitConstantValue(conv.Type, null);
                    return;
                }
                EmitExpression(conv.Operand, mode);
                if (mode == EmitMode.Discard)
                    return;

                switch (conv.Conversion.Kind)
                {
                    case ConversionKind.Identity:
                    case ConversionKind.ImplicitReference:
                        return;
                    case ConversionKind.ImplicitNumeric:
                    case ConversionKind.ExplicitNumeric:
                    case ConversionKind.ImplicitConstant:
                        EmitNumericConversion(conv.Operand.Type, conv.Type, conv.IsChecked);
                        return;
                    case ConversionKind.ExplicitReference:
                        if (conv.Operand.Type is TypeParameterSymbol)
                            _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(conv.Operand.Type), 1, 1);
                        _il.EmitToken(ILOpCode.Castclass, _tokens.GetTypeToken(conv.Type), 1, 1);
                        return;
                    case ConversionKind.Boxing:
                        _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(conv.Operand.Type), 1, 1);
                        return;
                    case ConversionKind.Unboxing:
                        _il.EmitToken(ILOpCode.Unbox_Any, _tokens.GetTypeToken(conv.Type), 1, 1);
                        return;
                    default:
                        throw new NotSupportedException($"Conversion kind '{conv.Conversion.Kind}' is not supported.");
                }
            }
            // Values of 32-bit-or-smaller integers live on the stack as int32, normalized to their own range.
            private static bool IsValuePreservingOnStack(SpecialType source, SpecialType target)
            {
                static (int Bits, bool Signed)? Range(SpecialType t) => t switch
                {
                    SpecialType.System_Boolean or SpecialType.System_UInt8 => (8, false),
                    SpecialType.System_Int8 => (8, true),
                    SpecialType.System_Char or SpecialType.System_UInt16 => (16, false),
                    SpecialType.System_Int16 => (16, true),
                    SpecialType.System_UInt32 => (32, false),
                    SpecialType.System_Int32 => (32, true),
                    _ => null,
                };
                if (Range(source) is not (int sourceBits, bool sourceSigned) || Range(target) is not (int targetBits, bool targetSigned))
                    return false;
                if (targetBits == 32)
                    return true;
                return targetSigned
                    ? sourceSigned ? targetBits >= sourceBits : targetBits > sourceBits
                    : !sourceSigned && targetBits >= sourceBits;
            }
            private void EmitNumericConversion(TypeSymbol sourceType, TypeSymbol targetType, bool isChecked)
            {
                bool sourceIsPointer = sourceType is PointerTypeSymbol or FunctionPointerTypeSymbol;
                bool targetIsPointer = targetType is PointerTypeSymbol or FunctionPointerTypeSymbol;
                if (sourceIsPointer && targetIsPointer)
                    return;

                var source = sourceIsPointer ? SpecialType.System_UIntPtr : UnderlyingType(sourceType).SpecialType;
                var target = targetIsPointer ? SpecialType.System_UIntPtr : UnderlyingType(targetType).SpecialType;
                bool sourceFloat = source is SpecialType.System_Single or SpecialType.System_Double;
                bool sourceUnsigned = source is SpecialType.System_Char or SpecialType.System_UInt8 or SpecialType.System_UInt16 or
                    SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_UIntPtr or SpecialType.System_Boolean;
                bool source64 = source is SpecialType.System_Int64 or SpecialType.System_UInt64;
                bool checkedConversion = isChecked;
                if (source == target && !sourceIsPointer && !targetIsPointer)
                    return;

                if (target == SpecialType.System_Boolean)
                {
                    if (sourceFloat)
                    {
                        _il.EmitR8(0);
                        _il.Emit(ILOpCode.Ceq, 2, 1);
                        EmitBooleanNot();
                        return;
                    }
                    if (source64)
                    {
                        _il.EmitI4(0);
                        _il.Emit(ILOpCode.Conv_I8, 1, 1);
                    }
                    else
                    {
                        _il.EmitI4(0);
                        if (source is SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
                            _il.Emit(ILOpCode.Conv_I, 1, 1);
                    }
                    _il.Emit(ILOpCode.Cgt_Un, 2, 1);
                    return;
                }
                if (target is SpecialType.System_Single or SpecialType.System_Double)
                {
                    if (sourceUnsigned && !sourceFloat)
                        _il.Emit(ILOpCode.Conv_R_Un, 1, 1);
                    if (target == SpecialType.System_Single)
                        _il.Emit(ILOpCode.Conv_R4, 1, 1);
                    else if (source != SpecialType.System_Double || sourceUnsigned)
                        _il.Emit(ILOpCode.Conv_R8, 1, 1);
                    return;
                }
                if (!checkedConversion)
                {
                    if (IsValuePreservingOnStack(source, target))
                        return;
                    ILOpCode? op = target switch
                    {
                        SpecialType.System_Int8 => ILOpCode.Conv_I1,
                        SpecialType.System_UInt8 => ILOpCode.Conv_U1,
                        SpecialType.System_Int16 => ILOpCode.Conv_I2,
                        SpecialType.System_UInt16 or SpecialType.System_Char => ILOpCode.Conv_U2,
                        SpecialType.System_Int32 => ILOpCode.Conv_I4,
                        SpecialType.System_UInt32 => ILOpCode.Conv_U4,
                        SpecialType.System_Int64 or SpecialType.System_UInt64 when source64 => null,
                        SpecialType.System_Int64 or SpecialType.System_UInt64 when sourceFloat =>
                            target == SpecialType.System_Int64 ? ILOpCode.Conv_I8 : ILOpCode.Conv_U8,
                        SpecialType.System_Int64 or SpecialType.System_UInt64 => sourceUnsigned ? ILOpCode.Conv_U8 : ILOpCode.Conv_I8,
                        SpecialType.System_IntPtr or SpecialType.System_UIntPtr when sourceFloat =>
                            target == SpecialType.System_IntPtr ? ILOpCode.Conv_I : ILOpCode.Conv_U,
                        SpecialType.System_IntPtr or SpecialType.System_UIntPtr => sourceUnsigned ? ILOpCode.Conv_U : ILOpCode.Conv_I,
                        _ => throw new NotSupportedException($"Numeric conversion to '{targetType.Name}' is not supported."),
                    };
                    if (op is { } conversion)
                        _il.Emit(conversion, 1, 1);
                    return;
                }

                bool un = sourceUnsigned && !sourceFloat;
                ILOpCode checkedOp = target switch
                {
                    SpecialType.System_Int8 => un ? ILOpCode.Conv_Ovf_I1_Un : ILOpCode.Conv_Ovf_I1,
                    SpecialType.System_UInt8 => un ? ILOpCode.Conv_Ovf_U1_Un : ILOpCode.Conv_Ovf_U1,
                    SpecialType.System_Int16 => un ? ILOpCode.Conv_Ovf_I2_Un : ILOpCode.Conv_Ovf_I2,
                    SpecialType.System_UInt16 or SpecialType.System_Char => un ? ILOpCode.Conv_Ovf_U2_Un : ILOpCode.Conv_Ovf_U2,
                    SpecialType.System_Int32 => un ? ILOpCode.Conv_Ovf_I4_Un : ILOpCode.Conv_Ovf_I4,
                    SpecialType.System_UInt32 => un ? ILOpCode.Conv_Ovf_U4_Un : ILOpCode.Conv_Ovf_U4,
                    SpecialType.System_Int64 => un ? ILOpCode.Conv_Ovf_I8_Un : ILOpCode.Conv_Ovf_I8,
                    SpecialType.System_UInt64 => un ? ILOpCode.Conv_Ovf_U8_Un : ILOpCode.Conv_Ovf_U8,
                    SpecialType.System_IntPtr => un ? ILOpCode.Conv_Ovf_I_Un : ILOpCode.Conv_Ovf_I,
                    SpecialType.System_UIntPtr => un ? ILOpCode.Conv_Ovf_U_Un : ILOpCode.Conv_Ovf_U,
                    _ => throw new NotSupportedException($"Numeric conversion to '{targetType.Name}' is not supported."),
                };
                _il.Emit(checkedOp, 1, 1);
            }

            private void EmitElementSize(TypeSymbol elementType, int knownSize)
            {
                if (knownSize > 0)
                    _il.EmitI4(knownSize);
                else
                    _il.EmitToken(ILOpCode.Sizeof, _tokens.GetTypeToken(elementType), 0, 1);
            }
            private void EmitScaledOffset(TypeSymbol elementType, TypeSymbol indexType)
            {
                switch (UnderlyingType(indexType).SpecialType)
                {
                    case SpecialType.System_IntPtr:
                    case SpecialType.System_UIntPtr:
                        break;
                    case SpecialType.System_UInt32:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_UInt8:
                    case SpecialType.System_Char:
                        _il.Emit(ILOpCode.Conv_U, 1, 1);
                        break;
                    default:
                        _il.Emit(ILOpCode.Conv_I, 1, 1);
                        break;
                }
                bool known = TryGetElementSize(elementType, out int size);
                if (known && size == 1)
                    return;
                EmitElementSize(elementType, known ? size : 0);
                _il.Emit(ILOpCode.Mul, 2, 1);
            }
            private void EmitPointerElementAddress(TypeSymbol elementType, TypeSymbol indexType, bool subtract)
            {
                EmitScaledOffset(elementType, indexType);
                _il.Emit(subtract ? ILOpCode.Sub : ILOpCode.Add, 2, 1);
            }
            private void EmitStackAlloc(BoundStackAllocArrayCreationExpression sa, EmitMode mode)
            {
                bool knownSize = TryGetElementSize(sa.ElementType, out int size);
                TypeSymbol countType = sa.Count.Type ?? sa.ElementType;
                if (knownSize && sa.Count.ConstantValueOpt.HasValue && sa.Count.ConstantValueOpt.Value is int constantCount &&
                    (long)(uint)constantCount * size < uint.MaxValue)
                {
                    _il.EmitI4(unchecked((int)((uint)constantCount * (uint)size)));
                    _il.Emit(ILOpCode.Conv_U, 1, 1);
                }
                else
                {
                    EmitExpression(sa.Count, EmitMode.Value);
                    _il.Emit(ILOpCode.Conv_U, 1, 1);
                    if (!knownSize || size != 1)
                    {
                        EmitElementSize(sa.ElementType, knownSize ? size : 0);
                        _il.Emit(ILOpCode.Mul_Ovf_Un, 2, 1);
                    }
                }
                _il.Emit(ILOpCode.Localloc, 1, 1);

                if (sa.InitializerOpt is not null)
                {
                    var elems = sa.InitializerOpt.Elements;
                    for (int i = 0; i < elems.Length; i++)
                    {
                        _il.Emit(ILOpCode.Dup, 1, 2);
                        if (i != 0)
                        {
                            _il.EmitI4(i);
                            EmitPointerElementAddress(sa.ElementType, countType, subtract: false);
                        }
                        EmitExpression(elems[i], EmitMode.Value);
                        EmitStoreIndirect(sa.ElementType);
                    }
                }
                if (mode == EmitMode.Discard)
                    _il.Emit(ILOpCode.Pop, 1, 0);
            }

            private void EmitLoadAddressOfLValue(BoundExpression lvalue)
            {
                switch (lvalue)
                {
                    case BoundLocalExpression loc:
                        {
                            int idx = GetOrCreateLocal(loc.Local);
                            _il.EmitLocal(loc.Local.IsByRef ? ILOpCode.Ldloc : ILOpCode.Ldloca, idx);
                            return;
                        }
                    case BoundArrayElementAccessExpression aea:
                        {
                            if (aea.Expression.Type is not ArrayTypeSymbol arrayType)
                                throw new InvalidOperationException("Array element access receiver is not an array type.");
                            if (aea.Indices.Length == 1 && arrayType.Rank != 1)
                            {
                                EmitLinearElementAddress(aea, arrayType);
                                return;
                            }
                            EmitExpression(aea.Expression, EmitMode.Value);
                            for (int i = 0; i < aea.Indices.Length; i++)
                                EmitArrayIndex(aea.Indices[i]);
                            if (arrayType.IsSZArray)
                                _il.EmitToken(ILOpCode.Ldelema, _tokens.GetTypeToken(aea.Type), 2, 1);
                            else
                                _il.EmitToken(ILOpCode.Call, _tokens.GetArrayMethodToken(arrayType, ArrayMethodKind.Address), arrayType.Rank + 1, 1);
                            return;
                        }
                    case BoundParameterExpression par:
                        _il.EmitArg(par.Parameter.Type is ByRefTypeSymbol ? ILOpCode.Ldarg : ILOpCode.Ldarga, GetArgIndex(par.Parameter));
                        return;
                    case BoundThisExpression:
                    case BoundBaseExpression:
                        _il.EmitArg(ILOpCode.Ldarg, 0);
                        return;
                    case BoundCallExpression call when call.Method.ReturnType is ByRefTypeSymbol:
                        EmitCall(call, EmitMode.Value);
                        return;
                    case BoundConditionalExpression conditional when conditional.Type is ByRefTypeSymbol:
                        EmitConditionalExpression(conditional, EmitMode.Value);
                        return;
                    case BoundFunctionPointerInvocationExpression invocation
                        when invocation.FunctionPointerType.ReturnRefKind != FunctionPointerRefKind.None:
                        EmitFunctionPointerInvocation(invocation, EmitMode.Value);
                        return;
                    case BoundIndexerAccessExpression ia when ia.Indexer.GetMethod?.ReturnType is ByRefTypeSymbol:
                        EmitIndexerAddress(ia);
                        return;
                    case BoundMemberAccessExpression { Member: FieldSymbol fs } ma when fs.Type is not ByRefTypeSymbol:
                        {
                            int tok = _tokens.GetFieldToken(fs);
                            if (fs.IsStatic)
                            {
                                _il.EmitToken(ILOpCode.Ldsflda, tok, 0, 1);
                                return;
                            }
                            if (ma.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");
                            if (ma.ReceiverOpt.Type.IsValueType)
                                EmitReceiverAddress(ma.ReceiverOpt);
                            else
                                EmitExpression(ma.ReceiverOpt, EmitMode.Value);
                            _il.EmitToken(ILOpCode.Ldflda, tok, 1, 1);
                            return;
                        }
                    case BoundMemberAccessExpression { Member: FieldSymbol fs } ma when fs.Type is ByRefTypeSymbol:
                        {
                            int tok = _tokens.GetFieldToken(fs);
                            if (fs.IsStatic)
                            {
                                _il.EmitToken(ILOpCode.Ldsfld, tok, 0, 1);
                                return;
                            }
                            if (ma.ReceiverOpt is null)
                                throw new InvalidOperationException($"Instance field '{fs.Name}' without receiver.");
                            EmitFieldReceiver(ma.ReceiverOpt);
                            _il.EmitToken(ILOpCode.Ldfld, tok, 1, 1);
                            return;
                        }
                    case BoundPointerIndirectionExpression pind:
                        EmitExpression(pind.Operand, EmitMode.Value);
                        return;
                    case BoundPointerElementAccessExpression pea:
                        EmitExpression(pea.Expression, EmitMode.Value);
                        EmitExpression(pea.Index, EmitMode.Value);
                        EmitPointerElementAddress(pea.Type, pea.Index.Type, subtract: false);
                        return;
                    default:
                        throw new NotSupportedException($"Cannot take address of lvalue '{lvalue.GetType().Name}'.");
                }
            }
            private void EmitIndexerAddress(BoundIndexerAccessExpression ia)
            {
                var getter = ia.Indexer.GetMethod!;
                if (ia.Indexer.IsStatic)
                    throw new NotSupportedException("Static indexers are not supported as lvalues.");

                bool thisIsManagedByRef = false;
                if (ia.Receiver.Type.IsValueType)
                {
                    bool methodDeclaredOnRefType = getter.ContainingSymbol is NamedTypeSymbol declType && declType.IsReferenceType;
                    if (methodDeclaredOnRefType)
                    {
                        EmitExpression(ia.Receiver, EmitMode.Value);
                        _il.EmitToken(ILOpCode.Box, _tokens.GetTypeToken(ia.Receiver.Type), 1, 1);
                    }
                    else
                    {
                        thisIsManagedByRef = true;
                        EmitReceiverAddress(ia.Receiver);
                    }
                }
                else
                {
                    EmitExpression(ia.Receiver, EmitMode.Value);
                }
                for (int i = 0; i < ia.Arguments.Length; i++)
                    EmitExpression(ia.Arguments[i], EmitMode.Value);
                _il.EmitToken(thisIsManagedByRef ? ILOpCode.Call : ILOpCode.Callvirt, _tokens.GetMethodToken(getter), ia.Arguments.Length + 1, 1);
            }
        }
    }
}
