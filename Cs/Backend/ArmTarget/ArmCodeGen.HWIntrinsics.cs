using Cnidaria.Arm;
using System;
using System.Collections.Generic;

namespace Cnidaria.Cs;

internal static partial class ArmCodeGenerator
{
    private sealed partial class Generator
    {
        private sealed partial class MethodEmitter
        {
            private static bool IsContainedOperand(GenTree node, int operandIndex)
                => !node.OperandFlags.IsDefaultOrEmpty && operandIndex < node.OperandFlags.Length &&
                   (node.OperandFlags[operandIndex] & LirOperandFlags.Contained) != 0;

            private ArmRegister OperandRegister(GenTree node, int operandIndex)
            {
                int use = 0;
                for (int i = 0; i < operandIndex; i++)
                {
                    if (!IsContainedOperand(node, i))
                        use++;
                }
                return ToArm(RequireUseRegisterForOperand(node, use, "vector intrinsic operand"));
            }

            private int ContainedImmediate(GenTree node, int operandIndex)
                => TryGetContainedIntegerImmediate(node, operandIndex, out long value)
                    ? checked((int)value)
                    : throw Unsupported(node, "Vector intrinsic requires a contained immediate");

            private List<ArmRegister> FloatTemps(GenTree node)
            {
                var temps = new List<ArmRegister>();
                foreach (GenTreeInternalRegister temp in node.LsraInfo.InternalRegisters)
                {
                    if (MachineRegisters.GetClass(temp.Register) == RegisterClass.Float)
                        temps.Add(ToArm(temp.Register));
                }
                return temps;
            }

            private ArmRegister FloatTemp(GenTree node, ArmRegister avoid = ArmRegister.Invalid)
            {
                foreach (ArmRegister temp in FloatTemps(node))
                {
                    if (temp != avoid)
                        return temp;
                }
                throw Unsupported(node, "Vector intrinsic has no internal vector register");
            }

            private void Emit2(ArmInstrKind opcode, ArmOperand d, ArmOperand a)
                => _owner.Emit(ArmInstruction.Binary(opcode, d, a));

            private void Emit3(ArmInstrKind opcode, ArmOperand d, ArmOperand a, ArmOperand b)
                => _owner.Emit(ArmInstruction.Ternary(opcode, d, a, b));

            private void EmitVectorMove(ArmRegister destination, ArmRegister source)
            {
                if (destination != source)
                    Emit2(ArmInstrKind.Mov, Vec(destination, 1, 16), Vec(source, 1, 16));
            }

            private void EmitInsertLane(ArmRegister destination, int element, int index, ArmRegister source, int sourceIndex)
                => Emit2(ArmInstrKind.Ins, ArmOperand.VectorElement(destination, element, index), ArmOperand.VectorElement(source, element, sourceIndex));

            private void EmitVectorConstant(ArmRegister destination, byte[] bytes)
            {
                bool splat = true;
                for (int i = 1; i < bytes.Length; i++)
                    splat &= bytes[i] == bytes[0];
                if (splat)
                {
                    Emit2(ArmInstrKind.Movi, Vec(destination, 1, 16), ArmOperand.ImmediateOperand(bytes[0]));
                    return;
                }
                int size = bytes.Length <= 8 ? 8 : 16;
                byte[] data = new byte[size];
                bytes.CopyTo(data, 0);
                string label = _owner.AddConstantData(data, size, size == 8 ? "v64" : "v128");
                _owner.EmitMaterializeAddress(label, ArmRegister.X16);
                EmitMemoryLoad(destination, ArmRegister.X16, 0, size, signed: false);
            }

            private static void WriteElement(byte[] bytes, int offset, int elementSize, GenTree element)
            {
                long value = element.Kind switch
                {
                    GenTreeKind.ConstI4 or GenTreeKind.ConstR4Bits => element.Int32,
                    _ => element.Int64,
                };
                for (int i = 0; i < elementSize; i++)
                    bytes[offset + i] = (byte)(value >> (i * 8));
            }

            private int VectorSizeOf(RuntimeType? type)
                => type is not null && SimdTypes.IsSimdType(type, Target) ? SimdTypes.SizeOf(SimdTypes.KindOf(type, Target)) : 0;

            private int ElementSizeOf(RuntimeType? type)
            {
                RuntimePrimitiveKind kind = ArmIntrinsics.ElementKind(type);
                return kind == RuntimePrimitiveKind.None ? 8 : HWIntrinsics.ElementSize(HWIntrinsics.NormalizeBaseKind(kind, Target));
            }

            private ArmOperand ValueOperand(RuntimeType? type, ArmRegister register, bool scalar, int element = 0)
            {
                int vs = VectorSizeOf(type);
                if (vs == 0)
                    return Reg(register, type is not null && type.SizeOf <= 4 ? 4 : 8);
                int e = element != 0 ? element : ElementSizeOf(type);
                if (element == 0 && (scalar && vs == 8 || e == 8 && vs == 8))
                    return Reg(register, e);
                return Vec(register, e, vs);
            }

            // A variable immediate selects one single-instruction case of a table, after a range check
            private void EmitWithImmediate(GenTree node, int operandIndex, int minimum, int maximum, Func<int, ArmInstruction> build)
            {
                if (TryGetContainedIntegerImmediate(node, operandIndex, out long value))
                {
                    _owner.Emit(build((int)value));
                    return;
                }

                ArmRegister index = OperandRegister(node, operandIndex);
                string outOfRange = BeginThrowTarget(node, "ArgumentOutOfRangeException", "immediate", out bool inlineThrow);
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ArmRegister.X16, 4), Reg(index, 4), ArmOperand.ImmediateOperand(minimum)));
                _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ArmRegister.X16, 4), ArmOperand.ImmediateOperand(maximum - minimum)));
                _owner.EmitConditionalJump(ArmCondition.Hi, outOfRange);
                string table = _owner.CreateLocalLabel($"{_methodLabel}_immediates_{node.LinearId}");
                string done = _owner.CreateLocalLabel($"{_methodLabel}_immediates_done_{node.LinearId}");
                AddRelocation(OffsetOfNextInstruction(), table, ArmObjectRelocationKind.AArch64Adr21);
                _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Adr, Reg(ArmRegister.X17, 8), ArmOperand.SymbolOperand(table, ArmRelocationKind.Adr)));
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.X17, 8), Reg(ArmRegister.X17, 8),
                    ArmOperand.ShiftedRegister(ArmRegister.X16, ArmShiftKind.Lsl, 3, 8)));
                _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Br, Reg(ArmRegister.X17, 8)));
                _owner.DefineLabel(table);
                for (int immediate = minimum; immediate <= maximum; immediate++)
                {
                    _owner.Emit(build(immediate));
                    _owner.EmitJump(done);
                }
                _owner.DefineLabel(done);
                EndThrowTarget(node, "ArgumentOutOfRangeException", outOfRange, inlineThrow);
            }

            private void EmitWithImmediates(GenTree node, int first, int second, Func<int, int, ArmInstruction> build)
            {
                bool firstConstant = TryGetContainedIntegerImmediate(node, first, out long firstValue);
                bool secondConstant = TryGetContainedIntegerImmediate(node, second, out long secondValue);
                (int firstMinimum, int firstMaximum) = ArmIntrinsics.ImmediateBounds(node, first);
                (int secondMinimum, int secondMaximum) = ArmIntrinsics.ImmediateBounds(node, second);
                if (firstConstant)
                {
                    EmitWithImmediate(node, second, secondMinimum, secondMaximum, value => build((int)firstValue, value));
                    return;
                }
                if (secondConstant)
                {
                    EmitWithImmediate(node, first, firstMinimum, firstMaximum, value => build(value, (int)secondValue));
                    return;
                }

                int secondRange = secondMaximum - secondMinimum + 1;
                string outOfRange = BeginThrowTarget(node, "ArgumentOutOfRangeException", "immediate", out bool inlineThrow);
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ArmRegister.X16, 4), Reg(OperandRegister(node, first), 4), ArmOperand.ImmediateOperand(firstMinimum)));
                _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ArmRegister.X16, 4), ArmOperand.ImmediateOperand(firstMaximum - firstMinimum)));
                _owner.EmitConditionalJump(ArmCondition.Hi, outOfRange);
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ArmRegister.X17, 4), Reg(OperandRegister(node, second), 4), ArmOperand.ImmediateOperand(secondMinimum)));
                _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ArmRegister.X17, 4), ArmOperand.ImmediateOperand(secondRange - 1)));
                _owner.EmitConditionalJump(ArmCondition.Hi, outOfRange);
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.X16, 8), Reg(ArmRegister.X17, 8),
                    ArmOperand.ShiftedRegister(ArmRegister.X16, ArmShiftKind.Lsl, System.Numerics.BitOperations.Log2((uint)secondRange), 8)));
                string table = _owner.CreateLocalLabel($"{_methodLabel}_immediates_{node.LinearId}");
                string done = _owner.CreateLocalLabel($"{_methodLabel}_immediates_done_{node.LinearId}");
                AddRelocation(OffsetOfNextInstruction(), table, ArmObjectRelocationKind.AArch64Adr21);
                _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Adr, Reg(ArmRegister.X17, 8), ArmOperand.SymbolOperand(table, ArmRelocationKind.Adr)));
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.X17, 8), Reg(ArmRegister.X17, 8),
                    ArmOperand.ShiftedRegister(ArmRegister.X16, ArmShiftKind.Lsl, 3, 8)));
                _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Br, Reg(ArmRegister.X17, 8)));
                _owner.DefineLabel(table);
                for (int a = firstMinimum; a <= firstMaximum; a++)
                {
                    for (int b = secondMinimum; b <= secondMaximum; b++)
                    {
                        _owner.Emit(build(a, b));
                        _owner.EmitJump(done);
                    }
                }
                _owner.DefineLabel(done);
                EndThrowTarget(node, "ArgumentOutOfRangeException", outOfRange, inlineThrow);
            }

            private void EmitArmIntrinsic(GenTree node)
            {
                ArmIntrinsicInfo info = ArmIntrinsics.Info(node);
                RuntimePrimitiveKind baseKind = HWIntrinsics.BaseKind(node);
                ArmInstrKind instruction = info.Instruction(baseKind);
                bool scalar = info.Has(ArmIntrinsicFlags.Scalar);
                int count = node.Operands.Length;
                RuntimeType? resultType = node.StackKind == GenStackKind.Void ? null : node.Type;
                ArmRegister d = resultType is null ? ArmRegister.Invalid : ToArm(RequireResultRegister(node));
                RuntimeType? TypeOf(int i) => node.Operands[i].Type;
                ArmRegister Use(int i) => OperandRegister(node, i);
                ArmOperand Value(int i, int element = 0) => ValueOperand(TypeOf(i), Use(i), scalar, element);
                ArmOperand Result(int element = 0) => ValueOperand(resultType, d, scalar, element);
                ArmOperand Address(int i) => Mem(Use(i), 0, 0);
                int Element(int i) => ElementSizeOf(TypeOf(i));
                void WithImmediate(int i, Func<int, ArmInstruction> build)
                {
                    (int minimum, int maximum) = ArmIntrinsics.ImmediateBounds(node, i);
                    EmitWithImmediate(node, i, minimum, maximum, build);
                }

                if (ArmIntrinsics.TupleCount(node) != 0)
                {
                    EmitArmTupleIntrinsic(node, info, d);
                    return;
                }

                switch (info.Name)
                {
                    case "Yield":
                        _owner.Emit(new ArmInstruction(ArmInstrKind.Yield));
                        return;
                    case "LoadVector64":
                    case "LoadVector128":
                        EmitMemoryLoad(d, Use(0), 0, VectorSizeOf(resultType), signed: false);
                        return;
                    case "LoadAndReplicateToVector64":
                    case "LoadAndReplicateToVector128":
                        Emit2(ArmInstrKind.Ld1r, ArmOperand.VectorList(d, ElementSizeOf(resultType), 1, VectorSizeOf(resultType)), Address(0));
                        return;
                    case "LoadAndInsertScalar":
                    {
                        ArmRegister address = Use(2);
                        EmitVectorMove(d, Use(0));
                        int e = Element(0);
                        WithImmediate(1, i => ArmInstruction.Binary(ArmInstrKind.Ld1, ArmOperand.VectorElement(d, e, i), Mem(address, 0, 0)));
                        return;
                    }
                    case "Store":
                        EmitMemoryStore(Use(1), Use(0), 0, VectorSizeOf(TypeOf(1)));
                        return;
                    case "StoreSelectedScalar":
                    {
                        ArmRegister address = Use(0), value = Use(1);
                        int e = Element(1);
                        WithImmediate(2, i => ArmInstruction.Binary(ArmInstrKind.St1, ArmOperand.VectorElement(value, e, i), Mem(address, 0, 0)));
                        return;
                    }
                    case "StorePair":
                    case "StorePairNonTemporal":
                    case "StorePairScalar":
                    case "StorePairScalarNonTemporal":
                    {
                        int size = info.Name.StartsWith("StorePairScalar", StringComparison.Ordinal) ? Element(1) : VectorSizeOf(TypeOf(1));
                        _owner.Emit(ArmInstruction.Ternary(instruction, Reg(Use(1), size), Reg(Use(2), size), Mem(Use(0), 0, size)));
                        return;
                    }
                    case "Extract":
                    {
                        ArmRegister vector = Use(0);
                        int e = Element(0);
                        bool floating = HWIntrinsics.IsFloating(baseKind);
                        ArmInstrKind move = floating ? ArmInstrKind.Dup : e <= 2 && !HWIntrinsics.IsUnsigned(baseKind) ? ArmInstrKind.Smov : ArmInstrKind.Umov;
                        ArmOperand destination = floating ? Reg(d, e) : Reg(d, e == 8 ? 8 : 4);
                        WithImmediate(1, i => ArmInstruction.Binary(move, destination, ArmOperand.VectorElement(vector, e, i)));
                        return;
                    }
                    case "ExtractVector64":
                    case "ExtractVector128":
                    {
                        ArmRegister upper = Use(0), lower = Use(1);
                        int vs = VectorSizeOf(resultType), e = Element(0);
                        WithImmediate(2, i => new ArmInstruction(ArmInstrKind.Ext, Vec(d, 1, vs), Vec(upper, 1, vs), Vec(lower, 1, vs), ArmOperand.ImmediateOperand(i * e)));
                        return;
                    }
                    case "DuplicateSelectedScalarToVector64":
                    case "DuplicateSelectedScalarToVector128":
                    {
                        ArmRegister value = Use(0);
                        ArmOperand destination = Vec(d, ElementSizeOf(resultType), VectorSizeOf(resultType));
                        int e = Element(0);
                        WithImmediate(1, i => ArmInstruction.Binary(ArmInstrKind.Dup, destination, ArmOperand.VectorElement(value, e, i)));
                        return;
                    }
                    case "DuplicateToVector64":
                    case "DuplicateToVector128":
                    {
                        int e = ElementSizeOf(resultType), vs = VectorSizeOf(resultType);
                        ArmRegister value = Use(0);
                        bool floating = HWIntrinsics.IsFloating(baseKind);
                        if (e == 8 && vs == 8)
                        {
                            if (floating)
                                Emit2(ArmInstrKind.Fmov, Reg(d, 8), Reg(value, 8));
                            else
                                EmitFmovFromGeneral(FloatRegisterOf(d), value, 8);
                        }
                        else if (floating)
                            Emit2(ArmInstrKind.Dup, Vec(d, e, vs), ArmOperand.VectorElement(value, e, 0));
                        else
                            Emit2(ArmInstrKind.Dup, Vec(d, e, vs), Reg(value, e == 8 ? 8 : 4));
                        return;
                    }
                    case "Insert":
                    {
                        ArmRegister data = Use(2);
                        EmitVectorMove(d, Use(0));
                        int e = Element(0);
                        ArmOperand source = HWIntrinsics.IsFloating(baseKind) ? ArmOperand.VectorElement(data, e, 0) : Reg(data, e == 8 ? 8 : 4);
                        WithImmediate(1, i => ArmInstruction.Binary(ArmInstrKind.Ins, ArmOperand.VectorElement(d, e, i), source));
                        return;
                    }
                    case "InsertScalar":
                    {
                        ArmRegister value = Use(2);
                        EmitVectorMove(d, Use(0));
                        WithImmediate(1, i => ArmInstruction.Binary(ArmInstrKind.Ins, ArmOperand.VectorElement(d, 8, i), ArmOperand.VectorElement(value, 8, 0)));
                        return;
                    }
                    case "InsertSelectedScalar":
                    {
                        ArmRegister value = Use(2);
                        EmitVectorMove(d, Use(0));
                        int e = Element(0);
                        EmitWithImmediates(node, 1, 3, (target, source) => ArmInstruction.Binary(ArmInstrKind.Ins,
                            ArmOperand.VectorElement(d, e, target), ArmOperand.VectorElement(value, e, source)));
                        return;
                    }
                    case "VectorTableLookup":
                    case "VectorTableLookupExtension":
                    {
                        int table = count - 2, vs = VectorSizeOf(resultType);
                        if (info.Name == "VectorTableLookupExtension")
                            EmitVectorMove(d, Use(0));
                        Emit3(instruction, Vec(d, 1, vs), ArmOperand.VectorList(Use(table), 1, 1, 16), Vec(Use(table + 1), 1, vs));
                        return;
                    }
                    case "FusedMultiplyAddScalar":
                    case "FusedMultiplySubtractScalar":
                    case "FusedMultiplyAddNegatedScalar":
                    case "FusedMultiplySubtractNegatedScalar":
                    {
                        int e = Element(0);
                        _owner.Emit(ArmInstruction.Quaternary(instruction, Reg(d, e), Reg(Use(1), e), Reg(Use(2), e), Reg(Use(0), e)));
                        return;
                    }
                    case "AddSaturateScalar" when count == 2:
                    {
                        bool leftUnsigned = HWIntrinsics.IsUnsigned(ArmIntrinsics.ElementKind(TypeOf(0)));
                        if (ArmIntrinsics.IsMixedSaturate(node))
                        {
                            EmitVectorMove(d, Use(0));
                            Emit2(leftUnsigned ? ArmInstrKind.Usqadd : ArmInstrKind.Suqadd, Result(), Value(1));
                        }
                        else
                            Emit3(leftUnsigned ? ArmInstrKind.Uqadd : ArmInstrKind.Sqadd, Result(), Value(0), Value(1));
                        return;
                    }
                    case "HashUpdateChoose":
                    case "HashUpdateMajority":
                    case "HashUpdateParity":
                    case "HashUpdate1":
                    case "HashUpdate2":
                    {
                        EmitVectorMove(d, Use(0));
                        bool sha1 = instruction is ArmInstrKind.Sha1c or ArmInstrKind.Sha1m or ArmInstrKind.Sha1p;
                        Emit3(instruction, Reg(d, 16), Reg(Use(1), sha1 ? 4 : 16), Vec(Use(2), 4, 16));
                        return;
                    }
                    case "FixedRotate":
                        Emit2(instruction, Reg(d, 4), Reg(Use(0), 4));
                        return;
                    case "BitwiseSelect":
                        EmitBitSelect(d, Use(1), Use(2), Use(0), VectorSizeOf(resultType));
                        return;
                }

                if (ArmIntrinsics.UsesRegisterShift(node))
                {
                    EmitArmRegisterShift(node, info, d, scalar);
                    return;
                }

                bool rmw = ArmIntrinsics.IsReadModifyWrite(node);
                if (rmw)
                    EmitVectorMove(d, Use(0));

                int forced = instruction switch
                {
                    ArmInstrKind.And or ArmInstrKind.Bic or ArmInstrKind.Orr or ArmInstrKind.Orn or ArmInstrKind.Eor or
                    ArmInstrKind.Bsl or ArmInstrKind.Bit or ArmInstrKind.Bif or ArmInstrKind.Not => 1,
                    _ => info.Name switch { "ReverseElement8" => 1, "ReverseElement16" => 2, "ReverseElement32" => 4, _ => 0 },
                };
                ReadOnlySpan<int> immediates = ArmIntrinsics.ImmediateOperands(info, count);
                int immediate = immediates.Length == 0 ? -1 : immediates[0];
                int indexed = info.Category == ArmIntrinsicCategory.ByElement ? ArmIntrinsics.IndexedElementOperand(node) : -1;

                var operands = new List<int>();
                for (int i = rmw ? 1 : 0; i < count; i++)
                {
                    if (i != immediate)
                        operands.Add(i);
                }
                if (info.Name.StartsWith("CompareLessThan", StringComparison.Ordinal) || info.Name.StartsWith("AbsoluteCompareLessThan", StringComparison.Ordinal))
                    operands.Reverse();

                if (instruction is ArmInstrKind.Saddl or ArmInstrKind.Uaddl or ArmInstrKind.Ssubl or ArmInstrKind.Usubl &&
                    Element(0) != Element(1))
                {
                    instruction = instruction switch
                    {
                        ArmInstrKind.Saddl => ArmInstrKind.Saddw,
                        ArmInstrKind.Uaddl => ArmInstrKind.Uaddw,
                        ArmInstrKind.Ssubl => ArmInstrKind.Ssubw,
                        _ => ArmInstrKind.Usubw,
                    };
                }

                ArmOperand ResultOperand()
                {
                    if (instruction is ArmInstrKind.Addv or ArmInstrKind.Saddlv or ArmInstrKind.Uaddlv or ArmInstrKind.Smaxv or ArmInstrKind.Sminv or
                        ArmInstrKind.Umaxv or ArmInstrKind.Uminv or ArmInstrKind.Fmaxv or ArmInstrKind.Fminv or ArmInstrKind.Fmaxnmv or ArmInstrKind.Fminnmv ||
                        instruction is ArmInstrKind.Addp or ArmInstrKind.Faddp or ArmInstrKind.Fmaxp or ArmInstrKind.Fminp or ArmInstrKind.Fmaxnmp or ArmInstrKind.Fminnmp &&
                        info.Name.EndsWith("Scalar", StringComparison.Ordinal))
                    {
                        return Reg(d, ElementSizeOf(resultType));
                    }
                    if (instruction == ArmInstrKind.Pmull && ElementSizeOf(resultType) == 8)
                        return Vec(d, 16, 16);
                    if (instruction is ArmInstrKind.Saddlp or ArmInstrKind.Uaddlp or ArmInstrKind.Sadalp or ArmInstrKind.Uadalp)
                        return Vec(d, ElementSizeOf(resultType), VectorSizeOf(resultType));
                    if (VectorSizeOf(resultType) == 0 && instruction is ArmInstrKind.Clz or ArmInstrKind.Cls or ArmInstrKind.Rbit or ArmInstrKind.Rev)
                        return ValueOperand(TypeOf(0), d, scalar);
                    return Result(forced);
                }

                ArmOperand OperandAt(int i, int index)
                {
                    if (ArmIntrinsics.IsAddressOperand(node, i))
                        return Address(i);
                    if (i == indexed)
                        return ArmOperand.VectorElement(Use(i), instruction is ArmInstrKind.Sdot or ArmInstrKind.Udot ? 4 : Element(i), index);
                    if (instruction == ArmInstrKind.Pmull && Element(i) == 8)
                        return Vec(Use(i), 8, VectorSizeOf(TypeOf(i)));
                    bool pairwiseSource = instruction is ArmInstrKind.Faddp or ArmInstrKind.Fmaxp or ArmInstrKind.Fminp or ArmInstrKind.Fmaxnmp or ArmInstrKind.Fminnmp &&
                        info.Name.EndsWith("Scalar", StringComparison.Ordinal);
                    if (pairwiseSource)
                        return Vec(Use(i), Element(i), VectorSizeOf(TypeOf(i)));
                    return Value(i, forced);
                }

                ArmInstruction Build(int value)
                {
                    var list = new List<ArmOperand>(5);
                    if (resultType is not null)
                        list.Add(ResultOperand());
                    foreach (int i in operands)
                        list.Add(OperandAt(i, indexed >= 0 && immediate < 0 ? 0 : value));
                    if (immediate >= 0 && indexed < 0)
                        list.Add(ArmOperand.ImmediateOperand(value));
                    if (instruction is ArmInstrKind.Smsubl or ArmInstrKind.Umsubl && list.Count == 3)
                        list.Add(Reg(ArmRegister.Xzr, 8));
                    if (instruction is ArmInstrKind.Sshll or ArmInstrKind.Ushll && immediate < 0)
                        list.Add(ArmOperand.ImmediateOperand(0));
                    while (list.Count < 5)
                        list.Add(default);
                    return new ArmInstruction(instruction, list[0], list[1], list[2], list[3], operand4: list[4]);
                }

                if (immediate >= 0)
                    WithImmediate(immediate, Build);
                else
                    _owner.Emit(Build(0));
            }

            // Multi-register instructions name consecutive registers, which the reserved v28-v31 provide
            private void EmitArmTupleIntrinsic(GenTree node, ArmIntrinsicInfo info, ArmRegister d)
            {
                int count = ArmIntrinsics.TupleCount(node), element = ArmIntrinsics.TupleElement(node);
                int operands = node.Operands.Length;
                RuntimeType? elementType = null;
                for (int i = 0; i < operands && elementType is null; i++)
                {
                    if (VectorSizeOf(node.Operands[i].Type) != 0)
                        elementType = node.Operands[i].Type;
                }
                elementType ??= node.Type;
                int e = ElementSizeOf(elementType), vs = VectorSizeOf(elementType);
                ArmOperand List(int registers) => ArmOperand.VectorList(ArmRegister.V28, e, registers, vs);
                ArmOperand Lanes(int lane) => ArmOperand.VectorListLane(ArmRegister.V28, e, count, lane);
                void Gather(int first)
                {
                    for (int i = 0; i < count; i++)
                        EmitVectorMove((ArmRegister)((int)ArmRegister.V28 + i), OperandRegister(node, first + i));
                }
                ArmInstrKind Structure(ArmInstrKind one, ArmInstrKind two, ArmInstrKind three, ArmInstrKind four)
                    => count switch { 1 => one, 2 => two, 3 => three, _ => four };

                switch (info.Name)
                {
                    case "VectorTableLookup":
                    case "VectorTableLookupExtension":
                    {
                        int first = info.Name == "VectorTableLookup" ? 0 : 1;
                        ArmRegister indexes = OperandRegister(node, operands - 1);
                        if (first == 1)
                            EmitVectorMove(d, OperandRegister(node, 0));
                        Gather(first);
                        Emit3(first == 0 ? ArmInstrKind.Tbl : ArmInstrKind.Tbx, Vec(d, 1, VectorSizeOf(node.Type)), ArmOperand.VectorList(ArmRegister.V28, 1, count, 16),
                            Vec(indexes, 1, VectorSizeOf(node.Type)));
                        return;
                    }
                    case "Store":
                    case "StoreVectorAndZip":
                        Gather(1);
                        Emit2(info.Name == "Store" ? ArmInstrKind.St1 : Structure(ArmInstrKind.St1, ArmInstrKind.St2, ArmInstrKind.St3, ArmInstrKind.St4),
                            List(count), Mem(OperandRegister(node, 0), 0, 0));
                        return;
                    case "StoreSelectedScalar":
                    {
                        ArmRegister address = OperandRegister(node, 0);
                        Gather(1);
                        ArmInstrKind store = Structure(ArmInstrKind.St1, ArmInstrKind.St2, ArmInstrKind.St3, ArmInstrKind.St4);
                        (int minimum, int maximum) = ArmIntrinsics.ImmediateBounds(node, operands - 1);
                        EmitWithImmediate(node, operands - 1, minimum, maximum, lane => ArmInstruction.Binary(store, Lanes(lane), Mem(address, 0, 0)));
                        return;
                    }
                }

                ArmRegister source = OperandRegister(node, operands - 1);
                if (info.Name.StartsWith("LoadPair", StringComparison.Ordinal))
                {
                    int size = info.Name.StartsWith("LoadPairScalar", StringComparison.Ordinal) ? e : vs;
                    _owner.Emit(ArmInstruction.Ternary(info.Name.EndsWith("NonTemporal", StringComparison.Ordinal) ? ArmInstrKind.Ldnp : ArmInstrKind.Ldp,
                        Reg(ArmRegister.V28, size), Reg(ArmRegister.V29, size), Mem(source, 0, size)));
                }
                else if (info.Name.StartsWith("LoadAndInsertScalar", StringComparison.Ordinal))
                {
                    Gather(0);
                    ArmInstrKind load = Structure(ArmInstrKind.Ld1, ArmInstrKind.Ld2, ArmInstrKind.Ld3, ArmInstrKind.Ld4);
                    (int minimum, int maximum) = ArmIntrinsics.ImmediateBounds(node, operands - 2);
                    EmitWithImmediate(node, operands - 2, minimum, maximum, lane => ArmInstruction.Binary(load, Lanes(lane), Mem(source, 0, 0)));
                }
                else if (info.Name.StartsWith("LoadAndReplicate", StringComparison.Ordinal))
                    Emit2(Structure(ArmInstrKind.Ld1r, ArmInstrKind.Ld2r, ArmInstrKind.Ld3r, ArmInstrKind.Ld4r), List(count), Mem(source, 0, 0));
                else if (info.Name.EndsWith("AndUnzip", StringComparison.Ordinal))
                    Emit2(Structure(ArmInstrKind.Ld1, ArmInstrKind.Ld2, ArmInstrKind.Ld3, ArmInstrKind.Ld4), List(count), Mem(source, 0, 0));
                else
                    Emit2(ArmInstrKind.Ld1, List(count), Mem(source, 0, 0));
                EmitVectorMove(d, (ArmRegister)((int)ArmRegister.V28 + element));
            }

            private void EmitArmRegisterShift(GenTree node, ArmIntrinsicInfo info, ArmRegister d, bool scalar)
            {
                RuntimeType? type = node.Operands[0].Type;
                int e = ElementSizeOf(type), vs = VectorSizeOf(type);
                ArmRegister source = OperandRegister(node, 0), amount = OperandRegister(node, 1);
                ArmRegister counts = FloatTemp(node, d);
                bool left = info.Category == ArmIntrinsicCategory.ShiftLeft;
                ArmOperand Op(ArmRegister register) => ValueOperand(type, register, scalar);
                if (left)
                    Emit2(ArmInstrKind.Dup, e == 8 && vs == 8 ? Vec(counts, 8, 16) : Vec(counts, e, vs), Reg(amount, e == 8 ? 8 : 4));
                else
                {
                    _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ArmRegister.X16, 8), Reg(ArmRegister.Xzr, 8), Reg(amount, 8)));
                    Emit2(ArmInstrKind.Dup, e == 8 && vs == 8 ? Vec(counts, 8, 16) : Vec(counts, e, vs), Reg(ArmRegister.X16, e == 8 ? 8 : 4));
                }
                bool arithmetic = info.Instruction(HWIntrinsics.BaseKind(node)) is ArmInstrKind.Sshr;
                Emit3(arithmetic ? ArmInstrKind.Sshl : ArmInstrKind.Ushl, Op(d), Op(source), Op(counts));
            }

            private void EmitHWIntrinsic(GenTree node)
            {
                if (ArmIntrinsics.IsArm(node))
                {
                    EmitArmIntrinsic(node);
                    return;
                }
                HWIntrinsicId id = HWIntrinsics.Id(node);
                RuntimePrimitiveKind kind = HWIntrinsics.BaseKind(node);
                int size = HWIntrinsics.SimdSize(node);
                int e = HWIntrinsics.ElementSize(kind);
                int vs = size <= 8 ? 8 : 16;
                int bits = e * 8;
                bool floating = HWIntrinsics.IsFloating(kind);
                bool unsigned = HWIntrinsics.IsUnsigned(kind);
                bool scalar = e == 8 && vs == 8;
                ArmRegister d = ToArm(RequireResultRegister(node));

                ArmOperand Op(ArmRegister register) => scalar ? Reg(register, 8) : Vec(register, e, vs);
                ArmOperand Bytes(ArmRegister register) => Vec(register, 1, vs);
                ArmRegister A() => OperandRegister(node, 0);
                ArmRegister B() => OperandRegister(node, 1);
                void Binary(ArmInstrKind integer, ArmInstrKind floatingOpcode) => Emit3(floating ? floatingOpcode : integer, Op(d), Op(A()), Op(B()));
                void Unary(ArmInstrKind integer, ArmInstrKind floatingOpcode) => Emit2(floating ? floatingOpcode : integer, Op(d), Op(A()));

                switch (id)
                {
                    case HWIntrinsicId.Zero:
                        Emit2(ArmInstrKind.Movi, Vec(d, 1, 16), ArmOperand.ImmediateOperand(0));
                        return;
                    case HWIntrinsicId.AllBitsSet:
                        Emit2(ArmInstrKind.Movi, Vec(d, 1, 16), ArmOperand.ImmediateOperand(255));
                        return;
                    case HWIntrinsicId.Broadcast:
                    case HWIntrinsicId.CreateElements:
                        EmitVectorCreate(node, d, e, vs, size, floating, scalar);
                        return;
                    case HWIntrinsicId.CreateScalar:
                    case HWIntrinsicId.CreateScalarUnsafe:
                    {
                        ArmRegister source = A();
                        if (floating)
                        {
                            if (id == HWIntrinsicId.CreateScalar || d != source)
                                Emit2(ArmInstrKind.Fmov, Reg(d, e), Reg(source, e));
                        }
                        else if (e >= 4)
                            EmitFmovFromGeneral(FloatRegisterOf(d), source, e);
                        else
                        {
                            if (id == HWIntrinsicId.CreateScalar)
                                Emit2(ArmInstrKind.Movi, Vec(d, 1, 16), ArmOperand.ImmediateOperand(0));
                            Emit2(ArmInstrKind.Ins, ArmOperand.VectorElement(d, e, 0), Reg(source, 4));
                        }
                        return;
                    }
                    case HWIntrinsicId.BitCast:
                        EmitVectorMove(d, A());
                        return;
                    case HWIntrinsicId.ZeroExtend:
                    {
                        ArmRegister source = A();
                        if (SimdTypes.SizeOf(node.Operands[0].StackKind) <= 8)
                        {
                            Emit2(ArmInstrKind.Fmov, Reg(d, 8), Reg(source, 8));
                            return;
                        }
                        EmitVectorMove(d, source);
                        Emit2(ArmInstrKind.Ins, ArmOperand.VectorElement(d, 4, 3), Reg(ArmRegister.Xzr, 4));
                        return;
                    }
                    case HWIntrinsicId.GetUpper:
                        Emit2(ArmInstrKind.Dup, Reg(d, 8), ArmOperand.VectorElement(A(), 8, 1));
                        return;
                    case HWIntrinsicId.WithLower:
                    case HWIntrinsicId.WithUpper:
                    {
                        ArmRegister vector = A(), half = B();
                        int replaced = id == HWIntrinsicId.WithLower ? 0 : 1;
                        if (d == half && d != vector)
                        {
                            if (replaced == 1)
                                EmitInsertLane(d, 8, 1, d, 0);
                            EmitInsertLane(d, 8, 1 - replaced, vector, 1 - replaced);
                            return;
                        }
                        EmitVectorMove(d, vector);
                        EmitInsertLane(d, 8, replaced, half, 0);
                        return;
                    }
                    case HWIntrinsicId.Combine:
                    {
                        ArmRegister lower = A(), upper = B();
                        if (d == upper && d != lower)
                        {
                            EmitInsertLane(d, 8, 1, d, 0);
                            EmitInsertLane(d, 8, 0, lower, 0);
                            return;
                        }
                        EmitVectorMove(d, lower);
                        EmitInsertLane(d, 8, 1, upper, 0);
                        return;
                    }
                    case HWIntrinsicId.GetElement:
                    case HWIntrinsicId.ToScalar:
                    {
                        int index = id == HWIntrinsicId.ToScalar ? 0 : ContainedImmediate(node, 1);
                        ArmRegister source = A();
                        if (floating)
                        {
                            if (index != 0 || d != source)
                                Emit2(ArmInstrKind.Dup, Reg(d, e), ArmOperand.VectorElement(source, e, index));
                        }
                        else
                        {
                            Emit2(e <= 2 && !unsigned ? ArmInstrKind.Smov : ArmInstrKind.Umov, Reg(d, e == 8 ? 8 : 4),
                                ArmOperand.VectorElement(source, e, index));
                        }
                        return;
                    }
                    case HWIntrinsicId.WithElement:
                    {
                        int index = ContainedImmediate(node, 1);
                        ArmRegister vector = A(), value = OperandRegister(node, 2);
                        EmitVectorMove(d, vector);
                        if (floating)
                            EmitInsertLane(d, e, index, value, 0);
                        else
                            Emit2(ArmInstrKind.Ins, ArmOperand.VectorElement(d, e, index), Reg(value, e == 8 ? 8 : 4));
                        return;
                    }

                    case HWIntrinsicId.Add:
                        Binary(ArmInstrKind.Add, ArmInstrKind.Fadd);
                        return;
                    case HWIntrinsicId.Subtract:
                        Binary(ArmInstrKind.Sub, ArmInstrKind.Fsub);
                        return;
                    case HWIntrinsicId.Multiply:
                        Binary(ArmInstrKind.Mul, ArmInstrKind.Fmul);
                        return;
                    case HWIntrinsicId.Divide:
                        Binary(ArmInstrKind.Invalid, ArmInstrKind.Fdiv);
                        return;
                    case HWIntrinsicId.Negate:
                        Unary(ArmInstrKind.Neg, ArmInstrKind.Fneg);
                        return;
                    case HWIntrinsicId.Abs:
                        Unary(ArmInstrKind.Abs, ArmInstrKind.Fabs);
                        return;
                    case HWIntrinsicId.Sqrt:
                        Unary(ArmInstrKind.Invalid, ArmInstrKind.Fsqrt);
                        return;
                    case HWIntrinsicId.Floor:
                        Unary(ArmInstrKind.Invalid, ArmInstrKind.Frintm);
                        return;
                    case HWIntrinsicId.Ceiling:
                        Unary(ArmInstrKind.Invalid, ArmInstrKind.Frintp);
                        return;
                    case HWIntrinsicId.Min:
                    case HWIntrinsicId.Max:
                    {
                        bool max = id == HWIntrinsicId.Max;
                        if (floating || e < 8)
                        {
                            Binary(max ? unsigned ? ArmInstrKind.Umax : ArmInstrKind.Smax : unsigned ? ArmInstrKind.Umin : ArmInstrKind.Smin,
                                max ? ArmInstrKind.Fmax : ArmInstrKind.Fmin);
                            return;
                        }
                        ArmRegister a = A(), b = B(), mask = FloatTemp(node, d);
                        ArmInstrKind greater = unsigned ? ArmInstrKind.Cmhi : ArmInstrKind.Cmgt;
                        Emit3(greater, Op(mask), Op(max ? b : a), Op(max ? a : b));
                        EmitBitSelect(d, b, a, mask, vs);
                        return;
                    }
                    case HWIntrinsicId.AddSaturate:
                        Binary(unsigned ? ArmInstrKind.Uqadd : ArmInstrKind.Sqadd, ArmInstrKind.Invalid);
                        return;
                    case HWIntrinsicId.SubtractSaturate:
                        Binary(unsigned ? ArmInstrKind.Uqsub : ArmInstrKind.Sqsub, ArmInstrKind.Invalid);
                        return;

                    case HWIntrinsicId.And:
                        Emit3(ArmInstrKind.And, Bytes(d), Bytes(A()), Bytes(B()));
                        return;
                    case HWIntrinsicId.Or:
                        Emit3(ArmInstrKind.Orr, Bytes(d), Bytes(A()), Bytes(B()));
                        return;
                    case HWIntrinsicId.Xor:
                        Emit3(ArmInstrKind.Eor, Bytes(d), Bytes(A()), Bytes(B()));
                        return;
                    case HWIntrinsicId.AndNot:
                        Emit3(ArmInstrKind.Bic, Bytes(d), Bytes(A()), Bytes(B()));
                        return;
                    case HWIntrinsicId.Not:
                        Emit2(ArmInstrKind.Not, Bytes(d), Bytes(A()));
                        return;
                    case HWIntrinsicId.ConditionalSelect:
                        EmitBitSelect(d, B(), OperandRegister(node, 2), A(), vs);
                        return;

                    case HWIntrinsicId.CompareEqual:
                        Binary(ArmInstrKind.Cmeq, ArmInstrKind.Fcmeq);
                        return;
                    case HWIntrinsicId.CompareGreaterThan:
                    case HWIntrinsicId.CompareGreaterThanOrEqual:
                    case HWIntrinsicId.CompareLessThan:
                    case HWIntrinsicId.CompareLessThanOrEqual:
                    {
                        bool orEqual = id is HWIntrinsicId.CompareGreaterThanOrEqual or HWIntrinsicId.CompareLessThanOrEqual;
                        bool swap = id is HWIntrinsicId.CompareLessThan or HWIntrinsicId.CompareLessThanOrEqual;
                        ArmInstrKind opcode = floating ? orEqual ? ArmInstrKind.Fcmge : ArmInstrKind.Fcmgt
                            : unsigned ? orEqual ? ArmInstrKind.Cmhs : ArmInstrKind.Cmhi
                            : orEqual ? ArmInstrKind.Cmge : ArmInstrKind.Cmgt;
                        ArmRegister a = A(), b = B();
                        Emit3(opcode, Op(d), Op(swap ? b : a), Op(swap ? a : b));
                        return;
                    }

                    case HWIntrinsicId.ShiftLeft:
                    case HWIntrinsicId.ShiftRightArithmetic:
                    case HWIntrinsicId.ShiftRightLogical:
                        EmitVectorShift(node, id, d, e, vs, scalar);
                        return;

                    case HWIntrinsicId.EqualsAll:
                    case HWIntrinsicId.EqualsAny:
                    {
                        ArmRegister lanes = FloatTemp(node);
                        Emit3(floating ? ArmInstrKind.Fcmeq : ArmInstrKind.Cmeq, Op(lanes), Op(A()), Op(B()));
                        if (size == 12)
                            EmitInsertLane(lanes, 4, 3, lanes, 2);
                        bool all = id == HWIntrinsicId.EqualsAll;
                        if (vs == 16)
                            Emit3(all ? ArmInstrKind.Uminp : ArmInstrKind.Umaxp, Vec(lanes, 4, 16), Vec(lanes, 4, 16), Vec(lanes, 4, 16));
                        EmitFmovToGeneral(d, FloatRegisterOf(lanes), 8);
                        _owner.Emit(ArmInstruction.Binary(all ? ArmInstrKind.Cmn : ArmInstrKind.Cmp, Reg(d, 8), ArmOperand.ImmediateOperand(all ? 1 : 0)));
                        EmitConditionSet(d, all ? ArmCondition.Eq : ArmCondition.Ne);
                        return;
                    }
                    case HWIntrinsicId.ExtractMostSignificantBits:
                        EmitExtractMostSignificantBits(node, d, e, vs, scalar);
                        return;
                    case HWIntrinsicId.Sum:
                        EmitVectorSum(node, d, e, vs, size, floating, unsigned);
                        return;
                }

                throw Unsupported(node, $"Vector intrinsic {HWIntrinsics.Format(node)} is not implemented on ARM64");
            }

            private static MachineRegister FloatRegisterOf(ArmRegister vector)
                => MachineRegister.F0 + (vector - ArmRegister.V0);

            private void EmitBitSelect(ArmRegister destination, ArmRegister whenSet, ArmRegister whenClear, ArmRegister mask, int vs)
            {
                if (destination == mask)
                    Emit3(ArmInstrKind.Bsl, Vec(destination, 1, vs), Vec(whenSet, 1, vs), Vec(whenClear, 1, vs));
                else if (destination == whenClear)
                    Emit3(ArmInstrKind.Bit, Vec(destination, 1, vs), Vec(whenSet, 1, vs), Vec(mask, 1, vs));
                else if (destination == whenSet)
                    Emit3(ArmInstrKind.Bif, Vec(destination, 1, vs), Vec(whenClear, 1, vs), Vec(mask, 1, vs));
                else
                {
                    EmitVectorMove(destination, mask);
                    Emit3(ArmInstrKind.Bsl, Vec(destination, 1, vs), Vec(whenSet, 1, vs), Vec(whenClear, 1, vs));
                }
            }

            private void EmitVectorCreate(GenTree node, ArmRegister d, int e, int vs, int size, bool floating, bool scalar)
            {
                HWIntrinsicId id = HWIntrinsics.Id(node);
                if (HWIntrinsics.HasConstantElements(node))
                {
                    byte[] bytes = new byte[size == 12 ? 16 : size];
                    for (int offset = 0, i = 0; offset < size; offset += e, i++)
                        WriteElement(bytes, offset, e, node.Operands[id == HWIntrinsicId.Broadcast ? 0 : i]);
                    EmitVectorConstant(d, bytes);
                    return;
                }

                if (id == HWIntrinsicId.Broadcast)
                {
                    ArmRegister value = OperandRegister(node, 0);
                    if (!floating)
                        Emit2(ArmInstrKind.Dup, scalar ? Vec(d, 8, 16) : Vec(d, e, vs), Reg(value, e == 8 ? 8 : 4));
                    else if (scalar)
                        Emit2(ArmInstrKind.Fmov, Reg(d, 8), Reg(value, 8));
                    else
                        Emit2(ArmInstrKind.Dup, Vec(d, e, vs), ArmOperand.VectorElement(value, e, 0));
                    return;
                }

                for (int i = 0; i < node.Operands.Length; i++)
                {
                    ArmRegister value = OperandRegister(node, i);
                    if (floating)
                    {
                        if (i != 0 || d != value)
                            EmitInsertLane(d, e, i, value, 0);
                    }
                    else
                        Emit2(ArmInstrKind.Ins, ArmOperand.VectorElement(d, e, i), Reg(value, e == 8 ? 8 : 4));
                }
            }

            // A variable count wraps at the element width, and a right shift is a left shift by its negation
            private void EmitVectorShift(GenTree node, HWIntrinsicId id, ArmRegister d, int e, int vs, bool scalar)
            {
                ArmRegister source = OperandRegister(node, 0);
                ArmOperand Op(ArmRegister register) => scalar ? Reg(register, 8) : Vec(register, e, vs);
                int bits = e * 8;
                if (TryGetContainedIntegerImmediate(node, 1, out long immediate))
                {
                    int count = (int)(immediate & (bits - 1));
                    if (count == 0)
                    {
                        EmitVectorMove(d, source);
                        return;
                    }
                    ArmInstrKind opcode = id switch
                    {
                        HWIntrinsicId.ShiftLeft => ArmInstrKind.Shl,
                        HWIntrinsicId.ShiftRightArithmetic => ArmInstrKind.Sshr,
                        _ => ArmInstrKind.Ushr,
                    };
                    Emit3(opcode, Op(d), Op(source), ArmOperand.ImmediateOperand(count));
                    return;
                }

                ArmRegister amount = OperandRegister(node, 1);
                ArmRegister counts = FloatTemp(node, d);
                _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.And, Reg(ArmRegister.X16, 4), Reg(amount, 4), ArmOperand.ImmediateOperand(bits - 1)));
                if (id != HWIntrinsicId.ShiftLeft)
                    _owner.Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ArmRegister.X16, 4), Reg(ArmRegister.Xzr, 4), Reg(ArmRegister.X16, 4)));
                Emit2(ArmInstrKind.Dup, scalar ? Vec(counts, 8, 16) : Vec(counts, e, vs), Reg(ArmRegister.X16, e == 8 ? 8 : 4));
                Emit3(id == HWIntrinsicId.ShiftRightArithmetic ? ArmInstrKind.Sshl : ArmInstrKind.Ushl, Op(d), Op(source), Op(counts));
            }

            // Each lane's sign bit moves to bit 0, is shifted to its lane position and the lanes are summed
            private void EmitExtractMostSignificantBits(GenTree node, ArmRegister d, int e, int vs, bool scalar)
            {
                ArmRegister source = OperandRegister(node, 0);
                List<ArmRegister> temps = FloatTemps(node);
                ArmRegister bitsLanes = temps[0], positions = temps[1];
                ArmOperand Op(ArmRegister register) => scalar ? Reg(register, 8) : Vec(register, e, vs);
                Emit3(ArmInstrKind.Ushr, Op(bitsLanes), Op(source), ArmOperand.ImmediateOperand(e * 8 - 1));
                if (scalar)
                {
                    Emit2(ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(bitsLanes, 4, 0));
                    return;
                }

                int lanes = vs / e;
                byte[] shifts = new byte[vs];
                for (int i = 0; i < lanes; i++)
                    shifts[i * e] = (byte)(i % 8);
                EmitVectorConstant(positions, shifts);
                Emit3(ArmInstrKind.Ushl, Op(bitsLanes), Op(bitsLanes), Op(positions));
                switch (e)
                {
                    case 1 when vs == 16:
                        _owner.Emit(new ArmInstruction(ArmInstrKind.Ext, Vec(positions, 1, 16), Vec(bitsLanes, 1, 16), Vec(bitsLanes, 1, 16), ArmOperand.ImmediateOperand(8)));
                        Emit3(ArmInstrKind.Zip1, Vec(bitsLanes, 1, 16), Vec(bitsLanes, 1, 16), Vec(positions, 1, 16));
                        Emit2(ArmInstrKind.Addv, Reg(bitsLanes, 2), Vec(bitsLanes, 2, 16));
                        Emit2(ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(bitsLanes, 2, 0));
                        return;
                    case 1:
                    case 2:
                        Emit2(ArmInstrKind.Addv, Reg(bitsLanes, e), Vec(bitsLanes, e, vs));
                        Emit2(ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(bitsLanes, e, 0));
                        return;
                    case 4 when vs == 16:
                        Emit2(ArmInstrKind.Addv, Reg(bitsLanes, 4), Vec(bitsLanes, 4, 16));
                        Emit2(ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(bitsLanes, 4, 0));
                        return;
                    case 4:
                        Emit3(ArmInstrKind.Addp, Vec(bitsLanes, 4, 8), Vec(bitsLanes, 4, 8), Vec(bitsLanes, 4, 8));
                        Emit2(ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(bitsLanes, 4, 0));
                        return;
                    default:
                        Emit2(ArmInstrKind.Addp, Reg(bitsLanes, 8), Vec(bitsLanes, 8, 16));
                        Emit2(ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(bitsLanes, 4, 0));
                        return;
                }
            }

            private void EmitVectorSum(GenTree node, ArmRegister d, int e, int vs, int size, bool floating, bool unsigned)
            {
                ArmRegister source = OperandRegister(node, 0);
                if (floating)
                {
                    if (e == 8)
                    {
                        if (vs == 16)
                            Emit2(ArmInstrKind.Faddp, Reg(d, 8), Vec(source, 8, 16));
                        else if (d != source)
                            Emit2(ArmInstrKind.Fmov, Reg(d, 8), Reg(source, 8));
                        return;
                    }
                    if (size == 12)
                    {
                        ArmRegister third = FloatTemp(node, d);
                        Emit2(ArmInstrKind.Dup, Reg(third, 4), ArmOperand.VectorElement(source, 4, 2));
                        Emit2(ArmInstrKind.Faddp, Reg(d, 4), Vec(source, 4, 8));
                        Emit3(ArmInstrKind.Fadd, Reg(d, 4), Reg(d, 4), Reg(third, 4));
                        return;
                    }
                    if (vs == 16)
                    {
                        Emit3(ArmInstrKind.Faddp, Vec(d, 4, 16), Vec(source, 4, 16), Vec(source, 4, 16));
                        source = d;
                    }
                    Emit2(ArmInstrKind.Faddp, Reg(d, 4), Vec(source, 4, 8));
                    return;
                }

                ArmRegister total = FloatTemp(node);
                if (e == 8)
                {
                    if (vs == 16)
                    {
                        Emit2(ArmInstrKind.Addp, Reg(total, 8), Vec(source, 8, 16));
                        source = total;
                    }
                    Emit2(ArmInstrKind.Umov, Reg(d, 8), ArmOperand.VectorElement(source, 8, 0));
                    return;
                }
                if (e == 4 && vs == 8)
                    Emit3(ArmInstrKind.Addp, Vec(total, 4, 8), Vec(source, 4, 8), Vec(source, 4, 8));
                else
                    Emit2(ArmInstrKind.Addv, Reg(total, e), Vec(source, e, vs));
                Emit2(e <= 2 && !unsigned ? ArmInstrKind.Smov : ArmInstrKind.Umov, Reg(d, 4), ArmOperand.VectorElement(total, e, 0));
            }
        }
    }
}
