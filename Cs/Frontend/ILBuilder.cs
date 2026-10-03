using System;
using System.Collections.Generic;

namespace Cnidaria.Cs
{
    internal readonly struct ILLabel
    {
        public readonly int Id;
        public ILLabel(int id) => Id = id;
    }
    internal enum ILExceptionClauseKind : byte
    {
        Catch = 0,
        Finally = 2,
    }
    internal sealed class ILBuilder
    {
        private struct Instruction
        {
            public ILOpCode Op;
            public long Operand;
            public int Label;
            public short Pop;
            public short Push;
            public bool IsLong;
        }
        private readonly struct Clause
        {
            public readonly ILExceptionClauseKind Kind;
            public readonly ILLabel TryStart;
            public readonly ILLabel TryEnd;
            public readonly ILLabel HandlerStart;
            public readonly ILLabel HandlerEnd;
            public readonly int CatchTypeToken;
            public Clause(ILExceptionClauseKind kind, ILLabel tryStart, ILLabel tryEnd, ILLabel handlerStart, ILLabel handlerEnd, int catchTypeToken)
            {
                Kind = kind;
                TryStart = tryStart;
                TryEnd = tryEnd;
                HandlerStart = handlerStart;
                HandlerEnd = handlerEnd;
                CatchTypeToken = catchTypeToken;
            }
        }

        private readonly List<Instruction> _code = new();
        private readonly List<int> _labels = new();
        private readonly List<Clause> _clauses = new();

        public int Count => _code.Count;
        public bool UsesLocalloc { get; private set; }
        public bool IsReachable { get; private set; } = true;

        public ILLabel DefineLabel()
        {
            _labels.Add(-1);
            return new ILLabel(_labels.Count - 1);
        }
        public void MarkLabel(ILLabel label)
        {
            if (_labels[label.Id] >= 0)
                throw new InvalidOperationException($"IL label {label.Id} is already marked.");
            _labels[label.Id] = _code.Count;
            IsReachable = true;
        }
        private void Add(ILOpCode op, long operand, int pop, int push, int label = -1)
        {
            if (op == ILOpCode.Localloc)
                UsesLocalloc = true;
            if (op is ILOpCode.Ret or ILOpCode.Br or ILOpCode.Br_S or ILOpCode.Leave or ILOpCode.Leave_S or
                ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Jmp)
            {
                IsReachable = false;
            }
            _code.Add(new Instruction { Op = op, Operand = operand, Label = label, Pop = (short)pop, Push = (short)push });
        }
        public void Emit(ILOpCode op, int pop, int push) => Add(op, 0, pop, push);
        public void EmitToken(ILOpCode op, int token, int pop, int push) => Add(op, token, pop, push);
        public void EmitPrefix(ILOpCode op, int operand = 0) => Add(op, operand, 0, 0);
        public void EmitI4(int value)
        {
            ILOpCode op = value switch
            {
                -1 => ILOpCode.Ldc_I4_M1,
                >= 0 and <= 8 => (ILOpCode)((ushort)ILOpCode.Ldc_I4_0 + value),
                >= sbyte.MinValue and <= sbyte.MaxValue => ILOpCode.Ldc_I4_S,
                _ => ILOpCode.Ldc_I4,
            };
            Add(op, value, 0, 1);
        }
        public void EmitI8(long value)
        {
            if (value >= int.MinValue && value <= int.MaxValue)
            {
                EmitI4((int)value);
                Add(ILOpCode.Conv_I8, 0, 1, 1);
                return;
            }
            Add(ILOpCode.Ldc_I8, value, 0, 1);
        }
        public void EmitR4(float value) => Add(ILOpCode.Ldc_R4, BitConverter.SingleToInt32Bits(value), 0, 1);
        public void EmitR8(double value) => Add(ILOpCode.Ldc_R8, BitConverter.DoubleToInt64Bits(value), 0, 1);
        public void EmitArg(ILOpCode op, int index)
        {
            ILOpCode form = op switch
            {
                ILOpCode.Ldarg when index <= 3 => (ILOpCode)((ushort)ILOpCode.Ldarg_0 + index),
                ILOpCode.Ldarg when index <= byte.MaxValue => ILOpCode.Ldarg_S,
                ILOpCode.Ldarga when index <= byte.MaxValue => ILOpCode.Ldarga_S,
                ILOpCode.Starg when index <= byte.MaxValue => ILOpCode.Starg_S,
                ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg => op,
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
            Add(form, index, op == ILOpCode.Starg ? 1 : 0, op == ILOpCode.Starg ? 0 : 1);
        }
        public void EmitLocal(ILOpCode op, int index)
        {
            ILOpCode form = op switch
            {
                ILOpCode.Ldloc when index <= 3 => (ILOpCode)((ushort)ILOpCode.Ldloc_0 + index),
                ILOpCode.Stloc when index <= 3 => (ILOpCode)((ushort)ILOpCode.Stloc_0 + index),
                ILOpCode.Ldloc when index <= byte.MaxValue => ILOpCode.Ldloc_S,
                ILOpCode.Ldloca when index <= byte.MaxValue => ILOpCode.Ldloca_S,
                ILOpCode.Stloc when index <= byte.MaxValue => ILOpCode.Stloc_S,
                ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => op,
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
            Add(form, index, op == ILOpCode.Stloc ? 1 : 0, op == ILOpCode.Stloc ? 0 : 1);
        }
        public void EmitBranch(ILOpCode op, ILLabel target, int pop)
        {
            if (!IsReachable && op is ILOpCode.Br or ILOpCode.Leave)
                return;
            Add(ILOpCodes.ToShortBranch(op), 0, pop, 0, target.Id);
        }
        public void AddExceptionClause(ILExceptionClauseKind kind, ILLabel tryStart, ILLabel tryEnd, ILLabel handlerStart, ILLabel handlerEnd, int catchTypeToken)
            => _clauses.Add(new Clause(kind, tryStart, tryEnd, handlerStart, handlerEnd, catchTypeToken));

        public byte[] Bake(int localSignatureToken, bool initLocals)
        {
            for (int i = 0; i < _labels.Count; i++)
            {
                if (_labels[i] < 0)
                    throw new InvalidOperationException($"IL label {i} was never marked.");
            }

            var code = _code.ToArray();
            var offsets = new int[code.Length + 1];
            bool changed = true;
            while (changed)
            {
                changed = false;
                int offset = 0;
                for (int i = 0; i < code.Length; i++)
                {
                    offsets[i] = offset;
                    offset += InstructionSize(code[i]);
                }
                offsets[code.Length] = offset;
                for (int i = 0; i < code.Length; i++)
                {
                    ref var ins = ref code[i];
                    if (ins.Label < 0 || ins.IsLong)
                        continue;
                    int displacement = offsets[_labels[ins.Label]] - (offsets[i] + InstructionSize(ins));
                    if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue)
                    {
                        ins.IsLong = true;
                        changed = true;
                    }
                }
            }

            var il = new MetadataBuffer(offsets[code.Length] + 16);
            for (int i = 0; i < code.Length; i++)
                WriteInstruction(il, code[i], i, offsets);

            int maxStack = ComputeMaxStack(code);
            var body = new MetadataBuffer(il.Length + 64);
            bool tiny = il.Length < 64 && maxStack <= 8 && localSignatureToken == 0 && _clauses.Count == 0 && !(initLocals && UsesLocalloc);
            if (tiny)
            {
                body.WriteByte((byte)((il.Length << 2) | 0x2));
                body.WriteBytes(il.Span);
                return body.ToArray();
            }

            ushort flags = 0x3;
            if (initLocals)
                flags |= 0x10;
            if (_clauses.Count != 0)
                flags |= 0x8;
            body.WriteUInt16((ushort)(flags | (3 << 12)));
            body.WriteUInt16((ushort)maxStack);
            body.WriteUInt32((uint)il.Length);
            body.WriteUInt32((uint)localSignatureToken);
            body.WriteBytes(il.Span);
            if (_clauses.Count != 0)
            {
                body.Align(4);
                WriteExceptionSection(body, offsets);
            }
            return body.ToArray();
        }
        private static int InstructionSize(in Instruction ins)
        {
            ILOpCode op = ins.IsLong ? ILOpCodes.ToLongBranch(ins.Op) : ins.Op;
            return ILOpCodes.OpCodeSize(op) + ILOpCodes.OperandSize(ILOpCodes.OperandKind(op));
        }
        private void WriteInstruction(MetadataBuffer il, in Instruction ins, int index, int[] offsets)
        {
            ILOpCode op = ins.IsLong ? ILOpCodes.ToLongBranch(ins.Op) : ins.Op;
            if ((ushort)op >= 0xFE00)
                il.WriteByte(0xFE);
            il.WriteByte((byte)op);
            switch (ILOpCodes.OperandKind(op))
            {
                case ILOperandKind.None:
                    break;
                case ILOperandKind.Int8:
                case ILOperandKind.UInt8:
                    il.WriteByte((byte)ins.Operand);
                    break;
                case ILOperandKind.UInt16:
                    il.WriteUInt16((ushort)ins.Operand);
                    break;
                case ILOperandKind.Int32:
                case ILOperandKind.Float32:
                case ILOperandKind.Token:
                    il.WriteUInt32(unchecked((uint)ins.Operand));
                    break;
                case ILOperandKind.Int64:
                case ILOperandKind.Float64:
                    il.WriteUInt64(unchecked((ulong)ins.Operand));
                    break;
                case ILOperandKind.ShortBranch:
                    il.WriteByte(unchecked((byte)(sbyte)(offsets[_labels[ins.Label]] - offsets[index + 1])));
                    break;
                case ILOperandKind.Branch:
                    il.WriteInt32(offsets[_labels[ins.Label]] - offsets[index + 1]);
                    break;
                default:
                    throw new NotSupportedException($"Operand kind of {op} is not supported by the IL builder.");
            }
        }
        private int ComputeMaxStack(Instruction[] code)
        {
            if (code.Length == 0)
                return 0;

            var depthAt = new int[code.Length + 1];
            Array.Fill(depthAt, -1);
            var work = new Stack<int>();
            void Enter(int index, int depth)
            {
                if (index >= depthAt.Length)
                    return;
                if (depthAt[index] < 0)
                {
                    depthAt[index] = depth;
                    work.Push(index);
                }
                else if (depthAt[index] != depth)
                {
                    throw new InvalidOperationException($"IL stack depth mismatch at instruction {index}: {depthAt[index]} versus {depth}.");
                }
            }

            Enter(0, 0);
            foreach (var clause in _clauses)
                Enter(_labels[clause.HandlerStart.Id], clause.Kind == ILExceptionClauseKind.Catch ? 1 : 0);

            int max = 0;
            while (work.Count != 0)
            {
                int index = work.Pop();
                int depth = depthAt[index];
                for (int i = index; i < code.Length; i++)
                {
                    if (i != index)
                    {
                        if (depthAt[i] >= 0)
                        {
                            if (depthAt[i] != depth)
                                throw new InvalidOperationException($"IL stack depth mismatch at instruction {i}: {depthAt[i]} versus {depth}.");
                            break;
                        }
                        depthAt[i] = depth;
                    }

                    var ins = code[i];
                    depth -= ins.Pop;
                    if (depth < 0)
                        throw new InvalidOperationException($"IL stack underflow at instruction {i} ({ins.Op}).");
                    depth += ins.Push;
                    if (depth > max)
                        max = depth;

                    ILOpCode op = ILOpCodes.ToLongBranch(ins.Op);
                    if (ins.Label >= 0)
                    {
                        Enter(_labels[ins.Label], op == ILOpCode.Leave ? 0 : depth);
                        if (op is ILOpCode.Br or ILOpCode.Leave)
                            break;
                    }
                    if (op is ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Jmp)
                        break;
                }
            }
            return max;
        }
        private void WriteExceptionSection(MetadataBuffer body, int[] offsets)
        {
            var rows = new (uint Flags, int TryOffset, int TryLength, int HandlerOffset, int HandlerLength, int Token)[_clauses.Count];
            bool small = _clauses.Count * 12 + 4 <= byte.MaxValue;
            for (int i = 0; i < _clauses.Count; i++)
            {
                var c = _clauses[i];
                int tryStart = offsets[_labels[c.TryStart.Id]];
                int tryEnd = offsets[_labels[c.TryEnd.Id]];
                int handlerStart = offsets[_labels[c.HandlerStart.Id]];
                int handlerEnd = offsets[_labels[c.HandlerEnd.Id]];
                rows[i] = ((uint)c.Kind, tryStart, tryEnd - tryStart, handlerStart, handlerEnd - handlerStart,
                    c.Kind == ILExceptionClauseKind.Catch ? c.CatchTypeToken : 0);
                if (tryStart > ushort.MaxValue || handlerStart > ushort.MaxValue ||
                    rows[i].TryLength > byte.MaxValue || rows[i].HandlerLength > byte.MaxValue)
                {
                    small = false;
                }
            }
            if (small)
            {
                body.WriteByte(0x01);
                body.WriteByte((byte)(rows.Length * 12 + 4));
                body.WriteUInt16(0);
                foreach (var row in rows)
                {
                    body.WriteUInt16((ushort)row.Flags);
                    body.WriteUInt16((ushort)row.TryOffset);
                    body.WriteByte((byte)row.TryLength);
                    body.WriteUInt16((ushort)row.HandlerOffset);
                    body.WriteByte((byte)row.HandlerLength);
                    body.WriteUInt32((uint)row.Token);
                }
                return;
            }
            int dataSize = rows.Length * 24 + 4;
            body.WriteUInt32(0x41u | ((uint)dataSize << 8));
            foreach (var row in rows)
            {
                body.WriteUInt32(row.Flags);
                body.WriteUInt32((uint)row.TryOffset);
                body.WriteUInt32((uint)row.TryLength);
                body.WriteUInt32((uint)row.HandlerOffset);
                body.WriteUInt32((uint)row.HandlerLength);
                body.WriteUInt32((uint)row.Token);
            }
        }
    }
}
