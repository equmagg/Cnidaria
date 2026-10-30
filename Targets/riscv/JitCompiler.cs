using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cnidaria.X86;
using static Cnidaria.X86.X86InstrKind;
using static Cnidaria.X86.X86Register;

namespace Cnidaria.RiscV;

public sealed partial class RiscVEmulator
{
    private sealed class JitCompiler
    {
        private static readonly X86Target HostTarget = new(64, X86AbiKind.WindowsX64,
            X86IsaFlags.Sse | X86IsaFlags.Sse2 | (System.Runtime.Intrinsics.X86.Sse41.IsSupported ? X86IsaFlags.Sse41 : X86IsaFlags.None),
            TargetEndianness.Little);
        private static readonly Dictionary<string, int> _contextOffsets = CreateContextOffsets();
        private readonly List<byte> _bytes = new();
        private readonly List<(int Offset, string Label)> _branches = new();
        private readonly Dictionary<string, int> _labels = new();
        private readonly List<(string Label, ulong Pc, int Count, int Size, RVMemoryAccess Access)> _misses = new();
        private readonly List<(string Label, ulong Pc, int Count)> _slowExits = new();
        private readonly RiscVEmulator _machine;
        private int _count;
        private ulong _pc;

        private JitCompiler(RiscVEmulator machine) => _machine = machine;

        public static JitBlock Compile(RiscVEmulator machine, ulong pc, ReadOnlySpan<byte> source)
        {
            var compiler = new JitCompiler(machine) { _pc = pc };
            var instructions = new List<(uint Instruction, int Size)>();
            int consumed = 0;
            bool vectorSpecialized = false;
            while (instructions.Count < JitState.MaxBlockInstructions && consumed + 2 <= source.Length)
            {
                uint instruction = BinaryPrimitives.ReadUInt16LittleEndian(source[consumed..]);
                int size = (instruction & 3) == 3 ? 4 : 2;
                if (consumed + size > source.Length || ((pc + (ulong)consumed) & PageMask) != 4094 && consumed + 4 > source.Length)
                    break;
                instruction = size == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(source[consumed..]) : ExpandCompressed((ushort)instruction);
                // A rejected vector instruction may become compilable under another VTYPE
                uint opcode = instruction & 127;
                int width = (int)((instruction >> 12) & 7);
                vectorSpecialized |= opcode == 0x57 || opcode is 0x07 or 0x27 && width is not (2 or 3);
                if (!compiler.Supported(instruction))
                    break;
                instructions.Add((instruction, size));
                consumed += size;
                if ((instruction & 127) is 0x63 or 0x67 or 0x6F || compiler.ImmediateVectorSet(instruction, out ulong type, out _) && type != machine._vtype)
                    break;
            }
            if (instructions.Count == 0)
                return new JitBlock { Count = 0, VectorSpecialized = vectorSpecialized, Bytes = Array.Empty<byte>(), Source = source[..Math.Min(source.Length, 4)].ToArray() };

            compiler.Emit(Mov, R(R11), R(OperatingSystem.IsWindows() ? Rcx : Rdi));
            compiler.Emit(Mov, R(R10), Context(nameof(JitContext.Registers)));
            compiler.Mxcsr(nameof(JitContext.SavedMxcsr), save: true);
            compiler.Mxcsr(nameof(JitContext.NativeMxcsr), save: false);
            int chainOffset = compiler._bytes.Count;
            compiler.Emit(Cmp, Context(nameof(JitContext.Remaining), 4), I(instructions.Count));
            compiler.Branch(X86Condition.B, "return");
            compiler.Emit(Add, Context(nameof(JitContext.Blocks), 4), I(1));
            bool terminated = false;
            foreach (var instruction in instructions)
            {
                terminated = compiler.Translate(instruction.Instruction, instruction.Size);
                compiler._count++;
                compiler._pc += (ulong)instruction.Size;
            }
            if (!terminated)
                compiler.Exit(compiler._pc, compiler._count);
            foreach (var miss in compiler._misses)
            {
                compiler.Label(miss.Label);
                compiler.Emit(Mov, Context(nameof(JitContext.MemoryAddress)), R(Rax));
                compiler.Emit(Mov, Context(nameof(JitContext.MemoryAccess), 1), I((int)miss.Access));
                compiler.Emit(Mov, Context(nameof(JitContext.MemorySize), 4), I(miss.Size));
                compiler.Emit(Mov, R(Rax), I(unchecked((long)miss.Pc)));
                compiler.Exit(miss.Count, chain: false);
            }
            foreach (var slow in compiler._slowExits)
            {
                compiler.Label(slow.Label);
                compiler.Emit(Mov, Context(nameof(JitContext.Fallback), 4), I(1));
                compiler.Emit(Mov, R(Rax), I(unchecked((long)slow.Pc)));
                compiler.Exit(slow.Count, chain: false);
            }
            compiler.Label("return");
            compiler.Mxcsr(nameof(JitContext.SavedMxcsr), save: false);
            compiler.Emit(Mov, R(Rax, 4), Context(nameof(JitContext.Executed), 4));
            compiler.Emit(X86Instruction.Ret());
            byte[] bytes = compiler._bytes.ToArray();
            foreach (var branch in compiler._branches)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(branch.Offset), compiler._labels[branch.Label] - branch.Offset - 4);
            return new JitBlock
            {
                Count = compiler._count,
                VectorSpecialized = vectorSpecialized,
                Source = source[..consumed].ToArray(),
                Bytes = bytes,
                ChainOffset = chainOffset,
            };
        }

        private bool Supported(uint instruction)
        {
            int op = (int)(instruction & 127);
            int f3 = (int)((instruction >> 12) & 7);
            uint f7 = instruction >> 25;
            if (ImmediateVectorSet(instruction, out _, out _) || SupportedBitmanip(instruction) || SupportedFloat(instruction) || SupportedVector(instruction) || SupportedAtomic(instruction))
                return true;
            return op switch
            {
                0x37 or 0x17 or 0x6F => true,
                0x67 => f3 == 0,
                0x63 => f3 is 0 or 1 or 4 or 5 or 6 or 7,
                0x03 => f3 <= 6,
                0x23 => f3 <= 3,
                0x13 => f3 switch { 1 => instruction >> 26 == 0, 5 => instruction >> 26 is 0 or 0x10, _ => true },
                0x1B => f3 == 0 || f3 == 1 && f7 == 0 || f3 == 5 && f7 is 0 or 0x20,
                0x33 => f7 is 0 or 1 || f7 == 0x20 && f3 is 0 or 4 or 5 or 6 or 7,
                0x3B => f7 == 0 && f3 is 0 or 1 or 5 || f7 == 0x20 && f3 is 0 or 5 || f7 == 1 && f3 is 0 or 4 or 5 or 6 or 7,
                _ => false,
            };
        }

        private bool Translate(uint instruction, int instructionSize)
        {
            if (ImmediateVectorSet(instruction, out ulong type, out int vlmax))
                return TranslateVectorSet(instruction, instructionSize, type, vlmax);
            if (TranslateBitmanip(instruction) || TranslateFloat(instruction) || TranslateVector(instruction) || TranslateAtomic(instruction))
                return false;
            int op = (int)(instruction & 127);
            int rd = (int)((instruction >> 7) & 31);
            int rs1 = (int)((instruction >> 15) & 31);
            int rs2 = (int)((instruction >> 20) & 31);
            int f3 = (int)((instruction >> 12) & 7);
            uint f7 = instruction >> 25;
            ulong nextPc = _pc + (ulong)instructionSize;
            if (op is 0x37 or 0x17)
            {
                Emit(Mov, R(Rax), I(unchecked((long)(op == 0x17 ? _pc + (ulong)ImmU(instruction) : (ulong)ImmU(instruction)))));
                Store(rd);
            }
            else if (op == 0x6F)
            {
                Emit(Mov, R(Rax), I(unchecked((long)nextPc)));
                Store(rd);
                Exit(_pc + (ulong)ImmJ(instruction), _count + 1);
                return true;
            }
            else if (op == 0x67)
            {
                Load(Rax, rs1);
                Emit(Add, R(Rax), I(ImmI(instruction)));
                Emit(And, R(Rax), I(-2));
                Emit(Mov, R(Rdx), I(unchecked((long)nextPc)));
                Store(rd, Rdx);
                Exit(_count + 1);
                return true;
            }
            else if (op == 0x63)
            {
                Load(Rax, rs1);
                Load(Rdx, rs2);
                Emit(Cmp, R(Rax), R(Rdx));
                Emit(Mov, R(Rax), I(unchecked((long)nextPc)));
                Emit(Mov, R(Rdx), I(unchecked((long)(_pc + (ulong)ImmB(instruction)))));
                X86Condition condition = f3 switch
                {
                    0 => X86Condition.E, 1 => X86Condition.Ne, 4 => X86Condition.L,
                    5 => X86Condition.Ge, 6 => X86Condition.B, _ => X86Condition.Ae,
                };
                Emit(X86Instruction.Cmovcc(condition, R(Rax), R(Rdx)));
                Exit(_count + 1);
                return true;
            }
            else if (op is 0x03 or 0x23)
            {
                bool store = op == 0x23;
                int size = 1 << (f3 & 3);
                Load(Rax, rs1);
                Emit(Add, R(Rax), I(store ? ImmS(instruction) : ImmI(instruction)));
                Memory(size, store ? RVMemoryAccess.Store : RVMemoryAccess.Load);
                if (store)
                {
                    Load(Rdx, rs2);
                    Emit(Mov, M(Rax, 0, size), R(Rdx, size));
                }
                else
                {
                    if (size == 8 || size == 4 && f3 == 6)
                        Emit(Mov, R(Rax, size), M(Rax, 0, size));
                    else if (size == 4)
                        Emit(Movsxd, R(Rax), M(Rax, 0, 4));
                    else
                        Emit(f3 >= 4 ? Movzx : Movsx, R(Rax), M(Rax, 0, size));
                    Store(rd);
                }
            }
            else
            {
                bool word = op is 0x1B or 0x3B;
                bool immediate = op is 0x13 or 0x1B;
                int size = word ? 4 : 8;
                Load(Rax, rs1);
                if (!immediate)
                    Load(Rcx, rs2);
                X86Operand operand = immediate ? I(ImmI(instruction)) : R(Rcx, size);
                if (!immediate && f7 == 1)
                {
                    MultiplyDivide(f3, size);
                }
                else if (!immediate && f7 == 0x20 && f3 is 4 or 6 or 7)
                {
                    Emit(X86Instruction.Unary(Not, R(Rcx)));
                    Emit(f3 == 4 ? Xor : f3 == 6 ? Or : And, R(Rax), R(Rcx));
                }
                else
                {
                    switch (f3)
                    {
                        case 0:
                            Emit(!immediate && f7 == 0x20 ? Sub : Add, R(Rax, size), operand);
                            break;
                        case 1:
                        case 5:
                            X86InstrKind shift = f3 == 1 ? Shl : (instruction & 0x40000000) != 0 ? Sar : Shr;
                            Emit(shift, R(Rax, size), immediate ? I((instruction >> 20) & (word ? 31U : 63U)) : R(Rcx, 1));
                            break;
                        case 2:
                        case 3:
                            Emit(Cmp, R(Rax), operand);
                            Emit(X86Instruction.Setcc(f3 == 2 ? X86Condition.L : X86Condition.B, R(Rax, 1)));
                            Emit(Movzx, R(Rax), R(Rax, 1));
                            break;
                        default:
                            Emit(f3 == 4 ? Xor : f3 == 6 ? Or : And, R(Rax), operand);
                            break;
                    }
                }
                if (word)
                    Emit(Movsxd, R(Rax), R(Rax, 4));
                Store(rd);
            }
            return false;
        }

        private void MultiplyDivide(int function, int size)
        {
            if (function == 0)
            {
                Emit(Imul, R(Rax, size), R(Rcx, size));
                return;
            }
            if (function < 4)
            {
                if (function == 2)
                    Emit(Mov, R(R8), R(Rax));
                Emit(X86Instruction.Unary(function == 1 ? Imul : Mul, R(Rcx)));
                if (function == 2)
                {
                    Emit(Test, R(R8), R(R8));
                    Branch(X86Condition.Ns, $"multiply{_count}");
                    Emit(Sub, R(Rdx), R(Rcx));
                    Label($"multiply{_count}");
                }
                Emit(Mov, R(Rax), R(Rdx));
                return;
            }

            bool signed = (function & 1) == 0;
            bool remainder = function >= 6;
            string zero = $"zero{_count}";
            string divide = $"divide{_count}";
            string done = $"done{_count}";
            Emit(Test, R(Rcx, size), R(Rcx, size));
            Branch(X86Condition.E, zero);
            if (signed)
            {
                Emit(Cmp, R(Rcx, size), I(-1));
                Branch(X86Condition.Ne, divide);
                if (remainder)
                    Emit(Xor, R(Rax, 4), R(Rax, 4));
                else
                    Emit(X86Instruction.Unary(Neg, R(Rax, size)));
                Jump(done);
            }
            Label(divide);
            if (signed)
                Emit(new X86Instruction(size == 8 ? Cqo : Cdq));
            else
                Emit(Xor, R(Rdx, 4), R(Rdx, 4));
            Emit(X86Instruction.Unary(signed ? Idiv : X86InstrKind.Div, R(Rcx, size)));
            if (remainder)
                Emit(Mov, R(Rax, size), R(Rdx, size));
            Jump(done);
            Label(zero);
            if (!remainder)
                Emit(Mov, R(Rax, size), I(-1));
            Label(done);
        }

        private void Memory(int size, RVMemoryAccess access, bool invalidateReservation = true)
        {
            string miss = $"memory{_count}_{_misses.Count}";
            _misses.Add((miss, _pc, _count, size, access));
            if (size != 1)
            {
                Emit(Test, R(Rax, 4), I(size - 1));
                Branch(X86Condition.Ne, miss);
            }
            Emit(Mov, R(Rdx), R(Rax));
            Emit(And, R(Rdx), I(-4096));
            Emit(Mov, R(R8), R(Rax));
            Emit(Shr, R(R8), I(12));
            Emit(And, R(R8, 4), I(JitState.TlbMask));
            Emit(Shl, R(R8), I(4));
            Emit(Add, R(R8), Context(access == RVMemoryAccess.Store ? nameof(JitContext.WriteTlb) : nameof(JitContext.ReadTlb)));
            Emit(Cmp, M(R8), R(Rdx));
            Branch(X86Condition.Ne, miss);
            Emit(Add, R(Rax), M(R8, 8));
            if (access == RVMemoryAccess.Store && invalidateReservation)
                InvalidateReservation();
        }

        private void Exit(ulong pc, int count)
        {
            Emit(Mov, R(Rax), I(unchecked((long)pc)));
            Exit(count);
        }

        private void Exit(int count, bool chain = true)
        {
            Emit(Mov, Context(nameof(JitContext.Pc)), R(Rax));
            if (count != 0)
            {
                Emit(Add, Context(nameof(JitContext.Executed), 4), I(count));
                Emit(Sub, Context(nameof(JitContext.Remaining), 4), I(count));
            }
            if (chain)
            {
                Emit(Mov, R(Rdx), R(Rax));
                Emit(Shr, R(Rdx), I(1));
                Emit(And, R(Rdx, 4), I(JitState.DispatchMask));
                Emit(Shl, R(Rdx), I(5));
                Emit(Add, R(Rdx), Context(nameof(JitContext.Dispatch)));
                Emit(Mov, R(R8), Context(nameof(JitContext.Generation)));
                Emit(Cmp, M(Rdx, 16), R(R8));
                Branch(X86Condition.Ne, "return");
                Emit(Cmp, M(Rdx), R(Rax));
                Branch(X86Condition.Ne, "return");
                Emit(X86Instruction.Branch(Jmp, M(Rdx, 8)));
            }
            else
                Jump("return");
        }

        private string SlowExit()
        {
            string label = $"slow{_count}_{_slowExits.Count}";
            _slowExits.Add((label, _pc, _count));
            return label;
        }

        private void Mxcsr(string field, bool save)
        {
            // Native floating point must not inherit or change the host's floating point environment
            _bytes.AddRange(new byte[] { 0x41, 0x0F, 0xAE, save ? (byte)0x9B : (byte)0x93 });
            int offset = _contextOffsets[field];
            for (int i = 0; i < 4; i++)
                _bytes.Add((byte)(offset >> (8 * i)));
        }

        private void Load(X86Register target, int source)
        {
            if (source == 0)
                Emit(Xor, R(target, 4), R(target, 4));
            else
                Emit(Mov, R(target), M(R10, source * 8));
        }

        private void Store(int target, X86Register source = Rax)
        {
            if (target != 0)
                Emit(Mov, M(R10, target * 8), R(source));
        }

        private static Dictionary<string, int> CreateContextOffsets()
        {
            var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var field in typeof(JitContext).GetFields())
                offsets.Add(field.Name, (int)Marshal.OffsetOf<JitContext>(field.Name));
            return offsets;
        }

        private static X86Operand R(X86Register register, int size = 8) => X86Operand.RegisterOperand(register, size);
        private static X86Operand M(X86Register register, int offset = 0, int size = 8) => X86Operand.Memory(register, offset, size);
        private static X86Operand I(long value) => X86Operand.ImmediateOperand(value);
        private static X86Operand Context(string field, int size = 8) => M(R11, _contextOffsets[field], size);
        private void Label(string name) => _labels.Add(name, _bytes.Count);
        private void Jump(string name)
        {
            _bytes.Add(0xE9);
            BranchOffset(name);
        }
        private void Branch(X86Condition condition, string name)
        {
            _bytes.Add(0x0F);
            _bytes.Add((byte)(0x80 + (int)condition));
            BranchOffset(name);
        }

        private void BranchOffset(string name)
        {
            _branches.Add((_bytes.Count, name));
            _bytes.AddRange(new byte[4]);
        }
        private void Emit(X86InstrKind opcode, X86Operand first, X86Operand second)
        {
            if (!EmitIntegerEncoding(opcode, first, second))
                Emit(X86Instruction.Binary(opcode, first, second));
        }

        private void Emit(X86Instruction instruction)
        {
            X86CodeEncoder.X86Selection selection = X86CodeEncoder.Select(instruction, HostTarget);
            _bytes.AddRange(X86CodeEncoder.EncodeWithForm(selection.Form, instruction, HostTarget));
        }

        private static bool SupportedBitmanip(uint instruction)
        {
            int op = (int)(instruction & 127);
            int function = (int)((instruction >> 12) & 7);
            uint high = instruction >> 25;
            return op switch
            {
                0x13 => TryExecuteBitmanipImmediate(instruction, 0, out _),
                0x33 => TryExecuteBitmanipRegister(instruction, 0, 0, out _) || high == 7 && function is 5 or 7,
                0x1B => function == 1 && (instruction >> 20 is 0x600 or 0x601 or 0x602 || instruction >> 26 == 2)
                    || function == 5 && high == 0x30,
                0x3B => high == 4 && (function == 0 || function == 4 && ((instruction >> 20) & 31) == 0)
                    || high == 0x10 && function is 2 or 4 or 6 || high == 0x30 && function is 1 or 5,
                _ => false,
            };
        }

        private bool TranslateBitmanip(uint instruction)
        {
            if (!SupportedBitmanip(instruction))
                return false;
            int op = (int)(instruction & 127);
            int function = (int)((instruction >> 12) & 7);
            int rd = (int)((instruction >> 7) & 31);
            int rs1 = (int)((instruction >> 15) & 31);
            int rs2 = (int)((instruction >> 20) & 31);
            uint high = instruction >> 25;
            bool word = op is 0x1B or 0x3B;
            bool immediate = op is 0x13 or 0x1B;
            int size = word ? 4 : 8;
            Load(Rax, rs1);
            if (!immediate)
                Load(Rcx, rs2);

            if (immediate && function == 1 && instruction >> 20 is >= 0x600 and <= 0x605)
            {
                int kind = (int)(instruction >> 20) - 0x600;
                if (kind is 0 or 1)
                {
                    Emit(Test, R(Rax, size), R(Rax, size));
                    Branch(X86Condition.E, $"countZero{_count}");
                    Emit(kind == 0 ? Bsr : Bsf, R(Rax, size), R(Rax, size));
                    if (kind == 0)
                        Emit(Xor, R(Rax), I(size * 8 - 1));
                    Jump($"countDone{_count}");
                    Label($"countZero{_count}");
                    Emit(Mov, R(Rax, 4), I(size * 8));
                    Label($"countDone{_count}");
                }
                else if (kind == 2)
                {
                    if (word)
                        Emit(Mov, R(Rax, 4), R(Rax, 4));
                    if (System.Runtime.Intrinsics.X86.Popcnt.X64.IsSupported)
                        _bytes.AddRange(new byte[] { 0xF3, 0x48, 0x0F, 0xB8, 0xC0 });
                    else
                    {
                        Emit(Xor, R(Rdx, 4), R(Rdx, 4));
                        Label($"popcount{_count}");
                        Emit(Test, R(Rax), R(Rax));
                        Branch(X86Condition.E, $"popcountDone{_count}");
                        Emit(Lea, R(Rcx), M(Rax, -1));
                        Emit(And, R(Rax), R(Rcx));
                        Emit(Add, R(Rdx, 4), I(1));
                        Jump($"popcount{_count}");
                        Label($"popcountDone{_count}");
                        Emit(Mov, R(Rax), R(Rdx));
                    }
                }
                else
                    Emit(Movsx, R(Rax), R(Rax, kind == 4 ? 1 : 2));
            }
            else if (immediate && word && function == 1)
            {
                Emit(Mov, R(Rax, 4), R(Rax, 4));
                Emit(Shl, R(Rax), I((instruction >> 20) & 63));
            }
            else if (immediate && instruction >> 20 == 0x6B8)
            {
                _bytes.AddRange(new byte[] { 0x48, 0x0F, 0xC8 });
            }
            else if (immediate && instruction >> 20 == 0x287)
            {
                Emit(Movq, R(Xmm0, 16), R(Rax));
                Emit(Pxor, R(Xmm1, 16), R(Xmm1, 16));
                Emit(Pcmpeqb, R(Xmm0, 16), R(Xmm1, 16));
                Emit(Pcmpeqb, R(Xmm1, 16), R(Xmm1, 16));
                Emit(Pxor, R(Xmm0, 16), R(Xmm1, 16));
                Emit(Movq, R(Rax), R(Xmm0, 16));
            }
            else if (high == 0x30 || immediate && instruction >> 26 == 0x18)
            {
                Emit(function == 1 ? Rol : Ror, R(Rax, size), immediate ? I((instruction >> 20) & (word ? 31U : 63U)) : R(Rcx, 1));
                if (word)
                    Emit(Movsxd, R(Rax), R(Rax, 4));
            }
            else if (!immediate && high == 4)
            {
                if (function == 4)
                    Emit(Movzx, R(Rax), R(Rax, 2));
                else
                {
                    Emit(Mov, R(Rax, 4), R(Rax, 4));
                    Emit(Add, R(Rax), R(Rcx));
                }
            }
            else if (!immediate && high == 0x10)
            {
                if (word)
                    Emit(Mov, R(Rax, 4), R(Rax, 4));
                Emit(Shl, R(Rax), I(function / 2));
                Emit(Add, R(Rax), R(Rcx));
            }
            else if (!immediate && high == 0x20)
            {
                Emit(X86Instruction.Unary(Not, R(Rcx)));
                Emit(function == 4 ? Xor : function == 6 ? Or : And, R(Rax), R(Rcx));
            }
            else if (!immediate && high == 5)
            {
                if (function <= 3)
                    CarrylessMultiply(function);
                else
                {
                    Emit(Cmp, R(Rax), R(Rcx));
                    X86Condition condition = function switch { 4 => X86Condition.G, 5 => X86Condition.A, 6 => X86Condition.L, _ => X86Condition.B };
                    Emit(X86Instruction.Cmovcc(condition, R(Rax), R(Rcx)));
                }
            }
            else if (!immediate && high == 7)
            {
                Emit(Xor, R(Rdx, 4), R(Rdx, 4));
                Emit(Test, R(Rcx), R(Rcx));
                Emit(X86Instruction.Cmovcc(function == 5 ? X86Condition.E : X86Condition.Ne, R(Rax), R(Rdx)));
            }
            else
            {
                if (immediate)
                    Emit(Mov, R(Rcx, 4), I((instruction >> 20) & 63));
                if (function == 5)
                {
                    Emit(Shr, R(Rax), R(Rcx, 1));
                    Emit(And, R(Rax, 4), I(1));
                }
                else
                {
                    Emit(Mov, R(Rdx, 4), I(1));
                    Emit(Shl, R(Rdx), R(Rcx, 1));
                    uint kind = instruction >> 27;
                    if (kind == 9)
                        Emit(X86Instruction.Unary(Not, R(Rdx)));
                    Emit(kind == 9 ? And : kind == 13 ? Xor : Or, R(Rax), R(Rdx));
                }
            }
            Store(rd);
            return true;
        }

        private void CarrylessMultiply(int function)
        {
            Emit(Xor, R(Rdx, 4), R(Rdx, 4));
            Emit(Xor, R(R8, 4), R(R8, 4));
            Emit(Xor, R(R9, 4), R(R9, 4));
            Label($"clmul{_count}");
            Emit(Shr, R(Rcx), I(1));
            Branch(X86Condition.Ae, $"clmulSkip{_count}");
            Emit(Xor, R(Rdx), R(Rax));
            Emit(Xor, R(R8), R(R9));
            Label($"clmulSkip{_count}");
            Emit(Shl, R(Rax), I(1));
            Emit(Adc, R(R9), R(R9));
            Emit(Test, R(Rcx), R(Rcx));
            Branch(X86Condition.Ne, $"clmul{_count}");
            if (function == 2)
            {
                Emit(Shl, R(R8), I(1));
                Emit(Shr, R(Rdx), I(63));
                Emit(Or, R(R8), R(Rdx));
            }
            Emit(Mov, R(Rax), R(function == 1 ? Rdx : R8));
        }

        private static bool SupportedFloat(uint instruction)
        {
            int op = (int)(instruction & 127);
            int function = (int)((instruction >> 12) & 7);
            uint high = instruction >> 25;
            int rs2 = (int)((instruction >> 20) & 31);
            if (op is 0x07 or 0x27)
                return function is 1 or 2 or 3;
            if (op is 0x43 or 0x47 or 0x4B or 0x4F)
                return function is 0 or 7 && (((instruction >> 25) & 3) == 0
                    || ((instruction >> 25) & 3) == 1 && System.Runtime.Intrinsics.X86.Fma.IsSupported);
            if (op != 0x53)
                return false;
            return high switch
            {
                0 or 1 or 4 or 5 or 8 or 9 or 12 or 13 => function is 0 or 7,
                0x10 or 0x11 => function <= 2,
                0x14 or 0x15 => function <= 3,
                0x20 => rs2 == 1 && function is 0 or 7,
                0x21 => rs2 == 0 && function is 0 or 7,
                0x2C or 0x2D => rs2 == 0 && function is 0 or 7,
                0x50 or 0x51 => function is 0 or 1 or 2 or 4 or 5,
                0x60 or 0x61 => rs2 <= 3 && function is 0 or 1 or 7,
                0x68 or 0x69 => rs2 <= 3 && function is 0 or 7,
                0x70 or 0x71 => rs2 == 0 && function <= 1,
                0x78 or 0x79 => rs2 <= 1 && function == 0,
                _ => false,
            };
        }

        private bool TranslateFloat(uint instruction)
        {
            if (!SupportedFloat(instruction))
                return false;
            int op = (int)(instruction & 127);
            int rd = (int)((instruction >> 7) & 31);
            int rs1 = (int)((instruction >> 15) & 31);
            int rs2 = (int)((instruction >> 20) & 31);
            int function = (int)((instruction >> 12) & 7);
            int high = (int)(instruction >> 25);
            if (op is 0x07 or 0x27)
            {
                int width = 1 << function;
                bool store = op == 0x27;
                Load(Rax, rs1);
                Emit(Add, R(Rax), I(store ? ImmS(instruction) : ImmI(instruction)));
                Memory(width, store ? RVMemoryAccess.Store : RVMemoryAccess.Load);
                Emit(Mov, R(R9), Context(nameof(JitContext.Floats)));
                if (store)
                {
                    Emit(Mov, R(Rdx), M(R9, rs2 * 8));
                    Emit(Mov, M(Rax, 0, width), R(Rdx, width));
                }
                else
                {
                    Emit(width == 2 ? Movzx : Mov, R(Rax, width == 8 ? 8 : 4), M(Rax, 0, width));
                    if (width != 8)
                    {
                        Emit(Mov, R(Rdx), I(width == 4 ? -4294967296L : -65536L));
                        Emit(Or, R(Rax), R(Rdx));
                    }
                    Emit(Mov, M(R9, rd * 8), R(Rax));
                }
                return true;
            }

            bool wide = (high & 1) != 0;
            int size = wide ? 8 : 4;
            Emit(Mov, R(R9), Context(nameof(JitContext.Floats)));
            if (op != 0x53)
            {
                RoundingGuard(function);
                LoadFloat(Xmm0, rs1, size, widen: true);
                LoadFloat(Xmm1, rs2, size, widen: true);
                LoadFloat(Xmm2, (int)(instruction >> 27), size, widen: true);
                if (op is 0x4B or 0x4F)
                    NegateFloat(Xmm0);
                if (op is 0x47 or 0x4F)
                    NegateFloat(Xmm2);
                if (wide)
                {
                    var target = new X86Target(64, X86AbiKind.SysV64, X86IsaFlags.Sse | X86IsaFlags.Sse2 | X86IsaFlags.Avx | X86IsaFlags.Fma, TargetEndianness.Little);
                    _bytes.AddRange(X86CodeEncoder.Encode(X86Instruction.AvxTernary(Vfmadd213sd, R(Xmm0, 16), R(Xmm1, 16), R(Xmm2, 16)), target));
                }
                else
                {
                    Emit(Mulsd, R(Xmm0, 16), R(Xmm1, 16));
                    Emit(Addsd, R(Xmm0, 16), R(Xmm2, 16));
                    Emit(Cvtsd2ss, R(Xmm0, 16), R(Xmm0, 16));
                }
                StoreFloat(rd, size);
            }
            else if (high is 0x70 or 0x71)
            {
                Emit(Mov, R(Rax, size), M(R9, rs1 * 8, size));
                if (function == 1)
                    ClassifyFloat(size);
                else if (!wide)
                    Emit(Movsxd, R(Rax), R(Rax, 4));
                Store(rd);
            }
            else if (high is 0x78 or 0x79)
            {
                if (rs2 == 1)
                {
                    double constant = LoadFloatConstant(rs1, wide);
                    ulong bits = wide ? (double.IsNaN(constant) ? CanonicalNaN64 : BitConverter.DoubleToUInt64Bits(constant))
                        : 0xFFFFFFFF00000000UL | (float.IsNaN((float)constant) ? CanonicalNaN32 : BitConverter.SingleToUInt32Bits((float)constant));
                    Emit(Mov, R(Rax), I(unchecked((long)bits)));
                    Emit(Mov, M(R9, rd * 8), R(Rax));
                }
                else
                {
                    Load(Rax, rs1);
                    if (wide)
                        Emit(Mov, M(R9, rd * 8), R(Rax));
                    else
                    {
                        Emit(Movd, R(Xmm0, 16), R(Rax, 4));
                        StoreFloat(rd, 4);
                    }
                }
            }
            else if (high is 0x10 or 0x11)
            {
                Emit(Mov, R(Rax, size), M(R9, rs1 * 8, size));
                Emit(Mov, R(Rdx, size), M(R9, rs2 * 8, size));
                if (function == 1)
                    Emit(X86Instruction.Unary(Not, R(Rdx, size)));
                else if (function == 2)
                    Emit(Xor, R(Rdx, size), R(Rax, size));
                Emit(Shr, R(Rdx, size), I(size * 8 - 1));
                Emit(Shl, R(Rdx, size), I(size * 8 - 1));
                Emit(Shl, R(Rax, size), I(1));
                Emit(Shr, R(Rax, size), I(1));
                Emit(Or, R(Rax, size), R(Rdx, size));
                if (!wide)
                {
                    Emit(Mov, R(Rdx), I(-4294967296L));
                    Emit(Or, R(Rax), R(Rdx));
                }
                Emit(Mov, M(R9, rd * 8), R(Rax));
            }
            else if (high is 0x50 or 0x51)
            {
                LoadFloat(Xmm0, rs1, size);
                LoadFloat(Xmm1, rs2, size);
                Emit(wide ? Ucomisd : Ucomiss, R(Xmm0, 16), R(Xmm1, 16));
                Emit(X86Instruction.Setcc(function == 2 ? X86Condition.E : (function & 3) == 0 ? X86Condition.Be : X86Condition.B, R(Rax, 1)));
                Emit(X86Instruction.Setcc(X86Condition.Np, R(Rdx, 1)));
                Emit(And, R(Rax, 1), R(Rdx, 1));
                Emit(Movzx, R(Rax), R(Rax, 1));
                Store(rd);
            }
            else if (high is 0x14 or 0x15)
            {
                LoadFloat(Xmm0, rs1, size);
                LoadFloat(Xmm1, rs2, size);
                FloatMinMax(function, size);
                StoreFloat(rd, size);
            }
            else if (high is 0x60 or 0x61)
            {
                FloatToInteger(rd, rs1, rs2, size, function);
            }
            else if (high is 0x68 or 0x69)
            {
                RoundingGuard(function);
                Load(Rax, rs1);
                if (rs2 < 2)
                    Emit(rs2 == 0 ? Movsxd : Mov, R(Rax, rs2 == 0 ? 8 : 4), R(Rax, 4));
                if (rs2 == 3)
                {
                    Emit(Test, R(Rax), R(Rax));
                    Branch(X86Condition.Ns, $"convertPositive{_count}");
                    Emit(Mov, R(Rdx), R(Rax));
                    Emit(And, R(Rdx, 4), I(1));
                    Emit(Shr, R(Rax), I(1));
                    Emit(Or, R(Rax), R(Rdx));
                    Emit(wide ? Cvtsi2sd : Cvtsi2ss, R(Xmm0, 16), R(Rax));
                    Emit(wide ? Addsd : Addss, R(Xmm0, 16), R(Xmm0, 16));
                    Jump($"convertDone{_count}");
                    Label($"convertPositive{_count}");
                }
                Emit(wide ? Cvtsi2sd : Cvtsi2ss, R(Xmm0, 16), R(Rax));
                if (rs2 == 3)
                    Label($"convertDone{_count}");
                StoreFloat(rd, size);
            }
            else
            {
                RoundingGuard(function);
                if (high is 0x20 or 0x21)
                {
                    LoadFloat(Xmm0, rs1, wide ? 4 : 8);
                    Emit(wide ? Cvtss2sd : Cvtsd2ss, R(Xmm0, 16), R(Xmm0, 16));
                }
                else
                {
                    LoadFloat(Xmm0, rs1, size, widen: true);
                    if (high is 0x2C or 0x2D)
                        Emit(Sqrtsd, R(Xmm0, 16), R(Xmm0, 16));
                    else
                    {
                        LoadFloat(Xmm1, rs2, size, widen: true);
                        Emit((high & ~1) switch { 0 => Addsd, 4 => Subsd, 8 => Mulsd, _ => Divsd }, R(Xmm0, 16), R(Xmm1, 16));
                    }
                    if (!wide)
                        Emit(Cvtsd2ss, R(Xmm0, 16), R(Xmm0, 16));
                }
                StoreFloat(rd, size);
            }
            return true;
        }

        private void RoundingGuard(int rounding)
        {
            if (rounding == 7)
            {
                Emit(Cmp, Context(nameof(JitContext.Rounding), 4), I(0));
                Branch(X86Condition.Ne, SlowExit());
            }
        }

        private void LoadFloat(X86Register target, int source, int size, bool widen = false)
        {
            Emit(size == 8 ? Movsd : Movss, R(target, 16), M(R9, source * 8, size));
            if (widen && size == 4)
                Emit(Cvtss2sd, R(target, 16), R(target, 16));
        }

        private void StoreFloat(int target, int size)
        {
            Emit(size == 8 ? Ucomisd : Ucomiss, R(Xmm0, 16), R(Xmm0, 16));
            Emit(size == 8 ? Movq : Movd, R(Rax, size), R(Xmm0, 16));
            Emit(Mov, R(Rdx), I(unchecked((long)(size == 8 ? CanonicalNaN64 : CanonicalNaN32))));
            Emit(X86Instruction.Cmovcc(X86Condition.P, R(Rax), R(Rdx)));
            if (size == 4)
            {
                Emit(Mov, R(Rdx), I(-4294967296L));
                Emit(Or, R(Rax), R(Rdx));
            }
            Emit(Mov, M(R9, target * 8), R(Rax));
        }

        private void NegateFloat(X86Register target)
        {
            Emit(Mov, R(Rax), I(long.MinValue));
            Emit(Movq, R(Xmm3, 16), R(Rax));
            Emit(Xorpd, R(target, 16), R(Xmm3, 16));
        }

        private void FloatMinMax(int function, int size)
        {
            X86InstrKind compare = size == 8 ? Ucomisd : Ucomiss;
            Emit(compare, R(Xmm0, 16), R(Xmm0, 16));
            Branch(X86Condition.P, $"minimumLeftNan{_count}");
            Emit(compare, R(Xmm1, 16), R(Xmm1, 16));
            Branch(X86Condition.P, function >= 2 ? $"minimumRight{_count}" : $"minimumDone{_count}");
            Emit(compare, R(Xmm0, 16), R(Xmm1, 16));
            Branch((function & 1) == 0 ? X86Condition.A : X86Condition.B, $"minimumRight{_count}");
            Branch(X86Condition.Ne, $"minimumDone{_count}");
            Emit((function & 1) == 0 ? Orpd : Andpd, R(Xmm0, 16), R(Xmm1, 16));
            Jump($"minimumDone{_count}");
            Label($"minimumLeftNan{_count}");
            if (function >= 2)
                Jump($"minimumDone{_count}");
            Label($"minimumRight{_count}");
            Emit(Movaps, R(Xmm0, 16), R(Xmm1, 16));
            Label($"minimumDone{_count}");
        }

        private void FloatToInteger(int rd, int rs1, int kind, int size, int rounding)
        {
            string slow = SlowExit();
            RoundingGuard(rounding);
            LoadFloat(Xmm0, rs1, size, widen: true);
            Emit(rounding == 1 ? Cvttsd2si : Cvtsd2si, R(Rax), R(Xmm0, 16));
            Emit(Mov, R(Rdx), I(long.MinValue));
            Emit(Cmp, R(Rax), R(Rdx));
            Branch(X86Condition.E, slow);
            if ((kind & 1) != 0)
            {
                Emit(Test, R(Rax), R(Rax));
                Branch(X86Condition.S, slow);
            }
            if (kind < 2)
            {
                Emit(kind == 0 ? Movsxd : Mov, R(Rdx, kind == 0 ? 8 : 4), R(Rax, 4));
                Emit(Cmp, R(Rdx), R(Rax));
                Branch(X86Condition.Ne, slow);
                Emit(Movsxd, R(Rax), R(Rax, 4));
            }
            Store(rd);
        }

        private void ClassifyFloat(int size)
        {
            int mantissa = size == 8 ? 52 : 23;
            int exponent = size == 8 ? 2047 : 255;
            Emit(Mov, R(Rcx), R(Rax));
            Emit(Shr, R(Rcx), I(size * 8 - 1));
            Emit(Mov, R(Rdx), R(Rax));
            Emit(Shr, R(Rdx), I(mantissa));
            Emit(And, R(Rdx, 4), I(exponent));
            Emit(Shl, R(Rax), I(64 - mantissa));
            Emit(Cmp, R(Rdx, 4), I(exponent));
            Branch(X86Condition.E, $"classSpecial{_count}");
            Emit(Test, R(Rdx, 4), R(Rdx, 4));
            Branch(X86Condition.E, $"classTiny{_count}");
            ClassPair(1, 6);
            Jump($"classDone{_count}");
            Label($"classTiny{_count}");
            Emit(Test, R(Rax), R(Rax));
            Branch(X86Condition.E, $"classZero{_count}");
            ClassPair(2, 5);
            Jump($"classDone{_count}");
            Label($"classZero{_count}");
            ClassPair(3, 4);
            Jump($"classDone{_count}");
            Label($"classSpecial{_count}");
            Emit(Test, R(Rax), R(Rax));
            Branch(X86Condition.Ne, $"classNan{_count}");
            ClassPair(0, 7);
            Jump($"classDone{_count}");
            Label($"classNan{_count}");
            Emit(Test, R(Rax), R(Rax));
            Emit(Mov, R(Rax, 4), I(256));
            Emit(Mov, R(Rdx, 4), I(512));
            Emit(X86Instruction.Cmovcc(X86Condition.S, R(Rax), R(Rdx)));
            Label($"classDone{_count}");
        }

        private void ClassPair(int negative, int positive)
        {
            Emit(Test, R(Rcx, 4), R(Rcx, 4));
            Emit(Mov, R(Rax, 4), I(1 << negative));
            Emit(Mov, R(Rdx, 4), I(1 << positive));
            Emit(X86Instruction.Cmovcc(X86Condition.E, R(Rax), R(Rdx)));
        }

        private bool SupportedVector(uint instruction)
        {
            int op = (int)(instruction & 127);
            int form = (int)((instruction >> 12) & 7);
            if (_machine._vsewBytes == 0 || (_machine._vtype & VectorTypeIllegal) != 0)
                return false;
            if (op is 0x07 or 0x27 && form is 0 or 5 or 6 or 7)
                return VectorMemoryShape(instruction, out _, out _, out _, out _);
            if (op != 0x57 || form == 7)
                return false;
            int function = (int)(instruction >> 26);
            bool unmasked = ((instruction >> 25) & 1) != 0;
            if (form == 3 && function == 0x27)
            {
                int count = (int)((instruction >> 15) & 31) + 1;
                return unmasked && count is 1 or 2 or 4 or 8
                    && CheckVectorGroup((int)((instruction >> 7) & 31), count)
                    && CheckVectorGroup((int)((instruction >> 20) & 31), count);
            }
            if (form == 2 && function is >= 0x18 and <= 0x1F)
                return unmasked;
            bool mask = form is 0 or 3 or 4 && function is >= 24 and <= 31;
            int groups = _machine._vgroupRegisters;
            int vd = (int)((instruction >> 7) & 31);
            int vs1 = (int)((instruction >> 15) & 31);
            int vs2 = (int)((instruction >> 20) & 31);
            if (!CheckVectorGroup(vd, mask ? 1 : groups) || !CheckVectorGroup(vs2, groups)
                || form is 0 or 1 or 2 && !CheckVectorGroup(vs1, groups))
                return false;
            if (form is 0 or 3 or 4)
                return function switch
                {
                    0 or 9 or 10 or 11 or 23 or 24 or 25 or 28 or 29 or 37 or 40 or 41 => true,
                    1 or 2 or 4 or 5 or 6 or 7 or 26 or 27 => form != 3,
                    3 or 30 or 31 => form != 0,
                    _ => false,
                };
            if (form is 2 or 6)
                return function is >= 32 and <= 39 or 41 or 43 or 45 or 47;
            return form is 1 or 5 && _machine._vsewBytes is 4 or 8 && function is 0 or 2 or 32 or 36;
        }

        private bool VectorMemoryShape(uint instruction, out int size, out int groups, out int fields, out int indexSize)
        {
            size = VectorMemoryElementBytes((int)((instruction >> 12) & 7));
            groups = 0;
            fields = (int)(instruction >> 29) + 1;
            indexSize = 0;
            if (((instruction >> 28) & 1) != 0 || size == 0)
                return false;
            int addressing = (int)((instruction >> 26) & 3);
            int source = (int)((instruction >> 20) & 31);
            int register = (int)((instruction >> 7) & 31);
            if (addressing == 0 && source != 0 && (source != 16 || (instruction & 127) != 7))
                return false;
            if (addressing is 1 or 3)
            {
                indexSize = size;
                size = _machine._vsewBytes;
                groups = _machine._vgroupRegisters;
                if (!_machine.TryGetVectorMemoryShape(indexSize, out _, out int indexGroups) || !CheckVectorGroup(source, indexGroups))
                    return false;
                if ((instruction & 127) == 7 && register < source + indexGroups && source < register + fields * groups)
                    return false;
            }
            else if (!_machine.TryGetVectorMemoryShape(size, out _, out groups))
                return false;
            return fields * groups <= 8 && CheckVectorGroup(register, groups) && register + fields * groups <= 32;
        }

        private bool TranslateVector(uint instruction)
        {
            if (!SupportedVector(instruction))
                return false;
            int op = (int)(instruction & 127);
            int form = (int)((instruction >> 12) & 7);
            int function = (int)(instruction >> 26);
            int vd = (int)((instruction >> 7) & 31);
            int vs1 = (int)((instruction >> 15) & 31);
            int vs2 = (int)((instruction >> 20) & 31);
            int size = _machine._vsewBytes;
            bool unmasked = ((instruction >> 25) & 1) != 0;
            if (op == 0x57 && form == 3 && function == 0x27)
            {
                Emit(Mov, R(R9), Context(nameof(JitContext.Vectors)));
                for (int offset = 0; offset < (vs1 + 1) * _machine.VectorRegisterBytes; offset += 16)
                {
                    Emit(Movdqu, R(Xmm0, 16), M(R9, vs2 * _machine.VectorRegisterBytes + offset, 16));
                    Emit(Movdqu, M(R9, vd * _machine.VectorRegisterBytes + offset, 16), R(Xmm0, 16));
                }
                Emit(Mov, Context(nameof(JitContext.VectorStart)), I(0));
                Emit(Or, Context(nameof(JitContext.VectorDirty)), I((long)MstatusVsMask));
                return true;
            }
            if (op == 0x57 && form == 2 && function is >= 0x18 and <= 0x1F)
                return TranslateMaskLogic(function, vd, vs1, vs2);
            bool maskDestination = op == 0x57 && form is 0 or 3 or 4 && function is >= 24 and <= 31;
            bool memory = op is 0x07 or 0x27;
            string slow = SlowExit();
            string loop = $"vectorLoop{_count}";
            string next = $"vectorNext{_count}";
            string masked = $"vectorMasked{_count}";
            string finish = $"vectorFinish{_count}";
            Emit(Mov, R(R8), Context(nameof(JitContext.VectorStart)));
            Emit(Cmp, R(R8), Context(nameof(JitContext.VectorLength)));
            Branch(X86Condition.A, slow);
            Emit(Cmp, Context(nameof(JitContext.VectorLength)), I(_machine._vvlmax));
            Branch(X86Condition.A, slow);
            Emit(Mov, R(R9), Context(nameof(JitContext.Vectors)));
            if (memory && unmasked && ((instruction >> 26) & 3) == 0 && vs2 == 0 && (instruction >> 29) == 0)
            {
                VectorMemoryShape(instruction, out int elementSize, out _, out _, out _);
                Label($"vectorMemoryPacked{_count}");
                Emit(Lea, R(Rax), M(R8, 16 / elementSize));
                Emit(Cmp, R(Rax), Context(nameof(JitContext.VectorLength)));
                Branch(X86Condition.A, loop);
                Load(Rax, vs1);
                Emit(Lea, R(Rax), X86Operand.Memory(Rax, 0, 8, R8, elementSize));
                // A TLB entry validates only one backing page
                Emit(Mov, R(Rdx), R(Rax));
                Emit(And, R(Rdx), I(4095));
                Emit(Cmp, R(Rdx), I(4096 - 16));
                Branch(X86Condition.A, loop);
                Emit(Mov, Context(nameof(JitContext.VectorStart)), R(R8));
                Memory(elementSize, op == 0x07 ? RVMemoryAccess.Load : RVMemoryAccess.Store);
                Emit(Mov, R(R8), Context(nameof(JitContext.VectorStart)));
                X86Operand vector = VectorElement(vd, elementSize).WithSize(16);
                Emit(Movdqu, R(Xmm0, 16), op == 0x07 ? M(Rax, 0, 16) : vector);
                Emit(Movdqu, op == 0x07 ? vector : M(Rax, 0, 16), R(Xmm0, 16));
                Emit(Add, R(R8, 4), I(16 / elementSize));
                Jump($"vectorMemoryPacked{_count}");
            }
            if (!memory && unmasked && PackedVectorOperation(function, form, size) is X86InstrKind packed)
            {
                bool scalar = form is 3 or 4 or 6;
                bool shift = form is 3 or 4 && function is 37 or 40 or 41;
                if (scalar)
                {
                    if (form == 3)
                        Emit(Mov, R(Rax), I(shift || vs1 < 16 ? vs1 : vs1 - 32));
                    else
                        Load(Rax, vs1);
                    if (shift)
                    {
                        Emit(And, R(Rax, 4), I(size * 8 - 1));
                        Emit(Movq, R(Xmm1, 16), R(Rax));
                    }
                    else
                    {
                        if (size == 1)
                        {
                            Emit(Movzx, R(Rax, 4), R(Rax, 1));
                            Emit(X86Instruction.Ternary(Imul, R(Rax, 4), R(Rax, 4), I(0x01010101)));
                        }
                        else if (size == 2)
                        {
                            Emit(Movzx, R(Rax, 4), R(Rax, 2));
                            Emit(X86Instruction.Ternary(Imul, R(Rax, 4), R(Rax, 4), I(0x00010001)));
                        }
                        Emit(size == 8 ? Movq : Movd, R(Xmm1, 16), R(Rax, size == 8 ? 8 : 4));
                        Emit(X86Instruction.Ternary(Pshufd, R(Xmm1, 16), R(Xmm1, 16), I(size == 8 ? 0x44 : 0)));
                    }
                }
                Label($"vectorPacked{_count}");
                Emit(Lea, R(Rax), M(R8, 16 / size));
                Emit(Cmp, R(Rax), Context(nameof(JitContext.VectorLength)));
                Branch(X86Condition.A, loop);
                Emit(Movdqu, R(Xmm0, 16), VectorElement(vs2, size).WithSize(16));
                if (!scalar)
                    Emit(Movdqu, R(Xmm1, 16), VectorElement(vs1, size).WithSize(16));
                Emit(packed, R(Xmm0, 16), R(Xmm1, 16));
                Emit(Movdqu, VectorElement(vd, size).WithSize(16), R(Xmm0, 16));
                Emit(Add, R(R8, 4), I(16 / size));
                Jump($"vectorPacked{_count}");
            }
            if (!memory && size == 4 && (maskDestination && unmasked || function == 23 && form == 0 && !unmasked))
                EmitPackedSelect(function, form, vd, vs1, vs2, maskDestination, loop);
            Label(loop);
            Emit(Cmp, R(R8), Context(nameof(JitContext.VectorLength)));
            Branch(X86Condition.Ae, finish);
            if (!unmasked)
                VectorMask(masked);
            if (memory)
            {
                VectorMemoryShape(instruction, out size, out int groups, out int fields, out int indexSize);
                int addressing = (int)((instruction >> 26) & 3);
                for (int field = 0; field < fields; field++)
                {
                    Emit(Mov, Context(nameof(JitContext.VectorStart)), R(R8));
                    if (indexSize != 0)
                        LoadElement(Rax, VectorElement(vs2, indexSize), indexSize);
                    else
                    {
                        Emit(Mov, R(Rax), R(R8));
                        if (addressing == 2)
                        {
                            Load(Rcx, vs2);
                            Emit(Imul, R(Rax), R(Rcx));
                        }
                        else
                            Emit(X86Instruction.Ternary(Imul, R(Rax), R(Rax), I(fields * size)));
                    }
                    Load(Rcx, vs1);
                    Emit(Add, R(Rax), R(Rcx));
                    if (field != 0)
                        Emit(Add, R(Rax), I(field * size));
                    Memory(size, op == 0x07 ? RVMemoryAccess.Load : RVMemoryAccess.Store);
                    Emit(Mov, R(R8), Context(nameof(JitContext.VectorStart)));
                    if (op == 0x07)
                    {
                        LoadElement(Rax, M(Rax, 0, size), size);
                        Emit(Mov, VectorElement(vd + field * groups, size), R(Rax, size));
                    }
                    else
                    {
                        LoadElement(Rdx, VectorElement(vd + field * groups, size), size);
                        Emit(Mov, M(Rax, 0, size), R(Rdx, size));
                    }
                }
            }
            else
            {
                LoadElement(Rax, VectorElement(vs2, size), size);
                if (form is 0 or 1 or 2)
                    LoadElement(Rcx, VectorElement(vs1, size), size);
                else if (form == 3)
                {
                    int value = function is 37 or 40 or 41 ? vs1 : vs1 < 16 ? vs1 : vs1 - 32;
                    Emit(Mov, R(Rcx), I(unchecked((long)((ulong)value & ElementMask(size * 8)))));
                }
                else if (form == 5)
                {
                    Emit(Mov, R(Rdx), Context(nameof(JitContext.Floats)));
                    LoadElement(Rcx, M(Rdx, vs1 * 8, size), size);
                }
                else
                {
                    Load(Rcx, vs1);
                    Truncate(Rcx, size);
                }
                if (form is 1 or 5)
                {
                    Emit(size == 8 ? Movq : Movd, R(Xmm0, 16), R(Rax, size));
                    Emit(size == 8 ? Movq : Movd, R(Xmm1, 16), R(Rcx, size));
                    X86InstrKind operation = function switch
                    {
                        0 => size == 8 ? Addsd : Addss, 2 => size == 8 ? Subsd : Subss,
                        32 => size == 8 ? Divsd : Divss, _ => size == 8 ? Mulsd : Mulss,
                    };
                    Emit(operation, R(Xmm0, 16), R(Xmm1, 16));
                    Emit(size == 8 ? Movq : Movd, R(Rax, size), R(Xmm0, 16));
                }
                else
                    VectorInteger(function, form, size, vd);
                if (maskDestination)
                    StoreVectorMask(vd);
                else
                    Emit(Mov, VectorElement(vd, size), R(Rax, size));
            }
            Jump(next);
            if (!unmasked)
            {
                Label(masked);
                if (!memory && function == 23)
                {
                    LoadElement(Rax, VectorElement(vs2, size), size);
                    Emit(Mov, VectorElement(vd, size), R(Rax, size));
                }
                else if (!maskDestination && op != 0x27 && (_machine._vtype & VectorTypeMaskAgnostic) != 0)
                {
                    int fields = 1;
                    int groups = _machine._vgroupRegisters;
                    if (memory)
                        VectorMemoryShape(instruction, out size, out groups, out fields, out _);
                    for (int field = 0; field < fields; field++)
                        Emit(Mov, VectorElement(vd + field * groups, size), I(-1));
                }
            }
            Label(next);
            Emit(Add, R(R8, 4), I(1));
            Jump(loop);
            Label(finish);
            if (!maskDestination && op != 0x27 && (_machine._vtype & VectorTypeTailAgnostic) != 0)
            {
                int fields = 1;
                int groups = _machine._vgroupRegisters;
                if (memory)
                    VectorMemoryShape(instruction, out size, out groups, out fields, out _);
                Emit(Pcmpeqb, R(Xmm0, 16), R(Xmm0, 16));
                Label($"vectorPackedTail{_count}");
                Emit(Lea, R(Rax), M(R8, 16 / size));
                Emit(Cmp, R(Rax), I(_machine._vvlmax));
                Branch(X86Condition.A, $"vectorTail{_count}");
                for (int field = 0; field < fields; field++)
                    Emit(Movdqu, VectorElement(vd + field * groups, size).WithSize(16), R(Xmm0, 16));
                Emit(Add, R(R8, 4), I(16 / size));
                Jump($"vectorPackedTail{_count}");
                Label($"vectorTail{_count}");
                Emit(Cmp, R(R8, 4), I(_machine._vvlmax));
                Branch(X86Condition.Ae, $"vectorDone{_count}");
                for (int field = 0; field < fields; field++)
                    Emit(Mov, VectorElement(vd + field * groups, size), I(-1));
                Emit(Add, R(R8, 4), I(1));
                Jump($"vectorTail{_count}");
                Label($"vectorDone{_count}");
            }
            Emit(Mov, Context(nameof(JitContext.VectorStart)), I(0));
            Emit(Or, Context(nameof(JitContext.VectorDirty)), I((long)MstatusVsMask));
            return true;
        }

        private void EmitPackedSelect(int function, int form, int vd, int vs1, int vs2, bool compare, string scalarLoop)
        {
            Emit(Test, R(R8, 4), I(3));
            Branch(X86Condition.Ne, scalarLoop);
            if (!compare)
            {
                Emit(Mov, R(Rax), I(0x0000000200000001));
                Emit(Movq, R(Xmm3, 16), R(Rax));
                Emit(Mov, R(Rax), I(0x0000000800000004));
                Emit(Movq, R(Xmm2, 16), R(Rax));
                Emit(Punpcklqdq, R(Xmm3, 16), R(Xmm2, 16));
            }
            else if (form != 0)
            {
                if (form == 3)
                    Emit(Mov, R(Rax), I(vs1 < 16 ? vs1 : vs1 - 32));
                else
                    Load(Rax, vs1);
                Emit(Movd, R(Xmm1, 16), R(Rax, 4));
                Emit(X86Instruction.Ternary(Pshufd, R(Xmm1, 16), R(Xmm1, 16), I(0)));
            }
            Label($"vectorSelect{_count}");
            Emit(Lea, R(Rax), M(R8, 4));
            Emit(Cmp, R(Rax), Context(nameof(JitContext.VectorLength)));
            Branch(X86Condition.A, scalarLoop);
            Emit(Movdqu, R(Xmm0, 16), VectorElement(vs2, 4).WithSize(16));
            if (form == 0)
                Emit(Movdqu, R(Xmm1, 16), VectorElement(vs1, 4).WithSize(16));
            if (compare)
            {
                Emit(Movdqa, R(Xmm2, 16), R(Xmm1, 16));
                if (function is 26 or 28 or 30)
                {
                    Emit(Mov, R(Rax, 4), I(unchecked((int)0x80000000)));
                    Emit(Movd, R(Xmm3, 16), R(Rax, 4));
                    Emit(X86Instruction.Ternary(Pshufd, R(Xmm3, 16), R(Xmm3, 16), I(0)));
                    Emit(Pxor, R(Xmm0, 16), R(Xmm3, 16));
                    Emit(Pxor, R(Xmm2, 16), R(Xmm3, 16));
                }
                if (function is 24 or 25)
                    Emit(Pcmpeqd, R(Xmm0, 16), R(Xmm2, 16));
                else if (function is 26 or 27)
                {
                    Emit(Pcmpgtd, R(Xmm2, 16), R(Xmm0, 16));
                    Emit(Movdqa, R(Xmm0, 16), R(Xmm2, 16));
                }
                else
                    Emit(Pcmpgtd, R(Xmm0, 16), R(Xmm2, 16));
                Emit(Movmskps, R(Rax, 4), R(Xmm0, 16));
                if (function is 25 or 28 or 29)
                    Emit(Xor, R(Rax, 4), I(15));
                Emit(Mov, R(Rcx, 4), R(R8, 4));
                Emit(And, R(Rcx, 4), I(7));
                Emit(Shl, R(Rax, 4), R(Rcx, 1));
                Emit(Mov, R(Rdx, 4), I(15));
                Emit(Shl, R(Rdx, 4), R(Rcx, 1));
                Emit(X86Instruction.Unary(Not, R(Rdx, 4)));
                Emit(Mov, R(Rcx), R(R8));
                Emit(Shr, R(Rcx), I(3));
                X86Operand destination = X86Operand.Memory(R9, vd * _machine.VectorRegisterBytes, 1, Rcx);
                Emit(And, destination, R(Rdx, 1));
                Emit(Or, destination, R(Rax, 1));
            }
            else
            {
                Emit(Mov, R(Rdx), R(R8));
                Emit(Shr, R(Rdx), I(3));
                Emit(Movzx, R(Rax, 4), X86Operand.Memory(R9, 0, 1, Rdx));
                Emit(Mov, R(Rcx, 4), R(R8, 4));
                Emit(And, R(Rcx, 4), I(7));
                Emit(Shr, R(Rax, 4), R(Rcx, 1));
                Emit(Movd, R(Xmm2, 16), R(Rax, 4));
                Emit(X86Instruction.Ternary(Pshufd, R(Xmm2, 16), R(Xmm2, 16), I(0)));
                Emit(Pand, R(Xmm2, 16), R(Xmm3, 16));
                Emit(Pcmpeqd, R(Xmm2, 16), R(Xmm3, 16));
                Emit(Pand, R(Xmm1, 16), R(Xmm2, 16));
                Emit(Pandn, R(Xmm2, 16), R(Xmm0, 16));
                Emit(Por, R(Xmm1, 16), R(Xmm2, 16));
                Emit(Movdqu, VectorElement(vd, 4).WithSize(16), R(Xmm1, 16));
            }
            Emit(Add, R(R8, 4), I(4));
            Jump($"vectorSelect{_count}");
        }

        private static X86InstrKind? PackedVectorOperation(int function, int form, int size)
        {
            if (!System.Runtime.Intrinsics.X86.Sse41.IsSupported && (function is >= 4 and <= 7 || form is 2 or 6 && size == 4))
                return null;
            if (form is 2 or 6)
                return function == 37 ? size switch { 2 => Pmullw, 4 => Pmulld, _ => null } : null;
            if (form is not (0 or 3 or 4))
                return null;
            return function switch
            {
                0 => size switch { 1 => Paddb, 2 => Paddw, 4 => Paddd, _ => Paddq },
                2 => size switch { 1 => Psubb, 2 => Psubw, 4 => Psubd, _ => Psubq },
                4 => size switch { 1 => Pminub, 2 => Pminuw, 4 => Pminud, _ => null },
                5 => size switch { 1 => Pminsb, 2 => Pminsw, 4 => Pminsd, _ => null },
                6 => size switch { 1 => Pmaxub, 2 => Pmaxuw, 4 => Pmaxud, _ => null },
                7 => size switch { 1 => Pmaxsb, 2 => Pmaxsw, 4 => Pmaxsd, _ => null },
                9 => Pand, 10 => Por, 11 => Pxor,
                37 when form is 3 or 4 => size switch { 2 => Psllw, 4 => Pslld, 8 => Psllq, _ => null },
                40 when form is 3 or 4 => size switch { 2 => Psrlw, 4 => Psrld, 8 => Psrlq, _ => null },
                41 when form is 3 or 4 => size switch { 2 => Psraw, 4 => Psrad, _ => null },
                _ => null,
            };
        }

        private bool TranslateMaskLogic(int function, int vd, int vs1, int vs2)
        {
            string slow = SlowExit();
            Emit(Cmp, Context(nameof(JitContext.VectorStart)), I(0));
            Branch(X86Condition.Ne, slow);
            Emit(Cmp, Context(nameof(JitContext.VectorLength)), I(_machine._vvlmax));
            Branch(X86Condition.A, slow);
            Emit(Mov, R(R9), Context(nameof(JitContext.Vectors)));
            Emit(Xor, R(R8, 4), R(R8, 4));
            Label($"maskWord{_count}");
            Emit(Mov, R(Rcx), Context(nameof(JitContext.VectorLength)));
            Emit(Mov, R(Rdx), R(R8));
            Emit(Shl, R(Rdx), I(3));
            Emit(Sub, R(Rcx), R(Rdx));
            Emit(Test, R(Rcx), R(Rcx));
            Branch(X86Condition.E, $"maskDone{_count}");
            X86Operand left = X86Operand.Memory(R9, vs2 * _machine.VectorRegisterBytes, 8, R8);
            X86Operand right = X86Operand.Memory(R9, vs1 * _machine.VectorRegisterBytes, 8, R8);
            X86Operand destination = X86Operand.Memory(R9, vd * _machine.VectorRegisterBytes, 8, R8);
            Emit(Mov, R(Rax), left);
            Emit(Mov, R(Rdx), right);
            if (function is 0x18 or 0x1C)
                Emit(X86Instruction.Unary(Not, R(Rdx)));
            Emit(function is 0x18 or 0x19 or 0x1D ? And : function is 0x1A or 0x1C or 0x1E ? Or : Xor, R(Rax), R(Rdx));
            if (function >= 0x1D)
                Emit(X86Instruction.Unary(Not, R(Rax)));
            Emit(Cmp, R(Rcx), I(64));
            Branch(X86Condition.B, $"maskPartial{_count}");
            Emit(Mov, destination, R(Rax));
            Emit(Add, R(R8, 4), I(8));
            Jump($"maskWord{_count}");
            Label($"maskPartial{_count}");
            Emit(Mov, R(Rdx), I(-1));
            Emit(Shl, R(Rdx), R(Rcx, 1));
            Emit(Mov, R(Rcx), destination);
            Emit(Xor, R(Rcx), R(Rax));
            Emit(And, R(Rcx), R(Rdx));
            Emit(Xor, R(Rax), R(Rcx));
            Emit(Mov, destination, R(Rax));
            Label($"maskDone{_count}");
            Emit(Mov, Context(nameof(JitContext.VectorStart)), I(0));
            Emit(Or, Context(nameof(JitContext.VectorDirty)), I((long)MstatusVsMask));
            return true;
        }

        private X86Operand VectorElement(int register, int size)
            => X86Operand.Memory(R9, register * _machine.VectorRegisterBytes, size, R8, size);

        private void LoadElement(X86Register target, X86Operand source, int size)
            => Emit(size < 4 ? Movzx : Mov, R(target, size == 8 ? 8 : 4), source);

        private void Truncate(X86Register target, int size)
        {
            if (size != 8)
                Emit(size < 4 ? Movzx : Mov, R(target, 4), R(target, size));
        }

        private void SignExtend(X86Register target, int size)
        {
            if (size != 8)
                Emit(size == 4 ? Movsxd : Movsx, R(target), R(target, size));
        }

        private void VectorMask(string masked)
        {
            Emit(Mov, R(Rdx), R(R8));
            Emit(Shr, R(Rdx), I(3));
            Emit(Movzx, R(Rax, 4), X86Operand.Memory(R9, 0, 1, Rdx));
            Emit(Mov, R(Rcx, 4), R(R8, 4));
            Emit(And, R(Rcx, 4), I(7));
            Emit(Shr, R(Rax, 4), R(Rcx, 1));
            Emit(Test, R(Rax, 4), I(1));
            Branch(X86Condition.E, masked);
        }

        private void StoreVectorMask(int register)
        {
            Emit(Mov, R(Rcx, 4), R(R8, 4));
            Emit(And, R(Rcx, 4), I(7));
            Emit(Shl, R(Rax, 4), R(Rcx, 1));
            Emit(Mov, R(Rdx, 4), I(1));
            Emit(Shl, R(Rdx, 4), R(Rcx, 1));
            Emit(X86Instruction.Unary(Not, R(Rdx, 4)));
            Emit(Mov, R(Rcx), R(R8));
            Emit(Shr, R(Rcx), I(3));
            X86Operand destination = X86Operand.Memory(R9, register * _machine.VectorRegisterBytes, 1, Rcx);
            Emit(And, destination, R(Rdx, 1));
            Emit(Or, destination, R(Rax, 1));
        }

        private void VectorInteger(int function, int form, int size, int destination)
        {
            if (form is 2 or 6)
            {
                if (function is 41 or 43 or 45 or 47)
                {
                    LoadElement(Rdx, VectorElement(destination, size), size);
                    if (function is 41 or 43)
                    {
                        Emit(Imul, R(Rcx), R(Rdx));
                        if (function == 41)
                            Emit(Add, R(Rax), R(Rcx));
                        else
                            Emit(Sub, R(Rax), R(Rcx));
                    }
                    else
                    {
                        Emit(Imul, R(Rax), R(Rcx));
                        if (function == 45)
                            Emit(Add, R(Rax), R(Rdx));
                        else
                        {
                            Emit(Sub, R(Rdx), R(Rax));
                            Emit(Mov, R(Rax), R(Rdx));
                        }
                    }
                }
                else if (function == 37)
                    Emit(Imul, R(Rax), R(Rcx));
                else if (function is 36 or 38 or 39)
                {
                    if (size == 8)
                    {
                        Emit(Mov, Context(nameof(JitContext.VectorIndex), 4), R(R8, 4));
                        MultiplyDivide(function == 36 ? 3 : function == 38 ? 2 : 1, 8);
                        Emit(Mov, R(R8, 4), Context(nameof(JitContext.VectorIndex), 4));
                    }
                    else
                    {
                        if (function != 36)
                            SignExtend(Rax, size);
                        if (function == 39)
                            SignExtend(Rcx, size);
                        Emit(Imul, R(Rax), R(Rcx));
                        Emit(Shr, R(Rax), I(size * 8));
                    }
                }
                else
                {
                    if ((function & 1) != 0)
                    {
                        SignExtend(Rax, size);
                        SignExtend(Rcx, size);
                    }
                    MultiplyDivide(function switch { 32 => 5, 33 => 4, 34 => 7, _ => 6 }, 8);
                }
                return;
            }
            switch (function)
            {
                case 0: Emit(Add, R(Rax), R(Rcx)); break;
                case 1:
                    Emit(X86Instruction.Unary(Not, R(Rcx)));
                    Emit(And, R(Rax), R(Rcx));
                    break;
                case 2: Emit(Sub, R(Rax), R(Rcx)); break;
                case 3:
                    Emit(Sub, R(Rcx), R(Rax));
                    Emit(Mov, R(Rax), R(Rcx));
                    break;
                case 4: case 5: case 6: case 7:
                    if ((function & 1) != 0)
                    {
                        SignExtend(Rax, size);
                        SignExtend(Rcx, size);
                    }
                    Emit(Cmp, R(Rax), R(Rcx));
                    Emit(X86Instruction.Cmovcc(function switch { 4 => X86Condition.A, 5 => X86Condition.G, 6 => X86Condition.B, _ => X86Condition.L }, R(Rax), R(Rcx)));
                    break;
                case 9: Emit(And, R(Rax), R(Rcx)); break;
                case 10: Emit(Or, R(Rax), R(Rcx)); break;
                case 11: Emit(Xor, R(Rax), R(Rcx)); break;
                case 23: Emit(Mov, R(Rax), R(Rcx)); break;
                case >= 24 and <= 31:
                    if (function is 27 or 29 or 31)
                    {
                        SignExtend(Rax, size);
                        SignExtend(Rcx, size);
                    }
                    Emit(Cmp, R(Rax), R(Rcx));
                    X86Condition condition = function switch
                    {
                        24 => X86Condition.E, 25 => X86Condition.Ne, 26 => X86Condition.B, 27 => X86Condition.L,
                        28 => X86Condition.Be, 29 => X86Condition.Le, 30 => X86Condition.A, _ => X86Condition.G,
                    };
                    Emit(X86Instruction.Setcc(condition, R(Rax, 1)));
                    Emit(Movzx, R(Rax), R(Rax, 1));
                    break;
                case 37: case 40: case 41:
                    Emit(And, R(Rcx, 4), I(size * 8 - 1));
                    if (function == 41)
                        SignExtend(Rax, size);
                    Emit(function == 37 ? Shl : function == 40 ? Shr : Sar, R(Rax), R(Rcx, 1));
                    break;
            }
        }

        private static bool SupportedAtomic(uint instruction)
        {
            int op = (int)(instruction & 127);
            int form = (int)((instruction >> 12) & 7);
            return op == 0x0F && form == 0 || op == 0x2F && form is 2 or 3
                && instruction >> 27 is 0 or 1 or 2 or 3 or 4 or 5 or 8 or 12 or 16 or 20 or 24 or 28;
        }

        private bool TranslateAtomic(uint instruction)
        {
            if (!SupportedAtomic(instruction))
                return false;
            if ((instruction & 127) == 0x0F)
            {
                Emit(new X86Instruction(Mfence));
                return true;
            }
            // RAM has a single owner while Run is active
            int rd = (int)((instruction >> 7) & 31);
            int rs1 = (int)((instruction >> 15) & 31);
            int rs2 = (int)((instruction >> 20) & 31);
            int size = ((instruction >> 12) & 7) == 2 ? 4 : 8;
            int function = (int)(instruction >> 27);
            if ((instruction & (3 << 25)) != 0)
                Emit(new X86Instruction(Mfence));
            if (function == 5)
            {
                Load(Rax, rs1);
                Memory(size, RVMemoryAccess.Store, invalidateReservation: false);
            }
            Load(Rax, rs1);
            Memory(size, RVMemoryAccess.Load);
            LoadElement(R9, M(Rax, 0, size), size);
            if (function == 2)
            {
                Load(Rcx, rs1);
                Emit(Mov, Context(nameof(JitContext.ReservationAddress)), R(Rcx));
                Emit(Mov, Context(nameof(JitContext.HasReservation), 4), I(1));
            }
            else if (function == 3)
            {
                Emit(Cmp, Context(nameof(JitContext.HasReservation), 4), I(0));
                Branch(X86Condition.E, $"scFail{_count}");
                Load(Rax, rs1);
                Emit(Cmp, R(Rax), Context(nameof(JitContext.ReservationAddress)));
                Branch(X86Condition.Ne, $"scFail{_count}");
                Memory(size, RVMemoryAccess.Store, invalidateReservation: false);
                Load(Rcx, rs2);
                Emit(Mov, M(Rax, 0, size), R(Rcx, size));
                Emit(Xor, R(R9, 4), R(R9, 4));
                Jump($"scDone{_count}");
                Label($"scFail{_count}");
                Emit(Mov, R(R9, 4), I(1));
                Label($"scDone{_count}");
                Emit(Mov, Context(nameof(JitContext.HasReservation), 4), I(0));
            }
            else
            {
                Load(Rax, rs1);
                Memory(size, RVMemoryAccess.Store, invalidateReservation: false);
                Load(Rcx, rs2);
                if (function == 5)
                {
                    Load(Rdx, rd);
                    Emit(Cmp, R(R9, size), R(Rdx, size));
                    Branch(X86Condition.Ne, $"amoDone{_count}");
                }
                else if (function is 0 or 4 or 8 or 12)
                    Emit(function switch { 0 => Add, 4 => Xor, 8 => Or, _ => And }, R(Rcx, size), R(R9, size));
                else if (function >= 16)
                {
                    Emit(Cmp, R(R9, size), R(Rcx, size));
                    Emit(X86Instruction.Cmovcc(function switch { 16 => X86Condition.L, 20 => X86Condition.G, 24 => X86Condition.B, _ => X86Condition.A }, R(Rcx, size), R(R9, size)));
                }
                InvalidateReservation();
                Emit(Mov, M(Rax, 0, size), R(Rcx, size));
                if (function == 5)
                    Label($"amoDone{_count}");
            }
            Emit(size == 4 ? Movsxd : Mov, R(Rax), R(R9, size));
            Store(rd);
            if ((instruction & (3 << 25)) != 0)
                Emit(new X86Instruction(Mfence));
            return true;
        }

        private void InvalidateReservation()
        {
            string done = $"reservation{_bytes.Count}";
            Emit(Cmp, Context(nameof(JitContext.HasReservation), 4), I(0));
            Branch(X86Condition.E, done);
            Emit(Mov, R(Rdx), R(Rax));
            Emit(Sub, R(Rdx), Context(nameof(JitContext.RamOffset)));
            Emit(Cmp, R(Rax), Context(nameof(JitContext.FramebufferStart)));
            Branch(X86Condition.B, done + "compare");
            Emit(Cmp, R(Rax), Context(nameof(JitContext.FramebufferEnd)));
            Branch(X86Condition.Ae, done + "compare");
            Emit(Mov, R(Rdx), R(Rax));
            Emit(Sub, R(Rdx), Context(nameof(JitContext.FramebufferOffset)));
            Label(done + "compare");
            Emit(Cmp, R(Rdx), Context(nameof(JitContext.ReservationAddress)));
            Branch(X86Condition.Ne, done);
            Emit(Mov, Context(nameof(JitContext.HasReservation), 4), I(0));
            Label(done);
        }

        private bool EmitIntegerEncoding(X86InstrKind opcode, X86Operand destination, X86Operand source)
        {
            int arithmetic = opcode switch { Add => 0, Or => 1, Adc => 2, Sbb => 3, And => 4, Sub => 5, Xor => 6, Cmp => 7, _ => -1 };
            if (arithmetic < 0 && opcode is not (Mov or Test))
                return false;
            int size = destination.Size;
            if (size is not (1 or 2 or 4 or 8) || !IntegerOperand(destination)
                || source.Kind != X86OperandKind.Immediate && (!IntegerOperand(source) || source.Size != size))
                return false;
            if (source.Kind == X86OperandKind.Immediate)
            {
                long value = source.Immediate;
                if (opcode == Mov && destination.Kind == X86OperandKind.Register)
                {
                    if (size == 8 && value >= 0 && value <= uint.MaxValue)
                        size = 4;
                    int register = (int)destination.Register;
                    IntegerPrefix(size, register >= 8 ? 1 : 0, size == 1 && register >= 4);
                    _bytes.Add((byte)((size == 1 ? 0xB0 : 0xB8) + (register & 7)));
                    IntegerImmediate(value, size);
                    return true;
                }
                if (size == 8 && (value < int.MinValue || value > int.MaxValue))
                    return false;
                bool shortImmediate = arithmetic >= 0 && size != 1 && value is >= -128 and <= 127;
                byte code = opcode == Mov ? (byte)(size == 1 ? 0xC6 : 0xC7)
                    : opcode == Test ? (byte)(size == 1 ? 0xF6 : 0xF7)
                    : (byte)(size == 1 ? 0x80 : shortImmediate ? 0x83 : 0x81);
                IntegerModRm(code, size, arithmetic >= 0 ? arithmetic : 0, destination, registerField: false);
                IntegerImmediate(value, size == 1 || shortImmediate ? 1 : size == 2 ? 2 : 4);
                return true;
            }
            if (destination.Kind == X86OperandKind.Memory && source.Kind == X86OperandKind.Memory)
                return false;
            bool reverse = source.Kind == X86OperandKind.Memory;
            X86Operand memory = reverse ? source : destination;
            int reg = (int)(reverse ? destination.Register : source.Register);
            byte operation = opcode == Mov ? (byte)(reverse ? 0x8A : 0x88)
                : opcode == Test ? (byte)0x84 : (byte)(arithmetic * 8 + (reverse ? 2 : 0));
            if (size != 1)
                operation++;
            IntegerModRm(operation, size, reg, memory, registerField: true);
            return true;
        }

        private static bool IntegerOperand(X86Operand operand)
            => operand.Kind == X86OperandKind.Register ? (int)operand.Register <= 15
                : operand.Kind == X86OperandKind.Memory && (int)operand.BaseRegister <= 15
                    && (operand.IndexRegister == X86Register.Invalid || (int)operand.IndexRegister <= 15)
                    && operand.Symbol is null && !operand.IsRipRelative && operand.Displacement is >= int.MinValue and <= int.MaxValue;

        private void IntegerPrefix(int size, int extension, bool byteRegister)
        {
            if (size == 2)
                _bytes.Add(0x66);
            int rex = 0x40 | (size == 8 ? 8 : 0) | extension;
            if (rex != 0x40 || byteRegister)
                _bytes.Add((byte)rex);
        }

        private void IntegerModRm(byte opcode, int size, int register, X86Operand operand, bool registerField)
        {
            bool direct = operand.Kind == X86OperandKind.Register;
            int basis = (int)(direct ? operand.Register : operand.BaseRegister);
            int index = !direct && operand.IndexRegister != X86Register.Invalid ? (int)operand.IndexRegister : -1;
            int extension = (registerField && register >= 8 ? 4 : 0) | (index >= 8 ? 2 : 0) | (basis >= 8 ? 1 : 0);
            IntegerPrefix(size, extension, size == 1 && (registerField && register >= 4 || direct && basis >= 4));
            _bytes.Add(opcode);
            if (direct)
            {
                _bytes.Add((byte)(0xC0 | ((register & 7) << 3) | (basis & 7)));
                return;
            }
            long displacement = operand.Displacement;
            int mode = displacement == 0 && (basis & 7) != 5 ? 0 : displacement is >= -128 and <= 127 ? 1 : 2;
            bool sib = index >= 0 || (basis & 7) == 4;
            _bytes.Add((byte)((mode << 6) | ((register & 7) << 3) | (sib ? 4 : basis & 7)));
            if (sib)
            {
                int scale = operand.Scale switch { 1 => 0, 2 => 1, 4 => 2, _ => 3 };
                _bytes.Add((byte)((scale << 6) | ((index < 0 ? 4 : index & 7) << 3) | (basis & 7)));
            }
            if (mode != 0)
                IntegerImmediate(displacement, mode == 1 ? 1 : 4);
        }

        private void IntegerImmediate(long value, int size)
        {
            for (int i = 0; i < size; i++)
                _bytes.Add((byte)(value >> (8 * i)));
        }

        private bool ImmediateVectorSet(uint instruction, out ulong type, out int vlmax)
        {
            type = 0;
            vlmax = 0;
            if ((instruction & 0x707F) != 0x7057 || (instruction & 0xC0000000U) == 0x80000000U)
                return false;
            type = (instruction >> 20) & ((instruction >> 31) != 0 ? 0x3FFUL : 0x7FFUL);
            if (!_machine.TryDecodeVectorType(type, out _, out _, out _, out vlmax))
                type = VectorTypeIllegal;
            return true;
        }

        private bool TranslateVectorSet(uint instruction, int size, ulong type, int vlmax)
        {
            int rd = (int)((instruction >> 7) & 31);
            int source = (int)((instruction >> 15) & 31);
            if (type == VectorTypeIllegal)
                Emit(Xor, R(Rax, 4), R(Rax, 4));
            else
            {
                if ((instruction >> 31) != 0)
                    Emit(Mov, R(Rax, 4), I(source));
                else if (source != 0)
                    Load(Rax, source);
                else if (rd == 0)
                    Emit(Mov, R(Rax), Context(nameof(JitContext.VectorLength)));
                else
                    Emit(Mov, R(Rax, 4), I(vlmax));
                Emit(Mov, R(Rcx, 4), I(vlmax));
                Emit(Cmp, R(Rax), R(Rcx));
                Emit(X86Instruction.Cmovcc(X86Condition.A, R(Rax), R(Rcx)));
            }
            Store(rd);
            Emit(Mov, Context(nameof(JitContext.VectorLength)), R(Rax));
            Emit(Mov, Context(nameof(JitContext.VectorStart)), I(0));
            Emit(Or, Context(nameof(JitContext.VectorDirty)), I((long)MstatusVsMask));
            if (type == _machine._vtype)
                return false;
            Emit(Mov, R(Rax), I(unchecked((long)type)));
            Emit(Mov, Context(nameof(JitContext.VectorType)), R(Rax));
            Emit(Mov, R(Rax), I(unchecked((long)(_pc + (ulong)size))));
            Exit(_count + 1, chain: false);
            return true;
        }
    }
}
