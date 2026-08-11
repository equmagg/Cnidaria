using System;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum GenTreeConstantKind : byte
    {
        I4,
        I8,
        Null,
    }

    internal readonly struct GenTreeConstantValue : IEquatable<GenTreeConstantValue>
    {
        public readonly GenTreeConstantKind Kind;
        public readonly int I4;
        public readonly long I8;

        private GenTreeConstantValue(GenTreeConstantKind kind, int i4, long i8)
        {
            Kind = kind;
            I4 = i4;
            I8 = i8;
        }

        public static GenTreeConstantValue ForI4(int value) => new GenTreeConstantValue(GenTreeConstantKind.I4, value, value);
        public static GenTreeConstantValue ForI8(long value) => new GenTreeConstantValue(GenTreeConstantKind.I8, unchecked((int)value), value);
        public static GenTreeConstantValue Null => new GenTreeConstantValue(GenTreeConstantKind.Null, 0, 0);

        public bool Equals(GenTreeConstantValue other) => Kind == other.Kind && I4 == other.I4 && I8 == other.I8;
        public override bool Equals(object? obj) => obj is GenTreeConstantValue other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ I4 ^ I8.GetHashCode();
    }

    internal static class GenTreeFolder
    {
        private const GenTreeFlags PersistentEffects =
            GenTreeFlags.ContainsCall |
            GenTreeFlags.CanThrow |
            GenTreeFlags.SideEffect |
            GenTreeFlags.GlobalRef |
            GenTreeFlags.Ordered;

        private const GenTreeFlags ComparisonBlockingEffects =
            GenTreeFlags.ContainsCall |
            GenTreeFlags.CanThrow |
            GenTreeFlags.SideEffect |
            GenTreeFlags.Ordered;

        public static GenTree Fold(GenTree tree, TargetInfo target)
        {
            if (tree is null)
                throw new ArgumentNullException(nameof(tree));
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return tree.Kind switch
            {
                GenTreeKind.Unary => FoldUnary(tree, target),
                GenTreeKind.Binary => FoldBinary(tree, target),
                GenTreeKind.Conv => FoldConversion(tree, target),
                _ => tree,
            };
        }

        internal static bool TryGetConstant(GenTree tree, out GenTreeConstantValue constant)
        {
            switch (tree.Kind)
            {
                case GenTreeKind.ConstI4:
                    constant = GenTreeConstantValue.ForI4(tree.Int32);
                    return true;
                case GenTreeKind.ConstI8:
                    constant = GenTreeConstantValue.ForI8(tree.Int64);
                    return true;
                case GenTreeKind.ConstNull:
                    constant = GenTreeConstantValue.Null;
                    return true;
                default:
                    constant = default;
                    return false;
            }
        }

        internal static GenTree CreateConstant(GenTree template, GenTreeConstantValue constant, int? id = null)
        {
            int nodeId = id ?? template.Id;
            return constant.Kind switch
            {
                GenTreeConstantKind.I4 => new GenTree(
                    nodeId,
                    GenTreeKind.ConstI4,
                    template.Pc,
                    BytecodeOp.Ldc_I4,
                    template.Type,
                    template.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty,
                    int32: constant.I4),
                GenTreeConstantKind.I8 => new GenTree(
                    nodeId,
                    GenTreeKind.ConstI8,
                    template.Pc,
                    BytecodeOp.Ldc_I8,
                    template.Type,
                    template.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty,
                    int64: constant.I8),
                GenTreeConstantKind.Null => new GenTree(
                    nodeId,
                    GenTreeKind.ConstNull,
                    template.Pc,
                    BytecodeOp.Ldnull,
                    template.Type,
                    GenStackKind.Null,
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty),
                _ => throw new InvalidOperationException("Unknown GenTree constant kind."),
            };
        }

        internal static bool TryFoldUnary(GenTree source, GenTreeConstantValue operand, TargetInfo target, out GenTreeConstantValue result)
        {
            result = default;
            if (!GenTreeArithmeticSemantics.IsIntegralArithmeticType(source.Type, source.StackKind))
                return false;

            int bits = GenTreeArithmeticSemantics.IntegralBits(source.Type, source.StackKind, target);
            long value = ConstantAsSigned(operand, bits);

            switch (source.SourceOp)
            {
                case BytecodeOp.Neg:
                    result = bits > 32
                        ? GenTreeConstantValue.ForI8(unchecked(-value))
                        : GenTreeConstantValue.ForI4(unchecked(-(int)value));
                    return true;
                case BytecodeOp.Not:
                    result = bits > 32
                        ? GenTreeConstantValue.ForI8(~value)
                        : GenTreeConstantValue.ForI4(~(int)value);
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryFoldBinary(
            GenTree source,
            GenTree leftOperand,
            GenTreeConstantValue left,
            GenTreeConstantValue right,
            TargetInfo target,
            out GenTreeConstantValue result)
        {
            result = default;

            if (left.Kind == GenTreeConstantKind.Null || right.Kind == GenTreeConstantKind.Null)
            {
                if (source.SourceOp == BytecodeOp.Ceq)
                {
                    result = GenTreeConstantValue.ForI4(left.Kind == GenTreeConstantKind.Null && right.Kind == GenTreeConstantKind.Null ? 1 : 0);
                    return true;
                }
                return false;
            }

            if (!GenTreeArithmeticSemantics.IsIntegralArithmeticType(leftOperand.Type, leftOperand.StackKind))
                return false;

            int bits = GenTreeArithmeticSemantics.IntegralBits(leftOperand.Type, leftOperand.StackKind, target);
            long leftSigned = ConstantAsSigned(left, bits);
            long rightSigned = ConstantAsSigned(right, bits);
            ulong leftUnsigned = ConstantAsUnsigned(left, bits);
            ulong rightUnsigned = ConstantAsUnsigned(right, bits);

            try
            {
                switch (source.SourceOp)
                {
                    case BytecodeOp.Add_Ovf:
                        result = SignedResult(bits, bits > 32 ? checked(leftSigned + rightSigned) : checked((int)leftSigned + (int)rightSigned));
                        return true;
                    case BytecodeOp.Sub_Ovf:
                        result = SignedResult(bits, bits > 32 ? checked(leftSigned - rightSigned) : checked((int)leftSigned - (int)rightSigned));
                        return true;
                    case BytecodeOp.Mul_Ovf:
                        result = SignedResult(bits, bits > 32 ? checked(leftSigned * rightSigned) : checked((int)leftSigned * (int)rightSigned));
                        return true;
                    case BytecodeOp.Add_Ovf_Un:
                        if (bits > 32)
                            result = GenTreeConstantValue.ForI8(unchecked((long)checked(leftUnsigned + rightUnsigned)));
                        else
                            result = GenTreeConstantValue.ForI4(unchecked((int)checked((uint)leftUnsigned + (uint)rightUnsigned)));
                        return true;
                    case BytecodeOp.Sub_Ovf_Un:
                        if (bits > 32)
                            result = GenTreeConstantValue.ForI8(unchecked((long)checked(leftUnsigned - rightUnsigned)));
                        else
                            result = GenTreeConstantValue.ForI4(unchecked((int)checked((uint)leftUnsigned - (uint)rightUnsigned)));
                        return true;
                    case BytecodeOp.Mul_Ovf_Un:
                        if (bits > 32)
                            result = GenTreeConstantValue.ForI8(unchecked((long)checked(leftUnsigned * rightUnsigned)));
                        else
                            result = GenTreeConstantValue.ForI4(unchecked((int)checked((uint)leftUnsigned * (uint)rightUnsigned)));
                        return true;
                    case BytecodeOp.Add:
                        result = bits > 32
                            ? GenTreeConstantValue.ForI8(unchecked(leftSigned + rightSigned))
                            : GenTreeConstantValue.ForI4(unchecked((int)leftSigned + (int)rightSigned));
                        return true;
                    case BytecodeOp.Sub:
                        result = bits > 32
                            ? GenTreeConstantValue.ForI8(unchecked(leftSigned - rightSigned))
                            : GenTreeConstantValue.ForI4(unchecked((int)leftSigned - (int)rightSigned));
                        return true;
                    case BytecodeOp.Mul:
                        result = bits > 32
                            ? GenTreeConstantValue.ForI8(unchecked(leftSigned * rightSigned))
                            : GenTreeConstantValue.ForI4(unchecked((int)leftSigned * (int)rightSigned));
                        return true;
                    case BytecodeOp.Div:
                        if (rightSigned == 0 || (GenTreeArithmeticSemantics.IsSignedMinValue(leftSigned, bits) && rightSigned == -1))
                            return false;
                        result = SignedResult(bits, leftSigned / rightSigned);
                        return true;
                    case BytecodeOp.Div_Un:
                        if (rightUnsigned == 0)
                            return false;
                        result = UnsignedResult(bits, leftUnsigned / rightUnsigned);
                        return true;
                    case BytecodeOp.Rem:
                        if (rightSigned == 0 || (GenTreeArithmeticSemantics.IsSignedMinValue(leftSigned, bits) && rightSigned == -1))
                            return false;
                        result = SignedResult(bits, leftSigned % rightSigned);
                        return true;
                    case BytecodeOp.Rem_Un:
                        if (rightUnsigned == 0)
                            return false;
                        result = UnsignedResult(bits, leftUnsigned % rightUnsigned);
                        return true;
                    case BytecodeOp.And:
                        result = UnsignedResult(bits, leftUnsigned & rightUnsigned);
                        return true;
                    case BytecodeOp.Or:
                        result = UnsignedResult(bits, leftUnsigned | rightUnsigned);
                        return true;
                    case BytecodeOp.Xor:
                        result = UnsignedResult(bits, leftUnsigned ^ rightUnsigned);
                        return true;
                    case BytecodeOp.Shl:
                        result = UnsignedResult(bits, leftUnsigned << ((int)rightUnsigned & (bits - 1)));
                        return true;
                    case BytecodeOp.Shr:
                        result = SignedResult(bits, leftSigned >> ((int)rightUnsigned & (bits - 1)));
                        return true;
                    case BytecodeOp.Shr_Un:
                        result = UnsignedResult(bits, leftUnsigned >> ((int)rightUnsigned & (bits - 1)));
                        return true;
                    case BytecodeOp.Ceq:
                        result = GenTreeConstantValue.ForI4(leftUnsigned == rightUnsigned ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt:
                        result = GenTreeConstantValue.ForI4(leftSigned < rightSigned ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt_Un:
                        result = GenTreeConstantValue.ForI4(leftUnsigned < rightUnsigned ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt:
                        result = GenTreeConstantValue.ForI4(leftSigned > rightSigned ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt_Un:
                        result = GenTreeConstantValue.ForI4(leftUnsigned > rightUnsigned ? 1 : 0);
                        return true;
                    default:
                        return false;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        internal static bool TryFoldConversion(GenTree source, GenTreeConstantValue operand, TargetInfo target, out GenTreeConstantValue result)
        {
            result = default;
            if (operand.Kind == GenTreeConstantKind.Null)
                return false;

            bool isChecked = (source.ConvFlags & NumericConvFlags.Checked) != 0;
            bool sourceUnsigned = (source.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;
            long signed = operand.Kind == GenTreeConstantKind.I8 ? operand.I8 : operand.I4;
            ulong unsigned = operand.Kind == GenTreeConstantKind.I8
                ? unchecked((ulong)operand.I8)
                : unchecked((uint)operand.I4);

            try
            {
                switch (source.ConvKind)
                {
                    case NumericConvKind.Bool:
                        result = GenTreeConstantValue.ForI4(sourceUnsigned ? (unsigned != 0 ? 1 : 0) : (signed != 0 ? 1 : 0));
                        return true;
                    case NumericConvKind.I1:
                        result = GenTreeConstantValue.ForI4(sourceUnsigned
                            ? (isChecked ? checked((sbyte)unsigned) : unchecked((sbyte)unsigned))
                            : (isChecked ? checked((sbyte)signed) : unchecked((sbyte)signed)));
                        return true;
                    case NumericConvKind.U1:
                        result = GenTreeConstantValue.ForI4(sourceUnsigned
                            ? (isChecked ? checked((byte)unsigned) : unchecked((byte)unsigned))
                            : (isChecked ? checked((byte)signed) : unchecked((byte)signed)));
                        return true;
                    case NumericConvKind.I2:
                        result = GenTreeConstantValue.ForI4(sourceUnsigned
                            ? (isChecked ? checked((short)unsigned) : unchecked((short)unsigned))
                            : (isChecked ? checked((short)signed) : unchecked((short)signed)));
                        return true;
                    case NumericConvKind.U2:
                    case NumericConvKind.Char:
                        result = GenTreeConstantValue.ForI4(sourceUnsigned
                            ? (isChecked ? checked((ushort)unsigned) : unchecked((ushort)unsigned))
                            : (isChecked ? checked((ushort)signed) : unchecked((ushort)signed)));
                        return true;
                    case NumericConvKind.I4:
                        result = GenTreeConstantValue.ForI4(sourceUnsigned
                            ? (isChecked ? checked((int)unsigned) : unchecked((int)unsigned))
                            : (isChecked ? checked((int)signed) : unchecked((int)signed)));
                        return true;
                    case NumericConvKind.U4:
                        result = GenTreeConstantValue.ForI4(unchecked((int)(sourceUnsigned
                            ? (isChecked ? checked((uint)unsigned) : unchecked((uint)unsigned))
                            : (isChecked ? checked((uint)signed) : unchecked((uint)signed)))));
                        return true;
                    case NumericConvKind.I8:
                        result = GenTreeConstantValue.ForI8(sourceUnsigned
                            ? (isChecked ? checked((long)unsigned) : unchecked((long)unsigned))
                            : signed);
                        return true;
                    case NumericConvKind.U8:
                        result = GenTreeConstantValue.ForI8(unchecked((long)(sourceUnsigned
                            ? unsigned
                            : (isChecked ? checked((ulong)signed) : unchecked((ulong)signed)))));
                        return true;
                    case NumericConvKind.NativeInt:
                        if (target.PointerSize == 4)
                        {
                            result = GenTreeConstantValue.ForI4(sourceUnsigned
                                ? (isChecked ? checked((int)unsigned) : unchecked((int)unsigned))
                                : (isChecked ? checked((int)signed) : unchecked((int)signed)));
                        }
                        else
                        {
                            result = GenTreeConstantValue.ForI8(sourceUnsigned
                                ? (isChecked ? checked((long)unsigned) : unchecked((long)unsigned))
                                : signed);
                        }
                        return true;
                    case NumericConvKind.NativeUInt:
                        if (target.PointerSize == 4)
                        {
                            result = GenTreeConstantValue.ForI4(unchecked((int)(sourceUnsigned
                                ? (isChecked ? checked((uint)unsigned) : unchecked((uint)unsigned))
                                : (isChecked ? checked((uint)signed) : unchecked((uint)signed)))));
                        }
                        else
                        {
                            result = GenTreeConstantValue.ForI8(unchecked((long)(sourceUnsigned
                                ? unsigned
                                : (isChecked ? checked((ulong)signed) : unchecked((ulong)signed)))));
                        }
                        return true;
                    default:
                        return false;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static GenTree FoldUnary(GenTree tree, TargetInfo target)
        {
            if (tree.Operands.Length != 1)
                return tree;

            GenTree operand = tree.Operands[0];
            if (TryGetConstant(operand, out var constant) && TryFoldUnary(tree, constant, target, out var folded))
                return CreateConstant(tree, folded);

            if (tree.SourceOp is BytecodeOp.Neg or BytecodeOp.Not &&
                operand.Kind == GenTreeKind.Unary &&
                operand.SourceOp == tree.SourceOp &&
                operand.Operands.Length == 1 &&
                GenTreeArithmeticSemantics.IsIntegralArithmeticType(tree.Type, tree.StackKind))
            {
                return operand.Operands[0];
            }

            return tree;
        }

        private static GenTree FoldBinary(GenTree tree, TargetInfo target)
        {
            if (tree.Operands.Length != 2)
                return tree;

            GenTree left = tree.Operands[0];
            GenTree right = tree.Operands[1];
            bool leftConstant = TryGetConstant(left, out var leftValue);
            bool rightConstant = TryGetConstant(right, out var rightValue);

            if (leftConstant && rightConstant && TryFoldBinary(tree, left, leftValue, rightValue, target, out var folded))
                return CreateConstant(tree, folded);

            if (leftConstant || rightConstant)
            {
                GenTree special = FoldBinaryWithOneConstant(tree, target, leftConstant, leftValue, rightConstant, rightValue);
                if (!ReferenceEquals(special, tree))
                    return special;
            }

            return FoldBinaryIdenticalOperands(tree);
        }

        private static GenTree FoldBinaryWithOneConstant(
            GenTree tree,
            TargetInfo target,
            bool leftConstant,
            GenTreeConstantValue leftValue,
            bool rightConstant,
            GenTreeConstantValue rightValue)
        {
            if (!GenTreeArithmeticSemantics.IsIntegralArithmeticType(tree.Type, tree.StackKind))
                return tree;

            int bits = GenTreeArithmeticSemantics.IntegralBits(tree.Type, tree.StackKind, target);
            GenTree other = leftConstant ? tree.Operands[1] : tree.Operands[0];
            GenTreeConstantValue constant = leftConstant ? leftValue : rightValue;
            if (constant.Kind == GenTreeConstantKind.Null)
                return tree;

            ulong value = ConstantAsUnsigned(constant, bits);
            bool constantOnRight = rightConstant;

            switch (tree.SourceOp)
            {
                case BytecodeOp.Add:
                case BytecodeOp.Add_Ovf:
                case BytecodeOp.Add_Ovf_Un:
                    if (value == 0)
                        return other;
                    break;
                case BytecodeOp.Sub:
                case BytecodeOp.Sub_Ovf:
                case BytecodeOp.Sub_Ovf_Un:
                    if (constantOnRight && value == 0)
                        return other;
                    break;
                case BytecodeOp.Mul:
                case BytecodeOp.Mul_Ovf:
                case BytecodeOp.Mul_Ovf_Un:
                    if (value == 1)
                        return other;
                    if (value == 0 && CanDiscard(other, PersistentEffects))
                        return CreateConstant(tree, Zero(bits));
                    break;
                case BytecodeOp.Div:
                case BytecodeOp.Div_Un:
                    if (constantOnRight && value == 1)
                        return tree.Operands[0];
                    break;
                case BytecodeOp.Rem:
                case BytecodeOp.Rem_Un:
                    if (constantOnRight && value == 1 && CanDiscard(tree.Operands[0], PersistentEffects))
                        return CreateConstant(tree, Zero(bits));
                    break;
                case BytecodeOp.And:
                    if (value == 0 && CanDiscard(other, PersistentEffects))
                        return CreateConstant(tree, Zero(bits));
                    if (value == (bits > 32 ? ulong.MaxValue : uint.MaxValue))
                        return other;
                    break;
                case BytecodeOp.Or:
                    if (value == 0)
                        return other;
                    if (value == (bits > 32 ? ulong.MaxValue : uint.MaxValue) && CanDiscard(other, PersistentEffects))
                        return CreateConstant(tree, SignedResult(bits, -1));
                    break;
                case BytecodeOp.Xor:
                    if (value == 0)
                        return other;
                    break;
                case BytecodeOp.Shl:
                case BytecodeOp.Shr:
                case BytecodeOp.Shr_Un:
                    if (value == 0)
                    {
                        if (constantOnRight)
                            return tree.Operands[0];
                        if (CanDiscard(tree.Operands[1], PersistentEffects))
                            return CreateConstant(tree, Zero(bits));
                    }
                    break;
            }

            return tree;
        }

        private static GenTree FoldBinaryIdenticalOperands(GenTree tree)
        {
            if ((tree.Flags & ComparisonBlockingEffects) != 0)
                return tree;

            GenTree left = tree.Operands[0];
            GenTree right = tree.Operands[1];
            if (!StructurallyEqual(left, right))
                return tree;

            switch (tree.SourceOp)
            {
                case BytecodeOp.Ceq:
                    if (!IsFloating(left.StackKind))
                        return CreateConstant(tree, GenTreeConstantValue.ForI4(1));
                    break;
                case BytecodeOp.Clt:
                case BytecodeOp.Clt_Un:
                case BytecodeOp.Cgt:
                case BytecodeOp.Cgt_Un:
                    if (GenTreeArithmeticSemantics.IsIntegralArithmeticType(left.Type, left.StackKind))
                        return CreateConstant(tree, GenTreeConstantValue.ForI4(0));
                    break;
            }

            return tree;
        }

        private static GenTree FoldConversion(GenTree tree, TargetInfo target)
        {
            if (tree.Operands.Length != 1)
                return tree;

            GenTree operand = tree.Operands[0];
            if (TryGetConstant(operand, out var constant) && TryFoldConversion(tree, constant, target, out var folded))
                return CreateConstant(tree, folded);

            if ((tree.ConvFlags & NumericConvFlags.Checked) != 0 || !IsSemanticallyNoOpConversion(tree.ConvKind, operand.StackKind, target))
                return tree;

            var sourceAbi = MachineAbi.ClassifyStorageValue(operand.Type, operand.StackKind, target);
            var destinationAbi = MachineAbi.ClassifyStorageValue(tree.Type, tree.StackKind, target);
            if (sourceAbi.PassingKind == destinationAbi.PassingKind &&
                sourceAbi.RegisterClass == destinationAbi.RegisterClass &&
                sourceAbi.Size == destinationAbi.Size &&
                sourceAbi.ContainsGcPointers == destinationAbi.ContainsGcPointers)
            {
                return operand;
            }

            return tree;
        }

        private static bool IsSemanticallyNoOpConversion(NumericConvKind targetKind, GenStackKind sourceStackKind, TargetInfo target)
        {
            if (targetKind is NumericConvKind.Bool or NumericConvKind.I1 or NumericConvKind.U1 or NumericConvKind.I2 or NumericConvKind.U2 or NumericConvKind.Char)
                return false;

            if (targetKind is NumericConvKind.I4 or NumericConvKind.U4)
                return sourceStackKind == GenStackKind.I4;
            if (targetKind is NumericConvKind.I8 or NumericConvKind.U8)
                return sourceStackKind == GenStackKind.I8;
            if (targetKind == NumericConvKind.R4)
                return sourceStackKind == GenStackKind.R4;
            if (targetKind == NumericConvKind.R8)
                return sourceStackKind == GenStackKind.R8;
            if (targetKind == NumericConvKind.NativeInt)
                return sourceStackKind == GenStackKind.NativeInt || (target.PointerSize == 4 && sourceStackKind == GenStackKind.I4);
            if (targetKind == NumericConvKind.NativeUInt)
                return sourceStackKind is GenStackKind.NativeUInt or GenStackKind.Ptr || (target.PointerSize == 4 && sourceStackKind == GenStackKind.I4);

            return false;
        }

        private static bool StructurallyEqual(GenTree left, GenTree right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left.Kind != right.Kind ||
                left.SourceOp != right.SourceOp ||
                !ReferenceEquals(left.Type, right.Type) ||
                left.StackKind != right.StackKind ||
                left.Int32 != right.Int32 ||
                left.Int64 != right.Int64 ||
                !StringComparer.Ordinal.Equals(left.Text, right.Text) ||
                !ReferenceEquals(left.RuntimeType, right.RuntimeType) ||
                !ReferenceEquals(left.Field, right.Field) ||
                !ReferenceEquals(left.Method, right.Method) ||
                left.ConvKind != right.ConvKind ||
                left.ConvFlags != right.ConvFlags ||
                left.TargetPc != right.TargetPc ||
                left.TargetBlockId != right.TargetBlockId ||
                left.BoundsCheckIndexOverride != right.BoundsCheckIndexOverride ||
                !SameNullable(left.SsaValueName, right.SsaValueName) ||
                !SameNullable(left.SsaStoreTargetName, right.SsaStoreTargetName) ||
                !SameNullable(left.SsaLocalFieldBaseValue, right.SsaLocalFieldBaseValue) ||
                !ReferenceEquals(left.SsaLocalField, right.SsaLocalField) ||
                left.Operands.Length != right.Operands.Length)
            {
                return false;
            }

            bool direct = true;
            for (int i = 0; i < left.Operands.Length; i++)
            {
                if (!StructurallyEqual(left.Operands[i], right.Operands[i]))
                {
                    direct = false;
                    break;
                }
            }
            if (direct)
                return true;

            if (left.Kind == GenTreeKind.Binary && left.Operands.Length == 2 && IsCommutative(left.SourceOp))
                return StructurallyEqual(left.Operands[0], right.Operands[1]) && StructurallyEqual(left.Operands[1], right.Operands[0]);

            return false;
        }

        private static bool SameNullable<T>(T? left, T? right)
            where T : struct, IEquatable<T>
        {
            if (left.HasValue != right.HasValue)
                return false;
            return !left.HasValue || left.Value.Equals(right!.Value);
        }

        private static bool IsCommutative(BytecodeOp op)
            => op is BytecodeOp.Add or BytecodeOp.Add_Ovf or BytecodeOp.Add_Ovf_Un or
                BytecodeOp.Mul or BytecodeOp.Mul_Ovf or BytecodeOp.Mul_Ovf_Un or
                BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor or BytecodeOp.Ceq;

        private static bool CanDiscard(GenTree tree, GenTreeFlags effects)
            => (tree.Flags & effects) == 0;

        private static bool IsFloating(GenStackKind stackKind)
            => stackKind is GenStackKind.R4 or GenStackKind.R8;

        private static long ConstantAsSigned(GenTreeConstantValue constant, int bits)
        {
            long value = constant.Kind == GenTreeConstantKind.I8 ? constant.I8 : constant.I4;
            return bits > 32 ? value : unchecked((int)value);
        }

        private static ulong ConstantAsUnsigned(GenTreeConstantValue constant, int bits)
        {
            ulong value = constant.Kind == GenTreeConstantKind.I8
                ? unchecked((ulong)constant.I8)
                : unchecked((uint)constant.I4);
            return bits > 32 ? value : (uint)value;
        }

        private static GenTreeConstantValue SignedResult(int bits, long value)
            => bits > 32 ? GenTreeConstantValue.ForI8(value) : GenTreeConstantValue.ForI4(unchecked((int)value));

        private static GenTreeConstantValue UnsignedResult(int bits, ulong value)
            => bits > 32 ? GenTreeConstantValue.ForI8(unchecked((long)value)) : GenTreeConstantValue.ForI4(unchecked((int)(uint)value));

        private static GenTreeConstantValue Zero(int bits)
            => bits > 32 ? GenTreeConstantValue.ForI8(0) : GenTreeConstantValue.ForI4(0);

    }
}
