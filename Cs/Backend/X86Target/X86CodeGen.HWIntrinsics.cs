using Cnidaria.X86;
using System;
using System.Collections.Generic;

namespace Cnidaria.Cs
{
    internal static partial class X86CodeGenerator
    {
        private sealed partial class Generator
        {
            private readonly Dictionary<string, string> _vectorConstantLabels = new Dictionary<string, string>(StringComparer.Ordinal);

            public string AddVectorConstant(byte[] bytes)
            {
                string key = Convert.ToHexString(bytes);
                if (!_vectorConstantLabels.TryGetValue(key, out string? label))
                {
                    label = AddConstantData(bytes, bytes.Length, "vector");
                    _vectorConstantLabels.Add(key, label);
                }
                return label;
            }

            private sealed partial class MethodEmitter
            {
                private int _vectorWidth = 16;

                // An AVX-512 compare writes a mask register; the allocator never hands out k1, so it is scratch within a node
                private static X86Operand MaskScratch => Reg(X86Register.K1, 8);

                private void EmitHWIntrinsic(GenTree node)
                {
                    if (X86Intrinsics.IsX86(node))
                    {
                        _vectorWidth = Math.Max(16, HWIntrinsics.SimdSize(node));
                        EmitX86Intrinsic(node);
                        return;
                    }

                    HWIntrinsicId id = HWIntrinsics.Id(node);
                    RuntimePrimitiveKind kind = HWIntrinsics.BaseKind(node);
                    int size = HWIntrinsics.SimdSize(node);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    bool floating = HWIntrinsics.IsFloating(kind);
                    X86Register d = RequireSimdResult(node);
                    _vectorWidth = Math.Max(16, size);

                    switch (id)
                    {
                        case HWIntrinsicId.Zero:
                            SimdZero(d);
                            return;
                        case HWIntrinsicId.AllBitsSet:
                            SimdAllBitsSet(d);
                            return;
                        case HWIntrinsicId.BitCast:
                        case HWIntrinsicId.CreateScalarUnsafe when floating:
                            SimdMove(d, SimdUse(node, 0));
                            return;
                        case HWIntrinsicId.Broadcast:
                        case HWIntrinsicId.CreateElements:
                            EmitSimdCreate(node, id, kind, size, d);
                            return;
                        case HWIntrinsicId.CreateScalar:
                        case HWIntrinsicId.CreateScalarUnsafe:
                            _vectorWidth = 16;
                            EmitSimdCreateScalar(node, id, kind, d);
                            return;
                        case HWIntrinsicId.Combine:
                        case HWIntrinsicId.WithUpper:
                            if (size > 16)
                            {
                                EmitSimdInsertHalf(d, SimdUse(node, 0), SimdUse(node, 1), 1);
                                return;
                            }
                            SimdMove(d, SimdUse(node, 0));
                            SimdOp(X86InstrKind.Movlhps, d, SimdUse(node, 1));
                            return;
                        case HWIntrinsicId.WithLower:
                            if (size > 16)
                            {
                                EmitSimdInsertHalf(d, SimdUse(node, 0), SimdUse(node, 1), 0);
                                return;
                            }
                            SimdMove(d, SimdUse(node, 0));
                            SimdOp(X86InstrKind.Movsd, d, SimdUse(node, 1));
                            return;
                        case HWIntrinsicId.ZeroExtend:
                            // A VEX move clears the register above its own width
                            if (size == 8)
                                SimdOp(X86InstrKind.Movq, d, SimdUse(node, 0));
                            else if (size == 12)
                            {
                                SimdMove(d, SimdUse(node, 0));
                                SimdShiftImmediate(X86InstrKind.Pslldq, d, 4);
                                SimdShiftImmediate(X86InstrKind.Psrldq, d, 4);
                            }
                            else
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Movaps, Reg(d, size), Reg(SimdUse(node, 0), size)));
                            return;
                        case HWIntrinsicId.GetUpper:
                            EmitSimdGetUpper(d, SimdUse(node, 0), size);
                            return;
                        case HWIntrinsicId.GetElement:
                        case HWIntrinsicId.ToScalar:
                            EmitSimdGetElement(node, kind, id == HWIntrinsicId.ToScalar ? 0 : SimdImmediate(node, 1));
                            return;
                        case HWIntrinsicId.WithElement:
                            EmitSimdWithElement(node, kind, d, SimdImmediate(node, 1));
                            return;

                        case HWIntrinsicId.Add:
                            EmitSimdRmw(SimdArithmeticOpcode(id, kind), d, SimdUse(node, 0), SimdUse(node, 1), commutative: true);
                            return;
                        case HWIntrinsicId.Subtract:
                        case HWIntrinsicId.Divide:
                            EmitSimdRmw(SimdArithmeticOpcode(id, kind), d, SimdUse(node, 0), SimdUse(node, 1), commutative: false);
                            return;
                        case HWIntrinsicId.AddSaturate:
                            EmitSimdRmw(SimdSaturatingOpcode(id, kind), d, SimdUse(node, 0), SimdUse(node, 1), commutative: true);
                            return;
                        case HWIntrinsicId.SubtractSaturate:
                            EmitSimdRmw(SimdSaturatingOpcode(id, kind), d, SimdUse(node, 0), SimdUse(node, 1), commutative: false);
                            return;
                        case HWIntrinsicId.Multiply:
                            EmitSimdMultiply(node, kind, d);
                            return;
                        case HWIntrinsicId.And:
                            EmitSimdRmw(floating ? (kind == RuntimePrimitiveKind.Single ? X86InstrKind.Andps : X86InstrKind.Andpd) : X86InstrKind.Pand,
                                d, SimdUse(node, 0), SimdUse(node, 1), commutative: true);
                            return;
                        case HWIntrinsicId.Or:
                            EmitSimdRmw(floating ? (kind == RuntimePrimitiveKind.Single ? X86InstrKind.Orps : X86InstrKind.Orpd) : X86InstrKind.Por,
                                d, SimdUse(node, 0), SimdUse(node, 1), commutative: true);
                            return;
                        case HWIntrinsicId.Xor:
                            EmitSimdRmw(floating ? (kind == RuntimePrimitiveKind.Single ? X86InstrKind.Xorps : X86InstrKind.Xorpd) : X86InstrKind.Pxor,
                                d, SimdUse(node, 0), SimdUse(node, 1), commutative: true);
                            return;
                        case HWIntrinsicId.AndNot:
                            SimdMove(d, SimdUse(node, 1));
                            SimdOp(floating ? (kind == RuntimePrimitiveKind.Single ? X86InstrKind.Andnps : X86InstrKind.Andnpd) : X86InstrKind.Pandn,
                                d, SimdUse(node, 0));
                            return;
                        case HWIntrinsicId.Not:
                            SimdMove(d, SimdUse(node, 0));
                            SimdOpConstant(X86InstrKind.Pxor, d, SimdSplat(-1, 8));
                            return;
                        case HWIntrinsicId.ConditionalSelect:
                            EmitSimdConditionalSelect(node, d);
                            return;
                        case HWIntrinsicId.Negate:
                            if (floating)
                            {
                                SimdMove(d, SimdUse(node, 0));
                                SimdOpConstant(X86InstrKind.Xorps, d, SimdSplat(long.MinValue >> (64 - elementSize * 8), elementSize));
                                return;
                            }
                            SimdZero(d);
                            SimdOp(SimdArithmeticOpcode(HWIntrinsicId.Subtract, kind), d, SimdUse(node, 0));
                            return;
                        case HWIntrinsicId.Abs:
                            EmitSimdAbs(node, kind, d);
                            return;
                        case HWIntrinsicId.Sqrt:
                            SimdOp(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Sqrtps : X86InstrKind.Sqrtpd, d, SimdUse(node, 0));
                            return;
                        case HWIntrinsicId.Floor:
                        case HWIntrinsicId.Ceiling:
                            {
                                bool single = kind == RuntimePrimitiveKind.Single;
                                X86InstrKind round = _vectorWidth == 64
                                    ? (single ? X86InstrKind.Vrndscaleps : X86InstrKind.Vrndscalepd)
                                    : (single ? X86InstrKind.Roundps : X86InstrKind.Roundpd);
                                SimdOp(round, d, SimdUse(node, 0), id == HWIntrinsicId.Floor ? 0x9 : 0xA);
                                return;
                            }
                        case HWIntrinsicId.Min:
                        case HWIntrinsicId.Max:
                            EmitSimdMinMax(node, id == HWIntrinsicId.Min, kind, d);
                            return;

                        case HWIntrinsicId.CompareEqual:
                            if (_vectorWidth == 64)
                                EmitSimdMaskCompare(node, id, kind, d);
                            else
                                EmitSimdCompareEqual(node, kind, d, SimdUse(node, 0), SimdUse(node, 1));
                            return;
                        case HWIntrinsicId.CompareGreaterThan:
                        case HWIntrinsicId.CompareGreaterThanOrEqual:
                        case HWIntrinsicId.CompareLessThan:
                        case HWIntrinsicId.CompareLessThanOrEqual:
                            if (_vectorWidth == 64)
                                EmitSimdMaskCompare(node, id, kind, d);
                            else
                                EmitSimdCompareOrdered(node, id, kind, d);
                            return;

                        case HWIntrinsicId.ShiftLeft:
                        case HWIntrinsicId.ShiftRightArithmetic:
                        case HWIntrinsicId.ShiftRightLogical:
                            EmitSimdShift(node, id, kind, d);
                            return;

                        case HWIntrinsicId.EqualsAll:
                        case HWIntrinsicId.EqualsAny:
                            EmitSimdEqualityTest(node, id, kind, size);
                            return;
                        case HWIntrinsicId.ExtractMostSignificantBits:
                            EmitSimdExtractMostSignificantBits(node, kind, size);
                            return;
                        case HWIntrinsicId.Sum:
                            EmitSimdSum(node, kind, size);
                            return;
                    }

                    throw Unsupported(node, $"vector operation {HWIntrinsics.Format(node)} is not implemented by the x86 backend");
                }

                private bool HasIsa(TargetArchitectureFeatures feature)
                    => HWIntrinsics.HasFeature(Target, feature);

                private X86Register RequireSimdResult(GenTree node)
                    => ToX86Register(RequireResultRegister(node), Target);

                private X86Register SimdUse(GenTree node, int operandIndex)
                {
                    RegisterOperand use = node.Uses[RequireCodegenUseIndexForOperand(node, operandIndex, "vector operation")];
                    if (!use.IsRegister)
                        throw Unsupported(node, $"vector operation operand {operandIndex} is not in a register");
                    return ToX86Register(use.Register, Target);
                }

                private int SimdImmediate(GenTree node, int operandIndex)
                    => TryGetContainedIntegerImmediate(node, operandIndex, out long value)
                        ? checked((int)value)
                        : throw Unsupported(node, $"vector operation operand {operandIndex} is not a contained immediate");

                // The allocator keeps internal registers off the operands but may hand one out again as the result
                private X86Register SimdTemp(GenTree node, int index, X86Register avoid = X86Register.Invalid)
                {
                    int seen = 0;
                    for (int i = 0; i < node.InternalRegisters.Length; i++)
                    {
                        GenTreeInternalRegister register = node.InternalRegisters[i];
                        if (register.RegisterClass != RegisterClass.Float)
                            continue;
                        X86Register candidate = ToX86Register(register.Register, Target);
                        if (candidate == avoid)
                            continue;
                        if (seen++ == index)
                            return candidate;
                    }
                    throw Unsupported(node, $"vector operation has no internal register {index}");
                }

                private void SimdMove(X86Register destination, X86Register source)
                {
                    if (destination != source)
                        SimdOp(X86InstrKind.Movaps, destination, source);
                }

                private void SimdOp(X86InstrKind opcode, X86Register destination, X86Register source)
                    => _owner.Emit(X86Instruction.Binary(opcode, SimdOperand(destination), SimdOperand(source)));

                private void SimdOp(X86InstrKind opcode, X86Register destination, X86Register source, int immediate)
                    => _owner.Emit(X86Instruction.Ternary(opcode, SimdOperand(destination), SimdOperand(source), Imm(immediate)));

                private void SimdShiftImmediate(X86InstrKind opcode, X86Register destination, int count)
                    => _owner.Emit(X86Instruction.Binary(opcode, SimdOperand(destination), Imm(count)));

                private void SimdOpConstant(X86InstrKind opcode, X86Register destination, byte[] bytes)
                    => _owner.Emit(X86Instruction.Binary(opcode, Reg(destination, bytes.Length), _owner.SymbolMemory(_owner.AddVectorConstant(bytes), 0, bytes.Length)));

                private X86Operand SimdOperand(X86Register register)
                    => Reg(register, X86Registers.IsVector(register) ? _vectorWidth : Target.GeneralRegisterSize);

                // A VEX xorps clears the whole register, whatever its width
                private void SimdZero(X86Register register)
                    => _owner.Emit(X86Instruction.Binary(X86InstrKind.Xorps, Reg(register, 16), Reg(register, 16)));

                private void SimdAllBitsSet(X86Register register)
                {
                    if (_vectorWidth == 64)
                        _owner.Emit(X86Instruction.Quaternary(X86InstrKind.Vpternlogd, SimdOperand(register), SimdOperand(register), SimdOperand(register), Imm(0xFF)));
                    else
                        SimdOp(X86InstrKind.Pcmpeqd, register, register);
                }

                private byte[] SimdSplat(long value, int elementSize)
                {
                    byte[] bytes = new byte[_vectorWidth];
                    for (int offset = 0; offset < bytes.Length; offset += elementSize)
                    {
                        for (int b = 0; b < elementSize; b++)
                            bytes[offset + b] = (byte)(value >> (8 * b));
                    }
                    return bytes;
                }

                private void EmitSimdGetUpper(X86Register destination, X86Register vector, int size)
                {
                    switch (size)
                    {
                        case 16:
                            SimdOp(X86InstrKind.Pshufd, destination, vector, 0xEE);
                            return;
                        case 32:
                            _owner.Emit(X86Instruction.Ternary(X86InstrKind.Vextracti128, Reg(destination, 16), Reg(vector, 32), Imm(1)));
                            return;
                        default:
                            _owner.Emit(X86Instruction.Ternary(X86InstrKind.Vextracti64x4, Reg(destination, 32), Reg(vector, 64), Imm(1)));
                            return;
                    }
                }

                private void EmitSimdInsertHalf(X86Register destination, X86Register vector, X86Register half, int index)
                {
                    X86InstrKind insert = _vectorWidth == 32 ? X86InstrKind.Vinserti128 : X86InstrKind.Vinserti64x4;
                    _owner.Emit(X86Instruction.Quaternary(insert, SimdOperand(destination), SimdOperand(vector), Reg(half, _vectorWidth / 2), Imm(index)));
                }

                private void EmitSimdExtractLane(X86Register destination, X86Register vector, int lane)
                {
                    if (lane == 0)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movaps, Reg(destination, 16), Reg(vector, 16)));
                        return;
                    }
                    X86InstrKind extract = _vectorWidth == 32 ? X86InstrKind.Vextracti128 : X86InstrKind.Vextracti32x4;
                    _owner.Emit(X86Instruction.Ternary(extract, Reg(destination, 16), SimdOperand(vector), Imm(lane)));
                }

                private void EmitSimdInsertLane(X86Register destination, X86Register vector, X86Register lane, int index)
                {
                    X86InstrKind insert = _vectorWidth == 32 ? X86InstrKind.Vinserti128 : X86InstrKind.Vinserti32x4;
                    _owner.Emit(X86Instruction.Quaternary(insert, SimdOperand(destination), SimdOperand(vector), Reg(lane, 16), Imm(index)));
                }

                private void EmitSimdRmw(X86InstrKind opcode, X86Register d, X86Register a, X86Register b, bool commutative)
                {
                    if (d == b && d != a)
                    {
                        if (!commutative)
                            throw new InvalidOperationException($"{opcode} result shares a register with its delay-freed operand.");
                        SimdOp(opcode, d, a);
                        return;
                    }
                    SimdMove(d, a);
                    SimdOp(opcode, d, b);
                }

                private static X86InstrKind SimdArithmeticOpcode(HWIntrinsicId id, RuntimePrimitiveKind kind)
                {
                    bool add = id == HWIntrinsicId.Add;
                    return kind switch
                    {
                        RuntimePrimitiveKind.Single => id switch { HWIntrinsicId.Add => X86InstrKind.Addps, HWIntrinsicId.Subtract => X86InstrKind.Subps, HWIntrinsicId.Multiply => X86InstrKind.Mulps, _ => X86InstrKind.Divps },
                        RuntimePrimitiveKind.Double => id switch { HWIntrinsicId.Add => X86InstrKind.Addpd, HWIntrinsicId.Subtract => X86InstrKind.Subpd, HWIntrinsicId.Multiply => X86InstrKind.Mulpd, _ => X86InstrKind.Divpd },
                        _ => HWIntrinsics.ElementSize(kind) switch
                        {
                            1 => add ? X86InstrKind.Paddb : X86InstrKind.Psubb,
                            2 => add ? X86InstrKind.Paddw : X86InstrKind.Psubw,
                            4 => add ? X86InstrKind.Paddd : X86InstrKind.Psubd,
                            _ => add ? X86InstrKind.Paddq : X86InstrKind.Psubq,
                        },
                    };
                }

                private static X86InstrKind SimdSaturatingOpcode(HWIntrinsicId id, RuntimePrimitiveKind kind)
                    => (id == HWIntrinsicId.AddSaturate, kind) switch
                    {
                        (true, RuntimePrimitiveKind.Int8) => X86InstrKind.Paddsb,
                        (true, RuntimePrimitiveKind.UInt8) => X86InstrKind.Paddusb,
                        (true, RuntimePrimitiveKind.Int16) => X86InstrKind.Paddsw,
                        (true, _) => X86InstrKind.Paddusw,
                        (false, RuntimePrimitiveKind.Int8) => X86InstrKind.Psubsb,
                        (false, RuntimePrimitiveKind.UInt8) => X86InstrKind.Psubusb,
                        (false, RuntimePrimitiveKind.Int16) => X86InstrKind.Psubsw,
                        (false, _) => X86InstrKind.Psubusw,
                    };

                private static X86InstrKind SimdCompareEqualOpcode(int elementSize)
                    => elementSize switch
                    {
                        1 => X86InstrKind.Pcmpeqb,
                        2 => X86InstrKind.Pcmpeqw,
                        4 => X86InstrKind.Pcmpeqd,
                        _ => X86InstrKind.Pcmpeqq,
                    };

                private static X86InstrKind SimdCompareGreaterOpcode(int elementSize)
                    => elementSize switch
                    {
                        1 => X86InstrKind.Pcmpgtb,
                        2 => X86InstrKind.Pcmpgtw,
                        4 => X86InstrKind.Pcmpgtd,
                        _ => X86InstrKind.Pcmpgtq,
                    };

                private static long SimdSignBit(int elementSize)
                    => long.MinValue >> (64 - elementSize * 8);

                private void EmitSimdCreate(GenTree node, HWIntrinsicId id, RuntimePrimitiveKind kind, int size, X86Register d)
                {
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (TryGetSimdConstant(node, id, elementSize, out byte[] constant))
                    {
                        EmitSimdConstant(d, constant, size);
                        return;
                    }

                    if (id == HWIntrinsicId.Broadcast)
                    {
                        X86Register value = SimdUse(node, 0);
                        if (size > 16)
                        {
                            EmitSimdBroadcastWide(kind, d, value);
                            return;
                        }
                        switch (kind)
                        {
                            case RuntimePrimitiveKind.Single:
                                SimdOp(X86InstrKind.Pshufd, d, value, 0x00);
                                return;
                            case RuntimePrimitiveKind.Double:
                                SimdOp(X86InstrKind.Pshufd, d, value, 0x44);
                                return;
                        }
                        _owner.Emit(X86Instruction.Binary(elementSize == 8 ? X86InstrKind.Movq : X86InstrKind.Movd, SimdOperand(d), Reg(value, elementSize == 8 ? 8 : 4)));
                        if (elementSize == 1)
                            SimdOp(X86InstrKind.Punpcklbw, d, d);
                        if (elementSize <= 2)
                            SimdOp(X86InstrKind.Pshuflw, d, d, 0x00);
                        if (elementSize == 4 || size == 16 && elementSize != 8)
                            SimdOp(X86InstrKind.Pshufd, d, d, 0x00);
                        else if (size == 16)
                            SimdOp(X86InstrKind.Pshufd, d, d, 0x44);
                        return;
                    }

                    int count = node.Operands.Length;
                    switch (kind)
                    {
                        case RuntimePrimitiveKind.Single:
                            {
                                X86Register e0 = SimdUse(node, 0), e1 = SimdUse(node, 1);
                                if (count == 2)
                                {
                                    X86Register pair = d != e1 ? d : SimdTemp(node, 0);
                                    SimdMove(pair, e0);
                                    SimdOp(X86InstrKind.Unpcklps, pair, e1);
                                    SimdMove(d, pair);
                                    return;
                                }
                                X86Register low = SimdTemp(node, 0), high = SimdTemp(node, 1);
                                SimdMove(low, e0);
                                SimdOp(X86InstrKind.Unpcklps, low, e1);
                                SimdMove(high, SimdUse(node, 2));
                                if (count == 4)
                                    SimdOp(X86InstrKind.Unpcklps, high, SimdUse(node, 3));
                                SimdOp(X86InstrKind.Movlhps, low, high);
                                SimdMove(d, low);
                                return;
                            }
                        case RuntimePrimitiveKind.Double:
                            {
                                X86Register e1 = SimdUse(node, 1);
                                X86Register pair = d != e1 ? d : SimdTemp(node, 0);
                                SimdMove(pair, SimdUse(node, 0));
                                SimdOp(X86InstrKind.Movlhps, pair, e1);
                                SimdMove(d, pair);
                                return;
                            }
                    }

                    int width = elementSize == 8 ? 8 : 4;
                    X86InstrKind move = elementSize == 8 ? X86InstrKind.Movq : X86InstrKind.Movd;
                    _owner.Emit(X86Instruction.Binary(move, SimdOperand(d), Reg(SimdUse(node, 0), width)));
                    if (elementSize == 2 || HasIsa(TargetArchitectureFeatures.X86Sse41))
                    {
                        X86InstrKind insert = elementSize switch
                        {
                            1 => X86InstrKind.Pinsrb,
                            2 => X86InstrKind.Pinsrw,
                            4 => X86InstrKind.Pinsrd,
                            _ => X86InstrKind.Pinsrq,
                        };
                        for (int i = 1; i < count; i++)
                            _owner.Emit(X86Instruction.Ternary(insert, SimdOperand(d), Reg(SimdUse(node, i), width), Imm(i)));
                        return;
                    }

                    X86Register scratch = SimdTemp(node, 0, d);
                    if (elementSize == 8)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movq, SimdOperand(scratch), Reg(SimdUse(node, 1), 8)));
                        SimdOp(X86InstrKind.Punpcklqdq, d, scratch);
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, SimdOperand(scratch), Reg(SimdUse(node, 1), 4)));
                    SimdOp(X86InstrKind.Punpckldq, d, scratch);
                    if (count == 2)
                        return;
                    X86Register upper = SimdTemp(node, 1, d);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, SimdOperand(scratch), Reg(SimdUse(node, 2), 4)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, SimdOperand(upper), Reg(SimdUse(node, 3), 4)));
                    SimdOp(X86InstrKind.Punpckldq, scratch, upper);
                    SimdOp(X86InstrKind.Punpcklqdq, d, scratch);
                }

                private void EmitSimdBroadcastWide(RuntimePrimitiveKind kind, X86Register d, X86Register value)
                {
                    switch (kind)
                    {
                        case RuntimePrimitiveKind.Single:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Vbroadcastss, SimdOperand(d), Reg(value, 16)));
                            return;
                        case RuntimePrimitiveKind.Double:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Vbroadcastsd, SimdOperand(d), Reg(value, 16)));
                            return;
                    }

                    int elementSize = HWIntrinsics.ElementSize(kind);
                    X86InstrKind broadcast = elementSize switch
                    {
                        1 => X86InstrKind.Vpbroadcastb,
                        2 => X86InstrKind.Vpbroadcastw,
                        4 => X86InstrKind.Vpbroadcastd,
                        _ => X86InstrKind.Vpbroadcastq,
                    };
                    X86Operand scalar = Reg(value, elementSize == 8 ? 8 : 4);
                    if (_vectorWidth == 64)
                    {
                        _owner.Emit(X86Instruction.Binary(broadcast, SimdOperand(d), scalar));
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(elementSize == 8 ? X86InstrKind.Movq : X86InstrKind.Movd, Reg(d, 16), scalar));
                    _owner.Emit(X86Instruction.Binary(broadcast, SimdOperand(d), Reg(d, 16)));
                }

                private bool TryGetSimdConstant(GenTree node, HWIntrinsicId id, int elementSize, out byte[] bytes)
                {
                    bytes = new byte[_vectorWidth];
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (!TryGetContainedSimdElement(node, i, out long value))
                            return false;
                        if (id == HWIntrinsicId.Broadcast)
                        {
                            bytes = SimdSplat(value, elementSize);
                            return true;
                        }
                        for (int b = 0; b < elementSize; b++)
                            bytes[i * elementSize + b] = (byte)(value >> (8 * b));
                    }
                    return node.Operands.Length != 0;
                }

                private static bool TryGetContainedSimdElement(GenTree node, int operandIndex, out long value)
                {
                    value = 0;
                    if (node.OperandFlags.IsDefaultOrEmpty || operandIndex >= node.OperandFlags.Length ||
                        (node.OperandFlags[operandIndex] & LirOperandFlags.Contained) == 0)
                    {
                        return false;
                    }
                    GenTree operand = node.Operands[operandIndex];
                    switch (operand.Kind)
                    {
                        case GenTreeKind.ConstI4:
                        case GenTreeKind.ConstR4Bits:
                            value = operand.Int32;
                            return true;
                        case GenTreeKind.ConstI8:
                        case GenTreeKind.ConstR8Bits:
                            value = operand.Int64;
                            return true;
                        default:
                            return false;
                    }
                }

                private void EmitSimdConstant(X86Register d, byte[] bytes, int size)
                {
                    bool zero = true, ones = true;
                    for (int i = 0; i < size; i++)
                    {
                        zero &= bytes[i] == 0;
                        ones &= bytes[i] == 0xFF;
                    }
                    if (zero)
                    {
                        SimdZero(d);
                        return;
                    }
                    if (ones)
                    {
                        SimdAllBitsSet(d);
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(
                        size > 8 ? X86InstrKind.Movups : X86InstrKind.Movsd,
                        SimdOperand(d),
                        _owner.SymbolMemory(_owner.AddVectorConstant(bytes), 0, size == 12 ? 16 : size)));
                }

                private void EmitSimdCreateScalar(GenTree node, HWIntrinsicId id, RuntimePrimitiveKind kind, X86Register d)
                {
                    X86Register value = SimdUse(node, 0);
                    switch (kind)
                    {
                        case RuntimePrimitiveKind.Single:
                            if (HasIsa(TargetArchitectureFeatures.X86Sse41))
                            {
                                SimdOp(X86InstrKind.Insertps, d, value, 0x0E);
                                return;
                            }
                            X86Register scalar = d != value ? d : SimdTemp(node, 0);
                            SimdOp(X86InstrKind.Xorps, scalar, scalar);
                            SimdOp(X86InstrKind.Movss, scalar, value);
                            SimdMove(d, scalar);
                            return;
                        case RuntimePrimitiveKind.Double:
                            SimdOp(X86InstrKind.Movq, d, value);
                            return;
                    }

                    int elementSize = HWIntrinsics.ElementSize(kind);
                    _owner.Emit(X86Instruction.Binary(
                        elementSize == 8 ? X86InstrKind.Movq : X86InstrKind.Movd,
                        SimdOperand(d),
                        Reg(value, elementSize == 8 ? 8 : 4)));
                    if (id == HWIntrinsicId.CreateScalar && elementSize < 4)
                    {
                        byte[] mask = new byte[16];
                        for (int i = 0; i < elementSize; i++)
                            mask[i] = 0xFF;
                        SimdOpConstant(X86InstrKind.Pand, d, mask);
                    }
                }

                private void EmitSimdGetElement(GenTree node, RuntimePrimitiveKind kind, int index)
                {
                    X86Register vector = SimdUse(node, 0);
                    X86Register d = RequireSimdResult(node);
                    int laneElements = 16 / HWIntrinsics.ElementSize(kind);
                    if (index >= laneElements)
                    {
                        X86Register lane = SimdTemp(node, 0);
                        EmitSimdExtractLane(lane, vector, index / laneElements);
                        vector = lane;
                        index %= laneElements;
                    }
                    _vectorWidth = 16;

                    switch (kind)
                    {
                        case RuntimePrimitiveKind.Single:
                            if (index == 0)
                                SimdMove(d, vector);
                            else
                                SimdOp(X86InstrKind.Pshufd, d, vector, index);
                            return;
                        case RuntimePrimitiveKind.Double:
                            if (index == 0)
                                SimdMove(d, vector);
                            else
                                SimdOp(X86InstrKind.Pshufd, d, vector, 0xEE);
                            return;
                    }

                    int elementSize = HWIntrinsics.ElementSize(kind);
                    bool signed = !HWIntrinsics.IsUnsigned(kind);
                    switch (elementSize)
                    {
                        case 1:
                            if (HasIsa(TargetArchitectureFeatures.X86Sse41))
                            {
                                _owner.Emit(X86Instruction.Ternary(X86InstrKind.Pextrb, Reg(d, 4), SimdOperand(vector), Imm(index)));
                            }
                            else
                            {
                                _owner.Emit(X86Instruction.Ternary(X86InstrKind.Pextrw, Reg(d, 4), SimdOperand(vector), Imm(index / 2)));
                                if ((index & 1) != 0)
                                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Shr, Reg(d, 4), Imm(8)));
                                else if (!signed)
                                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(d, 4), Reg(d, 1)));
                            }
                            if (signed)
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Movsx, Reg(d, 4), Reg(d, 1)));
                            return;
                        case 2:
                            _owner.Emit(X86Instruction.Ternary(X86InstrKind.Pextrw, Reg(d, 4), SimdOperand(vector), Imm(index)));
                            if (signed)
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Movsx, Reg(d, 4), Reg(d, 2)));
                            return;
                    }

                    int width = elementSize == 8 ? 8 : 4;
                    if (index != 0 && HasIsa(TargetArchitectureFeatures.X86Sse41))
                    {
                        _owner.Emit(X86Instruction.Ternary(width == 8 ? X86InstrKind.Pextrq : X86InstrKind.Pextrd, Reg(d, width), SimdOperand(vector), Imm(index)));
                        return;
                    }
                    X86Register source = vector;
                    if (index != 0)
                    {
                        source = SimdTemp(node, 0);
                        SimdOp(X86InstrKind.Pshufd, source, vector, width == 8 ? 0xEE : index);
                    }
                    _owner.Emit(X86Instruction.Binary(width == 8 ? X86InstrKind.Movq : X86InstrKind.Movd, Reg(d, width), SimdOperand(source)));
                }

                private void EmitSimdWithElement(GenTree node, RuntimePrimitiveKind kind, X86Register d, int index)
                {
                    X86Register vector = SimdUse(node, 0);
                    X86Register value = SimdUse(node, 2);
                    if (_vectorWidth > 16)
                    {
                        int laneElements = 16 / HWIntrinsics.ElementSize(kind);
                        int width = _vectorWidth;
                        X86Register lane = SimdTemp(node, 0);
                        EmitSimdExtractLane(lane, vector, index / laneElements);
                        _vectorWidth = 16;
                        EmitSimdInsertElement(node, kind, lane, value, index % laneElements);
                        _vectorWidth = width;
                        EmitSimdInsertLane(d, vector, lane, index / laneElements);
                        return;
                    }
                    SimdMove(d, vector);
                    EmitSimdInsertElement(node, kind, d, value, index);
                }

                private void EmitSimdInsertElement(GenTree node, RuntimePrimitiveKind kind, X86Register d, X86Register value, int index)
                {
                    switch (kind)
                    {
                        case RuntimePrimitiveKind.Single:
                            SimdOp(X86InstrKind.Insertps, d, value, index << 4);
                            return;
                        case RuntimePrimitiveKind.Double:
                            SimdOp(index == 0 ? X86InstrKind.Movsd : X86InstrKind.Movlhps, d, value);
                            return;
                    }

                    int elementSize = HWIntrinsics.ElementSize(kind);
                    int width = elementSize == 8 ? 8 : 4;
                    if (elementSize == 8 && !HasIsa(TargetArchitectureFeatures.X86Sse41))
                    {
                        X86Register scalar = SimdTemp(node, 0, d);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movq, SimdOperand(scalar), Reg(value, 8)));
                        SimdOp(index == 0 ? X86InstrKind.Movsd : X86InstrKind.Movlhps, d, scalar);
                        return;
                    }
                    X86InstrKind insert = elementSize switch
                    {
                        1 => X86InstrKind.Pinsrb,
                        2 => X86InstrKind.Pinsrw,
                        4 => X86InstrKind.Pinsrd,
                        _ => X86InstrKind.Pinsrq,
                    };
                    _owner.Emit(X86Instruction.Ternary(insert, SimdOperand(d), Reg(value, width), Imm(index)));
                }

                private void EmitSimdMultiply(GenTree node, RuntimePrimitiveKind kind, X86Register d)
                {
                    X86Register a = SimdUse(node, 0);
                    X86Register b = SimdUse(node, 1);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (HWIntrinsics.IsFloating(kind))
                    {
                        EmitSimdRmw(SimdArithmeticOpcode(HWIntrinsicId.Multiply, kind), d, a, b, commutative: true);
                        return;
                    }
                    if (elementSize == 2)
                    {
                        EmitSimdRmw(X86InstrKind.Pmullw, d, a, b, commutative: true);
                        return;
                    }
                    if (HasIsa(TargetArchitectureFeatures.X86Sse41))
                    {
                        EmitSimdRmw(X86InstrKind.Pmulld, d, a, b, commutative: true);
                        return;
                    }

                    // pmuludq multiplies the even lanes; the odd lanes are shifted down, multiplied and interleaved back
                    X86Register odd = SimdTemp(node, 0);
                    X86Register oddRight = SimdTemp(node, 1);
                    X86Register even = d != b && d != odd && d != oddRight ? d : SimdTemp(node, 2);
                    SimdOp(X86InstrKind.Pshufd, odd, a, 0xF5);
                    SimdOp(X86InstrKind.Pshufd, oddRight, b, 0xF5);
                    SimdOp(X86InstrKind.Pmuludq, odd, oddRight);
                    SimdMove(even, a);
                    SimdOp(X86InstrKind.Pmuludq, even, b);
                    SimdOp(X86InstrKind.Pshufd, even, even, 0x08);
                    SimdOp(X86InstrKind.Pshufd, odd, odd, 0x08);
                    SimdOp(X86InstrKind.Punpckldq, even, odd);
                    SimdMove(d, even);
                }

                private void EmitSimdConditionalSelect(GenTree node, X86Register d)
                {
                    X86Register mask = SimdUse(node, 0);
                    X86Register whenTrue = SimdUse(node, 1);
                    X86Register whenFalse = SimdUse(node, 2);
                    X86Register work = d != mask && d != whenFalse ? d : SimdTemp(node, 0);
                    SimdMove(work, whenTrue);
                    SimdOp(X86InstrKind.Pxor, work, whenFalse);
                    SimdOp(X86InstrKind.Pand, work, mask);
                    SimdOp(X86InstrKind.Pxor, work, whenFalse);
                    SimdMove(d, work);
                }

                private void EmitSimdAbs(GenTree node, RuntimePrimitiveKind kind, X86Register d)
                {
                    X86Register value = SimdUse(node, 0);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (HWIntrinsics.IsFloating(kind))
                    {
                        SimdMove(d, value);
                        SimdOpConstant(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Andps : X86InstrKind.Andpd, d, SimdSplat(~SimdSignBit(elementSize), elementSize));
                        return;
                    }
                    if (elementSize <= 4 && HasIsa(TargetArchitectureFeatures.X86Ssse3))
                    {
                        SimdOp(elementSize switch { 1 => X86InstrKind.Pabsb, 2 => X86InstrKind.Pabsw, _ => X86InstrKind.Pabsd }, d, value);
                        return;
                    }
                    if (_vectorWidth == 64)
                    {
                        SimdOp(X86InstrKind.Vpabsq, d, value);
                        return;
                    }
                    X86Register negated = SimdTemp(node, 0);
                    if (elementSize <= 2)
                    {
                        SimdOp(X86InstrKind.Pxor, negated, negated);
                        SimdOp(elementSize == 1 ? X86InstrKind.Psubb : X86InstrKind.Psubw, negated, value);
                        SimdOp(elementSize == 1 ? X86InstrKind.Pminub : X86InstrKind.Pmaxsw, negated, value);
                        SimdMove(d, negated);
                        return;
                    }

                    // (x ^ sign) - sign, with the sign of each 64-bit lane taken from its upper half
                    X86Register sign = negated;
                    X86Register work = d != sign ? d : SimdTemp(node, 1);
                    SimdMove(sign, value);
                    SimdShiftImmediate(X86InstrKind.Psrad, sign, 31);
                    if (elementSize == 8)
                        SimdOp(X86InstrKind.Pshufd, sign, sign, 0xF5);
                    SimdMove(work, value);
                    SimdOp(X86InstrKind.Pxor, work, sign);
                    SimdOp(elementSize == 4 ? X86InstrKind.Psubd : X86InstrKind.Psubq, work, sign);
                    SimdMove(d, work);
                }

                private void EmitSimdMinMax(GenTree node, bool min, RuntimePrimitiveKind kind, X86Register d)
                {
                    X86Register a = SimdUse(node, 0);
                    X86Register b = SimdUse(node, 1);
                    X86InstrKind direct = SimdDirectMinMaxOpcode(min, kind);
                    if (direct != X86InstrKind.Invalid)
                    {
                        EmitSimdRmw(direct, d, a, b, commutative: true);
                        return;
                    }

                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (kind == RuntimePrimitiveKind.UInt16)
                    {
                        // a - (a -sat b) is the smaller and (a -sat b) + b the larger of two unsigned words
                        X86Register saturated = SimdTemp(node, 0);
                        SimdMove(saturated, a);
                        SimdOp(X86InstrKind.Psubusw, saturated, b);
                        if (min)
                        {
                            X86Register work = d != saturated ? d : SimdTemp(node, 1);
                            SimdMove(work, a);
                            SimdOp(X86InstrKind.Psubw, work, saturated);
                            SimdMove(d, work);
                        }
                        else
                        {
                            SimdOp(X86InstrKind.Paddw, saturated, b);
                            SimdMove(d, saturated);
                        }
                        return;
                    }

                    X86Register greater = SimdTemp(node, 0);
                    X86Register select = SimdTemp(node, 1);
                    EmitSimdGreaterMask(node, greater, a, b, kind, biasTemp: 2);
                    SimdMove(select, min ? b : a);
                    SimdOp(X86InstrKind.Pand, select, greater);
                    SimdOp(X86InstrKind.Pandn, greater, min ? a : b);
                    SimdOp(X86InstrKind.Por, select, greater);
                    SimdMove(d, select);
                }

                private X86InstrKind SimdDirectMinMaxOpcode(bool min, RuntimePrimitiveKind kind)
                {
                    bool sse41 = HasIsa(TargetArchitectureFeatures.X86Sse41);
                    bool avx512 = _vectorWidth == 64;
                    return kind switch
                    {
                        RuntimePrimitiveKind.UInt8 => min ? X86InstrKind.Pminub : X86InstrKind.Pmaxub,
                        RuntimePrimitiveKind.Int16 => min ? X86InstrKind.Pminsw : X86InstrKind.Pmaxsw,
                        RuntimePrimitiveKind.Int8 when sse41 => min ? X86InstrKind.Pminsb : X86InstrKind.Pmaxsb,
                        RuntimePrimitiveKind.UInt16 when sse41 => min ? X86InstrKind.Pminuw : X86InstrKind.Pmaxuw,
                        RuntimePrimitiveKind.Int32 when sse41 => min ? X86InstrKind.Pminsd : X86InstrKind.Pmaxsd,
                        RuntimePrimitiveKind.UInt32 when sse41 => min ? X86InstrKind.Pminud : X86InstrKind.Pmaxud,
                        RuntimePrimitiveKind.Int64 when avx512 => min ? X86InstrKind.Vpminsq : X86InstrKind.Vpmaxsq,
                        RuntimePrimitiveKind.UInt64 when avx512 => min ? X86InstrKind.Vpminuq : X86InstrKind.Vpmaxuq,
                        _ => X86InstrKind.Invalid,
                    };
                }

                // Writes a > b into mask, which must differ from both operands; unsigned lanes compare with their sign bits flipped
                private void EmitSimdGreaterMask(GenTree node, X86Register mask, X86Register a, X86Register b, RuntimePrimitiveKind kind, int biasTemp)
                {
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    X86InstrKind compare = SimdCompareGreaterOpcode(elementSize);
                    SimdMove(mask, a);
                    if (!HWIntrinsics.IsUnsigned(kind))
                    {
                        SimdOp(compare, mask, b);
                        return;
                    }
                    byte[] bias = SimdSplat(SimdSignBit(elementSize), elementSize);
                    X86Register right = SimdTemp(node, biasTemp);
                    SimdOpConstant(X86InstrKind.Pxor, mask, bias);
                    SimdMove(right, b);
                    SimdOpConstant(X86InstrKind.Pxor, right, bias);
                    SimdOp(compare, mask, right);
                }

                private void EmitSimdCompareEqual(GenTree node, RuntimePrimitiveKind kind, X86Register d, X86Register a, X86Register b)
                {
                    switch (kind)
                    {
                        case RuntimePrimitiveKind.Single:
                            EmitSimdCompareFloat(X86InstrKind.Cmpps, 0, d, a, b);
                            return;
                        case RuntimePrimitiveKind.Double:
                            EmitSimdCompareFloat(X86InstrKind.Cmppd, 0, d, a, b);
                            return;
                    }
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (elementSize < 8 || HasIsa(TargetArchitectureFeatures.X86Sse41))
                    {
                        EmitSimdRmw(SimdCompareEqualOpcode(elementSize), d, a, b, commutative: true);
                        return;
                    }

                    X86Register halves = SimdTemp(node, 0);
                    X86Register swapped = SimdTemp(node, 1);
                    SimdMove(halves, a);
                    SimdOp(X86InstrKind.Pcmpeqd, halves, b);
                    SimdOp(X86InstrKind.Pshufd, swapped, halves, 0xB1);
                    SimdOp(X86InstrKind.Pand, halves, swapped);
                    SimdMove(d, halves);
                }

                private void EmitSimdCompareFloat(X86InstrKind opcode, int predicate, X86Register d, X86Register a, X86Register b)
                {
                    if (d == b && d != a)
                    {
                        if (predicate != 0)
                            throw new InvalidOperationException("Ordered vector comparison result shares a register with its delay-freed operand.");
                        SimdOp(opcode, d, a, predicate);
                        return;
                    }
                    SimdMove(d, a);
                    SimdOp(opcode, d, b, predicate);
                }

                private void EmitSimdCompareOrdered(GenTree node, HWIntrinsicId id, RuntimePrimitiveKind kind, X86Register d)
                {
                    X86Register a = SimdUse(node, 0);
                    X86Register b = SimdUse(node, 1);
                    bool swap = id is HWIntrinsicId.CompareGreaterThan or HWIntrinsicId.CompareGreaterThanOrEqual;
                    bool orEqual = id is HWIntrinsicId.CompareGreaterThanOrEqual or HWIntrinsicId.CompareLessThanOrEqual;
                    if (HWIntrinsics.IsFloating(kind))
                    {
                        // cmpps only orders as less-than and less-or-equal, so the greater forms swap their operands
                        EmitSimdCompareFloat(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Cmpps : X86InstrKind.Cmppd, orEqual ? 2 : 1,
                            d, swap ? b : a, swap ? a : b);
                        return;
                    }

                    // a < b is b > a; the or-equal forms negate the opposite strict comparison
                    X86Register left = swap != orEqual ? a : b;
                    X86Register right = swap != orEqual ? b : a;
                    if (HWIntrinsics.IsUnsigned(kind))
                    {
                        X86Register mask = SimdTemp(node, 0);
                        EmitSimdGreaterMask(node, mask, left, right, kind, biasTemp: 1);
                        if (orEqual)
                            SimdOpConstant(X86InstrKind.Pxor, mask, SimdSplat(-1, 8));
                        SimdMove(d, mask);
                        return;
                    }
                    SimdMove(d, left);
                    SimdOp(SimdCompareGreaterOpcode(HWIntrinsics.ElementSize(kind)), d, right);
                    if (orEqual)
                        SimdOpConstant(X86InstrKind.Pxor, d, SimdSplat(-1, 8));
                }

                private void EmitSimdMaskCompare(GenTree node, HWIntrinsicId id, RuntimePrimitiveKind kind, X86Register d)
                {
                    X86Register a = SimdUse(node, 0);
                    X86Register b = SimdUse(node, 1);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (HWIntrinsics.IsFloating(kind))
                    {
                        bool swap = id is HWIntrinsicId.CompareGreaterThan or HWIntrinsicId.CompareGreaterThanOrEqual;
                        int predicate = id == HWIntrinsicId.CompareEqual ? 0
                            : id is HWIntrinsicId.CompareGreaterThanOrEqual or HWIntrinsicId.CompareLessThanOrEqual ? 2 : 1;
                        _owner.Emit(X86Instruction.Quaternary(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Cmpps : X86InstrKind.Cmppd,
                            MaskScratch, SimdOperand(swap ? b : a), SimdOperand(swap ? a : b), Imm(predicate)));
                    }
                    else
                    {
                        bool unsigned = HWIntrinsics.IsUnsigned(kind);
                        X86InstrKind compare = elementSize switch
                        {
                            1 => unsigned ? X86InstrKind.Vpcmpub : X86InstrKind.Vpcmpb,
                            2 => unsigned ? X86InstrKind.Vpcmpuw : X86InstrKind.Vpcmpw,
                            4 => unsigned ? X86InstrKind.Vpcmpud : X86InstrKind.Vpcmpd,
                            _ => unsigned ? X86InstrKind.Vpcmpuq : X86InstrKind.Vpcmpq,
                        };
                        int predicate = id switch
                        {
                            HWIntrinsicId.CompareEqual => 0,
                            HWIntrinsicId.CompareLessThan => 1,
                            HWIntrinsicId.CompareLessThanOrEqual => 2,
                            HWIntrinsicId.CompareGreaterThanOrEqual => 5,
                            _ => 6,
                        };
                        _owner.Emit(X86Instruction.Quaternary(compare, MaskScratch, SimdOperand(a), SimdOperand(b), Imm(predicate)));
                    }
                    X86InstrKind expand = elementSize switch
                    {
                        1 => X86InstrKind.Vpmovm2b,
                        2 => X86InstrKind.Vpmovm2w,
                        4 => X86InstrKind.Vpmovm2d,
                        _ => X86InstrKind.Vpmovm2q,
                    };
                    _owner.Emit(X86Instruction.Binary(expand, SimdOperand(d), MaskScratch));
                }

                private void EmitSimdShift(GenTree node, HWIntrinsicId id, RuntimePrimitiveKind kind, X86Register d)
                {
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    X86InstrKind opcode = (id, elementSize) switch
                    {
                        (HWIntrinsicId.ShiftLeft, 2) => X86InstrKind.Psllw,
                        (HWIntrinsicId.ShiftLeft, 4) => X86InstrKind.Pslld,
                        (HWIntrinsicId.ShiftLeft, _) => X86InstrKind.Psllq,
                        (HWIntrinsicId.ShiftRightLogical, 2) => X86InstrKind.Psrlw,
                        (HWIntrinsicId.ShiftRightLogical, 4) => X86InstrKind.Psrld,
                        (HWIntrinsicId.ShiftRightLogical, _) => X86InstrKind.Psrlq,
                        (_, 2) => X86InstrKind.Psraw,
                        _ => X86InstrKind.Psrad,
                    };
                    X86Register vector = SimdUse(node, 0);
                    if (TryGetContainedIntegerImmediate(node, 1, out long immediate))
                    {
                        SimdMove(d, vector);
                        SimdShiftImmediate(opcode, d, (int)immediate & (elementSize * 8 - 1));
                        return;
                    }

                    // The count register shifts by its full 64-bit value, while the vector API wraps it to the lane width
                    X86Register count = SimdTemp(node, 0, d);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, Reg(count, 16), Reg(SimdUse(node, 1), 4)));
                    byte[] mask = new byte[16];
                    mask[0] = (byte)(elementSize * 8 - 1);
                    SimdOpConstant(X86InstrKind.Pand, count, mask);
                    SimdMove(d, vector);
                    _owner.Emit(X86Instruction.Binary(opcode, SimdOperand(d), Reg(count, 16)));
                }

                private void EmitSimdEqualityTest(GenTree node, HWIntrinsicId id, RuntimePrimitiveKind kind, int size)
                {
                    X86Register d = RequireSimdResult(node);
                    X86Register a = SimdUse(node, 0);
                    X86Register b = SimdUse(node, 1);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    bool floating = HWIntrinsics.IsFloating(kind);
                    int compareSize = id == HWIntrinsicId.EqualsAll && !floating ? 1 : elementSize;
                    if (_vectorWidth == 64)
                    {
                        // kortest sets the carry when every mask bit is set and clears the zero flag when any is
                        if (floating)
                        {
                            _owner.Emit(X86Instruction.Quaternary(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Cmpps : X86InstrKind.Cmppd,
                                MaskScratch, SimdOperand(a), SimdOperand(b), Imm(0)));
                        }
                        else
                        {
                            _owner.Emit(X86Instruction.AvxTernary(SimdCompareEqualOpcode(compareSize), MaskScratch, SimdOperand(a), SimdOperand(b)));
                        }
                        X86InstrKind test = compareSize switch
                        {
                            1 => X86InstrKind.Kortestq,
                            2 => X86InstrKind.Kortestd,
                            4 => X86InstrKind.Kortestw,
                            _ => X86InstrKind.Kortestb,
                        };
                        _owner.Emit(X86Instruction.Binary(test, MaskScratch, MaskScratch));
                        EmitBooleanResult(id == HWIntrinsicId.EqualsAll ? X86Condition.B : X86Condition.Ne, d);
                        return;
                    }

                    X86Register equal = SimdTemp(node, 0);
                    int laneMask;
                    if (floating)
                    {
                        SimdMove(equal, a);
                        SimdOp(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Cmpps : X86InstrKind.Cmppd, equal, b, 0);
                        _owner.Emit(X86Instruction.Binary(kind == RuntimePrimitiveKind.Single ? X86InstrKind.Movmskps : X86InstrKind.Movmskpd, Reg(d, 4), SimdOperand(equal)));
                        laneMask = (1 << (size / elementSize)) - 1;
                    }
                    else
                    {
                        if (compareSize == 8 && !HasIsa(TargetArchitectureFeatures.X86Sse41))
                        {
                            X86Register swapped = SimdTemp(node, 1);
                            SimdMove(equal, a);
                            SimdOp(X86InstrKind.Pcmpeqd, equal, b);
                            SimdOp(X86InstrKind.Pshufd, swapped, equal, 0xB1);
                            SimdOp(X86InstrKind.Pand, equal, swapped);
                        }
                        else
                        {
                            SimdMove(equal, a);
                            SimdOp(SimdCompareEqualOpcode(compareSize), equal, b);
                        }
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Pmovmskb, Reg(d, 4), SimdOperand(equal)));
                        laneMask = size switch { 8 => 0xFF, 12 => 0xFFF, 16 => 0xFFFF, _ => -1 };
                    }

                    if (id == HWIntrinsicId.EqualsAll)
                    {
                        if (laneMask is not (0xFFFF or -1))
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.And, Reg(d, 4), Imm(laneMask)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(d, 4), Imm(laneMask)));
                        EmitBooleanResult(X86Condition.E, d);
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(d, 4), Imm(laneMask)));
                    EmitBooleanResult(X86Condition.Ne, d);
                }

                private void EmitSimdExtractMostSignificantBits(GenTree node, RuntimePrimitiveKind kind, int size)
                {
                    X86Register d = RequireSimdResult(node);
                    X86Register vector = SimdUse(node, 0);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    if (_vectorWidth == 64)
                    {
                        X86InstrKind toMask = elementSize switch
                        {
                            1 => X86InstrKind.Vpmovb2m,
                            2 => X86InstrKind.Vpmovw2m,
                            4 => X86InstrKind.Vpmovd2m,
                            _ => X86InstrKind.Vpmovq2m,
                        };
                        X86InstrKind move = elementSize switch
                        {
                            1 => X86InstrKind.Kmovq,
                            2 => X86InstrKind.Kmovd,
                            4 => X86InstrKind.Kmovw,
                            _ => X86InstrKind.Kmovb,
                        };
                        _owner.Emit(X86Instruction.Binary(toMask, MaskScratch, SimdOperand(vector)));
                        _owner.Emit(X86Instruction.Binary(move, Reg(d, elementSize == 1 ? 8 : 4), MaskScratch));
                        return;
                    }
                    switch (elementSize)
                    {
                        case 1:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Pmovmskb, Reg(d, 4), SimdOperand(vector)));
                            break;
                        case 2:
                            {
                                // packsswb packs within each 128-bit lane, so a 256-bit vector gathers its packed halves into the low lane
                                X86Register packed = SimdTemp(node, 0);
                                SimdMove(packed, vector);
                                SimdOp(X86InstrKind.Packsswb, packed, packed);
                                if (_vectorWidth == 32)
                                    SimdOp(X86InstrKind.Vpermq, packed, packed, 0xD8);
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Pmovmskb, Reg(d, 4), Reg(packed, 16)));
                                break;
                            }
                        case 4:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Movmskps, Reg(d, 4), SimdOperand(vector)));
                            break;
                        default:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Movmskpd, Reg(d, 4), SimdOperand(vector)));
                            break;
                    }
                    int lanes = size / elementSize;
                    if (elementSize == 2 && size <= 16 || size < 16)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.And, Reg(d, 4), Imm((1 << lanes) - 1)));
                }

                private void EmitSimdSum(GenTree node, RuntimePrimitiveKind kind, int size)
                {
                    X86Register d = RequireSimdResult(node);
                    X86Register vector = SimdUse(node, 0);
                    int elementSize = HWIntrinsics.ElementSize(kind);
                    bool floating = HWIntrinsics.IsFloating(kind);
                    X86InstrKind add = SimdArithmeticOpcode(HWIntrinsicId.Add, kind);

                    if (size == 12)
                    {
                        X86Register first = SimdTemp(node, 0), second = SimdTemp(node, 1);
                        SimdMove(first, vector);
                        SimdOp(X86InstrKind.Pshufd, second, vector, 0x55);
                        SimdOp(X86InstrKind.Addss, first, second);
                        SimdOp(X86InstrKind.Pshufd, second, vector, 0xAA);
                        SimdOp(X86InstrKind.Addss, first, second);
                        SimdMove(d, first);
                        return;
                    }
                    int temp = 0;
                    while (_vectorWidth > 16)
                    {
                        X86Register half = SimdTemp(node, temp);
                        temp ^= 1;
                        EmitSimdGetUpper(half, vector, _vectorWidth);
                        _vectorWidth /= 2;
                        SimdOp(add, half, vector);
                        vector = half;
                    }

                    if (elementSize == 1)
                    {
                        X86Register zero = SimdTemp(node, temp);
                        X86Register sums = size > 16 ? vector : SimdTemp(node, temp ^ 1);
                        SimdOp(X86InstrKind.Pxor, zero, zero);
                        SimdMove(sums, vector);
                        SimdOp(X86InstrKind.Psadbw, sums, zero);
                        if (size >= 16)
                        {
                            SimdOp(X86InstrKind.Pshufd, zero, sums, 0xEE);
                            SimdOp(X86InstrKind.Paddq, sums, zero);
                        }
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, Reg(d, 4), SimdOperand(sums)));
                        _owner.Emit(X86Instruction.Binary(kind == RuntimePrimitiveKind.Int8 ? X86InstrKind.Movsx : X86InstrKind.Movzx, Reg(d, 4), Reg(d, 1)));
                        return;
                    }

                    X86Register acc = vector;
                    for (int lanes = Math.Min(size, 16) / elementSize; lanes > 1; lanes /= 2)
                    {
                        X86Register next = SimdTemp(node, temp);
                        temp ^= 1;
                        int halfBytes = lanes / 2 * elementSize;
                        if (halfBytes == 8)
                            SimdOp(X86InstrKind.Pshufd, next, acc, 0x4E);
                        else if (halfBytes == 4)
                            SimdOp(X86InstrKind.Pshufd, next, acc, 0xB1);
                        else
                            SimdOp(X86InstrKind.Pshuflw, next, acc, 0xB1);
                        SimdOp(add, next, acc);
                        acc = next;
                    }

                    if (floating)
                    {
                        SimdMove(d, acc);
                        return;
                    }
                    switch (elementSize)
                    {
                        case 2:
                            _owner.Emit(X86Instruction.Ternary(X86InstrKind.Pextrw, Reg(d, 4), SimdOperand(acc), Imm(0)));
                            if (kind == RuntimePrimitiveKind.Int16)
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Movsx, Reg(d, 4), Reg(d, 2)));
                            return;
                        case 4:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, Reg(d, 4), SimdOperand(acc)));
                            return;
                        default:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Movq, Reg(d, 8), SimdOperand(acc)));
                            return;
                    }
                }

                private void EmitX86Intrinsic(GenTree node)
                {
                    X86IntrinsicInfo info = X86Intrinsics.Info(node);
                    RuntimePrimitiveKind kind = HWIntrinsics.BaseKind(node);
                    X86InstrKind instruction = info.Instruction(kind);
                    switch (info.Form)
                    {
                        case X86IntrinsicForm.Simple:
                            EmitX86Simple(node, info, instruction);
                            return;
                        case X86IntrinsicForm.Scalar:
                            {
                                X86Operand d = IntrinsicResult(node);
                                IntrinsicMove(d, IntrinsicOperand(node, 0, instruction));
                                X86Operand source = node.Operands.Length == 1 ? d : IntrinsicOperand(node, 1, instruction);
                                EmitIntrinsicInstruction(instruction, d, source, info.Has(X86IntrinsicFlags.TableImmediate) ? info.Immediate : null);
                                return;
                            }
                        case X86IntrinsicForm.Shift:
                            EmitX86Shift(node, instruction);
                            return;
                        case X86IntrinsicForm.Compare:
                            EmitX86Compare(node, info, instruction);
                            return;
                        case X86IntrinsicForm.CompareFlags:
                            EmitX86CompareFlags(node, info, instruction);
                            return;
                        case X86IntrinsicForm.Store:
                        case X86IntrinsicForm.MaskMove:
                            _owner.Emit(X86Instruction.Binary(instruction, IntrinsicOperand(node, 0, instruction), IntrinsicOperand(node, 1, instruction)));
                            return;
                        case X86IntrinsicForm.BlendVariable:
                            {
                                X86Operand d = IntrinsicResult(node);
                                EmitIntrinsicRmw(node, instruction, d, IntrinsicOperand(node, 0, instruction), IntrinsicOperand(node, 1, instruction), commutative: false);
                                return;
                            }
                        case X86IntrinsicForm.Crc32:
                            {
                                X86Operand d = IntrinsicResult(node);
                                IntrinsicMove(d, IntrinsicOperand(node, 0, instruction));
                                _owner.Emit(X86Instruction.Binary(instruction, d, Reg(SimdUse(node, 1), HWIntrinsics.ElementSize(kind))));
                                return;
                            }
                        case X86IntrinsicForm.DivRem:
                            EmitX86DivRem(node, kind);
                            return;
                        case X86IntrinsicForm.Barrier:
                            _owner.Emit(new X86Instruction(instruction));
                            return;
                        case X86IntrinsicForm.Prefetch:
                            _owner.Emit(X86Instruction.Unary(instruction, IntrinsicOperand(node, 0, instruction)));
                            return;
                        case X86IntrinsicForm.Vex:
                        case X86IntrinsicForm.VexStore:
                        case X86IntrinsicForm.Tied:
                            EmitX86Vex(node, info, instruction);
                            return;
                        case X86IntrinsicForm.MaskCompare:
                            EmitX86Vex(node, info, instruction);
                            _owner.Emit(X86Instruction.Binary(MaskToVectorOpcode(kind), Reg(RequireSimdResult(node), Math.Max(16, SimdTypes.SizeOf(node.StackKind))),
                                MaskScratch));
                            return;
                        case X86IntrinsicForm.MaskedBlend:
                        case X86IntrinsicForm.MaskedMerge:
                        case X86IntrinsicForm.MaskedLoad:
                        case X86IntrinsicForm.MaskedStore:
                            EmitX86Masked(node, info, instruction);
                            return;
                        case X86IntrinsicForm.MaskToBits:
                            {
                                int width = Math.Max(16, SimdTypes.SizeOf(node.Operands[0].StackKind));
                                int count = width / HWIntrinsics.ElementSize(kind);
                                _owner.Emit(X86Instruction.Binary(instruction, MaskScratch, Reg(SimdUse(node, 0), width)));
                                _owner.Emit(X86Instruction.Binary(count <= 16 ? X86InstrKind.Kmovw : count == 32 ? X86InstrKind.Kmovd : X86InstrKind.Kmovq,
                                    Reg(RequireSimdResult(node), count == 64 ? 8 : 4), MaskScratch));
                                return;
                            }
                        case X86IntrinsicForm.Gather:
                        case X86IntrinsicForm.GatherMask:
                            EmitX86Gather(node, info, instruction);
                            return;
                        case X86IntrinsicForm.BitFieldExtract:
                            EmitX86BitFieldExtract(node, instruction);
                            return;
                        case X86IntrinsicForm.MultiplyNoFlags:
                            EmitX86MultiplyNoFlags(node, instruction);
                            return;
                    }

                    throw Unsupported(node, $"x86 intrinsic {X86Intrinsics.Format(node)} is not implemented");
                }

                private void EmitX86Vex(GenTree node, X86IntrinsicInfo info, X86InstrKind instruction)
                {
                    int immediateIndex = X86Intrinsics.ImmediateOperandIndex(node);
                    int roundingIndex = X86Intrinsics.RoundingOperandIndex(node);
                    int values = node.Operands.Length - (immediateIndex >= 0 ? 1 : 0) - (roundingIndex >= 0 ? 1 : 0);
                    bool tied = info.Form == X86IntrinsicForm.Tied;
                    var operands = new List<X86Operand>(5);
                    if (info.Form == X86IntrinsicForm.MaskCompare)
                        operands.Add(MaskScratch);
                    else if (info.Form != X86IntrinsicForm.VexStore)
                        operands.Add(IntrinsicResult(node));
                    int first = operands.Count;
                    for (int i = tied ? 1 : 0; i < values; i++)
                        operands.Add(IntrinsicOperand(node, i, instruction));
                    if (info.Has(X86IntrinsicFlags.SwapOperands))
                        (operands[first], operands[first + 1]) = (operands[first + 1], operands[first]);
                    if (tied)
                        IntrinsicMove(operands[0], IntrinsicOperand(node, 0, instruction));
                    if (info.Has(X86IntrinsicFlags.TableImmediate))
                        operands.Add(Imm(IsIntegerMaskCompare(instruction) ? IntegerPredicate(info.Immediate) : info.Immediate));

                    int length = Math.Max(16, HWIntrinsics.SimdSize(node));
                    X86Instruction Build(int? immediate)
                    {
                        if (immediate is not int value)
                            return FitIntrinsic(instruction, operands, length);
                        operands.Add(Imm(value));
                        X86Instruction built = FitIntrinsic(instruction, operands, length);
                        operands.RemoveAt(operands.Count - 1);
                        return built;
                    }

                    if (roundingIndex >= 0)
                    {
                        // As in .NET, the low two bits of a FloatRoundingMode pick the rounding in X86RoundingMode order
                        X86Instruction plain = Build(null);
                        if (TryGetContainedIntegerImmediate(node, roundingIndex, out long mode))
                            _owner.Emit(plain.WithRounding((X86RoundingMode)((mode & 3) + 1)));
                        else
                            EmitImmediateJumpTable(node, roundingIndex, value => plain.WithRounding((X86RoundingMode)(value + 1)), 4);
                        return;
                    }
                    if (immediateIndex < 0)
                    {
                        _owner.Emit(Build(null));
                        return;
                    }
                    if (TryGetContainedIntegerImmediate(node, immediateIndex, out long constant))
                    {
                        if ((ulong)constant > info.ImmediateBound)
                            EmitManagedExceptionThrow(node, "ArgumentOutOfRangeException");
                        else
                            _owner.Emit(Build((int)constant & 0xFF));
                        return;
                    }
                    if (info.ImmediateBound != 0xFF)
                        EmitImmediateRangeCheck(node, immediateIndex, info.ImmediateBound);
                    EmitImmediateJumpTable(node, immediateIndex, value => Build(value));
                }

                private static bool IsIntegerMaskCompare(X86InstrKind instruction)
                    => instruction is X86InstrKind.Vpcmpb or X86InstrKind.Vpcmpub or X86InstrKind.Vpcmpw or X86InstrKind.Vpcmpuw or
                        X86InstrKind.Vpcmpd or X86InstrKind.Vpcmpud or X86InstrKind.Vpcmpq or X86InstrKind.Vpcmpuq;

                private static int IntegerPredicate(int predicate)
                    => predicate switch { 0x09 => 1, 0x0A => 2, 0x0D => 5, 0x0E => 6, _ => predicate };

                private static X86InstrKind MaskToVectorOpcode(RuntimePrimitiveKind kind)
                    => HWIntrinsics.ElementSize(kind) switch
                    {
                        1 => X86InstrKind.Vpmovm2b,
                        2 => X86InstrKind.Vpmovm2w,
                        4 => X86InstrKind.Vpmovm2d,
                        _ => X86InstrKind.Vpmovm2q,
                    };

                private static X86InstrKind VectorToMaskOpcode(RuntimePrimitiveKind kind)
                    => HWIntrinsics.ElementSize(kind) switch
                    {
                        1 => X86InstrKind.Vpmovb2m,
                        2 => X86InstrKind.Vpmovw2m,
                        4 => X86InstrKind.Vpmovd2m,
                        _ => X86InstrKind.Vpmovq2m,
                    };

                private void EmitX86Masked(GenTree node, X86IntrinsicInfo info, X86InstrKind instruction)
                {
                    int length = Math.Max(16, HWIntrinsics.SimdSize(node));
                    int maskIndex = info.Form == X86IntrinsicForm.MaskedBlend ? 2 : 1;
                    _owner.Emit(X86Instruction.Binary(VectorToMaskOpcode(HWIntrinsics.BaseKind(node)), MaskScratch,
                        Reg(SimdUse(node, maskIndex), Math.Max(16, SimdTypes.SizeOf(node.Operands[maskIndex].StackKind)))));
                    X86Operand[] operands;
                    switch (info.Form)
                    {
                        case X86IntrinsicForm.MaskedBlend:
                            operands = new[] { IntrinsicResult(node), IntrinsicOperand(node, 0, instruction), IntrinsicOperand(node, 1, instruction) };
                            break;
                        case X86IntrinsicForm.MaskedMerge:
                            operands = new[] { IntrinsicResult(node), IntrinsicOperand(node, 2, instruction) };
                            IntrinsicMove(operands[0], IntrinsicOperand(node, 0, instruction));
                            break;
                        case X86IntrinsicForm.MaskedLoad:
                            operands = new[] { IntrinsicResult(node), IntrinsicOperand(node, 0, instruction) };
                            IntrinsicMove(operands[0], IntrinsicOperand(node, 2, instruction));
                            break;
                        default:
                            operands = new[] { IntrinsicOperand(node, 0, instruction), IntrinsicOperand(node, 2, instruction) };
                            break;
                    }
                    _owner.Emit(FitIntrinsic(instruction, operands, length).WithMask(1, zeroing: false));
                }

                // Operand views and memory sizes come from the encoding at this length, or from an xmm one if it has none
                private X86Instruction FitIntrinsic(X86InstrKind instruction, IReadOnlyList<X86Operand> operands, int length)
                {
                    static X86Operand At(IReadOnlyList<X86Operand> list, int i) => i < list.Count ? list[i] : X86Operand.None;
                    var fitted = new X86Operand[operands.Count];
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foreach (X86Form form in X86Forms.ForKind(instruction))
                        {
                            if (form.Space == X86EncodingSpace.Legacy || form.ExplicitOperandCount != operands.Count)
                                continue;
                            if (pass == 0 && !form.Has(X86FormFlags.VectorLengthIgnored) && 16 << form.VectorLength != length)
                                continue;
                            if (pass == 1 && Array.Exists(form.Operands, spec => spec.Class is X86OperandClass.Ymm or X86OperandClass.Zmm))
                                continue;
                            if (TryFitOperands(form, operands, fitted))
                                return new X86Instruction(instruction, At(fitted, 0), At(fitted, 1), At(fitted, 2), operand3: At(fitted, 3));
                        }
                    }
                    return new X86Instruction(instruction, At(operands, 0), At(operands, 1), At(operands, 2), operand3: At(operands, 3));
                }

                private static bool TryFitOperands(X86Form form, IReadOnlyList<X86Operand> operands, X86Operand[] fitted)
                {
                    int next = 0;
                    foreach (X86OperandSpec spec in form.Operands)
                    {
                        if (spec.Role == X86OperandRole.Implicit && !spec.One && spec.Fixed != X86Register.Rcx)
                            continue;
                        X86Operand operand = operands[next];
                        int vectorSize = spec.Class switch { X86OperandClass.Xmm => 16, X86OperandClass.Ymm => 32, X86OperandClass.Zmm => 64, _ => 0 };
                        switch (operand.Kind)
                        {
                            case X86OperandKind.Register when X86Registers.IsVector(operand.Register):
                                if (vectorSize == 0 || !spec.AllowsRegister)
                                    return false;
                                operand = Reg(operand.Register, vectorSize);
                                break;
                            case X86OperandKind.Register when X86Registers.IsMask(operand.Register):
                                if (spec.Class != X86OperandClass.Mask || !spec.AllowsRegister)
                                    return false;
                                break;
                            case X86OperandKind.Register:
                                if (spec.Class != X86OperandClass.Gpr || !spec.AllowsRegister)
                                    return false;
                                break;
                            case X86OperandKind.Memory:
                                if (!spec.AllowsMemory)
                                    return false;
                                if (spec.Class != X86OperandClass.Gpr && spec.MemorySize != 0)
                                    operand = operand.WithSize(spec.MemorySize);
                                break;
                            case X86OperandKind.Immediate:
                                if (spec.Class != X86OperandClass.Immediate)
                                    return false;
                                break;
                            default:
                                return false;
                        }
                        fitted[next++] = operand;
                    }
                    return next == operands.Count;
                }

                private void EmitImmediateRangeCheck(GenTree node, int operandIndex, int last)
                {
                    X86Register value = GeneralTemp(node, 1);
                    string inRange = _owner.CreateLocalLabel($"{_methodLabel}_imm_in_range");
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(value, 4), Reg(SimdUse(node, operandIndex), 1)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(value, 4), Imm(last)));
                    EmitConditionalJump(X86Condition.Be, inRange);
                    EmitManagedExceptionThrow(node, "ArgumentOutOfRangeException");
                    _owner.DefineLabel(inRange);
                }

                private void EmitX86Gather(GenTree node, X86IntrinsicInfo info, X86InstrKind instruction)
                {
                    bool merge = info.Form == X86IntrinsicForm.GatherMask;
                    int addressIndex = merge ? 1 : 0;
                    X86Register d = RequireSimdResult(node);
                    X86Register mask = SimdTemp(node, 0, avoid: d);
                    int resultSize = Math.Max(16, SimdTypes.SizeOf(node.StackKind));
                    int indexSize = SimdTypes.SizeOf(node.Operands[addressIndex + 1].StackKind);
                    if (merge)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movaps, Reg(mask, resultSize), Reg(SimdUse(node, 3), resultSize)));
                        IntrinsicMove(Reg(d, resultSize), Reg(SimdUse(node, 0), resultSize));
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Ternary(X86InstrKind.Pcmpeqd, Reg(mask, resultSize), Reg(mask, resultSize), Reg(mask, resultSize)));
                    }

                    X86Register address = SimdUse(node, addressIndex);
                    X86Register index = X86Registers.Vector(X86Registers.Index(SimdUse(node, addressIndex + 1)), indexSize);
                    X86Instruction Gather(int scale)
                        => FitIntrinsic(instruction, new[] { Reg(d, resultSize), X86Operand.Memory(address, 0, 0, index, scale), Reg(mask, resultSize) },
                            Math.Max(resultSize, indexSize));

                    int scaleIndex = node.Operands.Length - 1;
                    if (TryGetContainedIntegerImmediate(node, scaleIndex, out long constant))
                    {
                        if (constant is 1 or 2 or 4 or 8)
                            _owner.Emit(Gather((int)constant));
                        else
                            EmitManagedExceptionThrow(node, "ArgumentOutOfRangeException");
                        return;
                    }

                    X86Operand scaleOperand = Reg(SimdUse(node, scaleIndex), 1);
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_gather_done");
                    foreach (int scale in new[] { 1, 2, 4, 8 })
                    {
                        string next = _owner.CreateLocalLabel($"{_methodLabel}_gather_scale");
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, scaleOperand, Imm(scale)));
                        EmitConditionalJump(X86Condition.Ne, next);
                        _owner.Emit(Gather(scale));
                        EmitJump(done);
                        _owner.DefineLabel(next);
                    }
                    EmitManagedExceptionThrow(node, "ArgumentOutOfRangeException");
                    _owner.DefineLabel(done);
                }

                private void EmitX86BitFieldExtract(GenTree node, X86InstrKind instruction)
                {
                    X86Operand d = IntrinsicResult(node);
                    X86Operand value = IntrinsicOperand(node, 0, instruction);
                    if (node.Operands.Length == 2)
                    {
                        _owner.Emit(X86Instruction.Ternary(instruction, d, value, Reg(SimdUse(node, 1), d.Size)));
                        return;
                    }
                    X86Register control = GeneralTemp(node, 0);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(control, 4), Reg(SimdUse(node, 2), 1)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Shl, Reg(control, 4), Imm(8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(control, 1), Reg(SimdUse(node, 1), 1)));
                    _owner.Emit(X86Instruction.Ternary(instruction, d, value, Reg(control, d.Size)));
                }

                private void EmitX86MultiplyNoFlags(GenTree node, X86InstrKind instruction)
                {
                    X86Operand d = IntrinsicResult(node);
                    X86Operand right = IntrinsicOperand(node, 1, instruction);
                    if (node.Operands.Length == 2)
                    {
                        _owner.Emit(X86Instruction.Ternary(instruction, d, d, right));
                        return;
                    }
                    X86Register low = GeneralTemp(node, 0, avoid: d.Register);
                    _owner.Emit(X86Instruction.Ternary(instruction, d, Reg(low, d.Size), right));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, X86Operand.Memory(SimdUse(node, 2), 0, d.Size), Reg(low, d.Size)));
                }

                private void EmitX86Simple(GenTree node, X86IntrinsicInfo info, X86InstrKind instruction)
                {
                    int immediateIndex = X86Intrinsics.ImmediateOperandIndex(node);
                    int values = node.Operands.Length - (immediateIndex >= 0 ? 1 : 0);
                    X86Operand d = IntrinsicResult(node);
                    X86Operand source;
                    if (values == 1)
                    {
                        source = IntrinsicOperand(node, 0, instruction);
                    }
                    else
                    {
                        X86Operand right = IntrinsicOperand(node, 1, instruction);
                        if (immediateIndex < 0 && !info.Has(X86IntrinsicFlags.TableImmediate))
                        {
                            EmitIntrinsicRmw(node, instruction, d, IntrinsicOperand(node, 0, instruction), right, X86Intrinsics.IsCommutative(node));
                            return;
                        }
                        IntrinsicMove(d, IntrinsicOperand(node, 0, instruction));
                        source = right;
                    }

                    if (info.Has(X86IntrinsicFlags.TableImmediate))
                        EmitIntrinsicInstruction(instruction, d, source, info.Immediate);
                    else if (immediateIndex < 0)
                        EmitIntrinsicInstruction(instruction, d, source, null);
                    else if (TryGetContainedIntegerImmediate(node, immediateIndex, out long immediate))
                        EmitIntrinsicInstruction(instruction, d, source, (int)immediate & 0xFF);
                    else
                        EmitImmediateJumpTable(node, immediateIndex, value => X86Instruction.Ternary(instruction, d, source, Imm(value)));
                }

                private void EmitX86Shift(GenTree node, X86InstrKind instruction)
                {
                    X86Operand d = IntrinsicResult(node);
                    if (SimdTypes.IsSimdKind(node.Operands[1].StackKind))
                    {
                        EmitIntrinsicRmw(node, instruction, d, IntrinsicOperand(node, 0, instruction), IntrinsicOperand(node, 1, instruction), commutative: false);
                        return;
                    }
                    if (TryGetContainedIntegerImmediate(node, 1, out long count))
                    {
                        IntrinsicMove(d, IntrinsicOperand(node, 0, instruction));
                        _owner.Emit(X86Instruction.Binary(instruction, d, Imm((int)count & 0xFF)));
                        return;
                    }
                    if (!GenTreeLinearLoweringClassifier.X86ShiftHasRegisterCount(instruction))
                    {
                        IntrinsicMove(d, IntrinsicOperand(node, 0, instruction));
                        EmitImmediateJumpTable(node, 1, value => X86Instruction.Binary(instruction, d, Imm(value)));
                        return;
                    }
                    X86Register countVector = SimdTemp(node, 0, avoid: d.Register);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movd, SimdOperand(countVector), Reg(SimdUse(node, 1), 4)));
                    IntrinsicMove(d, IntrinsicOperand(node, 0, instruction));
                    _owner.Emit(X86Instruction.Binary(instruction, d, SimdOperand(countVector)));
                }

                private void EmitX86Compare(GenTree node, X86IntrinsicInfo info, X86InstrKind instruction)
                {
                    X86Operand d = IntrinsicResult(node);
                    X86Operand left = IntrinsicOperand(node, 0, instruction);
                    X86Operand right = IntrinsicOperand(node, 1, instruction);
                    bool floating = instruction is X86InstrKind.Cmpps or X86InstrKind.Cmppd or X86InstrKind.Cmpss or X86InstrKind.Cmpsd;
                    int? predicate = floating ? info.Immediate : null;
                    if (!X86Intrinsics.IsCompareSwap(node, out bool scalar))
                    {
                        if (predicate is null)
                        {
                            EmitIntrinsicRmw(node, instruction, d, left, right, X86Intrinsics.IsCommutative(node));
                            return;
                        }
                        if (d.Register == right.Register && d.Register != left.Register && X86Intrinsics.IsCommutative(node))
                        {
                            EmitIntrinsicInstruction(instruction, d, left, predicate);
                            return;
                        }
                        IntrinsicMove(d, left);
                        EmitIntrinsicInstruction(instruction, d, right, predicate);
                        return;
                    }

                    if (!scalar)
                    {
                        IntrinsicMove(d, right);
                        EmitIntrinsicInstruction(instruction, d, left, predicate);
                        return;
                    }

                    // A swapped scalar compare takes its upper lanes from the right operand, so they come back from the left one
                    X86Register mask = SimdTemp(node, 0, avoid: d.Register);
                    SimdMove(mask, right.Register);
                    EmitIntrinsicInstruction(instruction, SimdOperand(mask), left, predicate);
                    IntrinsicMove(d, left);
                    _owner.Emit(X86Instruction.Binary(instruction == X86InstrKind.Cmpss ? X86InstrKind.Movss : X86InstrKind.Movsd, d, SimdOperand(mask)));
                }

                private void EmitX86CompareFlags(GenTree node, X86IntrinsicInfo info, X86InstrKind instruction)
                {
                    X86Operand left = IntrinsicOperand(node, 0, instruction);
                    X86Operand right = IntrinsicOperand(node, 1, instruction);
                    if (info.Has(X86IntrinsicFlags.SwapOperands))
                        (left, right) = (right, left);
                    _owner.Emit(X86Instruction.Binary(instruction, left, right));

                    X86Register d = ToX86Register(RequireResultRegister(node), Target);
                    var condition = (X86Condition)info.Immediate;
                    if (!X86Intrinsics.CompareFlagsNeedParity(node))
                    {
                        EmitBooleanResult(condition, d);
                        return;
                    }

                    // Unordered operands set the parity flag along with the zero flag
                    X86Register parity = GeneralTemp(node, 0, avoid: d);
                    bool equal = condition == X86Condition.E;
                    _owner.Emit(X86Instruction.Setcc(equal ? X86Condition.Np : X86Condition.P, Reg(parity, 1)));
                    _owner.Emit(X86Instruction.Setcc(condition, Reg(d, 1)));
                    _owner.Emit(X86Instruction.Binary(equal ? X86InstrKind.And : X86InstrKind.Or, Reg(d, 1), Reg(parity, 1)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(d, 4), Reg(d, 1)));
                }

                private void EmitX86DivRem(GenTree node, RuntimePrimitiveKind kind)
                {
                    int size = HWIntrinsics.ElementSize(kind);
                    bool signed = kind is RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.Int64;
                    X86Register divisor = GeneralTemp(node, 0);
                    string overflow = _owner.CreateLocalLabel($"{_methodLabel}_divrem_overflow");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_divrem_done");
                    int divisorSize = signed && size == 4 ? 8 : size;
                    _owner.Emit(X86Instruction.Binary(signed && size == 4 ? X86InstrKind.Movsxd : X86InstrKind.Mov,
                        Reg(divisor, divisorSize), Reg(SimdUse(node, 2), size)));

                    string nonZero = _owner.CreateLocalLabel($"{_methodLabel}_divrem_nonzero");
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(divisor, divisorSize), Reg(divisor, divisorSize)));
                    EmitConditionalJump(X86Condition.Ne, nonZero);
                    EmitManagedExceptionThrow(node, "DivideByZeroException");
                    _owner.DefineLabel(nonZero);

                    if (!signed)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.Rdx, size), Reg(divisor, size)));
                        EmitConditionalJump(X86Condition.Ae, overflow);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Div, Reg(divisor, size)));
                    }
                    else if (size == 4)
                    {
                        // A 64-bit idiv cannot fault once a -1 divisor is handled apart; its quotient must still fit 32 bits
                        string divide = _owner.CreateLocalLabel($"{_methodLabel}_divrem_divide");
                        string check = _owner.CreateLocalLabel($"{_methodLabel}_divrem_check");
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Shl, Reg(X86Register.Rdx, 8), Imm(32)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Or, Reg(X86Register.Rax, 8), Reg(X86Register.Rdx, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(divisor, 8), Imm(-1)));
                        EmitConditionalJump(X86Condition.Ne, divide);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(X86Register.Rax, 8)));
                        EmitConditionalJump(X86Condition.O, overflow);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rdx, 4), Reg(X86Register.Rdx, 4)));
                        EmitJump(check);
                        _owner.DefineLabel(divide);
                        _owner.Emit(new X86Instruction(X86InstrKind.Cqo));
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Idiv, Reg(divisor, 8)));
                        _owner.DefineLabel(check);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movsxd, Reg(divisor, 8), Reg(X86Register.Rax, 4)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(divisor, 8), Reg(X86Register.Rax, 8)));
                        EmitConditionalJump(X86Condition.Ne, overflow);
                    }
                    else
                    {
                        // Divides the magnitudes so the range check is exact, then restores the signs
                        X86Register dividendSign = GeneralTemp(node, 1);
                        X86Register quotientSign = GeneralTemp(node, 2);
                        string dividendPositive = _owner.CreateLocalLabel($"{_methodLabel}_divrem_dividend_positive");
                        string divisorPositive = _owner.CreateLocalLabel($"{_methodLabel}_divrem_divisor_positive");
                        string negative = _owner.CreateLocalLabel($"{_methodLabel}_divrem_negative");
                        string remainder = _owner.CreateLocalLabel($"{_methodLabel}_divrem_remainder");
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dividendSign, 8), Reg(X86Register.Rdx, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(quotientSign, 8), Reg(X86Register.Rdx, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(quotientSign, 8), Reg(divisor, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.Rdx, 8), Reg(X86Register.Rdx, 8)));
                        EmitConditionalJump(X86Condition.Ns, dividendPositive);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(X86Register.Rdx, 8)));
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(X86Register.Rax, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Sbb, Reg(X86Register.Rdx, 8), Imm(0)));
                        _owner.DefineLabel(dividendPositive);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(divisor, 8), Reg(divisor, 8)));
                        EmitConditionalJump(X86Condition.Ns, divisorPositive);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(divisor, 8)));
                        _owner.DefineLabel(divisorPositive);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.Rdx, 8), Reg(divisor, 8)));
                        EmitConditionalJump(X86Condition.Ae, overflow);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Div, Reg(divisor, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(quotientSign, 8), Reg(quotientSign, 8)));
                        EmitConditionalJump(X86Condition.S, negative);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.Rax, 8), Reg(X86Register.Rax, 8)));
                        EmitConditionalJump(X86Condition.S, overflow);
                        EmitJump(remainder);
                        _owner.DefineLabel(negative);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(X86Register.Rax, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.Rax, 8), Reg(X86Register.Rax, 8)));
                        EmitConditionalJump(X86Condition.G, overflow);
                        _owner.DefineLabel(remainder);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(dividendSign, 8), Reg(dividendSign, 8)));
                        EmitConditionalJump(X86Condition.Ns, done);
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(X86Register.Rdx, 8)));
                    }
                    EmitJump(done);
                    _owner.DefineLabel(overflow);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(done);
                }

                // Indexes equally sized copies of the instruction, one per value, each followed by a jump past the table
                private void EmitImmediateJumpTable(GenTree node, int immediateIndex, Func<int, X86Instruction> entry, int count = 256)
                {
                    X86Register index = SimdUse(node, immediateIndex);
                    X86Register table = GeneralTemp(node, 0);
                    X86Register offset = GeneralTemp(node, 1);
                    int entrySize = X86CodeEncoder.Encode(entry(0), _owner._machineTarget).Length + 5;
                    string tableLabel = _owner.CreateLocalLabel($"{_methodLabel}_imm_table");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_imm_done");
                    _owner.EmitLea(table, tableLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(offset, 4), Reg(index, 1)));
                    if (count != 256)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.And, Reg(offset, 4), Imm(count - 1)));
                    _owner.Emit(X86Instruction.Ternary(X86InstrKind.Imul, Reg(offset, 4), Reg(offset, 4), Imm(entrySize)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(table, 8), Reg(offset, 8)));
                    _owner.Emit(X86Instruction.Branch(X86InstrKind.Jmp, Reg(table, 8)));
                    _owner.DefineLabel(tableLabel);
                    for (int value = 0; value < count; value++)
                    {
                        _owner.Emit(entry(value));
                        EmitJump(doneLabel);
                    }
                    _owner.DefineLabel(doneLabel);
                }

                private X86Register GeneralTemp(GenTree node, int index, X86Register avoid = X86Register.Invalid)
                {
                    int seen = 0;
                    for (int i = 0; i < node.InternalRegisters.Length; i++)
                    {
                        GenTreeInternalRegister register = node.InternalRegisters[i];
                        if (register.RegisterClass != RegisterClass.General)
                            continue;
                        X86Register candidate = ToX86Register(register.Register, Target);
                        if (candidate == avoid)
                            continue;
                        if (seen++ == index)
                            return candidate;
                    }
                    throw Unsupported(node, $"x86 intrinsic has no internal general register {index}");
                }

                private X86Operand IntrinsicResult(GenTree node)
                {
                    X86Register register = ToX86Register(RequireResultRegister(node), Target);
                    return X86Registers.IsVector(register) ? SimdOperand(register) : Reg(register, IntrinsicGeneralSize(node.StackKind));
                }

                private X86Operand IntrinsicOperand(GenTree node, int operandIndex, X86InstrKind instruction)
                {
                    X86Register register = SimdUse(node, operandIndex);
                    if (X86Intrinsics.IsAddressOperand(node, operandIndex))
                        return X86Operand.Memory(register, 0, IntrinsicMemorySize(node, instruction));
                    return X86Registers.IsVector(register) ? SimdOperand(register) : Reg(register, IntrinsicGeneralSize(node.Operands[operandIndex].StackKind));
                }

                private static int IntrinsicGeneralSize(GenStackKind kind)
                    => kind is GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr or GenStackKind.ByRef or GenStackKind.Ref ? 8 : 4;

                private static int IntrinsicMemorySize(GenTree node, X86InstrKind instruction)
                    => instruction switch
                    {
                        X86InstrKind.Prefetcht0 or X86InstrKind.Prefetcht1 or X86InstrKind.Prefetcht2 or X86InstrKind.Prefetchnta or
                        X86InstrKind.Maskmovdqu => 1,
                        X86InstrKind.Pmovsxbq or X86InstrKind.Pmovzxbq => 2,
                        X86InstrKind.Movss or X86InstrKind.Movd or X86InstrKind.Pmovsxbd or X86InstrKind.Pmovzxbd or
                        X86InstrKind.Pmovsxwq or X86InstrKind.Pmovzxwq => 4,
                        X86InstrKind.Movsd or X86InstrKind.Movq or X86InstrKind.Movlps or X86InstrKind.Movhps or X86InstrKind.Movlpd or
                        X86InstrKind.Movhpd or X86InstrKind.Movddup or X86InstrKind.Pmovsxbw or X86InstrKind.Pmovzxbw or X86InstrKind.Pmovsxwd or
                        X86InstrKind.Pmovzxwd or X86InstrKind.Pmovsxdq or X86InstrKind.Pmovzxdq => 8,
                        X86InstrKind.Movnti => HWIntrinsics.ElementSize(HWIntrinsics.BaseKind(node)),
                        _ => 16,
                    };

                private void IntrinsicMove(X86Operand destination, X86Operand source)
                {
                    if (source.Kind == X86OperandKind.Register && source.Register == destination.Register)
                        return;
                    if (X86Registers.IsVector(destination.Register))
                        SimdMove(destination.Register, source.Register);
                    else
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, destination, source.WithSize(destination.Size)));
                }

                private void EmitIntrinsicRmw(GenTree node, X86InstrKind instruction, X86Operand d, X86Operand left, X86Operand right, bool commutative)
                {
                    if (right.Kind == X86OperandKind.Register && right.Register == d.Register && left.Register != d.Register)
                    {
                        if (!commutative)
                            throw Unsupported(node, $"{instruction} result shares a register with its delay-freed operand");
                        _owner.Emit(X86Instruction.Binary(instruction, d, left));
                        return;
                    }
                    IntrinsicMove(d, left);
                    _owner.Emit(X86Instruction.Binary(instruction, d, right));
                }

                private void EmitIntrinsicInstruction(X86InstrKind instruction, X86Operand destination, X86Operand source, int? immediate)
                    => _owner.Emit(immediate is int value
                        ? X86Instruction.Ternary(instruction, destination, source, Imm(value))
                        : X86Instruction.Binary(instruction, destination, source));
            }
        }
    }
}
