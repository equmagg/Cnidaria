using Cnidaria.X86;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed partial class GenTreeMethodBuilder
    {
        private static int VectorApiSize(RuntimeType type)
        {
            int numericsSize = SimdTypes.NumericsSize(type);
            if (numericsSize != 0)
                return numericsSize;
            if (type.Namespace == "System.Numerics" && (type.GenericTypeDefinition?.Name ?? type.Name) is "Vector" or "Vector`1")
                return 16;
            if (!StringComparer.Ordinal.Equals(type.Namespace, "System.Runtime.Intrinsics"))
                return 0;
            return (type.GenericTypeDefinition?.Name ?? type.Name) switch
            {
                "Vector64" or "Vector64`1" => 8,
                "Vector128" or "Vector128`1" => 16,
                "Vector256" or "Vector256`1" => 32,
                "Vector512" or "Vector512`1" => 64,
                _ => 0,
            };
        }

        private bool IsHardwareAcceleratedVectorApi(RuntimeType type)
            => (type.Namespace != "System.Numerics" || SimdTypes.NumericsSize(type) != 0 || _rts.Target.IsRiscV) &&
               SimdTypes.TargetSupports(_rts.Target, VectorApiSize(type));

        private bool TryImportVectorElementTypeIsSupported(List<StackValue> stack, RuntimeMethod method, int pc, ILOpCode op)
        {
            RuntimeType owner = method.DeclaringType;
            if (!method.IsStatic || method.HasThis || method.ParameterTypes.Length != 0 ||
                method.MethodGenericArguments.Length != 0 ||
                !StringComparer.Ordinal.Equals(method.Name, "get_IsSupported") ||
                method.ReturnType.PrimitiveKind != RuntimePrimitiveKind.Boolean ||
                owner.GenericTypeArguments.Length != 1)
                return false;

            string name = owner.GenericTypeDefinition?.Name ?? owner.Name;
            bool isVector = owner.Namespace switch
            {
                "System.Runtime.Intrinsics" => name is "Vector64" or "Vector64`1" or
                    "Vector128" or "Vector128`1" or "Vector256" or "Vector256`1" or
                    "Vector512" or "Vector512`1",
                "System.Numerics" => name is "Vector" or "Vector`1",
                _ => false,
            };
            if (!isVector)
                return false;

            RuntimeType elementType = owner.GenericTypeArguments[0];
            if (elementType.Kind == RuntimeTypeKind.TypeParam)
                return false;

            Push(stack, ConstI4(pc, op, SimdTypes.IsSupportedElement(elementType) ? 1 : 0));
            return true;
        }

        private bool TryImportHWIntrinsic(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, RuntimeMethod method, int pc, ILOpCode op)
        {
            bool x86 = StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, X86Intrinsics.Namespace);
            if (!x86 && !SimdTypes.TargetSupports(_rts.Target, VectorApiSize(method.DeclaringType)))
                return false;

            int argCount = method.ParameterTypes.Length + (method.HasThis ? 1 : 0);
            if (stack.Count < argCount)
                return false;

            var args = ImmutableArray.CreateBuilder<GenTree>(argCount);
            for (int i = stack.Count - argCount; i < stack.Count; i++)
                args.Add(stack[i].Node);

            GenTree? result = x86 ? BuildX86Intrinsic(method, args.MoveToImmutable(), pc) : BuildHWIntrinsic(method, args.MoveToImmutable(), pc);
            if (result is null)
                return false;

            PopMany(stack, argCount, pc, op);
            if (result.Kind == GenTreeKind.HWIntrinsic && X86Intrinsics.IsX86(result) && X86Intrinsics.IsDivRem(result, out _))
            {
                ImportDivRem(frame, stack, statements, method, result, pc, op);
                return true;
            }
            if (result.StackKind == GenStackKind.Void)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, op, operands: One(result)));
            else
                PushImportedValue(stack, statements, result);
            return true;
        }

        private GenTree? BuildX86Intrinsic(RuntimeMethod method, ImmutableArray<GenTree> args, int pc)
        {
            TargetInfo target = _rts.Target;
            bool supported = X86Intrinsics.TryGetIsa(method.DeclaringType, out X86Isa isa, out X86IsaVariant variant) && X86Intrinsics.IsSupported(target, isa, variant);
            var ps = method.ParameterTypes;
            if (method.Name == "get_IsSupported" && ps.Length == 0)
                return ConstI4(pc, ILOpCode.Ldc_I4, supported ? 1 : 0);
            if (!supported || method.HasThis || !X86Intrinsics.TryLookup(isa, variant == X86IsaVariant.X64, X86RowName(method), out int id))
                return null;

            int simdSize = 16;
            foreach (RuntimeType type in ps)
            {
                int size = VectorApiSize(type);
                if (size != 0 && !SimdTypes.TargetSupports(target, size))
                    return null;
                simdSize = Math.Max(simdSize, size);
            }
            simdSize = Math.Max(simdSize, VectorApiSize(method.ReturnType));

            X86IntrinsicInfo info = X86Intrinsics.Info(id);
            RuntimeType? source = info.Has(X86IntrinsicFlags.BaseFromFirstArg) ? ps[0]
                : info.Has(X86IntrinsicFlags.BaseFromSecondArg) ? ps[1]
                : SimdTypes.IsSimdType(method.ReturnType, target) ? method.ReturnType
                : ps.Length != 0 ? ps[0] : null;
            RuntimePrimitiveKind baseKind = X86ElementKind(source, target);
            if (info.Instruction(baseKind) == X86InstrKind.Invalid)
                return null;

            int addressMask = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].Kind == RuntimeTypeKind.Pointer)
                    addressMask |= 1 << i;
            }

            bool immediate = false;
            bool rounding = false;
            if (X86Intrinsics.IsVex(info) && info.Form is not (X86IntrinsicForm.Gather or X86IntrinsicForm.GatherMask or X86IntrinsicForm.BitFieldExtract) &&
                ps.Length != 0)
            {
                RuntimeType last = ps[^1];
                rounding = IsX86IntrinsicEnum(last, "FloatRoundingMode");
                immediate = !rounding && (IsX86IntrinsicEnum(last, "FloatComparisonMode") || last.Kind != RuntimeTypeKind.Pointer && last.PrimitiveKind == RuntimePrimitiveKind.UInt8);
            }

            RuntimeType resultType = info.Form == X86IntrinsicForm.DivRem ? method.ReturnType.GenericTypeArguments[0] : method.ReturnType;
            GenStackKind stackKind = StackKindOf(resultType);
            GenTree node = Node(GenTreeKind.HWIntrinsic, pc, GenTreeOperator.None, type: stackKind == GenStackKind.Void ? null : resultType,
                stackKind: stackKind, operands: args, int32: id, int64: X86Intrinsics.Encode(baseKind, simdSize, addressMask, immediate, rounding));
            node.Flags |= X86Intrinsics.EffectFlags(node);
            return node;
        }

        private static bool IsX86IntrinsicEnum(RuntimeType type, string name)
            => StringComparer.Ordinal.Equals(type.Namespace, X86Intrinsics.Namespace) && StringComparer.Ordinal.Equals(type.Name, name);

        // Gathers with 64-bit indices and VNNI-INT multiplies of mixed signedness have rows of their own
        private static string X86RowName(RuntimeMethod method)
        {
            var ps = method.ParameterTypes;
            if (method.DeclaringType.Name.StartsWith("AvxVnniInt", StringComparison.Ordinal) && ps.Length == 3 &&
                ps[1].GenericTypeArguments.Length == 1 && ps[2].GenericTypeArguments.Length == 1 &&
                ps[1].GenericTypeArguments[0].PrimitiveKind != ps[2].GenericTypeArguments[0].PrimitiveKind)
            {
                return method.Name + ".Mixed";
            }
            int index = method.Name switch
            {
                "GatherVector128" or "GatherVector256" => 1,
                "GatherMaskVector128" or "GatherMaskVector256" => 2,
                _ => -1,
            };
            if (index >= 0 && index < ps.Length && ps[index].GenericTypeArguments.Length == 1 &&
                ps[index].GenericTypeArguments[0].PrimitiveKind == RuntimePrimitiveKind.Int64)
            {
                return method.Name + ".Q";
            }
            return method.Name;
        }

        private void ImportDivRem(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, RuntimeMethod method, GenTree quotient, int pc, ILOpCode op)
        {
            SpillEvaluationStackForImportBarrier(statements, stack, pc, op);
            var temps = new List<GenTemp>(quotient.Operands.Length);
            foreach (GenTree operand in quotient.Operands)
            {
                var temp = CreateImporterSpillTemp(operand.Type, operand.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, op, operands: One(operand), int32: temp.Index));
                temps.Add(temp);
            }

            GenTree DivRemNode(int id)
            {
                var operands = ImmutableArray.CreateBuilder<GenTree>(temps.Count);
                foreach (GenTemp temp in temps)
                    operands.Add(TempLoad(pc, op, temp).Node);
                GenTree node = Node(GenTreeKind.HWIntrinsic, pc, GenTreeOperator.None, type: quotient.Type, stackKind: quotient.StackKind,
                    operands: operands.MoveToImmutable(), int32: id, int64: quotient.Int64);
                node.Flags |= X86Intrinsics.EffectFlags(node);
                return node;
            }

            PushImportedValue(stack, statements, DivRemNode(quotient.Int32));
            PushImportedValue(stack, statements, DivRemNode(quotient.Int32 + 1));

            RuntimeType tuple = method.ReturnType;
            _rts.EnsureConstructedMembers(tuple);
            foreach (RuntimeMethod ctor in tuple.Methods)
            {
                if (!ctor.IsStatic && ctor.ParameterTypes.Length == 2 && StringComparer.Ordinal.Equals(ctor.Name, ".ctor"))
                {
                    EmitNewObject(frame, stack, statements, pc, op, token: 0, ctor);
                    return;
                }
            }
            throw Fail(pc, op, "ValueTuple has no two-argument constructor.");
        }

        private static RuntimePrimitiveKind X86ElementKind(RuntimeType? type, TargetInfo target)
        {
            if (type is null)
                return RuntimePrimitiveKind.None;
            if (type.Kind == RuntimeTypeKind.Pointer && type.ElementType is RuntimeType pointee)
                type = pointee;
            if (SimdTypes.IsSimdType(type, target))
                type = SimdTypes.ElementType(type);
            return HWIntrinsics.NormalizeBaseKind(type.PrimitiveKind, target);
        }

        private GenTree? BuildHWIntrinsic(RuntimeMethod method, ImmutableArray<GenTree> args, int pc)
        {
            TargetInfo target = _rts.Target;
            RuntimeType? vectorType = SimdTypes.IsSimdType(method.DeclaringType, target)
                ? method.DeclaringType
                : FindVectorOperandType(method);
            if (vectorType is null || !SimdTypes.IsSimdType(vectorType, target))
                return null;

            int size = SimdTypes.SizeOf(SimdTypes.KindOf(vectorType, target));
            RuntimeType elementType = SimdTypes.ElementType(vectorType);
            RuntimePrimitiveKind baseKind = HWIntrinsics.NormalizeBaseKind(elementType.PrimitiveKind, target);
            int count = size / HWIntrinsics.ElementSize(baseKind);
            RuntimeType returnType = method.ReturnType;
            var ps = method.ParameterTypes;
            bool instance = method.HasThis;

            switch (method.Name)
            {
                case "op_Addition" or "Add":
                    return VectorBinary(HWIntrinsicId.Add);
                case "op_Subtraction" or "Subtract":
                    return VectorBinary(HWIntrinsicId.Subtract);
                case "op_BitwiseAnd" or "BitwiseAnd":
                    return VectorBinary(HWIntrinsicId.And);
                case "op_BitwiseOr" or "BitwiseOr":
                    return VectorBinary(HWIntrinsicId.Or);
                case "op_ExclusiveOr" or "Xor":
                    return VectorBinary(HWIntrinsicId.Xor);
                case "AndNot":
                    return VectorBinary(HWIntrinsicId.AndNot);
                case "AddSaturate":
                    return VectorBinary(HWIntrinsicId.AddSaturate);
                case "SubtractSaturate":
                    return VectorBinary(HWIntrinsicId.SubtractSaturate);
                case "Equals" when !instance:
                    return VectorBinary(HWIntrinsicId.CompareEqual);
                case "GreaterThan":
                    return VectorBinary(HWIntrinsicId.CompareGreaterThan);
                case "GreaterThanOrEqual":
                    return VectorBinary(HWIntrinsicId.CompareGreaterThanOrEqual);
                case "LessThan":
                    return VectorBinary(HWIntrinsicId.CompareLessThan);
                case "LessThanOrEqual":
                    return VectorBinary(HWIntrinsicId.CompareLessThanOrEqual);
                case "Min" when !HWIntrinsics.IsFloating(baseKind):
                case "MinNative" when !HWIntrinsics.IsFloating(baseKind):
                    return VectorBinary(HWIntrinsicId.Min);
                case "Max" when !HWIntrinsics.IsFloating(baseKind):
                case "MaxNative" when !HWIntrinsics.IsFloating(baseKind):
                    return VectorBinary(HWIntrinsicId.Max);
                case "op_Multiply" or "Multiply":
                    return ScaledBinary(HWIntrinsicId.Multiply, allowScalarLeft: true);
                case "op_Division" or "Divide":
                    return ScaledBinary(HWIntrinsicId.Divide, allowScalarLeft: false);

                case "op_UnaryNegation" or "Negate":
                    return VectorUnary(HWIntrinsicId.Negate);
                case "op_OnesComplement" or "OnesComplement":
                    return VectorUnary(HWIntrinsicId.Not);
                case "Abs":
                    return HWIntrinsics.IsUnsigned(baseKind) && IsUnaryVector() ? args[0] : VectorUnary(HWIntrinsicId.Abs);
                case "Sqrt":
                    return VectorUnary(HWIntrinsicId.Sqrt);
                case "Floor":
                    return VectorUnary(HWIntrinsicId.Floor);
                case "Ceiling":
                    return VectorUnary(HWIntrinsicId.Ceiling);
                case "op_UnaryPlus":
                    return IsUnaryVector() ? args[0] : null;

                case "ConditionalSelect" when ps.Length == 3 && IsVector(ps[0]) && IsVector(ps[1]) && IsVector(ps[2]):
                    return Simd(HWIntrinsicId.ConditionalSelect, returnType, args[0], args[1], args[2]);

                case "op_LeftShift" or "ShiftLeft":
                    return Shift(HWIntrinsicId.ShiftLeft);
                case "op_RightShift" or "ShiftRightArithmetic":
                    return Shift(HWIntrinsics.IsUnsigned(baseKind) ? HWIntrinsicId.ShiftRightLogical : HWIntrinsicId.ShiftRightArithmetic);
                case "op_UnsignedRightShift" or "ShiftRightLogical":
                    return Shift(HWIntrinsicId.ShiftRightLogical);

                case "op_Equality" or "EqualsAll" when ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]):
                    return Simd(HWIntrinsicId.EqualsAll, returnType, args[0], args[1]);
                case "op_Inequality" when ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]):
                    {
                        GenTree? equal = Simd(HWIntrinsicId.EqualsAll, returnType, args[0], args[1]);
                        return equal is null
                            ? null
                            : Node(GenTreeKind.Binary, pc, GenTreeOperator.Ceq, stackKind: GenStackKind.I4, operands: Two(equal, ConstI4(pc, ILOpCode.Ceq, 0)));
                    }
                case "EqualsAny" when ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]):
                    return Simd(HWIntrinsicId.EqualsAny, returnType, args[0], args[1]);
                case "ExtractMostSignificantBits" when IsUnaryVector():
                    return Simd(HWIntrinsicId.ExtractMostSignificantBits, returnType, args[0]);
                case "Sum" when IsUnaryVector():
                    return Simd(HWIntrinsicId.Sum, returnType, args[0]);
                case "ToScalar" when IsUnaryVector():
                    return Simd(HWIntrinsicId.ToScalar, returnType, args[0]);
                case "GetElement" when ps.Length == 2 && IsVector(ps[0]) && IsConstantIndex(args[1]):
                    return Simd(HWIntrinsicId.GetElement, returnType, args[0], args[1]);
                case "get_Item" when instance && ps.Length == 1 && IsConstantIndex(args[1]):
                    return Simd(HWIntrinsicId.GetElement, returnType, ValueThroughAddress(args[0], vectorType), args[1]);
                case "WithElement" when ps.Length == 3 && IsVector(ps[0]) && IsConstantIndex(args[1]):
                    return Simd(HWIntrinsicId.WithElement, returnType, args[0], args[1], args[2]);

                case "AsVector2" or "AsVector3" or "AsVector4" or "AsVector128" when IsUnaryVector() && IsVector(returnType):
                    return Simd(SimdSizeOf(returnType) > size ? HWIntrinsicId.ZeroExtend : HWIntrinsicId.BitCast, returnType, args[0]);
                case "AsVector3Unsafe" or "AsVector4Unsafe" or "AsVector128Unsafe" when IsUnaryVector() && IsVector(returnType):
                    return Simd(HWIntrinsicId.BitCast, returnType, args[0]);

                case "GetLower" when IsUnaryVector():
                    return Simd(HWIntrinsicId.BitCast, returnType, args[0]);
                case "GetUpper" when IsUnaryVector():
                    return Simd(HWIntrinsicId.GetUpper, returnType, args[0]);
                case "WithLower" when ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]):
                    return Simd(HWIntrinsicId.WithLower, returnType, args[0], args[1]);
                case "WithUpper" when ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]):
                    return Simd(HWIntrinsicId.WithUpper, returnType, args[0], args[1]);
                case "ToVector128" or "ToVector256" or "ToVector512" when IsUnaryVector() && IsVector(returnType) && SimdSizeOf(returnType) > size:
                    return Simd(HWIntrinsicId.ZeroExtend, returnType, args[0]);
                case "ToVector128Unsafe" or "ToVector256Unsafe" or "ToVector512Unsafe" when IsUnaryVector() && IsVector(returnType) && SimdSizeOf(returnType) > size:
                    return Simd(HWIntrinsicId.BitCast, returnType, args[0]);
                case "As" or "AsByte" or "AsSByte" or "AsInt16" or "AsUInt16" or "AsInt32" or "AsUInt32" or "AsInt64" or "AsUInt64" or
                    "AsNInt" or "AsNUInt" or "AsSingle" or "AsDouble" or "AsVector"
                    when IsUnaryVector() && IsVector(returnType) && SimdSizeOf(returnType) == size:
                    return Simd(HWIntrinsicId.BitCast, returnType, args[0]);

                case "get_Zero" when ps.Length == 0:
                    return Simd(HWIntrinsicId.Zero, returnType);
                case "get_AllBitsSet" when ps.Length == 0:
                    return Simd(HWIntrinsicId.AllBitsSet, returnType);
                case "get_One" when ps.Length == 0:
                    return Simd(HWIntrinsicId.Broadcast, returnType, ElementConstant(1));
                case "get_Count" when ps.Length == 0:
                    return ConstI4(pc, ILOpCode.Ldc_I4, count);
                case "get_IsSupported" when ps.Length == 0:
                    return ConstI4(pc, ILOpCode.Ldc_I4, 1);

                case "Create" when IsVector(returnType):
                    return Create();
                case "CreateScalar" when ps.Length == 1 && IsElement(ps[0]) && IsVector(returnType):
                    return Simd(HWIntrinsicId.CreateScalar, returnType, args[0]);
                case "CreateScalarUnsafe" when ps.Length == 1 && IsElement(ps[0]) && IsVector(returnType):
                    return Simd(HWIntrinsicId.CreateScalarUnsafe, returnType, args[0]);
            }

            return null;

            bool IsVector(RuntimeType type) => SimdTypes.IsSimdType(type, target);

            int SimdSizeOf(RuntimeType type) => SimdTypes.SizeOf(SimdTypes.KindOf(type, target));

            bool IsElement(RuntimeType type) => ReferenceEquals(type, elementType);

            bool IsUnaryVector() => ps.Length == 1 && IsVector(ps[0]);

            bool IsConstantIndex(GenTree node) => node.Kind == GenTreeKind.ConstI4 && (uint)node.Int32 < (uint)count;

            GenTree? Simd(HWIntrinsicId id, RuntimeType type, params GenTree[] operands)
                => SimdOfSize(id, type, size, operands);

            GenTree? SimdOfSize(HWIntrinsicId id, RuntimeType type, int vectorSize, params GenTree[] operands)
            {
                if (!HWIntrinsics.IsSupported(target, id, baseKind, vectorSize))
                    return null;
                return Node(GenTreeKind.HWIntrinsic, pc, GenTreeOperator.None, type: type, stackKind: StackKindOf(type),
                    operands: ImmutableArray.Create(operands), int32: (int)id, int64: HWIntrinsics.Encode(baseKind, vectorSize));
            }

            GenTree? VectorBinary(HWIntrinsicId id)
                => ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]) && IsVector(returnType)
                    ? Simd(id, returnType, args[0], args[1])
                    : null;

            GenTree? VectorUnary(HWIntrinsicId id)
                => IsUnaryVector() && IsVector(returnType) ? Simd(id, returnType, args[0]) : null;

            GenTree? ScaledBinary(HWIntrinsicId id, bool allowScalarLeft)
            {
                if (ps.Length != 2 || !IsVector(returnType) ||
                    !HWIntrinsics.IsSupported(target, id, baseKind, size) ||
                    !HWIntrinsics.IsSupported(target, HWIntrinsicId.Broadcast, baseKind, size))
                {
                    return null;
                }
                if (IsVector(ps[0]) && IsVector(ps[1]))
                    return Simd(id, returnType, args[0], args[1]);
                if (IsVector(ps[0]) && IsElement(ps[1]))
                    return Simd(id, returnType, args[0], Simd(HWIntrinsicId.Broadcast, returnType, args[1])!);
                if (allowScalarLeft && IsElement(ps[0]) && IsVector(ps[1]))
                    return Simd(id, returnType, Simd(HWIntrinsicId.Broadcast, returnType, args[0])!, args[1]);
                return null;
            }

            GenTree? Shift(HWIntrinsicId id)
            {
                if (ps.Length != 2 || !IsVector(ps[0]) || ps[1].PrimitiveKind != RuntimePrimitiveKind.Int32 || !IsVector(returnType))
                    return null;
                return Simd(id, returnType, args[0], args[1]);
            }

            GenTree? Create()
            {
                if (ps.Length == 1 && IsElement(ps[0]))
                    return Simd(HWIntrinsicId.Broadcast, returnType, args[0]);
                if (ps.Length == count && count > 1 && AllElements())
                    return CreateElements(returnType, size, args.AsSpan().ToArray());
                if (ps.Length == 2 && IsVector(ps[0]) && IsVector(ps[1]) && SimdSizeOf(ps[0]) * 2 == SimdSizeOf(returnType))
                    return SimdOfSize(HWIntrinsicId.Combine, returnType, SimdSizeOf(returnType), args[0], args[1]);
                return null;
            }

            // A wide vector of non-constant elements is built from its halves
            GenTree? CreateElements(RuntimeType type, int vectorSize, GenTree[] elements)
            {
                if (HWIntrinsics.HasConstantElements(ImmutableArray.Create(elements)))
                    return SimdOfSize(HWIntrinsicId.CreateElements, type, vectorSize, elements);
                if (vectorSize > 16)
                {
                    int halfSize = vectorSize / 2;
                    RuntimeType halfType = _rts.GetGenericInstanceType(
                        _rts.GetRequiredNamedType(method.DeclaringType.AssemblyName, vectorType.Namespace, halfSize == 16 ? "Vector128`1" : "Vector256`1"), elementType);
                    GenTree? lower = CreateElements(halfType, halfSize, elements[..(elements.Length / 2)]);
                    GenTree? upper = CreateElements(halfType, halfSize, elements[(elements.Length / 2)..]);
                    return lower is null || upper is null ? null : SimdOfSize(HWIntrinsicId.Combine, type, vectorSize, lower, upper);
                }
                if (elements.Length <= 4)
                    return SimdOfSize(HWIntrinsicId.CreateElements, type, vectorSize, elements);
                if (!HWIntrinsics.IsSupported(target, HWIntrinsicId.WithElement, baseKind, vectorSize))
                    return null;
                GenTree vector = SimdOfSize(HWIntrinsicId.CreateScalarUnsafe, type, vectorSize, elements[0])!;
                for (int i = 1; i < elements.Length; i++)
                    vector = SimdOfSize(HWIntrinsicId.WithElement, type, vectorSize, vector, ConstI4(pc, ILOpCode.Ldc_I4, i), elements[i])!;
                return vector;
            }

            bool AllElements()
            {
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!IsElement(ps[i]))
                        return false;
                }
                return true;
            }

            GenTree ElementConstant(long value)
                => baseKind switch
                {
                    RuntimePrimitiveKind.Single => Node(GenTreeKind.ConstR4Bits, pc, ILOpCode.Ldc_R4, stackKind: GenStackKind.R4, int32: BitConverter.SingleToInt32Bits(value)),
                    RuntimePrimitiveKind.Double => Node(GenTreeKind.ConstR8Bits, pc, ILOpCode.Ldc_R8, stackKind: GenStackKind.R8, int64: BitConverter.DoubleToInt64Bits(value)),
                    RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 => Node(GenTreeKind.ConstI8, pc, ILOpCode.Ldc_I8, stackKind: GenStackKind.I8, int64: value),
                    _ => ConstI4(pc, ILOpCode.Ldc_I4, (int)value),
                };

            GenTree ValueThroughAddress(GenTree address, RuntimeType type)
            {
                GenTreeKind load = address.Kind switch
                {
                    GenTreeKind.LocalAddr => GenTreeKind.Local,
                    GenTreeKind.ArgAddr => GenTreeKind.Arg,
                    GenTreeKind.TempAddr => GenTreeKind.Temp,
                    _ => GenTreeKind.LoadIndirect,
                };
                return load == GenTreeKind.LoadIndirect
                    ? Node(GenTreeKind.LoadIndirect, pc, ILOpCode.Ldobj, type: type, stackKind: StackKindOf(type), operands: One(address), runtimeType: type)
                    : Node(load, pc, GenTreeOperator.None, type: type, stackKind: StackKindOf(type), int32: address.Int32);
            }
        }

        private RuntimeType? FindVectorOperandType(RuntimeMethod method)
        {
            for (int i = 0; i < method.ParameterTypes.Length; i++)
            {
                if (SimdTypes.IsSimdType(method.ParameterTypes[i], _rts.Target))
                    return method.ParameterTypes[i];
            }
            return SimdTypes.IsSimdType(method.ReturnType, _rts.Target) ? method.ReturnType : null;
        }
    }
}
