using Cnidaria.RiscV;
using System;

namespace Cnidaria.Cs;

internal static partial class RiscVCodeGenerator
{
    private sealed partial class Generator
    {
        private (int Type, int Length)? _vectorConfiguration;

        public void SetVectorConfiguration(int size, int elementSize, int length = -1)
        {
            int count = length < 0 ? size / elementSize : length;
            // V guarantees VLEN >= 128; fixed-size values must also fit that implementation.
            int group = RegisterInfo.VectorRegisterCount(size);
            int vtype = System.Numerics.BitOperations.Log2((uint)group) |
                (System.Numerics.BitOperations.Log2((uint)elementSize) << 3);
            if (_vectorConfiguration == (vtype, count))
                return;
            if (count <= 31)
                Emit(RVInstruction.Vsetivli(RVRegister.X0, count, vtype));
            else
            {
                EmitLoadImmediate(RVRegister.X30, count);
                Emit(RVInstruction.Vsetvli(RVRegister.X0, RVRegister.X30, vtype));
            }
            _vectorConfiguration = (vtype, count);
        }

        private sealed partial class MethodEmitter
        {
            private RVRegister _vectorTemp;
            private RVRegister _vectorTemp2;

            private void EmitVectorMove(RVRegister destination, RVRegister source, int size)
            {
                if (destination == source)
                    return;
                _owner.SetVectorConfiguration(size, 1);
                _owner.Emit(RVInstruction.Vx(RVInstrKind.VaddVx, destination, source, RVRegister.X0));
            }

            private void EmitVectorMemory(RVRegister vector, RVRegister address, int offset, int size, bool load)
            {
                // AVL materialization uses t5, so preserve an address held there first.
                if (offset != 0 || address == RVRegister.X30)
                {
                    _owner.EmitAddImmediate(RVRegister.X31, address, offset);
                    address = RVRegister.X31;
                }
                _owner.SetVectorConfiguration(size, 1);
                _owner.Emit(load ? RVInstruction.Vl(RVInstrKind.Vle8V, vector, address)
                    : RVInstruction.Vs(RVInstrKind.Vse8V, vector, address));
            }

            private RVRegister VectorUse(GenTree node, int operand)
                => ToRegister(RequireUseRegisterForOperand(node, operand, "vector intrinsic operand"));

            private int VectorImmediate(GenTree node, int operand)
                => TryGetContainedIntegerImmediate(node, operand, out long value) ? checked((int)value)
                    : throw Unsupported(node, "Vector intrinsic requires a contained immediate");

            private void EmitHWIntrinsic(GenTree node)
            {
                _vectorTemp = _vectorTemp2 = RVRegister.Invalid;
                foreach (GenTreeInternalRegister temporary in node.InternalRegisters)
                {
                    if (temporary.RegisterClass != RegisterClass.Vector)
                        continue;
                    if (_vectorTemp == RVRegister.Invalid)
                        _vectorTemp = ToRegister(temporary.Register);
                    else
                        _vectorTemp2 = ToRegister(temporary.Register);
                }
                HWIntrinsicId id = HWIntrinsics.Id(node);
                RuntimePrimitiveKind kind = HWIntrinsics.BaseKind(node);
                int size = HWIntrinsics.SimdSize(node);
                int elementSize = HWIntrinsics.ElementSize(kind);
                bool floating = HWIntrinsics.IsFloating(kind);
                bool unsigned = HWIntrinsics.IsUnsigned(kind);
                RVRegister d = ToRegister(RequireResultRegister(node));
                _owner.SetVectorConfiguration(size, elementSize);

                switch (id)
                {
                    case HWIntrinsicId.Zero:
                    case HWIntrinsicId.AllBitsSet:
                        _owner.Emit(RVInstruction.Vi(RVInstrKind.VmvVi, d, RVRegister.V0, id == HWIntrinsicId.Zero ? 0 : -1));
                        return;
                    case HWIntrinsicId.Broadcast:
                    case HWIntrinsicId.CreateElements:
                        EmitVectorCreate(node, d, size, elementSize, floating);
                        return;
                    case HWIntrinsicId.CreateScalar:
                    case HWIntrinsicId.CreateScalarUnsafe:
                        if (id == HWIntrinsicId.CreateScalar)
                            _owner.Emit(RVInstruction.Vi(RVInstrKind.VmvVi, d, RVRegister.V0, 0));
                        _owner.Emit(RVInstruction.Vx(floating ? RVInstrKind.VfmvSF : RVInstrKind.VmvSx, d, RVRegister.V0, VectorUse(node, 0)));
                        return;
                    case HWIntrinsicId.BitCast:
                    {
                        int sourceSize = SimdTypes.SizeOf(node.Operands[0].StackKind);
                        EmitVectorMove(d, VectorUse(node, 0), Math.Min(sourceSize, SimdTypes.SizeOf(node.StackKind)));
                        return;
                    }
                    case HWIntrinsicId.Combine:
                    case HWIntrinsicId.ZeroExtend:
                    case HWIntrinsicId.GetUpper:
                    case HWIntrinsicId.WithLower:
                    case HWIntrinsicId.WithUpper:
                        EmitVectorHalves(node, d, id, size);
                        return;
                    case HWIntrinsicId.GetElement:
                    case HWIntrinsicId.ToScalar:
                    {
                        int index = id == HWIntrinsicId.ToScalar ? 0 : VectorImmediate(node, 1);
                        RVRegister source = VectorUse(node, 0);
                        if (index != 0)
                        {
                            EmitVectorSlide(RVInstrKind.VslidedownVi, RVInstrKind.VslidedownVx, _vectorTemp, source, index);
                            source = _vectorTemp;
                        }
                        EmitVectorExtract(d, source, kind);
                        return;
                    }
                    case HWIntrinsicId.WithElement:
                        EmitVectorMove(_vectorTemp, VectorUse(node, 0), size);
                        _owner.SetVectorConfiguration(size, elementSize);
                        EmitVectorInsert(_vectorTemp, VectorUse(node, 2), VectorImmediate(node, 1), floating);
                        EmitVectorMove(d, _vectorTemp, size);
                        return;
                    case HWIntrinsicId.Add:
                    case HWIntrinsicId.Subtract:
                    case HWIntrinsicId.Multiply:
                    case HWIntrinsicId.Divide:
                    case HWIntrinsicId.Min:
                    case HWIntrinsicId.Max:
                    case HWIntrinsicId.AddSaturate:
                    case HWIntrinsicId.SubtractSaturate:
                    case HWIntrinsicId.And:
                    case HWIntrinsicId.Or:
                    case HWIntrinsicId.Xor:
                    {
                        RVInstrKind opcode = id switch
                        {
                            HWIntrinsicId.Add => floating ? RVInstrKind.VfaddVv : RVInstrKind.VaddVv,
                            HWIntrinsicId.Subtract => floating ? RVInstrKind.VfsubVv : RVInstrKind.VsubVv,
                            HWIntrinsicId.Multiply => floating ? RVInstrKind.VfmulVv : RVInstrKind.VmulVv,
                            HWIntrinsicId.Divide => RVInstrKind.VfdivVv,
                            HWIntrinsicId.Min => unsigned ? RVInstrKind.VminuVv : RVInstrKind.VminVv,
                            HWIntrinsicId.Max => unsigned ? RVInstrKind.VmaxuVv : RVInstrKind.VmaxVv,
                            HWIntrinsicId.AddSaturate => unsigned ? RVInstrKind.VsadduVv : RVInstrKind.VsaddVv,
                            HWIntrinsicId.SubtractSaturate => unsigned ? RVInstrKind.VssubuVv : RVInstrKind.VssubVv,
                            HWIntrinsicId.And => RVInstrKind.VandVv,
                            HWIntrinsicId.Or => RVInstrKind.VorVv,
                            _ => RVInstrKind.VxorVv,
                        };
                        _owner.Emit(RVInstruction.Vv(opcode, d, VectorUse(node, 0), VectorUse(node, 1)));
                        return;
                    }
                    case HWIntrinsicId.AndNot:
                        _owner.Emit(RVInstruction.Vi(RVInstrKind.VxorVi, _vectorTemp, VectorUse(node, 1), -1));
                        _owner.Emit(RVInstruction.Vv(RVInstrKind.VandVv, d, VectorUse(node, 0), _vectorTemp));
                        return;
                    case HWIntrinsicId.Not:
                        _owner.Emit(RVInstruction.Vi(RVInstrKind.VxorVi, d, VectorUse(node, 0), -1));
                        return;
                    case HWIntrinsicId.ConditionalSelect:
                        _owner.Emit(RVInstruction.Vv(RVInstrKind.VxorVv, _vectorTemp, VectorUse(node, 1), VectorUse(node, 2)));
                        _owner.Emit(RVInstruction.Vv(RVInstrKind.VandVv, _vectorTemp, _vectorTemp, VectorUse(node, 0)));
                        _owner.Emit(RVInstruction.Vv(RVInstrKind.VxorVv, d, _vectorTemp, VectorUse(node, 2)));
                        return;
                    case HWIntrinsicId.Negate:
                        _owner.Emit(floating ? RVInstruction.Vv(RVInstrKind.VfsgnjnVv, d, VectorUse(node, 0), VectorUse(node, 0))
                            : RVInstruction.Vx(RVInstrKind.VrsubVx, d, VectorUse(node, 0), RVRegister.X0));
                        return;
                    case HWIntrinsicId.Abs:
                        if (unsigned)
                            EmitVectorMove(d, VectorUse(node, 0), size);
                        else if (floating)
                            _owner.Emit(RVInstruction.Vv(RVInstrKind.VfsgnjxVv, d, VectorUse(node, 0), VectorUse(node, 0)));
                        else
                        {
                            _owner.Emit(RVInstruction.Vx(RVInstrKind.VmsltVx, RVRegister.V0, VectorUse(node, 0), RVRegister.X0));
                            _owner.Emit(RVInstruction.Vx(RVInstrKind.VrsubVx, _vectorTemp, VectorUse(node, 0), RVRegister.X0));
                            _owner.Emit(RVInstruction.Vv(RVInstrKind.VmergeVvm, d, VectorUse(node, 0), _vectorTemp, unmasked: false));
                        }
                        return;
                    case HWIntrinsicId.Sqrt:
                        _owner.Emit(RVInstruction.Vv(RVInstrKind.VfsqrtV, d, VectorUse(node, 0), RVRegister.V0));
                        return;
                    case HWIntrinsicId.CompareEqual:
                    case HWIntrinsicId.CompareGreaterThan:
                    case HWIntrinsicId.CompareGreaterThanOrEqual:
                    case HWIntrinsicId.CompareLessThan:
                    case HWIntrinsicId.CompareLessThanOrEqual:
                    case HWIntrinsicId.EqualsAll:
                    case HWIntrinsicId.EqualsAny:
                        EmitVectorCompare(node, d, id, kind, size);
                        return;
                    case HWIntrinsicId.ShiftLeft:
                    case HWIntrinsicId.ShiftRightArithmetic:
                    case HWIntrinsicId.ShiftRightLogical:
                    {
                        RVInstrKind opcode = id == HWIntrinsicId.ShiftLeft ? RVInstrKind.VsllVx
                            : id == HWIntrinsicId.ShiftRightArithmetic ? RVInstrKind.VsraVx : RVInstrKind.VsrlVx;
                        RVRegister count;
                        if (TryGetContainedIntegerImmediate(node, 1, out long immediate))
                        {
                            count = RVRegister.X30;
                            _owner.EmitLoadImmediate(count, immediate & (elementSize * 8 - 1));
                        }
                        else
                            count = VectorUse(node, 1);
                        _owner.Emit(RVInstruction.Vx(opcode, d, VectorUse(node, 0), count));
                        return;
                    }
                    case HWIntrinsicId.Sum:
                        _owner.Emit(RVInstruction.Vi(RVInstrKind.VmvVi, _vectorTemp, RVRegister.V0, 0));
                        _owner.Emit(RVInstruction.Vv(floating ? RVInstrKind.VfredosumVs : RVInstrKind.VredsumVs,
                            _vectorTemp2, VectorUse(node, 0), _vectorTemp));
                        EmitVectorExtract(d, _vectorTemp2, kind);
                        return;
                    case HWIntrinsicId.ExtractMostSignificantBits:
                    {
                        int count = size / elementSize;
                        _owner.Emit(RVInstruction.Vx(RVInstrKind.VmsltVx, RVRegister.V0, VectorUse(node, 0), RVRegister.X0));
                        _owner.SetVectorConfiguration(8, 8);
                        _owner.Emit(RVInstruction.Vx(RVInstrKind.VmvXs, d, RVRegister.V0, RVRegister.X0));
                        if (count < 64)
                        {
                            _owner.Emit(RVInstruction.I(RVInstrKind.Slli, d, d, 64 - count));
                            _owner.Emit(RVInstruction.I(RVInstrKind.Srli, d, d, 64 - count));
                        }
                        return;
                    }
                    default:
                        throw Unsupported(node, $"Unsupported RVV intrinsic {id}");
                }
            }

            private void EmitVectorCreate(GenTree node, RVRegister destination, int size, int elementSize, bool floating)
            {
                if (HWIntrinsics.HasConstantElements(node))
                {
                    byte[] bytes = new byte[size];
                    for (int i = 0; i < size / elementSize; i++)
                    {
                        GenTree element = node.Operands[node.Operands.Length == 1 ? 0 : i];
                        long bits = element.Kind is GenTreeKind.ConstI8 or GenTreeKind.ConstR8Bits ? element.Int64 : element.Int32;
                        for (int j = 0; j < elementSize; j++)
                            bytes[i * elementSize + j] = (byte)(bits >> (j * 8));
                    }
                    string label = _owner.AddConstantData(bytes, 8, "vector");
                    _owner.EmitMaterializeAddress(label, RVRegister.X31);
                    EmitVectorMemory(destination, RVRegister.X31, 0, size, load: true);
                    return;
                }
                if (HWIntrinsics.Id(node) == HWIntrinsicId.Broadcast)
                {
                    _owner.Emit(RVInstruction.Vx(floating ? RVInstrKind.VfmvVF : RVInstrKind.VmvVx, destination, RVRegister.V0, VectorUse(node, 0)));
                    return;
                }
                _owner.Emit(RVInstruction.Vi(RVInstrKind.VmvVi, _vectorTemp, RVRegister.V0, 0));
                for (int i = 0; i < node.Operands.Length; i++)
                    EmitVectorInsert(_vectorTemp, VectorUse(node, i), i, floating);
                EmitVectorMove(destination, _vectorTemp, size);
            }

            private void EmitVectorInsert(RVRegister destination, RVRegister scalar, int index, bool floating)
            {
                _owner.Emit(RVInstruction.Vv(RVInstrKind.VidV, _vectorTemp2, RVRegister.V0, RVRegister.V0));
                _owner.EmitLoadImmediate(RVRegister.X30, index);
                _owner.Emit(RVInstruction.Vx(RVInstrKind.VmseqVx, RVRegister.V0, _vectorTemp2, RVRegister.X30));
                _owner.Emit(RVInstruction.Vx(floating ? RVInstrKind.VfmergeVfm : RVInstrKind.VmergeVxm,
                    destination, destination, scalar, unmasked: false));
            }

            private void EmitVectorExtract(RVRegister destination, RVRegister source, RuntimePrimitiveKind kind)
            {
                _owner.Emit(RVInstruction.Vx(HWIntrinsics.IsFloating(kind) ? RVInstrKind.VfmvFS : RVInstrKind.VmvXs,
                    destination, source, RVRegister.X0));
                int bits = HWIntrinsics.ElementSize(kind) * 8;
                if (HWIntrinsics.IsUnsigned(kind) && bits < 64)
                {
                    _owner.Emit(RVInstruction.I(RVInstrKind.Slli, destination, destination, 64 - bits));
                    _owner.Emit(RVInstruction.I(RVInstrKind.Srli, destination, destination, 64 - bits));
                }
            }

            private void EmitVectorSlide(RVInstrKind immediateOpcode, RVInstrKind registerOpcode, RVRegister destination, RVRegister source, int offset)
            {
                if (offset <= 31)
                    _owner.Emit(RVInstruction.Vi(immediateOpcode, destination, source, offset));
                else
                {
                    _owner.EmitLoadImmediate(RVRegister.X30, offset);
                    _owner.Emit(RVInstruction.Vx(registerOpcode, destination, source, RVRegister.X30));
                }
            }

            private void EmitVectorHalves(GenTree node, RVRegister destination, HWIntrinsicId id, int size)
            {
                int resultSize = SimdTypes.SizeOf(node.StackKind);
                if (id == HWIntrinsicId.GetUpper)
                {
                    _owner.SetVectorConfiguration(size, 1);
                    EmitVectorSlide(RVInstrKind.VslidedownVi, RVInstrKind.VslidedownVx, _vectorTemp, VectorUse(node, 0), resultSize);
                    EmitVectorMove(destination, _vectorTemp, resultSize);
                    return;
                }
                int halfSize = id == HWIntrinsicId.ZeroExtend ? SimdTypes.SizeOf(node.Operands[0].StackKind) : resultSize / 2;
                if (id == HWIntrinsicId.ZeroExtend || id == HWIntrinsicId.Combine)
                {
                    EmitVectorMove(_vectorTemp2, VectorUse(node, 0), halfSize);
                    _owner.SetVectorConfiguration(resultSize, 1);
                    _owner.Emit(RVInstruction.Vi(RVInstrKind.VmvVi, _vectorTemp, RVRegister.V0, 0));
                    _owner.SetVectorConfiguration(resultSize, 1, halfSize);
                    _owner.Emit(RVInstruction.Vx(RVInstrKind.VaddVx, _vectorTemp, _vectorTemp2, RVRegister.X0));
                }
                else
                    EmitVectorMove(_vectorTemp, VectorUse(node, 0), resultSize);
                if (id != HWIntrinsicId.ZeroExtend)
                {
                    EmitVectorMove(_vectorTemp2, VectorUse(node, 1), halfSize);
                    _owner.SetVectorConfiguration(resultSize, 1, id == HWIntrinsicId.WithLower ? halfSize : resultSize);
                    if (id == HWIntrinsicId.WithLower)
                        _owner.Emit(RVInstruction.Vx(RVInstrKind.VaddVx, _vectorTemp, _vectorTemp2, RVRegister.X0));
                    else
                        EmitVectorSlide(RVInstrKind.VslideupVi, RVInstrKind.VslideupVx, _vectorTemp, _vectorTemp2, halfSize);
                }
                EmitVectorMove(destination, _vectorTemp, resultSize);
            }

            private void EmitVectorCompare(GenTree node, RVRegister destination, HWIntrinsicId id, RuntimePrimitiveKind kind, int size)
            {
                bool floating = HWIntrinsics.IsFloating(kind);
                bool unsigned = HWIntrinsics.IsUnsigned(kind);
                bool equal = id is HWIntrinsicId.CompareEqual or HWIntrinsicId.EqualsAll or HWIntrinsicId.EqualsAny;
                bool inclusive = id is HWIntrinsicId.CompareGreaterThanOrEqual or HWIntrinsicId.CompareLessThanOrEqual;
                RVInstrKind opcode = equal ? (floating ? RVInstrKind.VmfeqVv : RVInstrKind.VmseqVv)
                    : inclusive ? (floating ? RVInstrKind.VmfleVv : unsigned ? RVInstrKind.VmsleuVv : RVInstrKind.VmsleVv)
                    : floating ? RVInstrKind.VmfltVv : unsigned ? RVInstrKind.VmsltuVv : RVInstrKind.VmsltVv;
                bool reverse = id is HWIntrinsicId.CompareGreaterThan or HWIntrinsicId.CompareGreaterThanOrEqual;
                _owner.Emit(RVInstruction.Vv(opcode, RVRegister.V0, VectorUse(node, reverse ? 1 : 0), VectorUse(node, reverse ? 0 : 1)));
                if (id is HWIntrinsicId.EqualsAll or HWIntrinsicId.EqualsAny)
                {
                    _owner.Emit(RVInstruction.Vv(RVInstrKind.VcpopM, destination, RVRegister.V0, RVRegister.V0));
                    if (id == HWIntrinsicId.EqualsAll)
                    {
                        _owner.Emit(RVInstruction.I(RVInstrKind.Xori, destination, destination, size / HWIntrinsics.ElementSize(kind)));
                        _owner.Emit(RVInstruction.I(RVInstrKind.Sltiu, destination, destination, 1));
                    }
                    else
                        _owner.Emit(RVInstruction.R(RVInstrKind.Sltu, destination, RVRegister.X0, destination));
                    return;
                }
                _owner.Emit(RVInstruction.Vi(RVInstrKind.VmvVi, destination, RVRegister.V0, 0));
                _owner.Emit(RVInstruction.Vi(RVInstrKind.VmergeVim, destination, destination, -1, unmasked: false));
            }
        }
    }
}
