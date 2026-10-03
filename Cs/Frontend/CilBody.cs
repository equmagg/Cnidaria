using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    [Flags]
    internal enum CilPrefix : byte
    {
        None = 0,
        Constrained = 1 << 0,
        Unaligned = 1 << 1,
        Volatile = 1 << 2,
        Tail = 1 << 3,
        Readonly = 1 << 4,
        No = 1 << 5,
    }
    internal readonly struct CilInstruction
    {
        public readonly ILOpCode Op;
        public readonly CilPrefix Prefixes;
        public readonly int Offset;
        public readonly long Operand;
        public readonly int ConstrainedToken;

        public CilInstruction(ILOpCode op, CilPrefix prefixes, int offset, long operand, int constrainedToken)
        {
            Op = op;
            Prefixes = prefixes;
            Offset = offset;
            Operand = operand;
            ConstrainedToken = constrainedToken;
        }

        public int Int32 => (int)Operand;
        public int Token => (int)Operand;
        public int TargetPc => (int)Operand;
        public float Single => BitConverter.Int32BitsToSingle((int)Operand);
        public double Double => BitConverter.Int64BitsToDouble(Operand);
        public override string ToString() => $"IL_{Offset:X4}: {Op} {Operand}";
    }
    internal enum CilExceptionClauseKind : byte
    {
        Catch,
        Filter,
        Finally,
        Fault,
    }
    internal readonly struct CilExceptionClause
    {
        public readonly CilExceptionClauseKind Kind;
        public readonly int TryStartPc;
        public readonly int TryEndPc;
        public readonly int HandlerStartPc;
        public readonly int HandlerEndPc;
        public readonly int CatchTypeToken;
        public readonly int FilterStartPc;

        public CilExceptionClause(CilExceptionClauseKind kind, int tryStartPc, int tryEndPc, int handlerStartPc, int handlerEndPc, int catchTypeToken, int filterStartPc)
        {
            Kind = kind;
            TryStartPc = tryStartPc;
            TryEndPc = tryEndPc;
            HandlerStartPc = handlerStartPc;
            HandlerEndPc = handlerEndPc;
            CatchTypeToken = catchTypeToken;
            FilterStartPc = filterStartPc;
        }
    }
    // Branch and clause operands are instruction indexes. Short and macro opcode forms are expanded,
    // and prefixes are folded into the instruction they apply to.
    internal sealed class CilMethodBody
    {
        public ImmutableArray<CilInstruction> Instructions { get; }
        public ImmutableArray<int> SwitchTargets { get; }
        public ImmutableArray<CilExceptionClause> ExceptionClauses { get; }
        public int MaxStack { get; }
        public bool InitLocals { get; }
        public int LocalSignatureToken { get; }

        public CilMethodBody(
            ImmutableArray<CilInstruction> instructions,
            ImmutableArray<int> switchTargets,
            ImmutableArray<CilExceptionClause> exceptionClauses,
            int maxStack,
            bool initLocals,
            int localSignatureToken)
        {
            Instructions = instructions;
            SwitchTargets = switchTargets;
            ExceptionClauses = exceptionClauses;
            MaxStack = maxStack;
            InitLocals = initLocals;
            LocalSignatureToken = localSignatureToken;
        }

        public ReadOnlySpan<int> GetSwitchTargets(in CilInstruction instruction)
            => SwitchTargets.AsSpan().Slice((int)instruction.Operand, (int)(instruction.Operand >> 32));

        private (short[] Pops, short[] Pushes)? _stackEffects;

        // Ret pops one value exactly when the method returns one, so the caller supplies that fact.
        public (short[] Pops, short[] Pushes) GetStackEffects(EcmaMetadata md, bool returnsValue)
        {
            if (_stackEffects is { } cached)
                return cached;
            var pops = new short[Instructions.Length];
            var pushes = new short[Instructions.Length];
            for (int i = 0; i < Instructions.Length; i++)
            {
                var (pop, push) = CilStackEffects.Of(md, Instructions[i], returnsValue);
                pops[i] = (short)pop;
                pushes[i] = (short)push;
            }
            _stackEffects = (pops, pushes);
            return (pops, pushes);
        }
    }
    internal readonly struct CilCallShape
    {
        public readonly int ParameterCount;
        public readonly bool HasThis;
        public readonly bool ReturnsValue;

        public CilCallShape(int parameterCount, bool hasThis, bool returnsValue)
        {
            ParameterCount = parameterCount;
            HasThis = hasThis;
            ReturnsValue = returnsValue;
        }
    }
    internal static class CilStackEffects
    {
        public static CilCallShape GetCallShape(EcmaMetadata md, int token)
        {
            int rid = MetadataToken.Rid(token);
            int signature = MetadataToken.Table(token) switch
            {
                MetadataToken.MethodDef => md.GetMethodDef(rid).Signature,
                MetadataToken.MemberRef => md.GetMemberRef(rid).Signature,
                MetadataToken.MethodSpec => -1,
                MetadataToken.StandAloneSig => md.GetStandAloneSig(rid).Signature,
                _ => throw new BadImageFormatException($"Token 0x{token:X8} does not name a method signature."),
            };
            if (signature < 0)
                return GetCallShape(md, md.GetMethodSpec(rid).Method);

            var reader = new SigReader(md.GetBlob(signature));
            byte callingConvention = reader.ReadByte();
            if ((callingConvention & 0x10) != 0)
                _ = reader.ReadCompressedUInt();
            int parameterCount = checked((int)reader.ReadCompressedUInt());
            while (reader.PeekByte() is (byte)SigElementType.CMOD_REQD or (byte)SigElementType.CMOD_OPT)
            {
                _ = reader.ReadByte();
                _ = reader.ReadCompressedUInt();
            }
            bool returnsValue = reader.PeekByte() != (byte)SigElementType.VOID;
            return new CilCallShape(parameterCount, (callingConvention & 0x20) != 0, returnsValue);
        }

        public static int GetLocalCount(EcmaMetadata md, int localSignatureToken)
        {
            if (localSignatureToken == 0)
                return 0;
            var reader = new SigReader(md.GetBlob(md.GetStandAloneSig(MetadataToken.Rid(localSignatureToken)).Signature));
            if (reader.ReadByte() != 0x07)
                throw new BadImageFormatException($"Token 0x{localSignatureToken:X8} is not a local variable signature.");
            return checked((int)reader.ReadCompressedUInt());
        }

        public static (int Pop, int Push) Of(EcmaMetadata md, in CilInstruction instruction, bool methodReturnsValue)
        {
            switch (instruction.Op)
            {
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                    {
                        var shape = GetCallShape(md, instruction.Token);
                        return (shape.ParameterCount + (shape.HasThis ? 1 : 0), shape.ReturnsValue ? 1 : 0);
                    }
                case ILOpCode.Newobj:
                    return (GetCallShape(md, instruction.Token).ParameterCount, 1);
                case ILOpCode.Calli:
                    {
                        var shape = GetCallShape(md, instruction.Token);
                        return (shape.ParameterCount + (shape.HasThis ? 1 : 0) + 1, shape.ReturnsValue ? 1 : 0);
                    }
                case ILOpCode.Ret:
                    return (methodReturnsValue ? 1 : 0, 0);
            }
            return instruction.Op switch
            {
                ILOpCode.Nop or ILOpCode.Break or ILOpCode.Br or ILOpCode.Leave or ILOpCode.Rethrow or
                ILOpCode.Endfinally or ILOpCode.Jmp => (0, 0),
                ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Ldnull or
                ILOpCode.Ldc_I4 or ILOpCode.Ldc_I8 or ILOpCode.Ldc_R4 or ILOpCode.Ldc_R8 or ILOpCode.Ldstr or
                ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Sizeof or ILOpCode.Ldsfld or ILOpCode.Ldsflda or
                ILOpCode.Arglist => (0, 1),
                ILOpCode.Starg or ILOpCode.Stloc or ILOpCode.Pop or ILOpCode.Stsfld or ILOpCode.Brtrue or
                ILOpCode.Brfalse or ILOpCode.Switch or ILOpCode.Throw or ILOpCode.Initobj or ILOpCode.Endfilter => (1, 0),
                ILOpCode.Dup => (1, 2),
                ILOpCode.Add or ILOpCode.Add_Ovf or ILOpCode.Add_Ovf_Un or ILOpCode.Sub or ILOpCode.Sub_Ovf or
                ILOpCode.Sub_Ovf_Un or ILOpCode.Mul or ILOpCode.Mul_Ovf or ILOpCode.Mul_Ovf_Un or ILOpCode.Div or
                ILOpCode.Div_Un or ILOpCode.Rem or ILOpCode.Rem_Un or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
                ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_Un or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_Un or
                ILOpCode.Clt or ILOpCode.Clt_Un => (2, 1),
                ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_Un or
                ILOpCode.Bge_Un or ILOpCode.Bgt_Un or ILOpCode.Ble_Un or ILOpCode.Blt_Un => (2, 0),
                ILOpCode.Stind_Ref or ILOpCode.Stind_I1 or ILOpCode.Stind_I2 or ILOpCode.Stind_I4 or ILOpCode.Stind_I8 or
                ILOpCode.Stind_R4 or ILOpCode.Stind_R8 or ILOpCode.Stind_I or ILOpCode.Stobj or ILOpCode.Stfld or
                ILOpCode.Cpobj => (2, 0),
                ILOpCode.Ldelem or ILOpCode.Ldelema or ILOpCode.Ldelem_I1 or ILOpCode.Ldelem_U1 or ILOpCode.Ldelem_I2 or
                ILOpCode.Ldelem_U2 or ILOpCode.Ldelem_I4 or ILOpCode.Ldelem_U4 or ILOpCode.Ldelem_I8 or
                ILOpCode.Ldelem_I or ILOpCode.Ldelem_R4 or ILOpCode.Ldelem_R8 or ILOpCode.Ldelem_Ref => (2, 1),
                ILOpCode.Stelem or ILOpCode.Stelem_I or ILOpCode.Stelem_I1 or ILOpCode.Stelem_I2 or ILOpCode.Stelem_I4 or
                ILOpCode.Stelem_I8 or ILOpCode.Stelem_R4 or ILOpCode.Stelem_R8 or ILOpCode.Stelem_Ref or
                ILOpCode.Cpblk or ILOpCode.Initblk => (3, 0),
                _ => (1, 1),
            };
        }
    }
    internal static class CilBodyReader
    {
        private const byte TinyFormat = 0x2;
        private const byte FatFormat = 0x3;
        private const ushort InitLocalsFlag = 0x10;
        private const ushort MoreSectsFlag = 0x8;
        private const byte EhTableSection = 0x1;
        private const byte FatSection = 0x40;
        private const byte MoreSectsSection = 0x80;

        public static CilMethodBody Read(EcmaMetadata md, int rva)
        {
            byte first = md.GetRvaData(rva, 1)[0];
            int headerSize;
            int codeSize;
            int maxStack;
            int localSig;
            bool initLocals;
            bool moreSects;
            if ((first & 0x3) == TinyFormat)
            {
                headerSize = 1;
                codeSize = first >> 2;
                maxStack = 8;
                localSig = 0;
                initLocals = false;
                moreSects = false;
            }
            else if ((first & 0x3) == FatFormat)
            {
                var header = md.GetRvaData(rva, 12);
                ushort flags = (ushort)(header[0] | header[1] << 8);
                headerSize = (flags >> 12) * 4;
                maxStack = header[2] | header[3] << 8;
                codeSize = BitConverter.ToInt32(header.Slice(4));
                localSig = BitConverter.ToInt32(header.Slice(8));
                initLocals = (flags & InitLocalsFlag) != 0;
                moreSects = (flags & MoreSectsFlag) != 0;
            }
            else
            {
                throw new BadImageFormatException($"Invalid method body header 0x{first:X2} at RVA 0x{rva:X}.");
            }

            var code = md.GetRvaData(rva + headerSize, codeSize);
            var offsetToPc = new Dictionary<int, int>();
            var raw = new List<(ILOpCode Op, CilPrefix Prefixes, int Offset, long Operand, int Constrained, int OperandOffset, int Next)>();
            var switchTargetOffsets = new List<int>();
            int pos = 0;
            while (pos < code.Length)
            {
                int start = pos;
                CilPrefix prefixes = CilPrefix.None;
                int constrained = 0;
                ILOpCode op;
                for (; ; )
                {
                    if (!ILOpCodes.TryDecode(code, pos, out op))
                        throw new BadImageFormatException($"Invalid IL opcode at offset 0x{pos:X} (RVA 0x{rva:X}).");
                    pos += ILOpCodes.OpCodeSize(op);
                    CilPrefix prefix = op switch
                    {
                        ILOpCode.Constrained => CilPrefix.Constrained,
                        ILOpCode.Unaligned => CilPrefix.Unaligned,
                        ILOpCode.Volatile => CilPrefix.Volatile,
                        ILOpCode.Tail => CilPrefix.Tail,
                        ILOpCode.Readonly => CilPrefix.Readonly,
                        ILOpCode.No => CilPrefix.No,
                        _ => CilPrefix.None,
                    };
                    if (prefix == CilPrefix.None)
                        break;
                    prefixes |= prefix;
                    if (op == ILOpCode.Constrained)
                        constrained = BitConverter.ToInt32(code.Slice(pos));
                    pos += ILOpCodes.OperandSize(ILOpCodes.OperandKind(op));
                }

                var kind = ILOpCodes.OperandKind(op);
                int operandOffset = pos;
                long operand = kind switch
                {
                    ILOperandKind.None => 0,
                    ILOperandKind.Int8 or ILOperandKind.ShortBranch => (sbyte)code[pos],
                    ILOperandKind.UInt8 => code[pos],
                    ILOperandKind.UInt16 => BitConverter.ToUInt16(code.Slice(pos)),
                    ILOperandKind.Int32 or ILOperandKind.Float32 or ILOperandKind.Branch or ILOperandKind.Token => BitConverter.ToInt32(code.Slice(pos)),
                    ILOperandKind.Int64 or ILOperandKind.Float64 => BitConverter.ToInt64(code.Slice(pos)),
                    ILOperandKind.Switch => BitConverter.ToUInt32(code.Slice(pos)),
                    _ => throw new BadImageFormatException($"Unknown operand kind for {op}."),
                };
                pos += kind == ILOperandKind.Switch ? 4 + checked((int)operand * 4) : ILOpCodes.OperandSize(kind);
                if (kind == ILOperandKind.Switch)
                {
                    int count = (int)operand;
                    long first32 = switchTargetOffsets.Count;
                    for (int i = 0; i < count; i++)
                        switchTargetOffsets.Add(pos + BitConverter.ToInt32(code.Slice(operandOffset + 4 + i * 4)));
                    operand = first32 | ((long)count << 32);
                }
                else if (kind is ILOperandKind.ShortBranch or ILOperandKind.Branch)
                {
                    operand += pos;
                }

                offsetToPc.Add(start, raw.Count);
                raw.Add((op, prefixes, start, operand, constrained, operandOffset, pos));
            }
            offsetToPc[code.Length] = raw.Count;

            int PcOf(int offset)
                => offsetToPc.TryGetValue(offset, out int pc)
                    ? pc
                    : throw new BadImageFormatException($"IL offset 0x{offset:X} is not an instruction boundary (RVA 0x{rva:X}).");

            var instructions = ImmutableArray.CreateBuilder<CilInstruction>(raw.Count);
            foreach (var r in raw)
            {
                var (op, operand) = Normalize(r.Op, r.Operand);
                if (ILOpCodes.OperandKind(r.Op) is ILOperandKind.ShortBranch or ILOperandKind.Branch)
                    operand = PcOf((int)operand);
                instructions.Add(new CilInstruction(op, r.Prefixes, r.Offset, operand, r.Constrained));
            }

            var switchTargets = ImmutableArray.CreateBuilder<int>(switchTargetOffsets.Count);
            foreach (int target in switchTargetOffsets)
                switchTargets.Add(PcOf(target));

            var clauses = ImmutableArray<CilExceptionClause>.Empty;
            if (moreSects)
                clauses = ReadExceptionClauses(md, rva + headerSize + codeSize, PcOf);

            return new CilMethodBody(instructions.MoveToImmutable(), switchTargets.MoveToImmutable(), clauses, maxStack, initLocals, localSig);
        }

        private static ImmutableArray<CilExceptionClause> ReadExceptionClauses(EcmaMetadata md, int sectionRva, Func<int, int> pcOf)
        {
            var clauses = ImmutableArray.CreateBuilder<CilExceptionClause>();
            bool more = true;
            while (more)
            {
                sectionRva = (sectionRva + 3) & ~3;
                var header = md.GetRvaData(sectionRva, 4);
                byte kind = header[0];
                bool fat = (kind & FatSection) != 0;
                more = (kind & MoreSectsSection) != 0;
                int dataSize = fat ? header[1] | header[2] << 8 | header[3] << 16 : header[1];
                if ((kind & EhTableSection) != 0)
                {
                    int clauseSize = fat ? 24 : 12;
                    var data = md.GetRvaData(sectionRva + 4, dataSize - 4);
                    for (int at = 0; at + clauseSize <= data.Length; at += clauseSize)
                    {
                        var c = data.Slice(at, clauseSize);
                        int flags, tryOffset, tryLength, handlerOffset, handlerLength, classOrFilter;
                        if (fat)
                        {
                            flags = BitConverter.ToInt32(c);
                            tryOffset = BitConverter.ToInt32(c.Slice(4));
                            tryLength = BitConverter.ToInt32(c.Slice(8));
                            handlerOffset = BitConverter.ToInt32(c.Slice(12));
                            handlerLength = BitConverter.ToInt32(c.Slice(16));
                            classOrFilter = BitConverter.ToInt32(c.Slice(20));
                        }
                        else
                        {
                            flags = BitConverter.ToUInt16(c);
                            tryOffset = BitConverter.ToUInt16(c.Slice(2));
                            tryLength = c[4];
                            handlerOffset = BitConverter.ToUInt16(c.Slice(5));
                            handlerLength = c[7];
                            classOrFilter = BitConverter.ToInt32(c.Slice(8));
                        }
                        var clauseKind = flags switch
                        {
                            0 => CilExceptionClauseKind.Catch,
                            1 => CilExceptionClauseKind.Filter,
                            2 => CilExceptionClauseKind.Finally,
                            4 => CilExceptionClauseKind.Fault,
                            _ => throw new BadImageFormatException($"Invalid exception clause flags 0x{flags:X}."),
                        };
                        clauses.Add(new CilExceptionClause(
                            clauseKind,
                            pcOf(tryOffset),
                            pcOf(tryOffset + tryLength),
                            pcOf(handlerOffset),
                            pcOf(handlerOffset + handlerLength),
                            clauseKind == CilExceptionClauseKind.Catch ? classOrFilter : 0,
                            clauseKind == CilExceptionClauseKind.Filter ? pcOf(classOrFilter) : -1));
                    }
                }
                sectionRva += dataSize;
            }
            return clauses.ToImmutable();
        }

        private static (ILOpCode Op, long Operand) Normalize(ILOpCode op, long operand) => op switch
        {
            >= ILOpCode.Ldarg_0 and <= ILOpCode.Ldarg_3 => (ILOpCode.Ldarg, op - ILOpCode.Ldarg_0),
            >= ILOpCode.Ldloc_0 and <= ILOpCode.Ldloc_3 => (ILOpCode.Ldloc, op - ILOpCode.Ldloc_0),
            >= ILOpCode.Stloc_0 and <= ILOpCode.Stloc_3 => (ILOpCode.Stloc, op - ILOpCode.Stloc_0),
            ILOpCode.Ldarg_S => (ILOpCode.Ldarg, operand),
            ILOpCode.Ldarga_S => (ILOpCode.Ldarga, operand),
            ILOpCode.Starg_S => (ILOpCode.Starg, operand),
            ILOpCode.Ldloc_S => (ILOpCode.Ldloc, operand),
            ILOpCode.Ldloca_S => (ILOpCode.Ldloca, operand),
            ILOpCode.Stloc_S => (ILOpCode.Stloc, operand),
            ILOpCode.Ldc_I4_M1 => (ILOpCode.Ldc_I4, -1),
            >= ILOpCode.Ldc_I4_0 and <= ILOpCode.Ldc_I4_8 => (ILOpCode.Ldc_I4, op - ILOpCode.Ldc_I4_0),
            ILOpCode.Ldc_I4_S => (ILOpCode.Ldc_I4, operand),
            _ => (ILOpCodes.ToLongBranch(op), operand),
        };
    }
}
